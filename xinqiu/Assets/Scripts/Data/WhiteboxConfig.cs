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
        [SerializeField] float planetRadius = 24f;
        [SerializeField] float stageOneThreshold = 0.30f;
        [SerializeField] float stageTwoThreshold = 0.70f;
        [SerializeField] float mvpProgressTarget = 0.30f; // [待拍板 C7] MVP 各修到 30%

        // [待拍板] 策划案 §6.3 / §8 把「地图上限 50 件」列为冻结值；
        // 本次按用户要求放宽，目的是让垃圾留在地表持续累积，属于表现层覆盖，未改任何玩法结算数值。
        [Header("垃圾刷新 MECH-011 / 地表存量 [待拍板]")]
        [SerializeField, Min(1), Tooltip("地表同时存在的垃圾件数上限（满则暂停刷新）；放宽后垃圾可持续留在地表累积")]
        int mapTrashCap = 120;
        [SerializeField, Min(0), Tooltip("开局铺在球面上的垃圾件数")]
        int initialTrashCount = 18;
        [SerializeField] float[] spawnInterval = { 15f, 12f, 10f };
        [SerializeField, Range(0.05f, 1f)] float whiteboxSpawnTimeScale = 0.25f; // 仅白膜演示加速，非冻结数值

        [Header("逐批堆积表现 [待拍板]")]
        [SerializeField, Min(1), Tooltip("每次刷新产出的件数；一批聚成一簇同时冒出")]
        int spawnBatchSize = 4;
        [SerializeField, Range(1, 16), Tooltip("球面垃圾堆数量，批次在这些堆心之间轮转")]
        int pileCenterCount = 4;
        [SerializeField, Min(0.1f), Tooltip("单堆散布半径（弧长）；堆积度越高越收拢，山形越陡")]
        float pileRadius = 2.2f;
        // ART-001 红线：任何垃圾堆不得超过视线水平线（eyeHeight 1.62）。
        // 堆顶 ≈ base(0.28 大型) + pileMaxHeight + 本体半高(≤0.28) = 1.41 < 1.62。
        [SerializeField, Range(0f, 1.2f), Tooltip("堆积度满时堆心的最大抬升高度；受 ART-001 视线红线约束")]
        float pileMaxHeight = 0.85f;
        [SerializeField, Min(0.05f), Tooltip("单件从地表下冒出的时长（秒）")]
        float spawnPopSeconds = 0.35f;
        [SerializeField, Min(0f), Tooltip("冒出前藏在地表下的深度；只改位置、不改缩放与碰撞体")]
        float spawnSinkDepth = 0.4f;
        [SerializeField, Min(0f), Tooltip("批内相邻两件冒出的错开间隔，形成逐件叠加感")]
        float spawnPopStagger = 0.05f;

        [Header("拾取 / 拆解 [待拍板 C1 C4 C5]")]
        [SerializeField] int[] backpackCap = { 10, 8, 8, 4 };       // NUM-007
        [SerializeField] float[] pickRange = { 2f, 3f, 4f };          // MECH-007
        [SerializeField] int[] dismantleDamage = { 1, 1, 2 };
        [SerializeField] int[] largeDurability = { 5, 8, 10 };        // [待拍板 C1]
        [SerializeField] int largeYield = 2;
        [SerializeField] float highlightRange = 3f;                    // [待拍板 C5]

        [Header("加工 / 制造 [待拍板 C2 C3 C7]")]
        [SerializeField] float[] conversionRate = { 0.5f, 0.5f, 1f, 0.5f };
        [SerializeField] int[] minProcessBatch = { 2, 2, 1, 2 };
        [SerializeField] float processSeconds = 5f;
        [SerializeField] float[] craftSeconds = { 8f, 5f, 12f };      // 建材板 / 肥料包 / 过滤器
        [SerializeField] float repairPerItem = 0.10f;                  // NUM-004
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
        public int MapTrashCap => Mathf.Clamp(mapTrashCap, 1, 1000);
        public int InitialTrashCount => Mathf.Clamp(initialTrashCount, 0, MapTrashCap);
        public float[] SpawnInterval => spawnInterval;
        public float WhiteboxSpawnTimeScale => whiteboxSpawnTimeScale;
        public int SpawnBatchSize => Mathf.Clamp(spawnBatchSize, 1, 50);
        public int PileCenterCount => Mathf.Clamp(pileCenterCount, 1, 16);
        public float PileRadius => Mathf.Max(0.1f, pileRadius);
        public float PileMaxHeight => Mathf.Clamp(pileMaxHeight, 0f, 1.2f);
        public float SpawnPopSeconds => Mathf.Max(0.05f, spawnPopSeconds);
        public float SpawnSinkDepth => Mathf.Max(0f, spawnSinkDepth);
        public float SpawnPopStagger => Mathf.Max(0f, spawnPopStagger);
        public int[] BackpackCap => backpackCap;
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
            mapTrashCap = Mathf.Clamp(mapTrashCap, 1, 1000);
            initialTrashCount = Mathf.Clamp(initialTrashCount, 0, mapTrashCap);
            spawnBatchSize = Mathf.Clamp(spawnBatchSize, 1, 50);
            pileCenterCount = Mathf.Clamp(pileCenterCount, 1, 16);
            pileRadius = Mathf.Max(0.1f, pileRadius);
            pileMaxHeight = Mathf.Clamp(pileMaxHeight, 0f, 1.2f);
            spawnPopSeconds = Mathf.Max(0.05f, spawnPopSeconds);
            spawnSinkDepth = Mathf.Max(0f, spawnSinkDepth);
            spawnPopStagger = Mathf.Max(0f, spawnPopStagger);
            repairPerItem = Mathf.Clamp01(repairPerItem);
            mvpProgressTarget = Mathf.Clamp01(mvpProgressTarget);
            stageOneThreshold = Mathf.Clamp(stageOneThreshold, 0.01f, stageTwoThreshold - 0.01f);
            stageTwoThreshold = Mathf.Clamp(stageTwoThreshold, stageOneThreshold + 0.01f, 0.99f);

            Ensure(ref spawnInterval, 3, 15f);
            Ensure(ref backpackCap, 4, 8);
            Ensure(ref pickRange, 3, 2f);
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
