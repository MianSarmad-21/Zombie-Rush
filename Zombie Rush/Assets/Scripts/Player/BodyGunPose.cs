using System.Collections.Generic;
using UnityEngine;
using ZombieRush.Data;
using ZombieRush.Weapons;

namespace ZombieRush.Player
{
    /// Plays a gun's body clips (made in Blender with the gun in hand, Blender/grip_<gun>.py) on the
    /// player's body, after the Animator, every frame:
    ///   1. the clip is sampled, at the gun clip's moment, on a hidden copy of the Blender skeleton;
    ///   2. arm, hand and finger rotations are copied onto the body; chest/neck/head are turned by
    ///      the clip's change from rest on top of the game's own pose;
    ///   3. the upper chest turns so the gun points along the view, as it does in Blender;
    ///   4. the gun (the body's copy, and in first person the camera's gun) is placed so its Gun bone
    ///      sits where the copy's Gun bone is: the copy's gun skeleton rides its right hand;
    ///   5. first person: the world camera sits at the eyes of a steady head point (the head with the
    ///      walk's sway filtered out, plus a small designed bob); the arms and gun are drawn by their own
    ///      camera (FirstPersonViewmodel) from each gun's spot, on top of the world.
    /// Guns without body clips are left to WeaponAimRig's arm IK.
    [DefaultExecutionOrder(1000)]   // after the Animator, WeaponAimRig and the camera switcher
    public class BodyGunPose : MonoBehaviour
    {
        public WeaponAimRig aimRig;
        public PlayerWeaponController weapons;
        public PlayerViewSwitcher view;
        [Tooltip("The first-person camera (moved to the eyes while a gun with body clips is held).")]
        public Camera eyeCamera;

        class Proxy
        {
            public GameObject go;
            public Dictionary<string, Transform> bones;
            public Dictionary<string, Quaternion> rest;
            public Transform hand, gun, head;
        }

        readonly Dictionary<GunRig, Proxy> proxies = new Dictionary<GunRig, Proxy>();
        Dictionary<string, Transform> body;
        Animator animator;
        Transform rightHand, head, chest, spine, midChest;
        Transform rUpper, rLower, lUpper, lLower, leftHand, rShoulder, lShoulder;
        Vector3 headLocal;
        bool headReady;
        float aimWeight;
        bool eyeReady;
        int anchorFrame = -1;
        FirstPersonViewmodel viewmodel;
        Vector3 lastRootPos;
        float bobPhase, bobAmount;
        WeaponBase listening;

        /// True while the held gun is driven by body clips (the aim rig skips its arm IK).
        public bool Active { get; private set; }

        void Awake()
        {
            animator = GetComponent<Animator>();
            if (aimRig == null) aimRig = GetComponent<WeaponAimRig>();
            if (animator != null && GetComponent<PlayerBounceDamper>() == null) gameObject.AddComponent<PlayerBounceDamper>();
            body = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (!body.ContainsKey(t.name)) body[t.name] = t;
            if (animator != null && animator.isHuman)
            {
                rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                head = animator.GetBoneTransform(HumanBodyBones.Head);
                var upper = animator.GetBoneTransform(HumanBodyBones.UpperChest);
                var mid = animator.GetBoneTransform(HumanBodyBones.Chest);
                chest = upper != null ? upper : mid;
                midChest = upper != null ? mid : null;
                spine = animator.GetBoneTransform(HumanBodyBones.Spine);
                rUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                rLower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                lUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                lLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                rShoulder = animator.GetBoneTransform(HumanBodyBones.RightShoulder);
                lShoulder = animator.GetBoneTransform(HumanBodyBones.LeftShoulder);
            }
        }

        void OnDestroy()
        {
            foreach (var p in proxies.Values)
                if (p.go != null) Destroy(p.go);
            if (armsRoot != null) Destroy(armsRoot.gameObject);
            if (listening != null) listening.Fired -= OnFired;
        }

