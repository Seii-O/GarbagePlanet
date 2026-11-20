// @trace BAG-001, BAG-002, NUM-018
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 背包格子对外的最小接口：金币交易系统只认这个接口，不碰格子数组、不碰 ToolSystem 加成。
    /// 这样「背包管理」与「金币交易」两条链路互不混用。
    /// </summary>
    public interface IToolBag
    {
        int Capacity { get; }
        int UsedSlots { get; }
        bool Add(string itemId, int n, out string reason);
        bool RemoveFirst(string itemId, int n, out string reason);
        int CountOf(string itemId);
        string DisplayName(string itemId);
    }

    /// <summary>
    /// 玩家工具与技能书背包（与垃圾背包 bag[4] 完全独立）。
    ///
    /// 单一真值：物品归属仍以 WhiteboxState.ownedRewardIds/Counts 为准（抽奖、图鉴、加成继续读它）；
    /// 本系统只管理「携带格子」——把已归入库的工具/书放进格子，或从格子取出。
    /// 因此本系统不会改变任何既有加成公式，旧档 Load 后格子为空但效果照旧，I 键打开即可整理。
    /// </summary>
    public sealed class ToolBagSystem : MonoBehaviour, IToolBag
    {
        [SerializeField] ToolBagConfig config;
        [SerializeField] InventoryPanelView panel;

        WhiteboxGame game;
        ToolConfig toolConfig;

        public ToolBagConfig Config => config;
        public bool IsOpen => panel != null && panel.IsOpen;
        public int Capacity => config != null ? config.SlotCount : 12;
        public int UsedSlots
        {
            get
            {
                WhiteboxState s = GameState;
                if (s == null || s.bagSlotIds == null) return 0;
                int n = 0;
                for (int i = 0; i < s.bagSlotIds.Length; i++)
                    if (!string.IsNullOrEmpty(s.bagSlotIds[i])) n++;
                return n;
            }
        }

        public void Init(WhiteboxGame g)
        {
            game = g;
#if UNITY_EDITOR
            if (config == null)
                config = UnityEditor.AssetDatabase.LoadAssetAtPath<ToolBagConfig>("Assets/Data/ToolBagConfig.asset");
#endif
            if (config == null) config = ToolBagConfig.CreateDefault();
            toolConfig = g != null && g.Tool != null ? g.Tool.Config : null;
            if (toolConfig == null) toolConfig = ToolConfig.CreateDefault();
            EnsureSlots(GameState);
            if (panel != null) panel.Init(this);
            Debug.Log("[ToolBag] 初始化完成：格子=" + Capacity
                      + "，已用=" + UsedSlots + "，工具/书定义=" + toolConfig.Tools.Length + "/" + toolConfig.Books.Length);
        }

        /// <summary>供 Editor 构建脚本注入配置。</summary>
        public void Wire(ToolBagConfig cfg) { config = cfg; }

        /// <summary>供 Editor 构建脚本注入界面视图。</summary>
        public void WireView(InventoryPanelView v) { panel = v; }

        public void Toggle() { if (IsOpen) Close(); else Open(); }

        public void Open()
        {
            if (panel == null || panel.IsOpen) return;
            panel.Show();
            if (game != null) game.ApplyUiState();
        }

        public void Close()
        {
            if (panel == null || !panel.IsOpen) return;
            panel.Hide();
            if (game != null) game.ApplyUiState();
        }

        public void RefreshPanel() { if (panel != null) panel.Refresh(); }

        void EnsureSlots(WhiteboxState s)
        {
            if (s == null) return;
            int cap = Capacity;
            if (s.bagSlotIds == null) s.bagSlotIds = new string[0];
            if (s.bagSlotCounts == null) s.bagSlotCounts = new int[0];
            if (s.bagSlotIds.Length != cap) System.Array.Resize(ref s.bagSlotIds, cap);
            if (s.bagSlotCounts.Length != cap) System.Array.Resize(ref s.bagSlotCounts, cap);
            for (int i = 0; i < cap; i++)
            {
                if (string.IsNullOrEmpty(s.bagSlotIds[i])) { s.bagSlotIds[i] = ""; s.bagSlotCounts[i] = 0; }
                s.bagSlotCounts[i] = Mathf.Max(0, s.bagSlotCounts[i]);
                if (s.bagSlotCounts[i] == 0) s.bagSlotIds[i] = "";
            }
        }

        // ----------------------------------------------------------------
        // 发放桥接：抽奖/购买成功后把物品自动放进一个空格；满格只保留图鉴归属
        // ----------------------------------------------------------------
        public void OnRewardGranted(string itemId, int n = 1)
        {
            if (string.IsNullOrEmpty(itemId) || n <= 0) return;
            WhiteboxState s = GameState;
            if (s == null) return;
            EnsureSlots(s);
            int cap = StackCap(itemId);
            int left = n;
            for (int i = 0; i < s.bagSlotIds.Length && left > 0; i++)
            {
                if (s.bagSlotIds[i] == itemId)
                {
                    int room = cap - s.bagSlotCounts[i];
                    int put = Mathf.Min(room, left);
                    if (put <= 0) continue;
                    s.bagSlotCounts[i] += put;
                    left -= put;
                }
            }
            for (int i = 0; i < s.bagSlotIds.Length && left > 0; i++)
            {
                if (!string.IsNullOrEmpty(s.bagSlotIds[i])) continue;
                int put = Mathf.Min(cap, left);
                s.bagSlotIds[i] = itemId;
                s.bagSlotCounts[i] = put;
                left -= put;
            }
            if (left > 0 && game != null && game.Hud != null)
                game.Hud.Notice("背包已满：" + DisplayName(itemId) + " ×" + left + " 仅计入图鉴，可在 I 背包整理");
        }

        // ----------------------------------------------------------------
        // IToolBag：交易系统唯一可调用的入口
        // ----------------------------------------------------------------
        public bool Add(string itemId, int n, out string reason)
        {
            reason = "";
            if (string.IsNullOrEmpty(itemId) || n <= 0) { reason = "物品无效"; return false; }
            WhiteboxState s = GameState;
            if (s == null) { reason = "状态未初始化"; return false; }
            if (!IsBagItem(itemId)) { reason = "该物品不属于工具/技能书"; return false; }
            if (CountOf(itemId) + n > Mathf.Max(0, s.RewardCount(itemId)))
            { reason = "没有可放入的库存"; return false; }
            EnsureSlots(s);
            int cap = StackCap(itemId);
            int left = n;
            for (int i = 0; i < s.bagSlotIds.Length && left > 0; i++)
            {
                if (s.bagSlotIds[i] != itemId) continue;
                int put = Mathf.Min(cap - s.bagSlotCounts[i], left);
                s.bagSlotCounts[i] += put; left -= put;
            }
            for (int i = 0; i < s.bagSlotIds.Length && left > 0; i++)
            {
                if (!string.IsNullOrEmpty(s.bagSlotIds[i])) continue;
                int put = Mathf.Min(cap, left);
                s.bagSlotIds[i] = itemId; s.bagSlotCounts[i] = put; left -= put;
            }
            if (left > 0) { reason = "背包格子不足"; return false; }
            WhiteboxState.Save(s);
            return true;
        }

        public bool RemoveFirst(string itemId, int n, out string reason)
        {
            reason = "";
            if (string.IsNullOrEmpty(itemId) || n <= 0) { reason = "物品无效"; return false; }
            WhiteboxState s = GameState;
            if (s == null) { reason = "状态未初始化"; return false; }
            EnsureSlots(s);
            int left = n;
            for (int i = 0; i < s.bagSlotIds.Length && left > 0; i++)
            {
                if (s.bagSlotIds[i] != itemId) continue;
                int take = Mathf.Min(s.bagSlotCounts[i], left);
                s.bagSlotCounts[i] -= take; left -= take;
                if (s.bagSlotCounts[i] <= 0) { s.bagSlotCounts[i] = 0; s.bagSlotIds[i] = ""; }
            }
            if (left > 0) { reason = "背包里没有足够数量"; return false; }
            WhiteboxState.Save(s);
            return true;
        }

        public int CountOf(string itemId)
        {
            WhiteboxState s = GameState;
            if (s == null || string.IsNullOrEmpty(itemId) || s.bagSlotIds == null) return 0;
            int total = 0;
            for (int i = 0; i < s.bagSlotIds.Length; i++)
                if (s.bagSlotIds[i] == itemId) total += Mathf.Max(0, s.bagSlotCounts[i]);
            return total;
        }

        public string DisplayName(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return "";
            SkillBookDef b = toolConfig != null ? toolConfig.FindBook(itemId) : null;
            if (b != null) return b.displayName;
            ToolDef t = toolConfig != null ? toolConfig.FindByReward(itemId) : null;
            if (t != null) return t.displayName;
            ToolDef byId = toolConfig != null ? toolConfig.Find(itemId) : null;
            if (byId != null) return byId.displayName;
            // 兜底：普通小物件直接读 GachaConfig 的默认名（只做显示，不影响判定）
            GachaConfig gc = game != null && game.Gacha != null ? game.Gacha.Config : null;
            if (gc != null && gc.Rewards != null)
                for (int i = 0; i < gc.Rewards.Length; i++)
                    if (gc.Rewards[i] != null && gc.Rewards[i].id == itemId) return gc.Rewards[i].displayName;
            return itemId;
        }

        // ----------------------------------------------------------------
        // 背包自身的格子管理（I 键界面调用，不与金币交易共用）
        // ----------------------------------------------------------------
        public string SlotId(int i)
        {
            WhiteboxState s = GameState;
            if (s == null || s.bagSlotIds == null || i < 0 || i >= s.bagSlotIds.Length) return "";
            return s.bagSlotIds[i] ?? "";
        }

        public int SlotCount(int i)
        {
            WhiteboxState s = GameState;
            if (s == null || s.bagSlotCounts == null || i < 0 || i >= s.bagSlotCounts.Length) return 0;
            return Mathf.Max(0, s.bagSlotCounts[i]);
        }

        public bool IsBook(string itemId)
        {
            return toolConfig != null && toolConfig.FindBook(itemId) != null;
        }

        public bool IsBagItem(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return false;
            if (toolConfig == null) return false;
            return toolConfig.FindBook(itemId) != null || toolConfig.FindByReward(itemId) != null;
        }

        int StackCap(string itemId)
        {
            if (config != null && config.StackBooks)
            {
                SkillBookDef b = toolConfig != null ? toolConfig.FindBook(itemId) : null;
                if (b != null) return Mathf.Max(1, b.maxStacks);
            }
            return config != null ? config.ToolStackLimit : 1;
        }

        /// <summary>从仓库（图鉴归属）放入指定格子：只改格子，不增归属，避免凭空造物。</summary>
        public bool PutFromStorage(int slot, string itemId, int n, out string reason)
        {
            reason = "";
            WhiteboxState s = GameState;
            if (s == null || slot < 0 || slot >= Capacity) { reason = "格子无效"; return false; }
            EnsureSlots(s);
            if (!IsBagItem(itemId)) { reason = "不是工具或技能书"; return false; }
            int cap = StackCap(itemId);
            if (s.bagSlotIds[slot] != "" && s.bagSlotIds[slot] != itemId) { reason = "该格已有其他物品"; return false; }
            int room = cap - (s.bagSlotIds[slot] == itemId ? s.bagSlotCounts[slot] : 0);
            int put = Mathf.Min(room, n);
            if (put <= 0) { reason = "该格已满"; return false; }
            if (CountOf(itemId) + put > s.RewardCount(itemId)) { reason = "仓库没有这么多"; return false; }
            s.bagSlotIds[slot] = itemId;
            s.bagSlotCounts[slot] += put;
            WhiteboxState.Save(s);
            return true;
        }

        /// <summary>把格子里的物品退回仓库：只清格子，不减归属，加成不受影响。</summary>
        public bool TakeOut(int slot, int n, out string reason)
        {
            reason = "";
            WhiteboxState s = GameState;
            if (s == null || slot < 0 || slot >= Capacity) { reason = "格子无效"; return false; }
            EnsureSlots(s);
            string id = s.bagSlotIds[slot];
            if (string.IsNullOrEmpty(id)) { reason = "空格子"; return false; }
            int take = Mathf.Min(s.bagSlotCounts[slot], Mathf.Max(1, n));
            s.bagSlotCounts[slot] -= take;
            if (s.bagSlotCounts[slot] <= 0) { s.bagSlotCounts[slot] = 0; s.bagSlotIds[slot] = ""; }
            WhiteboxState.Save(s);
            return true;
        }

        /// <summary>列出可整理清单：归属数 > 背包数的工具/书（在仓库里等玩家放格）。</summary>
        public string[] StorageCandidates()
        {
            WhiteboxState s = GameState;
            if (s == null || toolConfig == null) return new string[0];
            System.Collections.Generic.List<string> list = new System.Collections.Generic.List<string>();
            System.Collections.Generic.HashSet<string> seen = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < s.ownedRewardIds.Length; i++)
            {
                string id = s.ownedRewardIds[i];
                if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;
                if (!IsBagItem(id)) continue;
                if (s.ownedRewardCounts[i] <= CountOf(id)) continue;
                list.Add(id);
            }
            return list.ToArray();
        }

        WhiteboxState GameState => game != null ? game.State : null;
    }
}
