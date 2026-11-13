using UnityEngine;

namespace ZombieRush.Data
{
    /// How the city props (wrecks, barricades, lamps, barrels; made in Blender) are spread over the
    /// play area by Zombie Rush > Level > Place City Props. Distances are metres from the safe base.
    [CreateAssetMenu(fileName = "CityPropsConfig", menuName = "Zombie Rush/City Props Config")]
    public class CityPropsConfig : ScriptableObject
    {
        [Tooltip("Same seed = same layout. Change it to reshuffle.")]
        public int seed = 2611;
        [Tooltip("Props stay this far inside the largest fight zone.")]
        public float edgeMargin = 10f;
        [Tooltip("Free space kept round every prop so players and zombies can pass.")]
        public float walkGap = 2.2f;

        [Header("Wrecked vehicles")]
        public int cars = 34;
        public int policeCars = 4;
        public int buses = 3;
        [Tooltip("Share of cars parked along the street; the rest sit at crash angles.")]
        [Range(0f, 1f)] public float parkedShare = 0.6f;
        public float vehicleSpacing = 9f;
        [Tooltip("No vehicles closer to the safe base than this (keeps the start area open).")]
        public float vehicleMinFromBase = 40f;

        [Header("Base defences (round the safe base)")]
        [Tooltip("Sandbag walls on a ring just outside the safe base, with gaps to walk through.")]
        public int sandbagSegments = 10;
        public float sandbagRingOffset = 3f;
        [Tooltip("Concrete barriers on a second ring, in short runs.")]
        public int barrierRuns = 8;
        public float barrierRingOffset = 11f;
        public int burningBarrelsAtBase = 4;

        [Header("Street")]
        public int streetLamps = 34;
        [Range(0f, 1f)] public float lampsWorkingShare = 0.35f;
        [Range(0f, 1f)] public float workingLampsFlickerShare = 0.3f;
        public float lampSpacing = 22f;
        public int trafficLights = 8;
        [Tooltip("A lamp or dumpster stands this far from a building wall.")]
        public Vector2 wallDistance = new Vector2(2f, 5f);

        [Header("Junk")]
        public int dumpsters = 14;
        public int barrelClusters = 16;
        public int burningBarrelsInCity = 8;

        [Header("Lights")]
        public Color lampColor = new Color(1f, 0.78f, 0.5f);
        public float lampIntensity = 2.5f;
        public float lampRange = 16f;
        public float lampSpotAngle = 110f;
        public Color fireColor = new Color(1f, 0.5f, 0.15f);
        public float fireIntensity = 2.2f;
        public float fireRange = 9f;
        [Tooltip("Bonfire light = fire light times this (intensity and range).")]
        public float bonfireScale = 1.7f;
        public Color searchlightColor = new Color(0.9f, 0.95f, 1f);
        public float searchlightIntensity = 9f;
        public float searchlightRange = 55f;
        public float searchlightAngle = 26f;
        [Tooltip("Searchlight beam tilt below the horizon, degrees.")]
        public float searchlightTilt = 14f;
        public Color floodlightColor = new Color(1f, 0.92f, 0.78f);
        public float floodlightIntensity = 4f;
        public float floodlightRange = 24f;
        public float floodlightAngle = 105f;
    }
}
