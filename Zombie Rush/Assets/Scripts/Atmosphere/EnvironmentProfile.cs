using UnityEngine;

namespace ZombieRush.Atmosphere
{
    /// Data for how a level looks: sky, ambient light, fog and the main light.
    /// Create more of these (day, dusk, fog...) and point EnvironmentApplier at one.
    [CreateAssetMenu(fileName = "Environment_New", menuName = "Zombie Rush/Environment Profile")]
    public class EnvironmentProfile : ScriptableObject
    {
        [Header("Sky")]
        public Material skybox;

        [Header("Ambient light (trilight)")]
        public Color ambientSky = new Color(0.16f, 0.22f, 0.38f);
        public Color ambientEquator = new Color(0.10f, 0.13f, 0.22f);
        public Color ambientGround = new Color(0.04f, 0.045f, 0.07f);
        [Range(0f, 2f)] public float reflectionIntensity = 0.4f;

        [Header("Fog")]
        public bool fogEnabled = true;
        public Color fogColor = new Color(0.045f, 0.065f, 0.11f);
        [Tooltip("Exponential-squared density. About 0.01 fades the world out around 100-150 m.")]
        [Min(0f)] public float fogDensity = 0.01f;

        [Header("Main light (sun / moon)")]
        public Color sunColor = new Color(0.62f, 0.72f, 1f);
        [Min(0f)] public float sunIntensity = 0.55f;
        [Tooltip("Direction the light travels. Keep it matching where the moon is in the sky texture.")]
        public Vector3 sunEuler = new Vector3(30f, 215f, 0f);
        [Range(0f, 1f)] public float shadowStrength = 0.85f;
    }
}
