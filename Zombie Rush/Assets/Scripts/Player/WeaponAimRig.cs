using System.Collections.Generic;
using UnityEngine;
using ZombieRush.Data;
using ZombieRush.Weapons;

namespace ZombieRush.Player
{
    /// Makes a humanoid body hold its gun and aim it where the screen centre points.
    /// The gun sits in `hold`, which is placed every frame: long guns with the stock in the right
    /// shoulder, pistols out in front of the chest, both turned at the aim point. Animator IK then
    /// puts both hands on the gun and turns the spine and head towards the aim point.
    ///
    /// Works on any humanoid avatar (code-built or Mixamo): hand angles come from the avatar's own
    /// T-pose, so nothing here is tied to one model. Sits on the model, next to its Animator.
    [RequireComponent(typeof(Animator))]
    public class WeaponAimRig : MonoBehaviour
    {
        public WeaponHoldConfig config;
        [Tooltip("Camera whose screen centre is the aim.")]
        public Camera aimCamera;
        [Tooltip("The player's root: the aim ray skips its own colliders.")]
        public Transform ignoreRoot;
        [Tooltip("Gun models are parented here. Made automatically when empty.")]
        public Transform hold;

        /// Gun tips down (reloading).
        public bool Lowered { get; set; }
        /// False = carry the gun lowered along the body's facing (running somewhere else than the
        /// crosshair); true = aim at the crosshair.
        public bool Aiming { get; set; } = true;
        /// 0..1 through the current reload (0 = not reloading). Drives the hand choreography.
        public float ReloadProgress { get; set; }
        /// 0..1+ how fast the body is moving: the carried gun bobs with it.
        public float MoveAmount { get; set; }
        /// First person: the gun drawn in front of the camera (its model). The hold follows it, so the
        /// body's hands (the first-person arms) grip the gun the player actually sees.
        public Transform FirstPersonGun { get; set; }
        Transform heldGun;
        /// The gun now in the body's hands (null for none).
        public Transform HeldGun => heldGun;
        /// How firmly the gun is held now (0 = not at all, e.g. dead or empty-handed).
        public float HoldWeight => hasGun && !(body != null && body.IsDead) ? weight : 0f;
        /// The held gun comes with body clips (BodyGunPose moves the arms; no arm IK or finger curl here).
        bool BodyClips => rig != null && rig.HasBodyClips;
        /// The held gun's Blender-made animation (null for a gun without one): its clips move the gun and
        /// both hand targets, so the procedural kick, reload path, pump and breathing are switched off.
        GunRig rig;
        /// How the gun in the hands is handled.
        public GunStyle Style { get; private set; } = GunStyle.Rifle;

        // Weapon change: the old gun is lowered, swapped at the bottom, the new one comes up.
        float swap, swapTarget;
        Transform pendingGun;
        GunStyle pendingStyle;
        System.Action pendingApply;

        // Gun parts that move on their own (found by name: pistols have both).
        Transform mag, slider;
        Vector3 magPos, sliderPos, wellL, chargeL;
        Quaternion magRot;
        bool partsDirty;
        Vector3 leftGripNow;

        float pumpTimer = -1f, kickRoll, bobPhase;
        readonly Vector3[] wp = new Vector3[20];
        readonly float[] wt = new float[20];
        int wn;

        Animator animator;
        CharacterBodyAnimator body;
        Transform chest, rightArm, leftArm, rightHand, leftHand;
        Quaternion restRight, restLeft;   // hand rotations in the avatar's T-pose, relative to this transform
        Quaternion wantRight, wantLeft;   // hand bone rotations to set after IK
        float handWeight;

        bool hasGun, pistol;
        Vector3 gripR, gripL;             // grip points in hold space
        float weight, recoil, lower, aimBlend = 1f;
        Vector3 aimPoint;
        readonly RaycastHit[] hits = new RaycastHit[12];

        HumanPoseHandler poseHandler;
        HumanPose pose;
        int[] gripMuscles, triggerMuscles;

        float Scale => animator != null && animator.isHuman ? animator.humanScale : 1f;

