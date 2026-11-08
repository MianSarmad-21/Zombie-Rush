using System.Collections.Generic;
using UnityEngine;

namespace ZombieRush.Effects
{
    /// Pooled explosion: fireball, smoke, flying sparks and a short flash of light.
    public static class ExplosionEffect
    {
        const int MaxPool = 6;
        static readonly List<GameObject> pool = new List<GameObject>();
        static readonly Color Fire = new Color(1f, 0.55f, 0.15f, 1f);
        static readonly Color Smoke = new Color(0.16f, 0.15f, 0.14f, 0.85f);
        static readonly Color Spark = new Color(1f, 0.85f, 0.4f, 1f);

        public static void Spawn(Vector3 position, float radius)
        {
            var root = GetFree();
            if (root == null) return;
            root.transform.position = position;
            root.transform.localScale = Vector3.one * Mathf.Max(0.5f, radius / 5f);
            root.SetActive(true);
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>())
            {
                ps.Clear();
                ps.Play();
            }
            var light = root.GetComponentInChildren<Light>();
            if (light != null) root.GetComponent<FlashRunner>().Flash(light, radius);
        }

        static GameObject GetFree()
        {
            foreach (var g in pool)
                if (g != null && !g.GetComponent<FlashRunner>().Busy) return g;
            if (pool.Count >= MaxPool) return null;
            var root = Create();
            pool.Add(root);
            return root;
        }

        static GameObject Create()
        {
            var root = new GameObject("Explosion");
            Object.DontDestroyOnLoad(root);
            root.AddComponent<FlashRunner>();

            Burst(root, "Fireball", Fire, 24, new Vector2(0.9f, 2.0f), new Vector2(2f, 6f), 0.55f, -0.2f, true);
            Burst(root, "Smoke", Smoke, 18, new Vector2(1.4f, 2.8f), new Vector2(0.8f, 2.6f), 1.8f, -0.05f, false);
            var sparks = Burst(root, "Sparks", Spark, 30, new Vector2(0.06f, 0.14f), new Vector2(6f, 14f), 0.9f, 1.4f, false);
            var r = sparks.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.05f;
            r.lengthScale = 2f;

            var lightGo = new GameObject("Flash");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = Vector3.up * 0.8f;
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.6f, 0.25f);
            light.shadows = LightShadows.None;
            light.enabled = false;
            return root;
        }

        static ParticleSystem Burst(GameObject root, string name, Color color, int count, Vector2 size, Vector2 speed,
                                    float life, float gravity, bool fadeToDark)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = life;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = gravity;
            main.maxParticles = count + 4;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.4f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            var end = fadeToDark ? new Color(0.25f, 0.08f, 0.02f) : color;
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(end, 0.6f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.4f));

            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.15f;
            limit.limit = 1.5f;

            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = FxMaterials.Particle(color);
            return ps;
        }
    }
}
