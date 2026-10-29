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
        public AudioClip gameMusic;
        [Range(0f, 1f)] public float musicLevel = 0.55f;

        [Header("Weapons (used when a weapon has no sounds of its own)")]
        public AudioClip[] gunshot;
        public AudioClip reload;
        public AudioClip emptyClick;
        public AudioClip pickup;

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

        [Header("Player")]
        public AudioClip[] playerHurt;
        public AudioClip playerDeath;

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
