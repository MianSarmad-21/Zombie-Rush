using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ZombieRush.Player;
using ZombieRush.World;

namespace ZombieRush.EditorTools
{
    /// The base compound round the safe base (Map/BaseCompound, built by SurvivalLayoutTools): survey it
    /// (marker ZR_RunMapCleanup "compound"), see it (top-down and from inside, Docs/Look/Compound_*.png).
    public static class BaseCompoundTools
    {
        const string OutDir = "Docs/Look";

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        static float Angle(Vector3 d) => (Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + 360f) % 360f;
        static Vector3 Dir(float deg) => new Vector3(Mathf.Sin(deg * Mathf.Deg2Rad), 0f, Mathf.Cos(deg * Mathf.Deg2Rad));

        public static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer)).ToArray();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static string SourceName(GameObject go)
        {
            var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
            return src != null ? src.name : go.name;
        }

        public static string Survey()
        {
            var safe = Object.FindAnyObjectByType<SafeBase>(FindObjectsInactive.Include);
            if (safe == null) return "FAILED: no SafeBase.";
            Physics.SyncTransforms();
            var sb = new StringBuilder();
            var c = safe.Center;
            sb.AppendLine($"safe base centre {c:F1}, radius {safe.radius:0.0}, half {safe.halfCircle}, ring {(safe.ring != null ? safe.ring.name : "none")}, showRing {safe.showRing}, noFire {safe.noFiringInside}");
            if (safe.building != null)
            {
                var bb = WorldBounds(safe.building.gameObject);
                sb.AppendLine($"HQ building '{safe.building.name}' bounds centre {bb.center:F1} size {bb.size:F1} (from base centre {Flat(bb.center - c).magnitude:0.0} m)");
            }
            foreach (var p in Object.FindObjectsByType<PlayerHealth>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var pos = p.transform.position;
                sb.AppendLine($"player '{p.name}' at {pos:F1}: {Flat(pos - c).magnitude:0.0} m from centre, angle {Angle(Flat(pos - c)):0}, inside safe {safe.Contains(pos)}");
            }

            var comp = GameObject.Find("BaseCompound");
            if (comp == null) { sb.AppendLine("no BaseCompound"); return sb.ToString(); }
            sb.AppendLine($"compound '{PathOf(comp.transform)}' children:");
            foreach (Transform g in comp.transform)
            {
                var kids = g.Cast<Transform>().ToList();
                sb.AppendLine($"  {g.name}: {kids.Count} -> " + string.Join(", ", kids.GroupBy(k => SourceName(k.gameObject)).Select(x => $"{x.Key} x{x.Count()}")));
            }

            // wall pieces: angle, radius, size
            var wall = comp.transform.Find("RingWall");
            var pieces = new List<(float ang, float r, GameObject go, Bounds b, string kind)>();
            if (wall != null)
                foreach (Transform t in wall)
                {
                    var b = WorldBounds(t.gameObject);
                    var d = Flat(t.position - c);
                    pieces.Add((Angle(d), d.magnitude, t.gameObject, b, SourceName(t.gameObject)));
                }
            pieces = pieces.OrderBy(p => p.ang).ToList();
            foreach (var k in pieces.GroupBy(p => p.kind))
            {
                var first = k.First();
                sb.AppendLine($"  wall kind {k.Key} x{k.Count()}: world size {first.b.size:F2}, scale {first.go.transform.localScale:F2}, radius {k.Min(p => p.r):0.0}-{k.Max(p => p.r):0.0}");
            }
            // gaps in the wall (drums excluded): every opening, measured from the pieces' real footprints
            var wallOnly = pieces.Where(p => !p.kind.ToLower().Contains("barrel")).ToList();
            sb.AppendLine("  openings in the wall (centre angle, width m at the wall, walk tests across it):");
            foreach (var g in Gaps(c, wallOnly.Select(p => p.go).ToList()))
            {
                var tests = new List<string>();
                float ringR = wallOnly.Average(p => p.r);
                float halfDeg = g.width * 0.5f / ringR * Mathf.Rad2Deg;
                foreach (float f in new[] { -0.6f, -0.3f, 0f, 0.3f, 0.6f })
                {
                    float a2 = g.angle + f * halfDeg * 2f;
                    var w = WalkTest(c, a2, ringR);
                    tests.Add($"{a2:0}:{(w == "OPEN" ? "open" : w)}");
                }
                sb.AppendLine($"   opening at {g.angle:0.0} deg, {g.width:0.00} m -> {string.Join(" | ", tests)}");
            }
            sb.AppendLine("other compound parts by angle:");
            foreach (Transform g in comp.transform)
            {
                if (g == wall) continue;
                foreach (Transform t in g)
                {
                    var b = WorldBounds(t.gameObject);
                    var d = Flat(t.position - c);
                    sb.AppendLine($"  {g.name}/{SourceName(t.gameObject)} at {Angle(d):0} deg, {d.magnitude:0.0} m, size {b.size:F1}");
                }
            }
            return sb.ToString();
        }

        /// Openings in a ring of wall pieces: the ring line is sampled every 0.2 deg; a sample is covered when a
        /// wall piece's collider (or its renderer bounds if it has none) sits on it.
        static List<(float angle, float width)> Gaps(Vector3 c, List<GameObject> wall)
        {
            var result = new List<(float, float)>();
            if (wall.Count == 0) return result;
            float ringR = wall.Average(w => Flat(w.transform.position - c).magnitude);
            var cols = wall.SelectMany(w => w.GetComponentsInChildren<Collider>()).Where(x => x.enabled && !x.isTrigger).ToList();
            const float step = 0.2f;
            int n = Mathf.RoundToInt(360f / step);
            var covered = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var at = c + Dir(i * step) * ringR + Vector3.up * 0.9f;
                foreach (var col in cols)
                    if ((col.ClosestPoint(at) - at).sqrMagnitude < 0.04f) { covered[i] = true; break; }
            }
            int start = System.Array.IndexOf(covered, true);
            if (start < 0) return result;
            int run = 0;
            for (int k = 1; k <= n; k++)
            {
                int i = (start + k) % n;
                if (!covered[i]) { run++; continue; }
                if (run > 0)
                {
                    float a0 = (i - run) * step, width = run * step * Mathf.Deg2Rad * ringR;
                    if (width >= 0.2f) result.Add(((a0 + run * step * 0.5f + 360f) % 360f, width));
                    run = 0;
                }
            }
            return result;
        }

        /// Walks a player-sized capsule out through the wall at `deg`: reports what stops it.
        static string WalkTest(Vector3 c, float deg, float ringR)
        {
            var d = Dir(deg);
            var hits = new List<string>();
            for (float r = ringR - 4f; r <= ringR + 4f; r += 0.25f)
            {
                var at = c + d * r;
                float y = GroundY(at);
                at.y = y;
                foreach (var col in Physics.OverlapCapsule(at + Vector3.up * 0.7f, at + Vector3.up * 1.5f, 0.38f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (col is TerrainCollider) continue;
                    if (col.GetComponentInParent<PlayerHealth>() != null) continue;
                    string k = $"{PathOf(col.transform)} @{r:0.0}m";
                    if (!hits.Any(h => h.StartsWith(PathOf(col.transform)))) hits.Add(k);
                }
            }
            return hits.Count == 0 ? "OPEN" : "BLOCKED by " + string.Join("; ", hits.Take(5));
        }

        static float GroundY(Vector3 p)
        {
            float best = float.NegativeInfinity;
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 20f, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider is TerrainCollider || h.collider.name.ToLower().Contains("ground") || h.collider.name.ToLower().Contains("road"))
                    best = Mathf.Max(best, h.point.y);
            return float.IsNegativeInfinity(best) ? p.y : best;
        }

        public static string PathOf(Transform t)
        {
            var s = t.name;
            for (var p = t.parent; p != null && p.parent != null; p = p.parent) s = p.name + "/" + s;
            return s;
        }

        /// What stands in the hall: colliders a player capsule would touch at points across the hall floor.
        public static string HallProbe()
        {
            var safe = Object.FindAnyObjectByType<SafeBase>(FindObjectsInactive.Include);
            var b = safe.building;
            var local = BaseBuildingTools.LocalBounds(b);
            var room = BaseBuildingTools.MeasureRoom(b, local);
            var sb = new StringBuilder($"hall floor {room.floor:0.00}, ceiling {room.ceiling:0.00}, longX {room.longX}" + System.Environment.NewLine);
            Vector3 hall = b.TransformPoint(local.center); hall.y = room.floor;
            for (float x = -8f; x <= 8f; x += 2f)
                for (float z = -6f; z <= 6f; z += 3f)
                {
                    var p = hall + Flat(b.right) .normalized * x + Flat(b.forward).normalized * z;
                    float gy = float.NaN;
                    if (Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out var gh, 3f, ~0, QueryTriggerInteraction.Ignore)) { p.y = gh.point.y; gy = gh.point.y; }
                    var cols = Physics.OverlapCapsule(p + Vector3.up * 0.45f, p + Vector3.up * 1.6f, 0.45f, ~0, QueryTriggerInteraction.Ignore)
                        .Where(col => col.GetComponentInParent<PlayerHealth>() == null).Select(col => PathOf(col.transform) + "(" + col.GetType().Name + ")").Distinct().ToList();
                    sb.AppendLine($"  ({x:0},{z:0}) ground {gy:0.00} ({(gh.collider != null ? PathOf(gh.collider.transform) : "-")}): {(cols.Count == 0 ? "CLEAR" : string.Join(", ", cols.Take(4)))}");
                }
            return sb.ToString();
        }

        /// The hall (safe.building): its parts, and every opening in its walls (rays out from the middle at knee,
        /// waist and head height, every 0.25 deg; an opening = all three get further than the wall), with its
        /// width at the wall and whether a player capsule fits through.
        public static string HallSurvey()
        {
            var safe = Object.FindAnyObjectByType<SafeBase>(FindObjectsInactive.Include);
            var b = safe.building;
            Physics.SyncTransforms();
            var local = BaseBuildingTools.LocalBounds(b);
            var room = BaseBuildingTools.MeasureRoom(b, local);
            var sb = new StringBuilder($"hall '{b.name}' pos {b.position:F1} rot {b.eulerAngles.y:0.0} scale {b.lossyScale:F2}, local size {local.size:F1}, floor {room.floor:0.00}, door {(room.hasDoor ? b.TransformPoint(local.center + room.doorLocal).ToString("F1") : "none")}" + System.Environment.NewLine);
            foreach (Transform t in b)
            {
                var col = t.GetComponentsInChildren<Collider>().Select(x => x.GetType().Name + (x is MeshCollider mc && mc.sharedMesh != null ? "(" + mc.sharedMesh.name + ")" : "")).Distinct();
                var wb = WorldBounds(t.gameObject);
                sb.AppendLine($"  child {t.name} active {t.gameObject.activeSelf} at {t.position:F1} size {wb.size:F1} colliders [{string.Join(", ", col)}]");
            }
            Vector3 c = b.TransformPoint(local.center); c.y = room.floor;
            var hallCols = new HashSet<Collider>(b.GetComponentsInChildren<Collider>());
            float WallDist(Vector3 from, Vector3 dir)
            {
                float best = 60f;
                foreach (var h in Physics.RaycastAll(from, dir, 60f, ~0, QueryTriggerInteraction.Ignore))
                    if (hallCols.Contains(h.collider) && h.distance < best) best = h.distance;
                return best;
            }
            // the wall outline: the nearest hall hit at waist height; an opening where knee, waist and head all go past it
            const float step = 0.25f;
            int n = Mathf.RoundToInt(360f / step);
            var open = new bool[n]; var dist = new float[n];
            // the box of the hall's walls, flat
            var corners = new[] { local.min, local.max }.Select(x => b.TransformPoint(x)).ToArray();
            for (int i = 0; i < n; i++)
            {
                var d = Dir(i * step);
                float knee = WallDist(c + Vector3.up * 0.5f, d), waist = WallDist(c + Vector3.up * 1.1f, d), head = WallDist(c + Vector3.up * 1.7f, d);
                dist[i] = Mathf.Min(knee, Mathf.Min(waist, head));
                open[i] = knee >= 59f && waist >= 59f && head >= 59f;
            }
            // the expected wall distance in this direction (to the bounds box) for the width estimate
            float BoxDist(Vector3 d)
            {
                Vector3 ld = b.InverseTransformDirection(d); Vector3 lc = local.center;
                float tx = Mathf.Abs(ld.x) > 1e-4f ? (local.extents.x) / Mathf.Abs(ld.x) : 1e9f;
                float tz = Mathf.Abs(ld.z) > 1e-4f ? (local.extents.z) / Mathf.Abs(ld.z) : 1e9f;
                return Mathf.Min(tx, tz) * b.lossyScale.x;
            }
            int start = System.Array.IndexOf(open, false);
            if (start < 0) { sb.AppendLine("  no walls hit"); return sb.ToString(); }
            int run = 0;
            for (int k = 1; k <= n; k++)
            {
                int i = (start + k) % n;
                if (open[i]) { run++; continue; }
                if (run > 0)
                {
                    float a0 = ((i - run) * step + 360f) % 360f, a1 = (i * step) % 360f, mid = (a0 + run * step * 0.5f) % 360f;
                    var d = Dir(mid);
                    float wd = BoxDist(d);
                    float width = run * step * Mathf.Deg2Rad * wd;
                    // walk test straight out through it
                    var blockers = new List<string>();
                    for (float r = wd - 1.5f; r <= wd + 1.5f; r += 0.2f)
                    {
                        var at = c + d * r;
                        foreach (var col in Physics.OverlapCapsule(at + Vector3.up * 0.6f, at + Vector3.up * 1.6f, 0.38f, ~0, QueryTriggerInteraction.Ignore))
                            if (!(col is TerrainCollider) && col.GetComponentInParent<PlayerHealth>() == null && !blockers.Contains(PathOf(col.transform))) blockers.Add(PathOf(col.transform));
                    }
                    sb.AppendLine($"  OPENING at {mid:0.0} deg ({a0:0.0}-{a1:0.0}), ~{width:0.00} m wide at the wall ({wd:0.0} m out), point {(c + d * wd):F1}: {(blockers.Count == 0 ? "WALKABLE" : "blocked by " + string.Join("; ", blockers.Take(4)))}");
                    run = 0;
                }
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- fix (2026-11-16)

        /// Bigger wall pieces (no slits between them, only the gates stay open), the safe zone out to the wall's
        /// outer face (no firing, no damage and zombie keep-out all match the wall you see), the energy ring off,
        /// a start point inside the hall facing the door, the view-kick timing on the player. Saves the scene
        /// (backup first in Backups/).
        public static string Fix()
        {
            var safe = Object.FindAnyObjectByType<SafeBase>(FindObjectsInactive.Include);
            var comp = GameObject.Find("BaseCompound");
            if (safe == null || comp == null) return "FAILED: no SafeBase or BaseCompound.";
            var sb = new StringBuilder("compound fix:\n");
            var scene = safe.gameObject.scene;
            if (!string.IsNullOrEmpty(scene.path) && File.Exists(scene.path))
            {
                Directory.CreateDirectory("Backups");
                string bak = $"Backups/{Path.GetFileNameWithoutExtension(scene.path)}_before_compound_2026-11-16.unity";
                if (!File.Exists(bak)) File.Copy(scene.path, bak);
                sb.AppendLine($"  scene backup {bak}");
            }
            var c = safe.Center;

            // 1. bigger wall pieces: long side, height and depth per kind; bottom and middle stay where they were
            var wall = comp.transform.Find("RingWall");
            int scaled = 0;
            var ringRs = new List<float>();
            foreach (Transform t in wall)
            {
                string kind = SourceName(t.gameObject);
                if (kind.ToLower().Contains("barrel")) continue;
                ringRs.Add(Flat(t.position - c).magnitude);
                if (Mathf.Abs(t.localScale.y - 1f) > 0.01f) continue;   // done already
                Vector3 mul = kind.Contains("Hesco") ? new Vector3(1.35f, 1.45f, 1.25f)   // long, height, depth
                            : kind.Contains("Sandbag") ? new Vector3(1.55f, 1.75f, 1.0f) : Vector3.one;
                if (mul == Vector3.one) continue;
                var before = WorldBounds(t.gameObject);
                var lb = PieceBounds(t);
                bool longX = lb.size.x >= lb.size.z;
                Undo.RecordObject(t, "Bigger wall");
                t.localScale = Vector3.Scale(t.localScale, longX ? new Vector3(mul.x, mul.y, mul.z) : new Vector3(mul.z, mul.y, mul.x));
                var after = WorldBounds(t.gameObject);
                t.position += new Vector3(before.center.x - after.center.x, before.min.y - after.min.y, before.center.z - after.center.z);
                scaled++;
            }
            Physics.SyncTransforms();
            sb.AppendLine($"  wall pieces made bigger: {scaled}");

            // 2. the safe zone out to the wall's outer face (rays from outside in, at chest height, onto the wall)
            var wallCols = new HashSet<Collider>(wall.GetComponentsInChildren<Collider>());
            float outer = 0f; int hitsN = 0;
            for (float a = 0f; a < 360f; a += 2f)
            {
                var from = c + Dir(a) * 40f + Vector3.up * 1.0f;
                foreach (var h in Physics.RaycastAll(from, -Dir(a), 40f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
                {
                    if (!wallCols.Contains(h.collider)) continue;
                    outer = Mathf.Max(outer, Flat(h.point - c).magnitude); hitsN++;
                    break;
                }
            }
            float ringR = ringRs.Count > 0 ? ringRs.Average() : safe.radius;
            if (outer > 0f)
            {
                float newR = Mathf.Ceil((outer + 0.3f) * 10f) / 10f;
                Undo.RecordObject(safe, "Safe radius");
                sb.AppendLine($"  safe radius {safe.radius:0.0} -> {newR:0.0} (wall centre line {ringR:0.0} m, outer face {outer:0.0} m, {hitsN} rays)");
                safe.radius = newR;
                EditorUtility.SetDirty(safe);
                var cfg = AssetDatabase.LoadAssetAtPath<ZombieRush.Data.SurvivalLayoutConfig>("Assets/Data/SurvivalLayoutConfig.asset");
                if (cfg != null)
                {
                    Undo.RecordObject(cfg, "Wall offset");
                    cfg.wallOffset = Mathf.Round((ringR - newR) * 100f) / 100f;   // a rebuild keeps the wall where it is
                    EditorUtility.SetDirty(cfg);
                    sb.AppendLine($"  SurvivalLayoutConfig.wallOffset -> {cfg.wallOffset:0.00}");
                }
            }

            // 3. the energy ring off in the scene too
            if (safe.ring != null && safe.ring.gameObject.activeSelf) { Undo.RecordObject(safe.ring.gameObject, "Ring off"); safe.ring.gameObject.SetActive(false); sb.AppendLine("  SafeRing switched off"); }
            safe.showRing = false; safe.noFiringInside = true;

            // 4. start point inside the hall, facing the door
            if (safe.building != null)
            {
                var b = safe.building;
                var local = BaseBuildingTools.LocalBounds(b);
                var room = BaseBuildingTools.MeasureRoom(b, local);
                Vector3 hall = b.TransformPoint(local.center); hall.y = room.floor;
                Vector3 door = room.hasDoor ? b.TransformPoint(local.center + room.doorLocal) : hall + b.forward * 5f;
                Vector3 toDoor = Flat(door - hall).normalized;
                Vector3 best = hall; bool found = false;
                foreach (float d in new[] { 1.5f, 0f, 3f, -1.5f, 4.5f, -3f })
                    foreach (float side in new[] { 0f, 1.5f, -1.5f })
                    {
                        if (found) break;
                        var p = hall + toDoor * d + Vector3.Cross(Vector3.up, toDoor) * side;
                        if (Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out var gh, 3f, ~0, QueryTriggerInteraction.Ignore)) p.y = gh.point.y;
                        bool blocked = Physics.OverlapCapsule(p + Vector3.up * 0.6f, p + Vector3.up * 1.6f, 0.45f, ~0, QueryTriggerInteraction.Ignore)
                            .Any(col => !(col is TerrainCollider) && col.GetComponentInParent<PlayerHealth>() == null);
                        if (!blocked) { best = p; found = true; }
                    }
                var st = safe.transform.Find("PlayerStart");
                if (st == null)
                {
                    st = new GameObject("PlayerStart").transform;
                    Undo.RegisterCreatedObjectUndo(st.gameObject, "Player start");
                    st.SetParent(safe.transform, true);
                }
                Undo.RecordObject(st, "Player start");
                st.SetPositionAndRotation(best, Quaternion.LookRotation(toDoor, Vector3.up));
                Undo.RecordObject(safe, "Player start");
                safe.playerStart = st;
                EditorUtility.SetDirty(safe);
                sb.AppendLine($"  PlayerStart at {best:F1} ({(found ? "clear" : "NOT CLEAR - fallback hall centre")}), {Flat(best - c).magnitude:0.0} m from base centre, facing the door at {door:F1}; inside safe {safe.Contains(best)}");
                // the scene's player stands there too
                foreach (var ph in Object.FindObjectsByType<PlayerHealth>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    Undo.RecordObject(ph.transform, "Player start");
                    ph.transform.SetPositionAndRotation(best + Vector3.up * 0.05f, st.rotation);
                }
            }

            // 5. the view kick timing on the scene's player (new fields saved with older defaults)
            foreach (var pc in Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Undo.RecordObject(pc, "Recoil");
                pc.recoilRise = 0.04f; pc.recoilSettle = 0.3f; pc.recoilMax = 5f;
                EditorUtility.SetDirty(pc);
            }

            var report = Survey().Split('\n').Where(l => l.Contains("opening at") || l.Contains("wall kind") || l.StartsWith("safe base") || l.StartsWith("player"));
            sb.AppendLine("  after:");
            foreach (var l in report) sb.AppendLine("   " + l.Trim());
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            sb.AppendLine("  scene saved");
            return sb.ToString();
        }

        /// The piece's mesh bounds in its own space (its children included).
        static Bounds PieceBounds(Transform root)
        {
            bool any = false; var b = new Bounds();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var m = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        /// Eye-height pictures of the hall's openings: from inside looking out and from outside looking in, at the
        /// given angles (degrees round the base centre). "hallviews <tag> a1 a2 ...".
        public static string HallViews(string args)
        {
            var parts = args.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            string tag = parts.Length > 0 ? parts[0] : "Now";
            var angles = parts.Skip(1).Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToList();
            var safe = Object.FindAnyObjectByType<SafeBase>(FindObjectsInactive.Include);
            var main = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();
            Directory.CreateDirectory(OutDir);
            var camGo = new GameObject("_HallCam") { hideFlags = HideFlags.HideAndDontSave };
            var lightGo = new GameObject("_HallLight") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            var shots = new List<string>();
            try
            {
                var l = lightGo.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.4f; l.shadows = LightShadows.None;
                lightGo.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
                var cam = camGo.AddComponent<Camera>(); cam.CopyFrom(main); cam.targetTexture = rt; cam.fieldOfView = 70f; cam.farClipPlane = 300f;
                var c = safe.Center;
                foreach (var a in angles)
                {
                    var d = Dir(a);
                    var eyeIn = c + d * 4f + Vector3.up * 1.6f;
                    cam.transform.SetPositionAndRotation(eyeIn, Quaternion.LookRotation(d + Vector3.down * 0.08f));
                    Shot(cam, rt, $"{OutDir}/Hall_{tag}_{a:0}_in.png"); shots.Add($"{a:0}_in");
                    var eyeOut = c + d * 20f + Vector3.up * 1.7f;
                    cam.transform.SetPositionAndRotation(eyeOut, Quaternion.LookRotation(-d + Vector3.down * 0.05f));
                    Shot(cam, rt, $"{OutDir}/Hall_{tag}_{a:0}_out.png"); shots.Add($"{a:0}_out");
                }
            }
            finally { Object.DestroyImmediate(camGo); Object.DestroyImmediate(lightGo); rt.Release(); Object.DestroyImmediate(rt); }
            return $"Hall views {tag}: {string.Join(", ", shots)}";
        }

        /// The hall's small doorway (door1): jamb positions along the wall, the head height, the door/frame
        /// objects, the meshes and colliders on the hall, and a player walk test through it.
        public static string DoorProbe()
        {
            var safe = Object.FindAnyObjectByType<SafeBase>(FindObjectsInactive.Include);
            var b = safe.building;
            var door = b.Find("door1");
            var sb = new StringBuilder();
            foreach (var t in door.GetComponentsInChildren<Transform>(true))
                sb.AppendLine($"  {PathOf(t)} local {t.localPosition:F2} rot {t.localEulerAngles:F0} scale {t.localScale:F2} mesh {(t.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null ? mf.sharedMesh.name + " " + mf.sharedMesh.bounds.size.ToString("F2") : "-")} collider {(t.TryGetComponent(out Collider cc) ? cc.GetType().Name + " enabled " + cc.enabled : "-")}");
            foreach (var mf in b.GetComponents<MeshFilter>()) sb.AppendLine($"  hall mesh {mf.sharedMesh?.name} verts {mf.sharedMesh?.vertexCount} path {AssetDatabase.GetAssetPath(mf.sharedMesh)}");
            foreach (var col in b.GetComponents<Collider>()) sb.AppendLine($"  hall collider {col.GetType().Name} {(col is MeshCollider m2 ? m2.sharedMesh?.name : "")}");
            foreach (var lg in b.GetComponents<LODGroup>()) sb.AppendLine($"  LODGroup {lg.lodCount} lods: " + string.Join(" | ", lg.GetLODs().Select(l => string.Join(",", l.renderers.Where(r => r != null).Select(r => r.name)))));
            sb.AppendLine($"  hall static flags {GameObjectUtility.GetStaticEditorFlags(b.gameObject)}");
            // along the wall at the door: the wall runs along the hall's local axis that is not toward the door
            Vector3 dp = door.position;
            Vector3 c = b.position; Vector3 outDir = Flat(dp - c).normalized;
            Vector3 lx = Flat(b.right).normalized, lz = Flat(b.forward).normalized;
            Vector3 wallAxis = Mathf.Abs(Vector3.Dot(outDir, lx)) > Mathf.Abs(Vector3.Dot(outDir, lz)) ? lz : lx;
            Vector3 normal = Mathf.Abs(Vector3.Dot(outDir, lx)) > Mathf.Abs(Vector3.Dot(outDir, lz)) ? lx * Mathf.Sign(Vector3.Dot(outDir, lx)) : lz * Mathf.Sign(Vector3.Dot(outDir, lz));
            sb.AppendLine($"  door at {dp:F2}, wall normal {normal:F2}, along {wallAxis:F2}");
            var hallCols = new HashSet<Collider>(b.GetComponentsInChildren<Collider>());
            foreach (float y in new[] { 0.3f, 1.0f, 1.8f, 2.1f, 2.4f })
            {
                // march along the wall line through the door: which samples hit the hall when probing outward
                var line = new StringBuilder();
                for (float s2 = -2.0f; s2 <= 2.0f; s2 += 0.1f)
                {
                    var from = dp + wallAxis * s2 - normal * 1.0f + Vector3.up * y;
                    bool hit = Physics.Raycast(from, normal, out var h, 2.0f, ~0, QueryTriggerInteraction.Ignore) && hallCols.Contains(h.collider);
                    line.Append(hit ? (h.collider.transform == b ? "#" : "d") : ".");
                }
                sb.AppendLine($"  y {y:0.0}: {line}   (-2.0 .. +2.0 m along, # hall wall, d door object)");
            }
            var blockers = new List<string>();
            for (float r = -1.5f; r <= 1.5f; r += 0.1f)
            {
                var at = dp + normal * r;
                foreach (var col in Physics.OverlapCapsule(at + Vector3.up * 0.6f, at + Vector3.up * 1.6f, 0.38f, ~0, QueryTriggerInteraction.Ignore))
                    if (!(col is TerrainCollider) && col.GetComponentInParent<PlayerHealth>() == null && !blockers.Contains(PathOf(col.transform))) blockers.Add(PathOf(col.transform));
            }
            sb.AppendLine($"  walk test through the door: {(blockers.Count == 0 ? "WALKABLE" : "blocked by " + string.Join("; ", blockers))}");
            return sb.ToString();
        }

        // ---------------------------------------------------------------- base layout (2026-11-16 evening)

        static Vector3 HallBoxCentre(Transform b, Bounds local) => b.TransformPoint(local.center);

        /// The hall's biggest walkable wall opening (the main entrance): its angle round the base centre.
        static float MainEntranceAngle(Transform b, Bounds local, BaseBuildingTools.Room room, out float entranceDist)
        {
            Vector3 c = b.TransformPoint(local.center); c.y = room.floor;
            var hallCols = new HashSet<Collider>(b.GetComponentsInChildren<Collider>());
            const float step = 0.5f;
            int n = Mathf.RoundToInt(360f / step);
            var open = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var d = Dir(i * step);
                bool all = true;
                foreach (float y in new[] { 0.5f, 1.1f, 1.7f })
                    foreach (var h in Physics.RaycastAll(c + Vector3.up * y, d, 60f, ~0, QueryTriggerInteraction.Ignore))
                        if (hallCols.Contains(h.collider)) { all = false; break; }
                open[i] = all;
            }
            float best = -1f, bestAng = 0f;
            int start = System.Array.IndexOf(open, false), run = 0;
            for (int k = 1; k <= n && start >= 0; k++)
            {
                int i = (start + k) % n;
                if (open[i]) { run++; continue; }
                if (run > best) { best = run; bestAng = ((i - run) * step + run * step * 0.5f + 360f) % 360f; }
                run = 0;
            }
            // distance to the wall that way: the hall's box
            Vector3 ld = b.InverseTransformDirection(Dir(bestAng));
            float tx = Mathf.Abs(ld.x) > 1e-4f ? local.extents.x / Mathf.Abs(ld.x) : 1e9f, tz = Mathf.Abs(ld.z) > 1e-4f ? local.extents.z / Mathf.Abs(ld.z) : 1e9f;
            entranceDist = Mathf.Min(tx, tz) * b.lossyScale.x;
            return bestAng;
        }

        /// Placement roots of everything drawn in the scene (prefab instance roots, else the renderer's object).
        static List<Transform> SceneRoots()
        {
            var set = new HashSet<Transform>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var root = PrefabUtility.GetOutermostPrefabInstanceRoot(r.gameObject);
                set.Add(root != null ? root.transform : r.transform);
            }
            return set.ToList();
        }

        static bool InBox(Vector3 p, Vector3 centre, Quaternion rot, Vector3 half)
        {
            var l = Quaternion.Inverse(rot) * (p - centre);
            return Mathf.Abs(l.x) <= half.x && Mathf.Abs(l.z) <= half.z;
        }

        /// Turns the hall so its main entrance faces the nearest gate (everything inside and on its walls turns
        /// with it), moves compound props out of the hall and of the walk from the entrance to that gate, narrows
        /// every gate to `gateWidth`, widens the hall's small doorway (door leaf off), sets the start point facing
        /// the main entrance. Saves the scene (backup first).
        public static string Layout(float gateWidth = 4.5f, float doorExtra = 0.35f)
        {
            var safe = Object.FindAnyObjectByType<SafeBase>(FindObjectsInactive.Include);
            var comp = GameObject.Find("BaseCompound");
            var b = safe.building;
            var sb = new StringBuilder("base layout:" + System.Environment.NewLine);
            var scene = safe.gameObject.scene;
            string bak = $"Backups/{Path.GetFileNameWithoutExtension(scene.path)}_before_layout_2026-11-16.unity";
            Directory.CreateDirectory("Backups");
            if (!File.Exists(bak)) File.Copy(scene.path, bak);
            sb.AppendLine($"  scene backup {bak}");
            Physics.SyncTransforms();
            var c = safe.Center;
            var wall = comp.transform.Find("RingWall");
            var wallPieces = wall.Cast<Transform>().Where(t => !SourceName(t.gameObject).ToLower().Contains("barrel")).ToList();
            float ringR = wallPieces.Average(t => Flat(t.position - c).magnitude);

            // ---- 1. turn the hall to face the nearest gate
            var local = BaseBuildingTools.LocalBounds(b);
            var room = BaseBuildingTools.MeasureRoom(b, local);
            float entrance = MainEntranceAngle(b, local, room, out float entranceDist);
            var gates = Gaps(c, wallPieces.Select(t => t.gameObject).ToList()).Where(g => g.width > 2.5f).ToList();
            var target = gates.OrderBy(g => Mathf.Abs(Mathf.DeltaAngle(entrance, g.angle))).First();
            float turn = Mathf.DeltaAngle(entrance, target.angle);
            sb.AppendLine($"  main entrance at {entrance:0.0} deg ({entranceDist:0.0} m out); gates {string.Join(", ", gates.Select(g => $"{g.angle:0.0} ({g.width:0.0} m)"))}; nearest {target.angle:0.0} -> turn {turn:+0.0;-0.0} deg");
            var riders = new List<Transform>();
            if (Mathf.Abs(turn) > 1.5f)
            {
                // what turns with it: the hall, the base marker (furniture, gear, start), and everything small that
                // stands in or on the hall (ivy on its walls, props inside)
                Vector3 boxC = HallBoxCentre(b, local); Quaternion boxR = b.rotation;
                Vector3 half = Vector3.Scale(local.extents, b.lossyScale) + new Vector3(1.2f, 0f, 1.2f);
                foreach (var t in SceneRoots())
                {
                    if (t == null || t.IsChildOf(b) || t.IsChildOf(safe.transform) || t.IsChildOf(comp.transform)) continue;
                    if (t.GetComponentInParent<PlayerHealth>() != null || t.GetComponentInParent<Terrain>() != null || t.GetComponentInParent<Camera>() != null) continue;
                    if (b.IsChildOf(t)) continue;   // a container of the hall (Map)
                    var wb = WorldBounds(t.gameObject);
                    if (Mathf.Max(wb.size.x, wb.size.z) > 8f) continue;   // big things are not on the hall
                    if (!InBox(wb.center, boxC, boxR, half)) continue;
                    riders.Add(t);
                }
                Undo.RecordObject(b, "Turn hall"); Undo.RecordObject(safe.transform, "Turn hall");
                b.RotateAround(c, Vector3.up, turn);
                safe.transform.RotateAround(c, Vector3.up, turn);
                foreach (var t in riders) { Undo.RecordObject(t, "Turn hall"); t.RotateAround(c, Vector3.up, turn); }
                Physics.SyncTransforms();
                sb.AppendLine($"  hall, base marker and {riders.Count} things on/in it turned {turn:+0.0;-0.0} deg ({string.Join(", ", riders.GroupBy(r => SourceName(r.gameObject)).Select(g => $"{g.Key} x{g.Count()}").Take(8))})");
                local = BaseBuildingTools.LocalBounds(b);
                room = BaseBuildingTools.MeasureRoom(b, local);
                entrance = MainEntranceAngle(b, local, room, out entranceDist);
                sb.AppendLine($"  main entrance now at {entrance:0.0} deg");
            }

            // ---- 2. narrow the gates: a hesco piece each side so `gateWidth` is left
            var hesco = wallPieces.FirstOrDefault(t => SourceName(t.gameObject).Contains("Hesco"));
            var hescoPrefab = hesco != null ? PrefabUtility.GetCorrespondingObjectFromSource(hesco.gameObject) : null;
            float pieceLen = hesco != null ? Mathf.Max(PieceBounds(hesco).size.x * hesco.localScale.x, PieceBounds(hesco).size.z * hesco.localScale.z) : 4.5f;
            int added = 0;
            foreach (var g in gates)
            {
                if (g.width <= gateWidth + 0.8f || hescoPrefab == null) continue;
                foreach (float side in new[] { -1f, 1f })
                {
                    float arc = gateWidth * 0.5f + pieceLen * 0.5f;
                    float ang = g.angle + side * arc / ringR * Mathf.Rad2Deg;
                    float hAng = Angle(Flat(hesco.position - c));
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(hescoPrefab, wall);
                    Undo.RegisterCreatedObjectUndo(go, "Gate piece");
                    go.name = hesco.name.Split(' ')[0] + " (gate)";
                    go.transform.localScale = Vector3.Scale(hesco.localScale, new Vector3(1f, 0.96f, 1f));   // a touch lower: no flickering tops where it overlaps
                    go.transform.rotation = Quaternion.Euler(0f, ang - hAng, 0f) * hesco.rotation;
                    var p = c + Dir(ang) * Flat(hesco.position - c).magnitude;
                    p.y = hesco.position.y + (GroundY(p) - GroundY(hesco.position));
                    go.transform.position = p;
                    added++;
                }
                // the fire drums of this gate go to the new gate sides
                var drums = wall.Cast<Transform>().Where(t => SourceName(t.gameObject).ToLower().Contains("barrel") && Mathf.Abs(Mathf.DeltaAngle(Angle(Flat(t.position - c)), g.angle)) < 20f)
                    .OrderBy(t => Mathf.DeltaAngle(g.angle, Angle(Flat(t.position - c)))).ToList();
                for (int i = 0; i < drums.Count && i < 2; i++)
                {
                    float side = i == 0 ? -1f : 1f;
                    float r = Flat(drums[i].position - c).magnitude;
                    float ang = g.angle + side * (gateWidth * 0.5f + 0.9f) / r * Mathf.Rad2Deg;
                    Undo.RecordObject(drums[i], "Gate drum");
                    var p = c + Dir(ang) * r; p.y = drums[i].position.y + (GroundY(p) - GroundY(drums[i].position));
                    drums[i].position = p;
                }
            }
            Physics.SyncTransforms();
            sb.AppendLine($"  gates narrowed to {gateWidth:0.0} m: {added} hesco pieces added");

            // ---- 3. compound props: out of the hall, out of the walk from the main entrance to its gate, out of the gates
            Vector3 hc = HallBoxCentre(b, local); Quaternion hr = b.rotation;
            Vector3 hh = Vector3.Scale(local.extents, b.lossyScale) + new Vector3(0.8f, 0f, 0.8f);
            var keepOut = new List<(Vector3 c, Quaternion r, Vector3 half, string what)> { (hc, hr, hh, "hall") };
            {
                var d = Dir(entrance);
                float r0 = entranceDist - 0.5f, r1 = ringR + 1f;
                keepOut.Add((c + d * (r0 + r1) * 0.5f, Quaternion.LookRotation(d), new Vector3(2.6f, 3f, (r1 - r0) * 0.5f), "walk to the gate"));
            }
            foreach (var g in Gaps(c, wall.Cast<Transform>().Where(t => !SourceName(t.gameObject).ToLower().Contains("barrel")).Select(t => t.gameObject).ToList()).Where(g => g.width > 2.5f))
            {
                var d = Dir(g.angle);
                keepOut.Add((c + d * (ringR - 3f), Quaternion.LookRotation(d), new Vector3(g.width * 0.5f + 0.5f, 3f, 4f), $"gate {g.angle:0}"));
            }
            bool Blocked(Transform t, out string why)
            {
                var wb = WorldBounds(t.gameObject);
                var corners = new List<Vector3> { wb.center };
                for (int i = 0; i < 4; i++) corners.Add(new Vector3((i & 1) == 0 ? wb.min.x : wb.max.x, 0f, (i & 2) == 0 ? wb.min.z : wb.max.z));
                foreach (var k in keepOut)
                    foreach (var p in corners)
                        if (InBox(new Vector3(p.x, 0f, p.z), new Vector3(k.c.x, 0f, k.c.z), k.r, k.half)) { why = k.what; return true; }
                // other solid things in the way (not the ground, not itself)
                foreach (var col in Physics.OverlapBox(wb.center + Vector3.up * 0.1f, new Vector3(wb.extents.x * 0.9f, Mathf.Max(0.2f, wb.extents.y * 0.8f), wb.extents.z * 0.9f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (col is TerrainCollider || col.transform.IsChildOf(t)) continue;
                    // only the hall and the other compound props count (not the wall, the ground or map clutter)
                    bool hallCol = col.transform.IsChildOf(b), propCol = col.transform.IsChildOf(comp.transform) && !col.transform.IsChildOf(wall);
                    if (!hallCol && !propCol) continue;
                    why = "touches " + PathOf(col.transform); return true;
                }
                why = null; return false;
            }
            var groups = new[] { "Tents", "Towers", "Power", "Lights", "Campfire", "Supplies" };
            int moved = 0;
            var homeless = new List<Transform>();
            foreach (var gname in groups)
            {
                var g = comp.transform.Find(gname);
                if (g == null) continue;
                foreach (Transform t in g.Cast<Transform>().ToList())
                {
                    if (!t.gameObject.activeSelf) { homeless.Add(t); continue; }
                    if (!Blocked(t, out var why)) continue;
                    bool ok = false;
                    Undo.RecordObject(t, "Move prop");
                    var startPos = t.position; var startRot = t.rotation;
                    for (int k = 1; k <= 60 && !ok; k++)
                        foreach (float s in new[] { 1f, -1f })
                        {
                            t.SetPositionAndRotation(startPos, startRot);
                            t.RotateAround(c, Vector3.up, s * k * 3f);
                            var p = t.position; p.y = startPos.y + (GroundY(p) - GroundY(startPos)); t.position = p;
                            Physics.SyncTransforms();
                            if (!Blocked(t, out _)) { ok = true; sb.AppendLine($"  moved {gname}/{SourceName(t.gameObject)} ({why}) by {s * k * 3f:+0;-0} deg round the base"); break; }
                        }
                    if (!ok) { t.SetPositionAndRotation(startPos, startRot); t.gameObject.SetActive(false); homeless.Add(t); sb.AppendLine($"  {gname}/{SourceName(t.gameObject)} ({why}): no spot at its distance"); }
                    moved++;
                }
            }
            // props without a spot: anywhere in the yard, other distances too, clear of the wall
            var wallCols = new HashSet<Collider>(wall.GetComponentsInChildren<Collider>());
            foreach (var t in homeless)
            {
                t.gameObject.SetActive(true);
                Undo.RecordObject(t, "Re-place prop");
                var startPos = t.position; var startRot = t.rotation;
                float r0 = Flat(startPos - c).magnitude, a0 = Angle(Flat(startPos - c));
                bool ok = false;
                foreach (float r in new[] { r0, r0 - 1f, r0 + 1f, r0 - 2f, r0 - 3f, r0 + 2f, r0 - 4f })
                {
                    for (int k = 0; k <= 120 && !ok; k++)
                        foreach (float sgn in new[] { 1f, -1f })
                        {
                            float da = sgn * k * 3f;
                            var p = c + Dir(a0 + da) * r; p.y = startPos.y + (GroundY(p) - GroundY(startPos));
                            t.SetPositionAndRotation(p, Quaternion.Euler(0f, da, 0f) * startRot);
                            Physics.SyncTransforms();
                            if (Blocked(t, out _)) continue;
                            var wb = WorldBounds(t.gameObject);
                            if (Physics.OverlapBox(wb.center, wb.extents * 0.95f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Any(col => wallCols.Contains(col))) continue;
                            ok = true; sb.AppendLine($"  re-placed {t.parent.name}/{SourceName(t.gameObject)} at {Angle(Flat(t.position - c)):0} deg, {r:0.0} m"); break;
                        }
                    if (ok) break;
                }
                if (!ok) { t.SetPositionAndRotation(startPos, startRot); t.gameObject.SetActive(false); sb.AppendLine($"  {t.parent.name}/{SourceName(t.gameObject)}: no free spot anywhere, left switched off"); }
            }
            // loose map junk with colliders left inside the hall or on the walk: off
            int junk = 0;
            foreach (var t in SceneRoots())
            {
                if (t == null || t.IsChildOf(b) || t.IsChildOf(safe.transform) || t.IsChildOf(comp.transform) || b.IsChildOf(t) || riders.Contains(t)) continue;
                if (t.GetComponentInParent<PlayerHealth>() != null || t.GetComponentInParent<Terrain>() != null || t.GetComponentInParent<Camera>() != null) continue;
                if (t.GetComponentInChildren<Collider>() == null) continue;
                var wb = WorldBounds(t.gameObject);
                if (Mathf.Max(wb.size.x, wb.size.z) > 12f) continue;
                if (!keepOut.Take(2).Any(k => InBox(new Vector3(wb.center.x, 0f, wb.center.z), new Vector3(k.c.x, 0f, k.c.z), k.r, k.half))) continue;
                Undo.RecordObject(t.gameObject, "Junk off");
                t.gameObject.SetActive(false); junk++;
                sb.AppendLine($"  switched off {PathOf(t)} (in the hall or on the walk)");
            }
            sb.AppendLine($"  compound props checked: {moved} moved/off, {junk} loose pieces off");

            // ---- 4. the small doorway: wider, door leaf off
            var door = b.Find("door1");
            if (door != null) sb.AppendLine("  " + WidenDoor(b, door, doorExtra));

            // ---- 5. start point in the hall, facing the main entrance
            {
                Vector3 hall = b.TransformPoint(local.center); hall.y = room.floor;
                var toGate = Dir(entrance);
                var st = safe.playerStart != null ? safe.playerStart : safe.transform.Find("PlayerStart");
                Vector3 best = hall;
                foreach (float d in new[] { 0f, -1.5f, 1.5f, -3f, 3f })
                {
                    var p = hall + toGate * d;
                    bool blocked = Physics.OverlapCapsule(p + Vector3.up * 0.6f, p + Vector3.up * 1.6f, 0.45f, ~0, QueryTriggerInteraction.Ignore)
                        .Any(col => !(col is TerrainCollider) && col.GetComponentInParent<PlayerHealth>() == null);
                    if (!blocked) { best = p; break; }
                }
                if (st != null)
                {
                    Undo.RecordObject(st, "Start");
                    st.SetPositionAndRotation(best, Quaternion.LookRotation(toGate, Vector3.up));
                    safe.playerStart = st; EditorUtility.SetDirty(safe);
                    foreach (var ph in Object.FindObjectsByType<PlayerHealth>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    { Undo.RecordObject(ph.transform, "Start"); ph.transform.SetPositionAndRotation(best + Vector3.up * 0.05f, st.rotation); }
                    sb.AppendLine($"  PlayerStart {best:F1} facing the main entrance ({entrance:0} deg)");
                }
            }

            var report = Survey().Split('\n').Where(l => l.Contains("opening at"));
            foreach (var l in report) sb.AppendLine("   " + l.Trim());
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            sb.AppendLine("  scene saved (re-bake occlusion: the hall moved)");
            return sb.ToString();
        }

        /// Widens the hall's doorway by `extra` metres each side: copies of the hall meshes (all LODs + collider)
        /// with the jamb vertices (up to the lintel) moved out, the frame stretched to match, the door leaf off.
        static string WidenDoor(Transform b, Transform door, float extra)
        {
            var lod0 = b.GetComponent<MeshFilter>();
            if (lod0 == null || lod0.sharedMesh == null) return "door: no hall mesh";
            // the frame's own collider is a solid block across the hole (convex): off, the hall's jambs stop the player
            string frameNote = "";
            foreach (var fc in door.GetComponents<MeshCollider>())
                if (fc.enabled) { Undo.RecordObject(fc, "Frame collider off"); frameNote = $" frame collider off (convex {fc.convex})"; fc.enabled = false; }
            if (lod0.sharedMesh.name.EndsWith("_wideDoor")) return "door: already widened" + frameNote;
            var frameMf = door.GetComponent<MeshFilter>();
            Bounds fb = frameMf != null && frameMf.sharedMesh != null ? frameMf.sharedMesh.bounds : new Bounds(Vector3.zero, new Vector3(1f, 2f, 0.2f));
            Vector3 frameW = door.TransformPoint(fb.center);
            Vector3 alongW = door.TransformDirection(Vector3.right).normalized;
            Vector3 normW = door.TransformDirection(Vector3.forward).normalized;
            float halfW = fb.extents.x * door.lossyScale.x, top = door.TransformPoint(new Vector3(fb.center.x, fb.max.y, fb.center.z)).y;
            float floorY = door.TransformPoint(new Vector3(fb.center.x, fb.min.y, fb.center.z)).y;
            Directory.CreateDirectory("Assets/Models/Base");
            int movedTotal = 0;
            var notes = new List<string>();
            foreach (var mf in b.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.transform != b && !mf.name.StartsWith(b.name)) continue;   // the hall and its LOD copies only
                var src = mf.sharedMesh;
                if (src == null) continue;
                var m = Object.Instantiate(src); m.name = src.name + "_wideDoor";
                var v = m.vertices;
                int moved = 0;
                for (int i = 0; i < v.Length; i++)
                {
                    var w = mf.transform.TransformPoint(v[i]);
                    var rel = w - frameW;
                    float a = Vector3.Dot(rel, alongW), n = Vector3.Dot(rel, normW);
                    if (Mathf.Abs(n) > 0.6f || w.y < floorY - 0.1f || w.y > top + 0.15f) continue;
                    if (Mathf.Abs(a) < halfW - 0.12f || Mathf.Abs(a) > halfW + 0.12f) continue;
                    w += alongW * (Mathf.Sign(a) * extra);
                    v[i] = mf.transform.InverseTransformPoint(w);
                    moved++;
                }
                m.vertices = v; m.RecalculateBounds();
                string path = $"Assets/Models/Base/{m.name}.asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(m, path);
                Undo.RecordObject(mf, "Wide door");
                mf.sharedMesh = m;
                foreach (var mc in mf.GetComponents<MeshCollider>()) { Undo.RecordObject(mc, "Wide door"); mc.sharedMesh = m; }
                movedTotal += moved; notes.Add($"{src.name}: {moved} verts");
            }
            // the frame stretched across the wider hole, the leaf off
            Undo.RecordObject(door, "Wide door");
            var s = door.localScale; s.x *= (halfW + extra) / halfW; door.localScale = s;
            var leaf = door.Find("door1");
            if (leaf != null) { Undo.RecordObject(leaf.gameObject, "Door leaf off"); leaf.gameObject.SetActive(false); }
            // LOD group renderers keep pointing at the same renderers: nothing to do
            AssetDatabase.SaveAssets();
            return $"door: {2f * halfW:0.00} m -> {2f * (halfW + extra):0.00} m ({string.Join(", ", notes)}), leaf off" + frameNote;
        }

        // ---------------------------------------------------------------- gate fires (2026-11-16)

        const string GateFireMatDir = "Assets/Materials/Base";

        static Material GateEmberMaterial()
        {
            Directory.CreateDirectory(GateFireMatDir);
            string path = $"{GateFireMatDir}/ZR_GateEmbers.mat";
            const string texPath = "Assets/Resources/ZombieRush/Fx/Fx_Embers.png";
            if (AssetImporter.GetAtPath(texPath) is TextureImporter ti && (ti.alphaIsTransparency || ti.wrapModeU != TextureWrapMode.Repeat))
            {
                ti.alphaIsTransparency = false;   // premultiplied
                ti.wrapModeU = TextureWrapMode.Repeat; ti.wrapModeV = TextureWrapMode.Clamp;
                ti.mipmapEnabled = true; ti.maxTextureSize = 1024;
                ti.SaveAndReimport();
            }
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var baseMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/ZombieRush/Fx/Fx_Glow.mat");
                m = baseMat != null ? new Material(baseMat) : new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.name = "ZR_GateEmbers";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 50;   // on the ground, under smoke and flames
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material GateSmokeMaterial()
        {
            string path = $"{GateFireMatDir}/ZR_GateSmoke.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "ZR_GateSmoke" };
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/ZombieRush/Fx/Fx_Smoke.png"));
            m.SetColor("_BaseColor", Color.white);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// A burning coal line across every gate of the compound wall: coal bed on the ground (follows it), low
        /// flames, sparks, a little smoke, a flickering warm light. No colliders: players cross it; zombies stop
        /// outside the base anyway (this shows why). Rebuilt from scratch each run (BaseCompound/GateFires). Saves.
        public static string GateFires()
        {
            var safe = Object.FindAnyObjectByType<SafeBase>(FindObjectsInactive.Include);
            var comp = GameObject.Find("BaseCompound");
            if (safe == null || comp == null) return "FAILED: no SafeBase or BaseCompound.";
            var c = safe.Center;
            var wall = comp.transform.Find("RingWall");
            var pieces = wall.Cast<Transform>().Where(t => !SourceName(t.gameObject).ToLower().Contains("barrel")).ToList();
            float ringR = pieces.Average(t => Flat(t.position - c).magnitude);
            var gates = Gaps(c, pieces.Select(t => t.gameObject).ToList()).Where(g => g.width > 2.5f).ToList();
            var old = comp.transform.Find("GateFires");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            var root = new GameObject("GateFires");
            Undo.RegisterCreatedObjectUndo(root, "Gate fires");
            root.transform.SetParent(comp.transform, false);
            var emberMat = GateEmberMaterial();
            var smokeMat = GateSmokeMaterial();
            var fireMat = CityPropsTools.FireParticleMaterial();
            Directory.CreateDirectory("Assets/Models/Base");
            var sb = new StringBuilder($"gate fires on {gates.Count} gates:" + System.Environment.NewLine);
            int gi = 0;
            foreach (var g in gates)
            {
                var d = Dir(g.angle);
                var right = Vector3.Cross(Vector3.up, d);
                var go = new GameObject($"GateFire_{g.angle:0}");
                go.transform.SetParent(root.transform, false);
                var centre = c + d * ringR; centre.y = GroundY(centre);
                go.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(d, Vector3.up));
                float L = g.width + 0.8f, D = 1.15f;   // a little under the hesco at each end

                // the coal bed: a grid on the ground, 4.4 m of texture per tile along the line
                const int nx = 28, nz = 5;
                var verts = new Vector3[(nx + 1) * (nz + 1)]; var uv = new Vector2[verts.Length]; var tris = new int[nx * nz * 6];
                for (int iz = 0; iz <= nz; iz++)
                    for (int ix = 0; ix <= nx; ix++)
                    {
                        float x = -L * 0.5f + L * ix / nx, z = -D * 0.5f + D * iz / nz;
                        var w = centre + right * x + d * z;
                        w.y = GroundY(w) + 0.035f;
                        verts[iz * (nx + 1) + ix] = go.transform.InverseTransformPoint(w);
                        uv[iz * (nx + 1) + ix] = new Vector2(x / (4f * D) + gi * 0.37f, (float)iz / nz);
                    }
                int ti2 = 0;
                for (int iz = 0; iz < nz; iz++)
                    for (int ix = 0; ix < nx; ix++)
                    {
                        int a = iz * (nx + 1) + ix, b2 = a + 1, cc = a + nx + 1, dd = cc + 1;
                        tris[ti2++] = a; tris[ti2++] = cc; tris[ti2++] = b2;
                        tris[ti2++] = b2; tris[ti2++] = cc; tris[ti2++] = dd;
                    }
                var mesh = new Mesh { name = $"GateEmbers_{g.angle:0}", vertices = verts, uv = uv, triangles = tris };
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                string mp = $"Assets/Models/Base/{mesh.name}.asset";
                AssetDatabase.DeleteAsset(mp); AssetDatabase.CreateAsset(mesh, mp);
                var bed = new GameObject("Embers");
                bed.transform.SetParent(go.transform, false);
                bed.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = bed.AddComponent<MeshRenderer>();
                mr.sharedMaterial = emberMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                var gf = go.AddComponent<ZombieRush.World.GateFire>();
                gf.embers = mr;

                // low flames all along the line
                var fl = Emitter(go.transform, "Flames", fireMat, new Vector3(L - 0.7f, D * 0.45f, 0.01f), 0.05f);
                var m1 = fl.main;
                m1.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
                m1.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.25f);
                m1.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.42f);
                m1.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.72f, 0.3f, 0.9f), new Color(1f, 0.42f, 0.1f, 0.9f));
                m1.maxParticles = 220;
                var e1 = fl.emission; e1.rateOverTime = 34f * L;
                var col1 = fl.colorOverLifetime; col1.enabled = true;
                var g1 = new Gradient();
                g1.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.9f, 0.55f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.1f), 0.45f), new GradientColorKey(new Color(0.55f, 0.07f, 0.02f), 1f) },
                           new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
                col1.color = g1;
                var s1 = fl.sizeOverLifetime; s1.enabled = true; s1.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
                var n1 = fl.noise; n1.enabled = true; n1.strength = 0.15f; n1.frequency = 2.5f; n1.scrollSpeed = 1.2f;
                fl.GetComponent<ParticleSystemRenderer>().maxParticleSize = 0.3f;

                // sparks drifting up
                var sp = Emitter(go.transform, "Sparks", fireMat, new Vector3(L - 0.7f, D * 0.4f, 0.01f), 0.08f);
                var m2 = sp.main;
                m2.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 2.2f);
                m2.startSpeed = new ParticleSystem.MinMaxCurve(0.7f, 1.6f);
                m2.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.05f);
                m2.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.35f, 1f), new Color(1f, 0.45f, 0.12f, 1f));
                m2.maxParticles = 60; m2.gravityModifier = -0.05f;
                var e2 = sp.emission; e2.rateOverTime = 2.2f * L;
                var col2 = sp.colorOverLifetime; col2.enabled = true;
                var g2 = new Gradient();
                g2.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.4f, 0.1f), 1f) },
                           new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
                col2.color = g2;
                var n2 = sp.noise; n2.enabled = true; n2.strength = 0.6f; n2.frequency = 1.2f;
                sp.GetComponent<ParticleSystemRenderer>().maxParticleSize = 0.04f;

                // thin smoke
                var sm = Emitter(go.transform, "Smoke", smokeMat, new Vector3(L - 1f, D * 0.3f, 0.01f), 0.3f);
                var m3 = sm.main;
                m3.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
                m3.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
                m3.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
                m3.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                m3.startColor = new Color(0.22f, 0.2f, 0.19f, 0.22f);
                m3.maxParticles = 30;
                var e3 = sm.emission; e3.rateOverTime = 0.9f * L;
                var col3 = sm.colorOverLifetime; col3.enabled = true;
                var g3 = new Gradient();
                g3.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                           new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
                col3.color = g3;
                var s3 = sm.sizeOverLifetime; s3.enabled = true; s3.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));
                sm.GetComponent<ParticleSystemRenderer>().maxParticleSize = 1.5f;

                // the glow on the ground and the hesco either side
                var lg = new GameObject("FireLight");
                lg.transform.SetParent(go.transform, false);
                lg.transform.localPosition = new Vector3(0f, 0.7f, 0f);
                var l = lg.AddComponent<Light>();
                l.type = LightType.Point; l.color = new Color(1f, 0.52f, 0.2f); l.range = 6f; l.intensity = 2.4f; l.shadows = LightShadows.None;
                var flick = lg.AddComponent<FlickerLight>();
                flick.style = FlickerLight.Style.Fire; flick.minShare = 0.65f; flick.speed = 8f;
                sb.AppendLine($"  gate {g.angle:0} deg: {g.width:0.0} m wide -> fire line {L:0.0} x {D:0.0} m at {centre:F1}");
                gi++;
            }
            var scene = safe.gameObject.scene;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            sb.AppendLine("  scene saved");
            return sb.ToString();
        }

        /// A particle system on a flat box at the ground, emitting straight up.
        static ParticleSystem Emitter(Transform parent, string name, Material mat, Vector3 box, float lift)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, lift, 0f);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // local +Z up
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.playOnAwake = true; main.loop = true; main.prewarm = true;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = box;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return ps;
        }

        /// Pictures: straight down over the compound, and from inside towards a few directions.
        public static string Views(string tag)
        {
            var safe = Object.FindAnyObjectByType<SafeBase>(FindObjectsInactive.Include);
            var main = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();
            if (safe == null || main == null) return "FAILED: no SafeBase or camera.";
            Directory.CreateDirectory(OutDir);
            var camGo = new GameObject("_CompoundCam") { hideFlags = HideFlags.HideAndDontSave };
            var lightGo = new GameObject("_CompoundLight") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(1600, 1600, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            var shots = new List<string>();
            try
            {
                var l = lightGo.AddComponent<Light>();
                l.type = LightType.Directional; l.intensity = 1.6f; l.shadows = LightShadows.None;
                lightGo.transform.rotation = Quaternion.Euler(60f, 30f, 0f);
                var cam = camGo.AddComponent<Camera>();
                cam.CopyFrom(main);
                cam.targetTexture = rt;
                cam.farClipPlane = 400f;
                var c = safe.Center;
                float ring = safe.radius + 6f;
                cam.orthographic = true; cam.orthographicSize = ring + 6f;
                cam.transform.SetPositionAndRotation(c + Vector3.up * 120f, Quaternion.Euler(90f, 0f, 0f));
                Shot(cam, rt, $"{OutDir}/Compound_{tag}_Top.png"); shots.Add("Top");
                cam.orthographic = false; cam.fieldOfView = 60f;
                rt.Release(); rt.width = 1600; rt.height = 900; rt.Create();
                // a high oblique look over the whole compound from the four sides
                foreach (float a in new[] { 0f, 90f, 180f, 270f })
                {
                    var eye = c + Dir(a) * (ring + 16f) + Vector3.up * 22f;
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(c - eye));
                    Shot(cam, rt, $"{OutDir}/Compound_{tag}_Side{a:0}.png"); shots.Add($"Side{a:0}");
                }
            }
            finally
            {
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(lightGo);
                rt.Release(); Object.DestroyImmediate(rt);
            }
            return $"Compound views {tag}: {string.Join(", ", shots)} in {OutDir}";
        }

        static void Shot(Camera cam, RenderTexture rt, string path)
        {
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
