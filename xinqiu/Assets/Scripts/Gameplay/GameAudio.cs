using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TrashPlanet.Whitebox
{
    public sealed class GameAudio : MonoBehaviour
    {
        public const string EffectsKey = "tp.volume.sfx";
        public const string MusicKey = "tp.volume.music";
        public static GameAudio Current { get; private set; }

        [System.Serializable]
        public sealed class Cue
        {
            public AudioClip clip;
            [Range(0f, 1f)] public float volume = 1f;
            [Range(0.5f, 2f)] public float pitch = 1f;
        }

        [SerializeField] Cue music = new Cue();
        [SerializeField] bool loopMusic = true;
        [SerializeField] Cue button = new Cue();
        [SerializeField] Cue pickup = new Cue();
        [SerializeField] Cue bagFull = new Cue();
        [SerializeField] Cue walking = new Cue();
        [SerializeField, Min(0.01f)] float movementSpeed = 0.15f;
        [SerializeField] Cue dogBark = new Cue();
        [SerializeField, Min(0.1f)] float barkDistance = 3f;
        [SerializeField, Min(0.1f)] float barkResetDistance = 4.5f;
        [SerializeField, Min(0f)] float barkGap = 0.2f;

        AudioSource musicSource, walkSource, buttonSource, pickupSource, bagFullSource, barkSource;
        WhiteboxGame game;
        Transform trackedPlayer;
        Vector3 lastPosition;
        bool dogNear;
        Coroutine barking;
        float nextButtonScan;
        readonly System.Collections.Generic.HashSet<Button> bound = new System.Collections.Generic.HashSet<Button>();

        static float Effects => Mathf.Clamp01(PlayerPrefs.GetFloat(EffectsKey, 1f));
        static float Music => Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, 1f));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterSceneSetup()
        {
            SceneManager.sceneLoaded -= EnsureSceneAudio;
            SceneManager.sceneLoaded += EnsureSceneAudio;
        }

        static void EnsureSceneAudio(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != TitleController.GameSceneName && scene.name != TitleController.TitleSceneName) return;
            if (FindObjectOfType<AudioListener>() == null)
            {
                Camera camera = Camera.main;
                if (camera != null) camera.gameObject.AddComponent<AudioListener>();
            }
            if (Current != null && Current.gameObject.scene == scene) return;
            GameObject fallback = Resources.Load<GameObject>("GameAudioFallback");
            if (fallback != null) Instantiate(fallback);
        }

        void Awake()
        {
            Current = this;
            game = FindObjectOfType<WhiteboxGame>();
            musicSource = CreateSource();
            walkSource = CreateSource();
            buttonSource = CreateSource();
            pickupSource = CreateSource();
            bagFullSource = CreateSource();
            barkSource = CreateSource();
            Configure(musicSource, music, Music, loopMusic);
            if (musicSource.clip != null) musicSource.Play();
        }

        AudioSource CreateSource()
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = true;
            return source;
        }

        static void Configure(AudioSource source, Cue cue, float gain, bool loop)
        {
            source.clip = cue.clip;
            source.volume = cue.volume * gain;
            source.pitch = cue.pitch;
            source.loop = loop;
        }

        public void RefreshVolume()
        {
            musicSource.volume = music.volume * Music;
            walkSource.volume = walking.volume * Effects;
            barkSource.volume = dogBark.volume * Effects;
            buttonSource.volume = Effects;
            pickupSource.volume = Effects;
            bagFullSource.volume = Effects;
        }

        public void PlayPickup() => PlayOnce(pickupSource, pickup);
        public void PlayBagFull() => PlayOnce(bagFullSource, bagFull);
        public void PlayButton() => PlayOnce(buttonSource, button);

        static void PlayOnce(AudioSource source, Cue cue)
        {
            if (cue.clip == null) return;
            source.pitch = cue.pitch;
            source.volume = Effects;
            source.PlayOneShot(cue.clip, cue.volume);
        }

        void Start() => BindButtons();

        void BindButtons()
        {
            bound.RemoveWhere(item => item == null);
            foreach (Button item in FindObjectsOfType<Button>(true))
                if (item.gameObject.scene == gameObject.scene && bound.Add(item))
                    item.onClick.AddListener(PlayButton);
        }

        void LateUpdate()
        {
            if (Time.unscaledTime >= nextButtonScan)
            {
                nextButtonScan = Time.unscaledTime + 0.15f;
                BindButtons();
            }

            Transform player = game != null ? game.Player : null;
            if (player == null || player != trackedPlayer || game.IsUiBlocking || Time.deltaTime <= 0f)
            {
                trackedPlayer = player;
                if (player != null) lastPosition = player.position;
                walkSource.Stop();
                return;
            }

            Vector3 position = player.position;
            bool moving = Vector3.Distance(position, lastPosition) >= movementSpeed * Time.deltaTime;
            lastPosition = position;
            if (!moving || walking.clip == null)
                walkSource.Stop();
            else if (!walkSource.isPlaying)
            {
                Configure(walkSource, walking, Effects, true);
                walkSource.Play();
            }

            if (game.Dog == null) { dogNear = false; return; }
            float distance = Vector3.Distance(position, game.Dog.transform.position);
            if (distance >= Mathf.Max(barkResetDistance, barkDistance + 0.01f)) dogNear = false;
            if (!dogNear && distance <= barkDistance)
            {
                dogNear = true;
                if (barking == null && dogBark.clip != null) barking = StartCoroutine(BarkTwice());
            }
        }

        IEnumerator BarkTwice()
        {
            Configure(barkSource, dogBark, Effects, false);
            barkSource.Play();
            while (barkSource.isPlaying) yield return null;
            yield return new WaitForSecondsRealtime(barkGap);
            barkSource.Play();
            while (barkSource.isPlaying) yield return null;
            barking = null;
        }

        void OnDestroy()
        {
            foreach (Button item in bound)
                if (item != null) item.onClick.RemoveListener(PlayButton);
            if (Current == this) Current = null;
        }
    }
}
