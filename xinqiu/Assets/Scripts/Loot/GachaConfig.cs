// 新增玩法：垃圾堆隐藏款 + 转盘奖励（本次需求，尚未纳入 design-freeze 编号）
using System;
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>奖励稀有度。普通 / 精英 / 史诗（显示名），权重与保底都按这三档配置。</summary>
    public enum GachaRarity { Common = 0, Rare = 1, Epic = 2 }

    /// <summary>
    /// 圆形转盘的扇区布局（参考图风格）：10 个等分扇区，顺时针从正上开始编号 0..9。
    /// 7 普通（含 2 个"再接再厉"空奖文案扇区）+ 2 精英 + 1 史诗，视觉比例贴近 70/25/5 的稀有度权重。
    /// 这里只定义视觉排布；实际中奖仍由 GachaSystem 的权重 + 保底决定，空奖扇区不改变奖励逻辑。
    /// 生成器（GachaBuilder）与旋转落点（GachaPanelView）共用本定义，避免角度漂移。
    /// </summary>
    public static class WheelLayout
    {
        public const int SectorCount = 10;
        public const float SectorAngle = 360f / SectorCount;

        static readonly GachaRarity[] Rarities =
        {
            GachaRarity.Common, GachaRarity.Common, GachaRarity.Common, GachaRarity.Common,
            GachaRarity.Common, GachaRarity.Common, GachaRarity.Common,
            GachaRarity.Rare, GachaRarity.Rare, GachaRarity.Epic
        };

        static readonly string[] Labels =
        {
            "普通", "再接\n再厉", "普通", "普通", "普通",
            "再接\n再厉", "普通", "精英", "精英", "史诗"
        };

        // 扇区配色：普通=暖色系、精英=冷蓝紫、史诗=正红最高层级、空奖=蓝灰弱化；
        // 相邻扇区色相拉开，整体高饱和糖果色系保证协调且清晰区分。
        static readonly string[] Colors =
        {
            "#FFC93C", // 0 普通  金黄
            "#95A5B5", // 1 空奖  蓝灰（再接再厉，视觉弱化）
            "#FF7043", // 2 普通  珊瑚橙
            "#EC407A", // 3 普通  品红
            "#FFD54F", // 4 普通  浅金
            "#95A5B5", // 5 空奖  蓝灰（再接再厉）
            "#FB8C00", // 6 普通  橘
            "#42A5F5", // 7 精英  亮蓝
            "#7E57C2", // 8 精英  紫
            "#E53935"  // 9 史诗  正红（最高视觉层级）
        };

        public static GachaRarity RarityAt(int i)
            => Rarities[Mathf.Clamp(i, 0, SectorCount - 1)];
        public static string LabelAt(int i)
            => Labels[Mathf.Clamp(i, 0, SectorCount - 1)];
        public static string ColorAt(int i)
            => Colors[Mathf.Clamp(i, 0, SectorCount - 1)];
        /// <summary>扇区中心顺时针角度（0° = 正上）。</summary>
        public static float CenterAngleAt(int i)
            => (Mathf.Clamp(i, 0, SectorCount - 1) + 0.5f) * SectorAngle;

        /// <summary>某档位对应的全部扇区编号，供旋转落点在该档内随机取值。</summary>
        public static int[] IndicesOf(GachaRarity rarity)
        {
            int count = 0;
            for (int i = 0; i < SectorCount; i++) if (Rarities[i] == rarity) count++;
            int[] result = new int[count];
            int k = 0;
            for (int i = 0; i < SectorCount; i++) if (Rarities[i] == rarity) result[k++] = i;
            return result;
        }
    }

    /// <summary>奖励类别：小物件 / 收垃圾武器 / 技能 / 技能书（抽奖专属，可叠层）。</summary>
    public enum GachaKind { Trinket = 0, Weapon = 1, Skill = 2, SkillBook = 3 }

    /// <summary>单个奖励条目。权重越大越容易在同稀有度内被抽到。</summary>
    [Serializable]
    public sealed class GachaReward
    {
        public string id = "";
        public string displayName = "";
        public GachaRarity rarity = GachaRarity.Common;
        public GachaKind kind = GachaKind.Trinket;
        [Range(1, 100)] public int weight = 10;
        [TextArea] public string description = "";
        public string colorHex = "#9AA694";
    }

    /// <summary>一次抽奖的结果，供 UI 展示与日志使用。</summary>
    [Serializable]
    public sealed class GachaDrawResult
    {
        public GachaReward reward;
        public GachaRarity rarity;
        public bool isNew;
        public int ownedCount; // 入库后的持有数量，供 UI 展示

        // 工具系统接入后的扩展字段（旧调用方不读则完全兼容）
        public bool isBook;
        public int stacks;      // 技能书层数 / 工具持有数
        public bool maxed;      // 技能书满层
        public int refundTickets; // 重复工具返券
        public int overflowMaterial; // 满层后折算的材料件数
        public string message;

        public string RarityName
        {
            get
            {
                if (rarity == GachaRarity.Epic) return "史诗";
                if (rarity == GachaRarity.Rare) return "精英";
                return "普通";
            }
        }

        public string KindName
        {
            get
            {
                if (reward == null) return "";
                if (reward.kind == GachaKind.Weapon) return "武器";
                if (reward.kind == GachaKind.Skill) return "技能";
                if (reward.kind == GachaKind.SkillBook) return "技能书";
                return "小物件";
            }
        }

        public string Color
        {
            get
            {
                if (reward != null && !string.IsNullOrEmpty(reward.colorHex)) return reward.colorHex;
                if (rarity == GachaRarity.Epic) return "#E2B84E";
                if (rarity == GachaRarity.Rare) return "#5E9BE8";
                return "#9AA694";
            }
        }
    }

    /// <summary>
    /// 转盘与隐藏款的可调配置（ScriptableObject）。
    /// 数据驱动：概率、权重、保底、奖励池全部在这里，玩法脚本不写死数值。
    /// 由 Tools/垃圾星球白膜/⑪ 生成转盘配置与界面 创建 Assets/Data/GachaConfig.asset。
    /// 若未生成资产，运行时用 CreateDefault() 的代码默认值兜底，保证不崩。
    /// </summary>
    [CreateAssetMenu(fileName = "GachaConfig", menuName = "垃圾星球/转盘与隐藏款配置")]
    public sealed class GachaConfig : ScriptableObject
    {
        [Header("隐藏款发现")]
        [SerializeField, Range(0f, 1f), Tooltip("每次成功拾取/拆解垃圾时，发现隐藏物品的概率")]
        float hiddenFindChance = 0.08f;
        [SerializeField, Min(1), Tooltip("每次发现获得的转盘券数量（1 券 = 1 次抽奖）")]
        int ticketsPerFind = 1;

        [Header("稀有度基础权重（决定抽到大档的概率比例）")]
        [SerializeField, Tooltip("依次对应 普通 / 稀有 / 史诗；非负值，内部归一化")]
        float[] rarityWeights = { 70f, 25f, 5f };

        [Header("保底：硬保底")]
        [SerializeField, Min(1), Tooltip("连续多少抽未出史诗则必出史诗；0 表示关闭硬保底")]
        int hardPityAt = 20;

        [Header("保底：软保底（从第 N 抽起逐步抬高高档权重）")]
        [SerializeField, Min(1), Tooltip("从第几抽开始叠加软保底加成")]
        int softPityStart = 10;
        [SerializeField, Min(0f), Tooltip("软保底每抽给稀有档增加的权重")]
        float softPityRareStep = 1.5f;
        [SerializeField, Min(0f), Tooltip("软保底每抽给史诗档增加的权重")]
        float softPityEpicStep = 0.8f;

        [Header("奖励池（同稀有度内按 weight 加权）")]
        [SerializeField] GachaReward[] rewards;

        public float HiddenFindChance => Mathf.Clamp01(hiddenFindChance);
        public int TicketsPerFind => Mathf.Max(1, ticketsPerFind);
        public int HardPityAt => Mathf.Max(0, hardPityAt);
        public int SoftPityStart => Mathf.Max(1, softPityStart);
        public float SoftPityRareStep => Mathf.Max(0f, softPityRareStep);
        public float SoftPityEpicStep => Mathf.Max(0f, softPityEpicStep);
        public GachaReward[] Rewards => rewards;

        /// <summary>按稀有度取基础权重，越界钳到普通档。</summary>
        public float RarityWeight(GachaRarity rarity)
        {
            int i = (int)rarity;
            if (rarityWeights == null || i < 0 || i >= rarityWeights.Length) return i == 0 ? 70f : 0f;
            return Mathf.Max(0f, rarityWeights[i]);
        }

        void OnValidate()
        {
            hiddenFindChance = Mathf.Clamp01(hiddenFindChance);
            ticketsPerFind = Mathf.Max(1, ticketsPerFind);
            hardPityAt = Mathf.Max(0, hardPityAt);
            softPityStart = Mathf.Max(1, softPityStart);
            softPityRareStep = Mathf.Max(0f, softPityRareStep);
            softPityEpicStep = Mathf.Max(0f, softPityEpicStep);
            if (rarityWeights == null || rarityWeights.Length != 3)
            {
                float[] fixedArr = { 70f, 25f, 5f };
                if (rarityWeights != null) Array.Copy(rarityWeights, fixedArr, Mathf.Min(rarityWeights.Length, 3));
                rarityWeights = fixedArr;
            }
            for (int i = 0; i < 3; i++) rarityWeights[i] = Mathf.Max(0f, rarityWeights[i]);
            if (rewards != null)
            {
                for (int i = 0; i < rewards.Length; i++)
                    if (rewards[i] != null) rewards[i].weight = Mathf.Clamp(rewards[i].weight, 1, 100);
            }
        }

        /// <summary>代码默认配置：资产缺失时的兜底，全部数值与 .asset 默认值一致。</summary>
        public static GachaConfig CreateDefault()
        {
            GachaConfig c = CreateInstance<GachaConfig>();
            c.hiddenFindChance = 0.08f;
            c.ticketsPerFind = 1;
            c.rarityWeights = new[] { 70f, 25f, 5f };
            c.hardPityAt = 20;
            c.softPityStart = 10;
            c.softPityRareStep = 1.5f;
            c.softPityEpicStep = 0.8f;
            c.rewards = DefaultRewards();
            return c;
        }

        static GachaReward[] DefaultRewards()
        {
            return new[]
            {
                // 普通：被人类误丢但其实崭新的小物件（高出现率）
                R("trinket_coin", "崭新的纪念币", GachaRarity.Common, GachaKind.Trinket, 12, "#9AA694", "标签还没拆，像是被连袋子一起丢掉的。"),
                R("trinket_cap", "限量瓶盖", GachaRarity.Common, GachaKind.Trinket, 12, "#9AA694", "收藏款，边缘一点划痕都没有。"),
                R("trinket_eraser", "未拆封橡皮", GachaRarity.Common, GachaKind.Trinket, 11, "#9AA694", "塑封完整，丢它的人一定很着急。"),
                R("trinket_hairpin", "发卡", GachaRarity.Common, GachaKind.Trinket, 10, "#9AA694", "崭新发卡，被误当成垃圾了。"),
                R("trinket_keychain", "星球钥匙扣", GachaRarity.Common, GachaKind.Trinket, 10, "#9AA694", "纪念品，擦一擦就和新的一样。"),
                R("trinket_notebook", "空白小本", GachaRarity.Common, GachaKind.Trinket, 9, "#9AA694", "一页没写，丢得太可惜。"),

                // 稀有：实用武器（中低出现率）
                R("weapon_gripper", "加强拾取夹", GachaRarity.Rare, GachaKind.Weapon, 6, "#5E9BE8", "夹得更稳，拾取范围似乎也变大了。"),
                R("weapon_magnet", "磁铁手套", GachaRarity.Rare, GachaKind.Weapon, 5, "#5E9BE8", "金属垃圾自己飞过来。"),
                R("skill_quickdismantle", "快速分解", GachaRarity.Rare, GachaKind.Skill, 5, "#5E9BE8", "拆解动作快了一截（图鉴展示，不改数值）。"),

                // 史诗：高阶武器 / 技能（低出现率，靠保底兜住）
                R("weapon_purifier", "星球净化器", GachaRarity.Epic, GachaKind.Weapon, 2, "#E2B84E", "传说中能把整颗星球洗干净的神器。"),
                R("skill_ecomaster", "生态专家", GachaRarity.Epic, GachaKind.Skill, 2, "#E2B84E", "一眼就能看出哪块地最需要修复。"),

                // 技能书：抽奖专属、数值弱、可叠层；精英书权重 5 与 weapon_magnet / skill_quickdismantle 同值
                R("book_sorting", "快分拣", GachaRarity.Rare, GachaKind.SkillBook, 5, "#5E9BE8", "加工耗时 ×0.99/层，最多 5 层。"),
                R("book_handwork", "熟手工", GachaRarity.Rare, GachaKind.SkillBook, 5, "#5E9BE8", "制造耗时 ×0.99/层，最多 5 层。"),
                R("book_flow", "一气呵成", GachaRarity.Epic, GachaKind.SkillBook, 2, "#E2B84E", "制造耗时 ×0.98/层，最多 3 层。"),
                R("book_tireless", "不知疲倦", GachaRarity.Epic, GachaKind.SkillBook, 2, "#E2B84E", "满 4 层：零耗时任务 +1 次。")
            };
        }

        static GachaReward R(string id, string name, GachaRarity rarity, GachaKind kind,
            int weight, string color, string desc)
        {
            return new GachaReward
            {
                id = id, displayName = name, rarity = rarity, kind = kind,
                weight = weight, colorHex = color, description = desc
            };
        }
    }
}
