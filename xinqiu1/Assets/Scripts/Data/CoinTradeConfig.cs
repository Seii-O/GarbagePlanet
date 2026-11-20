// @trace COIN-001, COIN-002, NUM-018
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 独立金币交易的配置（与背包格子、抽奖券完全分离）。
    /// 购买价复用 ToolConfig.purchase.price（T-06~T-10 已定 240/290/360/360/180）；
    /// 出售价 = 基准价 × sellRatio，基准价按稀有度兜底，防止凭空定价。
    /// </summary>
    [CreateAssetMenu(fileName = "CoinTradeConfig", menuName = "垃圾星球/金币交易配置")]
    public sealed class CoinTradeConfig : ScriptableObject
    {
        [SerializeField, Tooltip("金币交易总开关；关闭后购买/出售全部拒绝，余额仍可查看")]
        bool enabled = true;
        [SerializeField, Range(0f, 1f), Tooltip("出售价 = 基准价 × 本比例（默认 0.4，买贵卖便宜防刷）")]
        float sellRatio = 0.4f;
        [SerializeField, Tooltip("稀有度兜底基准价：普通 / 精英 / 史诗")]
        int[] basePriceByRarity = { 20, 120, 300 };
        [SerializeField, Tooltip("图鉴小物件统一基准价（普通档小物件无 purchase.price，用它兜底）")]
        int trinketBasePrice = 20;
        [SerializeField, Range(0f, 0.5f), Tooltip("交易手续费比例（暂默认 0；预留给后续经济调节）")]
        float tradeFee = 0f;
        [SerializeField, Tooltip("新局起始金币（默认 0；金币只能靠出售获得，不新增产出渠道）")]
        int startingCoins = 0;

        public bool Enabled => enabled;
        public float SellRatio => Mathf.Clamp01(sellRatio);
        public float TradeFee => Mathf.Clamp01(tradeFee);
        public int StartingCoins => Mathf.Max(0, startingCoins);
        public int TrinketBasePrice => Mathf.Max(1, trinketBasePrice);

        public int BasePrice(GachaRarity rarity, ToolConfig cfg)
        {
            int i = (int)rarity;
            if (basePriceByRarity != null && i >= 0 && i < basePriceByRarity.Length)
                return Mathf.Max(1, basePriceByRarity[i]);
            return TrinketBasePrice;
        }

        void OnValidate()
        {
            sellRatio = Mathf.Clamp01(sellRatio);
            tradeFee = Mathf.Clamp01(tradeFee);
            startingCoins = Mathf.Max(0, startingCoins);
            trinketBasePrice = Mathf.Max(1, trinketBasePrice);
            if (basePriceByRarity == null || basePriceByRarity.Length != 3)
            {
                int[] fixedArr = { 20, 120, 300 };
                if (basePriceByRarity != null)
                    System.Array.Copy(basePriceByRarity, fixedArr, Mathf.Min(basePriceByRarity.Length, 3));
                basePriceByRarity = fixedArr;
            }
            for (int i = 0; i < 3; i++) basePriceByRarity[i] = Mathf.Max(1, basePriceByRarity[i]);
        }

        public static CoinTradeConfig CreateDefault()
        {
            CoinTradeConfig c = CreateInstance<CoinTradeConfig>();
            c.enabled = true;
            c.sellRatio = 0.4f;
            c.basePriceByRarity = new[] { 20, 120, 300 };
            c.trinketBasePrice = 20;
            c.tradeFee = 0f;
            c.startingCoins = 0;
            return c;
        }
    }
}
