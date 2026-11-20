// @trace SAVE-001, SAVE-003, UI-004
using UnityEngine;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 局内存档面板（F9 打开）。UI 由 SaveSlotBuilder 生成到 UI_Whitebox 下；
    /// 本脚本按名字查找 5 个槽按钮，点击即把当前进度（含玩家位置）写入对应槽。
    ///
    /// - 每个按钮显示目标槽与现有存档时间；空槽显示"空"，点击 = 新建存档。
    /// - 已有存档的槽点击会覆盖（按钮文字提示"覆盖"），这是常规存档语义。
    /// - 保存后自动选中该槽为 ActiveSlot，后续自动保存都写这里。
    /// </summary>
    public sealed class InGameSaveView : MonoBehaviour
    {
        public const string PanelName = "Panel_InGameSave";
        public const int SlotCount = SaveSlotSystem.SlotCount;

        WhiteboxGame game;
        RectTransform panel;
        readonly Button[] slotButtons = new Button[SlotCount];
        readonly Text[] slotLabels = new Text[SlotCount];
        Button btnClose;
        bool bound;

        public bool IsOpen => panel != null && panel.gameObject.activeSelf;

        void Awake() { Bind(); }

        bool Bind()
        {
            if (bound) return true;
            if (game == null) game = FindFirstObjectByType<WhiteboxGame>();
            Transform found = FindDeep(transform, PanelName);
            if (found == null)
            {
                Debug.LogWarning("[InGameSave] 未找到 " + PanelName + "，请跑 Tools/垃圾星球白膜/⑮ 生成多存档界面与按钮");
                return false;
            }
            panel = found as RectTransform;
            if (panel == null) return false;

            for (int i = 0; i < SlotCount; i++)
            {
                string n = (i + 1).ToString();
                slotButtons[i] = FindButton(panel, "Btn_SaveSlot_" + n);
                slotLabels[i] = FindText(panel, "Txt_SaveSlot_" + n);
                if (slotButtons[i] != null)
                {
                    int idx = i;
                    slotButtons[i].onClick.RemoveAllListeners();
                    slotButtons[i].onClick.AddListener(() => OnSaveSlot(idx));
                }
            }
            btnClose = FindButton(panel, "Btn_SaveClose");
            if (btnClose != null)
            {
                btnClose.onClick.RemoveAllListeners();
                btnClose.onClick.AddListener(Close);
            }
            panel.gameObject.SetActive(false);
            bound = true;
            return true;
        }

        public void Open()
        {
            if (!Bind()) return;
            Refresh();
            panel.gameObject.SetActive(true);
        }

        public void Close()
        {
            if (panel != null) panel.gameObject.SetActive(false);
        }

        /// <summary>刷新按钮文字：显示每个槽的时间或"空"，并标注覆盖。</summary>
        public void Refresh()
        {
            if (!Bind()) return;
            for (int i = 0; i < SlotCount; i++)
            {
                if (slotLabels[i] == null) continue;
                int slot = i + 1;
                string at = SaveSlotSystem.SavedAtText(slot);
                slotLabels[i].text = string.IsNullOrEmpty(at)
                    ? "槽 " + slot + "  空（新建）"
                    : "槽 " + slot + "  " + at + "  · 覆盖";
            }
        }

        void OnSaveSlot(int index)
        {
            int slot = index + 1;
            if (game == null || game.State == null) return;
            WhiteboxState.Save(game.State, slot); // 写入指定槽（CapturePoseHook 会带上位置/视角）
            Refresh();
            game.Notice("已保存到槽 " + slot);
            Debug.Log("[InGameSave] 保存到槽 " + slot);
        }

        // 供 Builder 注入 WhiteboxGame 引用（不改场景 YAML）
        public void SetGame(WhiteboxGame g) { game = g; }

        static RectTransform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root as RectTransform;
            for (int i = 0; i < root.childCount; i++)
            {
                RectTransform r = FindDeep(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        static Button FindButton(Transform root, string name)
        {
            Transform t = FindDeepTransform(root, name);
            return t != null ? t.GetComponent<Button>() : null;
        }

        static Text FindText(Transform root, string name)
        {
            Transform t = FindDeepTransform(root, name);
            return t != null ? t.GetComponent<Text>() : null;
        }

        static Transform FindDeepTransform(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform r = FindDeepTransform(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
