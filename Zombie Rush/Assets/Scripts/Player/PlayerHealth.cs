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
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            CurrentHealth = maxHealth;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // The player always begins a wave at full health.
        void OnEnable() => GameEvents.OnWaveAnnounced += HandleWaveAnnounced;
        void OnDisable() => GameEvents.OnWaveAnnounced -= HandleWaveAnnounced;

        void Start() => GameEvents.RaisePlayerHealthChanged(CurrentHealth, maxHealth);

        void HandleWaveAnnounced(int wave) => FullHeal();

        public void FullHeal()
        {
            if (IsDead) return;
            CurrentHealth = maxHealth;
            GameEvents.RaisePlayerHealthChanged(CurrentHealth, maxHealth);
        }

        public void TakeDamage(float amount)
        {
            if (IsDead || amount <= 0f) return;

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
            if (IsDead || amount <= 0f) return;
            CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
            GameEvents.RaisePlayerHealthChanged(CurrentHealth, maxHealth);
        }
    }
}
