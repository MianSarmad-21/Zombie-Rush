using System.Collections;
using UnityEngine;
using ZombieRush.Core;
using ZombieRush.Data;
using ZombieRush.Effects;

namespace ZombieRush.Zombie
{
    [DisallowMultipleComponent]
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

        [Header("Corpse")]
        public float corpseDuration = 4f;

        public ZombieConfigData Config => config != null ? config : ZombieConfigData.Defaults;
        public float CurrentHealth { get; private set; }
        public float MaxHealth => Config.maxHealth * difficultyMultiplier;
        public bool IsDead { get; private set; }
        public ZombieAnimatorCache Anim { get; private set; }

        public event System.Action<ZombieHealth> Died;

        /// Assigned by a pool. When set, the corpse is handed back to the pool
        /// instead of being destroyed.
        public System.Action<ZombieHealth> ReleaseToPool;

        CharacterController controller;
        ZombieAI ai;
        Collider[] colliders;
        Transform visualRoot;
        Quaternion visualRootLocalRotation;
        Coroutine deathRoutine;

        void Awake()
        {
            Anim = new ZombieAnimatorCache(gameObject);
            controller = GetComponent<CharacterController>();
            ai = GetComponent<ZombieAI>();
            colliders = GetComponents<Collider>();
            CurrentHealth = MaxHealth;
        }

        /// Called by the spawner after Instantiate or when reusing a pooled zombie:
        /// clears death state and applies this wave's difficulty.
        public void ResetForSpawn(float multiplier)
        {
            if (deathRoutine != null) { StopCoroutine(deathRoutine); deathRoutine = null; }

            difficultyMultiplier = multiplier;
            CurrentHealth = MaxHealth;
            IsDead = false;

            // The CharacterController is re-enabled by ZombieSpawnRise once the zombie surfaces.
            foreach (var col in colliders)
                if (col != controller) col.enabled = true;

            if (Anim.Resolve())
            {
                Anim.Animator.enabled = true;
                if (visualRoot != null) visualRoot.localRotation = visualRootLocalRotation;
            }
        }

        public void TakeDamage(float amount)
        {
            if (IsDead) return;

            CurrentHealth -= amount;
            GameEvents.RaiseEnemyHit();
            BloodEffect.Spawn(transform.position + Vector3.up);

            if (CurrentHealth <= 0f) Die();
            else Anim.TriggerHit();
        }

        void Die()
        {
            IsDead = true;
            Died?.Invoke(this);

            if (ai != null) ai.enabled = false;
            foreach (var col in colliders) col.enabled = false;

            BloodEffect.Spawn(transform.position + Vector3.up * 0.8f);

            GameEvents.RaiseZombieKilled(new ZombieKilledInfo
            {
                Position = transform.position,
                Coins = Random.value <= coinDropChance ? Random.Range(minCoins, maxCoins + 1) : 0,
                Ammo = Random.value <= ammoDropChance ? Random.Range(minAmmo, maxAmmo + 1) : 0,
            });

            deathRoutine = StartCoroutine(DeathRoutine(!Anim.TriggerDie()));
        }

        IEnumerator DeathRoutine(bool collapse)
        {
            if (collapse) yield return CollapseRoutine();
            yield return new WaitForSeconds(corpseDuration);
            deathRoutine = null;

            if (ReleaseToPool != null) ReleaseToPool(this);
            else Destroy(gameObject);
        }

        /// No ragdoll - just tips the zombie over and sinks it slightly, so a kill
        /// reads as a real death rather than the model simply vanishing.
        IEnumerator CollapseRoutine()
        {
            var animator = Anim.Animator;
            if (animator != null) animator.enabled = false;

            if (visualRoot == null)
            {
                visualRoot = animator != null ? animator.transform : transform;
                visualRootLocalRotation = visualRoot.localRotation;
            }

            Vector3 fallAxis = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized;
            if (fallAxis == Vector3.zero) fallAxis = Vector3.right;
            Quaternion startRot = visualRoot.rotation;
            Quaternion endRot = Quaternion.AngleAxis(85f, fallAxis) * startRot;

            Vector3 startPos = transform.position;
            Vector3 endPos = startPos - Vector3.up * 0.5f;

            const float duration = 0.5f;
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
