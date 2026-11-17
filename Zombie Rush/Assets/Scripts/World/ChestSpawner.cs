using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using ZombieRush.Core;

namespace ZombieRush.World
{
    /// Puts locked loot chests down in the fight zone at the start of every wave: `perWave` new ones (+1 for each
    /// extra squad member), at most `maxClosed` unopened at once. Spots are random but seeded by the wave number, so
    /// every machine in a squad gets the same chests in the same places (each player opens their own: personal loot).
    /// Never inside the safe base, on walkable ground (the zombies' nav mesh), apart from each other, facing the base.
    public class ChestSpawner : MonoBehaviour
    {
        public GameObject chestPrefab;
        public int perWave = 3;
        public int maxClosed = 6;
        [Tooltip("Metres kept clear of the safe base edge and of the zone wall.")]
        public float baseMargin = 6f, wallMargin = 4f;
        public float minSpacing = 12f;

        void OnEnable() => GameEvents.OnWaveStarted += HandleWave;
        void OnDisable() => GameEvents.OnWaveStarted -= HandleWave;

        void HandleWave(int wave)
        {
            if (chestPrefab == null) return;
            int closed = 0;
            foreach (var c in LootChest.All) if (c != null && !c.Opened) closed++;
            int players = Mathf.Max(1, Player.PlayerHealth.All.Count);
            int want = Mathf.Min(perWave + players - 1, maxClosed - closed);
            if (want > 0) Spawn(wave, want);
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
            var taken = new List<Vector3>();
            foreach (var other in LootChest.All) if (other != null) taken.Add(other.transform.position);
            int placed = 0;
            for (int tries = 0; tries < count * 60 && placed < count; tries++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Lerp(inner, outer, Mathf.Sqrt((float)rng.NextDouble()));
                var p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                if (!NavMesh.SamplePosition(p + Vector3.up * 2f, out var hit, 3f, NavMesh.AllAreas)) continue;
                p = hit.position;
                if (!shape.Contains(p, -wallMargin) || (safe != null && safe.Contains(p, baseMargin))) continue;
                bool near = false;
                foreach (var q in taken) if ((q - p).sqrMagnitude < minSpacing * minSpacing) { near = true; break; }
                if (near) continue;
                var face = (safe != null ? safe.Center : c) - p; face.y = 0f;
                var rot = Quaternion.LookRotation(face.sqrMagnitude > 0.01f ? face.normalized : Vector3.forward, Vector3.up);
                if (Physics.CheckBox(p + Vector3.up * 0.5f, new Vector3(0.6f, 0.4f, 0.45f), rot, ~0, QueryTriggerInteraction.Ignore)) continue;
                var go = Instantiate(chestPrefab, p, rot);
                go.name = $"LootChest_w{wave}_{placed + 1}";
                taken.Add(p);
                placed++;
            }
            return placed;
        }
    }
}
