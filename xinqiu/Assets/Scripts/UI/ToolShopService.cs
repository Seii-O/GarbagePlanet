// @trace TOOL-001, NUM-017
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 金币商城的预留实现（C22：本轮不上线）。没有调用方、没有入口按钮，
    /// 即使误调用也会因 enabled=false 与余额恒 0 被拒绝。
    /// 金币上线时只需把 WalletStub 换成真实钱包，IToolShop 的调用顺序无需改动。
    /// </summary>
    public sealed class ToolShopService : IToolShop
    {
        readonly ToolConfig config;
        readonly WhiteboxState state;
        readonly IToolGrantSink grants;
        readonly ICurrencyWallet wallet;

        public ToolShopService(ToolConfig cfg, WhiteboxState s, IToolGrantSink grantSink, ICurrencyWallet currencyWallet = null)
        {
            config = cfg;
            state = s;
            grants = grantSink;
            wallet = currencyWallet != null ? currencyWallet : new WalletStub(cfg, s);
        }

        public bool IsAvailable(string toolId)
        {
            ToolDef def = config != null ? config.Find(toolId) : null;
            return def != null && def.purchase != null && def.purchase.enabled;
        }

        public bool CanPurchase(string toolId, out string reason)
        {
            reason = "";
            ToolDef def = config != null ? config.Find(toolId) : null;
            if (def == null) { reason = "未知工具"; return false; }
            ToolPurchase p = def.purchase;
            if (p == null || !p.enabled) { reason = "未开放购买"; return false; }
            if (state == null || grants == null) { reason = "状态未初始化"; return false; }
            if (p.minQuestStep > 0 && state.questStep < p.minQuestStep)
            {
                reason = "任务进度不足";
                return false;
            }
            if (string.IsNullOrEmpty(def.rewardId))
            {
                reason = "该工具不通过商店发放";
                return false;
            }
            int bought = state.ToolBoughtCount(toolId);
            if (p.limit == PurchaseLimit.Once && bought >= 1) { reason = "已购买"; return false; }
            if (p.limit == PurchaseLimit.Total && bought >= Mathf.Max(1, p.limitValue)) { reason = "已达总限购"; return false; }
            if (p.limit == PurchaseLimit.Daily)
            {
                reason = "每日限购待接入日历";
                return false;
            }
            if (!wallet.CanAfford(p.currency, p.price)) { reason = "余额不足"; return false; }
            return true;
        }

        public int PriceOf(string toolId)
        {
            ToolDef def = config != null ? config.Find(toolId) : null;
            return def != null && def.purchase != null ? Mathf.Max(0, def.purchase.price) : 0;
        }

        public int RemainingLimit(string toolId)
        {
            ToolDef def = config != null ? config.Find(toolId) : null;
            if (def == null || def.purchase == null || !def.purchase.enabled || state == null) return 0;
            ToolPurchase p = def.purchase;
            int bought = state.ToolBoughtCount(toolId);
            if (p.limit == PurchaseLimit.Once) return Mathf.Max(0, 1 - bought);
            if (p.limit == PurchaseLimit.Total) return Mathf.Max(0, Mathf.Max(1, p.limitValue) - bought);
            if (p.limit == PurchaseLimit.Daily) return 0; // 未接入日历前不开放
            return int.MaxValue;
        }

        /// <summary>购买顺序：可用性 → 限购/余额 → 扣款 → 统一发放 → 写购买记录 → 存档。</summary>
        public ToolPurchaseResult Purchase(string toolId)
        {
            ToolPurchaseResult result = new ToolPurchaseResult
            {
                ok = false, toolId = toolId, reason = "", price = PriceOf(toolId),
                grant = new ToolGrantResult()
            };
            string reason;
            if (!CanPurchase(toolId, out reason)) { result.reason = reason; return result; }
            ToolDef def = config.Find(toolId);
            ToolPurchase p = def.purchase;
            if (!wallet.Spend(p.currency, p.price)) { result.reason = "扣款失败"; return result; }
            result.grant = grants.Grant(def.rewardId, GrantSource.Shop);
            if (!result.grant.ok) { result.reason = "发放失败"; return result; }
            state.AddToolBought(toolId);
            WhiteboxState.Save(state);
            result.ok = true;
            return result;
        }
    }

    /// <summary>默认钱包：金币无产出恒 0；转盘券与材料只做接口自洽，本轮无商店入口。</summary>
    public sealed class WalletStub : ICurrencyWallet
    {
        readonly ToolConfig config;
        readonly WhiteboxState state;

        public WalletStub(ToolConfig cfg, WhiteboxState s) { config = cfg; state = s; }

        public int Balance(ToolCurrency c)
        {
            if (state == null) return 0;
            if (c == ToolCurrency.Coins) return 0; // C22：本轮金币系统不上线
            if (c == ToolCurrency.Tickets) return Mathf.Max(0, state.gachaTickets);
            int sum = 0;
            if (state.materials != null)
                for (int i = 0; i < state.materials.Length; i++) sum += Mathf.Max(0, state.materials[i]);
            return sum;
        }

        public bool CanAfford(ToolCurrency c, int n)
        {
            return n <= 0 || Balance(c) >= n;
        }

        public bool Spend(ToolCurrency c, int n)
        {
            if (!CanAfford(c, n) || state == null || n <= 0) return false;
            if (c == ToolCurrency.Coins) return false; // 无产出即永不扣款
            if (c == ToolCurrency.Tickets)
            {
                if (state.gachaTickets < n) return false;
                state.gachaTickets -= n;
                return true;
            }
            int left = n;
            for (int i = 0; i < 4 && left > 0; i++)
            {
                int take = Mathf.Min(left, Mathf.Max(0, state.materials[i]));
                state.materials[i] -= take;
                left -= take;
            }
            return left == 0;
        }
    }
}
