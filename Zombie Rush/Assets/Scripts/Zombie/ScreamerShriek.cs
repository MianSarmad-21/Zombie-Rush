using UnityEngine;
using ZombieRush.Audio;
using ZombieRush.Core;
using ZombieRush.Data;
using ZombieRush.Effects;
using ZombieRush.Player;

namespace ZombieRush.Zombie
{
    /// Screamer: hangs back behind the horde and shrieks, throwing her head back with arms
    /// flung wide. Every zombie in the radius goes into a frenzy (faster moves and attacks).
    /// A priority target. Cornered, she claws in melee.
    [RequireComponent(typeof(ZombieAI))]
    [DefaultExecutionOrder(110)] // poses after JuggernautMotion and the Animator
    public class ScreamerShriek : MonoBehaviour, IZombieAbility
    {
        public ScreamerConfig config;

        [Header("Shriek pose (degrees)")]
        public float headBack = 35f;
        public float chestBack = 14f;
        public float armsOut = 55f;

        ZombieHealth health;
        ZombieAI ai;
        Animator animator;
        Transform headBone, neckBone, chestBone, leftArm, rightArm;
        float cooldown;
        float shriekTime = -1f; // seconds into the current shriek, < 0 when not shrieking
        bool released;

        public bool BlocksMelee => false;
        public float SpeedMultiplier => 1f;
        public bool IsShrieking => shriekTime >= 0f;

        void Awake()
        {
            health = GetComponent<ZombieHealth>();
            ai = GetComponent<ZombieAI>();
            if (config == null) config = ScriptableObject.CreateInstance<ScreamerConfig>();
        }

        void OnEnable()
        {
            cooldown = Random.Range(1.5f, 3f);
            shriekTime = -1f;
        }

        public bool Tick(PlayerHealth target, Vector3 toTarget, float distance)
        {
            cooldown -= Time.deltaTime;
            if (IsShrieking) return true;
            if (distance <= config.meleeRange) return false;

            if (cooldown <= 0f && distance <= config.triggerRange)
            {
                shriekTime = 0f;
                released = false;
                return true;
            }
            return distance <= config.keepDistance;
        }

        void Update()
        {
            if (!IsShrieking) return;
            if (health.IsDead) { shriekTime = -1f; return; }

            shriekTime += Time.deltaTime;
            if (!released && shriekTime >= config.windup) Release();
            if (shriekTime >= config.duration)
            {
                shriekTime = -1f;
                cooldown = config.cooldown;
            }
        }

        void Release()
        {
            released = true;
            Vector3 c = transform.position;
            ShockwaveEffect.Spawn(c, config.radius, ShockwaveEffect.ShriekColor, 0.7f);
            var lib = SoundManager.Library;
            if (lib != null) SoundManager.PlayAt(lib.shriek, c + Vector3.up * 1.7f, 1f, 60f);
            CameraShake.AddAt(c, config.cameraShake, config.shakeRange);

            float r2 = config.radius * config.radius;
            foreach (var z in ZombieHealth.Active)
            {
                if (z == null || z.IsDead || z == health) continue;
                if ((z.transform.position - c).sqrMagnitude > r2) continue;
                var zai = z.GetComponent<ZombieAI>();
                if (zai != null) zai.Frenzy(config.speedMultiplier, config.frenzyDuration);
            }
        }

        /// The pose is laid over whatever the Animator played this frame.
        void LateUpdate()
        {
            if (!IsShrieking || !ResolveBones()) return;
            float t = shriekTime;
            float w = Mathf.SmoothStep(0f, 1f, t / Mathf.Max(0.05f, config.windup))
                      * Mathf.SmoothStep(0f, 1f, (config.duration - t) / 0.35f);
            if (w <= 0f) return;

            // Shudder while the shriek is on.
            float shake = t > config.windup ? Mathf.Sin(t * 60f) * 2f : 0f;
            Vector3 right = transform.right, fwd = transform.forward;
            Bend(chestBone, right, -chestBack * w);
            Bend(neckBone, right, -headBack * 0.4f * w);
            Bend(headBone, right, (-headBack * 0.6f + shake) * w);
            Bend(leftArm, fwd, -armsOut * w);
            Bend(rightArm, fwd, armsOut * w);
        }

        static void Bend(Transform bone, Vector3 axis, float degrees)
        {
            if (bone != null) bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
        }

        bool ResolveBones()
        {
            if (animator != null) return true;
            if (!health.Anim.Resolve() || !health.Anim.Animator.isHuman) return false;
            animator = health.Anim.Animator;
            headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            neckBone = animator.GetBoneTransform(HumanBodyBones.Neck);
            chestBone = animator.GetBoneTransform(HumanBodyBones.Chest);
            if (chestBone == null) chestBone = animator.GetBoneTransform(HumanBodyBones.Spine);
            leftArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            rightArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            return true;
        }
    }
}
