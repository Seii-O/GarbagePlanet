// @trace UI-008
using UnityEngine;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 任务日志面板的真实 uGUI 结构工厂：按「10_任务日志.png」用拆件拼装，
    /// 左侧统计 / 当前任务 / 生态进度，右侧 10 条任务列表，底部关闭 + 查看结算。
    /// 所有控件具名、Inspector 可改；Editor Builder 与运行时兜底共用本布局。
    /// </summary>
    public static class QuestLogUiFactory
    {
        public const int TaskCount = 10;
        const float PW = 1440f, PH = 920f;

        static readonly Color Ink = new Color(0.16f, 0.13f, 0.09f, 1f);
        static readonly Color InkSoft = new Color(0.42f, 0.36f, 0.28f, 1f);
        static readonly Color InkDim = new Color(0.58f, 0.53f, 0.45f, 1f);
        static readonly Color Amber = new Color(0.78f, 0.46f, 0.10f, 1f);
        static readonly Color Teal = new Color(0.17f, 0.66f, 0.60f, 1f);
        static readonly Color OnPrimary = new Color(0.04f, 0.11f, 0.10f, 1f);

        public static GameObject Build(Transform canvas, QuestLogArtSet art, Font font)
        {
            if (canvas == null) return null;
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            GameObject root = new GameObject(QuestLogPanelView.PanelName, typeof(RectTransform));
            root.transform.SetParent(canvas, false);
            Stretch((RectTransform)root.transform);

            RectTransform panel = Panel(root.transform, art != null ? art.panelBig : null);

            // 标题
            Img(panel, "Img_TitleMark", 30f, 26f, 26f, 26f, art?.statusDotTeal, Teal, false, Image.Type.Simple, true);
            Lbl(panel, "Txt_Title", "任务日志", 68f, 20f, 300f, 44f, 32, Ink, TextAnchor.MiddleLeft, true, font);
            Div(panel, "Img_HeaderDiv", 20f, 76f, 1400f, 2f, art);

            BuildLeft(panel, art, font);
            BuildList(panel, art, font);
            BuildBottom(panel, art, font);

            // 盖住旧面板但让数量弹窗/HUD 提示浮在上层
            Transform quantity = FindChild(canvas, "Panel_Quantity");
            Transform station = FindChild(canvas, "Panel_Station");
            if (quantity != null && quantity.parent == canvas)
                root.transform.SetSiblingIndex(quantity.GetSiblingIndex());
            else if (station != null && station.parent == canvas)
                root.transform.SetSiblingIndex(station.GetSiblingIndex() + 1);

            root.SetActive(false);
            return root;
        }

        static void BuildLeft(RectTransform panel, QuestLogArtSet art, Font font)
        {
            RectTransform left = Node(panel, "QuestLeft", 20f, 96f, 340f, 790f);

            Lbl(left, "Txt_ProgressLabel", "任务进度", 0f, 0f, 200f, 28f, 14, InkDim, TextAnchor.MiddleLeft, false, font);
            Lbl(left, "Txt_DoneBig", "00", 0f, 26f, 100f, 62f, 50, Amber, TextAnchor.MiddleLeft, true, font);
            Lbl(left, "Txt_DoneTotal", "/ 10", 104f, 50f, 80f, 30f, 17, InkDim, TextAnchor.MiddleLeft, false, font);
            for (int i = 0; i < TaskCount; i++)
                Img(left, "Dot_Progress_" + i, i * 22f, 96f, 14f, 14f, art?.dotEmpty, Color.white, false, Image.Type.Simple);

            // 三格统计
            string[] nums = { "0", "0", "0" };
            string[] caps = { "已完成", "进行中", "未解锁" };
            for (int i = 0; i < 3; i++)
            {
                RectTransform box = Card(left, "Stat_" + i, i * 116f, 132f, 108f, 62f, "", art, font, false);
                Lbl(box, "Txt_Num", nums[i], 8f, 6f, 92f, 28f, 24, i == 1 ? Teal : Amber, TextAnchor.MiddleCenter, true, font);
                Lbl(box, "Txt_Cap", caps[i], 8f, 34f, 92f, 22f, 12, InkDim, TextAnchor.MiddleCenter, false, font);
            }

            // 当前任务卡
            RectTransform cur = Card(left, "Card_CurrentQuest", 0f, 214f, 340f, 250f, "当前任务", art, font);
            Lbl(cur, "Txt_CurrentTask", "—", 14f, 44f, 310f, 30f, 18, Ink, TextAnchor.MiddleLeft, true, font);
            Lbl(cur, "Txt_Hint1", "① —", 16f, 84f, 310f, 26f, 13, InkSoft, TextAnchor.MiddleLeft, false, font);
            Lbl(cur, "Txt_Hint2", "② —", 16f, 114f, 310f, 26f, 13, InkSoft, TextAnchor.MiddleLeft, false, font);
            Lbl(cur, "Txt_Hint3", "③ —", 16f, 144f, 310f, 26f, 13, InkSoft, TextAnchor.MiddleLeft, false, font);
            for (int i = 0; i < 3; i++)
                Img(cur, "Dot_Step_" + i, 16f + i * 22f, 180f, 14f, 14f, art?.dotEmpty, Color.white, false, Image.Type.Simple);
            Lbl(cur, "Txt_CurrentProgress", "已完成 0 / 3 步", 92f, 174f, 230f, 26f, 13, Amber, TextAnchor.MiddleLeft, true, font);
            Btn(cur, "Btn_GoStation", "前往中转站", 14f, 208f, 312f, 36f,
                art?.btnSecondary, Ink, 17, TextAnchor.MiddleCenter, true, font);

            // 生态进度卡
            RectTransform eco = Card(left, "Card_Eco", 0f, 482f, 340f, 260f, "生态进度", art, font);
            Lbl(eco, "Txt_EcoPct", "0 %", 14f, 42f, 100f, 46f, 28, Amber, TextAnchor.MiddleLeft, true, font);
            EcoRow(eco, "Eco_Land", 14f, 104f, art?.ringTeal, "土地修复", "生态肥料包 0 / 10", font);
            EcoRow(eco, "Eco_Water", 14f, 170f, art?.ringTeal, "水源修复", "水质过滤器 0 / 10", font);
            Lbl(eco, "Txt_EcoNote", "任务进度中新增任务 10 条会进入成就页面", 14f, 226f, 312f, 26f, 11, InkDim, TextAnchor.MiddleLeft, false, font);
        }

        static void EcoRow(Transform parent, string name, float x, float y, Sprite ring, string title, string detail, Font font)
        {
            RectTransform row = Node(parent, name, x, y, 312f, 58f);
            Img(row, "Img_Ring", 0f, 4f, 44f, 44f, ring, Color.white, false, Image.Type.Simple, true);
            Lbl(row, "Txt_Title", title, 56f, 2f, 250f, 26f, 14, Ink, TextAnchor.MiddleLeft, true, font);
            Lbl(row, "Txt_Detail", detail, 56f, 28f, 250f, 24f, 12, InkDim, TextAnchor.MiddleLeft, false, font);
        }

        static void BuildList(RectTransform panel, QuestLogArtSet art, Font font)
        {
            RectTransform list = Node(panel, "QuestList", 380f, 96f, 1040f, 790f);
            for (int i = 0; i < TaskCount; i++)
            {
                RectTransform row = Row(list, "Row_Quest_" + i, 0f, i * 76f, 1040f, 70f, art);
                Img(row, "Img_State_" + i, 14f, 19f, 30f, 30f, art?.Step(2), Color.white, false, Image.Type.Simple, true);
                Lbl(row, "Txt_Index_" + i, (i + 1).ToString("00"), 14f, 19f, 30f, 30f, 12, InkSoft, TextAnchor.MiddleCenter, true, font);
                Img(row, "Img_Mark_" + i, 18f, 23f, 22f, 22f, art?.icCheck, Teal, false, Image.Type.Simple, true).gameObject.SetActive(false);
                Lbl(row, "Txt_Title_" + i, "任务", 56f, 10f, 170f, 28f, 17, Ink, TextAnchor.MiddleLeft, true, font);
                Lbl(row, "Txt_Desc_" + i, "—", 220f, 10f, 360f, 28f, 14, InkSoft, TextAnchor.MiddleLeft, false, font);
                for (int k = 0; k < 5; k++)
                    Img(row, "Dot_" + i + "_" + k, 600f + k * 26f, 26f, 14f, 14f, art?.dotEmpty, Color.white, false, Image.Type.Simple);
                Lbl(row, "Txt_Count_" + i, "0 / 1", 780f, 10f, 90f, 30f, 15, Amber, TextAnchor.MiddleRight, true, font);
                Lbl(row, "Txt_Status_" + i, "未解锁", 900f, 10f, 110f, 30f, 14, InkDim, TextAnchor.MiddleCenter, false, font);
            }
        }

        static void BuildBottom(RectTransform panel, QuestLogArtSet art, Font font)
        {
            RectTransform bar = Img(panel, "BottomBar", 20f, 852f, 1400f, 56f,
                art?.bottomBar, Color.white, false, Image.Type.Sliced).rectTransform;
            Lbl(bar, "Txt_BottomLeft", "完成全部任务后进入成就界面", 24f, 12f, 700f, 32f, 14, InkSoft, TextAnchor.MiddleLeft, false, font);
            Btn(bar, "Btn_QuestClose", "关闭", 1120f, 8f, 130f, 40f, art?.btnGhost, Ink, 18, TextAnchor.MiddleCenter, false, font);
            Btn(bar, "Btn_ViewSettlement", "查看结算", 1270f, 8f, 130f, 40f, art?.btnDisabled, InkDim, 18, TextAnchor.MiddleCenter, true, font);
        }

        // ---------------- 通用辅助 ----------------
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

        static RectTransform Card(Transform parent, string name, float x, float y, float w, float h,
            string title, QuestLogArtSet art, Font font, bool titleVisible = true)
        {
            RectTransform card = Img(parent, name, x, y, w, h, art?.card, Color.white, false, Image.Type.Sliced).rectTransform;
            if (titleVisible && !string.IsNullOrEmpty(title))
            {
                Lbl(card, "Txt_Title", title, 14f, 10f, w - 28f, 32f, 16, Ink, TextAnchor.MiddleLeft, true, font);
                Div(card, "Img_TitleDiv", 12f, 42f, w - 24f, 2f, art);
            }
            return card;
        }

        static RectTransform Row(Transform parent, string name, float x, float y, float w, float h, QuestLogArtSet art)
        {
            return Img(parent, name, x, y, w, h, art?.listRow, Color.white, false, Image.Type.Sliced).rectTransform;
        }

        static void Div(Transform parent, string name, float x, float y, float w, float h, QuestLogArtSet art)
        {
            Img(parent, name, x, y, w, h, art?.card, new Color(0.78f, 0.72f, 0.62f, 0.35f), false, Image.Type.Sliced);
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
            img.color = sprite != null ? Color.white : new Color(0.17f, 0.66f, 0.60f, 0.9f);
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
