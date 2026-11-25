// @trace UI-007
using UnityEngine;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 中转站面板的真实 uGUI 结构工厂（运行时可用，Editor 构建脚本复用同一份布局）。
    ///
    /// v2（2026-11-25 按新 UI 定稿）：原「02 垃圾分类」整块下线，改为四页签，页签索引前移一位。
    ///
    /// 视觉严格对齐 04~08 五张参考图：米白面板 / 深棕字 / 青绿主按钮 / 琥珀次按钮 /
    /// 白色幽灵关闭键 / 紧凑页签 / 分类色点。每个元素都是独立、具名、Inspector 可改的真实控件。
    ///
    /// 结构（默认失活）：
    ///   Panel_StationFiveTabs
    ///     ├ Img_Panel               面板大底
    ///     ├ Txt_Title / 徽章 / 关闭
    ///     ├ TabBar / Btn_Tab_0..3   紧凑页签
    ///     └ Pages / Page_0..3       四个内容页（真实控件）
    /// </summary>
    public static class StationFiveTabsUiFactory
    {
        public const int TabCount = 4;

        // 参考图配色（浅色米白主题）
        static readonly Color Ink = new Color(0.16f, 0.13f, 0.09f, 1f);        // 深棕主字
        static readonly Color InkSoft = new Color(0.42f, 0.36f, 0.28f, 1f);    // 次要字
        static readonly Color InkDim = new Color(0.58f, 0.53f, 0.45f, 1f);     // 说明字
        static readonly Color Amber = new Color(0.78f, 0.46f, 0.10f, 1f);      // 琥珀数字
        static readonly Color Teal = new Color(0.17f, 0.66f, 0.60f, 1f);       // 青绿强调
        static readonly Color OnPrimary = new Color(0.04f, 0.11f, 0.10f, 1f);  // 主按钮上的字

        static readonly string[] TabNames = { "垃圾接收", "材料加工", "产品制造", "人物升级" };
        static readonly string[] CatNames = { "塑料", "金属", "有机", "电子" };
        static readonly string[] ItemNames = { "塑料瓶", "塑料袋", "易拉罐", "铁片", "苹果核", "枯叶", "电池", "电路板" };
        static readonly string[] MaterialNames = { "再生塑料", "金属零件", "有机原料", "电子元件" };
        static readonly string[] ProductNames = { "生态肥料包", "再生建材板", "水质过滤器" };

        // 面板在 1920×1080 内的矩形（对齐参考图：约 x=60..1860, y=39..1029）
        const float PW = 1800f, PH = 990f;

        /// <summary>建出 Panel_StationFiveTabs（默认失活）。art 可为 null（纯色兜底）。</summary>
        public static GameObject Build(Transform canvas, StationArtSet art, Font font)
        {
            if (canvas == null) return null;
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            GameObject root = new GameObject(StationFiveTabsView.PanelName, typeof(RectTransform));
            root.transform.SetParent(canvas, false);
            Stretch((RectTransform)root.transform);

            // 面板大底
            RectTransform panel = Panel(root.transform, art != null ? art.panelBig : null);

            // 标题栏：标题更靠左，徽章/关闭更靠右，贴近参考图
            Img(panel, "Img_TitleMark", 22f, 20f, 24f, 24f, art?.statusDotTeal, Teal, false, Image.Type.Simple, true);
            Lbl(panel, "Txt_Title", "垃圾中转站", 58f, 16f, 360f, 48f, 34, Ink, TextAnchor.MiddleLeft, true, font);
            Img(panel, "Img_Badge", 1580f, 26f, 110f, 36f, art?.statusBadge, Color.white, false, Image.Type.Sliced);
            Img(panel, "Img_BadgeDot", 1594f, 38f, 10f, 10f, art?.statusDotTeal, Teal, false, Image.Type.Simple, true);
            Lbl(panel, "Txt_Badge", "运作中", 1610f, 26f, 70f, 36f, 15, Teal, TextAnchor.MiddleLeft, false, font);
            Button close = Btn(panel, "Btn_StationClose", "", 1710f, 22f, 52f, 52f,
                art?.btnCloseSquare, InkSoft, 24, TextAnchor.MiddleCenter, false, font);
            Img(close.transform, "Img_CloseIcon", 14f, 14f, 24f, 24f, art?.icClose, InkSoft, false, Image.Type.Simple, true);

            // 紧凑页签条：四个页签集中在左上，而非铺满整行
            Img(panel, "Img_TabBar", 30f, 84f, 1740f, 52f, art?.tabBar, new Color(1f, 1f, 1f, 0.35f), false, Image.Type.Sliced);
            for (int i = 0; i < TabCount; i++)
            {
                GameObject tab = new GameObject("Btn_Tab_" + i, typeof(RectTransform), typeof(Image), typeof(Button));
                Node(tab.transform, panel, 44f + i * 150f, 88f, 136f, 44f);
                Image bg = tab.GetComponent<Image>();
                bg.sprite = (i == 0 ? art?.tabSelected : art?.tabNormal) ?? art?.tabNormal;
                bg.type = Image.Type.Sliced;
                bg.color = Color.white;
                bg.raycastTarget = true;
                Button tb = tab.GetComponent<Button>();
                tb.targetGraphic = bg;
                Lbl(tab.transform, "Txt_TabNo", (i + 1).ToString("00"), 12f, 6f, 28f, 32f, 12,
                    i == 0 ? Teal : InkDim, TextAnchor.MiddleLeft, true, font);
                Lbl(tab.transform, "Txt_TabName", TabNames[i], 40f, 6f, 90f, 32f, 16,
                    i == 0 ? Teal : InkSoft, TextAnchor.MiddleLeft, i == 0, font);
            }

            // 四个内容页（原 02 垃圾分类整块已下线）
            RectTransform pages = Node(panel, "Pages", 0f, 0f, PW, PH);
            for (int i = 0; i < TabCount; i++)
            {
                RectTransform page = Node(pages, "Page_" + i, 0f, 0f, PW, PH);
                switch (i)
                {
                    case 0: BuildReceive(page, art, font); break;
                    case 1: BuildProcess(page, art, font); break;
                    case 2: BuildCraft(page, art, font); break;
                    case 3: BuildUpgrade(page, art, font); break;
                }
                page.gameObject.SetActive(i == 0);
            }

            // 层级：盖住旧 Panel_Station，但让数量弹窗 / HUD 提示仍浮在本面板之上
            Transform quantity = FindChild(canvas, "Panel_Quantity");
            Transform station = FindChild(canvas, "Panel_Station");
            if (quantity != null && quantity.parent == canvas)
                root.transform.SetSiblingIndex(quantity.GetSiblingIndex());
            else if (station != null && station.parent == canvas)
                root.transform.SetSiblingIndex(station.GetSiblingIndex() + 1);

            root.SetActive(false);
            return root;
        }

        // =====================================================================
        // 01 垃圾接收
        // =====================================================================
        static void BuildReceive(RectTransform page, StationArtSet art, Font font)
        {
            // 左：玩家背包
            RectTransform bag = Card(page, "Card_Bag", 20f, 160f, 720f, 720f, "玩家背包", art, font);
            Lbl(bag, "Txt_BagSum", "0 / 50", 540f, 14f, 160f, 40f, 28, Amber, TextAnchor.MiddleRight, true, font);
            for (int i = 0; i < 4; i++)
            {
                float y = 86f + i * 122f;
                RectTransform row = Row(bag, "Row_Bag_" + i, 14f, y, 692f, 104f, art);
                Img(row, "Img_Cat", 16f, 30f, 44f, 44f, art?.Cat(i), Color.white, false, Image.Type.Simple, true);
                Lbl(row, "Txt_Cat", CatNames[i], 72f, 20f, 120f, 64f, 22, Ink, TextAnchor.MiddleLeft, true, font);
                Dots(row, "Dots_" + i, 200f, 42f, 10, art);
                Lbl(row, "Txt_Count", "0", 462f, 20f, 70f, 64f, 27, Ink, TextAnchor.MiddleRight, true, font);
                Lbl(row, "Txt_Cap", "/ 0", 536f, 20f, 60f, 64f, 17, InkDim, TextAnchor.MiddleLeft, false, font);
                Btn(row, "Btn_Deposit_" + i, "存入", 610f, 26f, 66f, 52f,
                    art?.btnSmallPrimary, OnPrimary, 18, TextAnchor.MiddleCenter, true, font);
            }
            RectTransform bagFoot = Node(bag, "BagFoot", 14f, 620f, 692f, 88f);
            Img(bagFoot, "Img_FootBg", 0f, 0f, 692f, 88f, art?.bottomBar, new Color(1f, 0.98f, 0.94f, 0.7f), false, Image.Type.Sliced);
            Lbl(bagFoot, "Txt_FootLabel", "剩余可用格数", 18f, 24f, 300f, 40f, 18, InkSoft, TextAnchor.MiddleLeft, false, font);
            Lbl(bagFoot, "Txt_BagFree", "0 / 50", 400f, 14f, 270f, 58f, 26, Amber, TextAnchor.MiddleRight, true, font);

            // 中：一键存入
            RectTransform mid = Node(page, "Mid", 750f, 370f, 300f, 300f);
            Img(mid, "Img_ArrowBg", 110f, 0f, 80f, 80f, art?.labelTeal, Teal, false, Image.Type.Simple, true);
            Img(mid, "Img_Arrow", 134f, 24f, 32f, 32f, art?.icArrow, OnPrimary, false, Image.Type.Simple, true);
            Btn(mid, "Btn_DepositAll", "一键存入全部", 0f, 104f, 300f, 76f,
                art?.btnPrimary, OnPrimary, 26, TextAnchor.MiddleCenter, true, font);
            Lbl(mid, "Txt_Tip1", "转移后背包数量减少\n仓库库存相应增加", 10f, 204f, 280f, 56f, 16, InkSoft, TextAnchor.UpperCenter, false, font);
            Div(mid, "Img_TipDiv", 20f, 264f, 260f, 2f, art);
            Lbl(mid, "Txt_Tip2", "也可在左侧逐类存入\n仓库库存无上限", 10f, 278f, 280f, 56f, 15, InkDim, TextAnchor.UpperCenter, false, font);

            // 右：中转站仓库
            RectTransform storage = Card(page, "Card_Storage", 1060f, 160f, 720f, 720f, "中转站仓库", art, font);
            Lbl(storage, "Txt_RawSum", "0", 540f, 14f, 110f, 40f, 28, Amber, TextAnchor.MiddleRight, true, font);
            Lbl(storage, "Txt_RawSumUnit", "件", 660f, 14f, 40f, 40f, 18, InkSoft, TextAnchor.MiddleLeft, false, font);
            for (int i = 0; i < 4; i++)
            {
                float y = 86f + i * 122f;
                RectTransform row = Row(storage, "Row_Raw_" + i, 14f, y, 692f, 104f, art);
                Img(row, "Img_Cat", 16f, 30f, 44f, 44f, art?.Cat(i), Color.white, false, Image.Type.Simple, true);
                Lbl(row, "Txt_Cat", CatNames[i], 72f, 20f, 120f, 64f, 22, Ink, TextAnchor.MiddleLeft, true, font);
                Lbl(row, "Txt_Count", "0", 450f, 20f, 120f, 64f, 27, Amber, TextAnchor.MiddleRight, true, font);
                Lbl(row, "Txt_Unit", "件", 578f, 20f, 40f, 64f, 17, InkSoft, TextAnchor.MiddleLeft, false, font);
                Lbl(row, "Txt_NoLimit", "无上限", 620f, 20f, 62f, 64f, 14, InkDim, TextAnchor.MiddleRight, false, font);
            }
            RectTransform rawFoot = Node(storage, "RawFoot", 14f, 620f, 692f, 88f);
            Img(rawFoot, "Img_FootBg", 0f, 0f, 692f, 88f, art?.bottomBar, new Color(1f, 0.98f, 0.94f, 0.7f), false, Image.Type.Sliced);
            Lbl(rawFoot, "Txt_FootLabel", "仓库合计", 18f, 24f, 300f, 40f, 18, InkSoft, TextAnchor.MiddleLeft, false, font);
            Lbl(rawFoot, "Txt_RawTotal", "0", 440f, 14f, 230f, 58f, 26, Amber, TextAnchor.MiddleRight, true, font);

            Bottom(page, art, font, "Txt_BottomLeft", "本次可接收 0 件 · 仓库合计 0 件",
                "Btn_ConfirmIn", "确认入库", true, art?.btnSecondary, Ink);
        }

        // =====================================================================
        // 【已下线】02 垃圾分类（2×2 分类卡）
        // 2026-11-25 按新 UI 定稿：该页整块移除，Build 不再调用本方法。
        // 保留方法体仅为需要回滚时能快速恢复；真要恢复，记得同时改 TabCount、TabNames、
        // switch 分支、StationFiveTabsView 的 TabCount 与 RefreshValues 索引、以及自检数量。
        // =====================================================================
        static void BuildSort(RectTransform page, StationArtSet art, Font font)
        {
            for (int c = 0; c < 4; c++)
            {
                float x = 20f + (c % 2) * 830f;
                float y = 160f + (c / 2) * 370f;
                RectTransform card = Card(page, "Card_Cat_" + c, x, y, 810f, 350f, CatNames[c], art, font);
                Lbl(card, "Txt_CatSum", "0", 680f, 14f, 90f, 40f, 28, Amber, TextAnchor.MiddleRight, true, font);
                Lbl(card, "Txt_CatUnit", "件", 776f, 14f, 30f, 40f, 17, InkSoft, TextAnchor.MiddleLeft, false, font);

                for (int k = 0; k < 2; k++)
                {
                    int item = c * 2 + k;
                    RectTransform row = Row(card, "Row_Item_" + item, 16f, 70f + k * 58f, 778f, 54f, art);
                    Img(row, "Img_Item", 18f, 10f, 34f, 34f, art?.Item(item), Color.white, false, Image.Type.Simple, true);
                    Lbl(row, "Txt_ItemName", ItemNames[item], 62f, 8f, 220f, 38f, 18, Ink, TextAnchor.MiddleLeft, false, font);
                    Lbl(row, "Txt_ItemCount", "0", 630f, 8f, 130f, 38f, 20, Ink, TextAnchor.MiddleRight, true, font);
                }

                RectTransform prod = Row(card, "Row_Product", 16f, 194f, 778f, 54f, art);
                Img(prod, "Img_Product", 18f, 10f, 34f, 34f, art?.Material(c), Color.white, false, Image.Type.Simple, true);
                Lbl(prod, "Txt_Product", "加工产物 · " + MaterialNames[c], 62f, 8f, 420f, 38f, 17, InkSoft, TextAnchor.MiddleLeft, false, font);
                Btn(card, "Btn_Sort_" + c, "送往加工", 640f, 260f, 150f, 54f,
                    art?.btnSmallSecondary, Ink, 18, TextAnchor.MiddleCenter, true, font);
            }
            Bottom(page, art, font, "Txt_BottomLeft", "垃圾入站后自动按类统计 · 选中类别可送往加工",
                "Btn_SortAll", "全部加工", true, art?.btnSecondary, Ink);
        }

        // =====================================================================
        // 03 材料加工（表头 + 4 行 + 材料库存条）
        // =====================================================================
        static void BuildProcess(RectTransform page, StationArtSet art, Font font)
        {
            RectTransform head = Row(page, "Head", 20f, 160f, 1760f, 44f, art);
            Lbl(head, "Txt_H0", "原料 / 库存", 16f, 2f, 280f, 40f, 16, InkDim, TextAnchor.MiddleLeft, false, font);
            Lbl(head, "Txt_H1", "投入数量", 310f, 2f, 200f, 40f, 16, InkDim, TextAnchor.MiddleLeft, false, font);
            Lbl(head, "Txt_H2", "产物", 540f, 2f, 280f, 40f, 16, InkDim, TextAnchor.MiddleLeft, false, font);
            Lbl(head, "Txt_H3", "加工进度", 860f, 2f, 500f, 40f, 16, InkDim, TextAnchor.MiddleLeft, false, font);
            Lbl(head, "Txt_H4", "转化比例", 1400f, 2f, 220f, 40f, 16, InkDim, TextAnchor.MiddleLeft, false, font);
            Lbl(head, "Txt_H5", "操作", 1640f, 2f, 120f, 40f, 16, InkDim, TextAnchor.MiddleCenter, false, font);

            for (int i = 0; i < 4; i++)
            {
                float y = 216f + i * 130f;
                RectTransform row = Row(page, "Row_Proc_" + i, 20f, y, 1760f, 122f, art);
                Img(row, "Img_Cat", 16f, 38f, 46f, 46f, art?.Cat(i), Color.white, false, Image.Type.Simple, true);
                Lbl(row, "Txt_Name", CatNames[i] + "垃圾", 72f, 14f, 220f, 44f, 21, Ink, TextAnchor.MiddleLeft, true, font);
                Lbl(row, "Txt_Stock", "库存 0", 72f, 62f, 220f, 36f, 15, InkDim, TextAnchor.MiddleLeft, false, font);

                Btn(row, "Btn_Minus_" + i, "−", 310f, 40f, 42f, 42f, art?.btnSmallGhost, Ink, 20, TextAnchor.MiddleCenter, true, font);
                Lbl(row, "Txt_Input_" + i, "0", 358f, 34f, 56f, 52f, 24, Ink, TextAnchor.MiddleCenter, true, font);
                Btn(row, "Btn_Plus_" + i, "+", 420f, 40f, 42f, 42f, art?.btnSmallGhost, Ink, 20, TextAnchor.MiddleCenter, true, font);

                Img(row, "Img_Product", 540f, 34f, 50f, 50f, art?.Material(i), Color.white, false, Image.Type.Simple, true);
                Lbl(row, "Txt_Product", MaterialNames[i], 600f, 20f, 220f, 44f, 18, Ink, TextAnchor.MiddleLeft, false, font);
                Lbl(row, "Txt_ProductGain", "× 0", 600f, 64f, 220f, 36f, 16, Amber, TextAnchor.MiddleLeft, true, font);

                Img(row, "Img_ProgBg", 860f, 50f, 500f, 18f, art?.barBottom, new Color(0.92f, 0.88f, 0.80f, 1f), false, Image.Type.Sliced);
                Image fill = Img(row, "Img_ProgFill", 860f, 50f, 500f, 18f, art?.fillAmber, Color.white, false, Image.Type.Filled);
                fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillOrigin = 0; fill.fillAmount = 0f;
                Lbl(row, "Txt_Prog", "待机 · 0.0s / 5.0s", 860f, 72f, 500f, 36f, 14, InkDim, TextAnchor.MiddleLeft, false, font);

                Lbl(row, "Txt_Ratio", "转化比例 " + (i == 2 ? "1 : 1" : "2 : 1"), 1400f, 26f, 220f, 38f, 15, InkSoft, TextAnchor.MiddleLeft, false, font);
                Lbl(row, "Txt_Ratio2", "单批 5 件 · 5秒", 1400f, 64f, 220f, 36f, 14, InkDim, TextAnchor.MiddleLeft, false, font);
                Btn(row, "Btn_Process_" + i, "启动加工", 1640f, 38f, 120f, 48f,
                    art?.btnPrimary, OnPrimary, 18, TextAnchor.MiddleCenter, true, font);
            }

            RectTransform stock = Row(page, "MatStock", 20f, 786f, 1760f, 76f, art);
            Lbl(stock, "Txt_Label", "材料库存", 16f, 20f, 140f, 36f, 16, InkSoft, TextAnchor.MiddleLeft, false, font);
            for (int i = 0; i < 4; i++)
            {
                float x = 170f + i * 230f;
                Img(stock, "Img_Mat_" + i, x, 16f, 44f, 44f, art?.Material(i), Color.white, false, Image.Type.Simple, true);
                Lbl(stock, "Txt_Mat_" + i, MaterialNames[i] + " 0", x + 52f, 18f, 170f, 40f, 17, Ink, TextAnchor.MiddleLeft, false, font);
            }
            Bottom(page, art, font, "Txt_BottomLeft", "启动后扣除垃圾 · 完成后产物自动进入仓库库存",
                "Btn_ProcessAll", "全部启动", true, art?.btnPrimary, OnPrimary);
        }

        // =====================================================================
        // 04 产品制造（3 产品卡 + 右侧当前制作/成品库存）
        // =====================================================================
        static void BuildCraft(RectTransform page, StationArtSet art, Font font)
        {
            for (int i = 0; i < 3; i++)
            {
                float x = 20f + i * 475f;
                RectTransform card = Card(page, "Card_Product_" + i, x, 160f, 455f, 690f, "", art, font, false);
                Img(card, "Img_Product", 172f, 18f, 110f, 110f, art?.Product(i), Color.white, false, Image.Type.Simple, true);
                Lbl(card, "Txt_ProductName_" + i, ProductNames[i], 20f, 136f, 415f, 38f, 22, Ink, TextAnchor.MiddleCenter, true, font);
                Pill(card, "Tag_Use_" + i, i == 0 ? "土地修复" : (i == 1 ? "修复中转站" : "水质修复"),
                    142f, 180f, 170f, 28f, art?.labelTeal, Teal, 14, font);
                Div(card, "Img_Div1", 20f, 214f, 415f, 2f, art);
                Lbl(card, "Txt_MatHeader", "所需材料", 20f, 222f, 200f, 30f, 14, InkDim, TextAnchor.MiddleLeft, false, font);

                int matCount = i == 0 ? 1 : (i == 1 ? 2 : 3);
                for (int m = 0; m < matCount; m++)
                {
                    RectTransform mr = Row(card, "Row_Mat_" + i + "_" + m, 20f, 258f + m * 46f, 415f, 44f, art);
                    Img(mr, "Img_Mat", 8f, 7f, 30f, 30f, art?.Material(m), Color.white, false, Image.Type.Simple, true);
                    Lbl(mr, "Txt_Mat", MaterialNames[m], 46f, 4f, 220f, 36f, 15, Ink, TextAnchor.MiddleLeft, false, font);
                    Lbl(mr, "Txt_Need", "0 / 1", 270f, 4f, 130f, 36f, 15, Amber, TextAnchor.MiddleRight, true, font);
                }

                Lbl(card, "Txt_TimeLabel", "制作时间", 20f, 400f, 200f, 30f, 14, InkDim, TextAnchor.MiddleLeft, false, font);
                Lbl(card, "Txt_Time", (i == 0 ? 5 : (i == 1 ? 8 : 12)) + " 秒", 300f, 400f, 135f, 30f, 16, Ink, TextAnchor.MiddleRight, true, font);
                Div(card, "Img_Div2", 20f, 442f, 415f, 2f, art);
                Lbl(card, "Txt_OutLabel", "产出", 20f, 450f, 100f, 30f, 14, InkDim, TextAnchor.MiddleLeft, false, font);
                Lbl(card, "Txt_Out", ProductNames[i] + " ×1 → 成品库存", 110f, 450f, 325f, 30f, 14, InkSoft, TextAnchor.MiddleRight, false, font);
                Lbl(card, "Txt_Desc", i == 0 ? "埋入土壤后恢复肥力，提升土地修复进度"
                    : (i == 1 ? "中转站外壳的加固构件，用于修复中转站"
                    : "滤除水体杂质与污染物，提升水质修复进度"),
                    20f, 494f, 415f, 70f, 13, InkDim, TextAnchor.UpperLeft, false, font);
                Btn(card, "Btn_Craft_" + i, i == 0 ? "材料不足" : "开始制造", 20f, 604f, 415f, 56f,
                    i == 0 ? art?.btnDisabled : art?.btnPrimary,
                    i == 0 ? InkSoft : OnPrimary, 21, TextAnchor.MiddleCenter, true, font);
            }

            RectTransform side = Node(page, "CraftSide", 1440f, 160f, 440f, 690f);
            RectTransform cur = Card(side, "Card_Current", 0f, 0f, 440f, 300f, "当前制作", art, font);
            Lbl(cur, "Txt_CurState", "空闲", 330f, 14f, 90f, 36f, 15, Teal, TextAnchor.MiddleRight, false, font);
            Img(cur, "Img_RingBg", 20f, 70f, 90f, 90f, art?.ringBottom, Color.white, false, Image.Type.Simple, true);
            Lbl(cur, "Txt_RingPct", "0%", 20f, 70f, 90f, 90f, 19, Teal, TextAnchor.MiddleCenter, true, font);
            Lbl(cur, "Txt_CurName", "—", 124f, 72f, 290f, 36f, 19, Ink, TextAnchor.MiddleLeft, true, font);
            Lbl(cur, "Txt_CurTime", "剩余时间 0.0s", 124f, 112f, 290f, 34f, 15, InkSoft, TextAnchor.MiddleLeft, false, font);
            Lbl(cur, "Txt_CurHint", "同一时间仅可制作一种产品\n完成后自动进入成品库存", 124f, 152f, 290f, 90f, 13, InkDim, TextAnchor.UpperLeft, false, font);

            RectTransform inv = Card(side, "Card_Finished", 0f, 320f, 440f, 370f, "成品库存", art, font);
            for (int i = 0; i < 3; i++)
            {
                RectTransform row = Row(inv, "Row_Finished_" + i, 14f, 70f + i * 92f, 412f, 82f, art);
                Img(row, "Img_Product", 12f, 20f, 42f, 42f, art?.Product(i), Color.white, false, Image.Type.Simple, true);
                Lbl(row, "Txt_Product", ProductNames[i], 64f, 20f, 200f, 42f, 18, Ink, TextAnchor.MiddleLeft, false, font);
                Lbl(row, "Txt_Count", "× 0", 280f, 20f, 106f, 42f, 22, Amber, TextAnchor.MiddleRight, true, font);
            }
            Bottom(page, art, font, "Txt_BottomLeft", "材料从中转站库存扣除 · 同一时间仅可制作一种产品",
                "Btn_GoRepair", "前往修复", true, art?.btnSecondary, Ink);
        }

        // =====================================================================
        // 05 人物升级（C27：独立信息卡；当前/下一级/满级加成预览 + 经验条 + 材料消耗）
        // =====================================================================
        static void BuildUpgrade(RectTransform page, StationArtSet art, Font font)
        {
            // 主卡片：居中大卡，承载人物成长的全部信息（对齐人物升级浮层布局）
            RectTransform main = Card(page, "Card_LevelUp", 60f, 150f, 1680f, 760f, "", art, font, false);

            // 顶部青绿标签：中转站 · 人物成长
            Pill(main, "Pill_Section", "中转站 · 人物成长", 24f, 18f, 300f, 42f,
                art?.labelTeal, Teal, 16, font);

            // 标题 + 等级徽章
            Lbl(main, "Txt_LevelTitle", "人物升级", 24f, 74f, 360f, 56f, 34, Ink, TextAnchor.MiddleLeft, true, font);
            Img(main, "Img_LevelBadge", 400f, 82f, 130f, 44f, art?.labelTeal, Color.white, false, Image.Type.Sliced);
            Lbl(main, "Txt_Level", "Lv.1", 400f, 82f, 130f, 44f, 20, Teal, TextAnchor.MiddleCenter, true, font);

            // 经验区：累计经验 / 进度条 / 来源
            Lbl(main, "Txt_ExpLabel", "累计经验", 24f, 148f, 200f, 32f, 16, InkSoft, TextAnchor.MiddleLeft, false, font);
            Lbl(main, "Txt_ExpValue", "0 / 0", 1260f, 146f, 396f, 36f, 20, Amber, TextAnchor.MiddleRight, true, font);
            Img(main, "Img_ExpBg", 24f, 190f, 1632f, 22f, art?.barBottom, Color.white, false, Image.Type.Sliced);
            Image expFill = Img(main, "Img_ExpFill", 24f, 190f, 1632f, 22f, art?.fillTeal, Color.white, false, Image.Type.Filled);
            expFill.type = Image.Type.Filled;
            expFill.fillMethod = Image.FillMethod.Horizontal;
            expFill.fillOrigin = 0;
            expFill.fillAmount = 0f;
            Lbl(main, "Txt_ExpSource", "经验来源：拾取 / 拆解 / 加工 / 制造 / 修复", 24f, 220f, 1000f, 30f, 14, InkDim, TextAnchor.MiddleLeft, false, font);

            // 加成预览标题
            Div(main, "Img_Div1", 24f, 258f, 1632f, 2f, art);
            Lbl(main, "Txt_BonusHeader", "等级加成预览", 24f, 270f, 300f, 34f, 17, Ink, TextAnchor.MiddleLeft, true, font);

            // 三张加成卡：当前 / 下一级 / 满级
            string[] titles = { "当前等级", "下一等级", "满级目标" };
            string[] specLabels = { "拾取距离", "拆解效率", "加工耗时", "零耗时次数" };
            for (int i = 0; i < 3; i++)
            {
                float cx = 24f + i * 552f;
                RectTransform card = Card(main, "Card_Tool_" + i, cx, 312f, 528f, 260f, "", art, font, false);
                Pill(card, "Img_Idx_" + i, (i + 1).ToString(), 16f, 14f, 36f, 36f, art?.countBadge, Ink, 16, font);
                Lbl(card, "Txt_ToolName_" + i, titles[i], 62f, 14f, 300f, 40f, 19,
                    i == 1 ? Teal : Ink, TextAnchor.MiddleLeft, true, font);
                Lbl(card, "Txt_Sub", i == 0 ? "当前生效" : (i == 1 ? "升级后生效" : "等级上限"),
                    62f, 52f, 420f, 26f, 13, InkDim, TextAnchor.MiddleLeft, false, font);
                for (int r = 0; r < 4; r++)
                {
                    RectTransform rr = Row(card, "Row_Spec_" + i + "_" + r, 16f, 88f + r * 38f, 496f, 36f, art);
                    Lbl(rr, "Txt_Label", specLabels[r], 14f, 4f, 220f, 28f, 15, InkSoft, TextAnchor.MiddleLeft, false, font);
                    Lbl(rr, "Txt_Value", "—", 244f, 4f, 238f, 28f, 16,
                        i == 1 ? Teal : Ink, TextAnchor.MiddleRight, true, font);
                }
            }

            // 材料消耗
            Div(main, "Img_Div2", 24f, 580f, 1632f, 2f, art);
            Lbl(main, "Txt_CostHeader", "升级消耗", 24f, 592f, 300f, 34f, 17, Ink, TextAnchor.MiddleLeft, true, font);
            RectTransform matCard = Card(main, "Card_CostMat", 24f, 634f, 800f, 72f, "", art, font, false);
            Img(matCard, "Img_CostMat", 16f, 16f, 40f, 40f, art?.Material(1), Color.white, false, Image.Type.Simple, true);
            Lbl(matCard, "Txt_CostMatLabel", "金属零件", 66f, 16f, 170f, 40f, 15, InkSoft, TextAnchor.MiddleLeft, false, font);
            Lbl(matCard, "Txt_CostMat", "需 0 / 持有 0", 250f, 16f, 534f, 40f, 17, Amber, TextAnchor.MiddleRight, true, font);
            RectTransform elecCard = Card(main, "Card_CostElec", 856f, 634f, 800f, 72f, "", art, font, false);
            Img(elecCard, "Img_CostElec", 16f, 16f, 40f, 40f, art?.Material(3), Color.white, false, Image.Type.Simple, true);
            Lbl(elecCard, "Txt_CostElecLabel", "电子元件", 66f, 16f, 170f, 40f, 15, InkSoft, TextAnchor.MiddleLeft, false, font);
            Lbl(elecCard, "Txt_CostElec", "需 0 / 持有 0", 250f, 16f, 534f, 40f, 17, Amber, TextAnchor.MiddleRight, true, font);

            // 升级状态 / 提示
            Lbl(main, "Txt_UpState", "可升级", 24f, 714f, 240f, 30f, 16, Teal, TextAnchor.MiddleLeft, true, font);
            Lbl(main, "Txt_UpHint", "", 280f, 714f, 1376f, 30f, 14, InkDim, TextAnchor.MiddleLeft, false, font);

            Bottom(page, art, font, "Txt_BottomLeft", "经验达标并消耗加工材料后即可升级人物",
                "Btn_Upgrade", "升级", true, art?.btnPrimary, OnPrimary, "取消");
        }

        // =====================================================================
        // 通用辅助
        // =====================================================================
        static RectTransform Panel(Transform parent, Sprite bg)
        {
            GameObject go = new GameObject("Img_Panel", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(PW, PH);
            rt.anchoredPosition = new Vector2(0f, -6f);
            Image img = go.GetComponent<Image>();
            img.sprite = bg; img.type = Image.Type.Sliced; img.color = Color.white; img.raycastTarget = true;
            return rt;
        }

        static RectTransform Card(Transform parent, string name, float x, float y, float w, float h,
            string title, StationArtSet art, Font font, bool titleVisible = true)
        {
            RectTransform card = Img(parent, name, x, y, w, h, art?.card, Color.white, false, Image.Type.Sliced).rectTransform;
            if (titleVisible && !string.IsNullOrEmpty(title))
            {
                Lbl(card, "Txt_Title", title, 20f, 12f, w - 40f, 50f, 25, Ink, TextAnchor.MiddleLeft, true, font);
                Div(card, "Img_TitleDiv", 16f, 62f, w - 32f, 2f, art);
            }
            return card;
        }

        static RectTransform Row(Transform parent, string name, float x, float y, float w, float h, StationArtSet art)
        {
            return Img(parent, name, x, y, w, h, art?.listRow, Color.white, false, Image.Type.Sliced).rectTransform;
        }

        static RectTransform Pill(Transform parent, string name, string value, float x, float y, float w, float h,
            Sprite sprite, Color textColor, int size, Font font)
        {
            Image img = Img(parent, name, x, y, w, h, sprite, Color.white, false, Image.Type.Sliced);
            Lbl(img.transform, "Label", value, 0f, 0f, w, h, size, textColor, TextAnchor.MiddleCenter, false, font);
            return img.rectTransform;
        }

        static void Dots(Transform parent, string name, float x, float y, int count, StationArtSet art)
        {
            for (int i = 0; i < count; i++)
                Img(parent, name + "_" + i, x + i * 20f, y, 12f, 12f,
                    art?.dotEmpty, new Color(0.85f, 0.62f, 0.28f, 1f), false, Image.Type.Simple, true);
        }

        static void Div(Transform parent, string name, float x, float y, float w, float h, StationArtSet art)
        {
            Img(parent, name, x, y, w, h, art?.divDashed, new Color(0.78f, 0.72f, 0.62f, 0.7f), false, Image.Type.Sliced);
        }

        static void Bottom(Transform page, StationArtSet art, Font font, string leftName, string leftText,
            string rightBtnName, string rightText, bool showRight, Sprite primarySprite, Color primaryText,
            string closeText = "关闭")
        {
            RectTransform bar = Img(page, "BottomBar", 30f, 910f, 1740f, 62f, art?.bottomBar, Color.white, false, Image.Type.Sliced).rectTransform;
            Lbl(bar, leftName, leftText, 24f, 12f, 1000f, 38f, 16, InkSoft, TextAnchor.MiddleLeft, false, font);
            Btn(bar, "Btn_ClosePage", closeText, showRight ? 1440f : 1590f, 10f, 140f, 44f,
                art?.btnGhost, Ink, 19, TextAnchor.MiddleCenter, false, font);
            if (!showRight || rightBtnName == null) return;
            Btn(bar, rightBtnName, rightText, 1600f, 10f, 170f, 44f,
                primarySprite ?? art?.btnPrimary, primaryText, 19, TextAnchor.MiddleCenter, true, font);
        }

        static RectTransform Node(Transform child, Transform parent, float x, float y, float w, float h)
        {
            child.SetParent(parent, false);
            RectTransform rt = (RectTransform)child;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, -y);
            return rt;
        }

        static RectTransform Node(Transform parent, string name, float x, float y, float w, float h)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            return Node(go.transform, parent, x, y, w, h);
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        static Image Img(Transform parent, string name, float x, float y, float w, float h, Sprite sprite,
            Color tint, bool raycast, Image.Type type, bool preserve = false)
        {
            RectTransform rt = Node(parent, name, x, y, w, h);
            Image img = rt.gameObject.AddComponent<Image>();
            if (sprite != null)
            {
                img.sprite = sprite; img.type = type; img.color = tint;
            }
            else
            {
                img.color = type == Image.Type.Filled ? tint
                    : new Color(0.99f, 0.97f, 0.93f, Mathf.Max(tint.a, 0.5f));
            }
            img.preserveAspect = preserve;
            img.raycastTarget = raycast;
            return img;
        }

        static Text Lbl(Transform parent, string name, string value, float x, float y, float w, float h,
            int size, Color color, TextAnchor align, bool bold, Font font)
        {
            RectTransform rt = Node(parent, name, x, y, w, h);
            Text t = rt.gameObject.AddComponent<Text>();
            t.font = font; t.fontSize = size; t.color = color; t.alignment = align;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false; t.text = value;
            return t;
        }

        static Button Btn(Transform parent, string name, string caption, float x, float y, float w, float h,
            Sprite sprite, Color labelColor, int size, TextAnchor align, bool bold, Font font)
        {
            RectTransform rt = Node(parent, name, x, y, w, h);
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.color = sprite != null ? Color.white : new Color(0.17f, 0.66f, 0.60f, 0.9f);
            img.raycastTarget = true;
            Button b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            ColorBlock cb = b.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            cb.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            cb.disabledColor = new Color(0.7f, 0.7f, 0.7f, 0.9f);
            cb.fadeDuration = 0.08f;
            b.colors = cb;
            if (!string.IsNullOrEmpty(caption))
            {
                RectTransform lr = Node(rt, "Label", 0f, 0f, w, h);
                Text t = lr.gameObject.AddComponent<Text>();
                t.font = font; t.fontSize = size; t.color = labelColor; t.alignment = align;
                t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                t.raycastTarget = false; t.text = caption;
            }
            return b;
        }

        static Transform FindChild(Transform root, string name)
        {
            if (root == null) return null;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) if (all[i].name == name) return all[i];
            return null;
        }
    }
}
