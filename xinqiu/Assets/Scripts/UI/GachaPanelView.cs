// 新增玩法：垃圾堆隐藏款 + 场景内老虎机抽奖（尚未纳入 design-freeze 编号）
// 改造说明：由原「圆形转盘常驻界面」改为「走近老虎机按 F 弹出的小型三转轮面板」。
// 结算入口仍是 GachaSystem.Draw()，奖品/权重/保底沿用 GachaConfig，本文件只负责转轮表现与状态复位。
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 场景老虎机弹出的小型 2D 面板：三转轮滚动 → 依次停止 → 奖励结算。
    /// UI 对象由 GachaBuilder 用 Editor API 生成到 UI_Whitebox 下；本脚本 Awake 按名字查找并运行时绑定按钮。
    /// 面板默认失活，只由 GachaSystem.Open()（老虎机 F 交互）显示；关闭必须走 Hide() 以彻底复位转轮状态。
    /// </summary>
    public sealed class GachaPanelView : MonoBehaviour
    {
        const string PanelName = "Panel_Gacha";
        const int ReelCount = 3;
        static readonly string[] RaritySlots = { "普通", "精英", "史诗" };

        GachaSystem system;
        bool bound;

        RectTransform panel;
        Text txtTickets;
        Text txtDraws;
        Text txtPity;
        Text txtResult;
        Text txtHint;
        Button btnDraw;
        Button btnClose;

        // 三个转轮：每个转轮是「窗口 → 视图内容」，视图内容沿 Y 滚动；符号行由 Builder 预置为 9 行循环带。
        readonly RectTransform[] reelViews = new RectTransform[ReelCount];
        readonly float[] reelRestY = new float[ReelCount];
        readonly Text[] reelCenterLabels = new Text[ReelCount]; // 每个转轮中央窗口的奖品名文本

        Coroutine spinRoutine;
        bool spinning;
        bool pendingRefresh;

        // 转轮滚动参数：必须与 GachaBuilder.BuildReels 的行高/行数一致，否则落点错位。
        const float ReelRowHeight = 58f;
        const int ReelRows = 9;
        const float SpinSeconds = 1.35f;     // 总滚动时长
        const float StaggerSeconds = 0.22f;  // 三个转轮依次停止的错开间隔

        public bool IsOpen => panel != null && panel.gameObject.activeSelf;
        public bool IsSpinning => spinning;

        void Awake()
        {
            Bind();
        }

        bool Bind()
        {
            if (bound) return true;
            Transform root = transform;
            panel = root.Find(PanelName) as RectTransform;
            if (panel == null)
            {
                Debug.LogWarning("[GachaPanelView] 未找到 " + PanelName + "，请先跑 Tools/垃圾星球白膜/⑨ 生成转盘配置与老虎机");
                return false;
            }

            txtTickets = FindText(panel, "Txt_Tickets");
            txtDraws = FindText(panel, "Txt_Draws");
            txtPity = FindText(panel, "Txt_Pity");
            txtResult = FindText(panel, "Txt_Result");
            txtHint = FindText(panel, "Txt_Hint");
            btnDraw = FindButton(panel, "Btn_Draw");
            btnClose = FindButton(panel, "Btn_Close");

            for (int r = 0; r < ReelCount; r++)
            {
                reelViews[r] = FindRt(panel, "Reel_View_" + r);
                if (reelViews[r] != null) reelRestY[r] = reelViews[r].anchoredPosition.y;
                reelCenterLabels[r] = FindText(panel, "Reel_Center_" + r);
            }

            if (btnDraw != null) btnDraw.onClick.AddListener(OnDrawClick);
            if (btnClose != null) btnClose.onClick.AddListener(OnCloseClick);

            panel.gameObject.SetActive(false);
            bound = true;
            return true;
        }

        public void Init(GachaSystem s)
        {
            system = s;
            if (!Bind()) return;
            RefreshTexts(0, 0, 0, 0, null, null, false);
            ResetReels();
        }

        void OnDrawClick()
        {
            if (system != null && !spinning) system.Draw();
        }

        void OnCloseClick()
        {
            if (system != null) system.Close();
        }

        public void Show()
        {
            if (!Bind()) return;
            // 每次打开都从干净状态开始：清掉上一次的转轮落点与"旋转中"文案
            ResetReels();
            panel.gameObject.SetActive(true);
        }

        /// <summary>
        /// 关闭复位：停转轮协程、清滚动状态、恢复按钮与转轮初始位置。
        /// 这是"关闭后正确重置面板状态"的唯一实现，GachaSystem.Close() 会调用它。
        /// </summary>
        public void Hide()
        {
            if (!Bind()) return;
            if (spinRoutine != null) { StopCoroutine(spinRoutine); spinRoutine = null; }
            spinning = false;
            pendingRefresh = false;
            ResetReels();
            if (btnDraw != null) btnDraw.interactable = true;
            panel.gameObject.SetActive(false);
        }

        public void Refresh(int tickets, int draws, int pity, int hardPity,
            GachaDrawResult result, WhiteboxState state, bool epicPitySuspended = false)
        {
            if (!Bind()) return;
            RefreshTexts(tickets, draws, pity, hardPity, result, state, true, epicPitySuspended);
        }

        void RefreshTexts(int tickets, int draws, int pity, int hardPity,
            GachaDrawResult result, WhiteboxState state, bool fromSystem, bool epicPitySuspended = false)
        {
            if (txtTickets != null) txtTickets.text = "抽奖券  ×" + tickets;
            if (txtDraws != null) txtDraws.text = "已抽  " + draws + " 次";
            if (txtPity != null)
            {
                string p = epicPitySuspended
                    ? "史诗保底：修复中转站后开启"
                    : (hardPity > 0 ? "距史诗保底  " + Mathf.Max(0, hardPity - pity) + " 抽"
                                    : "距史诗保底  未启用");
                txtPity.text = p + "　（连续未出 " + pity + "）";
            }
            if (txtResult != null)
            {
                if (result != null && result.reward != null)
                {
                    int owned = result.ownedCount > 0 ? result.ownedCount
                                : (state != null ? state.RewardCount(result.reward.id) : 1);
                    string extra = "";
                    if (result.isBook)
                        extra = result.maxed ? " · 已满层" : " · " + Mathf.Max(1, result.stacks) + " 层";
                    if (result.refundTickets > 0) extra += " · 重复返 +1 券";
                    if (result.overflowMaterial > 0) extra += " · +1 材料";
                    string ownedLine = result.isBook
                        ? "层数 " + Mathf.Max(1, result.stacks)
                        : (result.isNew ? "★ 新获得！" : "已有 ×" + owned);
                    if (!string.IsNullOrEmpty(result.message)) ownedLine += "　" + result.message;
                    txtResult.text = "【" + result.RarityName + "】" + result.KindName + "：" + result.reward.displayName + extra
                                     + "\n" + ownedLine;
                    txtResult.color = Hex(result.Color);
                }
                else if (result != null)
                {
                    txtResult.text = "奖品池为空，请在 GachaConfig 配置奖品";
                    txtResult.color = Color.white;
                }
                else
                {
                    txtResult.text = "走近老虎机，按 F 抽奖\n奖品来自垃圾堆发现的隐藏款券";
                    txtResult.color = new Color(0.86f, 0.87f, 0.84f, 1f);
                }
            }
            if (txtHint != null)
            {
                txtHint.text = tickets > 0 ? "点【SPIN】消耗 1 券　·　F / ESC 关闭" : "没有券：去捡垃圾找隐藏款　·　F / ESC 关闭";
            }
            bool allowDraw = !spinning && (!pendingRefresh || fromSystem);
            if (btnDraw != null) btnDraw.interactable = tickets > 0 && allowDraw;
        }

        /// <summary>抽奖结算回调：GachaSystem.Draw() 写好 lastResult 后调用，播放三转轮滚动。</summary>
        public void PlaySpin(GachaDrawResult result)
        {
            if (!Bind() || result == null) return;
            if (spinRoutine != null) StopCoroutine(spinRoutine);
            pendingRefresh = true;
            spinRoutine = StartCoroutine(Spin(result));
        }

        IEnumerator Spin(GachaDrawResult result)
        {
            spinning = true;
            if (btnDraw != null) btnDraw.interactable = false;
            if (txtResult != null) { txtResult.text = "转轮滚动中…"; txtResult.color = new Color(0.86f, 0.87f, 0.84f, 1f); }

            // 三个转轮的停止时刻错开，形成左→中→右依次定格的节奏
            float[] stopAt = new float[ReelCount];
            float[] targetY = new float[ReelCount];
            float[] settleFrom = new float[ReelCount];
            bool[] settling = new bool[ReelCount];
            for (int r = 0; r < ReelCount; r++)
            {
                stopAt[r] = SpinSeconds - (ReelCount - 1 - r) * StaggerSeconds;
                // 目标落点：行 0 对应 restY，行 idx 对应 restY + row*idx（行号从上往下增大）。
                int idx = RarityRowIndex(result.rarity, r);
                targetY[r] = reelRestY[r] + ReelRowHeight * idx;
            }

            const float settleSeconds = 0.30f; // 停止后收尾缓动时长
            float t = 0f;
            while (t < SpinSeconds + settleSeconds + 0.02f)
            {
                t += Time.unscaledDeltaTime;
                for (int r = 0; r < ReelCount; r++)
                {
                    if (reelViews[r] == null) continue;
                    if (t >= stopAt[r])
                    {
                        // 以"停下的那一刻位置"为起点缓动到目标行（easeOutCubic）。
                        // 起点在首次进入收尾时记录一次，避免每帧从初始位置重算导致跳变。
                        if (!settling[r]) { settleFrom[r] = reelViews[r].anchoredPosition.y; settling[r] = true; }
                        float k = Mathf.Clamp01((t - stopAt[r]) / settleSeconds);
                        float e = 1f - Mathf.Pow(1f - k, 3f);
                        reelViews[r].anchoredPosition = new Vector2(reelViews[r].anchoredPosition.x, Mathf.Lerp(settleFrom[r], targetY[r], e));
                    }
                    else
                    {
                        // 滚动期：视图上移使行号快速增大（视觉上符号向上滚），循环回卷成连续带。
                        // 用 unscaledDeltaTime，暂停/结算时不受 timeScale 影响。
                        float y = reelViews[r].anchoredPosition.y + ReelRowHeight * 14f * Time.unscaledDeltaTime;
                        float span = ReelRowHeight * ReelRows;
                        float maxY = reelRestY[r] + (ReelRows - 1) * ReelRowHeight; // 行 8 对齐时的视图 y
                        if (y > maxY) y -= span;
                        reelViews[r].anchoredPosition = new Vector2(reelViews[r].anchoredPosition.x, y);
                    }
                }
                yield return null;
            }

            // 定格：精确写到目标行，并同步中央窗口文字/颜色
            for (int r = 0; r < ReelCount; r++)
            {
                if (reelViews[r] != null)
                    reelViews[r].anchoredPosition = new Vector2(reelViews[r].anchoredPosition.x, targetY[r]);
                if (reelCenterLabels[r] != null)
                {
                    reelCenterLabels[r].text = RaritySlots[(int)result.rarity];
                    reelCenterLabels[r].color = Hex(result.Color);
                }
            }
            spinning = false;
            spinRoutine = null;
            pendingRefresh = false;

            // 转完再显示完整结算（Draw 里已写入 lastResult）
            if (system != null) system.RefreshPanel();
        }

        /// <summary>
        /// 把稀有度映射到符号行（0..8）。必须与 GachaBuilder.ReelSymbols 一致：
        /// 普通占 0/1/2/4/5/6，精英占 3/7，史诗占 8；三转轮错开行号避免整齐撞行。
        /// </summary>
        static int RarityRowIndex(GachaRarity rarity, int reel)
        {
            if (rarity == GachaRarity.Epic) return 8;
            if (rarity == GachaRarity.Rare) return reel == 1 ? 7 : 3;
            int[] commonRows = { 0, 2, 5 };
            return commonRows[reel % commonRows.Length];
        }

        /// <summary>复位三个转轮到初始行与默认文字，清掉上一次抽奖的落点。</summary>
        void ResetReels()
        {
            for (int r = 0; r < ReelCount; r++)
            {
                if (reelViews[r] != null)
                    reelViews[r].anchoredPosition = new Vector2(reelViews[r].anchoredPosition.x, reelRestY[r]);
                if (reelCenterLabels[r] != null)
                {
                    reelCenterLabels[r].text = RaritySlots[r == 1 ? 1 : 0];
                    reelCenterLabels[r].color = new Color(0.86f, 0.87f, 0.84f, 1f);
                }
            }
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

        static RectTransform FindRt(Transform root, string name)
        {
            Transform t = FindDeep(root, name);
            return t != null ? t as RectTransform : null;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) if (all[i].name == name) return all[i];
            return null;
        }

        static Color Hex(string s)
        {
            Color c;
            if (ColorUtility.TryParseHtmlString(s, out c)) return c;
            return Color.white;
        }
    }
}
