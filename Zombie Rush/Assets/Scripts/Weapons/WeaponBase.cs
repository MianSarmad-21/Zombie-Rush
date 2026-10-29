using UnityEngine;
using ZombieRush.Audio;
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

        /// Raised after every shot (muzzle flash, recoil - see WeaponViewModel).
        public event System.Action Fired;
        /// Raised when a reload starts.
        public event System.Action ReloadStarted;

        int magazine;
        float nextFireTime;
        float nextEmptyClick;
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
                if (!reloading && Time.time >= nextEmptyClick)
                {
                    // Out of ammo completely: dry click, but not every frame while the trigger is held.
                    nextEmptyClick = Time.time + 0.35f;
                    SoundManager.Play(SoundManager.Library != null ? SoundManager.Library.emptyClick : null, 0.7f);
                }
                return false;
            }

            nextFireTime = Time.time + 1f / Mathf.Max(0.01f, data.fireRate);
            if (!data.unlimitedUses) magazine--;

            Fire();
            var lib = SoundManager.Library;
            var shot = data.fireSounds != null && data.fireSounds.Length > 0 ? data.fireSounds : lib != null ? lib.gunshot : null;
            SoundManager.Play(shot, data.fireVolume, 0.05f);
            Fired?.Invoke();
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
            var lib = SoundManager.Library;
            SoundManager.Play(data.reloadSound != null ? data.reloadSound : lib != null ? lib.reload : null, 0.8f, 0f);
            ReloadStarted?.Invoke();
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
