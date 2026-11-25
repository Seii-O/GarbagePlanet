// @trace AUDIO-001
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 玩家脚步：按「实际位移速度」判定走 / 跑，并用间隔计数触发 Step / Run 音效。
    ///
    /// 为什么不用角色控制器里的 velocity：PlayerController 在 PlanetCleaner 程序集里，
    /// 本工程只读它的 Transform，不改它。用每帧位移算速度对球面行走同样成立。
    ///
    /// 红线：只读玩家位置、只调 Sfx.PlayAt，不写任何玩法状态。
    /// </summary>
    public sealed class PlayerFootsteps : MonoBehaviour
    {
        /// <summary>低于这个速度视为站着，不出脚步。</summary>
        const float WalkSpeed = 1.2f;
        /// <summary>高于这个速度按跑步处理。</summary>
        const float RunSpeed = 4.2f;

        const float WalkInterval = 0.52f;
        const float RunInterval = 0.32f;

        WhiteboxGame game;
        Vector3 lastPos;
        bool ready;
        float timer;
        float smoothSpeed;

        public void Init(WhiteboxGame g)
        {
            game = g;
            ready = g != null && g.Player != null;
            if (ready) lastPos = g.Player.position;
            timer = 0f;
            smoothSpeed = 0f;
        }

        void Update()
        {
            if (!ready || game == null || game.Player == null) return;

            // 面板/暂停/结算时不踩：直接把基准点跟上，避免关面板瞬间算出一个巨大的假速度
            if (game.IsUiBlocking)
            {
                lastPos = game.Player.position;
                smoothSpeed = 0f;
                timer = 0f;
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 pos = game.Player.position;
            float speed = Vector3.Distance(pos, lastPos) / dt;
            lastPos = pos;
            // 位移含上下坡的垂直分量，平滑一下避免忽快忽慢导致脚步忽密忽疏
            smoothSpeed = Mathf.Lerp(smoothSpeed, speed, 0.25f);

            if (smoothSpeed < WalkSpeed)
            {
                timer = 0f; // 停下后下一次起步立刻出脚
                return;
            }

            bool running = smoothSpeed >= RunSpeed;
            float interval = running ? RunInterval : WalkInterval;
            // 越快步频越高
            interval *= Mathf.Clamp(2.2f / Mathf.Max(1f, smoothSpeed), 0.6f, 1.4f);
            interval = Mathf.Clamp(interval, 0.22f, 0.7f);

            timer -= dt;
            if (timer > 0f) return;
            timer = interval;

            Sfx.PlayAt(running ? SfxId.Run : SfxId.Step, pos, 1f);
        }
    }
}
