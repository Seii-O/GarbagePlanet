// 新增玩法：垃圾堆隐藏款 + 转盘奖励（参考图圆形平面转盘风格）
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 转盘玩法的一键生成 + 接线（Tools/垃圾星球白膜/⑨ 生成转盘配置与界面）。
    /// 红线与 WhiteboxUiBuilder 一致：全部 Editor API 生成，不手写 .unity / .asset YAML。
    /// 圆形盘面：10 扇区（7 普通 / 2 精英 / 1 史诗，含 2 个"再接再厉"空奖文案）、
    /// 红黄交替灯泡外环、中心圆形开始按钮、黑色三角指针；扇区布局与旋转落点共用 WheelLayout。
    /// 旧版方形界面在重跑菜单时自动重建；⑩ 为强制重建入口。
    /// </summary>
    public static class GachaBuilder
    {
        const string ConfigPath = "Assets/Data/GachaConfig.asset";
        const string UiRoot = "UI_Whitebox";
        const string PanelName = "Panel_Gacha";
        const string Tag = "[GachaBuilder] ";

        static readonly Color PanelBg = new Color(0.06f, 0.12f, 0.11f, 0.97f);
        static readonly Color DiscBg = new Color(0.09f, 0.20f, 0.18f, 1f);
        static readonly Color BtnBg = new Color(0.20f, 0.26f, 0.27f, 0.97f);
        static readonly Color TextColor = new Color(0.96f, 0.97f, 0.94f, 1f);
        static readonly Color Accent = new Color(0.24f, 0.84f, 0.75f, 1f);
        static readonly Color EpicC = Hex("#E2B84E");
        static readonly Color LampRed = Hex("#E8362B");
        static readonly Color LampYellow = Hex("#FFD42A");
        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        [MenuItem("Tools/垃圾星球白膜/⑨ 生成转盘配置与界面", false, 16)]
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
            if (uiRoot == null)
            {
                Debug.LogError(Tag + "未找到 " + UiRoot + " 画布，请先跑菜单 ④ 生成 Hierarchy UI");
                return;
            }

            bool changed = false;
            GameObject panel = FindChild(uiRoot.transform, PanelName);
            if (panel != null && FindChild(panel.transform, "Wheel_Disc") == null)
            {
                // 旧版方形盘面：整体重建，避免新旧扇区坐标混用导致指针落点错位
                UnityEngine.Object.DestroyImmediate(panel);
                panel = null;
            }
            if (panel == null) { panel = BuildPanel(uiRoot.transform); changed = true; }

            GachaPanelView view = uiRoot.GetComponent<GachaPanelView>();
            if (view == null) { view = uiRoot.AddComponent<GachaPanelView>(); changed = true; }

            GameObject sys = FindRoot(scene, "sys_whitebox");
            if (sys == null)
            {
                Debug.LogError(Tag + "未找到 sys_whitebox，请先跑菜单 ① 生成场景与预制体");
                return;
            }
            GachaSystem gacha = sys.GetComponent<GachaSystem>();
            if (gacha == null) { gacha = sys.AddComponent<GachaSystem>(); changed = true; }
            gacha.Wire(cfg, view);
            EditorUtility.SetDirty(gacha);

            WhiteboxGame game = sys.GetComponent<WhiteboxGame>();
            if (game != null) { game.WireGacha(gacha); EditorUtility.SetDirty(game); }
            else Debug.LogWarning(Tag + "sys_whitebox 上未找到 WhiteboxGame，转盘已生成但未接入启动链");

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
            Debug.Log(Tag + "完成：配置=" + ConfigPath + "，界面=" + UiRoot + "/" + PanelName
                      + "，系统=sys_whitebox/GachaSystem（changed=" + changed + "）");
        }

        [MenuItem("Tools/垃圾星球白膜/⑩ 重建转盘界面（圆形扇区）", false, 17)]
        public static void RebuildPanel()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != WhiteboxBuilder.ScenePath)
            {
                Debug.LogWarning(Tag + "请先打开白膜场景 " + WhiteboxBuilder.ScenePath + " 再执行本菜单");
                return;
            }
            GameObject uiRoot = FindRoot(scene, UiRoot);
            if (uiRoot == null) { Debug.LogError(Tag + "未找到 " + UiRoot); return; }
            GameObject panel = FindChild(uiRoot.transform, PanelName);
            if (panel != null) UnityEngine.Object.DestroyImmediate(panel);
            BuildPanel(uiRoot.transform);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log(Tag + "圆形转盘界面已重建");
        }

        /// <summary>批处理入口，供命令行 / CI 调用。</summary>
        public static void BuildBatch()
        {
            Scene scene = EditorSceneManager.OpenScene(WhiteboxBuilder.ScenePath, OpenSceneMode.Single);
            GachaConfig cfg = EnsureConfig();
            GameObject uiRoot = FindRoot(scene, UiRoot);
            GameObject sys = FindRoot(scene, "sys_whitebox");
            if (uiRoot == null || sys == null) { Debug.LogError(Tag + "batch: 缺少 UI 根或 sys_whitebox"); return; }

            GameObject panel = FindChild(uiRoot.transform, PanelName);
            if (panel == null || FindChild(panel.transform, "Wheel_Disc") == null)
            {
                if (panel != null) UnityEngine.Object.DestroyImmediate(panel);
                panel = BuildPanel(uiRoot.transform);
            }
            GachaPanelView view = uiRoot.GetComponent<GachaPanelView>();
            if (view == null) view = uiRoot.AddComponent<GachaPanelView>();
            GachaSystem gacha = sys.GetComponent<GachaSystem>();
            if (gacha == null) gacha = sys.AddComponent<GachaSystem>();
            gacha.Wire(cfg, view);
            WhiteboxGame game = sys.GetComponent<WhiteboxGame>();
            if (game != null) game.WireGacha(gacha);

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

        static GameObject BuildPanel(Transform canvas)
        {
            Font font = UiFont.Load();
            GameObject panel = Panel(canvas, PanelName, new Vector2(820f, 760f), Vector2.zero, PanelBg);

            Txt(panel.transform, "Txt_Title", "垃圾星球 · 幸运转盘", new Vector2(0f, 348f),
                new Vector2(560f, 44f), 26, Accent, TextAnchor.MiddleCenter, font);
            Txt(panel.transform, "Txt_Tickets", "转盘券  ×0", new Vector2(-250f, 300f),
                new Vector2(280f, 34f), 20, TextColor, TextAnchor.MiddleLeft, font);
            Txt(panel.transform, "Txt_Draws", "已抽  0 次", new Vector2(250f, 300f),
                new Vector2(280f, 34f), 18, TextColor, TextAnchor.MiddleRight, font);
            Txt(panel.transform, "Txt_Pity", "距史诗保底  --", new Vector2(0f, 268f),
                new Vector2(700f, 30f), 16, EpicC, TextAnchor.MiddleCenter, font);

            BuildWheel(panel.transform, font);

            Txt(panel.transform, "Txt_Result", "", new Vector2(0f, -300f),
                new Vector2(780f, 76f), 19, TextColor, TextAnchor.MiddleCenter, font);
            Txt(panel.transform, "Txt_Hint", "点中心【开始】消耗 1 券　·　R 关闭",
                new Vector2(0f, -352f), new Vector2(780f, 26f), 15,
                new Color(0.72f, 0.75f, 0.72f, 1f), TextAnchor.MiddleCenter, font);
            Btn(panel.transform, "Btn_Close", "关  闭  (R)", new Vector2(320f, 348f),
                new Vector2(150f, 44f), font, BtnBg);

            panel.SetActive(false);
            EditorUtility.SetDirty(panel);
            return panel;
        }

        /// <summary>圆形平面转盘：底板 → 10 扇区 → 红黄灯泡环 → 扇区文字 → 指针 → 中心开始按钮。</summary>
        static void BuildWheel(Transform panel, Font font)
        {
            RectTransform wheel = Rect(panel, "Wheel", Center, Center,
                new Vector2(500f, 500f), new Vector2(0f, -6f), false);

            // 底板（深色圆盘 + 白边），作为"旧界面检测"标记 Wheel_Disc
            Disc(wheel, "Wheel_Disc", 220f, 3f, Color.white, DiscBg);

            // 10 个扇区：颜色 / 文案全部来自 WheelLayout，与旋转落点共用
            int n = WheelLayout.SectorCount;
            for (int i = 0; i < n; i++)
            {
                float start = i * WheelLayout.SectorAngle;
                Color fill = Hex(WheelLayout.ColorAt(i));
                bool isEpic = WheelLayout.RarityAt(i) == GachaRarity.Epic;
                bool isBlank = WheelLayout.LabelAt(i).Contains("再接");
                // 描边分层：史诗金边加粗（最高层级）/ 空奖灰边弱化 / 其余白边
                Color edge = isEpic ? EpicC : (isBlank ? new Color(0.62f, 0.68f, 0.72f, 1f)
                                                       : new Color(1f, 1f, 1f, 0.92f));
                WheelSectorGraphic sector = Sector(wheel, "Sec_" + i.ToString("00"),
                    start, WheelLayout.SectorAngle, 214f, 0f, isEpic ? 5f : 3f, edge, fill);
                sector.raycastTarget = false;

                float centerAngle = WheelLayout.CenterAngleAt(i);
                Vector2 pos = new Vector2(Mathf.Sin(centerAngle * Mathf.Deg2Rad),
                    Mathf.Cos(centerAngle * Mathf.Deg2Rad)) * 142f;
                Color labelCol = isBlank ? new Color(0.88f, 0.92f, 0.95f, 1f) : Color.white;
                Text sectorLabel = Txt(wheel, "Lbl_" + i.ToString("00"), WheelLayout.LabelAt(i), pos,
                    new Vector2(118f, 82f), WheelLayout.LabelAt(i).Contains("\n") ? 19 : 24,
                    labelCol, TextAnchor.MiddleCenter, font);
                sectorLabel.fontStyle = FontStyle.Bold;
                // 文字朝向：本地 up 指向扇区外侧，读数方向与参考图一致
                sectorLabel.rectTransform.localEulerAngles = new Vector3(0f, 0f, -centerAngle);
                Outline outline = sectorLabel.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.72f);
                outline.effectDistance = new Vector2(1.5f, -1.5f);
                sectorLabel.raycastTarget = false;
            }

            // 红黄交替灯泡外环：40 段弧 + 每段白色圆点
            const int lampCount = 40;
            float lampStep = 360f / lampCount;
            for (int i = 0; i < lampCount; i++)
            {
                Color c = (i % 2 == 0) ? LampRed : LampYellow;
                WheelSectorGraphic lamp = Sector(wheel, "Lamp_" + i.ToString("00"),
                    i * lampStep, lampStep, 240f, 222f, 1.5f, Color.white, c);
                lamp.raycastTarget = false;

                float mid = (i + 0.5f) * lampStep;
                Vector2 dotPos = new Vector2(Mathf.Sin(mid * Mathf.Deg2Rad),
                    Mathf.Cos(mid * Mathf.Deg2Rad)) * 231f;
                RectTransform dotRt = Rect(wheel, "LampDot_" + i.ToString("00"), Center, Center,
                    new Vector2(10f, 10f), dotPos, false);
                WheelSectorGraphic dot = dotRt.gameObject.AddComponent<WheelSectorGraphic>();
                dot.Configure(0f, 360f, 5f, 0f, 0f, Color.white, Color.white);
                dot.raycastTarget = false;
            }

            // 中央指针：pivot 在底部中心（盘心），旋转 z 即指向对应扇区（GachaPanelView 按名字取用）。
            // 亮金描边 + 深棕主体 + 中轴高光，与彩色盘面强对比；长度伸到扇区中段、不压中心按钮。
            RectTransform needle = Rect(wheel, "Wheel_Needle", Center, new Vector2(0.5f, 0f),
                new Vector2(34f, 132f), Vector2.zero, false);
            WheelNeedleGraphic needleGraphic = needle.gameObject.AddComponent<WheelNeedleGraphic>();
            needleGraphic.Configure(5f, Hex("#FFD95B"), Hex("#241610"));
            needleGraphic.raycastTarget = false;

            // 中心圆形"开始"按钮：沿用 Btn_Draw 名称与运行时绑定，不新增交互入口
            RectTransform hub = Rect(wheel, "Btn_Draw", Center, Center,
                new Vector2(112f, 112f), Vector2.zero, true);
            WheelSectorGraphic hubGraphic = hub.gameObject.AddComponent<WheelSectorGraphic>();
            hubGraphic.Configure(0f, 360f, 54f, 0f, 5f, Hex("#FFD95B"), Hex("#F4511E"));
            hubGraphic.raycastTarget = true;
            Button draw = hub.gameObject.AddComponent<Button>();
            draw.targetGraphic = hubGraphic;
            draw.interactable = false;
            RectTransform labelRt = Rect(hub, "Label", Center, Center, new Vector2(96f, 60f), Vector2.zero, false);
            Text label = labelRt.gameObject.AddComponent<Text>();
            label.font = font; label.fontSize = 28; label.fontStyle = FontStyle.Bold;
            label.color = new Color(0.22f, 0.10f, 0.02f, 1f);
            label.alignment = TextAnchor.MiddleCenter; label.text = "开 始";
            label.raycastTarget = false;
        }

        // --------------------------- UI 小工具（与 WhiteboxUiBuilder 同风格） ---------------------------

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

        static WheelSectorGraphic Disc(Transform parent, string name, float radius,
            float outline, Color edge, Color fill)
        {
            RectTransform rt = Rect(parent, name, Center, Center,
                new Vector2(radius * 2f + 12f, radius * 2f + 12f), Vector2.zero, false);
            WheelSectorGraphic g = rt.gameObject.AddComponent<WheelSectorGraphic>();
            g.Configure(0f, 360f, radius, 0f, outline, edge, fill);
            g.raycastTarget = false;
            return g;
        }

        static WheelSectorGraphic Sector(Transform parent, string name, float start, float sweep,
            float outer, float inner, float outline, Color edge, Color fill)
        {
            RectTransform rt = Rect(parent, name, Center, Center,
                new Vector2(outer * 2f + 12f, outer * 2f + 12f), Vector2.zero, false);
            WheelSectorGraphic g = rt.gameObject.AddComponent<WheelSectorGraphic>();
            g.Configure(start, sweep, outer, inner, outline, edge, fill);
            return g;
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
            t.font = font; t.fontSize = 22; t.color = TextColor; t.alignment = TextAnchor.MiddleCenter;
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
