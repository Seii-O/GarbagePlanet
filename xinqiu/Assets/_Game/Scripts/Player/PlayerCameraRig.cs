using UnityEngine;

namespace PlanetCleaner
{
    /// <summary>
    /// 第一人称相机（纯第一人称，没有第三人称了）。
    ///
    /// 操作：
    /// - 进入 Play 自动锁定鼠标，鼠标移动 = 转视角，不需要按住任何键；
    /// - Esc 解锁鼠标 → 这时可以用左键拖动星球（星球旋转）；
    /// - 按 L 重新锁定鼠标 → 回到看视角模式。
    ///
    /// 挂载：Main Camera 上。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class PlayerCameraRig : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("玩家根节点")]
        [SerializeField] Transform target;

        [Tooltip("玩家上的 PlayerController（用来取胶囊高度）")]
        [SerializeField] PlayerController player;

        [Tooltip("星球中心，用于防止相机穿进球体")]
        [SerializeField] Transform planetCenter;

        [Header("视角")]
        [Tooltip("鼠标灵敏度：度 / 鼠标单位")]
        [SerializeField] float mouseSensitivity = 2.2f;

        [SerializeField] bool invertLookY;

        [Tooltip("进入 Play 自动锁定鼠标（Esc 解锁，L 重新锁定）")]
        [SerializeField] bool lockCursorOnPlay = true;

        [Header("俯仰限制")]
        [Range(30f, 89f)]
        [SerializeField] float pitchLimit = 85f;

        [Header("触屏")]
        [Tooltip("几根手指转视角（1 = 单指看视角，此时星球拖拽让位）")]
        [SerializeField] int touchFingersToLook = 2;

        [Tooltip("触屏灵敏度（度 / 像素）")]
        [SerializeField] float touchSensitivity = 0.16f;

        [Header("眼睛")]
        [Tooltip("眼睛高度（从脚底算起）")]
        [SerializeField] float eyeHeight = 1.62f;

        [SerializeField] float fov = 72f;
        [SerializeField] float fovLerpSpeed = 10f;

        public Camera Camera => cam;

        /// <summary>本帧视角转动量（度），双手晃动会用到。</summary>
        public Vector2 LastLookDelta { get; private set; }

        public Transform Target
        {
            get => target;
            set => target = value;
        }

        public PlayerController Player
        {
            get => player;
            set => player = value;
        }

        public Transform PlanetCenter
        {
            get => planetCenter;
            set => planetCenter = value;
        }

        Camera cam;

        // 第一人称视线方向（单位向量，世界空间）
        Vector3 viewDir;
        // 上一帧的球面法线，行走时用它把视线"搬运"到新切平面
        Vector3 lastUp;

        bool warnedUnlock;

        void Awake()
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (target == null && player != null) target = player.transform;
            if (planetCenter == null && player != null) planetCenter = player.PlanetCenter;

            // 防止 Target 被误指到星球之类的物体：否则隐藏身体时会把星球一起藏了
            if (target != null && player != null && target.GetComponent<PlayerController>() == null)
                target = player.transform;

            cam.nearClipPlane = 0.03f;
            cam.fieldOfView = fov;

