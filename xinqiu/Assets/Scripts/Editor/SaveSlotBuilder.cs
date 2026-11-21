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
    [InitializeOnLoad]
    public static class SaveSlotBuilder
    {
        const string UiRoot = "UI_Whitebox";
        const string SysRoot = "sys_whitebox";
        const string PanelName = "Panel_InGameSave";
        const string Tag = "[SaveSlotBuilder] ";
        const int Slots = 5;
        // v2：新增「存档名输入框 + 每槽垃圾桶 + 批量删除按钮」。改 key 让域重载后再补一次。
        const string AutoKey = "TrashPlanet.SaveSlot.Extras.v2";

        static SaveSlotBuilder()
        {
            EditorApplication.delayCall += AutoEnsure;
            EditorApplication.update += AutoEnsure;
        }

        static void AutoEnsure()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (SessionState.GetBool(AutoKey, false))
            {
                EditorApplication.update -= AutoEnsure;
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != WhiteboxBuilder.ScenePath) return; // 不在白膜场景：等菜单⑮
            // UI_Whitebox / sys_whitebox 由 WhiteboxUiBuilder 生成；若尚未就绪则这一轮不消费 SessionState，
            // 下一帧再来，避免抢在 UI 生成之前执行导致 BuildInGameScene 直接报错返回。
            if (FindRoot(scene, UiRoot) == null || FindRoot(scene, SysRoot) == null) return;

            SessionState.SetBool(AutoKey, true);
            EditorApplication.update -= AutoEnsure;
            // 新增了结构性元素（名字输入框 / 垃圾桶 / 批量删除），旧布局塞不下，
            // 按菜单⑮既有语义重建整个面板，保证位置正确。菜单⑮本就是"面板存在即重建"。
            if (!NeedsRebuild(scene)) return;
            BuildInGameScene(scene); // 内部会重建面板 + 重新接线 + 保存场景
            Debug.Log(Tag + "已重建存档面板：名字输入框 / 每槽垃圾桶 / 批量删除按钮，场景已保存");
        }

        /// <summary>存档面板缺失，或缺少新增元素（名字框 / 垃圾桶 / 删除按钮）时需要重建。</summary>
        static bool NeedsRebuild(Scene scene)
        {
            GameObject uiRoot = FindRoot(scene, UiRoot);
            GameObject panel = uiRoot != null ? FindChild(uiRoot.transform, PanelName) : null;
            if (panel == null) return true;
            if (FindChild(panel.transform, "Inp_SaveName") == null) return true;
            if (FindChild(panel.transform, "Btn_Trash_1") == null) return true;
            if (FindChild(panel.transform, "Btn_DeleteSelected") == null) return true;
            return false;
        }

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
            GameObject panel = Panel(canvas, PanelName, new Vector2(600f, 560f), Vector2.zero, PanelBg);
            Txt(panel.transform, "Txt_SaveTitle", "存档 · 选择槽位", new Vector2(0f, 250f),
                new Vector2(460f, 44f), 26, Accent, TextAnchor.MiddleCenter, font);
            Txt(panel.transform, "Txt_SaveHint", "输入名字后点槽保存  ·  点垃圾桶批量删除  ·  F9 / ESC 关闭",
                new Vector2(0f, 214f), new Vector2(560f, 30f), 14, TextColor, TextAnchor.MiddleCenter, font);

            // 存档名输入框：留空则显示回退到"槽 N"
            Txt(panel.transform, "Txt_NameLabel", "存档名：", new Vector2(-208f, 176f),
                new Vector2(100f, 34f), 17, Accent, TextAnchor.MiddleLeft, font);
            InputField nameInput = TextInput(panel.transform, "Inp_SaveName", new Vector2(42f, 176f),
                new Vector2(320f, 38f), "留空 = 槽 N", font);

            for (int i = 0; i < Slots; i++)
            {
                int slot = i + 1;
                float y = 128f - i * 56f;
                GameObject btn = Btn(panel.transform, "Btn_SaveSlot_" + slot, "",
                    new Vector2(-16f, y), new Vector2(500f, 48f), BtnBg, font);
                Txt(btn.transform, "Txt_SaveSlot_" + slot, "槽 " + slot + "  空（新建）",
                    Vector2.zero, new Vector2(480f, 42f), 18, TextColor, TextAnchor.MiddleCenter, font);
                // 垃圾桶：槽按钮右下角的小图标按钮；文本用「🗑」的后备字符，缺字形时仍可点
                Btn(panel.transform, "Btn_Trash_" + slot, "×",
                    new Vector2(248f, y - 9f), new Vector2(34f, 30f),
                    new Color(0.42f, 0.16f, 0.14f, 0.98f), font);
            }

            // 删除模式按钮：默认由 InGameSaveView 运行时按状态显隐
            Btn(panel.transform, "Btn_DeleteSelected", "删除所选",
                new Vector2(-104f, -192f), new Vector2(180f, 42f),
                new Color(0.55f, 0.20f, 0.16f, 0.98f), font);
            Btn(panel.transform, "Btn_DeleteCancel", "取消",
                new Vector2(104f, -192f), new Vector2(180f, 42f), BtnBg, font);
            Btn(panel.transform, "Btn_SaveClose", "关闭", new Vector2(0f, -240f),
                new Vector2(200f, 40f), BtnBg, font);
            panel.SetActive(false);
        }

        /// <summary>单行文本输入框（存档名用）：ContentType=Standard，限制 24 字。</summary>
        static InputField TextInput(Transform parent, string name, Vector2 pos, Vector2 size,
            string placeholder, Font font)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = Center; rt.anchorMax = Center; rt.pivot = Center;
            rt.sizeDelta = size; rt.anchoredPosition = pos;
            Image bg = go.GetComponent<Image>();
            bg.color = new Color(0.10f, 0.16f, 0.15f, 0.98f);

            GameObject phGo = new GameObject("Placeholder", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            phGo.transform.SetParent(go.transform, false);
            RectTransform phRt = (RectTransform)phGo.transform;
            phRt.anchorMin = Vector2.zero; phRt.anchorMax = Vector2.one; phRt.pivot = Center;
            phRt.offsetMin = new Vector2(10f, 2f); phRt.offsetMax = new Vector2(-10f, -2f);
            Text ph = phGo.GetComponent<Text>();
            ph.font = font; ph.fontSize = 16; ph.text = placeholder;
            ph.color = new Color(0.55f, 0.62f, 0.60f, 0.7f);
            ph.alignment = TextAnchor.MiddleLeft; ph.raycastTarget = false;

            GameObject txGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            txGo.transform.SetParent(go.transform, false);
            RectTransform txRt = (RectTransform)txGo.transform;
            txRt.anchorMin = Vector2.zero; txRt.anchorMax = Vector2.one; txRt.pivot = Center;
            txRt.offsetMin = new Vector2(10f, 2f); txRt.offsetMax = new Vector2(-10f, -2f);
            Text tx = txGo.GetComponent<Text>();
            tx.font = font; tx.fontSize = 17; tx.text = "";
            tx.color = TextColor; tx.alignment = TextAnchor.MiddleLeft; tx.raycastTarget = false;

            InputField field = go.GetComponent<InputField>();
            field.targetGraphic = bg;
            field.textComponent = tx;
            field.placeholder = ph;
            field.lineType = InputField.LineType.SingleLine;
            field.contentType = InputField.ContentType.Standard;
            field.characterLimit = 24;
            return field;
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
