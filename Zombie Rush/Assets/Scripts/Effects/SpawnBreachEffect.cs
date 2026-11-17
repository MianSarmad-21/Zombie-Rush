using System.Collections.Generic;
using UnityEngine;

namespace ZombieRush.Effects
{
    /// Where a zombie claws its way up (2026-11-16, replaces the flat pulsing red spawn discs): the ground
    /// splits open - a poisoned patch with glowing toxic-green fissures (Fx_Breach, made by
    /// Tools/Fx/make_breach_texture.py) that tears outward in a quarter second - clods of dirt are thrown up,
    /// a puff of green poison gas rolls out and glowing spores drift up while it climbs out; then the glow cools and the scar
    /// fades. Laid flat on the ground under the spot (follows slopes). Pooled; plays on every machine
    /// (host: ZombieSpawnRise.Begin, others: ZombieNet when the rising zombie appears).
    public static class SpawnBreachEffect
    {
        const int MaxPool = 16;
        public static float Size = 3.8f;                 // decal width, metres
        public static float Life = 5f;                   // seconds until gone
        static readonly Color Ember = new Color(0.45f, 1f, 0.15f, 1f);             // poison spores
        static readonly Color Dirt = new Color(0.20f, 0.15f, 0.11f, 1f);
        static readonly Color Dust = new Color(0.30f, 0.42f, 0.16f, 0.55f);        // sickly green gas

        class Breach
        {
            public GameObject go;
            public Transform decal;
            public MeshRenderer decalRenderer;
            public ParticleSystem dirt, dust, embers;
            public float started = -99f, spin;
            public bool Busy => Time.time - started < Life;
        }

        static readonly List<Breach> pool = new List<Breach>();
        static Material decalMat;
        static Mesh quad;
        static MaterialPropertyBlock block;
        static Runner runner;

