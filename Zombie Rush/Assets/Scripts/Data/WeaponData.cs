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
        [Tooltip("Damage times this when the shot hits a zombie's head.")]
        public float headshotMultiplier = 2f;

        [Header("Ammo")]
        public bool unlimitedUses = false;
        public int magazineSize = 12;
        public int maxReserveAmmo = 60;
        public float reloadTime = 1.4f;
        [Tooltip("Reload by itself when the magazine runs dry (if there is spare ammo).")]
        public bool autoReload = true;
        [Tooltip("Seconds after the magazine runs dry before the automatic reload starts (lets the last shot finish).")]
        public float autoReloadDelay = 0.3f;

        [Header("Ammo packs (base tables, zombie drops)")]
        [Tooltip("The ammo box model for this gun (a prefab with an AmmoPickup). Set by Base Furnish.")]
        public GameObject ammoPack;
        [Tooltip("Name in the pick-up message: \"+12 RIFLE AMMO\".")]
        public string ammoName = "";
        [Tooltip("Rounds in an ammo box on a base table.")]
        public int tableAmmo = 60;
        [Tooltip("Rounds in a pack a zombie drops (min, max).")]
        public Vector2Int dropAmmo = new Vector2Int(8, 16);

        public string AmmoLabel => string.IsNullOrEmpty(ammoName) ? weaponName.ToUpperInvariant() : ammoName;

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
        [Tooltip("Aiming looks through a scope: the sight in the middle of the screen, the gun's top showing below it.")]
        public bool hasScope = false;
        [Tooltip("Scope magnification: the field of view is divided by this while scoped.")]
        public float scopeZoom = 2f;
        [Tooltip("Looking through the scope: the gun is lined up straight along the view, centred under the eye, its middle this far below the eye, metres (the scope sits high over the gun): its top shows below the sight.")]
        public float scopeEyeHeight = 0.075f;
        [Tooltip("...and this far further back, metres.")]
        public float scopeEyeBack = 0.02f;
        [Tooltip("Aiming raises the gun to the eye and looks down its own iron sights (no scope picture): the gun is " +
                 "lined up along the view, its front sight on the middle of the screen.")]
        public bool ironSights = false;
        [Tooltip("Iron sights: the front sight's height above the bore at the muzzle, metres: it sits on the middle of the screen.")]
        public float sightHeight = 0.02f;
        [Tooltip("Iron sights: the gun this far ahead of the eye, metres (arms' length; the scope's eye-back is not used).")]
        public float sightForward = 0.0f;

        /// Aiming brings the gun to the eye (a scope, or iron sights).
        public bool AimsAtEye => hasScope || ironSights;

        [Header("Recoil (view kick per shot)")]
        [Tooltip("Degrees the view climbs per shot; it settles back between shots.")]
        public float recoilUp = 0.7f;
        [Tooltip("Most degrees the view drifts left or right per shot (random).")]
        public float recoilSide = 0.25f;
        [Tooltip("Kick while aiming / scoped, as a share of the hip kick.")]
        [Range(0f, 1f)] public float recoilAimScale = 0.7f;
        [Tooltip("First-person gun kick per shot: metres back and degrees muzzle-up (on top of the gun's own fire clip).")]
        public float gunKickBack = 0.02f;
        public float gunKickPitch = 2f;
        [Tooltip("Third person: the upper body rocks back this many degrees per shot.")]
        public float bodyKickPitch = 2f;

        [Header("Feel (per shot)")]
        [Tooltip("Camera shake per shot (0-1; it adds up over a burst and settles quickly).")]
        public float shakePerShot = 0.08f;
        [Tooltip("Field of view kicked outward per shot, degrees (springs back at once; less while aiming).")]
        public float fovKick = 0.6f;
        [Tooltip("A tracer flies every this many shots (0 = never).")]
        public int tracerEvery = 2;

        [Header("Steady hold")]
        [Tooltip("Standing still, how much of the idle clip's breathing is kept out (0 = all of it plays, 0.85 = only a small, smooth slice).")]
        [Range(0f, 1f)] public float idleSteady = 0f;

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