        void Awake()
        {
            animator = GetComponent<Animator>();
            body = GetComponent<CharacterBodyAnimator>();
            if (!animator.isHuman) { enabled = false; return; }

            chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Spine);
            rightArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            leftArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            restRight = RestRotation(rightHand);
            restLeft = RestRotation(leftHand);

            if (hold == null)
            {
                hold = new GameObject("WeaponHold").transform;
                hold.SetParent(transform, false);
            }
            if (config == null) config = ScriptableObject.CreateInstance<WeaponHoldConfig>();

            // Finger muscles: all curl around the gun, except the right index on the trigger.
            poseHandler = new HumanPoseHandler(animator.avatar, transform);
            var grip = new List<int>();
            var trigger = new List<int>();
            // A body whose four fingers are one block (only thumb + index mapped) grips with all of it.
            bool separateFingers = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal) != null;
            for (int i = 0; i < HumanTrait.MuscleCount; i++)
            {
                string n = HumanTrait.MuscleName[i];
                if (!n.Contains("Stretched") || !(n.StartsWith("Left ") || n.StartsWith("Right "))) continue;
                if (!(n.Contains("Thumb") || n.Contains("Index") || n.Contains("Middle") || n.Contains("Ring") || n.Contains("Little"))) continue;
                (separateFingers && n.StartsWith("Right Index") ? trigger : grip).Add(i);
            }
            gripMuscles = grip.ToArray();
            triggerMuscles = trigger.ToArray();
        }

        void OnDestroy() => poseHandler?.Dispose();

        /// Rotation of `bone` in the avatar's T-pose, relative to this transform.
        Quaternion RestRotation(Transform bone)
        {
            var rest = new Dictionary<string, Quaternion>();
            foreach (var s in animator.avatar.humanDescription.skeleton) rest[s.name] = s.rotation;
            Quaternion q = Quaternion.identity;
            for (var t = bone; t != null && t != transform; t = t.parent)
                q = (rest.TryGetValue(t.name, out var r) ? r : t.localRotation) * q;
            return q;
        }

        /// The gun now in the hands (a child of `hold`), or null for empty hands.
        public void SetGun(Transform gun) => SetGun(gun, GunStyle.Rifle);

        /// For checks: where the hold and the held gun point, relative to the body.
        public string Describe()
        {
            if (hold == null) return "no hold";
            Vector3 hf = transform.InverseTransformDirection(hold.forward);
            string s = $"hold fwd {hf.ToString("F2")} (pitch {Mathf.Asin(Mathf.Clamp(hf.y, -1f, 1f)) * Mathf.Rad2Deg:0}), swap {swap:0.00}, pin {(pistol ? gripR : stockBack).ToString("F2")}";
            if (heldGun != null)
            {
                var r = heldGun.GetComponentInChildren<GunRig>(true);
                if (r != null && r.muzzle != null)
                {
                    Vector3 md = transform.InverseTransformDirection(r.muzzle.position - r.transform.position);
                    var a = r.GetComponent<Animator>();
                    var ci = a != null && a.isActiveAndEnabled && a.isInitialized && a.runtimeAnimatorController != null ? a.GetCurrentAnimatorClipInfo(0) : null;
                    var st = a != null && a.isInitialized ? a.GetCurrentAnimatorStateInfo(0) : default;
                    if (leftHand != null && r.handL != null && leftArm != null)
                        s += $", left hand off target {Vector3.Distance(leftHand.position, r.handL.position):0.00} m (target {Vector3.Distance(leftArm.position, r.handL.position):0.00} m from the shoulder)";
                    if (rightHand != null && r.handR != null) s += $", right hand off {Vector3.Distance(rightHand.position, r.handR.position):0.00} m";
                    if (FirstPersonGun != null)
                    {
                        var fr = FirstPersonGun.GetComponentInChildren<GunRig>(true);
                        var fa = fr != null ? fr.GetComponent<Animator>() : null;
                        var fst = fa != null && fa.isInitialized ? fa.GetCurrentAnimatorStateInfo(0) : default;
                        s += $", FP gun {(fr != null ? fr.Now() : "-")} t {fst.normalizedTime:0.00} (body copy {r.Now()} t {st.normalizedTime:0.00}), FP handL vs copy {(fr != null ? Vector3.Distance(fr.handL.position, r.handL.position) : -1f):0.00} m";
                    }
                    s += $", gun muzzle dir {md.normalized.ToString("F2")}, clip {(ci != null && ci.Length > 0 ? ci[0].clip.name : "-")}, active {heldGun.gameObject.activeInHierarchy}" +
                         $", animator {(a == null ? "none" : $"ctrl {(a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name : "NULL")} enabled {a.enabled} init {a.isInitialized} avatar {(a.avatar != null ? a.avatar.name : "none")}")}";
                }
            }
            return s;
        }

        /// Brings `gun` into the hands with the usual draw animation: the current gun is lowered,
        /// `apply` (which shows `gun` and hides the old one) runs at the bottom, then the new gun comes up.
        public void Equip(Transform gun, GunStyle style, System.Action apply)
        {
            pendingGun = gun; pendingStyle = style; pendingApply = apply;
            if (!hasGun || weight <= 0.01f) { CommitEquip(); swap = 1f; swapTarget = 0f; return; }
            swapTarget = 1f;
        }

        void CommitEquip()
        {
            RestoreParts();
            pendingApply?.Invoke();
            pendingApply = null;
            SetGun(pendingGun, pendingStyle);
            swapTarget = 0f;
            pumpTimer = -1f;
        }

        public void SetGun(Transform gun, GunStyle style)
        {
            heldGun = gun;
            hasGun = false;
            mag = slider = null;
            rig = null;
            if (gun == null) return;
            rig = gun.GetComponentInChildren<GunRig>(true);

            var b = new Bounds();
            bool any = false;
            foreach (var mf in gun.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = hold.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            if (!any) return;

            float len = b.size.z;
            pistol = len < config.pistolMaxLength;
            if (pistol)
            {
                gripR = new Vector3(b.center.x, b.min.y + b.size.y * 0.35f, b.min.z + len * 0.3f);
                gripL = gripR;
            }
            else
            {
                gripR = new Vector3(b.center.x, b.center.y - b.size.y * 0.15f, b.min.z + len * config.rightGripAlong);
                gripL = new Vector3(b.center.x, b.center.y - b.size.y * 0.1f, b.min.z + len * config.leftGripAlong);
            }
            // Long guns: the stock's back end goes into the shoulder pocket.
            stockBack = new Vector3(b.center.x, b.center.y, b.min.z);

            Style = pistol ? GunStyle.Pistol : style;
            if (rig != null)
            {
                // the clips hold the gun by its own grip: the model's root is the right hand's grip point
                Style = rig.style;
                pistol = rig.style == GunStyle.Pistol;
                gripR = hold.InverseTransformPoint(rig.transform.position);
                gripL = gripR;
            }
            // Where the left hand goes to change magazines / load shells / work the action (hold space).
            wellL = Style == GunStyle.Pistol ? new Vector3(gripR.x, b.min.y, gripR.z)
                  : Style == GunStyle.Shotgun ? new Vector3(b.center.x, b.min.y + b.size.y * 0.3f, b.min.z + len * 0.5f)
                  : new Vector3(b.center.x, b.min.y, b.min.z + len * 0.45f);
            chargeL = new Vector3(b.center.x, b.max.y, b.min.z + len * (Style == GunStyle.Pistol ? 0.35f : 0.4f));

            // Parts that move on their own: the magazine and the slide of a pistol.
            mag = Style == GunStyle.Shotgun || rig != null ? null : FindPart(gun, "magazine");
            slider = Style == GunStyle.Pistol && rig == null ? (FindPart(gun, "slider") ?? FindPart(gun, "slide")) : null;
            if (mag != null) { magPos = hold.InverseTransformPoint(mag.position); magRot = Quaternion.Inverse(hold.rotation) * mag.rotation; }
            if (slider != null) sliderPos = hold.InverseTransformPoint(slider.position);
            partsDirty = false;
            hasGun = true;
        }

        Vector3 stockBack;

        static Transform FindPart(Transform root, string key)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                if (n.Contains(key) && !n.Contains("location")) return t;
            }
            return null;
        }

        /// One shot's kick.
        public void Kick()
        {
            recoil = Mathf.Min(1f, recoil + 0.7f);
            kickRoll = Random.Range(-1f, 1f);
            if (Style == GunStyle.Shotgun) pumpTimer = 0f;
        }

        float KickScale => Style == GunStyle.Pistol ? config.pistolKick : Style == GunStyle.Shotgun ? config.shotgunKick : config.rifleKick;

        /// Magazine and slide follow the reload / the shot. Runs after IK so the hand is where it ended up.
        void AnimateParts()
        {
            float p = ReloadProgress;
            float r = recoil * recoil * (3f - 2f * recoil);
            bool active = hasGun && (p > 0.001f || (slider != null && r > 0.001f));
            if (!active)
            {
                if (partsDirty) { RestoreParts(); }
                return;
            }
            partsDirty = true;
            if (mag != null)
            {
                // Out at 0.30-0.45 (carried by the left hand), in the pouch until 0.64, new one carried in, seated by 0.84.
                bool hidden = p > 0.46f && p < 0.64f;
                float k = 0f;
                if (p > 0.30f && p < 0.84f)
                    k = p < 0.36f ? Mathf.InverseLerp(0.30f, 0.36f, p) : p < 0.78f ? 1f : Mathf.InverseLerp(0.84f, 0.78f, p);
                mag.gameObject.SetActive(!hidden);
                mag.position = Vector3.Lerp(hold.TransformPoint(magPos), leftGripNow, k);
                mag.rotation = hold.rotation * magRot;
            }
            if (slider != null)
            {
                float rack = p > 0.90f && p < 0.97f ? Mathf.Sin(Mathf.PI * Mathf.InverseLerp(0.90f, 0.97f, p)) : 0f;
                slider.position = hold.TransformPoint(sliderPos) - hold.forward * (0.03f * Scale * Mathf.Clamp01(rack + r));
            }
        }

        void RestoreParts()
        {
            partsDirty = false;
            if (mag != null && hold != null)
            {
                mag.gameObject.SetActive(true);
                mag.position = hold.TransformPoint(magPos);
                mag.rotation = hold.rotation * magRot;
            }
            if (slider != null && hold != null) slider.position = hold.TransformPoint(sliderPos);
        }

        /// Left-hand path through a reload: to the magazine, out to the pouch, back in, then the
        /// action (charging handle / slide). Shotguns load shells from the pouch and pump.
        Vector3 ReloadHand(float p, Vector3 grip)
        {
            Vector3 F = hold.forward, U = hold.up;
            float k = Scale;
            Vector3 well = hold.TransformPoint(wellL);
            Vector3 charge = hold.TransformPoint(chargeL);
            Vector3 pouch = chest.position + YawRotation() * (config.magPouch * k);
            wn = 0;
            Add(0f, grip);
            if (Style == GunStyle.Shotgun)
            {
                Vector3 port = well - U * (0.02f * k);
                Add(0.08f, pouch); Add(0.18f, port); Add(0.26f, port);
                Add(0.34f, pouch); Add(0.44f, port); Add(0.52f, port);
                Add(0.60f, pouch); Add(0.70f, port); Add(0.78f, port);
                Add(0.86f, grip + F * (0.05f * k)); Add(0.92f, grip - F * (config.pumpDistance * k)); Add(0.97f, grip + F * (0.03f * k));
                Add(1f, grip);
            }
            else
            {
                Add(0.16f, well - U * (0.05f * k)); Add(0.30f, well - U * (0.04f * k));
                Add(0.44f, well - U * (0.16f * k) - F * (0.03f * k)); Add(0.54f, pouch); Add(0.64f, pouch);
                Add(0.74f, well - U * (0.16f * k)); Add(0.82f, well - U * (0.03f * k)); Add(0.86f, well);
                Add(0.91f, charge); Add(0.94f, charge - F * (0.08f * k)); Add(0.97f, charge);
                Add(1f, grip);
            }
            return Sample(p);
        }

        void Add(float t, Vector3 pos) { wt[wn] = t; wp[wn] = pos; wn++; }

        // Catmull-Rom through the waypoints: the hand keeps moving instead of stopping at each one.
        Vector3 Sample(float p)
        {
            if (wn == 0) return Vector3.zero;
            if (p <= wt[0]) return wp[0];
            for (int i = 1; i < wn; i++)
            {
                if (p > wt[i]) continue;
                float u = Mathf.InverseLerp(wt[i - 1], wt[i], p);
                Vector3 a = wp[Mathf.Max(i - 2, 0)], b = wp[i - 1], c = wp[i], d = wp[Mathf.Min(i + 1, wn - 1)];
                float u2 = u * u, u3 = u2 * u;
                return 0.5f * (2f * b + (-a + c) * u + (2f * a - 5f * b + 4f * c - d) * u2 + (-a + 3f * b - 3f * c + d) * u3);
            }
            return wp[wn - 1];
        }

        // IK has placed the wrists; now close the fingers and turn the hands onto the gun. Hand
        // rotation is set on the bones directly, so it does not depend on Unity's IK goal axes.
        void LateUpdate()
        {
            CurlFingers();
            AnimateParts();
            if (handWeight <= 0f || rightHand == null || leftHand == null) return;
            rightHand.rotation = Quaternion.Slerp(rightHand.rotation, wantRight, handWeight);
            leftHand.rotation = Quaternion.Slerp(leftHand.rotation, wantLeft, handWeight);
        }

        /// Closes the hands around the gun. Runs after animation and IK, on the final pose.
        void CurlFingers()
        {
            if (poseHandler == null || !hasGun || weight <= 0f || (body != null && body.IsDead) || BodyClips) return;
            poseHandler.GetHumanPose(ref pose);
            foreach (int i in gripMuscles) pose.muscles[i] = Mathf.Lerp(pose.muscles[i], config.fingerGrip, weight);
            foreach (int i in triggerMuscles) pose.muscles[i] = Mathf.Lerp(pose.muscles[i], config.triggerFinger, weight);
            poseHandler.SetHumanPose(ref pose);
        }

        void OnAnimatorIK(int layer)
        {
            if (rightHand == null || leftHand == null || chest == null) return;

            if (layer == 0) UpdateHold();

            bool dead = body != null && body.IsDead;
            animator.SetLookAtWeight(dead ? 0f : weight, config.spineWeight * aimBlend, config.headWeight, 0f, config.lookClamp);
            Vector3 ahead = chest.position + YawRotation() * Vector3.forward * 10f;
            animator.SetLookAtPosition(Vector3.Lerp(ahead, BodyLookTarget(), aimBlend));

            float w = hasGun && !dead ? weight : 0f;
            handWeight = w;
            if (BodyClips)
            {
                // the body clips pose the arms and fingers (BodyGunPose, after the Animator)
                handWeight = 0f;
                animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
                animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
                animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
                animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
                animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 0f);
                animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
                return;
            }
            Vector3 F = hold.forward, U = hold.up, R = hold.right;

            if (rig != null && rig.handR != null && rig.handL != null)
            {
                // Blender-made animation: both hands go where the clip puts them, turned as the clip turns them.
                rig.HandAxes(true, out var rf, out var rp);
                Hand(AvatarIKGoal.RightHand, rig.handR.position, rf, rp, restRight, true, w, out wantRight);
                rig.HandAxes(false, out var lf, out var lp);
                leftGripNow = rig.handL.position;
                Hand(AvatarIKGoal.LeftHand, rig.handL.position, lf, lp, restLeft, false, w, out wantLeft);
                Quaternion yaw0 = YawRotation();
                Hint(AvatarIKHint.RightElbow, rightArm, yaw0 * (config.rightElbowHint * Scale), w);
                Hint(AvatarIKHint.LeftElbow, leftArm, yaw0 * (config.leftElbowHint * Scale), w);
                return;
            }
            // Right hand on the grip, palm facing left, fingers forward.
            Hand(AvatarIKGoal.RightHand, hold.TransformPoint(gripR), (F + U * 0.15f).normalized, -R, restRight, true, w, out wantRight);
            // Left hand: cups the right one (pistol) or sits under the handguard (long gun) ...
            Vector3 left = pistol ? hold.TransformPoint(gripR) - R * 0.035f * Scale : hold.TransformPoint(gripL);
            // ... and leaves it to change magazines, load shells and work the action during a reload.
            if (ReloadProgress > 0.001f) left = ReloadHand(ReloadProgress, left);
            // Shotgun pump after each shot.
            if (pumpTimer >= 0f)
            {
                float u = (pumpTimer - config.pumpDelay) / Mathf.Max(0.05f, config.pumpTime);
                if (u > 0f && u < 1f) left -= F * (Mathf.Sin(Mathf.PI * u) * config.pumpDistance * Scale);
            }
            leftGripNow = left;
            if (pistol)
                Hand(AvatarIKGoal.LeftHand, left, (F * 0.8f - U * 0.3f + R * 0.4f).normalized, R, restLeft, false, w, out wantLeft);
            else
                Hand(AvatarIKGoal.LeftHand, left, (F * 0.5f + R * 0.85f).normalized, U, restLeft, false, w, out wantLeft);

            Quaternion yaw = YawRotation();
            Hint(AvatarIKHint.RightElbow, rightArm, yaw * (config.rightElbowHint * Scale), w);
            Hint(AvatarIKHint.LeftElbow, leftArm, yaw * (config.leftElbowHint * Scale), w);
        }

        void UpdateHold()
        {
            float dt = Time.deltaTime;
            bool dead = body != null && body.IsDead;
            weight = Mathf.MoveTowards(weight, hasGun && !dead ? 1f : 0f, dt / Mathf.Max(0.01f, config.blendTime));
            recoil = Mathf.MoveTowards(recoil, 0f, config.recoilReturn * dt * Mathf.Max(0.2f, recoil));
            lower = Mathf.MoveTowards(lower, Lowered ? 1f : 0f, dt * 5f);

            aimBlend = Mathf.MoveTowards(aimBlend, Aiming ? 1f : 0f, dt * 4f);
            aimPoint = FindAimPoint();
            Quaternion yaw = YawRotation();
            float k = Scale;

            // Weapon change: lower the old gun, swap at the bottom, raise the new one.
            swap = Mathf.MoveTowards(swap, swapTarget, dt / Mathf.Max(0.02f, config.swapTime));
            if (swapTarget >= 1f && swap >= 1f) CommitEquip();
            float s = swap * swap * (3f - 2f * swap);

            if (pumpTimer >= 0f)
            {
                pumpTimer += dt;
                if (pumpTimer > config.pumpDelay + config.pumpTime) pumpTimer = -1f;
            }

            Vector3 anchor = pistol ? chest.position + yaw * (config.pistolGrip * k) : chest.position + yaw * (config.shoulderPocket * k);
            // Carry: along the body's facing, muzzle tipped down. Aim: at the crosshair point.
            Quaternion carry = yaw * Quaternion.Euler(config.carryTilt, 0f, 0f);
            Quaternion aim = Quaternion.Slerp(carry, Quaternion.LookRotation(aimPoint - anchor, Vector3.up), aimBlend);
            float r = recoil * recoil * (3f - 2f * recoil);
            float l = lower * lower * (3f - 2f * lower);
            float kick = KickScale;
            if (rig != null) { r = 0f; l = 0f; }   // the gun's own clips kick, tilt and reload it
            aim *= Quaternion.Euler(-config.recoilPitch * kick * r + config.reloadTilt * l + config.swapTilt * s,
                                    0f, config.reloadRoll * l + kickRoll * 1.2f * kick * r);

            // Breathing and walking bob, faded out while shooting, reloading or changing guns.
            float calm = Mathf.Clamp01(1f - recoil - l - s);
            float breathe = rig != null ? 0f : 1f;   // the Idle clip breathes
            aim *= breathe == 0f ? Quaternion.identity : Quaternion.Euler(Mathf.Sin(Time.time * 1.6f) * config.breathe * calm, Mathf.Sin(Time.time * 1.1f + 1f) * config.breathe * 0.7f * calm, 0f);
            bobPhase += dt * (5f + 4f * MoveAmount);
            Vector3 bob = yaw * new Vector3(Mathf.Sin(bobPhase * 0.5f) * 0.006f, -Mathf.Abs(Mathf.Sin(bobPhase)) * 0.012f, 0f)
                          * (MoveAmount * config.bobAmount * k * calm);

            Vector3 pin = pistol ? gripR : stockBack;
            hold.rotation = aim;
            hold.position = anchor - aim * pin - aim * Vector3.forward * (config.recoilBack * kick * r) + bob
                            - Vector3.up * (config.swapDrop * k * s);

            // First person: lay the body's (hidden) copy of the gun exactly over the one in front of the
            // camera, so the hands land on the visible gun and follow its recoil, reload and draw.
            if (FirstPersonGun != null && heldGun != null)
            {
                hold.rotation = FirstPersonGun.rotation * Quaternion.Inverse(heldGun.localRotation);
                hold.position = FirstPersonGun.position - hold.rotation * Vector3.Scale(heldGun.localPosition, hold.lossyScale);
            }
        }

        /// Where the spine and head turn to: the aim point, but with its up/down angle limited so the
        /// body does not fold over when aiming at the ground (the gun itself still aims at aimPoint).
        Vector3 BodyLookTarget()
        {
            Vector3 d = aimPoint - chest.position;
            float flat = new Vector2(d.x, d.z).magnitude;
            if (flat < 0.01f) return aimPoint;
            float pitch = Mathf.Atan2(d.y, flat) * Mathf.Rad2Deg;
            float limited = Mathf.Clamp(pitch, -config.maxBendDown, config.maxBendUp);
            Vector3 dir = new Vector3(d.x, 0f, d.z) / flat;
            return chest.position + (dir * Mathf.Cos(limited * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(limited * Mathf.Deg2Rad)) * 10f;
        }

        /// The body's own facing (flat). It turns to the crosshair when aiming, to the travel
        /// direction when running free, so the gun and elbows follow the body.
        Quaternion YawRotation()
        {
            Vector3 f = transform.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 0.0001f) f = transform.forward;
            return Quaternion.LookRotation(f.normalized, Vector3.up);
        }

        Vector3 FindAimPoint()
        {
            if (aimCamera == null) return chest.position + transform.forward * config.aimDistance;
            var ray = new Ray(aimCamera.transform.position, aimCamera.transform.forward);
            int n = Physics.RaycastNonAlloc(ray, hits, config.aimDistance, ~0, QueryTriggerInteraction.Ignore);
            float best = config.aimDistance;
            for (int i = 0; i < n; i++)
            {
                if (ignoreRoot != null && hits[i].collider.transform.IsChildOf(ignoreRoot)) continue;
                // Ignore anything between the camera and the player (third person).
                if (Vector3.Dot(hits[i].point - chest.position, ray.direction) < 0.3f) continue;
                best = Mathf.Min(best, hits[i].distance);
            }
            return ray.GetPoint(best);
        }

        /// Puts a hand's wrist where it must be to hold `grip` with the fingers along `fingers` and
        /// the palm facing `palm`; `bone` is the hand rotation that goes with it.
        void Hand(AvatarIKGoal goal, Vector3 grip, Vector3 fingers, Vector3 palm, Quaternion rest, bool right, float w, out Quaternion bone)
        {
            // In the T-pose the fingers point straight out to the side and the palm faces down.
            Quaternion tpose = Quaternion.LookRotation(right ? Vector3.right : Vector3.left, Vector3.up);
            bone = Quaternion.LookRotation(fingers, -palm) * Quaternion.Inverse(tpose) * rest;
            Vector3 wrist = grip - fingers * (config.wristBack * Scale) - palm * (config.palmOffset * Scale);

            animator.SetIKPositionWeight(goal, w);
            animator.SetIKRotationWeight(goal, 0f);
            animator.SetIKPosition(goal, wrist);
        }

        void Hint(AvatarIKHint hint, Transform shoulder, Vector3 offset, float w)
        {
            if (shoulder == null) return;
            animator.SetIKHintPositionWeight(hint, w);
            animator.SetIKHintPosition(hint, shoulder.position + offset);
        }
    }
}
