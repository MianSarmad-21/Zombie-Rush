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
    /// Gameplay music switches to the combat track while a wave is on and back between waves.
    /// An ambience bed and a low-health heartbeat loop under the SFX slider; the local
    /// player gets PlayerFootsteps when a level loads.
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
        AudioSource ambienceSource;
        AudioSource heartbeatSource;
        AudioClip musicTarget;
        bool gameplay, combat, playerDown;
        float healthShare = 1f;
        float ambienceFade, heartbeatLevel;
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

            ambienceSource = gameObject.AddComponent<AudioSource>();
            ambienceSource.playOnAwake = false;
            ambienceSource.loop = true;

            heartbeatSource = gameObject.AddComponent<AudioSource>();
            heartbeatSource.playOnAwake = false;
            heartbeatSource.loop = true;

            GameSettings.Changed += ApplyVolumes;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            GameEvents.OnWaveAnnounced += HandleWaveAnnounced;
            GameEvents.OnWaveStarted += HandleWaveStarted;
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
            GameEvents.OnWaveStarted -= HandleWaveStarted;
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

        /// 3D sound at a set pitch (the chest's lock ticks rise as it is forced). Returns nothing useful; kept for symmetry.
        public static AudioSource PlayAtPitched(AudioClip clip, Vector3 position, float volume, float pitch, float maxDistance = 35f)
        {
            if (clip == null || Instance == null) return null;
            Instance.Emit(clip, clip, position, true, volume, 0.03f, maxDistance, 0);
            var src = Instance.pool[(Instance.next + Instance.pool.Length - 1) % Instance.pool.Length];
            src.pitch = pitch * src.pitch;
            return src;
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
            combat = false;
            playerDown = false;
            healthShare = 1f;
            // The gameplay scene is the one with a GameManager; everything else is menu.
            gameplay = Object.FindAnyObjectByType<GameManager>() != null;
            if (gameplay) AttachFootsteps();
        }

        /// Footsteps on the player this machine controls (a teammate's copy has PlayerController off).
        static void AttachFootsteps()
        {
            foreach (var pc in Object.FindObjectsByType<ZombieRush.Player.PlayerController>(FindObjectsInactive.Exclude))
                if (pc.enabled && !pc.TryGetComponent(out PlayerFootsteps _)) pc.gameObject.AddComponent<PlayerFootsteps>();
        }

        AudioClip MusicFor()
        {
            if (library == null) return null;
            if (!gameplay) return library.menuMusic;
            return combat && library.combatMusic != null ? library.combatMusic : library.gameMusic;
        }

        void Update()
        {
            // Fade out the old track, swap, fade in the new one. Unscaled: keeps working while paused.
            float step = Time.unscaledDeltaTime / MusicFade;
            musicTarget = MusicFor();
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

            UpdateAmbience(step);
            UpdateHeartbeat();
        }

        void UpdateAmbience(float step)
        {
            var clip = gameplay && library != null ? library.ambience : null;
            if (ambienceSource.clip != clip)
            {
                ambienceFade = Mathf.MoveTowards(ambienceFade, 0f, step);
                if (ambienceFade <= 0f || !ambienceSource.isPlaying)
                {
                    ambienceSource.clip = clip;
                    if (clip != null) ambienceSource.Play(); else ambienceSource.Stop();
                }
            }
            else ambienceFade = Mathf.MoveTowards(ambienceFade, 1f, step);
            float level = library != null ? library.ambienceLevel : 0f;
            ambienceSource.volume = ambienceFade * level * GameSettings.SfxVolume;
        }

        /// Fades in below the low-health line, louder and a little faster the lower health gets.
        void UpdateHeartbeat()
        {
            var clip = library != null ? library.heartbeat : null;
            float below = library != null ? Mathf.Max(0.01f, library.heartbeatBelow) : 0.3f;
            float target = gameplay && !playerDown && clip != null && healthShare < below ? 1f - healthShare / below : 0f;
            heartbeatLevel = Mathf.MoveTowards(heartbeatLevel, target, Time.unscaledDeltaTime * 1.5f);
            if (heartbeatLevel <= 0f)
            {
                if (heartbeatSource.isPlaying) heartbeatSource.Stop();
                return;
            }
            if (heartbeatSource.clip != clip) heartbeatSource.clip = clip;
            if (!heartbeatSource.isPlaying) heartbeatSource.Play();
            heartbeatSource.volume = Mathf.Lerp(0.35f, 1f, heartbeatLevel) * library.heartbeatVolume * GameSettings.SfxVolume;
            heartbeatSource.pitch = 1f + 0.2f * heartbeatLevel;
        }

        // =====================================================================
        // Game events
        // =====================================================================

        void HandleWaveAnnounced(int wave) { if (library != null) PlayUI(library.waveStart, 0.9f); }
        void HandleWaveStarted(int wave) => combat = true;
        void HandleWaveCompleted(int wave, float breather) { combat = false; if (library != null) PlayUI(library.waveCleared, 0.9f); }
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
            healthShare = max > 0f ? Mathf.Clamp01(current / max) : 1f;
            playerDown = current <= 0f;
            if (hurt && library != null && Time.time - lastHurt > 0.35f)
            {
                lastHurt = Time.time;
                Play(library.playerHurt, 0.85f, 0.06f);
            }
        }

        void HandlePlayerDied()
        {
            lastHealth = -1f;
            playerDown = true;
            if (library != null) Play(library.playerDeath, 1f, 0f);
        }

        void HandleGameState(GameState state)
        {
            if (state == GameState.GameOver) combat = false;
            if (state == GameState.GameOver && library != null) PlayUI(library.gameOver, 1f);
        }
    }
}
