// @trace TOOL-001, TOOL-002, TOOL-003, TOOL-004, TOOL-014
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 工具系统单一服务：配置查询、加成钳制、专精解锁/轮换、统一发放。
    /// 由 WhiteboxGame 在启动时自动 AddComponent 并 Init；不要求改场景 YAML。
    /// 所有基础数值仍来自 WhiteboxConfig / ToolConfig，本类只做查询与钳制。
    /// </summary>
    public sealed class ToolSystem : MonoBehaviour, IToolGrantSink
    {
        WhiteboxGame game;
        ToolConfig config;

        public ToolConfig Config => config;

        public void Init(WhiteboxGame g)
        {
            game = g;
#if UNITY_EDITOR
            if (config == null)
                config = UnityEditor.AssetDatabase.LoadAssetAtPath<ToolConfig>("Assets/Data/ToolConfig.asset");
#endif
            if (config == null) config = ToolConfig.CreateDefault();
            if (g != null && g.State != null) NormalizeState(g.State);
            Debug.Log("[Tool] 初始化完成：工具=" + config.Tools.Length + "，技能书=" + config.Books.Length
                      + "，配置来源=" + (name != null && config != null ? config.name : "默认"));
        }

        /// <summary>供 Editor / 外部显式注入配置资产。</summary>
        public void Wire(ToolConfig cfg)
        {
            config = cfg;
            if (config == null) config = ToolConfig.CreateDefault();
        }

        void NormalizeState(WhiteboxState s)
        {
            s.NormalizeTools();
            // 旧档已有 weapon_gripper 即视为已持有 T-06，不写新字段、不升 version
            if (s.RewardCount("weapon_gripper") <= 0) return;
        }

        // ----------------------------------------------------------------
        // 查询：工具持有 / 技能书层数
        // ----------------------------------------------------------------

        public bool HasTool(string rewardId)
        {
            return game != null && game.State != null && !string.IsNullOrEmpty(rewardId)
                   && game.State.RewardCount(rewardId) > 0;
        }

        public bool HasToolId(string toolId)
        {
            ToolDef def = config != null ? config.Find(toolId) : null;
            return def != null && HasTool(def.rewardId);
        }

        public bool IsCodex(string rewardId)
        {
            ToolDef def = config != null ? config.FindByReward(rewardId) : null;
            return def != null && def.codexOnly;
        }

        public bool AllToolsOwned()
        {
            if (game == null || game.State == null || config == null) return false;
            ToolDef[] tools = config.Tools;
            for (int i = 0; i < tools.Length; i++)
            {
                ToolDef t = tools[i];
                if (t == null || !t.gachaEligible) continue;
                if (game.State.RewardCount(t.rewardId) <= 0) return false;
            }
            return true;
        }

        public int BookStacks(string rewardId)
        {
            if (game == null || game.State == null || config == null) return 0;
            SkillBookDef book = config.FindBook(rewardId);
            if (book == null) return 0;
            return Mathf.Clamp(game.State.RewardCount(rewardId), 0, book.maxStacks);
        }

        public bool BookMaxed(string rewardId)
        {
            SkillBookDef book = config != null ? config.FindBook(rewardId) : null;
            return book != null && BookStacks(rewardId) >= book.maxStacks;
        }

        // ----------------------------------------------------------------
        // 统一发放（抽奖 / 商店 / 任务共用；调用方负责 Save）
        // ----------------------------------------------------------------

        public ToolGrantResult Grant(string id, GrantSource src)
        {
            ToolGrantResult r = new ToolGrantResult
            {
                ok = false, id = id, isNew = false, duplicate = false, isBook = false,
                stacks = 0, maxed = false, refundTickets = 0, overflowMaterial = 0, message = ""
            };
            if (game == null || game.State == null || config == null || string.IsNullOrEmpty(id)) return r;

            WhiteboxState s = game.State;
            int before = s.RewardCount(id);

            SkillBookDef book = config.FindBook(id);
            if (book != null)
            {
                r.isBook = true;
                r.maxStacks = book.maxStacks;
                if (before >= book.maxStacks)
                {
                    // 满层：层数不再增长，折 1 件该线对应材料（阀门可关）
                    if (config.BookOverflowMaterial)
                    {
                        int cat = Mathf.Clamp(book.overflowMaterialCat, 0, 3);
                        s.materials[cat] = Mathf.Max(0, s.materials[cat]) + 1;
                        r.overflowMaterial = 1;
                    }
                    r.duplicate = true;
                    r.maxed = true;
                    r.stacks = book.maxStacks;
                    r.message = "已满层" + (r.overflowMaterial > 0 ? "（+1 材料）" : "（无折算）");
                    r.ok = true;
                    return r;
                }
                s.AddReward(id);
                NotifyBag(id, 1); // 统一发放 → 自动入包（背包满只保留图鉴归属）
                r.isNew = before <= 0;
                r.duplicate = before > 0;
                r.stacks = Mathf.Min(book.maxStacks, before + 1);
                r.maxed = r.stacks >= book.maxStacks;
                r.message = r.isNew ? "技能书入手 · 1 层" : "技能书 +1 层（" + r.stacks + "/" + book.maxStacks + "）";
                r.ok = true;
                return r;
            }

            ToolDef tool = config.FindByReward(id);
            s.AddReward(id);
            NotifyBag(id, 1); // 统一发放 → 自动入包（背包满只保留图鉴归属）
            r.stacks = before + 1;
            r.isNew = before <= 0;
            r.duplicate = before > 0;
            if (tool != null)
            {
                r.maxStacks = 1;
                // C19：重复工具每件限返 1 券，总上限 5；位掩码在 WhiteboxState 里防循环
                int bit = config.RefundBitOf(id);
                if (before == 1 && bit >= 0 && s.TryToolRefund(bit))
                {
                    s.gachaTickets += 1;
                    r.refundTickets = 1;
                }
                if (tool.codexOnly) r.message = r.isNew ? "图鉴条目（不生效）" : "图鉴重复（无数值变化）";
                else if (r.refundTickets > 0) r.message = "重复工具 +1 券";
                else if (r.duplicate) r.message = "重复工具（已计数）";
                else r.message = "工具入手 · 已生效";
            }
            else
            {
                r.maxStacks = 1;
                r.message = r.isNew ? "新获得" : "重复（已计数）";
            }
            r.ok = true;
            return r;
        }

        void NotifyBag(string id, int n)
        {
            if (game != null && game.Bag != null) game.Bag.OnRewardGranted(id, n);
        }

        // ----------------------------------------------------------------
        // 属性加成（加法区，先加后钳）
        // ----------------------------------------------------------------

        public float EffectiveRange(TrashCategory cat)
        {
            WhiteboxConfig cfg = GameConfig;
            WhiteboxState s = GameState;
            if (cfg == null || s == null) return 0f;
            int lv = Mathf.Clamp(s.toolLevel - 1, 0, 2);
            float value = cfg.PickRange[lv];
            if (HasTool("weapon_gripper")) value += cfg.GripperRangeBonus;
            int c = (int)cat;
            if (c == 1 && stateActiveIs(1) && s.toolLevels[1] >= 1) value += cfg.SpecRangeBonus;
            return Mathf.Min(cfg.PickRangeCap, Mathf.Max(0f, value));
        }

        public float MaxRaycastRange()
        {
            float max = 0f;
            for (int i = 0; i < 4; i++) max = Mathf.Max(max, EffectiveRange((TrashCategory)i));
            return max;
        }

        public int EffectiveDamage(TrashCategory cat, bool isFirstHit)
        {
            WhiteboxConfig cfg = GameConfig;
            WhiteboxState s = GameState;
            if (cfg == null || s == null) return 1;
            int lv = Mathf.Clamp(s.toolLevel - 1, 0, 2);
            int damage = cfg.DismantleDamage[lv];
            int c = (int)cat;
            int specBonus = c == 1 && stateActiveIs(1) && s.toolLevels[1] >= 2 ? cfg.SpecDamageBonus : 0;
            int magnetBonus = isFirstHit && HasTool("weapon_magnet") ? cfg.MagnetFirstHitBonus : 0;
            // T-03 与 T-07 取最大，不相加
            damage += Mathf.Max(specBonus, magnetBonus);
            return Mathf.Max(1, Mathf.Min(cfg.DismantleDamageCap, damage));
        }

        public int EffectiveYield(TrashCategory cat)
        {
            WhiteboxConfig cfg = GameConfig;
            WhiteboxState s = GameState;
            if (cfg == null || s == null) return 1;
            int gain = cfg.LargeYield;
            if ((int)cat == 0 && stateActiveIs(0) && s.toolLevels[0] >= 2) gain += cfg.SpecYieldBonus;
            return Mathf.Max(1, Mathf.Min(3, gain));
        }

        // ----------------------------------------------------------------
        // 效率加成（工具同类取最小；技能书独立乘区；整条线钳 0.8）
        // ----------------------------------------------------------------

        public float ProcessMultiplier(int cat)
        {
            WhiteboxConfig cfg = GameConfig;
            WhiteboxState s = GameState;
            if (cfg == null || s == null) return 1f;
            float tool = 1f;
            if (stateActiveIs(cat) && s.toolLevels[Mathf.Clamp(cat, 0, 3)] >= 1)
                tool = Mathf.Min(tool, cfg.SpecProcessMult);
            float book = BookMultiplier(BookEffect.ProcessTime);
            return Mathf.Max(cfg.EfficiencyFloor, tool * book);
        }

        public float CraftMultiplier(int recipe)
        {
            WhiteboxConfig cfg = GameConfig;
            WhiteboxState s = GameState;
            if (cfg == null || s == null) return 1f;
            float tool = 1f;
            int cat = recipe == 1 ? 2 : (recipe == 2 ? 3 : -1);
            if (cat >= 0 && stateActiveIs(cat) && s.toolLevels[cat] >= 2)
                tool = Mathf.Min(tool, cfg.SpecCraftMult);
            float book = BookMultiplier(BookEffect.CraftTime);
            return Mathf.Max(cfg.EfficiencyFloor, tool * book);
        }

        float BookMultiplier(BookEffect effect)
        {
            if (config == null) return 1f;
            float mult = 1f;
            SkillBookDef[] all = config.Books;
            for (int i = 0; i < all.Length; i++)
            {
                SkillBookDef b = all[i];
                if (b == null || b.effect != effect) continue;
                int stacks = BookStacks(b.rewardId);
                for (int k = 0; k < stacks; k++) mult *= b.perStackMult;
            }
            return mult;
        }

        // ----------------------------------------------------------------
        // 特殊效果：专精解锁 / Q 轮换 / 零耗时次数
        // ----------------------------------------------------------------

        public void UnlockCheck()
        {
            WhiteboxConfig cfg = GameConfig;
            WhiteboxState s = GameState;
            if (cfg == null || s == null || config == null) return;
            for (int i = 0; i < 4; i++)
            {
                if (s.toolLevels[i] > 0) continue;
                if (s.processedByCat[i] < cfg.SpecUnlockProcessCount[i]) continue;
                s.toolLevels[i] = 1;
                ToolDef def = config.Find("T-0" + (i + 2));
                string name = def != null ? def.displayName : StationSystem.CategoryNames[i] + "专精";
                if (game != null && game.Hud != null) game.Hud.Notice("专精解锁：" + name + " Lv.1（按 Q 切换）");
                Debug.Log("[Tool] 专精解锁 cat=" + i + " " + name);
            }
            WhiteboxState.Save(s);
        }

        public void CycleActive()
        {
            WhiteboxState s = GameState;
            if (s == null) return;
            int[] unlocked = new int[4];
            int count = 0;
            for (int i = 0; i < 4; i++) if (s.toolLevels[i] > 0) unlocked[count++] = i;
            if (count <= 0)
            {
                if (game != null && game.Hud != null) game.Hud.Notice("还没有解锁专精：先累计加工对应类别垃圾");
                return;
            }
            int cur = -1;
            for (int i = 0; i < count; i++) if (unlocked[i] == s.toolActive) cur = i;
            s.toolActive = unlocked[(cur + 1) % count];
            if (game != null && game.Hud != null)
                game.Hud.Notice("当前专精：" + SpecToolName(s.toolActive) + " Lv." + s.toolLevels[s.toolActive]);
            WhiteboxState.Save(s);
        }

        public string SpecToolName(int cat)
        {
            ToolDef def = config != null ? config.Find("T-0" + (Mathf.Clamp(cat, 0, 3) + 2)) : null;
            return def != null ? def.displayName : StationSystem.CategoryNames[Mathf.Clamp(cat, 0, 3)];
        }

        public string ActiveSpecText()
        {
            WhiteboxState s = GameState;
            if (s == null) return "专精：未解锁";
            if (s.toolLevels[s.toolActive] <= 0) return "专精：未装备";
            return "专精：" + SpecToolName(s.toolActive) + " Lv." + s.toolLevels[s.toolActive];
        }

        public int FreeChargeCap()
        {
            WhiteboxConfig cfg = GameConfig;
            if (cfg == null) return 0;
            int cap = HasTool("weapon_purifier") ? Mathf.Max(0, cfg.PurifierChargesMax) : 0;
            if (BookMaxed("book_tireless")) cap += 1;
            return Mathf.Clamp(cap, 0, 3);
        }

        public bool TryConsumeFreeCharge()
        {
            WhiteboxState s = GameState;
            if (s == null || s.freeJobCharges <= 0) return false;
            s.freeJobCharges -= 1;
            return true;
        }

        /// <summary>生态阶段跃迁时由 EcoStage 调用：只加次数，不写进度、不碰 0.3/0.7 阈值。</summary>
        public void OnStageAdvanced()
        {
            WhiteboxState s = GameState;
            if (s == null || !HasTool("weapon_purifier")) return;
            int cap = FreeChargeCap();
            if (s.freeJobCharges >= cap) return;
            s.freeJobCharges += 1;
            if (game != null && game.Hud != null)
                game.Hud.Notice("净化器蓄能：零耗时任务 +1（" + s.freeJobCharges + "/" + cap + "）");
            WhiteboxState.Save(s);
        }

        bool stateActiveIs(int cat)
        {
            WhiteboxState s = GameState;
            return s != null && s.toolActive == cat;
        }

        WhiteboxConfig GameConfig => game != null ? game.Config : null;
        WhiteboxState GameState => game != null ? game.State : null;
    }
}
