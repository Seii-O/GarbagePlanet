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
            txtResult.color = r.ok ? new Color(0.45f, 0.88f, 0.68f, 1f) : new Color(0.95f, 0.55f, 0.45f, 1f);
        }

        void OnCloseClick() { if (system != null) system.Close(); }

        public void Show() { if (Bind()) { panel.gameObject.SetActive(true); Refresh(); } }

        public void Hide() { if (Bind()) panel.gameObject.SetActive(false); }

        public void Refresh()
        {
            if (!Bind() || system == null) return;
            if (txtBalance != null) txtBalance.text = "金币余额　" + system.Balance + "　（B 关闭）";
            buyIds = system.BuyCatalog();
            sellIds = system.SellCatalog();
            if (txtBuyList != null) txtBuyList.text = "【购买】点按钮用金币购买，自动入背包";
            if (txtSellList != null) txtSellList.text = "【出售】点按钮出售，金币来自出售";

            for (int i = 0; i < MaxRows; i++)
            {
                bool hasBuy = i < buyIds.Length;
                if (buyButtons[i] != null)
                {
                    buyButtons[i].gameObject.SetActive(hasBuy);
                    if (hasBuy)
                    {
                        string name = system.Bag != null ? system.Bag.DisplayName(buyIds[i]) : buyIds[i];
                        SetButtonLabel(buyButtons[i], name + "　" + system.BuyPrice(buyIds[i]) + " 金币");
                    }
                }
                bool hasSell = i < sellIds.Length;
                if (sellButtons[i] != null)
                {
                    sellButtons[i].gameObject.SetActive(hasSell);
                    if (hasSell)
                    {
                        string name = system.Bag != null ? system.Bag.DisplayName(sellIds[i]) : sellIds[i];
                        SetButtonLabel(sellButtons[i], name + "　+" + system.SellPrice(sellIds[i]) + " 金币");
                    }
                }
            }
            if (buyIds.Length == 0 && txtResult != null && string.IsNullOrEmpty(txtResult.text))
                txtResult.text = "暂无可购买物品";
            if (sellIds.Length == 0 && txtResult != null && string.IsNullOrEmpty(txtResult.text))
                txtResult.text = "暂无可出售物品：去转盘抽些小物件吧";

            if (txtLog != null)
            {
                WhiteboxState s = GetState();
                string log = "最近交易：\n";
                if (s != null && s.tradeItemIds != null)
                {
                    int start = Mathf.Max(0, s.tradeItemIds.Length - 6);
                    for (int i = s.tradeItemIds.Length - 1; i >= start; i--)
                    {
                        string kind = s.tradeKinds[i] == 0 ? "买入" : "卖出";
                        string name = system.Bag != null ? system.Bag.DisplayName(s.tradeItemIds[i]) : s.tradeItemIds[i];
                        log += kind + " " + name + " ×" + s.tradeAmounts[i] + "　" + s.tradePrices[i] + " 金币\n";
                    }
                }
                txtLog.text = log;
            }
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
