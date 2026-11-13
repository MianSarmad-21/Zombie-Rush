using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ZombieRush.EditorTools
{
    /// Ground clutter (bricks, slabs, sheets, small rubble) inside the playable area of the open scene.
    ///   Zombie Rush > Level > Survey Ground Clutter: report only (Temp/ZR_Clutter.log).
    ///   Zombie Rush > Level > Clear Ground Clutter: removes every other small rubble/panel painted on the
    ///   terrain in the play area (halves the junk, keeps big mounds and walls) and re-bakes zombie paths.
    ///   Undoable until the scene is saved.
    /// Marker: Temp/ZR_RunClutter, text inside "survey" or "clear".
    static class GroundClutterTools
    {
        const float AreaRadius = 345f;      // largest fight zone (see level layout)
        const float MaxHeight = 0.9f;       // taller than this is cover, not clutter
        const float MaxFootprint = 4f;      // wider than this is a slab or ruin, not clutter
        const float KeepEvery = 2;          // clear keeps one piece in this many
        // Terrain-painted ground junk that "Clear" thins out (rubble3 mounds and broken walls stay as cover).
        static readonly string[] ThinKinds = { "rubble1", "rubble2", "panel1", "panel2" };

        /// "rubble1 (12)" -> "rubble1".
        static string BaseName(string n)
        {
            int k = n.IndexOf(" (");
            return k > 0 ? n.Substring(0, k) : n;
        }


        [MenuItem("Zombie Rush/Level/Survey Ground Clutter")]
        static void SurveyMenu() => Debug.Log("Zombie Rush: " + Run(false));

        [MenuItem("Zombie Rush/Level/Clear Ground Clutter")]
        static void ClearMenu() => Debug.Log("Zombie Rush: " + Run(true));

        public static string Run(bool clear)
        {
            var player = Object.FindAnyObjectByType<ZombieRush.Player.PlayerController>(FindObjectsInactive.Include);
            if (player == null) return "FAILED: no Player in the open scene.";
            Vector3 centre = player.transform.position;
            var cc = player.GetComponent<CharacterController>();
            float step = cc != null ? cc.stepOffset : 0.35f;

            // Only the city pack's own pieces (under the "Map" object), never gameplay objects.
            var map = GameObject.Find("Map");
            if (map == null) return "FAILED: no 'Map' object in the open scene.";
            var pieces = new List<GameObject>();
            foreach (var r in map.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (r.GetComponent<MonoBehaviour>() != null) continue;
                if (r.GetComponentInParent<Canvas>() != null) continue;
                var b = r.bounds;
                Vector2 flat = new Vector2(b.center.x - centre.x, b.center.z - centre.z);
                if (flat.magnitude > AreaRadius) continue;
                if (b.size.y > MaxHeight || Mathf.Max(b.size.x, b.size.z) > MaxFootprint) continue;
                if (b.min.y > centre.y + 2f) continue;              // up on a building, not on the street
                if (r.transform.childCount > 0) continue;            // only leaf pieces
                if (r.gameObject.isStatic && PrefabUtility.IsPartOfPrefabAsset(r)) continue;
                pieces.Add(r.gameObject);
            }

            var report = new StringBuilder();
            int withCollider = pieces.Count(g => g.GetComponent<Collider>() != null);
            int blocking = pieces.Count(g => g.GetComponent<Collider>() != null && g.GetComponent<Renderer>().bounds.size.y > step);
            report.AppendLine($"Ground clutter within {AreaRadius} m: {pieces.Count} pieces, {withCollider} with colliders, {blocking} taller than the player's step ({step} m) with colliders.");
            foreach (var grp in pieces.GroupBy(g => MeshName(g)).OrderByDescending(x => x.Count()).Take(15))
                report.AppendLine($"  {grp.Key}: {grp.Count()} (colliders {grp.Count(g => g.GetComponent<Collider>() != null)})");

            if (!clear)
            {
                // Terrain details and trees (painted, not GameObjects).
                foreach (var terrain in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude))
                {
                    var td = terrain.terrainData;
                    report.AppendLine($"Terrain '{terrain.name}': {td.detailPrototypes.Length} detail kinds, {td.treeInstanceCount} trees, draw details {terrain.drawTreesAndFoliage}.");
                    for (int d = 0; d < td.detailPrototypes.Length; d++)
                    {
                        var proto = td.detailPrototypes[d];
                        long count = 0;
                        var layer = td.GetDetailLayer(0, 0, td.detailWidth, td.detailHeight, d);
                        foreach (var v in layer) count += v;
                        report.AppendLine($"  detail {d}: {(proto.prototype != null ? proto.prototype.name : proto.prototypeTexture != null ? proto.prototypeTexture.name : "?")} ({(proto.usePrototypeMesh ? "mesh" : "grass")}), {count} placed");
                    }
                    foreach (var tp in td.treePrototypes) report.AppendLine($"  tree kind: {(tp.prefab != null ? tp.prefab.name : "?")}");
                }
                // Every mesh in the city pack within 60 m, by mesh name (any size).
                report.AppendLine("City pack meshes within 60 m, by mesh:");
                foreach (var grp in map.GetComponentsInChildren<MeshFilter>(false)
                    .Where(f => f.sharedMesh != null && Vector2.Distance(new Vector2(f.transform.position.x, f.transform.position.z), new Vector2(centre.x, centre.z)) < 60f)
                    .GroupBy(f => f.sharedMesh.name).OrderByDescending(g => g.Count()).Take(25))
                    report.AppendLine($"  {grp.Key}: {grp.Count()}");

                // Everything solid the player can bump into near the start zone, by top-level object.
                report.AppendLine("Solid colliders within 60 m of the player, by root object / name:");
                var near = Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude)
                    .Where(c => !c.isTrigger && !c.transform.IsChildOf(player.transform))
                    .Where(c => Vector2.Distance(new Vector2(c.bounds.center.x, c.bounds.center.z), new Vector2(centre.x, centre.z)) < 60f)
                    .ToList();
                foreach (var grp in near.GroupBy(c => c.transform.root.name).OrderByDescending(g => g.Count()))
                {
                    report.AppendLine($" [{grp.Key}] {grp.Count()} colliders");
                    foreach (var sub in grp.GroupBy(c => $"{c.GetType().Name} {MeshName(c.gameObject)}").OrderByDescending(s => s.Count()).Take(12))
                    {
                        var b = sub.First().bounds;
                        report.AppendLine($"    {sub.Key}: {sub.Count()}  (e.g. {b.size.x:0.0}x{b.size.y:0.0}x{b.size.z:0.0} m, top {b.max.y - centre.y:0.0} m above player feet)");
                    }
                }
                return report.ToString();
            }

            // Clear: the junk on the streets is painted on the terrain as "trees". Remove every other
            // small piece (ThinKinds) inside the play area; big mounds and walls stay as cover.
            int deleted = 0;
            foreach (var terrain in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude))
            {
                var td = terrain.terrainData;
                Undo.RegisterCompleteObjectUndo(td, "Clear Ground Clutter");
                var protos = td.treePrototypes;
                var seen = new Dictionary<string, int>();
                var removed = new Dictionary<string, int>();
                var keep = new List<TreeInstance>();
                foreach (var inst in td.treeInstances)
                {
                    var prefab = inst.prototypeIndex < protos.Length ? protos[inst.prototypeIndex].prefab : null;
                    string kind = prefab != null ? prefab.name : "?";
                    Vector3 world = Vector3.Scale(inst.position, td.size) + terrain.transform.position;
                    bool inArea = Vector2.Distance(new Vector2(world.x, world.z), new Vector2(centre.x, centre.z)) < AreaRadius;
                    if (inArea && ThinKinds.Contains(kind))
                    {
                        seen[kind] = seen.TryGetValue(kind, out var s) ? s + 1 : 1;
                        if (seen[kind] % (int)KeepEvery == 0)
                        {
                            removed[kind] = removed.TryGetValue(kind, out var r) ? r + 1 : 1;
                            deleted++;
                            continue;
                        }
                    }
                    keep.Add(inst);
                }
                td.SetTreeInstances(keep.ToArray(), true);
                EditorUtility.SetDirty(td);
                terrain.Flush();
                foreach (var kv in seen)
                    report.AppendLine($"  terrain {kv.Key}: {kv.Value} in the play area, removed {(removed.TryGetValue(kv.Key, out var r2) ? r2 : 0)}.");
            }
            AssetDatabase.SaveAssets();

            // Zombie paths: re-bake so they use the freed ground.
            var zone = Object.FindAnyObjectByType<ZombieRush.World.ZoneManager>(FindObjectsInactive.Include);
            if (zone != null)
            {
                // Centre on the base, like Base At City Centre does: the zone object itself sits at the world origin.
                Vector3 navCentre = zone.playerBase != null ? zone.playerBase.position : zone.center;
                var nav = ZoneTools.BakeNavMesh(map, navCentre, zone.maxRadius + 15f, out string navNote);
                var loader = zone.GetComponent<ZombieRush.World.ArenaNavMesh>();
                if (nav != null && loader != null)
                {
                    Undo.RecordObject(loader, "Clear Ground Clutter");
                    loader.data = nav;
                    EditorUtility.SetDirty(loader);
                }
                report.AppendLine("Zombie paths: " + (nav != null ? "re-baked. " : "NOT re-baked. ") + navNote);
            }
            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
            EditorSceneManager.SaveScene(player.gameObject.scene);
            report.AppendLine($"Cleared: removed {deleted} painted debris pieces. Scene saved.");
            return report.ToString();
        }

        static string MeshName(GameObject g)
        {
            var mf = g.GetComponent<MeshFilter>();
            return mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : g.name;
        }
    }
}
