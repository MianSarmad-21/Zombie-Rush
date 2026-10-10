using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Core;

namespace ZombieRush.UI
{
    /// End-of-run results: "YOU DIED", a 0-5 star rating for the wave reached,
    /// the run's stats (wave, kills, coins earned, best) and Replay / Home.
    /// Shows the auto-restart countdown if GameManager has one.
    public class GameOverUI : MonoBehaviour
    {
        public UIPanel panel;
        public RectTransform titleRoot;
        public Text waveText;
        public Text bestText;
        public Text countdownText;
        public Button restartButton;
        public Button menuButton;

        [Header("Results")]
        public Text killsText;
        public Text coinsText;
        public Image[] stars;
        public Sprite starFull;
        public Sprite starEmpty;
        [Tooltip("Wave needed for each star (ascending).")]
        public int[] starWaves = { 1, 3, 5, 8, 12 };

        float slam, starClock;
        int lastShown = int.MinValue;
        int kills, coinsEarned, earnedStars;

        // Awake/OnDestroy, not OnEnable/OnDisable: this component disables itself when idle.
        void Awake()
        {
            GameEvents.OnPlayerDied += HandlePlayerDied;
            GameEvents.OnZombieKilled += HandleZombieKilled;
            if (restartButton != null) restartButton.onClick.AddListener(Restart);
            if (menuButton != null) menuButton.onClick.AddListener(ToMenu);
            enabled = false;
        }

        void OnDestroy()
        {
            GameEvents.OnPlayerDied -= HandlePlayerDied;
            GameEvents.OnZombieKilled -= HandleZombieKilled;
        }

        static void Restart() { if (GameManager.Instance != null) GameManager.Instance.RestartLevel(); }
        static void ToMenu() { if (GameManager.Instance != null) GameManager.Instance.QuitToMenu(); }

        void HandleZombieKilled(ZombieKilledInfo info)
        {
            kills++;
            coinsEarned += info.Coins;
        }

        void HandlePlayerDied()
        {
            var gm = GameManager.Instance;
            int wave = gm != null ? gm.ReachedWave : 0;

            if (waveText != null) waveText.text = wave.ToString();
            if (killsText != null) killsText.text = kills.ToString();
            if (coinsText != null) coinsText.text = coinsEarned.ToString();
            if (bestText != null)
            {
                bool isNew = gm != null && gm.IsNewBest;
                bestText.text = isNew ? $"{GameSettings.BestWave}  NEW!" : GameSettings.BestWave.ToString();
                bestText.color = isNew ? UITheme.Gold : UITheme.Bone;
            }
            if (countdownText != null) countdownText.text = "";

            earnedStars = 0;
            foreach (int w in starWaves) if (wave >= w) earnedStars++;
            starClock = 0f;
            SetStars(0);

            if (panel != null) panel.Show();
            slam = 1f;
            lastShown = int.MinValue;
            enabled = true;
        }

        void SetStars(int lit)
        {
            if (stars == null) return;
            for (int i = 0; i < stars.Length; i++)
            {
                if (stars[i] == null) continue;
                bool on = i < lit;
                var sprite = on ? starFull : starEmpty;
                if (sprite != null) stars[i].sprite = sprite;
                stars[i].rectTransform.localScale = Vector3.one;
            }
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (slam > 0f)
            {
                slam = Mathf.MoveTowards(slam, 0f, dt * 2.5f);
                if (titleRoot != null) titleRoot.localScale = Vector3.one * (1f + 0.6f * slam * slam);
            }

            // Stars pop in one by one after the title lands.
            if (stars != null && earnedStars > 0)
            {
                starClock += dt;
                for (int i = 0; i < Mathf.Min(earnedStars, stars.Length); i++)
                {
                    if (stars[i] == null) continue;
                    float t = starClock - 0.45f - i * 0.18f;
                    if (t < 0f) continue;
                    if (starFull != null && stars[i].sprite != starFull) stars[i].sprite = starFull;
                    float pop = Mathf.Clamp01(t / 0.22f);
                    stars[i].rectTransform.localScale = Vector3.one * (1f + 0.6f * (1f - pop) * (1f - pop));
                }
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
