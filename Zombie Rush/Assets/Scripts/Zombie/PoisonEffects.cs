using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZombieRush.Audio;
using ZombieRush.Data;
using ZombieRush.Effects;
using ZombieRush.Player;

namespace ZombieRush.Zombie
{
    /// An acid glob in flight: a glowing ball on a ballistic arc with a dripping trail. On impact it
    /// hurts a target it hit directly and leaves a ToxicZone puddle on the ground.
    public class AcidGlob : MonoBehaviour
    {
        PoisonConfig cfg;
        Transform owner;
        Vector3 velocity;
        float life;

        public static void Launch(Vector3 from, Vector3 to, float flightTime, PoisonConfig cfg, Transform owner)
        {
            var go = new GameObject("AcidGlob");
            go.transform.position = from;
            var g = go.AddComponent<AcidGlob>();
            g.cfg = cfg;
            g.owner = owner;
            // v such that from + v t + g t^2 / 2 = to
            g.velocity = (to - from - 0.5f * Physics.gravity * flightTime * flightTime) / flightTime;
            g.life = flightTime + 2f;

            var ball = new GameObject("Ball");
            ball.transform.SetParent(go.transform, false);
            ball.transform.localScale = Vector3.one * cfg.globSize;
            ball.AddComponent<MeshFilter>().sharedMesh = FxMaterials.Primitive(PrimitiveType.Sphere);
            var mr = ball.AddComponent<MeshRenderer>();
            mr.sharedMaterial = FxMaterials.Lit("AcidGlob", cfg.acidColor * 0.6f, cfg.acidColor * 2.5f);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            PoisonFx.Trail(go.transform, cfg.acidColor, cfg.globSize);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Vector3 p0 = transform.position;
            velocity += Physics.gravity * dt;
            Vector3 p1 = p0 + velocity * dt;
            life -= dt;
            Vector3 d = p1 - p0;
            foreach (var h in Physics.SphereCastAll(p0, cfg.globSize * 0.5f, d.normalized, d.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (owner != null && h.collider.transform.IsChildOf(owner)) continue;
                if (h.collider.GetComponentInParent<ZombieHealth>() != null) continue;
                Impact(h.point == Vector3.zero ? p0 : h.point);
                return;
            }
            transform.position = p1;
            if (life <= 0f) Impact(p1);
        }

        void Impact(Vector3 at)
        {
            // direct hit: anyone whose body is close to the impact (the host decides; elsewhere it is only for show)
            bool real = !ZombieRush.Net.NetSession.IsRunning || ZombieRush.Net.NetSession.IsServer;
            if (real) foreach (var ph in PoisonFx.Players())
            {
                Vector3 body = ph.transform.position + Vector3.up * 1.0f;
                if (Vector3.Distance(body, at) <= cfg.directRadius + 0.5f) ph.TakeDamage(cfg.directDamage);
            }
            // the puddle goes on the ground under the impact
            Vector3 ground = at;
            if (Physics.Raycast(at + Vector3.up * 0.5f, Vector3.down, out var gh, 4f, ~0, QueryTriggerInteraction.Ignore)) ground = gh.point;
            ToxicZone.Spawn(ground, cfg.puddleRadius, cfg.puddleDuration, cfg.puddleDamagePerSecond, cfg, false);
            PoisonFx.Splash(at, cfg.acidColor, 0.6f);
            var lib = SoundManager.Library;
            if (lib != null && lib.gooSplat != null) SoundManager.PlayAt(lib.gooSplat, at, 1f, 25f);
            Destroy(gameObject);
        }
    }

    /// A patch of poison: a glowing puddle on the ground, or a drifting gas cloud. Hurts any player
    /// standing in it every PoisonConfig.damageTick seconds, then fades away.
    public class ToxicZone : MonoBehaviour
    {
        float radius, duration, dps, age, tick;
        PoisonConfig cfg;
        bool cloud;
        MeshRenderer puddle;
        MaterialPropertyBlock block;
        ParticleSystem ps;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), ColorId = Shader.PropertyToID("_Color");

