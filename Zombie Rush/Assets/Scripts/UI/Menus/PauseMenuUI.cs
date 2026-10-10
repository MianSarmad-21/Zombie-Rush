using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Core;

namespace ZombieRush.UI
{
    /// Pause screen: Resume, Restart (wave 1), Options, Quit to Menu.
    public class PauseMenuUI : MonoBehaviour
    {
        public UIPanel panel;
        public OptionsMenuUI options;
        public Button resumeButton;
        public Button restartButton;
        public Button optionsButton;
        public Button menuButton;

        void Awake()
        {
            GameEvents.OnGameStateChanged += HandleState;
            if (resumeButton != null) resumeButton.onClick.AddListener(() => { if (GameManager.Instance != null) GameManager.Instance.Resume(); });
            if (restartButton != null) restartButton.onClick.AddListener(() => { if (GameManager.Instance != null) GameManager.Instance.RestartLevel(); });
            if (menuButton != null) menuButton.onClick.AddListener(() => { if (GameManager.Instance != null) GameManager.Instance.QuitToMenu(); });
            if (optionsButton != null) optionsButton.onClick.AddListener(OpenOptions);
        }

        void OnDestroy() => GameEvents.OnGameStateChanged -= HandleState;

        void HandleState(GameState state)
        {
            if (state == GameState.Paused)
            {
                if (panel != null) panel.Show();
                return;
            }

            if (options != null) options.CloseSilently();
            if (panel != null) panel.Hide();
        }

        void OpenOptions()
        {
            if (options == null) return;
            if (panel != null) panel.Hide();
            options.Open(() =>
            {
                if (GameManager.Instance != null && GameManager.Instance.State == GameState.Paused && panel != null) panel.Show();
            });
        }
    }
}
