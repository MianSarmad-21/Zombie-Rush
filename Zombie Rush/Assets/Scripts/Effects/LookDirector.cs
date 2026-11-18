using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ZombieRush.Data;

namespace ZombieRush.Data
{
    /// One complete look for the level: sky, sky light, fog, the moon/sun, camera exposure, how bright the
    /// zone walls glow and how strongly the lamps light their surroundings.
    [System.Serializable]
    public class LookPreset
    {
        public string name = "Look";
        [Tooltip("Leave the level exactly as the scene sets it up (a reference to compare with).")]
        public bool sceneAsIs;

        [Header("Sky light (ambient, trilight)")]
        public Color ambientSky = new Color(0.1f, 0.13f, 0.22f);
        public Color ambientEquator = new Color(0.07f, 0.09f, 0.14f);
        public Color ambientGround = new Color(0.03f, 0.03f, 0.04f);

        [Header("Fog")]
        public Color fogColor = new Color(0.03f, 0.04f, 0.07f);
        public float fogDensity = 0.016f;

        [Header("Moon or sun (the scene's main directional light)")]
        public Color mainLightColor = new Color(0.6f, 0.7f, 1f);
        public float mainLightIntensity = 0.6f;
        [Range(0f, 1f)] public float shadowStrength = 0.8f;
        [Tooltip("Height of the moon/sun above the horizon, degrees (low = long shadows).")]
        public float mainLightElevation = 35f;

        [Header("Sky (tint and brightness of the sky picture)")]
        public Color skyTint = new Color(0.25f, 0.3f, 0.42f);
        public float skyExposure = 0.45f;
        [Tooltip("Paint a clean sky in code (gradient, soft clouds, the moon or sun where the light comes from) instead of the scene's sky picture.")]
        public bool generatedSky = true;
        public Color skyTop = new Color(0.02f, 0.03f, 0.07f);
        public Color skyHorizon = new Color(0.08f, 0.1f, 0.16f);
        public Color skyBelow = new Color(0.02f, 0.02f, 0.03f);
        [Tooltip("Glow along the horizon on the side of the moon/sun.")]
        public Color horizonGlow = new Color(0.1f, 0.12f, 0.2f);
        [Range(0f, 1f)] public float clouds = 0.35f;
        public Color cloudColor = new Color(0.06f, 0.07f, 0.1f);
        [Tooltip("Moon/sun disc size, degrees (0 = no disc).")]
        public float discSize = 2.4f;
        public Color discColor = new Color(0.85f, 0.9f, 1f);

        [Header("Camera")]
        [Tooltip("Exposure in stops (the scene's night grade uses +1.7).")]
        public float postExposure = 0.6f;
        public float contrast = 18f;
        [Tooltip("Colour temperature: negative = colder/bluer, positive = warmer.")]
        public float temperature = -12f;
        [Tooltip("Colour saturation, -100 (grey) .. 100.")]
        public float saturation = 0f;

        [Header("Zone walls and lamps")]
        [Tooltip("Brightness of the red zone wall (and the green base wall) as a share of their normal glow; they still flare up close to the edge.")]
        [Range(0f, 1.5f)] public float zoneWallGlow = 0.35f;
        [Tooltip("Lamps, lanterns and fires: brightness and reach, as a share of the scene's.")]
        public float lampIntensity = 2.2f;
        public float lampRange = 1.6f;
        public Color lampTint = new Color(1f, 0.72f, 0.42f);

        [Header("Mist wall round the zone (hides the city outside)")]
        [Tooltip("Height of the mist wall, metres (140 as in the scene: lower shows its round top edge as a ring in the sky).")]
        public float fogWallHeight = 140f;
        [Tooltip("How strong its rolling billows are, 0..1 (the scene uses 0.45, which reads as blotches).")]
        [Range(0f, 1f)] public float fogWallBillows = 0.15f;
    }

