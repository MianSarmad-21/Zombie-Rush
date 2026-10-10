using System.Collections.Generic;
using UnityEngine;
using ZombieRush.Data;

namespace ZombieRush.Player
{
    /// Tracks reserve (non-chambered) ammo per weapon. Each weapon keeps its own
    /// magazine count and pulls from here on reload.
    public class PlayerInventory : MonoBehaviour
    {
        public event System.Action<WeaponData, int> OnReserveAmmoChanged;

        readonly Dictionary<WeaponData, int> reserveAmmo = new Dictionary<WeaponData, int>();

        public int GetReserveAmmo(WeaponData weapon)
        {
            if (weapon == null) return 0;
            return reserveAmmo.TryGetValue(weapon, out int count) ? count : weapon.maxReserveAmmo;
        }

        public void AddReserveAmmo(WeaponData weapon, int amount)
        {
            if (weapon == null || amount <= 0) return;
            int current = GetReserveAmmo(weapon);
            int updated = Mathf.Min(weapon.maxReserveAmmo, current + amount);
            reserveAmmo[weapon] = updated;
            if (updated != current) OnReserveAmmoChanged?.Invoke(weapon, updated);
        }

        /// Takes up to `requested` rounds from reserve; returns how many were actually available.
        public int TakeReserveAmmo(WeaponData weapon, int requested)
        {
            if (weapon == null || requested <= 0) return 0;
            int current = GetReserveAmmo(weapon);
            int taken = Mathf.Min(current, requested);
            reserveAmmo[weapon] = current - taken;
            if (taken > 0) OnReserveAmmoChanged?.Invoke(weapon, current - taken);
            return taken;
        }
    }
}
