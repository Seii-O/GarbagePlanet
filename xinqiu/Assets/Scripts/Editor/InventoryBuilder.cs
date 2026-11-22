// @trace BAG-001, BAG-002, COIN-001, COIN-002, UI-004
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 独立背包（I）与独立金币交易（B）界面的一键生成 + 接线（菜单⑬）。
    /// 红线与 WhiteboxBuilder / GachaBuilder 一致：全部用 Editor API 生成，不手写 .unity / .asset YAML。
    /// 两套面板同一菜单生成，但各自独立运行：背包视图只读格子，交易视图只读余额/目录/流水，互不读取对方的显示数据。
    /// 重跑菜单会重建面板（存在即删），保证元素名与视图 Bind() 始终一致；配置资产仅在缺失时创建，不覆盖已调好的数值。
    /// </summary>
    public static class InventoryBuilder
    {
        const string BagCfgPath = "Assets/Data/ToolBagConfig.asset";
        const string CoinCfgPath = "Assets/Data/CoinTradeConfig.asset";
        const string UiRoot = "UI_Whitebox";
        const string SysRoot = "sys_whitebox";
        const string InvPanel = "Panel_Inventory";
        const string TradePanel = "Panel_CoinTrade";
        const string Tag = "[InventoryBuilder] ";

        static readonly Color PanelBg = new Color(0.06f, 0.12f, 0.11f, 0.97f);
        static readonly Color BtnBg = new Color(0.20f, 0.26f, 0.27f, 0.97f);
        static readonly Color SlotBg = new Color(0.12f, 0.20f, 0.18f, 0.96f);
        static readonly Color TextColor = new Color(0.96f, 0.97f, 0.94f, 1f);
        static readonly Color Accent = new Color(0.24f, 0.84f, 0.75f, 1f);
        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        [MenuItem("Tools/垃圾星球白膜/⑬ 生成背包与交易界面", false, 18)]
        public static void Build()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != WhiteboxBuilder.ScenePath)
            {
                Debug.LogWarning(Tag + "请先打开白膜场景 " + WhiteboxBuilder.ScenePath + " 再执行本菜单");
                return;
            }
            BuildInScene(scene, false);
        }

        /// <summary>批处理入口，供命令行 / CI 调用。</summary>
        public static void BuildBatch()
        {
            Scene scene = EditorSceneManager.OpenScene(WhiteboxBuilder.ScenePath, OpenSceneMode.Single);
            BuildInScene(scene, true);
        }

        static void BuildInScene(Scene scene, bool batch)
        {
            ToolBagConfig bagCfg = EnsureBagConfig();
            CoinTradeConfig coinCfg = EnsureCoinConfig();

            GameObject uiRoot = FindRoot(scene, UiRoot);
            GameObject sysRoot = FindRoot(scene, SysRoot);
            if (uiRoot == null || sysRoot == null)
            {
                Debug.LogError(Tag + "缺少 " + UiRoot + " 或 " + SysRoot + "，请先跑菜单 ①/④ 生成场景与 Hierarchy UI");
                return;
            }

            bool changed = false;
            // 面板重建：保证 Btn_Slot_XX / Btn_Buy_XX 等元素名与视图 Bind() 完全一致
            GameObject invOld = FindChild(uiRoot.transform, InvPanel);
            if (invOld != null) { UnityEngine.Object.DestroyImmediate(invOld); changed = true; }
            GameObject tradeOld = FindChild(uiRoot.transform, TradePanel);
            if (tradeOld != null) { UnityEngine.Object.DestroyImmediate(tradeOld); changed = true; }
            GameObject invPanel = BuildInventoryPanel(uiRoot.transform);
            GameObject tradePanel = BuildTradePanel(uiRoot.transform);
            changed = true;

            Font font = UiFont.Load();
            InventoryPanelView invView = uiRoot.GetComponent<InventoryPanelView>();
            if (invView == null) { invView = uiRoot.AddComponent<InventoryPanelView>(); changed = true; }
            CoinTradePanelView tradeView = uiRoot.GetComponent<CoinTradePanelView>();
            if (tradeView == null) { tradeView = uiRoot.AddComponent<CoinTradePanelView>(); changed = true; }

            ToolBagSystem bag = sysRoot.GetComponent<ToolBagSystem>();
            if (bag == null) { bag = sysRoot.AddComponent<ToolBagSystem>(); changed = true; }
            bag.Wire(bagCfg);
            bag.WireView(invView);

            CoinTradeSystem coin = sysRoot.GetComponent<CoinTradeSystem>();
            if (coin == null) { coin = sysRoot.AddComponent<CoinTradeSystem>(); changed = true; }
            coin.Wire(coinCfg);
            coin.WireBag(bag);
            coin.WireView(tradeView);

            WhiteboxGame game = sysRoot.GetComponent<WhiteboxGame>();
            if (game != null)
            {
                game.WireBag(bag);
                game.WireCoinTrade(coin);
                EditorUtility.SetDirty(game);
            }
            else Debug.LogWarning(Tag + "sys_whitebox 上未找到 WhiteboxGame，面板已生成但未接入启动链");

            EditorUtility.SetDirty(bag);
            EditorUtility.SetDirty(coin);
            EditorUtility.SetDirty(invPanel);
            EditorUtility.SetDirty(tradePanel);
            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
            Debug.Log(Tag + "完成：背包=" + UiRoot + "/" + InvPanel + "（I），交易=" + UiRoot + "/"
                      + TradePanel + "（B），系统=sys_whitebox/{ToolBagSystem,CoinTradeSystem}（batch=" + batch + "）");
        }

        static ToolBagConfig EnsureBagConfig()
        {
            ToolBagConfig cfg = AssetDatabase.LoadAssetAtPath<ToolBagConfig>(BagCfgPath);
            if (cfg != null) return cfg;
            cfg = ToolBagConfig.CreateDefault();
            AssetDatabase.CreateAsset(cfg, BagCfgPath);
            AssetDatabase.SaveAssets();
            Debug.Log(Tag + "已生成默认背包配置：" + BagCfgPath);
            return cfg;
        }

        static CoinTradeConfig EnsureCoinConfig()
        {
            CoinTradeConfig cfg = AssetDatabase.LoadAssetAtPath<CoinTradeConfig>(CoinCfgPath);
            if (cfg != null) return cfg;
            cfg = CoinTradeConfig.CreateDefault();
            AssetDatabase.CreateAsset(cfg, CoinCfgPath);
            AssetDatabase.SaveAssets();
            Debug.Log(Tag + "已生成默认交易配置：" + CoinCfgPath);
            return cfg;
        }

        // ----------------------------------------------------------------
        // 背包面板：12 格（4 列 × 3 行）+ 标题/容量/详情 + 放入/取出/关闭
        // ----------------------------------------------------------------
        static GameObject BuildInventoryPanel(Transform canvas)
        {
            Font font = UiFont.Load();
            GameObject panel = Panel(canvas, InvPanel, new Vector2(720f, 760f), Vector2.zero, PanelBg);

            Txt(panel.transform, "Txt_InvTitle", "工具与技能书背包", new Vector2(0f, 344f),
                new Vector2(560f, 44f), 26, Accent, TextAnchor.MiddleCenter, font);
            Txt(panel.transform, "Txt_InvCapacity", "格子 0 / 12", new Vector2(0f, 304f),
                new Vector2(560f, 32f), 18, TextColor, TextAnchor.MiddleCenter, font);

            for (int i = 0; i < 12; i++)
            {
                int col = i % 4, row = i / 4;
                Vector2 pos = new Vector2(-258f + col * 172f, 218f - row * 130f);
                Button slot = Slot(panel.transform, "Btn_Slot_" + i.ToString("00"),
                    "空", pos, new Vector2(156f, 108f), font);
                Text label = slot.GetComponentInChildren<Text>(true);
                if (label != null) label.gameObject.name = "Txt_Slot_" + i.ToString("00");
                Transform iconTf = slot.transform.Find("Icon");
                if (iconTf != null) iconTf.gameObject.name = "Img_Slot_" + i.ToString("00");
            }

            Txt(panel.transform, "Txt_InvDetail", "", new Vector2(0f, -232f),
                new Vector2(660f, 92f), 16, TextColor, TextAnchor.UpperLeft, font);
            Btn(panel.transform, "Btn_InvPutIn", "放入", new Vector2(-206f, -324f),
                new Vector2(168f, 46f), font, BtnBg);
            Btn(panel.transform, "Btn_InvTakeOut", "取出", new Vector2(0f, -324f),
                new Vector2(168f, 46f), font, BtnBg);
            Btn(panel.transform, "Btn_InvClose", "关闭 (I)", new Vector2(238f, -324f),
                new Vector2(200f, 46f), font, BtnBg);
            // 手持装备 / 技能书使用（TOOL-015/016）：选中格子后点按；旧 UI 缺按钮时面板逻辑自动跳过
            Btn(panel.transform, "Btn_InvEquip", "装备/卸下", new Vector2(-103f, -378f),
                new Vector2(196f, 44f), font, BtnBg);
            Btn(panel.transform, "Btn_InvUse", "使用书", new Vector2(103f, -378f),
                new Vector2(196f, 44f), font, BtnBg);

            panel.SetActive(false);
            return panel;
        }

        // ----------------------------------------------------------------
        // 交易面板：左购买 / 右出售各 12 行 + 余额 + 结果 + 最近流水
        // ----------------------------------------------------------------
        static GameObject BuildTradePanel(Transform canvas)
        {
            Font font = UiFont.Load();
            GameObject panel = Panel(canvas, TradePanel, new Vector2(880f, 760f), Vector2.zero, PanelBg);

            Txt(panel.transform, "Txt_CoinBalance", "金币余额 0", new Vector2(0f, 344f),
                new Vector2(700f, 44f), 26, Accent, TextAnchor.MiddleCenter, font);

            Txt(panel.transform, "Txt_BuyList", "【购买】", new Vector2(-208f, 292f),
                new Vector2(420f, 34f), 18, Accent, TextAnchor.MiddleLeft, font);
            Txt(panel.transform, "Txt_SellList", "【出售】", new Vector2(208f, 292f),
                new Vector2(420f, 34f), 18, Accent, TextAnchor.MiddleLeft, font);

            for (int i = 0; i < 12; i++)
            {
                float y = 248f - i * 36f;
                Btn(panel.transform, "Btn_Buy_" + i.ToString("00"), "—", new Vector2(-208f, y),
                    new Vector2(420f, 32f), font, BtnBg, 17);
                Btn(panel.transform, "Btn_Sell_" + i.ToString("00"), "—", new Vector2(208f, y),
                    new Vector2(420f, 32f), font, BtnBg, 17);
            }

            Txt(panel.transform, "Txt_TradeResult", "", new Vector2(0f, -200f),
                new Vector2(820f, 42f), 18, TextColor, TextAnchor.MiddleCenter, font);
            Txt(panel.transform, "Txt_TradeLog", "最近交易：", new Vector2(0f, -272f),
                new Vector2(820f, 116f), 15, new Color(0.78f, 0.82f, 0.79f, 1f), TextAnchor.UpperLeft, font);

            Btn(panel.transform, "Btn_TradeClose", "关闭 (B)", new Vector2(0f, -350f),
                new Vector2(200f, 44f), font, BtnBg);

            panel.SetActive(false);
            return panel;
        }

        // --------------------------- UI 小工具（与 WhiteboxUiBuilder / GachaBuilder 同风格） ---------------------------
        static GameObject Panel(Transform parent, string name, Vector2 size, Vector2 pos, Color bg)
        {
            RectTransform rt = Rect(parent, name, Center, Center, size, pos, true);
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = bg;
            img.raycastTarget = true;
            return rt.gameObject;
        }

        static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pivot,
            Vector2 size, Vector2 pos, bool raycast)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = pivot; rt.sizeDelta = size; rt.anchoredPosition = pos;
            return rt;
        }

        static Text Txt(Transform parent, string name, string value, Vector2 pos, Vector2 size,
            int fontSize, Color color, TextAnchor align, Font font)
        {
            RectTransform rt = Rect(parent, name, Center, Center, size, pos, false);
            Text t = rt.gameObject.AddComponent<Text>();
            t.font = font; t.fontSize = fontSize; t.color = color; t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false; t.text = value;
            return t;
        }

        static Button Slot(Transform parent, string name, string caption, Vector2 pos, Vector2 size, Font font)
        {
            RectTransform rt = Rect(parent, name, Center, Center, size, pos, true);
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = SlotBg; img.raycastTarget = true;
            Button b = rt.gameObject.AddComponent<Button>();
            // 图标位（TOOL-016）：由 InventoryPanelView 按图标表填 Sprite；没图标时保持禁用，退回文字
            RectTransform iconRt = Rect(rt, "Icon", Center, Center, new Vector2(64f, 64f), new Vector2(0f, 16f), false);
            Image icon = iconRt.gameObject.AddComponent<Image>();
            icon.raycastTarget = false; icon.preserveAspect = true; icon.enabled = false;
            RectTransform label = Rect(rt, "Label", Center, Center, new Vector2(size.x - 16f, 34f), new Vector2(0f, -36f), false);
            Text t = label.gameObject.AddComponent<Text>();
            t.font = font; t.fontSize = 14; t.color = TextColor; t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.raycastTarget = false; t.text = caption;
            return b;
        }

        static Button Btn(Transform parent, string name, string caption, Vector2 pos, Vector2 size,
            Font font, Color bg, int fontSize = 22)
        {
            RectTransform rt = Rect(parent, name, Center, Center, size, pos, true);
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = bg; img.raycastTarget = true;
            Button b = rt.gameObject.AddComponent<Button>();
            RectTransform label = Rect(rt, "Label", Center, Center, size - new Vector2(16f, 8f), Vector2.zero, false);
            Text t = label.gameObject.AddComponent<Text>();
            t.font = font; t.fontSize = fontSize; t.color = TextColor; t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.raycastTarget = false; t.text = caption;
            return b;
        }

        static GameObject FindRoot(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) if (roots[i].name == name) return roots[i];
            return null;
        }

        static GameObject FindChild(Transform root, string name)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) if (all[i].name == name) return all[i].gameObject;
            return null;
        }
    }
}
