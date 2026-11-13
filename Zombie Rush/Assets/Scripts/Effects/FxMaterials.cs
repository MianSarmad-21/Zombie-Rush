using System.Collections.Generic;
using UnityEngine;

namespace ZombieRush.Effects
{
    /// Materials for runtime effects. Particle materials are copies of the BloodParticle
    /// material in Resources (so the particle shader is never stripped from a build), tinted
    /// per colour and cached. Lit materials come from Resources by name.
    public static class FxMaterials
    {
        const string ParticleBase = "ZombieRush/BloodParticle";

        static Material particleBase;
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        static Texture2D softDot, ring;

        /// Unlit particle material in a colour. texture: null = soft dot, or a custom one (ring).
        public static Material Particle(Color color, Texture2D texture = null, string key = null)
        {
            key = (key ?? "p") + ColorUtility.ToHtmlStringRGBA(color);
            if (cache.TryGetValue(key, out var m) && m != null) return m;

            if (particleBase == null) particleBase = Resources.Load<Material>(ParticleBase);
            if (particleBase != null) m = new Material(particleBase);
            else
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                m = new Material(shader);
            }
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            var tex = texture != null ? texture : SoftDot;
            m.mainTexture = tex;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            cache[key] = m;
            return m;
        }

        public const string FxFolder = "ZombieRush/Fx/";
        /// Transparent base materials made by the HUD/FX tool (Zombie Rush > Effects > Build Effect Materials):
        /// Fx_Glow (unlit, for glowing poison) and Fx_Lit (lit by the scene, for blood).
        public const string GlowBase = FxFolder + "Fx_Glow", LitBase = FxFolder + "Fx_Lit";
        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();

        /// One of the effect textures in Resources/ZombieRush/Fx (Fx_Splat, Fx_Puddle, Fx_Drop, Fx_Smoke, Fx_Chunk).
        public static Texture2D Tex(string name)
        {
            if (textures.TryGetValue(name, out var t) && t != null) return t;
            t = Resources.Load<Texture2D>(FxFolder + name);
            textures[name] = t;
            return t;
        }

        /// A see-through effect material: an effect texture tinted to a colour, lit by the scene (blood) or
        /// glowing on its own (poison). Cached per texture, colour and kind.
        public static Material Fx(string texture, Color color, bool lit)
        {
            string key = $"fx{texture}{(lit ? "L" : "G")}{ColorUtility.ToHtmlStringRGBA(color)}";
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            var baseMat = Resources.Load<Material>(lit ? LitBase : GlowBase);
            m = baseMat != null ? new Material(baseMat) : new Material(Particle(color));
            var tex = Tex(texture) ?? SoftDot;
            m.mainTexture = tex;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            cache[key] = m;
            return m;
        }

        /// A material from Resources/ZombieRush (made by the character setup tool), or a plain
        /// lit fallback in the given colour.
        public static Material Lit(string resourceName, Color fallback, Color emission = default)
        {
            if (cache.TryGetValue(resourceName, out var m) && m != null) return m;
            m = Resources.Load<Material>("ZombieRush/" + resourceName);
            if (m == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                m = new Material(shader) { color = fallback };
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", fallback);
                if (emission != default)
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", emission);
                }
            }
            cache[resourceName] = m;
            return m;
        }

        static Texture2D SoftDot => softDot != null ? softDot : softDot = MakeRadial(32, (r) => Mathf.Clamp01(1f - r) * Mathf.Clamp01(1f - r));

        /// A thin glowing ring, for shockwaves.
        public static Texture2D Ring => ring != null ? ring : ring = MakeRadial(128, (r) => Mathf.Exp(-Mathf.Pow((r - 0.85f) / 0.06f, 2f)));

        static Texture2D MakeRadial(int size, System.Func<float, float> alpha)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float h = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float r = Mathf.Sqrt((x - h) * (x - h) + (y - h) * (y - h)) / h;
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(r)) * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        /// The built-in sphere / cylinder meshes, without making a collider each time.
        public static Mesh Primitive(PrimitiveType type)
        {
            string key = "mesh" + type;
            if (meshes.TryGetValue(key, out var mesh) && mesh != null) return mesh;
            var go = GameObject.CreatePrimitive(type);
            mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(go);
            meshes[key] = mesh;
            return mesh;
        }

        static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
    }
}
