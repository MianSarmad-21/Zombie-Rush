using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using ZombieRush.World;

namespace ZombieRush.EditorTools
{
    /// Map clean-up: the city pack's rubble on streets and open ground.
    ///   Survey (report only): what the debris is made of, where it sits (open street or against a
    ///   building), and how the Map object is built. Report: Temp/ZR_MapCleanup.log.
    ///   Clear: removes loose junk and painted debris on open ground in the play area, keeps what
    ///   leans on buildings, re-bakes zombie paths, saves the scene.
    ///   Views: before/after pictures to Docs/Map/Cleanup/.
    /// Marker: Temp/ZR_RunMapCleanup, text inside "survey", "clear" or "views <Tag>".
    static class MapCleanupTools
    {
        const string MapRoot = "Map";
        const float NearBuilding = 4f;   // metres from a building footprint that still counts as "against it"

        [MenuItem("Zombie Rush/Level/Survey Map Debris")]
        static void SurveyMenu() => Debug.Log("Zombie Rush: " + Run("survey"));

        // ---------- clear streets: what goes

        /// Loose junk (Map children): removed when it does not touch a building.
        static readonly string[] LooseJunk =
        {
            "rubble1", "rubble2", "rubble3", "cloth1", "cloth2", "cloth3", "cloth4", "cloth5",
            "panel1", "panel2", "panel3", "panel4", "panel5", "panel6", "concrete2", "concrete3",
            "roof1", "broken-roof", "brick1", "beam", "beam1", "steel-beam1", "steel-beam2",
            "metal-bars", "metal-structure1", "metal-structure2", "broken-wall3", "broken-wall7", "broken-wall11",
        };
        /// Painted on the terrain: always removed in the play area (flat sheets and small heaps).
        static readonly string[] PaintedAlways = { "panel1", "panel2", "rubble1", "rubble2" };
        /// Painted on the terrain: removed only when it does not touch a building.
        static readonly string[] PaintedIfOpen = { "rubble3", "broken-wall5" };
        const float TouchMargin = 1.0f;  // metres: closer than this to a building counts as "against it"
        const float BaseClearRadius = 60f; // metres around the safe base where all loose junk goes, touching or not

        [MenuItem("Zombie Rush/Level/Clear Street Debris")]
        static void ClearMenu() => Debug.Log("Zombie Rush: " + Run("clear"));

