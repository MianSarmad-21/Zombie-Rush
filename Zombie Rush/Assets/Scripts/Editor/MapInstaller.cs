using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using ZombieRush.Player;
using ZombieRush.Waves;
using ZombieRush.World;

namespace ZombieRush.EditorTools
{
    /// Zombie Rush > Level > Install Final Map (Post-Apocalyptic City)
    ///
    /// Replaces the old industrial yard with the Post-Apocalyptic Buildings city:
    ///   1. deletes the old map (Environment/Arena - the Industrial pack yard),
    ///   2. copies the whole URP demo scene (terrain, buildings, sun, post-processing
    ///      volume, reflection probe) into one "Map" object - only its camera is left
    ///      out - and takes over its sky, fog and ambient light. The game's own night
    ///      light and post FX are switched off and Map gets KeepSceneEnvironment, so
    ///      the pack's look is what you see in play,
    ///   3. picks the most open street spot near the city centre for the safe zone
    ///      (GDD rules kept: start 30 m, +15 m per expansion, 20 coins each),
    ///   4. puts the player there; spawn pads follow the zone edge.
    /// Undoable with Ctrl+Z until the scene is saved.
    ///
    /// Zombie Rush > Level > Delete Old Map Packs removes the Industrial pack and the
    /// unused HDRP / Built-in copies of the new pack, after checking nothing uses them.
    public static class MapInstaller
    {
        const string PackFolder = "Assets/PostApocalypticBuildings";
        const string DemoScenePath = PackFolder + "/URP/demo.unity";
        const string MapName = "Map";
        const int MaxExpansions = 8;

        static readonly string[] OldFolders =
        {
            "Assets/RPG_FPS_game_assets_industrial",
            PackFolder + "/HDRP",
            PackFolder + "/Built-in",
        };

