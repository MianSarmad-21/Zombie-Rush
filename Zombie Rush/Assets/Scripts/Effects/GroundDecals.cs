using System.Collections.Generic;
using UnityEngine;

namespace ZombieRush.Effects
{
    /// Splats that lie on the ground (blood, poison goo): a quad laid on the surface under a point, turned
    /// at random, that fades out after a while. Pooled with a fixed cap, so heavy fights never pile up
    /// unbounded objects (the oldest splat is reused first).
    public static class GroundDecals
    {
        const int MaxDecals = 40;
        const float Lift = 0.02f;      // above the surface, so it never flickers into it

        class Decal
        {
            public GameObject go;
            public MeshRenderer renderer;
            public MaterialPropertyBlock block;
            public float born, life, fade;
            public Color color;
        }

        static readonly List<Decal> decals = new List<Decal>();
        static int next;
        static Runner runner;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), ColorId = Shader.PropertyToID("_Color");

        /// Lays a splat on the ground under `point` (looks down up to `reach` metres). Returns false if
        /// there is no ground there. life: seconds fully shown, then fade seconds to vanish.
        public static bool Place(Vector3 point, float size, Material material, Color color, float life, float fade = 2f, float reach = 3f)
        {
            if (material == null) return false;
            if (!Physics.Raycast(point + Vector3.up * 0.3f, Vector3.down, out var hit, reach + 0.3f, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (hit.collider.GetComponentInParent<CharacterController>() != null) return false;   // not on a body

            var d = Get();
            d.go.transform.SetPositionAndRotation(hit.point + hit.normal * Lift,
                Quaternion.LookRotation(-hit.normal, Vector3.ProjectOnPlane(Random.onUnitSphere, hit.normal).normalized + Vector3.forward * 1e-3f)
                * Quaternion.Euler(0f, 0f, Random.value * 360f));
            d.go.transform.localScale = Vector3.one * size;
            d.renderer.sharedMaterial = material;
            d.color = color;
            d.born = Time.time; d.life = life; d.fade = Mathf.Max(0.05f, fade);
            Tint(d, 1f);
            d.go.SetActive(true);
            EnsureRunner();
            return true;
        }

        static Decal Get()
        {
            if (decals.Count < MaxDecals)
            {
                var go = new GameObject("GroundSplat");
                Object.DontDestroyOnLoad(go);
                go.AddComponent<MeshFilter>().sharedMesh = FxMaterials.Primitive(PrimitiveType.Quad);
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                var nd = new Decal { go = go, renderer = mr, block = new MaterialPropertyBlock() };
                decals.Add(nd);
                return nd;
            }
            // reuse a free one, else the oldest
            for (int i = 0; i < decals.Count; i++)
                if (!decals[i].go.activeSelf) return decals[i];
            var oldest = decals[0];
            foreach (var x in decals) if (x.born < oldest.born) oldest = x;
            return oldest;
        }

        static void Tint(Decal d, float alpha)
        {
            var c = d.color; c.a *= alpha;
            d.renderer.GetPropertyBlock(d.block);
            d.block.SetColor(BaseColorId, c);
            d.block.SetColor(ColorId, c);
            d.renderer.SetPropertyBlock(d.block);
        }

        static void EnsureRunner()
        {
            if (runner != null) return;
            var go = new GameObject("GroundDecals");
            Object.DontDestroyOnLoad(go);
            runner = go.AddComponent<Runner>();
        }

        class Runner : MonoBehaviour
        {
            void Update()
            {
                float now = Time.time;
                foreach (var d in decals)
                {
                    if (d.go == null || !d.go.activeSelf) continue;
                    float age = now - d.born;
                    float k = age < 0.08f ? age / 0.08f : 1f - Mathf.Clamp01((age - d.life) / d.fade);   // pop in, then fade
                    if (k <= 0f) { d.go.SetActive(false); continue; }
                    Tint(d, k);
                    if (age < 0.15f) d.go.transform.localScale *= 1f + Time.deltaTime * 1.2f;        // the splash spreads a little
                }
            }
        }
    }
}