        public static string Run(string mode)
        {
            if (mode.StartsWith("propviews")) return PropViews();
            if (mode.StartsWith("views")) return Views(mode.Length > 5 ? mode.Substring(5).Trim() : "Before");
            if (mode.StartsWith("clear")) return Clear();
            if (mode.StartsWith("buildings")) return SwapCleanBuildings();
            if (mode.StartsWith("probe")) return Probe(mode);
            if (mode == "navcheck") return NavCheck();
            if (mode == "nightlook") return NightLookTools.Apply();
            if (mode == "mobileforwardplus") return NightLookTools.MobileForwardPlus();
            if (mode == "survival") return SurvivalLayoutTools.Build();
            if (mode == "guns") return GunSetupTools.Run();
            if (mode == "balance") return WaveBalance();
            if (mode == "fx") return ZombieRush.EditorTools.FxSetupTools.Run();
            if (mode == "net") return ZombieRush.EditorTools.NetSetupTools.Run();
            if (mode == "kitui") return ZombieRush.EditorTools.KitUIBuilder.Run();
            if (mode == "buildwin") return ZombieRush.EditorTools.BuildTools.Windows();
            if (mode == "buildandroid") return ZombieRush.EditorTools.BuildTools.Android();
            if (mode == "dumpplayer")
            {
                var pl = GameObject.Find("Player");
                if (pl == null) return "no Player";
                var sbd = new System.Text.StringBuilder();
                void Walk(Transform t, int d)
                {
                    var comps = t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name);
                    sbd.AppendLine($"{new string(' ', d * 2)}{t.name}{(t.gameObject.activeSelf ? "" : " (off)")} [{string.Join(", ", comps)}]");
                    if (d < 4) foreach (Transform c in t) Walk(c, d + 1);
                }
                Walk(pl.transform, 0);
                return sbd.ToString();
            }
            if (mode == "bodycheck") return GunSetupTools.BodyCheck();
            if (mode == "rigs")
            {
                string P(Component c) { if (c == null) return "null"; var t = c.transform; string s = t.name; while (t.parent != null) { t = t.parent; s = t.name + "/" + s; } return s + (c.gameObject.activeInHierarchy ? "" : " (inactive)"); }
                var sbr = new System.Text.StringBuilder();
                foreach (var r in Object.FindObjectsByType<ZombieRush.Player.WeaponAimRig>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    sbr.AppendLine($"aim rig {P(r)} enabled {r.enabled} hold {P(r.hold)} config {(r.config != null ? r.config.name : "null")} animator ctrl {(r.GetComponent<Animator>()?.runtimeAnimatorController?.name ?? "null")}");
                foreach (var l in Object.FindObjectsByType<ZombieRush.Player.PlayerBodyLink>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    sbr.AppendLine($"body link {P(l)} aimRig {P(l.aimRig)} bodyAnimator {P(l.bodyAnimator)} viewMode {P(l.viewMode)}");
                foreach (var v in Object.FindObjectsByType<ZombieRush.Player.CharacterViewMode>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    sbr.AppendLine($"view mode {P(v)} firstPersonOnly [{string.Join(", ", (v.firstPersonOnly ?? new Renderer[0]).Select(r => r == null ? "null" : $"{P(r)} mesh {((r as SkinnedMeshRenderer)?.sharedMesh != null ? (r as SkinnedMeshRenderer).sharedMesh.name : "NULL")}"))}]");
                foreach (var g in Object.FindObjectsByType<ZombieRush.Player.BodyGunPose>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    sbr.AppendLine($"body gun pose {P(g)} aimRig {P(g.aimRig)}");
                foreach (var a in Object.FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (a.isHuman) sbr.AppendLine($"humanoid animator {P(a)} ctrl {(a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name : "null")} avatar {(a.avatar != null ? a.avatar.name : "null")}");
                return sbr.ToString();
            }
            if (mode.StartsWith("eye ")) return GunSetupTools.SetEye(mode.Substring(4).Trim());
            if (mode.StartsWith("lift ")) return GunSetupTools.SetLift(mode.Substring(5).Trim());
            if (mode.StartsWith("gundump ")) return GunSetupTools.Dump(mode.Substring(8).Trim());
            if (mode == "gunctl") return string.Join(System.Environment.NewLine, Object.FindObjectsByType<ZombieRush.Weapons.GunRig>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Select(r => { var an = r.GetComponent<Animator>(); return $"{r.name} under {r.transform.parent?.parent?.name}: ctrl {(an != null && an.runtimeAnimatorController != null ? an.runtimeAnimatorController.name : "NULL")}, prefab ctrl {(PrefabUtility.GetCorrespondingObjectFromSource(an) is Animator pa && pa.runtimeAnimatorController != null ? pa.runtimeAnimatorController.name : "NULL")}"; }));
            if (mode == "floatcheck") return FloatCheck();
            if (mode == "mossbuildings") return MossyBuildingsTools.Run(true);
            if (mode == "mossbuildings off") return MossyBuildingsTools.Run(false);
            if (mode == "survival config reset") return SurvivalLayoutTools.ResetConfig();
            if (mode.StartsWith("restorescene ")) return RestoreScene(mode.Substring(13).Trim());
            if (mode == "props build") return CityPropsTools.BuildPrefabs();
            if (mode == "props place") return CityPropsTools.Place();
            if (mode.StartsWith("mounds")) return RemoveNearMounds();
            var zone = Object.FindAnyObjectByType<ZoneManager>(FindObjectsInactive.Include);
            var map = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(t => t.parent == null && t.name == MapRoot);
            if (zone == null || map == null) return "FAILED: open MainLevel first (needs ZoneManager and a root object named Map).";
            Vector3 centre = zone.ArcCenter;
            float area = zone.maxRadius + 20f;

            var sb = new StringBuilder();
            sb.AppendLine($"Play area: centre ({centre.x:0}, {centre.z:0}), radius {area:0} m.");

            // ---- how Map is built
            sb.AppendLine("Map children:");
            foreach (Transform c in map)
                sb.AppendLine($"  {c.name}: {c.childCount} children, {c.GetComponentsInChildren<Renderer>(true).Length} renderers{(c.gameObject.activeSelf ? "" : " (off)")}");
            var containers = map.Cast<Transform>().Where(c => c.childCount > 12).ToList();
            foreach (var c in containers)
            {
                sb.AppendLine($"  [{c.name}] child kinds:");
                foreach (var g in c.Cast<Transform>().GroupBy(t => BaseName(SourceName(t))).OrderByDescending(g => g.Count()).Take(40))
                    sb.AppendLine($"    {g.Key}: {g.Count()}");
            }

            // ---- pieces: outermost prefab roots (else leaf renderers) under Map
            var pieces = Pieces(map);
            var buildings = pieces.Where(p => IsBuilding(p)).ToList();
            var footprints = buildings.Select(p => p.bounds).ToList();
            sb.AppendLine($"Pieces under Map: {pieces.Count}, of which buildings: {buildings.Count}.");

            sb.AppendLine($"Non-building pieces inside the play area, by kind (open = more than {NearBuilding} m from any building):");
            sb.AppendLine("  kind: total | open | against building | with collider | typical size");
            var inArea = pieces.Where(p => !IsBuilding(p) && Flat(p.bounds.center - centre) < area).ToList();
            foreach (var g in inArea.GroupBy(p => p.kind).OrderByDescending(g => g.Count()))
            {
                int open = g.Count(p => !NearAny(p.bounds, footprints));
                int col = g.Count(p => p.t.GetComponentInChildren<Collider>() != null);
                var s = g.Select(p => p.bounds.size).OrderBy(v => v.magnitude).ElementAt(g.Count() / 2);
                sb.AppendLine($"  {g.Key}: {g.Count()} | {open} | {g.Count() - open} | {col} | {s.x:0.0}x{s.y:0.0}x{s.z:0.0}");
            }

            // ---- what a building is made of (children of a few building prefabs)
            sb.AppendLine("Inside buildings (child kinds, first 3 different building types):");
            foreach (var b in buildings.GroupBy(p => p.kind).Take(3).Select(g => g.First()))
            {
                sb.AppendLine($"  {b.kind} ({b.bounds.size.x:0}x{b.bounds.size.y:0}x{b.bounds.size.z:0}):");
                foreach (var g in b.t.GetComponentsInChildren<Renderer>(true).GroupBy(r => MeshName(r.gameObject)).Take(20))
                    sb.AppendLine($"    {g.Key}: {g.Count()}");
            }

            // ---- terrain-painted pieces (trees)
            foreach (var terrain in map.GetComponentsInChildren<Terrain>(true))
            {
                var td = terrain.terrainData;
                var protos = td.treePrototypes;
                sb.AppendLine($"Terrain '{terrain.name}': {td.treeInstanceCount} painted pieces, {protos.Length} kinds, tree colliders {(terrain.GetComponent<TerrainCollider>() != null)}.");
                sb.AppendLine("  kind: total | in play area | open in play area");
                var byKind = new Dictionary<string, int[]>();
                foreach (var inst in td.treeInstances)
                {
                    string kind = inst.prototypeIndex < protos.Length && protos[inst.prototypeIndex].prefab != null ? protos[inst.prototypeIndex].prefab.name : "?";
                    Vector3 w = Vector3.Scale(inst.position, td.size) + terrain.transform.position;
                    if (!byKind.TryGetValue(kind, out var c)) byKind[kind] = c = new int[3];
                    c[0]++;
                    if (Flat(w - centre) < area)
                    {
                        c[1]++;
                        if (!NearAny(new Bounds(w, Vector3.one), footprints)) c[2]++;
                    }
                }
                foreach (var kv in byKind.OrderByDescending(k => k.Value[1]))
                    sb.AppendLine($"  {kv.Key}: {kv.Value[0]} | {kv.Value[1]} | {kv.Value[2]}");
                sb.AppendLine($"  detail kinds: {td.detailPrototypes.Length}");
            }
            return sb.ToString();
        }

        // ---------- clear

        /// Removes the loose junk and painted debris on open ground inside the play area, keeps what
        /// leans on buildings, re-bakes zombie paths and saves the scene. Undoable until saved; the
        /// files from before are in Backups/.
        static string Clear()
        {
            var zone = Object.FindAnyObjectByType<ZoneManager>(FindObjectsInactive.Include);
            var mapT = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(t => t.parent == null && t.name == MapRoot);
            if (zone == null || mapT == null) return "FAILED: open MainLevel first.";
            Vector3 centre = zone.ArcCenter;
            float area = zone.maxRadius + 20f;
            Physics.SyncTransforms();

            // Building colliders: everything under a Map child whose kind is a building.
            var buildingCols = new HashSet<Collider>();
            foreach (Transform c in mapT)
            {
                string k = BaseName(c.name);
                if (k.StartsWith("building") && !k.Contains("rubble"))
                    foreach (var col in c.GetComponentsInChildren<Collider>(true)) buildingCols.Add(col);
            }
            bool Touches(Vector3 centreW, Vector3 half)
            {
                foreach (var h in Physics.OverlapBox(centreW, half + Vector3.one * TouchMargin, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                    if (buildingCols.Contains(h)) return true;
                return false;
            }

            var sb = new StringBuilder();
            Undo.SetCurrentGroupName("Clear street debris");
            int undo = Undo.GetCurrentGroup();

            // 1. loose junk objects
            var removed = new Dictionary<string, int>();
            var kept = new Dictionary<string, int>();
            foreach (var c in mapT.Cast<Transform>().ToList())
            {
                string k = BaseName(c.name);
                if (!LooseJunk.Contains(k) || c.childCount > 4) continue;
                var rs = c.GetComponentsInChildren<Renderer>(true);
                if (rs.Length == 0) continue;
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                float dist = Flat(b.center - centre);
                if (dist > area) continue;
                if (dist > BaseClearRadius && c.gameObject.activeSelf && Touches(b.center, b.extents)) { kept[k] = kept.TryGetValue(k, out var n) ? n + 1 : 1; continue; }
                removed[k] = removed.TryGetValue(k, out var m) ? m + 1 : 1;
                Undo.DestroyObjectImmediate(c.gameObject);
            }
            sb.AppendLine($"Loose junk: removed {removed.Values.Sum()}, kept {kept.Values.Sum()} that lean on buildings.");
            foreach (var kv in removed.OrderByDescending(x => x.Value))
                sb.AppendLine($"  {kv.Key}: removed {kv.Value}, kept {(kept.TryGetValue(kv.Key, out var kk) ? kk : 0)}");

            // 2. painted debris on the terrain
            foreach (var terrain in mapT.GetComponentsInChildren<Terrain>(true))
            {
                var td = terrain.terrainData;
                Undo.RegisterCompleteObjectUndo(td, "Clear street debris");
                var protos = td.treePrototypes;
                var keep = new List<TreeInstance>();
                var gone = new Dictionary<string, int>();
                foreach (var inst in td.treeInstances)
                {
                    string kind = inst.prototypeIndex < protos.Length && protos[inst.prototypeIndex].prefab != null ? protos[inst.prototypeIndex].prefab.name : "?";
                    Vector3 w = Vector3.Scale(inst.position, td.size) + terrain.transform.position;
                    bool inArea = Flat(w - centre) < area;
                    bool drop = inArea && (PaintedAlways.Contains(kind) ||
                                           (PaintedIfOpen.Contains(kind) && !Touches(w + Vector3.up, new Vector3(2f, 1f, 2f) * inst.widthScale)));
                    if (drop) { gone[kind] = gone.TryGetValue(kind, out var g) ? g + 1 : 1; continue; }
                    keep.Add(inst);
                }
                td.SetTreeInstances(keep.ToArray(), true);
                EditorUtility.SetDirty(td);
                terrain.Flush();
                sb.AppendLine($"Terrain '{terrain.name}': removed {gone.Values.Sum()} painted pieces, {keep.Count} left.");
                foreach (var kv in gone.OrderByDescending(x => x.Value)) sb.AppendLine($"  {kv.Key}: {kv.Value}");
            }
            Undo.CollapseUndoOperations(undo);
            AssetDatabase.SaveAssets();

            // 3. zombie paths over the freed ground
            var nav = ZoneTools.BakeNavMesh(mapT.gameObject, NavCentre(zone), zone.maxRadius + 15f, out string navNote);
            var loader = zone.GetComponent<ArenaNavMesh>();
            if (nav != null && loader != null)
            {
                Undo.RecordObject(loader, "Clear street debris");
                loader.data = nav;
                EditorUtility.SetDirty(loader);
            }
            sb.AppendLine("Zombie paths: " + (nav != null ? "re-baked. " : "NOT re-baked. ") + navNote);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(zone.gameObject.scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(zone.gameObject.scene);
            sb.AppendLine("Scene saved. Backup of the files from before: Backups/2026-11-09_BeforeMapCleanup/.");
            return sb.ToString();
        }

        // ---------- cleaned building meshes (made in Blender, Blender/clean_buildings.py)

        const string PackModels = "Assets/PostApocalypticBuildings/Models";
        const string CleanModels = "Assets/Models/2026-11-09_CleanBuildings";
        const float BoundsSlack = 0.6f;  // m: a cleaned mesh may stick out this far past its original (sunk mound)

        [MenuItem("Zombie Rush/Level/Use Cleaned Building Meshes")]
        static void SwapMenu() => Debug.Log("Zombie Rush: " + SwapCleanBuildings());

        /// Points every MeshFilter and MeshCollider under Map that uses a pack building mesh to its
        /// cleaned copy, after checking the copy lines up with the original.
        internal static string SwapCleanBuildings(bool bake = true)
        {
            var mapT = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(t => t.parent == null && t.name == MapRoot);
            if (mapT == null) return "FAILED: no Map in the open scene.";
            var sb = new StringBuilder();
            var pairs = new Dictionary<Mesh, Mesh>();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { CleanModels }))
            {
                string cleanPath = AssetDatabase.GUIDToAssetPath(guid);
                string name = System.IO.Path.GetFileNameWithoutExtension(cleanPath).Replace("_clean", "");
                string origPath = $"{PackModels}/{name}.fbx";
                // no materials from the cleaned file: the scene keeps the pack's own
                var imp = (ModelImporter)AssetImporter.GetAtPath(cleanPath);
                var origImp = AssetImporter.GetAtPath(origPath) as ModelImporter;
                if (imp != null && (imp.materialImportMode != ModelImporterMaterialImportMode.None || (origImp != null && imp.isReadable != origImp.isReadable)))
                {
                    imp.materialImportMode = ModelImporterMaterialImportMode.None;
                    if (origImp != null) { imp.isReadable = origImp.isReadable; imp.meshCompression = origImp.meshCompression; }
                    imp.SaveAndReimport();
                }
                var orig = AssetDatabase.LoadAllAssetsAtPath(origPath).OfType<Mesh>().FirstOrDefault();
                var clean = AssetDatabase.LoadAllAssetsAtPath(cleanPath).OfType<Mesh>().FirstOrDefault();
                if (orig == null || clean == null) { sb.AppendLine($"  {name}: SKIPPED, mesh missing"); continue; }
                // Blender may write a different file unit (cm vs m): match by a whole power of ten on the height (Y)
                {
                    const int upAxis = 1;  // Unity mesh space is Y-up
                    float ratio = orig.bounds.size[upAxis] / Mathf.Max(1e-6f, clean.bounds.size[upAxis]);
                    float pow10 = Mathf.Pow(10f, Mathf.Round(Mathf.Log10(ratio)));
                    if (imp != null && Mathf.Abs(pow10 - 1f) > 0.01f)
                    {
                        imp.globalScale *= pow10;
                        imp.SaveAndReimport();
                        clean = AssetDatabase.LoadAllAssetsAtPath(cleanPath).OfType<Mesh>().FirstOrDefault();
                    }
                }
                var ob = orig.bounds; var cb = clean.bounds;
                bool inside = cb.min.x >= ob.min.x - BoundsSlack && cb.min.y >= ob.min.y - BoundsSlack && cb.min.z >= ob.min.z - BoundsSlack &&
                              cb.max.x <= ob.max.x + BoundsSlack && cb.max.y <= ob.max.y + BoundsSlack && cb.max.z <= ob.max.z + BoundsSlack;
                // the top must not move: walls are never cut (only rubble at ground level goes). Y is up.
                bool sameHeight = Mathf.Abs(ob.max.y - cb.max.y) < 0.5f;
                if (!inside || !sameHeight || orig.subMeshCount != clean.subMeshCount)
                {
                    sb.AppendLine($"  {name}: SKIPPED, does not line up (orig {ob.center}/{ob.size}, clean {cb.center}/{cb.size}, submeshes {orig.subMeshCount}/{clean.subMeshCount})");
                    continue;
                }
                pairs[orig] = clean;
            }
            Undo.SetCurrentGroupName("Use cleaned building meshes");
            int undo = Undo.GetCurrentGroup();
            var used = new Dictionary<string, int>();
            int filters = 0, colliders = 0;
            foreach (var mf in mapT.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null && pairs.TryGetValue(mf.sharedMesh, out var c))
                {
                    Undo.RecordObject(mf, "Use cleaned building meshes");
                    used[c.name] = used.TryGetValue(c.name, out var n) ? n + 1 : 1;
                    mf.sharedMesh = c; filters++;
                }
            foreach (var mc in mapT.GetComponentsInChildren<MeshCollider>(true))
                if (mc.sharedMesh != null && pairs.TryGetValue(mc.sharedMesh, out var c))
                {
                    Undo.RecordObject(mc, "Use cleaned building meshes");
                    mc.sharedMesh = c; colliders++;
                }
            Undo.CollapseUndoOperations(undo);
            sb.Insert(0, $"Cleaned meshes lined up: {pairs.Count}. Swapped {filters} meshes and {colliders} colliders.\n");
            foreach (var kv in used.OrderBy(k => k.Key)) sb.AppendLine($"  {kv.Key}: {kv.Value}");
            return sb.ToString() + (bake ? RebakeAndSave(mapT) : "");
        }

        /// "probe x z r": everything (renderers and painted terrain pieces) within r metres of (x, z).
        static string Probe(string mode)
        {
            var a = mode.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            float x = float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture);
            float z = float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture);
            float r = a.Length > 3 ? float.Parse(a[3], System.Globalization.CultureInfo.InvariantCulture) : 5f;
            var p = new Vector3(x, 0f, z);
            var sb = new StringBuilder($"Within {r} m of ({x}, {z}):\n");
            foreach (var rend in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var b = rend.bounds;
                float dx = Mathf.Max(0f, Mathf.Max(b.min.x - x, x - b.max.x)), dz = Mathf.Max(0f, Mathf.Max(b.min.z - z, z - b.max.z));
                if (dx * dx + dz * dz > r * r || b.size.magnitude > 300f) continue;
                string path = rend.name; for (var t = rend.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
                sb.AppendLine($"  {path}  size {b.size.x:0.0}x{b.size.y:0.0}x{b.size.z:0.0} centre ({b.center.x:0.0}, {b.center.y:0.0}, {b.center.z:0.0})");
            }
            foreach (var terrain in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var td = terrain.terrainData;
                foreach (var inst in td.treeInstances)
                {
                    Vector3 w = Vector3.Scale(inst.position, td.size) + terrain.transform.position;
                    if (Flat(w - p) < r) sb.AppendLine($"  painted {td.treePrototypes[inst.prototypeIndex].prefab?.name} at ({w.x:0.0}, {w.z:0.0})");
                }
            }
            return sb.ToString();
        }

        // ---------- giant rubble mounds

        static readonly string[] MoundKinds = { "building8-rubble", "building8-rubble2", "broken-metal-structure" };
        const float MoundShareRemoved = 0.5f;  // the nearest half to the safe base goes

        [MenuItem("Zombie Rush/Level/Remove Near Rubble Mounds")]
        static void MoundMenu() => Debug.Log("Zombie Rush: " + RemoveNearMounds());

        /// Removes the giant collapsed-building mounds and twisted steel frames nearest the safe base
        /// (the play area's inner streets); the outer ones stay as a ruined skyline.
        static string RemoveNearMounds()
        {
            var zone = Object.FindAnyObjectByType<ZoneManager>(FindObjectsInactive.Include);
            var mapT = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(t => t.parent == null && t.name == MapRoot);
            if (zone == null || mapT == null) return "FAILED: open MainLevel first.";
            Vector3 centre = zone.ArcCenter;
            float area = zone.maxRadius + 20f;
            var sb = new StringBuilder();
            Undo.SetCurrentGroupName("Remove near rubble mounds");
            int undo = Undo.GetCurrentGroup();
            foreach (var kind in MoundKinds)
            {
                var list = mapT.Cast<Transform>().Where(t => BaseName(t.name) == kind)
                    .Select(t => (t, d: Flat(Centre(t) - centre))).Where(x => x.d < area).OrderBy(x => x.d).ToList();
                int remove = Mathf.RoundToInt(list.Count * MoundShareRemoved);
                foreach (var x in list.Take(remove)) Undo.DestroyObjectImmediate(x.t.gameObject);
                sb.AppendLine($"  {kind}: {list.Count} in the play area, removed the {remove} nearest (out to {(remove > 0 ? list[remove - 1].d : 0):0} m).");
            }
            Undo.CollapseUndoOperations(undo);
            return "Rubble mounds:\n" + sb + RebakeAndSave(mapT);
        }

        static Vector3 Centre(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return t.position;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b.center;
        }

        /// Zombie paths over the changed ground, then save.
        /// Puts a backup copy of the open scene back and reopens it (no save of the current state).
        static string RestoreScene(string backup)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (!System.IO.File.Exists(backup)) return "FAILED: no file " + backup;
            string path = scene.path;
            System.IO.File.Copy(backup, path, true);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Single);
            return $"Restored {path} from {backup} and reopened it.";
        }

        /// Every placed prop (CityProps, BaseCompound, SurvivalLayout): how far its base sits above the
        /// terrain right under it and under the corners of its footprint. Lists the worst by kind.
        static string FloatCheck()
        {
            var map = GameObject.Find(MapRoot);
            var terrain = map != null ? map.GetComponentInChildren<Terrain>() : null;
            if (terrain == null) return "FAILED: no terrain.";
            var rows = new List<(string kind, float gap, float worstCorner, Vector3 at)>();
            foreach (var rootName in new[] { "CityProps", "BaseCompound", "SurvivalLayout" })
            {
                var root = map.transform.Find(rootName);
                if (root == null) continue;
                foreach (Transform grp in root)
                    foreach (Transform t in grp)
                    {
                        var rs = t.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer)).ToArray();
                        if (rs.Length == 0) continue;
                        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                        float h = terrain.SampleHeight(t.position) + terrain.transform.position.y;
                        float gap = b.min.y - h;
                        float worst = 0f;
                        foreach (var c in new[] { new Vector3(b.min.x, 0, b.min.z), new Vector3(b.max.x, 0, b.min.z), new Vector3(b.min.x, 0, b.max.z), new Vector3(b.max.x, 0, b.max.z) })
                            worst = Mathf.Max(worst, b.min.y - (terrain.SampleHeight(c) + terrain.transform.position.y));
                        rows.Add((BaseName(t.name), gap, worst, t.position));
                    }
            }
            var sb = new StringBuilder();
            sb.AppendLine($"Props checked: {rows.Count}. Base above terrain under the pivot / under the lowest footprint corner:");
            foreach (var g in rows.GroupBy(r => r.kind).OrderByDescending(g => g.Max(r => r.worstCorner)))
            {
                int f5 = g.Count(r => r.gap > 0.05f), f20 = g.Count(r => r.worstCorner > 0.2f);
                var w = g.OrderByDescending(r => r.worstCorner).First();
                sb.AppendLine($"  {g.Key}: {g.Count()}, base over 5 cm up {f5}, a corner over 20 cm up {f20}; worst {w.worstCorner:0.00} m at ({w.at.x:0}, {w.at.z:0})");
            }
            return sb.ToString();
        }

