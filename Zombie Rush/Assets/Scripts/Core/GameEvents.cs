using System;

namespace ZombieRush.Core
{
    /// Central event bus. UI and gameplay systems subscribe here instead of
    /// referencing each other directly.
    public static class GameEvents
    {
        public static event Action<float, float> OnPlayerHealthChanged; // current, max
        public static event Action OnPlayerDied;
        public static event Action<string> OnWeaponChanged; // display name of the newly equipped weapon

        public static void RaisePlayerHealthChanged(float current, float max) => OnPlayerHealthChanged?.Invoke(current, max);
        public static void RaisePlayerDied() => OnPlayerDied?.Invoke();
        public static void RaiseWeaponChanged(string weaponName) => OnWeaponChanged?.Invoke(weaponName);
    }
}
