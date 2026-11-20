// @trace BAG-001, BAG-002, NUM-018
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 工具与技能书背包的独立配置（与垃圾背包、金币交易均分离）。
    /// 只负责格子数量与堆叠规则；具体物品清单仍来自 ToolConfig，避免第三份真值。
    /// 由菜单「⑬ 生成背包与交易界面」写入 Assets/Data/ToolBagConfig.asset；
    /// 旧场景未接线时运行时用 CreateDefault() 兜底。
    /// </summary>
    [CreateAssetMenu(fileName = "ToolBagConfig", menuName = "垃圾星球/工具背包配置")]
    public sealed class ToolBagConfig : ScriptableObject
    {
        [SerializeField, Range(4, 24), Tooltip("工具与技能书背包的格子数（默认 12；与垃圾背包 20/40 件完全不同）")]
        int slotCount = 12;
        [SerializeField, Range(1, 9), Tooltip("单格工具堆叠上限（工具默认 1 = 同 id 占 1 格）")]
        int toolStackLimit = 1;
        [SerializeField, Tooltip("技能书按 ToolConfig.SkillBookDef.maxStacks 叠层；关闭后每本占 1 格")]
        bool stackBooks = true;

        public int SlotCount => Mathf.Clamp(slotCount, 4, 24);
        public int ToolStackLimit => Mathf.Clamp(toolStackLimit, 1, 9);
        public bool StackBooks => stackBooks;

        void OnValidate()
        {
            slotCount = Mathf.Clamp(slotCount, 4, 24);
            toolStackLimit = Mathf.Clamp(toolStackLimit, 1, 9);
        }

        public static ToolBagConfig CreateDefault()
        {
            ToolBagConfig c = CreateInstance<ToolBagConfig>();
            c.slotCount = 12;
            c.toolStackLimit = 1;
            c.stackBooks = true;
            return c;
        }
    }
}
