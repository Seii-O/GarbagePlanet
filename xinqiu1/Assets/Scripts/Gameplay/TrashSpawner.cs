// @trace MECH-011, MECH-016, ART-001, ART-006
using System.Collections.Generic;
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 垃圾刷新 + 对象池（策划案 §12：上限全部走池，避免运行时反复 Instantiate）。
    ///
    /// 本次改动：
    /// 1. 全局总量：累计生成 1500 件（含玩家已捡走的）后，地表不再出现任何新垃圾。
    ///    计数落在 WhiteboxState.totalSpawned（随存档持久化），并发上限 MapTrashCap 仍然生效。
    /// 2. 随机散落：不再按固定堆心成簇生成。每件垃圾在整颗球面上随机取点，
    ///    与已有垃圾保持最小间距、且不刷在玩家身边，于是垃圾分散在地表各处而不是攒成几座山。
    /// 3. 出现动画：从地表下沿法线升起 + 由小到大（带一点回弹），批内逐件错开，形成陆续冒出的观感。
    ///    动画只改 position / localScale，不改碰撞体与类别，玩法结算完全不受影响。
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
        readonly List<Vector3> occupied = new List<Vector3>(); // 撒点去重用的位置缓存（每批重建一次）
        Stack<TrashItem>[] pools;
        WhiteboxGame game;
        float timer;
        int batchIndex;
        bool budgetAnnounced;

        public TrashItem[] Prefabs { get => prefabs; set => prefabs = value; }
        public Transform TrashRoot { get => trashRoot; set => trashRoot = value; }
        public List<TrashItem> Active => active;

        /// <summary>累计生成件数（含已捡走）——HUD 展示「累计 x/1500」用。</summary>
        public int TotalSpawned => game != null && game.State != null ? game.State.totalSpawned : 0;

        /// <summary>全局总量是否已用尽：用尽后地表不再刷新新垃圾。</summary>
        public bool BudgetSpent =>
            game != null && game.Config != null && game.State != null && game.State.TrashBudgetSpent(game.Config);

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
            occupied.Clear();
            timer = 0f;
            batchIndex = 0;
            budgetAnnounced = false;
            SpawnInitial();
        }

        void Update()
        {
            if (game == null || pools == null) return;
            // 冒出动画不受 UI 阻挡影响，避免打开/关闭面板后垃圾卡在半空
            UpdatePops();
            if (game.IsUiBlocking) return;
            WhiteboxConfig cfg = game.Config;
            if (cfg == null) return;

            // 全局总量用尽：彻底停止刷新（并发上限之外的第二道上限）
            if (BudgetSpent)
            {
                if (!budgetAnnounced)
                {
                    budgetAnnounced = true;
                    game.Notice("地表垃圾已达全局总量 " + cfg.TotalTrashBudget + " 件，不再刷新");
                    Debug.Log("[WhiteboxSpawner] 全局总量已用尽：" + cfg.TotalTrashBudget + " 件，停止刷新");
                }
                return;
            }

            timer += Time.deltaTime;
            int stage = game.Eco != null ? game.Eco.Stage : 0;
            float interval = cfg.SpawnInterval[Mathf.Clamp(stage, 0, 2)] * cfg.WhiteboxSpawnTimeScale;
            if (timer < interval) return;
            timer = 0f;
            if (active.Count >= cfg.MapTrashCap) return; // 并发满则暂停生成，等玩家捡走再补
            SpawnBatch(cfg);
        }

        void UpdatePops()
        {
            if (pops.Count == 0) return;
            WhiteboxConfig cfg = game.Config;
            float dur = cfg != null ? cfg.SpawnPopSeconds : 0.45f;
            float sink = cfg != null ? cfg.SpawnSinkDepth : 0.55f;
            float from = cfg != null ? cfg.SpawnStartScale : 0.55f;
            float dt = Time.deltaTime;
            for (int i = pops.Count - 1; i >= 0; i--)
            {
                PopAnim p = pops[i];
                if (p.Item == null || !p.Item.gameObject.activeSelf) { pops.RemoveAt(i); continue; }
                p.Age += dt;
                float t = Mathf.Clamp01((p.Age - p.Delay) / dur);

                // 位置：easeOutCubic 从地表下升到落点
                float rise = 1f - Mathf.Pow(1f - t, 3f);
                p.Item.transform.position = p.Target - p.Up * sink * (1f - rise);

                // 缩放：easeOutBack 由小到大并轻微回弹，落地瞬间稳定为 1
                float s = Mathf.LerpUnclamped(from, 1f, EaseOutBack(t));
                p.Item.transform.localScale = Vector3.one * Mathf.Max(0.01f, s);

                if (t >= 1f)
                {
                    p.Item.transform.localScale = Vector3.one;
                    pops.RemoveAt(i);
                }
                else pops[i] = p;
            }
        }

        static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        void SpawnInitial()
        {
            WhiteboxConfig cfg = game.Config;
            if (cfg == null) return;
            int n = cfg.InitialTrashCount;
            BuildOccupied();
            for (int i = 0; i < n; i++)
            {
                int r = i % 45;
                int cat = r < 15 ? 0 : (r < 27 ? 1 : (r < 39 ? 2 : 3)); // 初始配比 15:12:12:6
                int index = (i % 6 == 5) ? 4 + (cat % 3) : cat;
                Vector3 dir;
                if (!TryScatter(cfg, out dir)) break;
                Spawn(index, dir);
                occupied.Add(SphericalSurface.Point(Vector3.zero, dir, cfg.PlanetRadius, 0f));
            }
            Debug.Log("[WhiteboxSpawner] 初始垃圾 " + active.Count + " 件，地表上限 " + cfg.MapTrashCap
                      + "，全局总量 " + cfg.TotalTrashBudget + "，每批 " + cfg.SpawnBatchSize + " 件（随机散落）");
        }

        void SpawnBatch(WhiteboxConfig cfg)
        {
            int budgetLeft = cfg.TotalTrashBudget - TotalSpawned;
            int room = Mathf.Min(cfg.MapTrashCap - active.Count, budgetLeft);
            int n = Mathf.Min(cfg.SpawnBatchSize, room);
            if (n <= 0) return;

            BuildOccupied();
            int spawned = 0;
            for (int j = 0; j < n; j++)
            {
                Vector3 dir;
                if (!TryScatter(cfg, out dir)) break;
                // 批内逐件错开 0~0.3 s：同一批不是整齐划一地弹出，而是陆续冒出
                float delay = j * cfg.SpawnPopStagger + Random.Range(0f, 0.12f);
                if (SpawnAnimated(PickRandomCategory(), dir, delay) == null) continue;
                occupied.Add(SphericalSurface.Point(Vector3.zero, dir, cfg.PlanetRadius, 0f));
                spawned++;
            }
            batchIndex++;
            Debug.Log("[WhiteboxSpawner] 第 " + batchIndex + " 批 +" + spawned + " 件，地表 "
                      + active.Count + "/" + cfg.MapTrashCap + "，累计 " + TotalSpawned + "/"
                      + cfg.TotalTrashBudget);
        }

        /// <summary>把地表现存垃圾的位置缓存起来，撒点去重时只看这份列表，避免每帧重复读 transform。</summary>
        void BuildOccupied()
        {
            occupied.Clear();
            for (int i = 0; i < active.Count; i++)
                if (active[i] != null && active[i].gameObject.activeSelf)
                    occupied.Add(active[i].transform.position);
        }

        /// <summary>
        /// 在整个球面上随机取一个落点：避开玩家（ScatterPlayerClearance）、中转站（固定 2.5 m 占地，
        /// 随星球半径缩小同步收窄）与已有垃圾（ScatterMinSpacing）。
        /// 尝试若干次仍冲突时退化为纯随机点，保证整批不会被整批丢弃。
        /// </summary>
        bool TryScatter(WhiteboxConfig cfg, out Vector3 dir)
        {
            float clearance = cfg.ScatterPlayerClearance;
            float minSq = cfg.ScatterMinSpacing * cfg.ScatterMinSpacing;
            bool hasPlayer = game.Player != null;
            Vector3 playerPos = hasPlayer ? game.Player.position : Vector3.zero;
            bool hasStation = game.Station != null;
            Vector3 stationPos = hasStation ? game.Station.position : Vector3.zero;
            const float StationClearance = 2.5f;

            for (int attempt = 0; attempt < 24; attempt++)
            {
                Vector3 d = Random.onUnitSphere;
                if (d.sqrMagnitude < 0.001f) continue;
                d.Normalize();
                Vector3 p = SphericalSurface.Point(Vector3.zero, d, cfg.PlanetRadius, 0f);
                if (hasPlayer && clearance > 0f &&
                    (p - playerPos).sqrMagnitude < clearance * clearance) continue;
                if (hasStation && (p - stationPos).sqrMagnitude < StationClearance * StationClearance) continue;
                if (minSq > 0f && OccupiedTooClose(p, minSq)) continue;
                dir = d;
                return true;
            }

            // 地表已经很密：这一件仍然随机落点，但放宽间距要求（宁可挨着一点，也不要少刷）
            dir = Random.onUnitSphere;
            if (dir.sqrMagnitude < 0.001f) dir = Vector3.up;
            dir.Normalize();
            return true;
        }

        bool OccupiedTooClose(Vector3 p, float minSq)
        {
            for (int i = 0; i < occupied.Count; i++)
                if ((occupied[i] - p).sqrMagnitude < minSq) return true;
            return false;
        }

        /// <summary>类别概率完全沿用原 SpawnRandom 的表达式，不改玩法掉落分布。</summary>
        static int PickRandomCategory()
        {
            return Random.value < 0.333f ? 0 : (Random.value < 0.5f ? 1 : (Random.value < 0.5f ? 2 : 3));
        }

        /// <summary>
        /// 刻意嵌入地表的深度（米）。两个作用：
        /// 1. 消除平底与凸球面之间的漏光缝；
        /// 2. 吃掉低模球面的弦高误差 —— 星球是 Unity 内置 Sphere（经纬分段有限），
        ///    三角面中心比理想球面低 R(1-cos(半段角))，R=8 时约 2~4 cm。
        ///    垃圾按解析球面贴地，在这些位置就会看起来悬空；嵌入 3 cm 后视觉上才真的"压"在地面上。
        /// </summary>
        const float GroundSink = 0.03f;

        // 包围盒 8 个角点的符号组合，用于把 localBounds 展开成顶点
        static readonly Vector3[] BoxCorners =
        {
            new Vector3(-1f, -1f, -1f), new Vector3(-1f, -1f, 1f),
            new Vector3(-1f, 1f, -1f), new Vector3(-1f, 1f, 1f),
            new Vector3(1f, -1f, -1f), new Vector3(1f, -1f, 1f),
            new Vector3(1f, 1f, -1f), new Vector3(1f, 1f, 1f)
        };

        /// <summary>
        /// 让物体真正压在地表上：实测当前姿态（含 yaw 与倾斜）下所有可见渲染器的最低点，
        /// 沿法线把整件物体抬到「最低点刚好触地」，再刻意嵌入 GroundSink。
        /// 以前用固定值（Large ? 0.28 : 0.18）当半高，形状一换就对不上：
        /// 轮胎半高 0.22、金属设备 0.225 会浮 5~6 cm，塑料瓶 0.21 反而陷进地面，
        /// 再叠 ±8° 倾斜就会把整件垃圾抬离地表 —— 用户看到的「都浮起来」就是这个。
        /// </summary>
        static void GroundFit(Transform t, Vector3 up)
        {
            float lowest = LowestPointOffset(t, up);
            t.position += up * (-lowest - GroundSink);
        }

        /// <summary>所有可见渲染器在该姿态下沿 up 的最低投影（相对 transform.position，向下为负）。</summary>
        static float LowestPointOffset(Transform t, Vector3 up)
        {
            float lowest = 0f;
            Vector3 origin = t.position;
            Renderer[] renderers = t.GetComponentsInChildren<Renderer>(false);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                // 描边副本默认不激活，不能被算进最低点（它比本体大 6.67%，会让本体浮起来）
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                Bounds b = r.localBounds;
                Transform rt = r.transform;
                for (int c = 0; c < BoxCorners.Length; c++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, BoxCorners[c]);
                    float h = Vector3.Dot(rt.TransformPoint(corner) - origin, up);
                    if (h < lowest) lowest = h;
                }
            }
            return lowest;
        }

        /// <summary>兼容入口：贴地、无冒出动画，地点仍按传入方向。</summary>
        public TrashItem Spawn(int prefabIndex, Vector3 direction)
        {
            return SpawnCore(prefabIndex, direction, -1f);
        }

        TrashItem SpawnAnimated(int prefabIndex, Vector3 direction, float delay)
        {
            return SpawnCore(prefabIndex, direction, Mathf.Max(0f, delay));
        }

        TrashItem SpawnCore(int prefabIndex, Vector3 direction, float delay)
        {
            if (pools == null || prefabs == null || prefabIndex < 0 || prefabIndex >= prefabs.Length) return null;
            if (prefabs[prefabIndex] == null) return null;
            WhiteboxConfig cfg = game.Config;
            if (cfg == null) return null;

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
            Vector3 up = direction.sqrMagnitude < 0.000001f ? Vector3.up : direction.normalized;

            // 随机朝向 + 轻微随机倾斜：让散落的垃圾看起来是各自躺在地上的，而不是整齐竖着
            Vector3 hint = Random.onUnitSphere;
            if (Mathf.Abs(Vector3.Dot(hint.normalized, up)) > 0.98f)
                hint = Vector3.Cross(up, Vector3.right);
            // 先按「pivot 正好压在地表」摆位，姿态摆完后再按实测最低点整体抬升 —— 见下方 GroundFit
            SphericalSurface.Place(item.transform, Vector3.zero, up, cfg.PlanetRadius, 0f, hint);
            item.transform.Rotate(up, Random.Range(0f, 360f), Space.World);
            float tilt = cfg.ScatterTiltDegrees;
            if (tilt > 0f)
            {
                Vector3 axis = Vector3.Cross(up, item.transform.forward);
                if (axis.sqrMagnitude > 0.0001f)
                    item.transform.rotation =
                        Quaternion.AngleAxis(Random.Range(-tilt, tilt), axis.normalized) * item.transform.rotation;
            }

            GroundFit(item.transform, up);
            active.Add(item);
            if (game.State != null) game.State.totalSpawned++;

            if (delay >= 0f)
            {
                pops.Add(new PopAnim
                {
                    Item = item,
                    Target = item.transform.position,
                    Up = up,
                    Delay = delay,
                    Age = 0f
                });
                // 先藏在地表下、缩到最小：只改位置与缩放，碰撞体与类别不动，玩法判定不变
                item.transform.position -= up * cfg.SpawnSinkDepth;
                item.transform.localScale = Vector3.one * cfg.SpawnStartScale;
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
            item.transform.localScale = Vector3.one;
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
