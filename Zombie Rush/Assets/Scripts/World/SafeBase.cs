using UnityEngine;
using ZombieRush.Core;
using ZombieRush.Player;

namespace ZombieRush.World
{
    /// The players' base: a fixed-size safe zone around the PlayerBase marker.
    ///   - zombies can never enter it (ZombieAI keeps them at the edge) and players
    ///     inside can't be hurt;
    ///   - the starting gear (BasePickup) lives here;
    ///   - waves don't start until a player first steps out into the fight zone.
    /// The fight zone (ZoneManager) surrounds it and is what coins expand.
    public class SafeBase : MonoBehaviour
    {
        public static SafeBase Instance { get; private set; }

        [Tooltip("Radius of the safe zone in metres. Fixed - it never grows.")]
        public float radius = 12f;
        [Tooltip("Optional glowing edge ring (a ZoneBoundaryFX child).")]
        public ZoneBoundaryFX ring;

        /// True once any player has left the base; WaveManager waits for this.
        public bool BattleStarted { get; private set; }

        public Vector3 Center => transform.position;

        bool localInside = true;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            if (ring != null) ring.SetZone(Center, radius, false);
            GameEvents.RaiseSafeBaseChanged(true);
            GameEvents.RaiseNotice("SAFE BASE", "GEAR UP  ·  STEP OUT TO START THE WAVE");
        }

        /// Flat (XZ) test - height doesn't matter, the base is a cylinder.
        public bool Contains(Vector3 position, float margin = 0f)
        {
            var d = position - Center;
            d.y = 0f;
            float r = radius + margin;
            return d.sqrMagnitude <= r * r;
        }

        /// Nearest point just outside the edge, in the direction of `position` from the centre.
        public Vector3 EdgePoint(Vector3 position, float outside = 0.6f)
        {
            var d = position - Center;
            d.y = 0f;
            if (d.sqrMagnitude < 0.0001f) d = Vector3.forward;
            var p = Center + d.normalized * (radius + outside);
            p.y = position.y;
            return p;
        }

        void Update()
        {
            if (!BattleStarted)
            {
                foreach (var p in PlayerHealth.All)
                    if (p != null && !p.IsDead && !Contains(p.transform.position))
                    {
                        BattleStarted = true;
                        GameEvents.RaiseBattleStarted();
                        break;
                    }
            }

            var local = PlayerHealth.Instance;
            if (local == null) return;
            bool inside = Contains(local.transform.position);
            if (inside == localInside) return;
            localInside = inside;
            GameEvents.RaiseSafeBaseChanged(inside);
            if (inside && BattleStarted) GameEvents.RaiseNotice("SAFE BASE", "ZOMBIES CAN'T GET IN");
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.8f);
            const int n = 48;
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
                Gizmos.DrawLine(Center + new Vector3(Mathf.Cos(a0) * radius, 0.2f, Mathf.Sin(a0) * radius),
                                Center + new Vector3(Mathf.Cos(a1) * radius, 0.2f, Mathf.Sin(a1) * radius));
            }
        }
#endif
    }
}
