using UnityEngine;
using UnityEngine.AI;
using ZombieRush.Core;
using ZombieRush.Data;
using ZombieRush.Player;
using ZombieRush.World;

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
        bool guarding;          // every player is in the safe base: wait at its edge
        Vector3 guardPoint;
        Vector3 targetPosition;
        float retargetTimer;
        const float RetargetInterval = 0.4f;

        float attackTimer;
        float gravityVelocity;

        Vector3 avoidTangent;
        float avoidHoldTimer;
        float nextProbeTime;

        const float RepathInterval = 0.5f;
        const float DirectChaseDistance = 3f;
        NavMeshPath path; // created in Awake: Unity forbids NavMeshPath in field initializers
        Vector3[] corners = System.Array.Empty<Vector3>();
        int pathCorner = -1;
        float repathTimer;

        float stuckCheckTimer;
        Vector3 positionAtLastCheck;
        float escapeTimer;
        Vector3 escapeDir;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            health = GetComponent<ZombieHealth>();
            cfg = config != null ? config : health.Config;
            path = new NavMeshPath();
        }

        // Also runs when a pooled zombie is reused, so all steering state starts fresh.
        void OnEnable()
        {
            player = null;
            playerHealth = null;
            guarding = false;
            retargetTimer = 0f;
            attackTimer = 0f;
            gravityVelocity = 0f;
            avoidHoldTimer = 0f;
            escapeTimer = 0f;
            stuckCheckTimer = 0f;
            nextProbeTime = 0f;
            repathTimer = Random.value * RepathInterval;
            pathCorner = -1;
            positionAtLastCheck = transform.position;
        }

        /// Hunts the nearest living player who is OUT of the safe base. If every player
        /// is inside, the zombie walks to the base edge nearest them and waits there.
        void ChooseTarget()
        {
            var safeBase = SafeBase.Instance;
            PlayerHealth best = null, nearestAny = null;
            float bestSqr = float.MaxValue, anySqr = float.MaxValue;
            var pos = transform.position;

            foreach (var p in PlayerHealth.All)
            {
                if (p == null || p.IsDead) continue;
                var d = p.transform.position - pos;
                d.y = 0f;
                float sqr = d.sqrMagnitude;
                if (sqr < anySqr) { anySqr = sqr; nearestAny = p; }
                if (safeBase != null && safeBase.Contains(p.transform.position)) continue;
                if (sqr < bestSqr) { bestSqr = sqr; best = p; }
            }

            playerHealth = best;
            player = best != null ? best.transform : null;
            guarding = best == null && nearestAny != null && safeBase != null;
            if (guarding) guardPoint = safeBase.EdgePoint(nearestAny.transform.position, 1.2f + Random.value);
        }

        void Update()
        {
            if (health.IsDead) return;
            if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;

            retargetTimer -= Time.deltaTime;
            if (retargetTimer <= 0f || (player == null && !guarding) || (playerHealth != null && playerHealth.IsDead))
            {
                retargetTimer = RetargetInterval + Random.value * 0.2f;
                ChooseTarget();
            }
            if (player == null && !guarding)
            {
                health.Anim.SetSpeed(0f);
                ApplyGravityOnly();
                return;
            }

            targetPosition = guarding ? guardPoint : player.position;
            Vector3 toPlayer = targetPosition - transform.position;
            toPlayer.y = 0f;
            float sqrDistance = toPlayer.sqrMagnitude;

            if (!alwaysHunt && !guarding && sqrDistance > cfg.sightRange * cfg.sightRange)
            {
                health.Anim.SetSpeed(0f);
                ApplyGravityOnly();
                return;
            }

            attackTimer -= Time.deltaTime;

            // Waiting at the base edge: face the players inside and growl, never go in.
            if (guarding && sqrDistance <= 1f)
            {
                if (SafeBase.Instance != null) FaceDirection(SafeBase.Instance.Center - transform.position);
                health.Anim.SetSpeed(0f);
                ApplyGravityOnly();
                return;
            }

            if (!guarding && sqrDistance <= cfg.attackRange * cfg.attackRange)
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

            Vector3 desired = PathDirection(toPlayer / distance, distance);
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
            KeepOutOfSafeBase();
        }

        /// The safe base is off-limits: anything that slides inside is pushed back to the edge.
        void KeepOutOfSafeBase()
        {
            var safeBase = SafeBase.Instance;
            if (safeBase == null || !safeBase.Contains(transform.position, controller.radius)) return;
            var push = safeBase.EdgePoint(transform.position, controller.radius + 0.05f) - transform.position;
            push.y = 0f;
            controller.Move(push);
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

        /// Direction along the walk-map route to the player (around buildings), re-planned
        /// a couple of times a second. Straight at the player when close or with no walk map.
        Vector3 PathDirection(Vector3 straight, float distance)
        {
            if (!ArenaNavMesh.Ready || distance < DirectChaseDistance) return straight;

            repathTimer -= Time.deltaTime;
            if (repathTimer <= 0f)
            {
                repathTimer = RepathInterval + Random.value * 0.2f;
                pathCorner = -1;
                if (ArenaNavMesh.TrySnap(transform.position, out var from) && ArenaNavMesh.TrySnap(targetPosition, out var to, 3f)
                    && NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) && path.status != NavMeshPathStatus.PathInvalid
                    && path.corners.Length > 1)
                {
                    corners = path.corners;
                    pathCorner = 1;
                }
            }
            if (pathCorner < 0) return straight;

            // Skip corners already reached.
            while (pathCorner < corners.Length - 1 && FlatDistance(transform.position, corners[pathCorner]) < 0.8f) pathCorner++;
            Vector3 toCorner = corners[pathCorner] - transform.position;
            toCorner.y = 0f;
            return toCorner.sqrMagnitude > 0.01f ? toCorner.normalized : straight;
        }

        static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

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
                repathTimer = 0f; // re-plan the route once the escape ends
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
