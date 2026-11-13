using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using ZombieRush.Data;
using ZombieRush.World;
using Placer = ZombieRush.EditorTools.CityPropsTools.Placer;

namespace ZombieRush.EditorTools
{
    /// Zombie Rush > Level > Build Survival Layout (marker mode "survival" in Temp/ZR_RunMapCleanup)
    ///   1. building variety: swaps a share of the most repeated building for other pack buildings of a
    ///      similar size, and removes a few copies to open plazas,
    ///   2. base compound round the safe base (Map/BaseCompound): ring wall with gates, searchlight towers,
    ///      generators, floodlights, tents, a campfire, supply piles,
    ///   3. the city (Map/SurvivalLayout): plazas with a bonfire and trees, more bonfires, greenery, ivy,
    ///      climbable lookouts (scaffold towers, sniper nests, nests joined by plank bridges), street cover,
    ///   4. re-bakes zombie paths (stairs and bridges included) and saves.
    /// All numbers come from Assets/Data/SurvivalLayoutConfig.asset. Running it again rebuilds 2 and 3;
    /// 1 only swaps buildings that are still the original repeated type.
    static class SurvivalLayoutTools
    {
        const string ConfigPath = "Assets/Data/SurvivalLayoutConfig.asset";
        const string PackPrefabs = "Assets/PostApocalypticBuildings/URP/Prefabs";
        const string Repeated = "building9";
        const string CompoundRoot = "BaseCompound";
        const string LayoutRoot = "SurvivalLayout";
        const string PlazaMarkers = "PlazaMarkers";

        [MenuItem("Zombie Rush/Level/Build Survival Layout")]
        static void Menu() => Debug.Log("Zombie Rush: " + Build());

        static SurvivalLayoutConfig Config()
        {
            var c = AssetDatabase.LoadAssetAtPath<SurvivalLayoutConfig>(ConfigPath);
            if (c != null) return c;
            c = ScriptableObject.CreateInstance<SurvivalLayoutConfig>();
            AssetDatabase.CreateAsset(c, ConfigPath);
            AssetDatabase.SaveAssets();
            return c;
        }

        static GameObject P(string name) => CityPropsTools.Prefab(name);

        /// Puts every value in SurvivalLayoutConfig back to the defaults written in its script.
        public static string ResetConfig()
        {
            var cfg = Config();
            var fresh = ScriptableObject.CreateInstance<SurvivalLayoutConfig>();
            EditorUtility.CopySerialized(fresh, cfg);
            Object.DestroyImmediate(fresh);
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();
            return $"SurvivalLayoutConfig reset to script defaults: trees {cfg.trees}, dead trees {cfg.deadTrees}, shrubs {cfg.bushes}, weeds {cfg.grassTufts}, hanging vines {cfg.hangingVineShare}, terrain grass {cfg.terrainGrass}.";
        }

        public static string Build()
        {
            var cfg = Config();
            var zone = Object.FindAnyObjectByType<ZoneManager>(FindObjectsInactive.Include);
            var safe = Object.FindAnyObjectByType<SafeBase>(FindObjectsInactive.Include);
            var mapT = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(t => t.parent == null && t.name == "Map");
            if (zone == null || safe == null || mapT == null) return "FAILED: open MainLevel first.";
            if (P("ZR_Tent_Military") == null) return "FAILED: build the prefabs first (props build).";

            var sb = new StringBuilder();
            var rnd = new System.Random(cfg.seed);
            Undo.SetCurrentGroupName("Build survival layout");
            int undo = Undo.GetCurrentGroup();

            sb.AppendLine(SwapBuildings(mapT, safe, cfg, rnd, out var plazaCentres));
            sb.AppendLine(MapCleanupTools.SwapCleanBuildings(false));
            Physics.SyncTransforms();
            sb.AppendLine(BaseCompound(mapT, safe, cfg, rnd));
            sb.AppendLine(City(mapT, zone, safe, cfg, rnd, plazaCentres));

            Undo.CollapseUndoOperations(undo);
            return sb + MapCleanupTools.RebakeAndSave(mapT);
        }

        // ------------------------------------------------------------ building variety

        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static string BaseName(string n) { int k = n.IndexOf(" ("); return k > 0 ? n.Substring(0, k) : n; }

