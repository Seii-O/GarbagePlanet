// @trace ART-004, ART-008, MECH-013, PLAT-005
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// Hierarchy 内可编辑的 uGUI 视图。
    ///
    /// 设计红线：所有面板、文字、按钮、进度条都是 WhiteboxUiBuilder 生成到场景里的真实
    /// GameObject，用户在 Inspector / Scene 视图里改的 RectTransform、颜色、字体、字号、
    /// 层级、文案都不会被本组件覆盖。运行时只写 Text.text、Image.fillAmount、CanvasGroup.alpha
    /// 和 GameObject.SetActive —— 也就是说「内容由代码给，格式由用户定」。
    ///
    /// 与旧 IMGUI 的关系：WhiteboxHud 是代理，绑定了本组件后不再画 IMGUI；
    /// 没绑定（或勾选 forceImGui）时自动回退到纯 IMGUI，保证老场景不炸。
    /// </summary>
    public sealed class WhiteboxCanvasHud : MonoBehaviour
    {
        struct FloatLabel
        {
            public Text Label;
            public float Age;
        }

        [Header("装配引用（WhiteboxUiBuilder 写入；缺失时自动按名字在子层级里找）")]
        public WhiteboxGame Game;
        public WhiteboxHud Proxy;

        [Header("常驻表现")]
        public Image Crosshair;
        public Image StageFlash;
        public CanvasGroup BagWarning;
        public GameObject DurabilityGroup;
        public Image DurabilityFill;

        [Header("提示文字")]
        public Text TxtNotice;
        public Text TxtDeposit;
        public Text TxtHint;

        [Header("调试 HUD（F1）")]
        public GameObject DebugPanel;
        public Text TxtBag;
        public Text TxtMaterials;
        public Text TxtTool;
        public Text TxtProgress;
        public Text TxtQuest;
        public Text TxtStats;

        [Header("中转站面板（M）")]
        public GameObject StationPanel;
        public Text TxtRepaired;
        public Text TxtRaw;
        public Text TxtStationMaterials;
        public Text[] ProcessLabels;
        public Text[] CraftLabels;
        public Text TxtUpgrade;
        public Text TxtBagUpgrade;
        public GameObject JobGroup;
        public Image JobFill;
        public Text TxtJob;

        [Header("中转站数量选择弹窗")]
        [Tooltip("点加工/制造按钮后弹出，用滑块 / 输入框 / 一键全部决定连续做几次")]
        public StationQuantityDialog QuantityDialog;

        [Header("暂停 / 结算")]
        public GameObject PausePanel;
        public GameObject SettlementPanel;
        public Text TxtCollected;
        public Text TxtProcessed;
        public Text TxtCrafted;
        public Text TxtLand;
        public Text TxtWater;
        public Text TxtTime;

        [Header("拾取浮字")]
        public RectTransform FloatsRoot;
        public Text FloatTemplate;

        [Header("中文字体")]
        [Tooltip("运行时检测到内置字体缺中文字形时，自动换用本机中文字体；" +
                 "若要自己在 Inspector 指定字体资产，把这项关掉即可")]
        public bool AutoFixCjkFont = true;

        readonly List<FloatLabel> floats = new List<FloatLabel>();
        string notice = "";
        float noticeLeft;
        float depositLeft;
        float hintLeft = 12f;
        int durabilityLeft;
        int durabilityMax;
        bool settlement;
        float flashAlpha;

        /// <summary>编辑器校验用：关键引用是否齐全。</summary>
        public bool HasRequiredRefs =>
            Game != null && Proxy != null && Crosshair != null && DebugPanel != null &&
            StationPanel != null && PausePanel != null && SettlementPanel != null &&
            FloatsRoot != null && FloatTemplate != null && DurabilityFill != null &&
            QuantityDialog != null;

        public float FlashAlpha
        {
            get => flashAlpha;
            set
            {
                flashAlpha = Mathf.Clamp01(value);
                if (StageFlash == null) return;
                Color c = StageFlash.color;
                c.a = flashAlpha;
                StageFlash.color = c;
            }
        }

        public bool SettlementOpen => settlement;

        void Awake()
        {
            AutoResolveRefs();
            EnsureFillSprites();
            FixCjkFont();
            if (FloatTemplate != null) FloatTemplate.gameObject.SetActive(false);
        }

        // @trace MECH-006, ART-004
        // Unity 的 Image 在 sprite == null 时走基类绘制实心矩形，Image.Type.Filled 与
        // fillAmount 会被完全忽略 —— 进度条看起来永远满格，耐久扣了却「不减少」。
        // WhiteboxUiBuilder.FilledBar 只设了 color/type/fillAmount，没有设 sprite，
        // 场景里烘焙出来就是 m_Sprite: {fileID: 0}。这里运行时补一张纯白 2×2 sprite：
        // 只写 sprite，不动颜色 / 尺寸 / 锚点等用户在 Inspector 里摆好的格式。
        static Sprite solidFillSprite;

        static Sprite SolidFillSprite
        {
            get
            {
                if (solidFillSprite != null) return solidFillSprite;
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
                tex.Apply();
                tex.hideFlags = HideFlags.HideAndDontSave;
                solidFillSprite = Sprite.Create(tex, new Rect(0f, 0f, 2f, 2f),
                    new Vector2(0.5f, 0.5f), 100f);
                solidFillSprite.name = "WhiteboxSolidFill";
                solidFillSprite.hideFlags = HideFlags.HideAndDontSave;
                return solidFillSprite;
            }
        }

        /// <summary>补齐填充条的 sprite，让 fillAmount 真正生效（大型垃圾耐久条 / 中转站任务条）。</summary>
        void EnsureFillSprites()
        {
            Sprite s = SolidFillSprite;
            if (s == null) return;
            EnsureFillSprite(DurabilityFill, s);
            EnsureFillSprite(JobFill, s);
        }

        static void EnsureFillSprite(Image img, Sprite s)
        {
            if (img == null || img.sprite != null) return; // 用户在 Inspector 里指定过就绝不覆盖
            img.sprite = s;
            if (img.type != Image.Type.Filled)
            {
                img.type = Image.Type.Filled;
                img.fillMethod = Image.FillMethod.Horizontal;
            }
        }

        public void Init(WhiteboxGame g, WhiteboxHud p)
        {
            Game = g;
            Proxy = p;
            AutoResolveRefs();
            EnsureFillSprites();
            floats.Clear();
            durabilityLeft = durabilityMax = 0; // 重开一局时清掉上一局可能残留的黄条
            notice = "";
            noticeLeft = 0f;
            depositLeft = 0f;
            hintLeft = 12f;
            settlement = g != null && g.State != null && g.State.finished;
            // 注意：这里不能立刻 Refresh()。WhiteboxGame.Start() 里 hud.Init 早于
            // quest.Init / stationSystem.Init，此时读 Game.Quest.CurrentText 会 NRE 并中断整个 Start。
            // 文字与显隐统一交给第一帧 Update（那时所有系统都 Init 完成）。
            SetActive(DebugPanel, false);
            SetActive(StationPanel, false);
            SetActive(PausePanel, false);
            SetActive(SettlementPanel, false);
            SetActive(DurabilityGroup, false);
            SetActive(JobGroup, false);
        }

        /// <summary>供 Editor 构建脚本在场景里立即补全绑定（不改任何非空引用）。</summary>
        public void EditorResolve() { AutoResolveRefs(); }

        /// <summary>
        /// 用户删掉 / 重命名了某个 UI 对象时，按约定名字在子层级里找回；
        /// 已绑定的非空引用一律不动，避免覆盖用户在 Inspector 里手工换过的对象。
        /// </summary>
        void AutoResolveRefs()
        {
            if (Game == null) Game = FindObjectOfType<WhiteboxGame>();
            if (Proxy == null && Game != null) Proxy = Game.GetComponent<WhiteboxHud>();
            Transform root = transform;
            Crosshair = Fill(Crosshair, Find<Image>(root, "Crosshair"));
            StageFlash = Fill(StageFlash, Find<Image>(root, "StageFlash"));
            BagWarning = Fill(BagWarning, Find<CanvasGroup>(root, "BagWarning"));
            DurabilityGroup = Fill(DurabilityGroup, FindGo(root, "DurabilityBar"));
            DurabilityFill = Fill(DurabilityFill, Find<Image>(root, "DurabilityFill"));
            TxtNotice = Fill(TxtNotice, Find<Text>(root, "Txt_Notice"));
            TxtDeposit = Fill(TxtDeposit, Find<Text>(root, "Txt_Deposit"));
            TxtHint = Fill(TxtHint, Find<Text>(root, "Txt_Hint"));
            DebugPanel = Fill(DebugPanel, FindGo(root, "Hud_Debug"));
            TxtBag = Fill(TxtBag, Find<Text>(root, "Txt_Bag"));
            TxtMaterials = Fill(TxtMaterials, Find<Text>(root, "Txt_Materials"));
            TxtTool = Fill(TxtTool, Find<Text>(root, "Txt_Tool"));
            TxtProgress = Fill(TxtProgress, Find<Text>(root, "Txt_Progress"));
            TxtQuest = Fill(TxtQuest, Find<Text>(root, "Txt_Quest"));
            TxtStats = Fill(TxtStats, Find<Text>(root, "Txt_Stats"));
            StationPanel = Fill(StationPanel, FindGo(root, "Panel_Station"));
            TxtRepaired = Fill(TxtRepaired, Find<Text>(root, "Txt_Repaired"));
            TxtRaw = Fill(TxtRaw, Find<Text>(root, "Txt_Raw"));
            TxtStationMaterials = Fill(TxtStationMaterials, Find<Text>(root, "Txt_StationMaterials"));
            JobGroup = Fill(JobGroup, FindGo(root, "JobBar"));
            JobFill = Fill(JobFill, Find<Image>(root, "JobFill"));
            TxtJob = Fill(TxtJob, Find<Text>(root, "Txt_Job"));
            PausePanel = Fill(PausePanel, FindGo(root, "Panel_Pause"));
            SettlementPanel = Fill(SettlementPanel, FindGo(root, "Panel_Settlement"));
            TxtCollected = Fill(TxtCollected, Find<Text>(root, "Txt_Collected"));
            TxtProcessed = Fill(TxtProcessed, Find<Text>(root, "Txt_Processed"));
            TxtCrafted = Fill(TxtCrafted, Find<Text>(root, "Txt_Crafted"));
            TxtLand = Fill(TxtLand, Find<Text>(root, "Txt_Land"));
            TxtWater = Fill(TxtWater, Find<Text>(root, "Txt_Water"));
            TxtTime = Fill(TxtTime, Find<Text>(root, "Txt_Time"));
            FloatsRoot = Fill(FloatsRoot, Find<RectTransform>(root, "FloatsRoot"));
            FloatTemplate = Fill(FloatTemplate, Find<Text>(root, "Txt_FloatTemplate"));
            ProcessLabels = FillArray(ProcessLabels, root, "Btn_Process_", 4);
            CraftLabels = FillArray(CraftLabels, root, "Btn_Craft_", 3);
            TxtUpgrade = Fill(TxtUpgrade, ButtonText(root, "Btn_Upgrade"));
            TxtBagUpgrade = Fill(TxtBagUpgrade, ButtonText(root, "Btn_BagUpgrade"));
            QuantityDialog = Fill(QuantityDialog, Find<StationQuantityDialog>(root, "Panel_Quantity"));
        }

        static T Fill<T>(T current, T found) where T : Object => current != null ? current : found;

        static Text[] FillArray(Text[] current, Transform root, string prefix, int count)
        {
            var result = new Text[count];
            for (int i = 0; i < count; i++)
            {
                if (current != null && i < current.Length && current[i] != null)
                {
                    result[i] = current[i];
                    continue;
                }
                result[i] = ButtonText(root, prefix + i);
            }
            return result;
        }

        static Text ButtonText(Transform root, string buttonName)
        {
            Transform t = FindTransform(root, buttonName);
            return t != null ? t.GetComponentInChildren<Text>(true) : null;
        }

        static GameObject FindGo(Transform root, string name)
        {
            Transform t = FindTransform(root, name);
            return t != null ? t.gameObject : null;
        }

        static T Find<T>(Transform root, string name) where T : Component
        {
            Transform t = FindTransform(root, name);
            return t != null ? t.GetComponent<T>() : null;
        }

        static Transform FindTransform(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) if (all[i].name == name) return all[i];
            return null;
        }

        /// <summary>uGUI 内置字体不含中文字形，运行时按本机字体回退；用户自定义字体不受影响。</summary>
        void FixCjkFont()
        {
            if (!AutoFixCjkFont || !Application.isPlaying) return;
            Font cjk = null;
            try
            {
                cjk = Font.CreateDynamicFontFromOSFont(new[]
                {
                    "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "SimSun",
                    "PingFang SC", "Noto Sans CJK SC", "WenQuanYi Micro Hei", "Arial"
                }, 18);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[WhiteboxCanvasHud] 本机中文字体不可用：" + e.Message);
            }
            if (cjk == null) return;
            foreach (Text t in GetComponentsInChildren<Text>(true))
                if (t.font == null || !t.font.HasCharacter('中')) t.font = cjk;
        }

        // ---------------------------------------------------------------
        // 由 WhiteboxHud 代理转发过来的入口
        // ---------------------------------------------------------------

        public void FloatText(string text)
        {
            if (FloatTemplate == null || FloatsRoot == null) return;
            Text t = Instantiate(FloatTemplate, FloatsRoot);
            t.gameObject.name = "Float";
            t.text = text;
            t.gameObject.SetActive(true);
            floats.Add(new FloatLabel { Label = t, Age = 0f });
        }

        public void Notice(string text) { notice = text; noticeLeft = 2.4f; }
        public void ShowDeposit(float seconds) { depositLeft = seconds; }
        public void SetDurability(int left, int max)
        {
            durabilityLeft = left; durabilityMax = max;
            // 写入即生效：不依赖本组件 Update 与 InteractionController 的执行顺序，
            // 保证黄条与白边同帧出现 / 同帧消失。
            ApplyDurability();
        }

        /// <summary>
        /// 耐久条显隐与填充的唯一应用点。触发范围完全由 InteractionController 决定
        /// （与白边同源：给 (0,0) 即隐藏），这里只负责呈现，不自行判断距离，
        /// 避免两层各自维护一套条件而再次出现行为不一致。
        /// </summary>
        void ApplyDurability()
        {
            if (DurabilityGroup == null) return;
            bool block = Game != null && Game.IsUiBlocking;
            bool show = durabilityMax > 0 && durabilityLeft > 0 && !block;
            SetActive(DurabilityGroup, show);
            if (DurabilityFill != null && show)
                DurabilityFill.fillAmount = Mathf.Clamp01(durabilityLeft / (float)durabilityMax);
        }
        public void OpenSettlement() { settlement = true; Refresh(); }
        public void CloseSettlement() { settlement = false; Refresh(); }
        public void ToggleDebug() { if (Proxy != null) Proxy.DebugVisible = !Proxy.DebugVisible; }

        void Update()
        {
            float dt = Time.deltaTime;
            if (noticeLeft > 0f) noticeLeft -= dt;
            if (depositLeft > 0f) depositLeft -= dt;
            if (hintLeft > 0f) hintLeft -= dt;

            float duration = Game != null && Game.Config != null
                ? Mathf.Max(0.05f, Game.Config.PickupFloatSeconds) : 0.8f;
            for (int i = floats.Count - 1; i >= 0; i--)
            {
                FloatLabel f = floats[i];
                f.Age += dt / duration;
                if (f.Label == null) { floats.RemoveAt(i); continue; }
                if (f.Age >= 1f)
                {
                    Destroy(f.Label.gameObject);
                    floats.RemoveAt(i);
                    continue;
                }
                Color c = f.Label.color;
                c.a = 1f - f.Age;
                f.Label.color = c;
                f.Label.rectTransform.anchoredPosition += new Vector2(0f, 40f * dt);
                floats[i] = f;
            }

            Refresh();
        }

        /// <summary>
        /// 每帧只更新内容 / 显隐。绝不写 anchoredPosition（浮字实例除外）、
        /// sizeDelta、字体、颜色常量，避免把用户摆好的格式改回去。
        /// </summary>
        void Refresh()
        {
            // 每帧幂等保险：即便运行时替换了 Fill 引用、或域重载丢了动态 sprite，也能立即补回。
            EnsureFillSprites();
            if (Game == null || Game.Config == null || Game.State == null) return;
            WhiteboxConfig cfg = Game.Config;
            WhiteboxState s = Game.State;
            bool block = Game.IsUiBlocking;

            bool debugOn = Proxy == null || Proxy.DebugVisible;
            bool stationVisible = Game.StationOpen && !Game.IsPaused && !s.finished;
            SetActive(DebugPanel, debugOn && !block);
            SetActive(StationPanel, stationVisible);
            SetActive(PausePanel, Game.IsPaused && !settlement);
            SetActive(SettlementPanel, settlement);
            SetActive(TxtHint == null ? null : TxtHint.gameObject, hintLeft > 0f && !block);
            SetActive(TxtNotice == null ? null : TxtNotice.gameObject, noticeLeft > 0f);
            SetActive(TxtDeposit == null ? null : TxtDeposit.gameObject, depositLeft > 0f);

            // 数量选择弹窗：显隐完全跟着「中转站可见 + 有待确认操作」，自己不做判断
            if (QuantityDialog != null) QuantityDialog.Sync(Game, stationVisible);

            if (TxtNotice != null) TxtNotice.text = notice;
            if (TxtDeposit != null) TxtDeposit.text = "垃圾入站：漏斗下沉中…";

            // 背包 >80% 呼吸边框（容量按当前背包等级算）
            int[] bagCaps = cfg.BackpackCapAt(s.bagLevel);
            int bagSum = 0, bagCap = 0;
            for (int i = 0; i < 4; i++) { bagSum += s.bag[i]; bagCap += bagCaps[i]; }
            bool warn = bagCap > 0 && bagSum / (float)bagCap > 0.8f;
            SetActive(BagWarning == null ? null : BagWarning.gameObject, warn && !block);
            if (BagWarning != null && warn)
                BagWarning.alpha = 0.35f + 0.25f * Mathf.Sin(Time.time * 6f);

            // 大型垃圾耐久条：写入时已即时应用，这里每帧幂等兜底
            ApplyDurability();

            if (StageFlash != null)
            {
                Color c = StageFlash.color;
                c.a = flashAlpha;
                StageFlash.color = c;
            }

            if (debugOn && !block) RefreshDebug(s, cfg);
            if (Game.StationOpen && !Game.IsPaused && !s.finished) RefreshStation(s, cfg);
            if (settlement) RefreshSettlement(s);
        }

        void RefreshDebug(WhiteboxState s, WhiteboxConfig cfg)
        {
            SetText(TxtBag, "背包 Lv." + s.bagLevel + "  总 " + s.BagTotal() + "/" + s.BagCapTotal(cfg)
                            + "  塑/金/有/电: " + s.bag[0] + "/" + s.bag[1] + "/" + s.bag[2] + "/" + s.bag[3]);
            SetText(TxtMaterials, "材料 板/金/肥/芯: " + s.materials[0] + "/" + s.materials[1] + "/"
                                  + s.materials[2] + "/" + s.materials[3]);
            int lv = Mathf.Clamp(s.toolLevel, 1, 3);
            SetText(TxtTool, "工具 Lv." + s.toolLevel + " · 拾取 " + cfg.PickRange[lv - 1] + " m"
                             + " · 地表 " + (Game.Spawner != null ? Game.Spawner.Active.Count : 0)
                             + "/" + cfg.MapTrashCap);
            // 累计生成量放在进度行（与「阶段」同一行），避免工具行过长被 Text 换行裁掉
            SetText(TxtProgress, "土地 " + (s.landProgress * 100f).ToString("0") + "% · 水源 "
                                 + (s.waterProgress * 100f).ToString("0") + "% · 阶段 "
                                 + ((Game.Eco != null ? Game.Eco.Stage : 0) + 1) + "/3"
                                 + " · 累计 " + s.totalSpawned + "/" + cfg.TotalTrashBudget);
            SetText(TxtQuest, "任务 " + System.Math.Min(s.questStep + 1, 10) + "/10 ： "
                              + (Game.Quest != null ? Game.Quest.CurrentText : ""));
            SetText(TxtStats, "清理 " + s.collected + " · 加工 " + s.processed + " · 制造 "
                              + s.crafted + " · 用时 " + s.playTime.ToString("0") + " s");
        }

        void RefreshStation(WhiteboxState s, WhiteboxConfig cfg)
        {
            SetText(TxtRepaired, "已修复: " + (s.stationRepaired ? "是" : "否（先造 1 块再生建材板）")
                                 + "  ·  背包 Lv." + s.bagLevel + "/" + WhiteboxConfig.BagMaxLevel
                                 + "（总 " + s.BagTotal() + "/" + s.BagCapTotal(cfg) + "）");
            SetText(TxtRaw, "待加工垃圾  塑/金/有/电: " + s.raw[0] + "/" + s.raw[1] + "/"
                            + s.raw[2] + "/" + s.raw[3]);
            SetText(TxtStationMaterials, "材料  塑板/金件/有机/芯: " + s.materials[0] + "/"
                                         + s.materials[1] + "/" + s.materials[2] + "/" + s.materials[3]);
            // 按钮文案带「可N次」：点开弹窗之前就能看到这次能做多少
            for (int i = 0; i < 4; i++)
                if (ProcessLabels != null && i < ProcessLabels.Length)
                    SetText(ProcessLabels[i], "② 加工" + StationSystem.CategoryNames[i]
                        + " ×" + cfg.MinProcessBatch[i] + "→" + StationSystem.MaterialNames[i]
                        + "  可" + Game.StationLogic.MaxProcessBatches(i) + "次");
            for (int i = 0; i < 3; i++)
                if (CraftLabels != null && i < CraftLabels.Length)
                    SetText(CraftLabels[i], "③ " + StationSystem.RecipeNames[i]
                        + "（" + Game.StationLogic.CostText(i) + "）可"
                        + Game.StationLogic.MaxCraftCount(i) + "次");
            SetText(TxtUpgrade, "④ 升级工具（Lv." + s.toolLevel + " → " + (s.toolLevel + 1) + "）");
            SetText(TxtBagUpgrade, Game.StationLogic.BagUpgradeText());

            bool hasJob = Game.StationLogic != null && Game.StationLogic.HasJob;
            SetActive(JobGroup, hasJob);
            if (!hasJob) return;
            SetText(TxtJob, Game.StationLogic.JobLabel + "  "
                            + (Game.StationLogic.JobProgress * 100f).ToString("0") + "%");
            if (JobFill != null) JobFill.fillAmount = Game.StationLogic.JobProgress;
        }

        void RefreshSettlement(WhiteboxState s)
        {
            SetText(TxtCollected, "垃圾清理总数：" + s.collected);
            SetText(TxtProcessed, "材料加工总数：" + s.processed);
            SetText(TxtCrafted, "环保产品制造总数：" + s.crafted);
            SetText(TxtLand, "土地修复进度：" + (s.landProgress * 100f).ToString("0") + "%");
            SetText(TxtWater, "水源修复进度：" + (s.waterProgress * 100f).ToString("0") + "%");
            SetText(TxtTime, "总游戏时间：" + s.playTime.ToString("0.0") + " s");
        }

        static void SetActive(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }

        static void SetText(Text t, string value)
        {
            if (t != null && t.text != value) t.text = value;
        }
    }
}
