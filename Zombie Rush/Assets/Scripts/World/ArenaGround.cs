using UnityEngine;

namespace ZombieRush.World
{
    /// Physics helpers for finding standable, unobstructed ground in the arena
    /// (player deploy point, zombie spawn pads). Works in edit mode too.
    public static class ArenaGround
    {
        const float RayHeight = 60f;
        static readonly RaycastHit[] rayHits = new RaycastHit[16];
        static readonly Collider[] overlaps = new Collider[16];

        /// Colliders under this transform are ignored (e.g. the zone boundary cylinder).
        public static Transform Ignore;

        /// Highest surface at (x,z). Valid only if it's roughly flat and close to
        /// the arena ground height - i.e. not the roof of a container or hangar.
        public static bool TryGround(Vector3 position, float groundY, out Vector3 point, float tolerance = 1.5f)
        {
            point = default;
            var origin = new Vector3(position.x, groundY + RayHeight, position.z);
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, rayHits, RayHeight * 2f, ~0, QueryTriggerInteraction.Ignore);

            bool found = false;
            RaycastHit best = default;
            for (int i = 0; i < count; i++)
            {
                var hit = rayHits[i];
                if (IsIgnored(hit.collider)) continue;
                if (!found || hit.point.y > best.point.y) { best = hit; found = true; }
            }

            if (!found || best.normal.y < 0.75f || Mathf.Abs(best.point.y - groundY) > tolerance) return false;
            point = best.point;
            return true;
        }

        /// True if a body of the given size can stand at `groundPoint` without overlapping props.
        public static bool IsClear(Vector3 groundPoint, float radius = 0.5f, float height = 1.8f)
        {
            var bottom = groundPoint + Vector3.up * (radius + 0.15f);
            var top = groundPoint + Vector3.up * Mathf.Max(radius + 0.15f, height - radius);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, overlaps, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (!IsIgnored(overlaps[i])) return false;
            return true;
        }

        /// Searches outward from `center` in rings for the nearest clear, standable spot.
        public static bool TryFindClearSpot(Vector3 center, float groundY, float maxSearch, out Vector3 point, float radius = 0.5f)
        {
            for (float r = 0f; r <= maxSearch; r += 1.5f)
            {
                int steps = r <= 0f ? 1 : Mathf.Max(8, Mathf.CeilToInt(2f * Mathf.PI * r / 1.5f));
                for (int i = 0; i < steps; i++)
                {
                    float a = i * Mathf.PI * 2f / steps;
                    var probe = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                    if (TryGround(probe, groundY, out point) && IsClear(point, radius)) return true;
                }
            }
            point = default;
            return false;
        }

        static bool IsIgnored(Collider c)
        {
            if (c == null) return true;
            if (Ignore != null && c.transform.IsChildOf(Ignore)) return true;
            // Moving actors are never "props" blocking a spot.
            return c is CharacterController || c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic;
        }
    }
}
