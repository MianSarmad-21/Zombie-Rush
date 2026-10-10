using System;
using UnityEngine;

namespace ZombieRush.Core
{
    public enum ControlScheme { Auto, Touch, KeyboardMouse }

    /// Picked on the "Select the mode" screen; EnvironmentApplier loads the matching profile.
    public enum TimeOfDay { Night, Day }

    /// Player options, persisted in PlayerPrefs. Values are cached in memory so
    /// gameplay code can read them every frame for free.
    public static class GameSettings
    {
        const string KeyQuality = "zr.quality";
        const string KeyFps = "zr.fps";
        const string KeySensitivity = "zr.sensitivity";
        const string KeyInvertY = "zr.invertY";
        const string KeyControls = "zr.controls";
        const string KeyMaster = "zr.vol.master";
        const string KeySfx = "zr.vol.sfx";
        const string KeyMusic = "zr.vol.music";
        const string KeyVibration = "zr.vibration";
        const string KeyBestWave = "zr.bestWave";
        const string KeyMuted = "zr.muted";
        const string KeyTimeOfDay = "zr.timeOfDay";

        public const float MinSensitivity = 0.1f;
        public const float MaxSensitivity = 5f;

        /// Frame-rate choices; 0 means "VSync / uncapped" (PC only).
        public static int[] FrameRateOptions => Application.isMobilePlatform ? new[] { 30, 60 } : new[] { 30, 60, 120, 0 };

        public static event Action Changed;

        static bool loaded;
        static int quality, fps, bestWave;
        static float sensitivity, master, sfx, music;
        static bool invertY, vibration, muted;
        static TimeOfDay timeOfDay;
        static ControlScheme controls;

        static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            quality = PlayerPrefs.GetInt(KeyQuality, -1); // -1 = keep the project's platform default
            fps = PlayerPrefs.GetInt(KeyFps, 60);
            sensitivity = PlayerPrefs.GetFloat(KeySensitivity, 2f);
            invertY = PlayerPrefs.GetInt(KeyInvertY, 0) == 1;
            controls = (ControlScheme)PlayerPrefs.GetInt(KeyControls, (int)ControlScheme.Auto);
            master = PlayerPrefs.GetFloat(KeyMaster, 1f);
            sfx = PlayerPrefs.GetFloat(KeySfx, 1f);
            music = PlayerPrefs.GetFloat(KeyMusic, 0.7f);
            vibration = PlayerPrefs.GetInt(KeyVibration, 1) == 1;
            bestWave = PlayerPrefs.GetInt(KeyBestWave, 0);
            muted = PlayerPrefs.GetInt(KeyMuted, 0) == 1;
            timeOfDay = (TimeOfDay)PlayerPrefs.GetInt(KeyTimeOfDay, (int)TimeOfDay.Night);
        }

        public static int Quality
        {
            get { EnsureLoaded(); return quality < 0 ? QualitySettings.GetQualityLevel() : quality; }
            set { EnsureLoaded(); quality = Mathf.Clamp(value, 0, QualitySettings.names.Length - 1); PlayerPrefs.SetInt(KeyQuality, quality); ApplyGraphics(); Notify(); }
        }

        public static int TargetFps
        {
            get { EnsureLoaded(); return fps; }
            set { EnsureLoaded(); fps = value; PlayerPrefs.SetInt(KeyFps, fps); ApplyGraphics(); Notify(); }
        }

        public static float LookSensitivity
        {
            get { EnsureLoaded(); return sensitivity; }
            set { EnsureLoaded(); sensitivity = Mathf.Clamp(value, MinSensitivity, MaxSensitivity); PlayerPrefs.SetFloat(KeySensitivity, sensitivity); Notify(); }
        }

        public static bool InvertY
        {
            get { EnsureLoaded(); return invertY; }
            set { EnsureLoaded(); invertY = value; PlayerPrefs.SetInt(KeyInvertY, value ? 1 : 0); Notify(); }
        }

        public static ControlScheme Controls
        {
            get { EnsureLoaded(); return controls; }
            set { EnsureLoaded(); controls = value; PlayerPrefs.SetInt(KeyControls, (int)value); Notify(); }
        }

        public static float MasterVolume
        {
            get { EnsureLoaded(); return master; }
            set { EnsureLoaded(); master = Mathf.Clamp01(value); PlayerPrefs.SetFloat(KeyMaster, master); ApplyVolume(); Notify(); }
        }

        /// Stored for the upcoming audio manager (SFX mixer group).
        public static float SfxVolume
        {
            get { EnsureLoaded(); return sfx; }
            set { EnsureLoaded(); sfx = Mathf.Clamp01(value); PlayerPrefs.SetFloat(KeySfx, sfx); Notify(); }
        }

        /// Stored for the upcoming audio manager (music mixer group).
        public static float MusicVolume
        {
            get { EnsureLoaded(); return music; }
            set { EnsureLoaded(); music = Mathf.Clamp01(value); PlayerPrefs.SetFloat(KeyMusic, music); Notify(); }
        }

        public static bool Vibration
        {
            get { EnsureLoaded(); return vibration; }
            set { EnsureLoaded(); vibration = value; PlayerPrefs.SetInt(KeyVibration, value ? 1 : 0); Notify(); }
        }

        /// Quick sound on/off from the main menu; keeps the Master Volume slider value.
        public static bool Muted
        {
            get { EnsureLoaded(); return muted; }
            set { EnsureLoaded(); muted = value; PlayerPrefs.SetInt(KeyMuted, value ? 1 : 0); ApplyVolume(); Notify(); }
        }

        public static TimeOfDay TimeOfDay
        {
            get { EnsureLoaded(); return timeOfDay; }
            set { EnsureLoaded(); timeOfDay = value; PlayerPrefs.SetInt(KeyTimeOfDay, (int)value); Notify(); }
        }

        static void ApplyVolume() => AudioListener.volume = muted ? 0f : master;

        public static int BestWave
        {
            get { EnsureLoaded(); return bestWave; }
            set { EnsureLoaded(); if (value <= bestWave) return; bestWave = value; PlayerPrefs.SetInt(KeyBestWave, bestWave); }
        }

        /// True when the on-screen touch controls should be used instead of mouse look/fire.
        public static bool UseTouchControls
        {
            get
            {
                EnsureLoaded();
                return controls == ControlScheme.Touch || (controls == ControlScheme.Auto && Application.isMobilePlatform);
            }
        }

        public static void ApplyAll()
        {
            EnsureLoaded();
            ApplyGraphics();
            ApplyVolume();
        }

        static void ApplyGraphics()
        {
            if (quality >= 0 && quality != QualitySettings.GetQualityLevel())
                QualitySettings.SetQualityLevel(quality, true);

            if (fps <= 0)
            {
                QualitySettings.vSyncCount = 1;
                Application.targetFrameRate = -1;
            }
            else
            {
                // targetFrameRate is ignored while VSync is on, so turn it off to honour the cap.
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = fps;
            }
        }

        /// Flushes PlayerPrefs to disk; call when leaving an options screen, not per change.
        public static void Save() => PlayerPrefs.Save();

        static void Notify() => Changed?.Invoke();
    }
}
