using UnityEngine;

namespace ZombieRush.Data
{
    /// How the game answers the player's actions: bullet impacts, tracers, camera kicks, hit sounds, phone
    /// vibration, zombie stagger, the damage direction arrows and the last kill of a wave. Per-gun kick and
    /// tracers live on WeaponData. Optional asset at Resources/ZombieRush/FeelConfig (Create > Zombie Rush >
    /// Feel Config); without it the defaults below are used.
    [CreateAssetMenu(fileName = "FeelConfig", menuName = "Zombie Rush/Feel Config")]
    public class FeelConfig : ScriptableObject
    {
        public const string ResourcePath = "ZombieRush/FeelConfig";

        [Header("Bullet impacts (walls, ground, props)")]
        public Color sparkColor = new Color(1f, 0.78f, 0.38f, 1f);
        public Color dustColor = new Color(0.42f, 0.38f, 0.32f, 0.85f);
        [Tooltip("Sparks per impact on metal / on anything else.")]
        public Vector2Int sparksMetal = new Vector2Int(8, 14);
        public Vector2Int sparksOther = new Vector2Int(2, 5);
        [Tooltip("Dust puffs per impact (more on dirt and grass).")]
        public int dustPuffs = 3;
        public float dustSize = 0.35f;
        [Tooltip("Bullet hole size, metres, and how long it stays.")]
        public float holeSize = 0.075f;
        public float holeLife = 12f;
        [Tooltip("Tint over the bullet hole texture (Fx_BulletHole, made by Blender/fx_bullet_hole.py). White = as rendered.")]
        public Color holeColor = new Color(1f, 1f, 1f, 1f);
        [Tooltip("Impacts one shotgun blast may show (its pellets hit many spots).")]
        public int shotgunImpacts = 3;

        [Header("Tracers")]
        public float tracerSpeed = 260f;
        public float tracerLength = 4.5f;
        public float tracerWidth = 0.03f;
        public Color tracerColor = new Color(1f, 0.86f, 0.55f, 0.85f);

        [Header("Camera")]
        [Tooltip("Shake when a zombie hits the player, at about 20 damage (scales with the hit).")]
        public float hitTakenShake = 0.4f;
        [Tooltip("Shake on each of the player's kills.")]
        public float killShake = 0.12f;
        [Tooltip("How fast a shot's field-of-view kick settles, degrees per second.")]
        public float fovKickRecovery = 30f;
        [Tooltip("Most the field of view can be kicked out, degrees.")]
        public float fovKickMax = 6f;

        [Header("Hit sounds (made in code, no audio files)")]
        [Range(0f, 1f)] public float hitTickVolume = 0.45f;
        [Range(0f, 1f)] public float headshotVolume = 0.8f;
        [Range(0f, 1f)] public float killVolume = 0.75f;

        [Header("Phone vibration (Android): milliseconds, strength 1-255")]
        public Vector2Int shotTap = new Vector2Int(10, 60);
        public Vector2Int headshotTap = new Vector2Int(22, 150);
        public Vector2Int killTap = new Vector2Int(30, 170);
        public Vector2Int hitTakenTap = new Vector2Int(70, 230);

        [Header("Zombie stagger")]
        [Tooltip("Push speed per point of damage, m/s, for a 100-health zombie (tougher zombies move less).")]
        public float knockPerDamage = 0.05f;
        public float maxKnock = 5f;
        [Tooltip("How fast the push dies away (per second).")]
        public float knockDecay = 12f;
        [Tooltip("Seconds a solid hit slows the zombie; a headshot holds it longer.")]
        public float staggerTime = 0.22f;
        public float headshotStagger = 0.4f;
        [Tooltip("Speed while staggered, share of normal.")]
        [Range(0f, 1f)] public float staggerSlow = 0.3f;
        [Tooltip("A zombie can't be staggered again for this long (no stun-locking with a rifle).")]
        public float staggerCooldown = 0.55f;

        [Header("Damage direction arrows")]
        public float indicatorTime = 1.1f;
        [Tooltip("Only the alpha is used: how strong the markers show (the picture carries its own reds).")]
        public Color indicatorColor = new Color(0.9f, 0.08f, 0.05f, 0.95f);
        [Tooltip("Distance from the screen centre, share of the screen height.")]
        public float indicatorRadius = 0.2f;

        [Header("Last kill of a wave (solo only: a squad's world can't slow down)")]
        [Range(0.05f, 1f)] public float slowMoScale = 0.3f;
        [Tooltip("Real seconds of slow motion.")]
        public float slowMoSeconds = 0.75f;
        public float lastKillShake = 0.35f;

        [Header("Kill streaks")]
        [Tooltip("Kills this close together (seconds) chain into DOUBLE / TRIPLE / MULTI KILL.")]
        public float multiKillWindow = 1.8f;
        [Tooltip("Kills without being hurt for KILLING SPREE, RAMPAGE, UNSTOPPABLE.")]
        public int[] spreeKills = { 10, 20, 35 };
        [Range(0f, 1f)] public float streakVolume = 0.8f;

        [Header("Headshot kills and deaths")]
        [Tooltip("A killing headshot takes the head off (hidden bone) with a burst of blood.")]
        public bool headshotGore = true;
        [Tooltip("A corpse slides back along the killing shot: metres per point of damage (tougher zombies slide less), at most deathSlideMax.")]
        public float deathSlidePerDamage = 0.012f;
        public float deathSlideMax = 1.3f;

        [Header("Low health")]
        [Tooltip("Below this share of health the colour drains, a dark red vignette closes in and sound gets muffled.")]
        [Range(0f, 1f)] public float lowHealthBelow = 0.35f;
        [Tooltip("Saturation at the lowest health (-100 = black and white).")]
        public float lowHealthSaturation = -70f;
        [Tooltip("Extra dark vignette at the lowest health (0 = none: the HUD's DamageFlashUI already pulses a blood vignette).")]
        public float lowHealthVignette = 0f;
        public Color lowHealthVignetteColor = new Color(0.25f, 0f, 0f, 1f);
        [Tooltip("Sound low-pass cutoff at the lowest health, Hz (22000 = no change).")]
        public float lowHealthCutoff = 1100f;

        [Header("Movement")]
        [Tooltip("Field of view added while sprinting, degrees.")]
        public float sprintFov = 6f;
        [Tooltip("A landing from at least this fall speed (m/s) dips the view; harder landings dip more.")]
        public float landingMinSpeed = 4f;
        public float landingDip = 2.2f;

        [Header("Colour grade")]
        [Tooltip("Saturation of greens (grass, leaves) on top of the level's grade: 0.5 = unchanged, lower = more muted.")]
        [Range(0f, 1f)] public float greenSaturation = 0.3f;

        static FeelConfig current;
        public static FeelConfig Current
        {
            get
            {
                if (current != null) return current;
                current = Resources.Load<FeelConfig>(ResourcePath);
                if (current == null) { current = CreateInstance<FeelConfig>(); current.hideFlags = HideFlags.HideAndDontSave; }
                return current;
            }
        }
    }
}
