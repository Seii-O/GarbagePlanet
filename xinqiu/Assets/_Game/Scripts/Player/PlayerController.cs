// @trace MECH-002
using UnityEngine;

namespace PlanetCleaner
{
    /// <summary>
    /// 球面行走（白膜版，纯第一人称）。
    ///
    /// 之前的两个坑：
    /// 1. CharacterController 撞球面会被"退让"卡住 → 现在不撞星球，改成每帧投影回球壳；
    /// 2. 半径写死 1，星球一放大/换模型，人就被塞进球里 → 现在运行时自动量测真实半径和球心。
    ///
    /// 量测顺序：SphereCollider（优先）→ 渲染包围盒 → 手填的 Planet Radius。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("星球中心（一般就是 PlanetRoot）")]
        [SerializeField] Transform planetCenter;

        [Tooltip("Main Camera 上的 PlayerCameraRig")]
        [SerializeField] PlayerCameraRig cameraRig;

        [Header("移动")]
        [SerializeField] float moveSpeed = 5.5f;

        [Tooltip("越大起步/刹车越快")]
        [SerializeField] float acceleration = 22f;

        [Tooltip("转身速度")]
        [SerializeField] float turnSpeed = 14f;

        [Header("球面")]
        [Tooltip("兜底用的星球半径：自动量测失败时才会用它")]
        [SerializeField] float planetRadius = 1f;

        [Tooltip("胶囊中心到球心的距离；填 0 表示自动 = 真实半径 + 胶囊半高")]
        [SerializeField] float shellDistanceOverride = 0f;

        [Tooltip("贴地时额外抬起的一点点，避免边缘穿插")]
        [SerializeField] float groundClearance = 0.02f;

        [Header("碰撞")]
        [Tooltip("忽略 Planet / Trash 碰撞（白膜推荐；以后加真实障碍物再关掉）")]
        [SerializeField] bool ignorePlanetAndTrash = true;

        [Header("调试")]
        [SerializeField] bool logGeometryOnStart = true;

        public Transform PlanetCenter
        {
            get => planetCenter;
            set
            {
                planetCenter = value;
                resolved = false;
            }
        }

        public PlayerCameraRig CameraRig
        {
            get => cameraRig;
            set => cameraRig = value;
        }

        public float PlanetRadius
        {
            get => planetRadius;
            set => planetRadius = value;
        }

        /// <summary>运行时量测到的真实星球半径。</summary>
        public float ActualPlanetRadius => actualRadius;

        /// <summary>运行时量测到的真实球心（世界坐标）。</summary>
        public Vector3 PlanetCenterWorld => centerWorld;

        public float HalfHeight => controller != null ? controller.height * 0.5f : 1f;

        public float PlanarSpeed => horizontalVelocity.magnitude;

        public bool IsGrounded => true;

        CharacterController controller;
        Vector3 horizontalVelocity;

        float shellDistance;
        float actualRadius;
        Vector3 centerWorld;
        bool resolved;

        void Awake()
        {
            controller = GetComponent<CharacterController>();

            if (planetCenter == null)
            {
                GameObject root = GameObject.Find("PlanetRoot");
                if (root != null) planetCenter = root.transform;
            }

            if (ignorePlanetAndTrash)
            {
                IgnoreLayer("Planet");
                IgnoreLayer("Trash");
            }
        }

        void Start()
        {
            ResolvePlanetGeometry();
            AlignToShell(true);
        }

        void Update()
        {
            if (planetCenter == null || controller == null) return;
            if (!resolved) ResolvePlanetGeometry();

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 up = (transform.position - centerWorld).normalized;
            if (up.sqrMagnitude < 0.0001f) return;

            // 1. 输入
            Vector3 input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            if (input.sqrMagnitude > 1f) input.Normalize();

            // 2. 相机方向 → 投影到球面切线
            Vector3 moveDir = Vector3.zero;
            if (input.sqrMagnitude > 0.0001f && cameraRig != null && cameraRig.Camera != null)
            {
                Transform cam = cameraRig.Camera.transform;

                Vector3 forward = Vector3.ProjectOnPlane(cam.forward, up);
                if (forward.sqrMagnitude < 0.0001f)
                    forward = Vector3.ProjectOnPlane(transform.forward, up);
                forward.Normalize();

                Vector3 right = Vector3.Cross(up, forward).normalized;
                moveDir = (forward * input.z + right * input.x).normalized;
            }

            // 3. 速度平滑
            Vector3 targetVelocity = moveDir * moveSpeed;
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, acceleration * dt);

