// @trace TOOL-015, TOOL-016
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 第一人称手持工具视图：按 WhiteboxState.activeToolId 控制 HandRight 下各工具模型显隐。
    /// 只做视觉，不碰任何加成公式——加成由 ToolSystem 按"已装备/已使用"独立判定。
    /// 工具模型由 Editor/ToolViewModelBuilder 生成并注入映射；美术到位后只需替换映射里的模型对象。
    /// </summary>
    public sealed class ToolViewModel : MonoBehaviour
    {
        [System.Serializable]
        public struct Entry
        {
            public string toolId;       // T-01 ~ T-10
            public GameObject model;    // 挂在 HandRight 下的工具模型（默认 SetActive(false)）
        }

        [SerializeField] Entry[] entries = new Entry[0];

        WhiteboxGame game;
        string currentShown = "";

        public void Init(WhiteboxGame g)
        {
            game = g;
            ApplyHeld(g != null ? g.State : null);
        }

        /// <summary>供 Builder 注入模型映射（Editor API 调用）。</summary>
        public void Wire(Entry[] list) { entries = list ?? new Entry[0]; }

        /// <summary>按当前 activeToolId 刷新显隐；找不到映射则空手。</summary>
        public void ApplyHeld(WhiteboxState s)
        {
            string want = s != null ? (s.activeToolId ?? "") : "";
            for (int i = 0; i < entries.Length; i++)
            {
                bool on = entries[i].model != null && entries[i].toolId == want;
                if (entries[i].model != null && entries[i].model.activeSelf != on)
                    entries[i].model.SetActive(on);
            }
            if (want != currentShown)
            {
                currentShown = want;
                if (game != null && game.Hud != null && !string.IsNullOrEmpty(want))
                {
                    ToolDef def = game.Tool != null && game.Tool.Config != null
                        ? game.Tool.Config.Find(want) : null;
                    string label = def != null ? def.displayName : want;
                    if (def == null && game.Tool != null && game.Tool.Config != null)
                    {
                        SkillBookDef book = game.Tool.Config.FindBook(want);
                        if (book != null) label = book.displayName;
                    }
                    game.Hud.Notice("手持：" + label);
                }
            }
        }

        /// <summary>返回当前可拿手上的工具条目（供数字键/背包使用）；由 Game/背包调用 ToolSystem.SetHeldTool。</summary>
        public string ToolIdAt(int index)
        {
            return index >= 0 && index < entries.Length ? entries[index].toolId : "";
        }

        public int Count => entries.Length;
    }
}
