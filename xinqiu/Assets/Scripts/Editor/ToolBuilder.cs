// @trace TOOL-001, TOOL-014, NUM-017
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 工具系统的一键生成 + 接线（Tools/垃圾星球白膜/⑫ 生成工具配置资产）。
    /// 红线：用 Editor API 生成 .asset，不手写场景/预制体 YAML；旧场景未跑本菜单也能运行时兜底。
    /// </summary>
    public static class ToolBuilder
    {
        public const string ConfigPath = "Assets/Data/ToolConfig.asset";
        const string Tag = "[ToolBuilder] ";

        [MenuItem("Tools/垃圾星球白膜/⑫ 生成工具配置资产", false, 18)]
        public static void Build()
        {
            ToolConfig cfg = EnsureConfig();
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != WhiteboxBuilder.ScenePath)
            {
                Debug.Log(Tag + "配置已就绪：" + ConfigPath + "（请先打开白膜场景再接线）");
                return;
            }
            WireScene(scene, cfg);
        }

        /// <summary>命令行 / CI 批处理入口：打开白膜场景 → 生成配置 → 接线 → 保存。</summary>
        public static void BuildBatch()
        {
            ToolConfig cfg = EnsureConfig();
            Scene scene = EditorSceneManager.OpenScene(WhiteboxBuilder.ScenePath, OpenSceneMode.Single);
            WireScene(scene, cfg);
        }

        static void WireScene(Scene scene, ToolConfig cfg)
        {
            GameObject sys = null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                if (roots[i].name == "sys_whitebox") { sys = roots[i]; break; }
            if (sys == null) { Debug.LogWarning(Tag + "未找到 sys_whitebox，跳过接线"); return; }

            ToolSystem tool = sys.GetComponent<ToolSystem>();
            if (tool == null) { tool = sys.AddComponent<ToolSystem>(); }
            tool.Wire(cfg);
            EditorUtility.SetDirty(tool);

            WhiteboxGame game = sys.GetComponent<WhiteboxGame>();
            if (game != null) { game.WireTool(tool); EditorUtility.SetDirty(game); }
            else Debug.LogWarning(Tag + "未找到 WhiteboxGame，工具配置已生成但未接入启动链");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log(Tag + "完成：配置=" + ConfigPath + "，系统=sys_whitebox/ToolSystem");
        }

        static ToolConfig EnsureConfig()
        {
            ToolConfig cfg = AssetDatabase.LoadAssetAtPath<ToolConfig>(ConfigPath);
            if (cfg != null) return cfg;
            string dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder("Assets", "Data");
            cfg = ToolConfig.CreateDefault();
            AssetDatabase.CreateAsset(cfg, ConfigPath);
            AssetDatabase.SaveAssets();
            Debug.Log(Tag + "已生成默认配置：" + ConfigPath);
            return cfg;
        }
    }
}
