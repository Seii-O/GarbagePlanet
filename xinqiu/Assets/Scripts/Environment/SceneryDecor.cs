// @trace ART-013, ART-002, ART-003
using UnityEngine;

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
    /// visualT = 0 → 荒废态（脏色 / 垃圾碎屑在 / 广告牌倒地 / 绿植缩起）
    /// visualT = 1 → 复绿态（净色 / 碎屑与烟消失 / 广告牌立起 / 树冠与绿丘长出）
    /// </summary>
    public sealed class SceneryDecor : MonoBehaviour
    {
        [SerializeField] SceneryDecorKind kind;
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

        public SceneryDecorKind Kind => kind;

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
            int n = tintParts != null ? tintParts.Length : 0;
            tintInst = new Material[n];
            for (int i = 0; i < n; i++)
                tintInst[i] = tintParts[i] != null ? tintParts[i].material : null; // .material 自动实例，不污染共享资产
            shrinkBase = CacheScale(shrinkParts);
            growBase = CacheScale(growParts);
            appliedT = -1f; // 强制下一次 ApplyVisual 生效
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
