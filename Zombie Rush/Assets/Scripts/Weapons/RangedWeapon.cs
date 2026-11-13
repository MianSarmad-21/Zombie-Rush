namespace ZombieRush.Weapons
{
    /// Single-ray hitscan gun (pistol, rifle): the shot lands within the current spread of the crosshair.
    public class RangedWeapon : WeaponBase
    {
        protected override void Fire() => HitScan(SpreadDirection(SpreadDegrees), data.damage * ZombieRush.Core.WaveScaling.PlayerDamage);
    }
}