        Proxy GetProxy(GunRig rig)
        {
            if (proxies.TryGetValue(rig, out var p)) return p;
            p = new Proxy { go = Instantiate(rig.bodyProxy, rig.bodyProxy.transform.position, rig.bodyProxy.transform.rotation) };
            p.go.name = "BodyClipProxy_" + rig.name;
            p.go.hideFlags = HideFlags.HideInHierarchy;
            foreach (var r in p.go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            // posed by sampling only; a built game samples clips only onto an object with an Animator
            // TryGetComponent, not ??: in the editor a missing component is a fake null that ?? does not catch
            if (!p.go.TryGetComponent(out Animator anim)) anim = p.go.AddComponent<Animator>();
            anim.enabled = false;
            p.bones = new Dictionary<string, Transform>();
            foreach (var t in p.go.GetComponentsInChildren<Transform>(true))
                if (!p.bones.ContainsKey(t.name)) p.bones[t.name] = t;
            p.bones.TryGetValue("RightHand", out p.hand);
            p.bones.TryGetValue("Gun", out p.gun);
            p.bones.TryGetValue("Head", out p.head);
            if (rig.bodyRest != null) ClipSampler.Sample(p.go, rig.bodyRest, 0f);
            p.rest = new Dictionary<string, Quaternion>();
            if (rig.deltaBones != null)
                foreach (var n in rig.deltaBones)
                    if (p.bones.TryGetValue(n, out var t)) p.rest[n] = t.localRotation;
            proxies[rig] = p;
            return p;
        }

        void LateUpdate()
        {
            var cfg = aimRig != null ? aimRig.config : null;
            var gun = aimRig != null ? aimRig.HeldGun : null;
            var rig = gun != null ? gun.GetComponentInChildren<GunRig>(true) : null;
            Active = rig != null && rig.HasBodyClips && rightHand != null && aimRig.HoldWeight > 0.001f;
            Listen(Active && weapons != null ? weapons.CurrentWeapon : null);
            if (!Active)
            {
                ShowFpArms(null);
                eyeReady = false; headReady = false;
                if (viewmodel != null) { viewmodel.Hidden = view != null && view.Scoped; viewmodel.SetOn(false); }
                return;
            }

            var p = GetProxy(rig);
            if (p.hand == null || p.gun == null) return;

            // 1. the body clip at the gun's moment
            rig.Now(out int clip, out float t);
            var c = rig.bodyClips[clip] ?? rig.bodyClips[0];
            ClipSampler.Sample(p.go, c, t * c.length);

            // 2. arms, hands, fingers; chest/neck/head as a change from rest
            float w = aimRig.HoldWeight;
            if (rig.copyBones != null)
                foreach (var n in rig.copyBones)
                    if (body.TryGetValue(n, out var b) && p.bones.TryGetValue(n, out var s))
                        b.localRotation = Quaternion.Slerp(b.localRotation, s.localRotation, w);
            if (rig.deltaBones != null)
                foreach (var n in rig.deltaBones)
                    if (body.TryGetValue(n, out var b) && p.bones.TryGetValue(n, out var s) && p.rest.TryGetValue(n, out var r))
                        b.localRotation = b.localRotation * Quaternion.Slerp(Quaternion.identity, Quaternion.Inverse(r) * s.localRotation, w);

            // where the gun's Gun bone goes: the copy's hand-to-gun, on the body's hand
            Matrix4x4 handToGun = p.hand.worldToLocalMatrix * p.gun.localToWorldMatrix;

            // 3. the upper body turns so the gun lines up with the view, as it was made in Blender
            float blendTime = cfg != null ? Mathf.Max(0.01f, cfg.bodyAimBlendTime) : 0.15f;
            aimWeight = Mathf.MoveTowards(aimWeight, aimRig.Aiming ? 1f : 0f, Time.deltaTime / blendTime);
            // aiming: along the view; carrying (third person, running without shooting): level, the way the body faces
            Quaternion aimRot = AimRotation();
            if (chest != null && aimRig.aimCamera != null)
            {
                // gun turn relative to the body facing: the copy stands at the world origin facing +Z
                // (its root node is the armature, turned by the FBX axis change, so it is not the facing)
                Quaternion inClip = p.gun.rotation;
                Quaternion want = aimRot * inClip;
                Quaternion have = (rightHand.localToWorldMatrix * handToGun).rotation;
                Quaternion fix = want * Quaternion.Inverse(have);
                fix = Quaternion.RotateTowards(Quaternion.identity, fix, cfg != null ? cfg.aimBendMax : 50f);
                // shared over the spine so the torso bends as one piece (the backpack rides the upper chest)
                Vector3 share = cfg != null ? cfg.aimBendShare : new Vector3(0.3f, 0.3f, 0.4f);
                float sx = spine != null ? share.x : 0f, sy = midChest != null ? share.y : 0f;
                float sz = Mathf.Max(0f, 1f - sx - sy);
                if (spine != null) spine.rotation = Quaternion.Slerp(Quaternion.identity, fix, w * sx) * spine.rotation;
                if (midChest != null) midChest.rotation = Quaternion.Slerp(Quaternion.identity, fix, w * sy) * midChest.rotation;
                chest.rotation = Quaternion.Slerp(Quaternion.identity, fix, w * sz) * chest.rotation;
            }

            // 3b. steady: the gun stays where it sits from the (slowly following) head in Blender, the arms
            // soak up the walk's bounce
            SteadyHold(p, handToGun, cfg, w, aimRot);

            // 4. the gun on the hand
            Matrix4x4 target = rightHand.localToWorldMatrix * handToGun;
            Place(gun, rig, target);
            bool fp = view != null && !view.IsThirdPerson;

            // 5. first person: the camera at the eyes. Before the camera's gun is placed: that gun is
            // the camera's child, so moving the camera afterwards would drag it off the hands.
            bool armsFp = fp && rig.HasFirstPersonArms && eyeCamera != null && view.FullyFirstPerson && !view.Scoped;
            MoveEye(fp, cfg, armsFp);
            var vm = fp && weapons != null && weapons.CurrentWeapon != null ? weapons.CurrentWeapon.GetComponent<WeaponViewModel>() : null;
            FpArms shown = null;
            if (vm != null && vm.model != null)
            {
                var camRig = vm.model.GetComponentInChildren<GunRig>(true);
                if (camRig != null)
                {
                    if (armsFp && viewmodel != null && viewmodel.Ready && (shown = GetFpArms(rig)) != null && shown.gun != null)
                    {
                        // first-person arms: their clip at the gun's moment, the camera's gun on their Gun bone
                        UpdateArmsRoot(cfg);
                        var fc = rig.fpClips[clip] ?? rig.fpClips[0];
                        ClipSampler.Sample(shown.go, fc, t * fc.length);
                        Place(camRig.transform, camRig, shown.gun.localToWorldMatrix);
                    }
                    else Place(camRig.transform, camRig, target);
                }
            }
            ShowFpArms(shown);
        }

        // ---------------------------------------------------------------- first-person arms

        class FpArms
        {
            public GameObject go;
            public Transform gun;
        }

        readonly Dictionary<GunRig, FpArms> fpArms = new Dictionary<GunRig, FpArms>();
        Transform armsRoot;
        FpArms fpShown;
        Quaternion lastView, sway = Quaternion.identity;
        bool swayReady;

        /// The first-person arms of a gun: FP_<Gun>.fbx under the camera (its origin is the eye), on the
        /// first-person layer, dressed in the body's own material.
        FpArms GetFpArms(GunRig rig)
        {
            if (fpArms.TryGetValue(rig, out var a)) return a;
            if (armsRoot == null)
            {
                armsRoot = new GameObject("FirstPersonArmsRoot").transform;
                armsRoot.SetParent(eyeCamera.transform, false);
            }
            a = new FpArms { go = Instantiate(rig.fpArms, armsRoot, false) };
            a.go.name = "FirstPersonArms_" + rig.name;
            a.go.transform.localPosition = Vector3.zero;
            a.go.transform.localRotation = Quaternion.identity;
            if (!a.go.TryGetComponent(out Animator anim)) anim = a.go.AddComponent<Animator>();
            anim.enabled = false;   // posed by sampling only (an Animator must be there for it in a built game)
            foreach (var t in a.go.GetComponentsInChildren<Transform>(true))
                if (t.name == "Gun") { a.gun = t; break; }
            Material[] mats = null;
            if (view != null && view.body != null && view.body.firstPersonOnly != null)
                foreach (var r in view.body.firstPersonOnly)
                    if (r != null) { mats = r.sharedMaterials; break; }
            foreach (var r in a.go.GetComponentsInChildren<Renderer>(true))
            {
                if (mats != null && mats.Length > 0)
                {
                    // slot 0 is the sleeve (the body's material); the hands' own materials follow
                    int slots = r is SkinnedMeshRenderer sk && sk.sharedMesh != null ? sk.sharedMesh.subMeshCount : mats.Length;
                    var use = new Material[Mathf.Max(1, slots)];
                    for (int i = 0; i < use.Length; i++)
                        use[i] = i == 0 ? mats[0] : (rig.fpMaterials != null && i - 1 < rig.fpMaterials.Length && rig.fpMaterials[i - 1] != null ? rig.fpMaterials[i - 1] : mats[0]);
                    r.sharedMaterials = use;
                }
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }
            if (viewmodel != null) viewmodel.Claim(a.go);
            a.go.SetActive(false);
            fpArms[rig] = a;
            return a;
        }

        /// Only the held gun's arms show; while they do, the body's own first-person arms are hidden.
        void ShowFpArms(FpArms a)
        {
            if (a == fpShown) return;
            if (fpShown != null && fpShown.go != null) fpShown.go.SetActive(false);
            fpShown = a;
            if (a != null) a.go.SetActive(true);
            if (view != null && view.body != null && view.body.firstPersonOnly != null)
                foreach (var r in view.body.firstPersonOnly)
                    if (r != null) r.forceRenderingOff = a != null;
            if (a == null) swayReady = false;
        }

        /// The arms' holder under the camera: a little lag behind mouse look, a bob while walking,
        /// pulled back a touch at speed. Shots still come from the camera.
        void UpdateArmsRoot(WeaponHoldConfig cfg)
        {
            if (armsRoot == null) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Quaternion now = eyeCamera.transform.rotation;
            if (!swayReady) { lastView = now; sway = Quaternion.identity; swayReady = true; }
            Quaternion delta = Quaternion.Inverse(lastView) * now;   // this frame's turn, in camera space
            lastView = now;
            Vector3 e = delta.eulerAngles;
            float yaw = Mathf.DeltaAngle(0f, e.y) / dt, pitch = Mathf.DeltaAngle(0f, e.x) / dt;
            float k = cfg != null ? cfg.vmSway : 1.2f, max = cfg != null ? cfg.vmSwayMax : 3f;
            Quaternion want = Quaternion.Euler(Mathf.Clamp(-pitch * k / 100f, -max, max), Mathf.Clamp(-yaw * k / 100f, -max, max), 0f);
            sway = Quaternion.Slerp(sway, want, 1f - Mathf.Exp(-(cfg != null ? cfg.vmSwayFollow : 10f) * dt));
            Vector2 bobSize = cfg != null ? cfg.vmBob : new Vector2(0.006f, 0.005f);
            Vector3 run = cfg != null ? cfg.vmRunOffset : Vector3.zero;
            Vector3 bob = new Vector3(Mathf.Sin(bobPhase) * bobSize.x, -Mathf.Abs(Mathf.Sin(bobPhase)) * bobSize.y, 0f) * bobAmount;
            armsRoot.localPosition = bob + run * bobAmount;
            armsRoot.localRotation = sway;
        }

        /// Where the gun points: the camera's view while aiming, level along the body's facing while carrying.
        Quaternion AimRotation()
        {
            Quaternion view = aimRig.aimCamera != null ? aimRig.aimCamera.transform.rotation : transform.rotation;
            Vector3 f = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            Quaternion carry = f.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(f.normalized, Vector3.up) : view;
            return Quaternion.Slerp(carry, view, aimWeight);
        }

        void SteadyHold(Proxy p, Matrix4x4 handToGun, WeaponHoldConfig cfg, float w, Quaternion aimRot)
        {
            if (head == null || p.head == null || aimRig.aimCamera == null || rUpper == null || lUpper == null) return;
            // the steady head point (shared with the first-person camera), in the body's space
            UpdateAnchor(cfg);
            Transform root = AnchorSpace;
            float steady = (cfg != null ? cfg.holdSteady : 0.85f) * w;
            if (steady <= 0f) return;
            // the copy faces +Z at the origin: its head-to-gun offset is in body-facing space
            Vector3 rel = (p.gun.position - p.head.position) * transform.lossyScale.y;
            Vector3 ideal = root.TransformPoint(headLocal) + aimRot * rel;
            // first person: the whole hold rides up into view (shoulders lifted, the arms keep their natural shape)
            var data = weapons != null && weapons.CurrentWeapon != null ? weapons.CurrentWeapon.data : null;
            if (data != null && data.fpLift != Vector3.zero && view != null && !view.IsThirdPerson && rShoulder != null && lShoulder != null)
            {
                Vector3 lift = aimRot * (data.fpLift * transform.lossyScale.y) * w;
                rShoulder.position += lift;
                lShoulder.position += lift;
                ideal += lift;
            }
            Vector3 raw = (rightHand.localToWorldMatrix * handToGun).GetPosition();
            Vector3 move = Vector3.ClampMagnitude((ideal - raw) * steady, cfg != null ? cfg.holdSteadyMax : 0.15f);
            if (move.sqrMagnitude < 1e-8f) return;
            Reach(rUpper, rLower, rightHand, rightHand.position + move);
            Reach(lUpper, lLower, leftHand, leftHand.position + move);
        }

        /// Two-bone reach: the wrist goes to `target`, the elbow stays in its bend plane, the hand keeps its turn.
        static void Reach(Transform upper, Transform lower, Transform hand, Vector3 target)
        {
            Quaternion handRot = hand.rotation;
            Vector3 a = upper.position, b = lower.position, c = hand.position;
            float l1 = (b - a).magnitude, l2 = (c - b).magnitude;
            Vector3 to = target - a;
            float d = Mathf.Clamp(to.magnitude, Mathf.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
            Vector3 dir = to.normalized;
            Vector3 pole = Vector3.ProjectOnPlane(b - a, dir);
            if (pole.sqrMagnitude < 1e-8f) pole = Vector3.ProjectOnPlane(Vector3.down, dir);
            pole.Normalize();
            float x = (l1 * l1 - l2 * l2 + d * d) / (2f * d);
            float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - x * x));
            Vector3 elbow = a + dir * x + pole * h;
            upper.rotation = Quaternion.FromToRotation(b - a, elbow - a) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, a + dir * d - lower.position) * lower.rotation;
            hand.rotation = handRot;
        }

