using UnityEngine;
using ZombieRush.Core;
using ZombieRush.Data;
using ZombieRush.Player;

namespace ZombieRush.Zombie
{
    /// Always-chases-the-player zombie with lightweight obstacle avoidance: no
    /// NavMesh, instead it sphere-casts ahead and, when something's in the way,
    /// steers along the surface tangent (like a hand sliding along a wall) until
    /// it clears the obstacle, then resumes heading straight for the player.
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(ZombieHealth))]
    public class ZombieAI : MonoBehaviour
    {
        const float Gravity = 20f;
        const float MaxSpeedMultiplier = 1.6f;

        [Tooltip("Optional override; normally the config on ZombieHealth is used.")]
        public ZombieConfigData config;

        /// Set by whatever spawned this zombie (e.g. WaveManager) to scale attack
        /// damage and move speed up on later, harder waves. 1 = config's base values.
        public float difficultyMultiplier = 1f;

        [Tooltip("Chase the player from any distance, ignoring sightRange (set for wave-spawned zombies).")]
        public bool alwaysHunt;

        [Header("Obstacle avoidance")]
        public float probeDistance = 1.6f;
        public float probeRadius = 0.45f;
        public float avoidCommitTime = 0.35f; // briefly commits to a tangent direction to avoid jittering
        [Tooltip("Tip: put zombies on their own layer and exclude it here to skip zombie-vs-zombie probe hits entirely.")]
        public LayerMask obstacleMask = ~0;

        [Header("Last-resort unstuck")]
        public float stuckCheckInterval = 0.5f;
        public float stuckDistanceThreshold = 0.15f;
        public float escapeDuration = 1.2f;

        CharacterController controller;
        ZombieHealth health;
        ZombieConfigData cfg;
        Transform player;
        PlayerHealth playerHealth;

        float attackTimer;
        float gravityVelocity;

        Vector3 avoidTangent;
        float avoidHoldTimer;
        float nextProbeTime;

        float stuckCheckTimer;
        Vector3 positionAtLastCheck;
        float escapeTimer;
        Vector3 escapeDir;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            health = GetComponent<ZombieHealth>();
            cfg = config != null ? config : health.Config;
        }

        // Also runs when a pooled zombie is reused, so all steering state starts fresh.
        void OnEnable()
        {
            ResolvePlayer();
            attackTimer = 0f;
            gravityVelocity = 0f;
            avoidHoldTimer = 0f;
            escapeTimer = 0f;
            stuckCheckTimer = 0f;
            nextProbeTime = 0f;
            positionAtLastCheck = transform.position;
        }

        void ResolvePlayer()
        {
            if (player != null) return;
            playerHealth = PlayerHealth.Instance;
            if (playerHealth == null)
            {
                var go = GameObject.FindGameObjectWithTag(GameTags.Player);
                if (go != null) playerHealth = go.GetComponent<PlayerHealth>();
            }
            if (playerHealth != null) player = playerHealth.transform;
        }

        void Update()
        {
            if (health.IsDead || player == null) return;
            if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;

            Vector3 toPlayer = player.position - transform.position;
            toPlayer.y = 0f;
            float sqrDistance = toPlayer.sqrMagnitude;

            if (!alwaysHunt && sqrDistance > cfg.sightRange * cfg.sightRange)
            {
                health.Anim.SetSpeed(0f);
                ApplyGravityOnly();
                return;
            }

            attackTimer -= Time.deltaTime;

            if (sqrDistance <= cfg.attackRange * cfg.attackRange)
            {
                FaceDirection(toPlayer);
                ApplyGravityOnly();
                health.Anim.SetSpeed(0f);

                if (attackTimer <= 0f)
                {
                    health.Anim.TriggerAttack();
                    attackTimer = cfg.attackCooldown;
                    if (playerHealth != null) playerHealth.TakeDamage(cfg.attackDamage * difficultyMultiplier);
                }
                return;
            }

            // Chase, steering around anything in the way.
            float distance = Mathf.Sqrt(sqrDistance);
            bool running = cfg.runDistance <= 0f || distance <= cfg.runDistance;
            float speed = (running ? cfg.chaseSpeed : cfg.moveSpeed) * Mathf.Min(difficultyMultiplier, MaxSpeedMultiplier);
            health.Anim.SetSpeed(running ? 1f : 0.5f);

            Vector3 desired = toPlayer / distance;
            Vector3 moveDir = ComputeMoveDirection(desired);
            FaceDirection(moveDir);

            UpdateLastResortUnstuck(moveDir);
            if (escapeTimer > 0f)
            {
                escapeTimer -= Time.deltaTime;
                moveDir = escapeDir;
            }

            Vector3 move = moveDir * speed;
            move.y = UpdateGravity();
            controller.Move(move * Time.deltaTime);
        }

        /// Sphere-casts along the desired direction; if something's in the way,
        /// slides along its surface (perpendicular to the hit normal, on whichever
        /// side keeps making progress toward the player) instead of pushing
        /// straight into it. Probes are staggered (~10/s per zombie) to spread the physics cost.
        Vector3 ComputeMoveDirection(Vector3 desired)
        {
            if (avoidHoldTimer > 0f)
            {
                avoidHoldTimer -= Time.deltaTime;
                return avoidTangent;
            }

            if (Time.time < nextProbeTime) return desired;
            nextProbeTime = Time.time + 0.08f + Random.value * 0.04f;

            Vector3 origin = transform.position + Vector3.up;
            if (Physics.SphereCast(origin, probeRadius, desired, out var hit, probeDistance, obstacleMask, QueryTriggerInteraction.Ignore)
                && IsRealObstacle(hit.collider))
            {
                Vector3 tangent = Vector3.Cross(hit.normal, Vector3.up).normalized;
                if (Vector3.Dot(tangent, desired) < 0f) tangent = -tangent;

                avoidTangent = (tangent + desired * 0.35f).normalized;
                avoidHoldTimer = avoidCommitTime;
                return avoidTangent;
            }

            return desired;
        }

        bool IsRealObstacle(Collider col)
        {
            if (col.CompareTag(GameTags.Player)) return false;
            if (col.GetComponentInParent<ZombieHealth>() != null) return false;
            return true;
        }

        /// If, despite avoidance, it's genuinely still not making progress (a
        /// concave corner the tangent-follow can bounce around in, say), pick a
        /// random escape direction for a bit rather than getting permanently wedged.
        void UpdateLastResortUnstuck(Vector3 currentMoveDir)
        {
            if (escapeTimer > 0f) return;

            stuckCheckTimer += Time.deltaTime;
            if (stuckCheckTimer < stuckCheckInterval) return;

            float movedSqr = (transform.position - positionAtLastCheck).sqrMagnitude;
            stuckCheckTimer = 0f;
            positionAtLastCheck = transform.position;

            if (movedSqr < stuckDistanceThreshold * stuckDistanceThreshold)
            {
                escapeTimer = escapeDuration;
                escapeDir = (Quaternion.Euler(0f, Random.Range(60f, 300f), 0f) * currentMoveDir).normalized;
            }
        }

        void FaceDirection(Vector3 dir)
        {
            if (dir.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 8f);
        }

        float UpdateGravity()
        {
            if (controller.isGrounded) gravityVelocity = -1f;
            else gravityVelocity -= Gravity * Time.deltaTime;
            return gravityVelocity;
        }

        void ApplyGravityOnly()
        {
            controller.Move(new Vector3(0f, UpdateGravity(), 0f) * Time.deltaTime);
        }
    }
}
