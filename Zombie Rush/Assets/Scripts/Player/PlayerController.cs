using UnityEngine;
using ZombieRush.Core;

namespace ZombieRush.Player
{
    /// First-person movement + mouse look. Legacy Input Manager on purpose
    /// (Project Settings > Player > Active Input Handling must be "Both" or
    /// "Input Manager (Old)" for these Input.* calls to work).
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
        public float mouseSensitivity = 2f;
        public float lookPitchMin = -85f;
        public float lookPitchMax = 85f;

        [Header("Crouch")]
        public float standHeight = 1.8f;
        public float crouchHeight = 1.0f;
        public float standCameraY = 1.6f;
        public float crouchCameraY = 0.9f;

        CharacterController controller;
        float pitch;
        float verticalVelocity;
        bool isCrouching;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
        }

        void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
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
            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

            transform.Rotate(Vector3.up * mouseX);

            pitch = Mathf.Clamp(pitch - mouseY, lookPitchMin, lookPitchMax);
            if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        void HandleCrouch()
        {
            isCrouching = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);

            controller.height = Mathf.MoveTowards(controller.height, isCrouching ? crouchHeight : standHeight, Time.deltaTime * 8f);
            controller.center = new Vector3(0f, controller.height * 0.5f, 0f);

            if (cameraPivot != null)
            {
                float targetY = isCrouching ? crouchCameraY : standCameraY;
                var pos = cameraPivot.localPosition;
                pos.y = Mathf.MoveTowards(pos.y, targetY, Time.deltaTime * 8f);
                cameraPivot.localPosition = pos;
            }
        }

        void HandleMove()
        {
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            bool sprinting = Input.GetKey(KeyCode.LeftShift) && !isCrouching && v > 0f;

            float speed = isCrouching ? crouchSpeed : (sprinting ? sprintSpeed : walkSpeed);
            Vector3 move = (transform.right * h + transform.forward * v);
            if (move.sqrMagnitude > 1f) move.Normalize();

            if (controller.isGrounded)
            {
                verticalVelocity = -1f;
                if (Input.GetButtonDown("Jump") && !isCrouching)
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
