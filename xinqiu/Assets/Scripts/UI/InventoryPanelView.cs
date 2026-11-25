// @trace BAG-001, BAG-002, BAG-003, UI-004, UI-006
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

        [SerializeField] ToolIconSet iconSet;   // ⑲ 生成并接线；为空或查不到时自动退回纯文字
        public Sprite ItemIcon(string id) => iconSet != null ? iconSet.Get(id) : null;

        ToolBagSystem system;
        RectTransform panel;
        Text txtTitle;
        Text txtCapacity;
        Text txtDetail;
        Text[] slotTexts = new Text[SlotCount];
        Image[] slotIcons = new Image[SlotCount];
        Button[] slotButtons = new Button[SlotCount];
        Button btnTakeOut;
        Button btnPutIn;
        Button btnClose;
        Button btnEquip;    // 装备/卸下（工具）/ 拿手上（主工具/专精）
        Button btnUse;      // 使用技能书
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
                slotIcons[i] = FindImage(panel, "Img_Slot_" + i.ToString("00"));
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
            btnEquip = FindButton(panel, "Btn_InvEquip");
            btnUse = FindButton(panel, "Btn_InvUse");
            if (btnTakeOut != null) btnTakeOut.onClick.RemoveAllListeners();
            if (btnTakeOut != null) btnTakeOut.onClick.AddListener(OnTakeOutClick);
            if (btnPutIn != null) btnPutIn.onClick.RemoveAllListeners();
            if (btnPutIn != null) btnPutIn.onClick.AddListener(OnPutInClick);
            if (btnClose != null) btnClose.onClick.RemoveAllListeners();
            if (btnClose != null) btnClose.onClick.AddListener(OnCloseClick);
            if (btnEquip != null) btnEquip.onClick.RemoveAllListeners();
            if (btnEquip != null) btnEquip.onClick.AddListener(OnEquipClick);
            if (btnUse != null) btnUse.onClick.RemoveAllListeners();
            if (btnUse != null) btnUse.onClick.AddListener(OnUseClick);
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
                Notice("仓库里没有待放入的技能书（按 B 用金币购买）");
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

        /// <summary>
        /// 选中格子点【装备/卸下】：辅助工具切装备状态并拿手上；主工具/专精无 rewardId，只切手上显示。
        /// </summary>
        void OnEquipClick()
        {
            WhiteboxGame g = GetGame();
            if (g == null || g.Tool == null || system == null || selected < 0) return;
            string id = system.SlotId(selected);
            if (string.IsNullOrEmpty(id)) { Notice("先选中一件工具"); return; }

            ToolConfig cfg = g.Tool.Config;
            ToolDef byReward = cfg != null ? cfg.FindByReward(id) : null;
            ToolDef byToolId = cfg != null ? cfg.Find(id) : null;

            if (system.IsBook(id))
            {
                // 技能书也可以拿在手上看（纯视觉）；层数/效果仍由【使用书】决定
                if (g.Tool.IsHeld(id)) { g.Tool.ClearHeldTool(); Notice("已放下：" + system.DisplayName(id)); }
                else if (g.Tool.SetHeldTool(id)) Notice("已拿起：" + system.DisplayName(id));
                else Notice("先抽到这本书才能拿手上");
                Refresh();
                return;
            }

            if (byReward == null && byToolId == null) { Notice("该物品不可装备"); return; }

            // 辅助工具（有 rewardId）：切装备 + 拿手上
            if (byReward != null)
            {
                if (g.State.IsToolEquipped(id))
                {
                    g.Tool.UnequipTool(id);
                    Notice("已卸下：" + system.DisplayName(id));
                }
                else
                {
                    g.Tool.EquipTool(id);
                    g.Tool.SetHeldTool(byReward.toolId);
                    Notice("已装备并拿起：" + system.DisplayName(id));
                }
            }
            else
            {
                // 主工具/专精（T-01~T-05）：仅切手上显示
                g.Tool.SetHeldTool(byToolId.toolId);
                Notice("已拿起：" + byToolId.displayName);
            }
            Refresh();
        }

        /// <summary>选中格子点【使用书】：书消耗、效果按层数叠加（ToolSystem.UseBook）。</summary>
        void OnUseClick()
        {
            WhiteboxGame g = GetGame();
            if (g == null || g.Tool == null || system == null || selected < 0) return;
            string id = system.SlotId(selected);
            if (string.IsNullOrEmpty(id)) { Notice("先选中一本技能书"); return; }
            if (!system.IsBook(id)) { Notice("只有技能书能使用"); return; }
            ToolGrantResult r = g.Tool.UseBook(id);
            Notice(r.ok ? r.message : (string.IsNullOrEmpty(r.message) ? "使用失败" : r.message));
            Refresh();
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
            if (txtTitle != null) txtTitle.text = "技能书背包";
            if (txtCapacity != null) txtCapacity.text = "格子 " + system.UsedSlots + " / " + system.Capacity + "　（装技能书 · 金币按 B 买书 · I 关闭）";
            WhiteboxGame game = GetGame();
            for (int i = 0; i < SlotCount; i++)
            {
                if (slotTexts[i] == null && slotIcons[i] == null) continue;
                string id = system.SlotId(i);
                int count = system.SlotCount(i);

                // 图标：查不到就关掉 Image，退回纯文字（不影响任何逻辑）
                Sprite sp = (!string.IsNullOrEmpty(id) && iconSet != null) ? iconSet.Get(id) : null;
                if (slotIcons[i] != null)
                {
                    slotIcons[i].sprite = sp;
                    slotIcons[i].enabled = sp != null;
                }
                if (slotTexts[i] != null) slotTexts[i].fontSize = sp != null ? 13 : 14;

                if (string.IsNullOrEmpty(id))
                {
                    if (slotTexts[i] != null)
                    {
                        slotTexts[i].text = "空";
                        slotTexts[i].color = new Color(0.62f, 0.65f, 0.63f, 1f);
                    }
                    continue;
                }

                bool isBook = system.IsBook(id);
                int used = isBook && game != null && game.Tool != null && game.State != null ? game.Tool.BookStacks(id) : 0;
                bool equipped = !isBook && game != null && game.State != null && game.State.IsToolEquipped(id);
                bool held = !isBook && game != null && game.Tool != null && game.Tool.IsHeld(id);

                string suffix;
                if (isBook) suffix = "书 ×" + count + " · 生效 " + used;
                else
                {
                    suffix = equipped ? "已装备" : "未装备";
                    if (held) suffix += " · 手持";
                    if (count > 1) suffix += " ×" + count;
                }

                if (slotTexts[i] != null)
                {
                    slotTexts[i].text = sp != null
                        ? system.DisplayName(id) + "\n" + suffix
                        : system.DisplayName(id) + " · " + suffix;
                    if (selected == i) slotTexts[i].color = new Color(1f, 0.86f, 0.42f, 1f);
                    else if (held) slotTexts[i].color = new Color(0.62f, 0.90f, 0.68f, 1f);
                    else if (equipped || isBook) slotTexts[i].color = new Color(0.90f, 0.92f, 0.72f, 1f);
                    else slotTexts[i].color = new Color(0.72f, 0.76f, 0.72f, 1f);
                }
            }
            if (txtDetail != null)
            {
                int pending = system.StorageCandidates().Length;
                if (selected < 0 || string.IsNullOrEmpty(system.SlotId(selected)))
                    txtDetail.text = "仓库待放入 " + pending + " 件（按 B 用金币买技能书）。\n【放入】装进空格　【取出】放回仓库。\n【使用书】消耗书叠效果（加工/制造/零耗时）。\n人物等级在中转站第 5 页升级，接管拾取与效率加成。";
                else
                {
                    string id = system.SlotId(selected);
                    WhiteboxGame g = GetGame();
                    string extra = "";
                    if (system.IsBook(id))
                    {
                        int used = g != null && g.Tool != null && g.State != null ? g.Tool.BookStacks(id) : 0;
                        extra = "\n技能书：生效 " + used + " 层；点【使用书】消耗 1 本叠 1 层";
                    }
                    else if (g != null && g.State != null)
                    {
                        extra = g.State.IsToolEquipped(id) ? "\n已装备（生效中）；点【装备/卸下】卸下" : "\n未装备（不生效）；点【装备/卸下】装备并拿起";
                    }
                    txtDetail.text = system.DisplayName(id) + "　持有 " + system.SlotCount(selected)
                                     + extra
                                     + "\n格子 " + (selected + 1) + " / " + system.Capacity;
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
