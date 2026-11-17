using UnityEngine;

namespace ZombieRush.Audio
{
    /// Every sound the game plays, in one asset (Resources/ZombieRush/SoundLibrary).
    /// Arrays pick a random clip each time so repeated sounds don't feel robotic.
    /// Empty slots are simply silent. Menu: Zombie Rush > Audio > Auto-Fill Sound Library.
    [CreateAssetMenu(fileName = "SoundLibrary", menuName = "Zombie Rush/Sound Library")]
    public class SoundLibrary : ScriptableObject
    {
        public const string ResourcePath = "ZombieRush/SoundLibrary";

        [Header("Music")]
        public AudioClip menuMusic;
        [Tooltip("Gameplay music between waves (and the whole match when there is no combat track).")]
        public AudioClip gameMusic;
        [Tooltip("Gameplay music while a wave is on. Empty = gameMusic all match.")]
        public AudioClip combatMusic;
        [Range(0f, 1f)] public float musicLevel = 0.55f;

        [Header("Ambience (gameplay only, under the SFX slider)")]
        [Tooltip("Looping background bed: wind, night, distant city.")]
        public AudioClip ambience;
        [Range(0f, 1f)] public float ambienceLevel = 0.3f;

        [Header("Weapons (used when a weapon has no sounds of its own)")]
        public AudioClip[] gunshot;
        public AudioClip reload;
        public AudioClip emptyClick;
        public AudioClip pickup;
        [Tooltip("Played when the player switches guns.")]
        public AudioClip[] weaponDraw;
        [Range(0f, 1f)] public float weaponDrawVolume = 0.5f;

        [Header("Turret")]
        public AudioClip[] turretShot;
        public AudioClip turretDeploy;
        public AudioClip turretPowerDown;

        [Header("Zombies")]
        public AudioClip[] zombieGroan;
        public AudioClip[] zombieAttack;
        public AudioClip[] zombieHurt;
        public AudioClip[] zombieDeath;
        public AudioClip[] bulletImpact;

        [Header("Special zombies")]
        public AudioClip bombBeep;
        public AudioClip[] explosion;
        public AudioClip gooSpit;
        public AudioClip gooSplat;
        public AudioClip shriek;

        [Header("Player")]
        public AudioClip[] playerHurt;
        public AudioClip playerDeath;
        [Tooltip("Steps on roads, pavements, floors.")]
        public AudioClip[] footstepHard;
        [Tooltip("Steps on the terrain (grass, dirt).")]
        public AudioClip[] footstepSoft;
        [Range(0f, 1f)] public float footstepVolume = 0.3f;
        [Tooltip("Metres per step at walking and at sprinting speed.")]
        public float strideWalk = 1.8f;
        public float strideSprint = 2.6f;
        [Range(0f, 1f)] public float crouchStepVolume = 0.5f;
        [Tooltip("Looping heartbeat that fades in at low health.")]
        public AudioClip heartbeat;
        [Tooltip("Health share (0-1) below which the heartbeat starts; it is loudest near 0.")]
        [Range(0f, 1f)] public float heartbeatBelow = 0.3f;
        [Range(0f, 1f)] public float heartbeatVolume = 0.7f;

        [Header("Game")]
        public AudioClip waveStart;
        public AudioClip waveCleared;
        public AudioClip coin;
        public AudioClip zoneExpand;
        public AudioClip denied;
        public AudioClip purchase;
        public AudioClip gameOver;
        public AudioClip uiClick;
    }
}
