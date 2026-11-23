// @trace MECH-007, MECH-009, MECH-012, MECH-001, NUM-004, NUM-007, TOOL-002, BAG-003
using System;
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    public enum StationAction { None = 0, Process = 1, Craft = 2 }

    /// <summary>
    /// 中转站 2D 经营逻辑（白膜用 uGUI / IMGUI 呈现，逻辑在这里）。
    /// 流程：背包 → 存入(raw) → 加工(materials) → 制造 → 修复 → 升级。
    ///
    /// 本次改动：
    /// 1. 加工 / 制造都支持「一次任务连续做 N 次」：消耗与产出按 N 倍结算，
    ///    耗时默认固定为「一件的时长」（WhiteboxConfig.batchFixedDuration = true），
    ///    也就是做多少件都只等一次；改成 false 才回到按 N 倍线性叠加的旧行为。
    ///    仍然遵守「同一时间只允许 1 个任务」，进度条复用同一个 job 计时。
    /// 2. 点击加工/制造按钮不再直接执行 1 次，而是进入「待确认」状态（Begin*），
    ///    由数量选择弹窗（StationQuantityDialog）读 Max / 当前值，确认后调 ConfirmPending。
    ///    单次执行的旧入口 StartProcess / StartCraft 保留，IMGUI 回退与外部调用不受影响。
    /// 3. 新增背包升级（50 → 100，共 2 级，15 金属零件）。
    /// [待拍板 C2/C3/C7] 转化比例、过滤器配方、升级消耗全部是可配置默认值，不是定稿。
    /// </summary>
    public sealed class StationSystem : MonoBehaviour
    {
        WhiteboxGame game;
        Action onDone;
        string jobLabel = "";
        float jobLeft;
        float jobTotal = 1f;

        StationAction pending = StationAction.None;
        int pendingIndex;
        int pendingMax;
        int pendingCount = 1;

        public bool HasJob => onDone != null;
        public string JobLabel => jobLabel;
        public float JobLeft => jobLeft;
        public float JobTotal => jobTotal;
        public float JobProgress => HasJob ? 1f - Mathf.Clamp01(jobLeft / jobTotal) : 0f;

        public static readonly string[] CategoryNames = { "塑料", "金属", "有机", "电子" };
        public static readonly string[] MaterialNames = { "再生塑料", "金属零件", "有机原料", "电子元件" };
        public static readonly string[] RecipeNames = { "再生建材板", "生态肥料包", "水质过滤器" };

        // ---------------- 待确认的数量选择（弹窗数据源） ----------------

        public bool HasPending => pending != StationAction.None;
        public StationAction PendingAction => pending;
        public int PendingIndex => pendingIndex;
        public int PendingMax => pendingMax;
        /// <summary>弹窗里当前选中的次数；写入即钳到 1..PendingMax。</summary>
        public int PendingCount
        {
            get => Mathf.Clamp(pendingCount, 1, Mathf.Max(1, pendingMax));
            set => pendingCount = Mathf.Clamp(value, 1, Mathf.Max(1, pendingMax));
        }

        public string PendingTitle
        {
            get
            {
                if (!HasPending) return "";
                return pending == StationAction.Process
                    ? "加工" + CategoryNames[Mathf.Clamp(pendingIndex, 0, 3)]
                    : "制造" + RecipeNames[Mathf.Clamp(pendingIndex, 0, 2)];
            }
        }

        /// <summary>弹窗里的一行结算预览：消耗 / 产出 / 预计耗时。</summary>
        public string PendingDetail => DetailText(pending, pendingIndex, PendingCount);

        public void Init(WhiteboxGame g)
        {
            game = g;
            onDone = null;
            jobLabel = "";
            ClearPending();
        }

        void Update()
        {
            if (!HasJob || game == null || game.IsPaused) return;
            jobLeft -= Time.deltaTime;
            if (jobLeft > 0f) return;
            Action done = onDone;
            onDone = null;
            jobLabel = "";
            done?.Invoke();
            game.OnWorldEvent();
        }

        void StartJob(string label, float seconds, Action done)
        {
            if (HasJob) { game.Notice("同一时间只允许 1 个制造/加工任务"); return; }
            bool free = false;
            if (game.Tool != null && game.Tool.TryConsumeFreeCharge())
            {
                seconds = 0f;
                free = true;
            }
            jobLabel = label;
            jobTotal = Mathf.Max(0.1f, seconds);
            jobLeft = jobTotal;
            onDone = done;
            game.Notice("开始：" + label + (free ? "（净化器：零耗时）" : "（" + jobTotal.ToString("0.#") + " s）"));
        }

        // ---------------- ① 投放 ----------------

        public bool DepositAll()
        {
            WhiteboxState s = game.State;
            int sum = 0;
            for (int i = 0; i < 4; i++)
            {
                sum += s.bag[i];
                s.raw[i] += s.bag[i];
                s.bag[i] = 0;
                // BAG-003：投放后单品明细必须同步清空，否则独立垃圾背包会显示"已投放"的旧数量
                int a = GarbageBagCatalog.FirstItemOf(i);
                if (s.bagItems != null && a + 1 < s.bagItems.Length)
                {
                    s.bagItems[a] = 0;
                    s.bagItems[a + 1] = 0;
                }
            }
            if (sum <= 0) { game.Notice("背包是空的"); return false; }
            game.Notice("已投放 " + sum + " 件垃圾进中转站");
            game.Hud.ShowDeposit(game.Config.DepositSeconds);
            game.OnWorldEvent();
            return true;
        }

        // ---------------- ② 加工：可执行次数 / 待确认 / 执行 ----------------

        /// <summary>当前库存最多能连续加工几次（按 minProcessBatch 折算）。</summary>
        public int MaxProcessBatches(int cat)
        {
            if (game == null || cat < 0 || cat > 3) return 0;
            int batch = Mathf.Max(1, game.Config.MinProcessBatch[cat]);
            return Mathf.Max(0, game.State.raw[cat] / batch);
        }

        /// <summary>点击「加工X」：不立刻扣材料，先把数量交给弹窗让玩家确认。</summary>
        public bool BeginProcess(int cat, int count = -1)
        {
            if (game == null || cat < 0 || cat > 3) return false;
            if (HasJob) { game.Notice("同一时间只允许 1 个加工任务"); return false; }
            int max = MaxProcessBatches(cat);
            if (max <= 0)
            {
                game.Notice("需要至少 " + game.Config.MinProcessBatch[cat] + " 件" + CategoryNames[cat] + "垃圾");
                ClearPending();
                return false;
            }
            pending = StationAction.Process;
            pendingIndex = cat;
            pendingMax = max;
            pendingCount = count > 0 ? Mathf.Clamp(count, 1, max) : max; // 默认拉满：一次点击即可完成全部
            return true;
        }

        /// <summary>兼容入口：单个批次直接执行（不经过弹窗）。</summary>
        public bool StartProcess(int cat) => ExecuteProcess(cat, 1);

        /// <summary>一键全部：直接把库存全部加工完。</summary>
        public bool StartProcessAll(int cat) => ExecuteProcess(cat, MaxProcessBatches(cat));

        public bool ExecuteProcess(int cat, int batches)
        {
            if (game == null || cat < 0 || cat > 3) return false;
            WhiteboxConfig cfg = game.Config;
            WhiteboxState s = game.State;
            int batch = Mathf.Max(1, cfg.MinProcessBatch[cat]);
            int max = MaxProcessBatches(cat);
            if (max <= 0 || s.raw[cat] < batch)
            {
                game.Notice("需要至少 " + batch + " 件" + CategoryNames[cat] + "垃圾");
                return false;
            }
            if (HasJob) { game.Notice("同一时间只允许 1 个加工任务"); return false; }

            int n = Mathf.Clamp(batches, 1, max);
            int consumed = batch * n;
            int output = Mathf.Max(1, Mathf.FloorToInt(batch * cfg.ConversionRate[cat])) * n;
            // 批量耗时：配置 batchFixedDuration 打开时固定为「一批的时长」，数量只影响消耗与产出
            float seconds = cfg.BatchFixedDuration ? cfg.ProcessSeconds : cfg.ProcessSeconds * n;
            if (game.Tool != null) seconds *= game.Tool.ProcessMultiplier(cat);

            s.raw[cat] -= consumed;
            StartJob("加工" + CategoryNames[cat] + (n > 1 ? " ×" + n : ""), seconds, () =>
            {
                s.materials[cat] += output;
                s.processed += consumed;
                s.processedByCat[Mathf.Clamp(cat, 0, 3)] += n; // 专精解锁按本次批次次数累计
                s.processedMask |= 1 << cat;
                if (game.Tool != null) game.Tool.UnlockCheck();
                game.Notice("加工完成：+" + output + " " + MaterialNames[cat]
                            + (n > 1 ? "（" + n + " 批）" : ""));
            });
            return true;
        }

        // ---------------- ③ 制造：可执行次数 / 待确认 / 执行 ----------------

        /// <summary>当前材料最多能连续制造几次。</summary>
        public int MaxCraftCount(int recipe)
        {
            if (game == null || recipe < 0 || recipe > 2) return 0;
            WhiteboxState s = game.State;
            if (recipe > 0 && !s.stationRepaired) return 0; // 未修复只能造建材板
            int m0, m1, m2, m3;
            CraftCost(recipe, out m0, out m1, out m2, out m3);
            int max = int.MaxValue;
            max = Mathf.Min(max, m0 > 0 ? s.materials[0] / m0 : int.MaxValue);
            max = Mathf.Min(max, m1 > 0 ? s.materials[1] / m1 : int.MaxValue);
            max = Mathf.Min(max, m2 > 0 ? s.materials[2] / m2 : int.MaxValue);
            max = Mathf.Min(max, m3 > 0 ? s.materials[3] / m3 : int.MaxValue);
            return max == int.MaxValue ? 0 : Mathf.Max(0, max);
        }

        public bool BeginCraft(int recipe, int count = -1)
        {
            if (game == null || recipe < 0 || recipe > 2) return false;
            if (recipe > 0 && !game.State.stationRepaired)
            {
                game.Notice("中转站尚未修复：先制造 1 块再生建材板");
                ClearPending();
                return false;
            }
            if (HasJob) { game.Notice("同一时间只允许 1 个制造任务"); return false; }
            int max = MaxCraftCount(recipe);
            if (max <= 0)
            {
                game.Notice("材料不足：" + RecipeNames[recipe]);
                ClearPending();
                return false;
            }
            pending = StationAction.Craft;
            pendingIndex = recipe;
            pendingMax = max;
            pendingCount = count > 0 ? Mathf.Clamp(count, 1, max) : max;
            return true;
        }

        /// <summary>兼容入口：单个直接制造（不经过弹窗）。</summary>
        public bool StartCraft(int recipe) => ExecuteCraft(recipe, 1);

        public bool StartCraftAll(int recipe) => ExecuteCraft(recipe, MaxCraftCount(recipe));

        public bool ExecuteCraft(int recipe, int times)
        {
            if (game == null || recipe < 0 || recipe > 2) return false;
            WhiteboxConfig cfg = game.Config;
            WhiteboxState s = game.State;

            if (recipe > 0 && !s.stationRepaired)
            {
                game.Notice("中转站尚未修复：先制造 1 块再生建材板");
                return false;
            }
            if (HasJob) { game.Notice("同一时间只允许 1 个制造任务"); return false; }

            int max = MaxCraftCount(recipe);
            if (max <= 0)
            {
                game.Notice("材料不足：" + RecipeNames[recipe]);
                return false;
            }

            int n = Mathf.Clamp(times, 1, max);
            int m0, m1, m2, m3;
            CraftCost(recipe, out m0, out m1, out m2, out m3);
            s.materials[0] -= m0 * n;
            s.materials[1] -= m1 * n;
            s.materials[2] -= m2 * n;
            s.materials[3] -= m3 * n;

            // 批量耗时：与数量无关，做多少件都只用一件的时长（batchFixedDuration = false 时才线性叠加）
            float craftSeconds = cfg.BatchFixedDuration ? cfg.CraftSeconds[recipe] : cfg.CraftSeconds[recipe] * n;
            if (game.Tool != null) craftSeconds *= game.Tool.CraftMultiplier(recipe);
            StartJob("制造" + RecipeNames[recipe] + (n > 1 ? " ×" + n : ""), craftSeconds, () =>
            {
                s.crafted += n;
                if (recipe == 0)
                {
                    s.boards += n;
                    if (!s.stationRepaired)
                    {
                        s.stationRepaired = true;
                        game.Notice("中转站已修复，2D 经营全部解锁");
                    }
                }
                else if (recipe == 1)
                {
                    s.fertilizer += n;
                    s.landProgress = WhiteboxState.AddProgress(cfg, s.landProgress, n); // NUM-004
                    game.Eco.Refresh(true);
                }
                else
                {
                    s.filters += n;
                    s.waterProgress = WhiteboxState.AddProgress(cfg, s.waterProgress, n);
                    game.Eco.Refresh(true);
                }
                game.Notice("制造完成：" + RecipeNames[recipe] + (n > 1 ? " ×" + n : ""));
            });
            return true;
        }

        // ---------------- 弹窗确认 / 取消 ----------------

        /// <summary>弹窗「确认执行」：按当前选中的次数执行一次连续任务。</summary>
        public bool ConfirmPending()
        {
            if (!HasPending) return false;
            StationAction action = pending;
            int index = pendingIndex;
            int n = PendingCount;
            ClearPending();
            return action == StationAction.Process ? ExecuteProcess(index, n) : ExecuteCraft(index, n);
        }

        /// <summary>弹窗「一键全部」：确认执行并直接拉到上限。</summary>
        public bool ConfirmPendingAll()
        {
            if (!HasPending) return false;
            PendingCount = pendingMax;
            return ConfirmPending();
        }

        public void ClearPending()
        {
            pending = StationAction.None;
            pendingIndex = 0;
            pendingMax = 0;
            pendingCount = 1;
        }

        // ---------------- ④ 升级工具 / ⑤ 升级背包 ----------------

        public bool UpgradeTool()
        {
            WhiteboxConfig cfg = game.Config;
            WhiteboxState s = game.State;
            if (!s.stationRepaired) { game.Notice("中转站尚未修复"); return false; }
            if (s.toolLevel >= 3) { game.Notice("工具已满级 Lv.3"); return false; }
            int i = s.toolLevel - 1;
            int metal = cfg.UpgradeMetalCost[i];
            int elec = cfg.UpgradeElectronicCost[i];
            if (s.materials[1] < metal || s.materials[3] < elec)
            {
                game.Notice("升级需要 " + metal + " 金属零件 + " + elec + " 电子元件");
                return false;
            }
            s.materials[1] -= metal;
            s.materials[3] -= elec;
            s.toolLevel += 1;
            game.Notice("工具升级到 Lv." + s.toolLevel + "，拾取范围 " + cfg.PickRange[s.toolLevel - 1] + " m");
            game.OnWorldEvent();
            return true;
        }

        /// <summary>T-02~T-05 专精 Lv.1→Lv.2：6 件该类材料 + 3 金属零件；金属专精合计 9 金属。</summary>
        public bool UpgradeSpecTool(int cat)
        {
            WhiteboxConfig cfg = game.Config;
            WhiteboxState s = game.State;
            cat = Mathf.Clamp(cat, 0, 3);
            if (!s.stationRepaired) { game.Notice("中转站尚未修复"); return false; }
            if (s.toolLevels[cat] <= 0)
            {
                game.Notice("专精尚未解锁：累计加工该类别 " + cfg.SpecUnlockProcessCount[cat] + " 次");
                return false;
            }
            if (s.toolLevels[cat] >= 2)
            {
                game.Notice((game.Tool != null ? game.Tool.SpecToolName(cat) : "专精") + " 已满级 Lv.2");
                return false;
            }
            int materialCost = cfg.SpecUpgradeMaterialCost;
            int metalCost = cfg.SpecUpgradeMetalCost;
            int needMetal = cat == 1 ? materialCost + metalCost : metalCost;
            int needCat = cat == 1 ? 0 : materialCost;
            if (s.materials[1] < needMetal || s.materials[cat] < needCat)
            {
                game.Notice("升级需要 " + (cat == 1 ? needMetal + " 金属零件"
                                                  : materialCost + " " + MaterialNames[cat] + " + " + metalCost + " 金属零件"));
                return false;
            }
            s.materials[1] -= needMetal;
            if (cat != 1) s.materials[cat] -= materialCost;
            s.toolLevels[cat] = 2;
            string name = game.Tool != null ? game.Tool.SpecToolName(cat) : CategoryNames[cat] + "专精";
            game.Notice(name + " 升级到 Lv.2");
            WhiteboxState.Save(s);
            game.OnWorldEvent();
            return true;
        }

        /// <summary>中转站专精按钮文案：未解锁 / 可升级 / 已满级。</summary>
        public string SpecToolUpgradeText(int cat)
        {
            WhiteboxConfig cfg = game.Config;
            WhiteboxState s = game.State;
            cat = Mathf.Clamp(cat, 0, 3);
            string name = game.Tool != null ? game.Tool.SpecToolName(cat) : CategoryNames[cat] + "专精";
            if (s.toolLevels[cat] <= 0)
                return "⑥ " + name + "（加工 " + s.processedByCat[cat] + "/" + cfg.SpecUnlockProcessCount[cat] + " 解锁）";
            if (s.toolLevels[cat] >= 2) return "⑥ " + name + " 已满级 Lv.2";
            int materialCost = cfg.SpecUpgradeMaterialCost;
            int metalCost = cfg.SpecUpgradeMetalCost;
            string cost = cat == 1
                ? (materialCost + metalCost) + " 金属零件"
                : materialCost + " " + MaterialNames[cat] + " + " + metalCost + " 金属零件";
            return "⑥ 升级" + name + "（需 " + cost + "）";
        }

        /// <summary>背包升级：Lv.1（总 50）→ Lv.2（总 100），消耗 15 金属零件，满级后不再可升。</summary>
        public bool UpgradeBackpack()
        {
            WhiteboxConfig cfg = game.Config;
            WhiteboxState s = game.State;
            if (!s.stationRepaired) { game.Notice("中转站尚未修复"); return false; }
            if (s.bagLevel >= WhiteboxConfig.BagMaxLevel)
            {
                game.Notice("背包已满级 Lv." + s.bagLevel + "（总容量 " + cfg.BackpackTotalCap(s.bagLevel) + "）");
                return false;
            }
            int cost = cfg.BackpackUpgradeMetalCost;
            if (s.materials[1] < cost)
            {
                game.Notice("背包升级需要 " + cost + " 金属零件（现有 " + s.materials[1] + "）");
                return false;
            }
            s.materials[1] -= cost;
            s.bagLevel = Mathf.Min(WhiteboxConfig.BagMaxLevel, s.bagLevel + 1);
            s.ClampBagToCap(cfg);
            game.Notice("背包升级到 Lv." + s.bagLevel + "，总容量 " + cfg.BackpackTotalCap(s.bagLevel)
                        + "，每类 " + CapsText(cfg, s.bagLevel));
            game.OnWorldEvent();
            return true;
        }

        /// <summary>中转站「⑤ 升级背包」按钮文案。</summary>
        public string BagUpgradeText()
        {
            WhiteboxConfig cfg = game.Config;
            WhiteboxState s = game.State;
            if (s.bagLevel >= WhiteboxConfig.BagMaxLevel)
                return "⑤ 背包已满级 Lv." + s.bagLevel + "（总容量 " + cfg.BackpackTotalCap(s.bagLevel) + "）";
            return "⑤ 升级背包（总 " + cfg.BackpackTotalCap(s.bagLevel) + " → "
                   + cfg.BackpackTotalCap(s.bagLevel + 1) + "，需 " + cfg.BackpackUpgradeMetalCost
                   + " 金属零件 / 现有 " + s.materials[1] + "）";
        }

        // ---------------- 文案工具 ----------------

        public string CostText(int recipe) => CostText(recipe, 1);

        public string CostText(int recipe, int times)
        {
            int m0, m1, m2, m3;
            CraftCost(recipe, out m0, out m1, out m2, out m3);
            int n = Mathf.Max(1, times);
            string s = "";
            s = Append(s, m0 * n, MaterialNames[0]);
            s = Append(s, m1 * n, MaterialNames[1]);
            s = Append(s, m2 * n, MaterialNames[2]);
            s = Append(s, m3 * n, MaterialNames[3]);
            return string.IsNullOrEmpty(s) ? "无消耗" : s;
        }

        static string Append(string acc, int count, string name)
        {
            if (count <= 0) return acc;
            string one = count + " " + name;
            return string.IsNullOrEmpty(acc) ? one : acc + " + " + one;
        }

        static void CraftCost(int recipe, out int m0, out int m1, out int m2, out int m3)
        {
            m0 = m1 = m2 = m3 = 0;
            if (recipe == 0) { m0 = 2; m1 = 1; }
            else if (recipe == 1) { m2 = 3; }
            else if (recipe == 2) { m0 = 2; m1 = 2; m3 = 1; }
        }

        /// <summary>弹窗预览：动作 + 消耗 + 产出 + 预计耗时。</summary>
        public string DetailText(StationAction action, int index, int count)
        {
            if (game == null || action == StationAction.None) return "";
            WhiteboxConfig cfg = game.Config;
            int n = Mathf.Max(1, count);
            if (action == StationAction.Process)
            {
                int batch = Mathf.Max(1, cfg.MinProcessBatch[Mathf.Clamp(index, 0, 3)]);
                int total = batch * n;
                int output = Mathf.Max(1, Mathf.FloorToInt(batch * cfg.ConversionRate[Mathf.Clamp(index, 0, 3)])) * n;
                return "消耗 " + total + " " + CategoryNames[Mathf.Clamp(index, 0, 3)] + "垃圾  →  "
                       + output + " " + MaterialNames[Mathf.Clamp(index, 0, 3)];
            }
            return "消耗 " + CostText(index, n) + "  →  " + RecipeNames[Mathf.Clamp(index, 0, 2)] + " ×" + n;
        }

        /// <summary>一次任务预计耗时（秒），弹窗里直接显示，避免玩家点了才知道要等多久。</summary>
        public float PendingSeconds()
        {
            if (game == null || !HasPending) return 0f;
            WhiteboxConfig cfg = game.Config;
            int n = PendingCount;
            float seconds;
            if (pending == StationAction.Process)
            {
                int cat = Mathf.Clamp(pendingIndex, 0, 3);
                seconds = cfg.BatchFixedDuration ? cfg.ProcessSeconds : cfg.ProcessSeconds * n;
                if (game.Tool != null) seconds *= game.Tool.ProcessMultiplier(cat);
            }
            else
            {
                int recipe = Mathf.Clamp(pendingIndex, 0, 2);
                float one = cfg.CraftSeconds[recipe];
                if (game.Tool != null) one *= game.Tool.CraftMultiplier(recipe);
                seconds = cfg.BatchFixedDuration ? one : one * n;
            }
            return Mathf.Max(0.1f, seconds);
        }

        /// <summary>耗时是否与数量无关（弹窗据此决定要不要写「与数量无关」）。</summary>
        public bool PendingFixedSeconds()
        {
            return game != null && game.Config != null && game.Config.BatchFixedDuration;
        }

        /// <summary>「每类 13/13/12/12」这种短文案。</summary>
        public static string CapsText(WhiteboxConfig cfg, int level)
        {
            int[] caps = cfg.BackpackCapAt(level);
            return caps != null && caps.Length >= 4
                ? caps[0] + "/" + caps[1] + "/" + caps[2] + "/" + caps[3]
                : "--";
        }
    }
}
