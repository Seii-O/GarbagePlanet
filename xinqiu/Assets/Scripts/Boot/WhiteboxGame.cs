// @trace MECH-001, PLAT-001, PLAT-005
using PlanetCleaner;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 白膜装配与入口：固定顺序初始化各系统，管理 M / ESC / F1 / F5 / F9、暂停、存档与重开。
    /// 输入统一用旧版 Input Manager（与现有第一人称 Rig 一致，不混用两套输入方案）。
    /// 依赖方向：Boot → Gameplay / Station / Environment / UI → Data → Core。
    /// </summary>
    public sealed class WhiteboxGame : MonoBehaviour
    {
        [Header("数据")]
        [SerializeField] WhiteboxConfig config;

        [Header("场景引用（由 WhiteboxBuilder 写入）")]
        [SerializeField] Transform planetCenter;
        [SerializeField] Transform player;
        [SerializeField] Camera viewCamera;
        [SerializeField] Renderer ground;
        [SerializeField] Renderer water;
        [SerializeField] Light sun;
        [SerializeField] Transform station;

        [Header("系统引用（由 WhiteboxBuilder 写入）")]
        [SerializeField] TrashSpawner spawner;
        [SerializeField] InteractionController interaction;
        [SerializeField] CompanionDog dog;
        [SerializeField] StationSystem stationSystem;
        [SerializeField] QuestChain quest;
        [SerializeField] EcoStage eco;
        [SerializeField] WhiteboxHud hud;

        [Header("中转站交互半径")]
        [Tooltip("仅用于非星球场景（无 planetCenter）的兼容判定；星球场景内 M 键可在任意位置打开中转站。")]
        [SerializeField] float stationRange = 14f; // 字段名保持不变，场景里序列化的 14 不会丢

        WhiteboxState state;
        MonoBehaviour playerMovement;
        Vector3 lastPlayerPos;
        float saveTimer;
        bool paused;
        bool stationOpen;

        public WhiteboxConfig Config => config;
        public WhiteboxState State => state;
        public Transform PlanetCenter => planetCenter;
        public Transform Player => player;
        public Camera ViewCamera => viewCamera;
        public Renderer Ground => ground;
        public Renderer Water => water;
        public Light Sun => sun;
        public Transform Station => station;
        public TrashSpawner Spawner => spawner;
        public InteractionController Interaction => interaction;
        public CompanionDog Dog => dog;
        public StationSystem StationLogic => stationSystem;
        public QuestChain Quest => quest;
        public EcoStage Eco => eco;
        public WhiteboxHud Hud => hud;
        public bool IsPaused => paused;
        public bool StationOpen => stationOpen;
        public bool IsUiBlocking => paused || stationOpen || (state != null && state.finished);

        // 供 Editor 构建脚本注入引用
        public void Wire(WhiteboxConfig cfg, Transform center, Transform playerT, Camera cam,
            Renderer groundR, Renderer waterR, Light sunL, Transform stationT,
            TrashSpawner sp, InteractionController ic, CompanionDog d,
            StationSystem ss, QuestChain q, EcoStage e, WhiteboxHud h)
        {
            config = cfg; planetCenter = center; player = playerT; viewCamera = cam;
            ground = groundR; water = waterR; sun = sunL; station = stationT;
            spawner = sp; interaction = ic; dog = d; stationSystem = ss;
            quest = q; eco = e; hud = h;
        }

        void Awake()
        {
            state = WhiteboxState.Load();
            if (state.finished) state = new WhiteboxState(); // 已结算的存档不自动续上，避免验证时一进 Play 就是结算面板
            if (player != null) playerMovement = player.GetComponent<PlayerController>();
            if (player != null) lastPlayerPos = player.position;
            Time.timeScale = 1f;
        }

        void Start()
        {
            if (config == null)
            {
                Debug.LogError("[WhiteboxGame] 缺少 WhiteboxConfig，先跑 Tools/垃圾星球白膜/① 生成场景与预制体");
                enabled = false;
                return;
            }

            // 固定初始化顺序：状态 → 表现 → 系统 → 视图 → 任务
            eco.Init(this);
            hud.Init(this);
            spawner.Init(this);
            interaction.Init(this);
            dog.Init(this);
            stationSystem.Init(this);
            quest.Init(this);
            quest.Evaluate();
            ApplyUiState();

            // 运行时确保立体感：压低环境光，让主光产生清晰的明暗梯度（避免扁平化）；
            // 场景里另有冷色补光与轮廓光（由 BuildSceneContents / 菜单 ⑦ 写入），二者叠加给出体积与纵深。
            RenderSettings.ambientLight = new Color(0.30f, 0.30f, 0.34f);

            Debug.Log("[WhiteboxGame] 启动完成：半径=" + config.PlanetRadius
                      + "，垃圾=" + spawner.Active.Count + "，阶段=" + (eco.Stage + 1)
                      + "，Unity=" + Application.unityVersion);
        }

        void Update()
        {
            if (config == null || state == null) return;

            if (Input.GetKeyDown(KeyCode.F1)) hud.DebugVisible = !hud.DebugVisible;
            if (Input.GetKeyDown(KeyCode.F9)) WhiteboxState.Save(state);
            // 重开优先且独占本帧：命中后不再处理任何面板输入，避免场景卸载前又开/关一次面板。
            if (Input.GetKeyDown(KeyCode.F5)) { Restart(); return; }

            // 策划案 §6.4：同一帧只取一个输入，优先级 OpenStation > Pause。
            // 原来 M 与 ESC 是两段独立 if，同帧按下会「开→关」互相抵消，属于输入冲突。
            bool openStationKey = Input.GetKeyDown(KeyCode.M);
            bool pauseKey = Input.GetKeyDown(KeyCode.Escape);
            if (openStationKey) ToggleStation();
            else if (pauseKey)
            {
                if (stationOpen) ToggleStation();
                else SetPaused(!paused);
            }

            float dt = Time.deltaTime;
            if (!IsUiBlocking && dt > 0f)
            {
                state.playTime += dt;
                if (player != null)
                {
                    state.movedDistance += Vector3.Distance(lastPlayerPos, player.position);
                    lastPlayerPos = player.position;
                }
                saveTimer += dt;
                if (saveTimer > 15f) { saveTimer = 0f; WhiteboxState.Save(state); }
                quest.Evaluate();
            }
        }

        // 提供给 Hierarchy UI 按钮直接绑定的入口（WhiteboxUiActions.CloseStation）
        // M / ESC / 关闭按钮的形式与调用链完全不变，这里只调整「打开」的前置条件。
        public void ToggleStation()
        {
            if (state.finished || paused) return;
            if (!stationOpen)
            {
                // 星球场景：任意位置都能打开（回归策划案 §6.2「任意 → OpenStation(M) 无条件」）。
                // 非星球场景（无 planetCenter，如平面/测试场景）沿用原来的距离判定，
                // 保持其既有逻辑不被本次改动波及。
                bool planetScene = planetCenter != null;
                if (!planetScene && station != null && player != null &&
                    Vector3.Distance(station.position, player.position) > stationRange)
                {
                    Notice("先走回中转站（" + stationRange.ToString("0") + " m 内）再按 M");
                    return;
                }
                state.visitedStation = true; // 任务链第 4 步（QuestChain case 3）依赖，保持不变
                stationOpen = true;
                Notice("中转站已打开；后台加工/制造计时继续走");
            }
            else stationOpen = false; // 再按一次 M / ESC / 关闭按钮只关闭，不会重复打开
            ApplyUiState();
            quest.Evaluate();
        }

        public void SetPaused(bool value)
        {
            if (state != null && state.finished) return;
            paused = value;
            if (paused) stationOpen = false;
            ApplyUiState();
        }

        public void ApplyUiState()
        {
            bool block = IsUiBlocking;
            if (playerMovement != null) playerMovement.enabled = !block;
            Cursor.lockState = block ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = block;
            Time.timeScale = paused ? 0f : 1f;
        }

        public void OnWorldEvent()
        {
            if (quest != null) quest.Evaluate();
            if (eco != null) eco.Refresh(true);
        }

        public void Notice(string text)
        {
            if (hud != null) hud.Notice(text);
        }

        public void Restart()
        {
            WhiteboxState.Delete();
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        void OnApplicationQuit()
        {
            if (state != null) WhiteboxState.Save(state);
        }

        void OnDisable()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
