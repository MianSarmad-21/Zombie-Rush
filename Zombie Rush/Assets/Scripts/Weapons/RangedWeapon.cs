using System.Collections;
using UnityEngine;
using ZombieRush.Core;
using ZombieRush.Player;

namespace ZombieRush.Weapons
{
    /// Simple hitscan gun: raycast from the camera, apply damage to whatever has a
    /// ZombieHealth-like component (kept generic via SendMessage-free direct check).
    public class RangedWeapon : WeaponBase
    {
        public Transform muzzle;
        public LayerMask hitMask = ~0;

        int magazine;
        float nextFireTime;
        bool reloading;

        public override int CurrentMagazine => magazine;
        public override bool IsReloading => reloading;

        public override void Init(Camera cam, PlayerInventory inv)
        {
            base.Init(cam, inv);
            magazine = data != null ? data.magazineSize : 0;
        }

        public override bool TryUse()
        {
            if (data == null || reloading) return false;
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

        void Fire()
        {
            if (playerCamera == null) return;

            if (Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward, out var hit, data.range, hitMask, QueryTriggerInteraction.Ignore))
            {
                var damageable = hit.collider.GetComponentInParent<IDamageable>();
                damageable?.TakeDamage(data.damage);
            }
        }

        public override void TryReload()
        {
            if (reloading || data == null || data.unlimitedUses) return;
            if (magazine >= data.magazineSize) return;
            StartCoroutine(ReloadRoutine());
        }

        IEnumerator ReloadRoutine()
        {
            reloading = true;
            yield return new WaitForSeconds(data.reloadTime);

            int needed = data.magazineSize - magazine;
            int taken = inventory != null ? inventory.TakeReserveAmmo(data, needed) : needed;
            magazine += taken;

            reloading = false;
        }
    }
}
