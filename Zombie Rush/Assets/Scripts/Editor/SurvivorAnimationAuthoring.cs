using System.Collections.Generic;
using UnityEngine;
using P = ZombieRush.EditorTools.MinerAnimationAuthoring.P;

namespace ZombieRush.EditorTools
{
    /// The Survivor's (player character's) own animations, for third-person view and for what
    /// other squad players see. Same system as the zombies (poses in world terms, solved onto
    /// the Survivor's skeleton, planted feet). The rifle is held at low ready in every
    /// locomotion clip, so the upper body stays steady while the legs move.
    ///   Locomotion: Idle, Walk, Run, StrafeLeft, StrafeRight (walking backwards = Walk reversed).
    ///   Crouch: CrouchIdle, CrouchWalk.  Air: Air.  Death: Die.
    ///   Upper body (layered): Fire, Reload, Hit.
    static class SurvivorAnimationAuthoring
    {
        public const string ClipFolder = "Assets/Models/2026-11-05_Characters/ZR_Survivor/Animations";
        public const float ReloadLength = 2.2f, FireLength = 0.22f, HitLength = 0.45f, DieLength = 1.4f;

        static float Wave(float p, float o = 0f) => MinerAnimationAuthoring.Wave(p, o);
        static float Cos(float p, float o = 0f) => MinerAnimationAuthoring.Cos(p, o);
        static float Bump(float p, float c, float w) => MinerAnimationAuthoring.Bump(p, c, w);
        static float Planted(float p, float down, float up) => MinerAnimationAuthoring.Planted(p, down, up);

        public static Dictionary<string, AnimationClip> BakeAll(GameObject model, Avatar avatar) =>
            MinerAnimationAuthoring.BakeSet(model, avatar, ClipFolder, new[]
            {
                new MinerAnimationAuthoring.ClipSpec("Idle", "Survivor_Idle", 2.6f, true, t => Idle(t / 2.6f)),
                new MinerAnimationAuthoring.ClipSpec("Walk", "Survivor_Walk", 1.0f, true, t => Walk(t / 1.0f)),
                new MinerAnimationAuthoring.ClipSpec("Run", "Survivor_Run", 0.68f, true, t => Run(t / 0.68f)),
                new MinerAnimationAuthoring.ClipSpec("StrafeLeft", "Survivor_StrafeLeft", 0.9f, true, t => Strafe(t / 0.9f, -1f)),
                new MinerAnimationAuthoring.ClipSpec("StrafeRight", "Survivor_StrafeRight", 0.9f, true, t => Strafe(t / 0.9f, 1f)),
                new MinerAnimationAuthoring.ClipSpec("CrouchIdle", "Survivor_CrouchIdle", 2.4f, true, t => CrouchIdle(t / 2.4f)),
                new MinerAnimationAuthoring.ClipSpec("CrouchWalk", "Survivor_CrouchWalk", 1.1f, true, t => CrouchWalk(t / 1.1f)),
                new MinerAnimationAuthoring.ClipSpec("Air", "Survivor_Air", 0.8f, true, t => Air(t / 0.8f)),
                new MinerAnimationAuthoring.ClipSpec("Die", "Survivor_Die", DieLength, false, Die),
                new MinerAnimationAuthoring.ClipSpec("Fire", "Survivor_Fire", FireLength, false, Fire),
                new MinerAnimationAuthoring.ClipSpec("Reload", "Survivor_Reload", ReloadLength, false, Reload),
                new MinerAnimationAuthoring.ClipSpec("Hit", "Survivor_Hit", HitLength, false, Hit),
            });

        /// Rifle at low ready: trigger hand by the chest, support hand forward under the barrel.
        static void Rifle(P p, float lift = 0f)
        {
            p.RA.Down = 62f - lift; p.RA.Fwd = 30f; p.RA.Elbow = 100f; p.RA.Shrug = 2f;
            p.LA.Down = 40f - lift; p.LA.Fwd = 72f; p.LA.Elbow = 55f; p.LA.Shrug = 2f;
        }

        /// Alert, balanced, weight a little forward, feet staggered.
        static P Stance()
        {
            var p = new P { Pitch = 4f, Spine = 2f, Chest = 3f, Look = 0f };
            p.L.Thigh = 5f; p.L.Knee = 10f; p.L.Out = 9f;
            p.R.Thigh = -5f; p.R.Knee = 8f; p.R.Out = 9f;
            Rifle(p);
            return p;
        }

