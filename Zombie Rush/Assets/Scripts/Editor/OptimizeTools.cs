using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZombieRush.EditorTools
{
    /// Zombie Rush > Optimize > Optimize Game Assets (phones + PC)
    ///
    /// Only touches what the game actually uses (the enabled build scenes, their dependencies and
    /// Resources), so packs that were never placed are left alone. Safe to run again; it only
    /// changes what isn't already set.
    ///   1. Textures: an Android size cap per kind (environment/zombies 1024, first-person guns and
    ///      sky 2048, UI untouched) with compression - phones load and draw a quarter of the data.
    ///   2. Over-sized character maps (4096) down to 2048 everywhere.
    ///   3. Models: Read/Write off (no second copy of every mesh in RAM); environment models get
    ///      low vertex compression (smaller build).
    ///   4. Zombie Animators skip bone updates while off-screen.
    public static class OptimizeTools
    {
        const string MinerFolder = "Assets/Models/2026-10-18_MinerJuggernaut";
        static readonly string[] ZombiePrefabs = { "Assets/Prefabs/Zombie_Yaku.prefab", "Assets/Prefabs/Zombie_Miner.prefab", "Assets/Prefabs/Zombie.prefab" };

        [MenuItem("Zombie Rush/Optimize/Optimize Game Assets (phones + PC)", priority = 1)]
        static void Run()
        {
            if (!EditorUtility.DisplayDialog("Optimize game assets",
                    "This re-imports the textures and models the game uses with lighter settings (mainly for Android). " +
                    "It can take a few minutes the first time.\n\nNothing is deleted; source files are not changed.", "Optimize", "Cancel"))
                return;

            var used = UsedAssets();
            int texChanged = 0, texCapped = 0, modelChanged = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                int i = 0;
                foreach (var path in used)
                {
                    if (++i % 25 == 0 && EditorUtility.DisplayCancelableProgressBar("Optimizing", path, (float)i / used.Count)) break;
                    var importer = AssetImporter.GetAtPath(path);
                    if (importer is TextureImporter ti)
                    {
                        if (OptimizeTexture(path, ti, out bool capped)) { ti.SaveAndReimport(); texChanged++; }
                        if (capped) texCapped++;
                    }
                    else if (importer is ModelImporter mi && OptimizeModel(path, mi)) { mi.SaveAndReimport(); modelChanged++; }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }

            int animators = OptimizeZombieAnimators();
            AssetDatabase.SaveAssets();

            string msg = $"Assets the game uses: {used.Count}\n" +
                         $"Textures updated: {texChanged} (of which over-sized maps capped to 2048: {texCapped})\n" +
                         $"Models updated: {modelChanged}\n" +
                         $"Zombie animators set to skip off-screen updates: {animators}\n\n" +
                         "Android: textures now import at phone sizes with compression.\nPC: unchanged except the over-sized maps.";
            Debug.Log("[Zombie Rush] Optimize:\n" + msg);
            EditorUtility.DisplayDialog("Optimize game assets", "Done.\n\n" + msg, "OK");
        }

        /// Everything the enabled build scenes and Resources folders pull into the game.
        static List<string> UsedAssets()
        {
            var roots = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
            roots.AddRange(AssetDatabase.FindAssets("", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.Contains("/Resources/") && !AssetDatabase.IsValidFolder(p)));
            // Prefabs spawned by code (zombies, turret) are referenced from the scene; add them anyway.
            roots.AddRange(ZombiePrefabs.Where(p => AssetDatabase.LoadMainAssetAtPath(p) != null));
            return AssetDatabase.GetDependencies(roots.ToArray(), true).Where(p => p.StartsWith("Assets/")).Distinct().ToList();
        }

        static bool OptimizeTexture(string path, TextureImporter ti, out bool capped)
        {
            capped = false;
            bool changed = false;
            if (path.StartsWith("Assets/UI/") || ti.textureType == TextureImporterType.Sprite) return false; // UI stays crisp

            // Over-sized maps (the Miner's 4K set): 2048 is plenty for a character.
            if (ti.maxTextureSize > 2048)
            {
                ti.maxTextureSize = 2048;
                capped = changed = true;
            }

            int phone = PhoneSize(path);
            var android = ti.GetPlatformTextureSettings("Android");
            if (!android.overridden || android.maxTextureSize != phone || android.textureCompression != TextureImporterCompression.Compressed)
            {
                android.overridden = true;
                android.maxTextureSize = phone;
                android.format = TextureImporterFormat.Automatic; // ASTC on current Android
                android.textureCompression = TextureImporterCompression.Compressed;
                android.compressionQuality = 50;
                ti.SetPlatformTextureSettings(android);
                changed = true;
            }
            return changed;
        }

        /// Phone cap: things right in front of the camera keep 2048, the rest 1024.
        static int PhoneSize(string path)
        {
            string p = path.ToLowerInvariant();
            if (p.Contains("/weapons/") || p.Contains("delthorgames") || p.Contains("nokobot")) return 2048; // first-person guns fill the screen
            if (p.Contains("/sky")) return 2048;                                                             // stretched over the whole sky
            return 1024;
        }

        static bool OptimizeModel(string path, ModelImporter mi)
        {
            bool changed = false;
            if (mi.isReadable) { mi.isReadable = false; changed = true; } // no game code reads these meshes
            bool environment = path.StartsWith("Assets/PostApocalypticBuildings/") || path.StartsWith("Assets/RPG_FPS_game_assets_industrial/");
            if (environment && mi.meshCompression == ModelImporterMeshCompression.Off)
            {
                mi.meshCompression = ModelImporterMeshCompression.Low;
                changed = true;
            }
            return changed;
        }

        static int OptimizeZombieAnimators()
        {
            int count = 0;
            foreach (var path in ZombiePrefabs)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool dirty = false;
                    foreach (var a in root.GetComponentsInChildren<Animator>(true))
                    {
                        if (a.cullingMode == AnimatorCullingMode.CullUpdateTransforms) continue;
                        a.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                        dirty = true; count++;
                    }
                    foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        if (r.updateWhenOffscreen) { r.updateWhenOffscreen = false; dirty = true; }
                    if (dirty) PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            return count;
        }
    }
}
