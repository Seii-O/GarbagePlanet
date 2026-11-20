// @trace SAVE-001, SAVE-003, UI-005
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 主菜单选槽界面（挂在 Title.unity 的 ContinueGameUI 上，或任意能找到它的对象上）。
    ///
    /// 行为：
    /// - 扫描 ContinueGameUI 下的 5 个子 Button（按层级顺序 = 槽 1..5），把有存档的按钮点亮并显示
    ///   "槽 1  2026-11-20 17:30"；无存档的按钮置灰、不可点、子文字清空。
    /// - 点击有档按钮：记录选中的槽，然后加载 WhiteboxPrototype；进场景 Awake 自动读该槽并还原位置。
    ///
    /// 不手写场景 YAML：按钮与文字在运行时按名字/层级查找并绑定，菜单⑮负责生成文字对象。
    /// </summary>
    public sealed class SaveSlotMenuView : MonoBehaviour
    {
        public const int SlotCount = SaveSlotSystem.SlotCount;
        public const string PanelName = "ContinueGameUI";

        [Tooltip("选中的存档槽（0=未选）。一般不用手填，点按钮时自动写入。")]
        [SerializeField] int selectedSlot;

        GameObject panel;
        readonly Button[] slotButtons = new Button[SlotCount];
        readonly Text[] slotLabels = new Text[SlotCount];
        bool bound;

        public int SelectedSlot => selectedSlot;

        void OnEnable()
        {
            Bind();
            Refresh();
        }

        /// <summary>由 TitleController.ContinueGame 在打开面板后调用，刷新槽位状态。</summary>
        public void OnPanelOpened()
        {
            Bind();   // Bind 幂等：已绑定则复用缓存的槽按钮，不重新扫描（避免把关闭按钮误当槽位）
            Refresh();
        }

        bool Bind()
        {
            if (bound) return true;
            // 面板默认失活，GameObject.Find 找不到；用场景根节点递归查（含失活）
            panel = TitleController.FindInScene(PanelName);
            if (panel == null)
            {
                Debug.LogWarning("[SaveSlotMenu] 未找到 " + PanelName + "，无法列出存档槽");
                return false;
            }

            // 首次绑定：把"关闭/返回"按钮与存档槽按钮分开。
            // 早期版本对全部按钮按 Y 排序取前 5，而右上角红色 X 关闭按钮 Y 最高，
            // 被当成"槽 1"→ RemoveAllListeners 清掉关闭逻辑、改绑读档 → 点 X 反而进游戏。
            // 现在先剔除关闭按钮，只对真正的槽按钮做读档绑定。
            if (slotButtons[0] == null)
            {
                Button[] all = panel.GetComponentsInChildren<Button>(true);
                var candidates = new List<Button>();
                Button closeBtn = null;
                foreach (Button b in all)
                {
                    if (IsCloseButton(b)) { if (closeBtn == null) closeBtn = b; continue; }
                    candidates.Add(b);
                }

                // 槽按钮按屏幕纵坐标从高到低排序：最上面 = 槽 1。
                candidates.Sort((a, b) =>
                    ((RectTransform)b.transform).anchoredPosition.y
                        .CompareTo(((RectTransform)a.transform).anchoredPosition.y));
                if (candidates.Count < SlotCount)
                    Debug.LogWarning("[SaveSlotMenu] " + PanelName + " 下有 " + candidates.Count
                        + " 个槽按钮，需要 " + SlotCount + " 个");
                for (int i = 0; i < SlotCount && i < candidates.Count; i++)
                {
                    slotButtons[i] = candidates[i];
                    slotLabels[i] = FindChildText(candidates[i].gameObject);
                    int idx = i;
                    candidates[i].onClick.RemoveAllListeners();
                    candidates[i].onClick.AddListener(() => OnSlotClick(idx));
                }

                // 关闭按钮接到主菜单：原绑定只是 SetActive(false) 隐藏选槽面板，
                // 但继续游戏时 GameStartUI 已被隐藏，光隐藏面板会留黑屏；
                // BackToMain() 关掉选槽面板并重新显示主菜单，才是真正的"回主菜单"。
                if (closeBtn != null)
                {
                    TitleController tc = UnityEngine.Object.FindFirstObjectByType<TitleController>();
                    if (tc != null)
                    {
                        closeBtn.onClick.RemoveAllListeners();
                        closeBtn.onClick.AddListener(tc.BackToMain);
                        Debug.Log("[SaveSlotMenu] 关闭按钮已接到 TitleController.BackToMain（回主菜单）");
                    }
                    else Debug.LogWarning("[SaveSlotMenu] 未找到 TitleController，关闭按钮保留场景原绑定");
                }
            }

            bound = true;
            return true;
        }

        /// <summary>判定是否为"关闭/返回"按钮而非存档槽：语义命名，或场景里已带持久 onClick（槽按钮是空的）。</summary>
        static bool IsCloseButton(Button b)
        {
            string n = (b.name ?? "").ToLowerInvariant();
            if (n.Contains("close") || n.Contains("back") || n.Contains("exit")
                || n.Contains("关闭") || n.Contains("返回"))
                return true;
            // 场景中 5 个槽按钮的 m_OnClick 是空的；X 关闭按钮带一个 SetActive 持久监听。
            // 槽按钮运行时的 AddListener 非持久，GetPersistentEventCount 仍为 0，判定稳定。
            return b.onClick.GetPersistentEventCount() > 0;
        }

        /// <summary>刷新 5 个按钮：有档=亮+时间，无档=灰+空文字。</summary>
        public void Refresh()
        {
            if (!Bind()) return;
            for (int i = 0; i < SlotCount; i++)
            {
                int slot = i + 1;
                Button b = slotButtons[i];
                Text t = slotLabels[i];
                bool has = SaveSlotSystem.Exists(slot);
                if (b != null) b.interactable = has;
                if (t != null)
                {
                    if (has) t.text = "槽 " + slot + "  " + SaveSlotSystem.SavedAtText(slot);
                    else t.text = "";
                }
            }
        }

        void OnSlotClick(int index)
        {
            int slot = index + 1;
            if (!SaveSlotSystem.Exists(slot)) return; // 无档按钮本就不可点，这里再兜一层
            selectedSlot = slot;
            SaveSlotSystem.ActiveSlot = slot;
            Time.timeScale = 1f;
            Debug.Log("[SaveSlotMenu] 选择存档槽 " + slot + " → 加载 " + TitleController.GameSceneName);
            UnityEngine.SceneManagement.SceneManager.LoadScene(TitleController.GameSceneName);
        }

        static Text FindChildText(GameObject go)
        {
            if (go == null) return null;
            Text[] texts = go.GetComponentsInChildren<Text>(true);
            return texts.Length > 0 ? texts[0] : null;
        }
    }
}
