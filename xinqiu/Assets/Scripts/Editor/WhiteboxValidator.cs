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
                // C27 人物等级配置
                Check(cfg.PlayerLevelMax >= 1 && cfg.PlayerLevelExpAt(cfg.PlayerLevelMax) > cfg.PlayerLevelExpAt(1),
                    "人物等级经验阈值随等级递增（上限 Lv." + cfg.PlayerLevelMax + "）", errors, checks);
                Check(cfg.PlayerPickRangeAt(1) > 0f && cfg.PlayerProcessMultAt(cfg.PlayerLevelMax) <= cfg.PlayerProcessMultAt(1),
                    "人物等级加成随等级提升（拾取距离/效率）", errors, checks);
                Check(cfg.PlayerUpgradeMetalCostAt(1) > 0 && cfg.PlayerUpgradeElectronicCostAt(1) > 0,
                    "人物升级消耗材料已配置", errors, checks);
            }
            // C27 技能书购买价与材料出售价
            ToolConfig tc = AssetDatabase.LoadAssetAtPath<ToolConfig>("Assets/Data/ToolConfig.asset");
            if (tc != null)
            {
                int priced = 0;
                for (int i = 0; i < tc.Books.Length; i++) if (tc.Books[i] != null && tc.Books[i].Price > 0) priced++;
                Check(priced == tc.Books.Length && priced > 0,
                    "全部技能书已配置金币购买价（" + priced + "/" + tc.Books.Length + "）", errors, checks);
            }
            CoinTradeConfig ctc = AssetDatabase.LoadAssetAtPath<CoinTradeConfig>("Assets/Data/CoinTradeConfig.asset");
            Check(ctc != null && ctc.MaterialSellPrice(0) > 0 && ctc.MaterialSellPrice(3) > 0,
                "四种加工产物出售价已配置", errors, checks);

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
            ValidateDogExtras(cfg, dogPrefab, errors, checks);
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

                Check(uiRoot.transform.Find("Panel_GarbageBag") == null, "独立垃圾背包界面已移除", errors, checks);

                // UI-007：五页签中转站（真实 uGUI 控件：5 个内容 Page + 5 个具名页签按钮 + 控制器）
                GameObject fiveTabPanel = FindDeep(uiRoot.transform, StationFiveTabsView.PanelName)?.gameObject;
                Check(fiveTabPanel != null, "五页签中转站面板 Panel_StationFiveTabs 存在", errors, checks);
                Check(uiRoot.GetComponent<StationFiveTabsView>() != null,
                    "五页签控制器 StationFiveTabsView 已挂载", errors, checks);
                int pageCount = 0, tabCount = 0;
                for (int i = 0; i < 5; i++)
                {
                    if (fiveTabPanel != null && FindDeep(fiveTabPanel.transform, "Page_" + i) != null) pageCount++;
                    if (fiveTabPanel != null && FindDeep(fiveTabPanel.transform, "Btn_Tab_" + i) != null) tabCount++;
                }
                Check(pageCount == 5, "五页签含 5 个内容 Page（" + pageCount + "/5）", errors, checks);
                Check(tabCount == 5, "五页签含 5 个具名页签按钮（" + tabCount + "/5）", errors, checks);
                Transform artSetProbe = fiveTabPanel != null ? FindDeep(uiRoot.transform, "Card_Bag") : null;
                Check(artSetProbe != null, "五页签含真实控件卡片 Card_Bag（非静态整页图）", errors, checks);

                // UI-008：独立任务日志（真实控件 + HUD 常驻入口按钮）
                GameObject questLogPanel = FindDeep(uiRoot.transform, QuestLogPanelView.PanelName)?.gameObject;
                Check(questLogPanel != null, "任务日志面板 Panel_QuestLog 存在", errors, checks);
                Check(uiRoot.GetComponent<QuestLogPanelView>() != null,
                    "任务日志控制器 QuestLogPanelView 已挂载", errors, checks);
                Check(FindDeep(uiRoot.transform, "Btn_QuestLog") != null,
                    "HUD 含独立任务日志入口按钮 Btn_QuestLog", errors, checks);
                int questRows = 0;
                for (int i = 0; i < 10; i++)
                    if (questLogPanel != null && FindDeep(questLogPanel.transform, "Row_Quest_" + i) != null) questRows++;
                Check(questRows == 10, "任务日志含 10 条具名任务行（" + questRows + "/10）", errors, checks);

                // UI-009：独立结算界面（评级环 / 六行统计 / 五阶段条 / 双按钮）
                GameObject settlePanel = FindDeep(uiRoot.transform, SettlementPanelView.PanelName)?.gameObject;
                Check(settlePanel != null, "结算面板 Panel_SettlementFinal 存在", errors, checks);
                Check(uiRoot.GetComponent<SettlementPanelView>() != null,
                    "结算控制器 SettlementPanelView 已挂载", errors, checks);
                Check(settlePanel != null && FindDeep(settlePanel.transform, "Txt_Grade") != null
                      && FindDeep(settlePanel.transform, "Img_RingFill") != null,
                    "结算含评级环与评级文字（真实控件）", errors, checks);
                Check(settlePanel != null && FindDeep(settlePanel.transform, "Btn_ContinuePlaying") != null
                      && FindDeep(settlePanel.transform, "Btn_BackToTitle") != null,
                    "结算含「继续游玩 / 返回主菜单」双按钮", errors, checks);
                int stageCount = 0;
                for (int i = 0; i < 5; i++)
                    if (settlePanel != null && FindDeep(settlePanel.transform, "Stage_" + i) != null) stageCount++;
                Check(stageCount == 5, "结算含 5 个阶段节点（" + stageCount + "/5）", errors, checks);

                Button[] buttons = uiRoot.GetComponentsInChildren<Button>(true);
                int bound = 0;
                for (int i = 0; i < buttons.Length; i++)
                    if (buttons[i].onClick.GetPersistentEventCount() > 0) bound++;
                Check(bound >= 12, "按钮 OnClick 持久化绑定 " + bound + " 个（≥12）", errors, checks);
            }
            Check(eventSystem != null && eventSystem.GetComponent<StandaloneInputModule>() != null,
                "EventSystem + StandaloneInputModule（旧 Input Manager）", errors, checks);
        }

        /// <summary>
        /// MECH-017 / ART-014：探路站位可见性、固定材质、体高红线、部件引用与动画幅度。
        /// 注意只检查配置与预制体的静态事实，不依赖运行时状态。
        /// </summary>
        static void ValidateDogExtras(WhiteboxConfig cfg, GameObject dogPrefab,
            List<string> errors, List<string> checks)
        {
            if (cfg != null)
            {
                float forward = cfg.DogScoutForward;
                float side = cfg.DogScoutSide;
                float ratio = side / Mathf.Max(0.01f, forward);
                float distance = Mathf.Sqrt(forward * forward + side * side);
                Check(forward > 0f && Mathf.Abs(ratio) < 1f && distance >= 1.9f,
                    "MECH-017 探路站位在可见锥体内（前 " + forward.ToString("0.##")
                    + " m / 右 " + side.ToString("0.##") + " m）", errors, checks);

                float height = cfg.DogHeight + cfg.DogBobMeters;
                Check(height <= 0.6f,
                    "ART-011 狗体高含起伏不越红线（" + height.ToString("0.###") + " m）", errors, checks);

                Check(cfg.DogTailWagDeg <= 30f && cfg.DogTailBoostDeg <= 45f
                      && cfg.DogLegGaitDeg <= 25f && cfg.DogHeadLookDeg <= 20f,
                    "ART-014 部件动画幅度在保守区间", errors, checks);
            }

            Material m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/M_Dog_Whitebox.mat");
            if (dogPrefab != null && m != null)
            {
                CompanionDog dog = dogPrefab.GetComponent<CompanionDog>();
                Renderer[] rs = dog != null ? dog.FixedColorRenderers : null;
                bool allFixed = rs != null && rs.Length > 0;
                if (allFixed)
                    foreach (Renderer r in rs)
                        if (r == null || r.sharedMaterial != m) allFixed = false;
                Check(allFixed, "ART-012 狗所有 Renderer 仍绑定固定材质", errors, checks);

                bool parts = dogPrefab.transform.Find("Tail") != null
                             && dogPrefab.transform.Find("Head") != null
                             && dogPrefab.transform.Find("Leg_0") != null
                             && dogPrefab.transform.Find("Leg_1") != null
                             && dogPrefab.transform.Find("Leg_2") != null
                             && dogPrefab.transform.Find("Leg_3") != null;
                Check(parts, "ART-014 狗部件子物体命名齐全（Tail/Head/Leg_0..3）", errors, checks);
            }
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

        /// <summary>递归按名字找节点（含失活），供 UI-007 真实控件结构校验使用。</summary>
        static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) if (all[i].name == name) return all[i];
            return null;
        }
    }
}
