using UnityEngine;
using ZombieRush.Core;
using ZombieRush.Weapons;

namespace ZombieRush.Player
{
    /// Holds the player's equipped weapons and routes fire/switch/reload input to them.
    [RequireComponent(typeof(PlayerInventory))]
    public class PlayerWeaponController : MonoBehaviour
    {
        public static PlayerWeaponController Instance { get; private set; }

        public Camera playerCamera;
        public WeaponBase[] weapons;

        int currentIndex;
        int lastMag = -1, lastReserve = -1;
        bool lastReloading;
        WeaponBase lastWeapon;
        PlayerInventory inventory;

        void Awake()
        {
            Instance = this;

            inventory = GetComponent<PlayerInventory>();
            foreach (var weapon in weapons)
                weapon.Init(playerCamera, inventory);

            UpdateWeaponVisibility();
        }

        void Start()
        {
            RaiseCurrentWeaponChanged();
            RaiseAmmoStatus();
        }

        void Update()
        {
            if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;
            if (weapons == null || weapons.Length == 0) return;

            for (int i = 0; i < weapons.Length && i < 9; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) && i != currentIndex)
                {
                    currentIndex = i;
                    UpdateWeaponVisibility();
                    RaiseCurrentWeaponChanged();
                }
            }

            if (Input.GetMouseButton(0))
                weapons[currentIndex].TryUse();

            if (Input.GetKeyDown(KeyCode.R))
                weapons[currentIndex].TryReload();

            RaiseAmmoStatus();
        }

        void RaiseCurrentWeaponChanged()
        {
            var weapon = CurrentWeapon;
            string name = weapon != null && weapon.data != null ? weapon.data.weaponName : "None";
            GameEvents.RaiseWeaponChanged(name);
        }

        void RaiseAmmoStatus()
        {
            var weapon = CurrentWeapon;
            if (weapon == null || weapon.data == null) return;

            int reserve = inventory != null ? inventory.GetReserveAmmo(weapon.data) : 0;
            int mag = weapon.CurrentMagazine;
            bool reloading = weapon.IsReloading;
            if (weapon == lastWeapon && mag == lastMag && reserve == lastReserve && reloading == lastReloading) return;

            lastWeapon = weapon; lastMag = mag; lastReserve = reserve; lastReloading = reloading;
            GameEvents.RaiseAmmoChanged(mag, weapon.data.magazineSize, reserve, reloading);
        }

        public WeaponBase CurrentWeapon => weapons != null && weapons.Length > 0 ? weapons[currentIndex] : null;

        void UpdateWeaponVisibility()
        {
            if (weapons == null) return;
            for (int i = 0; i < weapons.Length; i++)
            {
                if (weapons[i] != null) weapons[i].gameObject.SetActive(i == currentIndex);
            }
        }

        /// Called by a zombie's ammo drop on death - tops up reserve ammo for
        /// every weapon the player is carrying, not just the equipped one.
        public void AddReserveAmmoToAll(int amount)
        {
            if (inventory == null || weapons == null) return;
            foreach (var weapon in weapons)
            {
                if (weapon != null && weapon.data != null) inventory.AddReserveAmmo(weapon.data, amount);
            }
            RaiseAmmoStatus();
        }
    }
}
