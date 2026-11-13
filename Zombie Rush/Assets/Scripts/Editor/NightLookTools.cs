using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ZombieRush.Data;

namespace ZombieRush.EditorTools
{
    /// Zombie Rush > Environment > Apply Night Look Config
    /// Sets moonlight, sky light, fog and the Map/Global Volume grade from
    /// Assets/Data/NightLookConfig.asset (created with the current defaults if missing), then saves the scene.
    /// Marker: Temp/ZR_RunMapCleanup with "nightlook".
    static class NightLookTools
    {
        const string ConfigPath = "Assets/Data/NightLookConfig.asset";
        const string GradePath = "Assets/Environment/MainLevel_DarkGrade.asset";

        [MenuItem("Zombie Rush/Environment/Apply Night Look Config", priority = 32)]
        static void Menu() => Debug.Log("Zombie Rush: " + Apply());

        /// Mobile renderer to Forward+, like PC: every light lights the terrain (plain Forward allows 4 per object).
        public static string MobileForwardPlus()
        {
            const string path = "Assets/Settings/Mobile_Renderer.asset";
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (data == null) return "FAILED: " + path + " not found.";
            var before = data.renderingMode;
            data.renderingMode = RenderingMode.ForwardPlus;
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            return $"Mobile renderer: {before} -> {data.renderingMode}.";
        }

        public static string Apply()
        {
            AssetDatabase.ImportAsset(ConfigPath, ImportAssetOptions.ForceUpdate);   // pick up edits made outside the editor
            var cfg = AssetDatabase.LoadAssetAtPath<NightLookConfig>(ConfigPath);
            if (cfg == null)
            {
                cfg = ScriptableObject.CreateInstance<NightLookConfig>();
                AssetDatabase.CreateAsset(cfg, ConfigPath);
            }
            var sun = RenderSettings.sun;
            if (sun == null)
                sun = Object.FindObjectsByType<Light>(FindObjectsInactive.Include).Where(l => l.type == LightType.Directional).OrderByDescending(l => l.intensity).FirstOrDefault();
            if (sun != null)
            {
                Undo.RecordObject(sun, "Night look");
                sun.color = cfg.moonColor;
                sun.intensity = cfg.moonIntensity;
                sun.shadowStrength = cfg.moonShadowStrength;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = cfg.ambientSky;
            RenderSettings.ambientEquatorColor = cfg.ambientEquator;
            RenderSettings.ambientGroundColor = cfg.ambientGround;
            RenderSettings.ambientIntensity = cfg.ambientIntensity;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = cfg.fogColor;
            RenderSettings.fogDensity = cfg.fogDensity;

            var grade = AssetDatabase.LoadAssetAtPath<VolumeProfile>(GradePath);
            string gradeNote = "grade profile not found";
            if (grade != null)
            {
                if (grade.TryGet(out ColorAdjustments ca))
                {
                    ca.postExposure.Override(cfg.postExposure);
                    ca.contrast.Override(cfg.contrast);
                    ca.saturation.Override(cfg.saturation);
                    ca.colorFilter.Override(cfg.colorFilter);
                }
                if (grade.TryGet(out WhiteBalance wb))
                {
                    wb.temperature.Override(cfg.temperature);
                    wb.tint.Override(cfg.tint);
                }
                if (grade.TryGet(out Bloom bloom))
                {
                    bloom.intensity.Override(cfg.bloomIntensity);
                    bloom.threshold.Override(cfg.bloomThreshold);
                }
                if (grade.TryGet(out Vignette vig)) vig.intensity.Override(cfg.vignette);
                EditorUtility.SetDirty(grade);
                gradeNote = $"exposure {cfg.postExposure}, contrast {cfg.contrast}, bloom {cfg.bloomIntensity}";
            }
            var sky = RenderSettings.skybox;
            if (sky != null)
            {
                if (sky.HasProperty("_Tint")) sky.SetColor("_Tint", cfg.skyTint);
                if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", cfg.skyExposure);
                EditorUtility.SetDirty(sky);
            }
            DynamicGI.UpdateEnvironment();
            AssetDatabase.SaveAssets();
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"Night look applied from {ConfigPath}: moon {cfg.moonIntensity}, sky light {cfg.ambientSky}, fog {cfg.fogDensity}, {gradeNote}. Scene saved.";
        }
    }
}
