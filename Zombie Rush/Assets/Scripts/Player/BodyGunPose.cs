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
            public Transform hand, gun, head, rUpper, lUpper, lHand;
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
        float bobPhase, bobAmount, smoothSpeed, eyeRootY, eyeRootVel;
        WeaponBase listening;

        /// The camera sits at the eyes: first person, or looking through a scope from third person.
        bool EyeView => view != null && (view.FullyFirstPerson || view.ScopedAtEye);

        float gunKick, gunKickRoll, kickR;   // first-person gun recoil, 0..1 (decays), and this shot's roll side

        /// True while the held gun is driven by body clips (the aim rig skips its arm IK).
        public bool Active { get; private set; }
        /// Test numbers (captures): the torso bend this frame (deg), the steady move (m), the right hand
        /// in the right shoulder's space of the body facing (m), the wanted vs. reached gun pitch.
        [System.NonSerialized] public float DebugFix, DebugMove, DebugWantPitch, DebugGunPitch;
        [System.NonSerialized] public Vector3 DebugHandFromShoulder;

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
            p.bones.TryGetValue("RightUpperArm", out p.rUpper);
            p.bones.TryGetValue("LeftUpperArm", out p.lUpper);
            p.bones.TryGetValue("LeftHand", out p.lHand);
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
            var rig = gun != null ? GunRig.Of(gun) : null;
            Active = rig != null && rig.HasBodyClips && rightHand != null && aimRig.HoldWeight > 0.001f;
            Listen(Active && weapons != null ? weapons.CurrentWeapon : null);
            if (!Active)
            {
                ShowFpArms(null);
                eyeReady = false; headReady = false;
                if (viewmodel != null) { viewmodel.Hidden = false; viewmodel.SetOn(false); }
                return;
            }

            var p = GetProxy(rig);
            if (p.hand == null || p.gun == null) return;

            // 1. the body clip at the gun's moment
            rig.Now(out int clip, out float t);
            var held = weapons != null && weapons.CurrentWeapon != null ? weapons.CurrentWeapon.data : null;
            if (clip == 0 && held != null && held.idleSteady > 0f)
            {
                // steady idle: only a small slice of the breathing loop, played forward and back (never a jump)
                t = (1f - held.idleSteady) * (0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * t));
            }
            // shot kick (both views): decays here every frame
            gunKick = Mathf.MoveTowards(gunKick, 0f, Time.deltaTime * 9f * Mathf.Max(0.25f, gunKick));
            kickR = gunKick * gunKick * (3f - 2f * gunKick);
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
                DebugFix = Quaternion.Angle(Quaternion.identity, fix);
                DebugWantPitch = -Mathf.Asin(Mathf.Clamp((want * Vector3.forward).y, -1f, 1f)) * Mathf.Rad2Deg;
                // shared over the spine so the torso bends as one piece (the backpack rides the upper chest)
                Vector3 share = cfg != null ? cfg.aimBendShare : new Vector3(0.3f, 0.3f, 0.4f);
                float sx = spine != null ? share.x : 0f, sy = midChest != null ? share.y : 0f;
                float sz = Mathf.Max(0f, 1f - sx - sy);
                if (spine != null) spine.rotation = Quaternion.Slerp(Quaternion.identity, fix, w * sx) * spine.rotation;
                if (midChest != null) midChest.rotation = Quaternion.Slerp(Quaternion.identity, fix, w * sy) * midChest.rotation;
                chest.rotation = Quaternion.Slerp(Quaternion.identity, fix, w * sz) * chest.rotation;
            }

            // 3a. third person shot kick: the upper body rocks back (arms and gun with it, so the hands stay on the gun)
            if (chest != null && held != null && kickR > 0f && view != null && view.IsThirdPerson && !view.Scoped)
            {
                Vector3 side = aimRot * Vector3.right;
                chest.rotation = Quaternion.AngleAxis(-held.bodyKickPitch * kickR * w, side) * chest.rotation;
            }

            // 3b. steady: the gun stays where it sits from the (slowly following) head in Blender, the arms
            // soak up the walk's bounce
            SteadyHold(p, handToGun, cfg, w, aimRot);

            // 4. the gun on the hand
            Matrix4x4 target = rightHand.localToWorldMatrix * handToGun;
            if (rUpper != null)
            {
                Vector3 f = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                DebugHandFromShoulder = Quaternion.Inverse(Quaternion.LookRotation(f, Vector3.up)) * (rightHand.position - rUpper.position);
                DebugGunPitch = -Mathf.Asin(Mathf.Clamp((target.rotation * (p.gun.rotation * Vector3.forward)).y, -1f, 1f)) * Mathf.Rad2Deg;
            }
            Place(gun, rig, target);
            bool fp = view != null && (!view.IsThirdPerson || view.Scoped);

            // 5. first person: the camera at the eyes. Before the camera's gun is placed: that gun is
            // the camera's child, so moving the camera afterwards would drag it off the hands.
            bool armsFp = fp && rig.HasFirstPersonArms && eyeCamera != null && EyeView;
            MoveEye(fp, cfg, armsFp);
            var vm = fp && weapons != null && weapons.CurrentWeapon != null ? weapons.CurrentWeapon.GetComponent<WeaponViewModel>() : null;
            FpArms shown = null;
            if (vm != null && vm.model != null)
            {
                var camRig = GunRig.Of(vm.model);
                if (camRig != null)
                {
                    if (armsFp && viewmodel != null && viewmodel.Ready && (shown = GetFpArms(rig)) != null && shown.gun != null)
                    {
                        // first-person arms: their clip at the gun's moment, the camera's gun on their Gun bone
                        UpdateArmsRoot(cfg);
                        var fc = rig.fpClips[clip] ?? rig.fpClips[0];
                        ClipSampler.Sample(shown.go, fc, t * fc.length);
                        LineUpUnderScope(shown, camRig);
                        KickArms();
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
            Vector2 bobSize = cfg != null ? cfg.vmBob : new Vector2(0.003f, 0.002f);
            Vector3 run = cfg != null ? cfg.vmRunOffset : Vector3.zero;
            // a soft figure-eight: one sway left-right per stride, one smooth dip per step (the old |sin| dip had
            // a sharp kink at every footfall, which read as the gun shaking)
            Vector3 bob = new Vector3(Mathf.Sin(bobPhase) * bobSize.x, -(1f - Mathf.Cos(2f * bobPhase)) * 0.5f * bobSize.y, 0f) * bobAmount;
            // recoil (added after the scope line-up, see KickArms): the gun punches back and the muzzle flips up
            var data = weapons != null && weapons.CurrentWeapon != null ? weapons.CurrentWeapon.data : null;
            float r = kickR;
            kickPos = data != null ? new Vector3(0f, 0.002f * r, -data.gunKickBack * r) : Vector3.zero;
            kickRot = data != null ? Quaternion.Euler(-data.gunKickPitch * r, 0f, gunKickRoll * 1.2f * r) : Quaternion.identity;
            scopeBlend = data != null && data.AimsAtEye && view != null ? view.AimAmount : 0f;
            armsRoot.localPosition = bob * (1f - 0.7f * scopeBlend) + run * bobAmount;
            armsRoot.localRotation = sway;
        }

        Vector3 kickPos;
        Quaternion kickRot = Quaternion.identity;
        float scopeBlend;
        readonly Dictionary<GunRig, (Vector3 dir, Vector3 centre, Vector3 tip)> barrels = new Dictionary<GunRig, (Vector3, Vector3, Vector3)>();

        /// Looking through a scope: the arms turn (about the eye) so the gun's barrel points straight along the
        /// view, and shift so the gun sits centred under the eye, its centre line WeaponData.scopeEyeHeight below
        /// it (the scope's height over the gun): the gun's top shows below the sight, as through a real scope.
        /// Call after the arms clip is sampled (the gun bone is in this frame's pose).
        void LineUpUnderScope(FpArms arms, GunRig camRig)
        {
            var data = weapons != null && weapons.CurrentWeapon != null ? weapons.CurrentWeapon.data : null;
            if (scopeBlend <= 0f || data == null || arms.gun == null || camRig == null) return;
            if (!barrels.TryGetValue(camRig, out var b)) barrels[camRig] = b = BarrelOf(camRig);
            if (b.dir == Vector3.zero) return;
            Transform cam = eyeCamera.transform;
            Vector3 axis = cam.InverseTransformDirection(arms.gun.TransformDirection(b.dir));
            Vector3 centre = cam.InverseTransformPoint(arms.gun.TransformPoint(b.centre));
            Quaternion q = Quaternion.FromToRotation(axis, Vector3.forward);
            Vector3 c2 = q * centre;
            Vector3 t = new Vector3(-c2.x, -data.scopeEyeHeight - c2.y, -data.scopeEyeBack);
            if (!data.hasScope && data.ironSights)
            {
                // iron sights: the gun lined up along the view with its FRONT SIGHT on the middle of the screen:
                // the sight sits WeaponData.sightHeight above the bore at the muzzle (and the rear sight at the
                // same height, the gun being straight), so the eye looks along the slide / barrel rib to it
                Vector3 m = q * cam.InverseTransformPoint(arms.gun.TransformPoint(b.tip));
                t = new Vector3(-m.x, -m.y - data.sightHeight, data.sightForward);
            }
            q = Quaternion.Slerp(Quaternion.identity, q, scopeBlend);
            t *= scopeBlend;
            // turning the holder about the eye (the camera's origin) turns everything under it the same way
            armsRoot.localPosition = q * armsRoot.localPosition + t;
            armsRoot.localRotation = q * armsRoot.localRotation;
        }

        /// The shot's kick on the arms' holder, about the eye, after any scope line-up.
        void KickArms()
        {
            if (armsRoot == null) return;
            armsRoot.localPosition = kickRot * armsRoot.localPosition + kickPos;
            armsRoot.localRotation = kickRot * armsRoot.localRotation;
        }

        /// The gun's barrel line in its Gun bone's space: the bone axis along the gun's longest side (the mesh
        /// bounds), pointing to the muzzle; and the middle of the gun.
        static (Vector3 dir, Vector3 centre, Vector3 tip) BarrelOf(GunRig rig)
        {
            Transform bone = rig.GunBone;
            if (bone == null) return (Vector3.zero, Vector3.zero, Vector3.zero);
            bool any = false;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            foreach (var r in rig.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = r is SkinnedMeshRenderer sk ? sk.sharedMesh : r.TryGetComponent(out MeshFilter mf) ? mf.sharedMesh : null;
                if (mesh == null || r is ParticleSystemRenderer) continue;
                Bounds mb = mesh.bounds;
                Matrix4x4 toBone = bone.worldToLocalMatrix * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 pt = toBone.MultiplyPoint3x4(corner);
                    min = Vector3.Min(min, pt); max = Vector3.Max(max, pt);
                    any = true;
                }
            }
            if (!any) return (Vector3.zero, Vector3.zero, Vector3.zero);
            Vector3 size = max - min, centre = (min + max) * 0.5f;
            Vector3 dir = size.x >= size.y && size.x >= size.z ? Vector3.right : size.y >= size.z ? Vector3.up : Vector3.forward;
            // the muzzle end: the WeaponViewModel's muzzle if there is one, else the end farther from the bone (the grip)
            var vm = rig.GetComponentInParent<WeaponViewModel>();
            Vector3 tip = vm != null && vm.muzzle != null ? bone.InverseTransformPoint(vm.muzzle.position) : centre * 2f;
            if (Vector3.Dot(tip - centre, dir) < 0f) dir = -dir;
            return (dir, centre, tip);
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
            // the copy faces +Z at the origin: its offsets are in body-facing space.
            // Looking up or down, the hold turns about the shoulder pocket (where a long gun's stock sits; between
            // the shoulders for a pistol), not about the head: turned about the head, the stock swung into the face
            // looking down and into the chest looking up. The pocket rides the body as it bends (the steady head
            // point plus the body's own head-to-shoulder offset now).
            float s = transform.lossyScale.y;
            Vector3 pivClip = p.head.position, pivBody = head.position;
            if (p.rUpper != null && p.lUpper != null && rUpper != null && lUpper != null)
            {
                // long gun (support hand far out from the trigger hand): the right shoulder; pistol: mostly the middle
                float k = p.lHand != null && Vector3.Distance(p.lHand.position, p.hand.position) > 0.22f ? 1f : 0.3f;
                pivClip = Vector3.Lerp((p.rUpper.position + p.lUpper.position) * 0.5f, p.rUpper.position, k);
                pivBody = Vector3.Lerp((rUpper.position + lUpper.position) * 0.5f, rUpper.position, k);
            }
            Vector3 rel = (p.gun.position - pivClip) * s;
            Vector3 ideal = root.TransformPoint(headLocal) + (pivBody - head.position) + aimRot * rel;
            // first person: the whole hold rides up into view (shoulders lifted, the arms keep their natural shape)
            var data = weapons != null && weapons.CurrentWeapon != null ? weapons.CurrentWeapon.data : null;
            if (data != null && data.fpLift != Vector3.zero && view != null && !view.IsThirdPerson && rShoulder != null && lShoulder != null)
            {
                Vector3 lift = aimRot * (data.fpLift * transform.lossyScale.y) * w;
                rShoulder.position += lift;
                lShoulder.position += lift;
                ideal += lift;
            }
            if (data != null && kickR > 0f) ideal -= aimRot * Vector3.forward * (data.gunKickBack * 1.5f * kickR * w);   // shot: the gun punches back
            Vector3 raw = (rightHand.localToWorldMatrix * handToGun).GetPosition();
            Vector3 move = Vector3.ClampMagnitude((ideal - raw) * steady, cfg != null ? cfg.holdSteadyMax : 0.15f);
            DebugMove = move.magnitude;
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
            if (viewmodel != null) viewmodel.Hidden = false;   // scoped: the gun stays in view below the sight
            if (fp && eyeCamera != null && head != null && EyeView)
            {
                UpdateAnchor(cfg);
                float dt = Mathf.Max(Time.deltaTime, 1e-4f);
                var data = weapons != null && weapons.CurrentWeapon != null ? weapons.CurrentWeapon.data : null;
                Vector3 fwd = Vector3.ProjectOnPlane(eyeCamera.transform.forward, Vector3.up).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                float s = transform.lossyScale.y;
                Vector3 anchor = AnchorSpace.TransformPoint(headLocal);

                // walk rhythm (drives the arms' gentle sway): the speed is smoothed, the stride follows the real
                // run (2.4 m per stride at 4 m/s) and the rhythm is capped, so a sprint never turns it into a buzz
                Transform player = view.transform;
                Vector3 v = (player.position - lastRootPos) / dt; v.y = 0f;
                lastRootPos = player.position;
                float rawSpeed = eyeReady ? v.magnitude : 0f;
                smoothSpeed = Mathf.Lerp(smoothSpeed, rawSpeed, 1f - Mathf.Exp(-8f * dt));
                float speed = smoothSpeed;
                float full = cfg != null ? Mathf.Max(0.1f, cfg.bobFullSpeed) : 4f;
                bobAmount = Mathf.MoveTowards(bobAmount, Mathf.Clamp01(speed / full), dt * 3f);
                float strides = speed / (cfg != null ? Mathf.Max(0.1f, cfg.strideLength) : 2.4f);
                bobPhase += dt * Mathf.Min(strides, cfg != null ? cfg.bobMaxRate : 1.6f) * 2f * Mathf.PI;
                // IGI-steady eye: no camera bob by default (bobHeight/bobSide 0); when set, a smooth one
                Vector3 bob = Vector3.up * (-(1f - Mathf.Cos(2f * bobPhase)) * 0.5f * (cfg != null ? cfg.bobHeight : 0f) * bobAmount)
                            + right * (Mathf.Sin(bobPhase) * (cfg != null ? cfg.bobSide : 0f) * bobAmount);
                // small bumps, kerbs and step-ups lift the controller in one frame: the eye glides up/down
                // over them instead of jolting (big drops and jumps are followed at once)
                float stepSmooth = cfg != null ? cfg.eyeStepSmooth : 0.08f;
                float rootY = player.position.y;
                if (!eyeReady || stepSmooth <= 0f || Mathf.Abs(rootY - eyeRootY) > 0.6f) { eyeRootY = rootY; eyeRootVel = 0f; }
                else eyeRootY = Mathf.SmoothDamp(eyeRootY, rootY, ref eyeRootVel, stepSmooth, Mathf.Infinity, dt);
                bob += Vector3.up * (eyeRootY - rootY);
                Vector3 Spot(Vector3 off) => anchor + right * (off.x * s) + Vector3.up * (off.y * s) + fwd * (off.z * s) + bob;

                Vector3 eyeOff = cfg != null ? cfg.eyeOffset : new Vector3(0f, 0.09f, 0.09f);
                Vector3 gunOff = data != null && data.overrideEyeOffset ? data.eyeOffset : eyeOff;
                if (armsFp && viewmodel != null && viewmodel.Ready && (cfg == null || cfg.eyeFromPivot))
                {
                    // first-person arms: the camera stays at the player's fixed eye spot (set by the view
                    // switcher this frame, crouch included), not the animated head, so it never shakes with
                    // the walk; the arms and gun hang off the camera and are drawn from the same spot
                    eyeCamera.transform.position += bob;
                    float zoom2 = view.ViewmodelZoom;
                    viewmodel.Pose(eyeCamera.transform.position, eyeCamera.transform.rotation,
                                   (cfg != null ? cfg.viewmodelFov : 55f) * zoom2, cfg != null ? cfg.fpArmsNearClip : 0.03f);
                    viewmodel.SetOn(true);
                }
                else if (viewmodel != null && viewmodel.Ready)
                {
                    eyeCamera.transform.position = Spot(eyeOff);
                    float zoom = view.ViewmodelZoom;   // the aim zoom PlayerViewSwitcher set this frame (not the scope's)
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
            gunKick = Mathf.Min(1f, gunKick + 0.85f);
            gunKickRoll = Random.Range(-1f, 1f);
            if (view == null || !view.IsThirdPerson || view.Scoped || aimRig == null || aimRig.HeldGun == null) return;
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
