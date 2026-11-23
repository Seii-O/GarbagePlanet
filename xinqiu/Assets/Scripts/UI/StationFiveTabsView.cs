// @trace UI-007
using System;
using UnityEngine;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 五页签中转站面板控制器（01 垃圾接收 / 02 垃圾分类 / 03 材料加工 / 04 产品制造 / 05 工具升级）。
    ///
    /// 视觉：五个 Page 全由真实 uGUI 控件拼成（Image 拆件 / Text / Button），页签选中态真实切换。
    /// 数据：面板打开时每帧把 Game.State / StationLogic / WhiteboxConfig 的实时数值刷到具名 Text/Image。
    /// 边界：只读 State / StationLogic / Config，不写玩法数据；按钮转发到现有 StationSystem API。
    /// </summary>
    public sealed class StationFiveTabsView : MonoBehaviour
    {
        public const string PanelName = "Panel_StationFiveTabs";
        public const int TabCount = 5;

        static readonly string[] CatNames = { "塑料", "金属", "有机", "电子" };
        static readonly string[] ItemNames = { "塑料瓶", "塑料袋", "易拉罐", "铁片", "苹果核", "枯叶", "电池", "电路板" };
        static readonly string[] MaterialNames = { "再生塑料", "金属零件", "有机原料", "电子元件" };
        static readonly string[] ProductNames = { "生态肥料包", "再生建材板", "水质过滤器" };

        // 产品卡顺序按参考图：肥料 / 建材板 / 过滤器；StationSystem 配方索引为 建材板 / 肥料 / 过滤器。
        static readonly int[] CardToRecipe = { 1, 0, 2 };
        // 每张产品卡的材料行（材料索引，需求数），与 Factory 的 matCount 对齐。
        static readonly int[][] CardMatIndex = { new[] { 2 }, new[] { 0, 1 }, new[] { 0, 1, 3 } };
        static readonly int[][] CardMatCost = { new[] { 3 }, new[] { 2, 1 }, new[] { 2, 2, 1 } };

        WhiteboxGame game;
        GameObject panel;
        readonly GameObject[] pages = new GameObject[TabCount];
        readonly GameObject[] tabObjs = new GameObject[TabCount];
        readonly Button[] tabs = new Button[TabCount];
        bool bound;
        bool wasActive;
        int currentTab = -1;

        // Editor 构建脚本注入并序列化：美术拆件资源集 + 字体，供运行时兜底重建使用。
        [SerializeField] StationArtSet art;
        [SerializeField] Font font;

        public bool IsOpen => panel != null && panel.activeSelf;

        void Awake() { Resolve(); }

        public void Wire(WhiteboxGame g) { game = g; bound = false; Resolve(); }

        /// <summary>Editor 注入美术资源集与字体，持久化到场景。</summary>
        public void SetArt(StationArtSet a, Font f)
        {
            art = a; font = f;
        }

        void Update()
        {
            if (!bound) Resolve();
            if (panel == null || game == null) return;
            bool show = game.StationOpen && !game.IsPaused
                        && game.State != null && !game.State.finished;
            if (panel.activeSelf != show) panel.SetActive(show);
            if (show)
            {
                if (!wasActive) SelectTab(0);
                RefreshValues();
            }
            wasActive = show;
        }

        bool Resolve()
        {
            if (bound && panel != null) return true;
            if (game == null) game = FindObjectOfType<WhiteboxGame>();

            Transform root = transform;
            Transform found = FindDeep(root, PanelName);
            if (found == null)
            {
                StationFiveTabsUiFactory.Build(root, art, font);
                found = FindDeep(root, PanelName);
                if (found == null)
                {
                    Debug.LogWarning("[Station5Tab] 未找到 " + PanelName
                                     + "，请跑 Tools/垃圾星球白膜/㉑ 生成五页签中转站");
                    return false;
                }
            }
            panel = found.gameObject;
            Transform pagesRoot = FindDeep(panel.transform, "Pages");
            for (int i = 0; i < TabCount; i++)
            {
                pages[i] = pagesRoot != null ? FindChild(pagesRoot, "Page_" + i) : FindChild(panel.transform, "Page_" + i);
                tabObjs[i] = FindChild(panel.transform, "Btn_Tab_" + i);
                tabs[i] = tabObjs[i] != null ? tabObjs[i].GetComponent<Button>() : null;
            }

            for (int i = 0; i < TabCount; i++) BindTab("Btn_Tab_" + i, i);
            BindClick("Btn_StationClose", Close);
            BindClick("Btn_ClosePage", Close);

            // 01 垃圾接收
            for (int i = 0; i < 4; i++) BindClick("Btn_Deposit_" + i, Deposit);
            BindClick("Btn_DepositAll", Deposit);
            BindClick("Btn_ConfirmIn", Deposit);
            // 02 垃圾分类
            for (int i = 0; i < 4; i++) BindCategory("Btn_Sort_" + i, i);
            BindClick("Btn_SortAll", ProcessAll);
            // 03 材料加工
            for (int i = 0; i < 4; i++)
            {
                BindCategory("Btn_Process_" + i, i);
                BindClick("Btn_Minus_" + i, () => AdjustInput(0, -1));
                BindClick("Btn_Plus_" + i, () => AdjustInput(0, +1));
            }
            BindClick("Btn_ProcessAll", ProcessAll);
            // 04 产品制造
            for (int i = 0; i < 3; i++) BindRecipe("Btn_Craft_" + i, i);
            BindClick("Btn_GoRepair", GoRepair);
            // 05 工具升级
            BindClick("Btn_Upgrade", UpgradeTool);

            panel.SetActive(false);
            wasActive = false;
            bound = true;
            currentTab = -1;
            return true;
        }

        /// <summary>切页并同步页签选中态（真实 sprite + 文字颜色）。</summary>
        public void SelectTab(int index)
        {
            index = Mathf.Clamp(index, 0, TabCount - 1);
            currentTab = index;
            for (int i = 0; i < TabCount; i++)
            {
                if (pages[i] != null) pages[i].SetActive(i == index);
                bool sel = i == index;
                if (tabObjs[i] != null)
                {
                    Image bg = tabObjs[i].GetComponent<Image>();
                    if (bg != null && art != null)
                        bg.sprite = sel ? art.tabSelected : art.tabNormal;
                    Text no = FindText(tabObjs[i].transform, "Txt_TabNo");
                    Text name = FindText(tabObjs[i].transform, "Txt_TabName");
                    Color c = sel ? new Color(0.17f, 0.66f, 0.60f, 1f) : new Color(0.42f, 0.36f, 0.28f, 1f);
                    if (no != null) no.color = c;
                    if (name != null) { name.color = c; name.fontStyle = sel ? FontStyle.Bold : FontStyle.Normal; }
                }
            }
        }

        // ---------------- 动态数值刷新 ----------------
        void RefreshValues()
        {
            if (game == null || game.State == null) return;
            WhiteboxState s = game.State;
            WhiteboxConfig cfg = game.Config;
            StationSystem st = game.StationLogic;

            // ---- 01 垃圾接收 ----
            if (currentTab == 0)
            {
                int[] cap = cfg != null ? cfg.BackpackCapAt(s.bagLevel) : new[] { 10, 8, 8, 4 };
                int used = 0, total = 0, rawSum = 0;
                for (int i = 0; i < 4; i++)
                {
                    used += s.bag[i]; total += cap[i]; rawSum += s.raw[i];
                    Transform bagRow = FindDeep(panel.transform, "Row_Bag_" + i);
                    SetText(bagRow, "Txt_Count", s.bag[i].ToString());
                    SetText(bagRow, "Txt_Cap", "/ " + cap[i]);
                    RefreshDots(bagRow, "Dots_" + i, s.bag[i], cap[i]);
                    Transform rawRow = FindDeep(panel.transform, "Row_Raw_" + i);
                    SetText(rawRow, "Txt_Count", s.raw[i].ToString());
                }
                SetText(panel.transform, "Txt_BagSum", used + " / " + total);
                SetText(panel.transform, "Txt_BagFree", (total - used) + " / " + total);
                SetText(panel.transform, "Txt_RawSum", rawSum.ToString());
                SetText(panel.transform, "Txt_RawTotal", rawSum.ToString());
                SetText(panel.transform, "Txt_BottomLeft", "本次可接收 " + used + " 件 · 仓库合计 " + rawSum + " 件");
            }
            // ---- 02 垃圾分类 ----
            else if (currentTab == 1)
            {
                for (int c = 0; c < 4; c++)
                {
                    Transform card = FindDeep(panel.transform, "Card_Cat_" + c);
                    SetText(card, "Txt_CatSum", s.raw[c].ToString());
                    for (int k = 0; k < 2; k++)
                    {
                        int item = c * 2 + k;
                        Transform row = FindDeep(panel.transform, "Row_Item_" + item);
                        SetText(row, "Txt_ItemCount", s.GarbageItemCount(item).ToString());
                    }
                }
            }
            // ---- 03 材料加工 ----
            else if (currentTab == 2)
            {
                float seconds = cfg != null ? cfg.ProcessSeconds : 5f;
                for (int i = 0; i < 4; i++)
                {
                    Transform row = FindDeep(panel.transform, "Row_Proc_" + i);
                    SetText(row, "Txt_Stock", "库存 " + s.raw[i]);

                    int batch = cfg != null ? cfg.MinProcessBatch[Mathf.Clamp(i, 0, 3)] : 5;
                    int input = batch;
                    if (st != null && st.HasPending && st.PendingAction == StationAction.Process && st.PendingIndex == i)
                        input = st.PendingCount * batch;
                    SetText(row, "Txt_Input_" + i, input.ToString());
                    int gain = Mathf.Max(1, Mathf.FloorToInt(batch * (i == 2 ? 1f : 0.5f)));
                    SetText(row, "Txt_ProductGain", "× " + gain);

                    Image fill = FindImage(row, "Img_ProgFill");
                    if (st != null && st.HasJob && st.PendingIndex == i)
                    {
                        if (fill != null) fill.fillAmount = st.JobProgress;
                        SetText(row, "Txt_Prog", st.JobLabel + " " + st.JobLeft.ToString("0.0") + "s / " + st.JobTotal.ToString("0.0") + "s");
                    }
                    else
                    {
                        if (fill != null) fill.fillAmount = 0f;
                        SetText(row, "Txt_Prog", "待机 · 0.0s / " + seconds.ToString("0.0") + "s");
                    }
                    SetText(row, "Txt_Ratio", "转化比例 " + (i == 2 ? "1 : 1" : "2 : 1"));
                    SetText(row, "Txt_Ratio2", "单批 " + batch + " 件 · " + seconds.ToString("0") + "秒");
                }
                Transform stock = FindDeep(panel.transform, "MatStock");
                for (int i = 0; i < 4; i++)
                    SetText(stock, "Txt_Mat_" + i, MaterialNames[i] + " " + s.materials[i]);
            }
            // ---- 04 产品制造 ----
            else if (currentTab == 3)
            {
                for (int i = 0; i < 3; i++)
                {
                    int recipe = CardToRecipe[i];
                    int[] idx = CardMatIndex[i], need = CardMatCost[i];
                    for (int m = 0; m < idx.Length; m++)
                    {
                        Transform mr = FindDeep(panel.transform, "Row_Mat_" + i + "_" + m);
                        if (mr == null) continue;
                        SetText(mr, "Txt_Mat", MaterialNames[idx[m]]);
                        SetText(mr, "Txt_Need", s.materials[idx[m]] + " / " + need[m]);
                    }
                    bool enough = true;
                    for (int m = 0; m < idx.Length; m++) if (s.materials[idx[m]] < need[m]) enough = false;
                    if (recipe > 0 && !s.stationRepaired) enough = false;
                    string label = st != null && st.HasJob ? "制作中" : (enough ? "开始制造" : "材料不足");
                    SetButtonLabel(panel.transform, "Btn_Craft_" + i, label);
                    SetButtonSprite("Btn_Craft_" + i, enough ? art?.btnPrimary : art?.btnDisabled);
                    if (cfg != null)
                    {
                        Transform productCard = FindDeep(panel.transform, "Card_Product_" + i);
                        SetText(productCard, "Txt_Time", cfg.CraftSeconds[Mathf.Clamp(recipe, 0, 2)].ToString("0") + " 秒");
                    }
                }

                Transform cur = FindDeep(panel.transform, "Card_Current");
                if (st != null && st.HasJob)
                {
                    SetText(cur, "Txt_CurState", "进行中");
                    SetText(cur, "Txt_CurName", st.JobLabel);
                    SetText(cur, "Txt_RingPct", Mathf.RoundToInt(st.JobProgress * 100) + "%");
                    SetText(cur, "Txt_CurTime", "剩余 " + st.JobLeft.ToString("0.0") + "s / " + st.JobTotal.ToString("0.0") + "s");
                }
                else
                {
                    SetText(cur, "Txt_CurState", "空闲");
                    SetText(cur, "Txt_CurName", "—");
                    SetText(cur, "Txt_RingPct", "0%");
                    SetText(cur, "Txt_CurTime", "剩余时间 0.0s");
                }

                int[] finished = { s.fertilizer, s.boards, s.filters };
                for (int i = 0; i < 3; i++)
                {
                    Transform row = FindDeep(panel.transform, "Row_Finished_" + i);
                    SetText(row, "Txt_Count", "× " + finished[i]);
                }
            }
            // ---- 05 工具升级 ----
            else if (currentTab == 4)
            {
                float[] pick = cfg != null ? cfg.PickRange : new[] { 2f, 3f, 4f };
                int[] dmg = cfg != null ? cfg.DismantleDamage : new[] { 1, 1, 2 };
                for (int i = 0; i < 3; i++)
                {
                    int lv = i + 1;
                    Transform card = FindDeep(panel.transform, "Card_Tool_" + i);
                    if (card != null)
                    {
                        SetText(FindDeep(card, "Row_Spec_" + i + "_0"), "Txt_Value", pick[Mathf.Clamp(lv - 1, 0, pick.Length - 1)].ToString("0") + " 米");
                        SetText(FindDeep(card, "Row_Spec_" + i + "_1"), "Txt_Value", (lv - 1) + " 米");
                        SetText(FindDeep(card, "Row_Spec_" + i + "_2"), "Txt_Value", dmg[Mathf.Clamp(lv - 1, 0, dmg.Length - 1)] + " 点/次");
                        SetText(FindDeep(card, "Row_Spec_" + i + "_3"), "Txt_Value", i == 2 ? "升级后开启" : "—");
                    }
                    string btn = lv <= s.toolLevel ? (lv == s.toolLevel ? "当前装备" : "已解锁") : "待解锁";
                    SetButtonLabel(panel.transform, "Btn_Tool_" + i, btn);
                }

                SetText(panel.transform, "Txt_UpPath", "升级路径 / lv." + s.toolLevel + " → lv." + Mathf.Min(3, s.toolLevel + 1) + " → lv.3");
                if (s.toolLevel >= 3)
                {
                    SetText(panel.transform, "Txt_UpState", "已满级");
                    SetText(panel.transform, "Txt_UpHint", "工具已达到最高等级 Lv.3");
                    SetText(panel.transform, "Txt_CostMat", "金属零件 — / —");
                    SetText(panel.transform, "Txt_CostElec", "电子元件 — / —");
                    SetButtonLabel(panel.transform, "Btn_Upgrade", "已满级");
                    SetButtonSprite("Btn_Upgrade", art?.btnDisabled);
                }
                else if (cfg != null)
                {
                    int ci = Mathf.Clamp(s.toolLevel - 1, 0, cfg.UpgradeMetalCost.Length - 1);
                    int metal = cfg.UpgradeMetalCost[ci], elec = cfg.UpgradeElectronicCost[ci];
                    SetText(panel.transform, "Txt_CostMat", "金属零件 " + s.materials[1] + " / " + metal);
                    SetText(panel.transform, "Txt_CostElec", "电子元件 " + s.materials[3] + " / " + elec);
                    bool enough = s.materials[1] >= metal && s.materials[3] >= elec && s.stationRepaired;
                    SetText(panel.transform, "Txt_UpState", enough ? "可升级" : "材料不足");
                    SetText(panel.transform, "Txt_UpHint", enough ? "点击右侧按钮升级回收器" : "返回星球上收集更多材料");
                    SetButtonLabel(panel.transform, "Btn_Upgrade", "升级");
                    SetButtonSprite("Btn_Upgrade", enough ? art?.btnSecondary : art?.btnDisabled);
                }
            }
        }

        void RefreshDots(Transform row, string prefix, int count, int cap)
        {
            if (row == null || cap <= 0) return;
            int lit = Mathf.RoundToInt((float)count / cap * 10f);
            for (int i = 0; i < 10; i++)
            {
                Image d = FindImage(row, prefix + "_" + i);
                if (d == null) continue;
                d.color = i < lit ? new Color(0.85f, 0.55f, 0.18f, 1f) : new Color(0.86f, 0.82f, 0.74f, 1f);
            }
        }

        // ---------------- 功能转发 ----------------
        void Deposit()
        {
            if (game != null && game.StationLogic != null) game.StationLogic.DepositAll();
        }
        void Process(int cat)
        {
            if (game == null || game.StationLogic == null) return;
            if (game.HasQuantityDialog) game.StationLogic.BeginProcess(cat);
            else game.StationLogic.StartProcessAll(cat);
        }
        void ProcessAll()
        {
            if (game == null || game.StationLogic == null) return;
            for (int i = 0; i < 4; i++) game.StationLogic.StartProcessAll(i);
        }
        void Craft(int cardIndex)
        {
            if (game == null || game.StationLogic == null) return;
            int recipe = CardToRecipe[Mathf.Clamp(cardIndex, 0, 2)];
            if (game.HasQuantityDialog) game.StationLogic.BeginCraft(recipe);
            else game.StationLogic.StartCraftAll(recipe);
        }
        void UpgradeTool()
        {
            if (game != null && game.StationLogic != null) game.StationLogic.UpgradeTool();
        }
        void GoRepair()
        {
            if (game != null) game.Notice("成品已入库存；请到星球上的修复点放置使用");
        }
        void AdjustInput(int cat, int delta)
        {
            if (game != null) game.Notice("投入数量：" + (delta > 0 ? "+" : "-") + "1（详细数量请在加工弹窗调整）");
        }
        void Close()
        {
            if (game != null && game.StationOpen) game.ToggleStation();
        }

        // ---------------- 绑定 / 查找辅助 ----------------
        void BindTab(string name, int index)
        {
            Button b = FindButton(panel.transform, name);
            if (b == null) return;
            int idx = index;
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(() => SelectTab(idx));
        }
        void BindClick(string name, Action call)
        {
            Button b = FindButton(panel.transform, name);
            if (b == null) return;
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(() => call());
        }
        void BindCategory(string name, int cat)
        {
            int c = cat;
            BindClick(name, () => Process(c));
        }
        void BindRecipe(string name, int cardIndex)
        {
            int c = cardIndex;
            BindClick(name, () => Craft(c));
        }
        static void SetText(Transform root, string name, string value)
        {
            if (root == null) return;
            Text t = FindText(root, name);
            if (t != null && t.text != value) t.text = value;
        }
        static void SetButtonLabel(Transform root, string name, string value)
        {
            Transform t = FindDeep(root, name);
            if (t == null) return;
            Text label = t.GetComponentInChildren<Text>(true);
            if (label != null && label.text != value) label.text = value;
        }
        void SetButtonSprite(string name, Sprite sprite)
        {
            if (art == null || sprite == null) return;
            Transform t = FindDeep(panel.transform, name);
            Image img = t != null ? t.GetComponent<Image>() : null;
            if (img != null && img.sprite != sprite) img.sprite = sprite;
        }
        static Text FindText(Transform root, string name)
        {
            Transform t = FindDeep(root, name);
            return t != null ? t.GetComponent<Text>() : null;
        }
        static Image FindImage(Transform root, string name)
        {
            Transform t = FindDeep(root, name);
            return t != null ? t.GetComponent<Image>() : null;
        }
        static Button FindButton(Transform root, string name)
        {
            Transform t = FindDeep(root, name);
            return t != null ? t.GetComponent<Button>() : null;
        }
        static GameObject FindChild(Transform root, string name)
        {
            Transform t = FindDeep(root, name);
            return t != null ? t.gameObject : null;
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