        [MenuItem("Zombie Rush/Level/Install Final Map (Post-Apocalyptic City)", priority = 10)]
        public static void InstallMap()
        {
            var zone = Object.FindAnyObjectByType<ZoneManager>(FindObjectsInactive.Include);
            if (zone == null)
            {
                EditorUtility.DisplayDialog("Install Final Map", "Open the MainLevel scene first (no ZoneManager in the open scene).", "OK");
                return;
            }
            if (!File.Exists(DemoScenePath))
            {
                EditorUtility.DisplayDialog("Install Final Map", $"Couldn't find {DemoScenePath}.\nImport the Post Apocalyptic Buildings pack (URP folder included).", "OK");
                return;
            }
            var mainScene = zone.gameObject.scene;
            var existing = mainScene.GetRootGameObjects().FirstOrDefault(g => g.name == MapName);
            if (existing != null && !EditorUtility.DisplayDialog("Install Final Map",
                    "A 'Map' is already in the scene. Replace it with a fresh copy?", "Replace", "Cancel"))
                return;

            Undo.SetCurrentGroupName("Install final map");
            int undoGroup = Undo.GetCurrentGroup();

            // 1. Old map out.
            int removedOld = 0;
            foreach (var old in OldMapRoots(mainScene, existing))
            {
                Undo.DestroyObjectImmediate(old);
                removedOld++;
            }

            // 2. City in: copy everything from the demo scene except its camera (the player has one).
            var map = new GameObject(MapName);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(map, mainScene);
            Undo.RegisterCreatedObjectUndo(map, "Install final map");

            int copied = 0, skipped = 0;
            SceneLook look;
            var demo = EditorSceneManager.OpenScene(DemoScenePath, OpenSceneMode.Additive);
            try
            {
                look = SceneLook.Capture(demo, mainScene);
                foreach (var root in demo.GetRootGameObjects())
                {
                    if (root.GetComponent<Camera>() != null) { skipped++; continue; }
                    var copy = Object.Instantiate(root, map.transform);
                    copy.name = root.name;
                    copied++;
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(demo, true);
            }

            // The pack's environment replaces the game's: its sky/fog/ambient, its sun, its post FX.
            var packSun = map.GetComponentsInChildren<Light>(true).Where(l => l.type == LightType.Directional)
                .OrderByDescending(l => l.intensity).FirstOrDefault();
            look.Apply(packSun);
            int disabled = UsePackEnvironment(mainScene, map);

            // Static so the city batches and culls well on mobile (keeps the pack's own flags too).
            const StaticEditorFlags flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;
            foreach (var t in map.GetComponentsInChildren<Transform>(true))
                if (t != map.transform && t.GetComponent<Light>() == null && t.GetComponent<ReflectionProbe>() == null)
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, GameObjectUtility.GetStaticEditorFlags(t.gameObject) | flags);

            Physics.SyncTransforms();
            ArenaGround.Ignore = zone.boundaryVisual;

            // 3. Safe zone on the most open ground near the middle of the city.
            var terrain = map.GetComponentInChildren<Terrain>();
            var cityBounds = CityBounds(map);
            var spot = FindOpenSpot(cityBounds, terrain, out float clearance);
            float needed = FarthestCorner(spot, cityBounds);
            int steps = Mathf.Clamp(Mathf.CeilToInt((needed - zone.startRadius) / Mathf.Max(1f, zone.expandStep)), 1, MaxExpansions);
            float maxRadius = zone.startRadius + steps * zone.expandStep;

            float oldCenterY = zone.center.y;
            Undo.RecordObject(zone, "Install final map");
            zone.center = spot;
            zone.maxRadius = maxRadius;
            if (zone.playerBase != null)
            {
                Undo.RecordObject(zone.playerBase, "Install final map");
                zone.playerBase.position = spot;
            }
            zone.deployPlayerAtCenter = true;
            EditorUtility.SetDirty(zone);

            if (zone.boundaryVisual != null)
            {
                Undo.RecordObject(zone.boundaryVisual, "Install final map");
                var p = zone.boundaryVisual.position;
                zone.boundaryVisual.position = new Vector3(spot.x, spot.y + (p.y - oldCenterY), spot.z);
                var s = zone.boundaryVisual.localScale;
                zone.boundaryVisual.localScale = new Vector3(zone.startRadius * 2f, s.y, zone.startRadius * 2f);
            }

            // 4. Player at the centre, spawn pads on the zone edge.
            string playerNote = "Player not found.";
            var player = Object.FindAnyObjectByType<PlayerHealth>(FindObjectsInactive.Include);
            if (player != null)
            {
                if (ArenaGround.TryFindClearSpot(spot, spot.y, zone.startRadius * 0.8f, out var deploy))
                {
                    Undo.RecordObject(player.transform, "Install final map");
                    player.transform.position = deploy + Vector3.up * 0.05f;
                    playerNote = "Player placed at the zone centre.";
                }
                else playerNote = "No clear ground at the centre - the player is placed at runtime instead.";
            }

            var waves = Object.FindAnyObjectByType<WaveManager>(FindObjectsInactive.Include);
            if (waves != null)
            {
                Undo.RecordObject(waves, "Install final map");
                waves.spawnAtZoneEdge = true;
                EditorUtility.SetDirty(waves);
                if (waves.spawnPoints != null)
                {
                    foreach (var pad in waves.spawnPoints) if (pad != null) Undo.RecordObject(pad, "Install final map");
                    waves.ArrangeSpawnPoints(spot, zone.startRadius * waves.edgeRadiusFactor);
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(mainScene);
            Selection.activeGameObject = map;
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.Frame(new Bounds(spot, Vector3.one * zone.startRadius * 2f), false);

            string summary =
                $"Removed the old map ({removedOld} object(s)). Copied {copied} objects from the pack's demo into '{MapName}' " +
                $"(left out {skipped} camera).\n" +
                $"Using the pack's own sky, fog, sun{(packSun != null ? $" ({packSun.name})" : "")} and post-processing; " +
                $"switched off {disabled} of the game's own light/post FX object(s).\n" +
                $"City {cityBounds.size.x:0} x {cityBounds.size.z:0} m{(terrain != null ? " on terrain" : "")}.\n" +
                $"Safe zone centre ({spot.x:0}, {spot.y:0.0}, {spot.z:0}), open space ~{clearance:0} m around it. " +
                $"Starts {zone.startRadius:0} m, +{zone.expandStep:0} m per expansion ({zone.expandCost} coins), max {maxRadius:0} m.\n" +
                playerNote + (waves != null ? "\nSpawn pads move to the zone edge before every wave." : "");
            Debug.Log("Zombie Rush: " + summary);
            EditorUtility.DisplayDialog("Final map installed", summary +
                "\n\nPress Play to check, then save (Ctrl+S). Ctrl+Z undoes all of this before saving.", "OK");
        }

        [MenuItem("Zombie Rush/Level/Delete Old Map Packs", priority = 11)]
        public static void DeleteOldPacks()
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                {
                    EditorUtility.DisplayDialog("Delete Old Map Packs", "Save the open scene first (Ctrl+S) - the check reads the saved scene.", "OK");
                    return;
                }

            var folders = OldFolders.Where(AssetDatabase.IsValidFolder).ToList();
            if (folders.Count == 0)
            {
                EditorUtility.DisplayDialog("Delete Old Map Packs", "Nothing to delete - the old packs are already gone.", "OK");
                return;
            }

            // Everything that stays: build scenes plus every scene/prefab/material/asset outside the old folders.
            bool InOld(string path) => folders.Any(f => path == f || path.StartsWith(f + "/"));
            var keepers = AssetDatabase.GetAllAssetPaths()
                .Where(p => p.StartsWith("Assets/") && !InOld(p))
                .Where(p => p.EndsWith(".unity") || p.EndsWith(".prefab") || p.EndsWith(".mat") || p.EndsWith(".asset") ||
                            p.EndsWith(".controller") || p.EndsWith(".overrideController"))
                .ToList();

            var blocked = new Dictionary<string, List<string>>();
            for (int i = 0; i < keepers.Count; i++)
            {
                if (i % 50 == 0 && EditorUtility.DisplayCancelableProgressBar("Delete Old Map Packs", "Checking what still uses the old packs...", (float)i / keepers.Count))
                {
                    EditorUtility.ClearProgressBar();
                    return;
                }
                foreach (var dep in AssetDatabase.GetDependencies(keepers[i], true))
                {
                    var folder = folders.FirstOrDefault(f => dep.StartsWith(f + "/"));
                    if (folder == null) continue;
                    if (!blocked.TryGetValue(folder, out var users)) blocked[folder] = users = new List<string>();
                    if (!users.Contains(keepers[i])) users.Add(keepers[i]);
                }
            }
            EditorUtility.ClearProgressBar();

            var deletable = folders.Where(f => !blocked.ContainsKey(f)).ToList();
            string blockedNote = string.Join("\n", blocked.Select(b =>
                $"KEPT {b.Key} - still used by: {string.Join(", ", b.Value.Take(3))}{(b.Value.Count > 3 ? ", ..." : "")}"));

            if (deletable.Count == 0)
            {
                EditorUtility.DisplayDialog("Delete Old Map Packs", "Nothing deleted.\n" + blockedNote +
                    "\n\nRun Install Final Map and save the scene first.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Delete Old Map Packs",
                    "Nothing in the game uses these any more. Delete them?\n\n" + string.Join("\n", deletable) +
                    (blockedNote.Length > 0 ? "\n\n" + blockedNote : ""), "Delete", "Cancel"))
                return;

            var failed = new List<string>();
            AssetDatabase.DeleteAssets(deletable.ToArray(), failed);
            AssetDatabase.Refresh();

            string msg = $"Deleted {deletable.Count - failed.Count} folder(s)." +
                         (failed.Count > 0 ? "\nCouldn't delete: " + string.Join(", ", failed) : "") +
                         (blockedNote.Length > 0 ? "\n" + blockedNote : "");
            Debug.Log("Zombie Rush: " + msg);
            EditorUtility.DisplayDialog("Old map packs deleted", msg, "OK");
        }

        /// Makes the map's own sun and post FX the scene's look: switches off the game's own
        /// directional light and volumes outside the map, and marks the map with
        /// KeepSceneEnvironment so the Day/Night profile isn't applied over it at runtime.
        /// Safe to run again. Returns how many objects were switched off.
        internal static int UsePackEnvironment(UnityEngine.SceneManagement.Scene scene, GameObject map)
        {
            int disabled = 0;
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go == map || !go.activeSelf) continue;
                bool ownSun = go.TryGetComponent<Light>(out var l) && l.type == LightType.Directional;
                bool ownVolume = go.GetComponent<Volume>() != null;
                if (!ownSun && !ownVolume) continue;
                Undo.RecordObject(go, "Use map environment");
                go.SetActive(false);
                disabled++;
            }
            if (map.GetComponent<ZombieRush.Atmosphere.KeepSceneEnvironment>() == null)
                Undo.AddComponent<ZombieRush.Atmosphere.KeepSceneEnvironment>(map);
            return disabled;
        }

