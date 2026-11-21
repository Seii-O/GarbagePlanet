// @trace SAVE-001, SAVE-003, UI-004
using UnityEngine;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 局内存档面板（F9 打开，或从退出确认框【是】进入）。UI 由 SaveSlotBuilder 生成到 UI_Whitebox 下；
    /// 本脚本按名字查找 5 个槽按钮，点击即把当前进度（含玩家位置）写入对应槽。
    ///
    /// - 顶部输入框可给本次存档取名；留空则显示回退到"槽 N"。
    /// - 每个按钮显示存档名与时间；空槽显示"空"，点击 = 新建存档，覆盖已有槽。
    /// - 每个槽右下角有垃圾桶按钮：点任一垃圾桶进入删除模式，槽位变为可多选，
    ///   选好后按「删除所选」统一删除；「取消」退出删除模式，不做任何删除。
    /// - 退出流程模式（OpenForQuit）：保存成功后自动回标题主界面，不做则关闭面板留在游戏。
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
        readonly Button[] trashButtons = new Button[SlotCount];
        readonly Image[] slotBodies = new Image[SlotCount]; // 删除模式勾选高亮用的槽底图
        readonly Color[] slotBaseColors = new Color[SlotCount]; // Bind 时记录用户原色，退出删除模式还原
        Button btnClose, btnDeleteSelected, btnDeleteCancel;
        InputField nameInput;
        bool bound;

        bool quitMode;        // 退出流程：保存成功后回主界面
        bool deleteMode;      // 删除模式：槽按钮改为勾选，不再保存
        readonly bool[] selected = new bool[SlotCount];

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
                trashButtons[i] = FindButton(panel, "Btn_Trash_" + n);
                Transform slotT = FindDeepTransform(panel, "Btn_SaveSlot_" + n);
                slotBodies[i] = slotT != null ? slotT.GetComponent<Image>() : null;
                if (slotBodies[i] != null) slotBaseColors[i] = slotBodies[i].color;
                if (slotButtons[i] != null)
                {
                    int idx = i;
                    slotButtons[i].onClick.RemoveAllListeners();
                    slotButtons[i].onClick.AddListener(() => OnSlotClick(idx));
                }
                if (trashButtons[i] != null)
                {
                    trashButtons[i].onClick.RemoveAllListeners();
                    trashButtons[i].onClick.AddListener(() => EnterDeleteMode());
                }
            }
            btnClose = FindButton(panel, "Btn_SaveClose");
            if (btnClose != null)
            {
                btnClose.onClick.RemoveAllListeners();
                btnClose.onClick.AddListener(Close);
            }
            btnDeleteSelected = FindButton(panel, "Btn_DeleteSelected");
            if (btnDeleteSelected != null)
            {
                btnDeleteSelected.onClick.RemoveAllListeners();
                btnDeleteSelected.onClick.AddListener(DeleteSelected);
            }
            btnDeleteCancel = FindButton(panel, "Btn_DeleteCancel");
            if (btnDeleteCancel != null)
            {
                btnDeleteCancel.onClick.RemoveAllListeners();
                btnDeleteCancel.onClick.AddListener(CancelDeleteMode);
            }
            Transform inputT = FindDeepTransform(panel, "Inp_SaveName");
            nameInput = inputT != null ? inputT.GetComponent<InputField>() : null;

            SetDeleteMode(false);
            panel.gameObject.SetActive(false);
            bound = true;
            return true;
        }

        /// <summary>普通打开（F9）：保存后留在游戏。</summary>
        public void Open()
        {
            quitMode = false;
            OpenCore();
        }

        /// <summary>退出流程打开（退出确认框【是】）：保存成功后自动回标题主界面。</summary>
        public void OpenForQuit()
        {
            quitMode = true;
            OpenCore();
        }

        void OpenCore()
        {
            if (!Bind()) return;
            SetDeleteMode(false); // 每次打开都回到常规保存态，不残留上次的勾选
            if (nameInput != null) nameInput.SetTextWithoutNotify("");
            Refresh();
            panel.gameObject.SetActive(true);
        }

        public void Close()
        {
            if (panel != null) panel.gameObject.SetActive(false);
            quitMode = false;
            SetDeleteMode(false);
        }

        public void Refresh()
        {
            if (!Bind()) return;
            for (int i = 0; i < SlotCount; i++)
            {
                int slot = i + 1;
                bool has = SaveSlotSystem.Exists(slot);
                if (slotLabels[i] != null)
                {
                    if (!has) slotLabels[i].text = "槽 " + slot + "  空（新建）";
                    else
                    {
                        string mark = selected[i] ? "  ✓已选" : "";
                        slotLabels[i].text = SaveSlotSystem.SlotTitle(slot) + mark + "  · 覆盖";
                    }
                }
                // 删除模式下：空槽不可勾选；垃圾桶在非删除模式才显示
                if (trashButtons[i] != null) trashButtons[i].gameObject.SetActive(!deleteMode);
                ApplySelectionVisual(i);
            }
            // 删除确认按钮：删除模式才显示，且至少选了一个档才可点
            if (btnDeleteSelected != null)
                btnDeleteSelected.gameObject.SetActive(deleteMode);
            if (btnDeleteSelected != null && deleteMode)
                btnDeleteSelected.interactable = HasSelection;
            if (btnDeleteCancel != null) btnDeleteCancel.gameObject.SetActive(deleteMode);
            if (btnClose != null) btnClose.gameObject.SetActive(!deleteMode);
        }

        bool HasSelection
        {
            get
            {
                for (int i = 0; i < SlotCount; i++) if (selected[i]) return true;
                return false;
            }
        }

        // ---------------- 保存 ----------------

        void OnSlotClick(int index)
        {
            if (deleteMode) { ToggleSelection(index); return; }
            SaveToSlot(index);
        }

        void SaveToSlot(int index)
        {
            int slot = index + 1;
            if (game == null || game.State == null) return;
            string name = nameInput != null ? nameInput.text : "";
            WhiteboxState.Save(game.State, slot, name); // CapturePoseHook 会带上位置/视角
            Refresh();
            string shown = string.IsNullOrEmpty(name.Trim()) ? SaveSlotSystem.DefaultName(slot) : name.Trim();
            if (game != null) game.Notice("已保存到「" + shown + "」");
            Debug.Log("[InGameSave] 保存到槽 " + slot + "，名字=" + shown);

            if (quitMode)
            {
                // 退出流程：先还原时间缩放（暂停时为 0），再回标题主界面
                Time.timeScale = 1f;
                Close();
                UnityEngine.SceneManagement.SceneManager.LoadScene(TitleController.TitleSceneName);
            }
        }

        // ---------------- 批量删除 ----------------

        /// <summary>点任一垃圾桶进入删除模式，槽位变为可多选。</summary>
        public void EnterDeleteMode()
        {
            if (!Bind()) return;
            deleteMode = true;
            for (int i = 0; i < SlotCount; i++) selected[i] = false;
            Refresh();
            if (game != null) game.Notice("删除模式：点选要删除的存档，再按「删除所选」");
        }

        public void CancelDeleteMode()
        {
            SetDeleteMode(false);
            Refresh();
        }

        void SetDeleteMode(bool on)
        {
            deleteMode = on;
            for (int i = 0; i < SlotCount; i++) selected[i] = false;
        }

        void ToggleSelection(int index)
        {
            if (!SaveSlotSystem.Exists(index + 1)) return; // 空槽不可选
            selected[index] = !selected[index];
            Refresh();
        }

        void ApplySelectionVisual(int i)
        {
            if (slotBodies[i] == null) return;
            // 只在删除模式临时改色，退出后还原 Bind 时记录的用户原色，不覆盖用户在 Inspector 的配色
            if (!deleteMode) { slotBodies[i].color = slotBaseColors[i]; return; }
            if (selected[i]) slotBodies[i].color = new Color(0.86f, 0.38f, 0.32f, 1f); // 选中=警示红
            else if (!SaveSlotSystem.Exists(i + 1))
            {
                Color c = slotBaseColors[i];
                slotBodies[i].color = new Color(c.r, c.g, c.b, c.a * 0.4f); // 空槽半透明
            }
            else slotBodies[i].color = slotBaseColors[i];
        }

        void DeleteSelected()
        {
            if (!HasSelection) return;
            int deleted = 0;
            for (int i = 0; i < SlotCount; i++)
            {
                if (!selected[i]) continue;
                int slot = i + 1;
                if (!SaveSlotSystem.Exists(slot)) continue;
                SaveSlotSystem.Delete(slot);
                deleted++;
            }
            SetDeleteMode(false);
            Refresh();
            if (game != null) game.Notice("已删除 " + deleted + " 个存档");
            Debug.Log("[InGameSave] 删除模式完成，共删除 " + deleted + " 槽");
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
