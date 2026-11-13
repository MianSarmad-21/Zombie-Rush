using UnityEngine;

namespace ZombieRush.Weapons
{
    /// Fires several pellets in a spread cone per shot instead of one hitscan
    /// ray - shorter effective range, heavier close-up damage than the pistol.
    /// Damage per pellet comes from WeaponData.damage, grown by the wave (WaveScaling.PlayerDamage).
    public class ShotgunWeapon : WeaponBase
    {
        public int pelletCount = 8;
        public float spreadDegrees = 6f;

        public override GunStyle Style => GunStyle.Shotgun;

        /// The pellet cone (the crosshair circle), a little tighter while aiming.
        public override float SpreadDegrees => spreadDegrees * (data != null ? Mathf.Lerp(1f, data.aimSpreadScale, AimAmount) : 1f);

        protected override void Fire()
        {
            float cone = SpreadDegrees;
            for (int i = 0; i < pelletCount; i++)
                HitScan(SpreadDirection(cone), data.damage * ZombieRush.Core.WaveScaling.PlayerDamage);   // each pellet somewhere in the cone
        }
    }
}