        /// Loads the saved walk map and reports where it is and how much of the play area it reaches.
        static string NavCheck()
        {
            var zone = Object.FindAnyObjectByType<ZoneManager>(FindObjectsInactive.Include);
            var data = AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>("Assets/Data/MainLevel_ZombieNavMesh.asset");
            if (zone == null || data == null) return "FAILED: no zone or no walk map asset.";
            UnityEngine.AI.NavMesh.RemoveAllNavMeshData();
            var inst = UnityEngine.AI.NavMesh.AddNavMeshData(data);
            var tri = UnityEngine.AI.NavMesh.CalculateTriangulation();
            var b = new Bounds(tri.vertices[0], Vector3.zero);
            foreach (var v in tri.vertices) b.Encapsulate(v);
            Vector3 c = NavCentre(zone);
            int hit = 0, total = 0;
            var rnd = new System.Random(1);
            for (int i = 0; i < 400; i++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f, r = Mathf.Sqrt((float)rnd.NextDouble()) * zone.maxRadius;
                var p = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                if (!Physics.Raycast(p + Vector3.up * 300f, Vector3.down, out var h, 600f, ~0, QueryTriggerInteraction.Ignore) || !(h.collider is TerrainCollider)) continue;
                total++;
                if (UnityEngine.AI.NavMesh.SamplePosition(h.point, out _, 1.5f, UnityEngine.AI.NavMesh.AllAreas)) hit++;
            }
            UnityEngine.AI.NavMesh.RemoveNavMeshData(inst);
            return $"Walk map: {tri.indices.Length / 3} triangles, x {b.min.x:0} to {b.max.x:0}, z {b.min.z:0} to {b.max.z:0} (base at {c.x:0}, {c.z:0}). " +
                   $"Open ground in the play area on the walk map: {hit} of {total} sample points.";
        }

