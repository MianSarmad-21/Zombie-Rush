using UnityEngine;

namespace ZombieRush.Weapons
{
    /// Fires several pellets in a spread cone per shot instead of one hitscan
    /// ray - shorter effective range, heavier close-up damage than the pistol.
    /// Damage per pellet comes from WeaponData.damage.
    public class ShotgunWeapon : WeaponBase
    {
        public int pelletCount = 8;
        public float spreadDegrees = 6f;

        protected override void Fire()
        {
            var aim = playerCamera.transform.rotation;
            for (int i = 0; i < pelletCount; i++)
            {
                // Random point in a circular cone around the aim direction.
                Vector2 offset = Random.insideUnitCircle * spreadDegrees;
                Vector3 dir = aim * Quaternion.Euler(offset.y, offset.x, 0f) * Vector3.forward;
                HitScan(dir, data.damage);
            }
        }
    }
}
