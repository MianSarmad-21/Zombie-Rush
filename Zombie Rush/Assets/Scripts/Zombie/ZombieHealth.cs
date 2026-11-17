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
        [Range(0f, 1f)] public float ammoDropChance = 0.08f;   // rare: ammo should not come too easily (base tables are the main supply)
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

        /// Any zombie took damage: (zombie, damage, fired by the player). Health bars and damage numbers listen.
        public static event System.Action<ZombieHealth, float, bool> AnyDamaged;

        /// Health left, 0 to 1.
        public float HealthFraction => MaxHealth > 0f ? Mathf.Clamp01(CurrentHealth / MaxHealth) : 0f;

        [Tooltip("Head bone to the top of the skull for a 1.85 m zombie, metres (grows with bigger zombies).")]
        public float skullHeight = 0.2f;
        Transform headBone;
        bool headLooked;

        /// Top of the head in world space: the animated head bone (so hunched and giant zombies get it
        /// right, and it follows the head), else the top of the CharacterController.
        public Vector3 HeadPosition
        {
            get
            {
                if (!headLooked)
                {
                    headLooked = true;
                    var an = GetComponentInChildren<Animator>(true);
                    if (an != null && an.isHuman) headBone = an.GetBoneTransform(HumanBodyBones.Head);
                }
                if (headBone != null)
                {
                    float size = controller != null ? controller.height * transform.lossyScale.y / 1.85f : 1f;
                    return headBone.position + Vector3.up * (skullHeight * size);
                }
                if (controller == null) return transform.position + Vector3.up * 1.9f;
                return transform.position + Vector3.up * ((controller.center.y + controller.height * 0.5f) * transform.lossyScale.y);
            }
        }

        void OnEnable()
        {
            Active.Add(this);
            ZombieRush.UI.ZombieOverheadUI.Ensure();
        }
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
        public void TakeDamage(float amount, bool byPlayer) => TakeHit(amount, byPlayer, transform.position + Vector3.up, Vector3.zero);

        Vector3 lastHitPoint, lastHitDir;
        [Tooltip("Leave a pool of blood when it dies (off for zombies that burst into something else, e.g. poison).")]
        public bool bleedsOnDeath = true;

        /// Its network side in a match (null outside one). On a joined player's machine this zombie is a mirror
        /// of the host's: hits are sent to the host, health and death come back from it.
        [System.NonSerialized] public ZombieRush.Net.ZombieNet net;
        bool Mirror => net != null && net.IsSpawned && !net.IsServer;

        /// Mirror: health from the host (bars and numbers).
        public void SetNetHealth(float current, float max)
        {
            var cfgMax = Config != null ? Config.maxHealth : 1f;
            if (cfgMax > 0f && max > 0f) difficultyMultiplier = max / cfgMax;
            CurrentHealth = current;
        }

        /// Blood and the hurt sound for a hit (without changing health).
        public void LocalHitFx(Vector3 point, Vector3 direction)
        {
            var lib = SoundManager.Library;
            if (lib != null)
            {
                SoundManager.PlayAt(lib.bulletImpact, transform.position + Vector3.up, 0.5f, 25f, 3);
                if (CurrentHealth > 0f) SoundManager.PlayAt(lib.zombieHurt, transform.position + Vector3.up * 1.6f, 0.8f, 30f, 2);
            }
            BloodEffect.Spawn(point, direction);
        }

        /// Mirror: the host says it died. Same look as a real death; the host removes it later.
        public void NetDie()
        {
            if (IsDead) return;
            CurrentHealth = 0f;
            IsDead = true;
            Died?.Invoke(this);
            if (ai != null) ai.enabled = false;
            foreach (var col in colliders) col.enabled = false;
            if (bleedsOnDeath) BloodEffect.SpawnDeath(lastHitPoint == Vector3.zero ? transform.position + Vector3.up * 0.8f : lastHitPoint, lastHitDir);
            var lib = SoundManager.Library;
            if (lib != null) SoundManager.PlayAt(lib.zombieDeath, transform.position + Vector3.up * 1.4f, 1f, 35f, 3);
            Anim.TriggerDie();
        }

        /// A hit at a point on the body, by a shot travelling along `direction` (blood sprays along it).
        public void TakeHit(float amount, bool byPlayer, Vector3 point, Vector3 direction)
        {
            if (IsDead) return;
            lastHitPoint = point; lastHitDir = direction;

            if (Mirror)
            {
                // a joined player's shot: show it here at once, the host applies the damage
                net.ClientHit(amount, point, direction);
                if (byPlayer) GameEvents.RaiseEnemyHit();
                LocalHitFx(point, direction);
                AnyDamaged?.Invoke(this, amount, byPlayer);
                return;
            }
            if (net != null && net.IsSpawned && byPlayer) net.LastAttacker = net.NetworkManager.LocalClientId;

            CurrentHealth -= amount;
            if (byPlayer) GameEvents.RaiseEnemyHit();
            var lib = SoundManager.Library;
            if (lib != null)
            {
                SoundManager.PlayAt(lib.bulletImpact, transform.position + Vector3.up, 0.5f, 25f, 3);
                if (CurrentHealth > 0f) SoundManager.PlayAt(lib.zombieHurt, transform.position + Vector3.up * 1.6f, 0.8f, 30f, 2);
            }
            BloodEffect.Spawn(point, direction);

            if (CurrentHealth <= 0f) Die(true);
            else Anim.TriggerHit();
            if (net != null) { net.PushHealth(); net.BroadcastHit(point, direction); }
            AnyDamaged?.Invoke(this, amount, byPlayer);
        }

        /// Kills the zombie outright. reward = false drops no coins or ammo (a Bomber that
        /// blew itself up on the player earns nothing).
        public void Kill(bool reward)
        {
            if (IsDead) return;
            CurrentHealth = 0f;
            Die(reward);
        }

        void Die(bool reward)
        {
            IsDead = true;
            Died?.Invoke(this);

            if (ai != null) ai.enabled = false;
            foreach (var col in colliders) col.enabled = false;

            if (bleedsOnDeath) BloodEffect.SpawnDeath(lastHitPoint == Vector3.zero ? transform.position + Vector3.up * 0.8f : lastHitPoint, lastHitDir);
            var lib = SoundManager.Library;
            if (lib != null) SoundManager.PlayAt(lib.zombieDeath, transform.position + Vector3.up * 1.4f, 1f, 35f, 3);

            var killed = new ZombieKilledInfo
            {
                Position = transform.position,
                Coins = reward && Random.value <= coinDropChance ? Random.Range(minCoins, maxCoins + 1) : 0,
                Ammo = reward && Random.value <= ammoDropChance ? Random.Range(minAmmo, maxAmmo + 1) : 0,
            };
            if (net != null && net.IsSpawned) net.ServerDied(killed);   // the reward goes to the killer's machine
            else GameEvents.RaiseZombieKilled(killed);

            deathRoutine = StartCoroutine(DeathRoutine(!Anim.TriggerDie()));
        }

        IEnumerator DeathRoutine(bool collapse)
        {
            float groundY = GroundHeightBelow();
            if (collapse) yield return CollapseRoutine(groundY);
            else yield return FallToGround(groundY);
            yield return new WaitForSeconds(corpseDuration);
            deathRoutine = null;

            if (Mirror) yield break;   // the host removes it from the match
            if (net != null) net.ServerDespawn();
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
