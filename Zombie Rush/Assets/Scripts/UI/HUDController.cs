using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Core;

namespace ZombieRush.UI
{
    /// Pure presentation: listens to GameEvents only. Health bar only for now.
    public class HUDController : MonoBehaviour
    {
        [Header("Health")]
        public Image healthFill;
        public Text healthText;
        public float healthSmoothSpeed = 2f;

        float targetHealth01 = 1f;
        float displayedHealth01 = 1f;

        void OnEnable() => GameEvents.OnPlayerHealthChanged += HandleHealthChanged;
        void OnDisable() => GameEvents.OnPlayerHealthChanged -= HandleHealthChanged;

        void Start() => ApplyBarFill(healthFill, displayedHealth01);

        void Update()
        {
            displayedHealth01 = Mathf.MoveTowards(displayedHealth01, targetHealth01, healthSmoothSpeed * Time.deltaTime);
            ApplyBarFill(healthFill, displayedHealth01);
        }

        void HandleHealthChanged(float current, float max)
        {
            if (healthText != null) healthText.text = $"HP: {Mathf.RoundToInt(current)} / {Mathf.RoundToInt(max)}";
            targetHealth01 = max > 0 ? current / max : 0f;
        }

        /// Resizes the bar by moving its RectTransform's right anchor rather than relying
        /// on Image.fillAmount, which wasn't reliably reflecting on screen in the last project.
        static void ApplyBarFill(Image bar, float t01)
        {
            if (bar == null) return;
            t01 = Mathf.Clamp01(t01);
            var rt = bar.rectTransform;
            rt.anchorMin = new Vector2(0f, rt.anchorMin.y);
            rt.anchorMax = new Vector2(t01, rt.anchorMax.y);
        }
    }
}
