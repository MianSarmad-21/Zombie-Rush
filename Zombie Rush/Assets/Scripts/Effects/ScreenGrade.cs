using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ZombieRush.Core;
using ZombieRush.Data;

namespace ZombieRush.Effects
{
    /// Two post-processing layers made at runtime on top of the level's own grade (the scene and its
    /// profiles are never changed):
    ///   - always on: greens muted (the grass read as flat neon), everything else as the level grades it;
    ///   - low health: the colour drains, a dark red vignette closes in and all sound gets muffled, deeper
    ///     the lower the health goes, with a slow heartbeat pulse.
    /// Both are colour-grading settings, baked into URP's colour lookup: next to no cost on phones.
    public class ScreenGrade : MonoBehaviour
    {
        static ScreenGrade instance;

        Volume lowVolume;
        ColorAdjustments lowColor;
        Vignette lowVignette;
        AudioLowPassFilter muffle;
        float health01 = 1f, shown, pulse;
        bool dead;

        /// Makes the layers for the loaded level (called on every level load).
        public static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject("ScreenGrade");
            instance = go.AddComponent<ScreenGrade>();
        }

        void Awake()
        {
            var cfg = FeelConfig.Current;

            // always-on: mute the greens
            var grade = ScriptableObject.CreateInstance<VolumeProfile>();
            var curves = grade.Add<ColorCurves>(true);
            float g = cfg.greenSaturation;
            curves.hueVsSat.Override(new TextureCurve(new[]
            {
                new Keyframe(0.12f, 0.5f), new Keyframe(0.2f, Mathf.Lerp(0.5f, g, 0.6f)), new Keyframe(0.3f, g),
                new Keyframe(0.4f, Mathf.Lerp(0.5f, g, 0.5f)), new Keyframe(0.5f, 0.5f),
            }, 0.5f, true, new Vector2(0f, 1f)));
            MakeVolume("Grade", grade, 40f, 1f);

            // low health: off (weight 0) until health drops, so it never fights the level's own values
            var low = ScriptableObject.CreateInstance<VolumeProfile>();
            lowColor = low.Add<ColorAdjustments>(true);
            lowColor.saturation.Override(cfg.lowHealthSaturation);
            lowColor.contrast.Override(12f);
            if (cfg.lowHealthVignette > 0f)   // off by default: the HUD already has a blood vignette
            {
                lowVignette = low.Add<Vignette>(true);
                lowVignette.color.Override(cfg.lowHealthVignetteColor);
                lowVignette.intensity.Override(cfg.lowHealthVignette);
                lowVignette.smoothness.Override(0.55f);
            }
            lowVolume = MakeVolume("LowHealth", low, 45f, 0f);

            GameEvents.OnPlayerHealthChanged += HandleHealth;
            GameEvents.OnPlayerDied += HandleDied;
        }

        void OnDestroy()
        {
            GameEvents.OnPlayerHealthChanged -= HandleHealth;
            GameEvents.OnPlayerDied -= HandleDied;
            if (muffle != null) { muffle.cutoffFrequency = 22000f; muffle.enabled = false; }
            if (instance == this) instance = null;
        }

        Volume MakeVolume(string name, VolumeProfile profile, float priority, float weight)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = priority;
            v.weight = weight;
            v.sharedProfile = profile;
            return v;
        }

        void HandleHealth(float current, float max)
        {
            health01 = max > 0f ? Mathf.Clamp01(current / max) : 1f;
            if (current > 0f) dead = false;
        }

        void HandleDied() => dead = true;

        void Update()
        {
            var cfg = FeelConfig.Current;
            float target = dead ? 0.6f : health01 >= cfg.lowHealthBelow ? 0f : 1f - health01 / Mathf.Max(0.01f, cfg.lowHealthBelow);
            shown = Mathf.MoveTowards(shown, target, Time.unscaledDeltaTime * 1.5f);
            pulse += Time.unscaledDeltaTime * Mathf.Lerp(3f, 7f, shown);   // faster as it gets worse
            float beat = shown > 0f ? 0.88f + 0.12f * Mathf.Sin(pulse) : 1f;
            lowVolume.weight = shown * beat;

            if (muffle == null)
            {
                var listener = FindAnyObjectByType<AudioListener>();
                if (listener != null && !listener.TryGetComponent(out muffle)) muffle = listener.gameObject.AddComponent<AudioLowPassFilter>();
            }
            if (muffle != null)
            {
                float cutoff = Mathf.Lerp(22000f, cfg.lowHealthCutoff, shown * shown);
                muffle.cutoffFrequency = cutoff;
                muffle.enabled = cutoff < 21000f;   // no filtering cost while healthy
            }
        }
    }
}
