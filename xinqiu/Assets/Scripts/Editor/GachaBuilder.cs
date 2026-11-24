// 新增玩法：垃圾堆隐藏款 + 场景内老虎机抽奖（转盘改造）
// 红线与 WhiteboxUiBuilder 一致：全部 Editor API 生成，不手写 .unity / .asset YAML。
// 布局：① 场景里一台老虎机实体（走近对准按 F）；② 弹出式小型三转轮面板（无常驻界面）。
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 老虎机玩法一键生成 + 接线（Tools/垃圾星球白膜/⑨ 生成老虎机配置、实体与面板）。
    /// - 配置：Assets/Data/GachaConfig.asset（奖品/权重/保底，沿用旧转盘）。
    /// - 场景实体：env_slotmachine_whitebox（预制体 + 实例，放中转站旁），带 SlotMachine 交互组件与碰撞体。
    /// - 面板：UI_Whitebox/Panel_Gacha（520×560 小型三转轮，平时隐藏，只由 F 交互弹出）。
    /// 旧圆形转盘面板会在重跑菜单时自动被替换为新转轮面板；⑩ 为强制重建入口。
    /// </summary>
    [InitializeOnLoad]
    public static class GachaBuilder
    {
        // 域重载后自动补齐一次：把旧圆形转盘换成老虎机实体 + 三转轮面板。
        // 换 key = 让已存在的编辑器会话在脚本重编译后重新走一次补齐。
        const string AutoKey = "TrashPlanet.Gacha.SlotMachine.v1";

        static GachaBuilder()
        {
            EditorApplication.delayCall += AutoEnsure;
            EditorApplication.update += AutoEnsure;
        }

        static void AutoEnsure()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
            // 全局总闸：UI 定稿并锁定后，编译/重启不再自动重建，保护手调布局。
            if (UiAutoBuildGuard.Locked) return;
            if (SessionState.GetBool(AutoKey, false)) { EditorApplication.update -= AutoEnsure; return; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != WhiteboxBuilder.ScenePath) return; // 不在白膜场景：等菜单⑨
            // 等 UI / sys / WorldRoot 就绪（由菜单 ①④ 生成），这一轮不消费 key
            if (FindRoot(scene, UiRoot) == null || FindRoot(scene, SysRoot) == null
                || FindRoot(scene, WorldRoot) == null) return;

            SessionState.SetBool(AutoKey, true);
            EditorApplication.update -= AutoEnsure;
            Build(); // 幂等：已有实体/面板且不旧 → 只接线，不重建
            Debug.Log(Tag + "已自动补齐场景老虎机与转轮面板");
        }

        const string ConfigPath = "Assets/Data/GachaConfig.asset";
        const string UiRoot = "UI_Whitebox";
        const string WorldRoot = "WorldRoot";
        const string SysRoot = "sys_whitebox";
        const string PanelName = "Panel_Gacha";
        const string MachineName = "env_slotmachine_whitebox";
        const string MachinePrefabPath = "Assets/Prefabs/Whitebox/env_slotmachine_whitebox.prefab";
        const string MaterialFolder = "Assets/Art/Materials";
        const string Tag = "[GachaBuilder] ";

        static readonly Color PanelBg = new Color(0.06f, 0.12f, 0.11f, 0.97f);
        static readonly Color WindowBg = new Color(0.03f, 0.07f, 0.07f, 1f);
        static readonly Color BtnBg = new Color(0.20f, 0.26f, 0.27f, 0.97f);
        static readonly Color TextColor = new Color(0.96f, 0.97f, 0.94f, 1f);
        static readonly Color Accent = new Color(0.24f, 0.84f, 0.75f, 1f);
        static readonly Color EpicC = Hex("#E2B84E");
        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        // 三转轮的 9 行符号（从上到下索引 0..8）：普通 6 行 / 精英 2 行 / 史诗 1 行。
        // 与 GachaPanelView.RarityRowIndex 的落点一一对应，改这里必须同步改那边。
        static readonly string[] ReelSymbols =
        {
            "普通 ★", "普通 ☆", "普通 ◆", "精英 ◆", "普通 ●",
            "普通 ▲", "普通 ■", "精英 ★", "史诗 ★"
        };
        static readonly string[] ReelSymbolColors =
        {
            "#FFC93C", "#FFD54F", "#FF7043", "#42A5F5", "#EC407A",
            "#FB8C00", "#FF8A65", "#7E57C2", "#E53935"
        };
        const float ReelRowHeight = 58f;   // 每行高
        const int ReelRows = 9;            // 行数，和 ReelSymbols 长度一致
        const float ReelWindowHeight = 174f; // 可见 3 行

        [MenuItem("Tools/垃圾星球白膜/⑨ 生成老虎机配置、实体与面板", false, 16)]
        public static void Build()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != WhiteboxBuilder.ScenePath)
            {
                Debug.LogWarning(Tag + "请先打开白膜场景 " + WhiteboxBuilder.ScenePath + " 再执行本菜单");
                return;
            }

            GachaConfig cfg = EnsureConfig();
            GameObject uiRoot = FindRoot(scene, UiRoot);
            GameObject sys = FindRoot(scene, SysRoot);
            GameObject world = FindRoot(scene, WorldRoot);
            if (uiRoot == null || sys == null || world == null)
            {
                Debug.LogError(Tag + "缺少 UI_Whitebox / sys_whitebox / WorldRoot，请先跑菜单 ① 和 ④");
                return;
            }

            bool changed = false;
            changed |= EnsureMachine(scene, world, sys);
            changed |= EnsurePanel(uiRoot);
            changed |= WireAll(scene, sys, uiRoot, cfg);

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
            Debug.Log(Tag + "完成：配置=" + ConfigPath + "，实体=" + WorldRoot + "/" + MachineName
                      + "，面板=" + UiRoot + "/" + PanelName + "（changed=" + changed + "）");
        }

        [MenuItem("Tools/垃圾星球白膜/⑩ 重建老虎机面板与实体", false, 17)]
        public static void Rebuild()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != WhiteboxBuilder.ScenePath)
            {
                Debug.LogWarning(Tag + "请先打开白膜场景 " + WhiteboxBuilder.ScenePath + " 再执行本菜单");
                return;
            }
            GameObject uiRoot = FindRoot(scene, UiRoot);
            GameObject sys = FindRoot(scene, SysRoot);
            GameObject world = FindRoot(scene, WorldRoot);
            if (uiRoot == null || sys == null || world == null) { Debug.LogError(Tag + "缺少场景根节点"); return; }

            GameObject oldPanel = FindChild(uiRoot.transform, PanelName);
            if (oldPanel != null) UnityEngine.Object.DestroyImmediate(oldPanel);
            GameObject oldMachine = FindChild(world.transform, MachineName);
            if (oldMachine != null) UnityEngine.Object.DestroyImmediate(oldMachine);

            EnsurePrefab();
            EnsureMachine(scene, world, sys);
            EnsurePanel(uiRoot);
            WireAll(scene, sys, uiRoot, EnsureConfig());
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log(Tag + "老虎机面板与实体已重建");
        }

        /// <summary>批处理入口，供命令行 / CI 调用。</summary>
        public static void BuildBatch()
        {
            Scene scene = EditorSceneManager.OpenScene(WhiteboxBuilder.ScenePath, OpenSceneMode.Single);
            GachaConfig cfg = EnsureConfig();
            GameObject uiRoot = FindRoot(scene, UiRoot);
            GameObject sys = FindRoot(scene, SysRoot);
            GameObject world = FindRoot(scene, WorldRoot);
            if (uiRoot == null || sys == null || world == null) { Debug.LogError(Tag + "batch: 缺少场景根节点"); return; }
            EnsurePrefab();
            EnsureMachine(scene, world, sys);
            EnsurePanel(uiRoot);
            WireAll(scene, sys, uiRoot, cfg);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log(Tag + "batch 完成");
        }

        static GachaConfig EnsureConfig()
        {
            GachaConfig cfg = AssetDatabase.LoadAssetAtPath<GachaConfig>(ConfigPath);
            if (cfg != null) return cfg;
            string dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir)) EnsureFolder(dir);
            cfg = GachaConfig.CreateDefault();
            AssetDatabase.CreateAsset(cfg, ConfigPath);
            AssetDatabase.SaveAssets();
            Debug.Log(Tag + "已生成默认配置：" + ConfigPath);
            return cfg;
        }

        static void EnsureFolder(string path)
        {
            string[] parts = path.Replace('\\', '/').Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = string.IsNullOrEmpty(cur) ? parts[i] : cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        // ------------------------------------------------------------------
        // 场景实体：老虎机
        // ------------------------------------------------------------------

        /// <summary>确保老虎机预制体存在（Editor API 生成，不手写 prefab YAML）。</summary>
        static void EnsurePrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(MachinePrefabPath) != null) return;
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs/Whitebox"))
                AssetDatabase.CreateFolder("Assets/Prefabs", "Whitebox");

            GameObject root = new GameObject(MachineName);
            // 机身主体：带碰撞体，准星射线靠它命中并 GetComponentInParent<SlotMachine>()
            GameObject body = Primitive(root.transform, "Body", PrimitiveType.Cube,
                new Vector3(1.1f, 1.9f, 0.7f), new Vector3(0f, 0.95f, 0f), Mat("M_Station_Whitebox.mat", Hex("#DBD5C5")), true);
            // 顶部漏斗（呼应参考图里的投入口）
            Primitive(root.transform, "Hopper", PrimitiveType.Cylinder,
                new Vector3(0.5f, 0.28f, 0.5f), new Vector3(0f, 2.02f, 0f), Mat("M_Rust_Whitebox.mat", Hex("#8C3E2E")), false);
            // 屏幕：发光青色，作为准星高亮对象
            GameObject screen = Primitive(root.transform, "Screen", PrimitiveType.Cube,
                new Vector3(0.82f, 0.78f, 0.06f), new Vector3(0f, 1.28f, 0.38f), Mat("M_SlotScreen_Whitebox.mat", new Color(0.10f, 0.55f, 0.52f)), false);
            // 三转轮窗口装饰（竖条，纯视觉，呼应面板里的三个转轮）
            for (int i = 0; i < 3; i++)
                Primitive(screen.transform, "ReelWin_" + i, PrimitiveType.Cube,
                    new Vector3(0.22f, 0.5f, 0.02f), new Vector3(-0.26f + i * 0.26f, 0f, -0.62f),
                    Mat("M_SlotReel_Whitebox.mat", new Color(0.04f, 0.10f, 0.09f)), false);
            // 拉杆
            Primitive(root.transform, "Lever", PrimitiveType.Cylinder,
                new Vector3(0.07f, 0.5f, 0.07f), new Vector3(0.72f, 1.0f, 0.18f), Mat("M_Rust_Whitebox.mat", Hex("#8C3E2E")), false);
            // 底座
            Primitive(root.transform, "Base", PrimitiveType.Cube,
                new Vector3(1.3f, 0.18f, 0.9f), new Vector3(0f, 0.09f, 0f), Mat("M_Landmark_Whitebox.mat", Hex("#5C5340")), false);

            // 交互组件：注入 Game 与高亮渲染器（Builder 注入，不改场景 YAML 手编）
            SlotMachine slot = root.AddComponent<SlotMachine>();
            slot.WireHighlight(screen.GetComponent<Renderer>());
            PrefabUtility.SaveAsPrefabAsset(root, MachinePrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log(Tag + "已生成老虎机预制体：" + MachinePrefabPath);
        }

        /// <summary>确保场景里有一台老虎机实例，摆在中转站旁的球面上。</summary>
        static bool EnsureMachine(Scene scene, GameObject world, GameObject sys)
        {
            bool changed = false;
            EnsurePrefab();
            GameObject machine = FindChild(world.transform, MachineName);
            if (machine == null)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MachinePrefabPath);
                if (prefab == null) { Debug.LogError(Tag + "老虎机预制体缺失"); return false; }
                machine = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                machine.name = MachineName;
                machine.transform.SetParent(world.transform, true);
                changed = true;
            }

            // 摆位：中转站在 Vector3.forward 方向，老虎机放在它稍偏右的位置，面向球心。
            WhiteboxConfig cfg = AssetDatabase.LoadAssetAtPath<WhiteboxConfig>("Assets/Data/WhiteboxConfig.asset");
            float radius = cfg != null ? cfg.PlanetRadius : 8f;
            Vector3 dir = (Vector3.forward + Vector3.right * 0.32f).normalized;
            SphericalSurface.Place(machine.transform, Vector3.zero, dir, radius, 0.02f, -dir);

            // 保证碰撞体与交互组件齐全（旧实例可能缺）
            BoxCollider col = machine.GetComponent<BoxCollider>();
            if (col == null) { col = machine.AddComponent<BoxCollider>(); col.size = new Vector3(1.2f, 2.1f, 0.9f); col.center = new Vector3(0f, 1.05f, 0f); changed = true; }
            if (machine.GetComponent<SlotMachine>() == null) { machine.AddComponent<SlotMachine>(); changed = true; }
            EditorUtility.SetDirty(machine);
            return changed;
        }

        // ------------------------------------------------------------------
        // 面板：小型三转轮
        // ------------------------------------------------------------------

        static bool EnsurePanel(GameObject uiRoot)
        {
            GameObject panel = FindChild(uiRoot.transform, PanelName);
            // 旧圆形转盘（有 Wheel_Disc）或旧结构（缺 Reel_View_0）→ 整体重建
            bool old = panel != null && (FindChild(panel.transform, "Wheel_Disc") != null
                                         || FindChild(panel.transform, "Reel_View_0") == null);
            if (old) { UnityEngine.Object.DestroyImmediate(panel); panel = null; }
            if (panel == null) { BuildPanel(uiRoot.transform); return true; }
            return false;
        }

        static void BuildPanel(Transform canvas)
        {
            GameObject panel = Panel(canvas, PanelName, new Vector2(520f, 560f), Vector2.zero, PanelBg);

            Txt(panel.transform, "Txt_Title", "老虎机 · 幸运抽奖", new Vector2(0f, 250f),
                new Vector2(420f, 42f), 26, Accent, TextAnchor.MiddleCenter, UiFont.Load());
            Txt(panel.transform, "Txt_Tickets", "抽奖券  ×0", new Vector2(-120f, 210f),
                new Vector2(220f, 30f), 18, TextColor, TextAnchor.MiddleLeft, UiFont.Load());
            Txt(panel.transform, "Txt_Draws", "已抽  0 次", new Vector2(150f, 210f),
                new Vector2(180f, 30f), 16, TextColor, TextAnchor.MiddleRight, UiFont.Load());
            Txt(panel.transform, "Txt_Pity", "距史诗保底  --", new Vector2(0f, 182f),
                new Vector2(460f, 26f), 14, EpicC, TextAnchor.MiddleCenter, UiFont.Load());

            BuildReels(panel.transform);

            // 奖品图标位（TOOL-016）：抽到物品后显示对应 2D 图标，没图标时保持空（只回退文字）
            RectTransform iconRt = Rect(panel.transform, "Img_GachaResult", Center, Center,
                new Vector2(72f, 72f), new Vector2(-196f, -152f), false);
            Image iconImg = iconRt.gameObject.AddComponent<Image>();
            iconImg.raycastTarget = false;
            iconImg.preserveAspect = true;
            iconImg.enabled = false;

            Txt(panel.transform, "Txt_Result", "", new Vector2(46f, -152f),
                new Vector2(392f, 72f), 17, TextColor, TextAnchor.MiddleCenter, UiFont.Load());
            Txt(panel.transform, "Txt_Hint", "", new Vector2(0f, -204f),
                new Vector2(470f, 26f), 14, new Color(0.72f, 0.75f, 0.72f, 1f), TextAnchor.MiddleCenter, UiFont.Load());

            Btn(panel.transform, "Btn_Draw", "S P I N  (1券)", new Vector2(-95f, -248f),
                new Vector2(210f, 46f), UiFont.Load(), new Color(0.16f, 0.42f, 0.38f, 0.98f));
            Btn(panel.transform, "Btn_Close", "关  闭", new Vector2(150f, -248f),
                new Vector2(140f, 46f), UiFont.Load(), BtnBg);

            panel.SetActive(false);
            EditorUtility.SetDirty(panel);
        }

        /// <summary>三个转轮窗口：每个窗口 RectMask2D 裁剪，内部视图含 9 行符号带，可沿 Y 滚动。</summary>
        static void BuildReels(Transform panel)
        {
            const float reelW = 132f;
            const float gap = 18f;
            float totalW = reelW * 3f + gap * 2f;
            float startX = -totalW * 0.5f + reelW * 0.5f;
            Font font = UiFont.Load();

            for (int r = 0; r < 3; r++)
            {
                float x = startX + r * (reelW + gap);
                RectTransform win = Rect(panel, "Reel_Window_" + r, Center, Center,
                    new Vector2(reelW, ReelWindowHeight), new Vector2(x, 18f), true);
                Image winBg = win.gameObject.AddComponent<Image>();
                winBg.color = WindowBg;
                winBg.raycastTarget = false;
                win.gameObject.AddComponent<RectMask2D>();

                // 视图：高 = 行数 × 行高；pivot 中心；初始 restY 使第 0 行落在窗口中心偏上
                RectTransform view = Rect(win, "Reel_View_" + r, Center, Center,
                    new Vector2(reelW - 8f, ReelRows * ReelRowHeight), Vector2.zero, false);
                // 行 0 在视图内局部 y = +232；要让行 0 落在窗口中心(y=0)，视图需下移 232。
                float restY = -(ReelRows - 1) * 0.5f * ReelRowHeight;
                view.anchoredPosition = new Vector2(0f, restY);
                for (int i = 0; i < ReelRows; i++)
                {
                    float rowY = (ReelRows - 1) * 0.5f * ReelRowHeight - i * ReelRowHeight;
                    Text row = Txt(view, "Reel_Row_" + r + "_" + i, ReelSymbols[i],
                        new Vector2(0f, rowY), new Vector2(reelW - 12f, ReelRowHeight - 4f),
                        15, Hex(ReelSymbolColors[i]), TextAnchor.MiddleCenter, font);
                    row.fontStyle = FontStyle.Bold;
                    row.raycastTarget = false;
                }

                // 中央窗口文字：显示本次中奖档（由运行时覆盖）
                Text center = Txt(win, "Reel_Center_" + r, RarityLabel(r),
                    new Vector2(0f, -ReelWindowHeight * 0.5f - 14f), new Vector2(reelW, 24f),
                    14, new Color(0.86f, 0.87f, 0.84f, 1f), TextAnchor.MiddleCenter, font);
                center.raycastTarget = false;
            }

            // 中奖线：横贯三窗口的青色细线
            RectTransform line = Rect(panel, "WinLine", Center, Center,
                new Vector2(totalW + 24f, 3f), new Vector2(0f, 18f), false);
            Image lineImg = line.gameObject.AddComponent<Image>();
            lineImg.color = new Color(0.24f, 0.84f, 0.75f, 0.55f);
            lineImg.raycastTarget = false;
        }

        static string RarityLabel(int r)
        {
            return r == 1 ? "精英" : "普通";
        }

        // ------------------------------------------------------------------
        // 接线：系统 / 视图 / 实体 / Game
        // ------------------------------------------------------------------

        static bool WireAll(Scene scene, GameObject sys, GameObject uiRoot, GachaConfig cfg)
        {
            bool changed = false;
            GachaPanelView view = uiRoot.GetComponent<GachaPanelView>();
            if (view == null) { view = uiRoot.AddComponent<GachaPanelView>(); changed = true; }

            GachaSystem gacha = sys.GetComponent<GachaSystem>();
            if (gacha == null) { gacha = sys.AddComponent<GachaSystem>(); changed = true; }
            gacha.Wire(cfg, view);
            EditorUtility.SetDirty(gacha);

            WhiteboxGame game = sys.GetComponent<WhiteboxGame>();
            if (game == null) { Debug.LogWarning(Tag + "sys_whitebox 上未找到 WhiteboxGame"); return changed; }
            game.WireGacha(gacha);
            EditorUtility.SetDirty(game);

            // 老虎机实体注入 Game 引用（走互斥入口）+ 高亮渲染器
            GameObject world = FindRoot(scene, WorldRoot);
            GameObject machine = world != null ? FindChild(world.transform, MachineName) : null;
            if (machine != null)
            {
                SlotMachine slot = machine.GetComponent<SlotMachine>();
                if (slot != null)
                {
                    slot.Wire(game);
                    Renderer screen = FindChild(machine.transform, "Screen") != null
                        ? FindChild(machine.transform, "Screen").GetComponent<Renderer>() : null;
                    if (screen != null) slot.WireHighlight(screen);
                    EditorUtility.SetDirty(slot);
                }
            }
            return changed;
        }

        // --------------------------- UI / 实体小工具 ---------------------------

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

        static GameObject Primitive(Transform parent, string name, PrimitiveType type,
            Vector3 scale, Vector3 localPos, Material mat, bool collider)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = scale;
            Renderer r = go.GetComponent<Renderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;
            if (!collider)
            {
                Collider c = go.GetComponent<Collider>();
                if (c != null) UnityEngine.Object.DestroyImmediate(c);
            }
            return go;
        }

        static Material Mat(string file, Color c)
        {
            string path = MaterialFolder + "/" + file;
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                if (!AssetDatabase.IsValidFolder(MaterialFolder)) EnsureFolder(MaterialFolder);
                Shader sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) sh = Shader.Find("Standard");
                m = new Material(sh) { name = file.Replace(".mat", "") };
                AssetDatabase.CreateAsset(m, path);
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            EditorUtility.SetDirty(m);
            return m;
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

        static Button Btn(Transform parent, string name, string caption, Vector2 pos, Vector2 size,
            Font font, Color bg)
        {
            RectTransform rt = Rect(parent, name, Center, Center, size, pos, true);
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = bg; img.raycastTarget = true;
            Button b = rt.gameObject.AddComponent<Button>();
            RectTransform label = Rect(rt, "Label", Center, Center, size - new Vector2(16f, 8f), Vector2.zero, false);
            Text t = label.gameObject.AddComponent<Text>();
            t.font = font; t.fontSize = 20; t.color = TextColor; t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false; t.text = caption;
            return b;
        }

        static Color Hex(string s)
        {
            Color c;
            return ColorUtility.TryParseHtmlString(s, out c) ? c : Color.gray;
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