        /// Breathing, a slow scan left and right.
        static P Idle(float p)
        {
            var s = Stance();
            s.Chest += 1.5f * Wave(p);
            s.RA.Down += 1.5f * Wave(p, 0.1f); s.LA.Down += 1.5f * Wave(p, 0.1f);
            s.HeadTurn = 12f * Bump(p, 0.3f, 0.16f) - 10f * Bump(p, 0.75f, 0.16f);
            s.Look = 1.5f * Wave(p, 0.3f);
            s.Roll = 1f * Wave(p, 0.25f);
            return s;
        }

        static P Walk(float p)
        {
            var s = Stance();
            s.L.Plant = Planted(p, 0.95f, 0.55f); s.R.Plant = Planted(p, 0.45f, 0.05f);
            s.Pitch = 6f;
            s.L.Thigh = 24f * Cos(p); s.L.Knee = 8f + 40f * Bump(p, 0.72f, 0.22f);
            s.R.Thigh = 24f * Cos(p, -0.5f); s.R.Knee = 8f + 40f * Bump(p, 0.22f, 0.22f);
            s.Roll = 2f * Wave(p);
            s.Twist = -3f * Wave(p);
            s.RA.Down += 2f * Mathf.Sin(p * Mathf.PI * 4f); s.LA.Down += 2f * Mathf.Sin(p * Mathf.PI * 4f);
            return s;
        }

        /// Combat jog: leaning in, rifle tucked across the chest.
        static P Run(float p)
        {
            var s = Stance();
            s.L.Plant = Planted(p, 0.97f, 0.4f); s.R.Plant = Planted(p, 0.47f, 0.9f);
            s.Pitch = 14f; s.Spine = 4f; s.Chest = 2f; s.Look = -6f;
            s.L.Thigh = 36f * Cos(p) + 6f; s.L.Knee = 22f + 72f * Bump(p, 0.7f, 0.22f);
            s.R.Thigh = 36f * Cos(p, -0.5f) + 6f; s.R.Knee = 22f + 72f * Bump(p, 0.2f, 0.22f);
            s.Yaw = 5f * Wave(p); s.Twist = -6f * Wave(p); s.Roll = 2f * Wave(p, 0.25f);
            s.RA.Down = 55f + 3f * Mathf.Sin(p * Mathf.PI * 4f); s.RA.Fwd = 40f; s.RA.Elbow = 105f;
            s.LA.Down = 35f + 3f * Mathf.Sin(p * Mathf.PI * 4f); s.LA.Fwd = 70f; s.LA.Elbow = 70f;
            return s;
        }

        /// Side-step: the leading leg steps out, the other closes in, rifle stays on target.
        /// dir +1 = to the right, -1 = to the left.
        static P Strafe(float p, float dir)
        {
            var s = Stance();
            s.L.Thigh = 4f; s.R.Thigh = 4f;
            float lead = 15f * Cos(p);
            if (dir > 0f)
            {
                s.R.Out = 10f + lead; s.L.Out = 10f - lead;
                s.R.Plant = Planted(p, 0.0f, 0.5f); s.L.Plant = Planted(p, 0.5f, 1.0f);
                s.R.Knee = 10f + 30f * Bump(p, 0.75f, 0.2f); s.L.Knee = 10f + 30f * Bump(p, 0.25f, 0.2f);
            }
            else
            {
                s.L.Out = 10f + lead; s.R.Out = 10f - lead;
                s.L.Plant = Planted(p, 0.0f, 0.5f); s.R.Plant = Planted(p, 0.5f, 1.0f);
                s.L.Knee = 10f + 30f * Bump(p, 0.75f, 0.2f); s.R.Knee = 10f + 30f * Bump(p, 0.25f, 0.2f);
            }
            s.Roll = 3f * dir * Wave(p);
            return s;
        }

        static P CrouchStance()
        {
            var s = Stance();
            s.Pitch = 16f; s.Spine = 6f; s.Look = -4f;
            s.L.Thigh = 55f; s.L.Knee = 100f; s.L.Out = 12f;
            s.R.Thigh = 20f; s.R.Knee = 105f; s.R.Out = 12f; s.R.Toe = 25f;
            Rifle(s, 8f);
            return s;
        }

        static P CrouchIdle(float p)
        {
            var s = CrouchStance();
            s.Chest += 1.5f * Wave(p);
            s.HeadTurn = 10f * Bump(p, 0.35f, 0.18f) - 8f * Bump(p, 0.8f, 0.18f);
            return s;
        }

        static P CrouchWalk(float p)
        {
            var s = CrouchStance();
            s.L.Plant = Planted(p, 0.95f, 0.55f); s.R.Plant = Planted(p, 0.45f, 0.05f);
            s.L.Thigh = 45f + 18f * Cos(p); s.L.Knee = 95f + 20f * Bump(p, 0.72f, 0.22f);
            s.R.Thigh = 45f + 18f * Cos(p, -0.5f); s.R.Knee = 95f + 20f * Bump(p, 0.22f, 0.22f);
            s.R.Toe = 0f;
            s.Roll = 2f * Wave(p);
            return s;
        }

