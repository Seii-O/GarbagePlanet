// @trace BAG-003, UI-006
using UnityEngine;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 独立垃圾背包面板（G 键），浅色卡片式布局：2×2 分类卡 × 每类 2 张单品卡。
    ///
    /// 边界：本面板只展示 State.bag / bagItems（垃圾），不读工具格子、金币、中转站库存；
    /// 投放/加工/制造仍在中转站（M），工具背包在 I，金币交易在 B。
    ///
    /// 与工程内其它视图同口径：运行时只写 Text.text / Image.sprite 开关 / Image.fillAmount / SetActive，
    /// 不改 RectTransform、颜色、字号、字体；美术换图直接改场景里的 Image Source Image。
    /// 面板由 GarbageBagUiBuilder 生成到 UI_Whitebox/Panel_GarbageBag，显隐由 GarbageBagSystem 控制。
    /// </summary>
    public sealed class GarbageBagPanelView : MonoBehaviour
    {
        public const string PanelName = "Panel_GarbageBag";

        GarbageBagSystem system;
        RectTransform panel;
        Text txtCapacity;
        Text txtTotal;
        readonly Text[] catCaps = new Text[4];
        readonly Text[] catRemain = new Text[4];
        readonly Image[] catBars = new Image[4];
        readonly Image[] itemIcons = new Image[GarbageBagCatalog.ItemCount];
        readonly Text[] itemNames = new Text[GarbageBagCatalog.ItemCount];
        readonly Text[] itemCounts = new Text[GarbageBagCatalog.ItemCount];
        Button btnSort;
        Button btnClose;

        public bool IsOpen => panel != null && panel.gameObject.activeSelf;

        void Awake() { Bind(); }

        public void Init(GarbageBagSystem s)
        {
            system = s;
            if (!Bind()) return;
            Refresh();
        }

        bool Bind()
        {
            if (panel != null) return true;
            Transform root = transform;
            Transform found = FindDeep(root, PanelName);
            if (found == null)
            {
                Debug.LogWarning("[GarbageBag] 未找到 " + PanelName
                                 + "，请跑 Tools/垃圾星球白膜/⑳ 生成垃圾背包界面（G）");
                return false;
            }
            panel = found as RectTransform;
            if (panel == null) return false;

            txtCapacity = FindText(panel, "Txt_GBCapacity");
            txtTotal = FindText(panel, "Txt_GBTotal");
            btnSort = FindButton(panel, "Btn_GBSort");
            btnClose = FindButton(panel, "Btn_GBClose");

            for (int cat = 0; cat < 4; cat++)
            {
                catCaps[cat] = FindText(panel, "Txt_GBCatCap_" + cat);
                catRemain[cat] = FindText(panel, "Txt_GBCatRemain_" + cat);
                catBars[cat] = FindImage(panel, "Img_GBBarFill_" + cat);
            }
            for (int i = 0; i < GarbageBagCatalog.ItemCount; i++)
            {
                itemIcons[i] = FindImage(panel, "Img_GBItemIcon_" + i);
                itemNames[i] = FindText(panel, "Txt_GBItemName_" + i);
                itemCounts[i] = FindText(panel, "Txt_GBItemCount_" + i);
                Button discard = FindButton(panel, "Btn_GBDiscard_" + i);
                if (discard != null)
                {
                    int idx = i;
                    discard.onClick.RemoveAllListeners();
                    discard.onClick.AddListener(() => OnDiscard(idx));
                }
            }
            if (btnSort != null)
            {
                btnSort.onClick.RemoveAllListeners();
                btnSort.onClick.AddListener(OnSort);
            }
            if (btnClose != null)
            {
                btnClose.onClick.RemoveAllListeners();
                btnClose.onClick.AddListener(OnClose);
            }
            panel.gameObject.SetActive(false);
            return true;
        }

        void OnDiscard(int itemIndex)
        {
            if (system == null) return;
            string reason;
            if (!system.Discard(itemIndex, 1, out reason)) system.Notice(reason);
        }

        void OnSort()
        {
            if (system == null) return;
            string reason;
            int changed = system.Tidy(out reason);
            if (!string.IsNullOrEmpty(reason)) { system.Notice(reason); return; }
            system.Notice(changed > 0 ? "已整理：校准 " + changed + " 件" : "已整理：数量本来就整齐");
        }

        void OnClose() { if (system != null) system.Close(); }

        public void Show() { if (Bind()) { panel.gameObject.SetActive(true); Refresh(); } }

        public void Hide()
        {
            if (!Bind()) return;
            panel.gameObject.SetActive(false);
        }

        public void Refresh()
        {
            if (!Bind() || system == null) return;
            int total = system.Total;
            int cap = system.Capacity;
            SetText(txtCapacity, "容量 " + total + " / " + cap);
            SetText(txtTotal, "总容量 " + total + " / " + cap);

            for (int cat = 0; cat < 4; cat++)
            {
                int have = system.CategoryCount(cat);
                int catCap = system.CategoryCap(cat);
                SetText(catCaps[cat], "容量 " + have + " / " + catCap);
                SetText(catRemain[cat], "剩余 " + Mathf.Max(0, catCap - have) + " 格");
                if (catBars[cat] != null)
                    catBars[cat].fillAmount = catCap > 0 ? Mathf.Clamp01(have / (float)catCap) : 0f;
            }
            for (int i = 0; i < GarbageBagCatalog.ItemCount; i++)
            {
                int count = system.ItemCount(i);
                SetText(itemNames[i], GarbageBagCatalog.NameOf(i));
                SetText(itemCounts[i], count.ToString());
                // 没捡到的单品隐藏图标，保留卡片轮廓，方便玩家知道还有哪些种类可收集
                if (itemIcons[i] != null) itemIcons[i].enabled = count > 0;
            }
        }

        static void SetText(Text t, string value)
        {
            if (t != null && t.text != value) t.text = value;
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

        static Image FindImage(Transform root, string name)
        {
            Transform t = FindDeep(root, name);
            return t != null ? t.GetComponent<Image>() : null;
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
