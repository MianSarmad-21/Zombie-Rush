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
        [Tooltip("Optional backing behind the subtitle: when set, it takes the subtitle colour instead of the text.")]
        public Graphic subtitleBack;
        public Color incomingColor = UITheme.Blood;
        public Color noticeColor = UITheme.Cyan;
        public Color clearedColor = UITheme.Cyan;
        public float incomingHold = 2.6f;
        public float noticeHold = 2.4f;
        public float fadeSpeed = 5f;
        [Tooltip("Simple HUD (user, 2026-11-17): no big banner - wave news and notices go to the side feed (PickupToast) as one line each.")]
        public bool simple = true;

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
            if (simple) { PickupToast.Notice($"WAVE {wave}", "INCOMING", incomingColor); return; }
            Show($"WAVE {wave}", "INCOMING", incomingColor, incomingHold);
            showingWave = true;
            countdown = 0f;
            SetDetail("");
        }

        /// Short messages (safe base, pickups). Never covers a wave banner that is still showing.
        void HandleNotice(string title, string subtitle)
        {
            if (simple) { PickupToast.Notice(title, subtitle, noticeColor); return; }
            if (enabled && holdTimer > 0f && showingWave) return;
            Show(title, subtitle, noticeColor, noticeHold);
            showingWave = false;
            SetDetail("");
        }

        void HandleWaveCompleted(int wave, float breather)
        {
            if (simple) { PickupToast.Notice($"WAVE {wave} CLEARED", breather >= 1f ? $"NEXT WAVE IN {Mathf.RoundToInt(breather)}s" : "", clearedColor); return; }
            Show($"WAVE {wave}", "CLEARED", clearedColor, Mathf.Max(0.5f, breather - 0.25f));
            showingWave = true;
            countdown = breather;
            lastCountdownShown = -1;
        }

        void Show(string title, string subtitle, Color subtitleColor, float hold)
        {
            if (titleText != null) titleText.text = title;
            if (subtitleText != null)
            {
                subtitleText.text = subtitle;
                // with a backing (an ink stamp) the colour goes on the backing, the words stay readable on it
                if (subtitleBack != null) { var c = subtitleColor; c.a = subtitleBack.color.a; subtitleBack.color = c; }
                else subtitleText.color = subtitleColor;
            }
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
