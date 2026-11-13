using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ZombieRush.EditorTools
{
    /// Zombie Rush > Level > Mossy Buildings (marker mode "mossbuildings"; "mossbuildings off" puts the pack look back)
    /// Uses the mossy colour textures from Blender/moss_building_textures.py: makes a copy of each pack
    /// building material with the mossy texture (the pack's own materials stay untouched) and points
    /// every renderer under Map at the copies. Saves the scene.
    static class MossyBuildingsTools
    {
        const string PackMats = "Assets/PostApocalypticBuildings/URP/Materials";
        const string Dir = "Assets/Models/2026-11-09_MossyBuildings";
        const string TexDir = Dir + "/Textures";
        const string MatDir = Dir + "/Materials";

        [MenuItem("Zombie Rush/Level/Mossy Buildings")]
        static void Menu() => Debug.Log("Zombie Rush: " + Run(true));

        public static string Run(bool on)
        {
            var map = GameObject.Find("Map");
            if (map == null) return "FAILED: no Map in the open scene.";
            Directory.CreateDirectory(MatDir);
            var pairs = new Dictionary<Material, Material>(); // pack -> mossy
            var sb = new StringBuilder();
            foreach (var texPath in Directory.GetFiles(TexDir, "*_color_moss.png"))
            {
                string name = Path.GetFileName(texPath).Replace("_color_moss.png", "");
                var pack = AssetDatabase.LoadAssetAtPath<Material>($"{PackMats}/{name}.mat");
                if (pack == null) { sb.AppendLine($"  {name}: no pack material"); continue; }
                string tp = texPath.Replace('\\', '/');
                // same import settings as the pack's own colour texture
                // the colour texture slot: URP Lit uses _BaseMap (and _MainTex), the pack's own shader _ColorMap
                var slots = new[] { "_BaseMap", "_MainTex", "_ColorMap" }.Where(pack.HasProperty).ToArray();
                var src = slots.Select(pack.GetTexture).FirstOrDefault(t => t != null);
                var srcImp = src != null ? AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(src)) as TextureImporter : null;
                var imp = AssetImporter.GetAtPath(tp) as TextureImporter;
                if (imp != null && srcImp != null && (imp.maxTextureSize != srcImp.maxTextureSize || imp.textureCompression != srcImp.textureCompression))
                {
                    imp.maxTextureSize = srcImp.maxTextureSize;
                    imp.textureCompression = srcImp.textureCompression;
                    imp.sRGBTexture = srcImp.sRGBTexture;
                    imp.mipmapEnabled = srcImp.mipmapEnabled;
                    imp.SaveAndReimport();
                }
                string mp = $"{MatDir}/{name}_Moss.mat";
                var moss = AssetDatabase.LoadAssetAtPath<Material>(mp);
                if (moss == null)
                {
                    moss = new Material(pack);
                    AssetDatabase.CreateAsset(moss, mp);
                }
                else moss.CopyPropertiesFromMaterial(pack);
                var mossTex = AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
                foreach (var slot in slots) moss.SetTexture(slot, mossTex);
                EditorUtility.SetDirty(moss);
                pairs[pack] = moss;
            }
            AssetDatabase.SaveAssets();

            var lookup = on ? pairs : pairs.ToDictionary(kv => kv.Value, kv => kv.Key);
            int swapped = 0, renderers = 0;
            foreach (var r in map.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && lookup.TryGetValue(mats[i], out var to)) { mats[i] = to; changed = true; swapped++; }
                if (!changed) continue;
                Undo.RecordObject(r, "Mossy buildings");
                r.sharedMaterials = mats;
                renderers++;
            }
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            return $"Mossy buildings {(on ? "on" : "off")}: {pairs.Count} materials, {swapped} material slots on {renderers} renderers swapped. Scene saved.\n" + sb;
        }
    }
}
