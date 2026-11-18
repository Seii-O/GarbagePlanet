// @trace LEV-001, MECH-013, MECH-001
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 十步顺序任务链（策划案 §7.2）。白膜把 8/9 步按 §7.3 MVP 裁剪建议改成
    /// 「各修到 MvpProgressTarget」[待拍板 C7]，其余保持原完成条件。
    /// 结算 6 项数据与 §7.2 一致；不在常驻 HUD 上做任务列表（§11）。
    /// </summary>
    public sealed class QuestChain : MonoBehaviour
    {
        public static readonly string[] Steps =
        {
            "初次探索：走几步，靠近垃圾",
            "初次回收：拾取 5 个垃圾",
            "拆解废弃物：完成 1 次大型垃圾拆解",
            "返回中转站：靠近中转站按 M",
            "修复中转站：制造 1 块再生建材板",
            "完成垃圾加工：四类各加工一次",
            "制作环保产品：制造 1 个生态肥料包",
            "修复土地：达到 MVP 进度",
            "净化水源：达到 MVP 进度",
            "恢复星球：完成最终目标"
        };

        WhiteboxGame game;

        public void Init(WhiteboxGame g) { game = g; }

        public int StepIndex => game != null && game.State != null ? game.State.questStep : 0;
        /// <summary>Init 之前 / 组件被单独查看时也不能抛异常（HUD 可能早于系统初始化读取）。</summary>
        public string CurrentText
        {
            get
            {
                if (game == null || game.State == null) return "—";
                int step = game.State.questStep;
                if (step >= Steps.Length) return "全部完成";
                return Steps[Mathf.Clamp(step, 0, Steps.Length - 1)];
            }
        }

        public void Evaluate()
        {
            if (game == null || game.State.finished) return;
            WhiteboxState s = game.State;
            WhiteboxConfig cfg = game.Config;
            while (s.questStep < Steps.Length && IsStepDone(s.questStep, s, cfg))
            {
                s.questStep += 1;
                game.Notice("任务完成 " + s.questStep + "/" + Steps.Length);
            }
            if (s.questStep >= Steps.Length && !s.finished)
            {
                s.finished = true;
                game.Hud.OpenSettlement();
                game.ApplyUiState(); // 结算时解锁光标、冻结 3D 输入，否则面板按钮点不到
                WhiteboxState.Save(s);
            }
        }

        bool IsStepDone(int step, WhiteboxState s, WhiteboxConfig cfg)
        {
            switch (step)
            {
                case 0: return s.movedDistance >= 3f || s.collected > 0;
                case 1: return s.collected >= 5;
                case 2: return s.dismantled >= 1;
                case 3: return s.visitedStation;
                case 4: return s.boards >= 1 && s.stationRepaired;
                case 5: return s.processedMask == 0xF;
                case 6: return s.fertilizer >= 1;
                case 7: return s.landProgress >= cfg.MvpProgressTarget - 0.0001f;
                case 8: return s.waterProgress >= cfg.MvpProgressTarget - 0.0001f;
                case 9: return s.landProgress >= cfg.MvpProgressTarget - 0.0001f
                            && s.waterProgress >= cfg.MvpProgressTarget - 0.0001f;
                default: return false;
            }
        }
    }
}
