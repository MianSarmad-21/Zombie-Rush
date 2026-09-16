using UnityEngine;
using ZombieRush.Core;

namespace ZombieRush.Player
{
    public class PlayerHealth : MonoBehaviour
    {
        public static PlayerHealth Instance { get; private set; }

        public float maxHealth = 100f;
        public float CurrentHealth { get; private set; }
        public bool IsDead { get; private set; }

        void Awake()
        {
            Instance = this;
            CurrentHealth = maxHealth;
        }

        /// Called by WaveManager at the start of every wave so the player
        /// always begins a wave at full health.
        public void FullHeal()
        {
            if (IsDead) return;
            CurrentHealth = maxHealth;
            GameEvents.RaisePlayerHealthChanged(CurrentHealth, maxHealth);
        }

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
