// @trace MECH-011, MECH-016, ART-001, ART-006
using System.Collections.Generic;
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 垃圾刷新 + 对象池（策划案 §12：上限全部走池，避免运行时反复 Instantiate）。
    ///
    /// 本次改动：
    /// 1. 按净化阶段维持地表数量，全净化后停止补充。
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
        int trashLayer = -1; // Trash 层：PlayerController 会忽略与该层的碰撞（玩家可直接穿过垃圾）
        // 「先小后大」：直接统计场上已有多少大件，补刷时按「缺口」决定这一件的大小。
        // 不按「抽签」决定，是因为补刷是 1:1 即时补位、每批只有 1 件，抽签会退化成纯随机；
        // 而且按缺口补能自动追平——升级后比例变高，新补的每一件都会先补成大件，直到比例对上。
        int largeOnSurface;
        float catchUpTimer;   // 静默补齐的节流计时（每秒结算一次配额）

        public TrashItem[] Prefabs { get => prefabs; set => prefabs = value; }
        public Transform TrashRoot { get => trashRoot; set => trashRoot = value; }
        public List<TrashItem> Active => active;

        public int SurfaceTarget => game != null && game.Config != null
            ? game.Config.SurfaceTrashTarget(game.Eco != null ? game.Eco.Stage : 0) : 0;
        public bool FinalCleanup => game != null && game.State != null && game.State.finalTrashTracked;
        public bool LastTrashOnSurface => FinalCleanup && active.Count == 1;
        public bool CleanupComplete => FinalCleanup && active.Count == 0 && game.State.finalTrashRemaining == 0;

        public void Init(WhiteboxGame g)
        {
            game = g;
            // 垃圾实例统一放到 Trash 层，让 PlayerController.Awake 里的
            // Physics.IgnoreLayerCollision(Player, Trash, true) 对垃圾生效：玩家不再被挡住。
            // 预制体本身是 Default(0) 层，所以必须在生成时改，不能直接靠预制体设置。
            trashLayer = LayerMask.NameToLayer("Trash");
            if (prefabs == null) prefabs = new TrashItem[0];
            if (trashRoot == null) trashRoot = transform;
            pools = new Stack<TrashItem>[prefabs.Length];
            for (int i = 0; i < pools.Length; i++) pools[i] = new Stack<TrashItem>();
            active.Clear();
            owner.Clear();
            pops.Clear();
            occupied.Clear();
            largeOnSurface = 0;
            catchUpTimer = 0f;
            if (game.State.finished) return;
            int count = FinalCleanup ? game.State.finalTrashRemaining : SurfaceTarget;
            FillSurface(count, true);
            SyncPopulation();

            // 一行诊断：大件比例到底读到了多少、铺场实际出了多少。
            // 「Lv.4 怎么没大件」这类问题看这一行就能定性（配置没读到 / 比例没生效 / 类别回落吃掉名额）。
            WhiteboxConfig logCfg = game.Config;
            if (logCfg != null)
            {
                int lv = game.State != null ? game.State.playerLevel : 1;
                Debug.Log("[WhiteboxTrash] 人物 Lv." + lv
                          + " · 大件目标占比 " + (logCfg.LargeSpawnRatioAt(lv) * 100f).ToString("0.#") + "%"
                          + " · 目标件数 " + LargeTarget(logCfg)
                          + " · 实际铺场大件 " + largeOnSurface + "/" + active.Count);
            }
        }

        void Update()
        {
            if (game == null || pools == null) return;
            UpdatePops();
            SyncPopulation();
            TickLargeCatchUp(Time.deltaTime);
        }

        public void SyncPopulation()
        {
            if (pools == null || game.State.finished || FinalCleanup) return;
            int target = SurfaceTarget;
            while (active.Count > target) Recycle(active[active.Count - 1]);

            if (game.Purification != null && game.Purification.FullyCovered)
            {
                // 最后一次净化后冻结地表存量，后续仅靠拾取减少。
                game.State.finalTrashTracked = true;
                game.State.finalTrashRemaining = active.Count;
                return;
            }
            if (active.Count < target) FillSurface(target, false);
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

        void FillSurface(int target, bool initial)
        {
            WhiteboxConfig cfg = game.Config;
            if (cfg == null) return;
            // 首次铺场按“使用次数最少的同类外观”随机挑选：既保留随机顺序，
            // 又保证苹果、枯枝等同类美术在数量足够时都会出现在场景里。
            // 后续补刷不传此表，仍为完全随机。
            Dictionary<int, int> initialPrefabUses = initial ? new Dictionary<int, int>() : null;
            BuildOccupied();
            while (active.Count < target)
            {
                int i = active.Count;
                int r = i % 45;
                int cat = r < 15 ? 0 : (r < 27 ? 1 : (r < 39 ? 2 : 3));
                TrashCategory category = initial ? (TrashCategory)cat : PickRandomCategory();
                int index = PickPrefabForCategory(category, RollLarge(cfg), initialPrefabUses);
                if (!TryScatter(cfg, out Vector3 direction)) break;
                TrashItem item = initial ? Spawn(index, direction) : SpawnAnimated(index, direction, 0f);
                if (item == null) break;
                occupied.Add(SphericalSurface.Point(Vector3.zero, direction, cfg.PlanetRadius, 0f));
            }
        }

        /// <summary>
        /// 决定「这一件」是大件还是小件：拿场上现有的量去和「按当前人物等级该有的大件数」比，
        /// 不足就补大件、够了就出小件。
        /// 好处一：比例天然收敛，不需要累加器也不会短期扎堆。
        /// 好处二：电子类没有大型外观，抽中名额会回落成小件 —— 用缺口判定时这件算「没补成」，
        ///         下一件会自动再补一次，实际比例不会像抽签那样被吃掉一截。
        /// 好处三：玩家升级后比例变高，地表存量不变、缺口立刻出现，新补的每一件都先补大件，
        ///         于是「慢慢变大件为主」是自动发生的，不需要额外写换血逻辑。
        /// </summary>
        bool RollLarge(WhiteboxConfig cfg)
        {
            int level = game.State != null ? game.State.playerLevel : 1;
            float ratio = cfg.LargeSpawnRatioAt(level);
            if (ratio <= 0.0001f) return false;

            int total = active.Count + 1;                        // 把这一件算进去之后的地表总量
            int want = Mathf.RoundToInt(total * ratio);          // 该有的大件数
            return largeOnSurface < want;
        }

        /// <summary>地表大件目标数（按当前人物等级与地表总量算），用于日志与调试。</summary>
        int LargeTarget(WhiteboxConfig cfg)
        {
            int level = game.State != null ? game.State.playerLevel : 1;
            return Mathf.RoundToInt(active.Count * cfg.LargeSpawnRatioAt(level));
        }

        /// <summary>
        /// 升级后静默补齐大件比例：不重排全图，也不等玩家一件件捡，
        /// 只把玩家**背半球**（角距 &gt; largeCatchUpMinAngle）的小件换成同类大件。
        /// 玩家站在半径 8 m 的球面上，地平线只有约 34°，60° 之外必然在地平线以下、看不见，
        /// 所以替换不会产生跳变；等玩家走过去时，那片区域已经是新比例了。
        /// 每秒最多换 largeCatchUpPerSecond 件，避免一次性 Instantiate 造成卡顿。
        /// </summary>
        void TickLargeCatchUp(float dt)
        {
            if (game == null || game.State == null || game.Player == null) return;
            if (game.State.finished || FinalCleanup) return;   // 结算/收尾阶段地表要冻结，不再改动
            WhiteboxConfig cfg = game.Config;
            if (cfg == null) return;

            int deficit = LargeTarget(cfg) - largeOnSurface;
            if (deficit <= 0) { catchUpTimer = 0f; return; }

            catchUpTimer -= dt;
            if (catchUpTimer > 0f) return;
            catchUpTimer = 1f;                                  // 每秒结一次配额

            ConvertFarSmall(deficit, Mathf.Max(1, Mathf.RoundToInt(cfg.LargeCatchUpPerSecond)), cfg);
        }

        /// <summary>把背半球的小件换成同类大件，最多换 budget 件。不动类别分布、不计入已清理数。</summary>
        void ConvertFarSmall(int deficit, int budget, WhiteboxConfig cfg)
        {
            Vector3 playerUp = game.Player.position.normalized;
            if (playerUp.sqrMagnitude < 0.0001f) return;   // 玩家坐标异常时不猜方向，宁可这一拍不换
            float minAngle = cfg.LargeCatchUpMinAngle;
            TrashItem aim = game.Interaction != null ? game.Interaction.CurrentTarget : null;

            int done = 0;
            // 倒序遍历：Recycle 会从 active 里摘掉元素，正序遍历会跳项
            for (int i = active.Count - 1; i >= 0 && done < budget && done < deficit; i--)
            {
                TrashItem t = active[i];
                if (t == null || t.Large || t == aim) continue;
                if (t.Category == TrashCategory.Electronic) continue;   // 电子类没有大型外观，换了只能回落成小件
                Vector3 dir = t.transform.position.normalized;
                if (Vector3.Angle(playerUp, dir) < minAngle) continue;  // 在可见半球，不动它

                int index = PickPrefabForCategory(t.Category, true, null);
                if (prefabs == null || index < 0 || index >= prefabs.Length) continue;
                if (prefabs[index] == null || !prefabs[index].Large) continue;

                // 先回收旧件再原地生成同类大件：active 数量不变，位置不变，只是换了体型。
                // 走 Recycle 而不是 Despawn，避免 clearedTrash 被算进去。
                if (!Recycle(t)) continue;
                if (Spawn(index, dir) != null) done++;
            }
        }

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

            // 净化区（MECH-011）：已净化土地不再生成垃圾。进度越高，被拒的球面区域越大，
            // 于是无垃圾区域随净化推进而增大；与玩家/中转站/间距判定叠加，谁先命中谁拒绝。
            PurificationSystem purify = game != null ? game.Purification : null;

            for (int attempt = 0; attempt < 24; attempt++)
            {
                Vector3 d = Random.onUnitSphere;
                if (d.sqrMagnitude < 0.001f) continue;
                d.Normalize();
                Vector3 p = SphericalSurface.Point(Vector3.zero, d, cfg.PlanetRadius, 0f);
                if (hasPlayer && clearance > 0f &&
                    (p - playerPos).sqrMagnitude < clearance * clearance) continue;
                if (hasStation && (p - stationPos).sqrMagnitude < StationClearance * StationClearance) continue;
                if (purify != null && purify.IsPurified(p)) continue;
                if (minSq > 0f && OccupiedTooClose(p, minSq)) continue;
                dir = d;
                return true;
            }

            // 地表已经很密 / 净化区很大：退化落点仍要避开净化区，否则会刷进"无垃圾区域"。
            // 扩到 48 次并放宽间距，优先找净化区外的点；全净化时仍补齐剩余配额。
            for (int attempt = 0; attempt < 48; attempt++)
            {
                Vector3 d = Random.onUnitSphere;
                if (d.sqrMagnitude < 0.001f) continue;
                d.Normalize();
                Vector3 p = SphericalSurface.Point(Vector3.zero, d, cfg.PlanetRadius, 0f);
                if (purify != null && purify.FullyCovered) { dir = d; return true; }
                if (purify != null && purify.IsPurified(p)) continue;
                if (hasPlayer && clearance > 0f &&
                    (p - playerPos).sqrMagnitude < clearance * clearance * 0.25f) continue;
                dir = d;
                return true;
            }

            // 极端兜底：随机点，宁可挨着一点也不要少刷
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
        static TrashCategory PickRandomCategory()
        {
            return (TrashCategory)(Random.value < 0.333f ? 0 :
                (Random.value < 0.5f ? 1 : (Random.value < 0.5f ? 2 : 3)));
        }

        /// <summary>
        /// 先按玩法类别抽取，再在同类别的全部美术预制体中随机。
        /// 这样 Garbage 目录新增的瓶子、苹果、轮胎等外观可以进入刷新池，
        /// 又不会因为预制体数组下标变化而被结算到错误类别。
        /// </summary>
        int PickPrefabForCategory(TrashCategory category, bool preferLarge,
            Dictionary<int, int> initialUseCounts = null)
        {
            if (prefabs == null || prefabs.Length == 0) return -1;
            var matches = new List<int>();
            for (int i = 0; i < prefabs.Length; i++)
            {
                TrashItem candidate = prefabs[i];
                if (candidate != null && candidate.Category == category && candidate.Large == preferLarge)
                    matches.Add(i);
            }

            // 某类别没有对应尺寸时退回同类别任意外观，保证旧场景/部分配置仍能正常刷新。
            if (matches.Count == 0)
            {
                for (int i = 0; i < prefabs.Length; i++)
                {
                    TrashItem candidate = prefabs[i];
                    if (candidate != null && candidate.Category == category) matches.Add(i);
                }
            }
            if (matches.Count == 0) return -1;
            if (initialUseCounts == null) return matches[Random.Range(0, matches.Count)];

            int minimumUses = int.MaxValue;
            for (int i = 0; i < matches.Count; i++)
            {
                int uses = initialUseCounts.TryGetValue(matches[i], out int value) ? value : 0;
                if (uses < minimumUses) minimumUses = uses;
            }

            // 在当前使用次数最少的候选中随机，避免固定的 ABAB 顺序。
            int selected = matches[0];
            int tied = 0;
            for (int i = 0; i < matches.Count; i++)
            {
                int uses = initialUseCounts.TryGetValue(matches[i], out int value) ? value : 0;
                if (uses != minimumUses) continue;
                tied++;
                if (Random.Range(0, tied) == 0) selected = matches[i];
            }
            initialUseCounts[selected] = minimumUses + 1;
            return selected;
        }

        /// <summary>
        /// 刻意嵌入地表的深度（米）。两个作用：
        /// 1. 消除平底与凸球面之间的漏光缝；
        /// 2. 吃掉低模球面的弦高误差 —— 星球是 Unity 内置 Sphere（经纬分段有限），
        ///    三角面中心比理想球面低 R(1-cos(半段角))，R=8 时约 2~4 cm。
        ///    垃圾按解析球面贴地，在这些位置就会看起来悬空；因此按物件外形使用独立嵌入深度。
        /// </summary>
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
        /// 沿法线把整件物体抬到「最低点刚好触地」，再按该物件的接地深度略微压入地面。
        /// 以前用固定值（Large ? 0.28 : 0.18）当半高，形状一换就对不上：
        /// 轮胎半高 0.22、金属设备 0.225 会浮 5~6 cm，塑料瓶 0.21 反而陷进地面，
        /// 再叠 ±8° 倾斜就会把整件垃圾抬离地表 —— 用户看到的「都浮起来」就是这个。
        /// </summary>
        static void GroundFit(Transform t, Vector3 up, float contactDepth)
        {
            float lowest = LowestPointOffset(t, up);
            t.position += up * (-lowest - Mathf.Clamp(contactDepth, 0.01f, 0.15f));
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
                // 描边副本默认不激活，不能被算进最低点（它沿法线外扩，比本体大，会让本体浮起来）
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

            // 整件垃圾（含所有子节点）设为 Trash 层：碰撞体在 Body 子节点上，
            // 只设根节点不够，必须递归，Player 对 Trash 的忽略才会覆盖到实际碰撞体。
            if (trashLayer >= 0) SetLayerRecursively(item.gameObject, trashLayer);

            item.ResetState();
            Vector3 up = direction.sqrMagnitude < 0.000001f ? Vector3.up : direction.normalized;

            // 随机朝向 + 轻微随机倾斜：让散落的垃圾看起来是各自躺在地上的，而不是整齐竖着
            Vector3 hint = Random.onUnitSphere;
            if (Mathf.Abs(Vector3.Dot(hint.normalized, up)) > 0.98f)
                hint = Vector3.Cross(up, Vector3.right);
            // 先按「pivot 正好压在地表」摆位，姿态摆完后再按实测最低点整体抬升 —— 见下方 GroundFit
            SphericalSurface.Place(item.transform, Vector3.zero, up, cfg.PlanetRadius, 0f, hint);
            item.transform.Rotate(up, Random.Range(0f, 360f), Space.World);
            // 大型家电约一半直立、一半横倒；旋转后仍由 GroundFit 按实际包围盒重新贴地。
            if (item.RandomlyLayOnSide && Random.value < 0.5f)
            {
                float side = Random.value < 0.5f ? -90f : 90f;
                item.transform.rotation = Quaternion.AngleAxis(side, item.transform.forward) * item.transform.rotation;
            }
            // 每种美术形状有自己的物理倾斜上限：纸箱等平底物体必须平放，
            // 瓶子、树枝和垃圾袋才允许轻微随机倾斜。GroundFit 会在最终姿态下再次贴地。
            float tilt = Mathf.Min(cfg.ScatterTiltDegrees, item.NaturalTiltDegrees);
            if (tilt > 0f)
            {
                Vector3 axis = Vector3.Cross(up, item.transform.forward);
                if (axis.sqrMagnitude > 0.0001f)
                    item.transform.rotation =
                        Quaternion.AngleAxis(Random.Range(-tilt, tilt), axis.normalized) * item.transform.rotation;
            }

            GroundFit(item.transform, up, item.GroundContactDepth);
            active.Add(item);
            if (item.Large) largeOnSurface++;

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

        /// <summary>递归设置整个层级到指定 Layer（碰撞体挂在各子节点，必须连子节点一起改）。</summary>
        static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            Transform t = go.transform;
            for (int i = 0; i < t.childCount; i++)
                SetLayerRecursively(t.GetChild(i).gameObject, layer);
        }

        public void Despawn(TrashItem item)
        {
            if (!Recycle(item)) return;
            game.State.clearedTrash++;
            if (FinalCleanup) game.State.finalTrashRemaining = active.Count;
        }

        bool Recycle(TrashItem item)
        {
            if (item == null || !active.Remove(item)) return false;
            if (item.Large) largeOnSurface = Mathf.Max(0, largeOnSurface - 1);
            for (int i = pops.Count - 1; i >= 0; i--)
                if (pops[i].Item == item) pops.RemoveAt(i);
            item.SetHighlight(false);
            item.transform.localScale = Vector3.one;
            item.gameObject.SetActive(false);
            int index;
            if (!owner.TryGetValue(item, out index)) index = 0;
            if (pools != null && index >= 0 && index < pools.Length) pools[index].Push(item);
            return true;
        }

        /// <summary>狗用：在半径内找最近的、类型允许的、未被玩家准星锁定的普通垃圾。</summary>
        public TrashItem FindNearest(Vector3 origin, float radius, int allowedMask, TrashItem exclude)
        {
            if (LastTrashOnSurface) return null;
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
