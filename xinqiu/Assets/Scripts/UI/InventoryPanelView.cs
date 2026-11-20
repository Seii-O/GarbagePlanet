// @trace BAG-001, BAG-002, UI-004
using UnityEngine;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 独立背包界面（I 键）。UI 由 InventoryBuilder 在「⑬ 生成背包与交易界面」时生成到 UI_Whitebox 下；
    /// 本脚本按名字查找并运行时绑定按钮，面板默认失活，显隐由 ToolBagSystem 控制。
    /// 与金币交易界面完全分离：不读金币、不显示价格、不触发交易。
    /// </summary>
    public sealed class InventoryPanelView : MonoBehaviour
    {
        public const string PanelName = "Panel_Inventory";
        const int SlotCount = 12;

        ToolBagSystem system;
        RectTransform panel;
        Text txtTitle;
        Text txtCapacity;
        Text txtDetail;
        Text[] slotTexts = new Text[SlotCount];
        Button[] slotButtons = new Button[SlotCount];
        Button btnTakeOut;
        Button btnPutIn;
        Button btnClose;
        int selected = -1;

        public bool IsOpen => panel != null && panel.gameObject.activeSelf;

        void Awake() { Bind(); }

        bool Bind()
        {
            if (panel != null) return true;
            Transform root = transform;
            Transform found = FindDeep(root, PanelName);
            if (found == null)
            {
                Debug.LogWarning("[Inventory] 未找到 " + PanelName + "，请跑 Tools/垃圾星球白膜/⑬ 生成背包与交易界面");
                return false;
            }
            panel = found as RectTransform;
            if (panel == null) return false;
            txtTitle = FindText(panel, "Txt_InvTitle");
            txtCapacity = FindText(panel, "Txt_InvCapacity");
            txtDetail = FindText(panel, "Txt_InvDetail");
            for (int i = 0; i < SlotCount; i++)
            {
                slotButtons[i] = FindButton(panel, "Btn_Slot_" + i.ToString("00"));
                slotTexts[i] = FindText(panel, "Txt_Slot_" + i.ToString("00"));
                if (slotButtons[i] != null)
                {
                    int idx = i;
                    slotButtons[i].onClick.RemoveAllListeners();
                    slotButtons[i].onClick.AddListener(() => OnSlotClick(idx));
                }
            }
            btnTakeOut = FindButton(panel, "Btn_InvTakeOut");
            btnPutIn = FindButton(panel, "Btn_InvPutIn");
            btnClose = FindButton(panel, "Btn_InvClose");
            if (btnTakeOut != null) btnTakeOut.onClick.RemoveAllListeners();
            if (btnTakeOut != null) btnTakeOut.onClick.AddListener(OnTakeOutClick);
            if (btnPutIn != null) btnPutIn.onClick.RemoveAllListeners();
            if (btnPutIn != null) btnPutIn.onClick.AddListener(OnPutInClick);
            if (btnClose != null) btnClose.onClick.RemoveAllListeners();
            if (btnClose != null) btnClose.onClick.AddListener(OnCloseClick);
            panel.gameObject.SetActive(false);
            return true;
        }

        public void Init(ToolBagSystem s)
        {
            system = s;
            if (!Bind()) return;
            selected = -1;
            Refresh();
        }

        void OnSlotClick(int i)
        {
            selected = selected == i ? -1 : i;
            Refresh();
        }

        void OnTakeOutClick()
        {
            if (system == null || selected < 0) return;
            string id = system.SlotId(selected);
            if (string.IsNullOrEmpty(id)) return;
            string reason;
            if (system.TakeOut(selected, 1, out reason)) { selected = -1; Refresh(); }
            else if (system != null) Notice(reason);
        }

        /// <summary>
        /// 从仓库放入格子：只读归属真值，不凭空造物。
        /// 若当前选中空格，则把首个候选定向放入该格；否则把仓库候选依次塞进可用空格。
        /// </summary>
        void OnPutInClick()
        {
            if (system == null) return;
            string[] candidates = system.StorageCandidates();
            if (candidates == null || candidates.Length == 0)
            {
                Notice("仓库里没有待放入的工具/技能书（去抽奖或交易获得）");
                return;
            }
            string reason;
            if (selected >= 0 && string.IsNullOrEmpty(system.SlotId(selected)))
            {
                // 选中了空格：定向放入候选中的第一件
                string id = candidates[0];
                if (system.PutFromStorage(selected, id, 1, out reason))
                {
                    selected = -1;
                    Refresh();
                    Notice("已放入格子：" + system.DisplayName(id));
                }
                else Notice(reason);
                return;
            }
            // 未选中：把候选依次装入背包（Add 会校验归属，不会超发）
            int put = 0;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (system.Add(candidates[i], 1, out reason)) put++;
            }
            Refresh();
            Notice(put > 0 ? "已整理入包 " + put + " 件" : "背包没有可用空格，请先【取出】腾位");
        }

        void OnCloseClick()
        {
            if (system != null) system.Close();
        }

        void Notice(string msg)
        {
            if (system == null) return;
            WhiteboxGame g = GetGame();
            if (g != null && g.Hud != null) g.Hud.Notice(msg);
        }

        WhiteboxGame GetGame()
        {
            Object[] all = FindObjectsOfType(typeof(WhiteboxGame));
            return all != null && all.Length > 0 ? all[0] as WhiteboxGame : null;
        }

        public void Show() { if (Bind()) { panel.gameObject.SetActive(true); selected = -1; Refresh(); } }

        public void Hide()
        {
            if (!Bind()) return;
            selected = -1;
            panel.gameObject.SetActive(false);
        }

        public void Refresh()
        {
            if (!Bind() || system == null) return;
            if (txtTitle != null) txtTitle.text = "工具与技能书背包";
            if (txtCapacity != null) txtCapacity.text = "格子 " + system.UsedSlots + " / " + system.Capacity + "　（I 关闭）";
            for (int i = 0; i < SlotCount; i++)
            {
                if (slotTexts[i] == null) continue;
                string id = system.SlotId(i);
                int count = system.SlotCount(i);
                if (string.IsNullOrEmpty(id))
                {
                    slotTexts[i].text = "空";
                    slotTexts[i].color = new Color(0.62f, 0.65f, 0.63f, 1f);
                }
                else
                {
                    string suffix = system.IsBook(id) ? " · 书 ×" + count : (count > 1 ? " ×" + count : "");
                    slotTexts[i].text = system.DisplayName(id) + suffix;
                    slotTexts[i].color = selected == i ? new Color(1f, 0.86f, 0.42f, 1f)
                                                       : new Color(0.94f, 0.95f, 0.92f, 1f);
                }
            }
            if (txtDetail != null)
            {
                int pending = system.StorageCandidates().Length;
                if (selected < 0 || string.IsNullOrEmpty(system.SlotId(selected)))
                    txtDetail.text = "仓库待放入 " + pending + " 件（去抽奖或交易获得）。\n【放入】把仓库物品装进空格；选中空格再点【放入】可定向放入。\n【取出】只放回仓库，不影响加成与金币。";
                else
                {
                    string id = system.SlotId(selected);
                    txtDetail.text = system.DisplayName(id) + "　持有 " + system.SlotCount(selected)
                                     + "\n格子 " + (selected + 1) + " / " + system.Capacity
                                     + "　（取出后可在仓库重新放入）";
                }
            }
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
