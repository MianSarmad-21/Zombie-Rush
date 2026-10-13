using UnityEngine;
using UnityEngine.AI;

namespace ZombieRush.World
{
    /// Loads the baked zombie walk map (made by Zombie Rush > Level > Set Up Zone + Zombie Paths)
    /// so zombies can path around buildings and spawns can be checked for a route to the player.
    /// Uses the built-in AI module only - no AI Navigation package or NavMeshAgent needed.
    public class ArenaNavMesh : MonoBehaviour
    {
        public NavMeshData data;

        NavMeshDataInstance instance;

        /// True while a walk map is loaded; everything falls back to straight-line behaviour without one.
        public static bool Ready { get; private set; }

        void OnEnable()
        {
            if (data == null) return;
            instance = NavMesh.AddNavMeshData(data);
            Ready = instance.valid;
        }

        void OnDisable()
        {
            if (instance.valid) instance.Remove();
            instance = default;
            Ready = false;
        }

        /// Nearest point on the walk map within `range`, or false (always false without a walk map).
        public static bool TrySnap(Vector3 position, out Vector3 snapped, float range = 1.5f)
        {
            if (Ready && NavMesh.SamplePosition(position, out var hit, range, NavMesh.AllAreas))
            {
                snapped = hit.position;
                return true;
            }
            snapped = position;
            return false;
        }

        /// Walkable route between two points (complete = reaches the end, not just the closest spot).
        public static bool HasCompletePath(Vector3 from, Vector3 to, NavMeshPath path)
        {
            if (!Ready) return true;
            if (!TrySnap(from, out var a) || !TrySnap(to, out var b, 3f)) return false;
            return NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
        }
    }

    /// Where zombies may surface: on open, clear, walkable ground under the open sky
    /// (never inside or under a building), inside the zone, away from the player,
    /// and with a walkable route to the player so nobody gets trapped.
    public static class ArenaSpawn
    {
        public const float MinPlayerDistance = 7f;
        /// Zombies never surface this close to the safe base edge.
        public const float BaseMargin = 8f;
        const float SkyCheck = 40f;

        static NavMeshPath path;

        /// Why the last IsGoodSpawn call said no (for diagnostics).
        public static string LastReject { get; private set; } = "";

        public static bool IsGoodSpawn(Vector3 candidate, Vector3 groundCenter, Vector3 player, out Vector3 spot, float groundTolerance = 2.5f)
        {
            path ??= new NavMeshPath();
            spot = candidate;
            if (!ArenaGround.TryGround(candidate, groundCenter.y, out var ground, groundTolerance)) return Reject("no flat ground / roof");
            if (ArenaNavMesh.Ready && !ArenaNavMesh.TrySnap(ground, out ground, 1f)) return Reject("off the NavMesh");
            if (Flat(ground - player).sqrMagnitude < MinPlayerDistance * MinPlayerDistance) return Reject("too close to the player");
            if (SafeBase.Instance != null && SafeBase.Instance.Contains(ground, BaseMargin)) return Reject("too close to the safe base");
            if (ZoneManager.Instance != null && !ZoneManager.Instance.Shape.Contains(ground, -0.5f)) return Reject("outside the fight zone");
            foreach (var p in ZombieRush.Player.PlayerHealth.All)
                if (p != null && Flat(ground - p.transform.position).sqrMagnitude < MinPlayerDistance * MinPlayerDistance) return Reject("too close to a player");
            if (Physics.Raycast(ground + Vector3.up * 2.2f, Vector3.up, SkyCheck, ~0, QueryTriggerInteraction.Ignore)) return Reject("under a roof");
            if (!ArenaGround.IsClear(ground, 0.6f)) return Reject("blocked by a prop");
            if (!ArenaNavMesh.HasCompletePath(ground, player, path)) return Reject("no walkable path to the player");
            spot = ground;
            LastReject = "";
            return true;
        }

        static bool Reject(string why) { LastReject = why; return false; }

        /// A good spot near `anchor` (the anchor itself first), kept inside the zone.
        public static bool TryNear(Vector3 anchor, ZoneShape zone, Vector3 player, out Vector3 spot, float scatter = 4f, int tries = 10)
        {
            for (int i = 0; i <= tries; i++)
            {
                var offset = i == 0 ? Vector2.zero : Random.insideUnitCircle * scatter;
                var p = zone.ClampInside(anchor + new Vector3(offset.x, 0f, offset.y), 1f);
                if (IsGoodSpawn(p, zone.center, player, out spot)) return true;
            }
            spot = anchor;
            return false;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
