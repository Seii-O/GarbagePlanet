// @trace MECH-001, PLAT-001, PLAT-005, TOOL-001, BAG-001, COIN-001, UI-004, SAVE-001, SAVE-002, ART-013
using PlanetCleaner;
using UnityEngine;
using UnityEngine.Rendering;
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
        /// <summary>唯一正式关卡场景（Build Settings 第 0 位）；启动与重开都指向它。</summary>
        public const string GameSceneName = "WhiteboxPrototype";

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
        [SerializeField] GachaSystem gacha; // 新增玩法：转盘（由 GachaBuilder 写入）
        [SerializeField] ToolSystem tool;   // 工具系统（运行时自动补建，无需改场景 YAML）
        [SerializeField] ToolBagSystem bag;       // 独立工具/技能书背包（I 键，运行时补建）
        [SerializeField] CoinTradeSystem coinTrade;// 独立金币交易（B 键，运行时补建）
        [SerializeField] InGameSaveView inGameSave;  // 局内存档面板（F6，运行时补建）
        [SerializeField] SceneryDecorSystem scenery; // 场景装饰生态联动（ART-013，由 Builder 写入）
        [SerializeField] PurificationSystem purification; // 星球净化区（MECH-011，运行时补建/Builder 写入）

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
        public GachaSystem Gacha => gacha;
        public ToolSystem Tool => tool;
        public ToolBagSystem Bag => bag;
        public CoinTradeSystem CoinTrade => coinTrade;
        public InGameSaveView InGameSave => inGameSave;
        public SceneryDecorSystem Scenery => scenery;
        public PurificationSystem Purification => purification;
        public bool IsPaused => paused;
        public bool StationOpen => stationOpen;
        public bool IsUiBlocking => paused || stationOpen || (gacha != null && gacha.IsOpen)
                                    || (bag != null && bag.IsOpen) || (coinTrade != null && coinTrade.IsOpen)
                                    || (inGameSave != null && inGameSave.IsOpen)
                                    || (state != null && state.finished);

        /// <summary>
        /// 场景里是否已生成「数量选择弹窗」。旧场景（UI 尚未补齐）时为 false，
        /// 此时加工/制造按钮退化为「一键全部」，保证批量功能不会因为缺 UI 而丢失。
        /// </summary>
        public bool HasQuantityDialog =>
            hud != null && hud.CanvasView != null && hud.CanvasView.QuantityDialog != null;

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

        /// <summary>单独注入转盘系统（GachaBuilder 生成后调用，不改上面 Wire 的签名）。</summary>
        public void WireGacha(GachaSystem g) { gacha = g; }

        /// <summary>单独注入工具系统（ToolBuilder / GachaBuilder 生成后调用）。</summary>
        public void WireTool(ToolSystem t) { tool = t; }

        /// <summary>单独注入独立背包（InventoryBuilder 生成后调用）。</summary>
        public void WireBag(ToolBagSystem b) { bag = b; }

        /// <summary>单独注入金币交易（InventoryBuilder 生成后调用）。</summary>
        public void WireCoinTrade(CoinTradeSystem c) { coinTrade = c; }

        /// <summary>单独注入局内存档面板（SaveSlotBuilder 生成后调用）。</summary>
        public void WireInGameSave(InGameSaveView v) { inGameSave = v; }

        /// <summary>单独注入场景装饰联动系统（SceneryDecorBuilder 生成后调用）。</summary>
        public void WireScenery(SceneryDecorSystem s) { scenery = s; }

        /// <summary>单独注入星球净化系统（PurificationBuilder 生成后调用）。</summary>
        public void WirePurification(PurificationSystem p) { purification = p; }

        void Awake()
        {
            state = WhiteboxState.Load();
            if (state.finished) state = new WhiteboxState(); // 已结算的存档不自动续上，避免验证时一进 Play 就是结算面板
            ApplyPlanetSize();
            if (player != null) playerMovement = player.GetComponent<PlayerController>();
            if (player != null) lastPlayerPos = player.position;

            // 多槽存档：注册"保存前写位置/视角"回调；进场景后还原上次存档的玩家位置。
            SaveSlotSystem.CapturePoseHook = CapturePose;
            RestorePose();

            Time.timeScale = 1f;
        }

        /// <summary>把当前玩家位置与相机旋转写进 state，随下一次 Save 一起落盘。</summary>
        void CapturePose(WhiteboxState s)
        {
            if (s == null || player == null) return;
            s.playerPosX = player.position.x;
            s.playerPosY = player.position.y;
            s.playerPosZ = player.position.z;
            s.playerRotX = player.rotation.x;
            s.playerRotY = player.rotation.y;
            s.playerRotZ = player.rotation.z;
            s.playerRotW = player.rotation.w;
            if (viewCamera != null)
            {
                Quaternion c = viewCamera.transform.rotation;
                s.camRotX = c.x; s.camRotY = c.y; s.camRotZ = c.z; s.camRotW = c.w;
            }
            s.hasPlayerPose = true;
        }

        static bool IsValidRotation(Quaternion q)
        {
            return Mathf.Abs(q.x) + Mathf.Abs(q.y) + Mathf.Abs(q.z) + Mathf.Abs(q.w) > 0.0001f;
        }

        /// <summary>还原位置：投影回球面后交给 PlayerController / CameraRig 各自对齐。</summary>
        void RestorePose()
        {
            if (state == null || !state.hasPlayerPose || player == null) return;
            Vector3 saved = new Vector3(state.playerPosX, state.playerPosY, state.playerPosZ);
            if (saved.sqrMagnitude < 0.0001f) return;
            player.position = saved;
            Quaternion pr = new Quaternion(state.playerRotX, state.playerRotY, state.playerRotZ, state.playerRotW);
            if (IsValidRotation(pr)) player.rotation = pr;
            if (viewCamera != null)
            {
                Quaternion cr = new Quaternion(state.camRotX, state.camRotY, state.camRotZ, state.camRotW);
                if (IsValidRotation(cr)) viewCamera.transform.rotation = cr;
            }
            // 位置/旋转已就位；PlayerController.Start 的 AlignToShell 会把位置投影回球壳、
            // 并按当前半径重建 player.rotation（保留 forward 方向）。不要在 Awake 调 SnapToSurface，
            // 它会用固定的 Vector3.forward 覆盖我们刚还原的朝向。
            if (playerMovement is PlayerController pc) pc.ResolvePlanetGeometry();
            lastPlayerPos = player.position;
            Debug.Log("[WhiteboxGame] 已从存档还原玩家位置 " + player.position);
        }

        /// <summary>
        /// 星球半径以配置为唯一真值：场景里烘焙的体积若与配置不一致，
        /// 就在这里按配置重设星球与地表道具，避免垃圾/玩家按新半径刷在旧球里外。
        /// 必须在所有 Start 之前完成 —— PlayerController 会在 Start 里量测真实半径。
        /// </summary>
        void ApplyPlanetSize()
        {
            if (config == null || planetCenter == null) return;
            float target = config.PlanetRadius;
            if (!PlanetLayout.Apply(planetCenter, target)) return;

            PlayerController pc = player != null ? player.GetComponent<PlayerController>() : null;
            if (pc != null)
            {
                pc.PlanetRadius = target;               // 兜底值同步（量测失败时才会用到）
                if (pc.PlanetCenter != null) pc.ResolvePlanetGeometry(); // 立刻按新体积重量测，避免 Start 之前的帧用旧壳距
            }
            Debug.Log("[WhiteboxGame] 星球半径按配置重设为 " + target
                      + " m（地表道具已同步换位）");
        }

        void Start()
        {
            if (config == null)
            {
                Debug.LogError("[WhiteboxGame] 缺少 WhiteboxConfig，先跑 Tools/垃圾星球白膜/① 生成场景与预制体");
                enabled = false;
                return;
            }

            // 固定初始化顺序：状态 → 工具 → 背包 → 金币交易 → 表现 → 系统 → 视图 → 任务
            // ToolSystem 必须在 interaction / stationSystem / eco / gacha 之前可用；
            // 背包在工具之后（读 ToolConfig），金币交易在背包之后（拿 IToolBag 引用）。
            if (tool == null) tool = gameObject.AddComponent<ToolSystem>();
            tool.Init(this);
            if (bag == null) bag = gameObject.AddComponent<ToolBagSystem>();
            bag.Init(this);
            if (coinTrade == null) coinTrade = gameObject.AddComponent<CoinTradeSystem>();
            coinTrade.Init(this);
            eco.Init(this);
            // 净化区必须在 spawner 之前 Init：初始撒点要靠 IsPurified 排除中转站周围已净化土地。
            // 旧场景无 Builder 引用时补建，组件内部会退回 game.Station 作为唯一中心。
            if (purification == null) purification = gameObject.AddComponent<PurificationSystem>();
            purification.Init(this);
            if (scenery == null) scenery = gameObject.AddComponent<SceneryDecorSystem>(); // 旧场景无 Builder 引用时补建
            scenery.Init(this); // 装饰联动：只读 eco.Stage 驱动视觉反馈，不写任何玩法/生态数值
            hud.Init(this);
            spawner.Init(this);
            interaction.Init(this);
            dog.Init(this);
            stationSystem.Init(this);
            quest.Init(this);
            quest.Evaluate();
            if (gacha != null) gacha.Init(this); // 新增玩法：转盘系统
            ApplyUiState();

            ConfigureBrightEnvironmentLighting();

            Debug.Log("[WhiteboxGame] 启动完成：半径=" + config.PlanetRadius
                      + "，垃圾=" + spawner.Active.Count + "，阶段=" + (eco.Stage + 1)
                      + "，Unity=" + Application.unityVersion);
        }

        /// <summary>
        /// 使用全局环境光照亮整颗星球，移除会造成局部过曝/远处发黑的点光源。
        /// 方向光只负责保留低模表面的明暗层次，不承担暗部填充。
        /// </summary>
        void ConfigureBrightEnvironmentLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.58f, 0.56f, 0.52f, 1f);
            RenderSettings.ambientIntensity = 1.10f;
            RenderSettings.reflectionIntensity = 0.85f;

            Light[] lights = FindObjectsOfType<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (light == null) continue;
                if (light.type == LightType.Point)
                {
                    light.enabled = false;
                    continue;
                }

                if (light == sun) light.intensity = 1.10f;
                else if (light.name == "env_fill_light") light.intensity = 0.30f;
                else if (light.name == "env_rim_light") light.intensity = 0.20f;
            }
        }

        void Update()
        {
            if (config == null || state == null) return;

            if (Input.GetKeyDown(KeyCode.F1)) hud.DebugVisible = !hud.DebugVisible;
            // 重开优先且独占本帧：命中后不再处理任何面板输入，避免场景卸载前又开/关一次面板。
            if (Input.GetKeyDown(KeyCode.F5)) { Restart(); return; }
            // F9：打开局内存档面板（可选任意槽）；没有面板时退回"保存到当前激活槽"。
            if (Input.GetKeyDown(KeyCode.F9))
            {
                if (inGameSave != null) ToggleInGameSave();
                else { WhiteboxState.Save(state); Notice(SaveSlotSystem.ActiveSlot > 0 ? "已存档" : "未选择存档槽"); }
            }

            // 策划案 §6.4 / UI-004：同一帧只取一个输入，互斥优先级
            // 背包(I) > 交易(B) > 中转站(M) > 暂停(ESC) > 专精(Q)。
            // 转盘不再有独立快捷键：只由场景内老虎机在准星对准、按 F 时经 InteractionController 弹出。
            // 抽奖面板与其它面板仍走同一套互斥（OpenGachaFromMachine → ClosePanelsExcept）。
            bool bagKey = Input.GetKeyDown(KeyCode.I);
            bool tradeKey = Input.GetKeyDown(KeyCode.B);
            bool stationKey = Input.GetKeyDown(KeyCode.M);
            bool pauseKey = Input.GetKeyDown(KeyCode.Escape);
            bool specKey = Input.GetKeyDown(KeyCode.Q);
            if (bagKey)
            {
                if (!state.finished && !paused && bag != null) ToggleBag();
            }
            else if (tradeKey)
            {
                if (!state.finished && !paused && coinTrade != null) ToggleCoinTrade();
            }
            else if (stationKey)
            {
                ClosePanelsExcept(null);
                ToggleStation();
            }
            else if (pauseKey)
            {
                // ESC 逐层退出：先关最上层面板，都关完才暂停
                if (bag != null && bag.IsOpen) bag.Close();
                else if (coinTrade != null && coinTrade.IsOpen) coinTrade.Close();
                else if (gacha != null && gacha.IsOpen) gacha.Close();
                else if (inGameSave != null && inGameSave.IsOpen) inGameSave.Close();
                else if (stationOpen) ToggleStation();
                else SetPaused(!paused);
            }
            else if (specKey)
            {
                if (!state.finished && !IsUiBlocking && tool != null) tool.CycleActive();
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

        // ------------------------------------------------------------------
        // 面板互斥开合（UI-004）：背包 / 金币交易 / 转盘 / 中转站
        // ------------------------------------------------------------------

        /// <summary>除 keep 外的所有面板都关闭。keep=null 表示全关，用于打开中转站/暂停前清场。</summary>
        void ClosePanelsExcept(string keep)
        {
            if (keep != "bag" && bag != null && bag.IsOpen) bag.Close();
            if (keep != "trade" && coinTrade != null && coinTrade.IsOpen) coinTrade.Close();
            if (keep != "gacha" && gacha != null && gacha.IsOpen) gacha.Close();
            if (keep != "save" && inGameSave != null && inGameSave.IsOpen) inGameSave.Close();
            if (keep != "station" && stationOpen) { stationOpen = false; if (stationSystem != null) stationSystem.ClearPending(); }
        }

        /// <summary>F9：局内存档面板开关。打开时先关其他面板；结算态不允许打开。</summary>
        public void ToggleInGameSave()
        {
            if (inGameSave == null || state == null || state.finished) return;
            bool opening = !inGameSave.IsOpen;
            ClosePanelsExcept(opening ? "save" : null);
            if (opening) inGameSave.Open();
            else inGameSave.Close();
            ApplyUiState();
        }

        /// <summary>
        /// 场景老虎机 F 交互的打开入口（由 InteractionController → SlotMachine.Interact 转调）。
        /// 带面板互斥：打开抽奖面板前先关掉背包/交易/存档/中转站；结算态与暂停态不响应。
        /// 面板已开时不再重复打开（此时关闭走 ESC / 面板的关闭按钮）。
        /// </summary>
        public void OpenGachaFromMachine()
        {
            if (state == null || state.finished || paused || gacha == null || gacha.IsOpen) return;
            ClosePanelsExcept("gacha");
            gacha.Open();
            ApplyUiState();
        }

        /// <summary>I 键：背包独立开关。打开时关掉交易/转盘/中转站，保证一次只有一个面板。</summary>
        public void ToggleBag()
        {
            if (state == null || state.finished || paused || bag == null) return;
            bool opening = !bag.IsOpen;
            ClosePanelsExcept("bag");
            if (opening) bag.Open();
            ApplyUiState();
        }

        /// <summary>B 键：金币交易独立开关。与背包完全分离，打开时关掉背包/转盘/中转站。</summary>
        public void ToggleCoinTrade()
        {
            if (state == null || state.finished || paused || coinTrade == null) return;
            bool opening = !coinTrade.IsOpen;
            ClosePanelsExcept("trade");
            if (opening) coinTrade.Open();
            ApplyUiState();
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
            else
            {
                stationOpen = false; // 再按一次 M / ESC / 关闭按钮只关闭，不会重复打开
                if (stationSystem != null) stationSystem.ClearPending(); // 关面板即放弃待确认的批量操作
            }
            ApplyUiState();
            quest.Evaluate();
        }

        public void SetPaused(bool value)
        {
            if (state != null && state.finished) return;
            paused = value;
            if (paused)
            {
                stationOpen = false;
                if (stationSystem != null) stationSystem.ClearPending();
            }
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
            if (purification != null) purification.Refresh(); // 进度推进后即时回收净化区内垃圾、扩张边界
        }

        public void Notice(string text)
        {
            if (hud != null) hud.Notice(text);
        }

        public void Restart()
        {
            WhiteboxState.Delete();
            Time.timeScale = 1f;
            // 始终加载正式关卡场景，而不是"当前场景名"——
            // 移除开始界面后唯一入口就是本关，重开必须稳定回到它，
            // 避免误重载到废案/空场景（如 _Game/Whitebox、SampleScene）导致黑屏或错关。
            SceneManager.LoadScene(GameSceneName);
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
            if (ReferenceEquals(SaveSlotSystem.CapturePoseHook, (System.Action<WhiteboxState>)CapturePose))
                SaveSlotSystem.CapturePoseHook = null;
        }
    }
}
