using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PlanetCleaner
{
    /// <summary>
    /// 3D 星球拖拽旋转（白膜版）。
    ///
    /// 设计要点：
    /// 1. 只旋转 PlanetRoot 的欧拉角，不用 Rigidbody、不做物理力矩 —— 手感稳定、不会飘；
    /// 2. 屏幕横轴 → 绕世界 Y 轴转，屏幕纵轴 → 绕世界 X 轴转，pitch 夹住避免翻到球底；
    /// 3. 同时区分「拖拽」和「点击」：点一下（或按住 0.45 秒不动）不算拖拽，
    ///    抬起时抛 Tapped 事件，阶段 2 的射线清理直接订阅它即可；
    /// 4. 鼠标在 UI 上时不旋转（EventSystem 拦截，触屏带 fingerId）。
    ///
    /// 挂载：挂在 PlanetRoot（空物体）上，球体和垃圾都是它的子物体。
    /// </summary>
    [DisallowMultipleComponent]
    public class PlanetDragRotator : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("观察星球的相机；留空自动取 Camera.main")]
        [SerializeField] Camera targetCamera;

        [Tooltip("被旋转的父节点；留空则旋转自身")]
        [SerializeField] Transform planetRoot;

        [Header("手感")]
        [Tooltip("纵向滑动一整个屏幕高度，星球转多少度（默认半圈）")]
        [SerializeField] float degreesPerScreenHeight = 180f;

        [Range(10f, 89f)]
        [Tooltip("俯仰角限制，防止翻到球底、UI 跟着倒过来")]
        [SerializeField] float pitchLimit = 80f;

        [Tooltip("按下后位移超过该像素数，判定为拖拽而不是点击")]
        [SerializeField] float dragThresholdPixels = 10f;

        [Tooltip("按下超过该秒数，即使没位移也视为拖拽（长按查看时不误清垃圾）")]
        [SerializeField] float holdToDragSeconds = 0.45f;

        [Header("松手惯性")]
        [Tooltip("阻尼系数，越大停得越快")]
        [SerializeField] float inertiaDamping = 6f;

        [Tooltip("惯性最大角速度（度/秒）")]
        [SerializeField] float maxInertiaSpeed = 720f;

        [Header("方向微调")]
        [Tooltip("勾上则左右方向反过来")]
        [SerializeField] bool invertHorizontal;

        [Tooltip("勾上则上下方向反过来")]
        [SerializeField] bool invertVertical;

        [Header("调试")]
        [SerializeField] bool logEvents;

        [Header("触屏")]
        [Tooltip("触屏手指达到该数量时让位给相机看视角（不再旋转星球）")]
        [SerializeField] int ignoreWhenTouchCountReaches = 2;

        /// <summary>点击（非拖拽）抬起时触发，参数是屏幕坐标。</summary>
        public event Action<Vector2> Tapped;

        /// <summary>开始拖拽时触发。</summary>
        public event Action<Vector2> DragStarted;

        /// <summary>拖拽结束时触发。</summary>
        public event Action DragEnded;

        public bool IsDragging { get; private set; }
        public float Yaw => yaw;
        public float Pitch => pitch;

        public Camera TargetCamera
        {
            get => targetCamera;
            set => targetCamera = value;
        }

        Transform Pivot => planetRoot != null ? planetRoot : transform;

        float yaw;
        float pitch;

        Vector2 pressPos;
        Vector2 lastPos;
        float pressTime;
        bool tracking;
        int activeFinger = -1;

        // 松手惯性：(yaw速度, pitch速度)，单位 度/秒
        Vector2 inertiaVelocity;

        void Awake()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;

            // 保留场景里摆好的初始朝向，避免一进 Play 就归零
            Vector3 euler = Pivot.rotation.eulerAngles;
            yaw = euler.y;
            pitch = NormalizePitch(euler.x);
        }

        void Update()
        {
            if (Input.touchCount > 0)
                HandleTouchInput();
            else
                HandleMouseInput();

            ApplyInertia();
        }

        // ---------------------------------------------------------------
        // 输入
        // ---------------------------------------------------------------

        void HandleMouseInput()
        {
            if (Input.GetMouseButtonDown(0))
            {
                if (IsPointerOverUI(-1))
                {
                    tracking = false;
                    return;
                }
                BeginPress(Input.mousePosition);
                return;
            }

            if (!tracking) return;

            if (Input.GetMouseButton(0))
                MoveTo(Input.mousePosition);
            else if (Input.GetMouseButtonUp(0))
                Release(Input.mousePosition);
        }

        void HandleTouchInput()
        {
            // 两指（及以上）交给相机：单指转星球，双指看视角
            if (Input.touchCount >= Mathf.Max(2, ignoreWhenTouchCountReaches))
            {
                CancelTouch();
                return;
            }

            // 优先追踪已经按下的那根手指，避免第二根手指打断
            if (activeFinger < 0)
            {
                HandleTouch(Input.GetTouch(0));
                return;
            }

            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch t = Input.GetTouch(i);
                if (t.fingerId == activeFinger)
                {
                    HandleTouch(t);
                    return;
                }
            }
        }

        void CancelTouch()
        {
            if (!tracking) return;
            tracking = false;
            IsDragging = false;
            activeFinger = -1;
            inertiaVelocity = Vector2.zero;
        }

        void HandleTouch(Touch touch)
        {
            switch (touch.phase)
            {
                case TouchPhase.Began:
                    if (activeFinger >= 0) return;
                    if (IsPointerOverUI(touch.fingerId)) return;
                    activeFinger = touch.fingerId;
                    BeginPress(touch.position);
                    break;

                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                    if (touch.fingerId != activeFinger || !tracking) return;
                    MoveTo(touch.position);
                    break;

                case TouchPhase.Ended:
                case TouchPhase.Canceled:
                    if (touch.fingerId != activeFinger) return;
                    if (tracking) Release(touch.position);
                    activeFinger = -1;
                    break;
            }
        }

        // ---------------------------------------------------------------
        // 按下 / 移动 / 抬起
        // ---------------------------------------------------------------

        void BeginPress(Vector2 screenPos)
        {
            pressPos = screenPos;
            lastPos = screenPos;
            pressTime = Time.unscaledTime;
            tracking = true;
            IsDragging = false;
            inertiaVelocity = Vector2.zero;
        }

        void MoveTo(Vector2 screenPos)
        {
            Vector2 delta = screenPos - lastPos;
            lastPos = screenPos;

            if (!IsDragging)
            {
                bool movedEnough = (screenPos - pressPos).sqrMagnitude >
                                   dragThresholdPixels * dragThresholdPixels;
                bool heldLongEnough = Time.unscaledTime - pressTime > holdToDragSeconds;

                if (!movedEnough && !heldLongEnough) return;

                IsDragging = true;
                DragStarted?.Invoke(screenPos);
                if (logEvents) Debug.Log("[PlanetDragRotator] DragStarted");
            }

            if (delta.sqrMagnitude <= 0f) return;

            Vector2 applied = ApplyDelta(delta);

            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            Vector2 instant = applied / dt;
            inertiaVelocity = Vector2.Lerp(inertiaVelocity, instant, 0.5f);
        }

        void Release(Vector2 screenPos)
        {
            bool wasDragging = IsDragging;

            tracking = false;
            IsDragging = false;

            if (wasDragging)
            {
                float speed = inertiaVelocity.magnitude;
                if (speed > maxInertiaSpeed)
                    inertiaVelocity *= maxInertiaSpeed / speed;

                DragEnded?.Invoke();
                if (logEvents) Debug.Log("[PlanetDragRotator] DragEnded");
                return;
            }

            // 没移动、也没按太久 → 这是一次点击
            bool isTap = (screenPos - pressPos).sqrMagnitude <=
                         dragThresholdPixels * dragThresholdPixels &&
                         Time.unscaledTime - pressTime <= holdToDragSeconds;

            inertiaVelocity = Vector2.zero;

            if (isTap)
            {
                Tapped?.Invoke(screenPos);
                if (logEvents) Debug.Log($"[PlanetDragRotator] Tapped {screenPos}");
            }
        }

        // ---------------------------------------------------------------
        // 旋转
        // ---------------------------------------------------------------

        /// <summary>应用一次像素位移，返回实际生效的角度增量。</summary>
        Vector2 ApplyDelta(Vector2 deltaPixels)
        {
            float degPerPixel = degreesPerScreenHeight / Mathf.Max(1f, Screen.height);

            // 手指往右拖 → 球面也跟着往右走（直接操作手感）
            float yawDelta = -deltaPixels.x * degPerPixel * (invertHorizontal ? -1f : 1f);
            float pitchDelta = deltaPixels.y * degPerPixel * (invertVertical ? -1f : 1f);

            yaw = Mathf.Repeat(yaw + yawDelta, 360f);

            float prevPitch = pitch;
            pitch = Mathf.Clamp(pitch + pitchDelta, -pitchLimit, pitchLimit);
            float appliedPitch = pitch - prevPitch;

            ApplyRotation();
            return new Vector2(yawDelta, appliedPitch);
        }

        void ApplyInertia()
        {
            if (tracking || inertiaVelocity.sqrMagnitude < 0.01f) return;

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (dt <= 0f) return;

            yaw = Mathf.Repeat(yaw + inertiaVelocity.x * dt, 360f);

            float prevPitch = pitch;
            pitch = Mathf.Clamp(pitch + inertiaVelocity.y * dt, -pitchLimit, pitchLimit);
            if (Mathf.Approximately(pitch, prevPitch))
            {
                // 撞到俯仰限制，纵向惯性清零，避免"贴边抖动"
                inertiaVelocity.y = 0f;
            }

            ApplyRotation();

            inertiaVelocity *= Mathf.Exp(-inertiaDamping * dt);
            if (inertiaVelocity.sqrMagnitude < 1f)
                inertiaVelocity = Vector2.zero;
        }

        void ApplyRotation()
        {
            Pivot.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        // ---------------------------------------------------------------
        // 工具
        // ---------------------------------------------------------------

        static bool IsPointerOverUI(int fingerId)
        {
            EventSystem es = EventSystem.current;
            if (es == null) return false;

            return fingerId < 0
                ? es.IsPointerOverGameObject()
                : es.IsPointerOverGameObject(fingerId);
        }

        static float NormalizePitch(float degrees)
        {
            if (degrees > 180f) degrees -= 360f;
            return Mathf.Clamp(degrees, -89f, 89f);
        }

        [ContextMenu("重置为初始朝向")]
        void ResetViewInEditor()
        {
            yaw = 0f;
            pitch = 0f;
            ApplyRotation();
        }

        void OnValidate()
        {
            if (degreesPerScreenHeight < 1f) degreesPerScreenHeight = 1f;
            if (dragThresholdPixels < 0f) dragThresholdPixels = 0f;
            if (holdToDragSeconds < 0f) holdToDragSeconds = 0f;
            if (inertiaDamping < 0f) inertiaDamping = 0f;
        }
    }
}
