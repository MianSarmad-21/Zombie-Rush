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
        public static event Action<int, int, int, bool> OnAmmoChanged; // magazine, magazineSize, reserve, isReloading
        public static event Action<int> OnCoinsChanged; // total coins

        public static void RaisePlayerHealthChanged(float current, float max) => OnPlayerHealthChanged?.Invoke(current, max);
        public static void RaisePlayerDied() => OnPlayerDied?.Invoke();
        public static void RaiseWeaponChanged(string weaponName) => OnWeaponChanged?.Invoke(weaponName);
        public static void RaiseAmmoChanged(int magazine, int magazineSize, int reserve, bool isReloading) => OnAmmoChanged?.Invoke(magazine, magazineSize, reserve, isReloading);
        public static void RaiseCoinsChanged(int total) => OnCoinsChanged?.Invoke(total);
    }
}
