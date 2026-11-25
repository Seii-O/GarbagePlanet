// @trace AUDIO-001
//
// 音效管理器（GarbagePlanet / 垃圾星球）
//
// 设计目标：导音效不需要改代码、不需要在场景里拖引用、不需要给按钮挂 AudioSource。
//   1) 每个音效一个「槽位」（SfxId），素材按槽位名放到 Resources 目录即自动加载：
//        Assets/Resources/Audio/SFX/UiClick/click_1.wav
//        Assets/Resources/Audio/SFX/UiClick/click_2.wav   ← 同槽位多个文件 = 随机变体
//        Assets/Resources/Audio/BGM/main.mp3              ← 自动循环播放
//   2) 播放只需一行：Sfx.Play(SfxId.UiClick);
//      3D 世界音：Sfx.PlayAt(SfxId.Pickup, transform.position);
//   3) 音量读 TitleController 写在 PlayerPrefs 的两个滑块（tp.volume.sfx / tp.volume.music），
//      SFX 与 BGM 各走独立通道，互不牵连（不再用 AudioListener.volume 做合并衰减）。
//
// 新增一个音效的三步：
//   ① SfxId 里加一个枚举值（例如 UiHover）；
//   ② 在 Defs 表里给它一行默认参数（音量/音高抖动/冷却/是否 3D）；
//   ③ 在 Assets/Resources/Audio/SFX/UiHover/ 下丢音频文件。
//   菜单 Tools/垃圾星球白膜/㉖ 会直接打开素材目录，㉗ 会列出每个槽位缺哪些文件。

using System.Collections.Generic;
using UnityEngine;

/// <summary>可播放的音效槽位。槽位名 == Resources 下的文件夹名。</summary>
public enum SfxId
{
    UiClick,        // 任意按钮点击
    UiOpen,         // 打开面板
    UiClose,        // 关闭面板
    Pickup,         // 拾取 / 拆解垃圾
    Deposit,        // 中转站投放
    ProcessStart,   // 开始加工
    ProcessDone,    // 加工完成
    CraftStart,     // 开始制造
    CraftDone,      // 制造完成
    Upgrade,        // 工具/专精/背包升级
    LevelUp,        // 人物升级
    Coin,           // 金币进出
    GachaSpin,      // 转盘启动
    GachaWin,       // 转盘中奖
    DogFetch,       // 小狗叼回垃圾
    DogBark,        // 狗叫（多段随机）
    DogSniff,       // 狗闻东西
    DogWhine,       // 狗撒娇
    DogPant,        // 狗喘气
    Step,           // 走路脚步
    Run,            // 跑步脚步
    Purify,         // 净化区扩张
    Error,          // 操作被拒 / 资源不足
}

/// <summary>单个槽位的播放参数。</summary>
public struct SfxDef
{
    public float volume;     // 基础音量 0..1（再乘玩家滑块）
    public float pitchMin;   // 随机音高下限（避免连续播放听起来像复读）
    public float pitchMax;   // 随机音高上限
    public float cooldown;   // 同槽位最短间隔（秒，用 unscaledTime，暂停也算）
    public float spatial;    // 0 = 纯 2D；>0 = 3D，按世界位置衰减

    public SfxDef(float volume, float pitchMin = 1f, float pitchMax = 1f,
                  float cooldown = 0.05f, float spatial = 0f)
    {
        this.volume = volume;
        this.pitchMin = pitchMin;
        this.pitchMax = pitchMax;
        this.cooldown = cooldown;
        this.spatial = spatial;
    }
}

/// <summary>
/// 音效 / BGM 唯一入口。运行时自动创建（DontDestroyOnLoad），跨「标题 → 关卡」不重播不重建。
/// 没有任何素材时全程静音，只在 Console 打一条清单，不影响玩法。
/// </summary>
public sealed class 音效管理 : MonoBehaviour
{
    public const string SfxRoot = "Audio/SFX/";
    public const string BgmRoot = "Audio/BGM/";

    /// <summary>与 TitleController 保持一致的 PlayerPrefs 键。</summary>
    const string KeySfx = "tp.volume.sfx";
    const string KeyMusic = "tp.volume.music";

    /// <summary>同时可发声的音源数（超了会抢占最久没用的那个）。</summary>
    const int VoiceCount = 12;

