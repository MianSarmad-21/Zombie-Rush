using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ZombieRush.EditorTools
{
    /// Zombie Rush > Environment > Apply Night Environment (open scene)
    ///
    /// Natural dark night: moonlit cloudy skybox, cool moonlight from the moon's
    /// direction, dim blue ambient fill, blue-black distance fog, and a light
    /// post-processing stack (ACES, contrast, slight desaturation, vignette, bloom).
    public static class EnvironmentTools
    {
        const string Folder = "Assets/Environment/Sky";
        const string SkyTexturePath = Folder + "/Sky_Night.png";
        const string SkyMaterialPath = Folder + "/Sky_Night.mat";
        const string ProfilePath = "Assets/Environment/Night_PostFX.asset";
        const string VolumeName = "Night_PostFX";

        // Where the moon sits in Sky_Night.png: u = 0.30, elevation 26 degrees.
        // Unity's lat-long mapping: u = 0.5 - atan2(z, x) / 2pi.
        const float MoonU = 0.30f;
        const float MoonElevation = 32f;

        static readonly Color FogColor = new Color(0.045f, 0.055f, 0.08f);

        [MenuItem("Zombie Rush/Environment/Apply Night Environment (open scene)", priority = 40)]
        public static void ApplyNight()
        {
            if (Object.FindAnyObjectByType<ZombieRush.Atmosphere.KeepSceneEnvironment>() != null &&
                !EditorUtility.DisplayDialog("Night Environment",
                    "This scene uses the map pack's own sky, sun and post-processing. Applying night replaces that look.\n\nApply night anyway?",
                    "Apply night", "Cancel"))
                return;

            var sky = SetupSkyTexture();
            if (sky == null)
            {
                EditorUtility.DisplayDialog("Night Environment", $"Sky texture not found at {SkyTexturePath}.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Apply night environment");
            int group = Undo.GetCurrentGroup();

            // Sky
            var skyMat = SetupSkyMaterial(sky);
            RenderSettings.skybox = skyMat;

            // Moonlight: comes from the moon, so shadows line up with the sky.
            var moon = FindOrCreateMoonLight();
            Undo.RecordObject(moon, "Apply night environment");
            Undo.RecordObject(moon.transform, "Apply night environment");
            moon.type = LightType.Directional;
            moon.color = new Color(0.72f, 0.8f, 1f);
            moon.intensity = 0.75f;
            moon.shadows = LightShadows.Soft;
            moon.shadowStrength = 0.85f;
            moon.transform.rotation = Quaternion.LookRotation(-MoonDirection());
            RenderSettings.sun = moon;

            // Ambient: cool fill so silhouettes still read in the dark.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.14f, 0.16f, 0.24f);
            RenderSettings.ambientEquatorColor = new Color(0.09f, 0.1f, 0.13f);
            RenderSettings.ambientGroundColor = new Color(0.035f, 0.035f, 0.045f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.6f;

            // Fog: zombies emerge from the murk at the edge of the zone.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogDensity = 0.012f;

            // Cameras render the skybox and post-processing.
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
            {
                Undo.RecordObject(cam, "Apply night environment");
                cam.clearFlags = CameraClearFlags.Skybox;
                if (cam.farClipPlane < 300f) cam.farClipPlane = 300f;
                var data = cam.GetUniversalAdditionalCameraData();
                Undo.RecordObject(data, "Apply night environment");
                data.renderPostProcessing = true;
                EditorUtility.SetDirty(data);
            }

            SetupPostProcessing();
            DynamicGI.UpdateEnvironment();

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("Zombie Rush: night environment applied (moonlit sky, moonlight, fog, post-processing). Save the scene.");
        }

        const string DaySkyMaterialPath = Folder + "/Sky_Day.mat";
        const string DayProfilePath = "Assets/Resources/" + ZombieRush.Atmosphere.EnvironmentApplier.DayProfileResourcePath + ".asset";

        /// Creates the profile used when DAY is picked on the "Select the mode" screen:
        /// hazy overcast daylight (procedural sky, warm sun, grey-blue fog).
        /// Safe to run again - it only fills in what's missing.
        [MenuItem("Zombie Rush/Environment/Create Day Mode Profile", priority = 41)]
        public static void CreateDayProfileMenu()
        {
            var p = EnsureDayProfile();
            Selection.activeObject = p;
            Debug.Log("Zombie Rush: day profile ready at " + DayProfilePath + ". Pick DAY on the main menu to play it.");
        }

        public static ZombieRush.Atmosphere.EnvironmentProfile EnsureDayProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<ZombieRush.Atmosphere.EnvironmentProfile>(DayProfilePath);
            if (profile != null) return profile;

            var mat = AssetDatabase.LoadAssetAtPath<Material>(DaySkyMaterialPath);
            if (mat == null)
            {
                UIAssetGenerator.EnsureFolder(Folder);
                mat = new Material(Shader.Find("Skybox/Procedural")) { name = "Sky_Day" };
                mat.SetFloat("_SunSize", 0.035f);
                mat.SetFloat("_SunSizeConvergence", 6f);
                mat.SetFloat("_AtmosphereThickness", 1.15f);
                mat.SetColor("_SkyTint", new Color(0.52f, 0.55f, 0.6f));
                mat.SetColor("_GroundColor", new Color(0.36f, 0.35f, 0.33f));
                mat.SetFloat("_Exposure", 1.05f);
                AssetDatabase.CreateAsset(mat, DaySkyMaterialPath);
            }

            UIAssetGenerator.EnsureFolder(System.IO.Path.GetDirectoryName(DayProfilePath).Replace('\\', '/'));
            profile = ScriptableObject.CreateInstance<ZombieRush.Atmosphere.EnvironmentProfile>();
            profile.skybox = mat;
            profile.ambientSky = new Color(0.52f, 0.58f, 0.68f);
            profile.ambientEquator = new Color(0.4f, 0.4f, 0.39f);
            profile.ambientGround = new Color(0.18f, 0.17f, 0.15f);
            profile.reflectionIntensity = 0.9f;
            profile.fogEnabled = true;
            profile.fogColor = new Color(0.6f, 0.65f, 0.7f);
            profile.fogDensity = 0.0075f;
            profile.sunColor = new Color(1f, 0.94f, 0.84f);
            profile.sunIntensity = 1.1f;
            profile.sunEuler = new Vector3(42f, 215f, 0f);
            profile.shadowStrength = 0.8f;
            AssetDatabase.CreateAsset(profile, DayProfilePath);
            AssetDatabase.SaveAssets();
            return profile;
        }

        static Vector3 MoonDirection()
        {
            float lon = (0.5f - MoonU) * 2f * Mathf.PI;
            float el = MoonElevation * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(el) * Mathf.Cos(lon), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(lon)).normalized;
        }

        static Texture2D SetupSkyTexture()
        {
            var importer = AssetImporter.GetAtPath(SkyTexturePath) as TextureImporter;
            if (importer == null) return null;

            if (importer.wrapModeU != TextureWrapMode.Repeat || importer.mipmapEnabled || importer.maxTextureSize != 4096)
            {
                importer.textureType = TextureImporterType.Default;
                importer.textureShape = TextureImporterShape.Texture2D;
                importer.sRGBTexture = true;
                importer.mipmapEnabled = false; // avoids the lat-long seam line
                importer.wrapModeU = TextureWrapMode.Repeat;
                importer.wrapModeV = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.maxTextureSize = 4096;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;

                // Phones: half resolution, ASTC - about 1.5 MB instead of 32 MB.
                var android = importer.GetPlatformTextureSettings("Android");
                android.overridden = true;
                android.maxTextureSize = 2048;
                android.format = TextureImporterFormat.ASTC_6x6;
                importer.SetPlatformTextureSettings(android);
                var ios = importer.GetPlatformTextureSettings("iPhone");
                ios.overridden = true;
                ios.maxTextureSize = 2048;
                ios.format = TextureImporterFormat.ASTC_6x6;
                importer.SetPlatformTextureSettings(ios);

                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(SkyTexturePath);
        }

        static Material SetupSkyMaterial(Texture2D sky)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);
            if (mat == null)
            {
                var shader = Shader.Find("Skybox/Panoramic");
                mat = new Material(shader) { name = "Sky_Night" };
                AssetDatabase.CreateAsset(mat, SkyMaterialPath);
            }
            mat.SetTexture("_MainTex", sky);
            mat.SetFloat("_Mapping", 1f);   // latitude-longitude layout
            mat.SetFloat("_ImageType", 0f); // 360 degrees
            mat.SetFloat("_MirrorOnBack", 0f);
            mat.SetFloat("_Layout", 0f);
            mat.SetFloat("_Exposure", 1.15f);
            mat.SetFloat("_Rotation", 0f);
            mat.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f));
            mat.DisableKeyword("_MAPPING_6_FRAMES_LAYOUT");
            mat.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Light FindOrCreateMoonLight()
        {
            var light = RenderSettings.sun;
            if (light == null)
                light = Object.FindObjectsByType<Light>(FindObjectsInactive.Include).FirstOrDefault(l => l.type == LightType.Directional);
            if (light != null) return light;

            var go = new GameObject("Moonlight");
            Undo.RegisterCreatedObjectUndo(go, "Apply night environment");
            return go.AddComponent<Light>();
        }

        static void SetupPostProcessing()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            var tone = Get<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.ACES);

            var color = Get<ColorAdjustments>(profile);
            color.postExposure.Override(0.35f);
            color.contrast.Override(18f);
            color.saturation.Override(-15f);
            color.colorFilter.Override(new Color(0.95f, 0.97f, 1f));

            var vignette = Get<Vignette>(profile);
            vignette.intensity.Override(0.36f);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(Color.black);

            var bloom = Get<Bloom>(profile);
            bloom.threshold.Override(0.95f);
            bloom.intensity.Override(0.7f);
            bloom.scatter.Override(0.65f);
            bloom.tint.Override(new Color(0.85f, 0.9f, 1f));
            bloom.highQualityFiltering.Override(false); // cheaper on mobile

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var old = GameObject.Find("HorrorNight_PostFX"); // from the earlier blood-moon version
            if (old != null) Undo.DestroyObjectImmediate(old);

            var volumeGo = GameObject.Find(VolumeName);
            if (volumeGo == null)
            {
                volumeGo = new GameObject(VolumeName);
                Undo.RegisterCreatedObjectUndo(volumeGo, "Apply night environment");
            }
            var volume = volumeGo.GetComponent<Volume>();
            if (volume == null) volume = Undo.AddComponent<Volume>(volumeGo);
            Undo.RecordObject(volume, "Apply night environment");
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = profile;
        }

        static T Get<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet<T>(out var existing)) return existing;
            var component = profile.Add<T>(true);
            component.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }
    }
}
