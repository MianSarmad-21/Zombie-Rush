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

        [Tooltip("Max health on wave 1; later waves add WaveBalanceConfig.playerHealthPerWave.")]
        public float maxHealth = 100f;
        float baseMaxHealth;
        [Header("Armor")]
        public float maxArmor = 100f;
        [Tooltip("Share of incoming damage the armor soaks up while it lasts.")]
        [Range(0f, 1f)] public float armorAbsorb = 0.66f;

        public float CurrentHealth { get; private set; }
        public float CurrentArmor { get; private set; }
        public bool IsDead { get; private set; }
        /// Counts lives: goes up each time the player is revived (the base's armour vest is one per life).
        public int Life { get; private set; }

        /// Inside the safe base (not necessarily protected yet: see Protected).
        public bool InSafeBase => SafeBase.Instance != null && SafeBase.Instance.Contains(transform.position);

        /// Inside the safe base for at least its reentryGrace: zombies can't hurt or target this player.
        /// Right after stepping back in the player is still fair game (no doorway peeking).
        public bool Protected => InSafeBase && Time.time - enteredBaseAt >= (SafeBase.Instance != null ? SafeBase.Instance.reentryGrace : 0f);

        float enteredBaseAt = float.NegativeInfinity;   // the match starts inside: protected at once
        bool wasInBase = true;

        void Update()
        {
            bool inside = InSafeBase;
            if (inside && !wasInBase) enteredBaseAt = Time.time;
            wasInBase = inside;
        }

        /// Set on a teammate's copy (multiplayer): damage dealt to the copy (on the host) is sent to the
        /// player who owns it instead of being applied here. Null on the local player.
        public System.Action<float, Vector3?> forwardDamage;   // amount, where it came from (null = not known)
        public bool IsRemoteCopy => forwardDamage != null;

        /// A teammate's copy: mirrors the owner's health and death.
        public void SetRemoteState(float health, float max, bool dead)
        {
            maxHealth = max; CurrentHealth = health; IsDead = dead;
        }

        void Awake()
        {
            if (forwardDamage == null && GetComponent<PlayerViewSwitcher>() is PlayerViewSwitcher pv && pv.remote) { CurrentHealth = maxHealth; return; }
            if (Instance == null) Instance = this;
            baseMaxHealth = maxHealth;
            CurrentHealth = maxHealth;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // The player always begins a wave at full health, and gains max health as the waves go on.
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

        /// Back on its feet with full health (a squad member who fell, at the next wave).
        public void Revive()
        {
            if (!IsDead) return;
            IsDead = false;
            Life++;
            CurrentHealth = maxHealth;
            enteredBaseAt = float.NegativeInfinity; wasInBase = true;   // back at the start point: protected at once
            var sb = SafeBase.Instance;
            if (sb != null)
            {
                var cc = GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;   // off first: its own capsule isn't "in the way"
                if (sb.TryGetStart(out var p, out var rot)) transform.SetPositionAndRotation(p, rot);
                else transform.position = sb.Center + Vector3.up * 0.1f;
                if (cc != null) cc.enabled = true;
            }
            RaiseHealth();
            if (this == Instance) GameEvents.RaiseNotice("BACK IN THE FIGHT", "");
        }

        void HandleWaveAnnounced(int wave)
        {
            if (IsDead && forwardDamage == null && ZombieRush.Net.NetSession.InSquad) Revive();
            maxHealth = WaveScaling.Balance.PlayerMaxHealth(baseMaxHealth, wave);
            FullHeal();
        }

        public void FullHeal()
        {
            if (IsDead) return;
            CurrentHealth = maxHealth;
            RaiseHealth();
        }

        public void TakeDamage(float amount) => TakeDamage(amount, null);

        /// `source`: where the hit came from (the zombie), for the damage direction arrows. Null = not known
        /// (poison ticks), no arrow.
        public void TakeDamage(float amount, Vector3? source)
        {
            if (IsDead || amount <= 0f || Protected) return;
            if (forwardDamage != null) { forwardDamage(amount, source); return; }
            if (source.HasValue && this == Instance) GameEvents.RaisePlayerHurtFrom(source.Value, amount);

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
