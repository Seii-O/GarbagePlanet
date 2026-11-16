using UnityEngine;

namespace PlanetCleaner
{
    /// <summary>
    /// 第一人称双手：走路上下晃动、静止呼吸、转视角时轻微滞后。
    /// 白膜阶段用两个胶囊 + 两个方块，后面换成真手模只需要替换 Mesh。
    ///
    /// 挂载：相机的子物体 "Hands" 上，由 Tools > 星球白膜 > ③ 自动生成。
    /// </summary>
    public class FirstPersonHands : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] Transform leftHand;
        [SerializeField] Transform rightHand;
        [SerializeField] PlayerController player;
        [SerializeField] PlayerCameraRig cameraRig;

        [Header("走路晃动")]
        [Tooltip("迈步频率")]
        [SerializeField] float walkBobSpeed = 8f;

        [Tooltip("走路时的上下幅度（米）")]
        [SerializeField] float walkBobAmount = 0.022f;

        [Tooltip("静止时的呼吸幅度")]
        [SerializeField] float idleBobAmount = 0.004f;

        [Header("转视角滞后")]
        [Tooltip("视角转动时手部反向拖拽的比例")]
        [SerializeField] float swayFactor = 0.0035f;

        [SerializeField] float maxSway = 0.045f;
        [SerializeField] float swaySmooth = 7f;

        public Transform LeftHand
        {
            get => leftHand;
            set => leftHand = value;
        }

        public Transform RightHand
        {
            get => rightHand;
            set => rightHand = value;
        }

        public PlayerController Player
        {
            get => player;
            set => player = value;
        }

        public PlayerCameraRig CameraRig
        {
            get => cameraRig;
            set => cameraRig = value;
        }

        Vector3 leftBase;
        Vector3 rightBase;
        Quaternion leftRotBase = Quaternion.identity;
        Quaternion rightRotBase = Quaternion.identity;
        Vector3 sway;
        float bobPhase;

        void Awake()
        {
            if (leftHand != null)
            {
                leftBase = leftHand.localPosition;
                leftRotBase = leftHand.localRotation;
            }

            if (rightHand != null)
            {
                rightBase = rightHand.localPosition;
                rightRotBase = rightHand.localRotation;
            }
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float speed01 = 0f;
            if (player != null)
            {
                // 5 m/s 当作满速，晃到最大
                speed01 = Mathf.Clamp01(player.PlanarSpeed / 5f);
            }

            // 走路：频率随速度提高，幅度随速度放大
            bobPhase += dt * walkBobSpeed * (0.35f + speed01);

            float bobY = Mathf.Sin(bobPhase * 2f) * walkBobAmount * speed01
                         + Mathf.Sin(Time.time * 1.3f) * idleBobAmount;

            float bobX = Mathf.Cos(bobPhase) * walkBobAmount * 0.6f * speed01;

            // 转视角 → 手往反方向"甩"一点，再弹回来
            Vector2 look = cameraRig != null ? cameraRig.LastLookDelta : Vector2.zero;
            Vector3 targetSway = new Vector3(-look.x * swayFactor, -look.y * swayFactor, 0f);
            targetSway = Vector3.ClampMagnitude(targetSway, maxSway);

            sway = Vector3.Lerp(sway, targetSway, 1f - Mathf.Exp(-swaySmooth * dt));

            Vector3 bob = new Vector3(bobX, bobY, 0f);
            Vector3 offset = bob + sway;

            Quaternion rotOffset = Quaternion.Euler(-sway.y * 35f, -sway.x * 35f, 0f);

            if (leftHand != null)
            {
                leftHand.localPosition = leftBase + offset;
                leftHand.localRotation = leftRotBase * rotOffset;
            }

            if (rightHand != null)
            {
                // 右手镜像一点，避免两只手完全同步显得假
                rightHand.localPosition = rightBase + new Vector3(-offset.x, offset.y, offset.z);
                rightHand.localRotation = rightRotBase * rotOffset;
            }
        }
    }
}
