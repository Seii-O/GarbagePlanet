// @trace MECH-005, MECH-016, ART-004, ART-001
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 单件垃圾。高度受 ART-001 红线约束（全部低于视线水平线），垃圾本体颜色永不随阶段改变；
    /// 可交互时用独立的青色描边子物体表达，不改垃圾固有色（策划案 §9.1 禁忌 ②）。
    /// </summary>
    public sealed class TrashItem : MonoBehaviour
    {
        [SerializeField] TrashCategory category;
        [SerializeField] bool large;
        [SerializeField] int baseDurability = 5;
        [SerializeField] int yieldAmount = 2;
        [SerializeField] GameObject outline;
        [SerializeField, Range(0f, 20f), Tooltip("该垃圾散落时允许的最大倾斜角。平底物体设为 0，圆柱/柔性物体可适当倾斜。")]
        float naturalTiltDegrees = 5f;
        [SerializeField, Range(0.01f, 0.15f), Tooltip("沿地表法线压入地面的深度，用于补偿低模球面与模型外轮廓产生的视觉悬空。")]
        float groundContactDepth = 0.05f;
        [SerializeField, Tooltip("大型家电等物体生成时是否随机以直立/横倒两种姿态落地。")]
        bool randomlyLayOnSide;
        [SerializeField, Tooltip("随机外观所使用的渲染器（例如易拉罐罐身）。")]
        Renderer[] variantRenderers;
        [SerializeField, Tooltip("随机外观材质；不改变垃圾类别与玩法数据。")]
        Material[] visualMaterials;

        int durability;

        public TrashCategory Category => category;
        public bool Large => large;
        public int Durability => durability;
        public int BaseDurability => Mathf.Max(1, baseDurability);
        public int YieldAmount => Mathf.Max(1, yieldAmount);
        public float NaturalTiltDegrees => Mathf.Clamp(naturalTiltDegrees, 0f, 20f);
        public float GroundContactDepth => Mathf.Clamp(groundContactDepth, 0.01f, 0.15f);
        public bool RandomlyLayOnSide => randomlyLayOnSide;

        public void Configure(TrashCategory cat, bool isLarge, int durabilityValue, int yield, GameObject outlineGo)
        {
            category = cat;
            large = isLarge;
            baseDurability = Mathf.Max(1, durabilityValue);
            yieldAmount = Mathf.Max(1, yield);
            outline = outlineGo;
            ResetState();
        }

        /// <summary>刷新预制体时重绑描边对象（由 WhiteboxBuilder.RefreshTrashOutlines 调用）。</summary>
        public void SetOutline(GameObject go)
        {
            outline = go;
            if (outline != null) outline.SetActive(false);
        }

        public void ResetState()
        {
            durability = BaseDurability;
            RandomizeVisualMaterial();
            SetHighlight(false);
            transform.localScale = Vector3.one;
        }

        void RandomizeVisualMaterial()
        {
            if (variantRenderers == null || variantRenderers.Length == 0 ||
                visualMaterials == null || visualMaterials.Length == 0) return;
            Material selected = visualMaterials[Random.Range(0, visualMaterials.Length)];
            if (selected == null) return;
            for (int i = 0; i < variantRenderers.Length; i++)
                if (variantRenderers[i] != null) variantRenderers[i].sharedMaterial = selected;
        }

        /// <summary>每帧由 InteractionController 调用：同一时刻只允许一个高亮目标。</summary>
        public void SetHighlight(bool on)
        {
            if (outline != null && outline.activeSelf != on) outline.SetActive(on);
        }

        /// <summary>返回剩余耐久；负值钳到 0（NUM-003 的钳制约定，不在此写死数值）。</summary>
        public int Hit(int damage)
        {
            durability = Mathf.Max(0, durability - Mathf.Max(0, damage));
            return durability;
        }
    }
}
