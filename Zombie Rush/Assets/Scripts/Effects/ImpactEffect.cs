using System.Collections.Generic;
using UnityEngine;
using ZombieRush.Data;

namespace ZombieRush.Effects
{
    /// A bullet hitting the world (not a zombie): sparks (many on metal), a puff of dust (more on dirt and
    /// grass) and a small dark bullet hole on still surfaces that fades after a while. Everything is pooled
    /// and made from the existing effect textures, so shooting allocates nothing and adds no files.
    public static class ImpactEffect
    {
        enum Surface : byte { Other, Metal, Dirt }

        class Burst { public GameObject go; public ParticleSystem sparks, dust; }
        class Hole { public Transform tr; public float until; }

        static readonly List<Burst> bursts = new List<Burst>();
        static readonly List<Hole> holes = new List<Hole>();
        static int nextBurst, nextHole;
        static readonly Dictionary<Collider, Surface> surfaceOf = new Dictionary<Collider, Surface>();
        static Material holeMaterial;
        static HoleFader fader;

        static int MaxBursts => ZombieRush.Core.MobilePerformance.Active ? 8 : 16;
        static int MaxHoles => ZombieRush.Core.MobilePerformance.Active ? 20 : 40;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { bursts.Clear(); holes.Clear(); surfaceOf.Clear(); nextBurst = nextHole = 0; fader = null; }

        /// A shot hit `hit` travelling along `direction`.
        public static void Spawn(RaycastHit hit, Vector3 direction)
        {
            var cfg = FeelConfig.Current;
            var surface = SurfaceOf(hit.collider);
            var b = GetBurst();
            if (b != null)
            {
                // sparks bounce off along the mirror of the shot, dust rises off the surface
                Vector3 bounce = Vector3.Reflect(direction.normalized, hit.normal);
                b.go.transform.SetPositionAndRotation(hit.point + hit.normal * 0.02f, Quaternion.LookRotation(Vector3.Slerp(hit.normal, bounce, 0.5f)));
                var range = surface == Surface.Metal ? cfg.sparksMetal : cfg.sparksOther;
                if (surface == Surface.Dirt) range = new Vector2Int(0, 1);
                var se = b.sparks.emission; se.SetBurst(0, new ParticleSystem.Burst(0f, (short)range.x, (short)Mathf.Max(range.x, range.y)));
                int puffs = surface == Surface.Dirt ? cfg.dustPuffs + 2 : surface == Surface.Metal ? 1 : cfg.dustPuffs;
                var de = b.dust.emission; de.SetBurst(0, new ParticleSystem.Burst(0f, (short)puffs));
                b.sparks.Clear(true);
                b.sparks.Play(true);
            }
            // holes only on still things (a hole on a moving car or a body would float in the air)
            if (hit.rigidbody == null && !(hit.collider is CharacterController)) PlaceHole(hit);
        }

        static Surface SurfaceOf(Collider c)
        {
            if (surfaceOf.TryGetValue(c, out var s)) return s;
            if (surfaceOf.Count > 4096) surfaceOf.Clear();   // colliders from earlier levels pile up: start over
            s = Surface.Other;
            if (c is TerrainCollider) s = Surface.Dirt;
            else
            {
                var r = c.GetComponent<Renderer>();
                if (r == null) r = c.GetComponentInParent<Renderer>();
                string n = ((r != null && r.sharedMaterial != null ? r.sharedMaterial.name : "") + " " + c.name).ToLowerInvariant();
                if (n.Contains("metal") || n.Contains("steel") || n.Contains("iron") || n.Contains("car") || n.Contains("container")
                    || n.Contains("barrel") || n.Contains("fence") || n.Contains("rust") || n.Contains("pipe") || n.Contains("truck")) s = Surface.Metal;
                else if (n.Contains("grass") || n.Contains("dirt") || n.Contains("ground") || n.Contains("mud") || n.Contains("sand")
                         || n.Contains("soil") || n.Contains("terrain") || n.Contains("sandbag")) s = Surface.Dirt;
            }
            surfaceOf[c] = s;   // looked up once per collider: no string work per shot after that
            return s;
        }

        static Burst GetBurst()
        {
            for (int tries = bursts.Count; tries > 0 && bursts.Count > 0; tries--)
            {
                int i = nextBurst % bursts.Count;
                var c = bursts[i];
                if (c.go == null) { bursts.RemoveAt(i); continue; }
                nextBurst = i + 1;
                if (!c.sparks.IsAlive(true)) return c;
            }
            if (bursts.Count >= MaxBursts) return null;   // a busy moment: skip this one rather than grow
            var b = CreateBurst();
            bursts.Add(b);
            return b;
        }

