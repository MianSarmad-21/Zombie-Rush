using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Core;

namespace ZombieRush.UI
{
    /// Full-screen red flash whenever the player's health drops, fading back
    /// to clear on its own. Pure GameEvents listener - no PlayerHealth reference needed.
    public class DamageFlashUI : MonoBehaviour
    {
        public Image flashImage;
        public float maxAlpha = 0.45f;
        public float fadeSpeed = 2f;

        float lastHealth = -1f;
        float currentAlpha;

        void Awake()
        {
            if (flashImage != null)
            {
                var c = flashImage.color;
                c.a = 0f;
                flashImage.color = c;
            }
        }

        void OnEnable() => GameEvents.OnPlayerHealthChanged += HandleHealthChanged;
        void OnDisable() => GameEvents.OnPlayerHealthChanged -= HandleHealthChanged;

        void Update()
        {
            if (flashImage == null || currentAlpha <= 0f) return;

            currentAlpha = Mathf.MoveTowards(currentAlpha, 0f, fadeSpeed * Time.deltaTime);
            var c = flashImage.color;
            c.a = currentAlpha;
            flashImage.color = c;
        }

        void HandleHealthChanged(float current, float max)
        {
            if (lastHealth >= 0f && current < lastHealth) currentAlpha = maxAlpha;
            lastHealth = current;
        }
    }
}
