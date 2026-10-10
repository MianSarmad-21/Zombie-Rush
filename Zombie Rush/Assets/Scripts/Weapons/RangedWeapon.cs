namespace ZombieRush.Weapons
{
    /// Single-ray hitscan gun (the pistol): what's under the crosshair gets hit.
    public class RangedWeapon : WeaponBase
    {
        protected override void Fire() => HitScan(playerCamera.transform.forward, data.damage);
    }
}
