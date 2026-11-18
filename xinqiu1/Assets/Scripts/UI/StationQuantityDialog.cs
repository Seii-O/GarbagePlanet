// @trace MECH-009, MECH-013, PLAT-005
using UnityEngine;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 中转站「数量选择」弹窗控制器。
    ///
    /// 解决的问题：一次加工/制造只处理 1 批时，库存几十件垃圾要点几十次按钮。
    /// 现在点「② 加工X」/「③ 制造X」只进入待确认状态（StationSystem.BeginProcess/BeginCraft），
    /// 本弹窗把「最多能做几次」交给玩家用滑块 / 输入框 / −+ / 一键全部选择，确认后执行一次连续任务。
    ///
    /// 红线：本组件只写文字、滑块值与按钮可交互状态，绝不动 Panel_Quantity 下的
    /// RectTransform、颜色、字体、字号（与 WhiteboxCanvasHud 同一套约定）。
    /// 显隐由 WhiteboxCanvasHud 每帧驱动（Sync），因此面板可以在场景里保持常驻失活，
    /// 不会出现「父面板关了弹窗还亮着」。
    /// </summary>
    public sealed class StationQuantityDialog : MonoBehaviour
    {
        [Header("装配引用（WhiteboxUiBuilder 写入；为空时按名字在子层级里找）")]
        public WhiteboxGame Game;
        public Text TxtTitle;
        public Text TxtInfo;
        public Text TxtValue;
        public Text TxtDetail;
        public Text TxtAll;
        public Slider Sld;
        public InputField Inp;
        public Button BtnConfirm;
        public Button BtnMinus;
        public Button BtnPlus;
        public Button BtnAll;
        public Button BtnCancel;

        bool wired;
        StationAction lastAction = StationAction.None;
        int lastIndex = -1;

        void Awake()
        {
            Wire();
        }

        /// <summary>
        /// 每帧由 WhiteboxCanvasHud 调用：stationVisible = 中转站面板当前是否可见。
        /// 完全没有「自己判断开没开」的分支，避免两层各维护一套状态。
        /// </summary>
        public void Sync(WhiteboxGame game, bool stationVisible)
        {
            if (game != null) Game = game;
            if (Game == null || Game.StationLogic == null) { Show(false); return; }

            StationSystem st = Game.StationLogic;
            bool show = stationVisible && st.HasPending;
            Show(show);
            if (!show) { lastAction = StationAction.None; lastIndex = -1; return; }

            Wire();
            if (st.PendingAction != lastAction || st.PendingIndex != lastIndex)
            {
                lastAction = st.PendingAction;
                lastIndex = st.PendingIndex;
                ApplyRange(st); // 换了操作类型：重建滑块区间与默认值
            }
            SyncRange(st);
            SyncWidgets(st);
            RefreshTexts(st);
        }

        // ---------------- 内部 ----------------

        void Show(bool on)
        {
            if (gameObject.activeSelf != on) gameObject.SetActive(on);
        }

        void ApplyRange(StationSystem st)
        {
            int max = Mathf.Max(1, st.PendingMax);
            if (Sld != null)
            {
                Sld.wholeNumbers = true;
                Sld.minValue = 1;
                Sld.maxValue = max;
                Sld.SetValueWithoutNotify(Mathf.Clamp(st.PendingCount, 1, max));
            }
            if (Inp != null && !Inp.isFocused) Inp.SetTextWithoutNotify(st.PendingCount.ToString());
        }

        /// <summary>材料在面板开着时被消耗（例如上一个任务完成）后，重新钳一次上限。</summary>
        void SyncRange(StationSystem st)
        {
            if (Sld == null) return;
            int max = Mathf.Max(1, st.PendingMax);
            if (!Mathf.Approximately(Sld.maxValue, max))
            {
                Sld.maxValue = max;
                Sld.SetValueWithoutNotify(Mathf.Clamp(st.PendingCount, 1, max));
            }
        }

        void SyncWidgets(StationSystem st)
        {
            int n = st.PendingCount;
            if (Sld != null && Mathf.RoundToInt(Sld.value) != n) Sld.SetValueWithoutNotify(n);
            if (Inp != null && !Inp.isFocused && Inp.text != n.ToString()) Inp.SetTextWithoutNotify(n.ToString());

            bool canRun = st.PendingMax > 0 && !st.HasJob;
            if (BtnConfirm != null) BtnConfirm.interactable = canRun;
            if (BtnAll != null) BtnAll.interactable = canRun;
            if (BtnMinus != null) BtnMinus.interactable = n > 1;
            if (BtnPlus != null) BtnPlus.interactable = n < st.PendingMax;
        }

        void RefreshTexts(StationSystem st)
        {
            SetText(TxtTitle, st.PendingTitle + " · 选择数量");
            SetText(TxtInfo, st.PendingMax > 0
                ? "可执行 1 ~ " + st.PendingMax + " 次  ·  预计 "
                  + st.PendingSeconds().ToString("0.#") + " s"
                  + (st.PendingFixedSeconds() ? "（做多少件都一样）" : "")
                : "材料/库存不足，无法执行");
            SetText(TxtValue, "× " + st.PendingCount);
            SetText(TxtDetail, st.HasJob ? "后台还有任务在跑，先等它完成" : st.PendingDetail);
            SetText(TxtAll, "一键全部（" + Mathf.Max(0, st.PendingMax) + "）");
        }

        static void SetText(Text t, string value)
        {
            if (t != null && t.text != value) t.text = value;
        }

        /// <summary>引用补齐 + 监听只挂一次；面板常驻失活时也能在首次激活就绪。</summary>
        void Wire()
        {
            if (wired) return;
            if (TxtTitle == null) TxtTitle = FindText("Txt_QtyTitle");
            if (TxtInfo == null) TxtInfo = FindText("Txt_QtyInfo");
            if (TxtValue == null) TxtValue = FindText("Txt_QtyValue");
            if (TxtDetail == null) TxtDetail = FindText("Txt_QtyDetail");
            if (TxtAll == null) TxtAll = ButtonText("Btn_QtyAll");
            if (Sld == null) Sld = Find<Slider>("Sld_Qty");
            if (Inp == null) Inp = Find<InputField>("Inp_Qty");
            if (BtnConfirm == null) BtnConfirm = Find<Button>("Btn_QtyConfirm");
            if (BtnMinus == null) BtnMinus = Find<Button>("Btn_QtyMinus");
            if (BtnPlus == null) BtnPlus = Find<Button>("Btn_QtyPlus");
            if (BtnAll == null) BtnAll = Find<Button>("Btn_QtyAll");
            if (BtnCancel == null) BtnCancel = Find<Button>("Btn_QtyCancel");

            if (Sld != null) Sld.onValueChanged.AddListener(OnSlider);
            if (Inp != null) Inp.onEndEdit.AddListener(OnInputEnd);
            if (BtnMinus != null) BtnMinus.onClick.AddListener(() => Step(-1));
            if (BtnPlus != null) BtnPlus.onClick.AddListener(() => Step(+1));
            if (BtnConfirm != null) BtnConfirm.onClick.AddListener(Confirm);
            if (BtnAll != null) BtnAll.onClick.AddListener(ConfirmAll);
            if (BtnCancel != null) BtnCancel.onClick.AddListener(Cancel);

            // 只挂一次：即使某个引用真的缺失也不能让 Sync 每帧重复 AddListener
            wired = true;
        }

        void OnSlider(float v)
        {
            if (Game == null || Game.StationLogic == null || !Game.StationLogic.HasPending) return;
            Game.StationLogic.PendingCount = Mathf.RoundToInt(v);
        }

        void OnInputEnd(string raw)
        {
            if (Game == null || Game.StationLogic == null || !Game.StationLogic.HasPending) return;
            StationSystem st = Game.StationLogic;
            int v;
            if (int.TryParse(raw, out v)) st.PendingCount = v;
            // 输入非法 / 越界时把控件拉回合法值（这里不受 isFocused 限制，否则会留下一个非法文本）
            if (Inp != null) Inp.SetTextWithoutNotify(st.PendingCount.ToString());
            SyncWidgets(st);
            RefreshTexts(st);
        }

        void Step(int delta)
        {
            if (Game == null || Game.StationLogic == null || !Game.StationLogic.HasPending) return;
            StationSystem st = Game.StationLogic;
            st.PendingCount = st.PendingCount + delta;
            SyncWidgets(st);
            RefreshTexts(st);
        }

        void Confirm()
        {
            if (Game == null || Game.StationLogic == null) return;
            Game.StationLogic.ConfirmPending();
            Show(false);
        }

        void ConfirmAll()
        {
            if (Game == null || Game.StationLogic == null) return;
            Game.StationLogic.ConfirmPendingAll();
            Show(false);
        }

        void Cancel()
        {
            if (Game != null && Game.StationLogic != null) Game.StationLogic.ClearPending();
            Show(false);
        }

        // ---------------- 查找工具 ----------------

        Text FindText(string name)
        {
            Transform t = FindTransform(name);
            return t != null ? t.GetComponent<Text>() : null;
        }

        Text ButtonText(string name)
        {
            Transform t = FindTransform(name);
            return t != null ? t.GetComponentInChildren<Text>(true) : null;
        }

        T Find<T>(string name) where T : Component
        {
            Transform t = FindTransform(name);
            return t != null ? t.GetComponent<T>() : null;
        }

        Transform FindTransform(string name)
        {
            if (transform.name == name) return transform;
            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) if (all[i].name == name) return all[i];
            return null;
        }
    }
}
