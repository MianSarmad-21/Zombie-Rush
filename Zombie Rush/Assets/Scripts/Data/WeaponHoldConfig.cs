using UnityEngine;

namespace ZombieRush.Data
{
    /// How a character body holds and aims its gun (WeaponAimRig). Distances are metres for a
    /// 1.8 m body and scale with the character's height.
    [CreateAssetMenu(fileName = "WeaponHoldConfig", menuName = "Zombie Rush/Weapon Hold Config")]
    public class WeaponHoldConfig : ScriptableObject
    {
        [Header("Aim")]
        [Tooltip("How far the spine turns towards the aim point (0-1). The rest is done by the arms.")]
        [Range(0f, 1f)] public float spineWeight = 0.25f;
        [Tooltip("Most the upper body bends down to follow the aim, degrees. The gun and arms still aim lower.")]
        public float maxBendDown = 20f;
        [Tooltip("Most the upper body leans back to follow the aim, degrees.")]
        public float maxBendUp = 25f;
        [Range(0f, 1f)] public float headWeight = 1f;
        [Tooltip("Limits how far the look-at can twist (0 = free, 1 = locked).")]
        [Range(0f, 1f)] public float lookClamp = 0.5f;
        [Tooltip("Aim point when the screen centre hits nothing.")]
        public float aimDistance = 60f;

        [Header("Long guns (rifle, shotgun)")]
        [Tooltip("Shoulder pocket, from the chest bone: x = right, y = up, z = forward.")]
        public Vector3 shoulderPocket = new Vector3(0.16f, 0.12f, 0.06f);
        [Tooltip("Right hand along the gun from the back end (0) to the muzzle (1).")]
        [Range(0f, 1f)] public float rightGripAlong = 0.34f;
        [Range(0f, 1f)] public float leftGripAlong = 0.66f;

        [Header("Pistol")]
        [Tooltip("Guns shorter than this are held out in both hands like a pistol.")]
        public float pistolMaxLength = 0.32f;
        [Tooltip("Pistol grip position, from the chest bone: x = right, y = up, z = forward.")]
        public Vector3 pistolGrip = new Vector3(0.06f, 0.08f, 0.46f);

        [Header("Hands")]
        [Tooltip("Wrist sits this far behind the grip point, along the fingers.")]
        public float wristBack = 0.07f;
        [Tooltip("Palm surface to the middle of the grip.")]
        public float palmOffset = 0.025f;
        [Tooltip("Elbow hint: right elbow out to the side and down, from the right shoulder.")]
        public Vector3 rightElbowHint = new Vector3(0.35f, -0.35f, -0.1f);
        [Tooltip("Elbow hint: left elbow down and in, from the left shoulder.")]
        public Vector3 leftElbowHint = new Vector3(-0.1f, -0.45f, 0.2f);

        [Tooltip("Finger muscle value while gripping (-1..1, humanoid 'Stretched' muscles; lower = more closed).")]
        [Range(-1f, 1f)] public float fingerGrip = -0.6f;
        [Tooltip("Right index finger (trigger finger) muscle value.")]
        [Range(-1f, 1f)] public float triggerFinger = 0.2f;

        [Header("Feel")]
        [Tooltip("Gun kicks back this far per shot.")]
        public float recoilBack = 0.025f;
        [Tooltip("Gun kicks up this many degrees per shot.")]
        public float recoilPitch = 2.5f;
        public float recoilReturn = 12f;
        [Tooltip("Gun tips down this many degrees while carried (running, not aiming).")]
        public float carryTilt = 30f;
        [Tooltip("Gun tips down this many degrees while reloading.")]
        public float reloadTilt = 35f;
        [Tooltip("Seconds to blend the hands on and off (death, respawn).")]
        public float blendTime = 0.2f;

        [Header("Kick per gun type (multiplies the recoil above)")]
        public float pistolKick = 1.6f;
        public float rifleKick = 1f;
        public float shotgunKick = 2.4f;
        [Tooltip("Shotgun: seconds after a shot before the left hand pumps, how long the pump takes, how far it slides.")]
        public float pumpDelay = 0.2f;
        public float pumpTime = 0.45f;
        public float pumpDistance = 0.09f;

        [Header("Reload")]
        [Tooltip("Gun rolls this many degrees while reloading so the magazine side turns to the left hand.")]
        public float reloadRoll = -12f;
        [Tooltip("Spare-magazine pouch, from the chest bone: x = right, y = up, z = forward (left hip is negative x).")]
        public Vector3 magPouch = new Vector3(-0.22f, -0.45f, 0.10f);

