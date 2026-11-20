// @trace TOOL-001, TOOL-014, NUM-017
using System;
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>工具三层：主工具 / 专精 / 辅助。</summary>
    public enum ToolLayer { Main = 0, Spec = 1, Support = 2 }

    /// <summary>发放入口：抽奖 / 商店 / 任务 / 调试。所有入口共用同一发放逻辑。</summary>
    public enum GrantSource { Gacha = 0, Shop = 1, Quest = 2, Dev = 3 }

    /// <summary>预留货币种类；本轮金币不产出，商店入口不生成。</summary>
    public enum ToolCurrency { Coins = 0, Tickets = 1, Material = 2 }
    public enum PurchaseLimit { None = 0, Once = 1, Total = 2, Daily = 3 }
    public enum ShopEntry { None = 0, Station = 1, GachaPanel = 2, Both = 3 }

    /// <summary>技能书作用线：加工耗时 / 制造耗时 / 零耗时次数。</summary>
    public enum BookEffect { None = 0, ProcessTime = 1, CraftTime = 2, FreeCharge = 3 }

    /// <summary>单件工具定义。rewardId 为空表示不进抽奖池（主工具 / 专精）。</summary>
    [Serializable]
    public sealed class ToolDef
    {
        public string toolId = "";
        public string rewardId = "";
        public string displayName = "";
        public ToolLayer layer = ToolLayer.Support;
        public int cat = -1;              // -1 = 全局
        public bool gachaEligible;
        public string poolId = "scrap";
        public bool codexOnly;
        public bool epicRequiresRepair;
        public GachaRarity rarity = GachaRarity.Rare;
        public float rangeBonus;
        public int damageBonus;
        public int yieldBonus;
        public float processMult = 1f;
        public float craftMult = 1f;
        public int freeChargesMax;
        public ToolPurchase purchase = new ToolPurchase();
    }

    /// <summary>技能书定义。层数直接复用 WhiteboxState.ownedRewardCounts，不新增存档字段。</summary>
    [Serializable]
    public sealed class SkillBookDef
    {
        public string rewardId = "";
        public string displayName = "";
        public GachaRarity rarity = GachaRarity.Rare;
        public int weight = 5;
        public int maxStacks = 5;
        public BookEffect effect = BookEffect.ProcessTime;
        public float perStackMult = 0.99f;
        public int chargesAtFull;
        public bool gachaOnly = true;
        public bool activeInPhaseA = true;   // 史诗书工具集齐前不入池
        public int overflowMaterialCat = 0;  // 满层后折算的材料类别 0..3
    }

    /// <summary>多池预留；本轮只有一个 scrap 池，默认不启用第二池。</summary>
    [Serializable]
    public sealed class GachaPoolConfig
    {
        public string poolId = "scrap";
        public bool enabled = true;
        public string[] rewardIds;
        public float[] rarityWeights;
        public int hardPityAt = -1;
        public int costTickets = 1;
        public string requireFlag = "";
        public int stockLimit;
    }

    /// <summary>金币购买配置（C22：本轮全部 enabled=false）。</summary>
    [Serializable]
    public sealed class ToolPurchase
    {
        public bool enabled = false;
        public ToolCurrency currency = ToolCurrency.Coins;
        public int price;
        public PurchaseLimit limit = PurchaseLimit.Once;
        public int limitValue = 1;
        public ShopEntry entry = ShopEntry.Station;
        public bool requiresGachaUnlock = false;
        public int minQuestStep = -1;
    }

    /// <summary>统一发放结果；抽奖与商店共用，供 UI 区分首获 / 重复 / 满层 / 返券。</summary>
    public struct ToolGrantResult
    {
        public bool ok;
        public string id;
        public bool isNew;
        public bool duplicate;
        public bool isBook;
        public int stacks;
        public int maxStacks;
        public bool maxed;
        public int refundTickets;
        public int overflowMaterial;
        public string message;
    }

    public interface IToolGrantSink
    {
        ToolGrantResult Grant(string id, GrantSource src);
    }

    public interface ICurrencyWallet
    {
        bool CanAfford(ToolCurrency c, int n);
        bool Spend(ToolCurrency c, int n);
        int Balance(ToolCurrency c);
    }

    public interface IToolShop
    {
        bool IsAvailable(string toolId);
        bool CanPurchase(string toolId, out string reason);
        int PriceOf(string toolId);
        int RemainingLimit(string toolId);
        ToolPurchaseResult Purchase(string toolId);
    }

    /// <summary>商店下单结果。本轮只有实现，无调用入口。</summary>
    public struct ToolPurchaseResult
    {
        public bool ok;
        public string toolId;
        public string reason;
        public int price;
        public ToolGrantResult grant;
    }

    /// <summary>
    /// 工具与技能书的数据真值（10 工具 + 4 书）。由菜单「⑫ 生成工具配置资产」写入 Assets/Data/ToolConfig.asset；
    /// 场景未接引用时运行时用 CreateDefault() 兜底，数值与资产一致，保证旧场景不崩。
    /// </summary>
    [CreateAssetMenu(fileName = "ToolConfig", menuName = "垃圾星球/工具与技能书配置")]
    public sealed class ToolConfig : ScriptableObject
    {
        [SerializeField] ToolDef[] tools;
        [SerializeField] SkillBookDef[] books;
        [SerializeField] GachaPoolConfig[] pools;
        [SerializeField] int coinPerTicket = 12;
        [SerializeField] bool bookOverflowMaterial = true;

        public ToolDef[] Tools => tools ?? (tools = new ToolDef[0]);
        public SkillBookDef[] Books => books ?? (books = new SkillBookDef[0]);
        public GachaPoolConfig[] Pools => pools ?? (pools = new GachaPoolConfig[0]);
        public int CoinPerTicket => Mathf.Max(1, coinPerTicket);
        public bool BookOverflowMaterial => bookOverflowMaterial;

        public ToolDef Find(string toolId)
        {
            if (string.IsNullOrEmpty(toolId)) return null;
            ToolDef[] all = Tools;
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].toolId == toolId) return all[i];
            return null;
        }

        public ToolDef FindByReward(string rewardId)
        {
            if (string.IsNullOrEmpty(rewardId)) return null;
            ToolDef[] all = Tools;
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].rewardId == rewardId) return all[i];
            return null;
        }

        public SkillBookDef FindBook(string rewardId)
        {
            if (string.IsNullOrEmpty(rewardId)) return null;
            SkillBookDef[] all = Books;
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].rewardId == rewardId) return all[i];
            return null;
        }

        public SkillBookDef FindBookByEffect(BookEffect effect)
        {
            SkillBookDef[] all = Books;
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].effect == effect) return all[i];
            return null;
        }

        /// <summary>重复返券位：按 gachaEligible 的工具顺序固定为 0..4，总上限 5 券。</summary>
        public int RefundBitOf(string rewardId)
        {
            if (string.IsNullOrEmpty(rewardId)) return -1;
            int bit = 0;
            ToolDef[] all = Tools;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || !all[i].gachaEligible) continue;
                if (all[i].rewardId == rewardId) return bit;
                bit++;
            }
            return -1;
        }

        void OnValidate()
        {
            coinPerTicket = Mathf.Max(1, coinPerTicket);
            if (tools != null)
                for (int i = 0; i < tools.Length; i++)
                    if (tools[i] != null) tools[i].cat = Mathf.Clamp(tools[i].cat, -1, 3);
            if (books != null)
                for (int i = 0; i < books.Length; i++)
                {
                    if (books[i] == null) continue;
                    books[i].weight = Mathf.Clamp(books[i].weight, 1, 100);
                    books[i].maxStacks = Mathf.Max(1, books[i].maxStacks);
                    books[i].perStackMult = Mathf.Clamp(books[i].perStackMult, 0.8f, 1f);
                    books[i].overflowMaterialCat = Mathf.Clamp(books[i].overflowMaterialCat, 0, 3);
                }
        }

        /// <summary>代码默认配置：数值与 .asset 完全一致；资产缺失或旧场景未接线时使用。</summary>
        public static ToolConfig CreateDefault()
        {
            ToolConfig c = CreateInstance<ToolConfig>();
            c.coinPerTicket = 12;
            c.bookOverflowMaterial = true;
            c.tools = new[]
            {
                Tool("T-01", "", "拾取器 MK-I/II/III", ToolLayer.Main, -1, false, 0f, 0, 0, 1f, 1f, 0, 0),
                Tool("T-02", "", "压缩钳", ToolLayer.Spec, 0, false, 0f, 0, 0, 1f, 1f, 0, 0),
                Tool("T-03", "", "磁吸爪", ToolLayer.Spec, 1, false, 0f, 0, 0, 1f, 1f, 0, 0),
                Tool("T-04", "", "堆肥铲", ToolLayer.Spec, 2, false, 0f, 0, 0, 1f, 1f, 0, 0),
                Tool("T-05", "", "解码钳", ToolLayer.Spec, 3, false, 0f, 0, 0, 1f, 1f, 0, 0),
                Tool("T-06", "weapon_gripper", "加强拾取夹", ToolLayer.Support, -1, true, 1f, 0, 0, 1f, 1f, 0, 240),
                Tool("T-07", "weapon_magnet", "磁铁手套", ToolLayer.Support, 1, true, 0f, 1, 0, 1f, 1f, 0, 290),
                Tool("T-08", "weapon_purifier", "星球净化器", ToolLayer.Support, -1, true, 0f, 0, 0, 1f, 1f, 2, 360, true),
                Tool("T-09", "skill_ecomaster", "生态专家", ToolLayer.Support, -1, true, 0f, 0, 0, 1f, 1f, 0, 360, true),
                Tool("T-10", "skill_quickdismantle", "快速分解", ToolLayer.Support, -1, true, 0f, 0, 0, 1f, 1f, 0, 180, false, true)
            };
            c.books = new[]
            {
                Book("book_sorting", "快分拣", GachaRarity.Rare, 5, 5, BookEffect.ProcessTime, 0.99f, 0, true, 0),
                Book("book_handwork", "熟手工", GachaRarity.Rare, 5, 5, BookEffect.CraftTime, 0.99f, 0, true, 0),
                Book("book_flow", "一气呵成", GachaRarity.Epic, 2, 3, BookEffect.CraftTime, 0.98f, 0, false, 3),
                Book("book_tireless", "不知疲倦", GachaRarity.Epic, 2, 4, BookEffect.FreeCharge, 1f, 1, false, 2)
            };
            c.pools = new[] { new GachaPoolConfig { poolId = "scrap", enabled = true } };
            return c;
        }

        static ToolDef Tool(string id, string rewardId, string name, ToolLayer layer, int cat,
            bool gacha, float range, int damage, int yield, float process, float craft,
            int charges, int price, bool epicRepair = false, bool codex = false)
        {
            return new ToolDef
            {
                toolId = id, rewardId = rewardId, displayName = name, layer = layer, cat = cat,
                gachaEligible = gacha, codexOnly = codex, epicRequiresRepair = epicRepair,
                rarity = epicRepair ? GachaRarity.Epic : GachaRarity.Rare,
                rangeBonus = range, damageBonus = damage, yieldBonus = yield,
                processMult = process, craftMult = craft, freeChargesMax = charges,
                purchase = new ToolPurchase
                {
                    enabled = false, currency = ToolCurrency.Coins, price = price,
                    limit = PurchaseLimit.Once, limitValue = 1, entry = ShopEntry.Station
                }
            };
        }

        static SkillBookDef Book(string rewardId, string name, GachaRarity rarity, int weight,
            int maxStacks, BookEffect effect, float perStack, int chargesAtFull, bool phaseA, int overflowCat)
        {
            return new SkillBookDef
            {
                rewardId = rewardId, displayName = name, rarity = rarity, weight = weight,
                maxStacks = maxStacks, effect = effect, perStackMult = perStack,
                chargesAtFull = chargesAtFull, gachaOnly = true, activeInPhaseA = phaseA,
                overflowMaterialCat = overflowCat
            };
        }
    }
}
