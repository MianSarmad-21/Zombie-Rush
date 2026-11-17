using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZombieRush.EditorTools
{
    /// Zombie Rush > Level > Optimize For Mobile (marker: Temp/ZR_RunOptimizeLevel, report Temp/ZR_OptimizeLevel.log).
    /// On the open level (only the scenery under "Map"; anything with a Rigidbody, Animator or game script above
    /// it is left alone):
    ///   - occlusion flags: big pieces (over 4 m) hide what is behind them and can be hidden, the rest can be hidden;
    ///   - shadows off on foliage (grass, bushes, leaves, weeds, flowers) and on debris under 0.6 m;
    ///   - the main camera draws to 400 m (the fog hides everything past ~350 m);
    ///   - bakes occlusion culling (nothing behind a building is drawn) and saves the scene.
    [InitializeOnLoad]
    static class OptimizeLevelTools
    {
        const string Marker = "Temp/ZR_RunOptimizeLevel";
        static double next;

        static OptimizeLevelTools() => EditorApplication.update += Poll;

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 2.0;
            if (!File.Exists(Marker) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            string mode = File.ReadAllText(Marker).Trim();
            File.Delete(Marker);
            string r;
            try
            {
                r = mode == "textures" ? CapTextures()
                  : mode == "size" ? CapTextures() + "\n" + CapCharacterTextures() + "\n" + DropArchive()
                  : Run(mode != "nobake");
            }
            catch (System.Exception e) { r = "FAILED: " + e; }
            File.WriteAllText("Temp/ZR_OptimizeLevel.log", r);
        }

        [MenuItem("Zombie Rush/Level/Optimize For Mobile")]
        static void Menu() => UnityEngine.Debug.Log(Run(true));

        /// The building packs ship 4096 textures; on Android the 12 metallic maps had no limit (4096 each, ~10 MB
        /// apiece in the APK and in memory). Android override: colour / normal maps 1024, masks (metallic, roughness,
        /// AO, height) 512, ASTC (automatic). PC keeps its own sizes.
        static readonly string[] TextureFolders = { "Assets/PostApocalypticBuildings/Textures", "Assets/Models/2026-11-09_MossyBuildings/Textures" };
        static readonly string[] MaskWords = { "metallic", "metal", "rough", "_ao", "occlusion", "height", "mask", "smooth" };

        [MenuItem("Zombie Rush/Level/Cap Building Textures For Android")]
        static void MenuTextures() => UnityEngine.Debug.Log(CapTextures());

        static string CapTextures()
        {
            int changed = 0, total = 0; long before = 0, after = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", TextureFolders))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                    total++;
                    string n = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                    int cap = MaskWords.Any(w => n.Contains(w)) ? 512 : 1024;
                    var a = ti.GetPlatformTextureSettings("Android");
                    int old = a.overridden ? a.maxTextureSize : ti.maxTextureSize;
                    before += (long)old * old; after += (long)Mathf.Min(old, cap) * Mathf.Min(old, cap);
                    var fmt = cap <= 512 ? TextureImporterFormat.ASTC_8x8 : TextureImporterFormat.ASTC_6x6;
                    if (a.overridden && a.maxTextureSize <= cap && a.format == fmt) continue;
                    a.overridden = true; a.maxTextureSize = cap; a.format = fmt;
                    a.textureCompression = TextureImporterCompression.Compressed;
                    ti.SetPlatformTextureSettings(a);
                    ti.SaveAndReimport();
                    changed++;
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            return $"Android texture caps: {changed} of {total} building textures changed (colour/normal 1024, masks 512). " +
                   $"Texels shipped: {before / 1000000} M -> {after / 1000000} M ({100f * after / Mathf.Max(1, before):0}%).";
        }

        /// Characters and guns on Android: zombies 1024 (small on screen); the player and the guns keep a 2048
        /// colour map (close to the camera) with 1024 normal / metal / occlusion maps. ASTC 6x6.
        static string CapCharacterTextures()
        {
            int changed = 0, total = 0;
            var rules = new (string folder, int albedo, int other)[]
            {
                ("Assets/Models/2026-11-08_Wanderer_Final", 1024, 1024), ("Assets/Models/2026-11-08_Runner_Final", 1024, 1024),
                ("Assets/Models/2026-11-08_Horror_Final", 1024, 1024), ("Assets/Models/2026-11-09_Ghoul_Final", 1024, 1024), ("Assets/Models/2026-11-15_Bloater_Final", 1024, 1024),
                ("Assets/Models/2026-11-14_ShadowOperative_Final", 2048, 1024),
                // (the guns' textures are set by GunSetupTools.SharpTextures: finer ASTC 4x4 colour maps, aniso 8)
            };
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var (folder, albedoCap, otherCap) in rules)
                {
                    if (!AssetDatabase.IsValidFolder(folder)) continue;
                    foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        if (path.Contains("_orig") || path.Contains("_v1") || !(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                        total++;
                        int cap = path.Contains("Albedo") ? albedoCap : otherCap;
                        var a = ti.GetPlatformTextureSettings("Android");
                        if (a.overridden && a.maxTextureSize == cap && a.format == TextureImporterFormat.ASTC_6x6) continue;
                        a.overridden = true; a.maxTextureSize = cap; a.format = TextureImporterFormat.ASTC_6x6;
                        a.textureCompression = TextureImporterCompression.Compressed;
                        ti.SetPlatformTextureSettings(a);
                        ti.SaveAndReimport();
                        changed++;
                    }
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            return $"Character/gun Android textures: {changed} of {total} changed (zombies 1024, player and guns albedo 2048 + maps 1024, ASTC 6x6).";
        }

        /// Removes the inactive "Archived_TestZombies" leftover from the open level (it shipped 3 old zombie models).
        static string DropArchive()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var go = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Archived_TestZombies");
            if (go == null) return "Archived_TestZombies: not in the scene.";
            if (go.activeSelf) return "Archived_TestZombies is ACTIVE: left alone.";
            int n = go.GetComponentsInChildren<Transform>(true).Length;
            Object.DestroyImmediate(go);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"Removed inactive Archived_TestZombies ({n} objects) from {scene.name}; scene saved.";
        }

        static readonly string[] Foliage = { "grass", "bush", "leaf", "leafy", "weed", "flower", "fern", "ivy", "plant" };

        /// Something on the way up from the renderer to (not including) the Map root moves or is game logic.
        /// (The Map root itself carries level scripts such as the zone manager: checking it marked everything.)
        static bool Dynamic(Transform t, Transform mapRoot)
        {
            for (var p = t; p != null && p != mapRoot; p = p.parent)
            {
                if (p.GetComponent<Rigidbody>() != null || p.GetComponent<Animator>() != null) return true;
                foreach (var mb in p.GetComponents<MonoBehaviour>())
                    if (mb != null && mb.GetType().Namespace != null && mb.GetType().Namespace.StartsWith("ZombieRush")) return true;
            }
            return false;
        }

        static string Run(bool bake)
        {
            var sb = new StringBuilder();
            var map = GameObject.Find("Map");
            if (map == null) return "FAILED: no 'Map' object in the open scene.";
            int occluders = 0, occludees = 0, shadowsOff = 0, skipped = 0;
            foreach (var r in map.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (Dynamic(r.transform, map.transform)) { skipped++; continue; }
                var go = r.gameObject;
                var flags = GameObjectUtility.GetStaticEditorFlags(go);
                float size = r.bounds.size.magnitude;
                bool big = Mathf.Max(r.bounds.size.x, r.bounds.size.y, r.bounds.size.z) > 4f;
                var want = flags | StaticEditorFlags.OccludeeStatic | (big ? StaticEditorFlags.OccluderStatic : 0);
                if (want != flags) { GameObjectUtility.SetStaticEditorFlags(go, want); }
                if (big) occluders++; else occludees++;
                string n = (go.name + " " + (r.GetComponent<MeshFilter>()?.sharedMesh?.name ?? "")).ToLowerInvariant();
                bool foliage = Foliage.Any(f => n.Contains(f));
                if ((foliage || size < 0.6f) && r.shadowCastingMode != ShadowCastingMode.Off)
                {
                    r.shadowCastingMode = ShadowCastingMode.Off; shadowsOff++;
                }
            }
            sb.AppendLine($"Occlusion flags: {occluders} occluders (over 4 m), {occludees} occludees; {skipped} dynamic renderers left alone.");
            sb.AppendLine($"Shadows switched off on {shadowsOff} foliage / small debris renderers.");

            var cam = Camera.main;
            if (cam != null)
            {
                float old = cam.farClipPlane;
                cam.farClipPlane = 400f; cam.useOcclusionCulling = true;
                EditorUtility.SetDirty(cam);
                sb.AppendLine($"Main camera far plane {old:0} -> {cam.farClipPlane:0} m (fog: {RenderSettings.fogMode}, density {RenderSettings.fogDensity}).");
            }

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            if (bake)
            {
                StaticOcclusionCulling.smallestOccluder = 10f;   // coarse: 4 m / 0.3 m gave 38 MB of data (too big for phones)
                StaticOcclusionCulling.smallestHole = 1f;
                StaticOcclusionCulling.backfaceThreshold = 100f;
                var sw = Stopwatch.StartNew();
                bool ok = StaticOcclusionCulling.Compute();
                sb.AppendLine($"Occlusion bake: {(ok ? "done" : "FAILED")} in {sw.Elapsed.TotalSeconds:0} s, data {StaticOcclusionCulling.umbraDataSize / 1024} KB.");
                EditorSceneManager.SaveScene(scene);
            }
            sb.AppendLine("Scene saved.");
            return sb.ToString();
        }
    }
}
