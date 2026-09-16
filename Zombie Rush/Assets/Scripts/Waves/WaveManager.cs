using System.Collections;
using UnityEngine;
using ZombieRush.Player;
using ZombieRush.Zombie;

namespace ZombieRush.Waves
{
    /// Drives the wave loop: announce -> spawn zombies over time from underground
    /// spawn points -> wait for them all to die -> breather -> next wave.
    public class WaveManager : MonoBehaviour
    {
        public static WaveManager Instance { get; private set; }

        [Header("Spawning")]
        public GameObject zombiePrefab;
        public Transform[] spawnPoints;
        public int baseZombiesPerWave = 5;
        public int extraZombiesPerWave = 2;
        public float timeBetweenSpawns = 0.8f;

        [Header("Pacing")]
        public float announceDuration = 3f;
        public float breatherDuration = 5f;

        [Header("Difficulty")]
        public float difficultyPerWave = 0.15f; // +15% zombie health/damage per wave beyond the first

        public System.Action<int> OnWaveAnnounced;
        public System.Action<int> OnWaveStarted;
        public System.Action<int> OnWaveCompleted;

        public int CurrentWave { get; private set; }
        public int AliveCount { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start() => StartCoroutine(RunWaves());

        IEnumerator RunWaves()
        {
            // Wait one frame first so every other script's Start() (in particular
            // WaveBannerUI's) has definitely run before Wave 1's announcement fires -
            // otherwise Wave 1 can be announced before anything is listening yet.
            yield return null;

            while (true)
            {
                CurrentWave++;
                PlayerHealth.Instance?.FullHeal();
                OnWaveAnnounced?.Invoke(CurrentWave);
                yield return new WaitForSeconds(announceDuration);

                int count = baseZombiesPerWave + (CurrentWave - 1) * extraZombiesPerWave;
                AliveCount = count;
                OnWaveStarted?.Invoke(CurrentWave);

                // Spawn in rounds - one zombie from EVERY spawn point per round, all
                // at once - rather than one at a time from a random point. That's
                // what actually gets zombies converging on the player from every
                // direction simultaneously instead of trickling in from wherever
                // the player happens to be standing near.
                int spawned = 0;
                while (spawned < count && spawnPoints != null && spawnPoints.Length > 0)
                {
                    foreach (var point in spawnPoints)
                    {
                        if (spawned >= count) break;
                        if (point == null) continue;
                        SpawnZombieAt(point);
                        spawned++;
                    }
                    if (spawned < count) yield return new WaitForSeconds(timeBetweenSpawns);
                }

                yield return new WaitUntil(() => AliveCount <= 0);
                OnWaveCompleted?.Invoke(CurrentWave);
                yield return new WaitForSeconds(breatherDuration);
            }
        }

        void SpawnZombieAt(Transform point)
        {
            if (zombiePrefab == null) return;

            var zombieGo = Instantiate(zombiePrefab, point.position, Quaternion.identity);

            float multiplier = 1f + (CurrentWave - 1) * difficultyPerWave;

            var health = zombieGo.GetComponent<ZombieHealth>();
            if (health != null)
            {
                health.ApplyDifficulty(multiplier);
                health.OnDied += HandleZombieDied;
            }

            var ai = zombieGo.GetComponent<ZombieAI>();
            if (ai != null) ai.difficultyMultiplier = multiplier;

            var riser = zombieGo.AddComponent<ZombieSpawnRise>();
            riser.Init(point.position);
        }

        void HandleZombieDied(ZombieHealth zombie)
        {
            AliveCount = Mathf.Max(0, AliveCount - 1);
        }
    }
}
