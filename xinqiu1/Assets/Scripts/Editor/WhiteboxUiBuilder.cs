// @trace ART-004, ART-008, MECH-013
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 把白膜 UI 生成成 Hierarchy 里真实可编辑的 uGUI 对象。
    ///
    /// 红线：
    /// 1. 全部用 Editor API 建对象，不手写 .unity / .prefab YAML；
    /// 2. 幂等且不覆盖用户格式 —— UI 根已存在时只补「空引用 / 缺失组件 / 空按钮绑定」，
    ///    RectTransform、颜色、字体、字号、文案一律不碰；
    /// 3. 不重建场景、不动玩家、不动相机下的 Hands（HandLeft / HandRight 保持原样）；
    /// 4. 事件系统用 StandaloneInputModule，因为工程 activeInputHandler=0（旧 Input Manager）。
    ///
    /// 菜单：Tools/垃圾星球白膜/④ 生成 / 补齐 Hierarchy UI
    /// 首次导入脚本后，若当前打开的正是白膜场景，会自动补一次并保存。
    /// </summary>
    [InitializeOnLoad]
    public static class WhiteboxUiBuilder
    {
        public const string RootName = "UI_Whitebox";
        // v2：新增「数量选择弹窗 + ⑤ 升级背包」区块。改版本号 = 让已存在的编辑器会话在脚本重载后
        // 也重新走一次补齐（SessionState 跨域重载保持，不换 key 就永远不再补）。
        const string SessionKey = "TrashPlanet.Whitebox.Ui.AutoBuilt.v2";
        const string Tag = "[WhiteboxUiBuilder] ";

        static readonly Color PanelBg = new Color(0.09f, 0.11f, 0.12f, 0.93f);
        static readonly Color BarBg = new Color(0.13f, 0.15f, 0.16f, 0.95f);
        static readonly Color BtnBg = new Color(0.20f, 0.26f, 0.27f, 0.97f);
        static readonly Color TextColor = new Color(0.93f, 0.94f, 0.91f, 1f);
        static readonly Color Accent = new Color(0.24f, 0.84f, 0.75f, 1f);
        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
        static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        static readonly Vector2 BottomLeft = new Vector2(0f, 0f);

        static WhiteboxUiBuilder()
        {
            EditorApplication.delayCall += AutoEnsure;
            // 编辑器可能正处于 Play 模式或还没编译完：持续轮询，等条件满足再补 UI，
            // 避免「只注册一次 delayCall，错过时机就再也不生成」。
            EditorApplication.update += AutoEnsure;
            // UI 层级变化后，若 Play 冒烟报告比场景旧，自动复验一次运行时
            EditorApplication.update += AutoSmokeAfterUi;
        }

        static void AutoSmokeAfterUi()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != WhiteboxBuilder.ScenePath) return;
            if (FindRoot(scene, RootName) == null) return;

            // 每次域重载（脚本改动后）刷新一次编辑器内校验报告，避免报告停留在旧代码状态
            WhiteboxValidator.Validate();

            string report = Path.Combine(Application.dataPath, "../docs/whitebox-play-smoke.json");
            string sceneFile = Path.Combine(Application.dataPath, "../" + WhiteboxBuilder.ScenePath);
            bool fresh = File.Exists(report) && File.Exists(sceneFile)
                         && File.GetLastWriteTimeUtc(report) >= File.GetLastWriteTimeUtc(sceneFile);
            if (fresh)
            {
                // 报告存在但上次判为失败时，修复后也要复验，不能让失败报告把复验挡住
                try { fresh = File.ReadAllText(report).Contains("\"pass\": true"); }
                catch (Exception) { fresh = false; }
            }
            EditorApplication.update -= AutoSmokeAfterUi; // 只判定一次，避免反复进 Play
            if (fresh) return;
            WhiteboxPlaySmoke.RequestRun();
        }

        static void AutoEnsure()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (SessionState.GetBool(SessionKey, false))
            {
                EditorApplication.update -= AutoEnsure;
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != WhiteboxBuilder.ScenePath) return; // 不在白膜场景：等菜单 ④

            SessionState.SetBool(SessionKey, true);
            EditorApplication.update -= AutoEnsure;
            bool changed = EnsureUiInScene(scene);
            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log(Tag + "已把可编辑 uGUI 面板写入 Hierarchy（" + RootName
                          + "），场景已保存；玩家双手与相机保持原样");
            }
        }

        /// <summary>
        /// 批处理入口（-executeMethod），供 CI / 命令行把新增区块写进已存在的白膜场景。
        /// 不弹任何对话框，成功与否只看日志与 docs 报告。
        /// </summary>
        public static void EnsureBatch()
        {
            Scene scene = EditorSceneManager.OpenScene(WhiteboxBuilder.ScenePath, OpenSceneMode.Single);
            bool changed = EnsureUiInScene(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log(Tag + "batch 补齐完成，changed=" + changed);
        }

        [MenuItem("Tools/垃圾星球白膜/④ 生成 / 补齐 Hierarchy UI", false, 13)]
        public static void MenuEnsure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning(Tag + "请先退出 Play 模式");
                return;
            }
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != WhiteboxBuilder.ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(WhiteboxBuilder.ScenePath, OpenSceneMode.Single);
            }
            bool created = FindRoot(scene, RootName) == null;
            bool changed = EnsureUiInScene(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log(Tag + (created ? "已生成完整 UI 层级" : "已补齐绑定，未改动你调整过的布局"));
            EditorUtility.DisplayDialog("Hierarchy UI",
                (created
                    ? "已生成 " + RootName + "：准星 / 调试 HUD / 中转站 / 暂停 / 结算 / 浮字 / 阶段白光，以及 EventSystem。\n\n"
                    : "UI 层级已存在：只补了空引用与空按钮绑定，你改过的布局、字体、颜色、文案全部保留。\n\n")
                + "在 Hierarchy 里展开 " + RootName + " 即可直接改格式；运行时组件只写文字和显隐。", "好");
        }

        /// <summary>
        /// 在指定场景里确保 UI 层级与绑定存在。返回是否需要保存场景。
        /// UI 根已存在时只补空引用，不覆盖用户改过的任何格式。
        /// </summary>
        public static bool EnsureUiInScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return false;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return false;

            bool changed = false;
            WhiteboxGame game = FindComponent<WhiteboxGame>(scene);
            GameObject ui = FindRoot(scene, RootName);
            bool created = ui == null;
            if (created) { ui = CreateUiHierarchy(scene, game); changed = true; }

            if (ui.GetComponent<WhiteboxCanvasHud>() == null) { ui.AddComponent<WhiteboxCanvasHud>(); changed = true; }
            if (ui.GetComponent<WhiteboxUiActions>() == null) { ui.AddComponent<WhiteboxUiActions>(); changed = true; }
            if (FindRoot(scene, "EventSystem") == null) { CreateEventSystem(scene); changed = true; }

            WhiteboxHud hud = game != null ? game.GetComponent<WhiteboxHud>() : null;
            WhiteboxCanvasHud view = ui.GetComponent<WhiteboxCanvasHud>();
            WhiteboxUiActions actions = ui.GetComponent<WhiteboxUiActions>();

            if (created)
            {
                view.Game = game;
                view.Proxy = hud;
                actions.Game = game;
                if (hud != null) hud.BindCanvas(view);
            }
            else
            {
                if (view.Game == null && game != null) { view.Game = game; changed = true; }
                if (view.Proxy == null && hud != null) { view.Proxy = hud; changed = true; }
                if (actions.Game == null && game != null) { actions.Game = game; changed = true; }
                if (hud != null && hud.CanvasView == null) { hud.BindCanvas(view); changed = true; }
            }

            // 按名字补空引用；已绑定的非空引用保持不变
            changed |= EnsureStationExtras(ui.transform, game);
            view.EditorResolve();
            changed |= EnsureButtonBindings(ui.transform, actions);

            EditorUtility.SetDirty(view);
            EditorUtility.SetDirty(actions);
            if (hud != null) EditorUtility.SetDirty(hud);
            return changed;
        }

        // ---------------------------------------------------------------
        // 层级生成
        // ---------------------------------------------------------------

        static GameObject CreateUiHierarchy(Scene scene, WhiteboxGame game)
        {
            Font font = LoadFont();
            WhiteboxConfig cfg = AssetDatabase.LoadAssetAtPath<WhiteboxConfig>(WhiteboxBuilder.ConfigPath);
            Color accent = cfg != null ? cfg.HighlightColor : Accent;
            Color warn = cfg != null ? cfg.BagWarningColor : new Color(0.89f, 0.38f, 0.24f, 1f);
            Color durability = cfg != null ? cfg.DurabilityColor : new Color(0.95f, 0.76f, 0.31f, 1f);

            var root = new GameObject(RootName, typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            PutInScene(root, scene);

            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            Transform r = root.transform;

            // ① 常驻准星（策划案 §11：正式版常驻 UI 只有准星）
            RectTransform cross = Rect(r, "Crosshair", Center, Center, new Vector2(6f, 6f), Vector2.zero);
            AddImage(cross, accent, false);

            // ② 背包 >80% 呼吸边框：CanvasGroup 控 alpha，4 条细边
            RectTransform bag = Stretch(r, "BagWarning");
            CanvasGroup bagGroup = bag.gameObject.AddComponent<CanvasGroup>();
            bagGroup.alpha = 0f;
            bagGroup.interactable = false;
            bagGroup.blocksRaycasts = false;
            Edge(bag, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 6f), new Vector2(0f, -3f), warn);
            Edge(bag, "Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 6f), new Vector2(0f, 3f), warn);
            Edge(bag, "Left", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(6f, 0f), new Vector2(3f, 0f), warn);
            Edge(bag, "Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(6f, 0f), new Vector2(-3f, 0f), warn);
            bag.gameObject.SetActive(false);

            // ③ 大型垃圾耐久条
            GameObject durBar = Panel(r, "DurabilityBar", new Vector2(240f, 14f),
                new Vector2(0f, -56f), Center, Center, BarBg);
            FilledBar(durBar.transform, "DurabilityFill", durability);
            durBar.SetActive(false);

            // ④ 拾取浮字容器 + 模板（改模板等于改所有浮字样式）
            RectTransform floats = Rect(r, "FloatsRoot", Center, Center, new Vector2(420f, 320f), Vector2.zero);
            Txt(floats, "Txt_FloatTemplate", "+1", Center, Center, new Vector2(200f, 34f),
                new Vector2(28f, -36f), 22, accent, TextAnchor.MiddleLeft, font).gameObject.SetActive(false);

            // ⑤ 调试 HUD（F1）—— 编辑器里默认可见，方便直接改格式
            GameObject debug = Panel(r, "Hud_Debug", new Vector2(390f, 268f),
                new Vector2(24f, -24f), TopLeft, TopLeft, PanelBg);
            TopLabel(debug.transform, "Txt_Title", "白膜调试信息（F1 隐藏）", 10f, 358f, 18, accent, font);
            TopLabel(debug.transform, "Txt_Bag", "背包 塑/金/有/电: 0/0/0/0", 42f, 358f, 15, TextColor, font);
            TopLabel(debug.transform, "Txt_Materials", "材料 板/金/肥/芯: 0/0/0/0", 70f, 358f, 15, TextColor, font);
            TopLabel(debug.transform, "Txt_Tool", "工具 Lv.1 · 拾取 -- m · 地图 0/50", 98f, 358f, 15, TextColor, font);
            TopLabel(debug.transform, "Txt_Progress", "土地 0% · 水源 0% · 生态阶段 1/3", 126f, 358f, 15, TextColor, font);
            TopLabel(debug.transform, "Txt_Quest", "任务 1/10 ： --", 154f, 358f, 15, TextColor, font);
            TopLabel(debug.transform, "Txt_Stats", "清理 0 · 加工 0 · 制造 0 · 用时 0 s", 182f, 358f, 15, TextColor, font);
            TopLabel(debug.transform, "Txt_DebugHint", "面板格式可直接在这里拖；代码只改文字", 216f, 358f, 13,
                new Color(0.62f, 0.68f, 0.68f, 1f), font);

            // ⑥ 中转站面板（M）
            GameObject station = Panel(r, "Panel_Station", new Vector2(580f, 690f),
                Vector2.zero, Center, Center, PanelBg);
            float w = 548f;
            TopLabel(station.transform, "Txt_StationTitle", "中转站 2D 经营  ·  M 或 ESC 关闭", 12f, w, 20, accent, font);
            TopLabel(station.transform, "Txt_Repaired", "已修复: 否（先造 1 块再生建材板）", 46f, w, 15, TextColor, font);
            TopButton(station.transform, "Btn_Deposit", "① 投放背包全部垃圾", 76f, w, 36f, font);
            TopLabel(station.transform, "Txt_Raw", "待加工垃圾  塑/金/有/电: 0/0/0/0", 120f, w, 15, TextColor, font);
            for (int i = 0; i < 4; i++)
                TopButton(station.transform, "Btn_Process_" + i, "② 加工 " + StationSystem.CategoryNames[i],
                    148f + i * 38f, w, 34f, font);
            TopLabel(station.transform, "Txt_StationMaterials", "材料  塑板/金件/有机/芯: 0/0/0/0", 306f, w, 15, TextColor, font);
            for (int i = 0; i < 3; i++)
                TopButton(station.transform, "Btn_Craft_" + i, "③ 制造 " + StationSystem.RecipeNames[i],
                    334f + i * 38f, w, 34f, font);
            TopButton(station.transform, "Btn_Upgrade", "④ 升级工具（Lv.1 → 2）", 452f, w, 34f, font);
            TopButton(station.transform, "Btn_BagUpgrade", "⑤ 升级背包（总 20 → 40，需 15 金属零件）", 636f, w, 34f, font);
            GameObject job = Panel(station.transform, "JobBar", new Vector2(w, 18f),
                new Vector2(16f, -496f), TopLeft, TopLeft, BarBg);
            FilledBar(job.transform, "JobFill", durability);
            TopLabel(station.transform, "Txt_Job", "后台任务 0%", 520f, w, 14, TextColor, font);
            job.SetActive(false);
            TopButton(station.transform, "Btn_CloseStation", "关闭面板（M / ESC）", 558f, w, 34f, font);
            TopLabel(station.transform, "Txt_StationHint", "按钮 OnClick 已持久化绑定到 WhiteboxUiActions，可自行改绑",
                604f, w, 13, new Color(0.62f, 0.68f, 0.68f, 1f), font);

            // ⑥-b 中转站数量选择弹窗：叠在中转站面板之上，默认失活，显隐由 WhiteboxCanvasHud 每帧驱动
            CreateQuantityDialog(r, font, game);

            // ⑦ 暂停面板
            GameObject pause = Panel(r, "Panel_Pause", new Vector2(360f, 230f),
                Vector2.zero, Center, Center, PanelBg);
            TopLabel(pause.transform, "Txt_PauseTitle", "已暂停（后台加工/制造计时一并冻结）", 16f, 328f, 17, accent, font);
            TopButton(pause.transform, "Btn_Resume", "继续（ESC）", 58f, 328f, 38f, font);
            TopButton(pause.transform, "Btn_Restart", "新的一局", 104f, 328f, 38f, font);
            TopButton(pause.transform, "Btn_QuitPause", "退出", 150f, 328f, 38f, font);
            pause.SetActive(false);

            // ⑧ 结算面板
            GameObject settle = Panel(r, "Panel_Settlement", new Vector2(470f, 370f),
                Vector2.zero, Center, Center, PanelBg);
            TopLabel(settle.transform, "Txt_SettleTitle", "结算 · 恢复星球", 14f, 438f, 21, accent, font);
            TopLabel(settle.transform, "Txt_Collected", "垃圾清理总数：0", 50f, 438f, 16, TextColor, font);
            TopLabel(settle.transform, "Txt_Processed", "材料加工总数：0", 80f, 438f, 16, TextColor, font);
            TopLabel(settle.transform, "Txt_Crafted", "环保产品制造总数：0", 110f, 438f, 16, TextColor, font);
            TopLabel(settle.transform, "Txt_Land", "土地修复进度：0%", 140f, 438f, 16, TextColor, font);
            TopLabel(settle.transform, "Txt_Water", "水源修复进度：0%", 170f, 438f, 16, TextColor, font);
            TopLabel(settle.transform, "Txt_Time", "总游戏时间：0.0 s", 200f, 438f, 16, TextColor, font);
            TopButton(settle.transform, "Btn_RestartSettlement", "重开一局", 244f, 438f, 38f, font);
            TopButton(settle.transform, "Btn_QuitSettlement", "退出", 290f, 438f, 38f, font);
            settle.SetActive(false);

            // ⑨ 常驻提示文字
            Txt(r, "Txt_Hint",
                "WASD 移动 · 鼠标看视角 · 左键拾取/拆解 · M 中转站 · R 转盘 · I 背包 · B 交易 · Q 切专精 · F1 调试 · F5 新局 · F9 存档 · ESC 暂停",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(1500f, 30f),
                new Vector2(0f, -14f), 15, TextColor, TextAnchor.MiddleCenter, font);
            Txt(r, "Txt_Notice", "", BottomLeft, BottomLeft, new Vector2(1000f, 30f),
                new Vector2(24f, 52f), 16, TextColor, TextAnchor.MiddleLeft, font);
            Txt(r, "Txt_Deposit", "垃圾入站：漏斗下沉中…", BottomLeft, BottomLeft,
                new Vector2(1000f, 30f), new Vector2(24f, 82f), 16, TextColor, TextAnchor.MiddleLeft, font);

            // ⑩ 阶段白光：最后创建 = 位于 Canvas 最上层；alpha 由代码控制
            RectTransform flash = Stretch(r, "StageFlash");
            Image flashImg = AddImage(flash, new Color(1f, 1f, 1f, 0f), false);
            flashImg.raycastTarget = false;

            Debug.Log(Tag + "UI 层级已建：准星 / 背包边框 / 耐久条 / 浮字 / 调试 HUD / 中转站 / 暂停 / 结算 / 提示 / 白光");
            return root;
        }

        static void CreateEventSystem(Scene scene)
        {
            // activeInputHandler = 0（旧 Input Manager），必须配 StandaloneInputModule
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            PutInScene(go, scene);
            Debug.Log(Tag + "已补 EventSystem + StandaloneInputModule（旧 Input Manager）");
        }

        static void PutInScene(GameObject go, Scene scene)
        {
            if (go.scene != scene) SceneManager.MoveGameObjectToScene(go, scene);
        }

        // ---------------------------------------------------------------
        // 新增区块的幂等补齐（已存在的 UI 根只补缺失对象，不动用户摆好的布局）
        // ---------------------------------------------------------------

        /// <summary>
        /// 补齐「⑤ 升级背包」按钮与「数量选择弹窗」。
        /// 已存在的对象一律不重建、不改 RectTransform，只补缺的部分，因此用户对白膜 UI 的
        /// 手改格式（颜色 / 字体 / 位置）不会被这次升级冲掉。
        /// </summary>
        static bool EnsureStationExtras(Transform uiRoot, WhiteboxGame game)
        {
            bool changed = false;
            Font font = LoadFont();
            WhiteboxConfig cfg = AssetDatabase.LoadAssetAtPath<WhiteboxConfig>(WhiteboxBuilder.ConfigPath);

            Transform station = FindTransform(uiRoot, "Panel_Station");
            if (station != null && FindTransform(station, "Btn_BagUpgrade") == null)
            {
                int baseCap = cfg != null ? cfg.BackpackTotalCap(1) : 20;
                int maxCap = cfg != null ? cfg.BackpackTotalCap(2) : 40;
                int cost = cfg != null ? cfg.BackpackUpgradeMetalCost : 15;
                TopButton(station, "Btn_BagUpgrade",
                    "⑤ 升级背包（总 " + baseCap + " → " + maxCap + "，需 " + cost + " 金属零件）",
                    636f, 548f, 34f, font);
                changed = true;
                Debug.Log(Tag + "已补 ⑤ 升级背包按钮（中转站面板底部）");
            }

            if (FindTransform(uiRoot, "Panel_Quantity") == null)
            {
                CreateQuantityDialog(uiRoot, font, game);
                changed = true;
                Debug.Log(Tag + "已补数量选择弹窗 Panel_Quantity（滑块 + 输入框 + 一键全部）");
            }
            else
            {
                StationQuantityDialog dialog = FindTransform(uiRoot, "Panel_Quantity")
                    .GetComponent<StationQuantityDialog>();
                if (dialog != null && dialog.Game == null && game != null) { dialog.Game = game; changed = true; }
            }
            return changed;
        }

        /// <summary>
        /// 生成中转站「数量选择」弹窗：标题 / 可执行范围 / 当前值 / 滑块 / 输入框 / −+ / 一键全部 / 确认 / 取消。
        /// 显隐完全由 WhiteboxCanvasHud 驱动（Panel_Quantity 默认失活）。
        /// </summary>
        static GameObject CreateQuantityDialog(Transform parent, Font font, WhiteboxGame game)
        {
            WhiteboxConfig cfg = AssetDatabase.LoadAssetAtPath<WhiteboxConfig>(WhiteboxBuilder.ConfigPath);
            Color accent = cfg != null ? cfg.HighlightColor : Accent;
            Color bg = new Color(0.07f, 0.085f, 0.095f, 0.99f);
            Color hint = new Color(0.62f, 0.68f, 0.68f, 1f);
            const float w = 560f;
            const float pad = 16f;
            float inner = w - pad * 2f;

            GameObject panel = Panel(parent, "Panel_Quantity", new Vector2(w, 296f),
                Vector2.zero, Center, Center, bg);
            Transform t = panel.transform;

            TopLabel(t, "Txt_QtyTitle", "加工塑料 · 选择数量", 16f, inner, 20, accent, font);
            TopLabel(t, "Txt_QtyInfo", "可执行 1 ~ 1 次  ·  预计 2.5 s", 50f, inner, 15, TextColor, font);
            TopLabel(t, "Txt_QtyValue", "× 1", 76f, inner, 26, accent, font);

            Slider sld = SliderBar(t, "Sld_Qty", 118f, pad, 380f, 26f, BarBg, accent, accent);
            InputField inp = NumberInput(t, "Inp_Qty", 110f, 410f, inner - 394f, 32f, BtnBg, TextColor, font);

            Button minus = RowButton(t, "Btn_QtyMinus", "−", pad, 158f, 40f, 34f, font);
            Button plus = RowButton(t, "Btn_QtyPlus", "+", pad + 46f, 158f, 40f, 34f, font);
            Button all = RowButton(t, "Btn_QtyAll", "一键全部（1）", pad + 96f, 158f, 186f, 34f, font);
            Button confirm = RowButton(t, "Btn_QtyConfirm", "确认执行", 310f, 158f, 118f, 34f, font);
            Button cancel = RowButton(t, "Btn_QtyCancel", "取消", 436f, 158f, 108f, 34f, font);

            Text detail = TopLabel(t, "Txt_QtyDetail", "消耗 --  →  --", 202f, inner, 15, TextColor, font);
            TopLabel(t, "Txt_QtyHint",
                "拖动滑块 / 直接输入数字 / 点「一键全部」一次做满；确认后开始一次连续任务（耗时按次数叠加）",
                232f, inner, 13, hint, font);

            StationQuantityDialog d = panel.AddComponent<StationQuantityDialog>();
            d.Game = game;
            d.TxtTitle = FindText(t, "Txt_QtyTitle");
            d.TxtInfo = FindText(t, "Txt_QtyInfo");
            d.TxtValue = FindText(t, "Txt_QtyValue");
            d.TxtDetail = detail;
            d.TxtAll = all.GetComponentInChildren<Text>(true);
            d.Sld = sld;
            d.Inp = inp;
            d.BtnConfirm = confirm;
            d.BtnMinus = minus;
            d.BtnPlus = plus;
            d.BtnAll = all;
            d.BtnCancel = cancel;
            panel.SetActive(false);
            return panel;
        }

        static Text FindText(Transform root, string name)
        {
            Transform t = FindTransform(root, name);
            return t != null ? t.GetComponent<Text>() : null;
        }

        /// <summary>水平滑块：Background / Fill Area / Handle Slide Area 三段结构，符合 uGUI Slider 的驱动约定。</summary>
        static Slider SliderBar(Transform parent, string name, float y, float x, float width, float height,
            Color bgColor, Color fillColor, Color handleColor)
        {
            RectTransform rt = Rect(parent, name, TopLeft, TopLeft, new Vector2(width, height), new Vector2(x, -y));
            Slider s = rt.gameObject.AddComponent<Slider>();

            RectTransform bg = Stretch(rt, "Background");
            bg.anchorMin = new Vector2(0f, 0.32f);
            bg.anchorMax = new Vector2(1f, 0.68f);
            bg.offsetMin = Vector2.zero;
            bg.offsetMax = Vector2.zero;
            AddImage(bg, bgColor, true); // 必须有 raycast 目标，否则拖不动滑块

            RectTransform area = Stretch(rt, "Fill Area");
            area.anchorMin = new Vector2(0f, 0.32f);
            area.anchorMax = new Vector2(1f, 0.68f);
            area.offsetMin = new Vector2(6f, 0f);
            area.offsetMax = new Vector2(-6f, 0f);
            RectTransform fill = Stretch(area, "Fill");
            AddImage(fill, fillColor, false);

            RectTransform handleArea = Stretch(rt, "Handle Slide Area");
            handleArea.offsetMin = new Vector2(10f, 0f);
            handleArea.offsetMax = new Vector2(-10f, 0f);
            RectTransform handle = Rect(handleArea, "Handle", new Vector2(0f, 0f), Center,
                new Vector2(18f, 0f), Vector2.zero);
            handle.anchorMin = new Vector2(0f, 0f);
            handle.anchorMax = new Vector2(0f, 1f);
            handle.sizeDelta = new Vector2(18f, 0f);
            Image handleImg = AddImage(handle, handleColor, true);

            s.fillRect = fill;
            s.handleRect = handle;
            s.targetGraphic = handleImg;
            s.direction = Slider.Direction.LeftToRight;
            s.wholeNumbers = true;
            s.minValue = 1f;
            s.maxValue = 10f;
            s.value = 1f;
            return s;
        }

        /// <summary>整数输入框（内容类型限定为 IntegerNumber，最长 5 位）。</summary>
        static InputField NumberInput(Transform parent, string name, float y, float x, float width, float height,
            Color bgColor, Color textColor, Font font)
        {
            RectTransform rt = Rect(parent, name, TopLeft, TopLeft, new Vector2(width, height), new Vector2(x, -y));
            Image bgImg = AddImage(rt, bgColor, true);
            InputField field = rt.gameObject.AddComponent<InputField>();

            RectTransform phRt = Stretch(rt, "Placeholder");
            phRt.offsetMin = new Vector2(8f, 2f);
            phRt.offsetMax = new Vector2(-8f, -2f);
            Text ph = phRt.gameObject.AddComponent<Text>();
            ph.font = font;
            ph.fontSize = 15;
            ph.color = new Color(textColor.r, textColor.g, textColor.b, 0.40f);
            ph.alignment = TextAnchor.MiddleCenter;
            ph.raycastTarget = false;
            ph.text = "1";

            RectTransform txRt = Stretch(rt, "Text");
            txRt.offsetMin = new Vector2(8f, 2f);
            txRt.offsetMax = new Vector2(-8f, -2f);
            Text tx = txRt.gameObject.AddComponent<Text>();
            tx.font = font;
            tx.fontSize = 16;
            tx.color = textColor;
            tx.alignment = TextAnchor.MiddleCenter;
            tx.horizontalOverflow = HorizontalWrapMode.Overflow;
            tx.verticalOverflow = VerticalWrapMode.Overflow;
            tx.raycastTarget = false;
            tx.text = "1";

            field.targetGraphic = bgImg;
            field.textComponent = tx;
            field.placeholder = ph;
            field.lineType = InputField.LineType.SingleLine;
            field.contentType = InputField.ContentType.IntegerNumber;
            field.characterLimit = 5;
            return field;
        }

        static Button RowButton(Transform parent, string name, string caption, float x, float y,
            float width, float height, Font font)
        {
            RectTransform rt = Rect(parent, name, TopLeft, TopLeft, new Vector2(width, height), new Vector2(x, -y));
            AddImage(rt, BtnBg, true);
            Button b = rt.gameObject.AddComponent<Button>();
            ColorBlock cb = b.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
            cb.pressedColor = new Color(0.76f, 0.76f, 0.76f, 1f);
            cb.selectedColor = Color.white;
            cb.fadeDuration = 0.08f;
            b.colors = cb;

            RectTransform label = Stretch(rt, "Label");
            label.offsetMin = new Vector2(8f, 2f);
            label.offsetMax = new Vector2(-8f, -2f);
            Text t = label.gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = 15;
            t.color = TextColor;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = caption;
            return b;
        }

        // ---------------------------------------------------------------
        // 按钮持久化绑定（Inspector 里可见；只补空绑定，不覆盖用户自定义）
        // ---------------------------------------------------------------

        static bool EnsureButtonBindings(Transform root, WhiteboxUiActions a)
        {
            if (a == null) return false;
            bool changed = false;
            changed |= BindButton(root, "Btn_Deposit", new UnityAction(a.DepositAll));
            for (int i = 0; i < 4; i++)
                changed |= BindIntButton(root, "Btn_Process_" + i, new UnityAction<int>(a.Process), i);
            for (int i = 0; i < 3; i++)
                changed |= BindIntButton(root, "Btn_Craft_" + i, new UnityAction<int>(a.Craft), i);
            changed |= BindButton(root, "Btn_Upgrade", new UnityAction(a.UpgradeTool));
            changed |= BindButton(root, "Btn_BagUpgrade", new UnityAction(a.UpgradeBackpack));
            changed |= BindButton(root, "Btn_CloseStation", new UnityAction(a.CloseStation));
            changed |= BindButton(root, "Btn_Resume", new UnityAction(a.Resume));
            changed |= BindButton(root, "Btn_Restart", new UnityAction(a.Restart));
            changed |= BindButton(root, "Btn_QuitPause", new UnityAction(a.Quit));
            changed |= BindButton(root, "Btn_RestartSettlement", new UnityAction(a.Restart));
            changed |= BindButton(root, "Btn_QuitSettlement", new UnityAction(a.Quit));
            return changed;
        }

        static bool BindButton(Transform root, string name, UnityAction call)
        {
            Button b = FindButton(root, name);
            if (b == null || b.onClick.GetPersistentEventCount() > 0) return false;
            UnityEventTools.AddPersistentListener(b.onClick, call);
            EditorUtility.SetDirty(b);
            return true;
        }

        static bool BindIntButton(Transform root, string name, UnityAction<int> call, int arg)
        {
            Button b = FindButton(root, name);
            if (b == null || b.onClick.GetPersistentEventCount() > 0) return false;
            UnityEventTools.AddIntPersistentListener(b.onClick, call, arg);
            EditorUtility.SetDirty(b);
            return true;
        }

        static Button FindButton(Transform root, string name)
        {
            Transform t = FindTransform(root, name);
            return t != null ? t.GetComponent<Button>() : null;
        }

        // ---------------------------------------------------------------
        // 小工具
        // ---------------------------------------------------------------

        /// <summary>字体统一走 UiFont：Assets/Art/Fonts/ 里有中文字体就优先用。</summary>
        static Font LoadFont()
        {
            return UiFont.Load();
        }

        static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pivot,
            Vector2 size, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return rt;
        }

        static RectTransform Stretch(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        static Image AddImage(RectTransform rt, Color c, bool raycast)
        {
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = raycast;
            return img;
        }

        static void Edge(RectTransform parent, string name, Vector2 min, Vector2 max,
            Vector2 size, Vector2 pos, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.pivot = Center;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            AddImage(rt, c, false);
        }

        static GameObject Panel(Transform parent, string name, Vector2 size, Vector2 pos,
            Vector2 anchor, Vector2 pivot, Color bg)
        {
            RectTransform rt = Rect(parent, name, anchor, pivot, size, pos);
            AddImage(rt, bg, true);
            return rt.gameObject;
        }

        static Image FilledBar(Transform parent, string name, Color c)
        {
            RectTransform rt = Stretch(parent, name);
            Image img = AddImage(rt, c, false);
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillAmount = 1f;
            return img;
        }

        static Text Txt(Transform parent, string name, string value, Vector2 anchor, Vector2 pivot,
            Vector2 size, Vector2 pos, int fontSize, Color color, TextAnchor align, Font font)
        {
            RectTransform rt = Rect(parent, name, anchor, pivot, size, pos);
            Text t = rt.gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = value;
            return t;
        }

        static Text TopLabel(Transform parent, string name, string value, float y, float width,
            int fontSize, Color color, Font font)
        {
            return Txt(parent, name, value, TopLeft, TopLeft, new Vector2(width, 26f),
                new Vector2(16f, -y), fontSize, color, TextAnchor.MiddleLeft, font);
        }

        static Button TopButton(Transform parent, string name, string caption, float y,
            float width, float height, Font font)
        {
            RectTransform rt = Rect(parent, name, TopLeft, TopLeft, new Vector2(width, height),
                new Vector2(16f, -y));
            AddImage(rt, BtnBg, true);
            Button b = rt.gameObject.AddComponent<Button>();
            ColorBlock cb = b.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
            cb.pressedColor = new Color(0.76f, 0.76f, 0.76f, 1f);
            cb.selectedColor = Color.white;
            cb.fadeDuration = 0.08f;
            b.colors = cb;

            RectTransform label = Stretch(rt, "Label");
            label.offsetMin = new Vector2(10f, 2f);
            label.offsetMax = new Vector2(-10f, -2f);
            Text t = label.gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = 15;
            t.color = TextColor;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = caption;
            return b;
        }

        static Transform FindTransform(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) if (all[i].name == name) return all[i];
            return null;
        }

        static GameObject FindRoot(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) if (roots[i].name == name) return roots[i];
            return null;
        }

        static T FindComponent<T>(Scene scene) where T : Component
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                T c = roots[i].GetComponentInChildren<T>(true);
                if (c != null) return c;
            }
            return null;
        }
    }
}
