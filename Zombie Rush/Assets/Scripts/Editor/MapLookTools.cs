using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ZombieRush.EditorTools
{
    /// Zombie Rush > Environment > Make Map Darker (no yellow haze)
    ///
    /// The Post Apocalyptic Buildings demo look is warm and bright: beige fog, a warm sun, a
    /// bright procedural sky with a brown ground colour, and colour grading that pushes the
    /// highlights strongly towards yellow. That clashes with the game's night colours (cyan =
    /// safe, red = danger, gold = reward). This keeps the same map, sky and sun position but:
    ///   - grading: neutral highlights, slightly cool shadows, darker, less saturated, vignette;
    ///   - sky: copy of the pack sky, dim and blue-grey (the pack's own material is untouched);
    ///   - fog: dark blue-grey and a bit thicker; ambient light: cool trilight (the old ambient
    ///     came from a stale baked probe of the previous map);
    ///   - sun: dimmer, cool moonlight colour.
    /// Undo with Zombie Rush > Environment > Restore Map Pack Look.
    public static class MapLookTools
    {
        const string GradePath = "Assets/Environment/MainLevel_DarkGrade.asset";
        const string SkyPath = "Assets/Environment/Sky/Skybox_MapDark.mat";
        const string PackProfile = "Assets/PostApocalypticBuildings/URP/demo/GlobalVolumeProfile.asset";
        const string PackSky = "Assets/PostApocalypticBuildings/URP/Materials/skybox.mat";
        const string Saved = "ZombieRush.MapLook.Original";

        // Moonlit night: the first "darker" pass was too dark to see, so the moon now lights the
        // city properly (from where the moon is in the sky) and the shadows stay soft blue.
        // Second pass (still too dark in play): brighter moon and sky light, lifted exposure,
        // thinner fog - still a night, just one you can see in.
        static readonly Color FogColor = new Color(0.18f, 0.21f, 0.27f);
        const float FogDensity = 0.006f;
        static readonly Color SunColor = new Color(0.74f, 0.83f, 1f);
        const float SunIntensity = 2.0f;
        static readonly Color AmbientSky = new Color(0.48f, 0.54f, 0.68f);
        static readonly Color AmbientEquator = new Color(0.27f, 0.30f, 0.37f);
        static readonly Color AmbientGround = new Color(0.13f, 0.13f, 0.15f);
        const float PostExposure = 0.5f;

        [MenuItem("Zombie Rush/Environment/Make Map Moonlit Night (no yellow haze)", priority = 30)]
        static void Apply()
        {
            var volumes = Object.FindObjectsByType<Volume>(FindObjectsInactive.Include)
                .Where(v => v.isGlobal && v.sharedProfile != null).ToArray();
            var packProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PackProfile);
            var sun = RenderSettings.sun;
            if (volumes.Length == 0 && sun == null)
            {
                EditorUtility.DisplayDialog("Map look", "No global Volume or sun in the open scene - open MainLevel first.", "OK");
                return;
            }

            SaveOriginal(sun);
            UIAssetGenerator.EnsureFolder("Assets/Environment/Sky");

            // ---- Colour grading -------------------------------------------------------
            var grade = AssetDatabase.LoadAssetAtPath<VolumeProfile>(GradePath);
            if (grade == null && packProfile != null && AssetDatabase.CopyAsset(PackProfile, GradePath))
                grade = AssetDatabase.LoadAssetAtPath<VolumeProfile>(GradePath);
            if (grade != null)
            {
                Grade(grade);
                foreach (var v in volumes)
                {
                    if (v.sharedProfile != packProfile && v.sharedProfile != grade) continue;
                    Undo.RecordObject(v, "Darker map");
                    v.sharedProfile = grade;
                    EditorUtility.SetDirty(v);
                }
            }

            // ---- Sky --------------------------------------------------------------------
            // The game's own moonlit sky (a real moon over clouds); falls back to a dimmed copy of the pack sky.
            var nightTex = EnvironmentTools.SetupSkyTexture();
            var sky = nightTex != null ? EnvironmentTools.SetupSkyMaterial(nightTex) : null;
            if (sky == null)
            {
                sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
                if (sky == null && AssetDatabase.CopyAsset(PackSky, SkyPath)) sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
                if (sky != null)
                {
                    Set(sky, "_Exposure", 0.6f);
                    Set(sky, "_AtmosphereThickness", 0.55f);
                    SetColor(sky, "_SkyTint", new Color(0.32f, 0.38f, 0.5f));
                    SetColor(sky, "_GroundColor", new Color(0.05f, 0.055f, 0.065f));
                    EditorUtility.SetDirty(sky);
                }
            }
            if (sky != null) RenderSettings.skybox = sky;

            // ---- Fog + ambient ------------------------------------------------------------
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogDensity = FogDensity;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = AmbientSky;
            RenderSettings.ambientEquatorColor = AmbientEquator;
            RenderSettings.ambientGroundColor = AmbientGround;
            RenderSettings.ambientIntensity = 1f;

            // ---- Moonlight: the sun light becomes the moon, shining from the moon in the sky ----
            if (sun != null)
            {
                Undo.RecordObject(sun, "Moonlit map");
                Undo.RecordObject(sun.transform, "Moonlit map");
                sun.color = SunColor;
                sun.intensity = SunIntensity;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.8f;
                if (nightTex != null) sun.transform.rotation = Quaternion.LookRotation(-EnvironmentTools.MoonDirection());
                EditorUtility.SetDirty(sun);
            }

            DynamicGI.UpdateEnvironment();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Map look",
                "Moonlit night applied: moon in the sky, bright cool moonlight from it, brighter sky light, thin blue-grey fog, no yellow grading.\n\n" +
                "Save the scene (Ctrl+S) and press Play.\nTo go back: Environment > Restore Map Pack Look.", "OK");
        }

        [MenuItem("Zombie Rush/Environment/Restore Map Pack Look", priority = 31)]
        static void Restore()
        {
            var packProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PackProfile);
            var grade = AssetDatabase.LoadAssetAtPath<VolumeProfile>(GradePath);
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include))
            {
                if (grade == null || v.sharedProfile != grade) continue;
                Undo.RecordObject(v, "Restore map look");
                v.sharedProfile = packProfile;
                EditorUtility.SetDirty(v);
            }

            string s = EditorPrefs.GetString(Saved, "");
            var o = string.IsNullOrEmpty(s) ? null : JsonUtility.FromJson<Original>(s);
            if (o != null)
            {
                RenderSettings.fogColor = o.fogColor;
                RenderSettings.fogDensity = o.fogDensity;
                RenderSettings.fogMode = (FogMode)o.fogMode;
                RenderSettings.ambientMode = (AmbientMode)o.ambientMode;
                RenderSettings.ambientSkyColor = o.ambientSky;
                RenderSettings.ambientEquatorColor = o.ambientEquator;
                RenderSettings.ambientGroundColor = o.ambientGround;
                if (RenderSettings.sun != null)
                {
                    Undo.RecordObject(RenderSettings.sun, "Restore map look");
                    RenderSettings.sun.color = o.sunColor;
                    RenderSettings.sun.intensity = o.sunIntensity;
                }
            }
            var packSky = AssetDatabase.LoadAssetAtPath<Material>(PackSky);
            if (packSky != null) RenderSettings.skybox = packSky;

            DynamicGI.UpdateEnvironment();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorUtility.DisplayDialog("Map look", "The map pack's original look is back. Save the scene (Ctrl+S).", "OK");
        }

        static void Grade(VolumeProfile p)
        {
            if (p.TryGet<ShadowsMidtonesHighlights>(out var smh))
            {
                smh.active = true;
                smh.shadows.Override(new Vector4(0.94f, 0.98f, 1.06f, -0.05f));
                smh.midtones.Override(new Vector4(1f, 1f, 1f, 0f));
                smh.highlights.Override(new Vector4(1f, 1f, 1f, 0f)); // was strong yellow
            }

            var ca = Get<ColorAdjustments>(p);
            ca.active = true;
            ca.postExposure.Override(PostExposure);
            ca.contrast.Override(6f);
            ca.saturation.Override(-12f);
            ca.colorFilter.Override(new Color(0.9f, 0.95f, 1f));

            var wb = Get<WhiteBalance>(p);
            wb.active = true;
            wb.temperature.Override(-15f);
            wb.tint.Override(0f);

            var vignette = Get<Vignette>(p);
            vignette.active = true;
            vignette.color.Override(Color.black);
            vignette.intensity.Override(0.2f);
            vignette.smoothness.Override(0.45f);

            EditorUtility.SetDirty(p);
            foreach (var c in p.components) EditorUtility.SetDirty(c);
        }

        /// Existing override, or a new one stored inside the profile asset.
        static T Get<T>(VolumeProfile p) where T : VolumeComponent
        {
            if (p.TryGet<T>(out var c)) return c;
            c = p.Add<T>(false);
            c.name = typeof(T).Name;
            c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(c, p);
            return c;
        }

        static void Set(Material m, string prop, float v) { if (m.HasProperty(prop)) m.SetFloat(prop, v); }
        static void SetColor(Material m, string prop, Color c) { if (m.HasProperty(prop)) m.SetColor(prop, c); }

        [System.Serializable]
        class Original
        {
            public Color fogColor, ambientSky, ambientEquator, ambientGround, sunColor;
            public float fogDensity, sunIntensity;
            public int fogMode, ambientMode;
        }

        /// Remembers the pack look once (first run), for Restore.
        static void SaveOriginal(Light sun)
        {
            if (EditorPrefs.HasKey(Saved)) return;
            var o = new Original
            {
                fogColor = RenderSettings.fogColor,
                fogDensity = RenderSettings.fogDensity,
                fogMode = (int)RenderSettings.fogMode,
                ambientMode = (int)RenderSettings.ambientMode,
                ambientSky = RenderSettings.ambientSkyColor,
                ambientEquator = RenderSettings.ambientEquatorColor,
                ambientGround = RenderSettings.ambientGroundColor,
                sunColor = sun != null ? sun.color : Color.white,
                sunIntensity = sun != null ? sun.intensity : 1f,
            };
            EditorPrefs.SetString(Saved, JsonUtility.ToJson(o));
        }
    }
}
