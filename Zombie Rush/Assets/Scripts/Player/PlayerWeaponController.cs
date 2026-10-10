using UnityEngine;
using ZombieRush.Core;
using ZombieRush.Data;
using ZombieRush.Weapons;

namespace ZombieRush.Player
{
    /// Holds the player's equipped weapons and routes fire/switch/reload input to them.
    [RequireComponent(typeof(PlayerInventory))]
    public class PlayerWeaponController : MonoBehaviour
    {
        public Camera playerCamera;
        public WeaponBase[] weapons;
        [Tooltip("How many weapons (from the top of the list) the player starts with. The rest are picked up from the racks in the safe base.")]
        public int startingWeapons = 1;

        int currentIndex;
        bool[] unlocked;
        PlayerInventory inventory;

        // Last values pushed to the HUD, so the event only fires on real changes.
        WeaponBase lastWeapon;
        int lastMag = -1, lastReserve = -1;
        bool lastReloading;

        public WeaponBase CurrentWeapon => weapons != null && weapons.Length > 0 ? weapons[currentIndex] : null;

        void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            if (weapons == null) weapons = new WeaponBase[0];
            unlocked = new bool[weapons.Length];
            for (int i = 0; i < weapons.Length; i++) unlocked[i] = i < Mathf.Max(1, startingWeapons);

            foreach (var weapon in weapons)
                if (weapon != null) weapon.Init(playerCamera, inventory);

            UpdateWeaponVisibility();
        }

        void OnEnable() => GameEvents.OnZombieKilled += HandleZombieKilled;
        void OnDisable() => GameEvents.OnZombieKilled -= HandleZombieKilled;

        void Start() => RaiseAmmoStatus(force: true);

        void Update()
        {
            if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;
            if (weapons.Length == 0) return;

            int slot = GameInput.WeaponSlotPressed(weapons.Length);
            if (slot >= 0) SelectWeapon(slot);
            if (GameInput.ConsumeSwapWeapon()) SelectWeapon(NextUnlocked(currentIndex));

            var weapon = CurrentWeapon;
            if (weapon != null)
            {
                if (GameInput.FireHeld) weapon.TryUse();
                if (GameInput.ConsumeReload()) weapon.TryReload();
            }

            RaiseAmmoStatus(force: false);
        }

        public void SelectWeapon(int index)
        {
            if (index < 0 || index >= weapons.Length || index == currentIndex || !unlocked[index]) return;
            currentIndex = index;
            UpdateWeaponVisibility();
            RaiseAmmoStatus(force: true);
        }

        int NextUnlocked(int from)
        {
            for (int step = 1; step <= weapons.Length; step++)
            {
                int i = (from + step) % weapons.Length;
                if (unlocked[i]) return i;
            }
            return from;
        }

        public bool IsUnlocked(WeaponData data) => IndexOf(data) is int i && i >= 0 && unlocked[i];

        /// Gives the player this weapon (from a base rack) and equips it. False if they already had it.
        public bool Unlock(WeaponData data)
        {
            int i = IndexOf(data);
            if (i < 0 || unlocked[i]) return false;
            unlocked[i] = true;
            SelectWeapon(i);
            return true;
        }

        /// Tops reserve ammo up to the max for every weapon the player has. False if all were already full.
        public bool RefillAmmo()
        {
            bool changed = false;
            for (int i = 0; i < weapons.Length; i++)
            {
                var data = weapons[i] != null ? weapons[i].data : null;
                if (!unlocked[i] || data == null || data.unlimitedUses) continue;
                int before = inventory.GetReserveAmmo(data);
                inventory.AddReserveAmmo(data, data.maxReserveAmmo);
                changed |= inventory.GetReserveAmmo(data) != before;
            }
            RaiseAmmoStatus(force: true);
            return changed;
        }

        int IndexOf(WeaponData data)
        {
            for (int i = 0; i < weapons.Length; i++)
                if (weapons[i] != null && weapons[i].data == data) return i;
            return -1;
        }

        void RaiseAmmoStatus(bool force)
        {
            var weapon = CurrentWeapon;
            if (weapon == null || weapon.data == null) return;

            int reserve = inventory.GetReserveAmmo(weapon.data);
            int mag = weapon.CurrentMagazine;
            bool reloading = weapon.IsReloading;
            if (!force && weapon == lastWeapon && mag == lastMag && reserve == lastReserve && reloading == lastReloading) return;

            lastWeapon = weapon; lastMag = mag; lastReserve = reserve; lastReloading = reloading;
            GameEvents.RaiseAmmoChanged(new AmmoInfo
            {
                WeaponName = weapon.data.weaponName,
                Icon = weapon.data.icon,
                Magazine = mag,
                MagazineSize = weapon.data.magazineSize,
                Reserve = reserve,
                IsReloading = reloading,
                ReloadDuration = weapon.data.reloadTime,
                Unlimited = weapon.data.unlimitedUses,
            });
        }

        void UpdateWeaponVisibility()
        {
            for (int i = 0; i < weapons.Length; i++)
                if (weapons[i] != null) weapons[i].gameObject.SetActive(i == currentIndex);
        }

        /// A zombie's ammo drop tops up reserve ammo for every weapon the player
        /// is carrying, not just the equipped one.
        void HandleZombieKilled(ZombieKilledInfo info)
        {
            if (info.Ammo <= 0) return;
            foreach (var weapon in weapons)
                if (weapon != null && weapon.data != null) inventory.AddReserveAmmo(weapon.data, info.Ammo);
            RaiseAmmoStatus(force: false);
        }
    }
}
