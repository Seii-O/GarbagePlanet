// @trace UI-007
using System;
using UnityEngine;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox
{
    /// <summary>
    /// 中转站面板控制器（四页签：01 垃圾接收 / 02 材料加工 / 03 产品制造 / 04 人物升级）。
    ///
    /// v2（2026-11-25 按新 UI 定稿）：原「02 垃圾分类」整块下线，页签索引全部前移一位。
    /// 解析页/页签时用 <see cref="IndexedChild"/>：先精确匹配 Page_1，再按「前缀+序号」排序取第 n 个，
    /// 这样场景里手改过命名（例如只剩 Page_0/2/3/4）也能按视觉顺序正确对应，不会被旧结构顶掉。
    ///
    /// 视觉：四个 Page 全由真实 uGUI 控件拼成（Image 拆件 / Text / Button），页签选中态真实切换。
    /// 数据：面板打开时每帧把 Game.State / StationLogic / WhiteboxConfig 的实时数值刷到具名 Text/Image。
    /// 边界：只读 State / StationLogic / Config，不写玩法数据；按钮转发到现有 StationSystem API。
    /// </summary>
    public sealed class StationFiveTabsView : MonoBehaviour
    {
        public const string PanelName = "Panel_StationFiveTabs";
        public const int TabCount = 4;

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
        readonly Outline[] processingFocus = new Outline[4];

        // Editor 构建脚本注入并序列化：美术拆件资源集 + 字体，供运行时兜底重建使用。
        [SerializeField] StationArtSet art;
        public StationArtSet Art => art;
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
                pages[i] = pagesRoot != null ? IndexedChild(pagesRoot, "Page_", i) : IndexedChild(panel.transform, "Page_", i);
                tabObjs[i] = IndexedChild(panel.transform, "Btn_Tab_", i);
                tabs[i] = tabObjs[i] != null ? tabObjs[i].GetComponent<Button>() : null;
            }

            for (int i = 0; i < TabCount; i++) BindTab(i);
            BindClick("Btn_StationClose", Close);
            // 每页底部栏各有一个「取消/关闭」（Page_0/2/3/4 下同名 Btn_ClosePage）。
            // FindDeep 只返回第一个匹配，只绑第一个会让 02 材料加工 / 03 产品制造 / 04 人物升级 的取消完全没反应，
            // 所以这里按名字逐个绑定全部实例；名字被手工改成 Btn_Cancel* 之类也能兜住。
            int closeBound = BindEveryButton(IsCloseButton, Close);
            Debug.Log("[Station5Tab] 关闭/取消按钮已绑定 " + closeBound + " 个（含顶栏 Btn_StationClose 与各页 BottomBar）");

            // 01 垃圾接收：逐类存入只动该类别，一键存入才全部投放
            for (int i = 0; i < 4; i++)
            {
                int cat = i;
                BindClick("Btn_Deposit_" + i, () => DepositCategory(cat));
            }
            BindClick("Btn_DepositAll", DepositAll);
            BindClick("Btn_ConfirmIn", DepositAll);
            // 02 材料加工
            for (int i = 0; i < 4; i++)
            {
                int cat = i;
                BindCategory("Btn_Process_" + i, i);
                BindClick("Btn_Minus_" + i, () => AdjustInput(cat, -1));
                BindClick("Btn_Plus_" + i, () => AdjustInput(cat, +1));
            }
            BindClick("Btn_ProcessAll", ProcessAll);
            // 03 产品制造
            for (int i = 0; i < 3; i++) BindRecipe("Btn_Craft_" + i, i);
            BindClick("Btn_GoRepair", GoRepair);
            // 04 人物升级
            BindPlayerUpgrade();
            BindMaterialShortcut("Card_CostMat", "Txt_CostMatLabel", "金属零件", OpenMetalProcessing);
            BindMaterialShortcut("Card_CostElec", "Txt_CostElecLabel", "电子元件", OpenElectronicProcessing);

            panel.SetActive(false);
            wasActive = false;
            bound = true;
            currentTab = -1;
            return true;
        }

        /// <summary>切页并同步页签选中态（真实 sprite + 文字颜色）。</summary>
        public void SelectTab(int index)
        {
            foreach (Outline outline in processingFocus)
                if (outline != null) outline.enabled = false;
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
                    // 旧场景删过一页后页码会跳号（01/03/04/05），运行时按当前顺序校正；
                    // 只在文本是纯数字且不等于期望值时改，非数字写法（一/二/三…）保持你手写的样子
                    if (no != null)
                    {
                        string want = (i + 1).ToString("00");
                        int parsed;
                        if (no.text != want && int.TryParse(no.text, out parsed)) no.text = want;
                    }
                    // C27：场景里烘焙的旧页签名「工具升级」运行时覆盖为「人物升级」
                    if (i == TabCount - 1 && name != null && name.text != PlayerLevelTabName)
                        name.text = PlayerLevelTabName;
                }
            }
        }
        const string PlayerLevelTabName = "人物升级";

        void BindMaterialShortcut(string cardName, string labelName, string label,
            UnityEngine.Events.UnityAction open)
        {
            Transform card = FindDeep(pages[3]?.transform, cardName);
            Image background = card != null ? card.GetComponent<Image>() : null;
            if (background == null) return;

            background.raycastTarget = true;
            Button button = card.GetComponent<Button>();
            if (button == null) button = card.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.RemoveListener(open);
            button.onClick.AddListener(open);
            SetText(card, labelName, label + " ›");
        }

        void OpenMetalProcessing() => OpenMaterialProcessing(1 << 1);
        void OpenElectronicProcessing() => OpenMaterialProcessing(1 << 3);

        void OpenMaterialProcessing(int categories)
        {
            if (pages[1] == null) return;
            SelectTab(1);

            Button firstProcess = null;
            for (int category = 0; category < processingFocus.Length; category++)
            {
                if ((categories & (1 << category)) == 0) continue;
                Transform row = FindDeep(pages[1].transform, "Row_Proc_" + category);
                Image background = row != null ? row.GetComponent<Image>() : null;
                if (background != null)
                {
                    Outline outline = background.GetComponent<Outline>();
                    if (outline == null) outline = background.gameObject.AddComponent<Outline>();
                    outline.effectColor = new Color(0.17f, 0.66f, 0.60f, 1f);
                    outline.effectDistance = new Vector2(3f, -3f);
                    outline.useGraphicAlpha = false;
                    outline.enabled = true;
                    processingFocus[category] = outline;
                }
                if (firstProcess == null) firstProcess = FindButton(row, "Btn_Process_" + category);
            }

            if (firstProcess != null) firstProcess.Select();
            RefreshValues();
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
            // ---- 02 材料加工 ----
            else if (currentTab == 1)
            {
                float seconds = cfg != null ? cfg.ProcessSeconds : 5f;
                for (int i = 0; i < 4; i++)
                {
                    Transform row = FindDeep(panel.transform, "Row_Proc_" + i);
                    SetText(row, "Txt_Stock", "库存 " + s.raw[i]);

                    int batch = cfg != null ? cfg.MinProcessBatch[Mathf.Clamp(i, 0, 3)] : 5;
                    int input = batch;
                    int batches = 1;
                    if (st != null)
                    {
                        input = st.PlannedInput(i);
                        batches = st.PlannedBatches(i);
                    }
                    int runningBatches = st != null ? st.AllProcessingBatches(i) : 0;
                    if (runningBatches > 0) { input = batch * runningBatches; batches = runningBatches; }
                    SetText(row, "Txt_Input_" + i, input.ToString());
                    float ratio = cfg != null ? cfg.ConversionRate[i] : (i == 2 ? 1f : 0.5f);
                    int gain = Mathf.Max(1, Mathf.FloorToInt(batch * ratio));
                    SetText(row, "Txt_ProductGain", "× " + (gain * Mathf.Max(0, batches)));

                    Image fill = FindImage(row, "Img_ProgFill");
                    if (st != null && st.HasJob && (st.JobCategory == i || runningBatches > 0))
                    {
                        if (fill != null) fill.fillAmount = st.JobProgress;
                        string label = runningBatches > 0 ? "并行加工 " + runningBatches + " 批" : st.JobLabel;
                        SetText(row, "Txt_Prog", label + " " + st.JobLeft.ToString("0.0") + "s / " + st.JobTotal.ToString("0.0") + "s");
                    }
                    else
                    {
                        if (fill != null) fill.fillAmount = 0f;
                        int plan = Mathf.Max(0, batches);
                        float total = cfg != null && !cfg.BatchFixedDuration ? seconds * Mathf.Max(1, plan) : seconds;
                        SetText(row, "Txt_Prog", "待机 · 计划 " + plan + " 批 / 消耗 " + (plan * batch)
                                + " 件 · " + total.ToString("0.0") + "s");
                    }
                    SetText(row, "Txt_Ratio", "转化比例 " + (i == 2 ? "1 : 1" : "2 : 1"));
                    SetText(row, "Txt_Ratio2", "单批 " + batch + " 件 · " + seconds.ToString("0") + "秒");
                }
                Transform stock = FindDeep(panel.transform, "MatStock");
                for (int i = 0; i < 4; i++)
                    SetText(stock, "Txt_Mat_" + i, MaterialNames[i] + " " + s.materials[i]);
            }
            // ---- 03 产品制造 ----
            else if (currentTab == 2)
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
                    bool missing = MissingMaterials(s, i) != 0;
                    bool locked = recipe > 0 && !s.stationRepaired;
                    bool busy = st != null && st.HasJob;
                    string label = missing ? "材料不足 · 去加工" : locked ? "需修复中转站" : busy ? "制作中" : "开始制造";
                    string buttonName = "Btn_Craft_" + i;
                    SetButtonLabel(panel.transform, buttonName, label);
                    SetButtonSprite(buttonName, missing || (!locked && !busy) ? art?.btnPrimary : art?.btnDisabled);
                    Button craftButton = FindButton(panel.transform, buttonName);
                    if (craftButton != null) craftButton.interactable = missing || (!locked && !busy);
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
            // ---- 04 人物升级（C27：工具下线，等级接管加成；经验达标 + 材料消耗才升级）----
            else if (currentTab == 3)
            {
                PlayerLevelSystem pl = game != null ? game.PlayerLevel : null;
                int lv = pl != null ? pl.Level : Mathf.Max(1, s.playerLevel);
                int maxLv = pl != null ? pl.MaxLevel : (cfg != null ? cfg.PlayerLevelMax : 5);

                // 标题等级徽章 + 经验条（累计 / 下一级门槛，进度按 PlayerLevelSystem.Progress）
                SetText(panel.transform, "Txt_Level", "Lv." + lv);
                int needTotal = (pl != null && !pl.IsMax && cfg != null) ? cfg.PlayerLevelExpAt(lv + 1) : s.playerExpTotal;
                SetText(panel.transform, "Txt_ExpValue", s.playerExpTotal + " / " + needTotal);
                Image expFill = FindImage(panel.transform, "Img_ExpFill");
                if (expFill != null) expFill.fillAmount = pl != null ? pl.Progress : 0f;

                // 三张卡展示：当前级 / 下一级 / 满级的加成摘要
                string[] cardTitles = { "当前等级", "下一等级", "满级目标" };
                string[] labels = { "拾取距离", "拆解效率", "加工耗时", "零耗时次数" };
                for (int i = 0; i < 3; i++)
                {
                    int showLv = i == 0 ? lv : (i == 1 ? Mathf.Min(lv + 1, maxLv) : maxLv);
                    Transform card = FindDeep(panel.transform, "Card_Tool_" + i);
                    if (card != null)
                    {
                        SetText(card, "Txt_ToolName_" + i, cardTitles[i] + "  Lv." + showLv);
                        float range = cfg != null ? cfg.PlayerPickRangeAt(showLv) : 4f;
                        int dmg = cfg != null ? cfg.PlayerDismantleDamageAt(showLv) : 1;
                        float proc = cfg != null ? cfg.PlayerProcessMultAt(showLv) : 1f;
                        int free = pl != null ? pl.FreeChargeAt(showLv) : 0;
                        string[] values = {
                            range.ToString("0.0") + " 米",
                            dmg + " 点/次",
                            Mathf.RoundToInt(proc * 100) + "% 耗时",
                            free > 0 ? ("零耗时 +" + free) : "—"
                        };
                        for (int r = 0; r < 4; r++)
                        {
                            Transform row = FindDeep(card, "Row_Spec_" + i + "_" + r);
                            SetText(row, "Txt_Label", labels[r]);
                            SetText(row, "Txt_Value", values[r]);
                        }
                    }
                }

                if (pl == null || pl.IsMax)
                {
                    SetText(panel.transform, "Txt_UpState", "已满级");
                    SetText(panel.transform, "Txt_UpHint", "人物已达最高等级 Lv." + maxLv);
                    SetText(panel.transform, "Txt_CostMat", "需 — / 持有 " + s.materials[1]);
                    SetText(panel.transform, "Txt_CostElec", "需 — / 持有 " + s.materials[3]);
                    SetText(panel.transform, "Txt_BottomLeft", "人物已达最高等级 Lv." + maxLv);
                    SetButtonLabel(panel.transform, "Btn_Upgrade", "已满级");
                    SetButtonSprite("Btn_Upgrade", art?.btnDisabled);
                }
                else
                {
                    int metal = pl.UpgradeMetalCost, elec = pl.UpgradeElectronicCost;
                    string why;
                    bool can = pl.CanLevelUp(out why);
                    SetText(panel.transform, "Txt_CostMat", "需 " + metal + " / 持有 " + s.materials[1]);
                    SetText(panel.transform, "Txt_CostElec", "需 " + elec + " / 持有 " + s.materials[3]);
                    SetText(panel.transform, "Txt_UpState", can ? "可升级" : "条件不足");
                    string unlock = lv < maxLv
                        ? " · 背包 " + cfg.BackpackTotalCap(lv) + " → " + cfg.BackpackTotalCap(lv + 1) + " 格"
                        : "";
                    SetText(panel.transform, "Txt_UpHint", (can ? "点击下方按钮升级人物" : why) + unlock);
                    SetText(panel.transform, "Txt_BottomLeft", "等级 Lv." + lv + " / Lv." + maxLv
                            + "　累计经验 " + s.playerExpTotal + " → " + cfg.PlayerLevelExpAt(lv + 1));
                    SetButtonLabel(panel.transform, "Btn_Upgrade", "升级到 Lv." + (lv + 1));
                    SetButtonSprite("Btn_Upgrade", can ? art?.btnPrimary : art?.btnDisabled);
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
        /// <summary>逐类存入：只投这一类，其余类别保持不动。</summary>
        void DepositCategory(int cat)
        {
            if (game != null && game.StationLogic != null) game.StationLogic.DepositCategory(cat);
        }
        /// <summary>一键存入：四类全部投放。</summary>
        void DepositAll()
        {
            if (game != null && game.StationLogic != null) game.StationLogic.DepositAll();
        }
        void Process(int cat)
        {
            if (game == null || game.StationLogic == null) return;
            StationSystem st = game.StationLogic;
            int batches = st.PlannedBatches(cat);
            int batch = game.Config != null ? Mathf.Max(1, game.Config.MinProcessBatch[Mathf.Clamp(cat, 0, 3)]) : 1;
            if (st.PlannedInput(cat) <= 0)
            {
                game.Notice(StationSystem.CategoryNames[cat] + "投入数量为 0，请点 + 增加");
                return;
            }
            if (batches <= 0)
            {
                game.Notice(StationSystem.CategoryNames[cat] + "投入数量不满 1 批（单批 " + batch + " 件），请点 + 增加");
                return;
            }
            if (game.HasQuantityDialog) st.BeginProcess(cat, batches);
            else st.ExecuteProcess(cat, batches);
        }
        void ProcessAll()
        {
            if (game == null || game.StationLogic == null) return;
            game.StationLogic.StartProcessAllCategories();
        }
        static int MissingMaterials(WhiteboxState state, int cardIndex)
        {
            int missing = 0;
            int[] categories = CardMatIndex[cardIndex];
            for (int i = 0; i < categories.Length; i++)
                if (state.materials[categories[i]] < CardMatCost[cardIndex][i])
                    missing |= 1 << categories[i];
            return missing;
        }

        void Craft(int cardIndex)
        {
            if (game == null || game.State == null || game.StationLogic == null) return;
            cardIndex = Mathf.Clamp(cardIndex, 0, CardToRecipe.Length - 1);
            int missing = MissingMaterials(game.State, cardIndex);
            if (missing != 0)
            {
                OpenMaterialProcessing(missing);
                return;
            }
            int recipe = CardToRecipe[cardIndex];
            if (game.HasQuantityDialog) game.StationLogic.BeginCraft(recipe);
            else game.StationLogic.StartCraftAll(recipe);
        }
        void BindPlayerUpgrade()
        {
            Button button = FindButton(pages[3]?.transform, "Btn_Upgrade");
            if (button == null) return;
            // 旧场景保存的工具升级回调属于持久监听，RemoveListener 无法移除。
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                if (button.onClick.GetPersistentTarget(i) is WhiteboxUiActions
                    && button.onClick.GetPersistentMethodName(i) == nameof(WhiteboxUiActions.UpgradeTool))
                    button.onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
            button.onClick.RemoveListener(UpgradePlayer);
            button.onClick.AddListener(UpgradePlayer);
        }

        void UpgradePlayer()
        {
            if (game != null && game.PlayerLevel != null && game.PlayerLevel.TryLevelUp())
                RefreshValues();
        }
        void GoRepair()
        {
            if (game != null) game.Notice("成品已入库存；请到星球上的修复点放置使用");
        }
        void AdjustInput(int cat, int delta)
        {
            if (game == null || game.StationLogic == null) return;
            StationSystem st = game.StationLogic;
            if (game.State == null || game.State.raw[Mathf.Clamp(cat, 0, 3)] <= 0)
            {
                game.Notice(StationSystem.CategoryNames[cat] + "库存为 0，请先到 01 垃圾接收存入");
                return;
            }
            int before = st.PlannedInput(cat);
            int after = st.AdjustPlannedInput(cat, delta);
            if (after == before)
            {
                if (delta > 0) game.Notice(StationSystem.CategoryNames[cat] + "库存已全部投入（" + after + " 件）");
                else game.Notice(StationSystem.CategoryNames[cat] + "投入数量已是最低（0 件）");
                return;
            }
            game.Notice(StationSystem.CategoryNames[cat] + "投入数量 " + after + " 件（"
                        + st.PlannedBatches(cat) + " 批）");
        }
        void Close()
        {
            if (game != null && game.StationOpen) game.ToggleStation();
        }

        // ---------------- 绑定 / 查找辅助 ----------------
        void BindTab(int index)
        {
            if (index < 0 || index >= tabObjs.Length) return;
            GameObject go = tabObjs[index];
            Button b = go != null ? go.GetComponent<Button>() : null;
            if (b == null) return;
            int idx = index;
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(() => { SelectTab(idx); });
        }
        void BindClick(string name, Action call)
        {
            Button b = FindButton(panel.transform, name);
            if (b == null) return;
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(() => { call(); });
        }

        /// <summary>
        /// 同名按钮在多个页面各有一份时（如每页底部栏的 Btn_ClosePage），按条件逐个绑定全部实例。
        /// FindDeep 只返回第一个匹配，所以同名按钮不能走 BindClick。
        /// 返回实际绑定成功的个数，调用方据此判断是否有漏绑。
        /// </summary>
        int BindEveryButton(Func<string, bool> match, Action call)
        {
            if (panel == null || match == null) return 0;
            Transform[] all = panel.GetComponentsInChildren<Transform>(true);
            int hit = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (!match(all[i].name)) continue;
                Button b = all[i].GetComponent<Button>();
                if (b == null) continue;
                b.onClick.RemoveAllListeners();
                b.onClick.AddListener(() => { call(); });
                hit++;
            }
            return hit;
        }

        /// <summary>页脚「取消/关闭」按钮识别：Btn_ClosePage 优先，名字里带 Close/Cancel 的也认（抗手工改名）。</summary>
        static bool IsCloseButton(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name == "Btn_ClosePage") return true;
            return name.IndexOf("Close", StringComparison.OrdinalIgnoreCase) >= 0
                   || name.IndexOf("Cancel", StringComparison.OrdinalIgnoreCase) >= 0;
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

        /// <summary>
        /// 按「前缀 + 序号」取子物体，抗手工改名与跳号：
        /// 先找出同一父物体下所有「prefix + 纯数字」的节点（取数量最多的那一组，
        /// Page_* 在 Pages 下、Btn_Tab_* 在 Img_Panel 下，各取各的），再按数字升序取第 index 个。
        /// 所以 Page_0/2/3/4、Btn_Tab_0/2/3/4 这种删过一页的跳号命名，也能按视觉顺序正确对应。
        /// </summary>
        static GameObject IndexedChild(Transform root, string prefix, int index)
        {
            if (root == null || index < 0) return null;

            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            var groups = new System.Collections.Generic.Dictionary<Transform, System.Collections.Generic.List<Transform>>();
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t.parent == null || !HasIndexSuffix(t.name, prefix)) continue;
                System.Collections.Generic.List<Transform> list;
                if (!groups.TryGetValue(t.parent, out list))
                {
                    list = new System.Collections.Generic.List<Transform>();
                    groups[t.parent] = list;
                }
                list.Add(t);
            }
            if (groups.Count == 0) return null;

            // 同一父物体下数量最多的那组才是页/页签集合，避免把别的面板里的同名节点混进来
            System.Collections.Generic.List<Transform> best = null;
            foreach (var kv in groups)
                if (best == null || kv.Value.Count > best.Count) best = kv.Value;
            if (best == null) return null;

            best.Sort((a, b) => SuffixIndex(a.name, prefix).CompareTo(SuffixIndex(b.name, prefix)));
            return index < best.Count ? best[index].gameObject : null;
        }

        static bool HasIndexSuffix(string name, string prefix)
        {
            if (string.IsNullOrEmpty(name) || !name.StartsWith(prefix, StringComparison.Ordinal)) return false;
            int num;
            return int.TryParse(name.Substring(prefix.Length), out num);
        }

        static int SuffixIndex(string name, string prefix)
        {
            int num;
            return int.TryParse(name.Substring(prefix.Length), out num) ? num : int.MaxValue;
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