    /// The looks the game can switch between (F8 in a match cycles them, the choice is remembered).
    /// Tune the looks in BuiltIn() below. (Kept in this file: Unity's script list missed a separate file.)
    public class LookConfig
    {

        [Tooltip("The look a fresh install starts with (index into presets).")]
        public int defaultLook = 0;   // "Current" until a look is chosen
        public LookPreset[] presets = BuiltIn();

        public static LookPreset[] BuiltIn() => new[]
        {
            new LookPreset { name = "Current", sceneAsIs = true },
            new LookPreset   // properly dark: warm lamp pools in cold blue, the distance fades to black
            {
                name = "Dark Night",
                ambientSky = new Color(0.13f, 0.17f, 0.27f), ambientEquator = new Color(0.08f, 0.1f, 0.15f), ambientGround = new Color(0.03f, 0.03f, 0.045f),
                fogColor = new Color(0.03f, 0.045f, 0.075f), fogDensity = 0.015f,
                mainLightColor = new Color(0.55f, 0.66f, 1f), mainLightIntensity = 0.75f, shadowStrength = 0.85f, mainLightElevation = 32f,
                skyTint = new Color(0.2f, 0.25f, 0.38f), skyExposure = 0.35f,
                postExposure = 1.05f, contrast = 20f, temperature = -14f,
                zoneWallGlow = 0.07f, lampIntensity = 2.6f, lampRange = 1.8f, lampTint = new Color(1f, 0.68f, 0.38f),
                skyTop = new Color(0.01f, 0.012f, 0.03f), skyHorizon = new Color(0.03f, 0.045f, 0.075f), horizonGlow = new Color(0.05f, 0.06f, 0.1f),
                clouds = 0.3f, cloudColor = new Color(0.02f, 0.025f, 0.04f), discSize = 2f, discColor = new Color(0.75f, 0.82f, 0.95f),
            },
            new LookPreset   // still night, easier to read: clear blue moonlight, softer fog
            {
                name = "Moonlit Night",
                ambientSky = new Color(0.18f, 0.24f, 0.38f), ambientEquator = new Color(0.12f, 0.15f, 0.22f), ambientGround = new Color(0.05f, 0.05f, 0.07f),
                fogColor = new Color(0.07f, 0.1f, 0.16f), fogDensity = 0.011f,
                mainLightColor = new Color(0.62f, 0.74f, 1f), mainLightIntensity = 1.15f, shadowStrength = 0.8f, mainLightElevation = 40f,
                skyTint = new Color(0.3f, 0.38f, 0.55f), skyExposure = 0.6f,
                postExposure = 0.9f, contrast = 16f, temperature = -16f,
                zoneWallGlow = 0.09f, lampIntensity = 1.8f, lampRange = 1.4f, lampTint = new Color(1f, 0.75f, 0.45f),
                skyTop = new Color(0.02f, 0.035f, 0.08f), skyHorizon = new Color(0.07f, 0.1f, 0.16f), horizonGlow = new Color(0.12f, 0.16f, 0.26f),
                clouds = 0.35f, cloudColor = new Color(0.05f, 0.065f, 0.1f), discSize = 2.6f, discColor = new Color(0.85f, 0.9f, 1f),
            },
            new LookPreset   // the apocalypse at dusk: a low orange sun through murky haze
            {
                name = "Toxic Dusk",
                ambientSky = new Color(0.46f, 0.34f, 0.3f), ambientEquator = new Color(0.34f, 0.24f, 0.18f), ambientGround = new Color(0.12f, 0.09f, 0.07f),
                fogColor = new Color(0.4f, 0.27f, 0.17f), fogDensity = 0.008f,
                mainLightColor = new Color(1f, 0.6f, 0.32f), mainLightIntensity = 1.6f, shadowStrength = 0.7f, mainLightElevation = 22f,
                skyTint = new Color(0.62f, 0.4f, 0.28f), skyExposure = 0.6f,
                postExposure = 0.75f, contrast = 20f, temperature = 16f,
                zoneWallGlow = 0.12f, lampIntensity = 1.2f, lampRange = 1.2f, lampTint = new Color(1f, 0.78f, 0.5f),
                skyTop = new Color(0.12f, 0.08f, 0.07f), skyHorizon = new Color(0.4f, 0.27f, 0.17f), horizonGlow = new Color(0.55f, 0.3f, 0.12f),
                clouds = 0.5f, cloudColor = new Color(0.22f, 0.14f, 0.1f), discSize = 3.2f, discColor = new Color(1f, 0.75f, 0.45f),
            },
            new LookPreset   // twilight: every texture readable, a cold blue mood, warm lamps
            {
                name = "Blue Hour",
                ambientSky = new Color(0.36f, 0.41f, 0.54f), ambientEquator = new Color(0.25f, 0.27f, 0.33f), ambientGround = new Color(0.11f, 0.11f, 0.12f),
                fogColor = new Color(0.15f, 0.18f, 0.25f), fogDensity = 0.008f,
                mainLightColor = new Color(0.78f, 0.84f, 1f), mainLightIntensity = 1.55f, shadowStrength = 0.7f, mainLightElevation = 34f,
                postExposure = 0.55f, contrast = 12f, temperature = -8f, saturation = -6f,
                zoneWallGlow = 0.12f, lampIntensity = 1.6f, lampRange = 1.3f, lampTint = new Color(1f, 0.74f, 0.45f),
                skyTop = new Color(0.03f, 0.05f, 0.12f), skyHorizon = new Color(0.15f, 0.18f, 0.25f), horizonGlow = new Color(0.32f, 0.3f, 0.36f),
                clouds = 0.4f, cloudColor = new Color(0.09f, 0.1f, 0.14f), discSize = 2.6f, discColor = new Color(0.9f, 0.93f, 1f),
            },
            new LookPreset   // overcast daylight under storm clouds, a low warm sun breaking through: textures clear, mood grim
            {
                name = "Stormy Dusk",
                ambientSky = new Color(0.4f, 0.42f, 0.42f), ambientEquator = new Color(0.31f, 0.31f, 0.29f), ambientGround = new Color(0.13f, 0.12f, 0.1f),
                fogColor = new Color(0.27f, 0.28f, 0.27f), fogDensity = 0.008f,
                mainLightColor = new Color(1f, 0.82f, 0.62f), mainLightIntensity = 1.35f, shadowStrength = 0.75f, mainLightElevation = 20f,
                postExposure = 0.45f, contrast = 16f, temperature = 3f, saturation = -14f,
                zoneWallGlow = 0.2f, lampIntensity = 1.1f, lampRange = 1.1f, lampTint = new Color(1f, 0.78f, 0.5f),
                skyTop = new Color(0.09f, 0.1f, 0.11f), skyHorizon = new Color(0.27f, 0.28f, 0.27f), horizonGlow = new Color(0.5f, 0.36f, 0.22f),
                clouds = 0.75f, cloudColor = new Color(0.12f, 0.12f, 0.13f), discSize = 0f, discColor = new Color(1f, 0.85f, 0.6f),
            },
        };