        /// Walk maps centre on the player base (the city), never on the ZoneManager object: it sits at the world origin.
        static Vector3 NavCentre(ZoneManager zone) => zone.playerBase != null ? zone.playerBase.position : zone.center;

        internal static string RebakeAndSave(Transform mapT)
        {
            var zone = Object.FindAnyObjectByType<ZoneManager>(FindObjectsInactive.Include);
            Physics.SyncTransforms();
            var nav = ZoneTools.BakeNavMesh(mapT.gameObject, NavCentre(zone), zone.maxRadius + 15f, out string navNote);
            var loader = zone.GetComponent<ArenaNavMesh>();
            if (nav != null && loader != null)
            {
                Undo.RecordObject(loader, "Re-bake zombie paths");
                loader.data = nav;
                EditorUtility.SetDirty(loader);
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(zone.gameObject.scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(zone.gameObject.scene);
            return "Zombie paths: " + (nav != null ? "re-baked. " : "NOT re-baked. ") + navNote + "\nScene saved.";
        }

        // ---------- pictures

        const string ViewDir = "Docs/Map/Cleanup";

        /// Street-level shots just outside the safe base (8 directions), 4 high overview shots and a
        /// top-down picture of the play area. Files: Docs/Map/Cleanup/{tag}_*.png.
        static string Views(string tag)
        {
            var zone = Object.FindAnyObjectByType<ZoneManager>(FindObjectsInactive.Include);
            var safe = Object.FindAnyObjectByType<SafeBase>(FindObjectsInactive.Include);
            var main = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>(FindObjectsInactive.Include);
            if (zone == null || main == null) return "FAILED: open MainLevel first (needs ZoneManager and a camera).";
            System.IO.Directory.CreateDirectory(ViewDir);
            Vector3 c = zone.ArcCenter;
            c = Ground(c);
            float outR = (safe != null ? safe.radius : 20f) + 6f;

            var camGo = new GameObject("_CleanupCam") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            int shots = 0;
            try
            {
                var cam = camGo.AddComponent<Camera>();
                cam.CopyFrom(main);
                var src = main.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                var dst = cam.GetUniversalAdditionalCameraData();
                if (src != null) { dst.renderPostProcessing = src.renderPostProcessing; dst.volumeLayerMask = src.volumeLayerMask; }
                dst.volumeTrigger = cam.transform;
                cam.targetTexture = rt;
                cam.farClipPlane = 2000f;

                for (int i = 0; i < 8; i++)
                {
                    float a = i * 45f * Mathf.Deg2Rad;
                    var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    var eye = Ground(c + d * outR) + Vector3.up * 1.7f;
                    cam.fieldOfView = 70f;
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(d + Vector3.down * 0.08f));
                    Shot(cam, rt, $"{ViewDir}/{tag}_Street{i * 45:000}.png"); shots++;
                }
                for (int i = 0; i < 4; i++)
                {
                    float a = (45f + i * 90f) * Mathf.Deg2Rad;
                    var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    var eye = c - d * 60f + Vector3.up * 45f;
                    cam.fieldOfView = 60f;
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation((c + d * 90f) - eye));
                    Shot(cam, rt, $"{ViewDir}/{tag}_High{i}.png"); shots++;
                }
                // top-down, the whole play area
                var sq = new RenderTexture(2048, 2048, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = sq;
                cam.orthographic = true;
                cam.orthographicSize = zone.maxRadius + 20f;
                cam.nearClipPlane = 1f;
                cam.farClipPlane = 600f;
                cam.transform.SetPositionAndRotation(c + Vector3.up * 250f, Quaternion.Euler(90f, 0f, 0f));
                bool fog = RenderSettings.fog;
                dst.renderPostProcessing = false;  // no fog or grading: a plain plan view
                RenderSettings.fog = false;
                try { Shot(cam, sq, $"{ViewDir}/{tag}_Top.png"); shots++; }
                finally { RenderSettings.fog = fog; }
                sq.Release(); Object.DestroyImmediate(sq);
            }
            finally
            {
                Object.DestroyImmediate(camGo);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
            return $"Saved {shots} pictures to {ViewDir}/{tag}_*.png (centre {c.x:0}, {c.y:0.0}, {c.z:0}).";
        }

        /// Close-ups of placed city props (one of each kind), as a player would see them.
        static string PropViews()
        {
            var main = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>(FindObjectsInactive.Include);
            var root = GameObject.Find(MapRoot);
            if (main == null || root == null) return "FAILED: no camera or no Map/CityProps.";
            System.IO.Directory.CreateDirectory(ViewDir);
            var kinds = new[] { "ZR_IvyCard", "ZR_ScaffoldTower", "ZR_SniperNest", "ZR_LeafyTree", "ZR_Bonfire", "ZR_Hesco" };
            var camGo = new GameObject("_PropCam") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            int n = 0;
            try
            {
                var cam = camGo.AddComponent<Camera>();
                cam.CopyFrom(main);
                var src = main.GetComponent<UniversalAdditionalCameraData>();
                var dst = cam.GetUniversalAdditionalCameraData();
                if (src != null) { dst.renderPostProcessing = src.renderPostProcessing; dst.volumeLayerMask = src.volumeLayerMask; }
                dst.volumeTrigger = cam.transform;
                cam.targetTexture = rt;
                cam.fieldOfView = 60f;
                foreach (var k in kinds)
                {
                    var t = root.GetComponentsInChildren<Transform>().FirstOrDefault(x => x.name.StartsWith(k));
                    if (t == null) continue;
                    var rs = t.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer)).ToArray();
                    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    float dist = Mathf.Max(3.5f, b.size.magnitude * 0.9f);
                    // stand on the most open side
                    Vector3 bestEye = b.center; float best = -1f;
                    for (int i = 0; i < 8; i++)
                    {
                        var d = Quaternion.Euler(0f, i * 45f + 20f, 0f) * Vector3.forward;
                        var eye = Ground(b.center + d * dist) + Vector3.up * 4.5f; // the game camera sits high, looking down
                        bool blocked = Physics.Linecast(eye, b.center + Vector3.up * 0.3f, out var hit, ~0, QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(t);
                        float score = blocked ? 0f : 1f + Vector3.Dot(d, Vector3.forward) * 0.01f;
                        if (score > best) { best = score; bestEye = eye; }
                    }
                    cam.transform.SetPositionAndRotation(bestEye, Quaternion.LookRotation(b.center - bestEye));
                    Shot(cam, rt, $"{ViewDir}/Prop_{k}.png"); n++;
                }
            }
            finally
            {
                Object.DestroyImmediate(camGo);
                rt.Release(); Object.DestroyImmediate(rt);
            }
            return $"Saved {n} prop close-ups to {ViewDir}/Prop_*.png.";
        }

        static Vector3 Ground(Vector3 p)
        {
            var hits = Physics.RaycastAll(p + Vector3.up * 30f, Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (var h in hits)
                if (h.point.y > best && h.point.y < p.y + 6f) best = h.point.y;
            if (!float.IsNegativeInfinity(best)) p.y = best;
            return p;
        }

        static void Shot(Camera cam, RenderTexture rt, string path)
        {
            var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
            if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, req)) UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req);
            else cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        // ---------- helpers

