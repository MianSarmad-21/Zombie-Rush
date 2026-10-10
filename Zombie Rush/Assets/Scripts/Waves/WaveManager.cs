using System.Collections;
using UnityEngine;
using ZombieRush.Core;
using ZombieRush.World;
using ZombieRush.Zombie;

namespace ZombieRush.Waves
{
    [System.Serializable]
    public class ZombieSpawnEntry
    {
        public GameObject prefab;
        public int minWave = 1;
        [Min(0f)] public float weight = 1f;
    }

    /// Drives the wave loop: announce -> spawn zombies over time from underground
    /// spawn points -> wait for them all to die -> breather -> next wave.
    public class WaveManager : MonoBehaviour
    {
        public static WaveManager Instance { get; private set; }

        [Header("Spawning")]
        [Tooltip("Zombie types; each is eligible from its minWave and picked by weight.")]
        public ZombieSpawnEntry[] zombieTypes;
        public Transform[] spawnPoints;
        public int baseZombiesPerWave = 5;
        public int extraZombiesPerWave = 2;
        public float timeBetweenSpawns = 0.8f;

        [Header("Spawn placement")]
        [Tooltip("Before each wave, move the spawn pads around the edge of the current safe zone, so zombies always surface at the boundary of whatever area the player controls.")]
        public bool spawnAtZoneEdge = true;
        [Range(0.4f, 1f)] public float edgeRadiusFactor = 0.85f;
        [Tooltip("Pad height above the ground surface.")]
        public float padHeightOffset = 0.05f;

        [Header("Pacing")]
        public float announceDuration = 3f;
        public float breatherDuration = 5f;

        [Header("Difficulty")]
        public float difficultyPerWave = 0.15f; // +15% zombie health/damage per wave beyond the first

        [Header("Performance")]
        [Tooltip("Zombies of each type created up-front (spread over frames) so wave 1 doesn't hitch.")]
        public int prewarmPerType = 6;

        public int CurrentWave { get; private set; }
        /// Zombies currently spawned and not yet dead.
        public int AliveCount { get; private set; }
        /// Zombies of this wave still to be killed (alive + not yet spawned).
        public int RemainingInWave { get; private set; }

        ZombiePool pool;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            pool = new ZombiePool(HandleZombieDied);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start() => StartCoroutine(RunWaves());

        IEnumerator RunWaves()
        {
            // Wait one frame first so every UI script has subscribed before
            // Wave 1's announcement fires.
            yield return null;

            if (!HasValidSetup()) yield break;
            yield return Prewarm();

            while (true)
            {
                CurrentWave++;
                var zone = ZoneManager.Instance;
                if (spawnAtZoneEdge && zone != null) ArrangeSpawnPoints(zone.center, zone.CurrentRadius * edgeRadiusFactor);
                GameEvents.RaiseWaveAnnounced(CurrentWave);
                yield return new WaitForSeconds(announceDuration);

                int count = baseZombiesPerWave + (CurrentWave - 1) * extraZombiesPerWave;
                RemainingInWave = count;
                GameEvents.RaiseWaveStarted(CurrentWave);
                GameEvents.RaiseZombiesRemainingChanged(RemainingInWave);

                // Spawn in rounds - one zombie from EVERY spawn point per round, all
                // at once - so zombies converge on the player from every direction
                // simultaneously instead of trickling in from one side.
                int spawned = 0;
                while (spawned < count)
                {
                    int spawnedThisRound = 0;
                    foreach (var point in spawnPoints)
                    {
                        if (spawned >= count) break;
                        if (point == null || !SpawnZombieAt(point)) continue;
                        spawned++;
                        spawnedThisRound++;
                    }

                    if (spawnedThisRound == 0)
                    {
                        // Nothing could spawn (all points/prefabs broken). Don't wait
                        // forever on zombies that will never exist.
                        Debug.LogError("WaveManager: no zombie could be spawned this round; skipping the rest of the wave.", this);
                        RemainingInWave -= count - spawned;
                        GameEvents.RaiseZombiesRemainingChanged(RemainingInWave);
                        break;
                    }

                    if (spawned < count) yield return new WaitForSeconds(timeBetweenSpawns);
                }

                // Only zombies that actually spawned are counted, so this can't hang.
                while (AliveCount > 0) yield return null;

                GameEvents.RaiseWaveCompleted(CurrentWave, breatherDuration);
                yield return new WaitForSeconds(breatherDuration);
            }
        }

