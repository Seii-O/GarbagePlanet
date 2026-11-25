// @trace MECH-014, MECH-015, MECH-016, MECH-017, NUM-010, NUM-011, ART-011, ART-012, ART-014
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 骨骼动画参数名（ART-014）：Editor 端建控制器与运行时驱动共用同一份常量，改名不会对不上。
    /// </summary>
    public static class DogAnimatorParams
    {
        public const string Speed = "Speed";
        public const string Jump = "Jump";
        public const string Tilt = "Tilt";
    }

    /// <summary>
    /// 伙伴狗（B 档，功能伙伴）：球面跟随 + 冷却到点自动叼回范围内普通垃圾。
    /// ART-012 / v1.2 D6：外观全程恒定。材质只在 Init 写入一次，之后本类与 EcoStage
    /// 都不会改写狗的任何 Renderer；没有 UpdateLook、没有阶段色、没有姿态变色。
    /// MECH-017：周期性探路——狗暂时走到玩家侧前方进入第一人称视野，停留后回位；
    ///           跟随/探路/回位只改根物体 transform，不碰材质与 Renderer。
    /// ART-014：程序化动作——尾巴、四肢、头部只改子物体 localRotation；
    ///           幅度受 WhiteboxConfig 钳制，体高仍 ≤ 0.6 m（ART-011）。
    /// ART-014 v2：换成 RobotPuppy.fbx 真模型后，Model 子物体上挂 Animator，
    ///           走 Idle_Wag / Walk / Jump / HeadTilt 四条骨骼动画；
    ///           取不到 Animator 时自动退回程序化动作，旧预制体行为不变。
    /// [待拍板 C13] 固定色号取干净米黄作为默认值，改 WhiteboxConfig.dogColorHex 即可换。
    /// </summary>
    public sealed class CompanionDog : MonoBehaviour
    {
        enum DogMotion { Follow, Scout, Return }

        const string ModelName = "Model";

        [SerializeField] Renderer[] fixedColorRenderers;
        [SerializeField] Material fixedMaterial;

        WhiteboxGame game;
        float cooldown;

        // MECH-017 状态机
        DogMotion motion = DogMotion.Follow;
        float scoutCooldown;
        float scoutTimer;
        float returnTimer;
        float idleTimer;
        bool idleTriggered;
        float playerSpeed;
        Vector3 lastPlayerPos;
        bool hasLastPlayerPos;

        // 根物体动作
        float animClock;
        float pulseTimer;
        const float PulseDuration = 0.18f;
        TrashItem lookTarget;
        float lookTimer;
        float lookScanTimer;
        const float LookDuration = 0.25f;

        // ART-014 子部件引用（运行时按名字解析，不增加序列化字段，旧预制体也能用）
        Transform tail;
        Transform head;
        Transform[] legs;
        float wagClock;
        float wagBoost;
        float gaitClock;
        float gaitAmp;

        // ART-014 v2 骨骼动画：有 Animator 时由状态机驱动，程序化动作让位
        Animator animator;
        float animSpeed;

        public Renderer[] FixedColorRenderers { get => fixedColorRenderers; set => fixedColorRenderers = value; }
        public Material FixedMaterial { get => fixedMaterial; set => fixedMaterial = value; }

        public void Init(WhiteboxGame g)
        {
            game = g;
            cooldown = g.Config.DogFetchCooldown[0];
            scoutCooldown = g.Config.DogScoutInterval;
            if (fixedMaterial != null && fixedColorRenderers != null)
            {
                for (int i = 0; i < fixedColorRenderers.Length; i++)
                    if (fixedColorRenderers[i] != null) fixedColorRenderers[i].sharedMaterial = fixedMaterial;
            }

            // ART-014：只取 Transform，绝不改 Renderer / material
            tail = FindChild(transform, "Tail");
            head = FindChild(transform, "Head");
            legs = new Transform[4];
            for (int i = 0; i < 4; i++) legs[i] = FindChild(transform, "Leg_" + i);

            // ART-014 v2：真模型走骨骼动画；控制器为空视为没接动画，继续用程序化动作
            Transform model = FindChild(transform, ModelName);
            animator = model != null ? model.GetComponent<Animator>() : null;
            if (animator != null && animator.runtimeAnimatorController == null) animator = null;
            if (animator != null) animator.applyRootMotion = false;

            if (g.Player != null)
            {
                lastPlayerPos = g.Player.position;
                hasLastPlayerPos = true;
            }

            Debug.Log("[WhiteboxDog] 固定外观已写入，色号=" + ColorUtility.ToHtmlStringRGB(g.Config.DogColor)
                      + "；后续阶段切换不会再改狗的材质（MECH-017 探路 / ART-014 动作只改 transform）"
                      + "；动画=" + (animator != null ? "骨骼（" + animator.runtimeAnimatorController.name + "）" : "程序化"));
        }

        static Transform FindChild(Transform root, string name)
        {
            if (root == null) return null;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name == name) return all[i];
            return null;
        }

        void Update()
        {
            if (game == null || game.Player == null || game.IsUiBlocking) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            UpdateSpeed(dt);
            TickScout(dt);
            animClock += dt;
            pulseTimer = Mathf.Max(0f, pulseTimer - dt);
            wagBoost = Mathf.Max(0f, wagBoost - dt);

            Follow(dt);
            Fetch(dt);
            AnimateParts(dt);
            UpdateAnimator(dt);
        }

        void UpdateSpeed(float dt)
        {
            Vector3 p = game.Player.position;
            playerSpeed = hasLastPlayerPos ? Vector3.Distance(p, lastPlayerPos) / dt : 0f;
            lastPlayerPos = p;
            hasLastPlayerPos = true;
        }

        // ---------------------------------------------------------------
        // MECH-017 探路状态机
        // ---------------------------------------------------------------

        void TickScout(float dt)
        {
            WhiteboxConfig cfg = game.Config;

            if (motion == DogMotion.Scout)
            {
                scoutTimer -= dt;
                if (scoutTimer <= 0f)
                {
                    motion = DogMotion.Return;
                    returnTimer = 2f;          // 兜底时限：玩家一直高速移动时也能回到 Follow
                    scoutCooldown = cfg.DogScoutInterval;
                }
                return;
            }

            if (motion == DogMotion.Return)
            {
                returnTimer -= dt;
                if (returnTimer <= 0f) motion = DogMotion.Follow;
                return;
            }

            scoutCooldown -= dt;
            if (playerSpeed < 0.05f)
            {
                idleTimer += dt;
                if (idleTimer >= cfg.DogScoutIdleTrigger && !idleTriggered && scoutCooldown <= 0f)
                    BeginScout(cfg);
            }
            else
            {
                idleTimer = 0f;
                idleTriggered = false;
            }

            if (motion == DogMotion.Follow && scoutCooldown <= 0f) BeginScout(cfg);
        }

        void BeginScout(WhiteboxConfig cfg)
        {
            motion = DogMotion.Scout;
            scoutTimer = cfg.DogScoutHold;
            idleTimer = 0f;
            idleTriggered = true;              // 一次静止只触发一次，回位后不立刻重来
        }

        /// <summary>按当前状态算根物体目标点；Follow/Return 在身后，Scout 在侧前方可见区。</summary>
        Vector3 DesiredPosition(WhiteboxConfig cfg, out Vector3 forward, out Vector3 up)
        {
            Vector3 center = Vector3.zero;
            Vector3 player = game.Player.position;
            up = SphericalSurface.Up(player, center);
            forward = Vector3.ProjectOnPlane(game.Player.forward, up);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.ProjectOnPlane(Vector3.forward, up);
            forward.Normalize();

            Vector3 offset = motion == DogMotion.Scout
                ? forward * cfg.DogScoutForward + Vector3.Cross(up, forward).normalized * cfg.DogScoutSide
                : -forward * cfg.DogFollowDistance;

            Vector3 anchor = SphericalSurface.Point(center, up, cfg.PlanetRadius, 0f);
            return SphericalSurface.MoveOnSurface(center, anchor, offset, offset.magnitude);
        }

        void Follow(float dt)
        {
            WhiteboxConfig cfg = game.Config;
            Vector3 center = Vector3.zero;
            Vector3 forward;
            Vector3 up;
            Vector3 desired = DesiredPosition(cfg, out forward, out up);

            // 叼回成功：沿前方做一次正弦前探再回弹
            if (pulseTimer > 0f)
            {
                float k = Mathf.Sin(Mathf.PI * (1f - pulseTimer / PulseDuration));
                desired = SphericalSurface.MoveOnSurface(center, desired, forward, cfg.DogFetchLunge * k);
            }

            Vector3 desiredUp = SphericalSurface.Up(desired, center);
            Vector3 currentUp = SphericalSurface.Up(transform.position, center);
            float smooth = motion == DogMotion.Return ? 2.5f : 3.5f;
            Vector3 newUp = Vector3.Slerp(currentUp, desiredUp, 1f - Mathf.Exp(-smooth * dt));

            // 朝向优先级：探路/回位的回望 > 发现垃圾 > 玩家 forward
            Vector3 facing = game.Player.forward;
            if (motion == DogMotion.Scout || motion == DogMotion.Return)
            {
                Vector3 back = Vector3.ProjectOnPlane(game.Player.position - transform.position, newUp);
                if (back.sqrMagnitude > 0.0001f) facing = back;
            }
            else if (lookTarget != null && lookTimer > 0f)
            {
                Vector3 toTrash = Vector3.ProjectOnPlane(lookTarget.transform.position - transform.position, newUp);
                if (toTrash.sqrMagnitude > 0.0001f)
                {
                    float k = Mathf.Sin(Mathf.PI * (1f - lookTimer / LookDuration));
                    facing = Vector3.Slerp(facing, toTrash, k).normalized;
                }
            }

            // 小跑起伏：只改离地高度，配合 ART-011 的体高上限
            float bob = 0f;
            if (playerSpeed > 0.05f || motion == DogMotion.Scout)
                bob = Mathf.Sin(animClock * Mathf.PI * 2f * cfg.DogLegGaitHz) * cfg.DogBobMeters;

            SphericalSurface.Place(transform, center, newUp, cfg.PlanetRadius,
                cfg.DogHeight * 0.5f + bob, facing);
        }

        void Fetch(float dt)
        {
            // 探路期间暂停叼回：避免狗在玩家视野里突然把垃圾叼走造成视觉跳变
            if (motion == DogMotion.Scout) return;

            lookTimer = Mathf.Max(0f, lookTimer - dt);
            lookScanTimer -= dt;

            WhiteboxConfig cfg = game.Config;
            int stage = game.Eco != null ? game.Eco.Stage : 0;
            stage = Mathf.Clamp(stage, 0, 2);
            // 阶段一只叼塑料与有机；阶段二起四类（策划案 D 节，[待确认] 白膜按建议默认实现）
            int mask = stage == 0 ? (1 << (int)TrashCategory.Plastic) | (1 << (int)TrashCategory.Organic) : 0xF;

            TrashItem t = null;
            if (cooldown <= 0f || lookScanTimer <= 0f)
            {
                t = game.Spawner.FindNearest(transform.position, cfg.DogFetchRadius[stage],
                    mask, game.Interaction.CurrentTarget);
                lookScanTimer = 0.25f;

                if (motion == DogMotion.Follow && t != null && lookTarget != t)
                {
                    lookTarget = t;
                    lookTimer = LookDuration;
                    if (animator != null) animator.SetTrigger(DogAnimatorParams.Tilt);
                }
                if (t == null) lookTarget = null;
            }

            cooldown -= dt;
            if (cooldown > 0f || t == null) return;

            int cat = (int)t.Category;
            if (!game.State.CanCollect(cfg, cat))
            {
                // 背包满时等待片刻再尝试。
                cooldown = 2f;
                return;
            }
            if (game.Spawner.LastTrashOnSurface || game.State.Collect(cfg, cat, 1) <= 0) return;

            game.Spawner.Despawn(t);
            game.Hud.FloatText("狗 +1");
            game.OnWorldEvent();
            cooldown = cfg.DogFetchCooldown[stage];
            pulseTimer = PulseDuration;
            wagBoost = 0.6f;
            lookTarget = null;
            lookTimer = 0f;
            if (animator != null) animator.SetTrigger(DogAnimatorParams.Jump);
        }

        // ---------------------------------------------------------------
        // ART-014 v2 骨骼动画驱动（只喂参数，不碰骨骼 Transform）
        // ---------------------------------------------------------------

        /// <summary>
        /// 只做一件事：把「在不在动」平滑成 0~1 的 Speed 参数喂给状态机，
        /// 由控制器决定 Idle_Wag / Walk。回滞区间（0.35 起步 / 0.15 收步）在控制器里。
        /// </summary>
        void UpdateAnimator(float dt)
        {
            if (animator == null) return;
            bool moving = playerSpeed > 0.05f || motion == DogMotion.Scout;
            animSpeed = Mathf.MoveTowards(animSpeed, moving ? 1f : 0f, dt * 4f);
            animator.SetFloat(DogAnimatorParams.Speed, animSpeed);
        }

        // ---------------------------------------------------------------
        // ART-014 程序化部件动画（只改 localRotation，永不碰材质）
        // 仅在没有骨骼动画时生效，白膜图元与旧预制体仍按这套跑
        // ---------------------------------------------------------------

        void AnimateParts(float dt)
        {
            if (animator != null) return;      // 已由骨骼动画接管
            WhiteboxConfig cfg = game.Config;

            // 尾巴：常态轻摆，叼回成功后 0.6 s 内放大摆幅
            wagClock += dt * Mathf.PI * 2f * cfg.DogTailWagHz;
            float wagAmp = cfg.DogTailWagDeg + (wagBoost > 0f ? cfg.DogTailBoostDeg : 0f);
            if (tail != null)
                tail.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(wagClock) * wagAmp);

            // 四肢：对角相位；停下时幅度平滑衰减到 0，避免腿停在半抬状态
            float moving = (playerSpeed > 0.05f || motion == DogMotion.Scout) ? 1f : 0f;
            gaitClock += dt * Mathf.PI * 2f * cfg.DogLegGaitHz * moving;
            gaitAmp = Mathf.Lerp(gaitAmp, moving * cfg.DogLegGaitDeg, 1f - Mathf.Exp(-8f * dt));
            if (legs != null)
            {
                float[] phase = { 0f, Mathf.PI, Mathf.PI, 0f };
                for (int i = 0; i < legs.Length && i < 4; i++)
                    if (legs[i] != null)
                        legs[i].localRotation = Quaternion.Euler(
                            Mathf.Sin(gaitClock + phase[i]) * gaitAmp, 0f, 0f);
            }

            // 头部：发现垃圾时叠加一个短促偏航
            if (head != null)
            {
                float yaw = (lookTarget != null && lookTimer > 0f)
                    ? Mathf.Sin(Mathf.PI * (1f - lookTimer / LookDuration)) * cfg.DogHeadLookDeg
                    : 0f;
                head.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }
        }
    }
}
