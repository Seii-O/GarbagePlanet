// @trace AUDIO-001
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 音效素材目录工具（配合 Assets/Scripts/音效管理.cs）：
    ///   ㉖ 按 SfxId 补齐 Assets/Resources/Audio/SFX/&lt;槽位&gt;/ 文件夹并打开目录，拖文件进去即可；
    ///   ㉗ 打印每个槽位有没有素材、BGM 是否配置，用来快速定位「为什么没声音」。
    ///
    /// 红线：只创建空文件夹与打印报告，不改任何导入设置、不动场景。
    /// </summary>
    public static class SfxToolMenu
    {
        const string Root = "Assets/Resources/Audio";
        const string SfxDir = Root + "/SFX";
        const string BgmDir = Root + "/BGM";

        static readonly string[] AudioExt = { ".wav", ".mp3", ".ogg", ".aiff", ".aif" };

        [MenuItem("Tools/垃圾星球白膜/㉖ 打开音效素材目录", false, 26)]
        public static void OpenFolders()
        {
            EnsureFolders();
            AssetDatabase.Refresh();
            EditorUtility.RevealInFinder(SfxDir + "/");
        }

        [MenuItem("Tools/垃圾星球白膜/㉗ 检查音效接入情况", false, 27)]
        public static void Report()
        {
            EnsureFolders();

            int ok = 0, total = 0;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("[音效检查] 目录：" + SfxDir + "/<槽位名>/（放音频文件即生效）");

            foreach (SfxId id in System.Enum.GetValues(typeof(SfxId)))
            {
                total++;
                int n = CountAudio(SfxDir + "/" + id);
                if (n == 0) n = CountFileLike(SfxDir + "/" + id); // 兼容 SFX/UiClick.wav 单文件放法
                if (n > 0) ok++;
                sb.AppendLine("  " + (n > 0 ? "√ " : "× ") + id + (n > 0 ? "（" + n + " 个）" : ""));
            }

            int bgm = CountAudio(BgmDir);
            sb.AppendLine("  BGM：" + (bgm > 0 ? "√ " + bgm + " 首（按文件名排序取第一首循环）" : "× 未配置"));
            sb.AppendLine("[音效检查] 结果：" + ok + "/" + total + " 个槽位已有素材");
            Debug.Log(sb.ToString());

            if (ok == 0)
                Debug.LogWarning("[音效检查] 目前一个音效都没有：把 .wav/.mp3 拖进上面列出的文件夹即可，无需改代码。");
        }

        static void EnsureFolders()
        {
            bool created = false;
            foreach (SfxId id in System.Enum.GetValues(typeof(SfxId)))
            {
                string dir = SfxDir + "/" + id;
                if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); created = true; }
            }
            if (!Directory.Exists(BgmDir)) { Directory.CreateDirectory(BgmDir); created = true; }
            if (created)
            {
                AssetDatabase.Refresh();
                Debug.Log("[音效检查] 已补齐缺失的素材文件夹");
            }
        }

        /// <summary>统计目录下的音频文件数（忽略 .meta）。</summary>
        static int CountAudio(string dir)
        {
            if (!Directory.Exists(dir)) return 0;
            string[] files = Directory.GetFiles(dir);
            int n = 0;
            for (int i = 0; i < files.Length; i++)
            {
                string ext = Path.GetExtension(files[i]).ToLowerInvariant();
                for (int k = 0; k < AudioExt.Length; k++)
                    if (ext == AudioExt[k]) { n++; break; }
            }
            return n;
        }

        /// <summary>兼容「不建子文件夹，直接 SFX/槽位名.wav」的放法。</summary>
        static int CountFileLike(string slotDir)
        {
            string name = Path.GetFileName(slotDir);
            if (!Directory.Exists(SfxDir)) return 0;
            int n = 0;
            for (int k = 0; k < AudioExt.Length; k++)
                if (File.Exists(SfxDir + "/" + name + AudioExt[k])) n++;
            return n;
        }
    }
}
