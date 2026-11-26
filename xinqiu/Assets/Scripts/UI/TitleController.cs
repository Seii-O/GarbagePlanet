// @trace UI-005, MECH-001, SAVE-001
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 标题场景控制器：把用户手搭的 Title.unity 上的按钮接到真实跳转逻辑（菜单⑭ TitleBuilder 接线）。
    /// 红线：不手写场景 YAML；按钮引用由 Builder 通过 Wire() 注入。
    ///
    /// 按钮语义（按按钮子 Text 文字识别）：
    ///   - 开始新游戏 → ActiveSlot=0 + LoadScene(WhiteboxPrototype)，不删任何槽
    ///   - 继续游戏   → 打开 ContinueGameUI 选槽面板（SaveSlotMenuView 列出 5 槽，点有档按钮进游戏）
    ///   - 退出游戏   → Application.Quit / 编辑器内停止 Play
    ///
    /// 多槽存档由 SaveSlotSystem 管理；本控制器只负责主菜单跳转。
    /// </summary>
    public sealed class TitleController : MonoBehaviour
    {
        public const string GameSceneName = "WhiteboxPrototype";
        public const string TitleSceneName = "Title";
        const string ControlHintsKey = "tp.ui.controls";
        public static bool ShowControlHints { get; private set; } = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void LoadInterfacePreferences()
        {
            ShowControlHints = PlayerPrefs.GetInt(ControlHintsKey, 1) != 0;
        }

        [Header("按钮（由 TitleBuilder 按文字注入）")]
        [SerializeField] Button btnNewGame;
        [SerializeField] Button btnContinue;
        [SerializeField] Button btnQuit;
        [SerializeField] Button btnSettings;
        [SerializeField] GameObject settingsPanel;
        [SerializeField] Slider sfxSlider;
        [SerializeField] Slider musicSlider;

        public void SetSfxVolume(float value) => SetVolume(GameAudio.EffectsKey, value);
        public void SetMusicVolume(float value) => SetVolume(GameAudio.MusicKey, value);

        static void SetVolume(string key, float value)
        {
            PlayerPrefs.SetFloat(key, Mathf.Clamp01(value));
            PlayerPrefs.Save();
            GameAudio.Current?.RefreshVolume();
        }

        /// <summary>供 Builder 注入按钮引用，不改场景 YAML。</summary>
        public void Wire(Button newGame, Button cont, Button quit, Button settings, GameObject panel)
        {
            btnNewGame = newGame;
            btnContinue = cont;
            btnQuit = quit;
            btnSettings = settings;
            settingsPanel = panel;
        }

        void Start()
        {
            ResolveVolumeSliders();
            if (sfxSlider != null)
                sfxSlider.SetValueWithoutNotify(Mathf.Clamp01(PlayerPrefs.GetFloat(GameAudio.EffectsKey, 1f)));
            if (musicSlider != null)
                musicSlider.SetValueWithoutNotify(Mathf.Clamp01(PlayerPrefs.GetFloat(GameAudio.MusicKey, 1f)));
            BuildSettingsTabs();
            BindSettingsButtons();
            CloseSettings();
            // 标题界面释放鼠标，避免延续游戏里的锁定状态
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 1f;
        }

        /// <summary>
        /// 把场景里"打开/关闭设置面板"的按钮统一接到本控制器。
        /// 除了目标指向 GameSettingUI 的按钮，还要兜住编辑器里引用被误清空的
        /// SetActive 按钮（持久化 target 为 null / fileID: 0）：否则按钮点了完全没反应。
        /// </summary>
        void BindSettingsButtons()
        {
            GameObject settings = ResolveSettingsPanel();
            if (settings == null) return;

            // 正式接线后的齿轮按钮直接引用控制器；运行时监听仍作为旧场景/引用丢失兜底。
            if (btnSettings != null)
            {
                DisableSetActiveListeners(btnSettings, settings);
                btnSettings.onClick.RemoveListener(OpenSettings);
                if (!HasPersistentOpenSettingsListener(btnSettings))
                    btnSettings.onClick.AddListener(OpenSettings);
            }

            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                foreach (Button button in root.GetComponentsInChildren<Button>(true))
                {
                    if (button == btnSettings) continue;
                    bool settingsToggle = false;
                    for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                    {
                        if (button.onClick.GetPersistentMethodName(i) != nameof(GameObject.SetActive)) continue;
                        UnityEngine.Object target = button.onClick.GetPersistentTarget(i);
                        // target 为 null = 场景引用丢失，也要接管，避免"点击无反应"
                        if (target != null && target != settings) continue;
                        button.onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
                        settingsToggle = true;
                        break;
                    }
                    if (!settingsToggle) continue;
                    if (button.transform.IsChildOf(settings.transform))
                        button.onClick.AddListener(CloseSettings);
                    else
                        button.onClick.AddListener(OpenSettings);
                }
        }

        static void DisableSetActiveListeners(Button button, GameObject settings)
        {
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                if (button.onClick.GetPersistentMethodName(i) != nameof(GameObject.SetActive)) continue;
                UnityEngine.Object target = button.onClick.GetPersistentTarget(i);
                if (target == null || target == settings)
                    button.onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
            }
        }

        bool HasPersistentOpenSettingsListener(Button button)
        {
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                if (button.onClick.GetPersistentTarget(i) == this
                    && button.onClick.GetPersistentMethodName(i) == nameof(OpenSettings))
                    return true;
            return false;
        }

        GameObject ResolveSettingsPanel()
        {
            if (settingsPanel != null && settingsPanel.scene == gameObject.scene) return settingsPanel;
            settingsPanel = FindInScene("GameSettingUI");
            return settingsPanel;
        }

        /// <summary>
        /// TitleController 组件上的滑块引用可能被编辑器误清空（sfxSlider/musicSlider = None）。
        /// 按 Slider.onValueChanged 上已接好的持久化回调把引用找回来，
        /// 保证设置面板音量页签和音量恢复逻辑不失效。
        /// </summary>
        void ResolveVolumeSliders()
        {
            if (sfxSlider != null && musicSlider != null) return;
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                foreach (Slider slider in root.GetComponentsInChildren<Slider>(true))
                    for (int i = 0; i < slider.onValueChanged.GetPersistentEventCount(); i++)
                    {
                        string method = slider.onValueChanged.GetPersistentMethodName(i);
                        if (method == nameof(SetSfxVolume)) sfxSlider = slider;
                        else if (method == nameof(SetMusicVolume)) musicSlider = slider;
                    }
        }

        public void OpenSettings()
        {
            GameObject settings = ResolveSettingsPanel();
            if (settings == null)
            {
                Debug.LogWarning("[TitleController] 未找到 GameSettingUI，设置面板无法打开");
                return;
            }
            if (!(settings.transform is RectTransform rect)) return;
            ConfigureTitleOverlay(rect, 110);
            // 旧标题场景里，外层与实际装饰、按钮所在的内容层可以分别失活。
            Transform content = rect.Find("GameObject");
            if (content != null) content.gameObject.SetActive(true);
            settings.SetActive(true);
            SetMainMenuInput(false);
            Debug.Log("[TitleController] 已打开设置面板");
        }

        public void CloseSettings()
        {
            GameObject settings = ResolveSettingsPanel();
            if (settings != null) settings.SetActive(false);
            SetMainMenuInput(true);
        }

        void BuildSettingsTabs()
        {
            GameObject settings = ResolveSettingsPanel();
            if (settings == null) return;
            RectTransform host = settings.transform.Find("GameObject") as RectTransform;
            if (host == null) host = (RectTransform)settings.transform;
            if (host.Find("Interface Settings") != null) return;

            var audioRows = new System.Collections.Generic.HashSet<RectTransform>();
            foreach (Text label in host.GetComponentsInChildren<Text>(true))
            {
                if (label.text.Contains("音效音量") || label.text.Contains("音乐音量"))
                    AddSettingsRow(host, label.rectTransform, audioRows);
            }
            if (sfxSlider != null) AddSettingsRow(host, (RectTransform)sfxSlider.transform, audioRows);
            if (musicSlider != null) AddSettingsRow(host, (RectTransform)musicSlider.transform, audioRows);
            foreach (RectTransform row in audioRows) row.anchoredPosition += Vector2.down * 78f;
            MakeSliderHitArea(sfxSlider);
            MakeSliderHitArea(musicSlider);
            Graphic frame = host.Find("RockFrame")?.GetComponent<Graphic>();
            if (frame != null) frame.raycastTarget = false;

            Text source = settings.GetComponentInChildren<Text>(true);
            Font font = source != null && source.font != null
                ? source.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Button audioTab = SettingsButton(host, "Audio Tab", "音量", -90f, 69f, 164f, font);
            Button interfaceTab = SettingsButton(host, "Interface Tab", "界面", 90f, 69f, 164f, font);
            RectTransform page = SettingsRect(host, "Interface Settings", 0f, 0f, 550f, 180f);
            SettingsLabel(page, "顶部操作提示", 0f, 5f, 460f, 28, font);
            Button hints = SettingsButton(page, "Control Hints", "", 0f, -53f, 260f, font);
            Text value = hints.GetComponentInChildren<Text>(true);
            void RefreshToggle() => value.text = ShowControlHints ? "显示 · 点击关闭" : "隐藏 · 点击开启";
            void SelectPage(bool showInterface)
            {
                EventSystem events = EventSystem.current;
                GameObject selectedObject = events != null ? events.currentSelectedGameObject : null;
                if (selectedObject != null && (showInterface
                    ? System.Linq.Enumerable.Any(audioRows, row => selectedObject.transform.IsChildOf(row))
                    : selectedObject.transform.IsChildOf(page)))
                    events.SetSelectedGameObject(null);

                if (!showInterface) page.gameObject.SetActive(false);
                foreach (RectTransform row in audioRows)
                {
                    row.gameObject.SetActive(!showInterface);
                    if (!showInterface) row.SetAsLastSibling();
                }
                if (showInterface) page.gameObject.SetActive(true);
                audioTab.transform.SetAsLastSibling();
                interfaceTab.transform.SetAsLastSibling();
                Color selected = new Color(0.23f, 0.38f, 0.24f);
                Color idle = new Color(0.30f, 0.26f, 0.20f);
                audioTab.image.color = showInterface ? idle : selected;
                interfaceTab.image.color = showInterface ? selected : idle;
            }
            audioTab.onClick.AddListener(() => SelectPage(false));
            interfaceTab.onClick.AddListener(() => SelectPage(true));
            hints.onClick.AddListener(() =>
            {
                ShowControlHints = !ShowControlHints;
                PlayerPrefs.SetInt(ControlHintsKey, ShowControlHints ? 1 : 0);
                PlayerPrefs.Save();
                RefreshToggle();
            });
            RefreshToggle();
            SelectPage(false);
        }

        static void MakeSliderHitArea(Slider slider)
        {
            if (slider == null) return;
            Graphic hitArea = slider.GetComponent<Graphic>();
            if (hitArea == null)
            {
                Image image = slider.gameObject.AddComponent<Image>();
                image.color = Color.clear;
                hitArea = image;
            }
            hitArea.raycastTarget = true;
            slider.transform.SetAsLastSibling();
        }

        static void AddSettingsRow(RectTransform host, RectTransform item,
            System.Collections.Generic.HashSet<RectTransform> rows)
        {
            while (item.parent != null && item.parent != host && item.parent is RectTransform parent)
                item = parent;
            if (item.parent == host) rows.Add(item);
        }

        static RectTransform SettingsRect(Transform parent, string name, float x, float y, float width, float height)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        static Text SettingsLabel(Transform parent, string caption, float x, float y, float width, int size, Font font)
        {
            Text label = SettingsRect(parent, "Label", x, y, width, 42f).gameObject.AddComponent<Text>();
            label.text = caption;
            label.font = font;
            label.fontSize = size;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(1f, 0.95f, 0.84f);
            label.raycastTarget = false;
            return label;
        }

        static Button SettingsButton(Transform parent, string name, string caption, float x, float y, float width, Font font)
        {
            RectTransform rect = SettingsRect(parent, name, x, y, width, 44f);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.23f, 0.38f, 0.24f);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            SettingsLabel(rect, caption, 0f, 0f, width, 23, font);
            return button;
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
                Debug.LogWarning("[TitleController] 未找到 ContinueGameUI 选槽面板，保留主菜单");
                return;
            }
            if (!(slotPanel.transform is RectTransform))
            {
                // 兼容仍打开着旧版标题场景的编辑器会话：沿用原来的槽位美术和按钮。
                Canvas legacy = slotPanel.GetComponentInChildren<Canvas>(true);
                if (legacy == null)
                {
                    Debug.LogWarning("[TitleController] 旧选槽面板缺少 Canvas，保留主菜单");
                    return;
                }
                slotPanel.SetActive(false);
                slotPanel.name = SaveSlotMenuView.PanelName + "_Legacy";
                slotPanel = new GameObject(SaveSlotMenuView.PanelName, typeof(RectTransform));
                slotPanel.SetActive(false);
                while (legacy.transform.childCount > 0)
                {
                    Transform content = legacy.transform.GetChild(0);
                    content.SetParent(slotPanel.transform, false);
                    Vector3 scale = content.localScale;
                    content.localScale = new Vector3(scale.x * 0.8f, scale.y * 0.8f, scale.z);
                }
            }
            // 标题有独立的封面 Canvas；普通同级顺序无法跨 Canvas 控制渲染层。
            ConfigureTitleOverlay((RectTransform)slotPanel.transform, 100);
            slotPanel.SetActive(true);
            if (!slotPanel.activeInHierarchy) return;
            Canvas.ForceUpdateCanvases();
            SetMainMenuInput(false);
            SaveSlotMenuView menu = slotPanel.GetComponent<SaveSlotMenuView>();
            if (menu == null) menu = slotPanel.AddComponent<SaveSlotMenuView>();
            menu.OnPanelOpened();
            Debug.Log("[TitleController] 继续游戏：打开选槽面板");
        }

        static void ConfigureTitleOverlay(RectTransform rect, int sortingOrder)
        {
            GameObject panel = rect.gameObject;
            rect.SetParent(null, false);
            rect.localScale = Vector3.one;
            Canvas canvas = panel.GetComponent<Canvas>();
            if (canvas == null) canvas = panel.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            CanvasScaler scaler = panel.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = panel.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            if (panel.GetComponent<GraphicRaycaster>() == null) panel.AddComponent<GraphicRaycaster>();
        }

        /// <summary>选槽面板的"返回"按钮：回到主菜单面板。</summary>
        public void BackToMain()
        {
            GameObject slotPanel = FindInScene("ContinueGameUI");
            GameObject startPanel = FindInScene("GameStartUI");
            if (slotPanel != null) slotPanel.SetActive(false);
            if (startPanel != null) startPanel.SetActive(true);
            SetMainMenuInput(true);
        }

        static void SetMainMenuInput(bool enabled)
        {
            GameObject main = FindInScene("GameStartUI");
            if (main == null) return;
            CanvasGroup group = main.GetComponent<CanvasGroup>();
            if (group == null) group = main.AddComponent<CanvasGroup>();
            group.interactable = enabled;
            group.blocksRaycasts = enabled;
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
    }
}
