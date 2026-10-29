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
    /// Zombie Rush > Level > Make Base Building (copy selected building)
    ///
    /// Copies a city building (the one selected in the Hierarchy, or "building2") onto
    /// the PlayerBase marker so the players' safe base is that building:
    ///   - door turned to face the city, sat on the ground, small props in its footprint hidden;
    ///   - lit inside (4 warm ceiling lamps with glowing fixtures - 4 is the mobile per-object light limit);
    ///   - half-circle safe base + fight zone fitted to it (see FitZones), gear + player inside;
    ///   - zombie walk map rebaked so they path around it.
    /// Undoable with Ctrl+Z. Re-running replaces the previous copy.
    public static class BaseBuildingTools
    {
        const string BuildingName = "BaseBuilding";
        const string DefaultSource = "building2";
        const string BaseFolder = "Assets/Environment/Base";
        // Cool white: the base is the "safe" (cyan) place; warm yellow clashed with the night look.
        static readonly Color LampColor = new Color(0.86f, 0.94f, 1f);
        const float LampHang = 3.3f;     // lamp height above the floor (hangs on a rod in tall rooms)
        const float LampIntensity = 6f;

        [MenuItem("Zombie Rush/Level/Make Base Building (copy selected building)", priority = 13)]
        public static void MakeBaseBuilding()
        {
            var zone = Object.FindAnyObjectByType<ZoneManager>(FindObjectsInactive.Include);
            if (zone == null || zone.playerBase == null)
            {
                EditorUtility.DisplayDialog("Base Building", "Open MainLevel and run Level > Set Up Base, Zone + Zombie Paths first (no PlayerBase yet).", "OK");
                return;
            }
            var map = zone.gameObject.scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Map");
            if (map == null)
            {
                EditorUtility.DisplayDialog("Base Building", "No 'Map' in the scene - run Level > Install Final Map first.", "OK");
                return;
            }

            var source = FindSource(map);
            if (source == null)
            {
                EditorUtility.DisplayDialog("Base Building", $"Select the building to copy in the Hierarchy (couldn't find '{DefaultSource}').", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Make base building");
            int undoGroup = Undo.GetCurrentGroup();

            var old = map.transform.Find(BuildingName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            var baseT = zone.playerBase;
            var terrain = map.GetComponentInChildren<Terrain>();
            var city = MapInstaller.CityBounds(map);

            // 1. Copy, keeping how deep the original sits in the ground.
            var local = LocalBounds(source);
            float srcGround = FootprintHeight(source, local, terrain, source.position.y);
            float embed = source.position.y - srcGround;

            var copy = Object.Instantiate(source.gameObject, source.position, source.rotation, map.transform);
            copy.name = BuildingName;
            copy.SetActive(true);
            Undo.RegisterCreatedObjectUndo(copy, "Make base building");
            var t = copy.transform;

            // 2. Door toward the city, so players step out into the fight zone.
            var door = t.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(c => c.name.StartsWith("door") && !c.name.StartsWith("doorframe"));
            Vector3 toCity = city.center - baseT.position;
            toCity.y = 0f;
            if (door != null && toCity.sqrMagnitude > 1f)
            {
                Vector3 doorDir = door.position - t.TransformPoint(local.center);
                doorDir.y = 0f;
                if (doorDir.sqrMagnitude > 0.01f)
                    t.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(doorDir, toCity, Vector3.up), Vector3.up) * t.rotation;
            }

            // 3. Centre on the base, sit on the highest ground under it (no terrain poking through the floor).
            Vector3 centre = t.TransformPoint(local.center);
            t.position += new Vector3(baseT.position.x - centre.x, 0f, baseT.position.z - centre.z);
            float ground = FootprintHeight(t, local, terrain, baseT.position.y);
            t.position = new Vector3(t.position.x, ground + embed, t.position.z);
            Physics.SyncTransforms();

            int hidden = HidePropsInside(map, t, local, terrain);
            Physics.SyncTransforms();

            string fit = FitZones(zone, map, t);
            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeGameObject = copy;

            string summary = $"Copied '{source.name}' onto the player base{(door != null ? ", door facing the city" : "")}" +
                             $"{(hidden > 0 ? $", {hidden} props in its way hidden" : "")}.\n" + fit;
            Debug.Log("Zombie Rush: " + summary);
            EditorUtility.DisplayDialog("Base building ready", summary + Footer, "OK");
        }

        [MenuItem("Zombie Rush/Level/Fit Half-Circle Zones To Base Building", priority = 14)]
        public static void FitZonesToBuilding()
        {
            var zone = Object.FindAnyObjectByType<ZoneManager>(FindObjectsInactive.Include);
            if (zone == null || zone.playerBase == null)
            {
                EditorUtility.DisplayDialog("Fit Zones", "Open MainLevel and run Level > Set Up Base, Zone + Zombie Paths first (no PlayerBase yet).", "OK");
                return;
            }
            var map = zone.gameObject.scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Map");
            if (map == null)
            {
                EditorUtility.DisplayDialog("Fit Zones", "No 'Map' in the scene - run Level > Install Final Map first.", "OK");
                return;
            }

            // The base building where you placed it: the last one used, Map/BaseBuilding, or the selected building.
            var safe = zone.playerBase.GetComponent<SafeBase>();
            Transform building = safe != null && safe.building != null ? safe.building : map.transform.Find(BuildingName);
            var sel = FindSource(map);
            if (Selection.activeTransform != null && sel != null && Selection.activeTransform.IsChildOf(sel)) building = sel;
            if (building == null)
            {
                EditorUtility.DisplayDialog("Fit Zones", "Select the base building in the Hierarchy, then run this again.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Fit half-circle zones");
            int undoGroup = Undo.GetCurrentGroup();
            string fit = FitZones(zone, map, building);
            Undo.CollapseUndoOperations(undoGroup);

            Debug.Log("Zombie Rush: " + fit);
            EditorUtility.DisplayDialog("Half-circle zones ready", $"Base building: '{building.name}'.\n" + fit + Footer, "OK");
        }

        /// The first lamps hung just under the roof (9+ m up in a tall shell) and were too weak to
        /// light the ground floor. This replaces them with brighter cool-white lamps hanging at
        /// ~3.3 m on rods, so the walls inside are clearly visible.
        [MenuItem("Zombie Rush/Level/Relight Base Building (brighter, lower lamps)", priority = 15)]
        public static void RelightBase()
        {
            var zone = Object.FindAnyObjectByType<ZoneManager>(FindObjectsInactive.Include);
            var map = zone != null ? zone.gameObject.scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Map") : null;
            var safe = zone != null && zone.playerBase != null ? zone.playerBase.GetComponent<SafeBase>() : null;
            Transform building = safe != null && safe.building != null ? safe.building : map != null ? map.transform.Find(BuildingName) : null;
            if (building == null)
            {
                EditorUtility.DisplayDialog("Relight base", "No base building found (Map/BaseBuilding). Open MainLevel first.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Relight base");
            int undoGroup = Undo.GetCurrentGroup();
            var old = building.Find("BaseLights");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            Physics.SyncTransforms();
            var local = LocalBounds(building);
            var room = MeasureRoom(building, local);
            int lamps = AddLights(building, local, room);
            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(building.gameObject.scene);

            string msg = $"{lamps} lamps in '{building.name}': floor {room.floor:0.0} m, lamps at {Mathf.Min(room.ceiling - 0.45f, room.floor + LampHang):0.0} m.";
            Debug.Log("Zombie Rush: " + msg);
            EditorUtility.DisplayDialog("Base relit", msg + "\n\nSave the scene (Ctrl+S) and press Play.", "OK");
        }

        const string Footer =
            "\n\nSave the scene (Ctrl+S) and press Play.\n" +
            "If the half circle opens the wrong way, rotate 'PlayerBase' (its blue arrow = where the zone opens) - the walls follow - then run Fit Half-Circle Zones again to redo zombie paths.\n" +
            "Don't re-run Set Up Base with 'Side of the city' - it moves the base off the building.";

        /// Shared by both menus: PlayerBase on the building, half-circle safe base behind/around it,
        /// half-circle fight zone (with fog curtain) opening into the city, lamps, gear, player, spawns, walk map.
        static string FitZones(ZoneManager zone, GameObject map, Transform building)
        {
            var scene = zone.gameObject.scene;
            var baseT = zone.playerBase;
            var city = MapInstaller.CityBounds(map);
            string seated = SeatOnGround(map, building);
            Physics.SyncTransforms();
            var local = LocalBounds(building);
            var room = MeasureRoom(building, local);

            // Which way the zone opens: the door's side if it faces the city at all,
            // else the building side that faces the city centre most.
            Vector3 centre = building.TransformPoint(local.center);
            Vector3 toCity = city.center - centre;
            toCity.y = 0f;
            toCity = toCity.sqrMagnitude > 1f ? toCity.normalized : Vector3.forward;
            var axes = new[] { building.right, -building.right, building.forward, -building.forward }
                .Select(a => new Vector3(a.x, 0f, a.z).normalized).ToArray();
            Vector3 doorAxis = axes.OrderByDescending(a => Vector3.Dot(a, Flat(room.doorLocal.x * building.right + room.doorLocal.z * building.forward))).First();
            Vector3 forward = room.hasDoor && Vector3.Dot(doorAxis, toCity) > 0f
                ? doorAxis
                : axes.OrderByDescending(a => Vector3.Dot(a, toCity)).First();

            // Depth (front-back) and width of the building along that direction.
            Vector3 size = Vector3.Scale(local.size, building.lossyScale);
            bool alongX = Mathf.Abs(Vector3.Dot(forward, Flat(building.right))) > 0.5f;
            float depth = alongX ? size.x : size.z;
            float width = alongX ? size.z : size.x;

            // PlayerBase in the middle of the building, facing where the zone opens.
            Undo.RecordObject(baseT, "Fit zones");
            baseT.SetPositionAndRotation(new Vector3(centre.x, room.floor, centre.z), Quaternion.LookRotation(forward));

            // Safe base: straight edge just behind the back wall, round edge a little past the front.
            const float behind = 1.5f, front = 6f;
            float backOffset = depth * 0.5f + behind;
            float safeRadius = Mathf.Max(
                new Vector2(width * 0.5f + 1.5f, depth + behind).magnitude,
                depth + behind + front);

            var safeBase = baseT.GetComponent<SafeBase>();
            if (safeBase == null) safeBase = Undo.AddComponent<SafeBase>(baseT.gameObject);
            Undo.RecordObject(safeBase, "Fit zones");
            safeBase.halfCircle = true;
            safeBase.backOffset = backOffset;
            safeBase.radius = safeRadius;
            safeBase.building = building;
            EditorUtility.SetDirty(safeBase);
            if (safeBase.ring != null)
            {
                Undo.RecordObject(safeBase.ring, "Fit zones");
                safeBase.ring.fixedRadius = safeRadius;
                var s = safeBase.Shape;
                safeBase.ring.SetShape(s.forward, false);
                safeBase.ring.SetZone(s.center, safeRadius, false);
                EditorUtility.SetDirty(safeBase.ring);
            }

            // Fight zone: the same straight edge, growing as a half circle across the city.
            Undo.RecordObject(zone, "Fit zones");
            zone.halfCircle = true;
            zone.backOffset = backOffset;
            zone.startRadius = Mathf.Max(safeRadius + ArenaSpawn.BaseMargin + 12f, 35f);
            var arc = zone.ArcCenter;
            float needed = MapInstaller.FarthestCorner(arc, city);
            int steps = Mathf.Clamp(Mathf.CeilToInt((needed - zone.startRadius) / Mathf.Max(1f, zone.expandStep)), 1, 16);
            zone.maxRadius = zone.startRadius + steps * zone.expandStep;
            zone.center = arc;
            zone.deployPlayerAtCenter = true;
            EditorUtility.SetDirty(zone);

            if (zone.boundaryVisual != null && zone.boundaryVisual.TryGetComponent<ZoneBoundaryFX>(out var fx))
            {
                Undo.RecordObject(fx, "Fit zones");
                fx.fogMaterial = null;           // no fog curtain: the rest of the map stays visible
                fx.wallOnStraightEdge = false;   // back edge = ground line only
                fx.enabled = false;
                fx.enabled = true;
                fx.SetShape(zone.Forward, true);
                fx.SetZone(arc, zone.startRadius, false);
                EditorUtility.SetDirty(fx);
            }

            // Lamps (once), gear inside, player inside facing out.
            int lamps = building.Find("BaseLights") != null ? 0 : AddLights(building, local, room);
            var gear = baseT.Find("BaseGear");
            int gearCount = gear != null ? ArrangeGearInside(baseT, gear) : 0;

            var player = Object.FindAnyObjectByType<PlayerHealth>(FindObjectsInactive.Include);
            if (player != null)
            {
                Undo.RecordObject(player.transform, "Fit zones");
                Vector3 spot = RoomPoint(building, local, room, 0f, -0.15f);
                player.transform.SetPositionAndRotation(spot + Vector3.up * 0.05f, Quaternion.LookRotation(forward));
            }

            var waves = Object.FindAnyObjectByType<WaveManager>(FindObjectsInactive.Include);
            if (waves != null && waves.spawnPoints != null)
            {
                foreach (var pad in waves.spawnPoints) if (pad != null) Undo.RecordObject(pad, "Fit zones");
                waves.ArrangeSpawnPoints(arc, zone.startRadius * waves.edgeRadiusFactor, zone.Forward);
            }

            // Zombies must route around the building.
            var nav = ZoneTools.BakeNavMesh(map, arc, zone.maxRadius + 15f, out string navNote);
            if (nav != null)
            {
                var loader = zone.GetComponent<ArenaNavMesh>();
                if (loader == null) loader = Undo.AddComponent<ArenaNavMesh>(zone.gameObject);
                Undo.RecordObject(loader, "Fit zones");
                loader.data = nav;
                EditorUtility.SetDirty(loader);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.Frame(new Bounds(arc, Vector3.one * zone.startRadius * 2.2f), false);

            return seated + $"Building {width:0} x {depth:0} m. Safe base: half circle {safeRadius:0} m - covers the back of the building and {front:0} m in front of it.\n" +
                   $"Fight zone: half circle opening into the city, starts {zone.startRadius:0} m, +{zone.expandStep:0} m per expansion, " +
                   $"{steps} expansions to {zone.maxRadius:0} m. Glowing edge like the safe base; the back of the map stays open to view.\n" +
                   (lamps > 0 ? $"{lamps} ceiling lamps added. " : "") + $"{gearCount} gear pickups inside, player starts inside.\n" + navNote;
        }

        /// Called by Set Up Base too, so re-running it keeps the gear inside the building.
        internal static int ArrangeGearInside(Transform baseT, Transform gear)
        {
            var map = baseT.gameObject.scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Map");
            var safe = baseT.GetComponent<SafeBase>();
            var building = safe != null && safe.building != null ? safe.building
                         : map != null ? map.transform.Find(BuildingName) : null;
            if (building == null || gear.childCount == 0) return 0;

            Physics.SyncTransforms();
            var local = LocalBounds(building);
            var room = MeasureRoom(building, local);
            int n = gear.childCount;
            for (int i = 0; i < n; i++)
            {
                var item = gear.GetChild(i);
                Undo.RecordObject(item, "Arrange base gear");
                // A row along the back half (away from the door), spread over the middle 60%.
                float along = n == 1 ? 0f : Mathf.Lerp(-0.3f, 0.3f, i / (float)(n - 1));
                item.position = RoomPoint(building, local, room, along, 0.25f);
            }
            return n;
        }

        // ---------------------------------------------------------------- helpers

        struct Room { public float floor, ceiling; public bool longX, hasDoor; public Vector3 doorLocal; }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        static Transform FindSource(GameObject map)
        {
            var sel = Selection.activeTransform;
            if (sel != null && sel.IsChildOf(map.transform) && sel.name != BuildingName && sel.GetComponentInChildren<Renderer>() != null)
            {
                var root = PrefabUtility.GetOutermostPrefabInstanceRoot(sel.gameObject);
                return root != null && root.transform.IsChildOf(map.transform) && root != map ? root.transform : sel;
            }
            return map.GetComponentsInChildren<Transform>(true).FirstOrDefault(c => c.name == DefaultSource);
        }

        /// Mesh bounds of the building in its own local space (LOD0-ish, all meshes).
        static Bounds LocalBounds(Transform root)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, (i & 4) / 2f - 1));
                    var p = root.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        static IEnumerable<Vector3> FootprintPoints(Transform t, Bounds local)
        {
            var c = local.center; var e = local.extents;
            yield return t.TransformPoint(new Vector3(c.x, local.min.y, c.z));
            for (int i = 0; i < 4; i++)
                yield return t.TransformPoint(new Vector3(c.x + e.x * ((i & 1) * 2 - 1), local.min.y, c.z + e.z * ((i & 2) - 1)));
        }

        static float FootprintHeight(Transform t, Bounds local, Terrain terrain, float fallback)
        {
            if (terrain == null) return fallback;
            return FootprintPoints(t, local).Max(p => terrain.SampleHeight(p) + terrain.transform.position.y);
        }

        /// Hides (undoably) standing props whose bounds overlap the building's footprint.
        /// Flat things (roads, decals) and anything bigger than the building are left alone.
        static int HidePropsInside(GameObject map, Transform building, Bounds local, Terrain terrain)
        {
            var pad = new Bounds(local.center, local.size + new Vector3(1.5f, 0f, 1.5f));
            float maxSize = Mathf.Max(local.size.x, local.size.z) * 1.2f;
            var done = new HashSet<GameObject>();
            int hidden = 0;
            foreach (var r in map.GetComponentsInChildren<Renderer>())
            {
                if (r.transform.IsChildOf(building) || (terrain != null && r.transform.IsChildOf(terrain.transform))) continue;
                var root = PrefabUtility.GetOutermostPrefabInstanceRoot(r.gameObject);
                var go = root != null && root != map && root.transform.IsChildOf(map.transform) ? root : r.gameObject;
                if (!done.Add(go) || !go.activeInHierarchy) continue;

                var renderers = go.GetComponentsInChildren<Renderer>();
                var wb = renderers[0].bounds;
                foreach (var rr in renderers) wb.Encapsulate(rr.bounds);
                if (wb.size.y < 0.4f || Mathf.Max(wb.size.x, wb.size.z) > maxSize) continue;

                var lc = building.InverseTransformPoint(wb.center);
                var flat = new Vector3(lc.x, pad.center.y, lc.z);
                float reach = 0.5f * Mathf.Max(wb.size.x, wb.size.z);
                var near = pad.ClosestPoint(flat);
                if ((near - flat).sqrMagnitude > reach * reach * 0.25f) continue;

                Undo.RecordObject(go, "Hide props in base");
                go.SetActive(false);
                hidden++;
            }
            return hidden;
        }

        /// Height of the building's own floor (its colliders only - terrain poking in is ignored),
        /// or the bottom of its walls if it has no floor mesh.
        static float BuildingFloor(Transform t, Bounds local, out bool hasFloor)
        {
            Vector3 c = t.TransformPoint(local.center);
            float bottom = t.TransformPoint(local.min).y;
            var start = new Vector3(c.x, bottom + 2.2f, c.z);
            float best = float.NegativeInfinity;
            foreach (var hit in Physics.RaycastAll(start, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.transform.IsChildOf(t) && hit.point.y > best) best = hit.point.y;
            hasFloor = best > float.NegativeInfinity;
            return hasFloor ? best : bottom;
        }

        const string TerrainCopyPath = "Assets/Data/MainLevel_Terrain.asset";

        /// Un-buries the building: moves it to the average ground height around it, then flattens
        /// the terrain under it (and blends the ground around it) to its floor level, clearing
        /// grass and trees inside. Works on the scene's own copy of the terrain data, never the pack's.
        static string SeatOnGround(GameObject map, Transform building)
        {
            Physics.SyncTransforms();
            var local = LocalBounds(building);
            Vector3 c = building.TransformPoint(local.center);
            var terrain = map.GetComponentsInChildren<Terrain>()
                .FirstOrDefault(tr => new Rect(tr.transform.position.x, tr.transform.position.z, tr.terrainData.size.x, tr.terrainData.size.z).Contains(new Vector2(c.x, c.z)));
            if (terrain == null) return "";

            // Average ground just outside the walls = where the floor should be.
            float H(Vector3 p) => terrain.SampleHeight(p) + terrain.transform.position.y;
            var sx = local.extents.x + 1.5f; var sz = local.extents.z + 1.5f;
            float sum = 0f; int n = 0;
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2f;
                var lp = local.center + new Vector3(Mathf.Clamp(Mathf.Cos(a) * 1.5f, -1f, 1f) * sx, 0f, Mathf.Clamp(Mathf.Sin(a) * 1.5f, -1f, 1f) * sz);
                sum += H(building.TransformPoint(lp)); n++;
            }
            float ground = sum / n;

            float floor = BuildingFloor(building, local, out bool hasFloor);
            float lift = ground - floor;
            Undo.RecordObject(building, "Seat base building");
            building.position += Vector3.up * lift;
            Physics.SyncTransforms();
            floor += lift;
            float target = hasFloor ? floor - 0.06f : floor + 0.04f;   // just under the floor slab / as the floor

            // Own copy of the terrain data so the pack's demo terrain is never changed.
            var data = terrain.terrainData;
            if (AssetDatabase.GetAssetPath(data) != TerrainCopyPath)
            {
                var existing = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainCopyPath);
                if (existing == null && AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(data), TerrainCopyPath))
                    existing = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainCopyPath);
                if (existing == null) return "Couldn't copy the terrain data, so the ground wasn't flattened.\n";
                Undo.RecordObject(terrain, "Seat base building");
                terrain.terrainData = existing;
                if (terrain.TryGetComponent<TerrainCollider>(out var tc))
                {
                    Undo.RecordObject(tc, "Seat base building");
                    tc.terrainData = existing;
                }
                data = existing;
            }
            Undo.RegisterCompleteObjectUndo(data, "Seat base building");

            // Heightmap region covering the building + blend ring.
            const float flatMargin = 1.5f, blend = 8f;
            var tp = terrain.transform.position;
            var size = data.size;
            int res = data.heightmapResolution;
            float reach = new Vector2(local.extents.x, local.extents.z).magnitude + flatMargin + blend;
            int x0 = Mathf.Clamp(Mathf.FloorToInt((c.x - reach - tp.x) / size.x * (res - 1)), 0, res - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((c.x + reach - tp.x) / size.x * (res - 1)), 0, res - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((c.z - reach - tp.z) / size.z * (res - 1)), 0, res - 1);
            int z1 = Mathf.Clamp(Mathf.CeilToInt((c.z + reach - tp.z) / size.z * (res - 1)), 0, res - 1);
            int w = x1 - x0 + 1, hgt = z1 - z0 + 1;
            var heights = data.GetHeights(x0, z0, w, hgt);
            float targetN = Mathf.Clamp01((target - tp.y) / size.y);

            // Distance outside the building's (rotated) footprint, in metres.
            float Outside(Vector3 world)
            {
                var l = building.InverseTransformPoint(world) - local.center;
                var s = building.lossyScale;
                float dx = Mathf.Max(0f, Mathf.Abs(l.x * s.x) - local.extents.x * s.x - flatMargin);
                float dz = Mathf.Max(0f, Mathf.Abs(l.z * s.z) - local.extents.z * s.z - flatMargin);
                return Mathf.Sqrt(dx * dx + dz * dz);
            }

            for (int z = 0; z < hgt; z++)
                for (int x = 0; x < w; x++)
                {
                    var world = new Vector3(tp.x + (x0 + x) / (float)(res - 1) * size.x, 0f, tp.z + (z0 + z) / (float)(res - 1) * size.z);
                    float k = 1f - Mathf.SmoothStep(0f, 1f, Outside(world) / blend);
                    if (k > 0f) heights[z, x] = Mathf.Lerp(heights[z, x], targetN, k);
                }
            data.SetHeights(x0, z0, heights);
            data.SyncHeightmap();

            // No grass growing through the floor, no trees inside.
            int dres = data.detailResolution;
            if (dres > 0 && data.detailPrototypes.Length > 0)
            {
                int dx0 = Mathf.Clamp(Mathf.FloorToInt((c.x - reach - tp.x) / size.x * dres), 0, dres - 1);
                int dx1 = Mathf.Clamp(Mathf.CeilToInt((c.x + reach - tp.x) / size.x * dres), 0, dres - 1);
                int dz0 = Mathf.Clamp(Mathf.FloorToInt((c.z - reach - tp.z) / size.z * dres), 0, dres - 1);
                int dz1 = Mathf.Clamp(Mathf.CeilToInt((c.z + reach - tp.z) / size.z * dres), 0, dres - 1);
                for (int layer = 0; layer < data.detailPrototypes.Length; layer++)
                {
                    var d = data.GetDetailLayer(dx0, dz0, dx1 - dx0 + 1, dz1 - dz0 + 1, layer);
                    for (int z = 0; z <= dz1 - dz0; z++)
                        for (int x = 0; x <= dx1 - dx0; x++)
                        {
                            var world = new Vector3(tp.x + (dx0 + x + 0.5f) / dres * size.x, 0f, tp.z + (dz0 + z + 0.5f) / dres * size.z);
                            if (Outside(world) <= 0.5f) d[z, x] = 0;
                        }
                    data.SetDetailLayer(dx0, dz0, layer, d);
                }
            }
            var trees = data.treeInstances;
            var kept = trees.Where(tree => Outside(Vector3.Scale(tree.position, size) + tp) > 1f).ToArray();
            if (kept.Length != trees.Length) data.treeInstances = kept;

            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Physics.SyncTransforms();
            return $"Building un-buried: moved {lift:+0.0;-0.0} m to the ground level around it, ground flattened under it and blended over {blend:0} m.\n";
        }

        static Room MeasureRoom(Transform t, Bounds local)
        {
            var room = new Room { longX = local.size.x * t.lossyScale.x >= local.size.z * t.lossyScale.z };
            Vector3 c = t.TransformPoint(local.center);
            float bottom = t.TransformPoint(local.min).y;
            float top = t.TransformPoint(new Vector3(local.center.x, local.max.y, local.center.z)).y;

            bool backfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;   // roof undersides are often single-sided
            try
            {
                room.floor = BuildingFloor(t, local, out _);
                var up = new Vector3(c.x, room.floor + 0.6f, c.z);
                room.ceiling = Physics.Raycast(up, Vector3.up, out var hit, top - room.floor + 1f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : top - 0.4f;
            }
            finally { Physics.queriesHitBackfaces = backfaces; }

            room.ceiling = Mathf.Clamp(room.ceiling, room.floor + 2.4f, room.floor + 9f);
            var door = t.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(d => d.name.StartsWith("door") && !d.name.StartsWith("doorframe"));
            room.hasDoor = door != null;
            room.doorLocal = door != null ? t.InverseTransformPoint(door.position) - local.center : Vector3.forward;
            return room;
        }

        /// A floor point inside the room. `along` runs the long axis (-0.5..0.5 of its length),
        /// `back` runs the short axis away from the door (positive = away from it).
        static Vector3 RoomPoint(Transform t, Bounds local, Room room, float along, float back)
        {
            Vector3 l = local.center;
            float doorSide = room.longX ? Mathf.Sign(room.doorLocal.z == 0 ? 1 : room.doorLocal.z)
                                        : Mathf.Sign(room.doorLocal.x == 0 ? 1 : room.doorLocal.x);
            if (room.longX) { l.x += along * local.size.x; l.z -= doorSide * back * local.size.z; }
            else { l.z += along * local.size.z; l.x -= doorSide * back * local.size.x; }
            var p = t.TransformPoint(l);
            p.y = room.floor;
            if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
            return p;
        }

        static int AddLights(Transform building, Bounds local, Room room)
        {
            var root = new GameObject("BaseLights");
            Undo.RegisterCreatedObjectUndo(root, "Base lights");
            root.transform.SetParent(building, false);

            var tubeMat = LampMaterial();
            float height = Mathf.Min(room.ceiling - 0.45f, room.floor + LampHang);
            Vector3 size = Vector3.Scale(local.size, building.lossyScale);
            float range = Mathf.Clamp(0.55f * Mathf.Max(size.x, size.z) + 3f, 10f, 20f);

            int count = 0;
            for (int i = 0; i < 4; i++)   // 2 x 2 grid - URP Forward (mobile) lights at most 4 per object
            {
                Vector3 l = local.center + new Vector3(((i & 1) * 2 - 1) * 0.25f * local.size.x, 0f, ((i & 2) - 1) * 0.25f * local.size.z);
                var p = building.TransformPoint(l);
                p.y = height;

                var lamp = new GameObject($"Lamp_{i + 1}");
                lamp.transform.SetParent(root.transform, false);
                lamp.transform.position = p;
                lamp.transform.rotation = building.rotation * (room.longX ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f));

                var light = lamp.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = LampColor;
                light.intensity = LampIntensity;
                light.range = range;
                light.shadows = i == 0 ? LightShadows.Soft : LightShadows.None;   // one shadow caster keeps it cheap
                light.renderMode = LightRenderMode.ForcePixel;

                // Visible fixture: a hanging fluorescent tube.
                var tube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tube.name = "Tube";
                Object.DestroyImmediate(tube.GetComponent<Collider>());
                tube.transform.SetParent(lamp.transform, false);
                tube.transform.localPosition = Vector3.up * 0.1f;
                tube.transform.localScale = new Vector3(1.4f, 0.07f, 0.18f);
                var mr = tube.GetComponent<MeshRenderer>();
                mr.sharedMaterial = tubeMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                // Rod up to the ceiling when the lamp hangs well below it.
                float drop = room.ceiling - height;
                if (drop > 0.6f)
                {
                    var rod = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    rod.name = "Rod";
                    Object.DestroyImmediate(rod.GetComponent<Collider>());
                    rod.transform.SetParent(lamp.transform, false);
                    rod.transform.localPosition = Vector3.up * (0.1f + drop * 0.5f);
                    rod.transform.localScale = new Vector3(0.03f, drop, 0.03f);
                    var rr = rod.GetComponent<MeshRenderer>();
                    rr.sharedMaterial = RodMaterial();
                    rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                count++;
            }
            return count;
        }

        static Material RodMaterial()
        {
            string path = $"{BaseFolder}/Base_LampRod.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { name = "Base_LampRod" };
            mat.SetColor("_BaseColor", new Color(0.12f, 0.12f, 0.13f));
            mat.SetFloat("_Metallic", 0.6f);
            mat.SetFloat("_Smoothness", 0.4f);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static Material LampMaterial()
        {
            string path = $"{BaseFolder}/Base_LampTube.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
            {
                // Older installs made the tubes warm yellow; keep them in step with the lamp colour.
                mat.SetColor("_BaseColor", LampColor);
                mat.SetColor("_EmissionColor", LampColor * 4f);
                EditorUtility.SetDirty(mat);
                return mat;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Environment")) AssetDatabase.CreateFolder("Assets", "Environment");
            if (!AssetDatabase.IsValidFolder(BaseFolder)) AssetDatabase.CreateFolder("Assets/Environment", "Base");
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader) { name = "Base_LampTube" };
            mat.SetColor("_BaseColor", LampColor);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", LampColor * 4f);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
