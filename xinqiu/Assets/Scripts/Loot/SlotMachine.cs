// 新增玩法：场景内老虎机抽奖交互（转盘改造，尚未纳入 design-freeze 编号）
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 场景内老虎机实体：玩家走近、准星对准、按 F 时打开抽奖面板。
    /// 只做「实体 + 交互入口 + 高亮 + 开关面板」，抽奖数值/奖品/保底全部复用 GachaSystem + GachaConfig。
    /// 不设独立常驻 UI：面板平时隐藏，只由本实体交互触发（关闭后由 GachaSystem 复位）。
    /// 实体与材质由 GachaBuilder 用 Editor API 生成；引用由 Builder 注入。
    /// </summary>
    public sealed class SlotMachine : MonoBehaviour
    {
        [SerializeField, Tooltip("可交互距离（米）；与拾取距离独立，避免捡垃圾顺手开老虎机")]
        float interactRange = 4f;

        [SerializeField, Tooltip("准星对准时高亮的屏幕渲染器（可选）")]
        Renderer highlightRenderer;

        [SerializeField, Tooltip("高亮色，默认青色；退出高亮时还原材质原色")]
        Color highlightColor = new Color(0.24f, 0.84f, 0.75f, 1f);

        [SerializeField] WhiteboxGame game;
        Color savedColor;
        bool hasSavedColor;
        bool highlighted;

        public float InteractRange => Mathf.Max(0.5f, interactRange);
        public bool IsHighlighted => highlighted;

        /// <summary>Builder 注入 WhiteboxGame（经它走面板互斥入口）。</summary>
        public void Wire(WhiteboxGame g) { game = g; }

        /// <summary>Builder 注入高亮渲染器（注入后重设一次，确保回到原色）。</summary>
        public void WireHighlight(Renderer r)
        {
            highlightRenderer = r;
            hasSavedColor = false;
            if (highlighted) { highlighted = false; SetHighlight(true); }
        }

        /// <summary>运行时兜底初始化：Builder 没注入 Game 时自行查找。</summary>
        public void Init(WhiteboxGame g)
        {
            game = g != null ? g : FindFirstObjectByType<WhiteboxGame>();
            SetHighlight(false);
        }

        /// <summary>
        /// InteractionController 在准星命中且距离达标时调用：走 WhiteboxGame 的互斥入口打开面板。
        /// 找不到 Game 时自行查找；仍找不到则安全退出（不越过仲裁层直开面板）。
        /// </summary>
        public void Interact()
        {
            if (game == null) game = FindFirstObjectByType<WhiteboxGame>();
            if (game == null)
            {
                Debug.LogWarning("[SlotMachine] 未找到 WhiteboxGame，请跑 Tools/垃圾星球白膜/⑨ 补齐接线");
                return;
            }
            game.OpenGachaFromMachine();
        }

        public void SetHighlight(bool on)
        {
            if (highlighted == on) return;
            highlighted = on;
            if (highlightRenderer == null) return;

            Material mat = highlightRenderer.sharedMaterial;
            if (mat == null) return;
            if (!hasSavedColor)
            {
                savedColor = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;
                hasSavedColor = true;
            }
            Color target = on ? highlightColor : savedColor;
            // URP Lit / Unlit 都写 _BaseColor；Standard 兼容 _Color。改的是资产材质，
            // 但只在这两个颜色属性间来回切换（on=高亮 / off=原色），不改其它物体共用的字段。
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", target);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", target);
        }

        void OnDisable()
        {
            if (highlighted) SetHighlight(false);
        }
    }
}
