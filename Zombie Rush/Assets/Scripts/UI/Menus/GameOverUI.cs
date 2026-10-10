using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Core;

namespace ZombieRush.UI
{
    /// "YOU DIED" screen with the wave reached, the best-wave record and the
    /// auto-restart countdown. Restart / Main Menu buttons skip the wait.
    public class GameOverUI : MonoBehaviour
    {
        public UIPanel panel;
        public RectTransform titleRoot;
        public Text waveText;
        public Text bestText;
        public Text countdownText;
        public Button restartButton;
        public Button menuButton;

        float slam;
        int lastShown = int.MinValue;

        // Awake/OnDestroy, not OnEnable/OnDisable: this component disables itself when idle.
        void Awake()
        {
            GameEvents.OnPlayerDied += HandlePlayerDied;
            if (restartButton != null) restartButton.onClick.AddListener(Restart);
            if (menuButton != null) menuButton.onClick.AddListener(ToMenu);
            enabled = false;
        }

        void OnDestroy() => GameEvents.OnPlayerDied -= HandlePlayerDied;

        static void Restart() { if (GameManager.Instance != null) GameManager.Instance.RestartLevel(); }
        static void ToMenu() { if (GameManager.Instance != null) GameManager.Instance.QuitToMenu(); }

        void HandlePlayerDied()
        {
            var gm = GameManager.Instance;
            int wave = gm != null ? gm.ReachedWave : 0;

            if (waveText != null) waveText.text = wave > 0 ? $"YOU SURVIVED TO WAVE {wave}" : "YOU DIDN'T MAKE IT";
            if (bestText != null)
            {
                bool isNew = gm != null && gm.IsNewBest;
                bestText.text = isNew ? "NEW BEST!" : $"BEST  ·  WAVE {GameSettings.BestWave}";
                bestText.color = isNew ? UITheme.Gold : UITheme.Muted;
            }
            if (countdownText != null) countdownText.text = "";

            if (panel != null) panel.Show();
            slam = 1f;
            lastShown = int.MinValue;
            enabled = true;
        }

        void Update()
        {
            if (slam > 0f)
            {
                slam = Mathf.MoveTowards(slam, 0f, Time.unscaledDeltaTime * 2.5f);
                if (titleRoot != null) titleRoot.localScale = Vector3.one * (1f + 0.6f * slam * slam);
            }

            var gm = GameManager.Instance;
            float remaining = gm != null ? gm.RestartCountdown : -1f;
            int shown = remaining < 0f ? -1 : Mathf.CeilToInt(remaining);
            if (shown != lastShown && countdownText != null)
            {
                lastShown = shown;
                countdownText.text = shown < 0 ? (GameSettings.UseTouchControls ? "" : "PRESS R TO RESTART") : $"RESTARTING IN {shown}";
            }
        }
    }
}
