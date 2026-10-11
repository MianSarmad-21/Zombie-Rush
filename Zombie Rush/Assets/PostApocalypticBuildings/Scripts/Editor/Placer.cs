using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Random = System.Random;

namespace AutoPlacer
{
    /// <summary>
    /// Lays out a ruined city from a <see cref="Catalog"/>. Streets carve the area into
    /// blocks; blocks are ranked by distance from the downtown centre so a fixed share of them become
    /// downtown (always at least one, whatever the city size). Downtown blocks are filled with big
    /// buildings, the rest with medium/small ones. Big rubble is then clustered only around the big
    /// buildings, and small debris is scattered thinly. Everything is instantiated as prefab links
    /// under one undoable root. Editor-only.
    /// </summary>
    public static class Placer
    {
        struct Placement
        {
            public GameObject prefab;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
        }

        // A block is a rectangle on the XZ plane (x = X, y = Z).
        struct Block { public Rect rect; public Zone zone; }

        // Where a building sits, so debris can be clustered around it. 'big' buildings also get rubble.
        struct Anchor { public Vector2 xz; public float radius; public bool big; }

        // One straight piece of an organic avenue (avenues are chains of these).
        struct RoadSeg { public Vector2 a, b; public float half; }

        public static GameObject Generate(PlacementCatalog catalog, Terrain terrain = null)
        {
            if (catalog == null) { Debug.LogError("[Placer] No catalog."); return null; }
            catalog.EnsureMeasured();
            foreach (PlacementPiece p in catalog.pieces)
                if (p != null && p.prefab != null) p.family = PlacementCatalog.FamilyKey(p.prefab.name);

            PlacementSettings s = catalog.settings;
            if (!catalog.HasPlaceable(PieceKind.Building))
            {
                EditorUtility.DisplayDialog("Nothing to place",
                    PlacementAuthoringGate.Enabled
                        ? "The catalog has no enabled Building pieces. Scan a prefab folder first, and make sure some pieces are classified as Building."
                        : "The catalog has no enabled Building pieces. Add some prefabs on the catalog and set their Kind to Building.",
                    "OK");
                return null;
            }

            Runner.Abort();   // cancel any placement still running before we clear/rebuild
            Clear(catalog);

            var rng = new Random(s.seed);
            var buildings = new List<Placement>();
            var rubble = new List<Placement>();
            var debris = new List<Placement>();
            var streetDebris = new List<Placement>();
            var anchors = new List<Anchor>();

            // Size buckets.
            var bySize = new Dictionary<SizeClass, List<PlacementPiece>>();
            foreach (SizeClass sc in System.Enum.GetValues(typeof(SizeClass)))
                bySize[sc] = new List<PlacementPiece>();
            var allBuildings = new List<PlacementPiece>(catalog.GetKind(PieceKind.Building));
            foreach (PlacementPiece b in allBuildings) bySize[b.sizeClass].Add(b);

            // GetKind reuses one buffer, so copy each kind out before the next call.
            var rubblePieces = new List<PlacementPiece>(catalog.GetKind(PieceKind.Rubble));
            var debrisPieces = new List<PlacementPiece>(catalog.GetKind(PieceKind.SmallDebris));
            var streetDebrisPieces = new List<PlacementPiece>(catalog.GetKind(PieceKind.StreetDebris));

            // The "big buildings" that define downtown. Large/Skyscraper if we have them, otherwise the
            // tallest quarter of whatever buildings exist - so there is ALWAYS a big set to draw from,
            // even when nothing measured tall enough to be classed Large.
            var bigBuildings = new List<PlacementPiece>();
            bigBuildings.AddRange(bySize[SizeClass.Skyscraper]);
            bigBuildings.AddRange(bySize[SizeClass.Large]);
            if (bigBuildings.Count == 0 && allBuildings.Count > 0)
            {
                allBuildings.Sort((a, b) => b.Height.CompareTo(a.Height));
                int take = Mathf.Max(1, Mathf.RoundToInt(allBuildings.Count * 0.25f));
                for (int i = 0; i < take; i++) bigBuildings.Add(allBuildings[i]);
            }
            var bigSet = new HashSet<PlacementPiece>(bigBuildings);

            List<Block> blocks = BuildBlocks(s, rng);
            List<RoadSeg> avenues = BuildAvenues(s, rng);

            foreach (Block block in blocks)
            {
                if (block.zone == Zone.Downtown)
                    FillDowntownBlock(block, bigBuildings, s, rng, buildings, anchors);
                else if (bigBuildings.Count > 0 && rng.NextDouble() < s.outskirtsTallChance)
                    PlaceLoneTower(block, bigBuildings, s, rng, buildings, anchors); // rare survivor tower
                else
                    FillBlock(block, bySize, bigSet, s, rng, buildings, anchors);
            }

            // Carve building-free corridors where the avenues run.
            if (avenues.Count > 0)
            {
                buildings.RemoveAll(pl => NearAnyRoad(pl.position.x, pl.position.z, avenues, s.blockSetback));
                anchors.RemoveAll(a => NearAnyRoad(a.xz.x, a.xz.y, avenues, s.blockSetback));
            }

            ScatterRubble(s, rng, anchors, rubblePieces, rubble);
            ScatterSmallDebris(s, rng, anchors, blocks, avenues, debrisPieces, debris);
            ScatterStreetDebris(s, rng, blocks, avenues, streetDebrisPieces, streetDebris);

            var background = new List<Placement>();
            ScatterBackground(s, rng, bigBuildings, rubblePieces, background);

            // --- instantiate (batched across editor frames) -------------------
            var root = new GameObject(s.rootObjectName);

            int bigCount = 0;
            foreach (Anchor a in anchors) if (a.big) bigCount++;
            string summary = $"[Placer] {buildings.Count} buildings, {rubble.Count} rubble, {debris.Count} debris; " +
                             $"{bigCount} big buildings across {blocks.Count} blocks. " +
                             (bigBuildings.Count == 0 ? "No buildings available!" : "");

            Runner.Start(root, s, terrain, blocks, avenues, summary, new[]
            {
                (buildings, "Buildings"),
                (rubble, "Rubble"),
                (debris, "Debris"),
                (streetDebris, "Street Debris"),
                (background, "Background"),
            });
            return root;
        }

