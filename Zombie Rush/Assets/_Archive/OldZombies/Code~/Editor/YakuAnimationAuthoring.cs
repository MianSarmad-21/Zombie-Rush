using System.Collections.Generic;
using UnityEngine;
using P = ZombieRush.EditorTools.MinerAnimationAuthoring.P;

namespace ZombieRush.EditorTools
{
    /// The Yaku's own animations: a fast, aggressive yakuza runner - the opposite of the Miner's
    /// heavy limp. Same system as MinerAnimationAuthoring (poses in world terms, solved onto the
    /// Yaku's own skeleton, feet planted): twitchy idle, drunken stagger, hungry sprint with both
    /// arms clawing ahead, a lunge-grab-and-bite attack, a head-snapping flinch. The death stays
    /// the Mixamo "Zombie Dying" clip.
    static class YakuAnimationAuthoring
    {
        public const string ClipFolder = "Assets/Models/2026-09-25_Yaku/Authored";
        public const float AttackLength = 1.2f, AttackBite = 0.65f;
        const float HitLength = 0.6f;

        static float Wave(float p, float o = 0f) => MinerAnimationAuthoring.Wave(p, o);
        static float Cos(float p, float o = 0f) => MinerAnimationAuthoring.Cos(p, o);
        static float Bump(float p, float c, float w) => MinerAnimationAuthoring.Bump(p, c, w);
        static float Planted(float p, float down, float up) => MinerAnimationAuthoring.Planted(p, down, up);

        public static Dictionary<string, AnimationClip> BakeAll(GameObject model, Avatar avatar) =>
            MinerAnimationAuthoring.BakeSet(model, avatar, ClipFolder, new[]
            {
                new MinerAnimationAuthoring.ClipSpec("Idle", "Yaku_Idle", 2.4f, true, t => Idle(t / 2.4f)),
                new MinerAnimationAuthoring.ClipSpec("Walk", "Yaku_Walk", 1.0f, true, t => Walk(t / 1.0f)),
                new MinerAnimationAuthoring.ClipSpec("Run", "Yaku_Run", 0.7f, true, t => Run(t / 0.7f)),
                new MinerAnimationAuthoring.ClipSpec("Attack", "Yaku_Attack", AttackLength, false, Attack),
                new MinerAnimationAuthoring.ClipSpec("Hit", "Yaku_Hit", HitLength, false, Hit),
            });

        /// Tense and wiry: shoulders up, slight crouch, arms hanging loose.
        static P Stance()
        {
            var p = new P { Pitch = 8f, Spine = 8f, Chest = 10f, Look = 5f };
            p.L.Thigh = 6f; p.L.Knee = 14f; p.L.Out = 9f;
            p.R.Thigh = -3f; p.R.Knee = 12f; p.R.Out = 9f;
            p.LA.Down = 80f; p.LA.Fwd = 22f; p.LA.Elbow = 25f; p.LA.Shrug = 10f;
            p.RA.Down = 80f; p.RA.Fwd = 18f; p.RA.Elbow = 22f; p.RA.Shrug = 10f;
            return p;
        }

        /// Restless: breathing, swaying, sudden jerks of the head and shoulders.
        static P Idle(float p)
        {
            var s = Stance();
            s.Chest += -3f * Wave(p);
            s.Roll = 3f * Wave(p, 0.25f);
            s.SpineSide = 3f * Wave(p, 0.25f);
            s.HeadTurn = 18f * Bump(p, 0.3f, 0.07f) - 15f * Bump(p, 0.72f, 0.07f);   // snaps left, then right
            s.HeadSide = 10f * Bump(p, 0.31f, 0.08f) + 5f * Wave(p, 0.1f);
            s.Look += 4f * Wave(p, 0.4f);
            s.LA.Shrug += 12f * Bump(p, 0.52f, 0.06f);                               // shoulder twitch
            s.RA.Fwd += 8f * Wave(p, 0.2f); s.LA.Fwd += 6f * Wave(p, 0.6f);
            return s;
        }

        /// Drunken stagger: rolling side to side, arms swinging dead, head lolling.
        static P Walk(float p)
        {
            var s = Stance();
            s.L.Plant = Planted(p, 0.95f, 0.55f); s.R.Plant = Planted(p, 0.45f, 0.05f);
            s.Pitch = 10f; s.Spine = 8f; s.Chest = 8f;
            s.L.Thigh = 26f * Cos(p); s.L.Knee = 12f + 45f * Bump(p, 0.72f, 0.22f);
            s.R.Thigh = 22f * Cos(p, -0.5f); s.R.Knee = 12f + 42f * Bump(p, 0.22f, 0.22f);
            s.Roll = 6f * Wave(p);
            s.Yaw = 6f * Wave(p);
            s.SpineSide = 7f * Wave(p, 0.25f);
            s.Twist = -8f * Wave(p);
            s.LA.Fwd = 20f + 18f * Wave(p, 0.6f); s.LA.Elbow = 25f + 8f * Wave(p, 0.7f);
            s.RA.Fwd = 20f + 18f * Wave(p, 0.1f); s.RA.Elbow = 22f + 8f * Wave(p, 0.2f);
            s.Look = 10f + 5f * Mathf.Sin(p * Mathf.PI * 4f);
            s.HeadSide = 12f * Wave(p, 0.15f);
            return s;
        }

