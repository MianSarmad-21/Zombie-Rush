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

        void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            foreach (var weapon in weapons)
                weapon.Init(playerCamera, inventory);
        }

        void Start() => RaiseCurrentWeaponChanged();

        void Update()
        {
            if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;
            if (weapons == null || weapons.Length == 0) return;

            for (int i = 0; i < weapons.Length && i < 9; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) && i != currentIndex)
                {
                    currentIndex = i;
                    RaiseCurrentWeaponChanged();
                }
            }

            if (Input.GetMouseButton(0))
                weapons[currentIndex].TryUse();

            if (Input.GetKeyDown(KeyCode.R))
                weapons[currentIndex].TryReload();
        }

        void RaiseCurrentWeaponChanged()
        {
            var weapon = CurrentWeapon;
            string name = weapon != null && weapon.data != null ? weapon.data.weaponName : "None";
            GameEvents.RaiseWeaponChanged(name);
        }

        public WeaponBase CurrentWeapon => weapons != null && weapons.Length > 0 ? weapons[currentIndex] : null;
    }
}
