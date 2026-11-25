// @trace AUDIO-001
using UnityEditor;
using UnityEngine;

namespace TrashPlanet.Whitebox.Editor
{
    /// <summary>
    /// 音效导入设置：只作用于 Assets/Resources/Audio/ 下的音频，按「短音效 / 长循环」两类给不同参数。
    ///
    /// 为什么要做：Unity 默认把所有音频按 DecompressOnLoad + Vorbis 导入，
    /// 60 秒风声、3 分钟齿轮声会被整段解压进内存（几十 MB）。这里按文件名前缀分流：
    ///   bgm_* / amb_* / run_* / sniff_long → Streaming（边播边读，内存几乎不占）
    ///   其余（点击、拾取、狗叫…）         → CompressedInMemory / DecompressOnLoad（延迟低）
    ///
    /// 红线：只改导入参数，不动音频内容，不影响 Assets 下其它音频。
    /// </summary>
    public sealed class SfxAudioImporter : AssetPostprocessor
    {
        const string Root = "Assets/Resources/Audio/";
        const string AutoKey = "TrashPlanet.SfxImport.v1";

        /// <summary>长循环判定：BGM / 环境音 / 长风声等，走 Streaming 不常驻内存。</summary>
        static bool IsLongClip(string fileNoExt)
        {
            string f = fileNoExt.ToLowerInvariant();
            return f.StartsWith("bgm_") || f.StartsWith("amb_")
                   || f.StartsWith("run_") || f.StartsWith("dog_sniff")
                   || f.StartsWith("dog_pant");
        }

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Root)) return;
            AudioImporter importer = assetImporter as AudioImporter;
            if (importer == null) return;

            string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            bool longClip = IsLongClip(file);

            AudioImporterSampleSettings s = importer.defaultSampleSettings;
            if (longClip)
            {
                s.loadType = AudioClipLoadType.Streaming;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.6f;
                importer.forceToMono = false;   // 音乐保留立体声
            }
            else
            {
                // 短音效：0.5 秒内的用 DecompressOnLoad（零解码延迟），其余 CompressedInMemory
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.ADPCM;
                s.quality = 0.8f;
                importer.forceToMono = true;    // 单声道省内存，2D/小 3D 听感无差
            }
            // 预加载：短音效预先解码（播第一声不卡），长循环不预加载（省内存）。
            // 注意 Unity 2022 起 AudioImporter.preloadAudioData 已被标记为 error 级过时（CS0619），
            // 必须写进 AudioImporterSampleSettings.preloadAudioData（per-platform local setting）。
            s.preloadAudioData = !longClip;
            importer.defaultSampleSettings = s;
        }

        /// <summary>本次编辑器会话里自动跑一次，把「导入设置脚本出现之前」已导入的音频一并修正。</summary>
        [InitializeOnLoadMethod]
        static void AutoApplyOnce()
        {
            if (SessionState.GetBool(AutoKey, false)) return;
            EditorApplication.delayCall += () =>
            {
                if (SessionState.GetBool(AutoKey, false)) return;
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                SessionState.SetBool(AutoKey, true);
                ReimportAll(true);
            };
        }

        [MenuItem("Tools/垃圾星球白膜/㉘ 应用音效导入设置（压缩/流式）", false, 28)]
        public static void MenuReimport() => ReimportAll(false);

        static void ReimportAll(bool quiet)
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { Root.TrimEnd('/') });
            int n = 0, streaming = 0, compressed = 0, badPreload = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith(Root))
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    n++;

                    AudioImporter ai = AssetImporter.GetAtPath(path) as AudioImporter;
                    if (ai != null)
                    {
                        AudioImporterSampleSettings st = ai.defaultSampleSettings;
                        if (st.loadType == AudioClipLoadType.Streaming) streaming++;
                        else compressed++;
                        if (IsLongClip(System.IO.Path.GetFileNameWithoutExtension(path)) && st.preloadAudioData) badPreload++;
                    }
                }
            }
            if (n == 0)
            {
                if (!quiet) Debug.LogWarning("[音效导入] 没有找到音频：先把文件放进 Assets/Resources/Audio/SFX/<槽位>/");
                return;
            }
            AssetDatabase.Refresh();
            Debug.Log("[音效导入] 已按规则重设 " + n + " 个音频：Streaming " + streaming + " 个 / 压缩内存 " + compressed + " 个"
                      + (badPreload > 0 ? "（其中 " + badPreload + " 个长循环仍开着预加载）" : ""));
        }
    }
}