        /// Hungry sprint: bent forward, both arms clawing out at the player, long strides.
        static P Run(float p)
        {
            var s = Stance();
            s.L.Plant = Planted(p, 0.97f, 0.4f); s.R.Plant = Planted(p, 0.47f, 0.9f);
            s.Pitch = 20f; s.Spine = 12f; s.Chest = 10f; s.Look = -2f;
            s.L.Thigh = 34f * Cos(p) + 6f; s.L.Knee = 25f + 70f * Bump(p, 0.7f, 0.22f);
            s.R.Thigh = 34f * Cos(p, -0.5f) + 6f; s.R.Knee = 25f + 70f * Bump(p, 0.2f, 0.22f);
            s.Yaw = 10f * Wave(p); s.Twist = -10f * Wave(p); s.Roll = 4f * Wave(p, 0.25f);
            s.LA.Down = 10f + 8f * Mathf.Sin(p * Mathf.PI * 4f); s.LA.Fwd = 80f + 8f * Wave(p); s.LA.Elbow = 25f + 15f * Wave(p, 0.1f); s.LA.Shrug = 12f;
            s.RA.Down = 10f + 8f * Mathf.Sin(p * Mathf.PI * 4f + 1f); s.RA.Fwd = 80f + 8f * Wave(p, 0.5f); s.RA.Elbow = 25f + 15f * Wave(p, 0.6f); s.RA.Shrug = 12f;
            s.HeadSide = 3f * Wave(p, 0.3f);
            return s;
        }

        /// Lunge, grab with both hands, pull in and bite, shake, recover.
        static P Attack(float t)
        {
            var start = Stance();
            start.L.Thigh = 10f; start.R.Thigh = -6f;

            var crouch = start.Copy();                       // gathers itself, claws up
            crouch.L.Knee = 35f; crouch.R.Knee = 30f; crouch.Pitch = 15f; crouch.Spine = 10f; crouch.Look = -10f;
            crouch.LA.Down = 30f; crouch.LA.Fwd = 40f; crouch.LA.Elbow = 70f; crouch.LA.Shrug = 15f;
            crouch.RA.Down = 30f; crouch.RA.Fwd = 40f; crouch.RA.Elbow = 70f; crouch.RA.Shrug = 15f;

            var lunge = start.Copy();                        // throws itself forward, arms out to grab
            lunge.L.Thigh = 35f; lunge.L.Knee = 30f; lunge.R.Thigh = -20f; lunge.R.Knee = 10f;
            lunge.Pitch = 28f; lunge.Spine = 14f; lunge.Chest = 12f; lunge.Look = -5f;
            lunge.LA.Down = 5f; lunge.LA.Fwd = 85f; lunge.LA.Elbow = 10f;
            lunge.RA.Down = 5f; lunge.RA.Fwd = 85f; lunge.RA.Elbow = 10f;

            var bite = lunge.Copy();                         // pulls the victim in, head down into the bite
            bite.LA.Down = 30f; bite.LA.Fwd = 70f; bite.LA.Elbow = 85f;
            bite.RA.Down = 30f; bite.RA.Fwd = 70f; bite.RA.Elbow = 85f;
            bite.Chest = 20f; bite.Look = 28f;

            var shake1 = bite.Copy(); shake1.HeadTurn = 14f; shake1.HeadSide = 8f;   // tearing
            var shake2 = bite.Copy(); shake2.HeadTurn = -12f; shake2.HeadSide = -6f;

            var keys = new[] { start, crouch, lunge, bite, shake1, shake2, start };
            var times = new[] { 0f, 0.3f, 0.5f, AttackBite, 0.78f, 0.9f, AttackLength };
            int i = 0;
            while (i < times.Length - 2 && t > times[i + 1]) i++;
            float u = Mathf.InverseLerp(times[i], times[i + 1], t);
            u = i == 1 ? u * u : Mathf.SmoothStep(0f, 1f, u); // the lunge accelerates
            return P.Lerp(keys[i], keys[i + 1], u);
        }

        /// The head snaps back, arms jerk out, then it recovers.
        static P Hit(float t)
        {
            var a = Stance();
            var b = Stance();
            b.Pitch = -6f; b.Spine = -8f; b.Chest = -15f; b.Look = -35f; b.HeadSide = 15f;
            b.LA.Down = 50f; b.LA.Fwd = 0f; b.LA.Elbow = 40f; b.RA.Down = 50f; b.RA.Fwd = 5f; b.RA.Elbow = 40f;
            b.L.Knee = 22f; b.R.Knee = 22f; b.Twist = -10f;
            float u = t < 0.09f ? Mathf.SmoothStep(0f, 1f, t / 0.09f) : 1f - Mathf.SmoothStep(0f, 1f, (t - 0.09f) / (HitLength - 0.09f));
            return P.Lerp(a, b, u);
        }
    }
}
