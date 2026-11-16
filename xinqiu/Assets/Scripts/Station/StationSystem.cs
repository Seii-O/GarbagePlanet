// @trace MECH-007, MECH-009, MECH-012, MECH-001, NUM-004
using System;
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 中转站 2D 经营逻辑（白膜用 IMGUI 呈现，逻辑在这里）。
    /// 流程：背包 → 存入(raw) → 加工(materials) → 制造 → 修复 → 升级。
    /// [待拍板 C2/C3/C7] 转化比例、过滤器配方、升级消耗 ÷10 全部是可配置默认值，不是定稿。
    /// [待确认 C10] 暂停时后台计时一并冻结；面板打开但没暂停时后台继续走（策划案 §5.3）。
    /// </summary>
    public sealed class StationSystem : MonoBehaviour
    {
        WhiteboxGame game;
        Action onDone;
        string jobLabel = "";
        float jobLeft;
        float jobTotal = 1f;

        public bool HasJob => onDone != null;
        public string JobLabel => jobLabel;
        public float JobProgress => HasJob ? 1f - Mathf.Clamp01(jobLeft / jobTotal) : 0f;

        public static readonly string[] CategoryNames = { "塑料", "金属", "有机", "电子" };
        public static readonly string[] MaterialNames = { "再生塑料", "金属零件", "有机原料", "电子元件" };
        public static readonly string[] RecipeNames = { "再生建材板", "生态肥料包", "水质过滤器" };

        public void Init(WhiteboxGame g)
        {
            game = g;
            onDone = null;
            jobLabel = "";
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
            jobLabel = label;
            jobTotal = Mathf.Max(0.1f, seconds);
            jobLeft = jobTotal;
            onDone = done;
            game.Notice("开始：" + label + "（" + jobTotal.ToString("0.#") + " s）");
        }

        public bool DepositAll()
        {
            WhiteboxState s = game.State;
            int sum = 0;
            for (int i = 0; i < 4; i++)
            {
                sum += s.bag[i];
                s.raw[i] += s.bag[i];
                s.bag[i] = 0;
            }
            if (sum <= 0) { game.Notice("背包是空的"); return false; }
            game.Notice("已投放 " + sum + " 件垃圾进中转站");
            game.Hud.ShowDeposit(game.Config.DepositSeconds);
            game.OnWorldEvent();
            return true;
        }

        public bool StartProcess(int cat)
        {
            if (cat < 0 || cat > 3) return false;
            WhiteboxConfig cfg = game.Config;
            WhiteboxState s = game.State;
            int batch = cfg.MinProcessBatch[cat];
            if (s.raw[cat] < batch)
            {
                game.Notice("需要至少 " + batch + " 件" + CategoryNames[cat] + "垃圾");
                return false;
            }
            if (HasJob) { game.Notice("同一时间只允许 1 个加工任务"); return false; }

            s.raw[cat] -= batch;
            int output = Mathf.Max(1, Mathf.FloorToInt(batch * cfg.ConversionRate[cat]));
            StartJob("加工" + CategoryNames[cat], cfg.ProcessSeconds, () =>
            {
                s.materials[cat] += output;
                s.processed += batch;
                s.processedMask |= 1 << cat;
                game.Notice("加工完成：+" + output + " " + MaterialNames[cat]);
            });
            return true;
        }

        public bool StartCraft(int recipe)
        {
            if (recipe < 0 || recipe > 2) return false;
            WhiteboxConfig cfg = game.Config;
            WhiteboxState s = game.State;

            if (recipe > 0 && !s.stationRepaired)
            {
                game.Notice("中转站尚未修复：先制造 1 块再生建材板");
                return false;
            }
            if (HasJob) { game.Notice("同一时间只允许 1 个制造任务"); return false; }

            int m0 = 0, m1 = 0, m2 = 0, m3 = 0;
            if (recipe == 0) { m0 = 2; m1 = 1; }
            else if (recipe == 1) { m2 = 3; }
            else { m0 = 2; m1 = 2; m3 = 1; }

            if (s.materials[0] < m0 || s.materials[1] < m1 ||
                s.materials[2] < m2 || s.materials[3] < m3)
            {
                game.Notice("材料不足：" + RecipeNames[recipe]);
                return false;
            }

            s.materials[0] -= m0;
            s.materials[1] -= m1;
            s.materials[2] -= m2;
            s.materials[3] -= m3;
            StartJob("制造" + RecipeNames[recipe], cfg.CraftSeconds[recipe], () =>
            {
                s.crafted += 1;
                if (recipe == 0)
                {
                    s.boards += 1;
                    if (!s.stationRepaired)
                    {
                        s.stationRepaired = true;
                        game.Notice("中转站已修复，2D 经营全部解锁");
                    }
                }
                else if (recipe == 1)
                {
                    s.fertilizer += 1;
                    s.landProgress = WhiteboxState.AddProgress(cfg, s.landProgress, 1); // NUM-004
                    game.Eco.Refresh(true);
                }
                else
                {
                    s.filters += 1;
                    s.waterProgress = WhiteboxState.AddProgress(cfg, s.waterProgress, 1);
                    game.Eco.Refresh(true);
                }
                game.Notice("制造完成：" + RecipeNames[recipe]);
            });
            return true;
        }

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

        public string CostText(int recipe)
        {
            if (recipe == 0) return "2 再生塑料 + 1 金属零件";
            if (recipe == 1) return "3 有机原料";
            return "2 再生塑料 + 2 金属零件 + 1 电子元件";
        }
    }
}