        static string SwapBuildings(Transform mapT, SafeBase safe, SurvivalLayoutConfig cfg, System.Random rnd, out List<Vector3> plazas)
        {
            plazas = new List<Vector3>();
            // runs once: the plaza spots are kept under Map/PlazaMarkers and reused on later runs
            var markers = mapT.Find(PlazaMarkers);
            if (markers != null)
            {
                foreach (Transform m in markers) plazas.Add(m.position);
                return $"Building variety: already done earlier ({plazas.Count} plazas kept).";
            }
            var copies = mapT.Cast<Transform>().Where(t => BaseName(t.name) == Repeated).ToList();
            if (copies.Count == 0) return "Building variety: no copies of " + Repeated + " left (already done).";
            var refSize = WorldBounds(copies[0].gameObject).size;

            // candidates: other pack buildings no bigger than the copy's footprint, not skyscrapers, not rubble
            var candidates = new List<(GameObject prefab, Vector3 size)>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PackPrefabs }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!name.StartsWith("building") || name.Contains("rubble") || name == Repeated ||
                    name.StartsWith("building2") || name.StartsWith("building10")) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var tmp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var s = WorldBounds(tmp).size;
                Object.DestroyImmediate(tmp);
                float big = Mathf.Max(s.x, s.z), small = Mathf.Min(s.x, s.z);
                if (big <= Mathf.Max(refSize.x, refSize.z) * 1.1f && small >= 6f && s.y >= 5f && s.y <= 60f) candidates.Add((prefab, s));
            }
            if (candidates.Count == 0) return "Building variety: no other building fits the footprint.";

            var order = copies.OrderBy(_ => rnd.Next()).ToList();
            int swaps = Mathf.RoundToInt(copies.Count * cfg.swapShare), done = 0, k = 0;
            var used = new Dictionary<string, int>();
            foreach (var old in order.Take(swaps))
            {
                var ob = WorldBounds(old.gameObject);
                ob.Expand(new Vector3(2f, 0f, 2f));
                for (int tryN = 0; tryN < candidates.Count * 4; tryN++)
                {
                    var (prefab, _) = candidates[(k + tryN / 4) % candidates.Count];
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, mapT);
                    go.transform.SetPositionAndRotation(old.position, old.rotation * Quaternion.Euler(0f, (tryN % 4) * 90f + rnd.Next(2) * 180f, 0f));
                    go.transform.localScale = old.localScale;
                    var nb = WorldBounds(go);
                    if (nb.min.x >= ob.min.x && nb.max.x <= ob.max.x && nb.min.z >= ob.min.z && nb.max.z <= ob.max.z)
                    {
                        go.name = $"{prefab.name} (zr{done})";
                        foreach (var t in go.GetComponentsInChildren<Transform>(true))
                            GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                        Undo.RegisterCreatedObjectUndo(go, "Build survival layout");
                        Undo.DestroyObjectImmediate(old.gameObject);
                        used[prefab.name] = used.TryGetValue(prefab.name, out var n) ? n + 1 : 1;
                        done++;
                        k = (k + tryN / 4 + 1) % candidates.Count;
                        break;
                    }
                    Object.DestroyImmediate(go);
                }
            }

            // plazas: remove a few remaining copies, spread out, mid-distance from the base
            Physics.SyncTransforms();
            var remaining = mapT.Cast<Transform>().Where(t => BaseName(t.name) == Repeated).OrderBy(_ => rnd.Next()).ToList();
            foreach (var t in remaining)
            {
                if (plazas.Count >= cfg.plazas) break;
                var c = WorldBounds(t.gameObject).center;
                float d = Flat(c - safe.Center);
                if (d < cfg.plazaRadius.x || d > cfg.plazaRadius.y || plazas.Any(p => Flat(p - c) < cfg.plazaSpacing)) continue;
                plazas.Add(new Vector3(c.x, 0f, c.z));
                Undo.DestroyObjectImmediate(t.gameObject);
            }
            var mk = new GameObject(PlazaMarkers).transform;
            mk.SetParent(mapT, false);
            Undo.RegisterCreatedObjectUndo(mk.gameObject, "Build survival layout");
            foreach (var c in plazas) { var m = new GameObject("Plaza").transform; m.SetParent(mk, false); m.position = c; }
            return $"Building variety: {copies.Count} copies of {Repeated}; swapped {done} for {string.Join(", ", used.Select(u => $"{u.Key} x{u.Value}"))}; " +
                   $"removed {plazas.Count} to open plazas. Candidates: {candidates.Count}.";
        }

        // ------------------------------------------------------------ base compound

        static Placer NewPlacer(Transform parent, string rootName, SafeBase safe, float minR, float maxR, System.Random rnd)
        {
            var old = parent.Find(rootName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            Physics.SyncTransforms();
            var root = new GameObject(rootName).transform;
            root.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Build survival layout");
            var p = new Placer { baseC = safe.Center, baseR = safe.radius, minR = minR, maxR = maxR, rnd = rnd, root = root, cfg = CityPropsTools.Config() };
            foreach (var c in safe.GetComponentsInChildren<Collider>(true)) if (c.isTrigger) p.ignore.Add(c);
            return p;
        }

        /// Turns a prop so the side it was built to face (Blender +Y) points along dir.
        static Quaternion Facing(Vector3 dir)
        {
            dir.y = 0f;
            var q = Quaternion.FromToRotation(CityPropsTools.BlenderForward(), dir.normalized);
            return Quaternion.Euler(0f, q.eulerAngles.y, 0f);
        }

        /// The footprint of a prefab placed at g with rot: centre and size (its pivot is not always in the middle).
        static bool FitsAt(Placer p, GameObject prefab, Vector3 g, Quaternion rot, float gap)
        {
            var b = CityPropsTools.PrefabBounds(prefab);
            var centre = g + rot * new Vector3(b.center.x, 0f, b.center.z);
            return p.Clear(centre, rot, b.size, gap);
        }

        static Vector3 Dir(float deg) => new Vector3(Mathf.Sin(deg * Mathf.Deg2Rad), 0f, Mathf.Cos(deg * Mathf.Deg2Rad));

        static string BaseCompound(Transform mapT, SafeBase safe, SurvivalLayoutConfig cfg, System.Random rnd)
        {
            // the earlier sandbag/barrier ring from Place City Props is replaced by the compound
            var oldDef = mapT.Find("CityProps/BaseDefences");
            if (oldDef != null) Undo.DestroyObjectImmediate(oldDef.gameObject);
            var sb = new StringBuilder("Base compound: ");
            float ringR = safe.radius + cfg.wallOffset;

            // clear the compound: buildings standing on the wall line go (the HQ stays), with whatever sits on them
            var keep = new HashSet<string> { CompoundRoot, LayoutRoot, PlazaMarkers, "CityProps" };
            var hq = safe.building;
            var cleared = new List<string>();
            var gone = new List<Bounds>();
            foreach (var t in mapT.Cast<Transform>().ToList())
            {
                if (keep.Contains(t.name) || (hq != null && (t == hq || hq.IsChildOf(t))) || t.GetComponentInChildren<Renderer>() == null) continue;
                if (t.GetComponentInChildren<Terrain>() != null) continue;
                var k = BaseName(t.name);
                if (!k.StartsWith("building") || k.Contains("rubble")) continue;
                var b = WorldBounds(t.gameObject);
                var closest = b.ClosestPoint(new Vector3(safe.Center.x, b.center.y, safe.Center.z));
                if (Flat(closest - safe.Center) > ringR + cfg.clearMargin) continue;
                gone.Add(b);
                cleared.Add(t.name);
                Undo.DestroyObjectImmediate(t.gameObject);
            }
            foreach (var t in mapT.Cast<Transform>().ToList()) // rooftop units, antennas and junk that sat on them
            {
                if (keep.Contains(t.name) || t.GetComponentInChildren<Renderer>() == null || t.GetComponentInChildren<Terrain>() != null) continue;
                if (hq != null && (t == hq || hq.IsChildOf(t))) continue;
                var c = WorldBounds(t.gameObject).center;
                if (gone.Any(b => b.Contains(c))) { cleared.Add(t.name); Undo.DestroyObjectImmediate(t.gameObject); }
            }
            if (cleared.Count > 0) sb.Append($"cleared {string.Join(", ", cleared)}; ");
            Physics.SyncTransforms();
            var p = NewPlacer(mapT, CompoundRoot, safe, 0f, safe.radius + 10f, rnd);
            p.root.gameObject.AddComponent<ShootThrough>(); // bullets fly through the compound so players in the base can hit zombies outside

            // gates where the streets leave the base: the most open directions, spread out
            var scored = Enumerable.Range(0, 24).Select(i => i * 15f)
                .Select(a => (a, open: p.Ground(safe.Center + Dir(a) * (ringR + 2f), out var g) ? p.Open(g, Dir(a), 90f) : 0f))
                .OrderByDescending(x => x.open).ToList();
            var gates = new List<float>();
            foreach (var s in scored)
                if (gates.Count < cfg.gates && gates.All(gA => Mathf.Abs(Mathf.DeltaAngle(gA, s.a)) >= 360f / cfg.gates * 0.6f)) gates.Add(s.a);
            gates.Sort();
            float gateHalf = cfg.gateWidth * 0.5f / ringR * Mathf.Rad2Deg;

            // ring wall: concrete barriers, every third piece sandbags
            var barrier = P("ZR_Hesco"); var bags = P("ZR_SandbagWall_Tall");
            int pieces = 0, n = 0;
            var blockers = new Dictionary<string, int>();
            var hs = CityPropsTools.PrefabSize(barrier);
            float step = (Mathf.Max(hs.x, hs.z) + 0.05f) / ringR * Mathf.Rad2Deg;
            for (float a = 0f; a < 360f; a += step, n++)
            {
                if (gates.Any(gA => Mathf.Abs(Mathf.DeltaAngle(gA, a)) < gateHalf + step * 0.5f)) continue;
                var prefab = n % (cfg.hescoPerSandbag + 1) == cfg.hescoPerSandbag ? bags : barrier;
                if (!p.Ground(safe.Center + Dir(a) * ringR, out var g))
                {
                    var at = safe.Center + Dir(a) * ringR;
                    var top = Physics.RaycastAll(new Vector3(at.x, 300f, at.z), Vector3.down, 600f, ~0, QueryTriggerInteraction.Ignore).OrderByDescending(h => h.point.y).FirstOrDefault();
                    var k = "ground under " + (top.collider != null ? PathOf(top.collider.transform) : "nothing");
                    blockers[k] = blockers.TryGetValue(k, out var bn0) ? bn0 + 1 : 1;
                    continue;
                }
                var rot = CityPropsTools.AlongAxis(prefab, Vector3.Cross(Vector3.up, Dir(a)));
                var size = CityPropsTools.PrefabSize(prefab) * 0.9f;
                if (!p.Clear(g, rot, size, -0.05f))
                {
                    foreach (var c in Physics.OverlapBox(g + Vector3.up * (size.y / 2f + 0.25f), size / 2f, rot, ~0, QueryTriggerInteraction.Ignore))
                        if (!(c is TerrainCollider)) { var k = PathOf(c.transform); blockers[k] = blockers.TryGetValue(k, out var bn) ? bn + 1 : 1; }
                    continue;
                }
                p.Put(prefab, g, rot, "RingWall", 0.5f); pieces++;
            }
            // big burning drums either side of each gate
            var drum = P("ZR_Barrel_Fire");
            int drums = 0;
            foreach (var gA in gates)
                foreach (float side in new[] { -1f, 1f })
                {
                    var at = safe.Center + Dir(gA + side * (gateHalf + 1.5f)) * (ringR - 1.6f);
                    if (!p.Ground(at, out var g) || !p.Clear(g, Quaternion.identity, CityPropsTools.PrefabSize(drum) * cfg.gateBarrelScale, 0.05f)) continue;
                    var go = p.Put(drum, g, Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f), "RingWall", 1f);
                    go.transform.localScale = Vector3.one * cfg.gateBarrelScale;
                    drums++;
                }
            sb.Append($"{drums} gate fire drums; ");
            sb.Append($"ring wall {pieces} pieces with {gates.Count} gates (blocked by: {string.Join(", ", blockers.OrderByDescending(x => x.Value).Take(6).Select(x => $"{x.Key} x{x.Value}"))}); ");

            // searchlight towers between the gates, beams facing out; a generator by each of the first few
            var tower = P("ZR_SearchlightTower"); var gen = P("ZR_Generator");
            int towers = 0, gens = 0;
            for (int i = 0; i < gates.Count && towers < cfg.searchlightTowers; i++)
            {
                float a0 = gates[i], a1 = gates[(i + 1) % gates.Count] + (i + 1 == gates.Count ? 360f : 0f);
                float mid = (a0 + a1) * 0.5f;
                bool placed = false;
                foreach (float da in new[] { 0f, -6f, 6f, -12f, 12f })
                    foreach (float dr in new[] { 0f, -2f, 2f, -4f })
                    {
                        if (placed) break;
                        var dir = Dir(mid + da);
                        if (!p.Ground(safe.Center + dir * (ringR - cfg.towerInset + dr), out var g)) continue;
                        var rot = Facing(dir);
                        if (!FitsAt(p, tower, g, rot, 0.2f)) continue;
                        p.Put(tower, g, rot, "Towers", 3f); towers++; placed = true;
                        if (gens < cfg.generators)
                        {
                            var side = Vector3.Cross(Vector3.up, dir);
                            foreach (var s in new[] { side, -side })
                            {
                                if (!p.Ground(g + s * 4f, out var gg)) continue;
                                var r2 = CityPropsTools.AlongAxis(gen, dir);
                                if (!p.Clear(gg, r2, CityPropsTools.PrefabSize(gen), 0.3f)) continue;
                                p.Put(gen, gg, r2, "Power", 1f); gens++; break;
                            }
                        }
                    }
            }
            sb.Append($"{towers} searchlight towers, {gens} generators; ");

            // floodlights inside each gate, lighting the yard
            var flood = P("ZR_Floodlight");
            int floods = 0;
            foreach (var gA in gates.Take(cfg.floodlights))
                foreach (float off in new[] { 1f, -1f })
                {
                    var dir = Dir(gA + off * (gateHalf + 7f));
                    if (!p.Ground(safe.Center + dir * (ringR - 2.5f), out var g)) continue;
                    var rot = Facing(-dir);
                    if (!p.Clear(g, rot, new Vector3(1.2f, 3.3f, 1.2f), 0.2f)) continue;
                    p.Put(flood, g, rot, "Lights", 1f); floods++; break;
                }
            sb.Append($"{floods} floodlights; ");

            // tents in the yard, door to the middle
            var tent = P("ZR_Tent_Military");
            int tents = 0;
            for (int tries = 0; tries < 400 && tents < cfg.tents; tries++)
            {
                float a = (float)rnd.NextDouble() * 360f;
                if (gates.Any(gA => Mathf.Abs(Mathf.DeltaAngle(gA, a)) < gateHalf + 8f)) continue;
                var dir = Dir(a);
                float r = Mathf.Lerp(safe.radius - 9f, ringR - 4f, (float)rnd.NextDouble());
                if (!p.Ground(safe.Center + dir * r, out var g)) continue;
                var rot = Facing(-dir);
                if (!FitsAt(p, tent, g, rot, 0.6f)) continue;
                p.Put(tent, g, rot, "Tents", 3f); tents++;
            }
            sb.Append($"{tents} tents; ");

            // campfire on the most open yard spot, crates round it as seats
            var fire = P("ZR_Campfire"); var crate = P("ZR_Crate");
            Vector3 best = Vector3.zero; float bestOpen = -1f;
            for (int i = 0; i < 160; i++)
            {
                float a = (float)rnd.NextDouble() * 360f, r = Mathf.Lerp(safe.radius * 0.4f, ringR - 3f, (float)rnd.NextDouble());
                if (!p.Ground(safe.Center + Dir(a) * r, out var g)) continue;
                float open = 0f;
                for (float rad = 0.5f; rad < 8f; rad += 0.5f)
                {
                    if (Physics.OverlapSphere(g + Vector3.up * 1.0f, rad, ~0, QueryTriggerInteraction.Ignore).Any(c => !(c is TerrainCollider))) break;
                    open = rad;
                }
                if (open > bestOpen) { bestOpen = open; best = g; }
            }
            bool campfire = false;
            if (bestOpen >= 2.5f)
            {
                p.Put(fire, best, Quaternion.identity, "Campfire", 2f); campfire = true;
                for (int i = 0; i < 4; i++)
                {
                    var q = best + Dir(i * 90f + 45f) * 2.2f;
                    if (p.Ground(q, out var gq) && p.Clear(gq, Quaternion.identity, CityPropsTools.PrefabSize(crate), 0.05f))
                        p.Put(crate, gq, Quaternion.Euler(0f, (float)rnd.NextDouble() * 30f, 0f), "Campfire", 0.5f);
                }
            }
            sb.Append(campfire ? $"campfire ({bestOpen:0.0} m clear); " : "no room for a campfire; ");

            // supply piles: crate stacks and ammo boxes along the inside of the wall
            var supplies = new[] { P("ZR_CrateStack"), P("ZR_AmmoBoxes"), P("ZR_Crate") };
            int piles = 0;
            for (int tries = 0; tries < 300 && piles < cfg.supplyPiles; tries++)
            {
                float a = (float)rnd.NextDouble() * 360f;
                if (gates.Any(gA => Mathf.Abs(Mathf.DeltaAngle(gA, a)) < gateHalf + 3f)) continue;
                if (!p.Ground(safe.Center + Dir(a) * (ringR - 1.8f), out var g)) continue;
                var prefab = supplies[rnd.Next(supplies.Length)];
                var rot = CityPropsTools.AlongAxis(prefab, Vector3.Cross(Vector3.up, Dir(a)));
                if (!p.Clear(g, rot, CityPropsTools.PrefabSize(prefab), 0.2f)) continue;
                p.Put(prefab, g, rot, "Supplies", 1f); piles++;
            }
            sb.Append($"{piles} supply piles.");
            return sb.ToString();
        }

        // ------------------------------------------------------------ the city

        static bool IsBuilding(Collider c, Transform mapT)
        {
            var t = c.transform;
            while (t.parent != null && t.parent != mapT) t = t.parent;
            if (t.parent != mapT) return false;
            var k = BaseName(t.name);
            return k.StartsWith("building") && !k.Contains("rubble");
        }

        static string City(Transform mapT, ZoneManager zone, SafeBase safe, SurvivalLayoutConfig cfg, System.Random rnd, List<Vector3> plazaCentres)
        {
            var city = CityPropsTools.Config();
            var p = NewPlacer(mapT, LayoutRoot, safe, safe.radius + cfg.wallOffset + 10f, zone.maxRadius - city.edgeMargin, rnd);
            var sb = new StringBuilder("City: ");
            var bonfire = P("ZR_Bonfire");
            var deadTreesP = new[] { P("ZR_TreeBare"), P("ZR_TreeBare_B") };
            var liveTrees = cfg.trees > 0 ? new[] { P("ZR_LeafyTree"), P("ZR_LeafyTree_B") } : deadTreesP;
            var bush = P("ZR_LeafyBush"); var bushB = P("ZR_LeafyBush_B"); var bushDry = P("ZR_DeadShrub");
            var grasses = new[] { P("ZR_WildGrass"), P("ZR_WildGrass_Tall"), P("ZR_WildGrass"), P("ZR_DryWeeds") };
            var nest = P("ZR_SniperNest"); var nestOpen = P("ZR_SniperNest_Open"); var bridge = P("ZR_PlankBridge"); var scaffold = P("ZR_ScaffoldTower");
            var crate = P("ZR_Crate");
            float Rf() => (float)rnd.NextDouble();
            Quaternion Yaw() => Quaternion.Euler(0f, Rf() * 360f, 0f);

            // plazas: bonfire in the middle, seats, trees and bushes round it, a lookout on some
            int plazaItems = 0, plazaFires = 0;
            for (int i = 0; i < plazaCentres.Count; i++)
            {
                if (!p.Ground(plazaCentres[i], out var c)) continue;
                // bonfire on the clear spot nearest the plaza centre
                bool lit = false;
                for (float r = 0f; r <= 9f && !lit; r += 1.5f)
                    for (int a = 0; a < (r == 0f ? 1 : 8) && !lit; a++)
                    {
                        if (!p.Ground(c + Dir(a * 45f) * r, out var gq) || !p.Clear(gq, Quaternion.identity, new Vector3(2.5f, 1.2f, 2.5f), 0.5f)) continue;
                        p.Put(bonfire, gq, Yaw(), "Plazas", 6f); plazaFires++; lit = true; c = gq;
                    }
                for (int s = 0; s < 3; s++)
                {
                    var q = c + Dir(s * 120f + 30f) * 3f;
                    if (p.Ground(q, out var gq) && p.Clear(gq, Quaternion.identity, Vector3.one, 0.05f)) { p.Put(crate, gq, Yaw(), "Plazas", 0.5f); plazaItems++; }
                }
                for (int t = 0; t < 5; t++)
                {
                    var q = c + Dir(Rf() * 360f) * Mathf.Lerp(6f, 12f, Rf());
                    var pf = t < 3 ? liveTrees[rnd.Next(liveTrees.Length)] : deadTreesP[rnd.Next(deadTreesP.Length)];
                    if (p.Ground(q, out var gq) && p.Clear(gq, Quaternion.identity, new Vector3(0.6f, 5f, 0.6f), 1.2f)) { p.Put(pf, gq, Yaw(), "Plazas", 1f); plazaItems++; }
                }
                for (int b = 0; b < 8; b++)
                {
                    var q = c + Dir(Rf() * 360f) * Mathf.Lerp(4f, 13f, Rf());
                    if (p.Ground(q, out var gq) && p.Clear(gq, Quaternion.identity, new Vector3(1f, 0.8f, 1f), 0.1f)) { p.Put(b % 4 == 0 ? bushDry : (b % 2 == 0 ? bushB : bush), gq, Yaw(), "Plazas", 0.5f); plazaItems++; }
                }
                for (int gN = 0; gN < 24; gN++)
                {
                    var q = c + Dir(Rf() * 360f) * Mathf.Lerp(1.5f, 14f, Rf());
                    if (p.Ground(q, out var gq)) { p.Put(grasses[rnd.Next(grasses.Length)], gq, Yaw(), "Plazas", 0f); plazaItems++; }
                }
                if (i % 2 == 0)
                    foreach (float a in new[] { 0f, 90f, 180f, 270f })
                    {
                        var q = c + Dir(a + Rf() * 30f) * 13f;
                        var rot = Facing(-Dir(a));
                        if (p.Ground(q, out var gq) && FitsAt(p, nest, gq, rot, 0.4f)) { p.Put(nest, gq, rot, "Lookouts", 4f); plazaItems++; break; }
                    }
            }
            sb.Append($"{plazaCentres.Count} plazas ({plazaFires} bonfires, {plazaItems} pieces); ");

            // more bonfires on open ground
            int fires = 0;
            for (int tries = 0; tries < cfg.extraBonfires * 80 && fires < cfg.extraBonfires; tries++)
            {
                var q = p.RandomPoint(p.minR, p.maxR);
                if (plazaCentres.Any(c => Flat(c - q) < cfg.bonfireSpacing) || !p.FarFromOthers(q, cfg.bonfireSpacing * 0.5f) || !p.Ground(q, out var g)) continue;
                if (!p.Clear(g, Quaternion.identity, new Vector3(2.5f, 1.2f, 2.5f), 1.5f)) continue;
                p.Put(bonfire, g, Yaw(), "Fires", cfg.bonfireSpacing * 0.5f); fires++;
            }
            sb.Append($"{fires} more bonfires; ");


            // climbable lookouts
            int scaffolds = 0, nests = 0, pairs = 0;
            for (int tries = 0; tries < cfg.scaffoldTowers * 150 && scaffolds < cfg.scaffoldTowers; tries++)
            {
                var q = p.RandomPoint(p.minR, p.maxR);
                if (!p.FarFromOthers(q, 30f) || !p.Ground(q, out var g) || !p.NearWall(g, 9f, out var wall, out float d) || d < 2f) continue;
                var rot = Facing(wall); // back of the tower to the wall, stairs to the street
                if (!FitsAt(p, scaffold, g, rot, 0.6f)) continue;
                p.Put(scaffold, g, rot, "Lookouts", 15f); scaffolds++;
            }
            for (int tries = 0; tries < cfg.sniperNests * 150 && nests < cfg.sniperNests; tries++)
            {
                var q = p.RandomPoint(p.minR, p.maxR);
                if (!p.FarFromOthers(q, 30f) || !p.Ground(q, out var g)) continue;
                var rot = Facing(-p.StreetAxis(g)) * Quaternion.Euler(0f, rnd.Next(2) * 180f, 0f);
                if (!FitsAt(p, nest, g, rot, 0.6f)) continue;
                p.Put(nest, g, rot, "Lookouts", 15f); nests++;
            }
            // pairs: two open-sided nests facing each other across a 10 m plank bridge
            var right = Vector3.Cross(Vector3.up, CityPropsTools.BlenderForward());
            float apart = 10f + 2.9f; // bridge length + one deck width (each deck is 2.9 m)
            for (int tries = 0; tries < cfg.bridgedPairs * 200 && pairs < cfg.bridgedPairs; tries++)
            {
                var q = p.RandomPoint(p.minR, p.maxR);
                if (!p.FarFromOthers(q, 35f) || !p.Ground(q, out var ga)) continue;
                var rotA = Quaternion.Euler(0f, Rf() * 360f, 0f);
                var across = rotA * right;
                if (!p.Ground(ga + across * apart, out var gb) || Mathf.Abs(gb.y - ga.y) > 0.25f) continue;
                var rotB = rotA * Quaternion.Euler(0f, 180f, 0f);
                if (!FitsAt(p, nestOpen, ga, rotA, 0.4f) || !FitsAt(p, nestOpen, gb, rotB, 0.4f)) continue;
                var mid = (ga + gb) * 0.5f;
                // nothing in the bridge's way at deck height
                if (Physics.OverlapBox(mid + Vector3.up * 4.6f, new Vector3(0.9f, 0.6f, 4.5f), Facing(across), ~0, QueryTriggerInteraction.Ignore).Any(c => !(c is TerrainCollider))) continue;
                p.Put(nestOpen, ga, rotA, "Lookouts", 8f);
                p.Put(nestOpen, gb, rotB, "Lookouts", 8f);
                var deckTop = Mathf.Max(ga.y, gb.y) + 4.06f;
                p.Put(bridge, new Vector3(mid.x, deckTop, mid.z), Facing(across), "Lookouts", 6f);
                pairs++;
            }
            sb.Append($"lookouts {scaffolds} scaffold towers, {nests} sniper nests, {pairs} bridged pairs; ");

            // street cover: small clusters across the street, keeping a walking gap
            var covers = new[] { P("ZR_ConcreteBlock"), P("ZR_LowWall_Brick"), P("ZR_LowWall_Brick_Short"), P("ZR_CrateStack"), P("ZR_Crate"), P("ZR_TireStack"), P("ZR_AmmoBoxes") };
            int clusters = 0, coverPieces = 0;
            for (int tries = 0; tries < cfg.coverClusters * 80 && clusters < cfg.coverClusters; tries++)
            {
                var q = p.RandomPoint(p.minR, p.maxR);
                if (!p.FarFromOthers(q, 10f) || !p.Ground(q, out var g)) continue;
                var axis = p.StreetAxis(g);
                var across = Vector3.Cross(Vector3.up, axis);
                int n = 1 + rnd.Next(3), got = 0;
                for (int k = 0; k < n; k++)
                {
                    var prefab = covers[rnd.Next(covers.Length)];
                    var gq = g + across * ((k - (n - 1) * 0.5f) * 2.4f) + axis * (Rf() - 0.5f) * 1.2f;
                    if (!p.Ground(gq, out gq)) continue;
                    var rot = CityPropsTools.AlongAxis(prefab, across) * Quaternion.Euler(0f, (Rf() - 0.5f) * 20f, 0f);
                    if (!p.Clear(gq, rot, CityPropsTools.PrefabSize(prefab), k == 0 ? city.walkGap : 0.3f)) continue;
                    p.Put(prefab, gq, rot, "Cover", 1.5f); got++;
                }
                if (got > 0) { clusters++; coverPieces += got; }
            }
            // razor-wire fence runs along walls
            var fence = P("ZR_RazorFence");
            int runs = 0, fences = 0;
            for (int tries = 0; tries < cfg.razorFenceRuns * 120 && runs < cfg.razorFenceRuns; tries++)
            {
                var q = p.RandomPoint(p.minR, p.maxR);
                if (!p.FarFromOthers(q, 20f) || !p.Ground(q, out var g) || !p.NearWall(g, 4f, out var wall, out float d) || d < 1.5f) continue;
                var along = Vector3.Cross(Vector3.up, wall);
                var rot = CityPropsTools.AlongAxis(fence, along);
                int got = 0;
                for (int k = 0; k < 3; k++)
                {
                    if (!p.Ground(g + along * (k * 4.5f), out var gk)) break;
                    if (!p.Clear(gk, rot, CityPropsTools.PrefabSize(fence), 0.3f)) break;
                    p.Put(fence, gk, rot, "Cover", 2f); got++;
                }
                if (got > 0) { runs++; fences += got; }
            }
            sb.Append($"cover {clusters} clusters ({coverPieces} pieces), {runs} razor-wire runs ({fences} fences).");
            // greenery last: it fills what the lookouts and cover left free
            int trees = 0, deadTrees = 0, bushes = 0, tufts = 0;
            for (int tries = 0; tries < (cfg.trees + cfg.deadTrees) * 60 && trees + deadTrees < cfg.trees + cfg.deadTrees; tries++)
            {
                bool live = trees < cfg.trees && (deadTrees >= cfg.deadTrees || Rf() < 0.55f);
                var q = p.RandomPoint(p.minR, p.maxR);
                if (!p.FarFromOthers(q, 4f) || !p.Ground(q, out var g)) continue;
                if (!p.Clear(g, Quaternion.identity, new Vector3(0.6f, 5f, 0.6f), 1.5f)) continue;
                var tp = live ? liveTrees[rnd.Next(liveTrees.Length)] : deadTreesP[rnd.Next(deadTreesP.Length)];
                var tgo = p.Put(tp, g, Yaw(), "Greenery", 2f);
                tgo.transform.localScale = Vector3.one * Mathf.Lerp(0.8f, 1.25f, Rf());
                if (live) trees++; else deadTrees++;
            }
            for (int tries = 0; tries < cfg.bushes * 40 && bushes < cfg.bushes; tries++)
            {
                var q = p.RandomPoint(p.minR, p.maxR);
                if (!p.Ground(q, out var g)) continue;
                if (Rf() < 0.75f && !p.NearWall(g, 4f, out _, out _)) continue; // most bushes hug walls
                if (!p.Clear(g, Quaternion.identity, new Vector3(1f, 0.8f, 1f), 0.05f)) continue;
                var bp = Rf() < 0.25f ? bushDry : (Rf() < 0.5f ? bushB : bush);
                p.Put(bp, g, Yaw(), "Greenery", 0.6f).transform.localScale = Vector3.one * Mathf.Lerp(0.8f, 1.3f, Rf());
                bushes++;
            }
            for (int tries = 0; tries < cfg.grassTufts * 20 && tufts < cfg.grassTufts; tries++)
            {
                var q = p.RandomPoint(p.minR, p.maxR);
                if (!p.Ground(q, out var g)) continue;
                if (Rf() < 0.8f && !p.NearWall(g, 3f, out _, out _)) continue; // patches along walls and kerbs
                if (!p.Clear(g, Quaternion.identity, new Vector3(0.4f, 0.4f, 0.4f), 0f)) continue;
                int clump = rnd.Next(cfg.weedPatchSize.x, cfg.weedPatchSize.y + 1);
                for (int c = 0; c < clump; c++)
                {
                    var gq = g + Dir(Rf() * 360f) * Mathf.Sqrt(Rf()) * 1.3f;
                    if (!p.Ground(gq, out gq) || !p.Clear(gq, Quaternion.identity, new Vector3(0.3f, 0.3f, 0.3f), 0f)) continue;
                    var go = p.Put(grasses[rnd.Next(grasses.Length)], gq, Yaw(), "Greenery", 0f);
                    go.transform.localScale = Vector3.one * Mathf.Lerp(0.75f, 1.2f, Rf());
                    tufts++;
                }
            }
            sb.Append($"greenery {trees} trees, {deadTrees} dead trees, {bushes} bushes, {tufts} grass clumps; ");
            sb.Append(OvergrowthTools.Ivy(p, mapT, safe, cfg, rnd, zone.maxRadius + 20f));
            sb.Append(OvergrowthTools.Ground(mapT, safe.Center, zone.maxRadius + 20f, cfg));
            int lights = p.root.GetComponentsInChildren<Light>(true).Length;
            return sb + $" Lights added: {lights}.";
        }

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
        static string PathOf(Transform t) { var s = t.name; for (int i = 0; i < 2 && t.parent != null; i++) { t = t.parent; s = t.name + "/" + s; } return s; }
    }
}
