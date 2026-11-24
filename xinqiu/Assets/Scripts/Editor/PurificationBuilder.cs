// @trace MECH-011, MECH-002, LEV-001
// 星球净化机制接线：全部 Editor API，不手写 .unity / .asset YAML（红线同 WhiteboxBuilder）。
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 星球净化系统一键接线（Tools/垃圾星球白膜/⑰ 生成星球净化系统并接线）。
    ///
    /// 不生成任何新场景实体：净化区以现有中转站为中心，逻辑在运行时组件 PurificationSystem 里。
    /// 本 Builder 只做三件事：
    ///   1. 在 sys_whitebox 上幂等挂 PurificationSystem；
    ///   2. 收集场景里所有 env_station_* 作为净化中心注入（当前一个，以后加多个自动纳入）；
    ///   3. Wire 到 WhiteboxGame，保存场景。
    ///
    /// 域重载后自动补齐一次（SessionState 防重复）；手动菜单 ⑰ 可随时重跑。
    /// </summary>
    [InitializeOnLoad]
    public static class PurificationBuilder
    {
        const string AutoKey = "TrashPlanet.Purification.Field.v1";
        const string SysRoot = "sys_whitebox";
        const string WorldRoot = "WorldRoot";
        const string StationPrefix = "env_station_";
        const string Tag = "[PurificationBuilder] ";

        static PurificationBuilder()
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
            if (!scene.IsValid() || scene.path != WhiteboxBuilder.ScenePath) return; // 不在白膜场景：等菜单⑰
            if (FindRoot(scene, SysRoot) == null || FindRoot(scene, WorldRoot) == null) return; // 等菜单①就绪

            SessionState.SetBool(AutoKey, true);
            EditorApplication.update -= AutoEnsure;
            Build();
            Debug.Log(Tag + "已自动补齐星球净化系统接线");
        }

        [MenuItem("Tools/垃圾星球白膜/⑰ 生成星球净化系统并接线", false, 25)]
        public static void Build()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != WhiteboxBuilder.ScenePath)
            {
                Debug.LogWarning(Tag + "请打开 WhiteboxPrototype.unity 再执行本菜单");
                return;
            }

            GameObject sys = FindRoot(scene, SysRoot);
            GameObject world = FindRoot(scene, WorldRoot);
            if (sys == null || world == null)
            {
                Debug.LogError(Tag + "找不到 " + SysRoot + " / " + WorldRoot + "，请先执行 ① 生成场景与预制体");
                return;
            }

            // 收集所有中转站作为净化中心（按名字前缀，含失活）
            var centers = new System.Collections.Generic.List<Transform>();
            for (int i = 0; i < world.transform.childCount; i++)
            {
                Transform c = world.transform.GetChild(i);
                if (c.name.StartsWith(StationPrefix, System.StringComparison.Ordinal)) centers.Add(c);
            }
            if (centers.Count == 0)
            {
                // 兜底：场景根下直接找（兼容中转站未挂 WorldRoot 的旧场景）
                GameObject st = GameObject.Find(StationPrefix + "whitebox");
                if (st != null) centers.Add(st.transform);
            }
            var arr = centers.ToArray();

            PurificationSystem field = sys.GetComponent<PurificationSystem>();
            if (field == null) field = sys.AddComponent<PurificationSystem>();
            field.Wire(arr);

            WhiteboxGame game = sys.GetComponent<WhiteboxGame>();
            if (game != null) game.WirePurification(field);
            else Debug.LogWarning(Tag + "sys_whitebox 上没有 WhiteboxGame，Wire 跳过");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log(Tag + "完成：净化中心 " + arr.Length + " 处（" + Names(arr) + "），系统已挂 "
                      + SysRoot + "，Wire=" + (game != null) + "，场景已保存");
        }

        static string Names(Transform[] arr)
        {
            if (arr == null || arr.Length == 0) return "无";
            string s = arr[0] != null ? arr[0].name : "?";
            for (int i = 1; i < arr.Length; i++) s += ", " + (arr[i] != null ? arr[i].name : "?");
            return s;
        }

        static GameObject FindRoot(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                if (roots[i].name == name) return roots[i];
            return null;
        }
    }
}
