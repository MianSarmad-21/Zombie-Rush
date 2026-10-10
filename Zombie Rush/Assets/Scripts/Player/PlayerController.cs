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
        public float walkSpeed = 5f;
        public float sprintSpeed = 8f;
        public float crouchSpeed = 2.5f;
        public float jumpHeight = 1.2f;
        public float gravity = -20f;

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
        float pitch;
        float verticalVelocity;
        bool isCrouching;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
        }

        void Update()
        {
            if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;

            HandleLook();
            HandleCrouch();
            HandleMove();
        }

        void HandleLook()
        {
            Vector2 look = GameInput.ReadLook();
            if (look == Vector2.zero) return;

            transform.Rotate(0f, look.x, 0f);
            pitch = Mathf.Clamp(pitch - look.y, lookPitchMin, lookPitchMax);
            if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
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
            bool sprinting = GameInput.SprintHeld && !isCrouching && input.y > 0f;
            bool jumpPressed = GameInput.ConsumeJump(); // always consumed so a mid-air tap doesn't fire on landing

            float speed = isCrouching ? crouchSpeed : (sprinting ? sprintSpeed : walkSpeed);
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

            Vector3 velocity = move * speed + Vector3.up * verticalVelocity;
            controller.Move(velocity * Time.deltaTime);
        }
    }
}
