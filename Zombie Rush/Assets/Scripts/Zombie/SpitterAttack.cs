using UnityEngine;
using ZombieRush.Audio;
using ZombieRush.Data;
using ZombieRush.Player;

namespace ZombieRush.Zombie
{
    /// Spitter: keeps its distance and lobs sticky goo at the player. Closer than minRange it
    /// gives up and claws in melee like any other zombie.
    [RequireComponent(typeof(ZombieAI))]
    public class SpitterAttack : MonoBehaviour, IZombieAbility
    {
        public SpitterConfig config;

        ZombieHealth health;
        ZombieAI ai;
        Transform head;
        float cooldown;
        float windup;       // > 0 while rearing back to spit
        PlayerHealth aimAt;
        static readonly RaycastHit[] sightHits = new RaycastHit[8];

        public bool BlocksMelee => false;
        public float SpeedMultiplier => 1f;

        void Awake()
        {
            health = GetComponent<ZombieHealth>();
            ai = GetComponent<ZombieAI>();
            if (config == null) config = ScriptableObject.CreateInstance<SpitterConfig>();
        }

        void OnEnable()
        {
            cooldown = Random.Range(0.8f, 1.6f); // a fresh spawn takes a moment before the first spit
            windup = 0f;
        }

        public bool Tick(PlayerHealth target, Vector3 toTarget, float distance)
        {
            cooldown -= Time.deltaTime * ai.FrenzyMultiplier;

            if (windup > 0f)
            {
                windup -= Time.deltaTime;
                if (windup <= 0f) Spit();
                return true;
            }

            if (distance < config.minRange || distance > config.maxRange) return false;

            if (cooldown <= 0f && (!config.needsLineOfSight || CanSee(target)))
            {
                aimAt = target;
                windup = config.windup;
                health.Anim.TriggerAttack();
                return true;
            }
            // Stand its ground at spitting range instead of walking into melee.
            return distance <= config.keepDistance;
        }

        Vector3 Mouth()
        {
            if (head == null && health.Anim.Resolve() && health.Anim.Animator.isHuman)
                head = health.Anim.Animator.GetBoneTransform(HumanBodyBones.Head);
            return head != null ? head.position + transform.forward * 0.2f : transform.position + Vector3.up * 1.6f + transform.forward * 0.3f;
        }

        bool CanSee(PlayerHealth target)
        {
            Vector3 from = Mouth();
            Vector3 to = target.transform.position + Vector3.up * 1.2f;
            Vector3 d = to - from;
            float len = d.magnitude;
            int n = Physics.RaycastNonAlloc(from, d / len, sightHits, len, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = sightHits[i].collider;
                if (c.GetComponentInParent<ZombieHealth>() != null || c.GetComponentInParent<PlayerHealth>() != null) continue;
                return false;
            }
            return true;
        }

        void Spit()
        {
            cooldown = config.cooldown;
            if (aimAt == null || aimAt.IsDead) return;

            Vector3 from = Mouth();
            Vector3 target = aimAt.transform.position + Vector3.up * 1.0f;
            var cc = aimAt.GetComponent<CharacterController>();
            if (cc != null)
            {
                float flight = Vector3.Distance(from, target) / Mathf.Max(1f, config.projectileSpeed);
                Vector3 v = cc.velocity;
                v.y = 0f;
                target += v * flight * config.lead;
            }
            GooProjectile.Launch(from, target, config, ai.difficultyMultiplier);

            var lib = SoundManager.Library;
            if (lib != null) SoundManager.PlayAt(lib.gooSpit, from, 1f, 30f);
        }
    }
}
