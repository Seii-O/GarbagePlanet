// @trace ART-003, ART-004, ART-005, ART-008, NUM-007, PLAT-005, NUM-012, NUM-013, MECH-017, ART-014
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

        [Header("垃圾刷新 MECH-011")]
        [SerializeField, Min(1), Tooltip("三个净化阶段分别维持的地表垃圾件数")]
        int[] surfaceTrashTargets = { 3000, 2500, 2000 };

        [Header("随机散落 + 出现动画 [待拍板]")]
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
        [SerializeField, Range(0.05f, 1f), Tooltip("出现动画的起始缩放（1 = 只做升起不做缩放）")]
        float spawnStartScale = 0.55f;

        [Header("拾取 / 拆解 [待拍板 C1 C4 C5]")]
        // 背包容量与人物等级绑定：Lv.1 合计 50（初始不变），人物每升 1 级在 4 类上共 +10。
        [SerializeField, Tooltip("背包 Lv.1（初始）每类容量（塑/金/有/电）")]
        int[] backpackCap = { 13, 13, 12, 12 };       // NUM-007，总计 50
        [SerializeField, Tooltip("人物每升 1 级，背包每类新增容量（塑/金/有/电）；四项之和即每级扩容格数（默认 3+3+2+2=10）")]
        int[] backpackCapPerLevel = { 3, 3, 2, 2 };   // 每级 +10，Lv.5 合计 90
        [SerializeField, Tooltip("旧版两级制满级容量，仅用于旧资产序列化兼容；容量计算已改走 backpackCapPerLevel")]
        int[] backpackCapMax = { 25, 25, 25, 25 };
        [SerializeField, Tooltip("最远拾取距离（按工具等级）；本次整体 ×2")]
        float[] pickRange = { 4f, 6f, 8f };          // MECH-007
        [SerializeField] int[] dismantleDamage = { 1, 1, 2 };
        [SerializeField] int[] largeDurability = { 5, 8, 10 };        // [待拍板 C1]
        [SerializeField] int largeYield = 2;
        // 「先小后大」节奏：大件占比按人物等级递增（下标 0 = Lv.1）。
        // 电子类没有大型外观，抽中名额会回落成小件；生成侧按「缺口」判定，会自动补回，不是按抽签扣掉。
        [SerializeField, Tooltip("大型垃圾生成占比（按人物等级 1→5）：等级越高越常见，0 = 不生成大件")]
        float[] largeSpawnRatio = { 0.03f, 0.06f, 0.10f, 0.16f, 0.22f };
        // 旧 .asset 里没有 largeSpawnRatio 这个键（新增字段不会自动写进已存在的资产），
        // 读出来是空数组；只靠一个标量兜底会让所有等级都变成同一个值。
        // 用这个版本号做一次性迁移：0 = 资产还是旧的，启动时按下面的曲线补齐。
        [SerializeField, HideInInspector] int spawnRatioVersion;
        // 升级后「补齐比例」的做法：只把玩家背半球的小件换成同类大件。
        // 玩家站在半径 8 m 的球面上，地平线只有约 34°，60° 之外一定在地平线以下，
        // 所以这次替换不可能被看见，走过去时世界已经是新比例了。
        [SerializeField, Tooltip("静默补齐大件的最小角距（度）：只替换与玩家相差超过该角度的小件。默认 60（已在地平线之外）")]
        float largeCatchUpMinAngle = 60f;
        [SerializeField, Tooltip("静默补齐的速率（件/秒）：越大追得越快，也越容易在低端机掉帧")]
        float largeCatchUpPerSecond = 6f;
        [SerializeField, Tooltip("描边亮起（含大型垃圾耐久条）的距离判定，随拾取距离一起 ×2")]
        float highlightRange = 6f;                    // [待拍板 C5]

        [Header("工具系统 TOOL-001~006 / NUM-012~013")]
        [SerializeField, Tooltip("T-02/03/04/05 专精 Lv.1 解锁所需累计加工次数（塑料/金属/有机/电子）")]
        int[] specUnlockProcessCount = { 6, 6, 4, 6 };
        [SerializeField, Tooltip("专精 Lv.2 升级消耗的该类材料件数")]
        int specUpgradeMaterialCost = 6;
        [SerializeField, Tooltip("专精 Lv.2 升级消耗的额外金属零件")]
        int specUpgradeMetalCost = 3;
        [SerializeField] float specRangeBonus = 0.5f;   // T-03 金属拾取 +0.5 m
        [SerializeField] int specYieldBonus = 1;        // T-02 Lv.2 塑料大型产出 +1
        [SerializeField] int specDamageBonus = 1;       // T-03 Lv.2 金属拆解 +1
        [SerializeField, Range(0.5f, 1f)] float specProcessMult = 0.9f;
        [SerializeField, Range(0.5f, 1f)] float specCraftMult = 0.9f;
        [SerializeField] float pickRangeCap = 9f;
        [SerializeField] int dismantleDamageCap = 3;
        [SerializeField, Range(0.5f, 1f)] float efficiencyFloor = 0.8f;
        [SerializeField] float gripperRangeBonus = 1f;  // T-06
        [SerializeField] int magnetFirstHitBonus = 1;    // T-07
        [SerializeField] int purifierChargesMax = 2;     // T-08

        // 人物等级接管原工具加成线（C27）：经验由既有计数推导，等级驱动拾取/效率/零耗时。
        [Header("人物等级（工具下线后接管加成）")]
        [SerializeField, Tooltip("人物等级上限；每级加成见下列数组，数组长度不足时按最后一项取值")]
        int playerLevelMax = 5;
        [SerializeField, Tooltip("各级升级所需历史累计经验（索引=等级-1；到顶级用最后一项）")]
        int[] playerLevelExp = { 0, 300, 800, 1600, 2800 };
        [SerializeField, Tooltip("行为转经验的系数：拾取 / 拆解 / 加工 / 制造 / 修复阶段")]
        int expPerCollect = 1;
        [SerializeField] int expPerDismantle = 2;
        [SerializeField] int expPerProcess = 2;
        [SerializeField] int expPerCraft = 3;
        [SerializeField] int expPerRepair = 10;
        [SerializeField, Tooltip("各级拾取范围（米）")]
        float[] playerPickRange = { 4f, 5f, 6f, 7f, 8f };
        [SerializeField] int[] playerDismantleDamage = { 1, 1, 1, 2, 2 };
        [SerializeField, Range(0.5f, 1f), Tooltip("各级加工耗时乘区（取对应级，钳到 efficiencyFloor）")]
        float[] playerProcessMult = { 1f, 0.97f, 0.94f, 0.91f, 0.88f };
        [SerializeField, Range(0.5f, 1f)] float[] playerCraftMult = { 1f, 0.97f, 0.94f, 0.91f, 0.88f };
        [SerializeField, Tooltip("达到该等级起每级白送的零耗时次数")]
        int playerFreeChargeFromLevel = 3;
        [SerializeField] int playerFreeChargePerLevel = 1;
        [SerializeField, Tooltip("从当前级升到下一级消耗的金属零件（索引=当前等级-1）")]
        int[] playerUpgradeMetalCost = { 8, 12, 18, 26, 36 };
        [SerializeField, Tooltip("从当前级升到下一级消耗的电子元件（索引=当前等级-1）")]
        int[] playerUpgradeElectronicCost = { 4, 6, 10, 15, 20 };

        [Header("加工 / 制造 [待拍板 C2 C3 C7]")]
        [SerializeField] float[] conversionRate = { 0.5f, 0.5f, 1f, 0.5f };
        [SerializeField] int[] minProcessBatch = { 2, 2, 1, 2 };
        [SerializeField] float processSeconds = 2.5f;     // 加工耗时减半：原 5f
        [SerializeField] float[] craftSeconds = { 2f, 1.25f, 3f };    // 建材板 / 肥料包 / 过滤器（两次减半：原 8/5/12 → 4/2.5/6 → 2/1.25/3）
        [SerializeField] float repairPerItem = 0.08f;                  // NUM-004
        [SerializeField, Tooltip("批量加工/制造时耗时是否固定为「一件的时长」：勾上 = 数量只影响消耗与产出，不叠加时间")]
        bool batchFixedDuration = true;
        [SerializeField] int[] upgradeMetalCost = { 5, 10 };           // [待拍板 C7] 原值 ÷10
        [SerializeField] int[] upgradeElectronicCost = { 6, 12 };

        [Header("伙伴狗 MECH-014/015 NUM-010")]
        [SerializeField] float[] dogFetchRadius = { 1.5f, 2.5f, 3.5f };
        [SerializeField] float[] dogFetchCooldown = { 4f, 3f, 2f };
        [SerializeField] float dogFollowDistance = 1.8f;                // 仅用于建场景时的出生点，不再驱动跟随

        // 自主拾荒：狗以玩家当前位置为中心、在活动半径内自己找垃圾，超出就被引导回来。
        // 半径必须 ≥ 最大嗅探半径，否则会出现「闻得到、够不着」的贴边死循环；OnValidate 会钳。
        // 取 5 m 而不是贴着嗅探上限的 4 m：阶段三嗅探 3.5 m 只占 0.7R，狗不会总在边界上直线冲刺。
        [SerializeField, Tooltip("狗的活动半径（球面弧长·米）：以玩家当前位置为中心，默认 5 m")]
        float dogRoamRadius = 5f;
        [SerializeField, Tooltip("漫游步速（米/秒）")] float dogRoamSpeed = 2.6f;
        [SerializeField, Tooltip("追击垃圾 / 越界回位的跑速（米/秒）")] float dogRunSpeed = 6.2f;
        [SerializeField, Tooltip("叼取判定距离（球面弧长·米）：追到这么近才真正拾取")] float dogPickDistance = 0.6f;
        [SerializeField] float dogHeight = 0.55f;                      // ART-011 体高 ≤ 0.6
        [SerializeField] string dogColorHex = "#D8C3A0";               // ART-012 全程唯一固有色

        // MECH-017 / ART-014 狗加戏：周期性探路入画 + 程序化动作。
        // 注意：这里只动 transform，不改 fixedMaterial / Renderer，ART-012 固定外观仍然成立。
        [SerializeField, Tooltip("两次探路之间的最小间隔（秒）")] float dogScoutInterval = 18f;
        [SerializeField, Tooltip("一次探路在身前停留的时长（秒）")] float dogScoutHold = 2.5f;
        [SerializeField, Tooltip("探路站位：玩家前方距离（米）")] float dogScoutForward = 2.2f;
        [SerializeField, Tooltip("探路站位：玩家右侧偏移（米）")] float dogScoutSide = 0.9f;
        [SerializeField, Tooltip("玩家静止多久触发一次探路（秒）")] float dogScoutIdleTrigger = 1.2f;
        [SerializeField, Tooltip("小跑时根物体沿法线的起伏幅度（米）")] float dogBobMeters = 0.01f;
        [SerializeField, Tooltip("叼回成功瞬间根物体的前探距离（米）")] float dogFetchLunge = 0.12f;
        [SerializeField, Tooltip("尾巴摆动频率（Hz）")] float dogTailWagHz = 2.2f;
        [SerializeField, Tooltip("尾巴常态摆幅（度）")] float dogTailWagDeg = 14f;
        [SerializeField, Tooltip("叼回成功后尾巴瞬时摆幅（度）")] float dogTailBoostDeg = 26f;
        [SerializeField, Tooltip("四肢步态频率（Hz，移动时）")] float dogLegGaitHz = 4.5f;
        [SerializeField, Tooltip("四肢步态摆幅（度）")] float dogLegGaitDeg = 12f;
        [SerializeField, Tooltip("看向垃圾时头部附加偏航（度）")] float dogHeadLookDeg = 12f;

        [Header("功能色 ART-004")]
        [SerializeField] string highlightHex = "#FFFFFF"; // 白色描边（去旧蓝环）
        [SerializeField] string bagWarningHex = "#E2603C";
        [SerializeField] string durabilityHex = "#F2C14E";

        [Header("三阶段色板 ART-003 / 光照 ART-005")]
        [SerializeField] string[] groundHex = { "#B56F32", "#A79A45", "#67B83A" };
        [SerializeField] string[] waterHex = { "#3A4A4E", "#4E7A6E", "#4FB3C4" };
        [SerializeField] string[] skyHex = { "#9A9083", "#A8B5A0", "#8FD3DE" };
        [SerializeField] string[] fogHex = { "#8C8375", "#9AA694", "#B8DDE4" };
        [SerializeField] float[] fogDistance = { 40f, 60f, 100f };
        [SerializeField] float[] lightAngle = { 25f, 45f, 60f };

        [Header("星球净化 MECH-011 / LEV-001 [待拍板]")]
        [SerializeField, Min(0f), Tooltip("中转站周围初始已净化半径（球面弧长·米），progress=0 时即生效")]
        float purifyBaseRadius = 5f;
        [SerializeField, Tooltip("是否显示净化区边界标记（一圈小点，随进度沿球面张开）；纯视觉，无碰撞")]
        bool purifyShowBoundary = false;

        [Header("动效时长 ART-008 / 9.5")]
        [SerializeField] float stageFadeSeconds = 3f;
        [SerializeField] float stageFlashSeconds = 2.5f;
        [SerializeField] float pickupFloatSeconds = 0.25f;
        [SerializeField] float depositSeconds = 0.4f;

        public float PlanetRadius => Mathf.Max(1f, planetRadius);
        public float MvpProgressTarget => mvpProgressTarget;
        /// <summary>地表并发上限（不会被全局总量顶掉，两者取小生效）。</summary>
        public int SurfaceTrashTarget(int stage)
        {
            stage = Mathf.Clamp(stage, 0, 2);
            return surfaceTrashTargets != null && stage < surfaceTrashTargets.Length
                ? Mathf.Max(1, surfaceTrashTargets[stage]) : 3000 - 500 * stage;
        }
        public float ScatterMinSpacing => Mathf.Max(0.1f, scatterMinSpacing);
        public float ScatterPlayerClearance => Mathf.Max(0f, scatterPlayerClearance);
        public float ScatterTiltDegrees => Mathf.Clamp(scatterTiltDegrees, 0f, 20f);
        public float SpawnPopSeconds => Mathf.Max(0.05f, spawnPopSeconds);
        public float SpawnSinkDepth => Mathf.Max(0f, spawnSinkDepth);
        public float SpawnStartScale => Mathf.Clamp(spawnStartScale, 0.05f, 1f);
        /// <summary>true = 批量加工/制造固定按「一件的时长」结算，数量不叠加时间。</summary>
        public bool BatchFixedDuration => batchFixedDuration;
        public int[] BackpackCap => backpackCap;
        /// <summary>背包等级上限 = 人物等级上限（容量已与人物等级绑定，不再有独立的背包等级）。</summary>
        public int BagMaxLevel => PlayerLevelMax;

        // 背包固定 4 类：塑 / 金 / 有 / 电。所有容量数组都以此长度为准。
        const int CapSlots = 4;

        // 容量按等级实时计算；UI 每帧都会读，因此按「等级」分行缓存，避免每帧 new 数组。
        // 分行而不是复用一个数组：不同等级各自独立，调用方持有某一级的引用后不会被下一次计算改写。
        int[][] capRows;

        // 每级增量的兜底值：旧资产可能没有该字段（运行时 OnValidate 不执行），按 3/3/2/2 保证升级确实扩容。
        static readonly int[] CapPerLevelDefault = { 3, 3, 2, 2 };

        /// <summary>按下标安全取元素：数组为 null、下标越界时返回兜底值，绝不抛 IndexOutOfRange。</summary>
        static int AtSlot(int[] a, int slot, int fallback)
            => a != null && slot >= 0 && slot < a.Length ? a[slot] : fallback;

        /// <summary>按等级取每类容量：初始容量 + 每级增量 ×(等级-1)；越界等级钳到 1..人物上限。返回长度恒为 4 的数组。</summary>
        public int[] BackpackCapAt(int level)
        {
            int maxLv = Mathf.Max(1, BagMaxLevel);
            int lv = Mathf.Clamp(level, 1, maxLv);

            if (capRows == null || capRows.Length != maxLv)
            {
                var rows = new int[maxLv][];
                if (capRows != null) Array.Copy(capRows, rows, Mathf.Min(capRows.Length, maxLv));
                capRows = rows;
            }

            int idx = lv - 1;
            if (idx < 0) idx = 0;                                  // 极端配置下 lv 兜底为 1，这里再保险一次
            if (idx >= capRows.Length) idx = capRows.Length - 1;

            int[] row = idx >= 0 ? capRows[idx] : null;
            if (row != null && row.Length == CapSlots) return row;

            row = new int[CapSlots];
            for (int i = 0; i < CapSlots; i++)
            {
                int baseCap = AtSlot(backpackCap, i, 10);
                int per = AtSlot(backpackCapPerLevel, i, AtSlot(CapPerLevelDefault, i, 0));
                row[i] = Mathf.Max(1, baseCap + Mathf.Max(0, per) * (lv - 1));
            }
            if (idx >= 0) capRows[idx] = row;

#if UNITY_EDITOR
            // 配置数组长度不对时只提示一次性质的问题，方便在 Inspector 里发现，不影响运行。
            if (backpackCap == null || backpackCap.Length != CapSlots)
                Debug.LogWarning("[WhiteboxConfig] backpackCap 长度应为 " + CapSlots + "，实际为 "
                    + (backpackCap == null ? "null" : backpackCap.Length.ToString()) + "，已按兜底值计算背包容量。", this);
#endif
            return row;
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
        /// <summary>
        /// 大件占比的默认曲线（下标 0 = Lv.1）。既是 Inspector 的初始值，
        /// 也是「旧资产没写这个字段」时按等级逐个兜底的来源。
        /// </summary>
        static readonly float[] LargeSpawnRatioCurve = { 0.03f, 0.06f, 0.10f, 0.16f, 0.22f };

        /// <summary>
        /// 按人物等级取大型垃圾生成占比。等级越界时钳到 1..上限（超出取末项）。
        /// 数组缺失或长度不够时，按 <see cref="LargeSpawnRatioCurve"/> 逐级兜底，
        /// 而不是所有等级都退化成同一个值（那会让 Lv.4 看起来和 Lv.1 一样冷清）。
        /// </summary>
        public float LargeSpawnRatioAt(int level)
        {
            int idx = Mathf.Clamp(level, 1, Mathf.Max(1, PlayerLevelMax)) - 1;
            float v = largeSpawnRatio != null && idx < largeSpawnRatio.Length
                ? largeSpawnRatio[idx]
                : LargeSpawnRatioCurve[Mathf.Clamp(idx, 0, LargeSpawnRatioCurve.Length - 1)];
            return Mathf.Clamp(v, 0f, 0.5f);
        }

        /// <summary>
        /// 静默补齐大件的最小角距（度）。只替换与玩家角距超过该值的小件。
        /// 旧资产缺该字段时读出来是 0，按 60° 兜底；下限 35° 是地平线（R=8、眼高 1.6 时约 34°），
        /// 再小就会在视野里看见垃圾变形。
        /// </summary>
        public float LargeCatchUpMinAngle
            => Mathf.Clamp(largeCatchUpMinAngle > 0.01f ? largeCatchUpMinAngle : 60f, 35f, 160f);

        /// <summary>静默补齐速率（件/秒）。旧资产缺该字段时为 0，按 6 兜底。</summary>
        public float LargeCatchUpPerSecond
            => Mathf.Clamp(largeCatchUpPerSecond > 0.01f ? largeCatchUpPerSecond : 6f, 0.5f, 30f);

        /// <summary>
        /// 启动时调用一次：把旧 .asset 里缺失的新字段迁移成当前默认值。
        /// 新增 SerializeField 不会自动写进已存在的资产，资产里查不到这个键时读出来是空数组；
        /// 只靠访问器兜底虽然不会崩，但 Inspector 里看到的仍是空表，容易误判成「配了没生效」。
        /// 迁移后会把版本号置 1，编进包里的运行时也不会再走这段。
        /// </summary>
        public void MigrateDefaults()
        {
            if (spawnRatioVersion >= 1 && largeSpawnRatio != null && largeSpawnRatio.Length == PlayerLevelMax) return;

            var curve = new float[Mathf.Max(1, PlayerLevelMax)];
            for (int i = 0; i < curve.Length; i++)
            {
                float kept = largeSpawnRatio != null && i < largeSpawnRatio.Length ? largeSpawnRatio[i] : 0f;
                curve[i] = kept > 0.0001f
                    ? kept
                    : LargeSpawnRatioCurve[Mathf.Clamp(i, 0, LargeSpawnRatioCurve.Length - 1)];
            }
            largeSpawnRatio = curve;
            spawnRatioVersion = 1;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
        public float HighlightRange => highlightRange;

        public int[] SpecUnlockProcessCount => specUnlockProcessCount;
        public int SpecUpgradeMaterialCost => Mathf.Max(1, specUpgradeMaterialCost);
        public int SpecUpgradeMetalCost => Mathf.Max(0, specUpgradeMetalCost);
        public float SpecRangeBonus => Mathf.Max(0f, specRangeBonus);
        public int SpecYieldBonus => Mathf.Max(0, specYieldBonus);
        public int SpecDamageBonus => Mathf.Max(0, specDamageBonus);
        public float SpecProcessMult => Mathf.Clamp(specProcessMult, 0.5f, 1f);
        public float SpecCraftMult => Mathf.Clamp(specCraftMult, 0.5f, 1f);
        public float PickRangeCap => Mathf.Max(1f, pickRangeCap);
        public int DismantleDamageCap => Mathf.Max(1, dismantleDamageCap);
        public float EfficiencyFloor => Mathf.Clamp(efficiencyFloor, 0.5f, 1f);
        public float GripperRangeBonus => Mathf.Max(0f, gripperRangeBonus);
        public int MagnetFirstHitBonus => Mathf.Max(0, magnetFirstHitBonus);
        public int PurifierChargesMax => Mathf.Clamp(purifierChargesMax, 0, 3);
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

        /// <summary>最大嗅探半径（各阶段取最大）：活动半径不得小于它。</summary>
        public float DogFetchRadiusMax
        {
            get
            {
                float m = 0f;
                if (dogFetchRadius != null)
                    for (int i = 0; i < dogFetchRadius.Length; i++) m = Mathf.Max(m, dogFetchRadius[i]);
                return m > 0.01f ? m : 3.5f;
            }
        }

        /// <summary>
        /// 活动半径（球面弧长·米）。旧 .asset 没有该字段（反序列化为 0），这里兜底 5 m，
        /// 并保证不小于最大嗅探半径，避免「界外有目标、走不到又被拽回」的来回打转。
        /// </summary>
        public float DogRoamRadius
        {
            get
            {
                float v = dogRoamRadius > 0.01f ? dogRoamRadius : 5f;
                return Mathf.Max(DogFetchRadiusMax, v);
            }
        }
        public float DogRoamSpeed => dogRoamSpeed > 0.01f ? dogRoamSpeed : 2.6f;
        public float DogRunSpeed => dogRunSpeed > 0.01f ? Mathf.Max(dogRunSpeed, DogRoamSpeed) : 6.2f;
        public float DogPickDistance => dogPickDistance > 0.01f ? Mathf.Clamp(dogPickDistance, 0.2f, 2f) : 0.6f;

        /// <summary>按阶段取嗅探半径；数组意外变短时兜底 2 m，绝不抛越界。</summary>
        public float DogFetchRadiusAt(int stage)
        {
            stage = Mathf.Clamp(stage, 0, 2);
            if (dogFetchRadius == null || dogFetchRadius.Length == 0) return 2f;
            return dogFetchRadius[Mathf.Min(stage, dogFetchRadius.Length - 1)];
        }

        /// <summary>按阶段取叼取冷却；数组意外变短时兜底 3 s。</summary>
        public float DogFetchCooldownAt(int stage)
        {
            stage = Mathf.Clamp(stage, 0, 2);
            if (dogFetchCooldown == null || dogFetchCooldown.Length == 0) return 3f;
            return dogFetchCooldown[Mathf.Min(stage, dogFetchCooldown.Length - 1)];
        }
        public float DogHeight => dogHeight;
        public Color DogColor => Hex(dogColorHex);
        public float DogScoutInterval => dogScoutInterval;
        public float DogScoutHold => dogScoutHold;
        public float DogScoutForward => dogScoutForward;
        public float DogScoutSide => dogScoutSide;
        public float DogScoutIdleTrigger => dogScoutIdleTrigger;
        public float DogBobMeters => dogBobMeters;
        public float DogFetchLunge => dogFetchLunge;
        public float DogTailWagHz => dogTailWagHz;
        public float DogTailWagDeg => dogTailWagDeg;
        public float DogTailBoostDeg => dogTailBoostDeg;
        public float DogLegGaitHz => dogLegGaitHz;
        public float DogLegGaitDeg => dogLegGaitDeg;
        public float DogHeadLookDeg => dogHeadLookDeg;
        public Color HighlightColor => Hex(highlightHex);
        public Color BagWarningColor => Hex(bagWarningHex);
        public Color DurabilityColor => Hex(durabilityHex);
        public Color[] GroundColors => HexArray(groundHex);
        public Color[] WaterColors => HexArray(waterHex);
        public Color[] SkyColors => HexArray(skyHex);
        public Color[] FogColors => HexArray(fogHex);
        public float[] FogDistance => fogDistance;
        public float[] LightAngle => lightAngle;
        public float PurifyBaseRadius => Mathf.Max(0f, purifyBaseRadius);
        public bool PurifyShowBoundary => purifyShowBoundary;

        // ---- 人物等级（C27）----
        public const int PlayerLevelHardCap = 20;
        public int PlayerLevelMax => Mathf.Clamp(playerLevelMax, 1, PlayerLevelHardCap);
        public int ExpPerCollect => Mathf.Max(0, expPerCollect);
        public int ExpPerDismantle => Mathf.Max(0, expPerDismantle);
        public int ExpPerProcess => Mathf.Max(0, expPerProcess);
        public int ExpPerCraft => Mathf.Max(0, expPerCraft);
        public int ExpPerRepair => Mathf.Max(0, expPerRepair);
        public int PlayerFreeChargeFromLevel => Mathf.Max(1, playerFreeChargeFromLevel);
        public int PlayerFreeChargePerLevel => Mathf.Max(0, playerFreeChargePerLevel);
        public int PlayerUpgradeMetalCostAt(int level) => At(playerUpgradeMetalCost, level, 8);
        public int PlayerUpgradeElectronicCostAt(int level) => At(playerUpgradeElectronicCost, level, 4);
        /// <summary>升到 level 级所需的历史累计经验；数组越界取最后一项（顶级封顶）。</summary>
        public int PlayerLevelExpAt(int level)
        {
            int i = Mathf.Clamp(level - 1, 0, PlayerLevelMax - 1);
            if (playerLevelExp == null || playerLevelExp.Length == 0) return 0;
            return Mathf.Max(0, playerLevelExp[Mathf.Min(i, playerLevelExp.Length - 1)]);
        }
        static float At(float[] arr, int level, float fallback)
        {
            if (arr == null || arr.Length == 0) return fallback;
            return arr[Mathf.Clamp(level - 1, 0, arr.Length - 1)];
        }
        static int At(int[] arr, int level, int fallback)
        {
            if (arr == null || arr.Length == 0) return fallback;
            return arr[Mathf.Clamp(level - 1, 0, arr.Length - 1)];
        }
        public float PlayerPickRangeAt(int level) => Mathf.Max(0.1f, At(playerPickRange, level, 4f));
        public int PlayerDismantleDamageAt(int level) => Mathf.Max(1, At(playerDismantleDamage, level, 1));
        public float PlayerProcessMultAt(int level) => Mathf.Clamp(At(playerProcessMult, level, 1f), 0.5f, 1f);
        public float PlayerCraftMultAt(int level) => Mathf.Clamp(At(playerCraftMult, level, 1f), 0.5f, 1f);

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
            capRows = null;       // 配置在 Inspector 改动后让容量缓存失效，下一帧按新值重算
            planetRadius = Mathf.Max(1f, planetRadius);
            scatterMinSpacing = Mathf.Max(0.1f, scatterMinSpacing);
            scatterPlayerClearance = Mathf.Max(0f, scatterPlayerClearance);
            scatterTiltDegrees = Mathf.Clamp(scatterTiltDegrees, 0f, 20f);
            spawnPopSeconds = Mathf.Max(0.05f, spawnPopSeconds);
            spawnSinkDepth = Mathf.Max(0f, spawnSinkDepth);
            spawnStartScale = Mathf.Clamp(spawnStartScale, 0.05f, 1f);
            repairPerItem = Mathf.Clamp01(repairPerItem);
            purifyBaseRadius = Mathf.Max(0f, purifyBaseRadius);
            highlightRange = Mathf.Max(0.1f, highlightRange);
            mvpProgressTarget = Mathf.Clamp01(mvpProgressTarget);
            stageOneThreshold = Mathf.Clamp(stageOneThreshold, 0.01f, stageTwoThreshold - 0.01f);
            stageTwoThreshold = Mathf.Clamp(stageTwoThreshold, stageOneThreshold + 0.01f, 0.99f);

            Ensure(ref backpackCap, 4, 12);
            // 每级增量：旧资产无此字段（null/长度不足）时补 3/3/2/2（合计 +10），保证升级后容量确实变大
            if (backpackCapPerLevel == null || backpackCapPerLevel.Length != 4)
            {
                var per = new int[4];
                int[] def = { 3, 3, 2, 2 };
                for (int i = 0; i < 4; i++)
                    per[i] = backpackCapPerLevel != null && i < backpackCapPerLevel.Length && backpackCapPerLevel[i] > 0
                        ? backpackCapPerLevel[i] : def[i];
                backpackCapPerLevel = per;
            }
            Ensure(ref backpackCapMax, 4, 25);
            Ensure(ref pickRange, 3, 4f);
            Ensure(ref dismantleDamage, 3, 1);
            Ensure(ref largeDurability, 3, 5);
            Ensure(ref conversionRate, 4, 0.5f);
            Ensure(ref minProcessBatch, 4, 2);
            Ensure(ref craftSeconds, 3, 5f);
            Ensure(ref dogFetchRadius, 3, 1.5f);
            Ensure(ref dogFetchCooldown, 3, 4f);
            // 人物等级（C27）：旧资产缺字段时补默认，再钳制
            playerLevelMax = Mathf.Clamp(playerLevelMax <= 0 ? 5 : playerLevelMax, 1, PlayerLevelHardCap);
            Ensure(ref playerLevelExp, PlayerLevelMax, 20);
            Ensure(ref playerPickRange, PlayerLevelMax, 4f);
            Ensure(ref playerDismantleDamage, PlayerLevelMax, 1);
            Ensure(ref playerProcessMult, PlayerLevelMax, 1f);
            Ensure(ref playerCraftMult, PlayerLevelMax, 1f);
            Ensure(ref playerUpgradeMetalCost, PlayerLevelMax, 8);
            Ensure(ref playerUpgradeElectronicCost, PlayerLevelMax, 4);
            for (int i = 0; i < playerLevelExp.Length; i++) playerLevelExp[i] = Mathf.Max(0, playerLevelExp[i]);
            for (int i = 1; i < playerLevelExp.Length; i++)
                if (playerLevelExp[i] <= playerLevelExp[i - 1]) playerLevelExp[i] = playerLevelExp[i - 1] + 10; // 确保严格递增
            for (int i = 0; i < playerPickRange.Length; i++) playerPickRange[i] = Mathf.Max(0.1f, playerPickRange[i]);
            for (int i = 0; i < playerDismantleDamage.Length; i++) playerDismantleDamage[i] = Mathf.Max(1, playerDismantleDamage[i]);
            for (int i = 0; i < playerProcessMult.Length; i++) playerProcessMult[i] = Mathf.Clamp(playerProcessMult[i], 0.5f, 1f);
            for (int i = 0; i < playerCraftMult.Length; i++) playerCraftMult[i] = Mathf.Clamp(playerCraftMult[i], 0.5f, 1f);
            // MECH-017 / ART-014：旧资产缺字段时补默认，再钳制；确保值 0 是用户主动关闭而非缺字段
            if (dogScoutInterval <= 0f) dogScoutInterval = 18f;
            if (dogScoutHold <= 0f) dogScoutHold = 2.5f;
            if (dogScoutForward <= 0f) dogScoutForward = 2.2f;
            if (dogScoutSide <= 0f) dogScoutSide = 0.9f;
            if (dogScoutIdleTrigger <= 0f) dogScoutIdleTrigger = 1.2f;
            if (dogBobMeters < 0f) dogBobMeters = 0.01f;
            if (dogFetchLunge < 0f) dogFetchLunge = 0.12f;
            if (dogTailWagHz <= 0f) dogTailWagHz = 2.2f;
            if (dogTailWagDeg < 0f) dogTailWagDeg = 14f;
            if (dogTailBoostDeg < 0f) dogTailBoostDeg = 26f;
            if (dogLegGaitHz <= 0f) dogLegGaitHz = 4.5f;
            if (dogLegGaitDeg < 0f) dogLegGaitDeg = 12f;
            if (dogHeadLookDeg < 0f) dogHeadLookDeg = 12f;
            dogScoutInterval = Mathf.Clamp(dogScoutInterval, 5f, 120f);
            dogScoutHold = Mathf.Clamp(dogScoutHold, 0.5f, 10f);
            dogScoutForward = Mathf.Clamp(dogScoutForward, 1.5f, 4f);
            dogScoutSide = Mathf.Clamp(dogScoutSide, 0.2f, 2f);
            dogScoutIdleTrigger = Mathf.Clamp(dogScoutIdleTrigger, 0.3f, 10f);
            dogBobMeters = Mathf.Clamp(dogBobMeters, 0f, 0.03f);
            dogFetchLunge = Mathf.Clamp(dogFetchLunge, 0f, 0.3f);
            dogTailWagHz = Mathf.Clamp(dogTailWagHz, 0.1f, 8f);
            dogTailWagDeg = Mathf.Clamp(dogTailWagDeg, 0f, 30f);
            dogTailBoostDeg = Mathf.Clamp(dogTailBoostDeg, 0f, 45f);
            dogLegGaitHz = Mathf.Clamp(dogLegGaitHz, 0.5f, 10f);
            dogLegGaitDeg = Mathf.Clamp(dogLegGaitDeg, 0f, 25f);
            dogHeadLookDeg = Mathf.Clamp(dogHeadLookDeg, 0f, 20f);
            Ensure(ref fogDistance, 3, 40f);
            Ensure(ref lightAngle, 3, 25f);
            Ensure(ref upgradeMetalCost, 2, 5);
            Ensure(ref upgradeElectronicCost, 2, 6);
            Ensure(ref specUnlockProcessCount, 4, 6);
            specUnlockProcessCount[2] = Mathf.Max(1, Mathf.Min(specUnlockProcessCount[2], 6));
            specUpgradeMaterialCost = Mathf.Max(1, specUpgradeMaterialCost);
            specUpgradeMetalCost = Mathf.Max(0, specUpgradeMetalCost);
            specRangeBonus = Mathf.Max(0f, specRangeBonus);
            specYieldBonus = Mathf.Max(0, specYieldBonus);
            specDamageBonus = Mathf.Max(0, specDamageBonus);
            specProcessMult = Mathf.Clamp(specProcessMult, 0.5f, 1f);
            specCraftMult = Mathf.Clamp(specCraftMult, 0.5f, 1f);
            pickRangeCap = Mathf.Max(1f, pickRangeCap);
            dismantleDamageCap = Mathf.Max(1, dismantleDamageCap);
            efficiencyFloor = Mathf.Clamp(efficiencyFloor, 0.5f, 1f);
            gripperRangeBonus = Mathf.Max(0f, gripperRangeBonus);
            magnetFirstHitBonus = Mathf.Max(0, magnetFirstHitBonus);
            purifierChargesMax = Mathf.Clamp(purifierChargesMax, 0, 3);

            for (int i = 0; i < 4; i++)
            {
                backpackCap[i] = Mathf.Max(1, backpackCap[i]);
                // 每级增量不得为负，否则升级反而变小
                if (backpackCapPerLevel != null && i < backpackCapPerLevel.Length)
                    backpackCapPerLevel[i] = Mathf.Max(0, backpackCapPerLevel[i]);
                // 旧版两级制字段：仅兼容旧资产，保证不低于初始即可
                backpackCapMax[i] = Mathf.Max(backpackCap[i], backpackCapMax[i]);
                minProcessBatch[i] = Mathf.Max(1, minProcessBatch[i]);
                conversionRate[i] = Mathf.Clamp01(conversionRate[i]);
            }
            for (int i = 0; i < 3; i++)
            {
                pickRange[i] = Mathf.Max(0.1f, pickRange[i]);
                dismantleDamage[i] = Mathf.Max(1, dismantleDamage[i]);
                largeDurability[i] = Mathf.Max(1, largeDurability[i]);
                dogFetchCooldown[i] = Mathf.Max(0.1f, dogFetchCooldown[i]);
                dogFetchRadius[i] = Mathf.Max(0.1f, dogFetchRadius[i]);
                fogDistance[i] = Mathf.Max(1f, fogDistance[i]);
            }

            // 大件占比：补齐到人物等级上限，缺的项按默认曲线补（不是统一填同一个值），再钳到 0~0.5
            int lvCount = Mathf.Max(1, PlayerLevelMax);
            if (largeSpawnRatio == null || largeSpawnRatio.Length != lvCount)
            {
                var fixedRatio = new float[lvCount];
                for (int i = 0; i < lvCount; i++)
                {
                    float kept = largeSpawnRatio != null && i < largeSpawnRatio.Length ? largeSpawnRatio[i] : 0f;
                    fixedRatio[i] = kept > 0.0001f
                        ? kept
                        : LargeSpawnRatioCurve[Mathf.Clamp(i, 0, LargeSpawnRatioCurve.Length - 1)];
                }
                largeSpawnRatio = fixedRatio;
            }
            for (int i = 0; i < largeSpawnRatio.Length; i++)
                largeSpawnRatio[i] = Mathf.Clamp(largeSpawnRatio[i], 0f, 0.5f);
            // 静默补齐：角距不得小于地平线（R=8、眼高 1.6 时约 34°），否则会在视野里看见垃圾变形
            largeCatchUpMinAngle = Mathf.Clamp(largeCatchUpMinAngle > 0.01f ? largeCatchUpMinAngle : 60f, 35f, 160f);
            largeCatchUpPerSecond = Mathf.Clamp(largeCatchUpPerSecond > 0.01f ? largeCatchUpPerSecond : 6f, 0.5f, 30f);

            // 狗的活动半径：不得小于最大嗅探半径（否则界外目标反复锁死），上限给到 3 倍星球半径防手滑
            float roamMin = DogFetchRadiusMax;
            float roamMax = Mathf.Max(roamMin, planetRadius * 3f);
            dogRoamRadius = Mathf.Clamp(dogRoamRadius > 0.01f ? dogRoamRadius : 5f, roamMin, roamMax);
            dogRoamSpeed = Mathf.Max(0.2f, dogRoamSpeed);
            dogRunSpeed = Mathf.Max(dogRoamSpeed, dogRunSpeed);
            dogPickDistance = Mathf.Clamp(dogPickDistance, 0.2f, 2f);
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
