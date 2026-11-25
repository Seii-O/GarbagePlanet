// @trace ART-004, ART-008, MECH-013, UI-006
using System.Collections.Generic;
using UnityEngine;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// HUD 代理层：绑定了 Hierarchy 里的 uGUI 视图（WhiteboxCanvasHud）后只做转发，
    /// 不再绘制 IMGUI；未绑定时保留完整的纯 IMGUI 回退实现（老场景 / 无 uGUI 依赖）。
    /// 正式取舍见策划案 §11：常驻只有准星；调试信息按 F1 可关，
    /// 中转站 / 暂停 / 结算面板是上下文触发的临时面板，不是常驻 UI。
    /// </summary>
    public sealed class WhiteboxHud : MonoBehaviour
    {
        struct FloatLabel
        {
            public string Text;
            public float Left;
            public float Y;
            public Color Color;
        }

        readonly List<FloatLabel> floats = new List<FloatLabel>();
        WhiteboxGame game;
        string notice = "";
        float noticeLeft;
        float depositLeft;
        float hintLeft = 12f;
        int durabilityLeft;
        int durabilityMax;
        bool settlement;
        float flashAlpha;

        [Header("Hierarchy UI（由 WhiteboxUiBuilder 写入）")]
        [Tooltip("绑定后本组件只做转发，不再绘制 IMGUI；所有面板由场景里的 uGUI 对象呈现")]
        [SerializeField] WhiteboxCanvasHud canvasView;
        [Tooltip("调试用：即使已绑定 uGUI，也同时画一份 IMGUI（会出现两层 UI）")]
        [SerializeField] bool forceImGui;

        /// <summary>当前绑定的 Hierarchy uGUI 视图；null 表示回退到纯 IMGUI。</summary>
        public WhiteboxCanvasHud CanvasView => canvasView;
        public void BindCanvas(WhiteboxCanvasHud view) { canvasView = view; }

        public float FlashAlpha
        {
            get => canvasView != null ? canvasView.FlashAlpha : flashAlpha;
            set
            {
                flashAlpha = value;
                if (canvasView != null) canvasView.FlashAlpha = value;
            }
        }
        public bool DebugVisible { get; set; } = false;
        public bool SettlementOpen => settlement;

        public void Init(WhiteboxGame g)
        {
            game = g;
            floats.Clear();
            settlement = g.State.finished;
            hintLeft = 12f;
            if (canvasView != null) canvasView.Init(g, this);
        }

        public void FloatText(string text)
        {
            if (canvasView != null) { canvasView.FloatText(text); return; }
            floats.Add(new FloatLabel
            {
                Text = text,
                Left = 0f,
                Y = Screen.height * 0.5f - 36f,
                Color = game != null ? game.Config.HighlightColor : Color.white
            });
        }

        public void Notice(string text)
        {
            notice = text;
            noticeLeft = 2.4f;
            if (canvasView != null) canvasView.Notice(text);
        }

        public void ShowDeposit(float seconds)
        {
            depositLeft = seconds;
            if (canvasView != null) canvasView.ShowDeposit(seconds);
        }

        public void SetDurability(int left, int max)
        {
            durabilityLeft = left; durabilityMax = max;
            if (canvasView != null) canvasView.SetDurability(left, max);
        }

        public void OpenSettlement()
        {
            settlement = true;
            if (canvasView != null) canvasView.OpenSettlement();
        }

        public void CloseSettlement()
        {
            settlement = false;
            if (canvasView != null) canvasView.CloseSettlement();
        }

        void Update()
        {
            // 有 Hierarchy UI 时，浮字与计时由 WhiteboxCanvasHud 自己驱动，这里不重复跑
            if (canvasView != null) return;
            float dt = Time.deltaTime;
            if (noticeLeft > 0f) noticeLeft -= dt;
            if (depositLeft > 0f) depositLeft -= dt;
            if (hintLeft > 0f) hintLeft -= dt;
            for (int i = floats.Count - 1; i >= 0; i--)
            {
                FloatLabel f = floats[i];
                f.Left += dt / Mathf.Max(0.05f, game.Config.PickupFloatSeconds);
                f.Y -= dt * 40f;
                if (f.Left >= 1f) floats.RemoveAt(i);
                else floats[i] = f;
            }
        }

        void OnGUI()
        {
            // Hierarchy UI 已接管：完全不再画 IMGUI，避免两层面板叠加。
            // forceImGui 仅供对照排查，默认关闭。
            if (canvasView != null && !forceImGui) return;
            if (game == null || game.Config == null) return;
            WhiteboxConfig cfg = game.Config;
            WhiteboxState s = game.State;
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;

            // 常驻 UI：一个极小的准星圆点
            DrawRect(new Rect(cx - 2f, cy - 2f, 4f, 4f), cfg.HighlightColor);

            // 阶段升级白光（2.5 s 衰减）
            if (FlashAlpha > 0f)
                DrawRect(new Rect(0f, 0f, Screen.width, Screen.height),
                    new Color(1f, 1f, 1f, FlashAlpha));

            // 背包 >80% 的边框呼吸（#E2603C）
            int[] bagCaps = cfg.BackpackCapAt(s.bagLevel);
            int bagSum = 0, bagCap = 0;
            for (int i = 0; i < 4; i++) { bagSum += s.bag[i]; bagCap += bagCaps[i]; }
            if (bagCap > 0 && bagSum / (float)bagCap > 0.8f)
            {
                float a = 0.35f + 0.25f * Mathf.Sin(Time.time * 6f);
                Color warn = cfg.BagWarningColor; warn.a = a;
                DrawRect(new Rect(0f, 0f, Screen.width, 6f), warn);
                DrawRect(new Rect(0f, Screen.height - 6f, Screen.width, 6f), warn);
                DrawRect(new Rect(0f, 0f, 6f, Screen.height), warn);
                DrawRect(new Rect(Screen.width - 6f, 0f, 6f, Screen.height), warn);
            }

            // 大型垃圾耐久条（#F2C14E）
            if (durabilityMax > 0 && durabilityLeft > 0)
            {
                float w = 240f, h = 12f;
                DrawRect(new Rect(cx - w * 0.5f, cy + 42f, w, h), new Color(0.12f, 0.12f, 0.12f, 0.85f));
                DrawRect(new Rect(cx - w * 0.5f, cy + 42f, w * durabilityLeft / durabilityMax, h),
                    cfg.DurabilityColor);
            }

            for (int i = 0; i < floats.Count; i++)
            {
                FloatLabel f = floats[i];
                Color c = f.Color; c.a = 1f - f.Left;
                GUI.color = c;
                GUI.Label(new Rect(cx + 22f, f.Y, 160f, 26f), f.Text);
                GUI.color = Color.white;
            }

            if (noticeLeft > 0f)
            {
                GUI.color = new Color(0.95f, 0.95f, 0.95f, Mathf.Clamp01(noticeLeft));
                GUI.Label(new Rect(20f, Screen.height - 58f, Screen.width - 40f, 24f), notice);
                GUI.color = Color.white;
            }
            if (depositLeft > 0f)
                GUI.Label(new Rect(20f, Screen.height - 78f, Screen.width - 40f, 24f), "垃圾入站：漏斗下沉中…");

            if (hintLeft > 0f && !game.IsUiBlocking)
            {
                GUI.Label(new Rect(20f, 16f, Screen.width - 40f, 22f),
                    "WASD 移动 · 鼠标看视角 · 左键拾取/拆解 · F 抽奖 · M 中转站 · I 工具背包 · B 交易 · Q 切专精 · F5 新局 · F9 存档 · ESC 暂停");
            }

            if (DebugVisible && !game.IsUiBlocking)
                DrawDebugPanel(s, cfg);

            if (settlement) DrawSettlement(s);
            else if (game.IsPaused) DrawPause();
            else if (game.StationOpen) DrawStation(s, cfg);
        }

        void DrawDebugPanel(WhiteboxState s, WhiteboxConfig cfg)
        {
            GUILayout.BeginArea(new Rect(16f, 44f, 320f, 250f), GUI.skin.box);
            GUILayout.Label("白膜调试信息（F1 隐藏）");
            GUILayout.Label("背包 Lv." + s.bagLevel + " 总 " + s.BagTotal() + "/" + s.BagCapTotal(cfg)
                            + "  塑/金/有/电: " + s.bag[0] + "/" + s.bag[1] + "/" + s.bag[2] + "/" + s.bag[3]);
            GUILayout.Label("材料 板/金/肥/芯: " + s.materials[0] + "/" + s.materials[1] + "/"
                            + s.materials[2] + "/" + s.materials[3]);
            GUILayout.Label("工具 Lv." + s.toolLevel + " · 拾取 " + cfg.PickRange[s.toolLevel - 1] + " m"
                            + (game.Tool != null ? "｜" + game.Tool.ActiveSpecText() : "")
                            + " · 地表 " + game.Spawner.Active.Count + "/" + cfg.MapTrashCap
                            + " · 累计 " + s.totalSpawned + "/" + cfg.TotalTrashBudget
                            + (s.TrashBudgetSpent(cfg) ? "（已停止）" : ""));
            GUILayout.Label("土地 " + (s.landProgress * 100f).ToString("0") + "% · 水源 "
                            + (s.waterProgress * 100f).ToString("0") + "% · 生态阶段 " + (game.Eco.Stage + 1) + "/3");
            GUILayout.Label("任务 " + (System.Math.Min(s.questStep + 1, 10)) + "/10 ： " + game.Quest.CurrentText);
            GUILayout.Label("清理 " + s.collected + " · 加工 " + s.processed + " · 制造 " + s.crafted
                            + " · 用时 " + s.playTime.ToString("0") + " s");
            GUILayout.EndArea();
        }

        void DrawStation(WhiteboxState s, WhiteboxConfig cfg)
        {
            float w = 430f, h = 580f;
            Rect r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUILayout.BeginArea(r, GUI.skin.box);
            GUILayout.Label("中转站 2D 经营  ·  M 或 ESC 关闭");
            GUILayout.Label("已修复: " + (s.stationRepaired ? "是" : "否（先造 1 块再生建材板）")
                            + "  ·  背包 Lv." + s.bagLevel + "（总 " + s.BagTotal() + "/" + s.BagCapTotal(cfg) + "）");
            if (GUILayout.Button("① 投放背包全部垃圾")) game.StationLogic.DepositAll();

            GUILayout.Label("待加工垃圾  塑/金/有/电: " + s.raw[0] + "/" + s.raw[1] + "/" + s.raw[2] + "/" + s.raw[3]);
            for (int i = 0; i < 4; i++)
            {
                int cat = i;
                // IMGUI 回退没有弹窗：主按钮做 1 批，旁边的「全部」一次做满（等价于 uGUI 弹窗的一键全部）
                if (GUILayout.Button("② 加工" + StationSystem.CategoryNames[cat]
                                    + " ×" + cfg.MinProcessBatch[cat] + "  →  "
                                    + StationSystem.MaterialNames[cat]))
                    game.StationLogic.StartProcess(cat);
                if (GUILayout.Button("　── 一键全部加工" + StationSystem.CategoryNames[cat]
                                     + "（" + game.StationLogic.MaxProcessBatches(cat) + " 次）"))
                    game.StationLogic.StartProcessAll(cat);
            }
            GUILayout.Label("材料  塑板/金件/有机/芯: " + s.materials[0] + "/" + s.materials[1] + "/"
                            + s.materials[2] + "/" + s.materials[3]);
            for (int i = 0; i < 3; i++)
            {
                int recipe = i;
                if (GUILayout.Button("③ 制造 " + StationSystem.RecipeNames[recipe]
                                    + "（" + game.StationLogic.CostText(recipe) + "）"))
                    game.StationLogic.StartCraft(recipe);
                if (GUILayout.Button("　── 一键全部制造" + StationSystem.RecipeNames[recipe]
                                     + "（" + game.StationLogic.MaxCraftCount(recipe) + " 次）"))
                    game.StationLogic.StartCraftAll(recipe);
            }
            if (GUILayout.Button("④ 升级工具（Lv." + s.toolLevel + " → " + (s.toolLevel + 1) + "）"))
                game.StationLogic.UpgradeTool();
            if (GUILayout.Button("⑤ " + game.StationLogic.BagUpgradeText().Replace("⑤ ", "")))
                game.StationLogic.UpgradeBackpack();

            if (game.StationLogic.HasJob) DrawBar(game.StationLogic.JobLabel, game.StationLogic.JobProgress, cfg.DurabilityColor);
            GUILayout.EndArea();
        }

        void DrawPause()
        {
            float w = 280f, h = 170f;
            GUILayout.BeginArea(new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h), GUI.skin.box);
            GUILayout.Label("已暂停（后台加工/制造计时一并冻结）");
            if (GUILayout.Button("继续（ESC）")) game.SetPaused(false);
            if (GUILayout.Button("新的一局")) game.Restart();
            if (GUILayout.Button("退出")) Application.Quit();
            GUILayout.EndArea();
        }

        void DrawSettlement(WhiteboxState s)
        {
            float w = 380f, h = 260f;
            GUILayout.BeginArea(new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h), GUI.skin.box);
            GUILayout.Label("结算 · 恢复星球");
            GUILayout.Label("垃圾清理总数：" + s.collected);
            GUILayout.Label("材料加工总数：" + s.processed);
            GUILayout.Label("环保产品制造总数：" + s.crafted);
            GUILayout.Label("土地修复进度：" + (s.landProgress * 100f).ToString("0") + "%");
            GUILayout.Label("水源修复进度：" + (s.waterProgress * 100f).ToString("0") + "%");
            GUILayout.Label("总游戏时间：" + s.playTime.ToString("0.0") + " s");
            if (GUILayout.Button("重开一局")) game.Restart();
            if (GUILayout.Button("退出")) Application.Quit();
            GUILayout.EndArea();
        }

        static void DrawBar(string label, float t, Color c)
        {
            GUILayout.Label(label + "  " + (t * 100f).ToString("0") + "%");
            Rect r = GUILayoutUtility.GetRect(200f, 12f);
            DrawRect(new Rect(r.x, r.y, r.width, r.height), new Color(0.12f, 0.12f, 0.12f, 0.85f));
            DrawRect(new Rect(r.x, r.y, r.width * Mathf.Clamp01(t), r.height), c);
        }

        static void DrawRect(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }
}
