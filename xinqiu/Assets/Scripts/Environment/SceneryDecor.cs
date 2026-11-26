// @trace ART-013, ART-002, ART-003
using UnityEngine;
using UnityEngine.Serialization;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 场景装饰类型：枯树 / 倒塌广告牌 / 污染水塘 / 垃圾山。
    /// 仅用于视觉表现，四类都不实现 TrashItem、不挂 Collider、不参与任何玩法结算。
    /// </summary>
    public enum SceneryDecorKind
    {
        DeadTree,
        FallenBillboard,
        PollutedPool,
        TrashMountain
    }

    /// <summary>
    /// 单个装饰物的「状态同步 + 视觉反馈」执行体（ART-013）。
    ///
    /// 设计红线：
    /// - 纯装饰：本类不含 Collider、不含 TrashItem、不读写 WhiteboxState、不改生态进度/资源/数值。
    /// - 只接收外部传入的 visualT(0..1) 做颜色 / 缩放 / 旋转插值，自身不判断阶段、不推进进度。
    /// - Builder 负责生成几何与部件引用，本类只负责按 visualT 呈现，二者职责分离。
    ///
    /// 美术模型（artModel）两种用法，按 kind 区分：
    /// - 枯树：模型整体替换白膜（树冠 / 枝干全部让位），模型本身静态不做插值。
    /// - 垃圾山：模型当「垃圾堆」用，替换白膜方块堆、纳入收缩部件——
    ///   随恢复缩小让位给长出来的绿丘，与原来的方块堆是同一条叙事。
    /// artModel 为空时一律回退白膜图元，所以缺模型不会坏。
    ///
    /// visualT = 0 → 荒废态（脏色 / 垃圾碎屑在 / 广告牌倒地 / 绿植缩起）
    /// visualT = 1 → 复绿态（净色 / 碎屑与烟消失 / 广告牌立起 / 树冠与绿丘长出）
    /// </summary>
    public sealed class SceneryDecor : MonoBehaviour
    {
        /// <summary>运行时装上的美术模型子物体名；Builder 在编辑器里预摆同名物体时本类直接接管，不重复实例化。</summary>
        public const string ArtVisualName = "Art Visual";

        [SerializeField] SceneryDecorKind kind;
        [Tooltip("美术模型（枯树=树 fbx，垃圾山=垃圾山 fbx）。留空则用白膜图元")]
        [FormerlySerializedAs("treeModel")]
        [SerializeField] GameObject artModel;
        [Tooltip("归一化目标：水平最大边（root 本地单位）")]
        [SerializeField] float artFitWidth = 3.5f;
        [Tooltip("归一化目标：高度上限（root 本地单位）")]
        [SerializeField] float artFitHeight = 2.5f;
        [Tooltip("随恢复由脏到净变色的部件（材质实例，运行时缓存）")]
        [SerializeField] Renderer[] tintParts;
        [SerializeField] Color[] dirtyColors;
        [SerializeField] Color[] cleanColors;
        [Tooltip("随恢复缩小消失的部件：垃圾碎屑 / 污染烟 / 广告牌残片")]
        [SerializeField] Transform[] shrinkParts;
        [Tooltip("随恢复长大出现的部件：树冠 / 绿丘 / 草丛 / 芦苇")]
        [SerializeField] Transform[] growParts;
        [Tooltip("随恢复扶正的部件：倒塌广告牌的牌面枢轴")]
        [SerializeField] Transform[] tiltParts;
        [SerializeField] Quaternion[] tiltFrom;
        [SerializeField] Quaternion[] tiltTo;

        Material[] tintInst;
        Vector3[] shrinkBase;
        Vector3[] growBase;
        float appliedT = -1f;
        GameObject artVisual;
        bool artAsJunkHeap;

        public SceneryDecorKind Kind => kind;
        public GameObject ArtModel => artModel;

        /// <summary>Builder 注入美术模型与归一化目标尺寸（root 本地单位）。</summary>
        public void SetArtModel(GameObject model, float fitWidth, float fitHeight)
        {
            artModel = model;
            artFitWidth = fitWidth;
            artFitHeight = fitHeight;
        }

        /// <summary>Builder 注入各部件引用（Editor API 写入，不改场景 YAML）。</summary>
        public void Setup(SceneryDecorKind k,
            Renderer[] tints, Color[] dirty, Color[] clean,
            Transform[] shrink, Transform[] grow,
            Transform[] tilt, Quaternion[] tiltFrom, Quaternion[] tiltTo)
        {
            kind = k;
            tintParts = tints ?? new Renderer[0];
            dirtyColors = dirty ?? new Color[0];
            cleanColors = clean ?? new Color[0];
            shrinkParts = shrink ?? new Transform[0];
            growParts = grow ?? new Transform[0];
            tiltParts = tilt ?? new Transform[0];
            this.tiltFrom = tiltFrom ?? new Quaternion[0];
            this.tiltTo = tiltTo ?? new Quaternion[0];
            // 立刻缓存满级缩放：即使 Init 尚未调用，ApplyVisual 也不会因数组为空而崩
            shrinkBase = CacheScale(shrinkParts);
            growBase = CacheScale(growParts);
        }

        /// <summary>缓存材质实例与各部件满级缩放；随后由系统推一次当前进度。</summary>
        public void Init()
        {
            if (artModel != null || transform.Find(ArtVisualName) != null)
                InstallArtVisual();

            if (artAsJunkHeap)
            {
                // 垃圾山走「模型当垃圾堆收缩」这条路，不需要脏→净变色
                tintInst = null;
            }
            else
            {
                int n = tintParts != null ? tintParts.Length : 0;
                tintInst = new Material[n];
                for (int i = 0; i < n; i++)
                    tintInst[i] = tintParts[i] != null ? tintParts[i].material : null; // .material 自动实例，不污染共享资产
            }

            shrinkBase = CacheScale(shrinkParts);
            growBase = CacheScale(growParts);
            appliedT = -1f; // 强制下一次 ApplyVisual 生效
        }

        void InstallArtVisual()
        {
            if (artVisual != null) return;
            bool asJunkHeap = kind == SceneryDecorKind.TrashMountain;

            // 编辑器里已经摆好的同名物体直接接管（不再二次归一化），否则运行时实例化一份
            Transform existing = transform.Find(ArtVisualName);
            if (existing != null)
            {
                artVisual = existing.gameObject;
            }
            else if (artModel != null)
            {
                artVisual = Instantiate(artModel, transform, false);
                artVisual.name = ArtVisualName;
                FitArt(artVisual, transform, artFitWidth, artFitHeight);
            }

            if (artVisual == null) return;
            foreach (Collider collider in artVisual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;

            if (asJunkHeap)
            {
                // 美术当垃圾堆：纳入收缩部件，随恢复缩小；白膜方块堆隐藏，但保留会长出来的绿丘 / 草丛
                var shrink = new Transform[(shrinkParts != null ? shrinkParts.Length : 0) + 1];
                if (shrinkParts != null) shrinkParts.CopyTo(shrink, 0);
                shrink[shrink.Length - 1] = artVisual.transform;
                shrinkParts = shrink;
                artAsJunkHeap = true;

                foreach (Transform child in transform)
                {
                    if (child == artVisual.transform) continue;
                    if (Contains(growParts, child)) continue;
                    child.gameObject.SetActive(false);
                }
            }
            else
            {
                foreach (Transform child in transform)
                    if (child != artVisual.transform) child.gameObject.SetActive(false);
            }
        }

        static bool Contains(Transform[] arr, Transform t)
        {
            if (arr == null) return false;
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] == t) return true;
            return false;
        }

        /// <summary>
        /// 把美术模型摆到 root 原点并归一化尺寸：底面对齐地面、水平居中，
        /// 同时受「高度上限」和「水平最大边」约束，取较小的缩放（瘦高模型不会戳穿绿丘，扁平模型不会溢出地块）。
        /// 尺寸单位是 root 本地单位——root 自身的缩放仍由 Builder 决定。
        /// </summary>
        public static void FitArt(GameObject visual, Transform root, float fitWidth, float fitHeight)
        {
            Transform t = visual.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;

            if (!TryLocalBounds(visual, root, out Bounds bounds)) return;

            float footprint = Mathf.Max(bounds.size.x, bounds.size.z);
            float sHeight = fitHeight > 0.001f && bounds.size.y > 0.001f
                ? fitHeight / bounds.size.y : float.PositiveInfinity;
            float sWidth = fitWidth > 0.001f && footprint > 0.001f
                ? fitWidth / footprint : float.PositiveInfinity;
            float scale = Mathf.Min(sHeight, sWidth);
            if (float.IsInfinity(scale) || scale <= 0f) return;

            t.localScale = Vector3.one * scale;
            t.localPosition = -new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * scale;
        }

        static bool TryLocalBounds(GameObject visual, Transform root, out Bounds bounds)
        {
            bounds = default;
            bool has = false;
            Matrix4x4 toRoot = root.worldToLocalMatrix;
            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                Bounds local = renderer.localBounds;
                Matrix4x4 toTree = toRoot * renderer.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 point = toTree.MultiplyPoint3x4(corner);
                    if (has) bounds.Encapsulate(point);
                    else { bounds = new Bounds(point, Vector3.zero); has = true; }
                }
            }
            return has;
        }

        static Vector3[] CacheScale(Transform[] arr)
        {
            if (arr == null) return new Vector3[0];
            var r = new Vector3[arr.Length];
            for (int i = 0; i < arr.Length; i++)
                r[i] = arr[i] != null ? arr[i].localScale : Vector3.one;
            return r;
        }

        /// <summary>按 visualT 呈现恢复效果。幂等：视觉值无变化时不重复写材质。</summary>
        public void ApplyVisual(float t)
        {
            t = Mathf.Clamp01(t);
            if (Mathf.Abs(t - appliedT) < 0.0005f) return;
            appliedT = t;
            // 静态美术（枯树）接管后就不再动弹；垃圾山的美术是收缩部件，要继续走下面的插值
            if (artVisual != null && !artAsJunkHeap) return;

            if (tintInst != null)
                for (int i = 0; i < tintInst.Length; i++)
                {
                    if (tintInst[i] == null) continue;
                    Color a = dirtyColors != null && i < dirtyColors.Length ? dirtyColors[i] : Color.gray;
                    Color b = cleanColors != null && i < cleanColors.Length ? cleanColors[i] : a;
                    SetColor(tintInst[i], Color.Lerp(a, b, t));
                }

            if (shrinkParts != null && shrinkBase != null)
                for (int i = 0; i < shrinkParts.Length; i++)
                    if (shrinkParts[i] != null && i < shrinkBase.Length)
                        shrinkParts[i].localScale = shrinkBase[i] * Mathf.Max(0.001f, 1f - t);

            if (growParts != null && growBase != null)
                for (int i = 0; i < growParts.Length; i++)
                    if (growParts[i] != null && i < growBase.Length)
                        growParts[i].localScale = growBase[i] * Mathf.Max(0.001f, t);

            if (tiltParts != null && tiltFrom != null && tiltTo != null)
                for (int i = 0; i < tiltParts.Length; i++)
                {
                    if (tiltParts[i] == null || i >= tiltFrom.Length || i >= tiltTo.Length) continue;
                    tiltParts[i].localRotation = Quaternion.Slerp(tiltFrom[i], tiltTo[i], t);
                }
        }

        static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            m.color = c;
        }
    }
}
