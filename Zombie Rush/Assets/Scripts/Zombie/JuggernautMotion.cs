using UnityEngine;
using ZombieRush.Audio;
using ZombieRush.Core;

namespace ZombieRush.Zombie
{
    /// Makes a big zombie move like a heavy, wounded giant on top of whatever clips it plays:
    /// a hunched back, a limp that drops one hip and rolls the body, a lolling head, slower and
    /// heavier playback, ground-shaking footsteps, a roar when it starts to charge, and a swing
    /// that winds up slowly and lands with a thud - the damage lands at that moment
    /// (ZombieAI.LandPendingHit), so the player can still back out of reach during the windup.
    /// Needs a Humanoid Animator under the zombie (it works on the humanoid bones).
    [DefaultExecutionOrder(100)]
    public class JuggernautMotion : MonoBehaviour
    {
        // Attack playback: slow windup, fast strike.
        public const float WindupPart = 0.3f, WindupPlayback = 0.65f, StrikePlayback = 1.1f;
        /// Average attack playback rate (for the editor's cooldown maths).
        public static float AttackPlayback => 1f / (WindupPart / WindupPlayback + (1f - WindupPart) / StrikePlayback);

        [Tooltip("The zombie plays its own authored clips (posture, limp and timing are already in them): " +
                 "only add the stomps, camera shake, roar and hit-on-impact.")]
        public bool authoredClips;

        [Header("Posture")]
        [Tooltip("Forward bend of the back while moving, degrees.")]
        public float hunch = 18f;
        [Tooltip("How far the head lolls from side to side, degrees.")]
        public float headLoll = 10f;

        [Header("Limp")]
        [Tooltip("Body roll towards the bad leg, degrees.")]
        public float limpRoll = 7f;
        [Tooltip("How far the hip drops on the bad leg, as a share of the zombie's height.")]
        public float limpDip = 0.03f;

        [Header("Playback")]
        public float walkPlayback = 0.75f;
        public float runPlayback = 0.85f;

        [Header("Weight")]
        public float shakeRange = 16f;
        public float stompShake = 0.3f;
        public float impactShake = 0.75f;
        [Range(0f, 1f)] public float stompVolume = 0.8f;

        [Header("Roar")]
        public float roarCooldown = 10f;
        public float roarDuration = 1.5f;

        static readonly int SpeedParam = Animator.StringToHash("Speed");
        static readonly int WalkState = Animator.StringToHash("Walk");
        static readonly int RunState = Animator.StringToHash("Run");
        static readonly int AttackState = Animator.StringToHash("Attack");

        class Foot { public Transform bone; public float lowest = float.MaxValue; public float lastHeight; public bool planted = true; }

        Animator animator;
        ZombieHealth health;
        ZombieAI ai;
        Renderer body;
        AudioSource voice, thuds;
        Transform hips, spine, chest, head, leftHand, rightHand;
        readonly Foot[] feet = { new Foot(), new Foot() };
        bool hasSpeed, ready;
        float height = 3f;

        float moveWeight, attackWeight;
        float roarStart = -100f, nextRoar;
        bool wasRunning;
        float nextStomp;
        Vector3 leftHandLast, rightHandLast;
        bool handsTracked;
        float swingPeak, nextImpact;

        void Awake()
        {
            health = GetComponent<ZombieHealth>();
            ai = GetComponent<ZombieAI>();
            var cc = GetComponent<CharacterController>();
            if (cc != null) height = cc.height;
            voice = MakeSource();
            thuds = MakeSource();
        }

        AudioSource MakeSource()
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 3f;
            s.maxDistance = 40f;
            s.dopplerLevel = 0f;
            return s;
        }

        void OnEnable()
        {
            moveWeight = attackWeight = 0f;
            roarStart = -100f;
            nextRoar = Time.time + Random.Range(0.5f, 3f);
            wasRunning = false;
            handsTracked = false;
            swingPeak = 0f;
            foreach (var f in feet) { f.lowest = float.MaxValue; f.planted = true; }
        }

