using UnityEngine;

namespace ZombieRush.World
{
    /// Bullets fly through everything under this object (the base compound: walls, crates,
    /// floodlights), so players and turrets in the safe base can hit the zombies waiting
    /// outside it. Players and zombies still collide with it as normal.
    [DisallowMultipleComponent]
    public class ShootThrough : MonoBehaviour
    {
        const int MaxSkips = 8;

        public static bool Is(Collider c) => c != null && c.GetComponentInParent<ShootThrough>() != null;

        /// Like Physics.Raycast (triggers ignored), but passes through shoot-through colliders.
        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit, float range, int mask)
        {
            for (int i = 0; i <= MaxSkips; i++)
            {
                if (!Physics.Raycast(origin, direction, out hit, range, mask, QueryTriggerInteraction.Ignore)) return false;
                if (!Is(hit.collider)) return true;
                // Carry on from just past the hit; a ray that starts inside a collider does not hit it again.
                float step = hit.distance + 0.01f;
                origin += direction * step;
                range -= step;
                if (range <= 0f) break;
            }
            hit = default;
            return false;
        }
    }
}
