using UnityEngine;
using ZombieRush.Player;

namespace ZombieRush.Data
{
    /// Tuning for the first-person / third-person camera switch (PlayerViewSwitcher).
    [CreateAssetMenu(fileName = "PlayerViewConfig", menuName = "Zombie Rush/Player View Config")]
    public class PlayerViewConfig : ScriptableObject
    {
        [Tooltip("View the match starts in.")]
        public CharacterView startView = CharacterView.FirstPerson;

        [Header("Third person")]
        [Tooltip("Camera position from the pivot (on the player's capsule at shoulder height), in the pivot's own space: x = right shoulder, y = up, z = back (negative).")]
        public Vector3 shoulderOffset = new Vector3(0.5f, 0.25f, -2.4f);
        [Tooltip("Look up/down limits in third person, degrees (down is negative).")]
        public float pitchMin = -55f;
        public float pitchMax = 60f;
        [Tooltip("Camera boom by look angle, like third-person shooters: looking up pulls the camera in and down " +
                 "(it would hit the ground), looking down lifts it and moves it back a little. Distance is a share of " +
                 "the normal one at the pitch limit; height is metres added at the limit.")]
        [Range(0.3f, 1f)] public float lookUpDistance = 0.65f;
        public float lookUpHeight = -0.15f;
        [Range(1f, 1.6f)] public float lookDownDistance = 1.0f;
        public float lookDownHeight = 0.25f;
        [Tooltip("Third-person camera trails the player's movement by this many seconds (0 = rigid).")]
        public float followLag = 0.12f;
        [Tooltip("Furthest the trailing camera may fall behind, metres.")]
        public float maxLag = 0.6f;
        [Tooltip("Seconds to slide between the two views.")]
        public float blendTime = 0.25f;
        [Tooltip("Seconds to move to the other shoulder (Q / shoulder button).")]
        public float shoulderSwapTime = 0.2f;

        [Header("Field of view (degrees, vertical)")]
        [Tooltip("First-person world view. Shooters use about 70-80 on phones; 60 feels zoomed in.")]
        public float firstPersonFov = 72f;
        [Tooltip("Third-person view.")]
        public float thirdPersonFov = 70f;

        [Header("Aiming (right mouse / aim button)")]
        [Tooltip("Third-person camera spot while aiming: closer in over the shoulder.")]
        public Vector3 aimOffset = new Vector3(0.45f, 0.2f, -1.4f);
        [Tooltip("The view narrows to this share of its normal field of view while aiming (zoom).")]
        [Range(0.4f, 1f)] public float aimFovScale = 0.8f;
        [Tooltip("Seconds to move in or out of aiming.")]
        public float aimBlendTime = 0.15f;

        [Header("Body facing (third person)")]
        [Tooltip("Off = Free Fire style: the body always faces the camera and side-steps. On = GTA style: when not shooting it turns to face the way it moves.")]
        public bool faceMoveDirection = false;
        [Tooltip("Fastest the body turns, degrees per second.")]
        public float turnSpeed = 360f;
        [Tooltip("How quickly a turn closes in (higher = snappier). The turn slows down as it finishes.")]
        public float turnSharpness = 10f;
        [Tooltip("Seconds after the last shot that the body keeps facing the crosshair.")]
        public float combatHoldTime = 1.2f;

        [Header("Wall check")]
        [Tooltip("Radius of the sphere that keeps the camera out of walls.")]
        public float collisionRadius = 0.2f;
        [Tooltip("Closest the camera gets to the head when a wall is behind the player.")]
        public float minDistance = 0.4f;
        [Tooltip("After a wall pushed the camera in, it eases back out at this speed (m/s). Pulling in is always instant.")]
        public float wallReturnSpeed = 3f;
        [Tooltip("Layers that push the camera in. Zombies and the player's own body never do.")]
        public LayerMask collisionMask = ~0;

        [Header("Body bounce (third person)")]
        [Tooltip("Share of the walk/run clips' up-and-down bounce the body keeps (1 = as animated). The feet stay planted.")]
        [Range(0f, 1f)] public float bounceKeep = 0.4f;
        [Tooltip("Seconds the body's average height takes to follow (crouching, slopes).")]
        public float bounceSmooth = 0.3f;
    }
}
