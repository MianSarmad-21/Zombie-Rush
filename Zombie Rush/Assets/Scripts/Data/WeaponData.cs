using UnityEngine;

namespace ZombieRush.Data
{
    [CreateAssetMenu(fileName = "NewWeapon", menuName = "Zombie Rush/Weapon Data")]
    public class WeaponData : ScriptableObject
    {
        public string weaponName = "Weapon";
        public float damage = 25f;
        public float fireRate = 8f; // rounds per second
        public float range = 100f;

        [Header("Ammo")]
        public bool unlimitedUses = false;
        public int magazineSize = 12;
        public int maxReserveAmmo = 60;
        public float reloadTime = 1.4f;
    }
}
