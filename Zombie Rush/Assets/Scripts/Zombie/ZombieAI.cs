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
        public ZombieConfigData config;

        /// Set by whatever spawned this zombie (e.g. WaveManager) to scale attack
        /// damage and move speed up on later, harder waves. 1 = config's base values.
        public float difficultyMultiplier = 1f;

        [Header("Obstacle avoidance")]
        public float probeDistance = 1.6f;
        public float probeRadius = 0.45f;
        public float avoidCommitTime = 0.35f; // briefly commits to a tangent direction to avoid jittering
        public LayerMask obstacleMask = ~0;

        [Header("Last-resort unstuck")]
        public float stuckCheckInterval = 0.5f;
        public float stuckDistanceThreshold = 0.15f;
        public float escapeDuration = 1.2f;

        CharacterController controller;
        ZombieHealth health;
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

        Animator anim;
        bool animResolved, hasSpeed, hasAttack;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            health = GetComponent<ZombieHealth>();

            var playerGo = GameObject.FindGameObjectWithTag(GameTags.Player);
            if (playerGo != null)
            {
                player = playerGo.transform;
                playerHealth = playerGo.GetComponent<PlayerHealth>();
            }

            positionAtLastCheck = transform.position;
        }

        void Update()
        {
            if (health.IsDead || player == null) return;
            if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;

            ResolveAnimator();

            float sightRange = config != null ? config.sightRange : 25f;
            Vector3 toPlayer = player.position - transform.position;
            toPlayer.y = 0f;
            float distance = toPlayer.magnitude;

            if (distance > sightRange)
            {
                SetMoving(false);
                ApplyGravityOnly();
                return;
            }

            float attackRange = config != null ? config.attackRange : 1.6f;
            attackTimer -= Time.deltaTime;

            if (distance <= attackRange)
            {
                FacePlayer(toPlayer);
                ApplyGravityOnly();
                SetMoving(false);

                if (attackTimer <= 0f)
                {
                    if (hasAttack) anim.SetTrigger(ZombieAnimatorUtil.AttackParam);
                    attackTimer = config != null ? config.attackCooldown : 1.1f;
                    float baseDamage = config != null ? config.attackDamage : 12f;
                    playerHealth?.TakeDamage(baseDamage * difficultyMultiplier);
                }
                return;
            }

            // Chase, steering around anything in the way.
            float runDistance = config != null ? config.runDistance : 0f;
            bool running = runDistance <= 0f || distance <= runDistance;
            float baseSpeed = config != null ? (running ? config.chaseSpeed : config.moveSpeed) : 3.5f;
            float speed = baseSpeed * Mathf.Min(difficultyMultiplier, 1.6f);
            SetMoving(true, running);
            Vector3 desired = toPlayer.normalized;
            Vector3 moveDir = ComputeMoveDirection(desired);
            FacePlayer(moveDir);

            UpdateLastResortUnstuck(moveDir);
            if (escapeTimer > 0f)
            {
                escapeTimer -= Time.deltaTime;
                moveDir = escapeDir;
            }

            Vector3 move = moveDir * speed;

            if (controller.isGrounded) gravityVelocity = -1f;
            else gravityVelocity -= 20f * Time.deltaTime;

            move.y = gravityVelocity;
            controller.Move(move * Time.deltaTime);
        }

        /// Sphere-casts along the desired direction; if something's in the way,
        /// slides along its surface (perpendicular to the hit normal, on whichever
        /// side keeps making progress toward the player) instead of pushing
        /// straight into it.
        Vector3 ComputeMoveDirection(Vector3 desired)
        {
            if (avoidHoldTimer > 0f)
            {
                avoidHoldTimer -= Time.deltaTime;
                return avoidTangent;
            }

            if (Time.time < nextProbeTime) return desired;
            nextProbeTime = Time.time + 0.08f + Random.value * 0.04f;

            Vector3 origin = transform.position + Vector3.up * 1f;
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

            float moved = Vector3.Distance(transform.position, positionAtLastCheck);
            stuckCheckTimer = 0f;
            positionAtLastCheck = transform.position;

            if (moved < stuckDistanceThreshold)
            {
                escapeTimer = escapeDuration;
                Vector3 randomDir = Quaternion.Euler(0f, Random.Range(60f, 300f), 0f) * currentMoveDir;
                escapeDir = randomDir.normalized;
            }
        }

        void ResolveAnimator()
        {
            if (animResolved) return;
            anim = GetComponentInChildren<Animator>();
            if (anim == null || anim.runtimeAnimatorController == null) return;
            animResolved = true;
            hasSpeed = ZombieAnimatorUtil.Has(anim, ZombieAnimatorUtil.SpeedParam, AnimatorControllerParameterType.Float);
            hasAttack = ZombieAnimatorUtil.Has(anim, ZombieAnimatorUtil.AttackParam, AnimatorControllerParameterType.Trigger);
        }

        void SetMoving(bool moving, bool running = true)
        {
            if (hasSpeed) anim.SetFloat(ZombieAnimatorUtil.SpeedParam, !moving ? 0f : (running ? 1f : 0.5f));
        }

        void FacePlayer(Vector3 dir)
        {
            if (dir.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir.normalized), Time.deltaTime * 8f);
        }

        void ApplyGravityOnly()
        {
            if (controller.isGrounded) gravityVelocity = -1f;
            else gravityVelocity -= 20f * Time.deltaTime;
            controller.Move(new Vector3(0f, gravityVelocity, 0f) * Time.deltaTime);
        }
    }
}
