using UnityEngine;
using ZombieRush.Data;
using ZombieRush.Player;

namespace ZombieRush.Weapons
{
    public abstract class WeaponBase : MonoBehaviour
    {
        public WeaponData data;

        protected Camera playerCamera;
        protected PlayerInventory inventory;

        public virtual void Init(Camera cam, PlayerInventory inv)
        {
            playerCamera = cam;
            inventory = inv;
        }

        /// Returns true if the weapon actually fired (had ammo/wasn't on cooldown).
        public abstract bool TryUse();

        public virtual void TryReload() { }

        public virtual int CurrentMagazine => 0;
        public virtual bool IsReloading => false;
    }
}
