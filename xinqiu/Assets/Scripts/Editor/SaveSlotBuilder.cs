// @trace SAVE-001, SAVE-003, UI-004, UI-005
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 多存档界面生成 + 接线（菜单⑮）：
    /// 1) 白膜场景：在 UI_Whitebox 下生成 Panel_InGameSave（5 个槽按钮 + 文字 + 关闭），
    ///    挂 InGameSaveView 到 UI_Whitebox，并 Wire 到 WhiteboxGame（F9 开关）。
    /// 2) 标题场景：给 ContinueGameUI 挂 SaveSlotMenuView；若无 5 个子按钮则补建，
    ///    每个按钮下保证有一个 Text 子对象用于显示"槽 N  时间"。
    /// 红线：全部用 Editor API 生成/接线，不手写 .unity YAML；可重复执行（面板存在即重建）。
    /// </summary>
    public static class SaveSlotBuilder
    {
        const string UiRoot = "UI_Whitebox";
        const string SysRoot = "sys_whitebox";
        const string PanelName = "Panel_InGameSave";
        const string Tag = "[SaveSlotBuilder] ";
        const int Slots = 5;

        static readonly Color PanelBg = new Color(0.06f, 0.12f, 0.11f, 0.97f);
        static readonly Color BtnBg = new Color(0.16f, 0.26f, 0.24f, 0.98f);
        static readonly Color TextColor = new Color(0.96f, 0.97f, 0.94f, 1f);
        static readonly Color Accent = new Color(0.24f, 0.84f, 0.75f, 1f);
        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        [MenuItem("Tools/垃圾星球白膜/⑮ 生成多存档界面与按钮", false, 20)]
        public static void Build()
        {
            Scene scene = SceneManager.GetActiveScene();
            string path = scene.IsValid() ? scene.path : "";
            bool inGame = path == WhiteboxBuilder.ScenePath;
            bool inTitle = path == TitleBuilder.ScenePath;
            if (!inGame && !inTitle)
            {
                Debug.LogWarning(Tag + "请打开白膜场景或标题场景再执行；本菜单只处理当前场景的那部分");
                return;
            }
            if (inGame) BuildInGameScene(scene);
            else BuildTitleScene(scene);
        }

        /// <summary>批处理入口：两个场景都处理，供命令行 / CI 验证。</summary>
        public static void BuildBatch()
        {
            Scene play = EditorSceneManager.OpenScene(WhiteboxBuilder.ScenePath, OpenSceneMode.Single);
            BuildInGameScene(play);
            Scene title = EditorSceneManager.OpenScene(TitleBuilder.ScenePath, OpenSceneMode.Single);
            BuildTitleScene(title);
        }

        // ----------------------------------------------------------------
        // 局内场景
        // ----------------------------------------------------------------
        static void BuildInGameScene(Scene scene)
        {
            GameObject uiRoot = FindRoot(scene, UiRoot);
            GameObject sysRoot = FindRoot(scene, SysRoot);
            if (uiRoot == null || sysRoot == null)
            {
                Debug.LogError(Tag + "缺少 " + UiRoot + " 或 " + SysRoot + "，请先跑菜单 ①/④");
                return;
            }

            GameObject old = FindChild(uiRoot.transform, PanelName);
            if (old != null) Object.DestroyImmediate(old);
            BuildSavePanel(uiRoot.transform);

            InGameSaveView view = uiRoot.GetComponent<InGameSaveView>();
            if (view == null) { view = uiRoot.AddComponent<InGameSaveView>(); }
            WhiteboxGame game = sysRoot.GetComponent<WhiteboxGame>();
            if (game != null)
            {
                view.SetGame(game);
                game.WireInGameSave(view);
                EditorUtility.SetDirty(game);
            }
            else Debug.LogWarning(Tag + "sys_whitebox 上未找到 WhiteboxGame，面板已生成但未接入 F9");

            EditorUtility.SetDirty(view);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log(Tag + "局内场景完成：" + UiRoot + "/" + PanelName + "（F9 开关）");
        }

        static void BuildSavePanel(Transform canvas)
        {
            Font font = UiFont.Load();
            GameObject panel = Panel(canvas, PanelName, new Vector2(560f, 520f), Vector2.zero, PanelBg);
            Txt(panel.transform, "Txt_SaveTitle", "存档 · 选择槽位", new Vector2(0f, 224f),
                new Vector2(460f, 44f), 26, Accent, TextAnchor.MiddleCenter, font);
            Txt(panel.transform, "Txt_SaveHint", "F9 / ESC 关闭  ·  点击槽位保存到该档（含玩家位置）",
                new Vector2(0f, 190f), new Vector2(500f, 32f), 15, TextColor, TextAnchor.MiddleCenter, font);

            for (int i = 0; i < Slots; i++)
            {
                int slot = i + 1;
                float y = 140f - i * 62f;
                GameObject btn = Btn(panel.transform, "Btn_SaveSlot_" + slot,
                    "槽 " + slot, new Vector2(0f, y), new Vector2(460f, 52f), BtnBg, font);
                Txt(btn.transform, "Txt_SaveSlot_" + slot, "槽 " + slot + "  空（新建）",
                    Vector2.zero, new Vector2(440f, 44f), 18, TextColor, TextAnchor.MiddleCenter, font);
            }
            Btn(panel.transform, "Btn_SaveClose", "关闭", new Vector2(0f, -200f),
                new Vector2(200f, 44f), BtnBg, font);
            panel.SetActive(false);
        }

        // ----------------------------------------------------------------
        // 标题场景
        // ----------------------------------------------------------------
        static void BuildTitleScene(Scene scene)
        {
            GameObject panel = FindRoot(scene, "ContinueGameUI");
            if (panel == null)
            {
                Debug.LogError(Tag + "标题场景缺少 ContinueGameUI，请先搭好选槽面板");
                return;
            }
            SaveSlotMenuView menu = panel.GetComponent<SaveSlotMenuView>();
            if (menu == null) menu = panel.AddComponent<SaveSlotMenuView>();

            // 找全部按钮；不足 5 个就补建（Text 子对象一并生成，供显示时间）
            Button[] buttons = panel.GetComponentsInChildren<Button>(true);
            Font font = UiFont.Load();
            Image image = panel.GetComponentInChildren<Image>(true);
            Transform host = image != null ? image.transform : panel.transform;
            for (int i = buttons.Length; i < Slots; i++)
            {
                GameObject btn = Btn(host, "Btn_Slot_" + (i + 1), "槽 " + (i + 1),
                    new Vector2(0f, -60f * i), new Vector2(360f, 48f), BtnBg, font);
                Txt(btn.transform, "Txt_SlotLabel_" + (i + 1), "", Vector2.zero,
                    new Vector2(340f, 40f), 18, TextColor, TextAnchor.MiddleCenter, font);
            }
            // 给已有按钮补 Text 子对象（若无）：SaveSlotMenuView 会取第一个子 Text
            buttons = panel.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < Slots && i < buttons.Length; i++)
            {
                if (buttons[i].GetComponentInChildren<Text>(true) == null)
                    Txt(buttons[i].transform, "Txt_SaveSlotLabel_" + (i + 1), "", Vector2.zero,
                        new Vector2(300f, 36f), 17, TextColor, TextAnchor.MiddleCenter, font);
            }

            EditorUtility.SetDirty(menu);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log(Tag + "标题场景完成：ContinueGameUI 挂 SaveSlotMenuView，列出 5 个槽");
        }

        // ----------------------------------------------------------------
        // Editor UI 辅助（与 InventoryBuilder 同风格，全部 Editor API）
        // ----------------------------------------------------------------
        static GameObject Panel(Transform parent, string name, Vector2 size, Vector2 pos, Color bg)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = Center; rt.anchorMax = Center; rt.pivot = Center;
            rt.sizeDelta = size; rt.anchoredPosition = pos;
            go.GetComponent<Image>().color = bg;
            return go;
        }

        static GameObject Btn(Transform parent, string name, string label, Vector2 pos, Vector2 size, Color bg, Font font)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = Center; rt.anchorMax = Center; rt.pivot = Center;
            rt.sizeDelta = size; rt.anchoredPosition = pos;
            go.GetComponent<Image>().color = bg;
            if (!string.IsNullOrEmpty(label))
                Txt(go.transform, "Txt_Label", label, Vector2.zero, size - new Vector2(8f, 4f), 18, TextColor, TextAnchor.MiddleCenter, font);
            return go;
        }

        static Text Txt(Transform parent, string name, string content, Vector2 pos, Vector2 size,
            int fontSize, Color color, TextAnchor anchor, Font font)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = Center; rt.anchorMax = Center; rt.pivot = Center;
            rt.sizeDelta = size; rt.anchoredPosition = pos;
            Text t = go.GetComponent<Text>();
            t.text = content; t.font = font; t.fontSize = fontSize; t.color = color;
            t.alignment = anchor; t.raycastTarget = false;
            return t;
        }

        static GameObject FindRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == name) return root;
            return null;
        }

        static GameObject FindChild(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform c = parent.GetChild(i);
                if (c.name == name) return c.gameObject;
                GameObject nested = FindChild(c, name);
                if (nested != null) return nested;
            }
            return null;
        }
    }
}
