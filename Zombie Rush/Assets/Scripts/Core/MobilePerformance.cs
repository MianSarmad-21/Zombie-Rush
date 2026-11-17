using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ZombieRush.Core
{
    /// Phones only (PC keeps the full look): runs on every level load and trims what costs the most on a
    /// mobile GPU without changing the game -
    ///   - only the nearest few scene lights (lanterns, fires, lamps) stay on (MobileLightCuller);
    ///   - small foliage casts no shadows and half the grass/weed tufts are left out;
    ///   - the view ends at 250 m (the fog hides it) and buildings switch to their simpler LOD sooner;
    ///   - bloom works on a quarter-size image.
    /// The URP mobile asset itself (Assets/Settings/Mobile_RPAsset: HDR off, 70 % render scale, 25 m shadows,
    /// Forward lighting) does the rest. ForceInEditor tries it in the editor (captures).
    public static class MobilePerformance
    {
        public static bool ForceInEditor;
        public static bool Active => Application.isMobilePlatform || ForceInEditor;

        public const float FarClip = 250f;
        public const float LodBias = 0.7f;
        static readonly string[] NoShadow = { "ZR_WildGrass", "ZR_DryWeeds", "ZR_IvyCard", "ZR_LeafyBush", "ZR_DeadShrub", "ZR_TireStack" };
        static readonly string[] Thin = { "ZR_WildGrass", "ZR_DryWeeds" };

        static bool hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { hooked = false; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!hooked)
            {
                hooked = true;
                UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, __) => { if (Active) Apply(); };
            }
            if (Active) Apply();
        }

        /// Returns a short report (for captures).
        public static string Apply()
        {
            QualitySettings.lodBias = Mathf.Min(QualitySettings.lodBias, LodBias);

            int cams = 0;
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (cam.farClipPlane > FarClip && cam.CompareTag("MainCamera")) { cam.farClipPlane = FarClip; cams++; }

            int noShadow = 0, thinned = 0;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                string n = r.gameObject.name;
                if (StartsWithAny(n, NoShadow) && r.shadowCastingMode != ShadowCastingMode.Off) { r.shadowCastingMode = ShadowCastingMode.Off; noShadow++; }
                if (StartsWithAny(n, Thin) && r.enabled)
                {
                    // every other tuft, picked by position so it is the same on every phone and every load
                    Vector3 p = r.transform.position;
                    if (((Mathf.FloorToInt(p.x * 3.1f) ^ Mathf.FloorToInt(p.z * 2.7f)) & 1) == 0) { r.enabled = false; thinned++; }
                }
            }

            int blooms = 0;
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (v.sharedProfile == null) continue;
                var prof = v.profile;   // a runtime copy: the project's profile asset is never changed
                if (prof.TryGet<Bloom>(out var bloom))
                {
                    bloom.highQualityFiltering.Override(false);
                    bloom.downscale.Override(BloomDownscaleMode.Quarter);
                    bloom.maxIterations.Override(4);
                    blooms++;
                }
            }

            if (Object.FindAnyObjectByType<MobileLightCuller>() == null)
                new GameObject("MobileLightCuller").AddComponent<MobileLightCuller>();

            return $"mobile perf: far clip {FarClip} on {cams} camera(s), lod bias {QualitySettings.lodBias:0.0}, {noShadow} foliage pieces without shadows, {thinned} grass tufts left out, {blooms} bloom(s) at quarter size, light culler on";
        }

        static bool StartsWithAny(string s, string[] prefixes)
        {
            foreach (var p in prefixes) if (s.StartsWith(p)) return true;
            return false;
        }
    }

    /// Keeps only the nearest `maxLights` scene point/spot lights switched on (within `range` of the camera),
    /// re-checked a few times a second. Lights that belong to players, zombies, turrets or guns (muzzle
    /// flashes) and lights made later are never touched. Each light shades every pixel it reaches, so on a
    /// phone 60 lanterns/fires/lamps cost far more than the 8 you can actually see nearby.
    public class MobileLightCuller : MonoBehaviour
    {
        public int maxLights = 8;
        public float range = 45f;
        public float interval = 0.25f;

        readonly List<Light> lights = new List<Light>();
        readonly List<(float d, Light l)> order = new List<(float, Light)>();
        float next;

        void Start()
        {
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type != LightType.Point && l.type != LightType.Spot) continue;
                if (!l.enabled) continue;
                if (l.GetComponentInParent<Player.PlayerHealth>() != null) continue;
                if (l.GetComponentInParent<Zombie.ZombieHealth>() != null) continue;
                if (l.GetComponentInParent<Weapons.Turret>() != null) continue;
                if (l.GetComponentInParent<Weapons.WeaponBase>() != null) continue;
                lights.Add(l);
            }
        }

        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + interval;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 eye = cam.transform.position;
            order.Clear();
            for (int i = lights.Count - 1; i >= 0; i--)
            {
                var l = lights[i];
                if (l == null) { lights.RemoveAt(i); continue; }
                // distance to the edge of the light's reach: a big floodlight further off still counts
                float d = Vector3.Distance(eye, l.transform.position) - l.range * 0.5f;
                order.Add((d, l));
            }
            order.Sort((a, b) => a.d.CompareTo(b.d));
            for (int i = 0; i < order.Count; i++)
            {
                bool on = i < maxLights && order[i].d < range;
                if (order[i].l.enabled != on) order[i].l.enabled = on;
            }
        }

        public int LitCount { get { int n = 0; foreach (var l in lights) if (l != null && l.enabled) n++; return n; } }
        public int Managed => lights.Count;
    }
}
