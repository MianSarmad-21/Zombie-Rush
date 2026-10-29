using UnityEngine;

namespace ZombieRush.Zombie
{
    /// Keeps a zombie's feet on the real ground (humanoid foot IK). The animations are made on
    /// flat ground, but the map has kerbs, ramps and rubble, and the CharacterController stands on
    /// one height - so on uneven ground a foot hangs in the air or sinks in. Every frame this:
    ///   1. finds the ground under each foot (raycast, ignoring the zombie itself);
    ///   2. lowers or raises the body so the lower foot touches it;
    ///   3. pins any foot that is (nearly) down onto the ground and tilts it to the slope.
    /// Feet that are clearly lifted (mid-step) are left alone. Off while rising out of the ground
    /// and once dead. Added at runtime by ZombieHealth; needs "IK Pass" on the controller's base
    /// layer (set by the zombie tools).
    [RequireComponent(typeof(Animator))]
    public class ZombieFootIK : MonoBehaviour
    {
        [Tooltip("A foot this close to the ground (share of the zombie's height) is planted on it.")]
        public float plantRange = 0.05f;
        [Tooltip("How far up/down the body may move to reach the ground (share of height).")]
        public float maxBodyShift = 0.15f;
        public LayerMask groundMask = ~0;

        Animator animator;
        ZombieHealth health;
        CharacterController controller;
        float height = 2f;
        float bodyShift;
        float weight;
        readonly RaycastHit[] hits = new RaycastHit[8];

        public void Init(ZombieHealth owner)
        {
            health = owner;
            controller = owner.GetComponent<CharacterController>();
            if (controller != null) height = controller.height;
        }

        void Awake() => animator = GetComponent<Animator>();

        void OnEnable() { bodyShift = 0f; weight = 0f; }

        bool Active => health != null && !health.IsDead && (controller == null || controller.enabled);

        void OnAnimatorIK(int layerIndex)
        {
            if (animator == null || !animator.isHuman) return;
            float dt = Time.deltaTime;
            weight = Mathf.MoveTowards(weight, Active ? 1f : 0f, dt * 4f);
            if (weight <= 0f) { bodyShift = 0f; return; }

            Vector3 up = transform.up;
            Vector3 left = animator.GetIKPosition(AvatarIKGoal.LeftFoot);
            Vector3 right = animator.GetIKPosition(AvatarIKGoal.RightFoot);
            float soleL = animator.leftFeetBottomHeight * animator.humanScale;
            float soleR = animator.rightFeetBottomHeight * animator.humanScale;
            bool hitL = Ground(left, out var gL, out var nL);
            bool hitR = Ground(right, out var gR, out var nR);
            if (!hitL && !hitR) return;

            // How far each sole is above (+) or below (-) the ground under it.
            float gapL = hitL ? Vector3.Dot(left - gL, up) - soleL : float.MaxValue;
            float gapR = hitR ? Vector3.Dot(right - gR, up) - soleR : float.MaxValue;

            // Move the body so the lower foot touches (smoothed; limited so it never folds up).
            float target = -Mathf.Clamp(Mathf.Min(gapL, gapR), -maxBodyShift * height, maxBodyShift * height);
            bodyShift = Mathf.Lerp(bodyShift, target, 1f - Mathf.Exp(-12f * dt));
            animator.bodyPosition += up * (bodyShift * weight);

            Plant(AvatarIKGoal.LeftFoot, hitL, left, gapL, gL, nL, soleL, up);
            Plant(AvatarIKGoal.RightFoot, hitR, right, gapR, gR, nR, soleR, up);
        }

        /// A foot near the ground (after the body moved) is put exactly on it, aligned to the slope.
        void Plant(AvatarIKGoal goal, bool hit, Vector3 foot, float gap, Vector3 ground, Vector3 normal, float sole, Vector3 up)
        {
            if (!hit) return;
            float range = plantRange * height;
            float gapAfter = gap + bodyShift;
            float w = (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(range * 0.6f, range, gapAfter))) * weight;
            if (w <= 0f) return;
            Vector3 onGround = foot + up * (bodyShift - gapAfter);
            animator.SetIKPositionWeight(goal, w);
            animator.SetIKPosition(goal, onGround);
            animator.SetIKRotationWeight(goal, w * 0.8f);
            animator.SetIKRotation(goal, Quaternion.FromToRotation(up, normal) * animator.GetIKRotation(goal));
        }

        /// Ground right under a foot, skipping the zombie's own colliders and triggers.
        bool Ground(Vector3 foot, out Vector3 point, out Vector3 normal)
        {
            point = foot; normal = transform.up;
            Vector3 up = transform.up;
            float reach = maxBodyShift * height + plantRange * height;
            Vector3 origin = foot + up * reach;
            int n = Physics.RaycastNonAlloc(origin, -up, hits, reach * 2f + 0.5f, groundMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Transform self = health != null ? health.transform : transform.root;
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (h.collider.transform.IsChildOf(self) || h.distance >= best) continue;
                best = h.distance; point = h.point; normal = h.normal;
            }
            return best < float.MaxValue;
        }
    }
}
