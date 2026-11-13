using UnityEngine;

namespace ZombieRush.Data
{
    /// Crosshair shape: Lines (rifle: four lines that spread apart), Dot (pistol: a dot with short ticks),
    /// Circle (shotgun: a ring showing the pellet spread).
    public enum CrosshairStyle { Lines, Dot, Circle }

    [CreateAssetMenu(fileName = "NewWeapon", menuName = "Zombie Rush/Weapon Data")]
    public class WeaponData : ScriptableObject
    {
        public string weaponName = "Weapon";
        [Tooltip("HUD icon (white on transparent).")]
        public Sprite icon;
        public float damage = 25f;
        public float fireRate = 8f; // rounds per second
        public float range = 100f;

        [Header("Ammo")]
        public bool unlimitedUses = false;
        public int magazineSize = 12;
        public int maxReserveAmmo = 60;
        public float reloadTime = 1.4f;
        [Tooltip("Reload by itself when the magazine runs dry (if there is spare ammo).")]
        public bool autoReload = true;
        [Tooltip("Seconds after the magazine runs dry before the automatic reload starts (lets the last shot finish).")]
        public float autoReloadDelay = 0.3f;

        [Header("Crosshair and spread")]
        public CrosshairStyle crosshair = CrosshairStyle.Lines;
        [Tooltip("Shot spread at rest, degrees from the crosshair (0 = dead straight). Shotguns use their pellet cone instead.")]
        public float spreadBase = 0.3f;
        [Tooltip("Spread each shot adds, degrees (it settles back between shots).")]
        public float spreadPerShot = 0.5f;
        [Tooltip("Most the spread can grow to from firing, degrees.")]
        public float spreadMax = 4f;
        [Tooltip("How fast the spread settles back, degrees per second.")]
        public float spreadRecovery = 6f;
        [Tooltip("Extra spread at full running speed, degrees.")]
        public float spreadMoving = 1.5f;

        [Header("Aiming (right mouse / aim button)")]
        [Tooltip("Spread while aiming, as a share of the normal spread.")]
        [Range(0f, 1f)] public float aimSpreadScale = 0.4f;
        [Tooltip("Field of view while aiming without a scope, as a share of normal (0.85 = a slight zoom).")]
        [Range(0.3f, 1f)] public float aimZoom = 0.85f;
        [Tooltip("Aiming looks through a scope: a full-screen sight, the gun and arms out of view.")]
        public bool hasScope = false;
        [Tooltip("Scope magnification: the field of view is divided by this while scoped.")]
        public float scopeZoom = 2f;

        [Header("Audio (empty = the Sound Library defaults)")]
        public AudioClip[] fireSounds;
        public AudioClip reloadSound;
        [Range(0f, 1f)] public float fireVolume = 0.8f;

        [Header("First person (guns with body clips)")]
        [Tooltip("Draw this gun's first-person arms and gun from their own camera spot instead of WeaponHoldConfig.eyeOffset (the world camera stays at the eyes).")]
        public bool overrideEyeOffset = false;
        [Tooltip("Arms-and-gun camera spot from the steady head point, metres: x = right, y = up, z = forward along the aim (negative = behind the eyes).")]
        public Vector3 eyeOffset = new Vector3(0f, 0.09f, 0.09f);
        [Tooltip("First person only: the shoulders (and so the whole hold: arms, hands, gun) move by this much, metres along the aim: x = right, y = up, z = forward. Brings a naturally low hold up into view; the body is not seen in first person.")]
        public Vector3 fpLift = Vector3.zero;
    }
}
