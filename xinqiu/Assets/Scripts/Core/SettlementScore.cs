// @trace UI-009
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 结算打分：只读 State / Config，纯计算，不写玩法数据、不改存档。
    /// 五个阶段维度（探索/回收/加工/制造/生态）+ 效率修正，总分 0~100，
    /// 按阈值映射 S/A/B/C/D。分母是可调目标值，集中放在本类顶部，避免散落。
    /// 每项先 Mathf.Clamp01 封顶，刷再多也不会把总分拉爆，S 必须靠均衡达成。
    /// </summary>
    public static class SettlementScore
    {
        public struct Result
        {
            public int total;
            public string grade;
            public int explore, collect, process, craft, eco, speed; // 各维度已含权重的得分
        }

        // 目标值：达到即该维度满分。数值按项目现有量级取（清理 ~200、加工 ~150、制造 ~60）。
        const float ExploreTarget = 200f;   // 探索：移动距离（米）
        const float CollectTarget = 200f;   // 回收：拾取件数
        const float ProcessTarget = 150f;   // 加工：加工次数
        const float CraftTarget = 60f;      // 制造：产品件数
        const float FastSeconds = 480f;     // 效率满分线：8 分钟内
        const float MidSeconds = 900f;      // 效率 3 分线：15 分钟内
        const float SlowSeconds = 1800f;    // 效率 1 分线：30 分钟内

        public static Result Calculate(WhiteboxState s, WhiteboxConfig cfg, bool fullyCovered)
        {
            Result r = new Result();
            if (s == null) { r.grade = "D"; return r; }

            float target = cfg != null && cfg.MvpProgressTarget > 0f ? cfg.MvpProgressTarget : 0.3f;
            r.explore = Mathf.RoundToInt(Mathf.Clamp01(s.movedDistance / ExploreTarget) * 3f
                        + (s.visitedStation ? 3f : 0f)
                        + Mathf.Clamp01(s.dismantled / 3f) * 4f);                       // 10
            r.collect = Mathf.RoundToInt(Mathf.Clamp01(s.collected / CollectTarget) * 16f
                        + Mathf.Clamp01(s.collected / 20f) * 4f);                       // 20
            r.process = Mathf.RoundToInt(Mathf.Clamp01(s.processed / ProcessTarget) * 14f
                        + BitCount4(s.processedMask) * 1.5f);                           // 20
            r.craft = Mathf.RoundToInt(Mathf.Clamp01(s.crafted / CraftTarget) * 20f);   // 20
            r.eco = Mathf.RoundToInt(Mathf.Clamp01((s.landProgress + s.waterProgress)
                        / (2f * target)) * 15f + (fullyCovered ? 10f : 0f));            // 25
            r.speed = s.playTime <= FastSeconds ? 5 : s.playTime <= MidSeconds ? 3
                    : s.playTime <= SlowSeconds ? 1 : 0;                                // 5

            r.total = Mathf.Clamp(r.explore + r.collect + r.process + r.craft + r.eco + r.speed, 0, 100);
            r.grade = r.total >= 90 ? "S" : r.total >= 75 ? "A"
                    : r.total >= 55 ? "B" : r.total >= 30 ? "C" : "D";
            return r;
        }

        public static int BitCount4(int mask)
        {
            int n = 0;
            for (int i = 0; i < 4; i++) if ((mask & (1 << i)) != 0) n++;
            return n;
        }

        /// <summary>把秒数格式化成参考图的 HH:MM:SS。</summary>
        public static string FormatTime(float seconds)
        {
            int t = Mathf.Max(0, Mathf.RoundToInt(seconds));
            int h = t / 3600, m = (t / 60) % 60, sec = t % 60;
            return h.ToString("00") + ":" + m.ToString("00") + ":" + sec.ToString("00");
        }
    }
}
