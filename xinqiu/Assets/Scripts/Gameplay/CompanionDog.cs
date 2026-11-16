// @trace MECH-014, MECH-015, MECH-016, NUM-010, ART-011, ART-012
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 伙伴狗（B 档，功能伙伴）：球面跟随 + 冷却到点自动叼回范围内普通垃圾。
    /// ART-012 / v1.2 D6：外观全程恒定。材质只在 Init 写入一次，之后本类与 EcoStage
    /// 都不会改写狗的任何 Renderer；没有 UpdateLook、没有阶段色、没有姿态插值。
    /// [待拍板 C13] 固定色号取干净米黄作为默认值，改 WhiteboxConfig.dogColorHex 即可换。
    /// </summary>
    public sealed class CompanionDog : MonoBehaviour
    {
        [SerializeField] Renderer[] fixedColorRenderers;
        [SerializeField] Material fixedMaterial;

        WhiteboxGame game;
        float cooldown;

        public Renderer[] FixedColorRenderers { get => fixedColorRenderers; set => fixedColorRenderers = value; }
        public Material FixedMaterial { get => fixedMaterial; set => fixedMaterial = value; }

        public void Init(WhiteboxGame g)
        {
            game = g;
            cooldown = g.Config.DogFetchCooldown[0];
            if (fixedMaterial != null && fixedColorRenderers != null)
            {
                for (int i = 0; i < fixedColorRenderers.Length; i++)
                    if (fixedColorRenderers[i] != null) fixedColorRenderers[i].sharedMaterial = fixedMaterial;
            }
            Debug.Log("[WhiteboxDog] 固定外观已写入，色号=" + ColorUtility.ToHtmlStringRGB(g.Config.DogColor)
                      + "；后续阶段切换不会再改狗的材质");
        }

        void Update()
        {
            if (game == null || game.Player == null || game.IsUiBlocking) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Follow(dt);
            Fetch(dt);
        }

        void Follow(float dt)
        {
            WhiteboxConfig cfg = game.Config;
            Vector3 center = Vector3.zero;
            Vector3 player = game.Player.position;
            Vector3 up = SphericalSurface.Up(player, center);
            Vector3 surface = SphericalSurface.Point(center, up, cfg.PlanetRadius, 0f);
            Vector3 back = Vector3.ProjectOnPlane(-game.Player.forward, up);
            if (back.sqrMagnitude < 0.0001f) back = Vector3.ProjectOnPlane(Vector3.back, up);
            back.Normalize();

            Vector3 desired = SphericalSurface.MoveOnSurface(center, surface, back, cfg.DogFollowDistance);
            Vector3 desiredUp = SphericalSurface.Up(desired, center);
            Vector3 currentUp = SphericalSurface.Up(transform.position, center);
            Vector3 newUp = Vector3.Slerp(currentUp, desiredUp, 1f - Mathf.Exp(-3.5f * dt));
            SphericalSurface.Place(transform, center, newUp, cfg.PlanetRadius,
                cfg.DogHeight * 0.5f, game.Player.forward);
        }

        void Fetch(float dt)
        {
            cooldown -= dt;
            if (cooldown > 0f) return;

            WhiteboxConfig cfg = game.Config;
            int stage = game.Eco != null ? game.Eco.Stage : 0;
            stage = Mathf.Clamp(stage, 0, 2);
            // 阶段一只叼塑料与有机；阶段二起四类（策划案 D 节，[待确认] 白膜按建议默认实现）
            int mask = stage == 0 ? (1 << (int)TrashCategory.Plastic) | (1 << (int)TrashCategory.Organic) : 0xF;
            TrashItem t = game.Spawner.FindNearest(transform.position, cfg.DogFetchRadius[stage],
                mask, game.Interaction.CurrentTarget);
            if (t == null) return;

            int cat = (int)t.Category;
            if (!game.State.CanCollect(cfg, cat)) return;
            if (game.State.Collect(cfg, cat, 1) <= 0) return;

            game.Spawner.Despawn(t);
            game.Hud.FloatText("狗 +1");
            game.OnWorldEvent();
            cooldown = cfg.DogFetchCooldown[stage];
        }
    }
}
