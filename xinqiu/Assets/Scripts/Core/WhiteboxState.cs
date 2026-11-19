// @trace NUM-001, NUM-004, NUM-007
using System;
using System.IO;
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    public enum TrashCategory { Plastic = 0, Metal = 1, Organic = 2, Electronic = 3 }

    /// <summary>
    /// 运行时状态与全部可复算公式。数值不写死，一律来自 WhiteboxConfig。
    /// 存档为单文件 JSON（策划案 §4.2 / §12），原子写入 + 版本字段 + 读失败回退。
    /// </summary>
    [Serializable]
    public sealed class WhiteboxState
    {
        const string SaveFileName = "trash_planet_whitebox_save.json";

        public int version = 1;
        public int[] bag = new int[4];        // 背包：4 类垃圾
        public int bagLevel = 1;              // 背包等级 1..2（总容量 20 → 40），在中转站升级
        public int[] raw = new int[4];        // 中转站：已存入待加工的垃圾
        public int[] materials = new int[4];  // 再生塑料 / 金属零件 / 有机原料 / 电子元件
        public int toolLevel = 1;
        public float landProgress;
        public float waterProgress;
        public int collected;
        /// <summary>累计生成过的垃圾件数（含开局铺设、含玩家已捡走/拆掉的）：全局总量 1500 的计数。</summary>
        public int totalSpawned;
        public int processed;
        public int crafted;
        public int dismantled;
        public int boards;
        public int fertilizer;
        public int filters;
        public int processedMask;
        public float playTime;
        public bool stationRepaired;
        public bool visitedStation;
        public float movedDistance;
        public int questStep;
        public bool finished;

        // 新增玩法：垃圾堆隐藏款 + 转盘奖励（version 保持 1，旧档读进来这些字段为 0/null，Load 里补默认值）
        public int gachaTickets;                 // 可用抽奖券（发现隐藏物品获得，1 券 = 1 抽）
        public int gachaHiddenFound;             // 累计发现隐藏物品次数
        public int gachaDraws;                   // 累计抽奖次数
        public int gachaPity;                    // 当前距上一次出史诗的连续抽数（硬/软保底共用）
        public string[] ownedRewardIds = new string[0];   // 奖励入库：条目 id
        public int[] ownedRewardCounts = new int[0];      // 奖励入库：与 ids 平行的持有数量

        /// <summary>奖励入库：新增/累加一件奖励，按 id 去重计数。</summary>
        public void AddReward(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (ownedRewardIds == null) ownedRewardIds = new string[0];
            if (ownedRewardCounts == null) ownedRewardCounts = new int[0];
            for (int i = 0; i < ownedRewardIds.Length; i++)
            {
                if (ownedRewardIds[i] == id && i < ownedRewardCounts.Length)
                {
                    ownedRewardCounts[i] += 1;
                    return;
                }
            }
            Array.Resize(ref ownedRewardIds, ownedRewardIds.Length + 1);
            Array.Resize(ref ownedRewardCounts, ownedRewardCounts.Length + 1);
            ownedRewardIds[ownedRewardIds.Length - 1] = id;
            ownedRewardCounts[ownedRewardCounts.Length - 1] = 1;
        }

        public int RewardCount(string id)
        {
            if (string.IsNullOrEmpty(id) || ownedRewardIds == null) return 0;
            for (int i = 0; i < ownedRewardIds.Length; i++)
                if (ownedRewardIds[i] == id && i < ownedRewardCounts.Length) return ownedRewardCounts[i];
            return 0;
        }

        public int RewardKinds => ownedRewardIds != null ? ownedRewardIds.Length : 0;

        /// <summary>NUM-001：collected = min(batch, capacityLeft[cat])，0 时目标不消失。</summary>
        public int Collect(WhiteboxConfig cfg, int cat, int batch)
        {
            int index = Mathf.Clamp(cat, 0, 3);
            int left = Mathf.Max(0, cfg.BackpackCapAt(bagLevel)[index] - bag[index]);
            int n = Mathf.Max(0, Mathf.Min(Mathf.Max(0, batch), left));
            if (n <= 0) return 0;
            bag[index] += n;
            collected += n;
            return n;
        }

        public bool CanCollect(WhiteboxConfig cfg, int cat)
        {
            int index = Mathf.Clamp(cat, 0, 3);
            return bag[index] < cfg.BackpackCapAt(bagLevel)[index];
        }

        /// <summary>当前等级下背包总容量（4 类相加），HUD / 升级提示共用。</summary>
        public int BagCapTotal(WhiteboxConfig cfg) => cfg.BackpackTotalCap(bagLevel);

        /// <summary>全局垃圾总量是否已经用尽（累计生成 ≥ 配置上限 → 地表不再出现新垃圾）。</summary>
        public bool TrashBudgetSpent(WhiteboxConfig cfg) =>
            cfg != null && totalSpawned >= cfg.TotalTrashBudget;

        /// <summary>当前背包已装总数。</summary>
        public int BagTotal()
        {
            int sum = 0;
            for (int i = 0; i < bag.Length; i++) sum += bag[i];
            return sum;
        }

        /// <summary>升级后容量是否装不下现有物品（容量只增不减，理论上不会溢出；留作保险）。</summary>
        public void ClampBagToCap(WhiteboxConfig cfg)
        {
            for (int i = 0; i < 4; i++) bag[i] = Mathf.Clamp(bag[i], 0, cfg.BackpackCapAt(bagLevel)[i]);
        }

        /// <summary>NUM-004：progress = min(1.0, p + items × 0.10)，负值钳到 0，溢出钳到 1。</summary>
        public static float AddProgress(WhiteboxConfig cfg, float current, int items)
        {
            return Mathf.Clamp01(Mathf.Max(0f, current) + Mathf.Max(0, items) * cfg.RepairPerItem);
        }

        public static string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

        public static WhiteboxState Load()
        {
            try
            {
                if (!File.Exists(SavePath)) return new WhiteboxState();
                WhiteboxState s = JsonUtility.FromJson<WhiteboxState>(File.ReadAllText(SavePath));
                if (s == null || s.version != 1) return new WhiteboxState();
                s.bag = Fix(s.bag);
                s.raw = Fix(s.raw);
                s.materials = Fix(s.materials);
                // 旧存档没有 bagLevel 字段（JsonUtility 反序列化为 0），统一归一到 1..2
                s.bagLevel = Mathf.Clamp(s.bagLevel <= 0 ? 1 : s.bagLevel, 1, WhiteboxConfig.BagMaxLevel);
                s.totalSpawned = Mathf.Max(0, s.totalSpawned);
                // 新增玩法字段：旧档反序列化为 0/null，这里归一到安全默认值
                s.gachaTickets = Mathf.Max(0, s.gachaTickets);
                s.gachaHiddenFound = Mathf.Max(0, s.gachaHiddenFound);
                s.gachaDraws = Mathf.Max(0, s.gachaDraws);
                s.gachaPity = Mathf.Max(0, s.gachaPity);
                if (s.ownedRewardIds == null) s.ownedRewardIds = new string[0];
                if (s.ownedRewardCounts == null) s.ownedRewardCounts = new int[0];
                if (s.ownedRewardCounts.Length < s.ownedRewardIds.Length)
                    Array.Resize(ref s.ownedRewardCounts, s.ownedRewardIds.Length);
                for (int i = 0; i < s.ownedRewardCounts.Length; i++)
                    s.ownedRewardCounts[i] = Mathf.Max(0, s.ownedRewardCounts[i]);
                return s;
            }
            catch (Exception e)
            {
                Debug.LogError("[WhiteboxState] 读档失败，回退新局：" + e.Message);
                return new WhiteboxState();
            }
        }

        public static void Save(WhiteboxState s)
        {
            try
            {
                string dir = Path.GetDirectoryName(SavePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string tmp = SavePath + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(s, true));
                if (File.Exists(SavePath)) File.Delete(SavePath);
                File.Move(tmp, SavePath);
                Debug.Log("[WhiteboxState] 已存档：" + SavePath);
            }
            catch (Exception e)
            {
                Debug.LogError("[WhiteboxState] 存档失败：" + e.Message);
            }
        }

        public static void Delete()
        {
            try { if (File.Exists(SavePath)) File.Delete(SavePath); }
            catch (Exception e) { Debug.LogWarning("[WhiteboxState] 删档失败：" + e.Message); }
        }

        static int[] Fix(int[] a)
        {
            if (a != null && a.Length == 4) return a;
            var r = new int[4];
            if (a != null) Array.Copy(a, r, Mathf.Min(a.Length, 4));
            return r;
        }
    }
}
