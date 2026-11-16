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

        int durability;

        public TrashCategory Category => category;
        public bool Large => large;
        public int Durability => durability;
        public int BaseDurability => Mathf.Max(1, baseDurability);
        public int YieldAmount => Mathf.Max(1, yieldAmount);

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
            SetHighlight(false);
            transform.localScale = Vector3.one;
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
