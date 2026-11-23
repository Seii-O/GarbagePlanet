// @trace BAG-003, UI-006
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 独立垃圾背包界面的一键生成 + 接线（菜单⑳），同时精简中转站里原有的「玩家背包」区域。
    ///
    /// 红线：
    /// 1. 全部用 Editor API 生成，不手写 .unity / .prefab YAML；
    /// 2. 只重建 Panel_GarbageBag，不动玩家双手、相机、中转站其它卡片；
    /// 3. 布局与运行时兜底共用 GarbageBagUiFactory，避免两处结构漂移；
    /// 4. 图标沿用 Assets/Art/UI/垃圾星球_UI资源库/ 现有 Sprite，不新增美术资产。
    ///
    /// 菜单：Tools/垃圾星球白膜/⑳ 生成垃圾背包界面（G）
    /// 批处理：-executeMethod TrashPlanet.Whitebox.Editor.GarbageBagUiBuilder.BuildBatch
    /// </summary>
    [InitializeOnLoad]
    public static class GarbageBagUiBuilder
    {
        // 与 WhiteboxUiBuilder 同口径：脚本首次编译进编辑器时自动补齐一次，避免"代码有了、场景没生成"。
        // 只在白膜场景已打开、非 Play、非编译中触发；每个编辑器会话只跑一次。
        const string SessionKey = "TrashPlanet.Whitebox.GarbageBag.AutoBuilt.v1";
        const string Tag = "[GarbageBagUiBuilder] ";
        const string UiRoot = "UI_Whitebox";
        const string SysRoot = "sys_whitebox";
        const string PanelName = "Panel_GarbageBag";
        const string LibPath = "Assets/Art/UI/垃圾星球_UI资源库/";

        static GarbageBagUiBuilder()
        {
            EditorApplication.delayCall += AutoEnsure;
            EditorApplication.update += AutoEnsure;
        }

        static void AutoEnsure()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (SessionState.GetBool(SessionKey, false)) return;
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != WhiteboxBuilder.ScenePath) return;
            if (FindRoot(scene, UiRoot) == null || FindRoot(scene, SysRoot) == null) return;

            SessionState.SetBool(SessionKey, true);
            try
            {
                UiArtImportSettings.Apply();
                StationUiBuilder.Rebuild(scene);
                BuildInScene(scene, false);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log(Tag + "首次载入已自动补齐垃圾背包面板与中转站入口");
            }
            catch (Exception e)
            {
                SessionState.SetBool(SessionKey, false); // 失败留待下次重试
                Debug.LogError(Tag + "自动补齐失败：" + e);
            }
        }

        [MenuItem("Tools/垃圾星球白膜/⑳ 生成垃圾背包界面（G）", false, 20)]
        public static void BuildMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning(Tag + "请先退出 Play 模式");
                return;
            }
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != WhiteboxBuilder.ScenePath)
            {
                Debug.LogWarning(Tag + "请先打开白膜场景 " + WhiteboxBuilder.ScenePath + " 再执行本菜单");
                return;
            }
            BuildInScene(scene, false);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        /// <summary>批处理：打开场景 → 应用图标导入 → 精简中转站 → 生成新面板 → 校验，退出码即结论。</summary>
        public static void BuildBatch()
        {
            try
            {
                Scene scene = EditorSceneManager.OpenScene(WhiteboxBuilder.ScenePath, OpenSceneMode.Single);
                UiArtImportSettings.Apply();
                bool stationOk = StationUiBuilder.Rebuild(scene);
                Debug.Log(Tag + "batch station rebuild=" + stationOk);
                BuildInScene(scene, true);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();

                bool pass = WhiteboxValidator.Validate();
                Debug.Log(Tag + "batch validate=" + pass);
                EditorApplication.Exit(pass && stationOk ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogError(Tag + "batch 失败：" + e);
                EditorApplication.Exit(1);
            }
        }

        static void BuildInScene(Scene scene, bool batch)
        {
            UiArtImportSettings.Apply();
            GameObject uiRoot = FindRoot(scene, UiRoot);
            GameObject sysRoot = FindRoot(scene, SysRoot);
            if (uiRoot == null || sysRoot == null)
            {
                Debug.LogError(Tag + "缺少 " + UiRoot + " 或 " + SysRoot + "，请先跑 ①/④ 生成场景与 Hierarchy UI");
                return;
            }

            // 存在即删重建：保证 Btn_GBDiscard_0..7 等名字与视图 Bind() 完全一致
            GameObject old = FindChild(uiRoot.transform, PanelName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            GameObject panel = GarbageBagUiFactory.Build(uiRoot.transform, UiFont.Load(),
                LoadAll(GarbageBagCatalog.CategoryIconPaths),
                LoadAll(GarbageBagCatalog.ItemIconPaths),
                LoadAll(GarbageBagCatalog.MaterialIconPaths));

            GarbageBagPanelView view = uiRoot.GetComponent<GarbageBagPanelView>();
            if (view == null) view = uiRoot.AddComponent<GarbageBagPanelView>();

            GarbageBagSystem system = sysRoot.GetComponent<GarbageBagSystem>();
            if (system == null) system = sysRoot.AddComponent<GarbageBagSystem>();
            system.WireView(view);
            WhiteboxGame game = sysRoot.GetComponent<WhiteboxGame>();
            if (game != null) game.WireGarbageBag(system);
            else Debug.LogWarning(Tag + "sys_whitebox 上未找到 WhiteboxGame，面板已生成但未接入启动链");

            EditorUtility.SetDirty(view);
            EditorUtility.SetDirty(system);
            EditorUtility.SetDirty(panel);
            if (game != null) EditorUtility.SetDirty(game);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log(Tag + "完成：" + UiRoot + "/" + PanelName + "（G 键，2×2 分类卡 + 8 单品卡），batch=" + batch);
        }

        static Sprite[] LoadAll(string[] relativePaths)
        {
            Sprite[] result = new Sprite[relativePaths.Length];
            for (int i = 0; i < relativePaths.Length; i++)
            {
                string path = LibPath + relativePaths[i] + ".png";
                result[i] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (result[i] == null) Debug.LogWarning(Tag + "图标缺失：" + path + "（显示时退回纯色）");
            }
            return result;
        }

        static GameObject FindRoot(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) if (roots[i].name == name) return roots[i];
            return null;
        }

        static GameObject FindChild(Transform root, string name)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) if (all[i].name == name) return all[i].gameObject;
            return null;
        }
    }
}
