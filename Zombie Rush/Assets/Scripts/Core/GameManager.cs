using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZombieRush.Core
{
    public enum GameState { Playing, Paused, GameOver }

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

        void Start() => SetState(GameState.Playing);

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
            IsNewBest = ReachedWave > bestWaveAtStart;
            GameSettings.Save();
            SetState(GameState.GameOver);
            if (autoRestartDelay > 0f) StartCoroutine(AutoRestartRoutine());
        }

        IEnumerator AutoRestartRoutine()
        {
            restartAt = Time.unscaledTime + autoRestartDelay;
            yield return new WaitForSecondsRealtime(autoRestartDelay);
            RestartLevel();
        }

        void SetState(GameState newState)
        {
            State = newState;

            // Paused really freezes the game: wave timers, spawning and rising zombies all stop.
            Time.timeScale = newState == GameState.Paused ? 0f : 1f;
            AudioListener.pause = newState == GameState.Paused;

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
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public void QuitToMenu()
        {
            GameSettings.Save();
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
