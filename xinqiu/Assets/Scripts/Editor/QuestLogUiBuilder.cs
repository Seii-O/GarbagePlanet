// @trace UI-008
using System;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 任务日志界面的一键生成 + 接线（菜单㉒）。
    /// 与五页签中转站同款模式：真实 uGUI 拆件、Editor 注入持久化 Sprite、运行时不碰 AssetDatabase。
    /// 同时保证 Canvas 上有一个常驻「任务日志 · J」按钮，并持久化绑定到 WhiteboxUiActions.OpenQuestLog。
    /// </summary>
    [InitializeOnLoad]
    public static class QuestLogUiBuilder
    {
        const string SessionKey = "TrashPlanet.Whitebox.QuestLog.AutoBuilt.v1";
        const string Tag = "[QuestLogUiBuilder] ";
        const string UiRoot = "UI_Whitebox";
        const string SysRoot = "sys_whitebox";
        const string BtnName = "Btn_QuestLog";

        const string Pnl = "06_UI界面拆件/01_面板与容器/";
        const string Btn = "06_UI界面拆件/02_按钮/";
        const string Tab = "06_UI界面拆件/03_页签与标签/";
        const string Prg = "06_UI界面拆件/04_进度与数值/";
        const string Fun = "06_UI界面拆件/07_图标_功能/";
        const string Dec = "06_UI界面拆件/09_徽章与装饰/";

        static QuestLogUiBuilder()
        {
            EditorApplication.delayCall += AutoEnsure;
            EditorApplication.update += AutoEnsure;
        }

        static void AutoEnsure()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            // 全局总闸：UI 定稿并锁定后，编译/重启不再自动重建，保护手调布局。
            if (UiAutoBuildGuard.Locked) return;
            if (SessionState.GetBool(SessionKey, false)) return;
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != WhiteboxBuilder.ScenePath) return;
            GameObject uiRootGuard = FindRoot(scene, UiRoot);
            if (uiRootGuard == null || FindRoot(scene, SysRoot) == null) return;

            // 防覆盖：面板已存在则跳过自动重建，保护 Inspector 里手调的布局。
            // 域重载后场景序列化引用仍有效，无需重建；强制刷新请手动执行菜单 ㉒（仍为存在即删重建）。
            if (FindDeep(uiRootGuard.transform, QuestLogPanelView.PanelName) != null)
            {
                SessionState.SetBool(SessionKey, true);
                return;
            }

            SessionState.SetBool(SessionKey, true);
            try
            {
                BuildInScene(scene, false);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log(Tag + "首次载入已自动补齐任务日志界面与 HUD 按钮");
            }
            catch (Exception e)
            {
                SessionState.SetBool(SessionKey, false);
                Debug.LogError(Tag + "自动补齐失败：" + e);
            }
        }

        [MenuItem("Tools/垃圾星球白膜/㉒ 生成任务日志界面（J）", false, 22)]
        public static void BuildMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning(Tag + "请先退出 Play 模式");
                return;
            }
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != WhiteboxBuilder.ScenePath)
            {
                Debug.LogWarning(Tag + "请先打开白膜场景 " + WhiteboxBuilder.ScenePath);
                return;
            }
            BuildInScene(scene, false);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        public static void BuildBatch()
        {
            try
            {
                Scene scene = EditorSceneManager.OpenScene(WhiteboxBuilder.ScenePath, OpenSceneMode.Single);
                UiArtImportSettings.Apply();
                BuildInScene(scene, true);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                bool pass = WhiteboxValidator.Validate();
                Debug.Log(Tag + "batch validate=" + pass);
                EditorApplication.Exit(pass ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogError(Tag + "batch 失败：" + e);
                EditorApplication.Exit(1);
            }
        }

        static void BuildInScene(Scene scene, bool batch)
        {
            UiArtImportSettings.Apply();
            GameObject uiRoot = FindRoot(scene, UiRoot);
            GameObject sysRoot = FindRoot(scene, SysRoot);
            if (uiRoot == null || sysRoot == null)
            {
                Debug.LogError(Tag + "缺少 " + UiRoot + " 或 " + SysRoot);
                return;
            }
            WhiteboxGame game = sysRoot.GetComponent<WhiteboxGame>();
            QuestLogArtSet art = LoadArt();
            Font font = UiFont.Load();

            Transform old = FindDeep(uiRoot.transform, QuestLogPanelView.PanelName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);

            GameObject panel = QuestLogUiFactory.Build(uiRoot.transform, art, font);
            if (panel == null) { Debug.LogError(Tag + "面板构建失败"); return; }

            QuestLogPanelView view = uiRoot.GetComponent<QuestLogPanelView>();
            if (view == null) view = uiRoot.AddComponent<QuestLogPanelView>();
            view.Wire(game);
            view.SetArt(art, font);
            if (game != null) game.WireQuestLog(view);

            EnsureHudButton(uiRoot, game, art, font);

            EditorUtility.SetDirty(view);
            EditorUtility.SetDirty(panel);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log(Tag + "完成：" + UiRoot + "/" + QuestLogPanelView.PanelName
                      + "，batch=" + batch + "，缺图=" + CountNull(art));
        }

        static void EnsureHudButton(GameObject uiRoot, WhiteboxGame game, QuestLogArtSet art, Font font)
        {
            WhiteboxUiActions actions = uiRoot.GetComponent<WhiteboxUiActions>();
            if (actions == null) actions = uiRoot.AddComponent<WhiteboxUiActions>();
            if (actions.Game == null && game != null) { actions.Game = game; EditorUtility.SetDirty(actions); }

            Transform t = FindDeep(uiRoot.transform, BtnName);
            GameObject go;
            if (t == null)
            {
                go = new GameObject(BtnName, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(uiRoot.transform, false);
                RectTransform rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 1f);
                rt.anchoredPosition = new Vector2(-28f, -26f);
                rt.sizeDelta = new Vector2(190f, 52f);

                Image img = go.GetComponent<Image>();
                img.sprite = art != null ? art.btnSecondary : null;
                img.type = Image.Type.Sliced;
                img.color = Color.white;
                img.raycastTarget = true;

                GameObject label = new GameObject("Label", typeof(RectTransform), typeof(Text));
                label.transform.SetParent(go.transform, false);
                RectTransform lr = (RectTransform)label.transform;
                lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
                lr.offsetMin = Vector2.zero; lr.offsetMax = Vector2.zero;
                Text txt = label.GetComponent<Text>();
                txt.font = font; txt.fontSize = 18; txt.color = new Color(0.16f, 0.13f, 0.09f, 1f);
                txt.alignment = TextAnchor.MiddleCenter;
                txt.fontStyle = FontStyle.Bold;
                txt.raycastTarget = false;
                txt.text = "任务日志 · J";
                EditorUtility.SetDirty(go);
            }
            else go = t.gameObject;

            Button btn = go.GetComponent<Button>();
            if (btn == null) btn = go.AddComponent<Button>();
            btn.targetGraphic = go.GetComponent<Image>();
            if (btn.onClick.GetPersistentEventCount() == 0)
            {
                UnityEventTools.AddPersistentListener(btn.onClick, new UnityAction(actions.OpenQuestLog));
                EditorUtility.SetDirty(btn);
            }
        }

        static QuestLogArtSet LoadArt()
        {
            QuestLogArtSet a = new QuestLogArtSet();
            a.panelBig = S(Pnl + "pn_面板_大");
            a.card = S(Pnl + "pn_卡片");
            a.listRow = S(Pnl + "pn_列表行");
            a.highlightRow = S(Pnl + "pn_高亮行");
            a.bottomBar = S(Pnl + "pn_底部操作条");

            a.btnPrimary = S(Btn + "bt_主按钮");
            a.btnSecondary = S(Btn + "bt_次按钮");
            a.btnGhost = S(Btn + "bt_幽灵按钮");
            a.btnDisabled = S(Btn + "bt_禁用按钮");
            a.btnCloseSquare = S(Btn + "bt_关闭方键");

            a.statusDotTeal = S(Tab + "cp_状态点_青");
            a.statusDotAmber = S(Tab + "cp_状态点_琥珀");
            a.labelTeal = S(Tab + "cp_类别_青");
            a.labelAmber = S(Tab + "cp_标签_琥珀");

            a.dotEmpty = S(Prg + "pg_容量点_空心");
            a.dotFilled = S(Prg + "pg_容量点_实心");
            a.ringBottom = S(Prg + "pg_进度环_底");
            a.ringTeal = S(Prg + "pg_进度环_青");

            a.icTask = S(Fun + "ic_功能_任务");
            a.icCheck = S(Fun + "ic_功能_勾");
            a.icLock = S(Fun + "ic_功能_锁");
            a.icArrow = S(Fun + "ic_功能_箭头");
            a.icClose = S(Fun + "ic_功能_关闭");
            a.icStar = S(Fun + "ic_功能_星");

            a.stepDone = S(Dec + "de_步骤序号_完成");
            a.stepCurrent = S(Dec + "de_步骤序号_当前");
            a.stepDefault = S(Dec + "de_步骤序号_默认");
            return a;
        }

        static Sprite S(string rel)
        {
            string path = UiArtImportSettings.LibraryFolder + "/" + rel + ".png";
            Sprite sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sp == null) Debug.LogWarning(Tag + "拆件缺失：" + path);
            return sp;
        }

        static int CountNull(QuestLogArtSet a)
        {
            Sprite[] probe =
            {
                a.panelBig, a.card, a.listRow, a.btnGhost, a.btnSecondary,
                a.dotEmpty, a.icCheck, a.stepDone, a.stepCurrent, a.stepDefault
            };
            int n = 0;
            for (int i = 0; i < probe.Length; i++) if (probe[i] == null) n++;
            return n;
        }

        static GameObject FindRoot(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) if (roots[i].name == name) return roots[i];
            return null;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) if (all[i].name == name) return all[i];
            return null;
        }
    }
}