        static LookConfig current;
        public static LookConfig Current => current ?? (current = new LookConfig());
    }
}


namespace ZombieRush.Effects
{
    /// Puts one of LookConfig's looks on the level at runtime: sky light, fog, moon/sun, sky picture, camera
    /// exposure, zone wall glow, lamps. F8 cycles the looks (a note says which one), the choice is remembered.
    /// The scene and the project's materials are never changed: the scene's own values are kept and restored
    /// for the "as is" look, and the sky and wall materials are swapped for runtime copies.
    public class LookDirector : MonoBehaviour
    {
        const string PrefKey = "zr.look";

        static LookDirector instance;
        public static int CurrentIndex { get; private set; } = -1;
        public static int Count => LookConfig.Current.presets != null ? LookConfig.Current.presets.Length : 0;
        public static string NameOf(int i) => i >= 0 && i < Count ? LookConfig.Current.presets[i].name : "?";

        // the scene's own values
        bool saved;
        AmbientMode ambientMode; Color ambSky, ambEq, ambGround;
        bool fog; FogMode fogMode; Color fogColor; float fogDensity;
        Material skybox, skyCopy;
        Light main; Color mainColor; float mainIntensity, mainShadow; Quaternion mainRotation;
        class LampState { public Light light; public float intensity, range; public Color color; }
        readonly List<LampState> lamps = new List<LampState>();
        class WallState { public Material original, copy; public float intensity; }
        readonly Dictionary<Material, WallState> walls = new Dictionary<Material, WallState>();
        Volume volume; ColorAdjustments grade; WhiteBalance balance;

        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int TintId = Shader.PropertyToID("_Tint"), ExposureId = Shader.PropertyToID("_Exposure"), IntensityId = Shader.PropertyToID("_Intensity");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnLoaded;
        }

