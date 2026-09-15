using UnityEngine;
using ZombieRush.Core;
using ZombieRush.Data;
using ZombieRush.Player;

namespace ZombieRush.Zombie
{
    /// Simple always-chase zombie: no navmesh, just walks straight at the player
    /// (fine for an open map; obstacle-avoidance can come later once there's real
    /// level geometry worth routing around).
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(ZombieHealth))]
    public class ZombieAI : MonoBehaviour
    {
        public ZombieConfigData config;

        CharacterController controller;
        ZombieHealth health;
        Transform player;
        PlayerHealth playerHealth;

        float attackTimer;
        float gravityVelocity;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            health = GetComponent<ZombieHealth>();

            var playerGo = GameObject.FindGameObjectWithTag("Player");
            if (playerGo != null)
            {
                player = playerGo.transform;
                playerHealth = playerGo.GetComponent<PlayerHealth>();
            }
        }

        void Update()
        {
            if (health.IsDead || player == null) return;
            if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;

            float sightRange = config != null ? config.sightRange : 25f;
            Vector3 toPlayer = player.position - transform.position;
            toPlayer.y = 0f;
            float distance = toPlayer.magnitude;

            if (distance > sightRange)
            {
                ApplyGravityOnly();
                return;
            }

            float attackRange = config != null ? config.attackRange : 1.6f;
            attackTimer -= Time.deltaTime;

            if (distance <= attackRange)
            {
                FacePlayer(toPlayer);
                ApplyGravityOnly();

                if (attackTimer <= 0f)
                {
                    attackTimer = config != null ? config.attackCooldown : 1.1f;
                    playerHealth?.TakeDamage(config != null ? config.attackDamage : 12f);
                }
                return;
            }

            // Chase.
            FacePlayer(toPlayer);
            float speed = config != null ? config.chaseSpeed : 3.5f;
            Vector3 move = toPlayer.normalized * speed;

            if (controller.isGrounded) gravityVelocity = -1f;
            else gravityVelocity -= 20f * Time.deltaTime;

            move.y = gravityVelocity;
            controller.Move(move * Time.deltaTime);
        }

        void FacePlayer(Vector3 toPlayer)
        {
            if (toPlayer.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toPlayer.normalized), Time.deltaTime * 8f);
        }

        void ApplyGravityOnly()
        {
            if (controller.isGrounded) gravityVelocity = -1f;
            else gravityVelocity -= 20f * Time.deltaTime;
            controller.Move(new Vector3(0f, gravityVelocity, 0f) * Time.deltaTime);
        }
    }
}
