// @trace MECH-002, MECH-011, ART-002, LEV-001, NUM-004
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 星球净化机制（以中转站为中心的已净化土地）。
    ///
    /// 进度源与 EcoStage 完全一致：progress = (landProgress + waterProgress) / 2，
    /// 玩家制造生态肥料包 / 水质过滤器时进度上升，净化区随之向外扩展。
    ///
    /// 范围口径：沿球面的弧长（玩家"走过去"的距离），球心固定在 PlanetCenter。
    /// - 初始半径 = cfg.PurifyBaseRadius（默认 5 m），即使 progress=0 也净化这一圈；
    /// - 半径随进度线性扩到 π·R（到对跖点 = 整颗球都净化）；
    /// - 判定用 cos(dot) 比较，避免每次撒点都 acos。
    ///
    /// 职责边界：
    /// - 只读 State 的进度，不写任何 land/water/gacha/任务字段；
    /// - 通过 game.Purification 被 TrashSpawner（撒点排除 + 已存在垃圾回收）与
    ///   QuestChain（最终步完成条件）读取，不改这两个系统的既有数值；
    /// - 边界标记（fx_purify_boundary）是纯视觉、运行时生成、无碰撞体、无 TrashItem。
    /// </summary>
    public sealed class PurificationSystem : MonoBehaviour
    {
        const int RingSegments = 36;
        const float RingHeight = 0.14f;
        const float MarkerScale = 0.13f;

        [SerializeField, Tooltip("净化中心（中转站）.transform；为空时退回 game.Station。可多个，以后加中转站自动纳入")]
        Transform[] centers = new Transform[0];

        WhiteboxGame game;
        float lastProgress = -1f;
        bool fullyNotified;
        Transform[] ringRoots;   // 与 centers 对齐；每个中心一圈边界标记
        Material ringMaterial;

        public float Progress
        {
            get
            {
                if (game == null || game.State == null) return 0f;
                return Mathf.Clamp01((game.State.landProgress + game.State.waterProgress) * 0.5f);
            }
        }

        /// <summary>整颗球被净化所需的最大球面弧长 = π·R（球面上一点到对跖点的距离）。</summary>
        public float FullArc
        {
            get
            {
                float r = game != null && game.Config != null ? game.Config.PlanetRadius : 8f;
                return Mathf.PI * Mathf.Max(0.01f, r);
            }
        }

        /// <summary>当前净化半径（球面弧长，米）：初始 base，随进度线性扩到 π·R。</summary>
        public float Radius
        {
            get
            {
                float baseR = game != null && game.Config != null ? game.Config.PurifyBaseRadius : 5f;
                baseR = Mathf.Clamp(baseR, 0f, FullArc);
                return Mathf.Lerp(baseR, FullArc, Progress);
            }
        }

        /// <summary>全星球是否已被净化土地覆盖（半径到达对跖点）。</summary>
        public bool FullyCovered => Radius >= FullArc - 0.001f;

        /// <summary>净化覆盖率（0..1），用最大球冠面积近似：cap 面积占比 = (1 - cosθ)/2。</summary>
        public float Coverage01
        {
            get
            {
                float r = game != null && game.Config != null ? game.Config.PlanetRadius : 8f;
                if (r <= 0.01f) return 0f;
                float theta = Mathf.Clamp(Radius / Mathf.Max(0.01f, r), 0f, Mathf.PI);
                return Mathf.Clamp01((1f - Mathf.Cos(theta)) * 0.5f);
            }
        }

        public int CenterCount => centers != null ? centers.Length : 0;

        public void Wire(Transform[] all)
        {
            centers = all != null ? all : new Transform[0];
        }

        public void Init(WhiteboxGame g)
        {
            game = g;
            fullyNotified = false;
            if (centers == null || centers.Length == 0)
            {
                // 旧场景未由 Builder 注入时退回唯一的中转站
                Transform st = g != null ? g.Station : null;
                centers = st != null ? new[] { st } : new Transform[0];
            }
            BuildRing();
            lastProgress = Progress;
            UpdateRing();
            CullPurifiedTrash();
            if (FullyCovered) fullyNotified = true; // 读档已全净化：不重复弹通知
        }

        /// <summary>进度或中转站位置变化后调用：回收已净化区内的旧垃圾、刷新边界环。</summary>
        public void Refresh()
        {
            if (game == null) return;
            float p = Progress;
            if (Mathf.Abs(p - lastProgress) < 0.0005f) return;
            lastProgress = p;
            CullPurifiedTrash();
            UpdateRing();
            if (FullyCovered && !fullyNotified)
            {
                fullyNotified = true;
                if (game.Hud != null) game.Notice("全星球已被净化土地覆盖！地表不再生成垃圾");
            }
        }

        void Update()
        {
            // 进度由 State 驱动，制造回调 / 读档 / 其它系统都可能改它。
            // 这里轻量轮询（只比一个 float），确保任何写入路径下一帧都会扩张净化区；
            // Refresh 内部以 lastProgress 幂等，与 OnWorldEvent 的即时调用重复也无害。
            if (game != null && Mathf.Abs(Progress - lastProgress) >= 0.0005f) Refresh();
        }

        /// <summary>世界坐标点是否已进入净化区（含所有中心）。用 cos 比较，避免 acos。</summary>
        public bool IsPurified(Vector3 worldPoint)
        {
            if (game == null || game.Config == null) return false;
            float rad = Radius;
            if (rad <= 0f) return false;
            if (rad >= FullArc - 0.001f) return true; // 全覆盖：整颗球任意点都算已净化

            Vector3 center = game.PlanetCenter != null ? game.PlanetCenter.position : Vector3.zero;
            float sphereR = game.Config.PlanetRadius;
            Vector3 d = worldPoint - center;
            if (d.sqrMagnitude < 0.000001f) return true;
            d.Normalize();

            float theta = Mathf.Clamp(rad / Mathf.Max(0.01f, sphereR), 0f, Mathf.PI);
            float cosMax = Mathf.Cos(theta);
            if (centers == null) return false;
            for (int i = 0; i < centers.Length; i++)
            {
                Transform c = centers[i];
                if (c == null) continue;
                Vector3 cd = c.position - center;
                if (cd.sqrMagnitude < 0.000001f) continue;
                if (Vector3.Dot(cd.normalized, d) >= cosMax) return true;
            }
            return false;
        }

        /// <summary>净化区扩张时，把落在新净化区内的现存垃圾回收进池（无垃圾区域真正增大）。</summary>
        void CullPurifiedTrash()
        {
            TrashSpawner sp = game != null ? game.Spawner : null;
            if (sp == null) return;
            var list = sp.Active;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                TrashItem t = list[i];
                if (t == null || !t.gameObject.activeSelf) continue;
                if (IsPurified(t.transform.position)) sp.Despawn(t);
            }
        }

        // ------------------------------------------------------------------
        // 边界可视化：每个中心一圈标记，随半径沿球面张开（运行时生成，纯装饰）
        // ------------------------------------------------------------------

        void BuildRing()
        {
            ringRoots = null;
            if (game == null || game.Config == null || !game.Config.PurifyShowBoundary) return;
            if (centers == null || centers.Length == 0) return;

            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null) return;
            ringMaterial = new Material(sh) { name = "M_PurifyBoundary_Runtime" };
            Color c = game.Config.HighlightColor;
            c.a = 1f;
            if (ringMaterial.HasProperty("_BaseColor")) ringMaterial.SetColor("_BaseColor", c);
            if (ringMaterial.HasProperty("_Color")) ringMaterial.SetColor("_Color", c);

            ringRoots = new Transform[centers.Length];
            for (int k = 0; k < centers.Length; k++)
            {
                GameObject root = new GameObject("fx_purify_boundary_" + (k + 1));
                root.transform.SetParent(transform, false);
                ringRoots[k] = root.transform;
                for (int i = 0; i < RingSegments; i++)
                {
                    GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    g.name = "mark_" + i;
                    g.transform.SetParent(root.transform, false);
                    g.transform.localScale = Vector3.one * MarkerScale;
                    Collider col = g.GetComponent<Collider>();
                    if (col != null) Object.Destroy(col);
                    Renderer r = g.GetComponent<Renderer>();
                    if (r != null) r.sharedMaterial = ringMaterial;
                }
            }
        }

        void UpdateRing()
        {
            if (ringRoots == null || centers == null) return;
            Vector3 center = game.PlanetCenter != null ? game.PlanetCenter.position : Vector3.zero;
            float sphereR = game.Config.PlanetRadius;
            float theta = Mathf.Clamp(Radius / Mathf.Max(0.01f, sphereR), 0f, Mathf.PI);
            float cosT = Mathf.Cos(theta), sinT = Mathf.Sin(theta);
            bool full = FullyCovered;

            for (int k = 0; k < ringRoots.Length && k < centers.Length; k++)
            {
                Transform root = ringRoots[k];
                Transform ct = centers[k];
                if (root == null) continue;
                root.gameObject.SetActive(!full && ct != null); // 全覆盖后边界无意义，隐藏
                if (full || ct == null) continue;

                Vector3 axis = ct.position - center;
                if (axis.sqrMagnitude < 0.000001f) continue;
                axis.Normalize();
                Vector3 u = Vector3.ProjectOnPlane(Vector3.up, axis);
                if (u.sqrMagnitude < 0.0001f) u = Vector3.ProjectOnPlane(Vector3.forward, axis);
                u.Normalize();
                Vector3 v = Vector3.Cross(axis, u).normalized;
                float shell = sphereR + RingHeight;

                for (int i = 0; i < RingSegments; i++)
                {
                    float phi = i * Mathf.PI * 2f / RingSegments;
                    Vector3 dir = axis * cosT + (u * Mathf.Cos(phi) + v * Mathf.Sin(phi)) * sinT;
                    Transform mk = root.GetChild(i);
                    mk.position = center + dir.normalized * shell;
                }
            }
        }

        void OnDestroy()
        {
            if (ringMaterial != null) Object.Destroy(ringMaterial);
        }
    }
}