    /// <summary>
    /// 每个槽位的默认参数。数值口径：UI 类偏轻（0.4~0.6），玩法正反馈略重（0.7），
    /// 冷却用来防止连点/连续拾取时糊成一片。
    /// </summary>
    static readonly Dictionary<SfxId, SfxDef> Defs = new Dictionary<SfxId, SfxDef>
    {
        { SfxId.UiClick,      new SfxDef(0.45f, 0.97f, 1.03f, 0.04f) },
        { SfxId.UiOpen,       new SfxDef(0.55f, 1.00f, 1.00f, 0.10f) },
        { SfxId.UiClose,      new SfxDef(0.50f, 0.98f, 1.00f, 0.10f) },
        { SfxId.Pickup,       new SfxDef(0.70f, 0.94f, 1.06f, 0.05f) },
        { SfxId.Deposit,      new SfxDef(0.80f, 0.98f, 1.02f, 0.15f) },
        { SfxId.ProcessStart, new SfxDef(0.70f, 1.00f, 1.00f, 0.20f) },
        { SfxId.ProcessDone,  new SfxDef(0.85f, 1.00f, 1.00f, 0.20f) },
        { SfxId.CraftStart,   new SfxDef(0.70f, 1.00f, 1.00f, 0.20f) },
        { SfxId.CraftDone,    new SfxDef(0.85f, 1.00f, 1.00f, 0.20f) },
        { SfxId.Upgrade,      new SfxDef(0.90f, 1.00f, 1.00f, 0.25f) },
        { SfxId.LevelUp,      new SfxDef(0.95f, 1.00f, 1.00f, 0.40f) },
        { SfxId.Coin,         new SfxDef(0.65f, 0.96f, 1.04f, 0.06f) },
        { SfxId.GachaSpin,    new SfxDef(0.80f, 1.00f, 1.00f, 0.30f) },
        { SfxId.GachaWin,     new SfxDef(0.95f, 1.00f, 1.00f, 0.50f) },
        { SfxId.DogFetch,     new SfxDef(0.70f, 0.98f, 1.04f, 0.30f, 0.6f) },
        { SfxId.DogBark,      new SfxDef(0.80f, 0.94f, 1.06f, 0.60f, 0.7f) },
        { SfxId.DogSniff,     new SfxDef(0.40f, 1.00f, 1.00f, 2.00f, 0.7f) },
        { SfxId.DogWhine,     new SfxDef(0.55f, 0.98f, 1.02f, 1.50f, 0.7f) },
        { SfxId.DogPant,      new SfxDef(0.45f, 1.00f, 1.00f, 2.00f, 0.7f) },
        { SfxId.Step,         new SfxDef(0.32f, 0.92f, 1.08f, 0.18f, 1f) },
        { SfxId.Run,          new SfxDef(0.40f, 0.94f, 1.06f, 0.20f, 1f) },
        { SfxId.Purify,       new SfxDef(0.75f, 1.00f, 1.00f, 0.50f) },
        { SfxId.Error,        new SfxDef(0.60f, 1.00f, 1.00f, 0.20f) },
    };

    static 音效管理 instance;
    static bool shuttingDown;

    /// <summary>当前实例；编辑模式下为 null（不误建对象）。</summary>
    public static 音效管理 Instance
    {
        get
        {
            if (instance == null && Application.isPlaying && !shuttingDown) EnsureExists();
            return instance;
        }
    }

    readonly Dictionary<SfxId, AudioClip[]> clips = new Dictionary<SfxId, AudioClip[]>();
    readonly Dictionary<SfxId, float> lastPlay = new Dictionary<SfxId, float>();
    readonly HashSet<SfxId> warned = new HashSet<SfxId>();
    readonly List<string> missing = new List<string>();

    AudioSource[] voices;
    int voiceCursor;
    AudioSource musicSource;
    AudioClip defaultBgm;
    Coroutine fadeRoutine;

    float sfxVolume = 1f;
    float musicVolume = 1f;
    bool quitting;

    /// <summary>已加载到素材的槽位数量。</summary>
    public int LoadedSfxCount { get; private set; }
    /// <summary>当前 SFX 通道音量（0..1，已含玩家滑块）。</summary>
    public float SfxVolume => sfxVolume;
    /// <summary>当前 BGM 通道音量（0..1，已含玩家滑块）。</summary>
    public float MusicVolume => musicVolume;

