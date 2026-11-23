// @trace BAG-003, UI-006
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 独立垃圾背包系统（G 键）：只管玩家捡到的垃圾/废弃物，是真值 WhiteboxState.bag[4] + bagItems[8] 的唯一写入口。
    ///
    /// 边界（BAG-002 / UI-006）：
    /// - 不碰工具/技能书背包（ToolBagSystem / I 键）；
    /// - 不碰金币余额与交易（CoinTradeSystem / B 键）；
    /// - 不碰中转站加工制造（StationSystem / M 键）——投放仍由中转站的「投放」按钮发起，本系统只负责查看/整理/丢弃。
    ///
    /// 显隐与互斥由 WhiteboxGame 统一仲裁；本系统只提供 Open/Close/IsOpen，并在变更后 Refresh 面板。
    /// 丢弃不改变 collected / 任务进度：丢掉的垃圾可以重新捡回，累计统计口径保持不变。
    /// </summary>
    public sealed class GarbageBagSystem : MonoBehaviour
    {
        [SerializeField] GarbageBagPanelView panel;

        WhiteboxGame game;

        public bool IsOpen => panel != null && panel.IsOpen;
        public WhiteboxState State => game != null ? game.State : null;

        public void Init(WhiteboxGame g)
        {
            game = g;
            if (panel == null) panel = FindObjectOfType<GarbageBagPanelView>(true);
            if (State != null)
            {
                State.NormalizeGarbageBagItems();
                WhiteboxState.Save(State);
            }
            if (panel != null) panel.Init(this);
            Debug.Log("[GarbageBag] 初始化完成：单品=" + GarbageBagCatalog.ItemCount
                      + "，当前持有=" + Total + "/" + Capacity);
        }

        /// <summary>供 Editor 构建脚本注入界面视图。</summary>
        public void WireView(GarbageBagPanelView v) { panel = v; }

        public void Toggle() { if (IsOpen) Close(); else Open(); }

        public void Open()
        {
            if (panel == null || panel.IsOpen) return;
            if (State != null) State.NormalizeGarbageBagItems();
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

        /// <summary>面板内的提示统一走 HUD，避免视图层到处 FindObjectOfType。</summary>
        public void Notice(string msg) { if (game != null && !string.IsNullOrEmpty(msg)) game.Notice(msg); }

        // ---------------- 只读查询（视图直接调用） ----------------

        public int ItemCount(int itemIndex) => State != null ? State.GarbageItemCount(itemIndex) : 0;

        public int CategoryCount(int cat)
        {
            if (State == null || State.bag == null || cat < 0 || cat > 3) return 0;
            return Mathf.Max(0, State.bag[cat]);
        }

        public int CategoryCap(int cat)
        {
            if (game == null || game.Config == null || State == null) return 0;
            int[] caps = game.Config.BackpackCapAt(State.bagLevel);
            return caps != null && cat >= 0 && cat < caps.Length ? Mathf.Max(0, caps[cat]) : 0;
        }

        public int Total => State != null ? State.BagTotal() : 0;

        public int Capacity => game != null && game.Config != null && State != null
            ? State.BagCapTotal(game.Config) : 0;

        // ---------------- 交互 ----------------

        /// <summary>丢弃 n 件指定单品；成功后立即落盘，保证关面板/退出后不会回滚。</summary>
        public bool Discard(int itemIndex, int n, out string reason)
        {
            reason = "";
            WhiteboxState s = State;
            if (s == null) { reason = "状态未初始化"; return false; }
            if (!s.DiscardGarbage(itemIndex, n, out reason)) return false;
            WhiteboxState.Save(s);
            RefreshPanel();
            if (game != null) game.Notice("已丢弃 " + GarbageBagCatalog.NameOf(itemIndex) + " ×" + n);
            return true;
        }

        /// <summary>整理：把单品明细校准到背包总量；旧档/调试档可能在这里一次性补齐。</summary>
        public int Tidy(out string reason)
        {
            reason = "";
            WhiteboxState s = State;
            if (s == null) { reason = "状态未初始化"; return 0; }
            int changed = s.TidyGarbageBag();
            if (changed > 0) WhiteboxState.Save(s);
            RefreshPanel();
            return changed;
        }
    }
}