        public static void Clear(PlacementCatalog catalog)
        {
            Runner.Abort();
            PlacementSettings settings = catalog != null ? catalog.settings : null;
            string name = settings != null ? settings.rootObjectName : "Placed City";
            // Plain destroy (not Undo.DestroyObjectImmediate): undo of a huge hierarchy is a slow blocking
            // call. The city isn't Undo-registered, so use the Clear button to remove it.
            foreach (GameObject go in Object.FindObjectsOfType<GameObject>())
                if (go != null && go.transform.parent == null && go.name == name)
                    Object.DestroyImmediate(go);
        }

        // --- placement progress (read/controlled by the window) --------------
        public static bool IsPlacing => Runner.Running;
        public static float PlacementProgress => Runner.Progress;
        public static int PlacementDone => Runner.Done;
        public static int PlacementTotal => Runner.Total;
        public static void CancelPlacement() => Runner.RequestCancel();

        // --- street grid + districts -----------------------------------------

        static List<Block> BuildBlocks(PlacementSettings s, Random rng)
        {
            List<(float start, float size)> cols = Axis(s.citySize.x, s, rng);
            List<(float start, float size)> rows = Axis(s.citySize.y, s, rng);
            int nc = cols.Count, nr = rows.Count;

            var blocks = new List<Block>(nc * nr);
            for (int ci = 0; ci < nc; ci++)
                for (int ri = 0; ri < nr; ri++)
                {
                    // Merge: with some chance a block extends over the street to the next column/row start,
                    // so blocks vary in size and the streets between them break up (T-junctions, dead ends).
                    float w = cols[ci].size, h = rows[ri].size;
                    if (ci + 1 < nc && rng.NextDouble() < s.blockMergeChance) w = cols[ci + 1].start - cols[ci].start;
                    if (ri + 1 < nr && rng.NextDouble() < s.blockMergeChance) h = rows[ri + 1].start - rows[ri].start;
                    blocks.Add(new Block { rect = new Rect(cols[ci].start, rows[ri].start, w, h), zone = Zone.Residential });
                }

            // Organic outline: drop blocks whose centre falls outside the irregular city boundary, so the
            // city reads as a natural lumpy shape rather than a filled rectangle. Keep the full grid if the
            // cull would leave nothing (tiny cities).
            if (s.organicShape)
            {
                var kept = blocks.FindAll(b => InShape(s, b.rect.center.x, b.rect.center.y));
                if (kept.Count > 0) blocks = kept;
            }

            // District by RANK, not absolute radius, so a downtown always exists no matter the size.
            Vector2 dt = new Vector2(s.downtownCenter01.x * s.citySize.x, s.downtownCenter01.y * s.citySize.y);
            float maxDist = Mathf.Max(0.001f, s.citySize.magnitude);

            var order = new List<int>(blocks.Count);
            for (int i = 0; i < blocks.Count; i++) order.Add(i);

            var key = new float[blocks.Count];
            for (int i = 0; i < blocks.Count; i++)
            {
                Vector2 ctr = blocks[i].rect.center;
                float noise = Mathf.PerlinNoise(ctr.x * s.zoneNoiseScale + 11.3f, ctr.y * s.zoneNoiseScale + 4.7f);
                key[i] = Vector2.Distance(ctr, dt) + (noise - 0.5f) * s.zoneNoiseAmount * maxDist;
            }
            order.Sort((a, b) => key[a].CompareTo(key[b]));

            int n = blocks.Count;
            int downtown = Mathf.Clamp(Mathf.RoundToInt(n * s.downtownRadius01 * s.downtownRadius01), 1, n);
            int midtown = Mathf.Clamp(Mathf.RoundToInt(n * s.midtownRadius01 * s.midtownRadius01), downtown, n) - downtown;

            for (int rank = 0; rank < n; rank++)
            {
                Block b = blocks[order[rank]];
                b.zone = rank < downtown ? Zone.Downtown
                       : rank < downtown + midtown ? Zone.Midtown
                       : Zone.Residential;
                blocks[order[rank]] = b;
            }
            return blocks;
        }

        static List<(float start, float size)> Axis(float total, PlacementSettings s, Random rng)
        {
            var list = new List<(float, float)>();
            float pos = 0f;
            int i = 0;
            float minBlock = Mathf.Max(1f, s.blockSize.x);
            while (pos < total - minBlock)
            {
                float w = Mathf.Lerp(s.blockSize.x, Mathf.Max(s.blockSize.x, s.blockSize.y), (float)rng.NextDouble());
                w = Mathf.Min(w, total - pos);
                list.Add((pos, w));
                float street = (i % Mathf.Max(1, s.mainStreetEvery) == s.mainStreetEvery - 1) ? s.mainStreetWidth : s.sideStreetWidth;
                pos += w + street;
                i++;
            }
            if (list.Count == 0) list.Add((0f, total));
            return list;
        }

        // --- organic avenues -------------------------------------------------

        /// <summary>A few long roads that wander across the city at their own angles, with gentle curves
        /// and the odd mid-city dead end - the diagonals/curves a pure grid can't produce.</summary>
        static List<RoadSeg> BuildAvenues(PlacementSettings s, Random rng)
        {
            var roads = new List<RoadSeg>();
            if (s.avenueCount <= 0 || s.avenueWidth <= 0f) return roads;

            float w = s.citySize.x, h = s.citySize.y;
            float step = Mathf.Max(4f, s.avenueWidth);
            int maxSteps = Mathf.CeilToInt((w + h) / step) + 4;

            for (int k = 0; k < s.avenueCount; k++)
            {
                // Start on a random edge and head roughly across, so most avenues traverse the city.
                Vector2 pos; float ang;
                switch (rng.Next(4))
                {
                    case 0: pos = new Vector2(0f, (float)rng.NextDouble() * h); ang = 0f; break;
                    case 1: pos = new Vector2(w, (float)rng.NextDouble() * h); ang = Mathf.PI; break;
                    case 2: pos = new Vector2((float)rng.NextDouble() * w, 0f); ang = Mathf.PI * 0.5f; break;
                    default: pos = new Vector2((float)rng.NextDouble() * w, h); ang = Mathf.PI * 1.5f; break;
                }
                ang += (float)(rng.NextDouble() * 2 - 1) * 0.7f;                       // skew off the axis (diagonal)
                float half = s.avenueWidth * 0.5f * (0.8f + (float)rng.NextDouble() * 0.6f);

                for (int i = 0; i < maxSteps; i++)
                {
                    ang += (float)(rng.NextDouble() * 2 - 1) * s.avenueCurve * Mathf.Deg2Rad; // curve
                    Vector2 nb = pos + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * step;
                    roads.Add(new RoadSeg { a = pos, b = nb, half = half });
                    pos = nb;
                    if (pos.x < -step || pos.x > w + step || pos.y < -step || pos.y > h + step) break; // ran off the map
                    if (i > 2 && rng.NextDouble() < s.avenueEndChance) break;                            // dead end
                }
            }
            return roads;
        }

