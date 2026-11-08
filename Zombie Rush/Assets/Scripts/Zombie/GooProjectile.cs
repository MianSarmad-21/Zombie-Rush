using System.Collections.Generic;
using UnityEngine;
using ZombieRush.Audio;
using ZombieRush.Data;
using ZombieRush.Effects;
using ZombieRush.Player;

namespace ZombieRush.Zombie
{
    /// A lobbed blob of Spitter goo. Flies on a ballistic arc; on a player it hurts and slows,
    /// and wherever it lands it leaves a sticky puddle. Pooled.
    public class GooProjectile : MonoBehaviour
    {
        static readonly List<GooProjectile> pool = new List<GooProjectile>();
        static readonly RaycastHit[] hits = new RaycastHit[8];
        const float MaxLife = 4f;

        SpitterConfig cfg;
        float damageScale;
        Vector3 velocity;
        float life;
        ParticleSystem trail;
        Transform blob;

        /// Throws goo from `from` so it lands on `target` (a point) after the flight time
        /// the config's speed gives.
        public static void Launch(Vector3 from, Vector3 target, SpitterConfig cfg, float damageScale)
        {
            var goo = Get();
            float dist = Vector3.Distance(from, target);
            float t = Mathf.Clamp(dist / Mathf.Max(1f, cfg.projectileSpeed), 0.35f, 1.6f);
            var v = (target - from) / t + Vector3.up * (0.5f * cfg.projectileGravity * t);
            goo.Begin(from, v, cfg, damageScale);
        }

        static GooProjectile Get()
        {
            foreach (var g in pool)
                if (g != null && !g.gameObject.activeSelf) return g;
            var go = new GameObject("GooProjectile");
            DontDestroyOnLoad(go);
            var p = go.AddComponent<GooProjectile>();
            p.Build();
            pool.Add(p);
            return p;
        }

        void Build()
        {
            var blobGo = new GameObject("Blob");
            blobGo.transform.SetParent(transform, false);
            blobGo.AddComponent<MeshFilter>().sharedMesh = FxMaterials.Primitive(PrimitiveType.Sphere);
            var mr = blobGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = FxMaterials.Lit("GooBlob", new Color(0.4f, 0.9f, 0.1f), new Color(0.6f, 2.2f, 0.2f));
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            blob = blobGo.transform;

            var trailGo = new GameObject("Drips");
            trailGo.transform.SetParent(transform, false);
            trail = trailGo.AddComponent<ParticleSystem>();
            trail.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = trail.main;
            main.startLifetime = 0.45f;
            main.startSpeed = 0.2f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            main.gravityModifier = 1.2f;
            main.maxParticles = 60;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = trail.emission;
            em.rateOverTime = 0f;
            em.rateOverDistance = 9f;
            var shape = trail.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.08f;
            var col = trail.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            trailGo.GetComponent<ParticleSystemRenderer>().sharedMaterial = FxMaterials.Particle(ShockwaveEffect.GooColor);
        }

        void Begin(Vector3 from, Vector3 v, SpitterConfig config, float scale)
        {
            cfg = config;
            damageScale = scale;
            velocity = v;
            life = 0f;
            transform.position = from;
            blob.localScale = Vector3.one * cfg.projectileRadius * 2.2f;
            gameObject.SetActive(true);
            trail.Clear();
            trail.Play();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            life += dt;
            Vector3 from = transform.position;
            velocity += Vector3.down * cfg.projectileGravity * dt;
            Vector3 step = velocity * dt;

            // Wobble so it reads as a liquid blob, stretched along its flight.
            float wob = 1f + 0.12f * Mathf.Sin(life * 30f);
            blob.localScale = new Vector3(wob, 1f / wob, 1.25f) * cfg.projectileRadius * 2.2f;
            if (velocity.sqrMagnitude > 0.01f) blob.rotation = Quaternion.LookRotation(velocity);

            if (TryHit(from, step, out var hit))
            {
                Splat(hit);
                return;
            }
            transform.position = from + step;
            if (life > MaxLife) Finish();
        }

        bool TryHit(Vector3 from, Vector3 step, out RaycastHit best)
        {
            best = default;
            float len = step.magnitude;
            if (len < 1e-5f) return false;
            int n = Physics.SphereCastNonAlloc(from, cfg.projectileRadius, step / len, hits, len, ~0, QueryTriggerInteraction.Ignore);
            float bestDist = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (h.collider.GetComponentInParent<ZombieHealth>() != null) continue; // passes through the horde
                if (h.distance < bestDist) { bestDist = h.distance; best = h; found = true; }
            }
            if (found && best.point == Vector3.zero) best.point = from;
            return found;
        }

        void Splat(RaycastHit hit)
        {
            var player = hit.collider.GetComponentInParent<PlayerHealth>();
            Vector3 at = hit.point;
            if (player != null && !player.IsDead)
            {
                player.TakeDamage(cfg.directDamage * damageScale);
                var pc = player.GetComponent<PlayerController>();
                if (pc != null) pc.ApplySlow(cfg.slowMultiplier, cfg.slowDuration);
                at = player.transform.position;
                GooPuddle.Spawn(at, cfg, damageScale);
            }
            else if (hit.normal.y > 0.5f)
            {
                GooPuddle.Spawn(at, cfg, damageScale);
            }

            ShockwaveEffect.Spawn(at, cfg.puddleRadius * 0.9f, ShockwaveEffect.GooColor, 0.35f, false);
            var lib = SoundManager.Library;
            if (lib != null) SoundManager.PlayAt(lib.gooSplat, at, 1f, 25f);
            Finish();
        }

        void Finish()
        {
            trail.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            gameObject.SetActive(false);
        }
    }
}
