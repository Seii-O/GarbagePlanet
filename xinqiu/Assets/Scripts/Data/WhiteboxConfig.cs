// @trace ART-003, ART-004, ART-005, ART-008, NUM-007, PLAT-005
using System;
using System.Globalization;
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 白膜唯一数值入口（ScriptableObject）。策划案 §12「数据驱动」：数值不写死在玩法脚本里。
    /// [待拍板] 标记对应 v1.2 第 16 章 C1–C13，这里只给「可配置默认值」，不代表已定稿。
    /// 工程口径：Unity 2022.3 LTS + URP；包版本以本机 Package Manager 实际解析为准。
    /// </summary>
    [CreateAssetMenu(fileName = "WhiteboxConfig", menuName = "垃圾星球/白膜配置")]
    public sealed class WhiteboxConfig : ScriptableObject
    {
        [Header("星球 [待拍板 C6]")]
        // 半径 24 → 16（缩 1/3）→ 8（再缩一半）。星球是同比例的球体：行走、拾取、刷怪全部按这个值算，
        // 场景里已烘焙的星球体积由 WhiteboxGame 在 Awake 里按本值重设（PlanetLayout）。
        [SerializeField] float planetRadius = 8f;
        [SerializeField] float stageOneThreshold = 0.30f;
        [SerializeField] float stageTwoThreshold = 0.70f;
        [SerializeField] float mvpProgressTarget = 0.30f; // [待拍板 C7] MVP 各修到 30%

        // [待拍板] 策划案 §6.3 / §8 把「地图上限 50 件」列为冻结值；
        // 本次按用户要求改为「全局总量 1500 件 + 地表并发 1000 件」两道上限：
        // 累计生成满 1500 件（含已被捡走的）后，地表不再出现任何新垃圾。
        [Header("垃圾刷新 MECH-011 / 全局总量 [待拍板]")]
        [SerializeField, Min(1), Tooltip("地表同时存在的垃圾件数上限（满则暂停刷新，等玩家捡走再补）")]
        int mapTrashCap = 1000;
        [SerializeField, Min(1), Tooltip("全局垃圾总量：累计生成件数（含玩家已捡走的）达到后，地表不再刷新新垃圾")]
        int totalTrashBudget = 1500;
        [SerializeField, Min(0), Tooltip("开局铺在球面上的垃圾件数（同样计入全局总量）")]
        int initialTrashCount = 18;
        [SerializeField, Tooltip("各阶段两次刷新之间的间隔（秒）；三档同为 10 s = 与生态阶段无关的固定节奏")]
        float[] spawnInterval = { 10f, 10f, 10f };
        [SerializeField, Range(0.05f, 4f), Tooltip("刷新节奏倍率：1 = 按真实间隔（10 s）；白膜演示可调小加速，不影响配置的 10 s 数值")]
        float whiteboxSpawnTimeScale = 1f;

        [Header("随机散落 + 出现动画 [待拍板]")]
        [SerializeField, Min(1), Tooltip("每次刷新产出的件数（1000 件并发上限下取 15）")]
        int spawnBatchSize = 15;
        [SerializeField, Min(0.1f), Tooltip("两件垃圾之间的最小间距（米）：随机撒点时的去重阈值，避免叠在一起成一堆")]
        float scatterMinSpacing = 0.4f;
        [SerializeField, Min(0f), Tooltip("不在玩家这个距离（米）以内刷新垃圾；星球半径减半后同步收窄，否则玩家身边半颗球都是净空")]
        float scatterPlayerClearance = 3.5f;
        [SerializeField, Range(0f, 20f), Tooltip("散落垃圾相对地表的随机倾斜角度，看起来是躺在地上的；0 = 全部竖直。倾角越大，单侧翘起越明显，贴合地面感越差")]
        float scatterTiltDegrees = 5f;
        [SerializeField, Min(0.05f), Tooltip("单件从地表下冒出的时长（秒）")]
        float spawnPopSeconds = 0.45f;
        [SerializeField, Min(0f), Tooltip("冒出前藏在地表下的深度；只改位置、不改碰撞体")]
        float spawnSinkDepth = 0.55f;
        [SerializeField, Min(0f), Tooltip("批内相邻两件冒出的错开间隔，形成陆续出现感")]
        float spawnPopStagger = 0.06f;
        [SerializeField, Range(0.05f, 1f), Tooltip("出现动画的起始缩放（1 = 只做升起不做缩放）")]
        float spawnStartScale = 0.55f;

        [Header("拾取 / 拆解 [待拍板 C1 C4 C5]")]
        // 背包改为「总量 20 → 40」两级：Lv.1 每类 5（合计 20），Lv.2 每类 10（合计 40，满级）。
        [SerializeField, Tooltip("背包 Lv.1 每类容量（塑/金/有/电）")]
        int[] backpackCap = { 5, 5, 5, 5 };       // NUM-007
        [SerializeField, Tooltip("背包 Lv.2（满级）每类容量；只有 2 级，升级 1 次即到顶")]
        int[] backpackCapMax = { 10, 10, 10, 10 };
        [SerializeField, Min(1), Tooltip("背包 Lv.1 → Lv.2 的金属零件消耗")]
        int backpackUpgradeMetalCost = 15;
        [SerializeField, Tooltip("最远拾取距离（按工具等级）；本次整体 ×2")]
        float[] pickRange = { 4f, 6f, 8f };          // MECH-007
        [SerializeField] int[] dismantleDamage = { 1, 1, 2 };
        [SerializeField] int[] largeDurability = { 5, 8, 10 };        // [待拍板 C1]
        [SerializeField] int largeYield = 2;
        [SerializeField, Tooltip("描边亮起（含大型垃圾耐久条）的距离判定，随拾取距离一起 ×2")]
        float highlightRange = 6f;                    // [待拍板 C5]

        [Header("加工 / 制造 [待拍板 C2 C3 C7]")]
        [SerializeField] float[] conversionRate = { 0.5f, 0.5f, 1f, 0.5f };
        [SerializeField] int[] minProcessBatch = { 2, 2, 1, 2 };
        [SerializeField] float processSeconds = 5f;
        [SerializeField] float[] craftSeconds = { 8f, 5f, 12f };      // 建材板 / 肥料包 / 过滤器
        [SerializeField] float repairPerItem = 0.10f;                  // NUM-004
        [SerializeField, Tooltip("批量加工/制造时耗时是否固定为「一件的时长」：勾上 = 数量只影响消耗与产出，不叠加时间")]
        bool batchFixedDuration = true;
        [SerializeField] int[] upgradeMetalCost = { 5, 10 };           // [待拍板 C7] 原值 ÷10
        [SerializeField] int[] upgradeElectronicCost = { 6, 12 };

        [Header("伙伴狗 MECH-014/015 NUM-010")]
        [SerializeField] float[] dogFetchRadius = { 1.5f, 2.5f, 3.5f };
        [SerializeField] float[] dogFetchCooldown = { 4f, 3f, 2f };
        [SerializeField] float dogFollowDistance = 1.8f;
        [SerializeField] float dogHeight = 0.55f;                      // ART-011 体高 ≤ 0.6
        [SerializeField] string dogColorHex = "#D8C3A0";               // ART-012 全程唯一固有色

        [Header("功能色 ART-004")]
        [SerializeField] string highlightHex = "#FFFFFF"; // 白色描边（去旧蓝环）
        [SerializeField] string bagWarningHex = "#E2603C";
        [SerializeField] string durabilityHex = "#F2C14E";

        [Header("三阶段色板 ART-003 / 光照 ART-005")]
        [SerializeField] string[] groundHex = { "#6E6053", "#7C7A50", "#5FA233" };
        [SerializeField] string[] waterHex = { "#3A4A4E", "#4E7A6E", "#4FB3C4" };
        [SerializeField] string[] skyHex = { "#9A9083", "#A8B5A0", "#8FD3DE" };
        [SerializeField] string[] fogHex = { "#8C8375", "#9AA694", "#B8DDE4" };
        [SerializeField] float[] fogDistance = { 40f, 60f, 100f };
        [SerializeField] float[] lightAngle = { 25f, 45f, 60f };

        [Header("动效时长 ART-008 / 9.5")]
        [SerializeField] float stageFadeSeconds = 3f;
        [SerializeField] float stageFlashSeconds = 2.5f;
        [SerializeField] float pickupFloatSeconds = 0.25f;
        [SerializeField] float depositSeconds = 0.4f;

        public float PlanetRadius => Mathf.Max(1f, planetRadius);
        public float MvpProgressTarget => mvpProgressTarget;
        /// <summary>地表并发上限（不会被全局总量顶掉，两者取小生效）。</summary>
        public int MapTrashCap => Mathf.Clamp(mapTrashCap, 1, Mathf.Max(1, TotalTrashBudget));
        /// <summary>全局总量：累计生成 1500 件后地表不再出现新垃圾。</summary>
        public int TotalTrashBudget => Mathf.Clamp(totalTrashBudget, 1, 100000);
        public int InitialTrashCount => Mathf.Clamp(initialTrashCount, 0, MapTrashCap);
        public float[] SpawnInterval => spawnInterval;
        public float WhiteboxSpawnTimeScale => whiteboxSpawnTimeScale;
        public int SpawnBatchSize => Mathf.Clamp(spawnBatchSize, 1, 50);
        public float ScatterMinSpacing => Mathf.Max(0.1f, scatterMinSpacing);
        public float ScatterPlayerClearance => Mathf.Max(0f, scatterPlayerClearance);
        public float ScatterTiltDegrees => Mathf.Clamp(scatterTiltDegrees, 0f, 20f);
        public float SpawnPopSeconds => Mathf.Max(0.05f, spawnPopSeconds);
        public float SpawnSinkDepth => Mathf.Max(0f, spawnSinkDepth);
        public float SpawnPopStagger => Mathf.Max(0f, spawnPopStagger);
        public float SpawnStartScale => Mathf.Clamp(spawnStartScale, 0.05f, 1f);
        /// <summary>true = 批量加工/制造固定按「一件的时长」结算，数量不叠加时间。</summary>
        public bool BatchFixedDuration => batchFixedDuration;
        public int[] BackpackCap => backpackCap;
        /// <summary>背包等级上限：1 = 初始 20 件，2 = 满级 40 件，一共只有 2 级。</summary>
        public const int BagMaxLevel = 2;
        public int BackpackUpgradeMetalCost => Mathf.Max(1, backpackUpgradeMetalCost);
        /// <summary>按背包等级取每类容量；越界等级一律钳到 1..2。</summary>
        public int[] BackpackCapAt(int level)
        {
            int[] src = Mathf.Clamp(level, 1, BagMaxLevel) >= BagMaxLevel && backpackCapMax != null
                ? backpackCapMax
                : backpackCap;
            return src;
        }
        /// <summary>某一等级的背包总容量（4 类相加）。</summary>
        public int BackpackTotalCap(int level)
        {
            int[] a = BackpackCapAt(level);
            int sum = 0;
            if (a != null) for (int i = 0; i < a.Length; i++) sum += Mathf.Max(0, a[i]);
            return sum;
        }
        public float[] PickRange => pickRange;
        public int[] DismantleDamage => dismantleDamage;
        public int[] LargeDurability => largeDurability;
        public int LargeYield => Mathf.Max(1, largeYield);
        public float HighlightRange => highlightRange;
        public float[] ConversionRate => conversionRate;
        public int[] MinProcessBatch => minProcessBatch;
        public float ProcessSeconds => processSeconds;
        public float[] CraftSeconds => craftSeconds;
        public float RepairPerItem => repairPerItem;
        public int[] UpgradeMetalCost => upgradeMetalCost;
        public int[] UpgradeElectronicCost => upgradeElectronicCost;
        public float[] DogFetchRadius => dogFetchRadius;
        public float[] DogFetchCooldown => dogFetchCooldown;
        public float DogFollowDistance => dogFollowDistance;
        public float DogHeight => dogHeight;
        public Color DogColor => Hex(dogColorHex);
        public Color HighlightColor => Hex(highlightHex);
        public Color BagWarningColor => Hex(bagWarningHex);
        public Color DurabilityColor => Hex(durabilityHex);
        public Color[] GroundColors => HexArray(groundHex);
        public Color[] WaterColors => HexArray(waterHex);
        public Color[] SkyColors => HexArray(skyHex);
        public Color[] FogColors => HexArray(fogHex);
        public float[] FogDistance => fogDistance;
        public float[] LightAngle => lightAngle;
        public float StageFadeSeconds => stageFadeSeconds;
        public float StageFlashSeconds => stageFlashSeconds;
        public float PickupFloatSeconds => pickupFloatSeconds;
        public float DepositSeconds => depositSeconds;

        public int StageOf(float progress)
        {
            if (progress < stageOneThreshold) return 0;
            if (progress < stageTwoThreshold) return 1;
            return 2;
        }

        public static Color Hex(string s)
        {
            Color c;
            if (ColorUtility.TryParseHtmlString(s, out c)) return c;
            return Color.magenta;
        }

        static Color[] HexArray(string[] src)
        {
            var r = new Color[3];
            for (int i = 0; i < 3; i++) r[i] = Hex(src != null && i < src.Length ? src[i] : "#FFFFFF");
            return r;
        }

        void OnValidate()
        {
            planetRadius = Mathf.Max(1f, planetRadius);
            totalTrashBudget = Mathf.Clamp(totalTrashBudget, 1, 100000);
            mapTrashCap = Mathf.Clamp(mapTrashCap, 1, totalTrashBudget);
            initialTrashCount = Mathf.Clamp(initialTrashCount, 0, mapTrashCap);
            spawnBatchSize = Mathf.Clamp(spawnBatchSize, 1, 50);
            scatterMinSpacing = Mathf.Max(0.1f, scatterMinSpacing);
            scatterPlayerClearance = Mathf.Max(0f, scatterPlayerClearance);
            scatterTiltDegrees = Mathf.Clamp(scatterTiltDegrees, 0f, 20f);
            spawnPopSeconds = Mathf.Max(0.05f, spawnPopSeconds);
            spawnSinkDepth = Mathf.Max(0f, spawnSinkDepth);
            spawnPopStagger = Mathf.Max(0f, spawnPopStagger);
            spawnStartScale = Mathf.Clamp(spawnStartScale, 0.05f, 1f);
            repairPerItem = Mathf.Clamp01(repairPerItem);
            backpackUpgradeMetalCost = Mathf.Max(1, backpackUpgradeMetalCost);
            highlightRange = Mathf.Max(0.1f, highlightRange);
            mvpProgressTarget = Mathf.Clamp01(mvpProgressTarget);
            stageOneThreshold = Mathf.Clamp(stageOneThreshold, 0.01f, stageTwoThreshold - 0.01f);
            stageTwoThreshold = Mathf.Clamp(stageTwoThreshold, stageOneThreshold + 0.01f, 0.99f);

            Ensure(ref spawnInterval, 3, 10f);
            Ensure(ref backpackCap, 4, 5);
            Ensure(ref backpackCapMax, 4, 10);
            Ensure(ref pickRange, 3, 4f);
            Ensure(ref dismantleDamage, 3, 1);
            Ensure(ref largeDurability, 3, 5);
            Ensure(ref conversionRate, 4, 0.5f);
            Ensure(ref minProcessBatch, 4, 2);
            Ensure(ref craftSeconds, 3, 5f);
            Ensure(ref dogFetchRadius, 3, 1.5f);
            Ensure(ref dogFetchCooldown, 3, 4f);
            Ensure(ref fogDistance, 3, 40f);
            Ensure(ref lightAngle, 3, 25f);
            Ensure(ref upgradeMetalCost, 2, 5);
            Ensure(ref upgradeElectronicCost, 2, 6);

            for (int i = 0; i < 4; i++)
            {
                backpackCap[i] = Mathf.Max(1, backpackCap[i]);
                // 满级容量不得低于初始容量，否则「升级」反而变小
                backpackCapMax[i] = Mathf.Max(backpackCap[i], backpackCapMax[i]);
                minProcessBatch[i] = Mathf.Max(1, minProcessBatch[i]);
                conversionRate[i] = Mathf.Clamp01(conversionRate[i]);
            }
            for (int i = 0; i < 3; i++)
            {
                spawnInterval[i] = Mathf.Max(0.1f, spawnInterval[i]);
                pickRange[i] = Mathf.Max(0.1f, pickRange[i]);
                dismantleDamage[i] = Mathf.Max(1, dismantleDamage[i]);
                largeDurability[i] = Mathf.Max(1, largeDurability[i]);
                dogFetchCooldown[i] = Mathf.Max(0.1f, dogFetchCooldown[i]);
                dogFetchRadius[i] = Mathf.Max(0.1f, dogFetchRadius[i]);
                fogDistance[i] = Mathf.Max(1f, fogDistance[i]);
            }
        }

        static void Ensure<T>(ref T[] a, int n, T fallback)
        {
            if (a != null && a.Length == n) return;
            var r = new T[n];
            for (int i = 0; i < n; i++) r[i] = fallback;
            if (a != null) Array.Copy(a, r, Mathf.Min(a.Length, n));
            a = r;
        }
    }
}
