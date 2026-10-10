using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using ZombieRush.Data;
using ZombieRush.Player;
using ZombieRush.Waves;
using ZombieRush.World;

namespace ZombieRush.EditorTools
{
    /// Zombie Rush > Level > Set Up Base, Zone + Zombie Paths
    ///
    ///   1. Player base: on a side of the city (the zone then grows across the map from
    ///      there), at the city centre, or wherever the "PlayerBase" marker was dragged.
    ///   2. Zone wall: swaps the dim cylinder for the animated ZoneBoundaryFX energy wall.
    ///   3. Zombie paths: bakes a walk map of the city (built-in AI module, no package)
    ///      so zombies route around buildings, and spawns are only used if they have a
    ///      walkable way to the player.
    /// Undoable with Ctrl+Z (the baked walk-map asset stays on disk; re-running replaces it).
    public static class ZoneTools
    {
        const string ZoneFolder = "Assets/Environment/Zone";
        const string ShaderName = "ZombieRush/ZoneWall";
        const string NavMeshPath = "Assets/Data/MainLevel_ZombieNavMesh.asset";
        const string BaseName = "PlayerBase";
        const int MaxExpansions = 16;

        [MenuItem("Zombie Rush/Level/Set Up Base, Zone + Zombie Paths", priority = 12)]
        public static void SetUp()
        {
            var zone = Object.FindAnyObjectByType<ZoneManager>(FindObjectsInactive.Include);
            if (zone == null)
            {
                EditorUtility.DisplayDialog("Set Up Zone", "Open the MainLevel scene first (no ZoneManager in the open scene).", "OK");
                return;
            }
            var scene = zone.gameObject.scene;
            var map = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Map");
            if (map == null)
            {
                EditorUtility.DisplayDialog("Set Up Zone", "No 'Map' in the scene - run Level > Install Final Map first.", "OK");
                return;
            }

            bool hasBase = zone.playerBase != null;
            int choice = EditorUtility.DisplayDialogComplex("Where is the player's base?",
                "Side of the city: the base sits near the edge and the safe zone grows across the whole map as you expand it (recommended).\n\n" +
                "City centre: the zone grows outward from the middle.\n\n" +
                (hasBase ? "Keep my base: use the PlayerBase marker where you placed it." : "Tip: afterwards you can drag the PlayerBase marker anywhere and re-run this with 'Keep my base'."),
                "Side of the city", "Cancel", hasBase ? "Keep my base" : "City centre");
            if (choice == 1) return;
            bool keep = choice == 2 && hasBase;
            bool centre = choice == 2 && !hasBase;

            Undo.SetCurrentGroupName("Set up base, zone and zombie paths");
            int undoGroup = Undo.GetCurrentGroup();

            // The map keeps its own sky, sun and post FX (no Day/Night profile on top).
            MapInstaller.UsePackEnvironment(scene, map);

            Physics.SyncTransforms();
            ArenaGround.Ignore = zone.boundaryVisual;
            var terrain = map.GetComponentInChildren<Terrain>();
            var city = MapInstaller.CityBounds(map);

            // 1. Base.
            Vector3 basePos;
            float clearance = 0f;
            if (keep)
            {
                basePos = zone.playerBase.position;
                if (ArenaGround.TryFindClearSpot(basePos, basePos.y, 8f, out var g)) basePos = g;
            }
            else if (centre)
            {
                basePos = MapInstaller.FindOpenSpot(city, terrain, out clearance);
            }
            else
            {
                // Open ground about 70% of the way out from the centre, so the base backs onto
                // the city edge and most of the map lies ahead of it.
                float half = 0.5f * Mathf.Min(city.size.x, city.size.z);
                basePos = MapInstaller.FindOpenSpot(city, terrain, half * 0.9f,
                    offset => Mathf.Abs(offset.magnitude / half - 0.7f) * 80f, out clearance);
            }

            var baseT = zone.playerBase;
            if (baseT == null)
            {
                var go = new GameObject(BaseName);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
                Undo.RegisterCreatedObjectUndo(go, "Create player base");
                baseT = go.transform;
            }
            Undo.RecordObject(baseT, "Set up zone");
            baseT.position = basePos;

            // Safe base (fixed size, zombies can't enter) with its starting gear. The fight
            // zone starts well outside it so zombies have room to spawn and approach.
            var safeBase = SetUpSafeBase(baseT, out int gearCount);
            Undo.RecordObject(zone, "Set up zone");
            zone.startRadius = Mathf.Max(zone.startRadius, safeBase.radius + ArenaSpawn.BaseMargin + 10f);

            float needed = MapInstaller.FarthestCorner(basePos, city);
            int steps = Mathf.Clamp(Mathf.CeilToInt((needed - zone.startRadius) / Mathf.Max(1f, zone.expandStep)), 1, MaxExpansions);

            Undo.RecordObject(zone, "Set up zone");
            zone.playerBase = baseT;
            zone.center = basePos;
            zone.maxRadius = zone.startRadius + steps * zone.expandStep;
            zone.deployPlayerAtCenter = true;
            EditorUtility.SetDirty(zone);

            // 2. Zone wall.
            var fx = SetUpWall(zone, scene);
            fx.SetZone(basePos, zone.startRadius, false);

            // 3. Zombie walk map, covering everything the zone can ever reach.
            var nav = BakeNavMesh(map, basePos, zone.maxRadius + 15f, out string navNote);
            if (nav != null)
            {
                var loader = zone.GetComponent<ArenaNavMesh>();
                if (loader == null) loader = Undo.AddComponent<ArenaNavMesh>(zone.gameObject);
                Undo.RecordObject(loader, "Set up zone");
                loader.data = nav;
                EditorUtility.SetDirty(loader);
            }

            // Player at the base; spawn pads around the starting zone.
            var player = Object.FindAnyObjectByType<PlayerHealth>(FindObjectsInactive.Include);
            if (player != null && ArenaGround.TryFindClearSpot(basePos, basePos.y, zone.startRadius * 0.8f, out var deploy))
            {
                Undo.RecordObject(player.transform, "Set up zone");
                player.transform.position = deploy + Vector3.up * 0.05f;
            }
            var waves = Object.FindAnyObjectByType<WaveManager>(FindObjectsInactive.Include);
            if (waves != null && waves.spawnPoints != null)
            {
                foreach (var pad in waves.spawnPoints) if (pad != null) Undo.RecordObject(pad, "Set up zone");
                waves.ArrangeSpawnPoints(basePos, zone.startRadius * waves.edgeRadiusFactor);
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = baseT.gameObject;
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.Frame(new Bounds(basePos, Vector3.one * zone.startRadius * 2.5f), false);

            string where = keep ? "kept where you placed it" : centre ? "at the city centre" : "on the side of the city";
            string summary =
                $"Player base {where} ({basePos.x:0}, {basePos.z:0}){(clearance > 0f ? $", ~{clearance:0} m of open ground" : "")}.\n" +
                $"Safe base: {safeBase.radius:0} m, zombies can't enter; {gearCount} gear pickups (weapons, armor, ammo, medkit). " +
                "Waves start when a player steps out.\n" +
                $"Fight zone starts {zone.startRadius:0} m, +{zone.expandStep:0} m per expansion ({zone.expandCost} coins), " +
                $"{steps} expansions to {zone.maxRadius:0} m - covers the whole city.\n" +
                navNote;
            Debug.Log("Zombie Rush: " + summary);
            EditorUtility.DisplayDialog("Base, zone and zombie paths ready", summary +
                "\n\nSave the scene (Ctrl+S) and press Play. To move the base later, drag 'PlayerBase' and run this again with 'Keep my base'.", "OK");
        }

        static ZoneBoundaryFX SetUpWall(ZoneManager zone, UnityEngine.SceneManagement.Scene scene)
        {
            if (zone.boundaryVisual == null)
            {
                var go = new GameObject("ZoneBoundaryVisual");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
                Undo.RegisterCreatedObjectUndo(go, "Create zone wall");
                zone.boundaryVisual = go.transform;
            }
            var visual = zone.boundaryVisual.gameObject;

            // The old cylinder: hide it and make sure nothing collides with it.
            if (visual.TryGetComponent<MeshRenderer>(out var oldRenderer) && oldRenderer.enabled)
            {
                Undo.RecordObject(oldRenderer, "Set up zone");
                oldRenderer.enabled = false;
            }
            if (visual.TryGetComponent<Collider>(out var oldCollider) && oldCollider.enabled)
            {
                Undo.RecordObject(oldCollider, "Set up zone");
                oldCollider.enabled = false;
            }

            var fx = visual.GetComponent<ZoneBoundaryFX>();
            if (fx == null) fx = Undo.AddComponent<ZoneBoundaryFX>(visual);
            Undo.RecordObject(fx, "Set up zone");
            fx.wallMaterial = LoadOrCreateMaterial("Zone_Wall", ground: false);
            fx.groundMaterial = LoadOrCreateMaterial("Zone_Ground", ground: true);
            EditorUtility.SetDirty(fx);

            // Re-create the parts with the new materials.
            fx.enabled = false;
            fx.enabled = true;
            return fx;
        }

        static readonly Color SafeColor = new Color(0.25f, 0.85f, 1f);   // GDD: cyan = safety
        static readonly Color GearColor = new Color(1f, 0.78f, 0.25f);   // GDD: gold = reward

        /// SafeBase on the PlayerBase marker, a cyan ring at its edge, and the starting
        /// gear (one rack per weapon the player doesn't start with, plus armor, ammo and a medkit).
        static SafeBase SetUpSafeBase(Transform baseT, out int gearCount)
        {
            var safeBase = baseT.GetComponent<SafeBase>();
            if (safeBase == null) safeBase = Undo.AddComponent<SafeBase>(baseT.gameObject);

            var ringT = baseT.Find("SafeRing");
            if (ringT == null)
            {
                var go = new GameObject("SafeRing");
                Undo.RegisterCreatedObjectUndo(go, "Create safe ring");
                Undo.SetTransformParent(go.transform, baseT, "Create safe ring");
                ringT = go.transform;
            }
            ringT.localPosition = Vector3.zero;
            var ring = ringT.GetComponent<ZoneBoundaryFX>();
            if (ring == null) ring = Undo.AddComponent<ZoneBoundaryFX>(ringT.gameObject);
            Undo.RecordObject(ring, "Set up safe base");
            ring.fixedRadius = safeBase.radius;
            ring.wallHeight = 3.5f;
            ring.warnNearEdge = false;
            ring.wallMaterial = LoadOrCreateMaterial("Zone_Safe_Wall", false, SafeColor, 1.8f);
            ring.groundMaterial = LoadOrCreateMaterial("Zone_Safe_Ground", true, SafeColor, 2.4f);
            ring.enabled = false;
            ring.enabled = true;

            Undo.RecordObject(safeBase, "Set up safe base");
            safeBase.ring = ring;
            EditorUtility.SetDirty(safeBase);

            // Gear, rebuilt fresh each run.
            var gearT = baseT.Find("BaseGear");
            if (gearT != null) Undo.DestroyObjectImmediate(gearT.gameObject);
            var gear = new GameObject("BaseGear");
            Undo.RegisterCreatedObjectUndo(gear, "Create base gear");
            Undo.SetTransformParent(gear.transform, baseT, "Create base gear");
            gear.transform.localPosition = Vector3.zero;

            var items = new List<(PickupKind kind, WeaponData weapon, string label, string icon)>();
            var weapons = Object.FindAnyObjectByType<PlayerWeaponController>(FindObjectsInactive.Include);
            if (weapons != null && weapons.weapons != null)
                for (int i = Mathf.Max(1, weapons.startingWeapons); i < weapons.weapons.Length; i++)
                {
                    var data = weapons.weapons[i] != null ? weapons.weapons[i].data : null;
                    if (data != null) items.Add((PickupKind.Weapon, data, data.weaponName.ToUpperInvariant(), null));
                }
            items.Add((PickupKind.Armor, null, "ARMOR", "armor"));
            items.Add((PickupKind.Ammo, null, "AMMO", "ammo"));
            items.Add((PickupKind.Medkit, null, "MEDKIT", "medkit"));

            var icons = UIAssetGenerator.Ensure();
            Physics.SyncTransforms();
            float ringRadius = safeBase.radius * 0.55f;
            for (int i = 0; i < items.Count; i++)
            {
                var (kind, weapon, label, iconName) = items[i];
                float a = (i + 0.5f) / items.Count * Mathf.PI * 2f;
                var pos = baseT.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ringRadius;
                if (Physics.Raycast(pos + Vector3.up * 3f, Vector3.down, out var hit, 10f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point;

                var go = new GameObject($"Gear_{label}");
                go.transform.SetParent(gear.transform, false);
                go.transform.position = pos;
                var col = go.AddComponent<SphereCollider>();
                col.isTrigger = true;
                col.radius = 1.1f;
                col.center = Vector3.up * 0.9f;

                var pickup = go.AddComponent<BasePickup>();
                pickup.kind = kind;
                pickup.weapon = weapon;
                pickup.label = label;
                pickup.amount = kind == PickupKind.Armor ? 50f : 60f;
                pickup.rechargeTime = kind == PickupKind.Weapon ? 0f : 25f;

                var beamGo = new GameObject("Beam");
                beamGo.transform.SetParent(go.transform, false);
                var beam = beamGo.AddComponent<ZoneBoundaryFX>();
                beam.fixedRadius = 0.7f;
                beam.wallHeight = 2.2f;
                beam.groundBandWidth = 0.5f;
                beam.sink = 0.1f;
                beam.warnNearEdge = false;
                beam.wallMaterial = LoadOrCreateMaterial("Gear_Wall", false, GearColor, 1.6f);
                beam.groundMaterial = LoadOrCreateMaterial("Gear_Ground", true, GearColor, 2.4f);
                pickup.beam = beam;

                var iconGo = new GameObject("Icon");
                iconGo.transform.SetParent(go.transform, false);
                iconGo.transform.localPosition = Vector3.up * 1.35f;
                iconGo.transform.localScale = Vector3.one * 0.55f;
                var sr = iconGo.AddComponent<SpriteRenderer>();
                sr.sprite = weapon != null && weapon.icon != null ? weapon.icon : icons.IconX(iconName ?? "ammo");
                sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                pickup.icon = iconGo.transform;
            }

            gearCount = items.Count;
            return safeBase;
        }

        static Material LoadOrCreateMaterial(string name, bool ground, Color? color = null, float? intensity = null)
        {
            string path = $"{ZoneFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"Zombie Rush: shader '{ShaderName}' not found (Assets/Environment/Zone/ZoneWall.shader).");
                return null;
            }
            if (!AssetDatabase.IsValidFolder(ZoneFolder)) AssetDatabase.CreateFolder("Assets/Environment", "Zone");
            mat = new Material(shader) { name = name };
            mat.SetFloat("_IsGround", ground ? 1f : 0f);
            if (ground) mat.SetFloat("_Intensity", 2.6f);
            if (color.HasValue) mat.SetColor("_Color", color.Value);
            if (intensity.HasValue) mat.SetFloat("_Intensity", intensity.Value);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// Bakes the walk map from the map's colliders (terrain + buildings) inside a square
        /// around the base, and saves it as an asset the ArenaNavMesh component loads.
        static NavMeshData BakeNavMesh(GameObject map, Vector3 center, float radius, out string note)
        {
            var sources = new List<NavMeshBuildSource>();
            UnityEngine.AI.NavMeshBuilder.CollectSources(map.transform, ~0, NavMeshCollectGeometry.PhysicsColliders, 0,
                new List<NavMeshBuildMarkup>(), sources);
            if (sources.Count == 0)
            {
                note = "Zombie paths: the map has no colliders, so no walk map was baked.";
                return null;
            }

            var settings = NavMesh.GetSettingsByID(0); // Humanoid
            settings.agentRadius = 0.45f;
            settings.agentHeight = 1.8f;
            settings.agentClimb = 0.45f;
            settings.agentSlope = 40f;
            settings.minRegionArea = 4f;
            settings.overrideVoxelSize = true;
            settings.voxelSize = 0.15f;

            var bounds = new Bounds(center, new Vector3(radius * 2f, 80f, radius * 2f));
            EditorUtility.DisplayProgressBar("Zombie paths", $"Baking the walk map ({sources.Count} pieces)...", 0.5f);
            NavMeshData data;
            try
            {
                data = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            if (data == null)
            {
                note = "Zombie paths: bake failed - zombies fall back to steering straight at the player.";
                return null;
            }

            data.name = "MainLevel_ZombieNavMesh";
            if (AssetDatabase.LoadAssetAtPath<NavMeshData>(NavMeshPath) != null) AssetDatabase.DeleteAsset(NavMeshPath);
            AssetDatabase.CreateAsset(data, NavMeshPath);
            AssetDatabase.SaveAssets();

            note = $"Zombie paths: walk map baked from {sources.Count} colliders ({NavMeshPath}). Zombies route around buildings, " +
                   "and each zombie spawns on open ground with a walkable way to you.";
            return data;
        }
    }
}
