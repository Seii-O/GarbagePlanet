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
    /// 手动 Play 冒烟测试（菜单⑤ 重跑 Play 冒烟测试）。
    ///
    /// 修复记录（2026-11-20）：旧版用 [InitializeOnLoad] + delayCall 自动进 Play，
    /// 报告文件缺失时每次域重载都会重跑；若用户已在 Play 模式，OpenScene 会抛
    /// "This cannot be used during play mode"，且残留的 RunningKey 会在约 3 秒后
    /// 强制 isPlaying=false，导致手动按 Play 也被退出（表现为"闪退"）。
    /// 现改为：①不再自动进 Play，只保留菜单⑤手动触发；②OpenScene 前后校验播放状态并 try/catch；
    /// ③用 SmokeOwnsPlay 标记确保只有本工具发起的 Play 才会被自动退出；④构造时清理残留标记。
    /// 静态校验（48 项）由 WhiteboxValidator 独立承担，不依赖本工具。
    /// </summary>
    [InitializeOnLoad]
    public static class WhiteboxPlaySmoke
    {
        const string DoneKey = "TrashPlanet.Whitebox.SmokeDone.v1";
        const string RunningKey = "TrashPlanet.Whitebox.SmokeRunning";
        const string StartKey = "TrashPlanet.Whitebox.SmokeStart";
        const string OwnsPlayKey = "TrashPlanet.Whitebox.SmokeOwnsPlay";
        const double Seconds = 3.0;

        static string ReportPath =>
            Path.Combine(Application.dataPath, "../docs/whitebox-play-smoke.json");

        static readonly List<string> runtimeErrors = new List<string>();
        static int frames;

        static WhiteboxPlaySmoke()
        {
            Application.logMessageReceived += OnLog;
            EditorApplication.update += OnUpdate;
            // 域重载时清理上次异常中断残留的标记，避免干扰用户手动按 Play。
            // 仅在非 Play（且不在切换中）时清，避免误清本工具自己发起的冒烟。
            if (!Application.isBatchMode && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SessionState.SetBool(RunningKey, false);
                SessionState.SetBool(OwnsPlayKey, false);
            }
        }

        /// <summary>菜单 / 手动复验入口：删旧报告，手动跑一次。不在 Play 时才受理。</summary>
        [MenuItem("Tools/垃圾星球白膜/⑤ 重跑 Play 冒烟测试", false, 14)]
        public static void RunNowMenu() { RequestRun(); }

        public static void RequestRun()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[WhiteboxPlaySmoke] 请先在非 Play 状态下重跑");
                return;
            }
            // 手动入口先清残留，确保本次是干净的冒烟
            SessionState.SetBool(RunningKey, false);
            SessionState.SetBool(OwnsPlayKey, false);
            EditorApplication.delayCall += DoRun;
        }

        static void DoRun()
        {
            // 延迟回调真正执行时可能已进入/正在切换 Play，必须二次校验。
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[WhiteboxPlaySmoke] 检测到已进入 Play，取消本次冒烟（避免在播放期调用 OpenScene）");
                SessionState.SetBool(RunningKey, false);
                SessionState.SetBool(OwnsPlayKey, false);
                return;
            }
            try { if (File.Exists(ReportPath)) File.Delete(ReportPath); }
            catch (Exception e) { Debug.LogWarning("[WhiteboxPlaySmoke] 旧报告删除失败：" + e.Message); }

            Scene active = EditorSceneManager.GetActiveScene();
            if (!active.IsValid() || active.path != WhiteboxBuilder.ScenePath)
            {
                Debug.LogWarning("[WhiteboxPlaySmoke] 当前场景不是白膜场景，取消冒烟");
                return;
            }
            if (active.isDirty) EditorSceneManager.SaveScene(active);

            // 打开场景只在确认非 Play 后执行，并加 try/catch 兜底
            try
            {
                EditorSceneManager.OpenScene(WhiteboxBuilder.ScenePath, OpenSceneMode.Single);
            }
            catch (Exception e)
            {
                SessionState.SetBool(RunningKey, false);
                SessionState.SetBool(OwnsPlayKey, false);
                Debug.LogError("[WhiteboxPlaySmoke] 打开白膜场景失败：" + e.Message);
                return;
            }

            SessionState.SetBool(DoneKey, true);
            SessionState.SetBool(RunningKey, true);
            SessionState.SetBool(OwnsPlayKey, true); // 仅本工具发起的 Play 才打这个标记
            SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
            runtimeErrors.Clear();
            frames = 0;

            Debug.Log("[WhiteboxPlaySmoke] 重新进入 Play 冒烟测试，约 " + Seconds + " 秒后自动退出");
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

        static void OnUpdate()
        {
            // 未在 Play：清掉标记，防止上次中断的残留影响下一次手动 Play
            if (!EditorApplication.isPlaying)
            {
                if (SessionState.GetBool(OwnsPlayKey, false) || SessionState.GetBool(RunningKey, false))
                {
                    SessionState.SetBool(OwnsPlayKey, false);
                    SessionState.SetBool(RunningKey, false);
                }
                return;
            }
            // 关键守卫：不是本工具发起的 Play（用户手动按的），完全不干预，不会强制退出
            if (!SessionState.GetBool(OwnsPlayKey, false)) return;
            if (!SessionState.GetBool(RunningKey, false)) return;

            frames++;
            double started = SessionState.GetFloat(StartKey, 0f);
            if (EditorApplication.timeSinceStartup - started < Seconds && frames < 240) return;

            SessionState.SetBool(RunningKey, false);
            SessionState.SetBool(OwnsPlayKey, false);
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
                sb.AppendLine("  \"note\": \"手动菜单⑤触发的 Play 冒烟测试（约 3 秒）；静态校验 48 项由 WhiteboxValidator 独立承担\",");
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
