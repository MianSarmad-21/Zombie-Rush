using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Core;
using ZombieRush.Data;
using ZombieRush.Effects;

namespace ZombieRush.UI.Kit
{
    /// The "Infection Meter" vitals: an anatomical heart that beats faster as health drops, a veined glass
    /// tube whose glowing liquid is the player's health, the "74/100 HP" read-out, and three MUTATION LEVEL
    /// orbs that light up with the kill spree (KILLING SPREE / RAMPAGE / UNSTOPPABLE) and go dark when hurt.
    /// Built by Zombie Rush/UI/Install Bio HUD; every reference is optional.
    public class InfectionMeter : MonoBehaviour
    {
        [Header("Heart")]
        public RectTransform heart;
        [Tooltip("Beats per minute at full health and at almost none.")]
        public Vector2 bpm = new Vector2(64f, 150f);
        [Range(0f, 0.3f)] public float beatSize = 0.09f;

        [Header("Tube")]
        [Tooltip("Filled (horizontal) image inside the tube: the health liquid.")]
        public Image liquid;
        [Tooltip("Drains slowly behind the liquid to show what was just lost.")]
        public Image trail;
        public Text hpText;
        public Color healthy = new Color(0.2f, 0.5f, 0.1f);
        public Color hurt = new Color(0.62f, 0.45f, 0.08f);
        public Color critical = new Color(0.65f, 0.08f, 0.05f);
        [Range(0f, 1f)] public float hurtAt = 0.6f;
        [Range(0f, 1f)] public float criticalAt = 0.3f;

        [Header("Mutation level (kill spree)")]
        public Image[] orbs;
        public Color orbLit = Color.white;
        public Color orbDark = new Color(0.5f, 0.56f, 0.5f, 0.9f);

        float health01 = 1f, shown = 1f, trailShown = 1f, beatPhase;
        bool dead;

        void Awake()
        {
            GameEvents.OnPlayerHealthChanged += OnHealth;
            GameEvents.OnPlayerDied += OnDied;
        }

        void OnDestroy()
        {
            GameEvents.OnPlayerHealthChanged -= OnHealth;
            GameEvents.OnPlayerDied -= OnDied;
        }

        void OnHealth(float current, float max)
        {
            health01 = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            dead = current <= 0f;
            if (hpText != null) hpText.text = $"{Mathf.CeilToInt(Mathf.Max(0f, current))}/{Mathf.RoundToInt(max)} HP";
        }

        void OnDied() { health01 = 0f; dead = true; }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;

            // liquid eases to the new value; the trail lags behind it
            shown = Mathf.MoveTowards(shown, health01, dt * 1.5f);
            trailShown = trailShown < shown ? shown : Mathf.MoveTowards(trailShown, shown, dt * 0.35f);
            Color c = health01 <= criticalAt ? critical : health01 <= hurtAt ? Color.Lerp(hurt, healthy, (health01 - criticalAt) / Mathf.Max(0.01f, hurtAt - criticalAt)) : healthy;
            if (liquid != null)
            {
                liquid.fillAmount = shown;
                float flicker = health01 <= criticalAt ? 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 12f) : 1f;
                liquid.color = new Color(c.r * flicker, c.g * flicker, c.b * flicker, 1f);
            }
            if (trail != null) trail.fillAmount = trailShown;
            if (hpText != null) hpText.color = health01 <= criticalAt ? critical : Color.Lerp(Color.white, healthy, 0.35f);

            // the heart: a lub-dub that speeds up as health falls, and stops at death
            if (heart != null)
            {
                float s = 1f;
                if (!dead)
                {
                    beatPhase = Mathf.Repeat(beatPhase + dt * Mathf.Lerp(bpm.y, bpm.x, health01) / 60f, 1f);
                    s += beatSize * (Pulse(beatPhase, 0.06f) + 0.6f * Pulse(beatPhase, 0.24f));
                }
                heart.localScale = new Vector3(s, s, 1f);
            }

            // mutation orbs = spree tiers reached
            if (orbs != null && orbs.Length > 0)
            {
                int[] tiers = FeelConfig.Current != null ? FeelConfig.Current.spreeKills : null;
                int spree = FeelDirector.Spree, lit = 0;
                if (tiers != null) foreach (int t in tiers) if (spree >= t) lit++;
                for (int i = 0; i < orbs.Length; i++)
                {
                    if (orbs[i] == null) continue;
                    bool on = i < lit;
                    float glow = on ? 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 4f + i) : 1f;
                    orbs[i].color = on ? orbLit * glow : orbDark;
                }
            }
        }

        // a quick bump centred on 'at' within the beat cycle
        static float Pulse(float phase, float at)
        {
            float d = (phase - at) / 0.07f;
            return Mathf.Exp(-d * d);
        }
    }
}
