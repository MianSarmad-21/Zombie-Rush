using UnityEngine;

namespace ZombieRush.Data
{
    [CreateAssetMenu(fileName = "NewZombie", menuName = "Zombie Rush/Zombie Config")]
    public class ZombieConfigData : ScriptableObject
    {
        public string zombieName = "Zombie";
        public float maxHealth = 60f;
        public float moveSpeed = 2.2f;
        public float chaseSpeed = 3.5f;
        [Tooltip("Beyond this distance the zombie walks at moveSpeed, inside it runs at chaseSpeed. 0 = always run.")]
        public float runDistance = 0f;

        [Header("Attack")]
        [Tooltip("Arm's reach: the gap between the zombie's body and the player's body (capsule edge to edge, metres) at which it swings.")]
        public float attackReach = 0.45f;
        [Tooltip("Once swinging, it keeps attacking until the gap grows past attackReach + this (stops it flicking between chase and attack).")]
        public float attackHoldMargin = 0.2f;
        [Tooltip("A delayed swing (attackHitDelay) still lands if the gap is within attackReach + this when the hit comes.")]
        public float attackLandTolerance = 0.3f;
        public float attackDamage = 12f;
        public float attackCooldown = 1.1f;
        [Tooltip("Seconds from the start of the swing until the damage lands (0 = at once). The player can still step out of reach meanwhile.")]
        public float attackHitDelay = 0f;

        [Header("Detection")]
        public float sightRange = 25f; // effectively "always sees the player" until real vision/sound checks are added

        static ZombieConfigData defaults;

        /// Fallback used when a zombie has no config assigned, so gameplay code
        /// never needs its own copy of these numbers.
        public static ZombieConfigData Defaults
        {
            get
            {
                if (defaults == null)
                {
                    defaults = CreateInstance<ZombieConfigData>();
                    defaults.hideFlags = HideFlags.HideAndDontSave;
                }
                return defaults;
            }
        }
    }
}
