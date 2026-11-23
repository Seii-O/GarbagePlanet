// @trace UI-008
using System;
using UnityEngine;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 任务日志面板控制器：按「10_任务日志.png」展示十步任务链。
    /// 只读 State / QuestChain / Config，不写玩法数据；按钮转调 WhiteboxGame 的既有入口。
    /// </summary>
    public sealed class QuestLogPanelView : MonoBehaviour
    {
        public const string PanelName = "Panel_QuestLog";

        // 十步任务链，与 QuestChain.Steps / Titles / MaxProgress / Hints 长度保持一致。
        const int TaskCount = 10;

        static readonly string[] Titles =
        {
            "初次探索", "初次回收", "拆解废弃物", "返回中转站", "修复中转站",
            "完成垃圾加工", "制作环保产品", "修复土地", "净化水源", "恢复星球"
        };
        static readonly string[] Descs =
        {
            "移动并接近垃圾", "拾取 5 个垃圾", "完成一次大型垃圾拆解", "与中转站交互", "制作 1 块再生建材板",
            "加工四种垃圾", "制作生态肥料", "完成土地修复", "完成水源修复", "完成最终目标"
        };
        static readonly int[] MaxProgress = { 1, 5, 1, 1, 1, 4, 1, 1, 1, 1 };
        static readonly string[][] Hints =
        {
            new[] { "① 移动一段距离", "② 靠近任意垃圾", "③ 完成首次拾取" },
            new[] { "① 找到散落的垃圾", "② 左键拾取", "③ 累计 5 个" },
            new[] { "① 找到大型垃圾", "② 对准并按住左键拆解", "③ 完成 1 次拆解" },
            new[] { "① 回到中转站附近", "② 按 M 打开中转站", "③ 完成交互" },
            new[] { "① 收集塑料与金属垃圾", "② 加工出金属零件", "③ 制造 1 块再生建材板" },
            new[] { "① 打开材料加工页", "② 四类垃圾各加工一次", "③ 材料自动入库" },
            new[] { "① 准备有机原料", "② 打开产品制造页", "③ 制造 1 个生态肥料包" },
            new[] { "① 制造生态肥料包", "② 前往土地修复点", "③ 达到 MVP 进度" },
            new[] { "① 制造水质过滤器", "② 前往水源修复点", "③ 达到 MVP 进度" },
            new[] { "① 继续净化周边", "② 扩张净化区", "③ 覆盖整颗星球" }
        };

        WhiteboxGame game;
        GameObject panel;
        bool bound;
        [SerializeField] QuestLogArtSet art;
        [SerializeField] Font font;

        public bool IsOpen => panel != null && panel.activeSelf;

        void Awake() { Resolve(); }

        public void Wire(WhiteboxGame g) { game = g; bound = false; Resolve(); }

        public void SetArt(QuestLogArtSet a, Font f) { art = a; font = f; }

        void Update()
        {
            if (IsOpen) Refresh();
        }

        bool Resolve()
        {
            if (bound && panel != null) return true;
            if (game == null) game = FindObjectOfType<WhiteboxGame>();
            Transform found = FindDeep(transform, PanelName);
            if (found == null)
            {
                QuestLogUiFactory.Build(transform, art, font);
                found = FindDeep(transform, PanelName);
                if (found == null) return false;
            }
            panel = found.gameObject;
            BindClick("Btn_QuestClose", Hide);
            BindClick("Btn_GoStation", GoStation);
            BindClick("Btn_ViewSettlement", ViewSettlement);
            panel.SetActive(false);
            bound = true;
            return true;
        }

        public void Show()
        {
            if (!Resolve()) return;
            panel.SetActive(true);
            Refresh();
            if (game != null) game.ApplyUiState();
        }

        public void Hide()
        {
            if (panel == null) return;
            panel.SetActive(false);
            if (game != null) game.ApplyUiState();
        }

        void GoStation()
        {
            Hide();
            if (game != null) game.ToggleStation();
        }

        void ViewSettlement()
        {
            if (game == null || game.State == null || !game.State.finished)
            {
                if (game != null) game.Notice("完成全部任务后才能查看结算");
                return;
            }
            Hide();
            if (game != null) game.OpenSettlement(); // 走独立结算面板；旧场景缺面板时内部降级到 HUD
        }

        // ---------------- 动态刷新 ----------------
        void Refresh()
        {
            if (game == null || game.State == null || panel == null) return;
            WhiteboxState s = game.State;
            WhiteboxConfig cfg = game.Config;
            QuestChain q = game.Quest;
            bool finished = s.finished;
            int step = Mathf.Clamp(s.questStep, 0, TaskCount);

            int done = finished ? TaskCount : step;
            int active = finished ? 0 : (step < TaskCount ? 1 : 0);
            int locked = Mathf.Max(0, TaskCount - done - active);

            SetText(panel.transform, "Txt_DoneBig", done.ToString("00"));
            SetText(panel.transform, "Txt_DoneTotal", "/ " + TaskCount);
            SetStat(0, done.ToString());
            SetStat(1, active.ToString());
            SetStat(2, locked.ToString());
            for (int i = 0; i < TaskCount; i++)
                SetDotColor(FindDeep(panel.transform, "Dot_Progress_" + i),
                    i < done ? new Color(0.85f, 0.55f, 0.18f, 1f)
                    : (i == done && !finished ? new Color(0.17f, 0.66f, 0.60f, 1f)
                    : new Color(0.86f, 0.82f, 0.74f, 1f)));

            // 当前任务卡
            if (finished)
            {
                SetText(panel.transform, "Txt_CurrentTask", "全部完成");
                SetText(panel.transform, "Txt_Hint1", "① 星球已恢复");
                SetText(panel.transform, "Txt_Hint2", "② 可查看结算");
                SetText(panel.transform, "Txt_Hint3", "③ —");
                SetText(panel.transform, "Txt_CurrentProgress", "已完成 3 / 3 步");
                for (int i = 0; i < 3; i++) SetDotColor(FindDeep(panel.transform, "Dot_Step_" + i), new Color(0.17f, 0.66f, 0.60f, 1f));
            }
            else
            {
                int idx = Mathf.Clamp(step, 0, TaskCount - 1);
                SetText(panel.transform, "Txt_CurrentTask", Titles[idx]);
                SetText(panel.transform, "Txt_Hint1", Hints[idx][0]);
                SetText(panel.transform, "Txt_Hint2", Hints[idx][1]);
                SetText(panel.transform, "Txt_Hint3", Hints[idx][2]);
                int sub = CurrentSubSteps(idx, s, cfg, game);
                SetText(panel.transform, "Txt_CurrentProgress", "已完成 " + sub + " / 3 步");
                for (int i = 0; i < 3; i++)
                    SetDotColor(FindDeep(panel.transform, "Dot_Step_" + i),
                        i < sub ? new Color(0.85f, 0.55f, 0.18f, 1f) : new Color(0.86f, 0.82f, 0.74f, 1f));
            }

            // 生态进度
            float target = cfg != null ? Mathf.Max(0.01f, cfg.MvpProgressTarget) : 0.3f;
            float eco = (Mathf.Clamp01(s.landProgress / target) + Mathf.Clamp01(s.waterProgress / target)) * 0.5f;
            int pct = finished ? 100 : Mathf.Clamp(Mathf.RoundToInt(eco * 100f), 0, 100);
            SetText(panel.transform, "Txt_EcoPct", pct + " %");
            Transform land = FindDeep(panel.transform, "Eco_Land");
            Transform water = FindDeep(panel.transform, "Eco_Water");
            SetText(land, "Txt_Detail", "生态肥料包 " + s.fertilizer + " / 10");
            SetText(water, "Txt_Detail", "水质过滤器 " + s.filters + " / 10");

            // 十行任务
            for (int i = 0; i < TaskCount; i++)
            {
                Transform row = FindDeep(panel.transform, "Row_Quest_" + i);
                if (row == null) continue;
                bool isDone = finished || (q != null ? q.IsStepDone(i) : i < step);
                bool isCurrent = !isDone && i == step && !finished;
                int progress = StepProgress(i, s, cfg, game);
                int max = MaxProgress[i];

                SetImageSprite(row, "Img_State_" + i, isDone ? art?.stepDone : (isCurrent ? art?.stepCurrent : art?.stepDefault));
                SetActive(FindDeep(row, "Txt_Index_" + i), !isDone && !isCurrent);
                SetActive(FindDeep(row, "Img_Mark_" + i), isDone || isCurrent);
                if (isDone) SetImageSprite(row, "Img_Mark_" + i, art?.icCheck, new Color(0.17f, 0.66f, 0.60f, 1f));
                else if (isCurrent) SetImageSprite(row, "Img_Mark_" + i, art?.icArrow, new Color(0.85f, 0.55f, 0.18f, 1f));

                SetText(row, "Txt_Title_" + i, Titles[i]);
                SetText(row, "Txt_Desc_" + i, Descs[i]);
                SetText(row, "Txt_Count_" + i, Mathf.Min(progress, max) + " / " + max);
                SetText(row, "Txt_Status_" + i, isDone ? "完成" : (isCurrent ? "进行中" : "未解锁"));
                SetTextColor(row, "Txt_Status_" + i, isDone ? new Color(0.17f, 0.66f, 0.60f, 1f)
                    : (isCurrent ? new Color(0.85f, 0.55f, 0.18f, 1f) : new Color(0.58f, 0.53f, 0.45f, 1f)));
                SetImageSprite(row, null, isCurrent ? art?.highlightRow : art?.listRow);

                for (int k = 0; k < 5; k++)
                {
                    Transform dot = FindDeep(row, "Dot_" + i + "_" + k);
                    SetActive(dot, k < max);
                    if (k < max)
                        SetDotColor(dot, k < progress ? new Color(0.17f, 0.66f, 0.60f, 1f)
                            : new Color(0.86f, 0.82f, 0.74f, 1f));
                }
            }

            // 结算按钮
            SetButtonSprite("Btn_ViewSettlement", finished ? art?.btnSecondary : art?.btnDisabled);
            SetButtonLabelColor("Btn_ViewSettlement", finished ? new Color(0.16f, 0.13f, 0.09f, 1f)
                : new Color(0.58f, 0.53f, 0.45f, 1f));
        }

        static int BitCount(int mask)
        {
            int n = 0;
            for (int i = 0; i < 4; i++) if ((mask & (1 << i)) != 0) n++;
            return n;
        }

        static int StepProgress(int i, WhiteboxState s, WhiteboxConfig cfg, WhiteboxGame game)
        {
            switch (i)
            {
                case 0: return (s.movedDistance >= 3f || s.collected > 0) ? 1 : 0;
                case 1: return Mathf.Clamp(s.collected, 0, 5);
                case 2: return Mathf.Clamp(s.dismantled, 0, 1);
                case 3: return s.visitedStation ? 1 : 0;
                case 4: return (s.boards >= 1 && s.stationRepaired) ? 1 : 0;
                case 5: return BitCount(s.processedMask);
                case 6: return s.fertilizer >= 1 ? 1 : 0;
                case 7: return cfg != null && s.landProgress >= cfg.MvpProgressTarget - 0.0001f ? 1 : 0;
                case 8: return cfg != null && s.waterProgress >= cfg.MvpProgressTarget - 0.0001f ? 1 : 0;
                case 9:
                    if (game != null && game.Purification != null) return game.Purification.FullyCovered ? 1 : 0;
                    return (cfg != null && s.landProgress >= cfg.MvpProgressTarget - 0.0001f
                            && s.waterProgress >= cfg.MvpProgressTarget - 0.0001f) ? 1 : 0;
                default: return 0;
            }
        }

        static int CurrentSubSteps(int step, WhiteboxState s, WhiteboxConfig cfg, WhiteboxGame game)
        {
            switch (step)
            {
                case 0: return StepProgress(0, s, cfg, game);
                case 1: return Mathf.Clamp(Mathf.CeilToInt(Mathf.Clamp(s.collected, 0, 5) / 5f * 3f), 0, 3);
                case 2: return StepProgress(2, s, cfg, game);
                case 3: return StepProgress(3, s, cfg, game);
                case 4: return (s.materials[1] > 0 ? 1 : 0) + ((s.processedMask & 0x3) != 0 ? 1 : 0) + (s.boards > 0 ? 1 : 0);
                case 5: return Mathf.Clamp(BitCount(s.processedMask), 0, 3);
                case 6: return StepProgress(6, s, cfg, game);
                case 7: return StepProgress(7, s, cfg, game);
                case 8: return StepProgress(8, s, cfg, game);
                case 9: return StepProgress(9, s, cfg, game);
                default: return 0;
            }
        }

        void SetStat(int i, string value)
        {
            Transform box = FindDeep(panel.transform, "Stat_" + i);
            SetText(box, "Txt_Num", value);
        }

        void BindClick(string name, Action call)
        {
            Button b = FindButton(panel.transform, name);
            if (b == null) return;
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(() => call());
        }

        void SetButtonSprite(string name, Sprite sprite)
        {
            if (art == null || sprite == null) return;
            Transform t = FindDeep(panel.transform, name);
            Image img = t != null ? t.GetComponent<Image>() : null;
            if (img != null && img.sprite != sprite) img.sprite = sprite;
        }

        void SetButtonLabelColor(string name, Color color)
        {
            Transform t = FindDeep(panel.transform, name);
            Text label = t != null ? t.GetComponentInChildren<Text>(true) : null;
            if (label != null) label.color = color;
        }

        static void SetActive(Transform t, bool active)
        {
            if (t != null && t.gameObject.activeSelf != active) t.gameObject.SetActive(active);
        }

        static void SetDotColor(Transform dot, Color color)
        {
            Image img = dot != null ? dot.GetComponent<Image>() : null;
            if (img != null) img.color = color;
        }

        static void SetImageSprite(Transform root, string name, Sprite sprite, Color? tint = null)
        {
            Transform t = name == null ? root : FindDeep(root, name);
            Image img = t != null ? t.GetComponent<Image>() : null;
            if (img == null || sprite == null) return;
            img.sprite = sprite;
            if (tint.HasValue) img.color = tint.Value;
        }

        static void SetText(Transform root, string name, string value)
        {
            if (root == null) return;
            Text t = FindText(root, name);
            if (t != null && t.text != value) t.text = value;
        }

        static void SetTextColor(Transform root, string name, Color color)
        {
            Text t = FindText(root, name);
            if (t != null) t.color = color;
        }

        static Text FindText(Transform root, string name)
        {
            Transform t = FindDeep(root, name);
            return t != null ? t.GetComponent<Text>() : null;
        }

        static Button FindButton(Transform root, string name)
        {
            Transform t = FindDeep(root, name);
            return t != null ? t.GetComponent<Button>() : null;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) if (all[i].name == name) return all[i];
            return null;
        }
    }
}
