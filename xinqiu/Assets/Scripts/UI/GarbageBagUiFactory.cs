// @trace BAG-003, UI-006
using UnityEngine;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 垃圾背包面板的 uGUI 结构工厂（运行时可用，Editor 构建脚本复用同一份布局）。
    ///
    /// 为什么放在运行时：旧场景还没跑菜单⑳ 时，GarbageBagSystem 可以在 Play 里补建面板，
    /// 保证功能不会因为"代码有了、场景没生成"而缺席；正式交付仍由 GarbageBagUiBuilder
    /// 用同一份工厂把层级烘焙进场景，方便美术在 Inspector 里改图改色。
    ///
    /// 只负责建节点，不写玩法数据、不做事件绑定（按钮由 GarbageBagPanelView.Bind 绑定）。
    /// </summary>
    public static class GarbageBagUiFactory
    {
        static readonly Color PageBg = new Color(0.965f, 0.951f, 0.902f, 0.99f);
        static readonly Color CardBg = new Color(1.000f, 0.992f, 0.961f, 1f);
        static readonly Color ItemBg = new Color(0.936f, 0.972f, 0.952f, 1f);
        static readonly Color Ink = new Color(0.145f, 0.180f, 0.165f, 1f);
        static readonly Color InkSoft = new Color(0.410f, 0.440f, 0.420f, 1f);
        static readonly Color Green = new Color(0.180f, 0.620f, 0.450f, 1f);
        static readonly Color GreenSoft = new Color(0.856f, 0.939f, 0.881f, 1f);
        static readonly Color Line = new Color(0.862f, 0.843f, 0.788f, 1f);
        static readonly Color BarBg = new Color(0.882f, 0.871f, 0.820f, 1f);
        static readonly Color BarFill = new Color(0.255f, 0.715f, 0.515f, 1f);
        static readonly Color Danger = new Color(0.690f, 0.255f, 0.220f, 1f);
        static readonly Vector2 TopLeft = new Vector2(0f, 1f);

        /// <summary>
        /// 建出 Panel_GarbageBag（默认失活）。图标数组可为 null（运行时缺图时只显示文字与数量）。
        /// </summary>
        public static GameObject Build(Transform canvas, Font font,
            Sprite[] categoryIcons, Sprite[] itemIcons, Sprite[] materialIcons)
        {
            if (canvas == null) return null;
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            GameObject root = Img(canvas, GarbageBagPanelView.PanelName,
                0f, 0f, 1360f, 820f, null, PageBg, true).gameObject;
            Transform p = root.transform;

            Img(p, "Img_GBTitleDot", 36f, 30f, 22f, 22f, null, Green, false);
            Lbl(p, "Txt_GBTitle", "垃圾背包 · 废弃物", 70f, 22f, 420f, 42f, 27, Ink,
                TextAnchor.MiddleLeft, true, font);
            Lbl(p, "Txt_GBSubTitle", "只存捡到的垃圾；工具/技能书按 I · 金币交易按 B · 投放加工按 M",
                70f, 62f, 800f, 26f, 13, InkSoft, TextAnchor.MiddleLeft, false, font);
            Lbl(p, "Txt_GBCapacity", "容量 0 / 50", 1010f, 30f, 310f, 36f, 18, Green,
                TextAnchor.MiddleRight, true, font);
            Div(p, "Img_GBHeaderLine", 28f, 98f, 1304f, 2f);

            float[] xs = { 32f, 698f, 32f, 698f };
            float[] ys = { 116f, 116f, 432f, 432f };
            for (int cat = 0; cat < 4; cat++)
                BuildCategoryCard(p, cat, xs[cat], ys[cat], font, categoryIcons, itemIcons, materialIcons);

            Div(p, "Img_GBFooterLine", 28f, 750f, 1304f, 2f);
            Lbl(p, "Txt_GBTotal", "总容量 0 / 50", 32f, 764f, 280f, 36f, 18, Ink,
                TextAnchor.MiddleLeft, true, font);
            Lbl(p, "Txt_GBHint",
                "点单品卡下方【丢弃 1】丢弃一件；【整理】校准数量。丢弃只影响当前持有，不影响累计清理数。",
                320f, 766f, 740f, 34f, 13, InkSoft, TextAnchor.MiddleLeft, false, font);
            Btn(p, "Btn_GBSort", "整理", 1070f, 762f, 110f, 40f,
                GreenSoft, Green, 15, TextAnchor.MiddleCenter, true, font);
            Btn(p, "Btn_GBClose", "关闭 (G)", 1192f, 762f, 136f, 40f,
                Green, Color.white, 15, TextAnchor.MiddleCenter, true, font);

            root.SetActive(false);
            return root;
        }

        static void BuildCategoryCard(Transform panel, int cat, float x, float y, Font font,
            Sprite[] categoryIcons, Sprite[] itemIcons, Sprite[] materialIcons)
        {
            Transform card = Img(panel, "Card_GB_" + cat, x, y, 630f, 300f, null, CardBg, false).transform;
            Img(card, "Img_GBAccent_" + cat, 16f, 16f, 4f, 24f, null, Green, false);
            Img(card, "Img_GBCatIcon_" + cat, 28f, 13f, 32f, 32f, At(categoryIcons, cat), Green, false, true);
            Lbl(card, "Txt_GBCatName_" + cat, StationSystem.CategoryNames[cat],
                70f, 10f, 260f, 38f, 21, Ink, TextAnchor.MiddleLeft, true, font);
            Img(card, "Img_GBBadgeBg_" + cat, 462f, 16f, 146f, 30f, null, GreenSoft, false);
            Lbl(card, "Txt_GBBadge_" + cat, "可回收垃圾", 462f, 16f, 146f, 30f,
                13, Green, TextAnchor.MiddleCenter, false, font);
            Div(card, "Img_GBCardLine_" + cat, 16f, 56f, 598f, 1f);

            for (int j = 0; j < GarbageBagCatalog.ItemsPerCategory; j++)
            {
                int item = GarbageBagCatalog.FirstItemOf(cat) + j;
                BuildItemCard(card, item, 18f + j * 158f, 72f, font, At(itemIcons, item));
            }
            BuildStatColumn(card, cat, 342f, 72f, font, At(materialIcons, cat));
        }

        static void BuildItemCard(Transform card, int item, float x, float y, Font font, Sprite icon)
        {
            Transform box = Img(card, "Card_GBItem_" + item, x, y, 150f, 196f, null, ItemBg, false).transform;
            Img(box, "Img_GBItemIcon_" + item, 47f, 14f, 56f, 56f, icon, Color.white, false, true);
            Lbl(box, "Txt_GBItemName_" + item, GarbageBagCatalog.NameOf(item),
                8f, 82f, 134f, 26f, 14, Ink, TextAnchor.MiddleCenter, false, font);
            Lbl(box, "Txt_GBItemCount_" + item, "0",
                8f, 108f, 134f, 44f, 30, Green, TextAnchor.MiddleCenter, true, font);
            Btn(box, "Btn_GBDiscard_" + item, "丢弃 1", 18f, 160f, 114f, 26f,
                Color.white, Danger, 12, TextAnchor.MiddleCenter, false, font);
        }

        static void BuildStatColumn(Transform card, int cat, float x, float y, Font font, Sprite materialIcon)
        {
            Lbl(card, "Txt_GBCatCap_" + cat, "容量 0 / 0", x, y, 272f, 28f,
                16, Ink, TextAnchor.MiddleLeft, true, font);
            Img(card, "Img_GBBarBg_" + cat, x, y + 34f, 272f, 10f, null, BarBg, false);
            Image fill = Img(card, "Img_GBBarFill_" + cat, x, y + 34f, 272f, 10f, null, BarFill, false);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.fillAmount = 0f;
            Lbl(card, "Txt_GBCatRemain_" + cat, "剩余 0 格", x, y + 50f, 272f, 24f,
                13, InkSoft, TextAnchor.MiddleLeft, false, font);
            Div(card, "Img_GBStatLine_" + cat, x, y + 84f, 272f, 1f);
            Lbl(card, "Txt_GBMatCaption_" + cat, "加工产物 →", x, y + 94f, 272f, 22f,
                12, InkSoft, TextAnchor.MiddleLeft, false, font);
            Img(card, "Img_GBMatIcon_" + cat, x, y + 124f, 30f, 30f, materialIcon, Color.white, false, true);
            Lbl(card, "Txt_GBMatName_" + cat, StationSystem.MaterialNames[cat],
                x + 38f, y + 124f, 234f, 30f, 15, Ink, TextAnchor.MiddleLeft, true, font);
        }

        static Sprite At(Sprite[] a, int i) => a != null && i >= 0 && i < a.Length ? a[i] : null;

        static RectTransform Node(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = TopLeft;
            rt.anchorMax = TopLeft;
            rt.pivot = TopLeft;
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, -y);
            return rt;
        }

        static Image Img(Transform parent, string name, float x, float y, float w, float h,
            Sprite sprite, Color tint, bool raycast, bool preserve = false)
        {
            RectTransform rt = Node(parent, name, x, y, w, h);
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = tint;
            img.preserveAspect = preserve;
            img.raycastTarget = raycast;
            return img;
        }

        static void Div(Transform parent, string name, float x, float y, float w, float h)
        {
            Img(parent, name, x, y, w, h, null, Line, false);
        }

        static Text Lbl(Transform parent, string name, string value, float x, float y, float w, float h,
            int size, Color color, TextAnchor align, bool bold, Font font)
        {
            RectTransform rt = Node(parent, name, x, y, w, h);
            Text t = rt.gameObject.AddComponent<Text>();
            t.font = font;
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

        static Button Btn(Transform parent, string name, string caption, float x, float y, float w, float h,
            Color bg, Color labelColor, int size, TextAnchor align, bool bold, Font font)
        {
            RectTransform rt = Node(parent, name, x, y, w, h);
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = bg;
            img.raycastTarget = true;
            Button b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            ColorBlock cb = b.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            cb.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            cb.fadeDuration = 0.06f;
            b.colors = cb;

            RectTransform label = Node(rt, "Label", 0f, 0f, w, h);
            Text t = label.gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.color = labelColor;
            t.alignment = align;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.raycastTarget = false;
            t.text = caption;
            return b;
        }
    }
}
