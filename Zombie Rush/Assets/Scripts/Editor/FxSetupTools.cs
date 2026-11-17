using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZombieRush.EditorTools
{
    /// Blood and poison effect assets in Assets/Resources/ZombieRush/Fx:
    ///   - imports the effect textures (Tools/Fx/make_fx_textures.py) with alpha, clamped, mipmapped;
    ///   - creates the tuning asset EffectsConfig (blood and poison look) if missing;
    ///   - makes the two see-through base materials the game tints at runtime: Fx_Lit (URP Particles/Simple
    ///     Lit, lit by the scene: blood) and Fx_Glow (URP Particles/Unlit: glowing poison). Keeping them as
    ///     assets in Resources keeps the transparent shader variants in builds.
    /// Marker: Temp/ZR_RunMapCleanup with "fx" (report in Temp/ZR_MapCleanup.log).
    public static class FxSetupTools
    {
        const string Folder = "Assets/Resources/ZombieRush/Fx";

        [MenuItem("Zombie Rush/Effects/Build Effect Materials", priority = 40)]
        static void Menu() => Debug.Log("Zombie Rush: " + Run());

        public static string Run()
        {
            int tex = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                // Fx_Breach (zombie spawn cracks) is PREMULTIPLIED: its glow lives in see-through pixels, which
                // "alpha is transparency" would overwrite; and it is seen big on the ground, so 512
                bool premul = path.EndsWith("Fx_Breach.png");
                ti.alphaIsTransparency = !premul;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.mipmapEnabled = true;
                ti.maxTextureSize = premul ? 512 : 256;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
                tex++;
            }
            var lit = Make("Fx_Lit", "Universal Render Pipeline/Particles/Simple Lit");
            var glow = Make("Fx_Glow", "Universal Render Pipeline/Particles/Unlit");
            string cfgPath = $"{Folder}/EffectsConfig.asset";
            bool madeCfg = AssetDatabase.LoadAssetAtPath<ZombieRush.Data.EffectsConfig>(cfgPath) == null;
            if (madeCfg) AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<ZombieRush.Data.EffectsConfig>(), cfgPath);
            AssetDatabase.SaveAssets();
            return $"Effects: {tex} textures imported; config {(madeCfg ? "created" : "kept")}; materials {(lit != null ? lit.shader.name : "MISSING")}, {(glow != null ? glow.shader.name : "MISSING")}.";
        }

        static Material Make(string name, string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) return null;
            string path = $"{Folder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            // transparent, alpha blended, both sides, no depth write
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.SetFloat("_AlphaClip", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.DisableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.6f);   // wet look on lit blood
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
