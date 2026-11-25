// @trace ART-004, ART-008, MECH-013, PLAT-005, BAG-003, UI-006
using System;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 中转站面板（Panel_Station）+ 数量弹窗（Panel_Quantity）的「美术版」重排。
    ///
    /// 红线：
    /// 1. 只重建这两个面板的层级，不动场景其它对象、不动玩家双手、不动 Canvas；
    /// 2. 所有功能对象沿用原名字（Txt_Repaired / Txt_Raw / Txt_StationMaterials /
    ///    Btn_Process_0..3 / Btn_Craft_0..2 / Btn_Upgrade / Btn_BagUpgrade / Btn_Deposit /
    ///    Btn_CloseStation / JobBar+JobFill+Txt_Job），因此 WhiteboxCanvasHud 的按名查找、
    ///    StationQuantityDialog 的按名装配、WhiteboxValidator 的检查全部照旧；
    /// 3. 按钮 OnClick 依旧用 UnityEventTools 持久化绑到 WhiteboxUiActions，Inspector 里可见可改绑；
    /// 4. 每个视觉元素 = 一个独立的 Image + 一个语义化名字，美术换图只要改 Image 的 Source Image，
    ///    或者直接覆盖 Assets/Art/UI/中转站皮肤/ 下的同名 PNG（边距由 UiArtImportSettings 统一写）。
    ///
    /// 菜单：Tools/垃圾星球白膜/⑤ 重排中转站 UI（美术版）
    /// 批处理：-executeMethod TrashPlanet.Whitebox.Editor.StationUiBuilder.RebuildBatch
    /// </summary>
    public static class StationUiBuilder
    {
        const string Tag = "[StationUiBuilder] ";
        const string PanelName = "Panel_Station";
        const string QtyName = "Panel_Quantity";
        const string SkinPath = "Assets/Art/UI/中转站皮肤/";
        const string LibPath = "Assets/Art/UI/垃圾星球_UI资源库/";

        // 与概念图一致的配色（面板本身由贴图决定，这里只给文字 / 纯色件用）
        static readonly Color TextStrong = new Color(0.965f, 0.955f, 0.925f, 1f);
        static readonly Color TextSoft = new Color(0.875f, 0.86f, 0.815f, 1f);
        static readonly Color TextDim = new Color(0.71f, 0.69f, 0.63f, 1f);
        static readonly Color Accent = new Color(0.24f, 0.84f, 0.75f, 1f);
        static readonly Color Amber = new Color(0.93f, 0.73f, 0.33f, 1f);
        static readonly Color OnAccent = new Color(0.06f, 0.11f, 0.10f, 1f);

        // 面板尺寸（1920×1080 参考分辨率下居中）
        const float PW = 1680f;
        const float PH = 840f;
        const float CY = 160f;   // 三张卡片的顶边
        const float CH = 520f;   // 三张卡片的高

        static readonly string[] CatIconPaths =
        {
            "02_类别图标/ic_类别_塑料", "02_类别图标/ic_类别_金属",
            "02_类别图标/ic_类别_有机", "02_类别图标/ic_类别_电子"
        };
        static readonly string[] CatNames = { "塑料", "金属", "有机", "电子" };
        static readonly string[] CatNamesEn = { "PLASTIC", "METAL", "ORGANIC", "E-WASTE" };
        static readonly string[] MatIconPaths =
        {
            "06_UI界面拆件/图标_材料与成品/ic_材料_再生塑料",
            "06_UI界面拆件/图标_材料与成品/ic_材料_金属零件",
            "06_UI界面拆件/图标_材料与成品/ic_材料_有机原料",
            "06_UI界面拆件/图标_材料与成品/ic_材料_电子元件"
        };
        static readonly string[] MatNames = { "再生塑料", "金属零件", "有机原料", "电子元件" };
        static readonly string[] ProductIconPaths =
        {
            "06_UI界面拆件/图标_材料与成品/ic_产品_再生建材板",
            "06_UI界面拆件/图标_材料与成品/ic_产品_生态肥料包",
            "06_UI界面拆件/图标_材料与成品/ic_产品_水质过滤器"
        };

        // ---------------------------------------------------------------
        // 入口
        // ---------------------------------------------------------------

        [MenuItem("Tools/垃圾星球白膜/⑤ 重排中转站 UI（美术版）", false, 14)]
        public static void RebuildMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning(Tag + "请先退出 Play 模式");
                return;
            }
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != WhiteboxBuilder.ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(WhiteboxBuilder.ScenePath, OpenSceneMode.Single);
            }
            bool ok = Rebuild(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log(Tag + (ok ? "重排完成，场景已保存" : "重排失败，详见上方日志"));
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("中转站 UI",
                    ok ? "中转站面板已按美术版重排。\n\n换图：选中任意 Img_* 节点，在 Image 的 Source Image 里换 Sprite；"
                         + "或直接覆盖 Assets/Art/UI/中转站皮肤/ 下的同名 PNG。"
                       : "重排失败：工程里还没生成 UI_Whitebox，请先跑 ④ 生成 / 补齐 Hierarchy UI。", "好");
        }

        /// <summary>批处理：重排 + 保存 + 校验，退出码即结论。</summary>
        public static void RebuildBatch()
        {
            try
            {
                Scene scene = EditorSceneManager.OpenScene(WhiteboxBuilder.ScenePath, OpenSceneMode.Single);
                bool ok = Rebuild(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log(Tag + "batch rebuild ok=" + ok);
                if (!ok) { EditorApplication.Exit(1); return; }
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

        // ---------------------------------------------------------------
        // 重建
        // ---------------------------------------------------------------

        public static bool Rebuild(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return false;
            UiArtImportSettings.Apply();

            GameObject ui = FindRoot(scene, WhiteboxUiBuilder.RootName);
            if (ui == null)
            {
                Debug.LogError(Tag + "场景里没有 " + WhiteboxUiBuilder.RootName + "，请先执行 ④ 生成 / 补齐 Hierarchy UI");
                return false;
            }

            WhiteboxGame game = UnityEngine.Object.FindObjectOfType<WhiteboxGame>();
            WhiteboxCanvasHud view = ui.GetComponent<WhiteboxCanvasHud>();
            WhiteboxUiActions actions = ui.GetComponent<WhiteboxUiActions>();
            Transform root = ui.transform;

            DestroyChild(root, PanelName);
            DestroyChild(root, QtyName);

            BuildStation(root, game);
            BuildQuantity(root, game);

            // 白光遮罩永远在最上层
            Transform flash = root.Find("StageFlash");
            if (flash != null) flash.SetAsLastSibling();

            // 旧面板被销毁后，视图里的引用是「伪 null」；按名字重新找回并写回
            if (view != null)
            {
                view.EditorResolve();
                EditorUtility.SetDirty(view);
                LogRefs(view);
            }
            if (actions != null) EditorUtility.SetDirty(actions);
            if (game != null) EditorUtility.SetDirty(game);

            Debug.Log(Tag + "中转站面板已重建：头部 / 状态条 / 背包卡 / 材料与加工卡 / 制造与升级卡 / 后台任务 / 底部");
            return true;
        }

        /// <summary>把 WhiteboxCanvasHud / 弹窗按名字找回来的关键引用点名打印一次，漏绑能立刻看到。</summary>
        static void LogRefs(WhiteboxCanvasHud v)
        {
            int missing = 0;
            missing += Check(v.StationPanel, "StationPanel");
            missing += Check(v.TxtRepaired, "Txt_Repaired");
            missing += Check(v.TxtRaw, "Txt_Raw");
            missing += Check(v.TxtStationMaterials, "Txt_StationMaterials");
            missing += Check(v.TxtUpgrade, "Txt_Upgrade");
            missing += Check(v.TxtBagUpgrade, "Txt_BagUpgrade");
            missing += Check(v.JobGroup, "JobBar");
            missing += Check(v.JobFill, "JobFill");
            missing += Check(v.TxtJob, "Txt_Job");
            missing += Check(v.QuantityDialog, "Panel_Quantity");
            if (v.ProcessLabels != null)
                for (int i = 0; i < v.ProcessLabels.Length; i++) missing += Check(v.ProcessLabels[i], "ProcessLabel" + i);
            if (v.CraftLabels != null)
                for (int i = 0; i < v.CraftLabels.Length; i++) missing += Check(v.CraftLabels[i], "CraftLabel" + i);
            Debug.Log(Tag + "引用体检：缺失 " + missing + " 项");
        }

        static int Check(UnityEngine.Object o, string name)
        {
            if (o != null) return 0;
            Debug.LogWarning(Tag + "引用缺失：" + name);
            return 1;
        }

        static GameObject BuildStation(Transform root, WhiteboxGame game)
        {
            Font font = LoadFont();
            GameObject panel = RootPanel(root, PanelName, PW, PH, Skin("pn_主面板"));

            // ---------------- 头部 ----------------
            Img(panel.transform, "Img_HeaderIcon", 24f, 20f, 44f, 44f, Lib("03_界面拆件/ic_回收"));
            Lbl(panel.transform, "Txt_StationTitle", "垃圾中转站", 84f, 10f, 560f, 44f, 32, TextStrong,
                TextAnchor.MiddleLeft, true);
            Lbl(panel.transform, "Txt_StationSub", "TRANSFER  STATION   ·   SORT  ·  PROCESS  ·  CRAFT",
                86f, 53f, 760f, 18f, 12, TextDim);

            Image badge = Img(panel.transform, "Img_Badge", PW - 24f - 36f - 16f - 132f, 24f, 132f, 30f,
                Skin("cp_状态徽章"), null, false, Image.Type.Sliced);
            if (badge != null)
                Lbl(badge.transform, "Txt_BadgeValue", "●  运营中", 0f, 0f, 132f, 30f, 13, Accent, TextAnchor.MiddleCenter);

            Button close = Btn(panel.transform, "Btn_CloseStation", "×", PW - 24f - 36f, 22f, 36f, 36f,
                Skin("bt_关闭方键"), TextStrong, 20, TextAnchor.MiddleCenter, false);
            if (close != null)
            {
                Image ci = close.GetComponent<Image>();
                if (ci != null) ci.type = Image.Type.Sliced;
            }

            Div(panel.transform, "Img_HeaderLine", 24f, 92f, PW - 48f, 1f);

            // ---------------- 状态条 ----------------
            RectTransform status = Img(panel.transform, "Bar_Status", 24f, 104f, PW - 48f, 42f,
                Skin("pn_列表行"), null, false, Image.Type.Sliced).rectTransform;
            Img(status, "Img_StatusIcon", 12f, 11f, 20f, 20f, Lib("03_界面拆件/ic_警示"), Amber, false,
                Image.Type.Simple, true);
            Lbl(status, "Txt_Repaired", "已修复: 否（先造 1 块再生建材板）  ·  背包 Lv.1/2（总 0/50）",
                42f, 7f, PW - 48f - 54f, 28f, 15, TextSoft);

            RectTransform bag = Card(panel.transform, "Card_Bag", "Bag", 24f, CY, 496f, CH, "垃圾背包", "GARBAGE BAG");
            Lbl(bag, "Txt_BagScope", "只存垃圾/废弃物 · 工具背包 I · 金币交易 B", 18f, 68f, 460f, 24f, 13, TextDim);
            Lbl(bag, "Txt_BagSum", "0 / 50", 18f, 100f, 240f, 48f, 30, Amber, TextAnchor.MiddleLeft, true);

            Div(bag, "Img_RawLine", 18f, 268f, 460f, 1f);
            Lbl(bag, "Txt_Raw", "待加工垃圾  塑/金/有/电: 0/0/0/0", 18f, 280f, 460f, 24f, 14, TextDim);
            Lbl(bag, "Txt_BagBoundary", "本区只做投放；背包的查看与丢弃已移到 G 面板",
                18f, 314f, 460f, 24f, 12, TextDim);

            Button deposit = Btn(bag, "Btn_Deposit", "① 投放背包全部垃圾", 18f, 352f, 460f, 52f,
                Skin("bt_主按钮"), OnAccent, 17, TextAnchor.MiddleCenter, true);
            if (deposit != null)
            {
                Image di = deposit.GetComponent<Image>();
                if (di != null) di.type = Image.Type.Sliced;
            }

            // ---------------- 卡片：材料库存 + 材料加工 ----------------
            RectTransform mat = Card(panel.transform, "Card_Process", "Proc", 536f, CY, 560f, CH, "材料库存", "MATERIALS");
            Lbl(mat, "Txt_StationMaterials", "材料  塑板/金件/有机/芯: 0/0/0/0", 18f, 50f, 524f, 18f, 13, TextDim);
            for (int i = 0; i < 4; i++)
            {
                float x = 18f + (i % 2) * 264f;
                float y = 74f + (i / 2) * 84f;
                RectTransform slot = Img(mat, "Slot_Mat_" + i, x, y, 260f, 76f,
                    Skin("pn_列表行"), null, false, Image.Type.Sliced).rectTransform;
                Img(slot, "Img_MatIcon_" + i, 10f, 20f, 36f, 36f, Lib(MatIconPaths[i]), null, false,
                    Image.Type.Simple, true);
                Lbl(slot, "Txt_MatName_" + i, MatNames[i], 54f, 5f, 160f, 20f, 13, TextDim);
                Lbl(slot, "Txt_MatVal_" + i, "0", 54f, 25f, 90f, 30f, 21, Amber, TextAnchor.MiddleLeft, true);
                Lbl(slot, "Txt_MatUnit_" + i, "件", 228f, 30f, 24f, 20f, 12, TextDim, TextAnchor.MiddleRight);
            }

            Div(mat, "Img_MatLine", 18f, 240f, 524f, 1f);
            SectionTitle(mat, "Txt_ProcTitle", "材料加工", 18f, 248f);
            Lbl(mat, "Txt_ProcHint", "投入 ×N  →  产物", 300f, 249f, 242f, 20f, 12, TextDim,
                TextAnchor.MiddleRight);

            for (int i = 0; i < 4; i++)
            {
                float y = 276f + i * 57f;
                Button b = Btn(mat, "Btn_Process_" + i,
                    "加工" + StationSystem.CategoryNames[i] + " ×" + 1 + "→" + StationSystem.MaterialNames[i],
                    18f, y, 524f, 52f, Skin("pn_列表行"), TextSoft, 15, TextAnchor.MiddleLeft, false);
                if (b == null) continue;
                Image bi = b.GetComponent<Image>();
                if (bi != null) bi.type = Image.Type.Sliced;
                Img(b.transform, "Img_ProcIcon_" + i, 10f, 13f, 26f, 26f,
                    Lib("06_UI界面拆件/图标_功能/ic_功能_加工"), Accent, false, Image.Type.Simple, true);
                Text label = b.GetComponentInChildren<Text>(true);
                if (label != null)
                {
                    RectTransform lrt = label.rectTransform;
                    lrt.offsetMin = new Vector2(44f, 10f);
                    lrt.offsetMax = new Vector2(-40f, -10f);
                }
                Img(b.transform, "Img_Arrow_" + i, 496f, 16f, 16f, 16f,
                    Lib("06_UI界面拆件/图标_功能/ic_功能_箭头"), TextDim, false, Image.Type.Simple, true);
            }

            // ---------------- 卡片：产品制造 + 升级 ----------------
            RectTransform craft = Card(panel.transform, "Card_Craft", "Craft", 1112f, CY, 544f, CH, "产品制造", "CRAFT");
            for (int i = 0; i < 3; i++)
            {
                float y = 54f + i * 74f;
                Button b = Btn(craft, "Btn_Craft_" + i, StationSystem.RecipeNames[i], 18f, y, 508f, 68f,
                    Skin("pn_列表行"), TextSoft, 15, TextAnchor.MiddleLeft, false);
                if (b == null) continue;
                Image bi = b.GetComponent<Image>();
                if (bi != null) bi.type = Image.Type.Sliced;
                Img(b.transform, "Img_ProdIcon_" + i, 10f, 14f, 40f, 40f, Lib(ProductIconPaths[i]), null,
                    false, Image.Type.Simple, true);
                Text label = b.GetComponentInChildren<Text>(true);
                if (label != null)
                {
                    RectTransform lrt = label.rectTransform;
                    lrt.offsetMin = new Vector2(58f, 12f);
                    lrt.offsetMax = new Vector2(-40f, -12f);
                }
                Img(b.transform, "Img_CraftArrow_" + i, 488f, 26f, 16f, 16f,
                    Lib("06_UI界面拆件/图标_功能/ic_功能_箭头"), TextDim, false, Image.Type.Simple, true);
            }

            Div(craft, "Img_CraftLine", 18f, 286f, 508f, 1f);
            SectionTitle(craft, "Txt_UpTitle", "工具与背包", 18f, 294f);

            Button up = Btn(craft, "Btn_Upgrade", "升级工具（Lv.1 → 2）", 18f, 320f, 508f, 48f,
                Skin("bt_次按钮"), Amber, 15, TextAnchor.MiddleLeft, false);
            if (up != null)
            {
                Image ui2 = up.GetComponent<Image>();
                if (ui2 != null) ui2.type = Image.Type.Sliced;
                Img(up.transform, "Img_UpIcon", 12f, 12f, 24f, 24f,
                    Lib("06_UI界面拆件/图标_功能/ic_功能_升级"), Amber, false, Image.Type.Simple, true);
                Text label = up.GetComponentInChildren<Text>(true);
                if (label != null) label.rectTransform.offsetMin = new Vector2(44f, 2f);
            }

            // ⑥ 专精升级 2×2：Q 轮换的单件生效对象，解锁/升级都走 StationSystem
            string[] specNames = { "压缩钳·塑料", "磁吸爪·金属", "堆肥铲·有机", "解码钳·电子" };
            for (int i = 0; i < 4; i++)
            {
                Button sb = Btn(craft, "Btn_SpecTool_" + i, "⑥ " + specNames[i],
                    18f + (i % 2) * 258f, 372f + (i / 2) * 52f, 250f, 44f,
                    Skin("bt_幽灵按钮"), TextSoft, 14, TextAnchor.MiddleLeft, false);
                if (sb == null) continue;
                Image si = sb.GetComponent<Image>();
                if (si != null) si.type = Image.Type.Sliced;
            }

            // Btn_BagUpgrade 必须是 Panel_Station 的直接子物体（WhiteboxValidator 用 transform.Find 检查），
            // 视觉位置仍落在「④ 工具与背包」区块里，绝对坐标与卡片内部一致。
            Button bagUp = Btn(panel.transform, "Btn_BagUpgrade",
                "升级背包（总 50 → 100，需 15 金属零件）", 1130f, CY + 376f, 508f, 48f,
                Skin("bt_幽灵按钮"), TextSoft, 15, TextAnchor.MiddleLeft, false);
            if (bagUp != null)
            {
                Image bi = bagUp.GetComponent<Image>();
                if (bi != null) bi.type = Image.Type.Sliced;
                Img(bagUp.transform, "Img_BagUpIcon", 12f, 12f, 24f, 24f,
                    Lib("06_UI界面拆件/图标_功能/ic_功能_背包"), TextSoft, false, Image.Type.Simple, true);
                Text label = bagUp.GetComponentInChildren<Text>(true);
                if (label != null) label.rectTransform.offsetMin = new Vector2(44f, 2f);
            }
            Lbl(panel.transform, "Txt_UpHint", "升级后立即生效；材料不足时按钮会给出缺口提示",
                1130f, CY + 434f, 508f, 20f, 12, TextDim);

            // ---------------- 后台任务 ----------------
            RectTransform job = Img(panel.transform, "JobBar", 24f, 692f, PW - 48f, 44f,
                Skin("pn_列表行"), null, false, Image.Type.Sliced).rectTransform;
            Lbl(job, "Txt_JobCaption", "后台任务", 14f, 11f, 92f, 22f, 13, TextDim);
            Img(job, "Img_JobBarBg", 110f, 17f, 1330f, 10f, Skin("pg_条_底"), null, false, Image.Type.Sliced);
            Image jobFill = Img(job, "JobFill", 110f, 17f, 1330f, 10f, Skin("pg_条_填充_琥珀"), null, false,
                Image.Type.Filled);
            if (jobFill != null)
            {
                jobFill.fillMethod = Image.FillMethod.Horizontal;
                jobFill.fillOrigin = 0;
                jobFill.fillAmount = 0.4f;
            }
            Lbl(job, "Txt_Job", "加工塑料 45%", 1436f, 10f, 180f, 24f, 14, Amber, TextAnchor.MiddleRight);

            // ---------------- 底部 ----------------
            Div(panel.transform, "Img_FooterLine", 24f, 748f, PW - 48f, 1f);
            Lbl(panel.transform, "Txt_StationHint",
                "M / ESC 关闭  ·  加工与制造会弹出数量选择  ·  按钮 OnClick 已持久化绑定到 WhiteboxUiActions，可自行改绑",
                24f, 755f, 1240f, 38f, 13, TextDim);
            Lbl(panel.transform, "Txt_Version", "中转站面板 · 美术版排版", 1380f, 758f, 276f, 20f, 12,
                TextDim, TextAnchor.MiddleRight);

            // ---------------- 数据填充（只读 State，仅写文字 / 进度） ----------------
            StationBoardView board = panel.AddComponent<StationBoardView>();
            board.Game = game;
            board.BagSum = FindText(panel.transform, "Txt_BagSum");
            board.BagCounts = new Text[4];
            board.BagCaps = new Text[4];
            board.BagFills = new Image[4];
            board.MaterialCounts = new Text[4];
            for (int i = 0; i < 4; i++)
            {
                board.BagCounts[i] = FindText(panel.transform, "Txt_BagCount_" + i);
                board.BagCaps[i] = FindText(panel.transform, "Txt_BagCap_" + i);
                board.BagFills[i] = FindImage(panel.transform, "Img_BagBarFill_" + i);
                board.MaterialCounts[i] = FindText(panel.transform, "Txt_MatVal_" + i);
            }

            BindStationButtons(panel.transform, root.GetComponent<WhiteboxUiActions>());

            // 初始失活，由 WhiteboxCanvasHud 每帧决定显隐
            panel.SetActive(false);
            return panel;
        }

        // ---------------------------------------------------------------
        // 数量弹窗（保持原有控件名，StationQuantityDialog 照旧按名字装配）
        // ---------------------------------------------------------------

        static GameObject BuildQuantity(Transform root, WhiteboxGame game)
        {
            Font font = LoadFont();
            const float w = 620f;
            const float h = 380f;
            GameObject panel = RootPanel(root, QtyName, w, h, Skin("pn_主面板"));
            Transform t = panel.transform;

            SectionTitle(t, "Txt_QtyTitle", "加工塑料 · 选择数量", 20f, 14f, 20);
            Lbl(t, "Txt_QtyInfo", "可执行 1 ~ 1 次  ·  预计 2.5 s", 20f, 48f, 580f, 22f, 14, TextDim);
            Lbl(t, "Txt_QtyValue", "× 1", 20f, 72f, 580f, 54f, 34, Amber, TextAnchor.MiddleLeft, true);
            Div(t, "Img_QtyLine", 20f, 134f, 580f, 1f);

            Button minus = Btn(t, "Btn_QtyMinus", "−", 20f, 150f, 38f, 38f, Skin("bt_幽灵按钮"),
                TextStrong, 18, TextAnchor.MiddleCenter, true);
            Slider sld = SliderBar(t, "Sld_Qty", 66f, 155f, 330f, 28f);
            Button plus = Btn(t, "Btn_QtyPlus", "+", 406f, 150f, 38f, 38f, Skin("bt_幽灵按钮"),
                TextStrong, 18, TextAnchor.MiddleCenter, true);
            InputField inp = NumberInput(t, "Inp_Qty", 454f, 150f, 146f, 38f);
            foreach (Button b in new[] { minus, plus })
                if (b != null)
                {
                    Image bi = b.GetComponent<Image>();
                    if (bi != null) bi.type = Image.Type.Sliced;
                }

            Lbl(t, "Txt_QtyDetail", "消耗 --  →  --", 20f, 200f, 580f, 24f, 15, TextSoft);
            Div(t, "Img_QtyLine2", 20f, 232f, 580f, 1f);
            Lbl(t, "Txt_QtyHint", "拖动滑块 / 直接输入数字 / 点「一键全部」一次做满；确认后开始一次连续任务（耗时按次数叠加）",
                20f, 240f, 580f, 38f, 12, TextDim);

            Button cancel = Btn(t, "Btn_QtyCancel", "取消", 20f, 300f, 120f, 44f, Skin("bt_幽灵按钮"),
                TextSoft, 15, TextAnchor.MiddleCenter, false);
            Button all = Btn(t, "Btn_QtyAll", "一键全部（1）", 152f, 300f, 200f, 44f, Skin("bt_次按钮"),
                Amber, 15, TextAnchor.MiddleCenter, false);
            Button confirm = Btn(t, "Btn_QtyConfirm", "确认执行", 364f, 300f, 236f, 44f, Skin("bt_主按钮"),
                OnAccent, 16, TextAnchor.MiddleCenter, true);
            foreach (Button b in new[] { cancel, all, confirm })
                if (b != null)
                {
                    Image bi = b.GetComponent<Image>();
                    if (bi != null) bi.type = Image.Type.Sliced;
                }

            StationQuantityDialog d = panel.AddComponent<StationQuantityDialog>();
            d.Game = game;
            d.TxtTitle = FindText(t, "Txt_QtyTitle");
            d.TxtInfo = FindText(t, "Txt_QtyInfo");
            d.TxtValue = FindText(t, "Txt_QtyValue");
            d.TxtDetail = FindText(t, "Txt_QtyDetail");
            d.TxtAll = all != null ? all.GetComponentInChildren<Text>(true) : null;
            d.Sld = sld;
            d.Inp = inp;
            d.BtnConfirm = confirm;
            d.BtnMinus = minus;
            d.BtnPlus = plus;
            d.BtnAll = all;
            d.BtnCancel = cancel;

            panel.SetActive(false);
            return panel;
        }

        static Slider SliderBar(Transform parent, string name, float x, float y, float w, float h)
        {
            RectTransform rt = Node(parent, name, x, y, w, h);
            Slider s = rt.gameObject.AddComponent<Slider>();

            RectTransform bg = Stretch(rt, "Background");
            bg.anchorMin = new Vector2(0f, 0.28f);
            bg.anchorMax = new Vector2(1f, 0.72f);
            bg.offsetMin = Vector2.zero;
            bg.offsetMax = Vector2.zero;
            Image bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.sprite = Skin("pg_条_底");
            bgImg.type = Image.Type.Sliced;
            bgImg.color = Color.white;
            bgImg.raycastTarget = true;

            RectTransform area = Stretch(rt, "Fill Area");
            area.anchorMin = new Vector2(0f, 0.28f);
            area.anchorMax = new Vector2(1f, 0.72f);
            area.offsetMin = new Vector2(6f, 0f);
            area.offsetMax = new Vector2(-6f, 0f);
            RectTransform fill = Stretch(area, "Fill");
            Image fillImg = fill.gameObject.AddComponent<Image>();
            fillImg.sprite = Skin("pg_条_填充_青");
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillOrigin = 0;
            fillImg.color = Color.white;

            RectTransform handleArea = Stretch(rt, "Handle Slide Area");
            handleArea.offsetMin = new Vector2(10f, 0f);
            handleArea.offsetMax = new Vector2(-10f, 0f);
            RectTransform handle = Node(handleArea, "Handle", 0f, 0f, 16f, 28f);
            handle.anchorMin = new Vector2(0f, 0f);
            handle.anchorMax = new Vector2(0f, 1f);
            handle.sizeDelta = new Vector2(16f, 0f);
            handle.pivot = new Vector2(0.5f, 0.5f);
            handle.anchoredPosition = Vector2.zero;
            Image handleImg = handle.gameObject.AddComponent<Image>();
            handleImg.sprite = Skin("cp_滑块手柄");
            handleImg.color = Color.white;
            handleImg.raycastTarget = true;

            s.fillRect = fill;
            s.handleRect = handle;
            s.targetGraphic = handleImg;
            s.direction = Slider.Direction.LeftToRight;
            s.wholeNumbers = true;
            s.minValue = 1f;
            s.maxValue = 10f;
            s.value = 1f;
            return s;
        }

        static InputField NumberInput(Transform parent, string name, float x, float y, float w, float h)
        {
            RectTransform rt = Node(parent, name, x, y, w, h);
            Image bgImg = rt.gameObject.AddComponent<Image>();
            bgImg.sprite = Skin("pn_列表行");
            bgImg.type = Image.Type.Sliced;
            bgImg.color = Color.white;
            bgImg.raycastTarget = true;
            InputField field = rt.gameObject.AddComponent<InputField>();

            Font font = LoadFont();
            RectTransform phRt = Stretch(rt, "Placeholder");
            phRt.offsetMin = new Vector2(8f, 2f);
            phRt.offsetMax = new Vector2(-8f, -2f);
            Text ph = phRt.gameObject.AddComponent<Text>();
            ph.font = font;
            ph.fontSize = 16;
            ph.color = new Color(TextDim.r, TextDim.g, TextDim.b, 0.6f);
            ph.alignment = TextAnchor.MiddleCenter;
            ph.raycastTarget = false;
            ph.text = "1";

            RectTransform txRt = Stretch(rt, "Text");
            txRt.offsetMin = new Vector2(8f, 2f);
            txRt.offsetMax = new Vector2(-8f, -2f);
            Text tx = txRt.gameObject.AddComponent<Text>();
            tx.font = font;
            tx.fontSize = 17;
            tx.color = TextStrong;
            tx.alignment = TextAnchor.MiddleCenter;
            tx.horizontalOverflow = HorizontalWrapMode.Overflow;
            tx.verticalOverflow = VerticalWrapMode.Overflow;
            tx.raycastTarget = false;
            tx.text = "1";

            field.targetGraphic = bgImg;
            field.textComponent = tx;
            field.placeholder = ph;
            field.lineType = InputField.LineType.SingleLine;
            field.contentType = InputField.ContentType.IntegerNumber;
            field.characterLimit = 5;
            return field;
        }

        // ---------------------------------------------------------------
        // 绑定（与原 WhiteboxUiBuilder 一致：只补空绑定）
        // ---------------------------------------------------------------

        static void BindStationButtons(Transform panel, WhiteboxUiActions a)
        {
            if (a == null)
            {
                Debug.LogWarning(Tag + "UI_Whitebox 上没找到 WhiteboxUiActions，按钮未绑定（跑一次 ④ 生成 / 补齐 Hierarchy UI 可自动补）");
                return;
            }
            Bind(panel, "Btn_Deposit", new UnityAction(a.DepositAll));
            for (int i = 0; i < 4; i++)
                BindInt(panel, "Btn_Process_" + i, new UnityAction<int>(a.Process), i);
            for (int i = 0; i < 3; i++)
                BindInt(panel, "Btn_Craft_" + i, new UnityAction<int>(a.Craft), i);
            Bind(panel, "Btn_Upgrade", new UnityAction(a.UpgradeTool));
            Bind(panel, "Btn_BagUpgrade", new UnityAction(a.UpgradeBackpack));
            for (int i = 0; i < 4; i++)
                BindInt(panel, "Btn_SpecTool_" + i, new UnityAction<int>(a.UpgradeSpecTool), i);
            Bind(panel, "Btn_CloseStation", new UnityAction(a.CloseStation));
        }

        static void Bind(Transform root, string name, UnityAction call)
        {
            Button b = FindButton(root, name);
            if (b == null || b.onClick.GetPersistentEventCount() > 0) return;
            UnityEventTools.AddPersistentListener(b.onClick, call);
            EditorUtility.SetDirty(b);
        }

        static void BindInt(Transform root, string name, UnityAction<int> call, int arg)
        {
            Button b = FindButton(root, name);
            if (b == null || b.onClick.GetPersistentEventCount() > 0) return;
            UnityEventTools.AddIntPersistentListener(b.onClick, call, arg);
            EditorUtility.SetDirty(b);
        }

        static Button FindButton(Transform root, string name)
        {
            Transform t = FindTransform(root, name);
            return t != null ? t.GetComponent<Button>() : null;
        }

        // ---------------------------------------------------------------
        // 构造小工具
        // ---------------------------------------------------------------

        static GameObject RootPanel(Transform parent, string name, float w, float h, Sprite bg)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = Vector2.zero;
            Image img = go.GetComponent<Image>();
            if (bg != null)
            {
                img.sprite = bg;
                img.type = Image.Type.Sliced;
                img.color = Color.white;
            }
            else
            {
                img.color = new Color(0.12f, 0.11f, 0.10f, 0.97f); // 缺图时退回纯色，UI 不至于空白
            }
            img.raycastTarget = true;
            return go;
        }

        /// <summary>卡片：底色 + 左侧竖条 + 中英文标题。</summary>
        static RectTransform Card(Transform parent, string name, string id, float x, float y, float w, float h,
            string title, string titleEn)
        {
            RectTransform card = Img(parent, name, x, y, w, h, Skin("pn_卡片"), null, false,
                Image.Type.Sliced).rectTransform;
            Img(card, "Img_" + id + "Accent", 18f, 14f, 3f, 20f, null, Accent, false);
            Lbl(card, "Txt_" + id + "Title", title, 28f, 6f, 360f, 28f, 18, TextStrong,
                TextAnchor.MiddleLeft, true);
            Lbl(card, "Txt_" + id + "TitleEn", titleEn, 28f, 34f, 360f, 16f, 10, TextDim);
            return card;
        }

        static void SectionTitle(Transform parent, string name, string text, float x, float y, int size = 16)
        {
            Img(parent, name + "Accent", x, y + 3f, 3f, (float)size - 2f, null, Accent, false);
            Lbl(parent, name, text, x + 10f, y - 1f, 320f, (float)size + 8f, size, Accent, TextAnchor.MiddleLeft, true);
        }

        static RectTransform Node(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, -y);
            return rt;
        }

        static RectTransform Stretch(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>建立一个 Image 节点；sprite 为空时（美术还没导入）退回纯色，保证布局可读。</summary>
        static Image Img(Transform parent, string name, float x, float y, float w, float h, Sprite sprite,
            Color? tint = null, bool raycast = false, Image.Type type = Image.Type.Simple, bool preserve = false)
        {
            RectTransform rt = Node(parent, name, x, y, w, h);
            Image img = rt.gameObject.AddComponent<Image>();
            Color c = tint ?? Color.white;
            if (sprite != null)
            {
                img.sprite = sprite;
                img.type = type;
                img.color = c;
            }
            else
            {
                img.color = c.a >= 1f ? c : new Color(c.r, c.g, c.b, Mathf.Max(c.a, 0.12f));
            }
            img.preserveAspect = preserve;
            img.raycastTarget = raycast;
            return img;
        }

        static void Div(Transform parent, string name, float x, float y, float w, float h)
        {
            Img(parent, name, x, y, w, h, Skin("de_分隔线"), Color.white, false);
        }

        static Text Lbl(Transform parent, string name, string value, float x, float y, float w, float h,
            int size, Color color, TextAnchor align = TextAnchor.MiddleLeft, bool bold = false)
        {
            RectTransform rt = Node(parent, name, x, y, w, h);
            Text t = rt.gameObject.AddComponent<Text>();
            t.font = LoadFont();
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = value;
            return t;
        }

        /// <summary>按钮：Image（可换图）+ Button + 居中/左对齐 Label。返回 Button，null 表示建失败。</summary>
        static Button Btn(Transform parent, string name, string caption, float x, float y, float w, float h,
            Sprite sprite, Color labelColor, int size, TextAnchor align, bool bold)
        {
            RectTransform rt = Node(parent, name, x, y, w, h);
            Image img = rt.gameObject.AddComponent<Image>();
            if (sprite != null)
            {
                img.sprite = sprite;
                img.type = Image.Type.Sliced;
                img.color = Color.white;
            }
            else
            {
                img.color = new Color(0.18f, 0.22f, 0.22f, 0.9f);
            }
            img.raycastTarget = true;

            Button b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            ColorBlock cb = b.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.16f, 1.16f, 1.16f, 1f);
            cb.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            cb.selectedColor = Color.white;
            cb.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.9f);
            cb.fadeDuration = 0.08f;
            b.colors = cb;

            RectTransform labelRt = Stretch(rt, "Label");
            labelRt.offsetMin = new Vector2(16f, 2f);
            labelRt.offsetMax = new Vector2(-16f, -2f);
            Text t = labelRt.gameObject.AddComponent<Text>();
            t.font = LoadFont();
            t.fontSize = size;
            t.color = labelColor;
            t.alignment = align;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = caption;
            return b;
        }

        // ---------------------------------------------------------------
        // 资产 / 查找
        // ---------------------------------------------------------------

        static Sprite Skin(string name) { return AssetDatabase.LoadAssetAtPath<Sprite>(SkinPath + name + ".png"); }

        static Sprite Lib(string rel) { return AssetDatabase.LoadAssetAtPath<Sprite>(LibPath + rel + ".png"); }

        /// <summary>字体统一走 UiFont：Assets/Art/Fonts/ 里有中文字体就优先用（比内置字体的系统回退更锐利）。</summary>
        static Font LoadFont()
        {
            return UiFont.Load();
        }

        static Text FindText(Transform root, string name)
        {
            Transform t = FindTransform(root, name);
            return t != null ? t.GetComponent<Text>() : null;
        }

        static Image FindImage(Transform root, string name)
        {
            Transform t = FindTransform(root, name);
            return t != null ? t.GetComponent<Image>() : null;
        }

        static Transform FindTransform(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) if (all[i].name == name) return all[i];
            return null;
        }

        static void DestroyChild(Transform parent, string name)
        {
            Transform t = parent.Find(name);
            if (t != null) UnityEngine.Object.DestroyImmediate(t.gameObject);
        }

        static GameObject FindRoot(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) if (roots[i].name == name) return roots[i];
            return null;
        }
    }
}