        [Header("Weapon change")]
        [Tooltip("Gun tips down this many degrees and drops this far (metres) while it is put away / drawn.")]
        public float swapTilt = 55f;
        public float swapDrop = 0.2f;
        [Tooltip("Seconds to put the old gun away; the new one takes the same to come up.")]
        public float swapTime = 0.2f;

        [Header("Life")]
        [Tooltip("Breathing sway of the held gun, degrees.")]
        public float breathe = 0.35f;
        [Tooltip("Walking bob of the held gun (1 = default).")]
        public float bobAmount = 1f;

        [Header("Body clips (guns animated with the body in Blender)")]
        [Tooltip("First-person world camera (the eyes) from the steady head point, metres: y = up, z = forward along the aim. Guns can place their arms-and-gun camera elsewhere (WeaponData.eyeOffset).")]
        public Vector3 eyeOffset = new Vector3(0f, 0.09f, 0.09f);
        [Tooltip("Seconds the steady head point takes to follow the head side to side and front to back: filters out the walk's sway (the gun hold and the first-person camera both hang off this point).")]
        public float anchorSmoothSide = 0.6f;
        [Tooltip("Seconds it takes to follow the head up and down: filters out the step bounce but still follows crouching.")]
        public float anchorSmoothHeight = 0.25f;
        [Tooltip("Field of view of the first-person arms-and-gun camera (drawn on top, so the gun never clips into walls). Narrower than the world view keeps the gun from looking stretched.")]
        public float viewmodelFov = 55f;
        [Tooltip("Near clip of the arms-and-gun camera, metres: cuts the shoulders and collar right at the camera (seen from inside when a gun's camera spot sits behind the eyes); hands and guns are further out.")]
        public float viewmodelNearClip = 0.18f;
        [Tooltip("First-person camera bob at running speed, metres: up-down and side-to-side. 0 = IGI-steady eye (the arms still sway, see vmBob).")]
        public float bobHeight = 0f;
        public float bobSide = 0f;
        [Tooltip("Metres covered per full stride (two steps): sets the walk rhythm of the arms' sway. The run clip covers 2.4 m per stride at 4 m/s.")]
        public float strideLength = 2.4f;
        [Tooltip("Most strides per second the sway follows (a sprint stays a calm sway, not a buzz).")]
        public float bobMaxRate = 1.6f;
        [Tooltip("Speed (m/s) at which the bob reaches full size.")]
        public float bobFullSpeed = 4f;
        [Tooltip("Seconds the first-person eye takes to glide over a kerb, bump or step-up (the controller lifts in one frame). 0 = rigid.")]
        public float eyeStepSmooth = 0.08f;
        [Tooltip("Seconds for the upper body to blend in or out of aiming the gun along the view.")]
        public float bodyAimBlendTime = 0.15f;
        [Tooltip("How the turn that lines the gun up with the view is shared over the spine, chest and upper chest " +
                 "(x, y, z; they add up to 1). Turning only the upper chest folds the backpack into the body.")]
        public Vector3 aimBendShare = new Vector3(0.3f, 0.3f, 0.4f);
        [Tooltip("Most the torso turns to line the gun up with the view, degrees. Past it (looking far up or down) the " +
                 "camera keeps going but the body stops bending, as in third-person shooters; shots still go where the crosshair is.")]
        public float aimBendMax = 45f;
        [Tooltip("How much of the walk's bounce the arms soak up while aiming (0 = the gun bounces with the chest, 1 = rock steady).")]
        [Range(0f, 1f)] public float holdSteady = 0.85f;
        [Tooltip("Furthest the arms move the gun to keep it steady, metres.")]
        public float holdSteadyMax = 0.15f;

        [Header("First-person arms (Blender/fp_viewmodel.py: arms and gun posed against the camera)")]
        [Tooltip("First person: the camera stays at the player's fixed eye spot (crouch included) instead of following the animated head.")]
        public bool eyeFromPivot = true;
        [Tooltip("Near clip of the arms camera while first-person arms are shown, metres.")]
        public float fpArmsNearClip = 0.03f;
        [Tooltip("Arms and gun lag behind mouse look: degrees of tilt per 100 degrees/second of turning.")]
        public float vmSway = 1.2f;
        [Tooltip("Most the look sway tilts the arms, degrees.")]
        public float vmSwayMax = 3f;
        [Tooltip("How quickly the arms catch up with the view (higher = snappier).")]
        public float vmSwayFollow = 10f;
        [Tooltip("Arms bob while walking (side, up), metres at full walk speed.")]
        public Vector2 vmBob = new Vector2(0.003f, 0.002f);
        [Tooltip("Arms drop back and down while running, metres at full speed.")]
        public Vector3 vmRunOffset = new Vector3(0f, -0.01f, -0.015f);
    }
}
