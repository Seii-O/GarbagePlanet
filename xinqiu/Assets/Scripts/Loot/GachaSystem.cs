// 新增玩法：垃圾堆隐藏款 + 转盘奖励（本次需求，尚未纳入 design-freeze 编号）
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 转盘玩法核心：隐藏款发现 → 获得券 → 抽奖（稀有度权重 + 软/硬保底）→ 奖励入库。
    /// 独立系统，Init(WhiteboxGame) 拿引用，不改任何既有玩法数值与存档格式（只扩展字段）。
    /// R 键为单独开启键：与 M/ESC 同层，在 WhiteboxGame 的输入仲裁里统一处理，避免同帧冲突。
    /// </summary>
    public sealed class GachaSystem : MonoBehaviour
    {
        [SerializeField] GachaConfig config;
        [SerializeField] GachaPanelView panel;

        WhiteboxGame game;
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
            lastResult = null;
            if (panel != null) panel.Init(this);
            Debug.Log("[Gacha] 初始化完成：隐藏款概率=" + (config.HiddenFindChance * 100f).ToString("0.0")
                      + "%，奖励池=" + (config.Rewards != null ? config.Rewards.Length : 0)
                      + "，硬保底=" + config.HardPityAt + " 抽");
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
                game.Hud.Notice("搜索垃圾堆时发现被误丢的崭新物品！按 R 打开转盘（券：" + state.gachaTickets + "）");
            }
            WhiteboxState.Save(state);
            Debug.Log("[Gacha] 发现隐藏款（" + category + "）：券 +" + config.TicketsPerFind
                      + "，当前券=" + state.gachaTickets);
        }

        // ------------------------------------------------------------------
        // 入口二：转盘开合（R 键由 WhiteboxGame 调用）
        // ------------------------------------------------------------------

        public void Toggle()
        {
            if (panel == null) return;
            if (panel.IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            if (panel == null || panel.IsOpen) return;
            // 打开中转站/暂停时不开转盘，避免两个面板叠加
            if (game != null && (game.StationOpen || game.IsPaused)) return;
            panel.Show();
            RefreshPanel();
            if (game != null) game.ApplyUiState(); // 解锁光标、禁用玩家移动（IsUiBlocking 已含转盘）
        }

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
                game.State.gachaPity, config != null ? config.HardPityAt : 0,
                lastResult, game.State);
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

            int ownedBefore = reward != null ? state.RewardCount(reward.id) : 0;
            if (reward != null) state.AddReward(reward.id);
            int ownedAfter = reward != null ? state.RewardCount(reward.id) : 0;

            lastResult = new GachaDrawResult
            {
                reward = reward,
                rarity = rarity,
                isNew = ownedBefore <= 0,
                ownedCount = ownedAfter
            };

            WhiteboxState.Save(state);
            RefreshPanel();

            string name = reward != null ? reward.displayName : "（奖励池为空）";
            Debug.Log("[Gacha] 第 " + state.gachaDraws + " 抽 → " + lastResult.RarityName
                      + "・" + name + "｜新获得=" + lastResult.isNew
                      + "，连续未出史诗=" + state.gachaPity + "，剩余券=" + state.gachaTickets);

            if (panel != null) panel.PlaySpin(lastResult);
            return lastResult;
        }

        /// <summary>
        /// 选稀有度。基础权重来自配置；软保底从第 N 抽起抬高稀有/史诗权重；
        /// 硬保底达到阈值时本抽必出史诗。阈值 0 = 关闭硬保底。
        /// </summary>
        GachaRarity RollRarity(int pityBefore)
        {
            int nextPity = pityBefore + 1;
            if (config.HardPityAt > 0 && nextPity >= config.HardPityAt)
                return GachaRarity.Epic;

            float w0 = Mathf.Max(0f, config.RarityWeight(GachaRarity.Common));
            float w1 = Mathf.Max(0f, config.RarityWeight(GachaRarity.Rare));
            float w2 = Mathf.Max(0f, config.RarityWeight(GachaRarity.Epic));

            if (pityBefore >= config.SoftPityStart)
            {
                int step = pityBefore - config.SoftPityStart + 1;
                w1 += step * config.SoftPityRareStep;
                w2 += step * config.SoftPityEpicStep;
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
            GachaReward[] pool = config.Rewards;
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
