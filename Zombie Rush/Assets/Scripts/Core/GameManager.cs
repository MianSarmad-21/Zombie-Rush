using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZombieRush.Core
{
    /// Lobby: before a match starts (choosing Solo / Host / Join); nothing moves or spawns.
    public enum GameState { Playing, Paused, GameOver, Lobby }

    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public GameState State { get; private set; } = GameState.Playing;

        /// Seconds to show the "YOU DIED" screen before the level reloads and
        /// wave progress resets back to Wave 1. 0 or less = wait for the player.
        public float autoRestartDelay = 2.5f;

        [Tooltip("Scene loaded by 'Quit to Menu'. Falls back to restarting the level if it isn't in Build Settings.")]
        public string mainMenuScene = "MainMenu";

        /// Highest wave announced this run (for the Game Over screen).
        public int ReachedWave { get; private set; }
        public bool IsNewBest { get; private set; }

        /// Seconds until the auto-restart fires, or -1 when none is pending.
        public float RestartCountdown => restartAt < 0f ? -1f : Mathf.Max(0f, restartAt - Time.unscaledTime);

        float restartAt = -1f;
        int bestWaveAtStart;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            bestWaveAtStart = GameSettings.BestWave;
        }

        /// Start in the lobby screen (multiplayer menu) instead of straight into play.
        public static bool StartInLobby = true;

        void Start() => SetState(StartInLobby ? GameState.Lobby : GameState.Playing);

        /// The lobby screen started a match (solo or with a squad).
        public void BeginMatch()
        {
            StartInLobby = false;   // a restart of this level goes straight into play
            if (State == GameState.Lobby) SetState(GameState.Playing);
        }

        /// Pausing freezes time only when playing alone; in a squad the others keep playing.
        public static System.Func<bool> CanFreezeTime = () => true;

        void OnEnable()
        {
            GameEvents.OnPlayerDied += HandlePlayerDied;
            GameEvents.OnWaveAnnounced += HandleWaveAnnounced;
            GameSettings.Changed += ApplyCursor;
        }

        void OnDisable()
        {
            GameEvents.OnPlayerDied -= HandlePlayerDied;
            GameEvents.OnWaveAnnounced -= HandleWaveAnnounced;
            GameSettings.Changed -= ApplyCursor;
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        void HandleWaveAnnounced(int wave)
        {
            ReachedWave = wave;
            GameSettings.BestWave = wave;
        }

        void HandlePlayerDied()
        {
            if (ZombieRush.Net.NetSession.InSquad)
            {
                // in a squad a fallen player waits for the next wave; the match ends when everyone is down
                GameEvents.RaiseNotice("YOU ARE DOWN", "BACK IN THE NEXT WAVE");
                return;
            }
            EndMatch();
        }

        /// The match is over (solo death, or the host says the whole squad is down).
        public void EndMatch()
        {
            if (State == GameState.GameOver) return;
            IsNewBest = ReachedWave > bestWaveAtStart;
            GameSettings.Save();
            SetState(GameState.GameOver);
            if (autoRestartDelay > 0f) StartCoroutine(AutoRestartRoutine());
        }

        IEnumerator AutoRestartRoutine()
        {
            restartAt = Time.unscaledTime + autoRestartDelay;
            yield return new WaitForSecondsRealtime(autoRestartDelay);
            // in a squad only the host restarts (everyone reloads with it)
            if (!ZombieRush.Net.NetSession.InSquad || ZombieRush.Net.NetSession.IsServer) RestartLevel();
        }

        void SetState(GameState newState)
        {
            State = newState;

            // Paused really freezes the game: wave timers, spawning and rising zombies all stop.
            bool freeze = newState == GameState.Paused && CanFreezeTime();
            Time.timeScale = freeze ? 0f : 1f;
            AudioListener.pause = freeze;

            GameInput.ClearQueued();
            ApplyCursor();
            GameEvents.RaiseGameStateChanged(newState);
        }

        void ApplyCursor()
        {
            bool locked = State == GameState.Playing && !GameSettings.UseTouchControls;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        public void TogglePause()
        {
            if (State == GameState.Playing) SetState(GameState.Paused);
            else if (State == GameState.Paused) SetState(GameState.Playing);
        }

        public void Resume()
        {
            if (State == GameState.Paused) SetState(GameState.Playing);
        }

        public void RestartLevel()
        {
            Time.timeScale = 1f;
            if (ZombieRush.Net.NetSession.InSquad)
            {
                // the host reloads the level for the whole squad; a joined player waits for the host
                if (ZombieRush.Net.NetSession.IsServer) ZombieRush.Net.NetSession.ReloadLevelForAll();
                else GameEvents.RaiseNotice("WAITING FOR THE HOST", "THE HOST RESTARTS THE MATCH");
                return;
            }
            ZombieRush.Net.NetSession.Shutdown();   // solo: a fresh local match after the reload
            StartInLobby = false;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public void QuitToMenu()
        {
            GameSettings.Save();
            // leave the match and go back to the first screen (Solo / Host / Join)
            ZombieRush.Net.NetSession.Leave();
            StartInLobby = true;
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            return;
            if (string.IsNullOrEmpty(mainMenuScene) || !Application.CanStreamedLevelBeLoaded(mainMenuScene))
            {
                RestartLevel();
                return;
            }
            Time.timeScale = 1f;
            SceneManager.LoadScene(mainMenuScene);
        }

        // Phones: going to the home screen / taking a call pauses the run.
        void OnApplicationPause(bool paused)
        {
            if (paused && State == GameState.Playing) SetState(GameState.Paused);
        }

        void Update()
        {
            bool pausePressed = GameInput.ConsumePause();

            if (pausePressed && (State == GameState.Playing || State == GameState.Paused))
                TogglePause();

            if (State == GameState.GameOver && Input.GetKeyDown(KeyCode.R))
                RestartLevel();
        }
    }
}