        /// The body's space: it turns towards where the player runs in third person, so smoothing in it never lags a turn.
        Transform AnchorSpace => transform.parent != null ? transform.parent : transform;

        /// The steady head point (headLocal, in AnchorSpace): the head with the walk's sway and bounce
        /// filtered out. The gun hold hangs off it, and so does the first-person camera. Side to side and
        /// front to back it follows slowly; up and down a little faster, so crouching still follows.
        void UpdateAnchor(WeaponHoldConfig cfg)
        {
            if (head == null || anchorFrame == Time.frameCount) return;
            anchorFrame = Time.frameCount;
            float dt = Time.deltaTime;
            float ks = cfg != null && cfg.anchorSmoothSide > 0f ? 1f - Mathf.Exp(-dt / cfg.anchorSmoothSide) : 1f;
            float kh = cfg != null && cfg.anchorSmoothHeight > 0f ? 1f - Mathf.Exp(-dt / cfg.anchorSmoothHeight) : 1f;
            Vector3 hl = AnchorSpace.InverseTransformPoint(head.position);
            if (!headReady) { headLocal = hl; headReady = true; return; }
            headLocal = new Vector3(Mathf.Lerp(headLocal.x, hl.x, ks), Mathf.Lerp(headLocal.y, hl.y, kh), Mathf.Lerp(headLocal.z, hl.z, ks));
        }

