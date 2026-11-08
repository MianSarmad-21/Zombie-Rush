using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using ZombieRush.Waves;
using ZombieRush.World;

namespace ZombieRush.EditorTools
{
    /// Zombie Rush > Level > Survey Map
    ///
    /// Draws a top-down plan of the open level to Docs/Map/Survey.png and writes
    /// Docs/Map/Survey.json, for planning the layout:
    ///   ground (terrain paint + height shading), where zombies can walk (blue tint),
    ///   every building footprint (red, numbered in the JSON), the safe base (green),
    ///   the fight-zone rings from the start size to the maximum (yellow, max in orange),
    ///   spawn pads (magenta). The JSON lists each building with its position and size,
    ///   which ring first reaches it, and the pixel mapping of the picture.
    /// Changes nothing in the scene.
    public static class MapSurveyTools
    {
        const string OutDir = "Docs/Map";
        const int Size = 2048;
        const string MapRoot = "Map";
        const string NavMeshPath = "Assets/Data/MainLevel_ZombieNavMesh.asset";

        [MenuItem("Zombie Rush/Level/Survey Map (top-down plan)", priority = 40)]
        static void RunFromMenu()
        {
            string msg = Run();
            EditorUtility.DisplayDialog("Survey Map", msg, "OK");
        }

        class Group { public string name, path; public Bounds b; public int renderers; }

        /// cityWalk = true: bakes a throw-away walk map over the whole city (not saved) and
        /// draws it to CityWalk.png, to see which streets are open.
        public static string Run(bool cityWalk = false)
        {
            var zone = Object.FindAnyObjectByType<ZoneManager>(FindObjectsInactive.Include);
            var safe = Object.FindAnyObjectByType<SafeBase>(FindObjectsInactive.Include);
            var waves = Object.FindAnyObjectByType<WaveManager>(FindObjectsInactive.Include);
            var map = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(t => t.parent == null && t.name == MapRoot);
            if (zone == null || map == null) return "Open MainLevel first (needs ZoneManager and a root object named Map).";

            // ---- world area: terrains, else everything under Map
            var terrains = map.GetComponentsInChildren<Terrain>(true);
            Bounds world = default;
            bool has = false;
            foreach (var t in terrains)
            {
                var tb = new Bounds(t.transform.position + t.terrainData.size * 0.5f, t.terrainData.size);
                if (!has) { world = tb; has = true; } else world.Encapsulate(tb);
            }
            // Highest-detail meshes only: LOD copies and stray giant meshes would distort footprints.
            var renderers = map.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.enabled && !(r is ParticleSystemRenderer) && !IsLowLod(r) && r.bounds.size.x < 150f && r.bounds.size.z < 150f)
                .ToArray();
            foreach (var r in renderers) { if (!has) { world = r.bounds; has = true; } else world.Encapsulate(r.bounds); }
            float span = cityWalk ? 1000f : Mathf.Max(world.size.x, world.size.z) * 1.02f;
            Vector3 origin = cityWalk ? new Vector3(330f, 0f, 300f) : new Vector3(world.center.x - span / 2f, 0f, world.center.z - span / 2f);
            float ppm = Size / span;
            System.Func<Vector3, Vector2Int> px = p => new Vector2Int(Mathf.RoundToInt((p.x - origin.x) * ppm), Mathf.RoundToInt((p.z - origin.z) * ppm));

            var img = new Color32[Size * Size];
            for (int i = 0; i < img.Length; i++) img[i] = new Color32(20, 20, 24, 255);

            // ---- ground: terrain paint colour, shaded by slope
            foreach (var t in terrains) PaintTerrain(t, img, origin, ppm);

