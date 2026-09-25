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

        [Header("Detection")]
        public float sightRange = 25f; // effectively "always sees the player" until real vision/sound checks are added
    }
}
