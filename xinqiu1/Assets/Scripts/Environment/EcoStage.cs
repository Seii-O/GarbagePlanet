// @trace ART-002, ART-003, ART-005, ART-008, ART-012
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 环境三阶段表现：地表 / 水 / 天空 / 雾 / 主光角度一起平滑插值（3 s），
    /// 阶段升级叠一次 2.5 s 全屏白光（ART-008 时长表）。
    /// 只改 ground / water 两个 Renderer 的材质实例 —— 狗的任何 Renderer 都不在本类接触范围内，
    /// 这是 ART-012「狗固定外观」在工程侧的保障。
    /// </summary>
    public sealed class EcoStage : MonoBehaviour
    {
        WhiteboxGame game;
        Material groundInst;
        Material waterInst;
        Color groundFrom, groundTo, waterFrom, waterTo, skyFrom, skyTo, fogFrom, fogTo;
        float fogDistFrom, fogDistTo, angleFrom, angleTo;
        float fade = 1f;
        float flash;
        int stage;

        public int Stage => stage;

        public void Init(WhiteboxGame g)
        {
            game = g;
            WhiteboxConfig cfg = g.Config;
            if (g.Ground != null) groundInst = g.Ground.material;   // 取材质实例，不改资产
            if (g.Water != null) waterInst = g.Water.material;
            stage = cfg.StageOf(Progress());
            ApplyPalette(stage, stage, 1f);
            fade = 1f;
            flash = 0f;
        }

        float Progress()
        {
            WhiteboxState s = game.State;
            return (s.landProgress + s.waterProgress) * 0.5f;
        }

        public void Refresh(bool allowFlash)
        {
            WhiteboxConfig cfg = game.Config;
            int target = cfg.StageOf(Progress());
            if (target == stage) return;

            CaptureCurrent();
            int old = stage;
            stage = target;
            groundTo = cfg.GroundColors[stage];
            waterTo = cfg.WaterColors[stage];
            skyTo = cfg.SkyColors[stage];
            fogTo = cfg.FogColors[stage];
            fogDistTo = cfg.FogDistance[stage];
            angleTo = cfg.LightAngle[stage];
            fade = 0f;
            if (allowFlash && target > old)
            {
                flash = cfg.StageFlashSeconds;
                game.Notice("生态阶段升级 → " + (stage + 1) + "/3");
            }
        }

        void CaptureCurrent()
        {
            WhiteboxConfig cfg = game.Config;
            groundFrom = groundInst != null ? groundInst.color : cfg.GroundColors[stage];
            waterFrom = waterInst != null ? waterInst.color : cfg.WaterColors[stage];
            skyFrom = game.ViewCamera != null ? game.ViewCamera.backgroundColor : cfg.SkyColors[stage];
            fogFrom = RenderSettings.fogColor;
            fogDistFrom = RenderSettings.fogEndDistance;
            angleFrom = angleTo;
        }

        void Update()
        {
            if (game == null || game.Config == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (fade < 1f)
            {
                fade = Mathf.Clamp01(fade + dt / Mathf.Max(0.1f, game.Config.StageFadeSeconds));
                ApplyPalette(stage, stage, Mathf.SmoothStep(0f, 1f, fade));
            }
            if (flash > 0f) flash = Mathf.Max(0f, flash - dt);
            if (game.Hud != null)
                game.Hud.FlashAlpha = flash > 0f
                    ? Mathf.Clamp01(flash / Mathf.Max(0.1f, game.Config.StageFlashSeconds)) * 0.65f : 0f;
        }

        void ApplyPalette(int fromIndex, int toIndex, float t)
        {
            WhiteboxConfig cfg = game.Config;
            if (t >= 1f)
            {
                groundFrom = groundTo = cfg.GroundColors[toIndex];
                waterFrom = waterTo = cfg.WaterColors[toIndex];
                skyFrom = skyTo = cfg.SkyColors[toIndex];
                fogFrom = fogTo = cfg.FogColors[toIndex];
                fogDistFrom = fogDistTo = cfg.FogDistance[toIndex];
                angleFrom = angleTo = cfg.LightAngle[toIndex];
            }

            Color ground = Color.Lerp(groundFrom, groundTo, t);
            Color water = Color.Lerp(waterFrom, waterTo, t);
            Color sky = Color.Lerp(skyFrom, skyTo, t);
            Color fog = Color.Lerp(fogFrom, fogTo, t);
            float dist = Mathf.Lerp(fogDistFrom, fogDistTo, t);
            float angle = Mathf.Lerp(angleFrom, angleTo, t);

            if (groundInst != null) SetColor(groundInst, ground);
            if (waterInst != null) SetColor(waterInst, water);
            if (game.ViewCamera != null) game.ViewCamera.backgroundColor = sky;
            RenderSettings.fogColor = fog;
            RenderSettings.fogEndDistance = dist;
            if (game.Sun != null) game.Sun.transform.rotation = Quaternion.Euler(angle, -30f, 0f);
        }

        static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            m.color = c;
        }
    }
}
