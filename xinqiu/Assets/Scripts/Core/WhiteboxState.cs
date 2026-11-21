// @trace NUM-001, NUM-004, NUM-007, TOOL-006, BAG-001, BAG-002, COIN-001, NUM-018, SAVE-001, SAVE-002
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
        public int bagLevel = 1;              // 背包等级 1..2（总容量 50 → 100），在中转站升级
        public int[] raw = new int[4];        // 中转站：已存入待加工的垃圾
        public int[] materials = new int[4];  // 再生塑料 / 金属零件 / 有机原料 / 电子元件
        public int toolLevel = 1;
        public float landProgress;
        public float waterProgress;
        public int collected;
        /// <summary>累计生成过的垃圾件数（含开局铺设、含玩家已捡走/拆掉的）：全局总量 2000 的计数。</summary>
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

        // 工具系统（version 保持 1；旧档缺字段时 Load 归一，不新增存档版本）
        public int[] toolLevels = new int[4];             // T-02~T-05 专精等级 0/1/2
        public int toolActive;                            // 当前生效的专精 0..3（Q 轮换）
        public int[] processedByCat = new int[4];         // 各类别累计加工次数（专精 Lv.1 解锁）
        public int freeJobCharges;                        // T-08 / 技能书提供的零耗时次数
        public int toolRefundMask;                        // 重复工具返券位掩码（5 bit，总上限 5 券）
        public int coins;                                 // 金币：独立交易系统余额（C22 仅有接口，本轮启用）
        public string[] toolBuyIds = new string[0];       // 预留：购买记录
        public int[] toolBuyCounts = new int[0];          // 预留：购买次数

        // 独立工具/技能书背包（BAG-001/002）：与垃圾背包 bag[4] 无关，只管理携带格子。
        // 归属真值仍是 ownedRewardIds/Counts，这里只记「哪一格放了什么、几件」。
        public string[] bagSlotIds = new string[0];       // 每格的 rewardId / toolId，空格为 ""
        public int[] bagSlotCounts = new int[0];          // 与格子平行的数量（技能书可叠层）

        // 独立金币交易（COIN-001）：余额 coins 在上文，这里记交易流水（时间用累计分钟，避免时区依赖）
        public string[] tradeItemIds = new string[0];     // 流水：物品 id
        public int[] tradeKinds = new int[0];             // 流水：0=购买 1=出售
        public int[] tradePrices = new int[0];            // 流水：单价
        public int[] tradeAmounts = new int[0];           // 流水：数量
        public float[] tradeTimes = new float[0];         // 流水：发生时 playTime
        public const int TradeLogCap = 40;                // 仅保留最近 40 条，存档不膨胀

        // 多槽存档（SAVE-002）：玩家位置与视角。全部为世界坐标/世界旋转。
        // 读档时把位置投影回球面（PlayerController 每帧 AlignToShell），旋转交给相机 Rig 重新取 forward，
        // 因此直接存四元数即可，不必在球面上重建 yaw/pitch。
        public float playerPosX, playerPosY, playerPosZ;         // 玩家世界坐标
        public float playerRotX, playerRotY, playerRotZ, playerRotW; // 玩家世界旋转
        public float camRotX, camRotY, camRotZ, camRotW;         // 相机世界旋转（决定视角朝向）
        public bool hasPlayerPose;                                // 旧档无此字段 → false，读档时不还原位置

        /// <summary>旧档归一：数组补位、负值清零、等级/掩码钳制。不搬家 rewardId，不升 version。</summary>
        public void NormalizeTools()
        {
            toolLevels = Fix(toolLevels);
            processedByCat = Fix(processedByCat);
            for (int i = 0; i < 4; i++)
            {
                toolLevels[i] = Mathf.Clamp(toolLevels[i], 0, 2);
                processedByCat[i] = Mathf.Max(0, processedByCat[i]);
            }
            toolActive = Mathf.Clamp(toolActive, 0, 3);
            freeJobCharges = Mathf.Max(0, freeJobCharges);
            toolRefundMask = Mathf.Clamp(toolRefundMask, 0, 31);
            coins = Mathf.Max(0, coins);
            if (toolBuyIds == null) toolBuyIds = new string[0];
            if (toolBuyCounts == null) toolBuyCounts = new int[0];
            if (toolBuyCounts.Length < toolBuyIds.Length) Array.Resize(ref toolBuyCounts, toolBuyIds.Length);
            for (int i = 0; i < toolBuyCounts.Length; i++) toolBuyCounts[i] = Mathf.Max(0, toolBuyCounts[i]);
        }

        /// <summary>
        /// 背包格子 + 交易流水的旧档归一（BAG-001 / COIN-001）：数组补位、平行数组对齐、
        /// 负值/空格清零、流水截断到最近 N 条。不升 version（保持 1），不碰图鉴归属。
        /// </summary>
        public void NormalizeInventory()
        {
            // 背包格子：旧档缺字段时补空；平行数组对齐、负值清零
            if (bagSlotIds == null) bagSlotIds = new string[0];
            if (bagSlotCounts == null) bagSlotCounts = new int[0];
            if (bagSlotCounts.Length < bagSlotIds.Length) Array.Resize(ref bagSlotCounts, bagSlotIds.Length);
            for (int i = 0; i < bagSlotIds.Length; i++)
            {
                if (bagSlotIds[i] == null) bagSlotIds[i] = "";
                if (bagSlotCounts[i] <= 0) { bagSlotCounts[i] = 0; bagSlotIds[i] = ""; }
            }
            // 交易流水：五段数组对齐并截断到上限（只保留最近 N 条）
            NormalizeTradeLog();
        }

        void NormalizeTradeLog()
        {
            if (tradeItemIds == null) tradeItemIds = new string[0];
            if (tradeKinds == null) tradeKinds = new int[0];
            if (tradePrices == null) tradePrices = new int[0];
            if (tradeAmounts == null) tradeAmounts = new int[0];
            if (tradeTimes == null) tradeTimes = new float[0];
            int n = Mathf.Min(tradeItemIds.Length,
                Mathf.Min(tradeKinds.Length, Mathf.Min(tradePrices.Length,
                    Mathf.Min(tradeAmounts.Length, tradeTimes.Length))));
            if (n > TradeLogCap)
            {
                int skip = n - TradeLogCap;
                Array.Copy(tradeItemIds, skip, tradeItemIds, 0, TradeLogCap);
                Array.Copy(tradeKinds, skip, tradeKinds, 0, TradeLogCap);
                Array.Copy(tradePrices, skip, tradePrices, 0, TradeLogCap);
                Array.Copy(tradeAmounts, skip, tradeAmounts, 0, TradeLogCap);
                Array.Copy(tradeTimes, skip, tradeTimes, 0, TradeLogCap);
                n = TradeLogCap;
            }
            Array.Resize(ref tradeItemIds, n);
            Array.Resize(ref tradeKinds, n);
            Array.Resize(ref tradePrices, n);
            Array.Resize(ref tradeAmounts, n);
            Array.Resize(ref tradeTimes, n);
            for (int i = 0; i < n; i++)
            {
                if (tradeItemIds[i] == null) tradeItemIds[i] = "";
                tradePrices[i] = Mathf.Max(0, tradePrices[i]);
                tradeAmounts[i] = Mathf.Max(0, tradeAmounts[i]);
                tradeTimes[i] = Mathf.Max(0f, tradeTimes[i]);
            }
        }

        /// <summary>追加一条交易流水并裁剪到最近 N 条。</summary>
        public void AddTradeLog(string itemId, int kind, int price, int amount, float at)
        {
            int n = tradeItemIds != null ? tradeItemIds.Length : 0;
            int next = n + 1;
            int start = next > TradeLogCap ? next - TradeLogCap : 0;
            string[] ids2 = new string[next - start];
            int[] kinds2 = new int[next - start];
            int[] prices2 = new int[next - start];
            int[] amounts2 = new int[next - start];
            float[] times2 = new float[next - start];
            if (n > 0)
            {
                Array.Copy(tradeItemIds, start, ids2, 0, Mathf.Min(n - start, ids2.Length));
                Array.Copy(tradeKinds, start, kinds2, 0, Mathf.Min(n - start, kinds2.Length));
                Array.Copy(tradePrices, start, prices2, 0, Mathf.Min(n - start, prices2.Length));
                Array.Copy(tradeAmounts, start, amounts2, 0, Mathf.Min(n - start, amounts2.Length));
                Array.Copy(tradeTimes, start, times2, 0, Mathf.Min(n - start, times2.Length));
            }
            int last = next - start - 1;
            ids2[last] = itemId ?? "";
            kinds2[last] = kind;
            prices2[last] = Mathf.Max(0, price);
            amounts2[last] = Mathf.Max(0, amount);
            times2[last] = Mathf.Max(0f, at);
            tradeItemIds = ids2; tradeKinds = kinds2; tradePrices = prices2;
            tradeAmounts = amounts2; tradeTimes = times2;
        }

        /// <summary>尝试领取某工具的唯一一次返券；已领过返回 false，防循环。</summary>
        public bool TryToolRefund(int bit)
        {
            if (bit < 0 || bit > 4) return false;
            int mask = 1 << bit;
            if ((toolRefundMask & mask) != 0) return false;
            toolRefundMask |= mask;
            return true;
        }

        /// <summary>购买次数查询（金币系统本轮不上线，接口预留）。</summary>
        public int ToolBoughtCount(string toolId)
        {
            if (string.IsNullOrEmpty(toolId) || toolBuyIds == null) return 0;
            for (int i = 0; i < toolBuyIds.Length && i < toolBuyCounts.Length; i++)
                if (toolBuyIds[i] == toolId) return Mathf.Max(0, toolBuyCounts[i]);
            return 0;
        }

        /// <summary>购买记录 +1（金币系统本轮不上线，接口预留）。</summary>
        public void AddToolBought(string toolId)
        {
            if (string.IsNullOrEmpty(toolId)) return;
            if (toolBuyIds == null) toolBuyIds = new string[0];
            if (toolBuyCounts == null) toolBuyCounts = new int[0];
            for (int i = 0; i < toolBuyIds.Length && i < toolBuyCounts.Length; i++)
                if (toolBuyIds[i] == toolId) { toolBuyCounts[i] += 1; return; }
            Array.Resize(ref toolBuyIds, toolBuyIds.Length + 1);
            Array.Resize(ref toolBuyCounts, toolBuyCounts.Length + 1);
            toolBuyIds[toolBuyIds.Length - 1] = toolId;
            toolBuyCounts[toolBuyCounts.Length - 1] = 1;
        }

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

        /// <summary>
        /// 出售物品时扣减归属（COIN-001）：count 归零则移除条目。
        /// 归属是加成/图鉴/抽奖阶段的唯一真值，出售必须走这里，不能只清背包格子，否则可无限刷金币。
        /// </summary>
        public bool ConsumeReward(string id, int n = 1)
        {
            if (string.IsNullOrEmpty(id) || ownedRewardIds == null || ownedRewardCounts == null) return false;
            n = Mathf.Max(0, n);
            if (n <= 0) return false;
            for (int i = 0; i < ownedRewardIds.Length; i++)
            {
                if (ownedRewardIds[i] != id) continue;
                if (ownedRewardCounts[i] < n) return false;
                ownedRewardCounts[i] -= n;
                if (ownedRewardCounts[i] > 0) return true;
                // 归零：从平行数组里移除该条目，避免留下空 id 让 RewardKinds 虚高
                var ids = new string[ownedRewardIds.Length - 1];
                var counts = new int[ownedRewardCounts.Length - 1];
                int k = 0;
                for (int j = 0; j < ownedRewardIds.Length; j++)
                {
                    if (j == i) continue;
                    ids[k] = ownedRewardIds[j];
                    counts[k] = j < ownedRewardCounts.Length ? ownedRewardCounts[j] : 0;
                    k++;
                }
                ownedRewardIds = ids;
                ownedRewardCounts = counts;
                return true;
            }
            return false;
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

        /// <summary>旧档归一：数组补位、版本校验、新增字段补默认值。Load 与多槽读档共用同一份口径。</summary>
        public static WhiteboxState NormalizeLoaded(WhiteboxState s)
        {
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
            s.NormalizeTools();     // 工具系统字段：旧档缺失时补位/钳制，version 仍为 1
            s.NormalizeInventory(); // 背包格子 + 交易流水：旧档补空数组，version 仍为 1
            // 多槽位置字段旧档缺失：hasPlayerPose=false，读档端不还原位置（保持场景默认出生点）
            return s;
        }

        public static WhiteboxState Load()
        {
            // 多槽系统上线后：单文件旧接口读"当前激活槽"，兼容既有系统代码。
            SaveSlotSystem.EnsureLegacyMigrated();
            return SaveSlotSystem.LoadActive();
        }

        public static void Save(WhiteboxState s)
        {
            // 所有既有系统的 Save(s) 都写进当前激活槽；未选槽时留内存不落盘。
            SaveSlotSystem.SaveActive(s);
        }

        /// <summary>写入指定槽（局内"保存到任意槽"用），并把它设为激活槽。</summary>
        public static void Save(WhiteboxState s, int slot)
        {
            SaveSlotSystem.Save(slot, s);
        }

        /// <summary>写入指定槽并显示命名；displayName 传 null 表示保留旧名。</summary>
        public static void Save(WhiteboxState s, int slot, string displayName)
        {
            SaveSlotSystem.Save(slot, s, displayName);
        }

        public static void Delete()
        {
            SaveSlotSystem.DeleteActive();
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
