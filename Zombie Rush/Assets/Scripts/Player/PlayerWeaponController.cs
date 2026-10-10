using UnityEngine;
using ZombieRush.Core;
using ZombieRush.Weapons;

namespace ZombieRush.Player
{
    /// Holds the player's equipped weapons and routes fire/switch/reload input to them.
    [RequireComponent(typeof(PlayerInventory))]
    public class PlayerWeaponController : MonoBehaviour
    {
        public Camera playerCamera;
        public WeaponBase[] weapons;

        int currentIndex;
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
            if (GameInput.ConsumeSwapWeapon()) SelectWeapon((currentIndex + 1) % weapons.Length);

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
            if (index < 0 || index >= weapons.Length || index == currentIndex) return;
            currentIndex = index;
            UpdateWeaponVisibility();
            RaiseAmmoStatus(force: true);
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