        /// The old yard: the "Environment" root that only holds "Arena", or "Arena" itself; plus a previous "Map".
        static IEnumerable<GameObject> OldMapRoots(UnityEngine.SceneManagement.Scene scene, GameObject previousMap)
        {
            if (previousMap != null) yield return previousMap;
            var arena = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .Select(t => t.gameObject)
                .FirstOrDefault(g => g.name == "Arena");
            if (arena == null) yield break;
            var parent = arena.transform.parent;
            yield return parent != null && parent.parent == null && parent.name == "Environment" && parent.childCount == 1
                ? parent.gameObject
                : arena;
        }

        /// A scene's Lighting-window environment (sky, fog, ambient, reflections). RenderSettings
        /// always reads the active scene, so it's captured with the demo scene active.
        struct SceneLook
        {
            Material skybox;
            bool fog;
            FogMode fogMode;
            Color fogColor;
            float fogDensity, fogStart, fogEnd;
            AmbientMode ambientMode;
            Color ambientSky, ambientEquator, ambientGround, ambientLight;
            float ambientIntensity, reflectionIntensity;
            int reflectionBounces;
            DefaultReflectionMode reflectionMode;
            Texture customReflection;

            public static SceneLook Capture(UnityEngine.SceneManagement.Scene source, UnityEngine.SceneManagement.Scene restoreActive)
            {
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(source);
                var l = new SceneLook
                {
                    skybox = RenderSettings.skybox,
                    fog = RenderSettings.fog,
                    fogMode = RenderSettings.fogMode,
                    fogColor = RenderSettings.fogColor,
                    fogDensity = RenderSettings.fogDensity,
                    fogStart = RenderSettings.fogStartDistance,
                    fogEnd = RenderSettings.fogEndDistance,
                    ambientMode = RenderSettings.ambientMode,
                    ambientSky = RenderSettings.ambientSkyColor,
                    ambientEquator = RenderSettings.ambientEquatorColor,
                    ambientGround = RenderSettings.ambientGroundColor,
                    ambientLight = RenderSettings.ambientLight,
                    ambientIntensity = RenderSettings.ambientIntensity,
                    reflectionIntensity = RenderSettings.reflectionIntensity,
                    reflectionBounces = RenderSettings.reflectionBounces,
                    reflectionMode = RenderSettings.defaultReflectionMode,
                    customReflection = RenderSettings.customReflectionTexture,
                };
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(restoreActive);
                return l;
            }

