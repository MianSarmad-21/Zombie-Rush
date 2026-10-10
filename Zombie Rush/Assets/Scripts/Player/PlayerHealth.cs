using System.Collections.Generic;
using UnityEngine;
using ZombieRush.Core;
using ZombieRush.World;

namespace ZombieRush.Player
{
    public class PlayerHealth : MonoBehaviour
    {
        /// The local player (camera, HUD). In a squad every player has a PlayerHealth; see All.
        public static PlayerHealth Instance { get; private set; }

        /// Every player in the match, alive or dead. Zombies pick their targets from here.
        public static readonly List<PlayerHealth> All = new List<PlayerHealth>();

        public float maxHealth = 100f;
        [Header("Armor")]
        public float maxArmor = 100f;
        [Tooltip("Share of incoming damage the armor soaks up while it lasts.")]
        [Range(0f, 1f)] public float armorAbsorb = 0.66f;

        public float CurrentHealth { get; private set; }
        public float CurrentArmor { get; private set; }
        public bool IsDead { get; private set; }

        /// Inside the safe base: zombies can't reach or hurt this player.
        public bool InSafeBase => SafeBase.Instance != null && SafeBase.Instance.Contains(transform.position);

        void Awake()
        {
            if (Instance == null) Instance = this;
            CurrentHealth = maxHealth;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // The player always begins a wave at full health.
        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
            GameEvents.OnWaveAnnounced += HandleWaveAnnounced;
        }

        void OnDisable()
        {
            All.Remove(this);
            GameEvents.OnWaveAnnounced -= HandleWaveAnnounced;
        }

        void Start()
        {
            RaiseHealth();
            RaiseArmor();
        }

        void HandleWaveAnnounced(int wave) => FullHeal();

        public void FullHeal()
        {
            if (IsDead) return;
            CurrentHealth = maxHealth;
            RaiseHealth();
        }

        public void TakeDamage(float amount)
        {
            if (IsDead || amount <= 0f || InSafeBase) return;

            if (CurrentArmor > 0f)
            {
                float soaked = Mathf.Min(CurrentArmor, amount * armorAbsorb);
                CurrentArmor -= soaked;
                amount -= soaked;
                RaiseArmor();
            }

            CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
            RaiseHealth();

            if (CurrentHealth <= 0f)
            {
                IsDead = true;
                if (this == Instance) GameEvents.RaisePlayerDied();
            }
        }

        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f) return;
            CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
            RaiseHealth();
        }

        /// Returns how much armor was actually added (0 if already full).
        public float AddArmor(float amount)
        {
            if (IsDead || amount <= 0f) return 0f;
            float before = CurrentArmor;
            CurrentArmor = Mathf.Min(maxArmor, CurrentArmor + amount);
            RaiseArmor();
            return CurrentArmor - before;
        }

        // Only the local player drives the HUD.
        void RaiseHealth() { if (this == Instance) GameEvents.RaisePlayerHealthChanged(CurrentHealth, maxHealth); }
        void RaiseArmor() { if (this == Instance) GameEvents.RaisePlayerArmorChanged(CurrentArmor, maxArmor); }
    }
}
