// @trace TOOL-016
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 工具 / 技能书图标表（运行时读）。由菜单「⑲ 生成工具图标表」用 Editor API 写入
    /// Assets/Data/ToolIconSet.asset，键同时包含 toolId（T-01~T-10）与 rewardId（weapon_* / book_*）。
    /// 只是显示层：查不到图标时返回 null，UI 回退到文字，不影响任何数值逻辑。
    /// </summary>
    [CreateAssetMenu(menuName = "垃圾星球/工具图标表", fileName = "ToolIconSet")]
    public sealed class ToolIconSet : ScriptableObject
    {
        [SerializeField] string[] ids = new string[0];
        [SerializeField] Sprite[] sprites = new Sprite[0];

        public int Count => ids != null ? ids.Length : 0;

        /// <summary>按 toolId 或 rewardId 查图标；未配置返回 null。</summary>
        public Sprite Get(string id)
        {
            if (string.IsNullOrEmpty(id) || ids == null || sprites == null) return null;
            for (int i = 0; i < ids.Length && i < sprites.Length; i++)
                if (ids[i] == id) return sprites[i];
            return null;
        }

        /// <summary>供 Editor 生成器整体写入（运行时不要调用）。</summary>
        public void SetAll(string[] newIds, Sprite[] newSprites)
        {
            ids = newIds ?? new string[0];
            sprites = newSprites ?? new Sprite[0];
        }
    }
}