            /// Applies to the active scene (undoable).
            public void Apply(Light sun)
            {
                var settings = typeof(RenderSettings).GetMethod("GetRenderSettings",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.Invoke(null, null) as Object;
                if (settings != null) Undo.RecordObject(settings, "Install final map");

                RenderSettings.skybox = skybox;
                RenderSettings.fog = fog;
                RenderSettings.fogMode = fogMode;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogDensity = fogDensity;
                RenderSettings.fogStartDistance = fogStart;
                RenderSettings.fogEndDistance = fogEnd;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientSkyColor = ambientSky;
                RenderSettings.ambientEquatorColor = ambientEquator;
                RenderSettings.ambientGroundColor = ambientGround;
                RenderSettings.ambientLight = ambientLight;
                RenderSettings.ambientIntensity = ambientIntensity;
                RenderSettings.reflectionIntensity = reflectionIntensity;
                RenderSettings.reflectionBounces = reflectionBounces;
                RenderSettings.defaultReflectionMode = reflectionMode;
                RenderSettings.customReflectionTexture = customReflection;
                RenderSettings.sun = sun;
                DynamicGI.UpdateEnvironment();
            }
        }

        /// Footprint of the buildings (terrain excluded - it's much bigger than the city).
        internal static Bounds CityBounds(GameObject map)
        {
            var renderers = map.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToArray();
            if (renderers.Length == 0) return new Bounds(map.transform.position, Vector3.one * 60f);
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        static float GroundY(Vector3 p, Terrain terrain, Bounds city) =>
            terrain != null ? terrain.SampleHeight(p) + terrain.transform.position.y : city.min.y;

        /// Grid-searches the inner city for the spot with the most open ground around it,
        /// preferring spots near the centre.
        internal static Vector3 FindOpenSpot(Bounds city, Terrain terrain, out float clearance)
        {
            float reach = Mathf.Min(70f, 0.45f * Mathf.Min(city.size.x, city.size.z));
            return FindOpenSpot(city, terrain, reach, offset => offset.magnitude, out clearance);
        }

        /// Grid-searches within `reach` of the city centre for open ground; score = openness
        /// minus `penalty(offset from centre)`, so callers choose where they prefer it.
        internal static Vector3 FindOpenSpot(Bounds city, Terrain terrain, float reach, System.Func<Vector2, float> penalty, out float clearance)
        {
            float[] radii = { 1f, 3f, 5f, 8f, 12f };
            var c = city.center;
            float bestScore = float.MinValue;
            var best = new Vector3(c.x, GroundY(c, terrain, city), c.z);
            clearance = 0f;

            for (float x = -reach; x <= reach; x += 4f)
            for (float z = -reach; z <= reach; z += 4f)
            {
                var probe = new Vector3(c.x + x, 0f, c.z + z);
                float gy = GroundY(probe, terrain, city);
                if (!ArenaGround.TryGround(probe, gy, out var ground, 0.75f)) continue;

                float open = 0f;
                foreach (var r in radii)
                {
                    if (!ArenaGround.IsClear(ground, r, 1.8f + r)) break;
                    open = r;
                }
                if (open <= 0f) continue;

                float score = open * 10f - penalty(new Vector2(x, z));
                if (score > bestScore) { bestScore = score; best = ground; clearance = open; }
            }
            return best;
        }

        internal static float FarthestCorner(Vector3 p, Bounds b)
        {
            float dx = Mathf.Max(Mathf.Abs(b.min.x - p.x), Mathf.Abs(b.max.x - p.x));
            float dz = Mathf.Max(Mathf.Abs(b.min.z - p.z), Mathf.Abs(b.max.z - p.z));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
