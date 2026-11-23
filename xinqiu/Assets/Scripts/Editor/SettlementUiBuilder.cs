// @trace UI-009
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 结算界面一键生成 + 接线（菜单㉓）。
    /// 与任务日志同款模式：真实 uGUI 拆件、Editor 注入持久化 Sprite、运行时不碰 AssetDatabase。
    /// 面板平时隐藏，由 QuestChain 游玩结束时调 WhiteboxGame.OpenSettlement() 弹出。
    /// </summary>
    [InitializeOnLoad]
    public static class SettlementUiBuilder
    {
        const string SessionKey = "TrashPlanet.Whitebox.Settlement.AutoBuilt.v1";
        const string Tag = "[SettlementUiBuilder] ";
        const string UiRoot = "UI_Whitebox";
        const string SysRoot = "sys_whitebox";

        const string Pnl = "06_UI界面拆件/01_面板与容器/";
        const string Btn = "06_UI界面拆件/02_按钮/";
        const string Tab = "06_UI界面拆件/03_页签与标签/";
        const string Prg = "06_UI界面拆件/04_进度与数值/";
        const string Fun = "06_UI界面拆件/07_图标_功能/";
        const string Dec = "06_UI界面拆件/09_徽章与装饰/";

        static SettlementUiBuilder()
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
                BuildInScene(scene, false);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log(Tag + "首次载入已自动补齐结算界面");
            }
            catch (Exception e)
            {
                SessionState.SetBool(SessionKey, false);
                Debug.LogError(Tag + "自动补齐失败：" + e);
            }
        }

        [MenuItem("Tools/垃圾星球白膜/㉓ 生成结算界面（游玩结束弹出）", false, 23)]
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
                Debug.LogWarning(Tag + "请先打开白膜场景 " + WhiteboxBuilder.ScenePath);
                return;
            }
            BuildInScene(scene, false);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        public static void BuildBatch()
        {
            try
            {
                Scene scene = EditorSceneManager.OpenScene(WhiteboxBuilder.ScenePath, OpenSceneMode.Single);
                UiArtImportSettings.Apply();
                BuildInScene(scene, true);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                bool pass = WhiteboxValidator.Validate();
                Debug.Log(Tag + "batch validate=" + pass);
                EditorApplication.Exit(pass ? 0 : 1);
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
                Debug.LogError(Tag + "缺少 " + UiRoot + " 或 " + SysRoot);
                return;
            }
            WhiteboxGame game = sysRoot.GetComponent<WhiteboxGame>();
            SettlementArtSet art = LoadArt();
            Font font = UiFont.Load();

            Transform old = FindDeep(uiRoot.transform, SettlementPanelView.PanelName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);

            GameObject panel = SettlementUiFactory.Build(uiRoot.transform, art, font);
            if (panel == null) { Debug.LogError(Tag + "面板构建失败"); return; }

            SettlementPanelView view = uiRoot.GetComponent<SettlementPanelView>();
            if (view == null) view = uiRoot.AddComponent<SettlementPanelView>();
            view.Wire(game);
            view.SetArt(art, font);
            if (game != null) game.WireSettlement(view);

            EditorUtility.SetDirty(view);
            EditorUtility.SetDirty(panel);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log(Tag + "完成：" + UiRoot + "/" + SettlementPanelView.PanelName
                      + "，batch=" + batch + "，缺图=" + CountNull(art));
        }

        static SettlementArtSet LoadArt()
        {
            SettlementArtSet a = new SettlementArtSet();
            a.panelBig = S(Pnl + "pn_面板_大");
            a.card = S(Pnl + "pn_卡片");
            a.listRow = S(Pnl + "pn_列表行");
            a.bottomBar = S(Pnl + "pn_底部操作条");

            a.btnPrimary = S(Btn + "bt_主按钮");
            a.btnSecondary = S(Btn + "bt_次按钮");
            a.btnGhost = S(Btn + "bt_幽灵按钮");
            a.btnDisabled = S(Btn + "bt_禁用按钮");

            a.statusDotTeal = S(Tab + "cp_状态点_青");
            a.labelTeal = S(Tab + "cp_类别_青");
            a.labelAmber = S(Tab + "cp_标签_琥珀");

            a.ringBottom = S(Prg + "pg_进度环_底");
            a.ringAmber = S(Prg + "pg_进度环_琥珀");
            a.ringTeal = S(Prg + "pg_进度环_青");
            a.dotEmpty = S(Prg + "pg_容量点_空心");
            a.dotFilled = S(Prg + "pg_容量点_实心");

            a.badgeCircle = S(Dec + "de_徽章_圆底");
            a.medallion = S(Dec + "de_奖章底");
            a.stepDone = S(Dec + "de_步骤序号_完成");
            a.stepCurrent = S(Dec + "de_步骤序号_当前");
            a.stepDefault = S(Dec + "de_步骤序号_默认");
            a.barPlastic = S(Dec + "de_色条_塑料");
            a.barMetal = S(Dec + "de_色条_金属");
            a.barOrganic = S(Dec + "de_色条_有机");
            a.barElectronic = S(Dec + "de_色条_电子");

            a.divDotted = S(Pnl + "de_分隔线_点线");
            a.divSolid = S(Pnl + "de_分隔线_实线");
            a.icStar = S(Fun + "ic_功能_星");
            a.icCheck = S(Fun + "ic_功能_勾");
            a.icClock = S(Fun + "ic_功能_时钟");
            return a;
        }

        static Sprite S(string rel)
        {
            string path = UiArtImportSettings.LibraryFolder + "/" + rel + ".png";
            Sprite sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sp == null) Debug.LogWarning(Tag + "拆件缺失：" + path);
            return sp;
        }

        static int CountNull(SettlementArtSet a)
        {
            Sprite[] probe =
            {
                a.panelBig, a.card, a.btnPrimary, a.btnGhost,
                a.ringBottom, a.ringTeal, a.badgeCircle, a.stepDone, a.divDotted
            };
            int n = 0;
            for (int i = 0; i < probe.Length; i++) if (probe[i] == null) n++;
            return n;
        }

        static GameObject FindRoot(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) if (roots[i].name == name) return roots[i];
            return null;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) if (all[i].name == name) return all[i];
            return null;
        }
    }
}