        static Burst CreateBurst()
        {
            var cfg = FeelConfig.Current;
            var go = new GameObject("BulletImpact");
            Object.DontDestroyOnLoad(go);

            var sparks = go.AddComponent<ParticleSystem>();
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = sparks.main;
            m.startColor = cfg.sparkColor;
            m.startSpeed = new ParticleSystem.MinMaxCurve(3f, 9f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.35f);
            m.duration = 0.4f; m.loop = false; m.playOnAwake = false; m.maxParticles = 20;
            m.gravityModifier = 1.2f;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            var e = sparks.emission; e.rateOverTime = 0; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 6) });
            var sh = sparks.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35f; sh.radius = 0.01f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.03f; r.lengthScale = 2f;
            r.sharedMaterial = FxMaterials.Fx("Fx_Drop", Color.white, false);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;

            var dg = new GameObject("Dust");
            dg.transform.SetParent(go.transform, false);
            var dust = dg.AddComponent<ParticleSystem>();
            dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var dm = dust.main;
            dm.startColor = cfg.dustColor;
            dm.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.4f);
            dm.startSize = new ParticleSystem.MinMaxCurve(cfg.dustSize * 0.5f, cfg.dustSize);
            dm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            dm.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            dm.duration = 0.4f; dm.loop = false; dm.playOnAwake = false; dm.maxParticles = 8;
            dm.gravityModifier = -0.05f;
            dm.simulationSpace = ParticleSystemSimulationSpace.World;
            var de = dust.emission; de.rateOverTime = 0; de.SetBursts(new[] { new ParticleSystem.Burst(0f, 3) });
            var ds = dust.shape; ds.shapeType = ParticleSystemShapeType.Cone; ds.angle = 25f; ds.radius = 0.03f;
            var dsz = dust.sizeOverLifetime; dsz.enabled = true; dsz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));
            var dc = dust.colorOverLifetime; dc.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            dc.color = g;
            var dr = dg.GetComponent<ParticleSystemRenderer>();
            dr.sharedMaterial = FxMaterials.Fx("Fx_Smoke", Color.white, true);
            dr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; dr.receiveShadows = false;

            return new Burst { go = go, sparks = sparks, dust = dust };
        }

        // ---------------------------------------------------------------- bullet holes

        static void PlaceHole(RaycastHit hit)
        {
            var cfg = FeelConfig.Current;
            Hole h;
            if (holes.Count < MaxHoles) { h = CreateHole(); holes.Add(h); }
            else
            {
                h = holes[nextHole % holes.Count];   // the oldest one moves here
                nextHole++;
                if (h.tr == null) { h = CreateHole(); holes[(nextHole - 1) % holes.Count] = h; }
            }
            h.tr.SetPositionAndRotation(hit.point + hit.normal * 0.006f, Quaternion.LookRotation(-hit.normal) * Quaternion.Euler(0f, 0f, Random.value * 360f));
            h.tr.localScale = Vector3.one * cfg.holeSize * Random.Range(0.8f, 1.2f);
            h.tr.gameObject.SetActive(true);
            h.until = Time.time + cfg.holeLife;
            if (fader == null)
            {
                var go = new GameObject("BulletHoles");
                Object.DontDestroyOnLoad(go);
                fader = go.AddComponent<HoleFader>();
            }
            fader.enabled = true;
        }

        static Hole CreateHole()
        {
            // the bullet hole rendered in Blender (Blender/fx_bullet_hole.py -> Resources/ZombieRush/Fx/Fx_BulletHole.png)
            if (holeMaterial == null) holeMaterial = FxMaterials.Fx("Fx_BulletHole", FeelConfig.Current.holeColor, true);
            var go = new GameObject("BulletHole");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<MeshFilter>().sharedMesh = FxMaterials.Primitive(PrimitiveType.Quad);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = holeMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return new Hole { tr = go.transform };
        }

        /// Shrinks old holes away in their last second, then hides them; switches itself off when none are left.
        class HoleFader : MonoBehaviour
        {
            void Update()
            {
                bool any = false;
                float now = Time.time;
                var cfg = FeelConfig.Current;
                foreach (var h in holes)
                {
                    if (h.tr == null || !h.tr.gameObject.activeSelf) continue;
                    float left = h.until - now;
                    if (left <= 0f) { h.tr.gameObject.SetActive(false); continue; }
                    any = true;
                    if (left < 1f) h.tr.localScale = Vector3.one * cfg.holeSize * left;
                }
                if (!any) enabled = false;
            }
        }
    }
}
