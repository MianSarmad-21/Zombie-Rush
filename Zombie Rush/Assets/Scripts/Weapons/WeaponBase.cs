using UnityEngine;
using ZombieRush.Core;
using ZombieRush.Data;
using ZombieRush.Player;

namespace ZombieRush.Weapons
{
    /// Shared magazine / fire-rate / reload logic. Subclasses only decide what a
    /// single shot does in Fire().
    public abstract class WeaponBase : MonoBehaviour
    {
        public WeaponData data;
        public LayerMask hitMask = ~0;

        protected Camera playerCamera;
        protected PlayerInventory inventory;

        int magazine;
        float nextFireTime;
        bool reloading;
        float reloadEndTime;

        public int CurrentMagazine => magazine;
        public bool IsReloading => reloading;

        public virtual void Init(Camera cam, PlayerInventory inv)
        {
            playerCamera = cam;
            inventory = inv;
            magazine = data != null ? data.magazineSize : 0;
        }

        /// Returns true if the weapon actually fired (had ammo/wasn't on cooldown).
        public bool TryUse()
        {
            if (data == null || reloading || playerCamera == null) return false;
            if (Time.time < nextFireTime) return false;

            if (!data.unlimitedUses && magazine <= 0)
            {
                TryReload();
                return false;
            }

            nextFireTime = Time.time + 1f / Mathf.Max(0.01f, data.fireRate);
            if (!data.unlimitedUses) magazine--;

            Fire();
            return true;
        }

        protected abstract void Fire();

        public void TryReload()
        {
            if (reloading || data == null || data.unlimitedUses) return;
            if (magazine >= data.magazineSize) return;
            if (inventory != null && inventory.GetReserveAmmo(data) <= 0) return; // nothing to load

            reloading = true;
            reloadEndTime = Time.time + data.reloadTime;
        }

        // Timer-based instead of a coroutine: no allocation, and it pauses with timeScale.
        void Update()
        {
            if (!reloading || Time.time < reloadEndTime) return;

            int needed = data.magazineSize - magazine;
            magazine += inventory != null ? inventory.TakeReserveAmmo(data, needed) : needed;
            reloading = false;
        }

        // Switching weapons deactivates this object. Cancel any reload in progress
        // so the gun isn't left stuck in "reloading" forever.
        void OnDisable() => reloading = false;

        /// Hitscan along `direction` from the camera, damaging the first IDamageable hit.
        protected void HitScan(Vector3 direction, float damage)
        {
            var origin = playerCamera.transform.position;
            if (!Physics.Raycast(origin, direction, out var hit, data.range, hitMask, QueryTriggerInteraction.Ignore)) return;

            var damageable = hit.collider.GetComponentInParent<IDamageable>();
            damageable?.TakeDamage(damage);
        }
    }
}
