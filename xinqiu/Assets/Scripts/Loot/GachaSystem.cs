// @trace TOOL-005, TOOL-014, TOOL-015, NUM-016
// 新增玩法：垃圾堆隐藏款 + 转盘奖励（本次需求，尚未纳入 design-freeze 编号）
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 老虎机抽奖核心：隐藏款发现 → 获得券 → 抽奖（稀有度权重 + 软/硬保底）→ 奖励入库。
    /// 独立系统，Init(WhiteboxGame) 拿引用，不改任何既有玩法数值与存档格式（只扩展字段）。
    /// 面板不再由 R 键独立开启：只由场景内老虎机实体在玩家走近对准、按 F 时调用 Open() 弹出；
    /// 结算数字与奖品池仍走本系统，UI 表现交给 GachaPanelView。
    /// </summary>
    public sealed class GachaSystem : MonoBehaviour
    {
        [SerializeField] GachaConfig config;
        [SerializeField] GachaPanelView panel;

        WhiteboxGame game;
        ToolSystem tools;
        GachaDrawResult lastResult;

        public GachaConfig Config => config;
        public GachaPanelView Panel => panel;
        public GachaDrawResult LastResult => lastResult;
        public bool IsOpen => panel != null && panel.IsOpen;

        /// <summary>供 Editor 构建脚本注入引用（不手写场景 YAML）。</summary>
        public void Wire(GachaConfig cfg, GachaPanelView view)
        {
            config = cfg;
            panel = view;
        }

        public void Init(WhiteboxGame g)
        {
            game = g;
            if (config == null) config = GachaConfig.CreateDefault();
            tools = g != null ? g.Tool : null; // 由 WhiteboxGame 在 gacha.Init 之前完成 ToolSystem.Init
            lastResult = null;
            if (panel != null) panel.Init(this);
            Debug.Log("[Gacha] 初始化完成：隐藏款概率=" + (config.HiddenFindChance * 100f).ToString("0.0")
                      + "%，奖励池=" + ActivePool().Length
                      + "，硬保底=" + (EpicUnlocked() ? config.HardPityAt.ToString() : "挂起")
                      + " 抽");
        }

        /// <summary>C21：史诗档需修复中转站后才开启；修复前权重 70/30/0，硬保底挂起不清零。</summary>
        bool EpicUnlocked()
        {
            return game != null && game.State != null && game.State.stationRepaired;
        }

        /// <summary>
        /// 活动池：一次性过滤（工具集齐后移出全部工具；修复前移除需门槛的史诗工具/书；
        /// 史诗书在阶段 A 不入池）。不逐件剔除，未集齐时重复件仍在池中走返券。
        /// </summary>
        GachaReward[] ActivePool()
        {
            if (config == null || config.Rewards == null) return new GachaReward[0];
            GachaReward[] all = config.Rewards;
            bool toolsDone = tools != null && tools.AllToolsOwned();
            bool repaired = EpicUnlocked();
            GachaReward[] result = new GachaReward[all.Length];
            int n = 0;
            for (int i = 0; i < all.Length; i++)
            {
                GachaReward r = all[i];
                if (r == null) continue;
                ToolDef td = tools != null && tools.Config != null ? tools.Config.FindByReward(r.id) : null;
                if (td != null && td.gachaEligible)
                {
                    if (toolsDone) continue;                    // 阶段 B：工具全部移出
                    if (td.epicRequiresRepair && !repaired) continue; // C21：修复前史诗工具不入池
                }
                SkillBookDef sb = tools != null && tools.Config != null ? tools.Config.FindBook(r.id) : null;
                if (sb != null && !sb.activeInPhaseA && !toolsDone) continue; // 史诗书只在阶段 B
                result[n++] = r;
            }
            if (n == result.Length) return result;
            GachaReward[] trimmed = new GachaReward[n];
            System.Array.Copy(result, trimmed, n);
            return trimmed;
        }

        // ------------------------------------------------------------------
        // 入口一：搜索垃圾堆发现隐藏款（由 InteractionController 在拾取成功后调用）
        // ------------------------------------------------------------------

        /// <summary>
        /// 每次成功拾取/拆解垃圾调用一次：按概率发现被误丢的崭新隐藏物品，转为抽奖券。
        /// 不改变背包、材料、任务链的任何数值；只写隐藏款相关字段并给 UI 提示。
        /// </summary>
        public void OnTrashSearched(TrashCategory category)
        {
            if (game == null || game.State == null || config == null) return;
            if (Random.value > config.HiddenFindChance) return;

            WhiteboxState state = game.State;
            state.gachaHiddenFound += 1;
            state.gachaTickets += config.TicketsPerFind;
            if (game.Hud != null)
            {
                game.Hud.FloatText("★ 隐藏款 +" + config.TicketsPerFind + "券");
                game.Hud.Notice("搜索垃圾堆时发现被误丢的崭新物品！走近老虎机按 F 抽奖（券：" + state.gachaTickets + "）");
            }
            WhiteboxState.Save(state);
            Debug.Log("[Gacha] 发现隐藏款（" + category + "）：券 +" + config.TicketsPerFind
                      + "，当前券=" + state.gachaTickets);
        }

        // ------------------------------------------------------------------
        // 入口二：老虎机开合（只由场景内 SlotMachine 在游戏里按 F 调用，无常驻快捷键）
        // ------------------------------------------------------------------

        /// <summary>
        /// 由老虎机 F 交互统一入口：先关掉其他面板，再打开本面板，保证一次只弹一个 UI。
        /// 由 WhiteboxGame 转调（面板互斥归它管），避免 GachaSystem 越过仲裁层。
        /// </summary>
        public void Toggle()
        {
            if (panel == null) return;
            if (panel.IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            if (panel == null || panel.IsOpen) return;
            // 打开中转站/暂停时不开面板，避免两个面板叠加
            if (game != null && (game.StationOpen || game.IsPaused)) return;
            panel.Show();
            RefreshPanel();
            if (game != null) game.ApplyUiState(); // 解锁光标、禁用玩家移动（IsUiBlocking 已含本面板）
        }

        /// <summary>
        /// 关闭面板并彻底复位：GachaPanelView.Hide() 停转轮协程+清落点+恢复按钮，
        /// ApplyUiState() 恢复光标锁定与玩家移动；InteractionController 下一帧自然重算准星目标。
        /// </summary>
        public void Close()
        {
            if (panel == null || !panel.IsOpen) return;
            panel.Hide();
            if (game != null) game.ApplyUiState(); // 恢复光标锁定与玩家移动
        }

        public void RefreshPanel()
        {
            if (panel == null || game == null || game.State == null) return;
            panel.Refresh(game.State.gachaTickets, game.State.gachaDraws,
                game.State.gachaPity, EpicUnlocked() ? config.HardPityAt : 0,
                lastResult, game.State, !EpicUnlocked());
        }

        // ------------------------------------------------------------------
        // 抽奖：稀有度权重 + 软保底递增 + 硬保底必出史诗
        // ------------------------------------------------------------------

        public bool CanDraw()
        {
            return game != null && game.State != null && config != null && game.State.gachaTickets > 0;
        }

        /// <summary>点击抽奖按钮：扣 1 券 → 计算稀有度与奖励 → 入库 → 返回展示结果。</summary>
        public GachaDrawResult Draw()
        {
            if (!CanDraw())
            {
                if (game != null && game.Hud != null)
                    game.Hud.Notice("没有转盘券了，去搜索垃圾堆找隐藏款吧");
                return null;
            }

            WhiteboxState state = game.State;
            state.gachaTickets -= 1;
            state.gachaDraws += 1;

            // 本次抽奖前的连续未出史诗数 = 保底计数
            int pityBefore = state.gachaPity;
            GachaRarity rarity = RollRarity(pityBefore);
            GachaReward reward = RollReward(rarity);

            bool isEpic = rarity == GachaRarity.Epic;
            state.gachaPity = isEpic ? 0 : pityBefore + 1; // 出史诗清零，否则累加（硬保底判定用）

            // 统一发放入口：工具 / 技能书 / 普通物件都经 ToolSystem.Grant
            ToolGrantResult grant = tools != null ? tools.Grant(reward != null ? reward.id : "", GrantSource.Gacha)
                                                 : FallbackGrant(reward != null ? reward.id : "");
            int ownedAfter = reward != null ? state.RewardCount(reward.id) : 0;

            lastResult = new GachaDrawResult
            {
                reward = reward,
                rarity = rarity,
                isNew = grant.isNew,
                ownedCount = ownedAfter,
                isBook = grant.isBook,
                stacks = grant.stacks,
                maxed = grant.maxed,
                refundTickets = grant.refundTickets,
                overflowMaterial = grant.overflowMaterial,
                message = grant.message
            };

            WhiteboxState.Save(state);
            RefreshPanel();

            string name = reward != null ? reward.displayName : "（奖励池为空）";
            Debug.Log("[Gacha] 第 " + state.gachaDraws + " 抽 → " + lastResult.RarityName
                      + "・" + name + "｜新获得=" + lastResult.isNew
                      + "，返券=" + lastResult.refundTickets
                      + "，连续未出史诗=" + state.gachaPity + "，剩余券=" + state.gachaTickets);

            if (panel != null) panel.PlaySpin(lastResult);
            return lastResult;
        }

        /// <summary>ToolSystem 未装配时的兜底入库（仅在旧场景未重跑builder时触发，不处理工具扩展）。</summary>
        ToolGrantResult FallbackGrant(string id)
        {
            ToolGrantResult r = new ToolGrantResult
            {
                ok = !string.IsNullOrEmpty(id), id = id, isNew = true, stacks = 1,
                message = "", maxStacks = 1
            };
            if (!r.ok) return r;
            WhiteboxState s = game.State;
            int before = s.RewardCount(id);
            s.AddReward(id);
            r.isNew = before <= 0;
            r.stacks = before + 1;
            return r;
        }

        /// <summary>
        /// 选稀有度。基础权重来自配置；软保底从第 N 抽起抬高稀有/史诗权重；
        /// 硬保底达到阈值时本抽必出史诗。阈值 0 = 关闭硬保底。
        /// </summary>
        GachaRarity RollRarity(int pityBefore)
        {
            int nextPity = pityBefore + 1;
            bool epicOpen = EpicUnlocked();
            // C21：未修复中转站时硬保底挂起（pity 继续累计），修复后第一抽若已达阈值立即兑现
            if (epicOpen && config.HardPityAt > 0 && nextPity >= config.HardPityAt)
                return GachaRarity.Epic;

            float w0 = Mathf.Max(0f, config.RarityWeight(GachaRarity.Common));
            float w1 = Mathf.Max(0f, config.RarityWeight(GachaRarity.Rare));
            float w2 = Mathf.Max(0f, config.RarityWeight(GachaRarity.Epic));

            // 未修复：5% 并入精英档（70/30/0），不制造"抽到史诗却给精英"的空档降级
            if (!epicOpen) { w1 += w2; w2 = 0f; }

            if (pityBefore >= config.SoftPityStart)
            {
                int step = pityBefore - config.SoftPityStart + 1;
                w1 += step * config.SoftPityRareStep;
                w2 += step * config.SoftPityEpicStep;
                if (!epicOpen) { w1 += w2; w2 = 0f; }
            }

            float total = w0 + w1 + w2;
            if (total <= 0f) return GachaRarity.Common;
            float r = Random.value * total;
            if (r < w0) return GachaRarity.Common;
            if (r < w0 + w1) return GachaRarity.Rare;
            return GachaRarity.Epic;
        }

        /// <summary>在目标稀有度的条目中按 weight 加权选一个；该档无条目时退化为全池等权。</summary>
        GachaReward RollReward(GachaRarity rarity)
        {
            GachaReward[] pool = ActivePool();
            if (pool == null || pool.Length == 0) return null;

            int matchCount = 0;
            for (int i = 0; i < pool.Length; i++)
                if (pool[i] != null && pool[i].rarity == rarity) matchCount++;

            if (matchCount == 0)
            {
                // 该稀有度没有配置奖励：退回任意档，保证抽奖有产出
                int idx = Random.Range(0, pool.Length);
                return pool[idx] != null ? pool[idx] : null;
            }

            float total = 0f;
            for (int i = 0; i < pool.Length; i++)
                if (pool[i] != null && pool[i].rarity == rarity) total += Mathf.Max(1, pool[i].weight);

            float r = Random.value * total;
            float acc = 0f;
            for (int i = 0; i < pool.Length; i++)
            {
                if (pool[i] == null || pool[i].rarity != rarity) continue;
                acc += Mathf.Max(1, pool[i].weight);
                if (r <= acc) return pool[i];
            }
            // 浮点误差兜底：返回最后一个匹配项
            for (int i = pool.Length - 1; i >= 0; i--)
                if (pool[i] != null && pool[i].rarity == rarity) return pool[i];
            return null;
        }
    }
}
