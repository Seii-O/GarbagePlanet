// @trace UI-005, MECH-001, SAVE-001
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 标题场景控制器：把用户手搭的 Title.unity 上的按钮接到真实跳转/音量逻辑（菜单⑭ TitleBuilder 接线）。
    /// 红线：不手写场景 YAML；按钮引用由 Builder 通过 Wire() 注入。
    ///
    /// 按钮语义（按按钮子 Text 文字识别）：
    ///   - 开始新游戏 → ActiveSlot=0 + LoadScene(WhiteboxPrototype)，不删任何槽
    ///   - 继续游戏   → 打开 ContinueGameUI 选槽面板（SaveSlotMenuView 列出 5 槽，点有档按钮进游戏）
    ///   - 退出游戏   → Application.Quit / 编辑器内停止 Play
    ///   - 音效音量 / 音乐音量 → PlayerPrefs 持久化 + AudioListener.volume（当前无独立音源，合并到主音量）
    ///
    /// 多槽存档由 SaveSlotSystem 管理；本控制器只负责主菜单跳转。
    /// </summary>
    public sealed class TitleController : MonoBehaviour
    {
        public const string GameSceneName = "WhiteboxPrototype";
        public const string TitleSceneName = "Title";

        const string KeyMusic = "tp.volume.music";
        const string KeySfx = "tp.volume.sfx";

        [Header("按钮（由 TitleBuilder 按文字注入）")]
        [SerializeField] Button btnNewGame;
        [SerializeField] Button btnContinue;
        [SerializeField] Button btnQuit;

        [Header("音量滑块（由 TitleBuilder 注入）")]
        [SerializeField] Slider sfxSlider;
        [SerializeField] Slider musicSlider;

        /// <summary>供 Builder 注入按钮与滑块引用，不改场景 YAML。</summary>
        public void Wire(Button newGame, Button cont, Button quit, Slider sfx, Slider music)
        {
            btnNewGame = newGame;
            btnContinue = cont;
            btnQuit = quit;
            sfxSlider = sfx;
            musicSlider = music;
        }

        void Start()
        {
            // 恢复音量（滑块在 Builder 里已接 onValueChanged → SetSfxVolume/SetMusicVolume）
            float sfx = PlayerPrefs.GetFloat(KeySfx, 1f);
            float music = PlayerPrefs.GetFloat(KeyMusic, 1f);
            if (sfxSlider != null) sfxSlider.SetValueWithoutNotify(sfx);
            if (musicSlider != null) musicSlider.SetValueWithoutNotify(music);
            ApplyVolume(sfx, music);

            // 标题界面释放鼠标，避免延续游戏里的锁定状态
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 1f;
        }

        // ------------------------------------------------------------------
        // 按钮回调（public，供 Button.onClick 持久化绑定）
        // ------------------------------------------------------------------

        /// <summary>开始新游戏：不删任何槽，ActiveSlot=0（新局未绑定槽，直到玩家在局内 F9 保存到某槽）。</summary>
        public void StartNewGame()
        {
            Debug.Log("[TitleController] 开始新游戏：全新一局 → 加载 " + GameSceneName);
            SaveSlotSystem.ActiveSlot = 0;
            Time.timeScale = 1f;
            SceneManager.LoadScene(GameSceneName);
        }

        /// <summary>继续游戏：打开选槽面板（SaveSlotMenuView），由玩家挑一个槽进入；不再直接加载。</summary>
        public void ContinueGame()
        {
            // 选槽面板默认失活，GameObject.Find 找不到失活对象，必须按场景根节点递归找。
            GameObject slotPanel = FindInScene("ContinueGameUI");
            if (slotPanel == null)
            {
                Debug.LogWarning("[TitleController] 未找到 ContinueGameUI 选槽面板；改按新游戏处理");
                StartNewGame();
                return;
            }
            GameObject startPanel = FindInScene("GameStartUI");
            if (startPanel != null) startPanel.SetActive(false);
            slotPanel.SetActive(true);
            SaveSlotMenuView menu = slotPanel.GetComponent<SaveSlotMenuView>();
            if (menu == null) menu = slotPanel.AddComponent<SaveSlotMenuView>();
            menu.OnPanelOpened();
            Debug.Log("[TitleController] 继续游戏：打开选槽面板");
        }

        /// <summary>选槽面板的"返回"按钮：回到主菜单面板。</summary>
        public void BackToMain()
        {
            GameObject slotPanel = FindInScene("ContinueGameUI");
            GameObject startPanel = FindInScene("GameStartUI");
            if (slotPanel != null) slotPanel.SetActive(false);
            if (startPanel != null) startPanel.SetActive(true);
        }

        /// <summary>在当前场景的全部根对象里按名字找（含失活对象）。</summary>
        public static GameObject FindInScene(string name)
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name == name) return root;
                Transform[] all = root.GetComponentsInChildren<Transform>(true);
                foreach (Transform t in all) if (t.name == name) return t.gameObject;
            }
            return null;
        }

        /// <summary>退出游戏：编辑器内停 Play，打包后退进程。</summary>
        public void QuitGame()
        {
            Debug.Log("[TitleController] 退出游戏");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void SetSfxVolume(float value)
        {
            PlayerPrefs.SetFloat(KeySfx, Mathf.Clamp01(value));
            PlayerPrefs.Save();
            ApplyVolume(PlayerPrefs.GetFloat(KeySfx, 1f), PlayerPrefs.GetFloat(KeyMusic, 1f));
        }

        public void SetMusicVolume(float value)
        {
            PlayerPrefs.SetFloat(KeyMusic, Mathf.Clamp01(value));
            PlayerPrefs.Save();
            ApplyVolume(PlayerPrefs.GetFloat(KeySfx, 1f), PlayerPrefs.GetFloat(KeyMusic, 1f));
        }

        /// <summary>
        /// 分轨音量交给 音效管理：SFX / BGM 各走一条独立通道，AudioListener 保持 1，
        /// 避免「总音量 × 通道音量」二次衰减。接口不变，滑块仍只写 PlayerPrefs。
        /// </summary>
        static void ApplyVolume(float sfx, float music)
        {
            AudioListener.volume = 1f;
            Sfx.ApplyVolumePrefs();
        }
    }
}
