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
    /// 伙伴狗（B 档，功能伙伴）：在玩家周围的可配置半径内自主漫游、找垃圾、走过去叼回来。
    /// 不再与玩家刚性绑定——玩家原地不动，狗也会自己巡游；一旦超出活动半径就回位。
    /// ART-012 / v1.2 D6：外观全程恒定。材质只在 Init 写入一次，之后本类与 EcoStage
    /// 都不会改写狗的任何 Renderer；没有 UpdateLook、没有阶段色、没有姿态变色。
    /// MECH-017：周期性探路——狗暂时走到玩家侧前方进入第一人称视野，停留后回位；
    ///           探路同样受活动半径约束，不会跑出范围。
    /// ART-014：程序化动作——尾巴、四肢、头部只改子物体 localRotation；
    ///           幅度受 WhiteboxConfig 钳制，体高仍 ≤ 0.6 m（ART-011）。
    /// ART-014 v2：换成 RobotPuppy.fbx 真模型后，Model 子物体上挂 Animator，
    ///           走 Idle_Wag / Walk / Jump / HeadTilt 四条骨骼动画；
    ///           取不到 Animator 时自动退回程序化动作，旧预制体行为不变。
    /// [待拍板 C13] 固定色号取干净米黄作为默认值，改 WhiteboxConfig.dogColorHex 即可换。
    /// </summary>
    public sealed class CompanionDog : MonoBehaviour
    {
        enum DogMotion { Roam, Seek, Return, Scout }

        const string ModelName = "Model";
        // 缰绳阈值按半径比例，而不是固定米数：R 改大改小都不用重调
        const float LeashEnterRatio = 0.98f;  // 到 0.98R 就开始回位（留 2% 余量防抖）
        const float LeashExitRatio = 0.80f;   // 回到 0.80R 内恢复漫游（滞回，不会来回切）
        const float ArriveDistance = 0.45f;   // 漫游到点判定（米）
        const float ScanInterval = 0.25f;     // 嗅探节拍（秒）

        [SerializeField] Renderer[] fixedColorRenderers;
        [SerializeField] Material fixedMaterial;

        WhiteboxGame game;
        float cooldown;

        // 自主拾荒状态机：漫游 → 追击 → 叼取，越界强制回位，探路（MECH-017）保留
        DogMotion motion = DogMotion.Roam;
        Vector3 roamTarget;
        bool hasRoamTarget;
        float roamPause;
        TrashItem seekTarget;
        float scanTimer;
        float scoutCooldown;
        float scoutTimer;
        float returnTimer;
        float idleTimer;
        bool idleTriggered;
        float playerSpeed;
        float dogSpeed;                       // 狗自身实际速度：动画/步态/起伏都看这个，不再借玩家的
        Vector3 lastPlayerPos;
        Vector3 lastDogPos;
        bool hasLastPlayerPos;
        bool hasLastDogPos;

        // 根物体动作
        float animClock;
        float pulseTimer;
        // 叼中前探的基准方向：脉冲期间始终从这一点重新落位再加偏移。
        // 绝不能在「当前已偏移的位置」上继续叠加，否则 0.18 s 内会累计出约 7 倍前探距离（观感就是瞬移）。
        Vector3 pulseUp;
        const float PulseDuration = 0.18f;
        TrashItem lookTarget;
        float lookTimer;
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
            cooldown = g.Config.DogFetchCooldownAt(0);
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
            lastDogPos = transform.position;
            hasLastDogPos = true;

            Debug.Log("[WhiteboxDog] 固定外观已写入，色号=" + ColorUtility.ToHtmlStringRGB(g.Config.DogColor)
                      + "；自主拾荒半径=" + g.Config.DogRoamRadius.ToString("0.##") + " m（超出自动回位）"
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

            TickBrain(dt);      // 先决定这一步该干什么
            Move(dt);           // 再按限速走过去
            ClampToRoam();      // 最后兜底：绝不允许留在活动半径之外
            Fetch(dt);          // 贴近目标才真正叼走
            AnimateParts(dt);
            UpdateAnimator(dt);
        }

        void UpdateSpeed(float dt)
        {
            Vector3 p = game.Player.position;
            playerSpeed = hasLastPlayerPos ? Vector3.Distance(p, lastPlayerPos) / dt : 0f;
            lastPlayerPos = p;
            hasLastPlayerPos = true;

            Vector3 d = transform.position;
            dogSpeed = hasLastDogPos ? Vector3.Distance(d, lastDogPos) / dt : 0f;
            lastDogPos = d;
            hasLastDogPos = true;
        }

        // ---------------------------------------------------------------
        // 球面距离：一律用两条法线的夹角折成弧长，避免不同高度导致误判
        // ---------------------------------------------------------------

        float ArcTo(Vector3 point, float planetRadius)
        {
            Vector3 a = SphericalSurface.Up(transform.position, Vector3.zero);
            Vector3 b = SphericalSurface.Up(point, Vector3.zero);
            return Vector3.Angle(a, b) * Mathf.Deg2Rad * planetRadius;
        }

        float ArcFromPlayer(Vector3 point, float planetRadius)
        {
            Vector3 a = SphericalSurface.Up(game.Player.position, Vector3.zero);
            Vector3 b = SphericalSurface.Up(point, Vector3.zero);
            return Vector3.Angle(a, b) * Mathf.Deg2Rad * planetRadius;
        }

        // ---------------------------------------------------------------
        // MECH-017 探路状态机（保留，但同样受活动半径约束）
        // ---------------------------------------------------------------

        void TickScout(float dt)
        {
            WhiteboxConfig cfg = game.Config;

            if (motion == DogMotion.Scout)
            {
                scoutTimer -= dt;
                if (scoutTimer <= 0f)
                {
                    motion = DogMotion.Roam;
                    hasRoamTarget = false;
                    roamPause = 0.2f;
                    scoutCooldown = cfg.DogScoutInterval;
                }
                return;
            }

            if (motion != DogMotion.Roam) return;   // 追击/回位期间不插播探路

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

            if (motion == DogMotion.Roam && scoutCooldown <= 0f) BeginScout(cfg);
        }

        void BeginScout(WhiteboxConfig cfg)
        {
            motion = DogMotion.Scout;
            scoutTimer = cfg.DogScoutHold;
            idleTimer = 0f;
            idleTriggered = true;              // 一次静止只触发一次，回位后不立刻重来
            game.Dialogue?.OnDogScout();
        }

        // ---------------------------------------------------------------
        // 自主拾荒：状态迁移
        // ---------------------------------------------------------------

        /// <summary>
        /// 每帧只做判定、不移动：超 0.98R→回位；回到 0.80R 内→漫游；
        /// 漫游中按节拍嗅探→追击；目标失效/冷却中→继续漫游。
        /// </summary>
        void TickBrain(float dt)
        {
            WhiteboxConfig cfg = game.Config;
            float radius = cfg.DogRoamRadius;
            float dist = ArcFromPlayer(transform.position, cfg.PlanetRadius);

            // 回位期间不做越界判定（回位就是去收边界）；探路时若玩家跑远了同样立刻转回位
            if (motion != DogMotion.Return && dist > radius * LeashEnterRatio)
            {
                EnterReturn();
                return;
            }

            if (motion == DogMotion.Return)
            {
                returnTimer -= dt;
                if (dist <= radius * LeashExitRatio)
                {
                    motion = DogMotion.Roam;
                    hasRoamTarget = false;
                    roamPause = Random.Range(0.2f, 0.8f);
                }
                else if (returnTimer <= 0f)
                {
                    returnTimer = 3f;           // 玩家还在跑：续命继续追，别在边界来回切状态
                }
                return;
            }

            if (motion == DogMotion.Seek)
            {
                if (!IsValidSeekTarget(cfg, seekTarget, radius))
                {
                    seekTarget = null;
                    motion = DogMotion.Roam;
                    hasRoamTarget = false;
                }
                return;
            }

            if (motion == DogMotion.Scout) return;

            scanTimer -= dt;
            if (scanTimer > 0f) return;
            scanTimer = ScanInterval;

            if (cooldown > 0f) return;          // 刚叼完在歇口气，先自己转一圈

            TrashItem t = FindSniffTarget(cfg, radius);
            if (t == null) return;

            seekTarget = t;
            motion = DogMotion.Seek;
            if (lookTarget != t)
            {
                lookTarget = t;
                lookTimer = LookDuration;
                if (animator != null) animator.SetTrigger(DogAnimatorParams.Tilt);
            }
        }

        void EnterReturn()
        {
            if (motion == DogMotion.Scout) scoutCooldown = game.Config.DogScoutInterval;  // 探路被打断，别立刻重来
            motion = DogMotion.Return;
            returnTimer = 3f;                   // 兜底时限：极端情况下靠 ClampToRoam 收尾
            seekTarget = null;
        }

        /// <summary>嗅探：只挑活动半径内的普通垃圾，界外的一律不追（避免越界再被拽回的死循环）。</summary>
        TrashItem FindSniffTarget(WhiteboxConfig cfg, float radius)
        {
            int stage = game.Eco != null ? game.Eco.Stage : 0;
            stage = Mathf.Clamp(stage, 0, 2);
            // 阶段一只叼塑料与有机；阶段二起四类（策划案 D 节，[待确认] 白膜按建议默认实现）
            int mask = stage == 0
                ? (1 << (int)TrashCategory.Plastic) | (1 << (int)TrashCategory.Organic)
                : 0xF;

            TrashItem t = game.Spawner.FindNearest(transform.position, cfg.DogFetchRadiusAt(stage),
                mask, game.Interaction.CurrentTarget);
            if (t == null) return null;
            return ArcFromPlayer(t.transform.position, cfg.PlanetRadius) <= radius * 0.95f ? t : null;
        }

        bool IsValidSeekTarget(WhiteboxConfig cfg, TrashItem t, float radius)
        {
            if (t == null || !t.gameObject.activeSelf || t.Large) return false;
            return ArcFromPlayer(t.transform.position, cfg.PlanetRadius) <= radius * 0.95f;
        }

        // ---------------------------------------------------------------
        // 自主拾荒：移动
        // ---------------------------------------------------------------

        void Move(float dt)
        {
            WhiteboxConfig cfg = game.Config;
            Vector3 target;
            float speed;

            switch (motion)
            {
                case DogMotion.Seek:
                    if (seekTarget == null)
                    {
                        motion = DogMotion.Roam;
                        hasRoamTarget = false;
                        return;
                    }
                    // 到嘴就停：进到拾取距离就不再挪，交给 Fetch 判定
                    if (ArcTo(seekTarget.transform.position, cfg.PlanetRadius) <= cfg.DogPickDistance * 0.8f) return;
                    target = seekTarget.transform.position;
                    speed = cfg.DogRunSpeed;
                    break;

                case DogMotion.Return:
                    target = ReturnPoint(cfg);
                    speed = cfg.DogRunSpeed;
                    break;

                case DogMotion.Scout:
                    target = ScoutPoint(cfg);
                    speed = cfg.DogRoamSpeed * 1.6f;
                    break;

                default: // Roam
                    if (roamPause > 0f)
                    {
                        roamPause -= dt;
                        PlaceOnSurface(SphericalSurface.Up(transform.position, Vector3.zero), transform.forward);
                        return;
                    }
                    if (!hasRoamTarget)
                    {
                        roamTarget = PickRoamTarget(cfg);
                        hasRoamTarget = true;
                    }
                    if (ArcTo(roamTarget, cfg.PlanetRadius) <= ArriveDistance)
                    {
                        roamPause = Random.Range(0.8f, 2.2f);   // 到了就站一会儿，像真的在闻地面
                        hasRoamTarget = false;
                        PlaceOnSurface(SphericalSurface.Up(transform.position, Vector3.zero), transform.forward);
                        return;
                    }
                    target = roamTarget;
                    speed = cfg.DogRoamSpeed;
                    break;
            }

            StepTowards(target, speed, dt);
        }

        /// <summary>沿球面按「速度 × dt」的弧长前进；不会再出现原先那种无限平滑的滑移。</summary>
        void StepTowards(Vector3 targetPoint, float speed, float dt)
        {
            WhiteboxConfig cfg = game.Config;
            Vector3 center = Vector3.zero;
            Vector3 up = SphericalSurface.Up(transform.position, center);
            Vector3 toTarget = Vector3.ProjectOnPlane(targetPoint - transform.position, up);

            if (toTarget.sqrMagnitude < 0.0001f)
            {
                PlaceOnSurface(up, transform.forward);
                return;
            }

            Vector3 dir = toTarget.normalized;
            float arc = Vector3.Angle(up, SphericalSurface.Up(targetPoint, center)) * Mathf.Deg2Rad * cfg.PlanetRadius;
            float step = Mathf.Min(speed * dt, arc);
            if (step <= 0.00001f)
            {
                PlaceOnSurface(up, transform.forward);
                return;
            }

            Vector3 newPos = SphericalSurface.MoveOnSurface(center, transform.position, dir, step);
            PlaceOnSurface(SphericalSurface.Up(newPos, center), dir);
        }

        /// <summary>落位：法线对齐 + 前进朝向 + 小跑起伏 + 叼中瞬间的前探回弹。</summary>
        void PlaceOnSurface(Vector3 up, Vector3 facing)
        {
            WhiteboxConfig cfg = game.Config;
            bool pulsing = pulseTimer > 0f;

            // 脉冲期间锁定在脉冲开始时的基准点上，不再跟着被偏移过的 transform 漂
            Vector3 baseUp = pulsing && pulseUp.sqrMagnitude > 0.0001f ? pulseUp : up;

            Vector3 look = facing;
            if (motion == DogMotion.Scout)
            {
                Vector3 back = Vector3.ProjectOnPlane(game.Player.position - transform.position, baseUp);
                if (back.sqrMagnitude > 0.0001f) look = back;
            }

            float bob = dogSpeed > 0.05f
                ? Mathf.Sin(animClock * Mathf.PI * 2f * cfg.DogLegGaitHz) * cfg.DogBobMeters
                : 0f;

            SphericalSurface.Place(transform, Vector3.zero, baseUp, cfg.PlanetRadius,
                cfg.DogHeight * 0.5f + bob, look);

            if (pulsing)
            {
                // 前探回弹：k 在 0→1→0 之间走一趟，位移上限 = DogFetchLunge，末帧精确回到基准点。
                float k = Mathf.Sin(Mathf.PI * (1f - pulseTimer / PulseDuration));
                Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, baseUp).normalized;
                transform.position = SphericalSurface.MoveOnSurface(Vector3.zero, transform.position, fwd,
                    cfg.DogFetchLunge * k);
                lastDogPos = transform.position;   // 前探不算狗自己的移动，避免动画速度被拉爆
            }
        }

        /// <summary>漫游目标：以玩家为中心、半径 0.4R~0.9R，方向在「当前所在方位」左右各 100° 内随机。</summary>
        Vector3 PickRoamTarget(WhiteboxConfig cfg)
        {
            Vector3 center = Vector3.zero;
            Vector3 playerUp = SphericalSurface.Up(game.Player.position, center);
            Vector3 tangent = Vector3.ProjectOnPlane(game.Player.forward, playerUp);
            if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.ProjectOnPlane(Vector3.forward, playerUp);
            tangent.Normalize();
            Vector3 side = Vector3.Cross(playerUp, tangent).normalized;

            Vector3 toDog = Vector3.ProjectOnPlane(transform.position - game.Player.position, playerUp);
            float baseAngle = toDog.sqrMagnitude > 0.0001f
                ? Mathf.Atan2(Vector3.Dot(toDog.normalized, side), Vector3.Dot(toDog.normalized, tangent))
                : Random.Range(0f, Mathf.PI * 2f);

            float angle = baseAngle + Random.Range(-100f, 100f) * Mathf.Deg2Rad;
            Vector3 dir = (tangent * Mathf.Cos(angle) + side * Mathf.Sin(angle)).normalized;

            float radius = cfg.DogRoamRadius;
            float dist = Random.Range(radius * 0.4f, radius * 0.9f);
            return SphericalSurface.MoveOnSurface(center, game.Player.position, dir, dist);
        }

        /// <summary>探路站位：玩家侧前方可见区（原 MECH-017 逻辑，保持数值不变）。</summary>
        Vector3 ScoutPoint(WhiteboxConfig cfg)
        {
            Vector3 center = Vector3.zero;
            Vector3 playerUp = SphericalSurface.Up(game.Player.position, center);
            Vector3 forward = Vector3.ProjectOnPlane(game.Player.forward, playerUp);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.ProjectOnPlane(Vector3.forward, playerUp);
            forward.Normalize();
            Vector3 side = Vector3.Cross(playerUp, forward).normalized;

            Vector3 offset = forward * cfg.DogScoutForward + side * cfg.DogScoutSide;
            Vector3 anchor = SphericalSurface.Point(center, playerUp, cfg.PlanetRadius, 0f);
            return SphericalSurface.MoveOnSurface(center, anchor, offset, offset.magnitude);
        }

        /// <summary>回位目标：玩家→狗 方向上的 0.6R 处，跑到这里就算回到范围内。</summary>
        Vector3 ReturnPoint(WhiteboxConfig cfg)
        {
            Vector3 center = Vector3.zero;
            Vector3 playerUp = SphericalSurface.Up(game.Player.position, center);
            Vector3 dogUp = SphericalSurface.Up(transform.position, center);
            float dist = ArcFromPlayer(transform.position, cfg.PlanetRadius);
            if (dist < 0.001f) return game.Player.position;

            float want = cfg.DogRoamRadius * 0.6f;
            Vector3 dir = Vector3.Slerp(playerUp, dogUp, Mathf.Clamp01(want / dist));
            return SphericalSurface.Point(center, dir, cfg.PlanetRadius, 0f);
        }

        /// <summary>硬钳制兜底：任何情况下都不允许狗留在活动半径外（正常应由 Return 提前接管）。</summary>
        void ClampToRoam()
        {
            WhiteboxConfig cfg = game.Config;
            float radius = cfg.DogRoamRadius;
            Vector3 center = Vector3.zero;
            Vector3 playerUp = SphericalSurface.Up(game.Player.position, center);
            Vector3 dogUp = SphericalSurface.Up(transform.position, center);
            float dist = Vector3.Angle(playerUp, dogUp) * Mathf.Deg2Rad * cfg.PlanetRadius;
            if (dist <= radius) return;

            // 玩家跑得比狗快时会被拽着走，这里直接投影回边界，保证永不越界
            Vector3 dir = Vector3.Slerp(playerUp, dogUp, Mathf.Clamp01(radius / dist));
            transform.position = SphericalSurface.Point(center, dir, cfg.PlanetRadius, cfg.DogHeight * 0.5f);
            lastDogPos = transform.position;    // 钳制不算狗自己的速度，避免动画突然抽搐
        }

        /// <summary>
        /// 叼取：只有「追击状态 + 已走到拾取距离内 + 冷却结束」才真正拾取。
        /// 拾取成功后的状态更新链保持原样：Collect → Despawn → FloatText → OnWorldEvent → Dialogue。
        /// </summary>
        void Fetch(float dt)
        {
            lookTimer = Mathf.Max(0f, lookTimer - dt);
            cooldown -= dt;

            // 探路期间不叼：避免狗在玩家视野里突然把垃圾叼走造成视觉跳变
            if (motion != DogMotion.Seek || seekTarget == null || cooldown > 0f) return;

            WhiteboxConfig cfg = game.Config;
            if (ArcTo(seekTarget.transform.position, cfg.PlanetRadius) > cfg.DogPickDistance) return;

            int stage = game.Eco != null ? game.Eco.Stage : 0;
            stage = Mathf.Clamp(stage, 0, 2);

            int cat = (int)seekTarget.Category;
            if (!game.State.CanCollect(cfg, cat))
            {
                // 背包满：放弃这次目标，歇一会儿再自己找别的，不原地死磕
                cooldown = 2f;
                seekTarget = null;
                motion = DogMotion.Roam;
                hasRoamTarget = false;
                roamPause = Random.Range(0.6f, 1.2f);
                return;
            }
            if (game.Spawner.LastTrashOnSurface || game.State.Collect(cfg, cat, 1) <= 0) return;

            TrashCategory fetchedCategory = seekTarget.Category;
            game.Spawner.Despawn(seekTarget);
            game.Hud.FloatText("狗 +1");
            game.OnWorldEvent();
            game.Dialogue?.OnDogFetch(fetchedCategory);
            cooldown = cfg.DogFetchCooldownAt(stage);
            pulseTimer = PulseDuration;
            pulseUp = SphericalSurface.Up(transform.position, Vector3.zero);   // 记下叼中瞬间的基准点
            wagBoost = 0.6f;
            seekTarget = null;
            motion = DogMotion.Roam;
            hasRoamTarget = false;
            roamPause = Random.Range(0.3f, 0.8f);
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
            // 自主漫游后必须看「狗自己」的速度：否则会出现「狗在走、腿不动」
            bool moving = dogSpeed > 0.05f;
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
            float moving = dogSpeed > 0.05f ? 1f : 0f;
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