        class Piece { public Transform t; public string kind; public Bounds bounds; }

        static List<Piece> Pieces(Transform map)
        {
            var list = new List<Piece>();
            var seen = new HashSet<Transform>();
            foreach (var r in map.GetComponentsInChildren<Renderer>(false))
            {
                if (r is ParticleSystemRenderer || r.GetComponentInParent<Terrain>() != null) continue;
                var root = PrefabUtility.GetOutermostPrefabInstanceRoot(r.gameObject);
                Transform key = root != null && root.transform != map && root.transform.IsChildOf(map) ? root.transform : r.transform;
                if (!seen.Add(key)) continue;
                var rs = key.GetComponentsInChildren<Renderer>(false).Where(x => !(x is ParticleSystemRenderer)).ToArray();
                var b = rs[0].bounds;
                foreach (var x in rs) b.Encapsulate(x.bounds);
                list.Add(new Piece { t = key, kind = BaseName(SourceName(key)), bounds = b });
            }
            return list;
        }

        static bool IsBuilding(Piece p) =>
            p.kind.StartsWith("building") && !p.kind.Contains("rubble") && p.bounds.size.y >= 4f && Mathf.Max(p.bounds.size.x, p.bounds.size.z) >= 5f;

        static bool NearAny(Bounds b, List<Bounds> footprints)
        {
            foreach (var f in footprints)
            {
                float dx = Mathf.Max(0f, Mathf.Max(f.min.x - b.max.x, b.min.x - f.max.x));
                float dz = Mathf.Max(0f, Mathf.Max(f.min.z - b.max.z, b.min.z - f.max.z));
                if (dx <= NearBuilding && dz <= NearBuilding) return true;
            }
            return false;
        }