        public static ToxicZone Spawn(Vector3 at, float radius, float duration, float dps, PoisonConfig cfg, bool cloud)
        {
            var go = new GameObject(cloud ? "ToxicCloud" : "ToxicPuddle");
            go.transform.position = at + Vector3.up * 0.03f;
            var z = go.AddComponent<ToxicZone>();
            z.radius = radius; z.duration = duration; z.dps = dps; z.cfg = cfg; z.cloud = cloud;
            if (!cloud)
            {
                // a lumpy goo puddle lying on the ground (tilted to the surface under it)
                var disc = new GameObject("Puddle");
                disc.transform.SetParent(go.transform, false);
                Vector3 n = Vector3.up;
                if (Physics.Raycast(at + Vector3.up * 0.5f, Vector3.down, out var gh, 1.5f, ~0, QueryTriggerInteraction.Ignore)) n = gh.normal;
                disc.transform.rotation = Quaternion.LookRotation(-n, Vector3.forward) * Quaternion.Euler(0f, 0f, Random.value * 360f);
                disc.transform.localScale = Vector3.one * radius * 2f * EffectsConfig.Current.puddleScale;
                disc.AddComponent<MeshFilter>().sharedMesh = FxMaterials.Primitive(PrimitiveType.Quad);
                var mr = disc.AddComponent<MeshRenderer>();
                mr.sharedMaterial = FxMaterials.Fx("Fx_Puddle", Color.white, false);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                z.puddle = mr; z.block = new MaterialPropertyBlock();
                z.ps = PoisonFx.Bubbles(go.transform, cfg.acidColor, radius);
            }
            else z.ps = PoisonFx.Cloud(go.transform, cfg.cloudColor, radius, duration);
            return z;
        }

        void Update()
        {
            age += Time.deltaTime;
            float fade = 1f - Mathf.Clamp01((age - (duration - 1.5f)) / 1.5f);
            if (puddle != null)
            {
                var c = cfg.acidColor * 0.55f; c.a = 0.9f * fade * Mathf.Clamp01(age * 4f);
                puddle.GetPropertyBlock(block);
                block.SetColor(BaseColorId, c); block.SetColor(ColorId, c);
                puddle.SetPropertyBlock(block);
                if (age < 0.25f) puddle.transform.localScale = Vector3.one * radius * 2f * EffectsConfig.Current.puddleScale * (0.6f + 1.6f * age);
            }
            if (ps != null && age > duration - 1.5f) { var em = ps.emission; em.enabled = false; }

            tick -= Time.deltaTime;
            bool real = !ZombieRush.Net.NetSession.IsRunning || ZombieRush.Net.NetSession.IsServer;
            if (real && tick <= 0f && fade > 0.3f)
            {
                tick = cfg.damageTick;
                foreach (var ph in PoisonFx.Players())
                {
                    Vector3 d = ph.transform.position - transform.position;
                    float up = d.y; d.y = 0f;
                    if (d.magnitude <= radius && up > -1f && up < (cloud ? 2.5f : 1.2f)) ph.TakeDamage(dps * cfg.damageTick);
                }
            }
            if (age >= duration) Destroy(gameObject);
        }

    }

    /// Particle looks shared by the glob, puddle and cloud (runtime-built, no assets).
    internal static class PoisonFx
    {
        static readonly List<PlayerHealth> players = new List<PlayerHealth>();
        static float playersAt = -10f;