            // ---- buildings: group renderers by outermost prefab instance (else the top child under Map's containers)
            var groups = new Dictionary<Transform, Group>();
            foreach (var r in renderers)
            {
                if (r.GetComponentInParent<Terrain>() != null) continue;
                var key = GroupRoot(r.transform, map);
                if (!groups.TryGetValue(key, out var g))
                {
                    g = new Group { name = key.name, path = PathOf(key, map), b = r.bounds };
                    groups[key] = g;
                }
                else g.b.Encapsulate(r.bounds);
                g.renderers++;
            }
            var buildings = groups.Values.Where(g => g.b.size.y >= 4f && Mathf.Max(g.b.size.x, g.b.size.z) >= 5f && Mathf.Max(g.b.size.x, g.b.size.z) < span * 0.5f)
                                         .OrderBy(g => g.b.center.x).ThenBy(g => g.b.center.z).ToList();
            int props = groups.Count - buildings.Count;
            foreach (var g in buildings)
            {
                var a = px(g.b.min); var c = px(g.b.max);
                FillRect(img, a, c, new Color32(190, 50, 45, 255), 0.75f);
                Rect(img, a, c, new Color32(255, 120, 100, 255));
            }

            // ---- walkable area for zombies
            var nav = cityWalk ? BakeCity(map) : AssetDatabase.LoadAssetAtPath<NavMeshData>(NavMeshPath);
            int navTris = 0;
            if (nav != null)
            {
                NavMesh.RemoveAllNavMeshData();
                var inst = NavMesh.AddNavMeshData(nav);
                var tri = NavMesh.CalculateTriangulation();
                navTris = tri.indices.Length / 3;
                for (int i = 0; i < tri.indices.Length; i += 3)
                    FillTri(img, px(tri.vertices[tri.indices[i]]), px(tri.vertices[tri.indices[i + 1]]), px(tri.vertices[tri.indices[i + 2]]), cityWalk ? new Color32(120, 220, 255, 255) : new Color32(60, 110, 200, 255), cityWalk ? 0.6f : 0.35f);
                NavMesh.RemoveNavMeshData(inst);
            }

            // ---- rings
            var rings = new List<float>();
            for (float r = zone.startRadius; r < zone.maxRadius - 0.01f; r += Mathf.Max(1f, zone.expandStep)) rings.Add(r);
            rings.Add(zone.maxRadius);
            var arc = zone.ArcCenter;
            var fwd = zone.Forward;
            for (int i = 0; i < rings.Count; i++)
            {
                var col = i == rings.Count - 1 ? new Color32(255, 140, 0, 255) : new Color32(250, 220, 60, 255);
                DrawZone(img, px, new ZoneShape(arc, rings[i], fwd), col, i == 0 || i == rings.Count - 1 ? 2 : 1);
            }
            if (safe != null)
            {
                var sb = new ZoneShape(safe.Center - safe.Forward * safe.backOffset, safe.radius, safe.Forward);
                DrawZone(img, px, sb, new Color32(60, 230, 90, 255), 3);
                if (safe.building != null)
                {
                    var bb = RendererBounds(safe.building);
                    if (bb.HasValue) Rect(img, px(bb.Value.min), px(bb.Value.max), new Color32(60, 230, 90, 255));
                }
            }
            if (waves != null && waves.spawnPoints != null)
                foreach (var sp in waves.spawnPoints.Where(s => s != null)) Dot(img, px(sp.position), 6, new Color32(240, 60, 240, 255));
            if (zone.playerBase != null) Dot(img, px(zone.playerBase.position), 8, new Color32(255, 255, 255, 255));

            // ---- save picture
            Directory.CreateDirectory(OutDir);
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.SetPixels32(img);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(OutDir, cityWalk ? "CityWalk.png" : "Survey.png"), tex.EncodeToPNG());
            if (cityWalk) return $"City walk map drawn: {navTris} walkable triangles. Saved Docs/Map/CityWalk.png.";
            Object.DestroyImmediate(tex);

