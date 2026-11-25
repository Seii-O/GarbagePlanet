// @trace C27（人物等级接管工具加成线；经验复用既有计数）
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 人物等级系统（C27）：工具下线后，拾取/伤害/加工/制造/零耗时加成全部由人物等级驱动。
    ///
    /// 设计红线：
    /// - 经验不新增采集挂钩——直接从 WhiteboxState 已存在的计数（collected/dismantled/processed/crafted/修复事件）推导，
    ///   每次 OnWorldEvent 重算一次即可，旧档自动获得应得等级，version 保持 1。
    /// - 等级/经验字段已在 WhiteboxState，旧档在 NormalizeLoaded 补位，不升存档版本。
    /// - 加成数值全部来自 WhiteboxConfig，本类不写死数值。
    /// </summary>
    public sealed class PlayerLevelSystem : MonoBehaviour
    {
        WhiteboxGame game;

        public void Init(WhiteboxGame g)
        {
            game = g;
            Recalculate(true);
            Debug.Log("[PlayerLevel] 初始化完成：Lv." + Level
                      + "，累计经验 " + TotalExp + "，下一级 " + ExpToNext);
        }

        WhiteboxConfig Cfg => game != null ? game.Config : null;
        WhiteboxState S => game != null ? game.State : null;

        public int Level => S != null ? Mathf.Clamp(S.playerLevel, 1, MaxLevel) : 1;
        public int Exp => S != null ? Mathf.Max(0, S.playerExp) : 0;
        public int TotalExp => S != null ? Mathf.Max(0, S.playerExpTotal) : 0;
        public int MaxLevel => Cfg != null ? Cfg.PlayerLevelMax : 5;
        public bool IsMax => Level >= MaxLevel;

        /// <summary>升下一级还需多少累计经验；已满级返回 0。</summary>
        public int ExpToNext
        {
            get
            {
                if (IsMax || Cfg == null) return 0;
                int need = Cfg.PlayerLevelExpAt(Level + 1);
                return Mathf.Max(0, need - TotalExp);
            }
        }

        /// <summary>当前等级内的经验进度 0..1；满级为 1。</summary>
        public float Progress
        {
            get
            {
                if (IsMax || Cfg == null) return 1f;
                int lo = Cfg.PlayerLevelExpAt(Level);
                int hi = Cfg.PlayerLevelExpAt(Level + 1);
                if (hi <= lo) return 1f;
                return Mathf.Clamp01((float)(TotalExp - lo) / (hi - lo));
            }
        }

        /// <summary>按既有计数刷新累计经验（不自动升级）。升级需在中转站手动消耗材料，见 TryLevelUp。</summary>
        public void Recalculate(bool silent = false)
        {
            WhiteboxState s = S;
            WhiteboxConfig cfg = Cfg;
            if (s == null || cfg == null) return;

            int exp = s.collected * cfg.ExpPerCollect
                      + s.dismantled * cfg.ExpPerDismantle
                      + s.processed * cfg.ExpPerProcess
                      + s.crafted * cfg.ExpPerCraft
                      + s.boards * cfg.ExpPerRepair;
            s.playerExpTotal = Mathf.Max(0, exp);
            // 当前级内经验 = 累计 - 本级起点，仅用于 UI 进度显示
            s.playerExp = Mathf.Max(0, s.playerExpTotal - cfg.PlayerLevelExpAt(s.playerLevel));
        }

        /// <summary>本级升下一级所需的金属/电子（UI 显示用）。</summary>
        public int UpgradeMetalCost => Cfg != null ? Cfg.PlayerUpgradeMetalCostAt(Level) : 8;
        public int UpgradeElectronicCost => Cfg != null ? Cfg.PlayerUpgradeElectronicCostAt(Level) : 4;

        /// <summary>是否满足升级前置：经验达标 + 材料足够。返回原因给 UI。</summary>
        public bool CanLevelUp(out string reason)
        {
            reason = "";
            if (IsMax) { reason = "已达满级"; return false; }
            WhiteboxState s = S;
            WhiteboxConfig cfg = Cfg;
            if (s == null || cfg == null) { reason = "系统未就绪"; return false; }
            if (s.playerExpTotal < cfg.PlayerLevelExpAt(Level + 1))
            { reason = "经验不足（还差 " + (cfg.PlayerLevelExpAt(Level + 1) - s.playerExpTotal) + "）"; return false; }
            if (s.materials == null || s.materials.Length < 4
                || s.materials[1] < UpgradeMetalCost || s.materials[3] < UpgradeElectronicCost)
            { reason = "材料不足：需 " + UpgradeMetalCost + " 金属 + " + UpgradeElectronicCost + " 电子"; return false; }
            return true;
        }

        /// <summary>中转站点升级调用：校验经验+材料 → 扣材料 → 升级 → 落盘。</summary>
        public bool TryLevelUp()
        {
            string reason;
            if (!CanLevelUp(out reason))
            {
                Sfx.Play(SfxId.Error);
                if (game != null && game.Hud != null) game.Hud.Notice(reason);
                return false;
            }
            WhiteboxState s = S;
            WhiteboxConfig cfg = Cfg;
            s.materials[1] -= UpgradeMetalCost;
            s.materials[3] -= UpgradeElectronicCost;
            s.playerLevel = Mathf.Clamp(s.playerLevel + 1, 1, cfg.PlayerLevelMax);
            s.playerExp = Mathf.Max(0, s.playerExpTotal - cfg.PlayerLevelExpAt(s.playerLevel));
            WhiteboxState.Save(s);
            Sfx.Play(SfxId.LevelUp);
            if (game != null && game.Hud != null)
                game.Hud.Notice("人物升级到 Lv." + s.playerLevel + "！拾取与效率提升");
            Debug.Log("[PlayerLevel] 升级到 Lv." + s.playerLevel);
            return true;
        }

        // ---- 等级驱动的加成（供 ToolSystem 调用；数值全部读配置）----

        public float PickRangeAt(int level) => Cfg != null ? Cfg.PlayerPickRangeAt(level) : 4f;
        public int DismantleDamageAt(int level) => Cfg != null ? Cfg.PlayerDismantleDamageAt(level) : 1;
        public float ProcessMultAt(int level) => Cfg != null ? Cfg.PlayerProcessMultAt(level) : 1f;
        public float CraftMultAt(int level) => Cfg != null ? Cfg.PlayerCraftMultAt(level) : 1f;

        /// <summary>等级白送的零耗时次数（与技能书 book_tireless 叠加）。</summary>
        public int FreeChargeAt(int level)
        {
            if (Cfg == null) return 0;
            if (level < Cfg.PlayerFreeChargeFromLevel) return 0;
            return Mathf.Max(0, (level - Cfg.PlayerFreeChargeFromLevel + 1) * Cfg.PlayerFreeChargePerLevel);
        }

        /// <summary>供 UI 显示一行等级摘要。</summary>
        public string Summary()
        {
            if (IsMax) return "Lv." + Level + "／满级";
            return "Lv." + Level + "　经验 " + TotalExp + "/" + Cfg.PlayerLevelExpAt(Level + 1);
        }
    }
}
