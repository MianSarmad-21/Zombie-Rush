using UnityEngine;
using UnityEngine.SceneManagement;
using ZombieRush.Core;

namespace ZombieRush.Audio
{
    /// One place that plays every sound. Creates itself at startup (no scene setup),
    /// loads the SoundLibrary from Resources, keeps a small pool of AudioSources and
    /// plays the menu / gameplay music. Game-flow sounds (waves, coins, player hurt)
    /// come from GameEvents; weapons, zombies and the turret call Play/PlayAt directly.
    ///
    /// Volumes: Master + mute = AudioListener.volume (GameSettings), SFX and Music
    /// sliders scale the sources here.
    public class SoundManager : MonoBehaviour
    {
        const int PoolSize = 20;
        const float MusicFade = 1.5f;

        public static SoundManager Instance { get; private set; }
        public static SoundLibrary Library => Instance != null ? Instance.library : null;

        SoundLibrary library;
        AudioSource[] pool;
        object[] poolTag; // which clip set each source is playing, for voice limits
        float[] poolBaseVolume;
        int next;
        AudioSource uiSource;
        AudioSource musicSource;
        AudioClip musicTarget;
        float musicFade = 1f; // 0..1, fades out then in when the track changes
        float lastHurt, lastCoin;
        int lastCoins = -1;
        float lastZoneRadius = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("[SoundManager]");
            DontDestroyOnLoad(go);
            go.AddComponent<SoundManager>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            library = Resources.Load<SoundLibrary>(SoundLibrary.ResourcePath);

            pool = new AudioSource[PoolSize];
            poolTag = new object[PoolSize];
            poolBaseVolume = new float[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var src = new GameObject("Sfx" + i).AddComponent<AudioSource>();
                src.transform.SetParent(transform, false);
                src.playOnAwake = false;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.dopplerLevel = 0f;
                pool[i] = src;
            }

            uiSource = gameObject.AddComponent<AudioSource>();
            uiSource.playOnAwake = false;
            uiSource.ignoreListenerPause = true; // menu clicks still sound while the game is paused

            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.loop = true;
            musicSource.ignoreListenerPause = true;

            GameSettings.Changed += ApplyVolumes;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            GameEvents.OnWaveAnnounced += HandleWaveAnnounced;
            GameEvents.OnWaveCompleted += HandleWaveCompleted;
            GameEvents.OnCoinsChanged += HandleCoins;
            GameEvents.OnZoneChanged += HandleZone;
            GameEvents.OnZoneExpandFailed += HandleDenied;
            GameEvents.OnPlayerHealthChanged += HandlePlayerHealth;
            GameEvents.OnPlayerDied += HandlePlayerDied;
            GameEvents.OnGameStateChanged += HandleGameState;
            StoreService.Purchased += HandlePurchased;
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            GameSettings.Changed -= ApplyVolumes;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            GameEvents.OnWaveAnnounced -= HandleWaveAnnounced;
            GameEvents.OnWaveCompleted -= HandleWaveCompleted;
            GameEvents.OnCoinsChanged -= HandleCoins;
            GameEvents.OnZoneChanged -= HandleZone;
            GameEvents.OnZoneExpandFailed -= HandleDenied;
            GameEvents.OnPlayerHealthChanged -= HandlePlayerHealth;
            GameEvents.OnPlayerDied -= HandlePlayerDied;
            GameEvents.OnGameStateChanged -= HandleGameState;
            StoreService.Purchased -= HandlePurchased;
        }

        // =====================================================================
        // Public API
        // =====================================================================

        public static AudioClip Pick(AudioClip[] set)
        {
            if (set == null || set.Length == 0) return null;
            return set[Random.Range(0, set.Length)];
        }

        /// Non-positional sound (the player's own gun, UI, stingers).
        public static void Play(AudioClip clip, float volume = 1f, float pitchJitter = 0.04f)
        {
            if (clip == null || Instance == null) return;
            Instance.Emit(clip, null, Vector3.zero, false, volume, pitchJitter, 0f, 0);
        }

        public static void Play(AudioClip[] set, float volume = 1f, float pitchJitter = 0.04f) => Play(Pick(set), volume, pitchJitter);

        /// 3D sound in the world. maxVoices > 0 limits how many sounds from the same set
        /// may play at once (twenty zombies must not all groan together).
        public static void PlayAt(AudioClip[] set, Vector3 position, float volume = 1f, float maxDistance = 35f, int maxVoices = 0)
        {
            var clip = Pick(set);
            if (clip == null || Instance == null) return;
            Instance.Emit(clip, set, position, true, volume, 0.08f, maxDistance, maxVoices);
        }

        public static void PlayAt(AudioClip clip, Vector3 position, float volume = 1f, float maxDistance = 35f)
        {
            if (clip == null || Instance == null) return;
            Instance.Emit(clip, clip, position, true, volume, 0.06f, maxDistance, 0);
        }

