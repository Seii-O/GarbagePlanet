// @trace COIN-001, COIN-002, UI-004
using UnityEngine;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 独立金币交易界面（B 键）。只显示余额、可购/可售与流水；不是背包界面的一部分，
    /// 也不读背包格子数组。UI 由「⑬ 生成背包与交易界面」生成到 UI_Whitebox 下。
    /// </summary>
    public sealed class CoinTradePanelView : MonoBehaviour
    {
        public const string PanelName = "Panel_CoinTrade";
        const int MaxRows = 12;

        CoinTradeSystem system;
        RectTransform panel;
        Text txtBalance;
        Text txtBuyList;
        Text txtSellList;
        Text txtResult;
        Text txtLog;
        Button[] buyButtons = new Button[MaxRows];
        Button[] sellButtons = new Button[MaxRows];
        Button btnClose;
        readonly RectTransform[] bookCards = new RectTransform[MaxRows];
        readonly Text[] bookNames = new Text[MaxRows];
        readonly Text[] bookDetails = new Text[MaxRows];
        RectTransform buyContent, sellContent;
        Text sellEmpty;
#if UNITY_EDITOR
        static readonly Color Ink = new Color(0.23f, 0.18f, 0.13f);
        static readonly Color Muted = new Color(0.34f, 0.29f, 0.22f);
        static readonly Color Gold = new Color(1f, 0.82f, 0.43f);
#endif

        string[] buyIds = new string[0];
        string[] sellIds = new string[0];

        public bool IsOpen => panel != null && panel.gameObject.activeSelf;

        void Awake() { Bind(); }

        bool Bind()
        {
            if (panel != null) return true;
            Transform found = FindDeep(transform, PanelName);
            if (found == null)
            {
                Debug.LogWarning("[CoinTrade] 未找到 " + PanelName + "，请跑 Tools/垃圾星球白膜/⑬ 生成背包与交易界面");
                return false;
            }
            panel = found as RectTransform;
            if (panel == null) return false;
            txtBalance = FindText(panel, "Txt_CoinBalance");
            txtBuyList = FindText(panel, "Txt_BuyList");
            txtSellList = FindText(panel, "Txt_SellList");
            txtResult = FindText(panel, "Txt_TradeResult");
            txtLog = FindText(panel, "Txt_TradeLog");
            for (int i = 0; i < MaxRows; i++)
            {
                buyButtons[i] = FindButton(panel, "Btn_Buy_" + i.ToString("00"));
                sellButtons[i] = FindButton(panel, "Btn_Sell_" + i.ToString("00"));
                bookCards[i] = FindDeep(panel, "Book Card " + i) as RectTransform;
                bookNames[i] = FindText(bookCards[i], "Book Name");
                bookDetails[i] = FindText(bookCards[i], "Book Effect");
                if (buyButtons[i] != null)
                {
                    int idx = i;
                    buyButtons[i].onClick.RemoveAllListeners();
                    buyButtons[i].onClick.AddListener(() => OnBuy(idx));
                }
                if (sellButtons[i] != null)
                {
                    int idx = i;
                    sellButtons[i].onClick.RemoveAllListeners();
                    sellButtons[i].onClick.AddListener(() => OnSell(idx));
                }
            }
            btnClose = FindButton(panel, "Btn_TradeClose");
            if (btnClose != null) btnClose.onClick.RemoveAllListeners();
            if (btnClose != null) btnClose.onClick.AddListener(OnCloseClick);
            buyContent = FindDeep(panel, "Buy Scroll")?.Find("Content") as RectTransform;
            sellContent = FindDeep(panel, "Sell Scroll")?.Find("Content") as RectTransform;
            sellEmpty = FindText(panel, "Sell Empty");
            panel.gameObject.SetActive(false);
            return true;
        }

        public void Init(CoinTradeSystem s)
        {
            system = s;
            if (!Bind()) return;
            Refresh();
        }

        void OnBuy(int row)
        {
            if (system == null || row < 0 || row >= buyIds.Length) return;
            string id = buyIds[row];
            if (string.IsNullOrEmpty(id)) return;
            CoinTradeResult r = system.Buy(id);
            ShowResult(r);
            Refresh();
        }

        void OnSell(int row)
        {
            if (system == null || row < 0 || row >= sellIds.Length) return;
            string id = sellIds[row];
            if (string.IsNullOrEmpty(id)) return;
            CoinTradeResult r = system.Sell(id, 1);
            ShowResult(r);
            Refresh();
        }

        void ShowResult(CoinTradeResult r)
        {
            if (txtResult == null) return;
            txtResult.text = (r.ok ? "✔ " : "✘ ") + r.message;
            txtResult.color = r.ok ? new Color(0.16f, 0.37f, 0.24f) : new Color(0.58f, 0.20f, 0.12f);
        }

        void OnCloseClick() { if (system != null) system.Close(); }

        public void Show() { if (Bind()) { panel.gameObject.SetActive(true); Refresh(); } }

        public void Hide() { if (Bind()) panel.gameObject.SetActive(false); }

        public void Refresh()
        {
            if (!Bind() || system == null) return;
            if (txtBalance != null) txtBalance.text = "金币余额  " + system.Balance;
            buyIds = system.BuyCatalog();
            sellIds = system.SellCatalog();
            if (sellEmpty != null) sellEmpty.gameObject.SetActive(sellIds.Length == 0);

            for (int i = 0; i < MaxRows; i++)
            {
                bool hasBuy = i < buyIds.Length;
                if (bookCards[i] != null)
                {
                    bookCards[i].gameObject.SetActive(hasBuy);
                    if (hasBuy)
                    {
                        string id = buyIds[i];
                        int price = system.BuyPrice(id);
                        SkillBookDef book = system.FindBook(id);
                        if (bookNames[i] != null) bookNames[i].text = book != null ? book.displayName : system.NameOf(id);
                        if (bookDetails[i] != null) bookDetails[i].text = DescribeBook(book);
                        bool affordable = system.Balance >= price;
                        buyButtons[i].interactable = affordable;
                        SetButtonLabel(buyButtons[i], price + " 金币\n" + (affordable ? "购买" : "余额不足"));
                    }
                }
                bool hasSell = i < sellIds.Length;
                if (sellButtons[i] != null)
                {
                    sellButtons[i].gameObject.SetActive(hasSell);
                    if (hasSell)
                    {
                        string name = system.NameOf(sellIds[i]);
                        SetButtonLabel(sellButtons[i], name + "　+" + system.SellPrice(sellIds[i]) + " 金币");
                    }
                }
            }
            FitScrollContent(buyContent);
            FitScrollContent(sellContent);
            if (buyIds.Length == 0 && txtResult != null && string.IsNullOrEmpty(txtResult.text))
                txtResult.text = "暂无可购买物品";

            if (txtLog != null)
            {
                WhiteboxState s = GetState();
                string log = "";
                if (s != null && s.tradeItemIds != null)
                {
                    int start = Mathf.Max(0, s.tradeItemIds.Length - 2);
                    for (int i = s.tradeItemIds.Length - 1; i >= start; i--)
                    {
                        string kind = s.tradeKinds[i] == 0 ? "买入" : "卖出";
                        string name = system.NameOf(s.tradeItemIds[i]);
                        if (log.Length > 0) log += "\n";
                        log += kind + " " + name + " ×" + s.tradeAmounts[i] + "　" + s.tradePrices[i] + " 金币";
                    }
                }
                txtLog.text = log.Length > 0 ? log : "暂无交易";
            }
        }

        static void FitScrollContent(RectTransform content)
        {
            if (content == null || !(content.parent is RectTransform viewport)) return;
            float height = viewport.rect.height;
            foreach (Transform child in content)
                if (child.gameObject.activeSelf && child is RectTransform row)
                    height = Mathf.Max(height, -row.anchoredPosition.y + row.rect.height * row.pivot.y);
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }

#if UNITY_EDITOR
        public void BakeLayout()
        {
            panel = FindDeep(transform, PanelName) as RectTransform;
            if (panel == null || panel.Find("Buy Scroll") != null) return;
            txtBalance = FindText(panel, "Txt_CoinBalance");
            txtBuyList = FindText(panel, "Txt_BuyList");
            txtSellList = FindText(panel, "Txt_SellList");
            txtResult = FindText(panel, "Txt_TradeResult");
            txtLog = FindText(panel, "Txt_TradeLog");
            btnClose = FindButton(panel, "Btn_TradeClose");
            for (int i = 0; i < MaxRows; i++)
            {
                buyButtons[i] = FindButton(panel, "Btn_Buy_" + i.ToString("00"));
                sellButtons[i] = FindButton(panel, "Btn_Sell_" + i.ToString("00"));
            }
            StationArtSet art = GetComponent<StationFiveTabsView>()?.Art;
            InventoryPanelView inventory = GetComponent<InventoryPanelView>();
            string[] previewBooks = { "book_sorting", "book_handwork", "book_flow", "book_tireless" };
            string[] previewNames = { "快分拣", "熟手工", "一气呵成", "不知疲倦" };
            ToolConfig tools = UnityEditor.AssetDatabase.LoadAssetAtPath<ToolConfig>("Assets/Data/ToolConfig.asset");
            Font font = txtBalance != null ? txtBalance.font : null;
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Keep the existing interactive objects and replace the old decorative layers.
            foreach (Transform child in panel)
            {
                string name = child.name;
                if (name != "Txt_CoinBalance" && name != "Txt_BuyList" && name != "Txt_SellList"
                    && name != "Txt_TradeResult" && name != "Txt_TradeLog" && name != "Btn_TradeClose"
                    && !name.StartsWith("Btn_Buy_") && !name.StartsWith("Btn_Sell_"))
                    child.gameObject.SetActive(false);
            }
            Place(panel, 0f, 0f, 1176f, 752f);
            Image frame = panel.GetComponent<Image>();
            if (frame != null)
            {
                Sprite frameSprite = art?.panelSmall ?? frame.sprite;
                Skin(frame, frameSprite, new Color(0.86f, 0.80f, 0.68f));
                frame.raycastTarget = true;
            }

            Image header = MakeImage(panel, "Shop Header", 0f, 317f, 1096f, 66f,
                art?.headerBar, new Color(0.25f, 0.20f, 0.15f));
            header.transform.SetSiblingIndex(0);
            MakeText(panel, "Title", "中转站商店", -307f, 317f, 430f, 46f, font, 32,
                Color.white, TextAnchor.MiddleLeft, true);
            StyleText(txtBalance, 325f, 317f, 420f, 44f, 27, Gold, TextAnchor.MiddleRight, true);
            StyleText(txtBuyList, -195f, 254f, 696f, 34f, 23, Ink, TextAnchor.MiddleLeft, true);
            if (txtBuyList != null) txtBuyList.text = "购买技能书";
            MakeText(panel, "Buy Hint", "购入后，在工具背包（I）里使用才会生效。", -195f, 222f, 696f, 28f,
                font, 17, Muted, TextAnchor.MiddleLeft);
            StyleText(txtSellList, 381f, 254f, 324f, 34f, 23, Ink, TextAnchor.MiddleLeft, true);
            if (txtSellList != null) txtSellList.text = "出售物品";
            MakeText(panel, "Sell Hint", "每次出售 1 件，换取金币。", 381f, 222f, 324f, 28f,
                font, 17, Muted, TextAnchor.MiddleLeft);

            buyContent = MakeScroll("Buy Scroll", -195f, 680f);
            sellContent = MakeScroll("Sell Scroll", 381f, 324f);
            for (int i = 0; i < MaxRows; i++)
            {
                if (buyButtons[i] != null)
                {
                    Image card = MakeImage(buyContent, "Book Card " + i, 0f, 0f, 680f, 98f,
                        art?.card, new Color(0.96f, 0.91f, 0.80f));
                    bookCards[i] = card.rectTransform;
                    PlaceRow(card.rectTransform, 53f + i * 109f);
                    Sprite icon = i < previewBooks.Length
                        ? inventory?.ItemIcon(previewBooks[i]) ?? UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(
                            "Assets/Art/UI/ToolIcons/ic_" + previewBooks[i] + ".png") : null;
                    Image iconImage = MakeImage(card.transform, "Book Icon", -294f, 0f, 58f, 58f, icon, Color.clear);
                    iconImage.preserveAspect = true;
                    bookNames[i] = MakeText(card.transform, "Book Name", i < previewNames.Length ? previewNames[i] : "", -49f, 27f, 414f, 32f,
                        font, 23, Ink, TextAnchor.MiddleLeft, true);
                    SkillBookDef preview = i < previewBooks.Length ? tools?.FindBook(previewBooks[i]) : null;
                    bookDetails[i] = MakeText(card.transform, "Book Effect", preview != null ? DescribeBook(preview) : "", -49f, -18f, 414f, 51f,
                        font, 17, Muted, TextAnchor.MiddleLeft);
                    buyButtons[i].transform.SetParent(card.transform, false);
                    StyleButton(buyButtons[i], 244f, 0f, 148f, 60f, 17, art?.btnPrimary, new Color(0.20f, 0.36f, 0.21f), Color.white);
                    if (preview != null) SetButtonLabel(buyButtons[i], preview.Price + " 金币\n购买");
                    card.gameObject.SetActive(i < previewBooks.Length);
                }
                if (sellButtons[i] != null)
                {
                    sellButtons[i].transform.SetParent(sellContent, false);
                    StyleButton(sellButtons[i], 0f, 0f, 320f, 43f, 17, art?.btnGhost, new Color(0.93f, 0.86f, 0.72f), Ink);
                    PlaceRow((RectTransform)sellButtons[i].transform, 24f + i * 50f);
                    sellButtons[i].gameObject.SetActive(false);
                }
            }
            sellEmpty = MakeText(panel, "Sell Empty", "暂无可出售物品\n加工材料和抽奖小物件可在这里出售。",
                381f, 132f, 302f, 90f, font, 18, Muted, TextAnchor.MiddleCenter);

            MakeText(panel, "History Heading", "最近交易", -447f, -261f, 188f, 26f,
                font, 19, Ink, TextAnchor.MiddleLeft, true);
            StyleText(txtLog, -253f, -309f, 576f, 62f, 16, Muted, TextAnchor.UpperLeft);
            StyleText(txtResult, 193f, -306f, 320f, 62f, 17, Ink, TextAnchor.MiddleLeft);
            if (btnClose != null)
                StyleButton(btnClose, 442f, -306f, 174f, 53f, 21, art?.btnPrimary, new Color(0.20f, 0.36f, 0.21f), Color.white);
            panel = null;
        }

        RectTransform MakeScroll(string name, float x, float width)
        {
            RectTransform viewport = MakeRect(panel, name, x, -12f, width, 436f);
            Image hit = viewport.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = MakeRect(viewport, "Content", 0f, 0f, width, 436f);
            content.anchorMin = content.anchorMax = content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 35f;
            return content;
        }

        static void PlaceRow(RectTransform row, float fromTop)
        {
            row.anchorMin = row.anchorMax = new Vector2(0.5f, 1f);
            row.anchoredPosition = new Vector2(0f, -fromTop);
        }

        static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localScale = Vector3.one;
            rect.sizeDelta = new Vector2(w, h);
            rect.anchoredPosition = new Vector2(x, y);
        }

        static RectTransform MakeRect(Transform parent, string name, float x, float y, float w, float h)
        {
            RectTransform rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            Place(rect, x, y, w, h);
            return rect;
        }

        static Image MakeImage(Transform parent, string name, float x, float y, float w, float h, Sprite sprite, Color fallback)
        {
            Image image = MakeRect(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            Skin(image, sprite, fallback);
            return image;
        }

        static void Skin(Image image, Sprite sprite, Color fallback)
        {
            image.sprite = sprite;
            image.color = sprite != null ? Color.white : fallback;
            image.type = sprite != null && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
        }

        static Text MakeText(Transform parent, string name, string value, float x, float y, float w, float h,
            Font font, int size, Color color, TextAnchor alignment, bool bold = false)
        {
            Text text = MakeRect(parent, name, x, y, w, h).gameObject.AddComponent<Text>();
            text.font = font;
            text.text = value;
            StyleText(text, x, y, w, h, size, color, alignment, bold);
            return text;
        }

        static void StyleText(Text text, float x, float y, float w, float h, int size, Color color,
            TextAnchor alignment, bool bold = false)
        {
            if (text == null) return;
            Place(text.rectTransform, x, y, w, h);
            text.fontSize = size;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
        }

        static void StyleButton(Button button, float x, float y, float w, float h, int size, Sprite sprite,
            Color fallback, Color textColor)
        {
            Place((RectTransform)button.transform, x, y, w, h);
            if (button.image != null) Skin(button.image, sprite, fallback);
            Text label = button.GetComponentInChildren<Text>(true);
            if (label != null) StyleText(label, 0f, 0f, w - 18f, h - 8f, size, textColor, TextAnchor.MiddleCenter, true);
        }
#endif

        string DescribeBook(SkillBookDef book)
        {
            if (book == null) return "购买后放入背包。";
            int learned = system != null ? system.LearnedStacks(book.rewardId) : 0;
            string effect;
            switch (book.effect)
            {
                case BookEffect.ProcessTime:
                    effect = "垃圾加工耗时每层减少 " + ((1f - book.perStackMult) * 100f).ToString("0.#") + "%（逐层相乘）";
                    break;
                case BookEffect.CraftTime:
                    effect = "产品制造耗时每层减少 " + ((1f - book.perStackMult) * 100f).ToString("0.#") + "%（逐层相乘）";
                    break;
                case BookEffect.FreeCharge:
                    effect = "满层后，可瞬间完成加工或制造的次数上限 +1";
                    break;
                default:
                    effect = "在背包使用后生效。";
                    break;
            }
            return effect + "\n已学习 " + learned + " / " + book.maxStacks + " 层"
                + (book.effect == BookEffect.FreeCharge ? " · 生态阶段提升时补充（上限 3）" : "");
        }

        WhiteboxState GetState()
        {
            Object[] all = FindObjectsOfType(typeof(WhiteboxGame));
            if (all == null || all.Length == 0) return null;
            WhiteboxGame g = all[0] as WhiteboxGame;
            return g != null ? g.State : null;
        }

        static Text FindText(Transform root, string name)
        {
            Transform t = FindDeep(root, name);
            return t != null ? t.GetComponent<Text>() : null;
        }

        static Button FindButton(Transform root, string name)
        {
            Transform t = FindDeep(root, name);
            return t != null ? t.GetComponent<Button>() : null;
        }

        static void SetButtonLabel(Button b, string text)
        {
            if (b == null) return;
            Text label = b.GetComponentInChildren<Text>(true);
            if (label != null) label.text = text;
        }

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