        /// The ground opens at `position` (the zombie's surfacing spot).
        public static void Play(Vector3 position)
        {
            var b = GetFree();
            if (b == null) return;
            Vector3 normal = Vector3.up;
            if (Physics.Raycast(position + Vector3.up * 1.5f, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<ZombieRush.Zombie.ZombieHealth>() == null)
            {
                position = hit.point;
                normal = Vector3.Slerp(Vector3.up, hit.normal, 0.85f);
            }
            b.go.transform.SetPositionAndRotation(position, Quaternion.FromToRotation(Vector3.up, normal));
            b.spin = Random.Range(0f, 360f);
            b.decal.localRotation = Quaternion.Euler(90f, b.spin, 0f);
            b.decal.localPosition = Vector3.up * 0.035f;
            b.decal.localScale = Vector3.one * 0.01f;
            b.started = Time.time;
            b.go.SetActive(true);
            foreach (var ps in new[] { b.dirt, b.dust, b.embers }) { ps.Clear(true); ps.Play(true); }
            Animate(b);
            if (runner == null)
            {
                var rg = new GameObject("SpawnBreaches");
                Object.DontDestroyOnLoad(rg);
                runner = rg.AddComponent<Runner>();
            }
        }

        /// Tear-open, glow and fade over the breach's life.
        static void Animate(Breach b)
        {
            float t = Time.time - b.started;
            if (t >= Life) { b.go.SetActive(false); return; }
            // tears open: fast out, a little overshoot
            float open = Mathf.Clamp01(t / 0.32f);
            float s = Size * (1f - Mathf.Pow(1f - open, 3f)) * (1f + 0.06f * Mathf.Sin(open * Mathf.PI));
            b.decal.localScale = new Vector3(Mathf.Max(0.01f, s), Mathf.Max(0.01f, s), 1f);
            // glow: a flash as it bursts, a throb while it climbs out (~1 s), then cools down
            float glow = t < 0.25f ? Mathf.Lerp(0.6f, 2.2f, t / 0.25f)
                       : t < 1.4f ? 1.45f + 0.35f * Mathf.Sin((t - 0.25f) * 9f)
                       : Mathf.Lerp(1.45f, 0f, Mathf.SmoothStep(0f, 1f, (t - 1.4f) / 2.2f));
            // the scar (dark soil) stays a little longer than the glow, then fades
            float fade = 1f - Mathf.SmoothStep(0f, 1f, (t - (Life - 1.4f)) / 1.4f);
            // premultiplied: rgb scales the glow, alpha the darkening
            block.SetColor("_BaseColor", new Color(glow * fade + 0.08f * fade, glow * fade + 0.08f * fade, glow * fade + 0.08f * fade, fade));
            b.decalRenderer.SetPropertyBlock(block);
        }

        static Breach GetFree()
        {
            foreach (var b in pool)
                if (b.go != null && !b.Busy) return b;
            pool.RemoveAll(b => b.go == null);
            if (pool.Count >= MaxPool)
            {
                // all busy (a big wave): reuse the oldest
                Breach oldest = pool[0];
                foreach (var b in pool) if (b.started < oldest.started) oldest = b;
                return oldest;
            }
            var n = Create();
            if (n != null) pool.Add(n);
            return n;
        }

        static Breach Create()
        {
            if (block == null) block = new MaterialPropertyBlock();
            if (decalMat == null)
            {
                var baseMat = Resources.Load<Material>(FxMaterials.GlowBase);
                var tex = FxMaterials.Tex("Fx_Breach");
                if (baseMat == null || tex == null) return null;
                decalMat = new Material(baseMat) { name = "Fx_Breach (runtime)" };
                decalMat.mainTexture = tex;
                if (decalMat.HasProperty("_BaseMap")) decalMat.SetTexture("_BaseMap", tex);
                // premultiplied alpha: the texture's soil darkens, its glow adds light, in one draw
                decalMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                decalMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                decalMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                decalMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 20;   // under blood and smoke
            }
            if (quad == null) quad = FxMaterials.Primitive(PrimitiveType.Quad);

            var go = new GameObject("SpawnBreach");
            Object.DontDestroyOnLoad(go);
            var d = new GameObject("Cracks");
            d.transform.SetParent(go.transform, false);
            d.AddComponent<MeshFilter>().sharedMesh = quad;
            var mr = d.AddComponent<MeshRenderer>();
            mr.sharedMaterial = decalMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            // clods of earth thrown up and out, falling back
            var dirt = Particles(go, "Dirt", FxMaterials.Fx("Fx_Chunk", Color.white, true), Dirt, 0.9f);
            {
                var m = dirt.main;
                m.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 5.5f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.17f);
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.1f);
                m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                m.gravityModifier = 1.8f; m.maxParticles = 30;
                var e = dirt.emission; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 14, 18), new ParticleSystem.Burst(0.35f, 6, 9) });
                var sh = dirt.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 28f; sh.radius = 0.35f;
                sh.rotation = new Vector3(-90f, 0f, 0f);
                var rot = dirt.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            }
            // a puff of dust rolling out low
            var dust = Particles(go, "Dust", FxMaterials.Fx("Fx_Smoke", Color.white, true), Dust, 1.4f);
            {
                var m = dust.main;
                m.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.4f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
                m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.0f);
                m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                m.gravityModifier = -0.02f; m.maxParticles = 12;
                var e = dust.emission; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 6, 8), new ParticleSystem.Burst(0.5f, 3) });
                var sh = dust.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.6f;
                sh.rotation = new Vector3(-90f, 0f, 0f);
                var sz = dust.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));
                FadeOut(dust);
            }
            // embers drifting up out of the cracks while it climbs
            var embers = Particles(go, "Embers", FxMaterials.Particle(Ember), Ember, 1.6f);
            {
                var m = embers.main;
                m.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
                m.gravityModifier = -0.15f; m.maxParticles = 40;
                var e = embers.emission; e.rateOverTime = 16f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 10) });
                var sh = embers.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 12f; sh.radius = 0.9f;
                sh.rotation = new Vector3(-90f, 0f, 0f);
                var n = embers.noise; n.enabled = true; n.strength = 0.6f; n.frequency = 1.2f; n.quality = ParticleSystemNoiseQuality.Low;
                FadeOut(embers);
            }
            go.SetActive(false);
            return new Breach { go = go, decal = d.transform, decalRenderer = mr, dirt = dirt, dust = dust, embers = embers };
        }

        static ParticleSystem Particles(GameObject parent, string name, Material mat, Color color, float duration)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent.transform, false);
            var ps = g.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.startColor = color; m.duration = duration; m.loop = false; m.playOnAwake = false;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            var e = ps.emission; e.rateOverTime = 0f;
            var r = g.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        static void FadeOut(ParticleSystem ps)
        {
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        class Runner : MonoBehaviour
        {
            void LateUpdate()
            {
                foreach (var b in pool)
                    if (b.go != null && b.go.activeSelf) Animate(b);
            }
        }
    }
}
