using UnityEngine;

namespace ZombieRush.Data
{
    /// Tuning for the floating "SAFE ZONE" label over the base (SafeZoneLabel).
    [CreateAssetMenu(fileName = "SafeZoneLabelConfig", menuName = "Zombie Rush/Safe Zone Label Config")]
    public class SafeZoneLabelConfig : ScriptableObject
    {
        [Header("Text")]
        public string text = "SAFE ZONE";
        public Color textColor = new Color(0.35f, 1f, 0.45f, 1f);
        public Color outlineColor = new Color(0f, 0.08f, 0.02f, 0.9f);
        [Tooltip("Dark backing plate behind the icon and text, so it reads against the sky.")]
        public Color panelColor = new Color(0.03f, 0.05f, 0.04f, 0.6f);
        [Tooltip("Thin strip along the bottom of the plate.")]
        public Color accentColor = new Color(0.2f, 1f, 0.35f, 0.9f);

        [Header("Placement")]
        [Tooltip("Metres above the top of the base building.")]
        public float heightAboveRoof = 6f;
        [Tooltip("Turn only around the vertical axis (stays upright), instead of tilting to face the camera fully.")]
        public bool uprightOnly = true;
        public float bobHeight = 0.3f;
        public float bobSpeed = 1.1f;

        [Header("Size")]
        [Tooltip("Height of the label in metres at its normal size.")]
        public float worldHeight = 6f;
        [Tooltip("Never smaller than this share of the screen height (stays readable from across the map).")]
        [Range(0f, 0.5f)] public float minScreenShare = 0.06f;
        [Tooltip("Never bigger than this share of the screen height (when standing right under it).")]
        [Range(0.05f, 1f)] public float maxScreenShare = 0.2f;

        [Header("Fade")]
        [Tooltip("Fully hidden beyond this many metres from the camera.")]
        public float maxDistance = 320f;
        [Tooltip("Fades out over this many metres before maxDistance.")]
        public float fadeBand = 60f;
    }
}