    // ------------------------------------------------------------------
    // 生命周期：进 Play 就自动建一个常驻实例，两个场景共用
    // ------------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        // 关闭 Domain Reload 时静态字段会跨 Play 保留，这里显式复位
        shuttingDown = false;
        instance = null;
        if (!Application.isPlaying) return;
        EnsureExists();
    }

    static void EnsureExists()
    {
        if (instance != null || shuttingDown) return;
        音效管理 existing = FindObjectOfType<音效管理>(true);
        if (existing != null) { instance = existing; return; }

        GameObject go = new GameObject("[音效管理]");
        DontDestroyOnLoad(go);
        go.AddComponent<音效管理>();
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);

        BuildVoices();
        LoadClips();
        ReadVolumePrefs();
        LogReport();

        if (defaultBgm != null) PlayMusic(null);
    }

    void OnApplicationQuit() { quitting = true; shuttingDown = true; }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    // ------------------------------------------------------------------
    // 初始化
    // ------------------------------------------------------------------

    void BuildVoices()
    {
        voices = new AudioSource[VoiceCount];
        for (int i = 0; i < VoiceCount; i++)
        {
            GameObject child = new GameObject("Voice_" + i);
            child.transform.SetParent(transform, false);
            AudioSource src = child.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 0f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 2f;
            src.maxDistance = 18f;
            src.dopplerLevel = 0f; // 星球场景移动快，开多普勒容易发飘
            voices[i] = src;
        }

        GameObject bgm = new GameObject("BGM");
        bgm.transform.SetParent(transform, false);
        musicSource = bgm.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.loop = true;
        musicSource.spatialBlend = 0f;
        musicSource.volume = 0f;
        musicSource.ignoreListenerPause = true;
    }

    void LoadClips()
    {
        foreach (SfxId id in System.Enum.GetValues(typeof(SfxId)))
        {
            AudioClip[] found = Resources.LoadAll<AudioClip>(SfxRoot + id);
            if (found == null || found.Length == 0)
            {
                // 兼容「不建子文件夹，直接 SFX/UiClick.wav」的放法
                AudioClip single = Resources.Load<AudioClip>(SfxRoot + id);
                if (single != null) found = new[] { single };
            }

            if (found != null && found.Length > 0)
            {
                clips[id] = found;
                LoadedSfxCount++;
            }
            else
            {
                missing.Add(id.ToString());
            }
        }

        AudioClip[] bgmClips = Resources.LoadAll<AudioClip>(BgmRoot);
        if (bgmClips != null && bgmClips.Length > 0)
        {
            // 约定：bgm_* = 音乐（排序后取第一首当默认曲）；amb_* = 环境音，只按名字点播，不参与默认
            System.Array.Sort(bgmClips, (a, b) => string.CompareOrdinal(a.name, b.name));
            for (int i = 0; i < bgmClips.Length; i++)
            {
                if (bgmClips[i].name.StartsWith("bgm_", System.StringComparison.OrdinalIgnoreCase))
                {
                    defaultBgm = bgmClips[i];
                    break;
                }
            }
            if (defaultBgm == null) defaultBgm = bgmClips[0];
        }
    }

    void LogReport()
    {
        string msg = "[音效] 就绪：SFX " + LoadedSfxCount + "/" + Defs.Count + " 个槽位有素材";
        if (defaultBgm != null) msg += "，BGM = " + defaultBgm.name;
        else msg += "，BGM 未配置";
        Debug.Log(msg);

        if (missing.Count > 0)
            Debug.Log("[音效] 待补素材（放对应文件夹即生效）：" + string.Join("、", missing.ToArray()));
    }

    /// <summary>按 PlayerPrefs 重新套用两条通道音量（TitleController 的滑块改动会调这里）。</summary>
    public void ReadVolumePrefs()
    {
        sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(KeySfx, 1f));
        musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyMusic, 1f));
        if (musicSource != null)
        {
            if (fadeRoutine == null) musicSource.volume = musicVolume;
        }
    }

    // ------------------------------------------------------------------
    // 播放
    // ------------------------------------------------------------------

    /// <summary>播放 2D 音效。volumeScale 用于同一音效的强弱区分（如拾取 1 件 vs 5 件）。</summary>
    public void Play(SfxId id, float volumeScale = 1f, float pitchOffset = 0f)
    {
        PlayInternal(id, volumeScale, pitchOffset, null);
    }

    /// <summary>播放 3D 音效：声音从世界坐标发出，离开会衰减。</summary>
    public void PlayAt(SfxId id, Vector3 position, float volumeScale = 1f)
    {
        PlayInternal(id, volumeScale, 0f, position);
    }

    void PlayInternal(SfxId id, float volumeScale, float pitchOffset, Vector3? position)
    {
        if (quitting) return;

        SfxDef def;
        if (!Defs.TryGetValue(id, out def)) def = new SfxDef(0.7f);

        AudioClip[] list;
        if (!clips.TryGetValue(id, out list) || list == null || list.Length == 0)
        {
            if (warned.Add(id))
                Debug.LogWarning("[音效] 缺少素材：" + SfxRoot + id + "/ 下没有音频文件，本次播放已跳过");
            return;
        }

        float now = Time.unscaledTime;
        float last;
        if (lastPlay.TryGetValue(id, out last) && now - last < def.cooldown) return;
        lastPlay[id] = now;

        AudioSource src = TakeVoice();
        src.transform.position = position.HasValue ? position.Value : transform.position;
        src.spatialBlend = position.HasValue ? Mathf.Max(0.05f, def.spatial <= 0f ? 1f : def.spatial) : 0f;
        src.clip = list[UnityEngine.Random.Range(0, list.Length)];
        src.volume = Mathf.Clamp01(def.volume * Mathf.Clamp01(volumeScale) * sfxVolume);

        float pitch = def.pitchMax > def.pitchMin
            ? UnityEngine.Random.Range(def.pitchMin, def.pitchMax)
            : def.pitchMin;
        src.pitch = Mathf.Clamp(pitch + pitchOffset, 0.5f, 2f);
        src.Play();
    }

    AudioSource TakeVoice()
    {
        for (int i = 0; i < voices.Length; i++)
        {
            int idx = (voiceCursor + i) % voices.Length;
            if (!voices[idx].isPlaying)
            {
                voiceCursor = (idx + 1) % voices.Length;
                return voices[idx];
            }
        }
        // 全忙：抢占下一个（短音效场景下听感损失最小）
        voiceCursor = (voiceCursor + 1) % voices.Length;
        return voices[voiceCursor];
    }

    // ------------------------------------------------------------------
    // BGM
    // ------------------------------------------------------------------

    /// <summary>
    /// 切歌（同曲不打断）。name 为空 = 播 BGM 文件夹里的默认曲；
    /// 素材放在 Assets/Resources/Audio/BGM/&lt;name&gt;.*
    /// </summary>
    public void PlayMusic(string name, float fade = 0.6f)
    {
        AudioClip clip = null;
        if (!string.IsNullOrEmpty(name)) clip = Resources.Load<AudioClip>(BgmRoot + name);
        if (clip == null) clip = defaultBgm;

        if (clip == null)
        {
            if (musicSource != null) musicSource.Stop();
            return;
        }
        if (musicSource.clip == clip && musicSource.isPlaying) return;

        musicSource.clip = clip;
        musicSource.loop = true;
        musicSource.volume = 0f;
        musicSource.Play();

        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeMusic(musicVolume, fade));
    }

    public void StopMusic(float fade = 0.4f)
    {
        if (musicSource == null || !musicSource.isPlaying) return;
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeMusicOut(fade));
    }

    System.Collections.IEnumerator FadeMusic(float target, float duration)
    {
        float from = musicSource.volume;
        if (duration <= 0f)
        {
            musicSource.volume = target;
            fadeRoutine = null;
            yield break;
        }
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            musicSource.volume = Mathf.Lerp(from, target, Mathf.Clamp01(t / duration));
            yield return null;
        }
        musicSource.volume = target;
        fadeRoutine = null;
    }

    System.Collections.IEnumerator FadeMusicOut(float duration)
    {
        float from = musicSource.volume;
        if (duration <= 0f) duration = 0.01f;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            musicSource.volume = Mathf.Lerp(from, 0f, Mathf.Clamp01(t / duration));
            yield return null;
        }
        musicSource.volume = 0f;
        musicSource.Stop();
        fadeRoutine = null;
    }

    // ------------------------------------------------------------------
    // 调试
    // ------------------------------------------------------------------

    /// <summary>把每个槽位的素材情况打到 Console（菜单 ㉗ 也调这个）。</summary>
    public void DumpStatus()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("[音效] 素材清单（目录 Assets/Resources/Audio/SFX/<槽位名>/）");
        foreach (SfxId id in System.Enum.GetValues(typeof(SfxId)))
        {
            AudioClip[] list;
            bool has = clips.TryGetValue(id, out list) && list != null && list.Length > 0;
            sb.AppendLine("  " + (has ? "√ " : "× ") + id
                          + (has ? "（" + list.Length + " 个变体）" : ""));
        }
        sb.AppendLine("  BGM：" + (defaultBgm != null ? defaultBgm.name : "未配置"));
        Debug.Log(sb.ToString());
    }
}

/// <summary>
/// 播放用的静态门面：Sfx.Play(SfxId.UiClick)。没有实例时自动创建；编辑模式下是空操作。
/// </summary>
public static class Sfx
{
    public static void Play(SfxId id, float volumeScale = 1f)
    {
        音效管理 m = 音效管理.Instance;
        if (m != null) m.Play(id, volumeScale);
    }

    public static void PlayAt(SfxId id, Vector3 position, float volumeScale = 1f)
    {
        音效管理 m = 音效管理.Instance;
        if (m != null) m.PlayAt(id, position, volumeScale);
    }

    public static void PlayMusic(string name = null)
    {
        音效管理 m = 音效管理.Instance;
        if (m != null) m.PlayMusic(name);
    }

    public static void StopMusic()
    {
        音效管理 m = 音效管理.Instance;
        if (m != null) m.StopMusic();
    }

    /// <summary>标题界面的音量滑块改动后调用：重新读 PlayerPrefs 并套到两条通道。</summary>
    public static void ApplyVolumePrefs()
    {
        音效管理 m = 音效管理.Instance;
        if (m != null) m.ReadVolumePrefs();
    }
}
