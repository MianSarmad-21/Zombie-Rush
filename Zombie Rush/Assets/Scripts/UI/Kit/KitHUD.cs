using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Core;

namespace ZombieRush.UI.Kit
{
    /// The Survivor's Kit HUD: reads the game events and drives the gear on screen.
    ///   vitals dog tag : heartbeat monitor, health number, armour strip;
    ///   pocket chips   : scrap (coins) and kills;
    ///   wave plate     : stencil wave number, slapped on each new wave;
    ///   tape strip     : tally marks + how many zombies are left / INCOMING / CLEARED;
    ///   radar          : the safe zone's size;
    ///   weapon plate   : gun icon and name, magazine as cartridges, rounds left, reserve, reload bar.
    public class KitHUD : MonoBehaviour
    {
        [Header("Vitals")]
        public HeartbeatGraphic heartbeat;
        public Text healthText;
        public Text healthMaxText;
        public Image healthBar;
        public Image armorBar;
        public GameObject armorRoot;

        [Header("Pockets")]
        public Text scrapText;
        public RectTransform scrapPunch;
        public Text killsText;
        public RectTransform killsPunch;

        [Header("Wave")]
        public Text waveText;
        public SlapIn waveSlap;
        public Text leftText;
        public TallyGraphic tally;
        public SlapIn tapeSlap;

        [Header("Zone")]
        public RadarGraphic radar;
        public Text zoneText;

        [Header("Weapon")]
        public Image weaponIcon;
        public Text weaponName;
        public Text magazineText;
        public Text reserveText;
        public MagazineView magazine;
        public GameObject reloadRoot;
        public Image reloadBar;

        int kills, lastScrap = -1;
        float health01 = 1f;
        float scrapPunchT, killsPunchT;
        bool reloading; float reloadStart, reloadDuration;

        void Awake()
        {
            GameEvents.OnPlayerHealthChanged += OnHealth;
            GameEvents.OnPlayerArmorChanged += OnArmor;
            GameEvents.OnPlayerDied += OnDied;
            GameEvents.OnAmmoChanged += OnAmmo;
            GameEvents.OnCoinsChanged += OnScrap;
            GameEvents.OnZombieKilled += OnKill;
            GameEvents.OnZoneChanged += OnZone;
            GameEvents.OnWaveAnnounced += OnWaveAnnounced;
            GameEvents.OnZombiesRemainingChanged += OnRemaining;
            GameEvents.OnWaveCompleted += OnWaveCompleted;
            if (waveText != null) waveText.text = "";
            if (leftText != null) leftText.text = "GEAR UP";
            if (tally != null) tally.SetCount(0);
            if (killsText != null) killsText.text = "0";
            if (reloadRoot != null) reloadRoot.SetActive(false);
            if (armorRoot != null) armorRoot.SetActive(false);
            SetBar(healthBar, 1f);
            if (heartbeat != null) heartbeat.SetHealth(1f, false);
        }

        void OnDestroy()
        {
            GameEvents.OnPlayerHealthChanged -= OnHealth;
            GameEvents.OnPlayerArmorChanged -= OnArmor;
            GameEvents.OnPlayerDied -= OnDied;
            GameEvents.OnAmmoChanged -= OnAmmo;
            GameEvents.OnCoinsChanged -= OnScrap;
            GameEvents.OnZombieKilled -= OnKill;
            GameEvents.OnZoneChanged -= OnZone;
            GameEvents.OnWaveAnnounced -= OnWaveAnnounced;
            GameEvents.OnZombiesRemainingChanged -= OnRemaining;
            GameEvents.OnWaveCompleted -= OnWaveCompleted;
        }

        static void SetBar(Image bar, float v)
        {
            if (bar == null) return;
            bar.fillAmount = Mathf.Clamp01(v);
        }

