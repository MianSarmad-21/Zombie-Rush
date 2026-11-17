using UnityEngine;

namespace ZombieRush.Core
{
    /// Applies saved graphics/audio settings and platform frame pacing at startup
    /// (no scene object needed).
    public static class PerformanceSettings
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
            if (Application.isMobilePlatform)
            {
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
                // landscape only, either way round (turns with the phone; never portrait)
                Screen.autorotateToPortrait = false;
                Screen.autorotateToPortraitUpsideDown = false;
                Screen.autorotateToLandscapeLeft = true;
                Screen.autorotateToLandscapeRight = true;
                Screen.orientation = ScreenOrientation.AutoRotation;
            }

            GameSettings.ApplyAll();
        }
    }
}
