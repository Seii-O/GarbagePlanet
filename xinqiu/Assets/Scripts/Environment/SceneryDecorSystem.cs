// @trace ART-013, ART-003, ART-008
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 场景装饰的生态联动总控（ART-013）。
    ///
    /// 只做一件事：读取 game.Eco.Stage（与地表 / 水 / 天空同一阶段源），
    /// 平滑推导出 visualT(0..1)，分发给每处 SceneryDecor.ApplyVisual。
    ///
    /// 严格边界：
    /// - 只读 Stage / Config，绝不写 WhiteboxState 的任何进度、资源、金币、生态值。
    /// - 不参与拾取 / 拆解 / 碰撞 / 对象池 / 狗的寻路，装饰物没有 Collider 与 TrashItem。
    /// - 不改变狗的固定外观（ART-012），不改变中转站 / 垃圾 / 玩家的任何逻辑。
    ///
    /// 阶段映射：stage 0→0（荒废）、1→0.5（恢复中）、2→1（复绿），
    /// 过渡时长复用 cfg.StageFadeSeconds，与地表色板插值同步推进。
    /// </summary>
    public sealed class SceneryDecorSystem : MonoBehaviour
    {
        [SerializeField] SceneryDecor[] decors;

        WhiteboxGame game;
        float visualT = -1f;

        public float VisualT => Mathf.Clamp01(visualT);
        public int DecorCount => decors != null ? decors.Length : 0;

        public void Wire(SceneryDecor[] all) { decors = all; }

        public void Init(WhiteboxGame g)
        {
            game = g;
            if (decors == null || decors.Length == 0)
            {
                // 兼容：Builder 未注入时，从星球根节点自动收集（含失活）
                Transform root = g != null ? g.PlanetCenter : null;
                if (root != null) decors = root.GetComponentsInChildren<SceneryDecor>(true);
                else decors = FindObjectsByType<SceneryDecor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            }
            if (decors == null) decors = new SceneryDecor[0];

            for (int i = 0; i < decors.Length; i++)
                if (decors[i] != null) decors[i].Init();

            visualT = StageToT(CurrentStage()); // 进场景立即呈现当前阶段，不从 0 动画
            ApplyAll();
            Debug.Log("[SceneryDecor] 联动已接入 " + decors.Length + " 处装饰，阶段="
                      + (CurrentStage() + 1) + "/3，visualT=" + visualT.ToString("F2"));
        }

        /// <summary>阶段升级时由外部调用可立即刷新；一般无需调用，Update 会平滑跟随。</summary>
        public void Refresh(bool instant)
        {
            if (game == null) return;
            float target = StageToT(CurrentStage());
            if (instant) visualT = target;
            ApplyAll();
        }

        void Update()
        {
            if (game == null || game.Config == null || decors == null || decors.Length == 0) return;
            float target = StageToT(CurrentStage());
            if (Mathf.Abs(target - visualT) < 0.0005f) return;

            // 与地表色板同一过渡时长：3 s 内从旧阶段推到新阶段
            float dur = Mathf.Max(0.1f, game.Config.StageFadeSeconds);
            visualT = Mathf.MoveTowards(visualT, target, Time.deltaTime / dur * 0.5f);
            ApplyAll();
        }

        int CurrentStage()
        {
            return game != null && game.Eco != null ? game.Eco.Stage : 0;
        }

        static float StageToT(int stage) { return Mathf.Clamp01(stage) * 0.5f; }

        void ApplyAll()
        {
            for (int i = 0; i < decors.Length; i++)
                if (decors[i] != null) decors[i].ApplyVisual(visualT);
        }
    }
}
