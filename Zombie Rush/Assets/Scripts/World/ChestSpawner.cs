using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using ZombieRush.Core;

namespace ZombieRush.World
{
    /// Puts locked loot chests down in the fight zone at the start of every wave: `perWave` new ones (+1 for each
    /// extra squad member), no more standing than `lifeWaves` waves' worth (at least `maxClosed`). Spots are random but seeded by the wave
    /// number, so every machine in a squad gets the same chests in the same places (each player opens their own:
    /// personal loot). To stay the same everywhere, nothing that differs between machines may change the result:
    /// the spawner keeps its own record of the chests it placed (opened or not), unopened chests sink after
    /// `lifeWaves` waves on every machine alike, and players, zombies and turrets don't block a spot.
    /// Never inside the safe base, on walkable ground (the zombies' nav mesh), apart from each other, facing the base.
    public class ChestSpawner : MonoBehaviour
    {
        public GameObject chestPrefab;
        public int perWave = 3;
        public int maxClosed = 6;
        [Tooltip("Waves a chest stays; an unopened one sinks away after that (the same on every machine).")]
        public int lifeWaves = 2;
        [Tooltip("Metres kept clear of the safe base edge and of the zone wall.")]
        public float baseMargin = 6f, wallMargin = 4f;
        public float minSpacing = 12f;

        struct Placed { public int wave; public Vector3 position; public LootChest chest; }
        readonly List<Placed> placed = new List<Placed>();

        void OnEnable() => GameEvents.OnWaveStarted += HandleWave;
        void OnDisable() => GameEvents.OnWaveStarted -= HandleWave;

        void HandleWave(int wave)
        {
            if (chestPrefab == null) return;
            ExpireOld(wave);
            int players = SquadSize();
            int perThisWave = perWave + players - 1;
            // room for every wave a chest lives (so a squad gets its chests every wave too), never below maxClosed
            int cap = Mathf.Max(maxClosed, perThisWave * Mathf.Max(1, lifeWaves));
            int want = Mathf.Min(perThisWave, cap - placed.Count);
            if (want > 0) Spawn(wave, want);
        }

        /// Players in the match, counted the same way on every machine.
        static int SquadSize()
        {
            if (Net.NetSession.IsRunning && Net.NetPlayer.All.Count > 0) return Net.NetPlayer.All.Count;
            return Mathf.Max(1, Player.PlayerHealth.All.Count);
        }

        void ExpireOld(int wave)
        {
            for (int i = placed.Count - 1; i >= 0; i--)
            {
                if (wave - placed[i].wave < Mathf.Max(1, lifeWaves)) continue;
                if (placed[i].chest != null && !placed[i].chest.Opened) placed[i].chest.Expire();
                placed.RemoveAt(i);
            }
        }

        /// Places `count` chests for this wave; returns how many found a spot.
        public int Spawn(int wave, int count)
        {
            var zone = ZoneManager.Instance;
            var safe = SafeBase.Instance;
            if (zone == null) return 0;
            var rng = new System.Random(wave * 7919 + 17);
            var shape = zone.Shape;
            Vector3 c = zone.center;
            float inner = (safe != null ? safe.radius : 0f) + baseMargin;
            float outer = Mathf.Max(inner + 2f, zone.CurrentRadius - wallMargin);
            int made = 0;
            for (int tries = 0; tries < count * 60 && made < count; tries++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Lerp(inner, outer, Mathf.Sqrt((float)rng.NextDouble()));
                var p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                if (!NavMesh.SamplePosition(p + Vector3.up * 2f, out var hit, 3f, NavMesh.AllAreas)) continue;
                p = hit.position;
                if (!shape.Contains(p, -wallMargin) || (safe != null && safe.Contains(p, baseMargin))) continue;
                bool near = false;
                foreach (var q in placed) if ((q.position - p).sqrMagnitude < minSpacing * minSpacing) { near = true; break; }
                if (near) continue;
                var face = (safe != null ? safe.Center : c) - p; face.y = 0f;
                var rot = Quaternion.LookRotation(face.sqrMagnitude > 0.01f ? face.normalized : Vector3.forward, Vector3.up);
                if (BlockedBySolid(p + Vector3.up * 0.5f, new Vector3(0.6f, 0.4f, 0.45f), rot)) continue;
                var go = Instantiate(chestPrefab, p, rot);
                go.name = $"LootChest_w{wave}_{made + 1}";
                placed.Add(new Placed { wave = wave, position = p, chest = go.GetComponent<LootChest>() });
                made++;
            }
            return made;
        }

        static readonly Collider[] overlap = new Collider[32];

        /// Something solid in the box that is the same on every machine: level geometry, not players, zombies,
        /// turrets or chests (those move or differ between machines; chests keep apart by minSpacing instead).
        static bool BlockedBySolid(Vector3 centre, Vector3 halfExtents, Quaternion rot)
        {
            int n = Physics.OverlapBoxNonAlloc(centre, halfExtents, overlap, rot, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = overlap[i];
                if (col is CharacterController) continue;
                if (col.GetComponentInParent<Player.PlayerHealth>() != null) continue;
                if (col.GetComponentInParent<Zombie.ZombieHealth>() != null) continue;
                if (col.GetComponentInParent<Weapons.Turret>() != null) continue;
                if (col.GetComponentInParent<LootChest>() != null) continue;
                return true;
            }
            return false;
        }
    }
}
