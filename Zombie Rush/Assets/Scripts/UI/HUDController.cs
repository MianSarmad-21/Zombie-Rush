using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Core;

namespace ZombieRush.UI
{
    /// Pure presentation: listens to GameEvents only. Text is rebuilt only when a
    /// value changes, and Update() goes to sleep when nothing is animating.
    public class HUDController : MonoBehaviour
    {
        [Header("Health")]
        public Image healthFill;
        public Image healthTrail; // lags behind the fill to show how much was just lost
        public Text healthText;
        public float trailDelay = 0.35f;
        public float trailSpeed = 0.9f;

        [Header("Weapon")]
        public Image weaponIcon;
        public Text weaponNameText;
        public Text magazineText;
        public Text reserveText;
        public GameObject reloadRoot;
        public Image reloadFill;

        [Header("Wave")]
        public Text waveText;
        public Text zombiesLeftText;

        [Header("Zone")]
        public Text zoneText;
        public Image zoneFill;

        [Header("Coins")]
        public Text coinsText;
        public RectTransform coinsPunchTarget;

        float health01 = 1f, trail01 = 1f, trailWait;
        float reloadElapsed, reloadDuration;
        bool reloading;
        float coinPunch;
        int lastCoins = -1;

        void Subscribe()
        {
            GameEvents.OnPlayerHealthChanged += HandleHealthChanged;
            GameEvents.OnAmmoChanged += HandleAmmoChanged;
            GameEvents.OnCoinsChanged += HandleCoinsChanged;
            GameEvents.OnZoneChanged += HandleZoneChanged;
            GameEvents.OnWaveAnnounced += HandleWaveAnnounced;
            GameEvents.OnZombiesRemainingChanged += HandleZombiesRemaining;
            GameEvents.OnWaveCompleted += HandleWaveCompleted;
        }

        void OnDestroy()
        {
            GameEvents.OnPlayerHealthChanged -= HandleHealthChanged;
            GameEvents.OnAmmoChanged -= HandleAmmoChanged;
            GameEvents.OnCoinsChanged -= HandleCoinsChanged;
            GameEvents.OnZoneChanged -= HandleZoneChanged;
            GameEvents.OnWaveAnnounced -= HandleWaveAnnounced;
            GameEvents.OnZombiesRemainingChanged -= HandleZombiesRemaining;
            GameEvents.OnWaveCompleted -= HandleWaveCompleted;
        }

        void Awake()
        {
            Subscribe(); // Awake/OnDestroy, not OnEnable/OnDisable: this component disables itself when idle.
            UITheme.SetBarFill(healthFill, 1f);
            UITheme.SetBarFill(healthTrail, 1f);
            if (reloadRoot != null) reloadRoot.SetActive(false);
            if (waveText != null) waveText.text = "WAVE -";
            if (zombiesLeftText != null) zombiesLeftText.text = "";
        }

        void Update()
        {
            bool busy = false;

            // Health trail: hold briefly, then drain toward the real value.
            if (trail01 > health01)
            {
                if (trailWait > 0f) trailWait -= Time.deltaTime;
                else trail01 = Mathf.MoveTowards(trail01, health01, trailSpeed * Time.deltaTime);
                UITheme.SetBarFill(healthTrail, trail01);
                busy = true;
            }

            if (reloading)
            {
                reloadElapsed += Time.deltaTime;
                UITheme.SetBarFill(reloadFill, reloadDuration > 0f ? reloadElapsed / reloadDuration : 1f);
                busy = true;
            }

            if (coinPunch > 0f)
            {
                coinPunch = Mathf.MoveTowards(coinPunch, 0f, Time.deltaTime * 4f);
                if (coinsPunchTarget != null) coinsPunchTarget.localScale = Vector3.one * (1f + 0.3f * coinPunch * coinPunch);
                busy = true;
            }

            if (!busy) enabled = false;
        }

        void Wake() => enabled = true;

        void HandleHealthChanged(float current, float max)
        {
            float new01 = max > 0f ? current / max : 0f;
            if (new01 < health01) trailWait = trailDelay;
            health01 = new01;
            if (trail01 < health01) { trail01 = health01; UITheme.SetBarFill(healthTrail, trail01); }

            UITheme.SetBarFill(healthFill, health01);
            if (healthFill != null) healthFill.color = health01 <= 0.3f ? Color.Lerp(UITheme.Blood, Color.white, 0.15f) : UITheme.Blood;
            if (healthText != null) healthText.text = $"{Mathf.CeilToInt(current)} / {Mathf.RoundToInt(max)}";
            Wake();
        }

        void HandleAmmoChanged(AmmoInfo info)
        {
            if (weaponNameText != null) weaponNameText.text = info.WeaponName.ToUpperInvariant();
            if (weaponIcon != null)
            {
                weaponIcon.enabled = info.Icon != null;
                if (info.Icon != null && weaponIcon.sprite != info.Icon) weaponIcon.sprite = info.Icon;
            }

            if (magazineText != null)
            {
                magazineText.text = info.Unlimited ? "--" : info.Magazine.ToString();
                bool low = !info.Unlimited && info.Magazine <= Mathf.CeilToInt(info.MagazineSize * 0.25f);
                magazineText.color = info.Magazine == 0 ? UITheme.Muted : (low ? UITheme.Blood : UITheme.Bone);
            }

            if (reserveText != null)
            {
                reserveText.text = info.Unlimited ? "" : $"/ {info.MagazineSize}   {info.Reserve}";
                reserveText.color = info.Reserve == 0 ? UITheme.Blood : UITheme.Muted;
            }

            if (info.IsReloading != reloading)
            {
                reloading = info.IsReloading;
                reloadElapsed = 0f;
                reloadDuration = info.ReloadDuration;
                if (reloadRoot != null) reloadRoot.SetActive(reloading);
                UITheme.SetBarFill(reloadFill, 0f);
                if (reloading) Wake();
            }
        }

        void HandleCoinsChanged(int total)
        {
            if (coinsText != null) coinsText.text = total.ToString();
            if (lastCoins >= 0 && total > lastCoins) { coinPunch = 1f; Wake(); }
            lastCoins = total;
        }

        void HandleZoneChanged(float radius, float max, int cost)
        {
            bool full = radius >= max - 0.01f;
            if (zoneText != null) zoneText.text = full ? $"ZONE  {radius:0}m  ·  MAX" : $"ZONE  {radius:0}m / {max:0}m";
            UITheme.SetBarFill(zoneFill, max > 0f ? radius / max : 1f);
        }

        void HandleWaveAnnounced(int wave)
        {
            if (waveText != null) waveText.text = $"WAVE {wave}";
            if (zombiesLeftText != null) zombiesLeftText.text = "INCOMING";
        }

        void HandleZombiesRemaining(int remaining)
        {
            if (zombiesLeftText != null) zombiesLeftText.text = remaining == 1 ? "1 ZOMBIE LEFT" : $"{remaining} ZOMBIES LEFT";
        }

        void HandleWaveCompleted(int wave, float breather)
        {
            if (zombiesLeftText != null) zombiesLeftText.text = "CLEARED";
        }
    }
}
