// @trace MECH-011, MECH-016, ART-001
using System.Collections.Generic;
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 垃圾刷新 + 对象池（策划案 §12：上限全部走池，避免运行时反复 Instantiate）。
    /// 三档刷新间隔 15/12/10 s 为冻结值；白膜另给一个「演示加速倍率」，仅用于缩短验证时间。
    ///
    /// 堆积表现（本次新增，只改生成侧，不动任何结算规则）：
    /// 每次刷新按上限剩余空间生成一批，批次在若干固定堆心之间轮转；批内按黄金角散布，
    /// 高度 = 基础高度 + 堆积度 × 最大堆高 × 锥形系数，中心高边缘低。
    /// 于是地表现存数量越多，后落的批次越往上叠、越往中心收拢，逐步形成垃圾山轮廓。
    /// 单件从地表下沿法线冒出：只改 transform.position，绝不改 scale / collider，
    /// 因此拾取、拆解、射线判定、类别概率等玩法规则保持原样。
    /// </summary>
    public sealed class TrashSpawner : MonoBehaviour
    {
        [SerializeField] TrashItem[] prefabs; // 0..3 普通四类，4..6 大型三种
        [SerializeField] Transform trashRoot;

        struct PopAnim
        {
            public TrashItem Item;
            public Vector3 Target;
            public Vector3 Up;
            public float Delay;
            public float Age;
        }

        readonly List<TrashItem> active = new List<TrashItem>();
        readonly Dictionary<TrashItem, int> owner = new Dictionary<TrashItem, int>();
        readonly List<PopAnim> pops = new List<PopAnim>();
        Stack<TrashItem>[] pools;
        Vector3[] pileCenters;
        WhiteboxGame game;
        float timer;
        int batchIndex;

        public TrashItem[] Prefabs { get => prefabs; set => prefabs = value; }
        public Transform TrashRoot { get => trashRoot; set => trashRoot = value; }
        public List<TrashItem> Active => active;

        public void Init(WhiteboxGame g)
        {
            game = g;
            if (prefabs == null) prefabs = new TrashItem[0];
            if (trashRoot == null) trashRoot = transform;
            pools = new Stack<TrashItem>[prefabs.Length];
            for (int i = 0; i < pools.Length; i++) pools[i] = new Stack<TrashItem>();
            active.Clear();
            owner.Clear();
            pops.Clear();
            pileCenters = BuildPileCenters(g.Config != null ? g.Config.PileCenterCount : 4);
            timer = 0f;
            batchIndex = 0;
            SpawnInitial();
        }

        /// <summary>黄金角确定性分布：每次开局堆位一致，便于复现验证。</summary>
        static Vector3[] BuildPileCenters(int count)
        {
            int n = Mathf.Max(1, count);
            var dirs = new Vector3[n];
            for (int i = 0; i < n; i++)
                dirs[i] = SphericalSurface.Direction((i + 0.5f) / n, i * 2.39996323f);
            return dirs;
        }

        void Update()
        {
            if (game == null || pools == null) return;
            // 冒出动画不受 UI 阻挡影响，避免打开/关闭面板后垃圾卡在半空
            UpdatePops();
            if (game.IsUiBlocking) return;
            WhiteboxConfig cfg = game.Config;
            if (cfg == null) return;

            timer += Time.deltaTime;
            int stage = game.Eco != null ? game.Eco.Stage : 0;
            float interval = cfg.SpawnInterval[Mathf.Clamp(stage, 0, 2)] * cfg.WhiteboxSpawnTimeScale;
            if (timer < interval) return;
            timer = 0f;
            if (active.Count >= cfg.MapTrashCap) return; // 满则暂停生成：上限仍严格遵守
            SpawnBatch(cfg);
        }

        void UpdatePops()
        {
            if (pops.Count == 0) return;
            WhiteboxConfig cfg = game.Config;
            float dur = cfg != null ? cfg.SpawnPopSeconds : 0.35f;
            float sink = cfg != null ? cfg.SpawnSinkDepth : 0.4f;
            float dt = Time.deltaTime;
            for (int i = pops.Count - 1; i >= 0; i--)
            {
                PopAnim p = pops[i];
                if (p.Item == null || !p.Item.gameObject.activeSelf) { pops.RemoveAt(i); continue; }
                p.Age += dt;
                float t = Mathf.Clamp01((p.Age - p.Delay) / dur);
                float e = 1f - Mathf.Pow(1f - t, 3f); // easeOutCubic
                p.Item.transform.position = p.Target - p.Up * sink * (1f - e);
                if (t >= 1f) pops.RemoveAt(i);
                else pops[i] = p;
            }
        }

        void SpawnInitial()
        {
            WhiteboxConfig cfg = game.Config;
            int n = cfg.InitialTrashCount;
            for (int i = 0; i < n; i++)
            {
                Vector3 dir = SphericalSurface.Direction((i + 0.5f) / Mathf.Max(1, n), i * 2.39996323f);
                int r = i % 45;
                int cat = r < 15 ? 0 : (r < 27 ? 1 : (r < 39 ? 2 : 3)); // 初始配比 15:12:12:6
                int index = (i % 6 == 5) ? 4 + (cat % 3) : cat;
                Spawn(index, dir);
            }
            Debug.Log("[WhiteboxSpawner] 初始垃圾 " + active.Count + " 件，地表上限 " + cfg.MapTrashCap
                      + "，每批 " + cfg.SpawnBatchSize + " 件，堆数 " + cfg.PileCenterCount);
        }

        void SpawnBatch(WhiteboxConfig cfg)
        {
            int room = cfg.MapTrashCap - active.Count;
            int n = Mathf.Min(cfg.SpawnBatchSize, room);
            if (n <= 0) return;

            // 堆积度只看地表现存数量：数量增长 → 新批次越堆越高、越收拢 → 山轮廓逐步成形
            float pile = Mathf.Clamp01(active.Count / (float)cfg.MapTrashCap);
            float radius = cfg.PileRadius * (1f - 0.45f * pile);
            float lift = cfg.PileMaxHeight * pile;

            Vector3 center = PickPileCenter(cfg);
            Vector3 up = center.sqrMagnitude < 0.000001f ? Vector3.up : center.normalized;
            Vector3 tangentA = Vector3.ProjectOnPlane(Vector3.right, up);
            if (tangentA.sqrMagnitude < 0.0001f) tangentA = Vector3.ProjectOnPlane(Vector3.forward, up);
            tangentA.Normalize();
            Vector3 tangentB = Vector3.Cross(up, tangentA).normalized;

            int spawned = 0;
            for (int j = 0; j < n; j++)
            {
                float r = radius * Mathf.Sqrt((j + 0.5f) / n);
                float a = j * 2.39996323f + batchIndex * 0.7f;
                Vector3 tangent = tangentA * Mathf.Cos(a) + tangentB * Mathf.Sin(a);
                Vector3 dir = (up * cfg.PlanetRadius + tangent * r).normalized;
                if (game.Player != null && Vector3.Distance(
                        SphericalSurface.Point(Vector3.zero, dir, cfg.PlanetRadius, 0f),
                        game.Player.position) < 6f) continue; // 沿用原「不刷在玩家 6 m 内」
                float cone = 1f - r / Mathf.Max(0.01f, radius); // 中心高、边缘低
                if (SpawnPiled(PickRandomCategory(), dir, lift * cone, j * cfg.SpawnPopStagger) != null)
                    spawned++;
            }
            batchIndex++;
            Debug.Log("[WhiteboxSpawner] 第 " + batchIndex + " 批 +" + spawned + " 件，地表 "
                      + active.Count + "/" + cfg.MapTrashCap + "，堆积度 " + pile.ToString("0.00"));
        }

        /// <summary>轮转堆心；避开玩家 6 m（沿用原刷新保护），全部太近时退化为随机方向。</summary>
        Vector3 PickPileCenter(WhiteboxConfig cfg)
        {
            int n = pileCenters != null ? pileCenters.Length : 0;
            for (int i = 0; i < n; i++)
            {
                Vector3 c = pileCenters[(batchIndex + i) % n];
                Vector3 p = SphericalSurface.Point(Vector3.zero, c, cfg.PlanetRadius, 0f);
                if (game.Player == null || Vector3.Distance(p, game.Player.position) >= 6f) return c;
            }
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Vector3 dir = new Vector3(Random.value * 2f - 1f, Random.value * 2f - 1f, Random.value * 2f - 1f);
                if (dir.sqrMagnitude < 0.001f) continue;
                dir.Normalize();
                Vector3 p = SphericalSurface.Point(Vector3.zero, dir, cfg.PlanetRadius, 0f);
                if (game.Player == null || Vector3.Distance(p, game.Player.position) >= 6f) return dir;
            }
            return n > 0 ? pileCenters[0] : Vector3.up;
        }

        /// <summary>类别概率完全沿用原 SpawnRandom 的表达式，不改玩法掉落分布。</summary>
        static int PickRandomCategory()
        {
            return Random.value < 0.333f ? 0 : (Random.value < 0.5f ? 1 : (Random.value < 0.5f ? 2 : 3));
        }

        /// <summary>兼容入口：贴地、无冒出动画，行为与改动前一致。</summary>
        public TrashItem Spawn(int prefabIndex, Vector3 direction)
        {
            return SpawnCore(prefabIndex, direction, 0f, -1f);
        }

        TrashItem SpawnPiled(int prefabIndex, Vector3 direction, float extraHeight, float delay)
        {
            return SpawnCore(prefabIndex, direction, extraHeight, delay);
        }

        TrashItem SpawnCore(int prefabIndex, Vector3 direction, float extraHeight, float delay)
        {
            if (pools == null || prefabs == null || prefabIndex < 0 || prefabIndex >= prefabs.Length) return null;
            if (prefabs[prefabIndex] == null) return null;

            TrashItem item;
            Stack<TrashItem> pool = pools[prefabIndex];
            if (pool.Count > 0)
            {
                item = pool.Pop();
                item.gameObject.SetActive(true);
            }
            else
            {
                item = Instantiate(prefabs[prefabIndex], trashRoot);
                item.name = prefabs[prefabIndex].name;
                owner[item] = prefabIndex;
            }

            item.ResetState();
            float height = (item.Large ? 0.28f : 0.18f) + Mathf.Max(0f, extraHeight);
            Vector3 hint = Vector3.ProjectOnPlane(Vector3.forward, direction);
            if (hint.sqrMagnitude < 0.0001f) hint = Vector3.right;
            SphericalSurface.Place(item.transform, Vector3.zero, direction,
                game.Config.PlanetRadius, height, hint);
            active.Add(item);

            if (delay >= 0f)
            {
                Vector3 up = direction.sqrMagnitude < 0.000001f ? Vector3.up : direction.normalized;
                pops.Add(new PopAnim
                {
                    Item = item,
                    Target = item.transform.position,
                    Up = up,
                    Delay = delay,
                    Age = 0f
                });
                // 先藏在地表下：只改位置，缩放与碰撞体保持原样，玩法判定不变
                item.transform.position -= up * game.Config.SpawnSinkDepth;
            }
            return item;
        }

        public void Despawn(TrashItem item)
        {
            if (item == null) return;
            active.Remove(item);
            for (int i = pops.Count - 1; i >= 0; i--)
                if (pops[i].Item == item) pops.RemoveAt(i);
            item.SetHighlight(false);
            item.gameObject.SetActive(false);
            int index;
            if (!owner.TryGetValue(item, out index)) index = 0;
            if (pools != null && index >= 0 && index < pools.Length) pools[index].Push(item);
        }

        /// <summary>狗用：在半径内找最近的、类型允许的、未被玩家准星锁定的普通垃圾。</summary>
        public TrashItem FindNearest(Vector3 origin, float radius, int allowedMask, TrashItem exclude)
        {
            TrashItem best = null;
            float bestD = radius * radius;
            for (int i = 0; i < active.Count; i++)
            {
                TrashItem t = active[i];
                if (t == null || !t.gameObject.activeSelf || t.Large || t == exclude) continue;
                if (((1 << (int)t.Category) & allowedMask) == 0) continue;
                float d = (t.transform.position - origin).sqrMagnitude;
                if (d > bestD) continue;
                if (Mathf.Approximately(d, bestD) && best != null && t.GetInstanceID() > best.GetInstanceID()) continue;
                best = t;
                bestD = d;
            }
            return best;
        }
    }
}
