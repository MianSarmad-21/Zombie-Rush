using System.Collections.Generic;
using UnityEngine;

namespace ZombieRush.Effects
{
    /// Pooled ground shockwave: an expanding glowing ring plus a puff of particles flung
    /// outwards. Used for the Screamer's shriek and the Spitter's goo splat.
    public static class ShockwaveEffect
    {
        const int MaxPool = 8;
        static readonly List<GameObject> pool = new List<GameObject>();

        public static readonly Color ShriekColor = new Color(0.55f, 0.95f, 1f, 0.9f);
        public static readonly Color GooColor = new Color(0.45f, 1f, 0.15f, 0.9f);

        /// radius: how far the ring spreads. seconds: how long it takes.
        public static void Spawn(Vector3 position, float radius, Color color, float seconds = 0.6f, bool light = true)
        {
            var root = GetFree();
            if (root == null) return;
            root.transform.position = position + Vector3.up * 0.1f;
            root.SetActive(true);

            var ring = root.transform.GetChild(0).GetComponent<ParticleSystem>();
            var main = ring.main;
            main.startSize = radius * 2f;
            main.startLifetime = seconds;
            ring.GetComponent<ParticleSystemRenderer>().sharedMaterial = FxMaterials.Particle(color, FxMaterials.Ring, "ring");

            var puff = root.transform.GetChild(1).GetComponent<ParticleSystem>();
            var pm = puff.main;
            pm.startSpeed = new ParticleSystem.MinMaxCurve(radius * 0.6f, radius * 1.2f);
            puff.GetComponent<ParticleSystemRenderer>().sharedMaterial = FxMaterials.Particle(new Color(color.r, color.g, color.b, 0.5f));

            ring.Clear(); ring.Play();
            puff.Clear(); puff.Play();
            var runner = root.GetComponent<FlashRunner>();
            var l = root.GetComponentInChildren<Light>(true);
            l.color = color;
            if (light) runner.Flash(l, radius * 0.5f, 4f, seconds * 0.6f, seconds + 0.6f);
            else runner.Hold(seconds + 0.6f);
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
            var root = new GameObject("Shockwave");
            Object.DontDestroyOnLoad(root);
            root.AddComponent<FlashRunner>();

            // 1. One flat ring particle that grows from nothing to full size.
            var ringGo = new GameObject("Ring");
            ringGo.transform.SetParent(root.transform, false);
            var ring = ringGo.AddComponent<ParticleSystem>();
            ring.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ring.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.6f;
            main.startSpeed = 0f;
            main.maxParticles = 2;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ring.emission;
            em.rateOverTime = 0;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            var shape = ring.shape;
            shape.enabled = false;
            var sol = ring.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.05f, 0f, 3f), new Keyframe(1f, 1f, 0.3f, 0f)));
            var col = ring.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var rr = ringGo.GetComponent<ParticleSystemRenderer>();
            rr.renderMode = ParticleSystemRenderMode.HorizontalBillboard;

            // 2. A puff flung outwards along the ground.
            var puffGo = new GameObject("Puff");
            puffGo.transform.SetParent(root.transform, false);
            var puff = puffGo.AddComponent<ParticleSystem>();
            puff.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var pm = puff.main;
            pm.loop = false;
            pm.playOnAwake = false;
            pm.duration = 0.6f;
            pm.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            pm.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            pm.maxParticles = 40;
            pm.simulationSpace = ParticleSystemSimulationSpace.World;
            var pe = puff.emission;
            pe.rateOverTime = 0;
            pe.SetBursts(new[] { new ParticleSystem.Burst(0f, 28) });
            var ps = puff.shape;
            ps.shapeType = ParticleSystemShapeType.Circle;
            ps.radius = 0.3f;
            ps.rotation = new Vector3(90f, 0f, 0f);
            var pc = puff.colorOverLifetime;
            pc.enabled = true;
            pc.color = g;
            var plim = puff.limitVelocityOverLifetime;
            plim.enabled = true;
            plim.dampen = 0.2f;
            plim.limit = 1f;

            var lightGo = new GameObject("Flash");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = Vector3.up * 1.2f;
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.shadows = LightShadows.None;
            light.enabled = false;
            return root;
        }
    }
}
