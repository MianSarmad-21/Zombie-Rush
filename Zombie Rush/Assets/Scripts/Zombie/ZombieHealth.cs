using UnityEngine;
using ZombieRush.Core;
using ZombieRush.Data;

namespace ZombieRush.Zombie
{
    public class ZombieHealth : MonoBehaviour, IDamageable
    {
        public ZombieConfigData config;

        public float CurrentHealth { get; private set; }
        public float MaxHealth => config != null ? config.maxHealth : 60f;
        public bool IsDead { get; private set; }

        public System.Action<ZombieHealth> OnDied;

        void Awake() => CurrentHealth = MaxHealth;

        public void TakeDamage(float amount)
        {
            if (IsDead) return;

            CurrentHealth -= amount;
            if (CurrentHealth <= 0f) Die();
        }

        void Die()
        {
            IsDead = true;
            OnDied?.Invoke(this);

            var agent = GetComponent<ZombieAI>();
            if (agent != null) agent.enabled = false;
            var controller = GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            foreach (var col in GetComponents<Collider>()) col.enabled = false;

            Destroy(gameObject, 3f);
        }
    }
}