        /// Jumping or falling: knees drawn up, rifle held in.
        static P Air(float p)
        {
            var s = Stance();
            s.L.Plant = 0f; s.R.Plant = 0f;
            s.L.Thigh = 35f + 3f * Wave(p); s.L.Knee = 65f;
            s.R.Thigh = -5f + 3f * Wave(p, 0.5f); s.R.Knee = 45f;
            s.Pitch = 6f;
            Rifle(s, 6f);
            return s;
        }

        /// Shot, staggers, sinks to the knees and slumps forward.
        static P Die(float t)
        {
            var a = Stance();
            var hit = Stance(); hit.Pitch = -8f; hit.Chest = -12f; hit.Look = -20f; hit.HeadSide = 10f;
            hit.RA.Down = 75f; hit.RA.Fwd = 15f; hit.RA.Elbow = 40f; hit.LA.Down = 70f; hit.LA.Fwd = 20f; hit.LA.Elbow = 30f;
            var knees = hit.Copy(); knees.Pitch = 10f; knees.Chest = 10f; knees.Look = 20f;
            knees.L.Thigh = 0f; knees.L.Knee = 100f; knees.R.Thigh = 5f; knees.R.Knee = 105f;
            knees.L.Plant = 0f; knees.R.Plant = 0f;
            var slump = knees.Copy(); slump.Pitch = 65f; slump.Spine = 15f; slump.Chest = 10f; slump.Look = 30f; slump.HeadSide = 20f;
            slump.RA.Down = 85f; slump.RA.Fwd = 40f; slump.RA.Elbow = 20f; slump.LA.Down = 85f; slump.LA.Fwd = 45f; slump.LA.Elbow = 25f;
            var keys = new[] { a, hit, knees, slump };
            var times = new[] { 0f, 0.15f, 0.7f, DieLength };
            int i = 0;
            while (i < times.Length - 2 && t > times[i + 1]) i++;
            return P.Lerp(keys[i], keys[i + 1], Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(times[i], times[i + 1], t)));
        }

        /// Recoil kick (upper body layer).
        static P Fire(float t)
        {
            var a = Stance();
            var b = Stance(); b.Chest = -2f; b.Look = -3f; b.RA.Elbow = 108f; b.LA.Elbow = 62f; b.RA.Shrug = 6f; b.LA.Shrug = 6f;
            float u = t < 0.04f ? t / 0.04f : 1f - Mathf.SmoothStep(0f, 1f, (t - 0.04f) / (FireLength - 0.04f));
            return P.Lerp(a, b, u);
        }

        /// Tilt the rifle, drop the magazine, fetch a fresh one from the vest, slap it in, back on target.
        static P Reload(float t)
        {
            var ready = Stance();
            var tilt = Stance(); tilt.RA.Down = 50f; tilt.RA.Fwd = 45f; tilt.RA.Elbow = 115f; tilt.Look = 12f; tilt.Twist = 8f;
            tilt.LA.Down = 55f; tilt.LA.Fwd = 55f; tilt.LA.Elbow = 95f;
            var pouch = tilt.Copy(); pouch.LA.Down = 80f; pouch.LA.Fwd = 35f; pouch.LA.Elbow = 70f; pouch.Look = 18f;
            var insert = tilt.Copy(); insert.LA.Down = 58f; insert.LA.Fwd = 58f; insert.LA.Elbow = 100f;
            var slap = insert.Copy(); slap.LA.Down = 50f; slap.LA.Elbow = 92f; slap.Chest = 2f;
            var keys = new[] { ready, tilt, pouch, pouch, insert, slap, tilt, ready };
            var times = new[] { 0f, 0.3f, 0.65f, 0.85f, 1.25f, 1.45f, 1.75f, ReloadLength };
            int i = 0;
            while (i < times.Length - 2 && t > times[i + 1]) i++;
            return P.Lerp(keys[i], keys[i + 1], Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(times[i], times[i + 1], t)));
        }

        /// Flinch (upper body layer).
        static P Hit(float t)
        {
            var a = Stance();
            var b = Stance(); b.Chest = -10f; b.Look = -15f; b.HeadSide = 10f; b.Twist = -8f;
            b.RA.Down = 70f; b.LA.Down = 52f;
            float u = t < 0.07f ? Mathf.SmoothStep(0f, 1f, t / 0.07f) : 1f - Mathf.SmoothStep(0f, 1f, (t - 0.07f) / (HitLength - 0.07f));
            return P.Lerp(a, b, u);
        }
    }
}