        static string SourceName(Transform t)
        {
            var src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(t.gameObject);
            if (src != null) return src.name;
            return MeshName(t.gameObject);
        }

        static string MeshName(GameObject g)
        {
            var mf = g.GetComponent<MeshFilter>();
            return mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : g.name;
        }

        /// "rubble1 (12)" -> "rubble1".
        static string BaseName(string n)
        {
            int k = n.IndexOf(" (");
            return k > 0 ? n.Substring(0, k) : n;
        }

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        const string WaveBalancePath = "Assets/Data/WaveBalanceConfig.asset";

        /// Creates Assets/Data/WaveBalanceConfig.asset (if missing) and hands it to the scene's WaveManager.
        static string WaveBalance()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<ZombieRush.Data.WaveBalanceConfig>(WaveBalancePath);
            bool made = cfg == null;
            if (made)
            {
                cfg = ScriptableObject.CreateInstance<ZombieRush.Data.WaveBalanceConfig>();
                AssetDatabase.CreateAsset(cfg, WaveBalancePath);
                AssetDatabase.SaveAssets();
            }
            var wm = Object.FindFirstObjectByType<ZombieRush.Waves.WaveManager>(FindObjectsInactive.Include);
            if (wm == null) return "Wave balance: NO WaveManager in the open scene.";
            wm.balance = cfg;
            EditorUtility.SetDirty(wm);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(wm.gameObject.scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(wm.gameObject.scene);
            var sb = new System.Text.StringBuilder($"Wave balance: {(made ? "created" : "found")} {WaveBalancePath}, set on {wm.name}, scene saved.");
            sb.AppendLine();
            foreach (int w in new[] { 1, 2, 3, 5, 8, 10, 15, 20, 30 })
                sb.AppendLine($"  wave {w,2}: zombie health x{cfg.ZombieHealth(w):0.00} damage x{cfg.ZombieDamage(w):0.00} speed x{cfg.ZombieSpeed(w):0.00} | player health {cfg.PlayerMaxHealth(100f, w):0} gun damage x{cfg.PlayerDamage(w):0.00}");
            return sb.ToString();
        }
    }
}
