using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ZombieRush.Atmosphere
{
    /// Applies an EnvironmentProfile to every scene as it loads, so the sky, ambient
    /// light, fog and main light come from one data asset instead of being baked
    /// separately into each scene. Switching to a day look later means changing
    /// ProfileResourcePath (or the asset) - no scene edits needed.
    public static class EnvironmentApplier
    {
        public const string ProfileResourcePath = "ZombieRush/Environment_Night";

        static EnvironmentProfile profile;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;

            if (profile == null) profile = Resources.Load<EnvironmentProfile>(ProfileResourcePath);
            if (profile == null)
            {
                Debug.LogWarning($"EnvironmentApplier: no profile at Resources/{ProfileResourcePath}; leaving the scene's own lighting.");
                return;
            }

            Apply(profile);
        }

        public static void Apply(EnvironmentProfile p)
        {
            if (p.skybox != null) RenderSettings.skybox = p.skybox;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = p.ambientSky;
            RenderSettings.ambientEquatorColor = p.ambientEquator;
            RenderSettings.ambientGroundColor = p.ambientGround;
            RenderSettings.reflectionIntensity = p.reflectionIntensity;

            RenderSettings.fog = p.fogEnabled;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = p.fogColor;
            RenderSettings.fogDensity = p.fogDensity;

            var sun = FindMainLight();
            if (sun != null)
            {
                sun.color = p.sunColor;
                sun.intensity = p.sunIntensity;
                sun.shadowStrength = p.shadowStrength;
                sun.transform.rotation = Quaternion.Euler(p.sunEuler);
                RenderSettings.sun = sun;
            }

            DynamicGI.UpdateEnvironment();
        }

        static Light FindMainLight()
        {
            if (RenderSettings.sun != null) return RenderSettings.sun;

            Light best = null;
            foreach (var light in Object.FindObjectsByType<Light>())
            {
                if (light.type != LightType.Directional) continue;
                if (best == null || light.intensity > best.intensity) best = light;
            }
            return best;
        }
    }
}
