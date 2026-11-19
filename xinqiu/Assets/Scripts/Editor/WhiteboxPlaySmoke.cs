using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 一次性 Play 冒烟测试：首次导入后自动打开白膜场景、进 Play 约 3 秒、收集运行时错误、退出 Play。
    /// 只跑一次（SessionState 跨域重载保持），当前场景有未保存改动时跳过，不弹保存对话框。
    /// 结果落盘 docs/whitebox-play-smoke.json；退出 Play 后场景仍停在白膜场景，方便用户直接按 Play。
    /// </summary>
    [InitializeOnLoad]
    public static class WhiteboxPlaySmoke
    {
        const string DoneKey = "TrashPlanet.Whitebox.SmokeDone.v1";
        const string RunningKey = "TrashPlanet.Whitebox.SmokeRunning";
        const string StartKey = "TrashPlanet.Whitebox.SmokeStart";
        const double Seconds = 3.0;

        static string ReportPath =>
            Path.Combine(Application.dataPath, "../docs/whitebox-play-smoke.json");

        static readonly List<string> runtimeErrors = new List<string>();
        static int frames;

        static WhiteboxPlaySmoke()
        {
            Application.logMessageReceived += OnLog;
            EditorApplication.update += OnUpdate;
            EditorApplication.delayCall += TryStart;
        }

        /// <summary>菜单 / 自动复验入口：删掉旧报告，重新进 Play 跑一次。</summary>
        [MenuItem("Tools/垃圾星球白膜/⑤ 重跑 Play 冒烟测试", false, 14)]
        public static void RunNowMenu() { RequestRun(); }

        public static void RequestRun()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[WhiteboxPlaySmoke] 请先在非 Play 状态下重跑");
                return;
            }
            EditorApplication.delayCall += DoRun;
        }

        static void DoRun()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
            try { if (File.Exists(ReportPath)) File.Delete(ReportPath); }
            catch (Exception e) { Debug.LogWarning("[WhiteboxPlaySmoke] 旧报告删除失败：" + e.Message); }

            // 当前场景就是白膜场景时才自动跑；有未保存改动先落盘，避免弹保存对话框
            Scene active = EditorSceneManager.GetActiveScene();
            if (!active.IsValid() || active.path != WhiteboxBuilder.ScenePath) return;
            if (active.isDirty) EditorSceneManager.SaveScene(active);

            SessionState.SetBool(DoneKey, true); // 阻止首次导入逻辑再插一脚
            SessionState.SetBool(RunningKey, true);
            SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
            runtimeErrors.Clear();
            frames = 0;

            EditorSceneManager.OpenScene(WhiteboxBuilder.ScenePath, OpenSceneMode.Single);
            Debug.Log("[WhiteboxPlaySmoke] 重新进入 Play 冒烟测试（UI 改造后复验），约 3 秒后自动退出");
            EditorApplication.isPlaying = true;
        }

        static void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (runtimeErrors.Count >= 40) return;
            string oneLine = (message ?? "").Replace("\r", " ").Replace("\n", " ");
            if (oneLine.Length > 220) oneLine = oneLine.Substring(0, 220);
            runtimeErrors.Add(type + ": " + oneLine);
        }

        static void TryStart()
        {
            if (Application.isBatchMode || EditorApplication.isPlaying) return;
            if (SessionState.GetBool(DoneKey, false)) return;
            // 报告存在说明已跑过：避免每次启动编辑器都自动进 Play
            if (File.Exists(ReportPath)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryStart;
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(WhiteboxBuilder.ScenePath) == null) return;

            if (EditorSceneManager.GetActiveScene().isDirty)
            {
                SessionState.SetBool(DoneKey, true);
                Debug.LogWarning("[WhiteboxPlaySmoke] 当前场景有未保存改动，跳过自动冒烟测试；请手动按 Play 验证");
                return;
            }

            SessionState.SetBool(DoneKey, true);
            SessionState.SetBool(RunningKey, true);
            SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
            runtimeErrors.Clear();
            frames = 0;

            EditorSceneManager.OpenScene(WhiteboxBuilder.ScenePath, OpenSceneMode.Single);
            Debug.Log("[WhiteboxPlaySmoke] 进入 Play 冒烟测试，约 3 秒后自动退出");
            EditorApplication.isPlaying = true;
        }

        static void OnUpdate()
        {
            if (!EditorApplication.isPlaying || !SessionState.GetBool(RunningKey, false)) return;
            frames++;
            double started = SessionState.GetFloat(StartKey, 0f);
            if (EditorApplication.timeSinceStartup - started < Seconds && frames < 240) return;

            SessionState.SetBool(RunningKey, false);
            WriteReport();
            Debug.Log("[WhiteboxPlaySmoke] 结束：运行帧数=" + frames
                      + "，捕获运行时错误 " + runtimeErrors.Count + " 条");
            EditorApplication.isPlaying = false;
        }

        static void WriteReport()
        {
            try
            {
                string dir = Path.Combine(Application.dataPath, "../docs");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"unityVersion\": \"" + Application.unityVersion + "\",");
                sb.AppendLine("  \"scene\": \"" + WhiteboxBuilder.ScenePath + "\",");
                sb.AppendLine("  \"frames\": " + frames + ",");
                sb.AppendLine("  \"pass\": " + (runtimeErrors.Count == 0 ? "true" : "false") + ",");
                sb.AppendLine("  \"runtimeErrorCount\": " + runtimeErrors.Count + ",");
                sb.AppendLine("  \"note\": \"自动 Play 约 3 秒的冒烟测试；只覆盖启动与若干帧 Update，不替代人工走完整任务链录屏\",");
                sb.AppendLine("  \"runtimeErrors\": [");
                for (int i = 0; i < runtimeErrors.Count; i++)
                    sb.Append("    \"" + Escape(runtimeErrors[i]) + "\""
                              + (i < runtimeErrors.Count - 1 ? "," : "") + Environment.NewLine);
                sb.AppendLine("  ]");
                sb.AppendLine("}");
                File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
                Debug.Log("[WhiteboxPlaySmoke] 报告已写入 docs/whitebox-play-smoke.json");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WhiteboxPlaySmoke] 报告写入失败：" + e.Message);
            }
        }

        static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