        public static void PlayUI(AudioClip clip, float volume = 0.8f)
        {
            if (clip == null || Instance == null) return;
            Instance.uiSource.PlayOneShot(clip, volume * GameSettings.SfxVolume);
        }

        public static void Click() => PlayUI(Library != null ? Library.uiClick : null, 0.6f);

        // =====================================================================
        // Pool
        // =====================================================================

        void Emit(AudioClip clip, object tag, Vector3 position, bool spatial, float volume, float pitchJitter, float maxDistance, int maxVoices)
        {
            if (maxVoices > 0 && tag != null)
            {
                int playing = 0;
                for (int i = 0; i < pool.Length; i++)
                    if (poolTag[i] == tag && pool[i].isPlaying) playing++;
                if (playing >= maxVoices) return;
            }

            // Prefer a free source; otherwise steal the next one in the ring.
            int index = -1;
            for (int i = 0; i < pool.Length; i++)
            {
                int k = (next + i) % pool.Length;
                if (!pool[k].isPlaying) { index = k; break; }
            }
            if (index < 0) index = next;
            next = (index + 1) % pool.Length;

            var src = pool[index];
            poolTag[index] = tag;
            poolBaseVolume[index] = volume;
            src.clip = clip;
            src.volume = volume * GameSettings.SfxVolume;
            src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            src.spatialBlend = spatial ? 1f : 0f;
            src.minDistance = 2f;
            src.maxDistance = Mathf.Max(5f, maxDistance);
            src.transform.position = position;
            src.Play();
        }

        void ApplyVolumes()
        {
            float sfx = GameSettings.SfxVolume;
            for (int i = 0; i < pool.Length; i++)
                if (pool[i].isPlaying) pool[i].volume = poolBaseVolume[i] * sfx;
        }

        // =====================================================================
        // Music
        // =====================================================================

        void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            lastCoins = -1;
            lastZoneRadius = -1f;
            lastHealth = -1f;
            if (library == null) return;
            // The gameplay scene is the one with a GameManager; everything else is menu.
            bool gameplay = Object.FindAnyObjectByType<GameManager>() != null;
            musicTarget = gameplay ? library.gameMusic : library.menuMusic;
        }

        void Update()
        {
            // Fade out the old track, swap, fade in the new one. Unscaled: keeps working while paused.
            float step = Time.unscaledDeltaTime / MusicFade;
            if (musicSource.clip != musicTarget)
            {
                musicFade = Mathf.MoveTowards(musicFade, 0f, step);
                if (musicFade <= 0f || !musicSource.isPlaying)
                {
                    musicSource.clip = musicTarget;
                    if (musicTarget != null) musicSource.Play(); else musicSource.Stop();
                }
            }
            else musicFade = Mathf.MoveTowards(musicFade, 1f, step);

            float level = library != null ? library.musicLevel : 0.5f;
            bool over = GameManager.Instance != null && GameManager.Instance.State == GameState.GameOver;
            musicSource.volume = musicFade * level * GameSettings.MusicVolume * (over ? 0.35f : 1f);
        }

        // =====================================================================
        // Game events
        // =====================================================================

        void HandleWaveAnnounced(int wave) { if (library != null) PlayUI(library.waveStart, 0.9f); }
        void HandleWaveCompleted(int wave, float breather) { if (library != null) PlayUI(library.waveCleared, 0.9f); }
        void HandleDenied() { if (library != null) PlayUI(library.denied, 0.8f); }
        void HandlePurchased(string id) { if (library != null) PlayUI(library.purchase, 0.9f); }

        void HandleCoins(int total)
        {
            bool gained = lastCoins >= 0 && total > lastCoins;
            lastCoins = total;
            // Several kills in one frame (shotgun) still give a single coin chime.
            if (gained && library != null && Time.unscaledTime - lastCoin > 0.08f)
            {
                lastCoin = Time.unscaledTime;
                Play(library.coin, 0.55f, 0.06f);
            }
        }

        void HandleZone(float radius, float max, int cost)
        {
            if (lastZoneRadius >= 0f && radius > lastZoneRadius + 0.01f && library != null) PlayUI(library.zoneExpand, 0.9f);
            lastZoneRadius = radius;
        }

        float lastHealth = -1f;

        void HandlePlayerHealth(float current, float max)
        {
            bool hurt = lastHealth >= 0f && current < lastHealth && current > 0f;
            lastHealth = current;
            if (hurt && library != null && Time.time - lastHurt > 0.35f)
            {
                lastHurt = Time.time;
                Play(library.playerHurt, 0.85f, 0.06f);
            }
        }

        void HandlePlayerDied()
        {
            lastHealth = -1f;
            if (library != null) Play(library.playerDeath, 1f, 0f);
        }

        void HandleGameState(GameState state)
        {
            if (state == GameState.GameOver && library != null) PlayUI(library.gameOver, 1f);
        }
    }
}
