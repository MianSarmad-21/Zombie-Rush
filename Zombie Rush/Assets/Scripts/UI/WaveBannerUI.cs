using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Core;

namespace ZombieRush.UI
{
    /// "WAVE 3 / INCOMING" slam-in banner before a wave, and "WAVE 3 / CLEARED"
    /// with a next-wave countdown during the breather. Runs on scaled time so it
    /// freezes with the pause menu.
    public class WaveBannerUI : MonoBehaviour
    {
        public CanvasGroup group;
        public RectTransform animRoot;
        public Text titleText;
        public Text subtitleText;
        public Text detailText;
        public float incomingHold = 2.6f;
        public float noticeHold = 2.4f;
        public float fadeSpeed = 5f;

        float holdTimer;
        float countdown;
        int lastCountdownShown = -1;
        float slam; // 1 -> 0 intro animation
        bool showingWave;

        void Awake()
        {
            Subscribe(); // Awake/OnDestroy, not OnEnable/OnDisable: this component disables itself when idle.
            if (group != null) group.alpha = 0f;
            enabled = false;
        }

        void Subscribe()
        {
            GameEvents.OnWaveAnnounced += HandleWaveAnnounced;
            GameEvents.OnWaveCompleted += HandleWaveCompleted;
            GameEvents.OnNotice += HandleNotice;
        }

        void OnDestroy()
        {
            GameEvents.OnWaveAnnounced -= HandleWaveAnnounced;
            GameEvents.OnWaveCompleted -= HandleWaveCompleted;
            GameEvents.OnNotice -= HandleNotice;
        }

        void HandleWaveAnnounced(int wave)
        {
            Show($"WAVE {wave}", "INCOMING", UITheme.Blood, incomingHold);
            showingWave = true;
            countdown = 0f;
            SetDetail("");
        }

        /// Short messages (safe base, pickups). Never covers a wave banner that is still showing.
        void HandleNotice(string title, string subtitle)
        {
            if (enabled && holdTimer > 0f && showingWave) return;
            Show(title, subtitle, UITheme.Cyan, noticeHold);
            showingWave = false;
            SetDetail("");
        }

        void HandleWaveCompleted(int wave, float breather)
        {
            Show($"WAVE {wave}", "CLEARED", UITheme.Cyan, Mathf.Max(0.5f, breather - 0.25f));
            showingWave = true;
            countdown = breather;
            lastCountdownShown = -1;
        }

        void Show(string title, string subtitle, Color subtitleColor, float hold)
        {
            if (titleText != null) titleText.text = title;
            if (subtitleText != null) { subtitleText.text = subtitle; subtitleText.color = subtitleColor; }
            holdTimer = hold;
            slam = 1f;
            enabled = true;
        }

        void SetDetail(string text)
        {
            if (detailText != null) detailText.text = text;
        }

        void Update()
        {
            float dt = Time.deltaTime;

            if (countdown > 0f)
            {
                countdown -= dt;
                int shown = Mathf.CeilToInt(countdown);
                if (shown != lastCountdownShown)
                {
                    lastCountdownShown = shown;
                    SetDetail(shown > 0 ? $"NEXT WAVE IN {shown}" : "");
                }
            }

            if (slam > 0f)
            {
                slam = Mathf.MoveTowards(slam, 0f, dt * 3.5f);
                float k = slam * slam;
                if (animRoot != null) animRoot.localScale = Vector3.one * (1f + 0.35f * k);
            }

            holdTimer -= dt;
            float target = holdTimer > 0f ? 1f : 0f;
            if (group != null) group.alpha = Mathf.MoveTowards(group.alpha, target, fadeSpeed * dt);

            if (holdTimer <= 0f && (group == null || group.alpha <= 0f) && countdown <= 0f) enabled = false;
        }
    }
}
