using System.Collections;
using UnityEngine;
using ZombieRush.Audio;
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

        /// Every zombie that is currently active in the scene (dead ones included until their
        /// corpse is cleared - check IsDead). Lets turrets find targets without physics queries.
        public static readonly System.Collections.Generic.List<ZombieHealth> Active = new System.Collections.Generic.List<ZombieHealth>();

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

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

        // After ZombieVisual has set up the Animator: keep the feet on uneven ground.
        void Start()
        {
            if (!Anim.Resolve() || !Anim.Animator.isHuman) return;
            var ik = Anim.Animator.GetComponent<ZombieFootIK>();
            if (ik == null) ik = Anim.Animator.gameObject.AddComponent<ZombieFootIK>();
            ik.Init(this);
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

        public void TakeDamage(float amount) => TakeDamage(amount, true);

        /// byPlayer = false for turret hits: no crosshair hit marker for shots the player didn't fire.
        public void TakeDamage(float amount, bool byPlayer)
        {
            if (IsDead) return;

            CurrentHealth -= amount;
            if (byPlayer) GameEvents.RaiseEnemyHit();
            var lib = SoundManager.Library;
            if (lib != null)
            {
                SoundManager.PlayAt(lib.bulletImpact, transform.position + Vector3.up, 0.5f, 25f, 3);
                if (CurrentHealth > 0f) SoundManager.PlayAt(lib.zombieHurt, transform.position + Vector3.up * 1.6f, 0.8f, 30f, 2);
            }
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
            var lib = SoundManager.Library;
            if (lib != null) SoundManager.PlayAt(lib.zombieDeath, transform.position + Vector3.up * 1.4f, 1f, 35f, 3);

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
            float groundY = GroundHeightBelow();
            if (collapse) yield return CollapseRoutine(groundY);
            else yield return FallToGround(groundY);
            yield return new WaitForSeconds(corpseDuration);
            deathRoutine = null;

            if (ReleaseToPool != null) ReleaseToPool(this);
            else Destroy(gameObject);
        }

        static readonly RaycastHit[] groundHits = new RaycastHit[16];

        /// Height of the ground under the corpse, ignoring zombies (this one's colliders are
        /// already off). A zombie killed in mid-air - off a ledge, on a slope step - must not
        /// stay floating once its AI (and with it, gravity) stops.
        float GroundHeightBelow()
        {
            Vector3 origin = transform.position + Vector3.up * 1.5f;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, groundHits, 30f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = groundHits[i];
                if (hit.collider.GetComponentInParent<ZombieHealth>() != null) continue;
                if (hit.point.y > best) best = hit.point.y;
            }
            // No ground found, or the zombie is already at/below it (e.g. shot while still rising): stay put.
            return float.IsNegativeInfinity(best) || best > transform.position.y ? transform.position.y : best;
        }

        /// The death animation plays on the spot; if the zombie died above the ground, drop it there.
        IEnumerator FallToGround(float groundY)
        {
            float speed = 0f;
            while (transform.position.y > groundY + 0.01f)
            {
                speed += 20f * Time.deltaTime;
                var p = transform.position;
                p.y = Mathf.Max(groundY, p.y - speed * Time.deltaTime);
                transform.position = p;
                yield return null;
            }
        }

        /// No ragdoll - just tips the zombie over onto the ground, so a kill reads as a
        /// real death rather than the model simply vanishing. Used only when the zombie's
        /// Animator Controller has no Die animation.
        IEnumerator CollapseRoutine(float groundY)
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
            Vector3 endPos = new Vector3(startPos.x, groundY - 0.15f, startPos.z);

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
