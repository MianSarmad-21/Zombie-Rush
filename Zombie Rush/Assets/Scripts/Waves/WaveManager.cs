using System.Collections;
using UnityEngine;
using ZombieRush.Core;
using ZombieRush.Data;
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
        [Tooltip("Surface each zombie at runtime close to a player (or just outside the safe base while they are inside it), instead of only at the zone edge. Falls back to the edge pads when no good spot is found.")]
        public bool spawnNearPlayer = true;
        [Min(7f)] public float nearPlayerMinDistance = 8f;
        [Min(8f)] public float nearPlayerMaxDistance = 15f;
        [Tooltip("A player still inside the safe base: zombies surface in a band this wide just outside it, waiting at the door.")]
        [Min(2f)] public float nearBaseSpread = 8f;

        [Header("Pacing")]
        public float announceDuration = 3f;
        public float breatherDuration = 5f;

        [Header("Difficulty")]
        [Tooltip("How zombies and the player grow wave by wave (health, damage, speed). Empty = the config's defaults.")]
        public WaveBalanceConfig balance;

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
            // The pads are only spawn positions now: the old flat pulsing discs are hidden, the ground breaks
            // open where each zombie actually surfaces instead (SpawnBreachEffect, on every machine).
            if (spawnPoints != null)
                foreach (var pad in spawnPoints)
                {
                    if (pad == null) continue;
                    foreach (var r in pad.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                    foreach (var c in pad.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                    foreach (var p in pad.GetComponentsInChildren<ZombieRush.Zombie.SpawnPointPulse>(true)) p.enabled = false;
                }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            WaveScaling.Balance = balance;
            WaveScaling.Wave = 1;
            StartCoroutine(RunWaves());
        }

        IEnumerator RunWaves()
        {
            // Wait one frame first so every UI script has subscribed before
            // Wave 1's announcement fires.
            yield return null;

            // the waves run on the host only; everyone else sees its zombies
            while (!ZombieRush.Net.NetSession.IsRunning) yield return null;
            if (!ZombieRush.Net.NetSession.IsServer) yield break;

            if (!HasValidSetup()) yield break;
            yield return Prewarm();

            // Players gear up in the safe base first; the fight starts when someone steps out.
            while (SafeBase.Instance != null && !SafeBase.Instance.BattleStarted) yield return null;

            while (true)
            {
                CurrentWave++;
                WaveScaling.Balance = balance;
                WaveScaling.Wave = CurrentWave;
                var zone = ZoneManager.Instance;
                if (spawnAtZoneEdge && zone != null) ArrangeSpawnPoints(zone.center, zone.CurrentRadius * edgeRadiusFactor, zone.Forward);
                GameEvents.RaiseWaveAnnounced(CurrentWave);
                yield return new WaitForSeconds(announceDuration);

                int players = PlayerCount;
                int count = Mathf.CeilToInt((baseZombiesPerWave + (CurrentWave - 1) * extraZombiesPerWave) * Difficulty.ZombieCountMultiplier(players));
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

                    if (spawned < count) yield return new WaitForSeconds(timeBetweenSpawns * Difficulty.SpawnIntervalMultiplier(players));
                }

                LogSpawnSummary();

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

            // Each zombie gets its own checked spot around the pad, so a wave doesn't stack
            // on one point and nobody surfaces inside a building or a walled-off pocket.
            var spawnPos = point.position;
            var zone = ZoneManager.Instance;
            if (spawnNearPlayer && zone != null && TryNearPlayer(zone, out var near))
            {
                spawnPos = near;
                point.position = near + Vector3.up * padHeightOffset; // the pad's glow marks where it surfaces
                nearSpawns++;
            }
            else
            {
                padSpawns++;
                if (zone != null && ArenaSpawn.TryNear(point.position, zone.Shape, PlayerPosition(zone.center), out var spot))
                    spawnPos = spot;
            }

            var zombie = pool.Get(prefab, spawnPos, Quaternion.identity);
            if (zombie == null) return false;

            var b = WaveScaling.Balance;
            float healthMultiplier = b.ZombieHealth(CurrentWave) * Difficulty.HealthMultiplier(PlayerCount);
            zombie.ResetForSpawn(healthMultiplier);

            var ai = zombie.GetComponent<ZombieAI>();
            if (ai != null)
            {
                ai.difficultyMultiplier = b.ZombieDamage(CurrentWave);
                ai.speedMultiplier = b.ZombieSpeed(CurrentWave);
                ai.alwaysHunt = true; // wave zombies may surface beyond sight range; they must never idle and stall the wave
            }

            zombie.GetComponent<ZombieSpawnRise>().Begin(spawnPos);
            var zn = zombie.GetComponent<ZombieRush.Net.ZombieNet>();
            if (zn != null) zn.ServerSpawn();

            AliveCount++;
            return true;
        }

        /// Spreads the spawn pads evenly around a ring (random rotation each call),
        /// nudging each one inward until it lands on a good spawn spot (open sky, clear,
        /// walkable route to the player - see ArenaSpawn); if its slice of the ring has
        /// none, it searches the rest of the zone. Also used by the editor level tools.
        /// `halfForward` (non-zero) keeps them on the half circle that opens that way.
        public void ArrangeSpawnPoints(Vector3 center, float radius, Vector3 halfForward = default)
        {
            if (spawnPoints == null || spawnPoints.Length == 0) return;
            var shape = new ZoneShape(center, radius, halfForward);
            // Random every wave, wave 1 included: a random turn of the ring (full circle), and every pad at a
            // random place inside its own slice and a random depth into the zone. (The half circle used to get
            // offset 0 and the middle of each slice, so the pads stood on the same 5 spots wave after wave
            // until the zone grew.)
            float offset = shape.Half ? 0f : Random.value;
            float slice = 1f / spawnPoints.Length;
            var player = PlayerPosition(center);

            for (int i = 0; i < spawnPoints.Length; i++)
            {
                var pad = spawnPoints[i];
                if (pad == null) continue;
                float inSlice = Random.Range(0.12f, 0.88f), depth = Random.Range(0.7f, 1f);

                for (int attempt = 0; attempt < 36; attempt++)
                {
                    // t runs round the circle (0..1), or side to side across the half circle.
                    float t, r;
                    if (attempt < 12)
                    {
                        t = offset + (i + inSlice) * slice + (attempt % 3 - 1) * slice * 0.2f;
                        r = Mathf.Max(3f, radius * depth - (attempt / 3) * 3f);
                    }
                    else
                    {
                        t = Random.value;
                        r = radius * Random.Range(0.45f, 1f);
                    }
                    t = shape.Half ? Mathf.Clamp(t, 0.06f, 0.94f) : Mathf.Repeat(t, 1f);
                    var probe = center + shape.Direction(t) * r;
                    if (!ArenaSpawn.IsGoodSpawn(probe, center, player, out var ground)) continue;

                    pad.position = ground + Vector3.up * padHeightOffset;
                    break;
                }
            }
        }

        /// A good spot (open sky, clear, inside the zone, away from the base, walkable route -
        /// see ArenaSpawn) on a ring around a random living player who is outside the safe base.
        bool TryNearPlayer(ZoneManager zone, out Vector3 spot)
        {
            spot = default;
            var targets = ZombieRush.Player.PlayerHealth.All;
            int start = targets.Count > 0 ? Random.Range(0, targets.Count) : 0;
            for (int n = 0; n < targets.Count; n++)
            {
                var player = targets[(start + n) % targets.Count];
                if (player == null || player.IsDead) continue;

                Vector3 p = player.transform.position;
                Vector3 around = p;
                float min = nearPlayerMinDistance, max = Mathf.Max(min + 1f, nearPlayerMaxDistance);
                var safe = SafeBase.Instance;
                if (player.InSafeBase)
                {
                    // Can't surface next to someone in the base: ring the base edge instead.
                    if (safe == null) continue;
                    around = safe.Center;
                    min = safe.radius + ArenaSpawn.BaseMargin + 1f;
                    max = min + nearBaseSpread;
                }
                for (int attempt = 0; attempt < 24; attempt++)
                {
                    Vector2 dir = Random.insideUnitCircle.normalized;
                    if (dir == Vector2.zero) dir = Vector2.up;
                    var probe = around + new Vector3(dir.x, 0f, dir.y) * Random.Range(min, max);
                    // Ground height is judged against the player's own height (the arena is hilly),
                    // with room for slopes over the ring's distance.
                    // In the base, the route is checked to the base edge (zombies wait there, never inside).
                    var target = player.InSafeBase ? around + new Vector3(dir.x, 0f, dir.y) * (safe.radius + 1f) : p;
                    if (ArenaSpawn.IsGoodSpawn(probe, p, target, out spot, 6f)) return true;
                    NoteReject(ArenaSpawn.LastReject);
                }
            }
            return false;
        }

        // Why near-player spawns fell back to the pads this wave - logged once per wave.
        readonly System.Collections.Generic.Dictionary<string, int> rejects = new System.Collections.Generic.Dictionary<string, int>();
        int nearSpawns, padSpawns;
        void NoteReject(string why) { if (!string.IsNullOrEmpty(why)) rejects[why] = rejects.TryGetValue(why, out var n) ? n + 1 : 1; }

        void LogSpawnSummary()
        {
            if (spawnNearPlayer && padSpawns > 0)
            {
                var reasons = string.Join(", ", System.Linq.Enumerable.Select(rejects, r => $"{r.Key} x{r.Value}"));
                Debug.Log($"[Zombie Rush] Wave {CurrentWave}: {nearSpawns} zombies spawned near the player, {padSpawns} at the zone-edge pads. Near-player spots rejected because: {(reasons.Length > 0 ? reasons : "player was in the safe base / no living player")}", this);
            }
            rejects.Clear(); nearSpawns = 0; padSpawns = 0;
        }

        /// Players in the match (alive or not) - drives squad difficulty. Never less than 1.
        static int PlayerCount => Mathf.Max(1, ZombieRush.Player.PlayerHealth.All.Count);

        static Vector3 PlayerPosition(Vector3 fallback) =>
            ZombieRush.Player.PlayerHealth.Instance != null ? ZombieRush.Player.PlayerHealth.Instance.transform.position : fallback;

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