            // ---- data
            float RingOf(Bounds b)
            {
                foreach (var r in rings)
                    if (new ZoneShape(arc, r, fwd).Contains(b.center)) return r;
                return -1f;
            }
            var sbj = new StringBuilder();
            sbj.Append("{\n");
            sbj.Append($"  \"picture\": {{\"file\": \"Survey.png\", \"size\": {Size}, \"originX\": {origin.x:0.##}, \"originZ\": {origin.z:0.##}, \"pixelsPerMetre\": {ppm:0.####}, \"note\": \"pixel x = (worldX - originX) * ppm, pixel y from the BOTTOM = (worldZ - originZ) * ppm\"}},\n");
            sbj.Append($"  \"world\": {{\"min\": {V(world.min)}, \"max\": {V(world.max)}}},\n");
            sbj.Append($"  \"terrains\": [{string.Join(", ", terrains.Select(t => $"{{\"name\": \"{t.name}\", \"pos\": {V(t.transform.position)}, \"size\": {V(t.terrainData.size)}}}"))}],\n");
            sbj.Append($"  \"zone\": {{\"arcCenter\": {V(arc)}, \"forward\": {V(fwd)}, \"startRadius\": {zone.startRadius}, \"maxRadius\": {zone.maxRadius}, \"expandStep\": {zone.expandStep}, \"expandCost\": {zone.expandCost}, \"backOffset\": {zone.backOffset}, \"halfCircle\": {(zone.halfCircle ? "true" : "false")}, \"playerBase\": {(zone.playerBase != null ? V(zone.playerBase.position) : "null")}}},\n");
            if (safe != null)
                sbj.Append($"  \"safeBase\": {{\"pos\": {V(safe.Center)}, \"radius\": {safe.radius}, \"backOffset\": {safe.backOffset}, \"building\": \"{(safe.building != null ? PathOf(safe.building, null) : "")}\"}},\n");
            sbj.Append($"  \"navMeshTriangles\": {navTris},\n");
            sbj.Append($"  \"propGroups\": {props},\n");
            sbj.Append("  \"buildings\": [\n");
            for (int i = 0; i < buildings.Count; i++)
            {
                var g = buildings[i];
                sbj.Append($"    {{\"i\": {i}, \"name\": \"{Esc(g.name)}\", \"path\": \"{Esc(g.path)}\", \"center\": {V(g.b.center)}, \"size\": {V(g.b.size)}, \"ring\": {RingOf(g.b):0.#}, \"distFromArc\": {Flat(g.b.center - arc):0.#}}}{(i < buildings.Count - 1 ? "," : "")}\n");
            }
            sbj.Append("  ],\n");
            sbj.Append("  \"buildingsPerRing\": {");
            sbj.Append(string.Join(", ", rings.Select(r => $"\"{r:0}\": {buildings.Count(b => RingOf(b.b) == r)}")));
            sbj.Append($", \"outsideMax\": {buildings.Count(b => RingOf(b.b) < 0)}}}\n}}\n");
            File.WriteAllText(Path.Combine(OutDir, "Survey.json"), sbj.ToString());

            return $"Map surveyed: {buildings.Count} buildings, {props} prop groups, {rings.Count} zone rings. Saved Docs/Map/Survey.png and Survey.json.";
        }

        // ------------------------------------------------------------------ helpers

        /// In-memory walk map over everything under Map that stands above the ground (coarse, for planning only).
        static NavMeshData BakeCity(Transform map)
        {
            var sources = new List<NavMeshBuildSource>();
            UnityEngine.AI.NavMeshBuilder.CollectSources(map, ~0, NavMeshCollectGeometry.PhysicsColliders, 0,
                new List<NavMeshBuildMarkup>(), sources);
            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = 0.45f; settings.agentHeight = 1.8f; settings.agentClimb = 0.45f; settings.agentSlope = 40f;
            settings.minRegionArea = 20f;
            settings.overrideVoxelSize = true;
            settings.voxelSize = 0.3f;
            var bounds = new Bounds(new Vector3(830f, 0f, 800f), new Vector3(1000f, 120f, 1000f));
            EditorUtility.DisplayProgressBar("Survey", "Baking a planning walk map over the city...", 0.5f);
            try { return UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity); }
            finally { EditorUtility.ClearProgressBar(); }
        }

        static Transform GroupRoot(Transform t, Transform map)
        {
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(t.gameObject);
            if (root != null && root.transform != map && root.transform.IsChildOf(map)) return root.transform;
            // Not a prefab: the direct child of Map, or one level lower when that child is a
            // container holding many things (e.g. Map/Buildings/building3).
            var cur = t;
            while (cur.parent != null && cur.parent != map) cur = cur.parent;
            if (cur.childCount > 12 && t != cur)
            {
                var below = t;
                while (below.parent != cur) below = below.parent;
                return below;
            }
            return cur;
        }

        static string PathOf(Transform t, Transform stop)
        {
            var parts = new List<string>();
            for (var c = t; c != null && c != stop; c = c.parent) parts.Add(c.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        static Bounds? RendererBounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return null;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static bool IsLowLod(Renderer r)
        {
            var group = r.GetComponentInParent<LODGroup>();
            if (group != null)
            {
                var lods = group.GetLODs();
                if (lods.Length > 1 && !lods[0].renderers.Contains(r)) return true;
            }
            string n = r.name.ToLowerInvariant();
            return n.Contains("lod1") || n.Contains("lod2") || n.Contains("lod3");
        }

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
        static string V(Vector3 v) => $"[{v.x:0.#}, {v.y:0.#}, {v.z:0.#}]";
        static string Esc(string s) => s.Replace("\\", "/").Replace("\"", "'");

        static void PaintTerrain(Terrain t, Color32[] img, Vector3 origin, float ppm)
        {
            var td = t.terrainData;
            var layers = td.terrainLayers;
            var layerCol = layers.Select(l =>
            {
                if (l == null || l.diffuseTexture == null) return new Color(0.4f, 0.4f, 0.4f);
                var path = AssetDatabase.GetAssetPath(l.diffuseTexture);
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                Color avg = new Color(0.4f, 0.4f, 0.4f);
                if (imp != null && imp.isReadable) avg = Average(l.diffuseTexture);
                else
                {
                    var tmp = RenderTexture.GetTemporary(16, 16);
                    Graphics.Blit(l.diffuseTexture, tmp);
                    var prev = RenderTexture.active; RenderTexture.active = tmp;
                    var small = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                    small.ReadPixels(new Rect(0, 0, 16, 16), 0, 0); small.Apply();
                    RenderTexture.active = prev; RenderTexture.ReleaseTemporary(tmp);
                    avg = Average(small); Object.DestroyImmediate(small);
                }
                return avg;
            }).ToArray();
            int aw = td.alphamapWidth, ah = td.alphamapHeight;
            var alpha = layers.Length > 0 ? td.GetAlphamaps(0, 0, aw, ah) : null;
            Vector3 tp = t.transform.position, ts = td.size;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((tp.x - origin.x) * ppm)), x1 = Mathf.Min(Size - 1, Mathf.CeilToInt((tp.x + ts.x - origin.x) * ppm));
            int y0 = Mathf.Max(0, Mathf.FloorToInt((tp.z - origin.z) * ppm)), y1 = Mathf.Min(Size - 1, Mathf.CeilToInt((tp.z + ts.z - origin.z) * ppm));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float u = ((x / ppm + origin.x) - tp.x) / ts.x, v = ((y / ppm + origin.z) - tp.z) / ts.z;
                    if (u < 0 || u > 1 || v < 0 || v > 1) continue;
                    Color c = new Color(0.35f, 0.35f, 0.35f);
                    if (alpha != null)
                    {
                        int ax = Mathf.Clamp((int)(u * (aw - 1)), 0, aw - 1), ay = Mathf.Clamp((int)(v * (ah - 1)), 0, ah - 1);
                        c = Color.black;
                        for (int l = 0; l < layerCol.Length; l++) c += layerCol[l] * alpha[ay, ax, l];
                    }
                    float steep = td.GetSteepness(u, v);
                    Vector3 n = td.GetInterpolatedNormal(u, v);
                    float shade = Mathf.Clamp01(0.55f + 0.45f * Vector3.Dot(n, new Vector3(-0.4f, 0.8f, 0.45f).normalized)) * (steep > 25f ? 0.6f : 1f);
                    c *= shade * 1.4f;
                    img[y * Size + x] = new Color(c.r, c.g, c.b, 1f);
                }
        }

        static Color Average(Texture2D t)
        {
            var p = t.GetPixels32();
            long r = 0, g = 0, b = 0;
            foreach (var c in p) { r += c.r; g += c.g; b += c.b; }
            float n = Mathf.Max(1, p.Length) * 255f;
            return new Color(r / n, g / n, b / n);
        }

        static void Blend(Color32[] img, int x, int y, Color32 c, float a)
        {
            if (x < 0 || y < 0 || x >= Size || y >= Size) return;
            int i = y * Size + x;
            img[i] = Color32.Lerp(img[i], c, a);
        }

        static void FillRect(Color32[] img, Vector2Int a, Vector2Int b, Color32 c, float alpha)
        {
            for (int y = Mathf.Min(a.y, b.y); y <= Mathf.Max(a.y, b.y); y++)
                for (int x = Mathf.Min(a.x, b.x); x <= Mathf.Max(a.x, b.x); x++) Blend(img, x, y, c, alpha);
        }

        static void Rect(Color32[] img, Vector2Int a, Vector2Int b, Color32 c)
        {
            Line(img, new Vector2Int(a.x, a.y), new Vector2Int(b.x, a.y), c, 1);
            Line(img, new Vector2Int(b.x, a.y), new Vector2Int(b.x, b.y), c, 1);
            Line(img, new Vector2Int(b.x, b.y), new Vector2Int(a.x, b.y), c, 1);
            Line(img, new Vector2Int(a.x, b.y), new Vector2Int(a.x, a.y), c, 1);
        }

        static void Dot(Color32[] img, Vector2Int p, int r, Color32 c)
        {
            for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                    if (x * x + y * y <= r * r) Blend(img, p.x + x, p.y + y, c, 1f);
        }

        static void Line(Color32[] img, Vector2Int a, Vector2Int b, Color32 c, int w)
        {
            int n = Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y), 1);
            for (int i = 0; i <= n; i++)
            {
                int x = a.x + (b.x - a.x) * i / n, y = a.y + (b.y - a.y) * i / n;
                for (int dy = -w + 1; dy < w; dy++)
                    for (int dx = -w + 1; dx < w; dx++) Blend(img, x + dx, y + dy, c, 1f);
            }
        }

        static void DrawZone(Color32[] img, System.Func<Vector3, Vector2Int> px, ZoneShape s, Color32 c, int w)
        {
            const int steps = 180;
            Vector2Int prev = px(s.center + s.Direction(0f) * s.radius);
            for (int i = 1; i <= steps; i++)
            {
                var p = px(s.center + s.Direction(i / (float)steps) * s.radius);
                Line(img, prev, p, c, w);
                prev = p;
            }
            if (s.Half) Line(img, px(s.center - s.Right * s.radius), px(s.center + s.Right * s.radius), c, w);
        }

        static void FillTri(Color32[] img, Vector2Int a, Vector2Int b, Vector2Int c, Color32 col, float alpha)
        {
            int minX = Mathf.Max(0, Mathf.Min(a.x, Mathf.Min(b.x, c.x))), maxX = Mathf.Min(Size - 1, Mathf.Max(a.x, Mathf.Max(b.x, c.x)));
            int minY = Mathf.Max(0, Mathf.Min(a.y, Mathf.Min(b.y, c.y))), maxY = Mathf.Min(Size - 1, Mathf.Max(a.y, Mathf.Max(b.y, c.y)));
            float Edge(Vector2Int p, Vector2Int q, int x, int y) => (q.x - p.x) * (y - p.y) - (q.y - p.y) * (x - p.x);
            float area = Edge(a, b, c.x, c.y);
            if (Mathf.Abs(area) < 0.5f) return;
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    float w0 = Edge(b, c, x, y), w1 = Edge(c, a, x, y), w2 = Edge(a, b, x, y);
                    if ((w0 >= 0 && w1 >= 0 && w2 >= 0) || (w0 <= 0 && w1 <= 0 && w2 <= 0)) Blend(img, x, y, col, alpha);
                }
        }
    }
}
