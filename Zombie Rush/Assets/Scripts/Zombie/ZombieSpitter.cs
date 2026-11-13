using UnityEngine;
using ZombieRush.Audio;
using ZombieRush.Data;
using ZombieRush.Effects;
using ZombieRush.Player;

namespace ZombieRush.Zombie
{
    /// Poison zombie: it stops at range, plays Spit and lobs an acid glob at the target. The glob leaves
    /// the mouth when the clip does (PoisonConfig.releaseTime). With PoisonConfig.rangedOnly it never claws:
    /// ZombieAI holds it at holdRange while it has a clear shot. Its death burst lives in ToxicBurst.
    [RequireComponent(typeof(ZombieAI), typeof(ZombieHealth))]
    public class ZombieSpitter : MonoBehaviour
    {
        public PoisonConfig config;

        ZombieAI ai;
        ZombieHealth health;
        Transform head;
        float cooldown, releaseTimer = -1f;
        Vector3 aimAt;

        void Awake()
        {
            ai = GetComponent<ZombieAI>();
            health = GetComponent<ZombieHealth>();
        }

        bool shot;
        float shotCheck;
        const float ShotCheckInterval = 0.25f;

        void Start()
        {
            if (config == null) return;
            ai.rangedOnly = config.rangedOnly;
            ai.holdRange = config.holdRange;
            ai.hasShot = () => shot;
        }

        void OnEnable()
        {
            shot = false; shotCheck = 0f;
            cooldown = config != null ? config.firstSpitDelay : 1.5f;
            releaseTimer = -1f;
            head = null;
        }

        Vector3 Mouth()
        {
            if (head == null)
            {
                var anim = GetComponentInChildren<Animator>();
                if (anim != null && anim.isHuman) head = anim.GetBoneTransform(HumanBodyBones.Head);
            }
            return head != null ? head.position + transform.forward * 0.18f : transform.position + Vector3.up * 1.6f + transform.forward * 0.3f;
        }

        void Update()
        {
            if (config == null || health.IsDead) return;

            // the glob leaves the mouth at the clip's release moment
            if (releaseTimer >= 0f)
            {
                releaseTimer -= Time.deltaTime;
                if (releaseTimer < 0f) Release();
                return;
            }

            cooldown -= Time.deltaTime;
            var target = ai.Target;
            shotCheck -= Time.deltaTime;
            if (shotCheck <= 0f)
            {
                shotCheck = ShotCheckInterval;
                shot = target != null && ClearShot(Mouth(), target.position + Vector3.up * 1.2f);
            }
            if (cooldown > 0f || target == null || ai.IsGuarding) return;
            Vector3 to = target.position - transform.position;
            to.y = 0f;
            float d = to.magnitude;
            if (d < config.spitMinRange || d > config.spitMaxRange) return;
            Vector3 chest = target.position + Vector3.up * 1.2f;
            if (!ClearShot(Mouth(), chest)) return;

            // stand, face the target, spit
            ai.Hold(config.spitDuration);
            health.Anim.TriggerSpit();
            var lib = SoundManager.Library;
            if (lib != null) SoundManager.PlayAt(lib.zombieAttack, transform.position + Vector3.up * 1.5f, 0.8f, 25f, 3);
            releaseTimer = config.releaseTime;
            cooldown = config.spitCooldown;
        }

        void Release()
        {
            var target = ai.Target;
            if (target == null) return;
            // lead the target a little along where it is heading
            var cc = target.GetComponent<CharacterController>();
            Vector3 vel = cc != null ? cc.velocity : Vector3.zero;
            vel.y = 0f;
            Vector3 from = Mouth();
            float dist = Vector3.Distance(from, target.position);
            float t = Mathf.Lerp(config.flightTime.x, config.flightTime.y, Mathf.InverseLerp(config.spitMinRange, config.spitMaxRange, dist));
            aimAt = target.position + vel * t * config.lead + Vector3.up * 0.4f;
            AcidGlob.Launch(from, aimAt, t, config, transform);
            var zn = GetComponent<ZombieRush.Net.ZombieNet>();
            if (zn != null) zn.BroadcastGlob(from, aimAt, t);
            var lib = SoundManager.Library;
            if (lib != null && lib.gooSpit != null) SoundManager.PlayAt(lib.gooSpit, from, 1f, 25f);
        }

        /// Nothing solid between the mouth and the target, zombies ignored.
        bool ClearShot(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            foreach (var h in Physics.RaycastAll(from, d.normalized, d.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.collider.GetComponentInParent<ZombieHealth>() != null) continue;
                if (h.collider.GetComponentInParent<PlayerHealth>() != null) continue;
                return false;
            }
            return true;
        }
    }
}