            // 第一人称不显示自己的身体（双手是相机的子物体，不受影响）
            SetPlayerVisible(false);
        }

        void Start()
        {
            if (lockCursorOnPlay && !Application.isMobilePlatform)
                LockCursor(true);
        }

        void Update()
        {
            // Esc 只负责解锁（Unity 本身也会解），L 负责锁回去，避免同一个键来回打架
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                LockCursor(false);
                return;
            }

            if (Input.GetKeyDown(KeyCode.L))
                LockCursor(true);
        }

        void LateUpdate()
        {
            if (target == null || cam == null) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 up = target.up;
            if (up.sqrMagnitude < 0.0001f) return;
            up.Normalize();

            EnsureInitialized(up);
            AdoptUpChange(up);

            LastLookDelta = ReadLookDelta();
            ApplyLook(up, LastLookDelta);

            Vector3 eye = ComputeEye(up);
            transform.position = eye;
            transform.rotation = Quaternion.LookRotation(viewDir, up);

            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, 1f - Mathf.Exp(-fovLerpSpeed * dt));
        }

        // ---------------------------------------------------------------
        // 初始化 / 跟着球面走
        // ---------------------------------------------------------------

        void EnsureInitialized(Vector3 up)
        {
            if (viewDir.sqrMagnitude > 0.0001f && lastUp.sqrMagnitude > 0.0001f) return;

            Vector3 fwd = transform.forward;
            Vector3 tangent = Vector3.ProjectOnPlane(fwd, up);
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.ProjectOnPlane(target.forward, up);
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.ProjectOnPlane(Vector3.forward, up);

            viewDir = tangent.normalized;
            lastUp = up;
        }

        /// <summary>玩家在球面上走动时，把视线整体转到新切平面，保持相对角度。</summary>
        void AdoptUpChange(Vector3 up)
        {
            if (lastUp.sqrMagnitude < 0.0001f)
            {
                lastUp = up;
                return;
            }

            if (Vector3.Angle(lastUp, up) > 0.01f)
            {
                Quaternion delta = Quaternion.FromToRotation(lastUp, up);
                viewDir = delta * viewDir;
            }

            lastUp = up;
        }

        // ---------------------------------------------------------------
        // 视角输入
        // ---------------------------------------------------------------

        Vector2 ReadLookDelta()
        {
            if (Input.touchCount >= Mathf.Max(1, touchFingersToLook))
            {
                Vector2 sum = Vector2.zero;
                for (int i = 0; i < Input.touchCount; i++)
                    sum += Input.GetTouch(i).deltaPosition;

                return sum / Input.touchCount * touchSensitivity;
            }

            if (Cursor.lockState != CursorLockMode.Locked)
                return Vector2.zero;

            return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * mouseSensitivity;
        }

        void ApplyLook(Vector3 up, Vector2 lookDegrees)
        {
            if (lookDegrees.sqrMagnitude < 0.000001f) return;

            float yaw = lookDegrees.x;
            float pitch = lookDegrees.y * (invertLookY ? -1f : 1f);

            viewDir = Quaternion.AngleAxis(yaw, up) * viewDir;

            float elevation = Mathf.Asin(Mathf.Clamp(Vector3.Dot(viewDir, up), -1f, 1f));
            elevation += pitch * Mathf.Deg2Rad;
            elevation = Mathf.Clamp(elevation, -pitchLimit * Mathf.Deg2Rad, pitchLimit * Mathf.Deg2Rad);

            viewDir = WithElevation(viewDir, up, elevation);
        }

        // ---------------------------------------------------------------
        // 位置
        // ---------------------------------------------------------------

        Vector3 ComputeEye(Vector3 up)
        {
            float halfHeight = player != null ? player.HalfHeight : 1f;
            Vector3 feet = target.position - up * halfHeight;
            return feet + up * eyeHeight;
        }

        // ---------------------------------------------------------------
        // 光标
        // ---------------------------------------------------------------

        void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;

            if (!locked && !warnedUnlock)
            {
                warnedUnlock = true;
                Debug.Log("[相机] 鼠标已解锁：左键拖动可以转星球；按 L 重新锁定视角。");
            }
        }

        void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void SetPlayerVisible(bool visible)
        {
            if (target == null) return;

            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].enabled = visible;
        }

        // ---------------------------------------------------------------
        // 数学
        // ---------------------------------------------------------------

        static Vector3 WithElevation(Vector3 dir, Vector3 up, float elevationRad)
        {
            Vector3 tangent = dir - up * Vector3.Dot(dir, up);
            if (tangent.sqrMagnitude < 0.000001f)
                tangent = Vector3.ProjectOnPlane(Vector3.forward, up);

            if (tangent.sqrMagnitude < 0.000001f)
                return elevationRad >= 0f ? up : -up;

            tangent.Normalize();
            return (tangent * Mathf.Cos(elevationRad) + up * Mathf.Sin(elevationRad)).normalized;
        }
    }
}