        bool HasValidSetup()
        {
            bool hasPoint = false;
            if (spawnPoints != null)
                foreach (var p in spawnPoints) hasPoint |= p != null;

            bool hasPrefab = false;
            if (zombieTypes != null)
                foreach (var t in zombieTypes) hasPrefab |= t != null && t.prefab != null;

            if (!hasPoint) Debug.LogError("WaveManager: no spawn points assigned - waves disabled.", this);
            if (!hasPrefab) Debug.LogError("WaveManager: no zombie prefabs assigned - waves disabled.", this);
            return hasPoint && hasPrefab;
        }

        IEnumerator Prewarm()
        {
            if (prewarmPerType <= 0) yield break;
            foreach (var t in zombieTypes)
            {
                if (t == null || t.prefab == null) continue;
                while (pool.FreeCount(t.prefab) < prewarmPerType)
                {
                    pool.Prewarm(t.prefab, pool.FreeCount(t.prefab) + 1);
                    yield return null; // one per frame keeps startup smooth
                }
            }
        }

        bool SpawnZombieAt(Transform point)
        {
            var prefab = PickZombiePrefab();
            if (prefab == null) return false;

            var zombie = pool.Get(prefab, point.position, Quaternion.identity);
            if (zombie == null) return false;

            float multiplier = 1f + (CurrentWave - 1) * difficultyPerWave;
            zombie.ResetForSpawn(multiplier);

            var ai = zombie.GetComponent<ZombieAI>();
            if (ai != null)
            {
                ai.difficultyMultiplier = multiplier;
                ai.alwaysHunt = true; // wave zombies may surface beyond sight range; they must never idle and stall the wave
            }

            zombie.GetComponent<ZombieSpawnRise>().Begin(point.position);

            AliveCount++;
            return true;
        }

        /// Spreads the spawn pads evenly around a ring (random rotation each call),
        /// nudging each one inward until it lands on clear, open ground. Also used by
        /// the editor "Merge Zones" tool to preview pad placement.
        public void ArrangeSpawnPoints(Vector3 center, float radius)
        {
            if (spawnPoints == null || spawnPoints.Length == 0) return;
            float offset = Random.value * Mathf.PI * 2f;
            float slice = Mathf.PI * 2f / spawnPoints.Length;

            for (int i = 0; i < spawnPoints.Length; i++)
            {
                var pad = spawnPoints[i];
                if (pad == null) continue;

                for (int attempt = 0; attempt < 12; attempt++)
                {
                    float angle = offset + i * slice + (attempt % 3 - 1) * slice * 0.25f;
                    float r = Mathf.Max(3f, radius - (attempt / 3) * 3f);
                    var probe = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * r;
                    if (!ArenaGround.TryGround(probe, center.y, out var ground) || !ArenaGround.IsClear(ground, 0.6f)) continue;

                    pad.position = ground + Vector3.up * padHeightOffset;
                    break;
                }
            }
        }

        GameObject PickZombiePrefab()
        {
            if (zombieTypes == null) return null;

            float total = 0f;
            foreach (var t in zombieTypes)
                if (t != null && t.prefab != null && CurrentWave >= t.minWave) total += t.weight;
            if (total > 0f)
            {
                float roll = Random.value * total;
                foreach (var t in zombieTypes)
                {
                    if (t == null || t.prefab == null || CurrentWave < t.minWave) continue;
                    roll -= t.weight;
                    if (roll <= 0f) return t.prefab;
                }
            }

            // Nothing eligible this wave: spawn the first configured type rather than
            // spawning nothing, which would leave the wave waiting forever.
            foreach (var t in zombieTypes)
                if (t != null && t.prefab != null) return t.prefab;
            return null;
        }

        void HandleZombieDied(ZombieHealth zombie)
        {
            AliveCount = Mathf.Max(0, AliveCount - 1);
            RemainingInWave = Mathf.Max(0, RemainingInWave - 1);
            GameEvents.RaiseZombiesRemainingChanged(RemainingInWave);
        }
    }
}
