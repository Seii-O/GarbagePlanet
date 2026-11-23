// @trace UI-009
using UnityEngine;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 结算面板真实 uGUI 结构工厂：按「12_结算.png」用拆件拼装。
    /// 顶部标题 / 评级圆环 / 左右六行统计 / 三小标签 / 五阶段条 / 底部双按钮。
    /// 所有控件具名、Inspector 可改；Editor Builder 与运行时兜底共用本布局。
    /// </summary>
    public static class SettlementUiFactory
    {
        public const int StageCount = 5;
        const float PW = 1440f, PH = 920f;

        static readonly Color Ink = new Color(0.16f, 0.13f, 0.09f, 1f);
        static readonly Color InkSoft = new Color(0.42f, 0.36f, 0.28f, 1f);
        static readonly Color InkDim = new Color(0.58f, 0.53f, 0.45f, 1f);
        static readonly Color Amber = new Color(0.78f, 0.46f, 0.10f, 1f);
        static readonly Color Teal = new Color(0.12f, 0.55f, 0.48f, 1f);
        static readonly Color RingFill = new Color(0.55f, 0.82f, 0.78f, 1f);

        public static GameObject Build(Transform canvas, SettlementArtSet art, Font font)
        {
            if (canvas == null) return null;
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            GameObject root = new GameObject(SettlementPanelView.PanelName, typeof(RectTransform));
            root.transform.SetParent(canvas, false);
            Stretch((RectTransform)root.transform);

            RectTransform panel = Panel(root.transform, art != null ? art.panelBig : null);

            BuildHeader(panel, art, font);
            BuildGrade(panel, art, font);
            BuildStats(panel, art, font);
            BuildStages(panel, art, font);
            BuildBottom(panel, art, font);

            // 盖在旧结算/暂停面板之上，退出确认框仍压在最上层
            Transform quitDialog = FindChild(canvas, "Panel_QuitConfirm");
            if (quitDialog != null && quitDialog.parent == canvas)
                root.transform.SetSiblingIndex(quitDialog.GetSiblingIndex());
            else
                root.transform.SetAsLastSibling();

            root.SetActive(false);
            return root;
        }

        static void BuildHeader(RectTransform panel, SettlementArtSet art, Font font)
        {
            Img(panel, "Img_TitleMark", 30f, 26f, 26f, 26f, art?.statusDotTeal, Teal, false, Image.Type.Simple, true);
            Lbl(panel, "Txt_Title", "最终结算", 68f, 20f, 300f, 44f, 30, Ink, TextAnchor.MiddleLeft, true, font);
            Div(panel, "Img_HeaderDiv", 20f, 74f, 1400f, 2f, art);

            Lbl(panel, "Txt_MainTitle", "星球生态恢复完成", 0f, 96f, 1440f, 50f, 36, Ink, TextAnchor.MiddleCenter, true, font);
            Lbl(panel, "Txt_SubTitle", "土地与水源全部修复 · 维修重建星球表面", 0f, 148f, 1440f, 30f, 15, InkDim, TextAnchor.MiddleCenter, false, font);
            Div(panel, "Img_TitleDiv", 60f, 190f, 1320f, 2f, art);
        }

        static void BuildGrade(RectTransform panel, SettlementArtSet art, Font font)
        {
            RectTransform g = Node(panel, "GradeBlock", 40f, 224f, 280f, 420f);
            // 双层圆环：底环 + 青色填充环，中间米色圆底放评级字母
            Img(g, "Img_RingBottom", 40f, 10f, 200f, 200f, art?.ringBottom, Color.white, false, Image.Type.Simple, true);
            Img(g, "Img_RingFill", 40f, 10f, 200f, 200f, art?.ringTeal, RingFill, false, Image.Type.Simple, true);
            Img(g, "Img_Badge", 62f, 32f, 156f, 156f, art?.badgeCircle, Color.white, false, Image.Type.Simple, true);
            Lbl(g, "Txt_Grade", "S", 62f, 44f, 156f, 120f, 96, Teal, TextAnchor.MiddleCenter, true, font);
            Lbl(g, "Txt_GradeCap", "评级", 62f, 166f, 156f, 28f, 14, InkDim, TextAnchor.MiddleCenter, false, font);
            Lbl(g, "Txt_GradeNote", "生态完全恢复", 40f, 226f, 200f, 30f, 15, Ink, TextAnchor.MiddleCenter, true, font);

            // 三条配色条：与项目四色垃圾一致，取青/青/琥珀示意
            Img(g, "Bar_0", 66f, 266f, 40f, 8f, art?.barPlastic, Teal, false, Image.Type.Sliced);
            Img(g, "Bar_1", 112f, 266f, 40f, 8f, art?.barMetal, Teal, false, Image.Type.Sliced);
            Img(g, "Bar_2", 158f, 266f, 40f, 8f, art?.barOrganic, Amber, false, Image.Type.Sliced);

            // 三个小标签
            Tag(g, "Tag_0", "等待清空", 8f, 300f, art, font);
            Tag(g, "Tag_1", "全部回收", 100f, 300f, art, font);
            Tag(g, "Tag_2", "土地 + 水源恢复", 188f, 300f, art, font);
        }

        static void BuildStats(RectTransform panel, SettlementArtSet art, Font font)
        {
            RectTransform mid = Node(panel, "StatsMid", 372f, 236f, 500f, 330f);
            StatRow(mid, "Stat_Collected", 0f, 20f, "垃圾清理总数", "件", art, font);
            StatRow(mid, "Stat_Processed", 0f, 108f, "材料加工总数", "件", art, font);
            StatRow(mid, "Stat_Crafted", 0f, 196f, "产品制造总数", "件", art, font);
            Img(mid, "Img_MidDiv", 508f, 0f, 2f, 300f, art?.divSolid, new Color(0.78f, 0.72f, 0.62f, 0.35f), false, Image.Type.Sliced);

            RectTransform right = Node(panel, "StatsRight", 920f, 236f, 480f, 330f);
            StatRow(right, "Stat_Land", 0f, 20f, "土地修复进度", "%", art, font);
            StatRow(right, "Stat_Water", 0f, 108f, "水源修复进度", "%", art, font);
            StatRow(right, "Stat_Time", 0f, 196f, "总游戏时间", "", art, font);
        }

        static void StatRow(Transform parent, string name, float x, float y, string label, string unit,
            SettlementArtSet art, Font font)
        {
            RectTransform row = Node(parent, name, x, y, 480f, 70f);
            Lbl(row, "Txt_Label", label, 0f, 4f, 210f, 30f, 16, InkSoft, TextAnchor.MiddleLeft, false, font);
            Img(row, "Img_Line", 208f, 20f, 160f, 2f, art?.divDotted, new Color(0.78f, 0.72f, 0.62f, 0.55f), false, Image.Type.Sliced);
            Lbl(row, "Txt_Value", "0", 356f, 0f, 108f, 44f, 30, Amber, TextAnchor.MiddleRight, true, font);
            Lbl(row, "Txt_Unit", unit, 466f, 6f, 14f, 30f, 13, Amber, TextAnchor.MiddleLeft, false, font);
        }

        static void BuildStages(RectTransform panel, SettlementArtSet art, Font font)
        {
            Div(panel, "Img_StageDiv", 60f, 600f, 1320f, 2f, art);
            RectTransform bar = Img(panel, "StageBar", 20f, 636f, 1400f, 170f,
                art?.card, Color.white, false, Image.Type.Sliced).rectTransform;

            string[] names = { "探索", "回收", "加工", "制造", "恢复" };
            for (int i = 0; i < StageCount; i++)
            {
                float cx = 20f + i * 280f;
                RectTransform node = Node(bar, "Stage_" + i, cx, 26f, 240f, 118f);
                Img(node, "Img_Idx", 99f, 0f, 42f, 42f, art?.stepDone, Color.white, false, Image.Type.Simple, true);
                Lbl(node, "Txt_Idx", "0" + (i + 1), 99f, 0f, 42f, 42f, 15, Teal, TextAnchor.MiddleCenter, true, font);
                Lbl(node, "Txt_Name", names[i], 0f, 48f, 240f, 26f, 15, Ink, TextAnchor.MiddleCenter, true, font);
                Lbl(node, "Txt_Detail", "—", 0f, 76f, 240f, 26f, 12, InkDim, TextAnchor.MiddleCenter, false, font);
                if (i < StageCount - 1)
                    Img(node, "Img_Link", 214f, 19f, 66f, 2f, art?.divSolid,
                        new Color(0.78f, 0.72f, 0.62f, 0.45f), false, Image.Type.Sliced);
            }
        }

        static void BuildBottom(RectTransform panel, SettlementArtSet art, Font font)
        {
            RectTransform bar = Img(panel, "BottomBar", 20f, 852f, 1400f, 56f,
                art?.bottomBar, Color.white, false, Image.Type.Sliced).rectTransform;
            Lbl(bar, "Txt_StatNote", "统计口径：本次存档累计值", 24f, 12f, 700f, 32f, 14, InkDim, TextAnchor.MiddleLeft, false, font);
            Btn(bar, "Btn_ContinuePlaying", "继续游玩", 1116f, 8f, 140f, 40f,
                art?.btnGhost, Ink, 18, TextAnchor.MiddleCenter, true, font);
            Btn(bar, "Btn_BackToTitle", "返回主菜单", 1266f, 8f, 120f, 40f,
                art?.btnPrimary, new Color(0.04f, 0.11f, 0.10f, 1f), 18, TextAnchor.MiddleCenter, true, font);
        }

        static void Tag(Transform parent, string name, string caption, float x, float y,
            SettlementArtSet art, Font font)
        {
            RectTransform t = Img(parent, name, x, y, 88f, 26f,
                art?.labelAmber, Color.white, false, Image.Type.Sliced).rectTransform;
            Lbl(t, "Label", caption, 0f, 0f, 88f, 26f, 11, Amber, TextAnchor.MiddleCenter, false, font);
        }

        // ---------------- 通用辅助（与 QuestLogUiFactory 同款，避免类型混用） ----------------
        static RectTransform Panel(Transform parent, Sprite bg)
        {
            GameObject go = new GameObject("Img_Panel", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(PW, PH);
            rt.anchoredPosition = new Vector2(0f, -10f);
            Image img = go.GetComponent<Image>();
            img.sprite = bg; img.type = Image.Type.Sliced; img.color = Color.white; img.raycastTarget = true;
            return rt;
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
            if (sprite != null) { img.sprite = sprite; img.type = type; img.color = tint; }
            else img.color = type == Image.Type.Filled ? tint
                : new Color(0.99f, 0.97f, 0.93f, Mathf.Max(tint.a, 0.5f));
            img.preserveAspect = preserve; img.raycastTarget = raycast;
            return img;
        }

        static void Div(Transform parent, string name, float x, float y, float w, float h, SettlementArtSet art)
        {
            Img(parent, name, x, y, w, h, art?.divDotted,
                new Color(0.78f, 0.72f, 0.62f, 0.35f), false, Image.Type.Sliced);
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
            img.sprite = sprite; img.type = Image.Type.Sliced;
            img.color = sprite != null ? Color.white : new Color(0.12f, 0.55f, 0.48f, 0.9f);
            img.raycastTarget = true;
            Button b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            ColorBlock cb = b.colors;
            cb.normalColor = Color.white; cb.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            cb.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f); cb.fadeDuration = 0.08f;
            b.colors = cb;
            if (!string.IsNullOrEmpty(caption))
            {
                RectTransform lr = Node(rt, "Label", 0f, 0f, w, h);
                Text t = lr.gameObject.AddComponent<Text>();
                t.font = font; t.fontSize = size; t.color = labelColor; t.alignment = align;
                t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
                t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
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
