using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using ZombieRush.Data;
using ZombieRush.World;

namespace ZombieRush.EditorTools
{
    /// Overgrowth for the abandoned city, run as part of Build Survival Layout:
    ///   - ivy climbing building walls from the ground, and vines hanging from higher ledges
    ///     (leaf cards under Map/SurvivalLayout/Overgrowth),
    ///   - moss painted into the terrain round building footprints and in patches on open ground
    ///     (a new terrain layer; the other layers fade under it),
    ///   - wild grass grown on the mossy ground with the terrain's own grass (detail) system.
    /// Numbers come from SurvivalLayoutConfig (Overgrowth headers). The terrain is saved in
    /// Assets/Data/MainLevel_Terrain.asset; the backup from before the map work is in Backups/.
    static class OvergrowthTools
    {
        const string TexDir = "Assets/Models/2026-11-09_CityProps/Textures";
        const string MossLayerPath = "Assets/Data/ZR_MossLayer.terrainlayer";
        const string Group = "Overgrowth";

        static string BaseName(string n) { int k = n.IndexOf(" ("); return k > 0 ? n.Substring(0, k) : n; }
        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        static List<Transform> Buildings(Transform mapT) =>
            mapT.Cast<Transform>().Where(t => { var k = BaseName(t.name); return k.StartsWith("building") && !k.Contains("rubble"); }).ToList();

