using UnityEngine;

namespace ZombieRush.Core
{
    /// Platform-appropriate frame pacing, applied automatically at startup
    /// (no scene object needed): 60 FPS cap on mobile to save battery/heat.
    public static class PerformanceSettings
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
            if (Application.isMobilePlatform)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 60;
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
            }
        }
    }
}
