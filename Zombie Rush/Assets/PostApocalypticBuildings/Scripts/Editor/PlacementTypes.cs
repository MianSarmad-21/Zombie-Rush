using System;
using UnityEngine;

namespace AutoPlacer
{
    /// <summary>What role a prefab plays in the generated city. Guessed from its name on scan,
    /// hand-correctable in the catalog inspector. Only Building / Rubble / SmallDebris are placed;
    /// everything else (walls, doors, props, antennas...) is left as Ignored.</summary>
    public enum PieceKind
    {
        Ignored = 0,
        Building = 1,
        Rubble = 2,       // large collapsed piles - clustered around big buildings
        SmallDebris = 3,  // scattered around buildings and across open ground, but NOT on streets
        StreetDebris = 4  // scattered along the streets and avenues instead
    }

    /// <summary>Coarse size bucket, derived from the measured height. Drives which zone a building
    /// belongs to (skyscrapers downtown, small houses on the outskirts) and whether it may tilt.</summary>
    public enum SizeClass
    {
        Small = 0,
        Medium = 1,
        Large = 2,
        Skyscraper = 3
    }

    /// <summary>District a block belongs to. Assigned by distance from the downtown centre so the
    /// skyline rises toward the middle, the way the reference screenshot does.</summary>
    public enum Zone
    {
        Residential = 0,
        Midtown = 1,
        Downtown = 2
    }

    /// <summary>
    /// One prefab plus the measurements the placer needs. Populated by the scanner
    /// (<see cref="PlacementCatalogEditor"/>), tweakable by hand in the catalog inspector.
    /// Sizes are measured from the meshes and never typed in.
    /// </summary>
    [Serializable]
    public class PlacementPiece
    {
        public string id;
        public string displayName;
        public GameObject prefab;

        [Tooltip("How this prefab is used. Buildings and rubble are laid out in blocks; small debris is scattered. Ignored pieces are never placed.")]
        public PieceKind kind = PieceKind.Ignored;

        [Tooltip("Size bucket derived from height. Buildings use it to pick a district; you can override it.")]
        public SizeClass sizeClass = SizeClass.Medium;

        [Tooltip("Let the generator lean this piece a few degrees. Turned on for big buildings, off for small ones.")]
        public bool allowTilt;

        [Tooltip("Relative chance of being chosen. 2 = twice as likely as a piece weighted 1.")]
        public float weight = 1f;

        [Tooltip("Uncheck to keep the prefab in the catalog but exclude it from generation.")]
        public bool enabled = true;

        // --- measured from the meshes, never entered by hand -------------------
        // Serialized so the measurement survives reloads.
        [HideInInspector] public Vector3 size;         // bounds size in root-local space, root scale folded in
        [HideInInspector] public Vector3 localCenter;  // bounds centre relative to the prefab pivot
        [HideInInspector] public bool measured;
        [HideInInspector] public string family;        // e.g. "building6" for building6 + its variants

        public bool IsValid => prefab != null && measured && size.sqrMagnitude > 1e-4f;
        public bool IsPlaceable => IsValid && enabled && kind != PieceKind.Ignored;

        public float Height => size.y;
        public float FootprintMin => Mathf.Min(size.x, size.z);
        public float FootprintMax => Mathf.Max(size.x, size.z);

        /// <summary>Measures the prefab's meshes if it hasn't been measured yet.</summary>
        public bool EnsureMeasured(bool force = false)
        {
            if (measured && !force && size.sqrMagnitude > 1e-4f) return true;
            if (prefab == null) return false;

            if (!PlacementMetrics.TryMeasure(prefab, out Bounds b)) return false;

            size = b.size;
            localCenter = b.center;
            measured = true;
            if (string.IsNullOrEmpty(displayName)) displayName = prefab.name;
            return true;
        }
    }

    /// <summary>
    /// Works out a prefab's dimensions from its meshes, in the prefab root's local space with the
    /// root's own scale folded in - so a measurement equals the size the prefab actually spawns at.
    /// Same approach the Modular Wall Builder uses; editor-time mesh vertices are always readable.
    /// </summary>
    public static class PlacementMetrics
    {
        public static bool TryMeasure(GameObject prefab, out Bounds localBounds)
        {
            localBounds = new Bounds();
            if (prefab == null) return false;

            Transform root = prefab.transform;
            Matrix4x4 rootScale = Matrix4x4.Scale(root.localScale);
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);

