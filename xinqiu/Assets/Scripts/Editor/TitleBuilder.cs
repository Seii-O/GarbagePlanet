// @trace UI-005, MECH-001
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 标题场景按钮接线（菜单⑭）：把用户手搭的 Title.unity 上的按钮接到 TitleController。
    /// 红线：与 WhiteboxBuilder / InventoryBuilder 一致——全部用 Editor API 接线，不手写 .unity YAML。
    /// 用户手搭的按钮是默认命名（Button (Legacy)），靠按钮子 Text 的文字识别功能：
    ///   "开始新游戏" → StartNewGame；"继续游戏" → ContinueGame；"退出游戏" → QuitGame；
    ///   "设置"等面板显隐按钮保留原 SetActive 不动。
    /// 可重复执行：绑定前清空目标按钮的持久事件，保证幂等。
    /// </summary>
    public static class TitleBuilder
    {
        public const string ScenePath = "Assets/Scenes/Title.unity";
        const string Tag = "[TitleBuilder] ";
        const string CtrlGoName = "TitleController";

        [MenuItem("Tools/垃圾星球白膜/⑭ 接线标题场景按钮", false, 19)]
        public static void Build()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != ScenePath)
            {
                Debug.LogWarning(Tag + "请先打开标题场景 " + ScenePath + " 再执行本菜单");
                return;
            }
            BuildInScene(scene, false);
        }

        /// <summary>批处理入口，供命令行 / CI 在副本工程上验证接线。</summary>
        public static void BuildBatch()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            BuildInScene(scene, true);
        }

        static void BuildInScene(Scene scene, bool batch)
        {
            // 1) 控制器宿主：复用同名对象，否则新建（新建对象落在当前活动场景 = Title）
            GameObject host = FindByName(scene, CtrlGoName);
            if (host == null)
            {
                host = new GameObject(CtrlGoName);
                Debug.Log(Tag + "新建控制器宿主 " + CtrlGoName);
            }
            TitleController ctrl = host.GetComponent<TitleController>();
            if (ctrl == null) ctrl = host.AddComponent<TitleController>();

            // 2) 扫描全部 Button，按子 Text 文字归类
            var allButtons = new List<Button>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                allButtons.AddRange(root.GetComponentsInChildren<Button>(true));
            }

            var newGames = new List<Button>();
            var continues = new List<Button>();
            var quits = new List<Button>();
            var report = new List<string>();
            foreach (Button b in allButtons)
            {
                string label = LabelOf(b);
                if (label.Contains("开始新游戏") || label == "开始游戏") newGames.Add(b);
                else if (label.Contains("继续游戏")) continues.Add(b);
                else if (label.Contains("退出游戏") || label == "退出") quits.Add(b);
                // "设置" 等面板显隐按钮不动，保留用户原有的 SetActive 绑定
                if (!string.IsNullOrEmpty(label)) report.Add(label + "→" + b.name);
            }

            // 4) 绑定（先清后有，幂等）
            UnityAction startNew = ctrl.StartNewGame;
            UnityAction contGame = ctrl.ContinueGame;
            UnityAction quitGame = ctrl.QuitGame;
            int bound = 0;
            foreach (Button b in newGames) { Bind(b, startNew); bound++; }
            foreach (Button b in continues) { Bind(b, contGame); bound++; }
            foreach (Button b in quits) { Bind(b, quitGame); bound++; }

            ctrl.Wire(FirstOrNull(newGames), FirstOrNull(continues), FirstOrNull(quits));

            // 5) 保存
            EditorUtility.SetDirty(ctrl);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene); // 菜单与批处理都落盘
            AssetDatabase.SaveAssets();

            Debug.Log(Tag + "完成：按钮[" + string.Join(", ", report) + "]；绑定=" + bound
                      + "（batch=" + batch + "）");
        }

        static Button FirstOrNull(List<Button> list) => list.Count > 0 ? list[0] : null;

        static string LabelOf(Button b)
        {
            Text[] ts = b.GetComponentsInChildren<Text>(true);
            string s = "";
            foreach (Text t in ts) if (!string.IsNullOrEmpty(t.text)) s += t.text;
            return (s ?? "").Replace("\n", "").Replace("\r", "").Replace(" ", "").Trim();
        }

        static void Bind(Button b, UnityAction action)
        {
            int n = b.onClick.GetPersistentEventCount();
            for (int i = n - 1; i >= 0; i--) UnityEventTools.RemovePersistentListener(b.onClick, i);
            UnityEventTools.AddPersistentListener(b.onClick, action);
            EditorUtility.SetDirty(b);
        }

        static GameObject FindByName(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name) return root;
                Transform[] all = root.GetComponentsInChildren<Transform>(true);
                foreach (Transform tr in all) if (tr.name == name) return tr.gameObject;
            }
            return null;
        }
    }
}
