using UnityEngine;

namespace ZombieRush.Data
{
    /// Tuning for the Fortress Haven base (FortressBaseTools, built from Blender/fortress_base.py).
    /// Re-run the tool (marker Temp/ZR_RunFortress) after changing these; it rebuilds the prefab and re-places it.
    [CreateAssetMenu(fileName = "FortressBaseConfig", menuName = "Zombie Rush/Fortress Base Config")]
    public class FortressBaseConfig : ScriptableObject
    {
        [Header("Placement")]
        [Tooltip("Extra turn (degrees) on top of the old hall's rotation. The room doors and the fortress gates face the hall's long axis.")]
        public float yawOffset = 0f;
        [Tooltip("The room's centre sits on the safe base centre (true), or the fortress's middle does (false).")]
        public bool centreOnRoom = true;
        [Tooltip("Metres to raise (+) or sink (-) the fortress against the old hall floor.")]
        public float floorOffset = 0f;

        [Header("Ground")]
        [Tooltip("Flatten the terrain under the fortress to just below its yard floor.")]
        public bool flattenTerrain = true;
        [Tooltip("Metres the flat ground reaches past the fortress footprint.")]
        public float flatMargin = 1.5f;
        [Tooltip("Metres over which the flat ground blends back into the terrain.")]
        public float blend = 6f;
        [Tooltip("The ground sits this far under the yard floor (stops it poking through).")]
        public float groundBelowFloor = 0.08f;
        [Tooltip("Hide small props (crates, barrels, lamps) that would stand inside the fortress.")]
        public bool hideOverlappingProps = true;
        [Tooltip("Metres to the side of the back door for the medkit pickup (inside the room, 2.2 m from the end wall).")]
        public float medkitSideOffset = 4.5f;

        [Header("Room lamps (hanging, inside)")]
        public Color lampColor = new Color(1f, 0.8f, 0.56f);
        public float lampIntensity = 5f;
        public float lampRange = 11f;
        [Tooltip("Lamps that cast shadows (0 = none). Each shadow light costs a shadow map on phones.")]
        public int lampShadowCasters = 1;
        [Tooltip("HDR glow of the lamp shades' bulbs.")]
        public float lampGlow = 6f;

        [Header("Outside lights")]
        public Color gateOutsideColor = new Color(1f, 0.78f, 0.5f);
        public float gateOutsideIntensity = 5f;
        public float gateOutsideRange = 16f;
        public Color gateInsideColor = new Color(1f, 0.82f, 0.6f);
        public float gateInsideIntensity = 3f;
        public float gateInsideRange = 5.5f;
        [Tooltip("High corner flood lights on the deck, pointing out over the yard and compound.")]
        public Color floodColor = new Color(0.86f, 0.92f, 1f);
        public float floodIntensity = 6f;
        public float floodRange = 26f;
        [Range(10f, 170f)] public float floodAngle = 95f;
        [Range(10f, 170f)] public float gateAngle = 110f;
        [Tooltip("How steeply the yard-side gate lights point down (they have no shadows, so a flat aim lights the room through its wall).")]
        public float gateInsideDrop = 2.2f;
        [Tooltip("Put a floodlight lamp model at each corner flood light.")]
        public bool floodFixtures = true;

        [Header("Room furniture")]
        [Tooltip("Swap the steel benches for stacked steel crates (guns and ammo stay on top).")]
        public bool crateStacks = true;
        [Tooltip("Swap the lantern posts for caged lamps on the wall behind them.")]
        public bool wallLamps = true;
        [Tooltip("Height of the wall lamps above the room floor.")]
        public float wallLampHeight = 2.5f;
        public Color wallLampColor = new Color(1f, 0.74f, 0.45f);
        public float wallLampIntensity = 5f;
        public float wallLampRange = 9f;
        [Tooltip("HDR glow of the wall lamps' glass.")]
        public float wallLampGlow = 2.5f;
        [Tooltip("Size of the wall lamp model (1 = 26 cm deep).")]
        public float wallLampScale = 1.5f;

        [Header("Materials")]
        [Range(0f, 2f)] public float fortressNormalScale = 0.8f;
        [Range(0f, 1f)] public float fortressOcclusion = 0.85f;
        [Range(0f, 2f)] public float roomNormalScale = 1f;
        [Tooltip("Largest texture size on Android (PC keeps the full 4096).")]
        public int androidMaxSize = 2048;
    }
}