        static Bounds WorldBounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(false).Where(r => !(r is ParticleSystemRenderer)).ToArray();
            if (rs.Length == 0) return new Bounds(t.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        // ------------------------------------------------------------ ivy on walls

        public static string Ivy(CityPropsTools.Placer p, Transform mapT, SafeBase safe, SurvivalLayoutConfig cfg, System.Random rnd, float area)
        {
            var card = CityPropsTools.Prefab("ZR_IvyCard");
            if (card == null) return "ivy: no ZR_IvyCard prefab; ";
            var fwd = CityPropsTools.BlenderForward();
            bool fwdIsX = Mathf.Abs(fwd.x) > Mathf.Abs(fwd.z);
            float R(Vector2 r) => Mathf.Lerp(r.x, r.y, (float)rnd.NextDouble());
            int climbing = 0, hanging = 0, buildings = 0;
            foreach (var b in Buildings(mapT))
            {
                if (safe.building != null && (b == safe.building || safe.building.IsChildOf(b))) continue;
                var bb = WorldBounds(b);
                if (Flat(bb.center - p.baseC) > area) continue;
                var cols = new HashSet<Collider>(b.GetComponentsInChildren<Collider>(true));
                if (cols.Count == 0) continue;
                float perimeter = 2f * (bb.size.x + bb.size.z);
                int want = Mathf.Min(cfg.ivyMaxPerBuilding, Mathf.CeilToInt(perimeter / cfg.ivyEveryMetres));
                int got = 0;
                for (int tries = 0; tries < want * 6 && got < want; tries++)
                {
                    // a point just outside the footprint, aimed at the middle of the building
                    float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    float reach = Mathf.Max(bb.extents.x, bb.extents.z) + 4f;
                    var from = new Vector3(bb.center.x, 0f, bb.center.z) + dir * reach;
                    if (!p.Ground(from, out var g)) continue;
                    bool hang = rnd.NextDouble() < cfg.hangingVineShare;
                    float h0 = hang ? R(cfg.hangingVineHeight) : Mathf.Lerp(0.6f, 2.5f, (float)rnd.NextDouble());
                    if (h0 > bb.max.y - 1f) continue;
                    var start = new Vector3(from.x, g.y + h0, from.z);
                    var hits = Physics.RaycastAll(start, -dir, reach + 2f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance);
                    var hit = hits.FirstOrDefault(h => !(h.collider is TerrainCollider));
                    if (hit.collider == null || !cols.Contains(hit.collider)) continue;
                    if (Mathf.Abs(hit.normal.y) > 0.25f) continue; // upright wall only, never a slope of rubble
                    var n = hit.normal; n.y = 0f;
                    n.Normalize();
                    float w = R(cfg.ivyWidth), h = hang ? Mathf.Min(R(cfg.ivyHeight) * 1.2f, h0 - 0.5f) : R(cfg.ivyHeight);
                    if (h < 1.5f) continue;
                    // the wall must be there behind the whole card: probe its corners and middle
                    var along = Vector3.Cross(Vector3.up, n);
                    float bottom = hang ? hit.point.y - h : g.y + 0.2f, top = hang ? hit.point.y : g.y + h;
                    bool solid = true;
                    foreach (var probe in new[] { (-0.45f, 0.1f), (0.45f, 0.1f), (-0.45f, 0.9f), (0.45f, 0.9f), (0f, 0.5f) })
                    {
                        var q = hit.point + along * (probe.Item1 * w);
                        q.y = Mathf.Lerp(bottom, top, probe.Item2);
                        if (!Physics.Raycast(q + n * 1.0f, -n, out var ph, 1.6f, ~0, QueryTriggerInteraction.Ignore) || !cols.Contains(ph.collider)
                            || Mathf.Abs(ph.normal.y) > 0.3f) { solid = false; break; }
                    }
                    if (!solid) continue;
                    // climbing ivy grows up from the ground; hanging vines drop down from the ledge
                    float baseY = hang ? hit.point.y - h : g.y - 0.15f;
                    var at = new Vector3(hit.point.x, baseY, hit.point.z) + n * 0.06f;
                    var rot = Quaternion.Euler(0f, Quaternion.FromToRotation(fwd, n).eulerAngles.y, 0f);
                    var go = p.Put(card, at, rot, Group, 0f);
                    go.transform.localScale = fwdIsX ? new Vector3(1f, h, w) : new Vector3(w, h, 1f);
                    got++;
                    if (hang) hanging++; else climbing++;
                }
                if (got > 0) buildings++;
            }
            return $"ivy on {buildings} buildings ({climbing} climbing, {hanging} hanging vines); ";
        }

        // ------------------------------------------------------------ moss and grass on the terrain

        static TerrainLayer MossLayer(SurvivalLayoutConfig cfg)
        {
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(MossLayerPath);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, MossLayerPath);
            }
            layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/ZR_MossGround.png");
            layer.tileSize = new Vector2(cfg.mossTileSize, cfg.mossTileSize);
            layer.smoothness = 0.05f;
            layer.metallic = 0f;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        public static string Ground(Transform mapT, Vector3 centre, float area, SurvivalLayoutConfig cfg)
        {
            var terrain = mapT.GetComponentInChildren<Terrain>();
            if (terrain == null) return "moss: no terrain; ";
            var td = terrain.terrainData;
            Undo.RegisterCompleteObjectUndo(td, "Overgrowth");
            var moss = MossLayer(cfg);

            // the moss layer: added once, found again on later runs
            var layers = td.terrainLayers.ToList();
            int mi = layers.IndexOf(moss);
            if (mi < 0)
            {
                layers.Add(moss);
                td.terrainLayers = layers.ToArray();
                mi = layers.Count - 1;
            }

            int res = td.alphamapResolution;
            var maps = td.GetAlphamaps(0, 0, res, res);
            int nl = maps.GetLength(2);
            float cell = td.size.x / res;
            var origin = terrain.transform.position;

            // building footprints as a mask, then distance-based falloff
            var mask = new float[res, res];
            foreach (var b in Buildings(mapT))
            {
                var bb = WorldBounds(b);
                if (Flat(bb.center - centre) > area + 30f) continue;
                int x0 = Mathf.Clamp(Mathf.FloorToInt((bb.min.x - cfg.mossReach - origin.x) / cell), 0, res - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt((bb.max.x + cfg.mossReach - origin.x) / cell), 0, res - 1);
                int z0 = Mathf.Clamp(Mathf.FloorToInt((bb.min.z - cfg.mossReach - origin.z) / cell), 0, res - 1);
                int z1 = Mathf.Clamp(Mathf.CeilToInt((bb.max.z + cfg.mossReach - origin.z) / cell), 0, res - 1);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float wx = origin.x + (x + 0.5f) * cell, wz = origin.z + (z + 0.5f) * cell;
                        float dx = Mathf.Max(0f, Mathf.Max(bb.min.x - wx, wx - bb.max.x));
                        float dz = Mathf.Max(0f, Mathf.Max(bb.min.z - wz, wz - bb.max.z));
                        float d = Mathf.Sqrt(dx * dx + dz * dz);
                        float v = 1f - Mathf.Clamp01(d / cfg.mossReach);
                        if (v > mask[z, x]) mask[z, x] = v;
                    }
            }