        /// The living players (re-found at most once a second).
        public static List<PlayerHealth> Players()
        {
            if (Time.time - playersAt > 1f)
            {
                playersAt = Time.time;
                players.Clear();
                foreach (var p in Object.FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
                    if (!p.IsDead) players.Add(p);
            }
            players.RemoveAll(p => p == null || p.IsDead);
            return players;
        }

        /// A particle system with an effect texture (Fx_Drop, Fx_Smoke, Fx_Chunk). Particles carry the colour.
        static ParticleSystem Make(Transform parent, string name, Color color, string texture = "Fx_Drop", bool lit = false, bool stretch = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main; m.startColor = color;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = FxMaterials.Fx(texture, Color.white, lit);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (stretch) { r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.06f; r.lengthScale = 1.5f; }
            return ps;
        }

        public static void Trail(Transform parent, Color c, float size)
        {
            var ps = Make(parent, "Trail", c, "Fx_Drop");
            var m = ps.main; m.startLifetime = 0.45f; m.startSpeed = 0.2f; m.startSize = size * 0.9f; m.simulationSpace = ParticleSystemSimulationSpace.World; m.gravityModifier = 0.4f; m.maxParticles = 80;
            var e = ps.emission; e.rateOverTime = 70f;
            var s = ps.shape; s.shapeType = ParticleSystemShapeType.Sphere; s.radius = size * 0.3f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.6f, 0.6f, 0.6f), 1f) }, new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            ps.Play();
        }

        public static void Splash(Vector3 at, Color c, float size)
        {
            var go = new GameObject("AcidSplash");
            go.transform.SetPositionAndRotation(at, Quaternion.Euler(-90f, 0f, 0f));
            var ps = Make(go.transform, "Drops", c, "Fx_Drop", false, true);
            var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f); m.startSpeed = new ParticleSystem.MinMaxCurve(2f * size, 5f * size);
            m.startSize = new ParticleSystem.MinMaxCurve(0.04f * size, 0.10f * size); m.gravityModifier = 1.4f; m.loop = false; m.simulationSpace = ParticleSystemSimulationSpace.World; m.maxParticles = 60;
            var e = ps.emission; e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(25 * size), (short)(40 * size)) });
            var s = ps.shape; s.shapeType = ParticleSystemShapeType.Hemisphere; s.radius = 0.15f * size;
            ps.Play();
            Gas(go.transform, c, 0.5f * size, 4);
            Object.Destroy(go, 2f);
        }

        /// A quick puff of gas: a few smoke billboards that swell and fade.
        static void Gas(Transform parent, Color c, float size, int count)
        {
            var ps = Make(parent, "Puff", c, "Fx_Smoke");
            var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.0f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
            m.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size * 1.2f); m.loop = false; m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var cc = c; cc.a = EffectsConfig.Current.gasAlpha; m.startColor = cc;
            var e = ps.emission; e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            ps.Play();
        }

        /// The poison zombie bursting: goo chunks and droplets flung out, a gas puff, a goo splat on the ground.
        public static void Burst(Vector3 at, Color c)
        {
            var cfg = EffectsConfig.Current;
            var go = new GameObject("ToxicBurst");
            go.transform.SetPositionAndRotation(at, Quaternion.Euler(-90f, 0f, 0f));
            var chunks = Make(go.transform, "Chunks", c * 0.55f, "Fx_Chunk", true);
            var m = chunks.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.5f); m.startSpeed = new ParticleSystem.MinMaxCurve(cfg.burstSpeed * 0.4f, cfg.burstSpeed);
            m.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f); m.gravityModifier = 2f; m.loop = false; m.simulationSpace = ParticleSystemSimulationSpace.World; m.maxParticles = 80;
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var e = chunks.emission; e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)cfg.burstChunks) });
            var s = chunks.shape; s.shapeType = ParticleSystemShapeType.Hemisphere; s.radius = 0.3f;
            var rot = chunks.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            chunks.Play();
            var drops = Make(go.transform, "Drops", c, "Fx_Drop", false, true);
            var dm = drops.main; dm.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f); dm.startSpeed = new ParticleSystem.MinMaxCurve(cfg.burstSpeed * 0.5f, cfg.burstSpeed * 1.3f);
            dm.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f); dm.gravityModifier = 1.6f; dm.loop = false; dm.simulationSpace = ParticleSystemSimulationSpace.World; dm.maxParticles = 120;
            var de = drops.emission; de.rateOverTime = 0f; de.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)cfg.burstDrops) });
            var ds = drops.shape; ds.shapeType = ParticleSystemShapeType.Sphere; ds.radius = 0.25f;
            drops.Play();
            Gas(go.transform, c, 1.6f, 7);
            GroundDecals.Place(at, cfg.burstSplatSize, FxMaterials.Fx("Fx_Puddle", Color.white, false), c * 0.45f, 10f, 3f);
            Object.Destroy(go, 3f);
        }

        public static ParticleSystem Bubbles(Transform parent, Color c, float radius)
        {
            var ps = Make(parent, "Bubbles", c, "Fx_Drop");
            ps.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.4f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.18f); m.maxParticles = 60;
            var e = ps.emission; e.rateOverTime = 14f * radius;
            var s = ps.shape; s.shapeType = ParticleSystemShapeType.Circle; s.radius = radius * 0.85f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            ps.Play();
            return ps;
        }

        public static ParticleSystem Cloud(Transform parent, Color c, float radius, float duration)
        {
            var cc = c; cc.a = Mathf.Max(c.a, EffectsConfig.Current.gasAlpha);
            var ps = Make(parent, "Gas", cc, "Fx_Smoke");
            ps.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(2.0f, 3.5f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
            m.startSize = new ParticleSystem.MinMaxCurve(radius * 0.6f, radius * 1.2f); m.maxParticles = 90; m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var e = ps.emission; e.rateOverTime = 18f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 25) });
            var s = ps.shape; s.shapeType = ParticleSystemShapeType.Circle; s.radius = radius * 0.6f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.75f, 0.75f, 0.75f), 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.3f));
            ps.Play();
            return ps;
        }
    }
}
