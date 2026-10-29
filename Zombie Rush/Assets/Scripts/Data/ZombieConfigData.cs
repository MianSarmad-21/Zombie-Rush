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
        public float attackRange = 1.6f;
        public float attackDamage = 12f;
        public float attackCooldown = 1.1f;
        [Tooltip("Seconds from the start of the swing until the damage lands (0 = at once). The player can still step out of reach meanwhile. A JuggernautMotion on the zombie lands it earlier, at the moment the swing connects.")]
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