            int painted = 0;
            float seedX = 37.1f, seedZ = 91.7f;
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float wx = origin.x + (x + 0.5f) * cell, wz = origin.z + (z + 0.5f) * cell;
                    float d = Flat(new Vector3(wx - centre.x, 0f, wz - centre.z));
                    if (d > area + 30f) continue;
                    float edge = 1f - Mathf.Clamp01((d - area) / 30f); // fade out past the play area
                    float n = Mathf.PerlinNoise(seedX + wx / cfg.mossPatchScale, seedZ + wz / cfg.mossPatchScale);
                    float fine = Mathf.PerlinNoise(seedZ + wx / 6f, seedX + wz / 6f);
                    float patch = Mathf.Clamp01((n - (1f - cfg.mossPatchCover)) / 0.15f);
                    float m = Mathf.Max(mask[z, x] * (0.6f + 0.4f * fine), patch * (0.5f + 0.5f * fine)) * cfg.mossStrength * edge;
                    if (m <= 0.01f) continue;
                    float rest = 0f;
                    for (int l = 0; l < nl; l++) if (l != mi) rest += maps[z, x, l];
                    float keep = 1f - m;
                    for (int l = 0; l < nl; l++)
                        maps[z, x, l] = l == mi ? Mathf.Max(maps[z, x, l], m) : (rest > 0f ? maps[z, x, l] / rest * keep : 0f);
                    // normalise
                    float sum = 0f; for (int l = 0; l < nl; l++) sum += maps[z, x, l];
                    if (sum > 0f) for (int l = 0; l < nl; l++) maps[z, x, l] /= sum;
                    painted++;
                }
            td.SetAlphamaps(0, 0, maps);

            // wild grass: terrain detail on mossy ground (removed when switched off)
            var grassTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/ZR_GrassCard.png");
            var protos = td.detailPrototypes.ToList();
            int gi = protos.FindIndex(dp => dp.prototypeTexture == grassTex);
            if (!cfg.terrainGrass)
            {
                if (gi >= 0)
                {
                    protos.RemoveAt(gi);
                    td.detailPrototypes = protos.ToArray();
                }
                terrain.heightmapPixelError = cfg.terrainPixelError; // small props stay on the drawn ground at a distance
                EditorUtility.SetDirty(terrain);
                EditorUtility.SetDirty(td);
                terrain.Flush();
                return $"moss painted on {painted} terrain cells ({cell:0.0} m each), terrain grass off; ";
            }
            if (gi < 0) { protos.Add(new DetailPrototype()); gi = protos.Count - 1; }
            // tufts, not sheets: narrow and short, sizes vary with noise
            protos[gi].prototypeTexture = grassTex;
            protos[gi].usePrototypeMesh = false;
            protos[gi].renderMode = DetailRenderMode.Grass;
            protos[gi].healthyColor = new Color(0.62f, 0.68f, 0.45f);
            protos[gi].dryColor = new Color(0.66f, 0.58f, 0.40f);
            protos[gi].minWidth = 0.35f; protos[gi].maxWidth = 0.75f;
            protos[gi].minHeight = 0.3f; protos[gi].maxHeight = 0.7f;
            protos[gi].noiseSpread = 0.5f;
            td.detailPrototypes = protos.ToArray();
            int dres = td.detailWidth;
            if (dres <= 0)
            {
                td.SetDetailResolution(1024, 32);
                dres = td.detailWidth;
            }
            var layer = new int[dres, dres];
            long blades = 0;
            int ares = res;
            for (int z = 0; z < dres; z++)
                for (int x = 0; x < dres; x++)
                {
                    int ax = Mathf.Min(ares - 1, x * ares / dres), az = Mathf.Min(ares - 1, z * ares / dres);
                    float m = maps[az, ax, mi];
                    if (m < 0.35f) continue;
                    float wx = origin.x + (x + 0.5f) * td.size.x / dres, wz = origin.z + (z + 0.5f) * td.size.z / dres;
                    float n = Mathf.PerlinNoise(wx / 3.1f, wz / 3.1f);
                    int dens = Mathf.RoundToInt(cfg.grassDensity * Mathf.Clamp01((m - 0.35f) / 0.5f) * n);
                    layer[z, x] = dens;
                    blades += dens;
                }
            td.SetDetailLayer(0, 0, gi, layer);
            terrain.detailObjectDistance = cfg.grassDrawDistance;
            terrain.heightmapPixelError = cfg.terrainPixelError;
            terrain.drawTreesAndFoliage = true;
            EditorUtility.SetDirty(td);
            EditorUtility.SetDirty(terrain);
            terrain.Flush();
            return $"moss painted on {painted} terrain cells ({cell:0.0} m each), {blades} grass blades on mossy ground; ";
        }
    }
}
