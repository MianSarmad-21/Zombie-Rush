using UnityEngine;

namespace ZombieRush.Data
{
    /// Tuning for the health bars over zombies and the damage numbers on each hit
    /// (ZombieOverheadUI). Sizes are in canvas units of the 1920x1080 reference screen.
    [CreateAssetMenu(fileName = "ZombieHudConfig", menuName = "Zombie Rush/Zombie HUD Config")]
    public class ZombieHudConfig : ScriptableObject
    {
        [Header("Health bar: when it shows")]
        [Tooltip("Show bars on unhurt zombies too. Off = a bar appears after the first hit.")]
        public bool showAtFullHealth = true;
        [Tooltip("Zombies further than this (metres from the camera) get no bar.")]
        public float maxDistance = 40f;
        [Tooltip("Hide the bar when a wall or building is between the camera and the zombie's head.")]
        public bool hideBehindWalls = true;
        [Tooltip("Seconds between wall checks per zombie (spreads the physics cost).")]
        public float wallCheckInterval = 0.15f;
        [Tooltip("How fast bars fade in and out (alpha per second).")]
        public float fadeSpeed = 8f;

        [Header("Health bar: look")]
        public Vector2 barSize = new Vector2(120f, 14f);
        [Tooltip("Dark border around the fill, in canvas units.")]
        public float border = 2f;
        [Tooltip("Metres above the top of the head.")]
        public float headClearance = 0.12f;
        [Tooltip("Zombies with at least this much max health (the Horror) get a wider bar.")]
        public float bigZombieHealth = 300f;
        public float bigBarWidthScale = 1.6f;
        [Tooltip("Full size up to nearDistance, shrinking to farScale at farDistance.")]
        public float nearDistance = 6f;
        public float farDistance = 35f;
        [Range(0.2f, 1f)] public float farScale = 0.55f;
        public Color backColor = new Color(0.03f, 0.03f, 0.04f, 0.75f);
        public Color fillColor = new Color(0.82f, 0.13f, 0.12f, 1f);
        [Tooltip("The lighter strip that drains after the fill, showing the chunk just lost.")]
        public Color chipColor = new Color(0.93f, 0.90f, 0.85f, 0.9f);
        [Tooltip("Seconds the chip waits after a hit before draining.")]
        public float chipDelay = 0.35f;
        [Tooltip("Share of the bar the chip drains per second.")]
        public float chipSpeed = 1.2f;

        [Header("Damage numbers")]
        [Tooltip("Also show numbers for turret hits, not only the player's shots.")]
        public bool showTurretDamage = false;
        [Tooltip("Hits on one zombie this close together (seconds) add into one number: all shotgun pellets of a shot.")]
        public float mergeWindow = 0.05f;
        public float popupDuration = 0.8f;
        [Tooltip("How far a number rises, in canvas units.")]
        public float popupRise = 70f;
        [Tooltip("Random sideways spread so quick hits don't stack on one spot.")]
        public float popupScatter = 28f;
        [Tooltip("Start size of the pop, easing down to 1.")]
        public float popupPunch = 1.6f;
        public float popupPunchTime = 0.12f;
        [Tooltip("Share of the duration after which the number starts fading.")]
        [Range(0f, 1f)] public float popupFadeStart = 0.55f;
        public int fontSize = 34;
        public int killFontSize = 44;
        public Color hitColor = new Color(0.97f, 0.95f, 0.90f, 1f);
        [Tooltip("The shot that kills.")]
        public Color killColor = new Color(0.96f, 0.72f, 0.24f, 1f);
        [Tooltip("A shot to the head that does not kill.")]
        public Color headshotColor = new Color(1f, 0.36f, 0.26f, 1f);
        public int headshotFontSize = 40;

        [Header("Pools")]
        public int maxBars = 30;
        public int maxPopups = 30;
    }
}
