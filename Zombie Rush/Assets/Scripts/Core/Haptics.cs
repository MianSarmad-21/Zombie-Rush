using UnityEngine;

namespace ZombieRush.Core
{
    /// Phone vibration (respects the Vibration option). Tap() gives short, light pulses on Android (shots,
    /// hits, kills), through the system vibrator, since Handheld.Vibrate is always a long, heavy buzz.
    /// Pulse() is the heavy buzz, kept as the fallback (and it makes the build ask for the vibrate permission).
    public static class Haptics
    {
        const float MinPulseInterval = 0.5f;
        const float MinTapInterval = 0.04f;

        static float lastPulseTime = -10f, lastTapTime = -10f;

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject vibrator;
        static AndroidJavaClass effectClass;
        static bool javaReady, javaFailed;
        static int sdk;

        static bool InitJava()
        {
            if (javaReady) return true;
            if (javaFailed) return false;
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                using (var version = new AndroidJavaClass("android.os.Build$VERSION")) sdk = version.GetStatic<int>("SDK_INT");
                if (sdk >= 26) effectClass = new AndroidJavaClass("android.os.VibrationEffect");
                javaReady = vibrator != null;
                javaFailed = !javaReady;
            }
            catch (System.Exception e) { javaFailed = true; Debug.LogWarning("[Zombie Rush] short vibration unavailable: " + e.Message); }
            return javaReady;
        }
#endif

        /// A short tap: `ms` milliseconds at `strength` 1-255 (Android 8+; older phones ignore the strength).
        public static void Tap(int ms, int strength)
        {
            if (!GameSettings.Vibration || ms <= 0) return;
            if (Time.unscaledTime - lastTapTime < MinTapInterval) return;
            lastTapTime = Time.unscaledTime;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!InitJava()) return;
            try
            {
                if (sdk >= 26)
                    using (var effect = effectClass.CallStatic<AndroidJavaObject>("createOneShot", (long)ms, Mathf.Clamp(strength, 1, 255)))
                        vibrator.Call("vibrate", effect);
                else vibrator.Call("vibrate", (long)ms);
            }
            catch (System.Exception) { javaFailed = true; javaReady = false; }
#endif
        }

        /// Tap with (milliseconds, strength) from a config.
        public static void Tap(Vector2Int msStrength) => Tap(msStrength.x, msStrength.y);

        /// A heavy buzz (the lock of a loot chest snapping).
        public static void Pulse()
        {
            if (!GameSettings.Vibration) return;
            if (Time.unscaledTime - lastPulseTime < MinPulseInterval) return;
            lastPulseTime = Time.unscaledTime;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (InitJava()) { Tap(45, 200); return; }
#endif
#if UNITY_ANDROID || UNITY_IOS
            Handheld.Vibrate();
#endif
        }
    }
}
