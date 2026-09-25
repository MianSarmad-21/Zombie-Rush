using System.Collections.Generic;
using UnityEngine;

namespace ZombieRush.Effects
{
    /// Pooled blood burst: particle systems are reused instead of created and
    /// destroyed on every hit, which matters on mobile (shotgun = many hits/shot).
    public static class BloodEffect
    {
        const int MaxPoolSize = 24;
        const float BurstLifetime = 1.5f;

        static Material cachedMaterial;
        static readonly List<ParticleSystem> pool = new List<ParticleSystem>();

        public static void Spawn(Vector3 position)
        {
            var ps = GetFree();
            if (ps == null) return;

            ps.transform.position = position;
            ps.gameObject.SetActive(true);
            ps.Clear();
            ps.Play();
        }

        static ParticleSystem GetFree()
        {
            for (int i = pool.Count - 1; i >= 0; i--)
            {
                if (pool[i] == null) { pool.RemoveAt(i); continue; }
                if (!pool[i].isPlaying) return pool[i];
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
            var main = ps.main;
            main.startColor = new Color(0.5f, 0.02f, 0.02f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
            main.startLifetime = 0.7f;
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

            go.GetComponent<ParticleSystemRenderer>().material = GetMaterial();
            return ps;
        }

        static Material GetMaterial()
        {
            if (cachedMaterial != null) return cachedMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            cachedMaterial = new Material(shader) { color = new Color(0.5f, 0.02f, 0.02f) };
            return cachedMaterial;
        }
    }
}
