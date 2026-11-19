using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PlanetCleaner;
using TrashPlanet.Whitebox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 白膜工程离线/编辑器内校验。菜单：Tools/垃圾星球白膜/③ 校验白膜工程
    /// 命令行：Unity.exe -batchmode -quit -projectPath <工程>
    ///         -executeMethod TrashPlanet.Whitebox.Editor.WhiteboxValidator.ValidateBatch -logFile <日志>
    /// 结果落盘到 docs/whitebox-validate.json，退出码 0/1 可直接接流水线。
    /// </summary>
    public static class WhiteboxValidator
    {
        const string Tag = "[WhiteboxValidator] ";
        static readonly string[] TrashPrefabNames =
        {
            "prop_trash_plastic_whitebox", "prop_trash_metal_whitebox",
            "prop_trash_organic_whitebox", "prop_trash_electronic_whitebox",
            "prop_trash_large_tire_whitebox", "prop_trash_large_metal_device_whitebox",
            "prop_trash_large_appliance_whitebox"
        };

        [MenuItem("Tools/垃圾星球白膜/③ 校验白膜工程", false, 12)]
        public static void ValidateMenu()
        {
            bool ok = Validate();
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("白膜工程校验",
                    (ok ? "通过" : "未通过") + "，详细结论见 Console 与 docs/whitebox-validate.json", "好");
        }

        public static void ValidateBatch()
        {
            try
            {
                bool ok = Validate();
                Debug.Log(Tag + "batch result = " + (ok ? "OK" : "FAILED"));
                EditorApplication.Exit(ok ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogError(Tag + "batch exception: " + e);
                EditorApplication.Exit(1);
            }
        }

        public static bool Validate()
        {
            var errors = new List<string>();
            var checks = new List<string>();

            WhiteboxConfig cfg = AssetDatabase.LoadAssetAtPath<WhiteboxConfig>(WhiteboxBuilder.ConfigPath);
            Check(cfg != null, "配置资产存在", errors, checks);
            if (cfg != null)
            {
                Check(cfg.TotalTrashBudget >= cfg.MapTrashCap,
                    "全局垃圾总量 ≥ 地表并发上限（" + cfg.TotalTrashBudget + " ≥ " + cfg.MapTrashCap + "）",
                    errors, checks);
                Check(cfg.PlanetRadius > 1f && cfg.PlanetRadius < 60f,
                    "星球半径在可玩区间（" + cfg.PlanetRadius + " m）", errors, checks);
            }

            for (int i = 0; i < TrashPrefabNames.Length; i++)
            {
                string p = "Assets/Prefabs/Whitebox/" + TrashPrefabNames[i] + ".prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                Check(prefab != null, "预制体存在 " + TrashPrefabNames[i], errors, checks);
                if (prefab != null)
                    Check(prefab.GetComponent<TrashItem>() != null,
                        "预制体含 TrashItem " + TrashPrefabNames[i], errors, checks);
            }

            GameObject dogPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Whitebox/chr_dog_whitebox.prefab");
            Check(dogPrefab != null && dogPrefab.GetComponent<CompanionDog>() != null,
                "伙伴狗预制体与组件存在", errors, checks);
            Check(AssetDatabase.LoadAssetAtPath<GameObject>(
                      "Assets/Prefabs/Whitebox/env_station_whitebox.prefab") != null,
                "中转站预制体存在", errors, checks);
            Check(AssetDatabase.LoadAssetAtPath<GameObject>(
                      "Assets/Prefabs/Whitebox/env_landmark_whitebox.prefab") != null,
                "地标预制体存在", errors, checks);

            SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(WhiteboxBuilder.ScenePath);
            Check(sceneAsset != null, "场景资产存在", errors, checks);

            bool inBuild = false;
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            for (int i = 0; i < scenes.Length; i++)
                if (scenes[i].path == WhiteboxBuilder.ScenePath && scenes[i].enabled) inBuild = true;
            Check(inBuild, "场景已启用并写入构建列表", errors, checks);

            if (sceneAsset != null) ValidateSceneContents(cfg, errors, checks);
            if (cfg != null) ValidateDogColor(cfg, errors, checks);

            WriteReport(errors, checks);
            foreach (string c in checks) Debug.Log(Tag + c);
            foreach (string e in errors) Debug.LogError(Tag + e);
            Debug.Log(Tag + "结论：" + (errors.Count == 0 ? "通过" : "未通过")
                      + "，检查 " + checks.Count + " 项，错误 " + errors.Count + " 项");
            return errors.Count == 0;
        }

        static void ValidateSceneContents(WhiteboxConfig cfg, List<string> errors, List<string> checks)
        {
            // 场景可能正是用户当前打开的那个：此时直接复用，绝不能 Additive 再开一次又把它关掉
            Scene scene = SceneManager.GetSceneByPath(WhiteboxBuilder.ScenePath);
            bool openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(WhiteboxBuilder.ScenePath, OpenSceneMode.Additive);
                openedHere = true;
            }
            try
            {
                int missing = 0;
                WhiteboxGame game = null;
                FirstPersonHands hands = null;
                GameObject uiRoot = null;
                EventSystem eventSystem = null;
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    GameObject root = roots[i];
                    missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(root);
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                        missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                    if (game == null) game = root.GetComponentInChildren<WhiteboxGame>(true);
                    if (hands == null) hands = root.GetComponentInChildren<FirstPersonHands>(true);
                    if (eventSystem == null) eventSystem = root.GetComponentInChildren<EventSystem>(true);
                    if (root.name == WhiteboxUiBuilder.RootName) uiRoot = root;
                }
                Check(missing == 0, "场景无缺失脚本组件（missing=" + missing + "）", errors, checks);
                Check(game != null, "场景含 WhiteboxGame 装配根", errors, checks);

                // 用户明确要求：人物含双手，UI 改造不得动到 Hands / HandLeft / HandRight
                Check(hands != null && hands.LeftHand != null && hands.RightHand != null,
                    "玩家第一人称双手保留（Hands / HandLeft / HandRight）", errors, checks);

                if (game != null)
                {
                    Check(game.Config != null && game.Player != null && game.ViewCamera != null
                          && game.Ground != null && game.Water != null && game.Sun != null
                          && game.Station != null,
                        "装配引用齐全（配置/玩家/相机/地表/水/光/中转站）", errors, checks);
                    Check(game.Spawner != null && game.Spawner.Prefabs != null
                          && game.Spawner.Prefabs.Length == 7,
                        "刷新器绑定 7 个垃圾预制体", errors, checks);
                    bool allPrefabs = true;
                    if (game.Spawner != null && game.Spawner.Prefabs != null)
                        foreach (TrashItem t in game.Spawner.Prefabs) if (t == null) allPrefabs = false;
                    Check(allPrefabs, "垃圾预制体引用无空项", errors, checks);
                    Check(game.Dog != null && game.Interaction != null && game.Quest != null
                          && game.Eco != null && game.Hud != null && game.StationLogic != null,
                        "核心系统组件齐全", errors, checks);
                    Check(game.Hud != null && game.Hud.CanvasView != null,
                        "IMGUI 代理已绑定 Hierarchy uGUI 视图", errors, checks);

                    // 星球体积是烘焙值，配置才是真值：不一致时由 WhiteboxGame.Awake 的 PlanetLayout 运行期对齐，
                    // 这里只做信息记录，不作为失败项（否则每次改半径都得重跑①重建场景）。
                    if (game.PlanetCenter != null && cfg != null)
                    {
                        float measured = PlanetLayout.MeasureRadius(game.PlanetCenter);
                        checks.Add("INFO 星球场景半径 " + measured.ToString("0.##") + " m，配置 "
                                   + cfg.PlanetRadius + " m（不一致时运行期自动对齐）");
                    }
                }

                ValidateHierarchyUi(uiRoot, eventSystem, errors, checks);
            }
            finally
            {
                if (openedHere && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        static void ValidateHierarchyUi(GameObject uiRoot, EventSystem eventSystem,
            List<string> errors, List<string> checks)
        {
            Check(uiRoot != null, "Hierarchy 含可编辑 UI 根 " + WhiteboxUiBuilder.RootName, errors, checks);
            if (uiRoot != null)
            {
                Canvas canvas = uiRoot.GetComponent<Canvas>();
                Check(canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay,
                    "UI Canvas 为 Screen Space Overlay", errors, checks);
                Check(uiRoot.GetComponent<CanvasScaler>() != null && uiRoot.GetComponent<GraphicRaycaster>() != null,
                    "UI 含 CanvasScaler / GraphicRaycaster", errors, checks);
                WhiteboxCanvasHud view = uiRoot.GetComponent<WhiteboxCanvasHud>();
                Check(view != null && view.HasRequiredRefs, "uGUI 视图引用绑定齐全", errors, checks);
                Check(uiRoot.GetComponent<WhiteboxUiActions>() != null,
                    "按钮回调目标 WhiteboxUiActions 存在", errors, checks);
                string[] panels = { "Hud_Debug", "Panel_Station", "Panel_Pause", "Panel_Settlement" };
                for (int i = 0; i < panels.Length; i++)
                    Check(uiRoot.transform.Find(panels[i]) != null,
                        "面板在 Hierarchy 中可见 " + panels[i], errors, checks);

                // 本次新增：数量选择弹窗（滑块 + 数字输入框）与中转站「⑤ 升级背包」按钮
                GameObject qtyPanel = uiRoot.transform.Find("Panel_Quantity")?.gameObject;
                Check(qtyPanel != null, "数量选择弹窗 Panel_Quantity 存在", errors, checks);
                Check(qtyPanel != null && qtyPanel.GetComponent<StationQuantityDialog>() != null,
                    "弹窗挂载 StationQuantityDialog 控制器", errors, checks);
                Check(qtyPanel != null && qtyPanel.GetComponentInChildren<Slider>(true) != null,
                    "弹窗含数量滑块 Slider", errors, checks);
                Check(qtyPanel != null && qtyPanel.GetComponentInChildren<InputField>(true) != null,
                    "弹窗含数字输入框 InputField", errors, checks);
                GameObject stationPanel = uiRoot.transform.Find("Panel_Station")?.gameObject;
                Check(stationPanel != null && stationPanel.transform.Find("Btn_BagUpgrade") != null,
                    "中转站含 ⑤ 升级背包按钮", errors, checks);
                Button[] buttons = uiRoot.GetComponentsInChildren<Button>(true);
                int bound = 0;
                for (int i = 0; i < buttons.Length; i++)
                    if (buttons[i].onClick.GetPersistentEventCount() > 0) bound++;
                Check(bound >= 12, "按钮 OnClick 持久化绑定 " + bound + " 个（≥12）", errors, checks);
            }
            Check(eventSystem != null && eventSystem.GetComponent<StandaloneInputModule>() != null,
                "EventSystem + StandaloneInputModule（旧 Input Manager）", errors, checks);
        }

        static void ValidateDogColor(WhiteboxConfig cfg, List<string> errors, List<string> checks)
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/M_Dog_Whitebox.mat");
            Check(m != null, "狗固定材质资产存在", errors, checks);
            if (m == null) return;
            Color actual = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
            string want = ColorUtility.ToHtmlStringRGB(cfg.DogColor).ToUpperInvariant();
            string got = ColorUtility.ToHtmlStringRGB(actual).ToUpperInvariant();
            Check(want == got, "ART-012 狗固定色号一致（期望 #" + want + "，实际 #" + got + "）", errors, checks);
        }

        static void Check(bool ok, string label, List<string> errors, List<string> checks)
        {
            string line = (ok ? "PASS " : "FAIL ") + label;
            if (ok) checks.Add(line); else errors.Add(line);
        }

        static void WriteReport(List<string> errors, List<string> checks)
        {
            try
            {
                string dir = Path.Combine(Application.dataPath, "../docs");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"unityVersion\": \"" + Application.unityVersion + "\",");
                sb.AppendLine("  \"scene\": \"" + WhiteboxBuilder.ScenePath + "\",");
                sb.AppendLine("  \"pass\": " + (errors.Count == 0 ? "true" : "false") + ",");
                sb.AppendLine("  \"checkCount\": " + checks.Count + ",");
                sb.AppendLine("  \"errorCount\": " + errors.Count + ",");
                sb.AppendLine("  \"checks\": [");
                for (int i = 0; i < checks.Count; i++)
                    sb.Append("    \"" + Escape(checks[i]) + "\"" + (i < checks.Count - 1 ? "," : "") + Environment.NewLine);
                sb.AppendLine("  ],");
                sb.AppendLine("  \"errors\": [");
                for (int i = 0; i < errors.Count; i++)
                    sb.Append("    \"" + Escape(errors[i]) + "\"" + (i < errors.Count - 1 ? "," : "") + Environment.NewLine);
                sb.AppendLine("  ]");
                sb.AppendLine("}");
                File.WriteAllText(Path.Combine(dir, "whitebox-validate.json"), sb.ToString(), new UTF8Encoding(false));
                Debug.Log(Tag + "报告已写入 docs/whitebox-validate.json");
            }
            catch (Exception e)
            {
                Debug.LogWarning(Tag + "报告写入失败：" + e.Message);
            }
        }

        static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
