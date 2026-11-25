// @trace UI-007
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 中转站界面的一键生成 + 接线（菜单㉑）。
    ///
    /// v5（2026-11-25 按新 UI 定稿）：02 垃圾分类整块下线，面板改为四页签；
    /// 菜单改为「重建（覆盖手改）」并带二次确认，避免代码版把手工调好的界面顶掉。
    ///
    /// 红线：
    /// 1. 全部用 Editor API 生成，不手写 .unity / .prefab YAML；
    /// 2. 只新建 Panel_StationFiveTabs，不动旧 Panel_Station 及其数据/校验依赖；
    /// 3. 用美术库拆件拼出真实 uGUI 控件（面板/卡片/按钮/页签/进度/图标），
    ///    每个元素具名可在 Inspector 修改，不用静态整页图；
    /// 4. 布局与运行时兜底共用 StationFiveTabsUiFactory，避免两处结构漂移。
    ///
    /// 菜单：Tools/垃圾星球白膜/㉑ 重建中转站界面（四页签·覆盖手改）
    /// 批处理：-executeMethod TrashPlanet.Whitebox.Editor.StationFiveTabsBuilder.BuildBatch
    /// </summary>
    [InitializeOnLoad]
    public static class StationFiveTabsBuilder
    {
        // v4：按 04~08 五张参考图重新对齐真实控件布局（紧凑页签 / 卡片比例 / 按钮配色），触发一次自动重烘焙。
        const string SessionKey = "TrashPlanet.Whitebox.StationFiveTabs.AutoBuilt.v4";
        const string Tag = "[StationFiveTabsBuilder] ";
        const string UiRoot = "UI_Whitebox";
        const string SysRoot = "sys_whitebox";

        // 新库拆件相对 LibraryFolder 的路径（不含扩展名）
        const string Pnl = "06_UI界面拆件/01_面板与容器/";
        const string Btn = "06_UI界面拆件/02_按钮/";
        const string Tab = "06_UI界面拆件/03_页签与标签/";
        const string Prg = "06_UI界面拆件/04_进度与数值/";
        const string Fun = "06_UI界面拆件/07_图标_功能/";

        static StationFiveTabsBuilder()
        {
            EditorApplication.delayCall += AutoEnsure;
            EditorApplication.update += AutoEnsure;
        }

        static void AutoEnsure()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            // 全局总闸：UI 定稿并锁定后，编译/重启不再自动重建，保护手调布局。
            if (UiAutoBuildGuard.Locked) return;
            if (SessionState.GetBool(SessionKey, false)) return;
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != WhiteboxBuilder.ScenePath) return;
            GameObject uiRootGuard = FindRoot(scene, UiRoot);
            if (uiRootGuard == null || FindRoot(scene, SysRoot) == null) return;

            // 防覆盖：面板已存在则跳过自动重建，保护 Inspector 里手调的布局。
            // 域重载后场景序列化引用仍有效，无需重建；强制刷新请手动执行菜单 ㉑（仍为存在即删重建）。
            if (FindDeep(uiRootGuard.transform, StationFiveTabsView.PanelName) != null)
            {
                SessionState.SetBool(SessionKey, true);
                return;
            }

            SessionState.SetBool(SessionKey, true);
            try
            {
                BuildInScene(scene, false);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log(Tag + "首次载入已自动补齐中转站界面（四页签·真实控件版）");
            }
            catch (Exception e)
            {
                SessionState.SetBool(SessionKey, false);
                Debug.LogError(Tag + "自动补齐失败：" + e);
            }
        }

        [MenuItem("Tools/垃圾星球白膜/㉑ 重建中转站界面（四页签·覆盖手改）", false, 21)]
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
            // 本菜单是「存在即删重建」：手调过的层级/位置/颜色/文案会被代码版整块替换，先确认
            if (!EditorUtility.DisplayDialog("重建中转站界面？",
                    "这会删掉现有的 " + StationFiveTabsView.PanelName + "，按代码重新生成四页签面板。\n\n"
                    + "你在 Hierarchy / Inspector 里手调过的层级、位置、尺寸、颜色、文案都会丢失。\n"
                    + "只是想保留现状的话，点「取消」。", "重建（会覆盖手改）", "取消"))
            {
                Debug.Log(Tag + "已取消重建，场景中的面板保持手改现状");
                return;
            }
            BuildInScene(scene, false);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        /// <summary>批处理：打开场景 → 应用贴图导入 → 生成四页签面板 → 校验。</summary>
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
            // 拆件默认可能是 Texture 导入，先确保是 Sprite
            UiArtImportSettings.Apply();

            GameObject uiRoot = FindRoot(scene, UiRoot);
            GameObject sysRoot = FindRoot(scene, SysRoot);
            if (uiRoot == null || sysRoot == null)
            {
                Debug.LogError(Tag + "缺少 " + UiRoot + " 或 " + SysRoot + "，请先跑 ①/④ 生成场景与 Hierarchy UI");
                return;
            }

            // 存在即删重建：只删根节点一次，不在遍历中销毁
            Transform old = FindDeep(uiRoot.transform, StationFiveTabsView.PanelName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);

            StationArtSet art = LoadArt();
            Font font = UiFont.Load();
            GameObject panel = StationFiveTabsUiFactory.Build(uiRoot.transform, art, font);
            if (panel == null) { Debug.LogError(Tag + "面板构建失败"); return; }

            StationFiveTabsView view = uiRoot.GetComponent<StationFiveTabsView>();
            if (view == null) view = uiRoot.AddComponent<StationFiveTabsView>();
            WhiteboxGame game = sysRoot.GetComponent<WhiteboxGame>();
            view.Wire(game);
            view.SetArt(art, font);

            EditorUtility.SetDirty(view);
            EditorUtility.SetDirty(panel);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log(Tag + "完成：" + UiRoot + "/" + StationFiveTabsView.PanelName
                      + "（真实 uGUI 控件），batch=" + batch
                      + "，拆件缺失=" + CountNull(art));
        }

        static StationArtSet LoadArt()
        {
            StationArtSet a = new StationArtSet();
            // 面板 / 容器
            a.panelBig = S(Pnl + "pn_面板_大_氛围");
            a.panelSmall = S(Pnl + "pn_面板_小");
            a.card = S(Pnl + "pn_卡片");
            a.listRow = S(Pnl + "pn_列表行");
            a.highlightRow = S(Pnl + "pn_高亮行");
            a.headerBar = S(Pnl + "pn_头部条");
            a.bottomBar = S(Pnl + "pn_底部操作条");
            a.divSolid = S(Pnl + "de_分隔线_实线");
            a.divDotted = S(Pnl + "de_分隔线_点线");
            a.divDashed = S(Pnl + "de_分隔线_虚线");
            // 按钮
            a.btnPrimary = S(Btn + "bt_主按钮");
            a.btnPrimaryDown = S(Btn + "bt_主按钮_按下");
            a.btnSecondary = S(Btn + "bt_次按钮");
            a.btnSecondaryDown = S(Btn + "bt_次按钮_按下");
            a.btnGhost = S(Btn + "bt_幽灵按钮");
            a.btnSmallPrimary = S(Btn + "bt_小按钮_主");
            a.btnSmallSecondary = S(Btn + "bt_小按钮_次");
            a.btnSmallGhost = S(Btn + "bt_小按钮_幽灵");
            a.btnDanger = S(Btn + "bt_危险按钮");
            a.btnDisabled = S(Btn + "bt_禁用按钮");
            a.btnCloseSquare = S(Btn + "bt_关闭方键");
            // 页签 / 标签 / 徽章
            a.tabNormal = S(Tab + "cp_页签_未选中");
            a.tabSelected = S(Tab + "cp_页签_选中");
            a.tabBar = S(Tab + "cp_页签条底");
            a.statusBadge = S(Tab + "cp_状态徽章_正常");
            a.statusDotTeal = S(Tab + "cp_状态点_青");
            a.statusDotAmber = S(Tab + "cp_状态点_琥珀");
            a.statusDotRed = S(Tab + "cp_状态点_红");
            a.labelAmber = S(Tab + "cp_标签_琥珀");
            a.labelTeal = S(Tab + "cp_类别_青");
            a.countBadge = S(Tab + "cp_数量角标底");
            a.stickerAmber = S(Tab + "cp_贴纸_琥珀");
            a.stickerTeal = S(Tab + "cp_贴纸_青");
            // 进度 / 数值
            a.barBottom = S(Prg + "pg_进度条_底");
            a.fillAmber = S(Prg + "pg_填充_琥珀");
            a.fillTeal = S(Prg + "pg_填充_青色");
            a.fillGreen = S(Prg + "pg_填充_绿色");
            a.fillRed = S(Prg + "pg_填充_红色");
            a.dotEmpty = S(Prg + "pg_容量点_空心");
            a.dotFilled = S(Prg + "pg_容量点_实心");
            a.ringBottom = S(Prg + "pg_进度环_底");
            a.ringTeal = S(Prg + "pg_进度环_青");
            a.ringAmber = S(Prg + "pg_进度环_琥珀");
            // 类别图标（新库目录名与 GarbageBagCatalog 旧目录不同，按新库实际文件名）
            string catDir = "02_类别图标/";
            a.catIcons[0] = S(catDir + "01_塑料");
            a.catIcons[1] = S(catDir + "02_金属");
            a.catIcons[2] = S(catDir + "03_有机");
            a.catIcons[3] = S(catDir + "04_电子");
            // 垃圾单品 8
            string itemDir = "01_垃圾图标_八种/";
            string[] items = { "01_塑料_塑料瓶", "02_塑料_塑料袋", "03_金属_易拉罐", "04_金属_铁片",
                "05_有机_苹果核", "06_有机_枯叶", "07_电子_电池", "08_电子_电路板" };
            for (int i = 0; i < items.Length; i++) a.itemIcons[i] = S(itemDir + items[i]);
            // 材料 4 / 成品 3
            string matDir = "06_UI界面拆件/06_图标_材料与成品/";
            string[] mats = { "ic_材料_再生塑料", "ic_材料_金属零件", "ic_材料_有机原料", "ic_材料_电子元件" };
            for (int i = 0; i < mats.Length; i++) a.materialIcons[i] = S(matDir + mats[i]);
            string[] prods = { "ic_产品_生态肥料包", "ic_产品_再生建材板", "ic_产品_水质过滤器" };
            for (int i = 0; i < prods.Length; i++) a.productIcons[i] = S(matDir + prods[i]);
            // 功能图标
            a.icClose = S(Fun + "ic_功能_关闭");
            a.icArrow = S(Fun + "ic_功能_箭头");
            a.icReceive = S(Fun + "ic_功能_接收");
            a.icSort = S(Fun + "ic_功能_分类");
            a.icProcess = S(Fun + "ic_功能_加工");
            a.icCraft = S(Fun + "ic_功能_制造");
            a.icRepair = S(Fun + "ic_功能_修复");
            a.icUpgrade = S(Fun + "ic_功能_升级");
            a.icPlus = S(Fun + "ic_功能_加");
            a.icMinus = S(Fun + "ic_功能_减");
            a.icCheck = S(Fun + "ic_功能_勾");
            a.icClock = S(Fun + "ic_功能_时钟");
            a.icStar = S(Fun + "ic_功能_星");
            a.icBackpack = S(Fun + "ic_功能_背包");
            return a;
        }

        static Sprite S(string rel)
        {
            string path = UiArtImportSettings.LibraryFolder + "/" + rel + ".png";
            Sprite sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sp == null) Debug.LogWarning(Tag + "拆件缺失：" + path);
            return sp;
        }

        static int CountNull(StationArtSet a)
        {
            int n = 0;
            Sprite[] groups = { a.panelBig, a.card, a.btnPrimary, a.tabSelected, a.barBottom, a.icClose };
            for (int i = 0; i < groups.Length; i++) if (groups[i] == null) n++;
            for (int i = 0; i < a.catIcons.Length; i++) if (a.catIcons[i] == null) n++;
            for (int i = 0; i < a.itemIcons.Length; i++) if (a.itemIcons[i] == null) n++;
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
