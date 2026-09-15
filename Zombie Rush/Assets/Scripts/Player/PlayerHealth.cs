using UnityEngine;
using ZombieRush.Core;

namespace ZombieRush.Player
{
    public class PlayerHealth : MonoBehaviour
    {
        public float maxHealth = 100f;
        public float CurrentHealth { get; private set; }
        public bool IsDead { get; private set; }

        void Awake() => CurrentHealth = maxHealth;

        void Start() => GameEvents.RaisePlayerHealthChanged(CurrentHealth, maxHealth);

        public void TakeDamage(float amount)
        {
            if (IsDead) return;

            CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
            GameEvents.RaisePlayerHealthChanged(CurrentHealth, maxHealth);

            if (CurrentHealth <= 0f)
            {
                IsDead = true;
                GameEvents.RaisePlayerDied();
            }
        }

        public void Heal(float amount)
        {
            if (IsDead) return;
            CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
            GameEvents.RaisePlayerHealthChanged(CurrentHealth, maxHealth);
        }
    }
}
