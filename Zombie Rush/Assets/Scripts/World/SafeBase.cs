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
        [Tooltip("Show the glowing energy wall round the base. Off: the compound's sandbags and barriers mark it.")]
        public bool showRing = false;
        [Tooltip("Players can't shoot while inside the base (they can't be hurt there either).")]
        public bool noFiringInside = true;
        [Tooltip("Where players start (and come back after falling): inside the hall, facing the door. Set by the compound tool.")]
        public Transform playerStart;

        /// A free spot at the start point (or right next to it) for a player capsule, facing the start's way.
        public bool TryGetStart(out Vector3 position, out Quaternion rotation)
        {
            position = default; rotation = Quaternion.identity;
            if (playerStart == null) return false;
            Vector3 f = Vector3.ProjectOnPlane(playerStart.forward, Vector3.up);
            rotation = f.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(f.normalized, Vector3.up) : Quaternion.identity;
            for (float r = 0f; r <= 3f; r += 0.75f)
                for (int i = 0; i < (r <= 0f ? 1 : 8); i++)
                {
                    Vector3 p = playerStart.position + Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward * r;
                    if (Physics.CheckCapsule(p + Vector3.up * 0.45f, p + Vector3.up * 1.5f, 0.35f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    position = p + Vector3.up * 0.05f;
                    return true;
                }
            position = playerStart.position + Vector3.up * 0.05f;
            return true;
        }
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
            if (ring != null && !showRing) ring.gameObject.SetActive(false);   // the energy wall is off: the compound marks the base
            else if (ring != null)
            {
                var shape = Shape;
                ring.SetShape(shape.forward, straightEdge: false);   // the fight zone's wall already runs along the back
                ring.SetZone(shape.center, radius, false);
            }
            GameEvents.RaiseSafeBaseChanged(true);
            GameEvents.RaiseNotice("SAFE BASE", noFiringInside ? "GEAR UP  ·  NO FIRING IN THE BASE  ·  STEP OUT TO FIGHT" : "GEAR UP  ·  STEP OUT TO START THE WAVE");
            ZombieRush.UI.SafeZoneLabel.Ensure(this); // floating "SAFE ZONE" sign over the base building
        }

        /// Flat (XZ) test - height doesn't matter, the base is a cylinder (or half of one).
        public bool Contains(Vector3 position, float margin = 0f) => Shape.Contains(position, margin);

        /// False while this spot is inside a base where firing is not allowed.
        public static bool FiringAllowedAt(Vector3 position) =>
            Instance == null || !Instance.noFiringInside || !Instance.Contains(position);

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
            if (inside && BattleStarted) GameEvents.RaiseNotice("SAFE BASE", noFiringInside ? "ZOMBIES CAN'T GET IN  ·  NO FIRING HERE" : "ZOMBIES CAN'T GET IN");
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