        /// First person: the world camera at the eyes of the steady head point, with a small designed walk
        /// bob; the arms-and-gun camera at the gun's own spot (WeaponData.eyeOffset) with its own field of view.
        void MoveEye(bool fp, WeaponHoldConfig cfg, bool armsFp)
        {
            if (viewmodel == null && eyeCamera != null)
            {
                viewmodel = FirstPersonViewmodel.For(eyeCamera);
                ClaimFirstPersonObjects();
            }
            if (viewmodel != null) viewmodel.Hidden = view != null && view.Scoped;   // the scope sight replaces the gun
            if (fp && eyeCamera != null && head != null && view.FullyFirstPerson)
            {
                UpdateAnchor(cfg);
                float dt = Mathf.Max(Time.deltaTime, 1e-4f);
                var data = weapons != null && weapons.CurrentWeapon != null ? weapons.CurrentWeapon.data : null;
                Vector3 fwd = Vector3.ProjectOnPlane(eyeCamera.transform.forward, Vector3.up).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                float s = transform.lossyScale.y;
                Vector3 anchor = AnchorSpace.TransformPoint(headLocal);

                // designed walk bob: two dips per stride, one sway left-right, growing with speed
                Transform player = view.transform;
                Vector3 v = (player.position - lastRootPos) / dt; v.y = 0f;
                lastRootPos = player.position;
                float speed = eyeReady ? v.magnitude : 0f;
                float full = cfg != null ? Mathf.Max(0.1f, cfg.bobFullSpeed) : 4f;
                bobAmount = Mathf.MoveTowards(bobAmount, Mathf.Clamp01(speed / full), dt * 4f);
                bobPhase += dt * speed / (cfg != null ? Mathf.Max(0.1f, cfg.strideLength) : 1.5f) * 2f * Mathf.PI;
                Vector3 bob = Vector3.up * (Mathf.Sin(2f * bobPhase) * (cfg != null ? cfg.bobHeight : 0.006f) * bobAmount)
                            + right * (Mathf.Sin(bobPhase) * (cfg != null ? cfg.bobSide : 0.004f) * bobAmount);
                Vector3 Spot(Vector3 off) => anchor + right * (off.x * s) + Vector3.up * (off.y * s) + fwd * (off.z * s) + bob;

                Vector3 eyeOff = cfg != null ? cfg.eyeOffset : new Vector3(0f, 0.09f, 0.09f);
                Vector3 gunOff = data != null && data.overrideEyeOffset ? data.eyeOffset : eyeOff;
                if (armsFp && viewmodel != null && viewmodel.Ready && (cfg == null || cfg.eyeFromPivot))
                {
                    // first-person arms: the camera stays at the player's fixed eye spot (set by the view
                    // switcher this frame, crouch included), not the animated head, so it never shakes with
                    // the walk; the arms and gun hang off the camera and are drawn from the same spot
                    eyeCamera.transform.position += bob;
                    float baseFov2 = view.config != null ? Mathf.Max(1f, view.config.firstPersonFov) : 72f;
                    float zoom2 = eyeCamera.fieldOfView / baseFov2;
                    viewmodel.Pose(eyeCamera.transform.position, eyeCamera.transform.rotation,
                                   (cfg != null ? cfg.viewmodelFov : 55f) * zoom2, cfg != null ? cfg.fpArmsNearClip : 0.03f);
                    viewmodel.SetOn(true);
                }
                else if (viewmodel != null && viewmodel.Ready)
                {
                    eyeCamera.transform.position = Spot(eyeOff);
                    float baseFov = view.config != null ? Mathf.Max(1f, view.config.firstPersonFov) : 72f;
                    float zoom = eyeCamera.fieldOfView / baseFov;   // the aim zoom PlayerViewSwitcher set this frame
                    viewmodel.Pose(Spot(gunOff), eyeCamera.transform.rotation, (cfg != null ? cfg.viewmodelFov : 55f) * zoom, cfg != null ? cfg.viewmodelNearClip : 0.12f);
                    viewmodel.SetOn(true);
                }
                else eyeCamera.transform.position = Spot(gunOff);   // no overlay: the one camera shows the gun from its spot
                eyeReady = true;
            }
            else
            {
                eyeReady = false;
                if (viewmodel != null) viewmodel.SetOn(false);
            }
        }

