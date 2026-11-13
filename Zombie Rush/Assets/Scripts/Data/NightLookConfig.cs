using UnityEngine;

namespace ZombieRush.Data
{
    /// The night look of the city map: moonlight, sky light, fog and the camera grade.
    /// Applied by Zombie Rush > Environment > Apply Night Look Config. Brighter = higher
    /// ambient colours, moon intensity and post exposure; fog density sets how far you see.
    [CreateAssetMenu(fileName = "NightLookConfig", menuName = "Zombie Rush/Night Look Config")]
    public class NightLookConfig : ScriptableObject
    {
        [Header("Moon (the scene's directional light)")]
        public Color moonColor = new Color(0.74f, 0.83f, 1f);
        public float moonIntensity = 2.6f;
        [Range(0f, 1f)] public float moonShadowStrength = 0.7f;

        [Header("Sky light (ambient, trilight)")]
        public Color ambientSky = new Color(0.62f, 0.68f, 0.85f);
        public Color ambientEquator = new Color(0.36f, 0.40f, 0.50f);
        public Color ambientGround = new Color(0.20f, 0.20f, 0.23f);
        public float ambientIntensity = 1f;

        [Header("Fog")]
        public Color fogColor = new Color(0.20f, 0.24f, 0.32f);
        [Tooltip("Exponential squared density. 0.006 = old look; lower sees further.")]
        public float fogDensity = 0.0045f;

        [Header("Camera grade (Map/Global Volume profile)")]
        public float postExposure = 1.7f;
        public float contrast = 10f;
        public float saturation = -8f;
        public float bloomIntensity = 1.3f;
        public float bloomThreshold = 0.9f;
        [Range(0f, 1f)] public float vignette = 0.18f;
        [Tooltip("Tint over the whole picture (white = none).")]
        public Color colorFilter = new Color(0.9f, 0.95f, 1f);
        [Tooltip("White balance temperature: below 0 cooler (blue), above 0 warmer.")]
        [Range(-100f, 100f)] public float temperature = -15f;
        [Tooltip("White balance tint: below 0 green, above 0 magenta.")]
        [Range(-100f, 100f)] public float tint = 0f;

        [Header("Sky (the scene's skybox material)")]
        public Color skyTint = new Color(0.5f, 0.5f, 0.5f);
        public float skyExposure = 1.15f;
    }
}
