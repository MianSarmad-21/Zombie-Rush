using UnityEngine;

namespace ZombieRush.Data
{
    /// Tuning for the frag grenade (GrenadeThrower, GrenadeProjectile, GrenadePickup, the chest roll).
    /// Lives in Resources (ZombieRush/Grenade/GrenadeConfig) so every machine and the loot chests find it.
    [CreateAssetMenu(fileName = "GrenadeConfig", menuName = "Zombie Rush/Grenade Config")]
    public class GrenadeConfig : ScriptableObject
    {
        public const string ResourcePath = "ZombieRush/Grenade/GrenadeConfig";
        static GrenadeConfig cached;
        public static GrenadeConfig Get() => cached != null ? cached : (cached = Resources.Load<GrenadeConfig>(ResourcePath));

        [Header("Carrying")]
        public int maxCarry = 3;
        public int startCount = 0;

        [Header("Fuse and cooking")]
        [Tooltip("Seconds from pulling the pin to the blast. Holding the button cooks it: the fuse runs while you hold.")]
        public float fuse = 3.5f;
        [Tooltip("Cooked too long, it goes off in your hand (as in COD). Off: it is thrown by itself when the fuse is nearly out.")]
        public bool explodeInHand = true;
        [Tooltip("Seconds from the press before a throw can start (also never before the arm is cocked: cookRaise).")]
        public float minHold = 0.25f;

        [Header("Fuse timer")]
        [Tooltip("The countdown ring and seconds at the centre of the screen while cooking (the crosshair hides).")]
        public bool centerTimer = true;
        [Tooltip("Seconds between fuse ticks while cooking (0 = no ticks).")]
        public float tickEvery = 0.5f;
        [Tooltip("In the last this-many seconds the ticks speed up to tickFastEvery and the timer turns red and pulses.")]
        public float dangerUnder = 1f;
        public float tickFastEvery = 0.2f;
        public float tickVolume = 0.35f;

        [Header("Throw")]
        [Tooltip("Speed along the aim (m/s).")]
        public float throwSpeed = 17f;
        [Tooltip("Extra upward speed (m/s) so a flat aim still lobs.")]
        public float throwLift = 3.5f;
        [Tooltip("Seconds of the throw (arm swing) after letting go; the gun is held for this long.")]
        public float throwTime = 0.45f;
        [Tooltip("When in the throw the grenade leaves the hand (0..1 of throwTime).")]
        [Range(0f, 1f)] public float releaseAt = 0.38f;
        public float bounciness = 0.32f;
        public float friction = 0.7f;
        [Tooltip("Rolling drag once it is on the ground.")]
        public float rollDrag = 1.2f;

        [Header("Aim arc (while cooking)")]
        public bool showArc = true;
        public Color arcColor = new Color(1f, 0.82f, 0.35f, 0.75f);
        [Tooltip("Arc and landing marker colour when you stand inside the blast of where it lands.")]
        public Color dangerColor = new Color(1f, 0.25f, 0.18f, 0.85f);
        [Tooltip("Width of the dashed arc (m) once it is away from the hand; it narrows toward the hand.")]
        public float arcWidth = 0.045f;
        [Tooltip("Length of one dash plus its gap along the arc (m).")]
        public float arcDash = 0.45f;
        [Tooltip("Metres of path from the hand before the arc shows (keeps it from filling the view).")]
        public float arcStartDistance = 1.2f;
        [Tooltip("Seconds of flight the arc shows (it also stops where it hits something).")]
        public float arcSeconds = 2.2f;
        [Tooltip("Radius of the landing ring (m).")]
        public float landingRing = 0.35f;
        [Tooltip("A faint dashed ring on the ground at the blast radius round the landing spot.")]
        public bool showBlastRing = true;
        [Tooltip("Where the grenade leaves the hand, from the eye in camera axes (right, up, forward), first person.")]
        public Vector3 fpRelease = new Vector3(0.07f, 0.05f, 0.5f);
        [Tooltip("Where the grenade leaves the hand, from the feet in body axes (right, up, forward), third person.")]
        public Vector3 tpRelease = new Vector3(0.17f, 1.64f, 0.46f);

        [Header("Blast")]
        public float radius = 7f;
        [Tooltip("Damage to a zombie at the centre; falls off to edgeDamage at the radius.")]
        public float zombieDamage = 320f;
        [Tooltip("Damage to the player (the thrower) at the centre.")]
        public float playerDamage = 120f;
        [Range(0f, 1f)] public float edgeDamage = 0.25f;
        [Tooltip("Teammates' grenades hurt you too.")]
        public bool friendlyFire = false;
        [Tooltip("Walls and buildings shield you from the blast (checked with a ray to the chest).")]
        public bool wallsBlock = true;
        public float cameraShake = 1.1f;
        public float shakeRange = 30f;
        public Vector2Int haptics = new Vector2Int(160, 255);
        public Color shockwaveColor = new Color(1f, 0.7f, 0.35f, 0.8f);
        [Tooltip("Size of the scorch mark left on the ground (0 = none).")]
        public float scorchSize = 3.2f;

        [Header("Loot")]
        [Tooltip("Chance (0..1) that a loot chest also throws out a grenade.")]
        [Range(0f, 1f)] public float chestChance = 0.33f;
        [Tooltip("Grenades in one pickup.")]
        public int pickupAmount = 1;
        public float pickupLifetime = 45f;

        [Header("Sounds")]
        public AudioClip pinPull;
        public AudioClip spoon;
        public AudioClip throwSwing;
        public AudioClip[] bounce;
        public AudioClip[] explode;
        public float explodeVolume = 1f;
        public float explodeDistance = 90f;

        [Header("Models (set by Grenade setup)")]
        public GameObject projectilePrefab;
        public GameObject pickupPrefab;
        [Tooltip("The grenade as held in first person (FirstPersonView layer, child of the viewmodel camera).")]
        public GameObject heldPrefab;
        [Tooltip("Where the held grenade sits in front of the viewmodel camera while cooking.")]
        public Vector3 fpHoldPosition = new Vector3(0.19f, -0.16f, 0.42f);
        public Vector3 fpHoldEuler = new Vector3(-20f, 25f, -10f);
        [Tooltip("How far the gun dips out of view while a grenade is in hand (degrees the viewmodel camera turns up).")]
        public float fpGunDip = 32f;

        [Header("Throw animations (Blender/grenade_anim.py; set by Grenade setup)")]
        [Tooltip("First person: the right arm with the grenade in its fist (FP_Grenade.fbx), origin = the eye.")]
        public GameObject fpArms;
        public AnimationClip fpCook, fpThrow;
        [Tooltip("Third person: the skeleton the body clips are sampled onto (Operative_Grenade.fbx).")]
        public GameObject tpProxy;
        public AnimationClip tpRest, tpCook, tpThrow;
        [Tooltip("Bones copied from the clip onto the body (right arm, hand, fingers).")]
        public string[] tpCopyBones;
        [Tooltip("Bones turned by the clip's change from rest (the chest twist).")]
        public string[] tpDeltaBones;
        [Tooltip("Seconds of the Cook clips that bring the grenade up, pull the pin and cock the arm; the rest loops while cooking.")]
        public float cookRaise = 16f / 30f;
        [Tooltip("Cook clip second the left hand pulls the pin out (first person: the pin moves from the grenade to the hand).")]
        public float pinPullAt = 8f / 30f;
        [Tooltip("Seconds the pulled pin stays in the left hand before it drops out of view.")]
        public float pinHandFor = 0.4f;
    }
}