        static bool NearAnyRoad(float x, float z, List<RoadSeg> roads, float extra)
        {
            var p = new Vector2(x, z);
            for (int i = 0; i < roads.Count; i++)
                if (DistToSegment(p, roads[i].a, roads[i].b) <= roads[i].half + extra) return true;
            return false;
        }

        static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + ab * t);
        }

        // --- downtown: guaranteed big buildings ------------------------------

        /// <summary>
        /// Tiles a downtown block into plots sized to the largest big building and drops one big building
        /// per plot at (near) natural scale - so big buildings always appear, even in a city so small it
        /// is a single block. A tower bigger than its plot just overhangs a little rather than shrinking.
        /// </summary>
        static void FillDowntownBlock(Block block, List<PlacementPiece> bigBuildings, PlacementSettings s,
            Random rng, List<Placement> buildings, List<Anchor> anchors)
        {
            if (bigBuildings.Count == 0) return;
            Rect inner = Inset(block.rect, s.blockSetback);
            if (inner.width <= 0.5f || inner.height <= 0.5f) return;

            // Plots sized to the LARGEST big building (+ gap) so every big building fits a plot at (near)
            // natural scale - we never have to shrink a tower drastically to make it fit.
            float plot = MaxFootprint(bigBuildings) + s.buildingGap;
            int cols = Mathf.Max(1, Mathf.FloorToInt(inner.width / plot));
            int rows = Mathf.Max(1, Mathf.FloorToInt(inner.height / plot));
            float cw = inner.width / cols, ch = inner.height / rows;

            // Cluster: bias this block toward one tower family so downtown reads as a coherent skyline.
            List<PlacementPiece> domBig = ClusterSubset(bigBuildings, block.rect.center, s, 2);
            bool placedAny = false; // guarantee at least one tower per downtown block

            for (int cx = 0; cx < cols; cx++)
                for (int cz = 0; cz < rows; cz++)
                {
                    if (placedAny && rng.NextDouble() > s.downtownDensity) continue;

                    var src = (domBig != null && rng.NextDouble() < s.clusterStrength) ? domBig : bigBuildings;
                    PlacementPiece piece = WeightedPick(src, rng);
                    if (piece == null) continue;

                    // Orient the long side along the roomier cell axis.
                    bool rotate = (cw >= ch) == (piece.size.z > piece.size.x);
                    float fx = rotate ? piece.size.z : piece.size.x;
                    float fz = rotate ? piece.size.x : piece.size.z;

                    // Buildings only ever scale UP (never shrunk to fit); a tower bigger than its plot
                    // just overhangs a little. Plots are sized to the largest big building, so this is rare.
                    float scale = 1f + (float)rng.NextDouble() * s.buildingScaleJitter;

                    var centre = new Vector2(inner.xMin + (cx + 0.5f) * cw, inner.yMin + (cz + 0.5f) * ch);
                    buildings.Add(MakeBuilding(piece, centre, rotate ? 90f : 0f, scale, s, rng));
                    anchors.Add(new Anchor { xz = centre, radius = Mathf.Max(fx, fz) * 0.5f * scale, big = true });
                    placedAny = true;
                }
        }

        /// <summary>Drops a single big building in a non-downtown block - a lone tower surviving among the
        /// low-rise. Scaled up to +10% but capped to fit the block so it doesn't crash into neighbours.</summary>
        static void PlaceLoneTower(Block block, List<PlacementPiece> bigBuildings, PlacementSettings s,
            Random rng, List<Placement> buildings, List<Anchor> anchors)
        {
            Rect inner = Inset(block.rect, s.blockSetback);
            if (inner.width <= 0.5f || inner.height <= 0.5f) return;

            PlacementPiece piece = WeightedPick(bigBuildings, rng);
            if (piece == null) return;

            bool rotate = (inner.width >= inner.height) == (piece.size.z > piece.size.x);
            float fx = rotate ? piece.size.z : piece.size.x;
            float fz = rotate ? piece.size.x : piece.size.z;

            float fitScale = Mathf.Min(inner.width / Mathf.Max(0.01f, fx), inner.height / Mathf.Max(0.01f, fz));
            float scale = Mathf.Min(1f + (float)rng.NextDouble() * s.buildingScaleJitter, Mathf.Max(0.05f, fitScale));

            var centre = inner.center;
            buildings.Add(MakeBuilding(piece, centre, rotate ? 90f : 0f, scale, s, rng));
            anchors.Add(new Anchor { xz = centre, radius = Mathf.Max(fx, fz) * 0.5f * scale, big = true });
        }

        // --- ordinary blocks: shelf packing ----------------------------------

        static void FillBlock(Block block, Dictionary<SizeClass, List<PlacementPiece>> bySize,
            HashSet<PlacementPiece> bigSet, PlacementSettings s, Random rng,
            List<Placement> buildings, List<Anchor> anchors)
        {
            Rect inner = Inset(block.rect, s.blockSetback);
            if (inner.width <= 0.5f || inner.height <= 0.5f) return;

            List<(PlacementPiece piece, float weight)> pool = ZonePool(block.zone, bySize);
            if (pool.Count == 0) return;

            float smallest = float.MaxValue;
            foreach (var e in pool) smallest = Mathf.Min(smallest, e.piece.FootprintMin);

            // Cluster: bias this block toward one building family so same-type buildings group up.
            List<(PlacementPiece piece, float weight)> domPool = ClusterSubset(pool, block.rect.center, s, 1);

            float cursorX = inner.xMin, rowZ = inner.yMin, rowDepth = 0f;
            bool placedAny = false; // guarantee at least one building so blocks are never totally empty
            int guard = 0;
            while (rowZ < inner.yMax && guard++ < 2000)
            {
                float availW = inner.xMax - cursorX + s.blockSetback;
                if (availW < smallest) { rowZ += rowDepth + s.buildingGap; cursorX = inner.xMin; rowDepth = 0f; continue; }

                var src = (domPool != null && rng.NextDouble() < s.clusterStrength) ? domPool : pool;
                PlacementPiece piece = PickFitting(src, availW, rng) ?? PickFitting(pool, availW, rng);
                if (piece == null) { rowZ += rowDepth + s.buildingGap; cursorX = inner.xMin; rowDepth = 0f; continue; }

                bool rotate = ChooseOrientation(piece, availW, rng);
                float fx = rotate ? piece.size.z : piece.size.x;
                float fz = rotate ? piece.size.x : piece.size.z;

                if (rowZ + fz > inner.yMax + s.blockSetback)
                {
                    if (rowDepth <= 0f) break;
                    rowZ += rowDepth + s.buildingGap; cursorX = inner.xMin; rowDepth = 0f; continue;
                }

                var centre = new Vector2(cursorX + fx * 0.5f, rowZ + fz * 0.5f);
                if (!placedAny || rng.NextDouble() <= s.density) // density leaves gaps, but never a whole empty block
                {
                    float scale = 1f + (float)rng.NextDouble() * s.buildingScaleJitter; // up only
                    buildings.Add(MakeBuilding(piece, centre, rotate ? 90f : 0f, scale, s, rng));
                    anchors.Add(new Anchor { xz = centre, radius = Mathf.Max(fx, fz) * 0.5f, big = bigSet.Contains(piece) });
                    placedAny = true;
                }

                cursorX += fx + s.buildingGap;
                rowDepth = Mathf.Max(rowDepth, fz);
            }
        }

        static List<(PlacementPiece, float)> ZonePool(Zone zone, Dictionary<SizeClass, List<PlacementPiece>> bySize)
        {
            var pool = new List<(PlacementPiece, float)>();
            void Add(SizeClass sc, float w)
            {
                foreach (PlacementPiece p in bySize[sc]) pool.Add((p, w * Mathf.Max(0.01f, p.weight)));
            }

            if (zone == Zone.Midtown)
            {
                // A blend that fills densely, so midtown doesn't read as an empty band between the
                // downtown towers and the residential outskirts.
                Add(SizeClass.Large, 1.2f); Add(SizeClass.Medium, 2f); Add(SizeClass.Small, 1.5f);
            }
            else // Residential / outskirts
            {
                Add(SizeClass.Small, 2f); Add(SizeClass.Medium, 1.5f); Add(SizeClass.Large, 0.3f);
            }

            if (pool.Count == 0)
                foreach (SizeClass sc in System.Enum.GetValues(typeof(SizeClass)))
                    foreach (PlacementPiece p in bySize[sc]) pool.Add((p, Mathf.Max(0.01f, p.weight)));
            return pool;
        }

        static PlacementPiece PickFitting(List<(PlacementPiece piece, float weight)> pool, float availW, Random rng)
        {
            float total = 0f;
            foreach (var e in pool) if (e.piece.FootprintMin <= availW) total += e.weight;
            if (total <= 0f) return null;
            double r = rng.NextDouble() * total;
            foreach (var e in pool)
            {
                if (e.piece.FootprintMin > availW) continue;
                r -= e.weight;
                if (r <= 0) return e.piece;
            }
            return null;
        }

        static bool ChooseOrientation(PlacementPiece p, float availW, Random rng)
        {
            bool xFits = p.size.x <= availW, zFits = p.size.z <= availW;
            if (xFits && zFits) return rng.NextDouble() < 0.5;
            return zFits && !xFits;
        }

        // --- clustering (group same-family buildings) ------------------------

        /// <summary>The pieces of the one family this block leans toward, or null if clustering is off.</summary>
        static List<(PlacementPiece piece, float weight)> ClusterSubset(
            List<(PlacementPiece piece, float weight)> pool, Vector2 center, PlacementSettings s, int salt)
        {
            if (!s.clusterBuildings || pool.Count == 0) return null;
            var fams = new SortedSet<string>();
            foreach (var e in pool) if (!string.IsNullOrEmpty(e.piece.family)) fams.Add(e.piece.family);
            string dom = PickFamily(fams, center, s, salt);
            if (dom == null) return null;
            var subset = pool.FindAll(e => e.piece.family == dom);
            return subset.Count > 0 ? subset : null;
        }

        static List<PlacementPiece> ClusterSubset(
            List<PlacementPiece> pieces, Vector2 center, PlacementSettings s, int salt)
        {
            if (!s.clusterBuildings || pieces.Count == 0) return null;
            var fams = new SortedSet<string>();
            foreach (PlacementPiece p in pieces) if (!string.IsNullOrEmpty(p.family)) fams.Add(p.family);
            string dom = PickFamily(fams, center, s, salt);
            if (dom == null) return null;
            var subset = pieces.FindAll(p => p.family == dom);
            return subset.Count > 0 ? subset : null;
        }

        /// <summary>Deterministically picks one family for the cluster cell a point falls in. Neighbouring
        /// blocks in the same cell get the same family, forming a contiguous same-type pocket.</summary>
        static string PickFamily(SortedSet<string> families, Vector2 center, PlacementSettings s, int salt)
        {
            if (families.Count == 0) return null;
            var arr = new List<string>(families);
            // Nudge the sample point by noise so zone borders wander instead of forming perfect rectangles.
            float amp = s.clusterZoneSize * 0.6f;
            float nx = (Mathf.PerlinNoise(center.x * 0.05f + salt * 3.1f, center.y * 0.05f) - 0.5f) * amp;
            float nz = (Mathf.PerlinNoise(center.x * 0.05f, center.y * 0.05f + salt * 3.1f) - 0.5f) * amp;
            var p = new Vector2(center.x + nx, center.y + nz);
            return arr[CellHash(p, s.clusterZoneSize, s.seed, salt) % arr.Count];
        }

        static int CellHash(Vector2 center, float cellSize, int seed, int salt)
        {
            int cx = Mathf.FloorToInt(center.x / Mathf.Max(1f, cellSize));
            int cy = Mathf.FloorToInt(center.y / Mathf.Max(1f, cellSize));
            unchecked
            {
                int h = seed * 374761393 + salt * 668265263;
                h = (h ^ cx) * 1274126177;
                h = (h ^ cy) * -1640531527;
                h ^= h >> 13;
                return h & 0x7fffffff;
            }
        }

        // --- debris ----------------------------------------------------------

        /// <summary>Big rubble clusters ONLY around big buildings - not scattered across the map.</summary>
        static void ScatterRubble(PlacementSettings s, Random rng, List<Anchor> anchors,
            List<PlacementPiece> rubblePieces, List<Placement> rubble)
        {
            if (rubblePieces.Count == 0) return;
            foreach (Anchor a in anchors)
            {
                if (!a.big) continue;
                int count = Mathf.RoundToInt(s.rubblePerBigBuilding * (0.5f + (float)rng.NextDouble()));
                for (int i = 0; i < count; i++)
                {
                    float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float dist = a.radius + (float)rng.NextDouble() * s.rubbleSpread;
                    var p = new Vector2(a.xz.x + Mathf.Cos(ang) * dist, a.xz.y + Mathf.Sin(ang) * dist);
                    rubble.Add(MakeLoose(rubblePieces[rng.Next(rubblePieces.Count)], p, s, rng,
                        allowTilt: true, s.rubbleScaleMin, s.rubbleScaleMax));
                }
            }
        }

        /// <summary>
        /// Small debris around EVERY building - a fixed count each, spread over the footprint. Small
        /// buildings pack that count into a small area (dense); big ones spread it out (sparse).
        /// </summary>
        static void ScatterSmallDebris(PlacementSettings s, Random rng, List<Anchor> anchors,
            List<Block> blocks, List<RoadSeg> avenues, List<PlacementPiece> debrisPieces, List<Placement> debris)
        {
            if (debrisPieces.Count == 0) return;
            BlockBounds(blocks, out float minX, out float maxX, out float minZ, out float maxZ);

            // Clustered on buildings (denser around small ones), skipping anything that lands on a street.
            foreach (Anchor a in anchors)
            {
                int count = Mathf.RoundToInt(s.smallDebrisPerBuilding * (0.5f + (float)rng.NextDouble()));
                float maxR = Mathf.Max(0.5f, a.radius * s.smallDebrisSpread);
                for (int i = 0; i < count; i++)
                {
                    float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float dist = (float)rng.NextDouble() * maxR;
                    float x = a.xz.x + Mathf.Cos(ang) * dist, z = a.xz.y + Mathf.Sin(ang) * dist;
                    if (IsStreet(s, x, z, blocks, avenues, minX, maxX, minZ, maxZ)) continue;
                    debris.Add(MakeLoose(debrisPieces[rng.Next(debrisPieces.Count)], new Vector2(x, z), s, rng,
                        allowTilt: false, s.debrisScaleMin, s.debrisScaleMax));
                }
            }

            // ...plus an even scatter across the open ground (courtyards, empty blocks), never the streets.
            int areaCount = Mathf.RoundToInt(s.citySize.x * s.citySize.y / 100f * s.smallDebrisPerArea);
            for (int i = 0; i < areaCount; i++)
            {
                float x = (float)rng.NextDouble() * s.citySize.x, z = (float)rng.NextDouble() * s.citySize.y;
                if (!InShape(s, x, z)) continue; // organic shape: leave the ground outside the outline bare
                if (IsStreet(s, x, z, blocks, avenues, minX, maxX, minZ, maxZ)) continue;
                debris.Add(MakeLoose(debrisPieces[rng.Next(debrisPieces.Count)], new Vector2(x, z), s, rng,
                    allowTilt: false, s.debrisScaleMin, s.debrisScaleMax));
            }
        }

        /// <summary>Scatters Street Debris pieces ONLY on the streets and avenues.</summary>
        static void ScatterStreetDebris(PlacementSettings s, Random rng, List<Block> blocks, List<RoadSeg> avenues,
            List<PlacementPiece> pieces, List<Placement> streetDebris)
        {
            if (pieces.Count == 0) return;
            BlockBounds(blocks, out float minX, out float maxX, out float minZ, out float maxZ);

            int count = Mathf.RoundToInt(s.citySize.x * s.citySize.y / 100f * s.streetDebrisPerArea);
            for (int i = 0; i < count; i++)
            {
                float x = (float)rng.NextDouble() * s.citySize.x, z = (float)rng.NextDouble() * s.citySize.y;
                if (!IsStreet(s, x, z, blocks, avenues, minX, maxX, minZ, maxZ)) continue;
                streetDebris.Add(MakeLoose(pieces[rng.Next(pieces.Count)], new Vector2(x, z), s, rng,
                    allowTilt: false, s.debrisScaleMin, s.debrisScaleMax));
            }
        }

        /// <summary>
        /// Rings the city with distant background buildings (big buildings + large rubble) in a band that
        /// starts just outside the city and reaches out by backgroundDepth - so the skyline encloses the
        /// generated area and reads as continuing past it.
        /// </summary>
        static void ScatterBackground(PlacementSettings s, Random rng, List<PlacementPiece> bigBuildings,
            List<PlacementPiece> rubblePieces, List<Placement> background)
        {
            if (!s.backgroundEnabled || s.backgroundDepth <= 0f) return;
            bool haveBig = bigBuildings.Count > 0;
            bool haveRub = rubblePieces.Count > 0;
            if (!haveBig && !haveRub) return;

            // Inner (kept clear) rect = the city plus the gap; outer rect = inner grown by the depth.
            float inMinX = -s.backgroundGap, inMaxX = s.citySize.x + s.backgroundGap;
            float inMinZ = -s.backgroundGap, inMaxZ = s.citySize.y + s.backgroundGap;
            float outMinX = inMinX - s.backgroundDepth, outMaxX = inMaxX + s.backgroundDepth;
            float outMinZ = inMinZ - s.backgroundDepth, outMaxZ = inMaxZ + s.backgroundDepth;

            float spacing = Mathf.Max(4f, (haveBig ? MaxFootprint(bigBuildings) : MaxFootprint(rubblePieces)) + s.buildingGap);

            for (float gx = outMinX; gx < outMaxX; gx += spacing)
                for (float gz = outMinZ; gz < outMaxZ; gz += spacing)
                {
                    float cx = gx + spacing * (0.5f + (float)(rng.NextDouble() * 2 - 1) * 0.25f);
                    float cz = gz + spacing * (0.5f + (float)(rng.NextDouble() * 2 - 1) * 0.25f);
                    if (cx > inMinX && cx < inMaxX && cz > inMinZ && cz < inMaxZ) continue; // keep the city clear
                    if (rng.NextDouble() > s.backgroundDensity) continue;

                    var centre = new Vector2(cx, cz);
                    if (haveRub && (!haveBig || rng.NextDouble() < s.backgroundRubbleShare))
                        background.Add(MakeLoose(rubblePieces[rng.Next(rubblePieces.Count)], centre, s, rng,
                            allowTilt: true, s.rubbleScaleMin, s.rubbleScaleMax));
                    else
                    {
                        PlacementPiece piece = WeightedPick(bigBuildings, rng);
                        if (piece == null) continue;
                        float scale = 1f + (float)rng.NextDouble() * s.buildingScaleJitter;
                        background.Add(MakeBuilding(piece, centre, rng.Next(2) * 90f, scale, s, rng));
                    }
                }
        }

        /// <summary>A point is "street" if it's on an avenue, or inside the block grid but in a gap between blocks.</summary>
        static bool IsStreet(PlacementSettings s, float x, float z, List<Block> blocks, List<RoadSeg> avenues,
            float minX, float maxX, float minZ, float maxZ)
        {
            if (!InShape(s, x, z)) return false; // outside the organic outline = not part of the city, so not street
            if (NearAnyRoad(x, z, avenues, 0f)) return true;
            if (x < minX || x > maxX || z < minZ || z > maxZ) return false; // outside the grid = open edge, not street
            var p = new Vector2(x, z);
            for (int i = 0; i < blocks.Count; i++) if (blocks[i].rect.Contains(p)) return false; // inside a block
            return true; // in a gap between blocks
        }

        static void BlockBounds(List<Block> blocks, out float minX, out float maxX, out float minZ, out float maxZ)
        {
            minX = float.MaxValue; maxX = float.MinValue; minZ = float.MaxValue; maxZ = float.MinValue;
            foreach (Block b in blocks)
            {
                minX = Mathf.Min(minX, b.rect.xMin); maxX = Mathf.Max(maxX, b.rect.xMax);
                minZ = Mathf.Min(minZ, b.rect.yMin); maxZ = Mathf.Max(maxZ, b.rect.yMax);
            }
            if (blocks.Count == 0) { minX = maxX = minZ = maxZ = 0f; }
        }

        // --- placement maths -------------------------------------------------

        static Placement MakeBuilding(PlacementPiece piece, Vector2 centreXZ, float baseYaw, float scale, PlacementSettings s, Random rng)
        {
            // 90-degree steps: keep the packing orientation (baseYaw 0 or 90) and add a random 0/180
            // flip - so all four facings appear without changing the footprint the packer reserved.
            float yaw = s.alignToGrid ? baseYaw + rng.Next(2) * 180f : (float)rng.NextDouble() * 360f;
            Quaternion rot = TiltedYaw(yaw, piece.allowTilt, s, rng);
            return Build(piece, centreXZ, rot, scale, s);
        }

        static Placement MakeLoose(PlacementPiece piece, Vector2 centreXZ, PlacementSettings s, Random rng,
            bool allowTilt, float scaleMin, float scaleMax)
        {
            float yaw = (float)rng.NextDouble() * 360f; // debris is always fully random, never snapped to 90 steps
            Quaternion rot = TiltedYaw(yaw, allowTilt, s, rng);
            float scale = Mathf.Lerp(scaleMin, scaleMax, (float)rng.NextDouble());
            return Build(piece, centreXZ, rot, scale, s);
        }

        static Quaternion TiltedYaw(float yaw, bool allowTilt, PlacementSettings s, Random rng)
        {
            float tx = 0f, tz = 0f;
            if (allowTilt && s.tiltMaxDegrees > 0f)
            {
                tx = (float)(rng.NextDouble() * 2 - 1) * s.tiltMaxDegrees;
                tz = (float)(rng.NextDouble() * 2 - 1) * s.tiltMaxDegrees;
            }
            return Quaternion.Euler(tx, yaw, tz);
        }

        static Placement Build(PlacementPiece piece, Vector2 centreXZ, Quaternion rot, float scale, PlacementSettings s)
        {
            scale = Mathf.Max(0.05f, scale);

            float groundY = s.groundY;
            if (s.conformToGround &&
                Physics.Raycast(new Vector3(centreXZ.x, s.groundY + 1000f, centreXZ.y), Vector3.down,
                    out RaycastHit hit, 5000f, s.groundMask, QueryTriggerInteraction.Ignore))
                groundY = hit.point.y;

            // The prefab pivot is authored as the ground-contact point, and Unity rotates AND scales about
            // the pivot - so the pivot sits on the ground. A tilted piece rotates about that pivot, lifting
            // its far side, so sink it further the more it leans (default 1 unit per 4 degrees).
            float tilt = Vector3.Angle(rot * Vector3.up, Vector3.up);
            var pos = new Vector3(centreXZ.x, groundY - s.groundEmbed - tilt * s.tiltSinkPerDegree, centreXZ.y);
            return new Placement { prefab = piece.prefab, position = pos, rotation = rot, scale = Vector3.one * scale };
        }

        // --- helpers ---------------------------------------------------------

        static Rect Inset(Rect r, float m) => new Rect(r.xMin + m, r.yMin + m, r.width - 2f * m, r.height - 2f * m);

        /// <summary>
        /// True if a world XZ point is inside the city's outline. With Organic Shape off this is the whole
        /// citySize rectangle; with it on it's a noise-perturbed radial blob inscribed in that rectangle -
        /// an ellipse whose radius wobbles with the angle, so corners are cut and the edge lumps in and out.
        /// Seeded by the settings seed, so rerolling changes the outline too.
        /// </summary>
        static bool InShape(PlacementSettings s, float x, float z)
        {
            if (!s.organicShape) return true;

            float hx = Mathf.Max(1f, s.citySize.x * 0.5f);
            float hz = Mathf.Max(1f, s.citySize.y * 0.5f);
            float nx = (x - s.citySize.x * 0.5f) / hx;   // normalised so the inscribed ellipse is radius 1
            float nz = (z - s.citySize.y * 0.5f) / hz;
            float r = Mathf.Sqrt(nx * nx + nz * nz);
            if (r <= 1e-4f) return true;                 // dead centre is always in

            float ang = Mathf.Atan2(nz, nx);
            float freq = Mathf.Max(0.5f, s.organicLobes);
            float seed = s.seed * 0.001f;
            // Sample the noise around a circle so the threshold is continuous where the angle wraps at +/-pi.
            float n = Mathf.PerlinNoise(Mathf.Cos(ang) * freq + seed, Mathf.Sin(ang) * freq + seed * 1.7f);
            float threshold = Mathf.Max(0.2f, 1f + (n - 0.5f) * 2f * Mathf.Clamp01(s.organicEdge));
            return r <= threshold;
        }

        static float MaxFootprint(List<PlacementPiece> pieces)
        {
            float max = 0f;
            foreach (PlacementPiece p in pieces) max = Mathf.Max(max, p.FootprintMax);
            return max <= 0f ? 10f : max;
        }

        static PlacementPiece WeightedPick(List<PlacementPiece> pieces, Random rng)
        {
            float total = 0f;
            foreach (PlacementPiece p in pieces) total += Mathf.Max(0.01f, p.weight);
            double r = rng.NextDouble() * total;
            foreach (PlacementPiece p in pieces) { r -= Mathf.Max(0.01f, p.weight); if (r <= 0) return p; }
            return pieces.Count > 0 ? pieces[pieces.Count - 1] : null;
        }

        // --- terrain streets -------------------------------------------------

        /// <summary>Paints the street gaps (everything inside the city area that isn't a block) onto the
        /// terrain's splatmap using the chosen street layer, and clears that layer back off the blocks.</summary>
        static void PaintStreets(Terrain terrain, List<Block> blocks, List<RoadSeg> avenues, PlacementSettings s)
        {
            if (terrain == null || terrain.terrainData == null)
            { Debug.LogWarning("[Placer] Paint streets: no terrain found. Assign one in the Destroyed City Generator window or add a Terrain to the scene."); return; }
            if (s.streetLayer == null)
            { Debug.LogWarning("[Placer] Paint streets: no Street Layer assigned in the settings."); return; }

            TerrainData td = terrain.terrainData;
            int streetIdx = System.Array.IndexOf(td.terrainLayers, s.streetLayer);
            if (streetIdx < 0)
            { Debug.LogWarning($"[Placer] Paint streets: layer '{s.streetLayer.name}' is not on terrain '{terrain.name}'. Add it to the terrain's layers first."); return; }

            if (blocks.Count == 0) return;
            int res = td.alphamapResolution;
            int layers = td.alphamapLayers;
            Vector3 tpos = terrain.transform.position;
            Vector3 size = td.size;
            if (size.x <= 0f || size.z <= 0f) return;

            // Streets live only INSIDE the block grid's bounding box - between the blocks. The leftover
            // margin between the last block and City Size is city edge, not street, so it isn't painted.
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (Block b in blocks)
            {
                minX = Mathf.Min(minX, b.rect.xMin); maxX = Mathf.Max(maxX, b.rect.xMax);
                minZ = Mathf.Min(minZ, b.rect.yMin); maxZ = Mathf.Max(maxZ, b.rect.yMax);
            }

            bool swap = s.terrainStreetsSwapXZ; // insurance if your terrain's splat axes are transposed

            float[,,] maps = td.GetAlphamaps(0, 0, res, res);
            Undo.RegisterCompleteObjectUndo(td, "Paint city streets");

            float e = s.streetNoiseScale;

            // Test each texel by its CENTRE against the actual block rects. Using the centre (not a
            // floor/ceil raster) means blocks don't bleed outward and swallow the thin street gaps.
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    // World position of this texel centre (swap flips which index feeds which world axis).
                    float wx = tpos.x + ((swap ? y : x) + 0.5f) / res * size.x;
                    float wz = tpos.z + ((swap ? x : y) + 0.5f) / res * size.z;

                    if (!InShape(s, wx, wz)) continue; // organic shape: don't paint roads outside the outline

                    bool inGrid = wx >= minX && wx <= maxX && wz >= minZ && wz <= maxZ;
                    bool onAvenue = NearAnyRoad(wx, wz, avenues, 0f);
                    if (!inGrid && !onAvenue) continue; // outside the block grid and off every avenue

                    // Organic edges: warp the sample point by noise, so the street/block border wanders
                    // off the perfect grid instead of running dead straight.
                    float px = wx + (Mathf.PerlinNoise(wx * e, wz * e) - 0.5f) * 2f * s.streetEdgeWobble;
                    float pz = wz + (Mathf.PerlinNoise(wx * e + 51.3f, wz * e + 19.7f) - 0.5f) * 2f * s.streetEdgeWobble;
                    var p = new Vector2(px, pz);

                    bool inBlock = inGrid && !onAvenue;
                    if (inBlock)
                    {
                        inBlock = false;
                        for (int i = 0; i < blocks.Count; i++)
                            if (blocks[i].rect.Contains(p)) { inBlock = true; break; }
                    }

                    if (!inBlock)
                    {
                        // Strength patches: fade the road toward sand where the noise is low.
                        float t = Mathf.PerlinNoise(wx * e * 2f + 7.1f, wz * e * 2f + 3.9f);
                        PaintStreetTexel(maps, y, x, streetIdx, layers, Mathf.Lerp(s.streetStrengthMin, 1f, t));
                    }
                    else if (maps[y, x, streetIdx] > 0f)
                    {
                        // A block texel that used to be street: clear the street layer, renormalise the rest.
                        maps[y, x, streetIdx] = 0f;
                        float sum = 0f;
                        for (int l = 0; l < layers; l++) sum += maps[y, x, l];
                        if (sum > 1e-4f) for (int l = 0; l < layers; l++) maps[y, x, l] /= sum;
                        else maps[y, x, streetIdx == 0 ? Mathf.Min(1, layers - 1) : 0] = 1f;
                    }
                }

            td.SetAlphamaps(0, 0, maps);
            Debug.Log($"[Placer] Painted streets onto '{terrain.name}' using layer '{s.streetLayer.name}'.", terrain);
        }

        /// <summary>Sets the street layer to <paramref name="strength"/> at one texel and rescales the
        /// other layers into the remainder, so a partial-strength street lets the ground show through.</summary>
        static void PaintStreetTexel(float[,,] maps, int y, int x, int streetIdx, int layers, float strength)
        {
            strength = Mathf.Clamp01(strength);
            float otherSum = 0f;
            for (int l = 0; l < layers; l++) if (l != streetIdx) otherSum += maps[y, x, l];

            if (otherSum > 1e-4f)
            {
                float scale = (1f - strength) / otherSum;
                for (int l = 0; l < layers; l++)
                    maps[y, x, l] = (l == streetIdx) ? strength : maps[y, x, l] * scale;
            }
            else
            {
                // Nothing else on this texel to reveal: fall back to a base layer for the remainder.
                for (int l = 0; l < layers; l++) maps[y, x, l] = 0f;
                int baseL = streetIdx == 0 ? Mathf.Min(1, layers - 1) : 0;
                maps[y, x, streetIdx] = baseL == streetIdx ? 1f : strength;
                if (baseL != streetIdx) maps[y, x, baseL] = 1f - strength;
            }
        }

        // --- instantiation ---------------------------------------------------

        static void InstantiateOne(Placement pl, Transform group, PlacementSettings s)
        {
            if (pl.prefab == null) return;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(pl.prefab);
            if (go == null) return;
            Transform t = go.transform;
            t.SetParent(group, false);
            t.position = pl.position;
            t.rotation = pl.rotation;
            t.localScale = Vector3.Scale(pl.prefab.transform.localScale, pl.scale);
            if (s.markStatic) GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);
        }

        /// <summary>
        /// Instantiates the placements a batch per editor frame (driven by EditorApplication.update). It does
        /// NOT show a modal progress bar - a modal bar makes Unity tight-pump update without rendering real
        /// frames, which reintroduces the freeze. Progress + Cancel are shown inside the window instead, so
        /// the editor keeps rendering between batches and the city builds up live.
        /// </summary>
        static class Runner
        {
            struct Job { public List<Placement> items; public Transform group; }

            static readonly List<Job> _jobs = new List<Job>();
            static int _job, _item, _done, _total, _batch;
            static GameObject _root;
            static PlacementSettings _s;
            static Terrain _terrain;
            static List<Block> _blocks;
            static List<RoadSeg> _avenues;
            static string _summary;
            static bool _running, _cancel;

            public static bool Running => _running;
            public static int Done => _done;
            public static int Total => _total;
            public static float Progress => _total > 0 ? (float)_done / _total : 1f;
            public static void RequestCancel() => _cancel = true;

            public static void Start(GameObject root, PlacementSettings s, Terrain terrain, List<Block> blocks,
                List<RoadSeg> avenues, string summary, (List<Placement> items, string name)[] groups)
            {
                Abort();
                _root = root; _s = s; _terrain = terrain; _blocks = blocks; _avenues = avenues; _summary = summary;
                _batch = Mathf.Max(1, s.objectsPerFrame);
                _cancel = false;

                _jobs.Clear();
                _total = 0;
                foreach (var g in groups)
                {
                    if (g.items == null || g.items.Count == 0) continue;
                    var grp = new GameObject(g.name);
                    grp.transform.SetParent(root.transform, false);
                    _jobs.Add(new Job { items = g.items, group = grp.transform });
                    _total += g.items.Count;
                }
                _job = _item = _done = 0;
                _running = true;

                if (_total <= _batch) { RunAll(); return; } // small enough to finish in one go
                EditorApplication.update += Tick;
            }

            /// <summary>Stops an in-progress run and removes its half-built root (used before a fresh placement).</summary>
            public static void Abort()
            {
                if (!_running) return;
                EditorApplication.update -= Tick;
                if (_root != null) Object.DestroyImmediate(_root);
                _jobs.Clear();
                _running = false;
                _root = null;
            }

            static void RunAll()
            {
                while (_job < _jobs.Count)
                {
                    Job job = _jobs[_job];
                    for (; _item < job.items.Count; _item++) { InstantiateOne(job.items[_item], job.group, _s); _done++; }
                    _job++; _item = 0;
                }
                Finish();
            }

            static void Tick()
            {
                try
                {
                    int n = 0;
                    while (n < _batch && _job < _jobs.Count)
                    {
                        Job job = _jobs[_job];
                        if (_item >= job.items.Count) { _job++; _item = 0; continue; }
                        InstantiateOne(job.items[_item], job.group, _s);
                        _item++; _done++; n++;
                    }

                    PlacementWindow.RepaintOpen(); // refresh the in-window progress bar (no modal bar!)

                    if (_cancel) { Cancel(); return; }
                    if (_job >= _jobs.Count) Finish();
                }
                catch (System.Exception e) { Debug.LogException(e); Finish(); }
            }

            static void Cancel()
            {
                EditorApplication.update -= Tick;
                if (_root != null) Object.DestroyImmediate(_root);
                _jobs.Clear();
                _running = false;
                _root = null;
                PlacementWindow.RepaintOpen();
                Debug.Log("[Placer] Placement cancelled.");
            }

            static void Finish()
            {
                EditorApplication.update -= Tick;
                if (_root == null) { _running = false; return; }

                if (_s.paintTerrainStreets)
                    PaintStreets(_terrain != null ? _terrain : Terrain.activeTerrain, _blocks, _avenues, _s);

                // The placed city is intentionally NOT registered with Unity's Undo: doing so on a huge
                // hierarchy is a single O(N) blocking call (the old end-of-generation freeze). Use the
                // Clear button to remove a city instead of Ctrl+Z.
                EditorSceneManager.MarkSceneDirty(_root.scene);
                Debug.Log(_summary, _root);

                _jobs.Clear();
                _running = false;
                _root = null;
                PlacementWindow.RepaintOpen();
            }
        }
    }
}