        bool Resolve()
        {
            if (ready) return true;
            animator = GetComponentInChildren<Animator>(true);
            if (animator == null || !animator.isHuman) return false;
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            if (chest == null) chest = spine;
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            feet[0].bone = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            feet[1].bone = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            body = GetComponentInChildren<SkinnedMeshRenderer>(true);
            hasSpeed = ZombieAnimatorUtil.Has(animator, SpeedParam, AnimatorControllerParameterType.Float);
            ready = hips != null && spine != null && head != null;
            return ready;
        }

        bool Dead => health != null && health.IsDead;

        // Playback rate is set before the Animator evaluates this frame.
        void Update()
        {
            if (!Resolve()) return;
            if (Dead || authoredClips) { animator.speed = 1f; return; }
            var state = animator.GetCurrentAnimatorStateInfo(0);
            float rate = 1f;
            if (state.shortNameHash == WalkState) rate = walkPlayback;
            else if (state.shortNameHash == RunState) rate = runPlayback;
            else if (state.shortNameHash == AttackState) rate = Mathf.Repeat(state.normalizedTime, 1f) < WindupPart ? WindupPlayback : StrikePlayback;
            animator.speed = rate;
        }

        // Runs after the Animator has posed the bones: bend them further.
        void LateUpdate()
        {
            if (!ready || Dead) return;
            float dt = Time.deltaTime;
            var state = animator.GetCurrentAnimatorStateInfo(0);
            bool attacking = state.shortNameHash == AttackState;
            float speed = hasSpeed ? animator.GetFloat(SpeedParam) : 0f;

            moveWeight = Mathf.MoveTowards(moveWeight, speed > 0.05f ? 1f : 0f, dt * 3f);
            attackWeight = Mathf.MoveTowards(attackWeight, attacking ? 1f : 0f, dt * 4f);

            // Off-screen the Animator stops writing the bones; adding to them would pile up.
            if (body == null || body.isVisible) Pose(state);

            Footsteps(dt, speed, attacking);
            Roar(speed);
            if (attacking) Swing(dt); else { handsTracked = false; swingPeak = 0f; }
        }

        void Pose(AnimatorStateInfo state)
        {
            Vector3 right = transform.right, forward = transform.forward, up = transform.up;
            float phase = Mathf.Repeat(state.normalizedTime, 1f) * Mathf.PI * 2f; // one gait cycle = two steps
            float roar = RoarWeight();
            if (authoredClips)
            {
                // Only the roar: chest and head thrown back.
                if (roar <= 0f) return;
                Turn(spine, right, -12f * roar);
                if (chest != spine) Turn(chest, right, -18f * roar);
                Turn(head, right, -25f * roar);
                return;
            }
            float upright = 1f - 0.6f * attackWeight;                            // straightens up to swing
            float limp = moveWeight * (1f - attackWeight) * (1f - roar);

            // Hunched back (thrown back during the roar).
            float bend = hunch * upright * (0.55f + 0.45f * moveWeight) - 30f * roar;
            Turn(spine, right, bend * 0.45f);
            if (chest != spine) Turn(chest, right, bend * 0.55f);

            // Limp: on the bad leg's step the hip drops and the body rolls towards it,
            // the shoulders roll back the other way.
            float step = Mathf.Sin(phase);
            float sag = Mathf.Max(0f, step);
            hips.position -= up * (limpDip * height * sag * sag * limp);
            Turn(hips, forward, limpRoll * step * limp);
            Turn(chest, forward, -limpRoll * 0.6f * step * limp);

            // Head: lifted to look ahead from the hunch, hanging a little, lolling side to side.
            float loll = Mathf.Sin(phase * 0.5f + 1.3f) * headLoll * (0.5f + 0.5f * moveWeight) * (1f - attackWeight);
            Turn(head, right, -bend * 0.6f + 6f - 25f * roar);
            Turn(head, forward, loll * (1f - roar));
        }

        static void Turn(Transform bone, Vector3 axis, float degrees)
        {
            if (bone != null && degrees != 0f) bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
        }

