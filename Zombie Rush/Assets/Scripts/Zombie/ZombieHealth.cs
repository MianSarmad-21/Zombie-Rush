using System.Collections;
using UnityEngine;
using ZombieRush.Core;
using ZombieRush.Data;
using ZombieRush.Effects;
using ZombieRush.Player;

namespace ZombieRush.Zombie
{
    public class ZombieHealth : MonoBehaviour, IDamageable
    {
        public ZombieConfigData config;

        /// Set by whatever spawned this zombie to make later waves tougher.
        public float difficultyMultiplier = 1f;

        [Header("Coin drop")]
        [Range(0f, 1f)] public float coinDropChance = 1f;
        public int minCoins = 5;
        public int maxCoins = 15;

        [Header("Ammo drop")]
        [Range(0f, 1f)] public float ammoDropChance = 0.25f;
        public int minAmmo = 4;
        public int maxAmmo = 10;

        public float CurrentHealth { get; private set; }
        public float MaxHealth => (config != null ? config.maxHealth : 60f) * difficultyMultiplier;
        public bool IsDead { get; private set; }

        public System.Action<ZombieHealth> OnDied;

        void Awake() => CurrentHealth = MaxHealth;

        /// Called by the spawner right after Instantiate - Awake() already ran with
        /// the default multiplier, so this re-applies it and refills health to match.
        public void ApplyDifficulty(float multiplier)
        {
            difficultyMultiplier = multiplier;
            CurrentHealth = MaxHealth;
        }

        public void TakeDamage(float amount)
        {
            if (IsDead) return;

            CurrentHealth -= amount;
            BloodEffect.Spawn(transform.position + Vector3.up * 1f);

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

            BloodEffect.Spawn(transform.position + Vector3.up * 0.8f);
            StartCoroutine(CollapseRoutine());

            if (Random.value <= coinDropChance && PlayerCurrency.Instance != null)
                PlayerCurrency.Instance.AddCoins(Random.Range(minCoins, maxCoins + 1));

            if (Random.value <= ammoDropChance && PlayerWeaponController.Instance != null)
                PlayerWeaponController.Instance.AddReserveAmmoToAll(Random.Range(minAmmo, maxAmmo + 1));

            Destroy(gameObject, 3f);
        }

        /// No ragdoll - just tips the zombie over and sinks it slightly, so a kill
        /// reads as a real death rather than the model simply vanishing.
        IEnumerator CollapseRoutine()
        {
            var animator = GetComponentInChildren<Animator>();
            if (animator != null) animator.enabled = false;

            var visualRoot = animator != null ? animator.transform : transform;
            Vector3 fallAxis = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized;
            Quaternion startRot = visualRoot.rotation;
            Quaternion endRot = Quaternion.AngleAxis(85f, fallAxis) * startRot;

            Vector3 startPos = transform.position;
            Vector3 endPos = startPos - Vector3.up * 0.5f;

            float duration = 0.5f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / duration), 3f); // ease-out
                visualRoot.rotation = Quaternion.Slerp(startRot, endRot, k);
                transform.position = Vector3.Lerp(startPos, endPos, k);
                yield return null;
            }
        }
    }
}
