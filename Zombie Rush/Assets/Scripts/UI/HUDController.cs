using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Core;

namespace ZombieRush.UI
{
    /// Pure presentation: listens to GameEvents only.
    public class HUDController : MonoBehaviour
    {
        [Header("Health")]
        public Image healthFill;
        public Text healthText;
        public float healthSmoothSpeed = 2f;

        [Header("Weapon")]
        public Text weaponNameText;
        public Text ammoCurrentText;
        public Text ammoMaxText;

        [Header("Zone")]
        public Text zoneText;

        [Header("Coins")]
        public Text coinsText;
        public Text requiredCoinsText;

        float targetHealth01 = 1f;
        float displayedHealth01 = 1f;

        void OnEnable()
        {
            GameEvents.OnPlayerHealthChanged += HandleHealthChanged;
            GameEvents.OnWeaponChanged += HandleWeaponChanged;
            GameEvents.OnAmmoChanged += HandleAmmoChanged;
            GameEvents.OnCoinsChanged += HandleCoinsChanged;
        }

        void OnDisable()
        {
            GameEvents.OnPlayerHealthChanged -= HandleHealthChanged;
            GameEvents.OnWeaponChanged -= HandleWeaponChanged;
            GameEvents.OnAmmoChanged -= HandleAmmoChanged;
            GameEvents.OnCoinsChanged -= HandleCoinsChanged;
        }

        void Start()
        {
            ApplyBarFill(healthFill, displayedHealth01);

            if (ZoneManager.Instance != null)
            {
                ZoneManager.Instance.OnZoneChanged += HandleZoneChanged;
                HandleZoneChanged(ZoneManager.Instance.CurrentRadius, ZoneManager.Instance.maxRadius);
            }
        }

        void OnDestroy()
        {
            if (ZoneManager.Instance != null) ZoneManager.Instance.OnZoneChanged -= HandleZoneChanged;
        }

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

        void HandleWeaponChanged(string weaponName)
        {
            if (weaponNameText != null) weaponNameText.text = weaponName.ToUpperInvariant();
        }

        void HandleAmmoChanged(int magazine, int magazineSize, int reserve, bool isReloading)
        {
            if (ammoCurrentText != null) ammoCurrentText.text = isReloading ? "..." : magazine.ToString();
            if (ammoMaxText != null) ammoMaxText.text = $"/ {magazineSize}   ({reserve})";
        }

        void HandleZoneChanged(float current, float max)
        {
            bool isFull = current >= max;
            int cost = ZoneManager.Instance != null ? ZoneManager.Instance.expandCost : 0;

            if (zoneText != null)
            {
                zoneText.text = isFull ? $"ZONE: {current:0}m (FULL)" : $"ZONE: {current:0}m / {max:0}m";
            }

            if (requiredCoinsText != null)
            {
                requiredCoinsText.text = isFull ? "" : $"EXPAND (P): {cost} COINS";
            }
        }

        void HandleCoinsChanged(int total)
        {
            if (coinsText != null) coinsText.text = $"COINS: {total}";
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
