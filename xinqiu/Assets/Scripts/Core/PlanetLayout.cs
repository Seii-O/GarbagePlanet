// @trace MECH-002, LEV-003
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 星球尺寸的统一入口：场景里的星球体积是烘焙出来的（编辑器生成），
    /// 而半径是 WhiteboxConfig 里可以随时改的数值。两者一旦不一致，
    /// 垃圾、玩家、狗都会按配置半径浮在空中或埋进球里。
    ///
    /// 本类在启动时做一次对齐：按配置半径重设星球本体的缩放，
    /// 并把贴在地表上的固定道具（水体 / 中转站 / 四区块地标）沿原方向挪到新半径，
    /// 保留它们原本的离地高度。玩家与狗由各自的控制脚本自动量测/跟随，不需要在这里搬。
    /// 幂等：半径已经一致时直接返回 false，不产生任何改动。
    /// </summary>
    public static class PlanetLayout
    {
        static readonly string[] SurfacePropPrefixes =
        {
            "env_west_water", "env_station", "env_landmark"
        };

        // 贴地「平面装饰」（水体圆盘等）必须跟着半径一起缩，否则半径减半后 3.2 m 的圆盘
        // 会横跨半个星球并把边缘埋进球体；建筑 / 地标是实体道具，只搬位置、不缩体积。
        static readonly string[] ScaleWithRadiusPrefixes =
        {
            "env_west_water"
        };

        /// <summary>量测星球当前的真实世界半径：优先星球本体上的 SphereCollider，其次渲染包围盒。</summary>
        public static float MeasureRadius(Transform planetCenter)
        {
            if (planetCenter == null) return 0f;

            // 只认星球本体：WorldRoot 下面还挂着垃圾（有机垃圾是球体+SphereCollider），
            // 直接 GetComponentInChildren 会量到某个垃圾上，必须按名字锁定本体。
            Transform surface = FindSurface(planetCenter);
            if (surface != null)
            {
                SphereCollider sc = surface.GetComponent<SphereCollider>();
                if (sc != null)
                {
                    Vector3 scale = sc.transform.lossyScale;
                    float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                    float r = sc.radius * maxScale;
                    if (r > 0.01f) return r;
                }
                Renderer sr = surface.GetComponent<Renderer>();
                if (sr != null)
                {
                    Bounds b = sr.bounds;
                    return Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z));
                }
            }

            // 兜底：整个层级里最大的球体碰撞体（球心在根节点附近时才算数）
            SphereCollider[] spheres = planetCenter.GetComponentsInChildren<SphereCollider>();
            float best = 0f;
            for (int i = 0; i < spheres.Length; i++)
            {
                Vector3 scale = spheres[i].transform.lossyScale;
                float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                best = Mathf.Max(best, spheres[i].radius * maxScale);
            }
            return best;
        }

        /// <summary>按目标半径重设星球与地表道具。返回 true 表示确实做了改动。</summary>
        public static bool Apply(Transform planetCenter, float targetRadius)
        {
            if (planetCenter == null || targetRadius <= 0.1f) return false;

            float oldRadius = MeasureRadius(planetCenter);
            if (oldRadius <= 0.01f) return false;                       // 量不到就不猜
            if (Mathf.Abs(oldRadius - targetRadius) < 0.01f) return false; // 已经一致

            Transform surface = FindSurface(planetCenter);
            if (surface != null)
            {
                // Unity 内置球体网格半径 = 0.5 → 缩放 2R 得到半径 R 的球（与 WhiteboxBuilder 一致）
                surface.localScale = Vector3.one * (targetRadius * 2f);
            }

            Vector3 center = planetCenter.position;
            float ratio = targetRadius / oldRadius;
            for (int i = 0; i < planetCenter.childCount; i++)
            {
                Transform child = planetCenter.GetChild(i);
                if (child == surface || !IsSurfaceProp(child.name)) continue;

                Vector3 offset = child.position - center;
                float dist = offset.magnitude;
                if (dist < 0.01f) continue;                              // 灯之类的原点物体跳过

                float height = Mathf.Max(0f, dist - oldRadius);           // 原离地高度
                child.position = center + offset / dist * (targetRadius + height);

                if (IsScaleWithRadius(child.name))
                    child.localScale = child.localScale * ratio;
            }

            return true;
        }

        static Transform FindSurface(Transform planetCenter)
        {
            Transform named = planetCenter.Find("env_planet_surface");
            if (named != null) return named;

            SphereCollider sphere = planetCenter.GetComponentInChildren<SphereCollider>();
            return sphere != null ? sphere.transform : null;
        }

        static bool IsSurfaceProp(string name)
        {
            for (int i = 0; i < SurfacePropPrefixes.Length; i++)
                if (name.StartsWith(SurfacePropPrefixes[i], System.StringComparison.Ordinal)) return true;
            return false;
        }

        static bool IsScaleWithRadius(string name)
        {
            for (int i = 0; i < ScaleWithRadiusPrefixes.Length; i++)
                if (name.StartsWith(ScaleWithRadiusPrefixes[i], System.StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
