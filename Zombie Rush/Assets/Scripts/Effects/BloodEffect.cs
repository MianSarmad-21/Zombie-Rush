using System.Collections.Generic;
using UnityEngine;
using ZombieRush.Data;

namespace ZombieRush.Effects
{
    /// Blood when a zombie is hit: droplets sprayed out of the wound along the shot (stretched by their
    /// speed, falling under gravity), a short puff of blood mist, and now and then a splat on the ground
    /// under it. A kill leaves a big splat. Particle systems are pooled (a shotgun is many hits per shot).
    /// Tuning: Resources/ZombieRush/Fx/EffectsConfig.
    public static class BloodEffect
    {
        const int MaxPoolSize = 24;

        class Burst { public GameObject go; public ParticleSystem drops, mist; }
        static readonly List<Burst> pool = new List<Burst>();
        static int nextIndex;

        /// A hit at `position`; `direction` is the way the shot travelled (zero = not known).
        public static void Spawn(Vector3 position, Vector3 direction = default)
        {
            var cfg = EffectsConfig.Current;
            var b = GetFree();
            if (b != null)
            {
                Vector3 dir = direction.sqrMagnitude > 1e-4f ? direction.normalized : Random.onUnitSphere;
                // mostly out of the far side (exit wound), sometimes back towards the shooter
                b.go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(Random.value < 0.75f ? dir : -dir));
                var e = b.drops.emission;
                e.SetBurst(0, new ParticleSystem.Burst(0f, (short)cfg.hitDrops.x, (short)cfg.hitDrops.y));
                b.drops.Clear(true);
                b.drops.Play(true);
            }
            if (Random.value < cfg.hitSplatChance)
                GroundDecals.Place(position + Random.insideUnitSphere * 0.4f, Random.Range(cfg.hitSplatSize.x, cfg.hitSplatSize.y),
                                   FxMaterials.Fx("Fx_Splat", Color.white, true), cfg.bloodColor, cfg.splatLife, cfg.splatFade);
        }

        /// A kill: a spray plus a big pool of blood on the ground.
        public static void SpawnDeath(Vector3 position, Vector3 direction = default)
        {
            var cfg = EffectsConfig.Current;
            Spawn(position, direction);
            GroundDecals.Place(position, cfg.deathSplatSize * Random.Range(0.85f, 1.15f), FxMaterials.Fx("Fx_Splat", Color.white, true),
                               cfg.bloodColor, cfg.splatLife, cfg.splatFade);
        }

        static Burst GetFree()
        {
            for (int attempts = pool.Count; attempts > 0 && pool.Count > 0; attempts--)
            {
                int i = nextIndex % pool.Count;
                var c = pool[i];
                if (c.go == null) { pool.RemoveAt(i); continue; }
                nextIndex = i + 1;
                if (!c.drops.IsAlive(true)) return c;
            }
            if (pool.Count >= MaxPoolSize) return null;
            var b = Create();
            pool.Add(b);
            return b;
        }

        static Burst Create()
        {
            var cfg = EffectsConfig.Current;
            var go = new GameObject("BloodBurst");
            Object.DontDestroyOnLoad(go);

            // droplets: thrown in a cone, stretched along their speed, falling
            var drops = go.AddComponent<ParticleSystem>();
            drops.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = drops.main;
            m.startColor = cfg.bloodColor;
            m.startSpeed = new ParticleSystem.MinMaxCurve(cfg.dropSpeed.x, cfg.dropSpeed.y);
            m.startSize = new ParticleSystem.MinMaxCurve(cfg.dropSize.x, cfg.dropSize.y);
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
            m.duration = 0.9f; m.loop = false; m.playOnAwake = false;
            m.gravityModifier = 1.6f; m.maxParticles = 40;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            var e = drops.emission; e.rateOverTime = 0; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 12) });
            var sh = drops.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = cfg.sprayCone; sh.radius = 0.04f;
            var sz = drops.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.6f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.05f; r.lengthScale = 1.6f;
            r.sharedMaterial = FxMaterials.Fx("Fx_Drop", Color.white, true);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // mist: a few soft puffs that swell and fade quickly
            var mg = new GameObject("Mist");
            mg.transform.SetParent(go.transform, false);
            var mist = mg.AddComponent<ParticleSystem>();
            mist.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var mm = mist.main;
            var mc = cfg.bloodColor; mc.a = cfg.mistAlpha;
            mm.startColor = mc;
            mm.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
            mm.startSize = new ParticleSystem.MinMaxCurve(cfg.mistSize * 0.6f, cfg.mistSize);
            mm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            mm.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            mm.duration = 0.5f; mm.loop = false; mm.playOnAwake = false; mm.maxParticles = 8;
            mm.simulationSpace = ParticleSystemSimulationSpace.World;
            var me = mist.emission; me.rateOverTime = 0; me.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)cfg.mistPuffs) });
            var ms = mist.shape; ms.shapeType = ParticleSystemShapeType.Cone; ms.angle = cfg.sprayCone; ms.radius = 0.05f;
            var msz = mist.sizeOverLifetime; msz.enabled = true; msz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));
            var mcol = mist.colorOverLifetime; mcol.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            mcol.color = g;
            var mr = mg.GetComponent<ParticleSystemRenderer>();
            mr.sharedMaterial = FxMaterials.Fx("Fx_Smoke", Color.white, true);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            return new Burst { go = go, drops = drops, mist = mist };
        }
    }
}
