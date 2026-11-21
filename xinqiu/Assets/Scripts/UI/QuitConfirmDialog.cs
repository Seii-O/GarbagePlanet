// @trace UI-005, SAVE-001, MECH-013
using UnityEngine;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 局内「退出」确认弹窗：暂停页 / 结算页的退出按钮点击后弹出，二选一：
    ///   - 【是】保存进度 → 关闭本弹窗 → 打开局内存档面板（退出模式，选槽命名保存后回主界面）
    ///   - 【否】直接回标题主界面（Title.unity），不打开存档面板
    ///
    /// 视觉与按钮由 WhiteboxUiBuilder 生成到 UI_Whitebox 下；本组件只写显隐与按钮回调，
    /// 不碰 RectTransform / 颜色 / 字体（与 WhiteboxCanvasHud 同一套约定）。
    /// 面板常驻失活，按名字查找（含失活对象），可重复执行不重复绑定。
    /// </summary>
    public sealed class QuitConfirmDialog : MonoBehaviour
    {
        public const string PanelName = "Panel_QuitConfirm";

        [Tooltip("由 WhiteboxUiBuilder 写入；指向场景里的 sys_whitebox")]
        public WhiteboxGame Game;

        GameObject panel;
        Button btnYes, btnNo;
        bool bound;

        public bool IsOpen => panel != null && panel.activeSelf;

        void Awake() { Bind(); }

        bool Bind()
        {
            if (bound) return true;
            if (Game == null) Game = FindFirstObjectByType<WhiteboxGame>();
            Transform found = FindDeep(transform, PanelName);
            if (found == null)
            {
                Debug.LogWarning("[QuitConfirm] 未找到 " + PanelName + "，请跑 Tools/垃圾星球白膜/④ 生成 / 补齐 Hierarchy UI");
                return false;
            }
            panel = found.gameObject;

            btnYes = FindButton(panel.transform, "Btn_QuitYes");
            btnNo = FindButton(panel.transform, "Btn_QuitNo");
            if (btnYes != null)
            {
                btnYes.onClick.RemoveAllListeners();
                btnYes.onClick.AddListener(OnYes);
            }
            if (btnNo != null)
            {
                btnNo.onClick.RemoveAllListeners();
                btnNo.onClick.AddListener(OnNo);
            }
            panel.SetActive(false);
            bound = true;
            return true;
        }

        /// <summary>由退出按钮（WhiteboxUiActions.Quit）调用。</summary>
        public void Open()
        {
            if (!Bind()) return;
            panel.SetActive(true);
        }

        public void Close()
        {
            if (panel != null) panel.SetActive(false);
        }

        void OnYes()
        {
            Close();
            InGameSaveView save = Game != null ? Game.InGameSave : null;
            if (save == null)
            {
                // 存档面板缺失（旧场景未补齐 UI）：降级为直接存当前激活槽再回主界面，功能不丢
                Debug.LogWarning("[QuitConfirm] 未找到局内存档面板，【是】降级为保存到当前槽后回主界面");
                if (Game != null && Game.State != null) WhiteboxState.Save(Game.State);
                Time.timeScale = 1f;
                UnityEngine.SceneManagement.SceneManager.LoadScene(TitleController.TitleSceneName);
                return;
            }
            save.OpenForQuit(); // 选槽保存成功后自动回主界面
            Debug.Log("[QuitConfirm] 【是】→ 打开存档面板（退出模式）");
        }

        void OnNo()
        {
            Close();
            Time.timeScale = 1f; // 暂停时为 0，不还原会让标题与下次进关卡卡在时间静止
            Debug.Log("[QuitConfirm] 【否】→ 直接返回标题 " + TitleController.TitleSceneName);
            UnityEngine.SceneManagement.SceneManager.LoadScene(TitleController.TitleSceneName);
        }

        // 供 Builder 注入 WhiteboxGame 引用（不改场景 YAML）
        public void SetGame(WhiteboxGame g) { Game = g; }

        static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform r = FindDeep(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        static Button FindButton(Transform root, string name)
        {
            Transform t = FindDeep(root, name);
            return t != null ? t.GetComponent<Button>() : null;
        }
    }
}
