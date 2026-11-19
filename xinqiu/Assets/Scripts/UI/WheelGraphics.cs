// 新增玩法：圆形平面转盘（参考图改版）——纯 uGUI 网格绘制，无纹理依赖
using UnityEngine;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 扇形 / 扇环绘制。角度约定：0° = 正上（12 点），顺时针为正，与转盘扇区编号一致。
    /// 颜色只写进顶点色，Graphic.color 保持白色：UI 最终色 = 顶点色 × CanvasRenderer.color，
    /// 若两者都填同一个颜色会变成"颜色自乘"而明显变暗（旧版深色底板几乎全黑即此因）。
    /// innerRadius = 0 时即实心扇形；sweep = 360 时即整圆（灯泡白点、中心圆钮复用）。
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(CanvasRenderer))] // 自定义 Graphic 不会像 Image/Text 那样自带渲染器，必须显式声明依赖
    public sealed class WheelSectorGraphic : MaskableGraphic
    {
        [SerializeField] float startAngle;
        [SerializeField] float sweepAngle = 30f;
        [SerializeField] float outerRadius = 200f;
        [SerializeField] float innerRadius;
        [SerializeField] float outlineWidth = 2f;
        [SerializeField] Color outlineColor = Color.white;
        [SerializeField] Color fillColor = Color.white;

        protected override void Awake()
        {
            // 旧场景由生成器 AddComponent 时未带 CanvasRenderer；必须在同对象 Button.Awake
            // 读取 Graphic.color 之前补齐，否则 Play 抛 MissingComponentException。
            if (GetComponent<CanvasRenderer>() == null) gameObject.AddComponent<CanvasRenderer>();
            base.Awake();
        }

        /// <summary>供 Editor 生成器配置；所有几何参数都写入场景序列化数据。</summary>
        public void Configure(float start, float sweep, float outer, float inner,
            float outline, Color edge, Color fill)
        {
            startAngle = start;
            sweepAngle = Mathf.Max(0.1f, sweep);
            outerRadius = Mathf.Max(1f, outer);
            innerRadius = Mathf.Clamp(inner, 0f, outerRadius - 1f);
            outlineWidth = Mathf.Max(0f, outline);
            outlineColor = edge;
            fillColor = fill;
            color = Color.white; // 乘数保持白：最终色 = 顶点色，避免颜色自乘变暗
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (outlineWidth > 0f)
                AddRing(vh, startAngle, sweepAngle,
                    Mathf.Max(0f, innerRadius - outlineWidth), outerRadius + outlineWidth, outlineColor);
            AddRing(vh, startAngle, sweepAngle, innerRadius, outerRadius, fillColor);
        }

        void AddRing(VertexHelper vh, float start, float sweep, float inner, float outer, Color c)
        {
            int seg = Mathf.Max(2, Mathf.CeilToInt(sweep / 6f)); // 每 6° 一段，弧线足够平滑
            for (int i = 0; i < seg; i++)
            {
                float a0 = (start + sweep * i / seg) * Mathf.Deg2Rad;
                float a1 = (start + sweep * (i + 1) / seg) * Mathf.Deg2Rad;
                Vector2 d0 = new Vector2(Mathf.Sin(a0), Mathf.Cos(a0));
                Vector2 d1 = new Vector2(Mathf.Sin(a1), Mathf.Cos(a1));
                int v = vh.currentVertCount;
                vh.AddVert(d0 * inner, c, Vector2.zero);
                vh.AddVert(d1 * inner, c, Vector2.zero);
                vh.AddVert(d0 * outer, c, Vector2.zero);
                vh.AddVert(d1 * outer, c, Vector2.zero);
                vh.AddTriangle(v, v + 1, v + 2);
                vh.AddTriangle(v + 2, v + 1, v + 3);
            }
        }
    }

    /// <summary>
    /// 中央指针：从盘心指向正上的箭头，pivot 在底部中心（盘心），绕盘心旋转。
    /// 三层结构：亮色描边 → 深色主体 → 半透明中轴高光，保证在任何扇区颜色上都清晰可辨。
    /// 颜色同样只写顶点色，Graphic.color 保持白，避免颜色自乘变暗。
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class WheelNeedleGraphic : MaskableGraphic
    {
        [SerializeField] float outlineWidth = 4f;
        [SerializeField] Color outlineColor = new Color(1f, 0.88f, 0.51f, 1f);
        [SerializeField] Color fillColor = new Color(0.15f, 0.10f, 0.08f, 1f);

        protected override void Awake()
        {
            if (GetComponent<CanvasRenderer>() == null) gameObject.AddComponent<CanvasRenderer>();
            base.Awake();
        }

        public void Configure(float outline, Color edge, Color fill)
        {
            outlineWidth = Mathf.Max(0f, outline);
            outlineColor = edge;
            fillColor = fill;
            color = Color.white; // 乘数保持白：最终色 = 顶点色
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            // pivot 在底部中心（0.5, 0），rect.yMin = 0 即盘心，三角形从盘心(yMin)向外(yMax)延伸。
            Rect r = rectTransform.rect;
            float midX = (r.xMin + r.xMax) * 0.5f;
            float w = Mathf.Min(outlineWidth, r.width * 0.30f);

            // 外描边（亮色）：顶点 → 左下 → 右下，法线朝 +Z（UI 正面）
            AddTri(vh, new Vector2(midX, r.yMax), new Vector2(r.xMin, r.yMin),
                new Vector2(r.xMax, r.yMin), outlineColor);
            // 主体（深色）
            AddTri(vh, new Vector2(midX, r.yMax - w * 2.2f),
                new Vector2(r.xMin + w, r.yMin + w), new Vector2(r.xMax - w, r.yMin + w), fillColor);
            // 中轴高光：细长三角，提升指向辨识度
            float hw = Mathf.Max(1.5f, r.width * 0.10f);
            AddTri(vh, new Vector2(midX, r.yMax - w * 3f),
                new Vector2(midX - hw, r.yMin + w * 2f),
                new Vector2(midX + hw, r.yMin + w * 2f),
                new Color(outlineColor.r, outlineColor.g, outlineColor.b, 0.55f));
        }

        static void AddTri(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color col)
        {
            int v = vh.currentVertCount;
            vh.AddVert(a, col, Vector2.zero);
            vh.AddVert(b, col, Vector2.zero);
            vh.AddVert(c, col, Vector2.zero);
            vh.AddTriangle(v, v + 1, v + 2);
        }
    }
}
