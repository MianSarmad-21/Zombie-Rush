using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZombieRush.Player;
using ZombieRush.Waves;
using ZombieRush.World;

namespace ZombieRush.EditorTools
{
    /// Zombie Rush > Level > Merge Zone A + B Into One Arena
    ///
    /// Turns the two fenced yards into one open arena:
    ///   1. deletes Zone A's inner fence (the walls splitting the map),
    ///   2. moves everything from ZoneA/ZoneB under a single "Arena" object,
    ///   3. centres the safe zone on the arena ground; max radius grows to cover it
    ///      (GDD rules kept: start 30 m, +15 m per expansion, 20 coins each),
    ///   4. deploys the player at the centre; spawn pads follow the zone edge.
    /// Zone B's outer fence stays as the map boundary. Everything is undoable (Ctrl+Z).
    public static class ArenaTools
    {
        const string ArenaName = "Arena";

        [MenuItem("Zombie Rush/Level/Merge Zone A + B Into One Arena", priority = 20)]
        public static void MergeZones()
        {
            var zone = Object.FindAnyObjectByType<ZoneManager>(FindObjectsInactive.Include);
            if (zone == null)
            {
                EditorUtility.DisplayDialog("Merge Zones", "No ZoneManager in the open scene.", "OK");
                return;
            }

            var zoneA = FindByName("ZoneA");
            var zoneB = FindByName("ZoneB");
            var arena = FindByName(ArenaName);
            if (arena == null && (zoneA == null || zoneB == null))
            {
                EditorUtility.DisplayDialog("Merge Zones", "Couldn't find 'ZoneA' and 'ZoneB' (or an existing 'Arena') in the open scene.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Merge zones into one arena");
            int undoGroup = Undo.GetCurrentGroup();

            // 1. Remove the inner fence: every fence piece that belongs to Zone A.
            int removedFences = 0;
            if (zoneA != null)
            {
                var fences = zoneA.GetComponentsInChildren<Transform>(true)
                    .Select(t => t.gameObject)
                    .Where(IsFencePiece)
                    .Select(go => PrefabUtility.GetOutermostPrefabInstanceRoot(go) ?? go)
                    .Distinct()
                    .ToList();
                foreach (var f in fences)
                {
                    if (f == null) continue;
                    Undo.DestroyObjectImmediate(f);
                    removedFences++;
                }
            }

            // 2. One arena: move both zones' contents under "Arena".
            if (arena == null)
            {
                var parent = zoneB.transform.parent;
                arena = new GameObject(ArenaName);
                Undo.RegisterCreatedObjectUndo(arena, "Create arena");
                Undo.SetTransformParent(arena.transform, parent, "Create arena");
                arena.transform.SetSiblingIndex(zoneB.transform.GetSiblingIndex());
            }
            foreach (var source in new[] { zoneB, zoneA })
            {
                if (source == null) continue;
                var children = new List<Transform>();
                foreach (Transform c in source.transform) children.Add(c);
                foreach (var c in children) Undo.SetTransformParent(c, arena.transform, "Merge zones");
                Undo.DestroyObjectImmediate(source);
            }

            // 3. Size the zone from the arena ground (the big outer yard). The visible
            //    ground must be solid, or actors stand on whatever hidden surface is below it.
            var grounds = GroundRenderers(arena);
            EnsureGroundColliders(grounds);
            var bounds = ArenaBounds(arena);
            Physics.SyncTransforms();
            ArenaGround.Ignore = zone.boundaryVisual;

            float groundY = grounds.Length > 0 ? grounds.Max(r => r.bounds.max.y) : EstimateGroundY(bounds);
            var center = new Vector3(bounds.center.x, groundY, bounds.center.z);
            float needed = 0.5f * Mathf.Sqrt(bounds.size.x * bounds.size.x + bounds.size.z * bounds.size.z);
            int steps = Mathf.Max(1, Mathf.CeilToInt((needed - zone.startRadius) / Mathf.Max(1f, zone.expandStep)));
            float maxRadius = zone.startRadius + steps * zone.expandStep;

            Undo.RecordObject(zone, "Merge zones");
            zone.center = center;
            zone.maxRadius = maxRadius;
            zone.deployPlayerAtCenter = true;
            EditorUtility.SetDirty(zone);

            if (zone.boundaryVisual != null)
            {
                Undo.RecordObject(zone.boundaryVisual, "Merge zones");
                var p = zone.boundaryVisual.position;
                zone.boundaryVisual.position = new Vector3(center.x, p.y, center.z);
                var s = zone.boundaryVisual.localScale;
                zone.boundaryVisual.localScale = new Vector3(zone.startRadius * 2f, s.y, zone.startRadius * 2f);
            }

            // 4. Player at the centre (nearest clear ground), spawn pads on the zone edge.
            string playerNote = "Player not found.";
            var player = Object.FindAnyObjectByType<PlayerHealth>(FindObjectsInactive.Include);
            if (player != null)
            {
                if (ArenaGround.TryFindClearSpot(center, groundY, zone.startRadius * 0.8f, out var spot))
                {
                    Undo.RecordObject(player.transform, "Merge zones");
                    player.transform.position = spot + Vector3.up * 0.05f;
                    playerNote = $"Player deployed {Vector3.Distance(Flat(spot), Flat(center)):0.0} m from the centre.";
                }
                else playerNote = "No clear ground near the centre - player left in place (it will retry at runtime).";
            }

            var waves = Object.FindAnyObjectByType<WaveManager>(FindObjectsInactive.Include);
            if (waves != null)
            {
                Undo.RecordObject(waves, "Merge zones");
                waves.spawnAtZoneEdge = true;
                EditorUtility.SetDirty(waves);
                if (waves.spawnPoints != null)
                {
                    foreach (var pad in waves.spawnPoints) if (pad != null) Undo.RecordObject(pad, "Merge zones");
                    waves.ArrangeSpawnPoints(center, zone.startRadius * waves.edgeRadiusFactor);
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(zone.gameObject.scene);
            Selection.activeGameObject = arena;

            string summary =
                $"Removed {removedFences} inner fence pieces; Zone A and Zone B are now one '{ArenaName}'.\n" +
                $"Arena {bounds.size.x:0} x {bounds.size.z:0} m, centre ({center.x:0.0}, {center.z:0.0}).\n" +
                $"Safe zone: starts {zone.startRadius:0} m, +{zone.expandStep:0} m per expansion ({zone.expandCost} coins), " +
                $"max {maxRadius:0} m after {steps} expansions.\n" +
                playerNote + "\n" +
                (waves != null ? "Spawn pads move to the zone edge before every wave." : "");
            Debug.Log("Zombie Rush: " + summary);
            EditorUtility.DisplayDialog("One big arena", summary + "\n\nSave the scene (Ctrl+S). Ctrl+Z undoes all of this.", "OK");
        }

        /// Zombie Rush > Level > Fix Ground Level
        /// The visible ground had no collider, so the player and zombies stood on a hidden
        /// surface ~1.2 m below it (zombies looked cut off at the knees). This makes the
        /// visible ground solid and snaps the player, spawn pads and placed zombies onto it.
        [MenuItem("Zombie Rush/Level/Fix Ground Level (sunken zombies)", priority = 21)]
        public static void FixGroundLevel()
        {
            var root = FindByName(ArenaName) ?? FindByName("Environment");
            var grounds = GroundRenderers(root);
            if (grounds.Length == 0)
            {
                EditorUtility.DisplayDialog("Fix Ground Level", "No ground object found (looking for objects with \"Ground\" in the name).", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Fix ground level");
            int undoGroup = Undo.GetCurrentGroup();

            int added = EnsureGroundColliders(grounds);
            Physics.SyncTransforms();
            float groundY = grounds.Max(r => r.bounds.max.y);

            var zone = Object.FindAnyObjectByType<ZoneManager>(FindObjectsInactive.Include);
            if (zone != null)
            {
                ArenaGround.Ignore = zone.boundaryVisual;
                Undo.RecordObject(zone, "Fix ground level");
                zone.center = new Vector3(zone.center.x, groundY, zone.center.z);
                EditorUtility.SetDirty(zone);
            }

            int snapped = 0;
            var player = Object.FindAnyObjectByType<PlayerHealth>(FindObjectsInactive.Include);
            if (player != null && ArenaGround.TryFindClearSpot(player.transform.position, groundY, 6f, out var spot))
            {
                Undo.RecordObject(player.transform, "Fix ground level");
                player.transform.position = spot + Vector3.up * 0.05f;
                snapped++;
            }

            var waves = Object.FindAnyObjectByType<WaveManager>(FindObjectsInactive.Include);
            if (waves != null && waves.spawnPoints != null)
                foreach (var pad in waves.spawnPoints)
                    if (pad != null && ArenaGround.TryGround(pad.position, groundY, out var g, 3f))
                    {
                        Undo.RecordObject(pad, "Fix ground level");
                        pad.position = g + Vector3.up * waves.padHeightOffset;
                        snapped++;
                    }

            foreach (var z in Object.FindObjectsByType<ZombieRush.Zombie.ZombieHealth>(FindObjectsInactive.Include))
                if (ArenaGround.TryGround(z.transform.position, groundY, out var g, 3f))
                {
                    Undo.RecordObject(z.transform, "Fix ground level");
                    z.transform.position = g;
                    snapped++;
                }

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(grounds[0].gameObject.scene);
            string msg = $"Ground top is y = {groundY:0.00}. Added {added} ground collider(s); moved {snapped} object(s) onto the ground.";
            Debug.Log("Zombie Rush: " + msg);
            EditorUtility.DisplayDialog("Ground fixed", msg + "\n\nSave the scene (Ctrl+S).", "OK");
        }

        static Renderer[] GroundRenderers(GameObject root)
        {
            var all = root != null ? root.GetComponentsInChildren<Renderer>(true) : Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include);
            return all.Where(r => r.name.ToLowerInvariant().Contains("ground") && !(r is ParticleSystemRenderer)).ToArray();
        }

        /// Adds a BoxCollider (auto-fitted to the mesh) to any ground without a collider.
        static int EnsureGroundColliders(Renderer[] grounds)
        {
            int added = 0;
            foreach (var r in grounds)
            {
                if (r.GetComponent<Collider>() != null) continue;
                Undo.AddComponent<BoxCollider>(r.gameObject);
                added++;
            }
            return added;
        }

        /// A fence piece from the Industrial pack (concrete fences, columns, gates...).
        static bool IsFencePiece(GameObject go)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(go);
            string path = source != null ? AssetDatabase.GetAssetPath(source) : "";
            string n = (path + "/" + go.name).ToLowerInvariant();
            return n.Contains("/fences/") || n.Contains("fence");
        }

        /// Footprint of the arena: its ground plane if there is one, else all its renderers.
        static Bounds ArenaBounds(GameObject arena)
        {
            var grounds = arena.GetComponentsInChildren<Renderer>(true).Where(r => r.name.ToLowerInvariant().Contains("ground")).ToArray();
            var source = grounds.Length > 0 ? grounds : arena.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToArray();
            var b = source[0].bounds;
            foreach (var r in source) b.Encapsulate(r.bounds);
            return b;
        }

        static float EstimateGroundY(Bounds b)
        {
            var heights = new List<float>();
            for (int ix = 0; ix <= 10; ix++)
                for (int iz = 0; iz <= 10; iz++)
                {
                    var origin = new Vector3(Mathf.Lerp(b.min.x, b.max.x, ix / 10f), b.max.y + 50f, Mathf.Lerp(b.min.z, b.max.z, iz / 10f));
                    var hits = Physics.RaycastAll(origin, Vector3.down, b.size.y + 100f, ~0, QueryTriggerInteraction.Ignore);
                    float lowest = hits.Where(h => h.normal.y > 0.9f && (ArenaGround.Ignore == null || !h.collider.transform.IsChildOf(ArenaGround.Ignore)))
                        .Select(h => h.point.y).DefaultIfEmpty(float.NaN).Min();
                    if (!float.IsNaN(lowest)) heights.Add(lowest);
                }
            if (heights.Count == 0) return b.max.y;
            heights.Sort();
            return heights[heights.Count / 2]; // median of per-sample lowest surfaces
        }

        static GameObject FindByName(string name) =>
            Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Select(t => t.gameObject).FirstOrDefault(g => g.name == name);

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
