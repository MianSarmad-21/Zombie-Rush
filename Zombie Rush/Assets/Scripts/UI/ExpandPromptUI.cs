using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Core;

namespace ZombieRush.UI
{
    public enum SchemeFilter { Any, KeyboardMouseOnly, TouchOnly }

    /// "Expand zone" prompt: gold when affordable, grey when not, pulsing during
    /// the breather between waves, flashing red when a purchase fails. Hidden
    /// once the zone is at its maximum. Used for both the PC key hint and the
    /// touch button (each with its own text format and scheme filter).
    ///
    /// Keep this component on an always-active object; it shows/hides `content`.
    public class ExpandPromptUI : MonoBehaviour
    {
        public GameObject content;
        public Text label;
        public Graphic frame;
        public SchemeFilter showOn = SchemeFilter.Any;
        [Tooltip("{0} = cost in coins")]
        public string format = "[P]  EXPAND ZONE  ·  {0} COINS";

        bool canExpand;
        int cost, coins;
        bool breather;
        float failFlash;
        float pulseTime;

        // Awake/OnDestroy, not OnEnable/OnDisable: this component disables itself when idle.
        void Awake()
        {
            GameEvents.OnZoneChanged += HandleZone;
            GameEvents.OnCoinsChanged += HandleCoins;
            GameEvents.OnWaveCompleted += HandleWaveCompleted;
            GameEvents.OnWaveAnnounced += HandleWaveAnnounced;
            GameEvents.OnZoneExpandFailed += HandleFailed;
            GameSettings.Changed += Refresh;
            Refresh();
        }

        void OnDestroy()
        {
            GameEvents.OnZoneChanged -= HandleZone;
            GameEvents.OnCoinsChanged -= HandleCoins;
            GameEvents.OnWaveCompleted -= HandleWaveCompleted;
            GameEvents.OnWaveAnnounced -= HandleWaveAnnounced;
            GameEvents.OnZoneExpandFailed -= HandleFailed;
            GameSettings.Changed -= Refresh;
        }

        void HandleZone(float radius, float max, int expandCost)
        {
            canExpand = radius < max - 0.01f;
            cost = expandCost;
            Refresh();
        }

        void HandleCoins(int total) { coins = total; Refresh(); }
        void HandleWaveCompleted(int wave, float b) { breather = true; Refresh(); }
        void HandleWaveAnnounced(int wave) { breather = false; Refresh(); }
        void HandleFailed() { failFlash = 1f; Refresh(); }

        bool Affordable => coins >= cost;
        bool Pulsing => breather && Affordable;

        bool SchemeMatches
        {
            get
            {
                bool touch = GameSettings.UseTouchControls;
                return showOn == SchemeFilter.Any || (showOn == SchemeFilter.TouchOnly) == touch;
            }
        }

        void Refresh()
        {
            bool visible = canExpand && SchemeMatches;
            if (content != null && content.activeSelf != visible) content.SetActive(visible);
            if (!visible) { enabled = false; return; }

            if (label != null) label.text = string.Format(format, cost);
            ApplyColor();
            enabled = Pulsing || failFlash > 0f;
            if (!enabled && content != null) content.transform.localScale = Vector3.one;
        }

        void ApplyColor()
        {
            Color baseColor = Affordable ? UITheme.Gold : UITheme.Muted;
            Color c = Color.Lerp(baseColor, UITheme.Blood, failFlash);
            if (label != null) label.color = c;
            if (frame != null) frame.color = c;
        }

        void Update()
        {
            if (failFlash > 0f) failFlash = Mathf.MoveTowards(failFlash, 0f, Time.unscaledDeltaTime * 2.5f);
            ApplyColor();

            float scale = 1f;
            if (Pulsing)
            {
                pulseTime += Time.unscaledDeltaTime;
                scale = 1f + 0.06f * (0.5f + 0.5f * Mathf.Sin(pulseTime * 6f));
            }
            if (failFlash > 0f) scale += Mathf.Sin(failFlash * 40f) * 0.03f * failFlash;
            if (content != null) content.transform.localScale = Vector3.one * scale;

            if (failFlash <= 0f && !Pulsing)
            {
                if (content != null) content.transform.localScale = Vector3.one;
                enabled = false;
            }
        }
    }
}
