using UnityEngine;

namespace ZombieRush.Player
{
    /// Drives a character's full-body Animator (Survivor.controller) from its movement: walk,
    /// run, strafe and back-pedal blend from the velocity, plus crouch, airborne, and one-shot
    /// upper-body actions (fire, reload, hit) and death. Holds the left hand on the weapon with IK.
    ///
    /// Sits on the model, next to its Animator. Reads the CharacterController and
    /// PlayerController above it when there are any; otherwise call Drive() yourself
    /// (e.g. for a networked squad mate or a menu showcase).
    [RequireComponent(typeof(Animator))]
    public class CharacterBodyAnimator : MonoBehaviour
    {
        [Tooltip("Ground speed that counts as walking (blend 1). Taken from the PlayerController when there is one.")]
        public float walkSpeed = 5f;
        [Tooltip("Ground speed that counts as running (blend 2). Taken from the PlayerController when there is one.")]
        public float runSpeed = 8f;
        [Tooltip("Smoothing of the movement blend, seconds.")]
        public float blendDamping = 0.12f;
        [Tooltip("How fast the upper-body action layer fades in and out (weight per second).")]
        public float layerFade = 8f;

        [Header("Weapon")]
        [Tooltip("Where the left hand holds the weapon (e.g. a 'LeftGrip' child on the gun). Empty = no IK.")]
        public Transform leftHandTarget;
        [Range(0f, 1f)] public float leftHandWeight = 1f;
        [Tooltip("Gun sockets made by the character setup tool. Parent weapon models here.")]
        public Transform rightHandSocket, leftHandSocket;
        [Tooltip("Eye position for a first-person camera that follows the body.")]
        public Transform eyeAnchor;

        static readonly int MoveX = Animator.StringToHash("MoveX");
        static readonly int MoveY = Animator.StringToHash("MoveY");
        static readonly int Crouch = Animator.StringToHash("Crouch");
        static readonly int Grounded = Animator.StringToHash("Grounded");
        static readonly int FireId = Animator.StringToHash("Fire");
        static readonly int ReloadId = Animator.StringToHash("Reload");
        static readonly int HitId = Animator.StringToHash("Hit");
        static readonly int DieId = Animator.StringToHash("Die");

        Animator animator;
        CharacterController body;
        PlayerController player;
        int upperLayer = -1;
        float upperBusy;   // seconds left of the current upper-body action
        float upperWeight;
        bool dead;
        float lastGrounded;
        const float CoyoteTime = 0.15f; // isGrounded flickers on slopes and steps: don't flash the air pose

        public Animator Animator => animator;

        void Awake()
        {
            animator = GetComponent<Animator>();
            body = GetComponentInParent<CharacterController>();
            player = GetComponentInParent<PlayerController>();
            if (player != null) { walkSpeed = player.walkSpeed; runSpeed = player.sprintSpeed; }
            upperLayer = animator.runtimeAnimatorController != null ? animator.GetLayerIndex("UpperBody") : -1;
        }

        void Update()
        {
            if (body != null)
            {
                if (body.isGrounded) lastGrounded = Time.time;
                Drive(body.velocity, player != null && player.IsCrouching, Time.time - lastGrounded < CoyoteTime);
            }

            if (upperLayer >= 0)
            {
                upperBusy -= Time.deltaTime;
                upperWeight = Mathf.MoveTowards(upperWeight, upperBusy > 0f ? 1f : 0f, layerFade * Time.deltaTime);
                animator.SetLayerWeight(upperLayer, upperWeight);
            }
        }

        /// Feeds the movement blend: world velocity, crouching, standing on the ground.
        public void Drive(Vector3 worldVelocity, bool crouching, bool grounded)
        {
            Vector3 local = transform.InverseTransformDirection(worldVelocity);
            local.y = 0f;
            float speed = local.magnitude;
            float amount = speed <= walkSpeed ? speed / Mathf.Max(0.01f, walkSpeed)
                                              : 1f + (speed - walkSpeed) / Mathf.Max(0.01f, runSpeed - walkSpeed);
            Vector3 dir = speed > 0.05f ? local / speed * Mathf.Min(amount, 2f) : Vector3.zero;
            animator.SetFloat(MoveX, dir.x, blendDamping, Time.deltaTime);
            animator.SetFloat(MoveY, dir.z, blendDamping, Time.deltaTime);
            animator.SetBool(Crouch, crouching);
            animator.SetBool(Grounded, grounded);
        }

        public void PlayFire() => Upper(FireId, 0.25f);
        public void PlayReload(float seconds = 2.2f) => Upper(ReloadId, seconds);
        public void PlayHit() => Upper(HitId, 0.45f);

        public void PlayDie()
        {
            if (dead) return;
            dead = true;
            upperBusy = 0f;
            animator.SetTrigger(DieId);
        }

        /// Back to life (respawn, pooled squad mate).
        public void ResetBody()
        {
            dead = false;
            animator.Rebind();
            animator.Update(0f);
        }

        void Upper(int trigger, float seconds)
        {
            if (dead) return;
            animator.SetTrigger(trigger);
            upperBusy = Mathf.Max(upperBusy, seconds);
        }

        void OnAnimatorIK(int layer)
        {
            float w = leftHandTarget != null && !dead ? leftHandWeight : 0f;
            animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, w);
            animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, w);
            if (w <= 0f) return;
            animator.SetIKPosition(AvatarIKGoal.LeftHand, leftHandTarget.position);
            animator.SetIKRotation(AvatarIKGoal.LeftHand, leftHandTarget.rotation);
        }
    }
}
