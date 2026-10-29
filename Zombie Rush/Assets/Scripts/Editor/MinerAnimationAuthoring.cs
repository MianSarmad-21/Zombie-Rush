using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZombieRush.EditorTools
{
    /// The Miner Juggernaut's own animations, authored in code: Idle, Walk, Run, Attack, Hit.
    ///
    /// Poses are written in plain world terms (thigh 22 degrees forward of vertical, hammer arm
    /// raised behind the head, eyes on the horizon) and the Solver finds the humanoid muscle values
    /// that produce them on the Miner's own skeleton, so every clip is smooth, stays inside the
    /// rig's joint limits and can never flip, and the planted foot stays on the ground. Needs the
    /// avatar built from the bind pose straightened into a T-pose (see MinerZombieTools).
    static class MinerAnimationAuthoring
    {
        const float Fps = 30f;
        internal static System.Text.StringBuilder Trace; // diagnostics, off unless set
        public const string ClipFolder = "Assets/Models/2026-10-18_MinerJuggernaut/Animations";

        /// A pose in world terms. Angles in degrees.
        internal class P
        {
            public float Bob;                                  // extra body height (share of hip height)
            public float Pitch, Yaw, Roll;                     // pelvis: lean forward, turn, roll right-side-up
            public float Spine, Chest, SpineSide, Twist;       // extra forward bend on top of the pelvis; side lean; shoulders turn
            public float Look, HeadSide, HeadTurn;             // gaze below horizontal, head tilt, head turn
            public Leg L = new Leg(), R = new Leg();
            public Arm LA = new Arm(), RA = new Arm();

            public class Leg { public float Thigh, Knee, Out = 8f, Toe, Plant = 1f; }  // thigh forward of vertical; knee bend; spread; toes up; how planted (0 = lifted, 1 = flat on the ground)
            public class Arm { public float Down = 85f, Fwd = 20f, Elbow = 15f, Shrug; } // below horizontal (-up); forward of the side; elbow bend

            public P Copy() => Lerp(this, this, 0f);

            public static P Lerp(P a, P b, float t)
            {
                float F(float x, float y) => Mathf.LerpUnclamped(x, y, t);
                Leg LL(Leg x, Leg y) => new Leg { Thigh = F(x.Thigh, y.Thigh), Knee = F(x.Knee, y.Knee), Out = F(x.Out, y.Out), Toe = F(x.Toe, y.Toe), Plant = F(x.Plant, y.Plant) };
                Arm AA(Arm x, Arm y) => new Arm { Down = F(x.Down, y.Down), Fwd = F(x.Fwd, y.Fwd), Elbow = F(x.Elbow, y.Elbow), Shrug = F(x.Shrug, y.Shrug) };
                return new P
                {
                    Bob = F(a.Bob, b.Bob), Pitch = F(a.Pitch, b.Pitch), Yaw = F(a.Yaw, b.Yaw), Roll = F(a.Roll, b.Roll),
                    Spine = F(a.Spine, b.Spine), Chest = F(a.Chest, b.Chest), SpineSide = F(a.SpineSide, b.SpineSide), Twist = F(a.Twist, b.Twist),
                    Look = F(a.Look, b.Look), HeadSide = F(a.HeadSide, b.HeadSide), HeadTurn = F(a.HeadTurn, b.HeadTurn),
                    L = LL(a.L, b.L), R = LL(a.R, b.R), LA = AA(a.LA, b.LA), RA = AA(a.RA, b.RA),
                };
            }
        }

        internal static float Wave(float p, float offset = 0f) => Mathf.Sin((p + offset) * Mathf.PI * 2f);
        internal static float Cos(float p, float offset = 0f) => Mathf.Cos((p + offset) * Mathf.PI * 2f);
        /// Smooth periodic bump around `center` (0..1 cycle): 1 at the centre, 0 beyond `width`.
        /// A foot's plant amount over a gait cycle: 1 while it's down (from phase `down` to `up`,
        /// wrapping), easing on and off over `soft` so a step never snaps.
        internal static float Planted(float p, float down, float up, float soft = 0.12f)
        {
            float x = Mathf.Repeat(p - down, 1f), len = Mathf.Repeat(up - down, 1f);
            if (x > len) return 0f;
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x / soft)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((len - x) / soft));
        }

        internal static float Bump(float p, float center, float width)
        {
            float d = Mathf.Abs(Mathf.Repeat(p - center + 0.5f, 1f) - 0.5f) / width;
            return d >= 1f ? 0f : (1f - d * d) * (1f - d * d);
        }

        // ------------------------------------------------------------------ the clips

        /// A hulking, hunched giant: torso leaning ~38 degrees, head up to stare ahead, knees soft,
        /// hammer hanging from the right hand, the free left arm dangling forward.
        internal static P Stance()
        {
            var p = new P { Pitch = 10f, Spine = 10f, Chest = 11f, Look = 8f };
            p.L.Thigh = 10f; p.L.Knee = 22f; p.L.Out = 9f;
            p.R.Thigh = -4f; p.R.Knee = 16f; p.R.Out = 9f;
            p.RA.Down = 82f; p.RA.Fwd = 30f; p.RA.Elbow = 15f; p.RA.Shrug = 6f;
            p.LA.Down = 68f; p.LA.Fwd = 50f; p.LA.Elbow = 30f; p.LA.Shrug = 6f;
            return p;
        }

        /// Heaving breaths, a slow sway, the head slowly rolling.
        static P Idle(float p)
        {
            var s = Stance();
            s.Chest += -4f * Wave(p);                 // chest heaves
            s.Bob = 0.006f * Wave(p);
            s.Roll = 2f * Wave(p, 0.25f);
            s.SpineSide = 3f * Wave(p, 0.25f);
            s.Look += 4f * Wave(p, 0.3f);
            s.HeadSide = 6f * Wave(p, 0.1f);
            s.HeadTurn = 8f * Wave(p, 0.6f);
            s.LA.Fwd += 6f * Wave(p, 0.45f); s.LA.Elbow += 6f * Wave(p, 0.5f);
            s.RA.Fwd += 3f * Wave(p, 0.2f);
            return s;
        }

        /// A dragging limp. Left is the good leg and takes heavy steps. The right leg is stiff,
        /// swung out round the side, and the body sinks onto it every step.
        static P Walk(float p)
        {
            var s = Stance();
            s.L.Plant = Planted(p, 0.95f, 0.55f); s.R.Plant = Planted(p, 0.45f, 0.05f);   // left down 1st half, right 2nd, brief overlap
            s.Pitch = 12f; s.Spine = 12f; s.Chest = 12f;
            s.L.Thigh = 22f * Cos(p); s.L.Knee = 10f + 50f * Bump(p, 0.72f, 0.2f); s.L.Out = 8f;
            s.R.Thigh = 12f * Cos(p, -0.5f) - 3f; s.R.Knee = 6f + 14f * Bump(p, 0.25f, 0.16f);
            s.R.Out = 9f + 9f * Bump(p, 0.25f, 0.22f); s.R.Toe = -12f;   // swung out, toes scraping
            s.Bob = -0.03f * Bump(p, 0.75f, 0.2f);
            s.Roll = -7f * Bump(p, 0.75f, 0.25f) + 3f * Bump(p, 0.25f, 0.25f);
            s.Yaw = 8f * Wave(p);
            s.SpineSide = 8f * Bump(p, 0.75f, 0.25f) - 3f * Bump(p, 0.25f, 0.25f);
            s.Twist = -10f * Wave(p);
            s.RA.Fwd = 25f + 15f * Wave(p, -0.5f);                      // hammer swings heavily, low
            s.LA.Fwd = 50f + 15f * Wave(p, 0.1f); s.LA.Elbow = 30f + 8f * Wave(p, 0.2f);
            s.Look = 8f + 5f * Wave(p);
            s.HeadSide = 7f * Wave(p, -0.3f);
            return s;
        }

        /// Bent-forward charge: left arm reaching for the player, hammer cocked back, heavy
        /// uneven strides.
        static P Run(float p)
        {
            var s = Stance();
            s.L.Plant = Planted(p, 0.97f, 0.4f); s.R.Plant = Planted(p, 0.47f, 0.9f);     // running: shorter contact
            s.Pitch = 18f; s.Spine = 14f; s.Chest = 13f; s.Look = 0f;
            s.L.Thigh = 30f * Cos(p) + 8f; s.L.Knee = 25f + 65f * Bump(p, 0.7f, 0.22f);
            s.R.Thigh = 26f * Cos(p, -0.5f) + 6f; s.R.Knee = 22f + 55f * Bump(p, 0.2f, 0.22f); s.R.Out = 11f;
            s.Yaw = 12f * Wave(p);
            s.Roll = 5f * Wave(p, 0.25f);
            s.Twist = -14f * Wave(p);
            s.LA.Down = 15f; s.LA.Fwd = 85f; s.LA.Elbow = 20f + 10f * Mathf.Sin(p * Mathf.PI * 4f); s.LA.Shrug = 10f; // reaching, clawing
            s.RA.Down = 60f; s.RA.Fwd = -30f + 25f * Wave(p, -0.5f); s.RA.Elbow = 70f;                                 // hammer cocked back
            s.HeadSide = 4f * Wave(p, 0.4f);
            return s;
        }

        public const float AttackLength = 2.4f, AttackImpact = 1.05f;

        /// Overhead hammer slam: slow windup (arch back, hammer behind the head, roaring up),
        /// short hold, fast strike into the ground in front, crouched hold, heavy recovery.
        static P Attack(float t)
        {
            var start = Stance();
            start.L.Thigh = 15f; start.R.Thigh = -10f;

            // Lift: the hammer comes up in front of the body on its way overhead (not out sideways).
            var lift = start.Copy();
            lift.Pitch = 4f; lift.Spine = 2f; lift.Chest = 0f; lift.Look = -8f;
            lift.RA.Down = 10f; lift.RA.Fwd = 70f; lift.RA.Elbow = 70f; lift.RA.Shrug = 8f;
            lift.LA.Down = 40f; lift.LA.Fwd = 70f; lift.LA.Elbow = 25f;
            lift.Yaw = -6f; lift.Twist = -8f;

            var windup = start.Copy();
            windup.Pitch = -5f; windup.Spine = -10f; windup.Chest = -12f; windup.Look = -25f;
            windup.RA.Down = -80f; windup.RA.Fwd = -40f; windup.RA.Elbow = 80f; windup.RA.Shrug = 12f;
            windup.LA.Down = 10f; windup.LA.Fwd = 80f; windup.LA.Elbow = 20f;
            windup.L.Knee = 15f; windup.R.Knee = 12f;
            windup.Yaw = -15f; windup.Twist = -20f; windup.Bob = 0.01f;

            var hold = windup.Copy();
            hold.RA.Down = -86f; hold.Twist = -24f; hold.Look = -30f;

            var impact = start.Copy();
            impact.Pitch = 25f; impact.Spine = 18f; impact.Chest = 18f; impact.Look = 35f;
            impact.RA.Down = 40f; impact.RA.Fwd = 90f; impact.RA.Elbow = 5f; impact.RA.Shrug = 0f;
            impact.LA.Down = 50f; impact.LA.Fwd = 20f; impact.LA.Elbow = 30f;
            impact.L.Thigh = 40f; impact.L.Knee = 50f; impact.R.Thigh = 0f; impact.R.Knee = 45f;
            impact.Yaw = 15f; impact.Twist = 18f;

            var settle = impact.Copy();
            settle.RA.Down = 45f; settle.Chest = 20f; settle.Look = 25f;

            var keys = new[] { start, lift, windup, hold, impact, settle, start };
            var times = new[] { 0f, 0.38f, 0.75f, 0.85f, AttackImpact, 1.5f, AttackLength };
            int i = 0;
            while (i < times.Length - 2 && t > times[i + 1]) i++;
            float u = Mathf.InverseLerp(times[i], times[i + 1], t);
            u = i == 3 ? u * u * u : Mathf.SmoothStep(0f, 1f, u); // the strike (hold -> impact) accelerates into the ground
            return P.Lerp(keys[i], keys[i + 1], u);
        }

        const float HitLength = 0.7f;

        /// Jolted back by a shot, arms flung out, then settles.
        static P Hit(float t)
        {
            var a = Stance();
            var b = Stance();
            b.Pitch = -8f; b.Spine = -10f; b.Chest = -12f; b.Look = -25f; b.HeadSide = 12f;
            b.LA.Down = 30f; b.LA.Fwd = 20f; b.LA.Elbow = 40f; b.RA.Down = 60f; b.RA.Fwd = 0f;
            b.L.Knee = 28f; b.R.Knee = 26f; b.Twist = 12f;
            float u = t < 0.12f ? Mathf.SmoothStep(0f, 1f, t / 0.12f) : 1f - Mathf.SmoothStep(0f, 1f, (t - 0.12f) / (HitLength - 0.12f));
            return P.Lerp(a, b, u);
        }


        // ------------------------------------------------------------------ pose -> muscles

        /// Turns a world-terms pose into muscle values on the Miner's real skeleton. Each limb is
        /// solved by searching its muscles until the bone points where the pose says, measured
        /// on the posed model - starting from the previous frame's answer, so motion stays
        /// continuous and can never flip. The hips are then raised or lowered until the lowest
        /// foot touches the ground.
        class Solver
        {
            readonly HumanPoseHandler handler;
            HumanPose hp;
            readonly float[] m = new float[HumanTrait.MuscleCount];
            readonly Transform[] all;
            readonly Vector3[] restPos;
            readonly Quaternion[] restRot;
            readonly Transform lThigh, lShin, lFoot, rThigh, rShin, rFoot;
            readonly Transform lArm, lFore, lHand, rArm, rFore, rHand, head, headTop, hips, spine, neck;
            readonly Vector3 R, U = Vector3.up, F;
            readonly float ground, pelvis0, torso0, head0;   // neutral pelvis / torso / head forward tilt
            readonly Transform lToes, rToes;
            readonly float groundToes;                       // toe height of a flat foot on the ground
            float pitch;
            public readonly bool ok;
            Quaternion bodyRot = Quaternion.identity;
            float bodyY = 1f;

            public Solver(GameObject model, Avatar avatar)
            {
                var animator = model.GetComponent<Animator>();
                if (animator == null) animator = model.AddComponent<Animator>();
                animator.avatar = avatar;
                Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);
                lThigh = B(HumanBodyBones.LeftUpperLeg); lShin = B(HumanBodyBones.LeftLowerLeg); lFoot = B(HumanBodyBones.LeftFoot);
                rThigh = B(HumanBodyBones.RightUpperLeg); rShin = B(HumanBodyBones.RightLowerLeg); rFoot = B(HumanBodyBones.RightFoot);
                lArm = B(HumanBodyBones.LeftUpperArm); lFore = B(HumanBodyBones.LeftLowerArm); lHand = B(HumanBodyBones.LeftHand);
                rArm = B(HumanBodyBones.RightUpperArm); rFore = B(HumanBodyBones.RightLowerArm); rHand = B(HumanBodyBones.RightHand);
                head = B(HumanBodyBones.Head);
                headTop = head != null && head.childCount > 0 ? head.GetChild(0) : null;
                hips = B(HumanBodyBones.Hips); spine = B(HumanBodyBones.Spine); neck = B(HumanBodyBones.Neck);
                lToes = B(HumanBodyBones.LeftToes); rToes = B(HumanBodyBones.RightToes);
                if (neck == null) neck = head;
                ok = avatar != null && avatar.isHuman && lThigh && lShin && lFoot && rThigh && rShin && rFoot
                     && lArm && lFore && lHand && rArm && rFore && rHand && head && headTop && hips && spine;
                if (!ok) return;

                all = model.GetComponentsInChildren<Transform>(true);
                restPos = System.Array.ConvertAll(all, t => t.localPosition);
                restRot = System.Array.ConvertAll(all, t => t.localRotation);
                handler = new HumanPoseHandler(avatar, model.transform);
                hp = new HumanPose();

                R = model.transform.right; R.y = 0f;
                if (Vector3.Dot(rThigh.position - lThigh.position, R) < 0f) R = -R; // the model's own right side
                R.Normalize();
                F = Vector3.Cross(R, U);                                            // left-handed: right x up = forward

                // Standing straight (measured reference): where the feet are = the ground.
                Set("Left Upper Leg Front-Back", 0.6f); Set("Right Upper Leg Front-Back", 0.6f);
                Set("Left Lower Leg Stretch", 1f); Set("Right Lower Leg Stretch", 1f);
                Apply();
                ground = Mathf.Min(lFoot.position.y, rFoot.position.y);
                // A flat foot's toes sit this far below the ankle: take the flatter-looking (lower) foot.
                groundToes = ground - 0.5f * Vector3.Distance(lFoot.position, lToes != null ? lToes.position : lFoot.position);
                if (lToes && rToes) groundToes = Mathf.Min(lToes.position.y, rToes.position.y);

                // Neutral posture (no muscles, body upright): the tilts every pose is measured from.
                System.Array.Clear(m, 0, m.Length);
                Apply();
                pelvis0 = Tilt(hips, spine);
                torso0 = Tilt(hips, neck);
                head0 = Tilt(head, headTop);
            }

            /// Forward tilt of the segment a -> b from vertical, degrees (negative = leaning back).
            float Tilt(Transform a, Transform b)
            {
                Vector3 d = b.position - a.position;
                return Mathf.Atan2(Vector3.Dot(d, F), Vector3.Dot(d, U)) * Mathf.Rad2Deg;
            }

            public void Dispose()
            {
                handler?.Dispose();
                if (all != null) for (int i = 0; i < all.Length; i++) { all[i].localPosition = restPos[i]; all[i].localRotation = restRot[i]; }
            }

            static readonly Dictionary<string, int> index = new Dictionary<string, int>();
            static int I(string name)
            {
                if (index.Count == 0) for (int i = 0; i < HumanTrait.MuscleCount; i++) index[HumanTrait.MuscleName[i]] = i;
                return index.TryGetValue(name, out var i2) ? i2 : -1;
            }
            void Set(string name, float v) { int i = I(name); if (i >= 0) m[i] = Mathf.Clamp(v, -1f, 1f); }
            float Get(string name) { int i = I(name); return i >= 0 ? m[i] : 0f; }

            void Apply()
            {
                hp.muscles = m;
                hp.bodyPosition = new Vector3(0f, bodyY, 0f);
                hp.bodyRotation = bodyRot;
                handler.SetHumanPose(ref hp);
            }

            Vector3 W(Vector3 v) => (R * v.x + U * v.y + F * v.z).normalized;

            /// Two muscles -> make (to - from) point along target. Coordinate descent from a coarse
            /// grid (fresh) or from the current values (the previous frame).
            void Aim(string a, string b, Transform from, Transform to, Vector3 target, bool fresh)
            {
                int ia = I(a), ib = I(b);
                float Err() { Apply(); return Vector3.Angle(to.position - from.position, target); }
                if (fresh)
                {
                    float bestE = float.MaxValue, ba = 0f, bb = 0f;
                    for (float x = -1f; x <= 1.001f; x += 0.25f)
                        for (float y = -1f; y <= 1.001f; y += 0.25f)
                        {
                            m[ia] = x; m[ib] = y;
                            float e = Err();
                            if (e < bestE) { bestE = e; ba = x; bb = y; }
                        }
                    m[ia] = ba; m[ib] = bb;
                }
                float best = Err();
                foreach (float step in new[] { 0.12f, 0.05f, 0.02f, 0.008f, 0.003f })
                    for (int iter = 0; iter < 12; iter++)
                    {
                        bool improved = false;
                        foreach (var (i, d) in new[] { (ia, step), (ia, -step), (ib, step), (ib, -step) })
                        {
                            float old = m[i];
                            m[i] = Mathf.Clamp(old + d, -1f, 1f);
                            float e = Err();
                            if (e < best - 1e-4f) { best = e; improved = true; }
                            else m[i] = old;
                        }
                        if (!improved) break;
                    }
            }

            /// One muscle (or several sharing a value) -> make measure() equal target.
            void Match(string[] names, System.Func<float> measure, float target) =>
                Match(v => { foreach (var n in names) Set(n, v); }, Get(names[0]), 1f, measure, target);

            /// One value in [-range, range] (fed through `set`) -> make measure() equal target.
            float Match(System.Action<float> set, float start, float range, System.Func<float> measure, float target)
            {
                float Err(float v) { set(v); Apply(); return Mathf.Abs(measure() - target); }
                float cur = start, best = Err(cur);
                foreach (float step in new[] { 0.1f, 0.04f, 0.015f, 0.005f, 0.002f })
                    for (int iter = 0; iter < 25; iter++)
                    {
                        bool improved = false;
                        foreach (float d in new[] { step * range, -step * range })
                        {
                            float v = Mathf.Clamp(cur + d, -range, range), e = Err(v);
                            if (e < best - 1e-4f) { best = e; cur = v; improved = true; break; }
                        }
                        if (!improved) break;
                    }
                set(cur);
                return cur;
            }

            static float Bend(Transform a, Transform b, Transform c) => Vector3.Angle(b.position - a.position, c.position - b.position);

            public (float[] m, Vector3 pos, Quaternion rot) Solve(P p, bool fresh)
            {
                // Side lean, twist and head tilt/turn: direct muscle values (40 degrees per unit).
                const float Deg = 40f;
                Set("Spine Left-Right", p.SpineSide / Deg);
                Set("Chest Left-Right", p.SpineSide * 0.6f / Deg);
                Set("Spine Twist Left-Right", p.Twist * 0.4f / Deg);
                Set("Chest Twist Left-Right", p.Twist * 0.6f / Deg);
                Set("Neck Tilt Left-Right", p.HeadSide * 0.5f / Deg);
                Set("Head Tilt Left-Right", p.HeadSide * 0.5f / Deg);
                Set("Neck Turn Left-Right", p.HeadTurn * 0.5f / Deg);
                Set("Head Turn Left-Right", p.HeadTurn * 0.5f / Deg);
                Set("Left Shoulder Down-Up", p.LA.Shrug / 30f);
                Set("Right Shoulder Down-Up", p.RA.Shrug / 30f);
                bodyY = 1f;

                // Forward lean, measured: tilt the pelvis by Pitch, then bend the spine until the
                // whole torso (hips -> neck) leans Pitch + Spine + Chest.
                // Both at once: Unity's body rotation is the whole body's average, so bending the
                // spine also re-tilts the pelvis - two separate searches would fight each other.
                float pelvisTarget = pelvis0 + p.Pitch, torsoTarget = torso0 + p.Pitch + p.Spine + p.Chest;
                float bend = Get("Spine Front-Back");
                void SetLean(float pv, float bv)
                {
                    bodyRot = Quaternion.Euler(pv, p.Yaw, p.Roll);
                    Set("Spine Front-Back", bv); Set("Chest Front-Back", bv); Set("UpperChest Front-Back", bv * 0.7f);
                }
                float LeanErr(float pv, float bv)
                {
                    SetLean(pv, bv); Apply();
                    return Mathf.Abs(Tilt(hips, spine) - pelvisTarget) + Mathf.Abs(Tilt(hips, neck) - torsoTarget);
                }
                if (fresh)
                {
                    float bestE = float.MaxValue;
                    for (float pv = -60f; pv <= 60.1f; pv += 10f)
                        for (float bv = -1f; bv <= 1.001f; bv += 0.2f)
                        {
                            float e = LeanErr(pv, bv);
                            if (e < bestE) { bestE = e; pitch = pv; bend = bv; }
                        }
                }
                float leanBest = LeanErr(pitch, bend);
                foreach (float step in new[] { 0.1f, 0.04f, 0.015f, 0.005f })
                    for (int iter = 0; iter < 20; iter++)
                    {
                        bool improved = false;
                        foreach (var (dp, db) in new[] { (step * 60f, 0f), (-step * 60f, 0f), (0f, step), (0f, -step) })
                        {
                            float np = Mathf.Clamp(pitch + dp, -80f, 80f), nb = Mathf.Clamp(bend + db, -1f, 1f);
                            float e = LeanErr(np, nb);
                            if (e < leanBest - 1e-4f) { leanBest = e; pitch = np; bend = nb; improved = true; }
                        }
                        if (!improved) break;
                    }
                SetLean(pitch, bend);
                Trace?.AppendLine($"pitch {pitch:F1} pelvis {Tilt(hips, spine):F1}/{pelvis0 + p.Pitch:F1} torso {Tilt(hips, neck):F1}/{torso0 + p.Pitch + p.Spine + p.Chest:F1} spineM {Get("Spine Front-Back"):F2} hipsToSpine {(spine.position - hips.position).magnitude:F3} neutral {pelvis0:F1}/{torso0:F1}");

                // Legs: thigh direction, then the knee bend.
                void Leg(string side, float s, P.Leg l, Transform thigh, Transform shin, Transform foot)
                {
                    float th = l.Thigh * Mathf.Deg2Rad, o = l.Out * Mathf.Deg2Rad;
                    Aim(side + " Upper Leg Front-Back", side + " Upper Leg In-Out", thigh, shin,
                        W(new Vector3(s * Mathf.Sin(o), -Mathf.Cos(th) * Mathf.Cos(o), Mathf.Sin(th) * Mathf.Cos(o))), fresh);
                    Match(new[] { side + " Lower Leg Stretch" }, () => Bend(thigh, shin, foot), l.Knee);
                    Set(side + " Foot Up-Down", l.Toe / 40f); // free foot angle; planting may flatten it below
                }
                Leg("Left", -1f, p.L, lThigh, lShin, lFoot);
                Leg("Right", 1f, p.R, rThigh, rShin, rFoot);

                // Arms: upper-arm direction in the world, then the elbow bend.
                void Arm(string side, float s, P.Arm a, Transform arm, Transform fore, Transform hand)
                {
                    float d = a.Down * Mathf.Deg2Rad, f = a.Fwd * Mathf.Deg2Rad;
                    Aim(side + " Arm Down-Up", side + " Arm Front-Back", arm, fore,
                        W(new Vector3(s * Mathf.Cos(f) * Mathf.Cos(d), -Mathf.Sin(d), Mathf.Sin(f) * Mathf.Cos(d))), fresh);
                    Match(new[] { side + " Forearm Stretch" }, () => Bend(arm, fore, hand), a.Elbow);
                }
                Arm("Left", -1f, p.LA, lArm, lFore, lHand);
                Arm("Right", 1f, p.RA, rArm, rFore, rHand);

                // Head: tilted forward by Look from its neutral upright angle (eyes on the horizon at 0).
                Match(new[] { "Neck Nod Down-Up", "Head Nod Down-Up" }, () => Tilt(head, headTop), head0 + p.Look);

                // Feet on the ground: move the body until the lowest foot is at ground height.
                for (int k = 0; k < 3; k++)
                {
                    Apply();
                    float low = Mathf.Min(lFoot.position.y, rFoot.position.y);
                    float y0 = bodyY;
                    bodyY += 0.1f; Apply();
                    float gain = (Mathf.Min(lFoot.position.y, rFoot.position.y) - low) / 0.1f;
                    bodyY = y0;
                    if (Mathf.Abs(gain) < 1e-4f) break;
                    bodyY += (ground - low) / gain;
                }
                PlantFeet(p, p.Bob);
                return ((float[])m.Clone(), new Vector3(0f, bodyY, 0f), bodyRot);
            }

            void PlantFeet(P p, float bob)
            {
                // Planted feet: rigs aren't symmetric (the Miner's legs differ in length), so the lowest
                // foot alone would leave the other standing foot hovering. Each leg says how planted it
                // is (P.Leg.Plant, blended through a step so nothing snaps): the hips come down until
                // every planted foot can touch, then each planted knee bends so its foot sits exactly
                // on the ground, flat.
                float wL = Mathf.Clamp01(p.L.Plant), wR = Mathf.Clamp01(p.R.Plant);
                float hipW(float w) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(w * 2f));   // full by half-planted
                float Drop() => Mathf.Max(hipW(wL) * (lFoot.position.y - ground), hipW(wR) * (rFoot.position.y - ground));
                Apply();
                float drop = Drop();
                if (drop > 0f)
                {
                    float y0 = bodyY; bodyY += 0.1f; Apply();
                    float gain = (Drop() - drop) / 0.1f;
                    bodyY = y0;
                    if (gain > 1e-4f) bodyY -= drop / gain;
                }
                bodyY += bob;
                void Plant(string side, float w, Transform foot, Transform toes)
                {
                    if (w <= 0.001f) return;
                    string knee = side + " Lower Leg Stretch", ankle = side + " Foot Up-Down";
                    float freeKnee = Get(knee), freeAnkle = Get(ankle);
                    Match(new[] { knee }, () => foot.position.y, ground);
                    if (toes) Match(new[] { ankle }, () => toes.position.y, groundToes); // flat: toes down too
                    Set(knee, Mathf.Lerp(freeKnee, Get(knee), w));
                    Set(ankle, Mathf.Lerp(freeAnkle, Get(ankle), w * 0.9f));
                }
                Plant("Left", wL, lFoot, lToes);
                Plant("Right", wR, rFoot, rToes);

                // A lifted foot never goes into the ground: the hips came down for the standing
                // foot, so a swinging leg bends its knee (and lifts its toes) to clear it.
                void Clear(string side, Transform foot, Transform toes)
                {
                    Apply();
                    if (foot.position.y < ground) Match(new[] { side + " Lower Leg Stretch" }, () => Mathf.Min(foot.position.y, ground), ground);
                    Apply();
                    if (toes && toes.position.y < groundToes) Match(new[] { side + " Foot Up-Down" }, () => Mathf.Min(toes.position.y, groundToes), groundToes);
                }
                Clear("Left", lFoot, lToes);
                Clear("Right", rFoot, rToes);
                Apply();
            }
        }

        // ------------------------------------------------------------------ baking

        /// Bakes all clips into ClipFolder using `model` (an instance of the Miner with its Humanoid
        /// `avatar`; left in its rest pose afterwards). Returns state name -> clip.
        public static Dictionary<string, AnimationClip> BakeAll(GameObject model, Avatar avatar) =>
            BakeSet(model, avatar, ClipFolder, new[]
            {
                new ClipSpec("Idle", "Miner_Idle", 3.0f, true, t => Idle(t / 3.0f)),
                new ClipSpec("Walk", "Miner_Walk", 1.6f, true, t => Walk(t / 1.6f)),
                new ClipSpec("Run", "Miner_Run", 1.0f, true, t => Run(t / 1.0f)),
                new ClipSpec("Attack", "Miner_Attack", AttackLength, false, Attack),
                new ClipSpec("Hit", "Miner_Hit", HitLength, false, Hit),
            });

        /// One clip for a character: the controller state it replaces, its asset name, length,
        /// looping, and the pose at each moment (seconds).
        internal readonly struct ClipSpec
        {
            public readonly string State, Name; public readonly float Length; public readonly bool Loop; public readonly System.Func<float, P> Pose;
            public ClipSpec(string state, string name, float length, bool loop, System.Func<float, P> pose)
            { State = state; Name = name; Length = length; Loop = loop; Pose = pose; }
        }

        /// Bakes any character's clip set (see YakuAnimationAuthoring) on its own skeleton into `folder`.
        internal static Dictionary<string, AnimationClip> BakeSet(GameObject model, Avatar avatar, string folder, IEnumerable<ClipSpec> clips)
        {
            var solver = new Solver(model, avatar);
            if (!solver.ok)
            {
                Debug.LogError("[Zombie Rush] Animation authoring: the avatar is not Humanoid or is missing arm, leg or head bones.");
                solver.Dispose();
                return new Dictionary<string, AnimationClip>();
            }
            if (!AssetDatabase.IsValidFolder(folder))
            {
                string parent = "Assets";
                foreach (var part in folder.Substring("Assets/".Length).Split('/'))
                {
                    if (!AssetDatabase.IsValidFolder(parent + "/" + part)) AssetDatabase.CreateFolder(parent, part);
                    parent += "/" + part;
                }
            }
            try
            {
                var result = new Dictionary<string, AnimationClip>();
                foreach (var c in clips) result[c.State] = Bake(solver, folder, c.Name, c.Length, c.Loop, c.Pose);
                return result;
            }
            finally { solver.Dispose(); }
        }

        static AnimationClip Bake(Solver solver, string folder, string name, float length, bool loop, System.Func<float, P> pose)
        {
            int muscleCount = HumanTrait.MuscleCount;
            var curves = new AnimationCurve[muscleCount];
            for (int i = 0; i < muscleCount; i++) curves[i] = new AnimationCurve();
            var root = new AnimationCurve[7];
            for (int i = 0; i < 7; i++) root[i] = new AnimationCurve();

            int frames = Mathf.Max(2, Mathf.CeilToInt(length * Fps));
            var samples = new (float[] m, Vector3 pos, Quaternion rot)[frames + 1];
            for (int f = 0; f <= frames; f++)
            {
                float t = length * f / frames;
                samples[f] = loop && f == frames ? samples[0] : solver.Solve(pose(t), fresh: f == 0); // loops end where they start
            }

            // Light smoothing of the legs and hip height only: planting and ground clearance switch on
            // and off through a step, which leaves small kinks in the knee/ankle curves. Arms and torso
            // are left sharp so strikes keep their snap. Loops wrap around.
            var legs = new List<int>();
            for (int i = 0; i < muscleCount; i++)
            {
                string n = HumanTrait.MuscleName[i];
                if (n.Contains("Leg") || n.Contains("Foot") || n.Contains("Toes")) legs.Add(i);
            }
            int last = frames; // samples[frames] repeats samples[0] for loops
            float At(int f, System.Func<int, float> get)
            {
                if (loop) f = ((f % last) + last) % last;
                else f = Mathf.Clamp(f, 0, last);
                return get(f);
            }
            // [1,2,1]: cancels frame-to-frame zig-zag from the per-frame search, barely moves real motion.
            float Smooth3(int f, System.Func<int, float> get) => (At(f - 1, get) + 2f * At(f, get) + At(f + 1, get)) / 4f;
            var copy = samples.Select(s => ((float[])s.m.Clone(), s.pos, s.rot)).ToArray();
            for (int f = 0; f <= frames; f++)
            {
                foreach (int i in legs) samples[f].m[i] = Smooth3(f, k => copy[k].Item1[i]);
                samples[f].pos.y = Smooth3(f, k => copy[k].pos.y);
            }

            for (int f = 0; f <= frames; f++)
            {
                float t = length * f / frames;
                var s = samples[f];
                for (int i = 0; i < muscleCount; i++) curves[i].AddKey(t, s.m[i]);
                root[0].AddKey(t, s.pos.x); root[1].AddKey(t, s.pos.y); root[2].AddKey(t, s.pos.z);
                root[3].AddKey(t, s.rot.x); root[4].AddKey(t, s.rot.y); root[5].AddKey(t, s.rot.z); root[6].AddKey(t, s.rot.w);
            }

            var clip = new AnimationClip { name = name, frameRate = Fps };
            string[] rootNames = { "RootT.x", "RootT.y", "RootT.z", "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
            for (int i = 0; i < 7; i++) clip.SetCurve("", typeof(Animator), rootNames[i], root[i]);
            for (int i = 0; i < muscleCount; i++)
            {
                string n = HumanTrait.MuscleName[i];
                if (n.Contains("Thumb") || n.Contains("Index") || n.Contains("Middle") || n.Contains("Ring") || n.Contains("Little")) continue;
                clip.SetCurve("", typeof(Animator), n, curves[i]);
            }

            // In place: all body movement baked into the pose.
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            settings.loopBlendOrientation = true;
            settings.loopBlendPositionY = true;
            settings.loopBlendPositionXZ = true;
            settings.keepOriginalOrientation = true;
            settings.keepOriginalPositionY = true;
            settings.keepOriginalPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            string path = $"{folder}/{name}.anim";
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }
    }
}