            // 4. 朝向
            if (moveDir.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(moveDir, up);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, look, 1f - Mathf.Exp(-turnSpeed * dt));
            }

            // 5. 平移（忽略星球碰撞，不会被退让卡住）
            controller.Move(horizontalVelocity * dt);

            // 6. 贴回球壳
            AlignToShell(false);
        }

        // ---------------------------------------------------------------
        // 球面几何
        // ---------------------------------------------------------------

        /// <summary>重新量测星球半径与球心。换了模型 / 缩放了星球后调用一次即可。</summary>
        public void ResolvePlanetGeometry()
        {
            if (controller == null) controller = GetComponent<CharacterController>();

            centerWorld = planetCenter != null ? planetCenter.position : Vector3.zero;
            float measured = -1f;

            if (planetCenter != null)
            {
                // 1) 碰撞体最准（SphereCollider 的半径会跟着缩放走）
                SphereCollider sphere = planetCenter.GetComponentInChildren<SphereCollider>();
                if (sphere != null)
                {
                    centerWorld = sphere.transform.TransformPoint(sphere.center);
                    Vector3 scale = sphere.transform.lossyScale;
                    float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                    measured = sphere.radius * maxScale;
                }
                else
                {
                    // 2) 没有球体碰撞体就看渲染包围盒
                    Renderer renderer = planetCenter.GetComponentInChildren<Renderer>();
                    if (renderer != null)
                    {
                        Bounds bounds = renderer.bounds;
                        centerWorld = bounds.center;
                        measured = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
                    }
                }
            }

            actualRadius = measured > 0.01f ? measured : Mathf.Max(0.1f, planetRadius);

            if (measured <= 0.01f)
            {
                Debug.LogWarning(
                    "[Player] Planet Center 下面没找到球体碰撞体或渲染器，只能用兜底半径 " +
                    $"{planetRadius:F2}。如果星球换过模型，请把 Planet Center 指向新的星球物体。");
            }

            float halfHeight = controller != null ? controller.height * 0.5f : 1f;
            shellDistance = shellDistanceOverride > 0f
                ? shellDistanceOverride
                : actualRadius + halfHeight + groundClearance;

            resolved = true;

            if (logGeometryOnStart)
            {
                Debug.Log($"[Player] 星球量测：球心={centerWorld}，半径={actualRadius:F2}，" +
                          $"壳距={shellDistance:F2}，玩家离球心={Vector3.Distance(transform.position, centerWorld):F2}");
            }
        }

        void AlignToShell(bool resetVelocity)
        {
            Vector3 offset = transform.position - centerWorld;
            Vector3 dir = offset.sqrMagnitude < 0.000001f ? Vector3.up : offset.normalized;

            transform.position = centerWorld + dir * shellDistance;

            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, dir);
            if (forward.sqrMagnitude < 0.000001f)
                forward = Vector3.ProjectOnPlane(Vector3.forward, dir);
            if (forward.sqrMagnitude < 0.000001f)
                forward = Vector3.ProjectOnPlane(Vector3.right, dir);

            transform.rotation = Quaternion.LookRotation(forward.normalized, dir);

            if (resetVelocity) horizontalVelocity = Vector3.zero;
        }

        void IgnoreLayer(string layerName)
        {
            int other = LayerMask.NameToLayer(layerName);
            int self = gameObject.layer;

            if (other < 0 || self < 0 || self == other) return;

            Physics.IgnoreLayerCollision(self, other, true);
        }

        /// <summary>传送回球面（调试用）。direction 会被归一化。</summary>
        public void SnapToSurface(Vector3 direction)
        {
            ResolvePlanetGeometry();

            Vector3 up = direction.normalized;
            transform.position = centerWorld + up * shellDistance;
            transform.rotation = Quaternion.LookRotation(
                Vector3.ProjectOnPlane(Vector3.forward, up).normalized, up);

            horizontalVelocity = Vector3.zero;
        }

        void OnValidate()
        {
            if (moveSpeed < 0f) moveSpeed = 0f;
            if (acceleration < 0.01f) acceleration = 0.01f;
            if (planetRadius <= 0f) planetRadius = 1f;
            if (groundClearance < 0f) groundClearance = 0f;

            resolved = false;
        }
    }
}
