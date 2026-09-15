using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZombieRush.Core
{
    public enum GameState { Playing, Paused, GameOver }

    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public GameState State { get; private set; } = GameState.Playing;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnEnable() => GameEvents.OnPlayerDied += HandlePlayerDied;
        void OnDisable() => GameEvents.OnPlayerDied -= HandlePlayerDied;

        void HandlePlayerDied() => SetState(GameState.GameOver);

        void SetState(GameState newState)
        {
            State = newState;
            bool locked = newState == GameState.Playing;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        public void TogglePause()
        {
            if (State == GameState.Playing) SetState(GameState.Paused);
            else if (State == GameState.Paused) SetState(GameState.Playing);
        }

        public void RestartLevel()
        {
            SetState(GameState.Playing);
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && (State == GameState.Playing || State == GameState.Paused))
                TogglePause();

            if (State == GameState.GameOver && Input.GetKeyDown(KeyCode.R))
                RestartLevel();
        }
    }
}
