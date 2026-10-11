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
    /// Half circle mode: covers the base building from its back edge (shared with the
    /// fight zone's straight edge) to a little in front of it, opening toward transform.forward.
    public class SafeBase : MonoBehaviour
    {
        public static SafeBase Instance { get; private set; }

        [Tooltip("Radius of the safe zone in metres. Fixed - it never grows.")]
        public float radius = 12f;
        [Tooltip("Optional glowing edge ring (a ZoneBoundaryFX child).")]
        public ZoneBoundaryFX ring;
        [Tooltip("The base building (set by Level > Fit Half-Circle Zones To Base Building).")]
        public Transform building;

        [Header("Half circle")]
        [Tooltip("Half circle opening toward this object's forward, straight edge behind it.")]
        public bool halfCircle;
        [Tooltip("Metres from this marker back to the straight edge.")]
        public float backOffset = 10f;

        /// True once any player has left the base; WaveManager waits for this.
        public bool BattleStarted { get; private set; }

        public Vector3 Center => transform.position;

        public Vector3 Forward
        {
            get
            {
                if (!halfCircle) return Vector3.zero;
                var f = transform.forward;
                f.y = 0f;
                return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.zero;
            }
        }

        public ZoneShape Shape => new ZoneShape(Center - Forward * backOffset, radius, Forward);

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
            if (ring != null)
            {
                var shape = Shape;
                ring.SetShape(shape.forward, straightEdge: false);   // the fight zone's wall already runs along the back
                ring.SetZone(shape.center, radius, false);
            }
            GameEvents.RaiseSafeBaseChanged(true);
            GameEvents.RaiseNotice("SAFE BASE", "GEAR UP  ·  STEP OUT TO START THE WAVE");
        }

        /// Flat (XZ) test - height doesn't matter, the base is a cylinder (or half of one).
        public bool Contains(Vector3 position, float margin = 0f) => Shape.Contains(position, margin);

        /// Nearest point just outside the round edge, in the direction of `position`.
        public Vector3 EdgePoint(Vector3 position, float outside = 0.6f) => Shape.OutsidePoint(position, outside);

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
            var shape = Shape;
            var lift = Vector3.up * 0.2f;
            const int n = 48;
            for (int i = 0; i < n; i++)
                Gizmos.DrawLine(shape.center + shape.Direction(i / (float)n) * radius + lift,
                                shape.center + shape.Direction((i + 1) / (float)n) * radius + lift);
            if (shape.Half)
                Gizmos.DrawLine(shape.center - shape.Right * radius + lift, shape.center + shape.Right * radius + lift);
        }
#endif
    }
}
