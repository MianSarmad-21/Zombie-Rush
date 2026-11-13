using UnityEngine;

namespace ZombieRush.Data
{
    /// The survival layout: the fortified compound round the safe base, building variety, plazas,
    /// greenery, climbable lookouts and street cover. Used by Zombie Rush > Level > Build Survival Layout.
    /// Distances are metres; "radius" means distance from the safe base centre.
    [CreateAssetMenu(fileName = "SurvivalLayoutConfig", menuName = "Zombie Rush/Survival Layout Config")]
    public class SurvivalLayoutConfig : ScriptableObject
    {
        [Tooltip("Same seed = same layout.")]
        public int seed = 909;

        [Header("Base compound")]
        [Tooltip("Ring wall sits this far outside the safe base edge.")]
        public float wallOffset = 1.5f;
        [Tooltip("Buildings closer than this outside the ring wall are cleared (the HQ building always stays).")]
        public float clearMargin = 2f;
        public int gates = 4;
        public float gateWidth = 8f;
        public int searchlightTowers = 4;
        [Tooltip("Towers stand this far inside the ring wall.")]
        public float towerInset = 3.5f;
        public int tents = 5;
        public int generators = 2;
        public int floodlights = 4;
        public int supplyPiles = 6;

        [Header("Building variety")]
        [Tooltip("Share of the most repeated building swapped for other building types of a similar size.")]
        [Range(0f, 1f)] public float swapShare = 0.4f;
        [Tooltip("Copies of the most repeated building removed to open plazas.")]
        public int plazas = 6;
        public Vector2 plazaRadius = new Vector2(70f, 260f);
        public float plazaSpacing = 70f;

        [Header("Fires")]
        [Tooltip("Bonfires outside the plazas (each plaza also gets one).")]
        public int extraBonfires = 5;
        public float bonfireSpacing = 45f;

        [Header("Base wall")]
        [Tooltip("Ring wall pattern: this many HESCO bastions, then one tall sandbag wall.")]
        public int hescoPerSandbag = 2;
        [Tooltip("Burning barrels either side of each gate, scaled up to read as big oil drums.")]
        public float gateBarrelScale = 1.3f;

        [Header("Greenery")]
        [Tooltip("Leafy trees (small real leaves, not cards).")]
        public int trees = 30;
        public int deadTrees = 35;
        [Tooltip("Leafy green bushes; a quarter of them are dead twig shrubs.")]
        public int bushes = 170;
        [Tooltip("Clumps of wild green grass (real thin blades), grown in patches.")]
        public int grassTufts = 1400;

        [Header("Overgrowth on buildings")]
        [Tooltip("Ivy patches climbing from the ground: one per this many metres of building outline.")]
        public float ivyEveryMetres = 5f;
        public int ivyMaxPerBuilding = 18;
        public Vector2 ivyWidth = new Vector2(1.2f, 3f);
        public Vector2 ivyHeight = new Vector2(3f, 9f);
        [Range(0f, 1f)] public float hangingVineShare = 0f;
        public Vector2 hangingVineHeight = new Vector2(8f, 22f);

        [Header("Overgrowth on the ground (terrain)")]
        [Tooltip("Moss reaches this far out from building footprints.")]
        public float mossReach = 14f;
        [Range(0f, 1f)] public float mossStrength = 1f;
        [Tooltip("Moss patches on open ground: noise scale in metres and how much of the ground they cover.")]
        public float mossPatchScale = 40f;
        [Range(0f, 1f)] public float mossPatchCover = 0.6f;
        public float mossTileSize = 6f;
        [Tooltip("Terrain grass cards on mossy ground. Off: they look like flat flakes from the game camera.")]
        public bool terrainGrass = false;
        [Tooltip("Terrain draw accuracy (Unity pixel error). Lower = the drawn ground follows the real shape closer, so small props never seem to float; 1 to 2 is safe.")]
        [Range(1f, 10f)] public float terrainPixelError = 1.5f;
        [Tooltip("Wild grass grows in patches of this many clumps, mostly along walls and kerbs.")]
        public Vector2Int weedPatchSize = new Vector2Int(6, 14);
        [Tooltip("Wild grass on mossy ground (terrain detail): most per detail cell, and how far it draws.")]
        public int grassDensity = 6;
        public float grassDrawDistance = 90f;

        [Header("Climbable lookouts")]
        public int scaffoldTowers = 5;
        public int sniperNests = 5;
        [Tooltip("Pairs of nests joined by a plank bridge.")]
        public int bridgedPairs = 3;

        [Header("Street cover")]
        public int coverClusters = 40;
        public int razorFenceRuns = 12;
    }
}
