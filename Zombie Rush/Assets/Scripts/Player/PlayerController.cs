using UnityEngine;
using ZombieRush.Core;

namespace ZombieRush.Player
{
    /// First-person movement + look. Reads everything through GameInput, so the
    /// same code drives keyboard/mouse and the on-screen touch controls.
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("References")]
        public Transform cameraPivot;

        [Header("Move")]
        public float walkSpeed = 4f;
        public float sprintSpeed = 6.5f;
        public float crouchSpeed = 2.5f;
        [Tooltip("Side-step speed as a share of the move speed (A/D).")]
        [Range(0.5f, 1f)] public float strafeSpeedFactor = 0.75f;
        [Tooltip("Backpedal speed as a share of the move speed (S).")]
        [Range(0.5f, 1f)] public float backSpeedFactor = 0.75f;
        public float jumpHeight = 1.2f;
        public float gravity = -20f;

        [Tooltip("How fast the player speeds up, m/s per second. Real people need a moment to get going.")]
        public float acceleration = 12f;
        [Tooltip("How fast the player slows down, m/s per second.")]
        public float deceleration = 16f;
        [Tooltip("No sprinting while the trigger is held, nor this many seconds after letting go.")]
        public float sprintBlockAfterFire = 0.35f;

        [Header("Look")]
        public float lookPitchMin = -85f;
        public float lookPitchMax = 85f;

        [Header("Crouch")]
        public float standHeight = 1.8f;
        public float crouchHeight = 1.0f;
        public float standCameraY = 1.6f;
        public float crouchCameraY = 0.9f;
        public float crouchTransitionSpeed = 8f;

        CharacterController controller;
        PlayerHealth health;
        float slowMultiplier = 1f;
        float slowTimer;
        float pitch;
        float verticalVelocity;
        bool isCrouching;
        float sprintBlockedUntil;
        Vector3 planarVelocity;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            health = GetComponent<PlayerHealth>();
        }

        float pitchMin, pitchMax;
        bool pitchOverride;

        /// Narrows the look up/down range (third person); ResetPitchLimits goes back to the defaults.
        public void SetPitchLimits(float min, float max)
        {
            pitchMin = min; pitchMax = max; pitchOverride = true;
            ClampPitch();
        }

        public void ResetPitchLimits()
        {
            pitchOverride = false;
            ClampPitch();
        }

        void ClampPitch()
        {
            pitch = Mathf.Clamp(pitch, pitchOverride ? pitchMin : lookPitchMin, pitchOverride ? pitchMax : lookPitchMax);
            ApplyPivot();
        }

        // ---- recoil: a view kick on top of the look angle (the shots go where the camera points, so it counts)
        [Header("Recoil")]
        [Tooltip("Seconds the view takes to snap up to a kick.")]
        public float recoilRise = 0.035f;
        [Tooltip("Seconds the view takes to settle back after the shooting stops.")]
        public float recoilSettle = 0.22f;
        [Tooltip("Most degrees a long burst can climb.")]
        public float recoilMax = 4f;
        Vector2 recoilWant, recoilNow, recoilVel;   // x = degrees up, y = degrees right

        /// One shot's kick: `up` degrees up, `side` degrees right (negative = left).
        public void AddRecoil(float up, float side)
        {
            recoilWant.x = Mathf.Min(recoilWant.x + up, recoilMax);
            recoilWant.y = Mathf.Clamp(recoilWant.y + side, -recoilMax * 0.5f, recoilMax * 0.5f);
        }

        void UpdateRecoil()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            recoilNow = Vector2.SmoothDamp(recoilNow, recoilWant, ref recoilVel, recoilRise, Mathf.Infinity, dt);
            // the wanted kick drains back to rest all the time, so single shots bob and a burst climbs a little
            recoilWant *= Mathf.Exp(-dt / Mathf.Max(0.01f, recoilSettle));
            if (recoilWant.sqrMagnitude < 1e-6f && recoilNow.sqrMagnitude < 1e-6f) { recoilWant = recoilNow = recoilVel = Vector2.zero; }
            ApplyPivot();
        }

        void ApplyPivot()
        {
            if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch - recoilNow.x, recoilNow.y, 0f);
        }

        /// True while crouched (drives the third-person body's crouch animation).
        public bool IsCrouching => isCrouching;

        /// True while sprinting forward (the camera widens a little).
        public bool IsSprinting { get; private set; }

        bool wasGrounded = true;

        /// Look up/down angle, degrees (down is positive).
        public float Pitch => pitch;

        /// A teammate's copy (multiplayer): crouch and look angle come from the network, not input.
        public void SetRemoteState(bool crouching, float lookPitch)
        {
            isCrouching = crouching;
            pitch = lookPitch;
            if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            float h = crouching ? crouchHeight : standHeight;
            if (controller != null && !Mathf.Approximately(controller.height, h))
            {
                controller.height = Mathf.MoveTowards(controller.height, h, Time.deltaTime * crouchTransitionSpeed);
                controller.center = new Vector3(0f, controller.height * 0.5f, 0f);
            }
        }

        /// Current speed factor from sticky goo and the like (1 = free).
        public float SlowMultiplier => slowTimer > 0f ? slowMultiplier : 1f;

        /// Slows this player for a while (Spitter goo). The strongest active slow wins.
        /// Ignored inside the safe base, where nothing can touch the player.
        public void ApplySlow(float multiplier, float duration)
        {
            if (health != null && (health.IsDead || health.Protected)) return;
            if (slowTimer <= 0f || multiplier <= slowMultiplier) slowMultiplier = multiplier;
            slowTimer = Mathf.Max(slowTimer, duration);
            if (health != null && health == PlayerHealth.Instance) GameEvents.RaisePlayerSlowed(1f - slowMultiplier, slowTimer);
        }

        void Update()
        {
            if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;
            if (health != null && health.IsDead) return;   // down in a squad: wait for the next wave

            HandleLook();
            UpdateRecoil();
            HandleCrouch();
            HandleMove();
        }

        void HandleLook()
        {
            Vector2 look = GameInput.ReadLook();
            if (look == Vector2.zero) return;

            transform.Rotate(0f, look.x, 0f);
            // the momentum turns with the player: running forward stays forward while turning (otherwise the
            // lagging velocity is briefly sideways and the legs blend into strafe/diagonal steps: a zig-zag)
            planarVelocity = Quaternion.Euler(0f, look.x, 0f) * planarVelocity;
            pitch -= look.y;
            ClampPitch();
        }

        void HandleCrouch()
        {
            isCrouching = GameInput.CrouchHeld;

            float targetHeight = isCrouching ? crouchHeight : standHeight;
            if (!Mathf.Approximately(controller.height, targetHeight))
            {
                controller.height = Mathf.MoveTowards(controller.height, targetHeight, Time.deltaTime * crouchTransitionSpeed);
                controller.center = new Vector3(0f, controller.height * 0.5f, 0f);
            }

            if (cameraPivot != null)
            {
                float targetY = isCrouching ? crouchCameraY : standCameraY;
                var pos = cameraPivot.localPosition;
                if (!Mathf.Approximately(pos.y, targetY))
                {
                    pos.y = Mathf.MoveTowards(pos.y, targetY, Time.deltaTime * crouchTransitionSpeed);
                    cameraPivot.localPosition = pos;
                }
            }
        }

        void HandleMove()
        {
            Vector2 input = GameInput.Move;
            if (GameInput.FireHeld) sprintBlockedUntil = Time.time + sprintBlockAfterFire;
            bool sprinting = GameInput.SprintHeld && !isCrouching && input.y > 0f && Time.time >= sprintBlockedUntil;
            IsSprinting = sprinting && planarVelocity.sqrMagnitude > walkSpeed * walkSpeed * 0.8f;
            bool jumpPressed = GameInput.ConsumeJump(); // always consumed so a mid-air tap doesn't fire on landing

            if (slowTimer > 0f) slowTimer -= Time.deltaTime;
            float speed = (isCrouching ? crouchSpeed : (sprinting ? sprintSpeed : walkSpeed)) * SlowMultiplier;
            // sideways and backwards slower than forwards, as in most shooters: a body facing the aim can't
            // side-step or backpedal at a full run, and the strafe/backpedal animations are made for these speeds
            float fwd = input.y >= 0f ? input.y : input.y * backSpeedFactor;
            Vector3 move = transform.right * (input.x * strafeSpeedFactor) + transform.forward * fwd;

            if (controller.isGrounded)
            {
                if (!wasGrounded) Land(-verticalVelocity);
                verticalVelocity = -1f;
                if (jumpPressed && !isCrouching)
                    verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
            else
            {
                verticalVelocity += gravity * Time.deltaTime;
            }

            // Ease into and out of the move speed instead of jumping to it in one frame.
            Vector3 targetPlanar = move * speed;
            float rate = targetPlanar.sqrMagnitude >= planarVelocity.sqrMagnitude ? acceleration : deceleration;
            planarVelocity = Vector3.MoveTowards(planarVelocity, targetPlanar, rate * Time.deltaTime);
            Vector3 velocity = planarVelocity + Vector3.up * verticalVelocity;
            controller.Move(velocity * Time.deltaTime);
            wasGrounded = controller.isGrounded;
        }

        /// Touched down at `fallSpeed` m/s: a hard landing dips the view (and jolts it), harder ones more.
        void Land(float fallSpeed)
        {
            var feel = ZombieRush.Data.FeelConfig.Current;
            if (fallSpeed < feel.landingMinSpeed) return;
            float k = Mathf.Clamp01((fallSpeed - feel.landingMinSpeed) / 8f) * 0.7f + 0.3f;
            AddRecoil(-feel.landingDip * k, 0f);   // a negative kick: the view drops, then springs back
            CameraShake.Add(0.15f * k);
        }
    }
}