        void OnHealth(float current, float max)
        {
            health01 = max > 0f ? current / max : 0f;
            var s = KitStyle.Current;
            if (healthText != null)
            {
                healthText.text = Mathf.CeilToInt(Mathf.Max(0f, current)).ToString();
                healthText.color = health01 <= s.lowHealth ? s.blood : s.bone;
            }
            if (healthMaxText != null) healthMaxText.text = "/" + Mathf.RoundToInt(max);
            SetBar(healthBar, health01);
            if (healthBar != null) healthBar.color = health01 <= s.lowHealth ? s.blood : health01 <= s.hurtHealth ? s.amber : s.bone;
            if (heartbeat != null) heartbeat.SetHealth(health01, current <= 0f);
        }

        void OnDied() { if (heartbeat != null) heartbeat.SetHealth(0f, true); }

        void OnArmor(float current, float max)
        {
            if (armorRoot != null) armorRoot.SetActive(current > 0f);
            SetBar(armorBar, max > 0f ? current / max : 0f);
        }

        void OnScrap(int total)
        {
            if (scrapText != null) scrapText.text = total.ToString();
            if (lastScrap >= 0 && total > lastScrap) scrapPunchT = 1f;
            lastScrap = total;
        }

        void OnKill(ZombieKilledInfo info)
        {
            kills++;
            if (killsText != null) killsText.text = kills.ToString();
            killsPunchT = 1f;
        }

        void OnZone(float radius, float max, int cost)
        {
            if (radar != null) radar.SetZone(max > 0f ? radius / max : 1f);
            if (zoneText != null) zoneText.text = radius >= max - 0.01f ? $"{radius:0}M MAX" : $"{radius:0}M";
        }

        void OnWaveAnnounced(int wave)
        {
            if (waveText != null) waveText.text = wave.ToString();
            if (waveSlap != null) waveSlap.Play();
            if (leftText != null) leftText.text = "INCOMING";
            if (tally != null) tally.SetCount(0);
            if (tapeSlap != null) tapeSlap.Play();
        }

        void OnRemaining(int n)
        {
            if (leftText != null) leftText.text = n == 1 ? "1 LEFT" : $"{n} LEFT";
            if (tally != null) tally.SetCount(n);
        }

        void OnWaveCompleted(int wave, float breather)
        {
            if (leftText != null) leftText.text = "CLEARED";
            if (tally != null) tally.SetCount(0);
            if (tapeSlap != null) tapeSlap.Play();
        }

        void OnAmmo(AmmoInfo info)
        {
            var s = KitStyle.Current;
            if (weaponName != null) weaponName.text = info.WeaponName.ToUpperInvariant();
            if (weaponIcon != null)
            {
                weaponIcon.enabled = info.Icon != null;
                if (info.Icon != null) weaponIcon.sprite = info.Icon;
            }
            if (magazineText != null)
            {
                magazineText.text = info.Unlimited ? "∞" : info.Magazine.ToString();
                bool low = !info.Unlimited && info.Magazine <= Mathf.CeilToInt(info.MagazineSize * 0.25f);
                magazineText.color = info.Magazine == 0 ? s.blood : low ? s.amber : s.bone;
            }
            if (reserveText != null)
            {
                reserveText.text = info.Unlimited ? "" : info.Reserve.ToString();
                reserveText.color = info.Reserve == 0 ? s.blood : s.muted;
            }
            if (magazine != null) magazine.Set(info.Unlimited ? 0 : info.Magazine, info.Unlimited ? 0 : info.MagazineSize);
            if (info.IsReloading != reloading)
            {
                reloading = info.IsReloading;
                reloadStart = Time.time; reloadDuration = info.ReloadDuration;
                if (reloadRoot != null) reloadRoot.SetActive(reloading);
                if (magazine != null) magazine.gameObject.SetActive(!reloading);
            }
        }

        void Update()
        {
            if (reloading) SetBar(reloadBar, reloadDuration > 0f ? (Time.time - reloadStart) / reloadDuration : 1f);
            Punch(scrapPunch, ref scrapPunchT);
            Punch(killsPunch, ref killsPunchT);
        }

        static void Punch(RectTransform rt, ref float t)
        {
            if (rt == null || t <= 0f) return;
            t = Mathf.MoveTowards(t, 0f, Time.unscaledDeltaTime * 4f);
            rt.localScale = Vector3.one * (1f + KitStyle.Current.punch * t * t);
        }
    }
}
