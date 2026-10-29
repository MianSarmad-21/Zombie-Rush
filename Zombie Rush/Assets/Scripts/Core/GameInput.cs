using UnityEngine;

namespace ZombieRush.Core
{
    public enum TouchAction { Fire, Reload, Jump, Crouch, SwapWeapon, ExpandZone, Pause, Turret } // append only: stored as ints in the scene

    /// The one place gameplay reads input from. Keyboard/mouse comes from the
    /// legacy Input Manager; the on-screen touch widgets write into this class.
    /// Gameplay code therefore never knows (or cares) which scheme is active.
    ///
    /// "Consume" methods return a one-shot press and clear it, so each action
    /// must have exactly one reader.
    public static class GameInput
    {
        /// Degrees turned by dragging one full screen height at sensitivity 1.
        const float TouchDegreesPerScreen = 35f;

        static Vector2 touchMove;
        static Vector2 touchLookPixels;
        static bool touchFireHeld;
        static bool touchCrouchToggled;
        static bool reloadQueued, jumpQueued, swapQueued, expandQueued, pauseQueued, turretQueued;

        static bool MouseActive => !GameSettings.UseTouchControls;

        // ---- Written by touch widgets ------------------------------------------------

        public static void SetTouchMove(Vector2 value) => touchMove = Vector2.ClampMagnitude(value, 1f);
        public static void AddTouchLook(Vector2 pixelDelta) => touchLookPixels += pixelDelta;

        public static void TouchPress(TouchAction action)
        {
            switch (action)
            {
                case TouchAction.Fire: touchFireHeld = true; break;
                case TouchAction.Crouch: touchCrouchToggled = !touchCrouchToggled; break;
                case TouchAction.Reload: reloadQueued = true; break;
                case TouchAction.Jump: jumpQueued = true; break;
                case TouchAction.SwapWeapon: swapQueued = true; break;
                case TouchAction.ExpandZone: expandQueued = true; break;
                case TouchAction.Pause: pauseQueued = true; break;
                case TouchAction.Turret: turretQueued = true; break;
            }
        }

        public static void TouchRelease(TouchAction action)
        {
            if (action == TouchAction.Fire) touchFireHeld = false;
        }

        public static bool TouchCrouchActive => touchCrouchToggled;

        /// Drops held/queued touch state, e.g. when the controls are hidden or the game pauses,
        /// so nothing stays "stuck down" after a finger was lifted off-screen.
        public static void ResetTouchState()
        {
            touchMove = Vector2.zero;
            touchLookPixels = Vector2.zero;
            touchFireHeld = false;
            ClearQueued();
        }

        public static void ClearQueued()
        {
            reloadQueued = jumpQueued = swapQueued = expandQueued = pauseQueued = turretQueued = false;
        }

        // ---- Read by gameplay ---------------------------------------------------------

        public static Vector2 Move
        {
            get
            {
                var keys = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
                return Vector2.ClampMagnitude(keys + touchMove, 1f);
            }
        }

        /// Look delta in degrees for this frame (x = yaw, y = pitch-up). Clears accumulated touch drag.
        public static Vector2 ReadLook()
        {
            float sensitivity = GameSettings.LookSensitivity;
            Vector2 look = Vector2.zero;

            if (MouseActive)
                look = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * sensitivity;

            if (touchLookPixels != Vector2.zero)
            {
                float scale = TouchDegreesPerScreen * sensitivity / Mathf.Max(1, Screen.height);
                look += touchLookPixels * scale;
                touchLookPixels = Vector2.zero;
            }

            if (GameSettings.InvertY) look.y = -look.y;
            return look;
        }

        public static bool FireHeld => touchFireHeld || (MouseActive && Input.GetMouseButton(0));

        /// Keyboard sprint, or on touch: stick pushed (almost) fully forward.
        public static bool SprintHeld => Input.GetKey(KeyCode.LeftShift) || (touchMove.y > 0.8f && touchMove.sqrMagnitude > 0.9f);

        public static bool CrouchHeld => touchCrouchToggled || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);

        public static bool ConsumeJump() => Consume(ref jumpQueued) | Input.GetButtonDown("Jump");
        public static bool ConsumeReload() => Consume(ref reloadQueued) | Input.GetKeyDown(KeyCode.R);
        public static bool ConsumeSwapWeapon() => Consume(ref swapQueued) | (MouseActive && Input.mouseScrollDelta.y != 0f);
        public static bool ConsumeExpandZone() => Consume(ref expandQueued) | Input.GetKeyDown(KeyCode.P);
        public static bool ConsumeTurret() => Consume(ref turretQueued) | Input.GetKeyDown(KeyCode.T);

        /// Escape on PC, the Back button on Android, or the on-screen pause button.
        public static bool ConsumePause() => Consume(ref pauseQueued) | Input.GetKeyDown(KeyCode.Escape);

        /// 0-based weapon slot from the number row, or -1.
        public static int WeaponSlotPressed(int slotCount)
        {
            int count = Mathf.Min(slotCount, 9);
            for (int i = 0; i < count; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) return i;
            return -1;
        }

        static bool Consume(ref bool flag)
        {
            bool value = flag;
            flag = false;
            return value;
        }
    }
}
