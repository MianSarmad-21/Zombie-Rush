using System.Collections.Generic;
using UnityEngine;

namespace ZombieRush.Effects
{
    /// Pooled blood burst: particle systems are reused instead of created and
    /// destroyed on every hit, which matters on mobile (shotgun = many hits/shot).
    public static class BloodEffect
    {
        const int MaxPoolSize = 24;
        const string MaterialResourcePath = "ZombieRush/BloodParticle";
        static readonly Color BloodColor = new Color(0.5f, 0.02f, 0.02f);

        static Material cachedMaterial;
        static readonly List<ParticleSystem> pool = new List<ParticleSystem>();
        static int nextIndex;

        public static void Spawn(Vector3 position)
        {
            var ps = GetFree();
            if (ps == null) return;

            ps.transform.position = position;
            ps.Clear();
            ps.Play();
        }

        static ParticleSystem GetFree()
        {
            // Round-robin from the last used slot: the next one is almost always
            // already finished, so this is O(1) in practice.
            for (int attempts = pool.Count; attempts > 0 && pool.Count > 0; attempts--)
            {
                int i = nextIndex % pool.Count;
                var candidate = pool[i];
                if (candidate == null) { pool.RemoveAt(i); continue; }
                nextIndex = i + 1;
                if (!candidate.isPlaying) return candidate;
            }

            if (pool.Count >= MaxPoolSize) return null;

            var ps = Create();
            pool.Add(ps);
            return ps;
        }

        static ParticleSystem Create()
        {
            var go = new GameObject("BloodBurst");
            Object.DontDestroyOnLoad(go);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.startColor = BloodColor;
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
            main.startLifetime = 0.7f;
            main.duration = 0.7f;
            main.gravityModifier = 2f;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = 32;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 12) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.12f;

            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = GetMaterial();
            return ps;
        }

        /// Prefers a material asset in Resources (created by Zombie Rush > Build tools)
        /// because a shader looked up by name can be stripped from builds.
        static Material GetMaterial()
        {
            if (cachedMaterial != null) return cachedMaterial;

            cachedMaterial = Resources.Load<Material>(MaterialResourcePath);
            if (cachedMaterial != null) return cachedMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            cachedMaterial = new Material(shader) { color = BloodColor };
            return cachedMaterial;
        }
    }
}
