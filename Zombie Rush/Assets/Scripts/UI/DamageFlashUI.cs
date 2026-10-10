using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Core;

namespace ZombieRush.UI
{
    /// Full-screen red flash whenever the player's health drops, plus a pulsing
    /// blood vignette while health is low. Both Images are switched off at zero
    /// alpha so they cost no fill-rate most of the time.
    public class DamageFlashUI : MonoBehaviour
    {
        public Image flashImage;
        public Image lowHealthVignette;
        public float maxAlpha = 0.4f;
        public float fadeSpeed = 2f;
        [Range(0f, 1f)] public float lowHealthThreshold = 0.3f;
        public float pulseSpeed = 4f;

        float lastHealth = -1f;
        float flashAlpha;
        float lowHealthAmount; // 0 = healthy, 1 = nearly dead
        float pulseTime;

        void Awake()
        {
            // Subscribed for the object's whole life: this component disables itself when idle.
            GameEvents.OnPlayerHealthChanged += HandleHealthChanged;
            UITheme.SetAlpha(flashImage, 0f);
            UITheme.SetAlpha(lowHealthVignette, 0f);
        }

        void OnDestroy() => GameEvents.OnPlayerHealthChanged -= HandleHealthChanged;

        void HandleHealthChanged(float current, float max)
        {
            if (lastHealth >= 0f && current < lastHealth) flashAlpha = maxAlpha;
            lastHealth = current;

            float h01 = max > 0f ? current / max : 0f;
            lowHealthAmount = h01 < lowHealthThreshold && current > 0f ? 1f - h01 / lowHealthThreshold : 0f;
            enabled = true;
        }

        void Update()
        {
            flashAlpha = Mathf.MoveTowards(flashAlpha, 0f, fadeSpeed * Time.deltaTime);
            UITheme.SetAlpha(flashImage, flashAlpha);

            float vignette = 0f;
            if (lowHealthAmount > 0f)
            {
                pulseTime += Time.deltaTime * pulseSpeed;
                vignette = Mathf.Lerp(0.35f, 0.85f, lowHealthAmount) * (0.75f + 0.25f * Mathf.Sin(pulseTime));
            }
            UITheme.SetAlpha(lowHealthVignette, vignette);

            if (flashAlpha <= 0f && lowHealthAmount <= 0f) enabled = false;
        }
    }
}
