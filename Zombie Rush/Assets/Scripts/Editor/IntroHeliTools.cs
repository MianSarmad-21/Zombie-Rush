using System.IO;
using UnityEditor;
using UnityEngine;

namespace ZombieRush.EditorTools
{
    /// The intro helicopter's materials, from what Blender/heli_refit.py writes to Resources/ZombieRush/Cutscene:
    /// Helicopter_Paint (albedo, mask: R metallic / A smoothness, normal map) and the troop seats' Seat_Paint (their own
    /// texture: aluminium tubes and red webbing, drawn on both faces). Saved as assets so a build keeps the shader variants they need; IntroCutscene loads them by name.
    /// Also sets the textures' import types. Marker: Temp/ZR_RunIntroHeli, report Temp/ZR_IntroHeli.log.
    [InitializeOnLoad]
    static class IntroHeliTools
    {
        const string Folder = "Assets/Resources/ZombieRush/Cutscene";
        const string Marker = "Temp/ZR_RunIntroHeli", Report = "Temp/ZR_IntroHeli.log";
        static double next;

        static IntroHeliTools() => EditorApplication.update += Poll;

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 2.0;
            if (!File.Exists(Marker)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            File.Delete(Marker);
            string r;
            try { r = Run(); }
            catch (System.Exception e) { r = "FAILED: " + e; }
            File.WriteAllText(Report, r);
        }

        [MenuItem("Zombie Rush/Intro/Helicopter Materials")]
        static void Menu() => Debug.Log("Zombie Rush: " + Run());

        static string Run()
        {
            var log = new System.Text.StringBuilder();
            Texture2D albedo = Tex("Helicopter_Albedo", false, true, log), mask = Tex("Helicopter_Mask", false, false, log), normal = Tex("Helicopter_Normal", true, false, log);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) return "FAILED: URP Lit shader not found.";

            var paint = Mat("Helicopter_Paint", lit);
            paint.SetTexture("_BaseMap", albedo); paint.mainTexture = albedo;
            paint.SetColor("_BaseColor", Color.white);
            paint.SetTexture("_MetallicGlossMap", mask);
            paint.SetFloat("_Metallic", 1f); paint.SetFloat("_Smoothness", 1f);   // the mask carries both
            if (mask != null) paint.EnableKeyword("_METALLICSPECGLOSSMAP"); else paint.DisableKeyword("_METALLICSPECGLOSSMAP");
            paint.SetTexture("_BumpMap", normal); paint.SetFloat("_BumpScale", 1f);
            if (normal != null) paint.EnableKeyword("_NORMALMAP"); else paint.DisableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(paint);

            var seatTex = Tex("Seats_Albedo", false, true, log);
            var seats = Mat("Seat_Paint", lit);
            seats.SetTexture("_BaseMap", seatTex); seats.mainTexture = seatTex;
            seats.SetColor("_BaseColor", Color.white);
            seats.SetFloat("_Metallic", 0.25f); seats.SetFloat("_Smoothness", 0.3f);
            seats.SetFloat("_Cull", 0f);   // the webbing is one sheet: both faces
            seats.doubleSidedGI = true;
            EditorUtility.SetDirty(seats);
            AssetDatabase.SaveAssets();
            log.Append($"materials: Helicopter_Paint (albedo {albedo != null}, mask {mask != null}, normal {normal != null}), Seat_Paint (texture {seatTex != null}) in {Folder}.");
            return log.ToString();
        }

        static Material Mat(string name, Shader shader)
        {
            string path = $"{Folder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader) { name = name }; AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != shader) m.shader = shader;
            return m;
        }

        static Texture2D Tex(string name, bool normal, bool srgb, System.Text.StringBuilder log)
        {
            string path = $"{Folder}/{name}.png";
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) { log.AppendLine($"{path}: missing (run Blender/heli_refit.py)."); return null; }
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (ti.textureType != type || ti.sRGBTexture != srgb || ti.alphaIsTransparency || ti.maxTextureSize != 2048)
            {
                ti.textureType = type; ti.sRGBTexture = srgb; ti.alphaIsTransparency = false; ti.maxTextureSize = 2048;
                ti.SaveAndReimport();
                log.AppendLine($"{path}: imported as {type}, sRGB {srgb}.");
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