        /// A foot that comes down and stops near the lowest point it reaches is a step.
        void Footsteps(float dt, float speed, bool attacking)
        {
            if (dt <= 0f) return;
            bool walking = speed > 0.1f && !attacking;
            foreach (var f in feet)
            {
                if (f.bone == null) continue;
                float h = Vector3.Dot(f.bone.position - transform.position, transform.up);
                f.lowest = Mathf.Min(f.lowest + height * 0.02f * dt, h); // slowly forgets, follows slopes
                float contact = f.lowest + height * 0.04f;
                if (f.planted)
                {
                    if (h > contact + height * 0.03f) f.planted = false;
                }
                else if (h <= contact && h <= f.lastHeight)
                {
                    f.planted = true;
                    if (walking && Time.time >= nextStomp)
                    {
                        nextStomp = Time.time + 0.2f;
                        bool running = speed > 0.75f;
                        Thud(f.bone.position, running ? 1f : 0.7f, Random.Range(0.4f, 0.5f));
                        CameraShake.AddAt(f.bone.position, stompShake * (running ? 1f : 0.6f), shakeRange);
                    }
                }
                f.lastHeight = h;
            }
        }

        void Roar(float speed)
        {
            bool running = speed > 0.75f;
            if (running && !wasRunning && Time.time >= nextRoar)
            {
                roarStart = Time.time;
                nextRoar = Time.time + roarCooldown;
                var lib = SoundManager.Library;
                if (lib != null && lib.zombieGroan != null && lib.zombieGroan.Length > 0)
                {
                    voice.pitch = Random.Range(0.55f, 0.65f); // a deep bellow from a normal groan
                    voice.PlayOneShot(SoundManager.Pick(lib.zombieGroan), GameSettings.SfxVolume);
                }
                CameraShake.AddAt(transform.position, 0.2f, shakeRange);
            }
            wasRunning = running;
        }

        float RoarWeight()
        {
            float t = (Time.time - roarStart) / roarDuration;
            return t >= 0f && t <= 1f ? Mathf.Sin(t * Mathf.PI) : 0f;
        }

        /// The swing connects when a fast-moving hand suddenly stops low (not at the top of the windup).
        void Swing(float dt)
        {
            if (dt <= 0f || leftHand == null || rightHand == null) return;
            Vector3 l = leftHand.position, r = rightHand.position;
            if (!handsTracked) { leftHandLast = l; rightHandLast = r; handsTracked = true; return; }

            float lSpeed = (l - leftHandLast).magnitude / dt / height;
            float rSpeed = (r - rightHandLast).magnitude / dt / height;
            leftHandLast = l;
            rightHandLast = r;
            bool rightLeads = rSpeed >= lSpeed;
            float speed = rightLeads ? rSpeed : lSpeed;
            Vector3 hand = rightLeads ? r : l;

            swingPeak = Mathf.Max(swingPeak, speed);
            bool low = Vector3.Dot(hand - transform.position, transform.up) < height * 0.6f;
            if (swingPeak > 0.8f && speed < swingPeak * 0.3f && low && Time.time >= nextImpact)
            {
                nextImpact = Time.time + 0.35f;
                swingPeak = 0f;
                bool hit = ai != null && ai.LandPendingHit();
                Thud(hand, 1f, Random.Range(0.3f, 0.36f));
                CameraShake.AddAt(hand, impactShake * (hit ? 1.4f : 1f), shakeRange);
            }
            else if (speed < swingPeak * 0.3f) swingPeak = Mathf.Max(0f, swingPeak - dt); // windup pause: forget slowly
        }

        void Thud(Vector3 at, float volume, float pitch)
        {
            var lib = SoundManager.Library;
            if (lib == null || lib.bulletImpact == null || lib.bulletImpact.Length == 0) return;
            thuds.pitch = pitch; // a bullet impact pitched far down reads as a heavy, dull thud
            thuds.PlayOneShot(SoundManager.Pick(lib.bulletImpact), stompVolume * volume * GameSettings.SfxVolume);
        }
    }
}