        /// The first-person arms and the camera's guns go on the FirstPersonView layer (drawn by the overlay).
        void ClaimFirstPersonObjects()
        {
            if (viewmodel == null || view == null) return;
            if (view.body != null && view.body.firstPersonOnly != null)
                foreach (var r in view.body.firstPersonOnly)
                    if (r != null) viewmodel.Claim(r.gameObject);
            if (view.firstPersonModels != null)
                foreach (var m in view.firstPersonModels)
                    if (m != null) viewmodel.Claim(m.gameObject);
        }

        /// Moves a gun so its Gun bone matches `target` (world).
        static void Place(Transform gunRoot, GunRig rig, Matrix4x4 target)
        {
            Transform g = rig.GunBone;
            if (g == null) return;
            Matrix4x4 rootToGun = rig.transform.worldToLocalMatrix * g.localToWorldMatrix;
            Matrix4x4 rigWorld = target * rootToGun.inverse;
            // the rig object may sit under a holder: move the holder's child (gunRoot) so the rig lands on rigWorld
            Matrix4x4 rootFromRig = gunRoot.worldToLocalMatrix * rig.transform.localToWorldMatrix;
            Matrix4x4 rootWorld = rigWorld * rootFromRig.inverse;
            var parent = gunRoot.parent;
            Matrix4x4 local = parent != null ? parent.worldToLocalMatrix * rootWorld : rootWorld;
            gunRoot.localPosition = local.GetPosition();
            gunRoot.localRotation = local.rotation;
        }

        // Third person: the body's gun flashes (the camera's gun is hidden there).
        void Listen(WeaponBase w)
        {
            if (w == listening) return;
            if (listening != null) listening.Fired -= OnFired;
            listening = w;
            if (listening != null) listening.Fired += OnFired;
        }

        void OnFired()
        {
            if (view == null || !view.IsThirdPerson || aimRig == null || aimRig.HeldGun == null) return;
            foreach (var ps in aimRig.HeldGun.GetComponentsInChildren<ParticleSystem>(true))
                if (ps.transform.parent == null || ps.transform.parent.GetComponent<ParticleSystem>() == null) ps.Play(true);
            foreach (var l in aimRig.HeldGun.GetComponentsInChildren<Light>(true)) StartCoroutine(Flash(l));
        }

        static System.Collections.IEnumerator Flash(Light l)
        {
            l.enabled = true;
            yield return new WaitForSeconds(0.05f);
            if (l != null) l.enabled = false;
        }
    }
}
