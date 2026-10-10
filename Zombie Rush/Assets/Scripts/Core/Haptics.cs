using UnityEngine;

namespace ZombieRush.Core
{
    /// Phone vibration on taking damage (respects the Vibration option).
    /// Self-registering: no scene object needed.
    public static class Haptics
    {
        const float MinInterval = 0.5f;

        static float lastPulseTime = -10f;
        static float lastHealth = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            GameEvents.OnPlayerHealthChanged -= HandleHealthChanged;
            GameEvents.OnPlayerHealthChanged += HandleHealthChanged;
        }

        static void HandleHealthChanged(float current, float max)
        {
            bool tookDamage = lastHealth >= 0f && current < lastHealth;
            lastHealth = current;
            if (tookDamage) Pulse();
        }

        public static void Pulse()
        {
            if (!GameSettings.Vibration) return;
            if (Time.unscaledTime - lastPulseTime < MinInterval) return;
            lastPulseTime = Time.unscaledTime;
#if UNITY_ANDROID || UNITY_IOS
            Handheld.Vibrate();
#endif
        }
    }
}
