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
        public float sprintSpeed = 8f;
        public float crouchSpeed = 2.5f;
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
            if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        /// True while crouched (drives the third-person body's crouch animation).
        public bool IsCrouching => isCrouching;

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
            if (health != null && (health.IsDead || health.InSafeBase)) return;
            if (slowTimer <= 0f || multiplier <= slowMultiplier) slowMultiplier = multiplier;
            slowTimer = Mathf.Max(slowTimer, duration);
            if (health != null && health == PlayerHealth.Instance) GameEvents.RaisePlayerSlowed(1f - slowMultiplier, slowTimer);
        }

        void Update()
        {
            if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;
            if (health != null && health.IsDead) return;   // down in a squad: wait for the next wave

            HandleLook();
            HandleCrouch();
            HandleMove();
        }

        void HandleLook()
        {
            Vector2 look = GameInput.ReadLook();
            if (look == Vector2.zero) return;

            transform.Rotate(0f, look.x, 0f);
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
            bool jumpPressed = GameInput.ConsumeJump(); // always consumed so a mid-air tap doesn't fire on landing

            if (slowTimer > 0f) slowTimer -= Time.deltaTime;
            float speed = (isCrouching ? crouchSpeed : (sprinting ? sprintSpeed : walkSpeed)) * SlowMultiplier;
            Vector3 move = transform.right * input.x + transform.forward * input.y;

            if (controller.isGrounded)
            {
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
        }
    }
}