            bool has = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Mesh mesh = MeshOf(renderers[i]);
                if (mesh == null) continue;

                Matrix4x4 toRoot = rootScale * root.worldToLocalMatrix * renderers[i].transform.localToWorldMatrix;
                Bounds mb = mesh.bounds;

                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3(
                        (c & 1) == 0 ? mb.min.x : mb.max.x,
                        (c & 2) == 0 ? mb.min.y : mb.max.y,
                        (c & 4) == 0 ? mb.min.z : mb.max.z);
                    Vector3 p = toRoot.MultiplyPoint3x4(corner);

                    if (!has) { localBounds = new Bounds(p, Vector3.zero); has = true; }
                    else localBounds.Encapsulate(p);
                }
            }

            // Fall back to colliders for prefabs whose visuals are procedural or disabled by default.
            if (!has)
            {
                Collider[] colliders = prefab.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < colliders.Length; i++)
                {
                    Bounds cb = colliders[i].bounds;
                    var b = new Bounds(root.InverseTransformPoint(cb.center), cb.size);
                    if (!has) { localBounds = b; has = true; }
                    else localBounds.Encapsulate(b);
                }
            }

            return has && localBounds.size.sqrMagnitude > 1e-6f;
        }

        static Mesh MeshOf(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
            return renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
        }
    }

    /// <summary>
    /// All the knobs the generator reads. Stored on the catalog so a preset travels with the asset,
    /// and edited straight from the Destroyed City Generator window. Every field is auto-drawn from these attributes.
    /// Distances are in the same units your prefabs are modelled in (metres, if the reference is metres).
    /// </summary>
    [Serializable]
    public class PlacementSettings
    {
        [Header("General")]
        [Tooltip("Same seed + same catalog = same city. Change it to reroll.")]
        public int seed = 12345;
        [Tooltip("Name of the root GameObject the city is generated under. Clear removes an object with this name.")]
        public string rootObjectName = "Placed City";
        [Tooltip("Width (X) and depth (Z) of the whole city area.")]
        public Vector2 citySize = new Vector2(260f, 260f);

        [Header("City shape")]
        [Tooltip("ON = the city fills an organic, irregular outline (rounded, lumpy edges) instead of the full square/rectangle. Blocks whose centre falls outside the outline are dropped.")]
        public bool organicShape = false;
        [Tooltip("How far the organic outline bulges in and out. 0 = a clean rounded ellipse; higher = lumpier, more ragged coastline.")]
        [Range(0f, 0.6f)] public float organicEdge = 0.28f;
        [Tooltip("How many bumps run around the organic outline. Low = a few big lobes; high = many small inlets.")]
        [Range(1f, 8f)] public float organicLobes = 2.5f;

        [Header("Streets & blocks")]
        [Tooltip("Min / max size of a city block between streets. Set the max above your tallest building's footprint so towers fit.")]
        public Vector2 blockSize = new Vector2(30f, 55f);
        [Tooltip("Width of ordinary side streets.")]
        [Min(0f)] public float sideStreetWidth = 7f;
        [Tooltip("Width of the wider main avenues.")]
        [Min(0f)] public float mainStreetWidth = 14f;
        [Tooltip("Every Nth street is a wide main avenue.")]
        [Min(1)] public int mainStreetEvery = 4;
        [Tooltip("Empty margin kept inside each block edge so buildings don't spill into the street.")]
        [Min(0f)] public float blockSetback = 1.5f;
        [Tooltip("Gap left between neighbouring buildings inside a block.")]
        [Min(0f)] public float buildingGap = 1.5f;

        [Header("Organic streets")]
        [Tooltip("Chance a block swallows the street beside it, breaking the perfect grid into irregular blocks, T-junctions and dead-end streets. 0 = clean grid.")]
        [Range(0f, 1f)] public float blockMergeChance = 0.3f;
        [Tooltip("Number of long organic avenues (curved / diagonal roads) cut across the city on top of the block grid.")]
        [Min(0)] public int avenueCount = 3;
        [Tooltip("Width of those avenues, in world units.")]
        [Min(0f)] public float avenueWidth = 12f;
        [Tooltip("How sharply the avenues curve, in degrees per step. 0 = straight diagonals; higher = winding.")]
        [Range(0f, 45f)] public float avenueCurve = 14f;
        [Tooltip("Chance per step that an avenue just stops mid-city, leaving a dead end. Keep small.")]
        [Range(0f, 0.3f)] public float avenueEndChance = 0.02f;

        [Header("Districts")]
        [Tooltip("Where the tall downtown core sits, as a fraction of the city (0.5,0.5 = middle).")]
        public Vector2 downtownCenter01 = new Vector2(0.5f, 0.5f);
        [Tooltip("Roughly what share of blocks become downtown (big buildings). At least one block is always downtown, whatever the city size.")]
        [Range(0.05f, 0.9f)] public float downtownRadius01 = 0.3f;
        [Tooltip("Share of blocks that become midtown (medium/large). The rest are outskirts.")]
        [Range(0.1f, 1f)] public float midtownRadius01 = 0.62f;
        [Tooltip("Scale of the noise that makes district borders ragged instead of perfect rings.")]
        [Range(0f, 0.2f)] public float zoneNoiseScale = 0.03f;
        [Tooltip("How strongly that noise pushes districts around.")]
        [Range(0f, 1f)] public float zoneNoiseAmount = 0.35f;
        [Tooltip("How densely downtown blocks are filled with big buildings (1 = tower on every plot).")]
        [Range(0.3f, 1f)] public float downtownDensity = 0.9f;
        [Tooltip("Chance a midtown/outskirts block instead holds a single tall building - a lone tower surviving among the low-rise. 0 = never.")]
        [Range(0f, 0.5f)] public float outskirtsTallChance = 0.08f;

        [Header("Buildings")]
        [Tooltip("BUILDING DENSITY - fraction of each ordinary block that gets filled. Lower leaves courtyards and gaps.")]
        [Range(0.05f, 1f)] public float density = 0.85f;
        [Tooltip("Give every building and debris piece a random rotation in 90-degree steps. Off = free any-angle yaw.")]
        public bool alignToGrid = true;
        [Tooltip("Max lean, in degrees, for buildings whose 'Allow Tilt' is on.")]
        [Range(0f, 20f)] public float tiltMaxDegrees = 6f;
        [Tooltip("Random scale-UP of buildings, from 0 to this (max +10%). Buildings are only ever made bigger than authored, never smaller.")]
        [Range(0f, 0.1f)] public float buildingScaleJitter = 0.08f;
        [Tooltip("Extra downward sink per degree of tilt, to keep tilted pieces planted. 0.25 = 1 unit lower for every 4 degrees of lean.")]
        [Min(0f)] public float tiltSinkPerDegree = 0.25f;
        [Tooltip("Sink every piece's pivot this far below the ground. The pivot is the contact point, so a tiny value is enough; raise it only if pieces hover due to their pivot sitting slightly above their base.")]
        [Min(0f)] public float groundEmbed = 0.05f;

        [Header("Clustering (group same-type buildings)")]
        [Tooltip("Bias each block toward one building 'family' (a main model + its -variant/-variation prefabs), so the city forms pockets of similar buildings.")]
        public bool clusterBuildings = true;
        [Tooltip("Size of a same-family pocket, in world units. Bigger = larger neighbourhoods of one type.")]
        [Min(1f)] public float clusterZoneSize = 45f;
        [Tooltip("How strongly a block sticks to its family. 1 = only that family; lower mixes in other types (0.65 = mostly one dominant type with variety).")]
        [Range(0f, 1f)] public float clusterStrength = 0.65f;

        [Header("Debris")]
        [Tooltip("Big rubble (Rubble kind) is placed ONLY around big buildings. This is how many piles per big building.")]
        [Min(0f)] public float rubblePerBigBuilding = 4f;
        [Tooltip("How far out from a big building's footprint the rubble can land, in world units.")]
        [Min(0f)] public float rubbleSpread = 6f;
        [Tooltip("Small debris scattered around EVERY building. Fixed count per building, so small buildings get denser debris than big ones.")]
        [Min(0f)] public float smallDebrisPerBuilding = 8f;
        [Tooltip("How far small debris spreads from a building, as a multiple of its footprint radius.")]
        [Min(0f)] public float smallDebrisSpread = 1.3f;
        [Tooltip("Small debris scattered evenly across the open ground per 100 square units - fills courtyards and empty blocks. Kept OFF the streets (use Street Debris for those).")]
        [Min(0f)] public float smallDebrisPerArea = 1.5f;
        [Tooltip("Street debris (the Street Debris kind) scattered along the streets and avenues, per 100 square units.")]
        [Min(0f)] public float streetDebrisPerArea = 2f;

        [Header("Debris scale range")]
        [Tooltip("Smallest random scale for small debris (1 = authored size).")]
        [Range(0.1f, 3f)] public float debrisScaleMin = 0.85f;
        [Tooltip("Largest random scale for small debris.")]
        [Range(0.1f, 3f)] public float debrisScaleMax = 1.05f;
        [Tooltip("Smallest random scale for rubble.")]
        [Range(0.1f, 3f)] public float rubbleScaleMin = 0.8f;
        [Tooltip("Largest random scale for rubble.")]
        [Range(0.1f, 3f)] public float rubbleScaleMax = 1.2f;

        [Header("Ground")]
        [Tooltip("Flat height everything is placed at when 'Conform To Ground' is off.")]
        public float groundY = 0f;
        [Tooltip("Raycast down onto existing colliders (terrain / meshes) to follow the ground height.")]
        public bool conformToGround = false;
        [Tooltip("Layers the ground raycast is allowed to hit.")]
        public LayerMask groundMask = ~0;

        [Header("Background buildings (surround the city)")]
        [Tooltip("Ring the city with distant background buildings (skyscrapers + large rubble) so it feels like it continues past the generated area.")]
        public bool backgroundEnabled = false;
        [Tooltip("How far out from the city edge the background band extends, in world units.")]
        [Min(0f)] public float backgroundDepth = 60f;
        [Tooltip("How densely the background band is filled - fraction of plots that get a piece.")]
        [Range(0f, 1f)] public float backgroundDensity = 0.7f;
        [Tooltip("Empty gap between the city edge and where the background starts, in world units.")]
        [Min(0f)] public float backgroundGap = 6f;
        [Tooltip("Share of the background that is large rubble rather than standing skyscrapers.")]
        [Range(0f, 1f)] public float backgroundRubbleShare = 0.35f;

        [Header("Terrain streets")]
        [Tooltip("Paint the street gaps onto a terrain's splatmap using the layer below. Uses the Terrain assigned in the Destroyed City Generator window, or the active terrain if none.")]
        public bool paintTerrainStreets = false;
        [Tooltip("Which terrain texture layer represents streets. Must be one of the layers already on the terrain.")]
        public TerrainLayer streetLayer;
        [Tooltip("Only if the painted streets come out rotated 90 degrees from the buildings: tick this to swap the terrain's X/Z splatmap axes.")]
        public bool terrainStreetsSwapXZ = false;
        [Tooltip("How far the painted street edges wander off the grid, in world units. 0 = perfectly straight grid streets.")]
        [Min(0f)] public float streetEdgeWobble = 5f;
        [Tooltip("Frequency of the street noise (both the edge wobble and the strength patches). Higher = busier, smaller variation.")]
        [Range(0.005f, 0.3f)] public float streetNoiseScale = 0.08f;
        [Tooltip("Lowest street-layer strength - where the ground/sand shows through the road. 1 = solid streets everywhere, lower = more worn/broken.")]
        [Range(0f, 1f)] public float streetStrengthMin = 0.45f;

        [Header("Output")]
        [Tooltip("Mark generated objects static (batching / lightmaps / occlusion). Leave OFF for big cities: turning it on makes Unity build static-batch meshes on the first scene click, which freezes the editor for a while. Enable it only once, when finalising a scene you're keeping.")]
        public bool markStatic = false;
        [Tooltip("How many objects to place per editor frame. The city is built in batches so the editor stays responsive and shows a cancelable progress bar. Lower = smoother; higher = faster overall.")]
        [Min(1)] public int objectsPerFrame = 300;
    }
}