        static void OnLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
        {
            if (m == UnityEngine.SceneManagement.LoadSceneMode.Single) Ensure();
        }

        // the level that is open when play starts (its load event can come before anything is listening)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void FirstLevel() => Ensure();

        static void Ensure()
        {
            if (instance != null) return;
            instance = new GameObject("LookDirector").AddComponent<LookDirector>();
        }

        void Awake() { if (instance == null) instance = this; }
        void OnDestroy() { if (instance == this) instance = null; }

        // a frame after the level's own setup has run, so its values are the ones kept
        IEnumerator Start()
        {
            yield return null;
            int pick = PlayerPrefs.GetInt(PrefKey, LookConfig.Current.defaultLook);
            Apply(Mathf.Clamp(pick, 0, Mathf.Max(0, Count - 1)), announce: false);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8) && Count > 0) Apply((CurrentIndex + 1) % Count, announce: true);
        }

        /// What the current level offers the looks (for test logs).
        public static string Describe() => instance == null ? "no LookDirector" :
            $"{instance.walls.Count} zone wall material(s), {instance.fogs.Count} mist wall(s), {instance.lamps.Count} lamp(s), sky {(instance.skyCopy != null ? instance.skyCopy.shader.name : "none")}, main light {(instance.main != null ? instance.main.name : "none")}";

        /// Puts look `index` on the level (also from tests).
        public static void Apply(int index, bool announce)
        {
            if (Count == 0) return;
            Ensure();
            index = Mathf.Clamp(index, 0, Count - 1);
            instance.ApplyLook(LookConfig.Current.presets[index]);
            CurrentIndex = index;
            PlayerPrefs.SetInt(PrefKey, index);
            if (announce) ZombieRush.UI.PickupToast.Show($"LOOK: {NameOf(index).ToUpperInvariant()}   (F8 TO CHANGE)", new Color(0.75f, 0.85f, 1f));
        }

        void Save()
        {
            if (saved) return;
            saved = true;
            ambientMode = RenderSettings.ambientMode;
            ambSky = RenderSettings.ambientSkyColor; ambEq = RenderSettings.ambientEquatorColor; ambGround = RenderSettings.ambientGroundColor;
            fog = RenderSettings.fog; fogMode = RenderSettings.fogMode; fogColor = RenderSettings.fogColor; fogDensity = RenderSettings.fogDensity;
            skybox = RenderSettings.skybox;
            if (skybox != null) skyCopy = new Material(skybox) { name = skybox.name + " (runtime look)" };

            main = RenderSettings.sun;
            if (main == null)
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional && (main == null || l.intensity > main.intensity)) main = l;
            if (main != null) { mainColor = main.color; mainIntensity = main.intensity; mainShadow = main.shadowStrength; mainRotation = main.transform.rotation; }

            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if ((l.type == LightType.Point || l.type == LightType.Spot) && IsLamp(l.transform))
                    lamps.Add(new LampState { light = l, intensity = l.intensity, range = l.range, color = l.color });

            foreach (var zone in FindObjectsByType<ZombieRush.World.ZoneBoundaryFX>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var copy = WallCopy(zone.wallMaterial);
                if (copy != null) zone.wallMaterial = copy;   // walls built later (the zone growing) use the copy too
                var fogCopy = FogCopy(zone.fogMaterial);
                if (fogCopy != null) zone.fogMaterial = fogCopy;
                foreach (var r in zone.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var c = WallCopy(mats[i]);
                        if (c == null) c = FogCopy(mats[i]);
                        if (c != null && c != mats[i]) { mats[i] = c; changed = true; }
                    }
                    if (changed) r.sharedMaterials = mats;
                }
            }

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            grade = profile.Add<ColorAdjustments>(true);
            balance = profile.Add<WhiteBalance>(true);
            var go = new GameObject("LookVolume");
            go.transform.SetParent(transform, false);
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 41;   // above the level's grade, below the low-health layer
            volume.sharedProfile = profile;
        }

        // the misty wall round the zone that hides the city outside: its height and billows per look
        class FogState { public Material copy; public float height, contrast; }
        readonly Dictionary<Material, FogState> fogs = new Dictionary<Material, FogState>();
        static readonly int HeightId = Shader.PropertyToID("_Height"), ContrastId = Shader.PropertyToID("_Contrast");

        Material FogCopy(Material m)
        {
            if (m == null || !m.HasProperty(HeightId) || !m.HasProperty(ContrastId)) return null;
            if (fogs.TryGetValue(m, out var f)) return f.copy;
            foreach (var kv in fogs) if (kv.Value.copy == m) return m;   // already a copy
            var copy = new Material(m) { name = m.name + " (runtime look)" };
            fogs[m] = new FogState { copy = copy, height = m.GetFloat(HeightId), contrast = m.GetFloat(ContrastId) };
            return copy;
        }

        /// A runtime copy of a zone wall material (glowing walls only), made once per material.
        Material WallCopy(Material m)
        {
            if (m == null || !m.HasProperty(IntensityId)) return null;
            if (walls.TryGetValue(m, out var w)) return w.copy;
            foreach (var kv in walls) if (kv.Value.copy == m) return m;   // already a copy
            var copy = new Material(m) { name = m.name + " (runtime look)" };
            walls[m] = new WallState { original = m, copy = copy, intensity = m.GetFloat(IntensityId) };
            return copy;
        }

        static bool IsLamp(Transform t)
        {
            for (var p = t; p != null; p = p.parent)
            {
                string n = p.name;
                if (n.StartsWith("Lamp") || n.StartsWith("FireLight") || n.Contains("Lantern") || n == "BaseLights" || n == "StreetLamps"
                    || n.StartsWith("RoomLamp") || n.StartsWith("Gate_In") || n.StartsWith("Gate_Out") || n.StartsWith("DeckFlood")) return true;   // the fortress's own lights
                if (p.GetComponent<ZombieRush.Player.PlayerHealth>() != null || p.GetComponent<ZombieRush.Zombie.ZombieHealth>() != null) return false;
            }
            return false;
        }

        // ---- generated sky: a 360 picture for the Skybox/Panoramic shader (latitude-longitude layout)
        const int SkyW = 1024, SkyH = 512;
        readonly Dictionary<LookPreset, Texture2D> skies = new Dictionary<LookPreset, Texture2D>();

        Texture2D SkyFor(LookPreset p, Vector3 toLight)
        {
            if (skies.TryGetValue(p, out var t) && t != null) return t;
            t = PaintSky(p, toLight.normalized);
            skies[p] = t;
            return t;
        }

        void OnDisable()
        {
            foreach (var t in skies.Values) if (t != null) Destroy(t);
            skies.Clear();
        }

        /// Gradient from the horizon haze up to the zenith, a glow along the horizon on the light's side, soft
        /// clouds (thickest towards the horizon) and the moon/sun disc where the main light comes from.
        static Texture2D PaintSky(LookPreset p, Vector3 toLight)
        {
            var px = new Color32[SkyW * SkyH];
            float discRad = p.discSize * 0.5f * Mathf.Deg2Rad;
            float lightAz = Mathf.Atan2(toLight.z, toLight.x) * Mathf.Rad2Deg;
            for (int y = 0; y < SkyH; y++)
            {
                // the shader reads v = 1 - acos(dir.y) / pi and u = 0.5 - atan2(dir.z, dir.x) / (2 pi)
                float theta = (1f - (y + 0.5f) / SkyH) * Mathf.PI;   // angle from straight up
                float elev = Mathf.PI * 0.5f - theta;                // radians above the horizon
                float sinT = Mathf.Sin(theta), cosT = Mathf.Cos(theta);
                for (int x = 0; x < SkyW; x++)
                {
                    float phi = (0.5f - (x + 0.5f) / SkyW) * 2f * Mathf.PI;
                    var dir = new Vector3(sinT * Mathf.Cos(phi), cosT, sinT * Mathf.Sin(phi));
                    Color c;
                    if (elev >= 0f)
                    {
                        float t = Mathf.Pow(Mathf.Clamp01(elev / (Mathf.PI * 0.5f)), 0.55f);
                        c = Color.Lerp(p.skyHorizon, p.skyTop, t);
                        float az = Mathf.DeltaAngle(phi * Mathf.Rad2Deg, lightAz) * Mathf.Deg2Rad;
                        float glow = Mathf.Exp(-az * az * 1.6f) * Mathf.Exp(-elev * 7f);
                        c += p.horizonGlow * glow;
                        if (p.clouds > 0f && elev > 0.01f)
                        {
                            // noise on a flat cloud ceiling seen from below: fades out straight overhead and at the horizon line
                            float k = 0.9f / Mathf.Max(0.08f, dir.y);
                            float cx = dir.x * k, cz = dir.z * k;
                            float n = Mathf.PerlinNoise(cx + 13.1f, cz + 7.7f) * 0.55f
                                    + Mathf.PerlinNoise(cx * 2.3f + 3f, cz * 2.3f + 11f) * 0.3f
                                    + Mathf.PerlinNoise(cx * 5.1f + 21f, cz * 5.1f + 2f) * 0.15f;
                            float cover = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((n - (1f - p.clouds) * 0.75f) * 3f));
                            float fade = Mathf.Clamp01(elev * 10f) * Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(elev / 1.2f));
                            c = Color.Lerp(c, p.cloudColor + p.horizonGlow * (glow * 0.6f), cover * fade);
                        }
                    }
                    else c = Color.Lerp(p.skyHorizon, p.skyBelow, Mathf.Clamp01(-elev * 5f));

                    if (discRad > 0f)
                    {
                        float ang = Mathf.Acos(Mathf.Clamp(Vector3.Dot(dir, toLight), -1f, 1f));
                        float disc = Mathf.Clamp01((discRad - ang) / (discRad * 0.12f) + 0.5f);
                        float halo = Mathf.Exp(-ang * ang / (discRad * discRad * 9f)) * 0.35f;
                        c = Color.Lerp(c, p.discColor, disc) + p.discColor * (halo * (1f - disc));
                    }
                    px[y * SkyW + x] = new Color32((byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f), (byte)(Mathf.Clamp01(c.b) * 255f), 255);
                }
            }
            var tex = new Texture2D(SkyW, SkyH, TextureFormat.RGBA32, false)
            { wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Sky_" + p.name };
            tex.SetPixels32(px);
            tex.Apply(false, true);   // no CPU copy kept
            return tex;
        }

        void ApplyLook(LookPreset p)
        {
            Save();
            if (p.sceneAsIs)
            {
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientSkyColor = ambSky; RenderSettings.ambientEquatorColor = ambEq; RenderSettings.ambientGroundColor = ambGround;
                RenderSettings.fog = fog; RenderSettings.fogMode = fogMode; RenderSettings.fogColor = fogColor; RenderSettings.fogDensity = fogDensity;
                RenderSettings.skybox = skybox;
                if (main != null) { main.color = mainColor; main.intensity = mainIntensity; main.shadowStrength = mainShadow; main.transform.rotation = mainRotation; }
                foreach (var l in lamps) if (l.light != null) { l.light.intensity = l.intensity; l.light.range = l.range; l.light.color = l.color; }
                foreach (var w in walls.Values) w.copy.SetFloat(IntensityId, w.intensity);
                foreach (var f in fogs.Values) { f.copy.SetFloat(HeightId, f.height); f.copy.SetFloat(ContrastId, f.contrast); }
                volume.weight = 0f;
                DynamicGI.UpdateEnvironment();
                return;
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = p.ambientSky; RenderSettings.ambientEquatorColor = p.ambientEquator; RenderSettings.ambientGroundColor = p.ambientGround;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = p.fogColor; RenderSettings.fogDensity = p.fogDensity;
            if (skyCopy != null)
            {
                if (p.generatedSky && skyCopy.HasProperty(MainTexId))
                {
                    // towards the moon/sun: the opposite of the way its light travels
                    Vector3 toLight = Quaternion.Euler(p.mainLightElevation, mainRotation.eulerAngles.y, 0f) * Vector3.back;
                    skyCopy.SetTexture(MainTexId, SkyFor(p, toLight));
                    if (skyCopy.HasProperty(TintId)) skyCopy.SetColor(TintId, new Color(0.5f, 0.5f, 0.5f, 1f));   // 0.5 = as painted
                    if (skyCopy.HasProperty(ExposureId)) skyCopy.SetFloat(ExposureId, 1f);
                }
                else
                {
                    if (skyCopy.HasProperty(MainTexId) && skybox != null) skyCopy.SetTexture(MainTexId, skybox.GetTexture(MainTexId));
                    if (skyCopy.HasProperty(TintId)) skyCopy.SetColor(TintId, p.skyTint);
                    if (skyCopy.HasProperty(ExposureId)) skyCopy.SetFloat(ExposureId, p.skyExposure);
                }
                RenderSettings.skybox = skyCopy;
            }
            if (main != null)
            {
                main.color = p.mainLightColor;
                main.intensity = p.mainLightIntensity;
                main.shadowStrength = p.shadowStrength;
                Vector3 e = mainRotation.eulerAngles;   // keep the scene's compass direction, set the height
                main.transform.rotation = Quaternion.Euler(p.mainLightElevation, e.y, 0f);
            }
            foreach (var l in lamps)
            {
                if (l.light == null) continue;
                l.light.intensity = l.intensity * p.lampIntensity;
                l.light.range = l.range * p.lampRange;
                l.light.color = Color.Lerp(l.color, p.lampTint, 0.6f);
            }
            foreach (var w in walls.Values) w.copy.SetFloat(IntensityId, w.intensity * p.zoneWallGlow);
            foreach (var f in fogs.Values) { f.copy.SetFloat(HeightId, p.fogWallHeight); f.copy.SetFloat(ContrastId, p.fogWallBillows); }
            grade.postExposure.Override(p.postExposure);
            grade.contrast.Override(p.contrast);
            balance.temperature.Override(p.temperature);
            grade.saturation.Override(p.saturation);
            volume.weight = 1f;
            DynamicGI.UpdateEnvironment();
        }
    }
}
