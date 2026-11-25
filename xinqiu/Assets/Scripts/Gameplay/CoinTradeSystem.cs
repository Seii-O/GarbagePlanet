// @trace COIN-001, COIN-002, TOOL-001, NUM-017, NUM-018
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 一次交易的结果（购买 / 出售 / 以物易物共用）。
    /// </summary>
    public struct CoinTradeResult
    {
        public bool ok;
        public int kind;          // 0=购买 1=出售 2=以物易物
        public string itemId;     // 得到/失去的物品
        public string otherId;    // 以物易物时的另一方
        public int price;         // 成交单价
        public int amount;        // 成交数量
        public int balanceAfter;  // 成交后余额
        public string message;
    }

    /// <summary>
    /// 独立金币交易系统：金币余额、购买、出售、交易记录。
    ///
    /// 与背包的边界（COIN-002）：
    /// - 只通过 IToolBag.Add / RemoveFirst / CountOf 与背包交互，不读格子数组、不改格子结构；
    /// - 归属真值仍在 WhiteboxState.ownedRewardIds/Counts，出售必须 ConsumeReward 扣归属，
    ///   否则「卖掉→重新放回格子→再卖」可无限刷金币；
    /// - 购买复用 ToolSystem.Grant（与抽奖同一发放入口），技能书仍只走抽奖（C20）不开放购买。
    ///
    /// 金币唯一来源是出售，不新增产出渠道（C22 的「金币不上线」在本需求中改为「仅交易上线」）。
    /// </summary>
    public sealed class CoinTradeSystem : MonoBehaviour
    {
        [SerializeField] CoinTradeConfig config;
        [SerializeField] CoinTradePanelView panel;

        WhiteboxGame game;
        ToolBagSystem bag;

        public CoinTradeConfig Config => config;
        public bool IsOpen => panel != null && panel.IsOpen;
        public int Balance => game != null && game.State != null ? Mathf.Max(0, game.State.coins) : 0;
        /// <summary>只用于界面显示物品名；不暴露格子数组，不参与格子管理。</summary>
        public IToolBag Bag => bag;

        public void Init(WhiteboxGame g)
        {
            game = g;
#if UNITY_EDITOR
            if (config == null)
                config = UnityEditor.AssetDatabase.LoadAssetAtPath<CoinTradeConfig>("Assets/Data/CoinTradeConfig.asset");
#endif
            if (config == null) config = CoinTradeConfig.CreateDefault();
            if (g != null) bag = g.Bag;
            WhiteboxState s = GameState;
            if (s != null && s.coins < config.StartingCoins) s.coins = config.StartingCoins;
            if (panel != null) panel.Init(this);
            Debug.Log("[CoinTrade] 初始化完成：余额=" + Balance + "，出售价系数=" + config.SellRatio
                      + "，手续费=" + config.TradeFee + "，背包已接=" + (bag != null));
        }

        public void Wire(CoinTradeConfig cfg) { config = cfg; }

        /// <summary>供 Builder 注入背包引用（IToolBag 的实体）。</summary>
        public void WireBag(ToolBagSystem b) { bag = b; }

        /// <summary>供 Editor 构建脚本注入界面视图（与背包视图完全独立）。</summary>
        public void WireView(CoinTradePanelView v) { panel = v; }

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

        // ----------------------------------------------------------------
        // 价格：购买价取工具 purchase.price；出售价按稀有度基准 × sellRatio
        // ----------------------------------------------------------------
        public int BuyPrice(string itemId)
        {
            ToolConfig tc = ToolConfigRef;
            if (tc == null) return 0;
            // C27：技能书可用金币购买（price>0）
            SkillBookDef book = tc.FindBook(itemId);
            if (book != null) return book.Price;
            ToolDef def = tc.FindByReward(itemId);
            if (def == null || def.purchase == null) return 0;
            return Mathf.Max(0, def.purchase.price);
        }

        public int SellPrice(string itemId)
        {
            ToolConfig tc = ToolConfigRef;
            if (tc == null || config == null) return 0;
            // C27：加工产物的出售单价直接取配置（不走稀有度基准 × sellRatio）
            int matCat;
            if (IsMaterialItem(itemId, out matCat)) return config.MaterialSellPrice(matCat);
            SkillBookDef book = tc.FindBook(itemId);
            if (book != null) return Mathf.Max(1, Mathf.RoundToInt(config.BasePrice(book.rarity, tc) * config.SellRatio));
            ToolDef tool = tc.FindByReward(itemId);
            if (tool != null)
            {
                int basePrice = tool.purchase != null && tool.purchase.price > 0
                    ? tool.purchase.price : config.BasePrice(tool.rarity, tc);
                return Mathf.Max(1, Mathf.RoundToInt(basePrice * config.SellRatio));
            }
            // 普通小物件：从抽奖配置兜底稀有度与基准价
            GachaConfig gc = game != null && game.Gacha != null ? game.Gacha.Config : null;
            if (gc != null && gc.Rewards != null)
                for (int i = 0; i < gc.Rewards.Length; i++)
                    if (gc.Rewards[i] != null && gc.Rewards[i].id == itemId)
                        return Mathf.Max(1, Mathf.RoundToInt(config.BasePrice(gc.Rewards[i].rarity, tc) * config.SellRatio));
            return 0;
        }

        /// <summary>可购买清单：price>0 的技能书 + 工具（C27：技能书进商城；工具随下线不再出现）。</summary>
        public string[] BuyCatalog()
        {
            ToolConfig tc = ToolConfigRef;
            if (tc == null) return new string[0];
            System.Collections.Generic.List<string> list = new System.Collections.Generic.List<string>();
            SkillBookDef[] books = tc.Books;
            for (int i = 0; i < books.Length; i++)
            {
                SkillBookDef b = books[i];
                if (b == null || string.IsNullOrEmpty(b.rewardId) || b.Price <= 0) continue;
                if (!list.Contains(b.rewardId)) list.Add(b.rewardId);
            }
            ToolDef[] all = tc.Tools;
            for (int i = 0; i < all.Length; i++)
            {
                ToolDef t = all[i];
                if (t == null || string.IsNullOrEmpty(t.rewardId)) continue;
                if (t.purchase != null && t.purchase.enabled && t.purchase.price > 0 && !list.Contains(t.rewardId))
                    list.Add(t.rewardId);
            }
            return list.ToArray();
        }

        const string MaterialItemPrefix = "mat_"; // C27 材料出售伪 ID：mat_0..3，不进归属系统

        static bool IsMaterialItem(string id, out int cat)
        {
            cat = -1;
            if (string.IsNullOrEmpty(id) || !id.StartsWith(MaterialItemPrefix)) return false;
            return int.TryParse(id.Substring(MaterialItemPrefix.Length), out cat) && cat >= 0 && cat <= 3;
        }

        /// <summary>可出售清单：四种加工产物（存量>0）+ 图鉴里有归属的物品；技能书不可卖。</summary>
        public string[] SellCatalog()
        {
            WhiteboxState s = GameState;
            ToolConfig tc = ToolConfigRef;
            if (s == null || tc == null) return new string[0];
            System.Collections.Generic.List<string> list = new System.Collections.Generic.List<string>();
            // C27：加工产物换金币（放在最前，玩家最先看到）
            if (s.materials != null)
                for (int c = 0; c < 4; c++)
                    if (s.materials[c] > 0) list.Add(MaterialItemPrefix + c);
            if (s.ownedRewardIds == null) return list.ToArray();
            for (int i = 0; i < s.ownedRewardIds.Length; i++)
            {
                string id = s.ownedRewardIds[i];
                if (string.IsNullOrEmpty(id) || s.ownedRewardCounts[i] <= 0) continue;
                if (tc.FindBook(id) != null) continue; // 技能书不可出售
                if (!list.Contains(id)) list.Add(id);
            }
            return list.ToArray();
        }

        // ----------------------------------------------------------------
        // 购买：扣金币 → 统一发放 → 入背包 → 写流水（复用 ToolSystem.Grant，不改加成）
        // ----------------------------------------------------------------
        public CoinTradeResult Buy(string itemId)
        {
            CoinTradeResult r = Fail(0, itemId, "");
            if (!config.Enabled) { r.message = "金币交易未开启"; return r; }
            int price = BuyPrice(itemId);
            if (price <= 0) { r.message = "该物品不开放购买"; return r; }
            WhiteboxState s = GameState;
            if (s == null) { r.message = "状态未初始化"; return r; }
            if (s.coins < price) { r.message = "金币不足（需要 " + price + "）"; return r; }
            if (game == null || game.Tool == null) { r.message = "发放系统未就绪"; return r; }

            // 先扣款再发放；发放失败必须退款，避免出现「扣钱没拿到东西」
            s.coins -= price;
            ToolGrantResult grant = game.Tool.Grant(itemId, GrantSource.Shop);
            if (!grant.ok)
            {
                s.coins += price;
                r.message = "发放失败，已退款：" + grant.message;
                return r;
            }
            // ToolSystem.Grant 内部已通知背包入格，这里不再重复调用，避免同一物品占两格
            s.AddTradeLog(itemId, 0, price, 1, s.playTime);
            WhiteboxState.Save(s);
            r.ok = true; r.kind = 0; r.itemId = itemId; r.price = price; r.amount = 1;
            r.balanceAfter = s.coins; r.message = "购买成功：" + NameOf(itemId) + "（-" + price + " 金币）";
            Debug.Log("[CoinTrade] 购买 " + itemId + " 花费 " + price + "，余额 " + s.coins);
            return r;
        }

        // ----------------------------------------------------------------
        // 出售：背包出库 → 扣归属 → 加金币 → 写流水（扣归属是防刷的关键）
        // ----------------------------------------------------------------
        public CoinTradeResult Sell(string itemId, int amount = 1)
        {
            CoinTradeResult r = Fail(1, itemId, "");
            if (!config.Enabled) { r.message = "金币交易未开启"; return r; }
            WhiteboxState s = GameState;
            if (s == null) { r.message = "状态未初始化"; return r; }
            amount = Mathf.Max(1, amount);
            int price = SellPrice(itemId);
            if (price <= 0) { r.message = "该物品不可出售"; return r; }

            int matCat;
            bool isMaterial = IsMaterialItem(itemId, out matCat);
            if (!isMaterial)
            {
                if (ToolConfigRef != null && ToolConfigRef.FindBook(itemId) != null)
                { r.message = "技能书不可出售"; return r; }
                if (s.RewardCount(itemId) < amount) { r.message = "持有数量不足"; return r; }
            }
            else if (s.materials == null || matCat >= s.materials.Length || s.materials[matCat] < amount)
            { r.message = "产物数量不足"; return r; }

            // 非材料物品：背包出库 + 扣归属（防刷）；材料直接扣 materials，不进归属/背包
            if (!isMaterial)
            {
                if (bag != null && bag.CountOf(itemId) > 0)
                {
                    string why;
                    if (!bag.RemoveFirst(itemId, Mathf.Min(amount, bag.CountOf(itemId)), out why))
                    { r.message = "出库失败：" + why; return r; }
                }
                if (!s.ConsumeReward(itemId, amount)) { r.message = "扣减归属失败"; return r; }
            }
            else s.materials[matCat] -= amount;

            int fee = Mathf.FloorToInt(price * amount * config.TradeFee);
            int income = Mathf.Max(1, price * amount - fee);
            s.coins += income;
            s.AddTradeLog(itemId, 1, price, amount, s.playTime);
            WhiteboxState.Save(s);
            r.ok = true; r.kind = 1; r.itemId = itemId; r.price = price; r.amount = amount;
            r.balanceAfter = s.coins;
            r.message = "出售成功：" + NameOf(itemId) + " ×" + amount + "（+" + income + " 金币"
                        + (fee > 0 ? "，手续费 " + fee : "") + "）";
            Debug.Log("[CoinTrade] 出售 " + itemId + " ×" + amount + " 收入 " + income + "，余额 " + s.coins);
            return r;
        }

        /// <summary>以物易物：先出售 giveId 得金币，再买入 wantId；任一失败则整体回滚。</summary>
        public CoinTradeResult Trade(string giveId, string wantId)
        {
            CoinTradeResult r = Fail(2, wantId, giveId);
            if (!config.Enabled) { r.message = "金币交易未开启"; return r; }
            WhiteboxState s = GameState;
            if (s == null) { r.message = "状态未初始化"; return r; }
            int givePrice = SellPrice(giveId);
            int wantPrice = BuyPrice(wantId);
            if (givePrice <= 0 || wantPrice <= 0) { r.message = "该交易组合不受支持"; return r; }
            int coinsBefore = s.coins;
            CoinTradeResult sold = Sell(giveId, 1);
            if (!sold.ok) { r.message = "出售失败：" + sold.message; return r; }
            CoinTradeResult bought = Buy(wantId);
            if (!bought.ok)
            {
                // 回滚：退回出售的物品与归属，恢复余额
                s.coins = coinsBefore;
                if (bag != null) bag.OnRewardGranted(giveId, 1);
                s.AddReward(giveId);
                WhiteboxState.Save(s);
                r.message = "购买失败，已回滚：" + bought.message;
                return r;
            }
            r.ok = true; r.kind = 2; r.itemId = wantId; r.otherId = giveId;
            r.price = wantPrice; r.amount = 1; r.balanceAfter = s.coins;
            r.message = "交易成功：" + NameOf(giveId) + " → " + NameOf(wantId);
            return r;
        }

        CoinTradeResult Fail(int kind, string id, string other)
        {
            return new CoinTradeResult
            {
                ok = false, kind = kind, itemId = id, otherId = other,
                price = 0, amount = 0, balanceAfter = Balance, message = ""
            };
        }

        public SkillBookDef FindBook(string id) => ToolConfigRef?.FindBook(id);
        public int LearnedStacks(string id) => GameState != null ? GameState.UsedBookStacks(id) : 0;

        public string NameOf(string id)
        {
            int matCat;
            if (IsMaterialItem(id, out matCat))
            {
                string[] names = StationSystem.MaterialNames;
                return names != null && matCat < names.Length ? names[matCat] : "加工产物";
            }
            return bag != null ? bag.DisplayName(id) : id;
        }

        ToolConfig ToolConfigRef
        {
            get { return game != null && game.Tool != null ? game.Tool.Config : null; }
        }

        WhiteboxState GameState => game != null ? game.State : null;
    }
}
