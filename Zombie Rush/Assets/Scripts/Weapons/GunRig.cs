using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace ZombieRush.Weapons
{
    /// A gun animated in Blender (Blender/gun_build.py + clips_gun_*.py): its Animator holds the
    /// Idle, Fire, Reload and Draw clips, and this picks the clip and the moment in it every frame
    /// from the weapon's state, so the first-person gun and the body's copy always show the same
    /// frame. Reload is stretched over the weapon's reload time.
    ///
    /// The clips also move HandR and HandL: the arm IK (WeaponAimRig) puts the hands there, so the
    /// left hand really takes the magazine out, loads shells and works the action.
    [RequireComponent(typeof(Animator))]
    public class GunRig : MonoBehaviour
    {
        [Tooltip("The weapon whose shots and reloads this gun shows. The body's copy keeps the same one.")]
        public WeaponBase source;
        public GunStyle style = GunStyle.Rifle;
        [Tooltip("The gun's Idle/Fire/Reload/Draw controller (given to the Animator at start: an imported model's Animator does not keep it).")]
        public RuntimeAnimatorController controller;
        [Tooltip("The gun's clips, posed directly every frame (set by the gun setup tool).")]
        public AnimationClip idleClip, fireClip, reloadClip, drawClip;
        public Transform handR, handL, muzzle;
        [Tooltip("Clip lengths in seconds (Fire and Draw play at this speed; Reload is stretched).")]
        public float idleLength = 2.4f, fireLength = 0.2f, drawLength = 0.45f;

        [Tooltip("The gun's own frame (+Z muzzle, +Y top) in this object's space (set by the gun setup tool).")]
        public Quaternion frame = Quaternion.identity;
        [Header("Hand directions at rest, in the gun's frame (+Z muzzle, +Y up, +X right)")]
        public Vector3 rightFingers = new Vector3(0f, 0.15f, 1f);
        public Vector3 rightPalm = new Vector3(-1f, 0f, 0f);
        public Vector3 leftFingers = new Vector3(0.85f, 0f, 0.5f);
        public Vector3 leftPalm = new Vector3(0f, 1f, 0f);
        [Tooltip("Hand bones' rotations in the gun's rest pose, relative to this object (set by the gun setup tool).")]
        public Quaternion restRight = Quaternion.identity, restLeft = Quaternion.identity;

        [Header("Body clips (Blender/grip_<gun>.py): the arms, hands and fingers made with the gun in hand")]
        [Tooltip("Survivor_<Gun>.fbx: the body's skeleton (Armature.002) with this gun's skeleton riding the right hand.")]
        public GameObject bodyProxy;
        [Tooltip("Body clips matching the gun clips: Idle, Fire, Reload, Draw.")]
        public AnimationClip[] bodyClips = new AnimationClip[4];
        [Tooltip("Optional: the body skeleton at rest (for deltaBones).")]
        public AnimationClip bodyRest;
        [Tooltip("Bones whose rotation is copied from the clip (arms, hands, fingers).")]
        public string[] copyBones;
        [Tooltip("Bones the clip turns on top of the game's own pose (chest, neck, head), as a change from rest.")]
        public string[] deltaBones;

        [Header("First-person arms (Blender/fp_viewmodel.py): arms and gun posed against the camera")]
        [Tooltip("FP_<Gun>.fbx: the arms rig (camera at its origin), the arms mesh and this gun's skeleton riding the right hand.")]
        public GameObject fpArms;
        [Tooltip("First-person clips matching the gun clips: Idle, Fire, Reload, Draw.")]
        public AnimationClip[] fpClips = new AnimationClip[4];
        [Tooltip("Materials for the arms mesh's slots after the sleeve (slot 0 wears the body's material): glove, skin, cloth roll, glove trim.")]
        public Material[] fpMaterials;

        /// True when this gun has its own first-person arms (shown instead of the body's arms in first person).
        public bool HasFirstPersonArms => fpArms != null && fpClips != null && fpClips.Length == 4 && fpClips[0] != null;

        Transform gunBone;
        /// The gun's "Gun" bone (the whole gun's motion; the body clips' gun skeleton has the same one).
        public Transform GunBone
        {
            get
            {
                if (gunBone == null)
                    foreach (var t in GetComponentsInChildren<Transform>(true))
                        if (t.name == "Gun") { gunBone = t; break; }
                return gunBone;
            }
        }

        /// True when this gun comes with body clips: the arms follow them instead of arm IK.
        public bool HasBodyClips => bodyProxy != null && bodyClips != null && bodyClips.Length == 4 && bodyClips[0] != null;

        /// Which clip (0 Idle, 1 Fire, 2 Reload, 3 Draw) and where in it (0..1) this gun shows now.
        public void Now(out int clip, out float t)
        {
            Pick(out int state, out t);
            clip = state == FireState ? 1 : state == ReloadState ? 2 : state == DrawState ? 3 : 0;
            if (clipList != null && clipList[clip] == null) clip = 0;
        }

        static readonly int IdleState = Animator.StringToHash("Idle");
        static readonly int FireState = Animator.StringToHash("Fire");
        static readonly int ReloadState = Animator.StringToHash("Reload");
        static readonly int DrawState = Animator.StringToHash("Draw");

        // Shared by every copy of the same weapon's gun, so they stay on the same frame.
        static readonly Dictionary<WeaponBase, float> lastShot = new Dictionary<WeaponBase, float>();
        static readonly Dictionary<WeaponBase, float> drawStart = new Dictionary<WeaponBase, float>();

        Animator animator;
        bool subscribed;

        void Awake()
        {
            animator = GetComponent<Animator>();
            animator.runtimeAnimatorController = null;   // the playable graph below poses the gun
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            BuildGraph();
        }

        void OnEnable()
        {
            if (source != null)
            {
                if (!drawStart.TryGetValue(source, out float t) || Time.time - t > 0.5f) drawStart[source] = Time.time;
                if (!subscribed) { source.Fired += OnFired; subscribed = true; }
            }
            Pose();
        }

        void OnDestroy()
        {
            if (source != null && subscribed) source.Fired -= OnFired;
            if (graph.IsValid()) graph.Destroy();
        }

        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable[] playables;
        AnimationClip[] clipList;

        /// One clip input per clip into a mixer; the graph is evaluated by hand with the chosen clip at weight 1.
        void BuildGraph()
        {
            clipList = new[] { idleClip, fireClip, reloadClip, drawClip };
            graph = PlayableGraph.Create(name + "_Gun");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "Gun", animator);
            mixer = AnimationMixerPlayable.Create(graph, clipList.Length);
            playables = new AnimationClipPlayable[clipList.Length];
            for (int i = 0; i < clipList.Length; i++)
            {
                if (clipList[i] == null) continue;
                playables[i] = AnimationClipPlayable.Create(graph, clipList[i]);
                playables[i].SetApplyFootIK(false);
                graph.Connect(playables[i], 0, mixer, i);
            }
            output.SetSourcePlayable(mixer);
            graph.Play();
        }

        void OnFired() => lastShot[source] = Time.time;

        void Update() => Pose();

        /// Which clip and where in it, from the weapon's state; the pose is applied at once, so the
        /// hands (IK later this frame) and both copies of the gun see the same frame.
        void Pose()
        {
            if (!graph.IsValid() || animator == null || !animator.isActiveAndEnabled) return;
            Pick(out int state, out float t);
            int sel = state == FireState ? 1 : state == ReloadState ? 2 : state == DrawState ? 3 : 0;
            if (clipList[sel] == null) sel = 0;
            for (int i = 0; i < clipList.Length; i++) mixer.SetInputWeight(i, i == sel && clipList[i] != null ? 1f : 0f);
            if (clipList[sel] != null) playables[sel].SetTime(t * clipList[sel].length);
            graph.Evaluate();
        }

        void Pick(out int state, out float t)
        {
            float now = Time.time;
            if (source != null && source.IsReloading) { state = ReloadState; t = Mathf.Clamp01(source.ReloadProgress); return; }
            if (source != null && drawStart.TryGetValue(source, out float d) && now - d < drawLength) { state = DrawState; t = (now - d) / drawLength; return; }
            if (source != null && lastShot.TryGetValue(source, out float s) && now - s < fireLength) { state = FireState; t = (now - s) / fireLength; return; }
            state = IdleState; t = Mathf.Repeat(now / Mathf.Max(0.1f, idleLength), 1f);
        }

        /// For checks: the clip and moment this gun shows now.
        public string Now()
        {
            Pick(out int st, out float t);
            string n = st == ReloadState ? "Reload" : st == DrawState ? "Draw" : st == FireState ? "Fire" : "Idle";
            return $"{n} {t:0.00}{(source == null ? " (NO SOURCE)" : "")}";
        }

        /// World directions of a hand's fingers and palm as the clip turns that hand now.
        public void HandAxes(bool right, out Vector3 fingers, out Vector3 palm)
        {
            Transform h = right ? handR : handL;
            Quaternion rest = right ? restRight : restLeft;
            Quaternion now = h != null ? Quaternion.Inverse(transform.rotation) * h.rotation : rest;
            Quaternion turn = transform.rotation * now * Quaternion.Inverse(rest);
            fingers = turn * (frame * (right ? rightFingers : leftFingers).normalized);
            palm = turn * (frame * (right ? rightPalm : leftPalm).normalized);
        }
    }
}
