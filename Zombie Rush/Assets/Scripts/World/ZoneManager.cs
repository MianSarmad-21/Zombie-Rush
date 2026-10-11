using UnityEngine;
using ZombieRush.Core;
using ZombieRush.Player;

namespace ZombieRush.World
{
    /// The playable area starts small and grows as the player expands it by
    /// spending coins. The boundary is a hard wall - you physically cannot walk
    /// past it until it's expanded.
    /// Half circle mode: the base backs onto a straight edge just behind it and the zone
    /// opens toward the PlayerBase marker's forward (blue arrow) - rotate the marker to aim it.
    public class ZoneManager : MonoBehaviour
    {
        public static ZoneManager Instance { get; private set; }

        [Header("Zone")]
        public Vector3 center = new Vector3(500f, 0f, 500f);
        [Tooltip("Optional: the player's base. Drag it anywhere on the map - the safe zone starts there and grows outward from it.")]
        public Transform playerBase;
        public float startRadius = 30f;
        public float maxRadius = 90f;
        public float expandStep = 15f;

        [Header("Half circle")]
        [Tooltip("Zone is a half circle opening toward the PlayerBase's forward, with its straight edge behind the base.")]
        public bool halfCircle;
        [Tooltip("Metres from the PlayerBase back to the straight edge.")]
        public float backOffset = 10f;

        [Header("Cost")]
        public int expandCost = 20;

        [Header("Visual")]
        public Transform boundaryVisual; // ZoneBoundaryFX wall, or a plain cylinder scaled to match the current radius

        [Header("Deploy")]
        [Tooltip("Place the player on clear ground at the base (or zone centre) when the level starts.")]
        public bool deployPlayerAtCenter = true;

        public float CurrentRadius { get; private set; }
        public bool CanExpand => CurrentRadius < maxRadius;

        /// Flat direction the half circle opens toward (zero for a full circle).
        public Vector3 Forward
        {
            get
            {
                if (!halfCircle || playerBase == null) return Vector3.zero;
                var f = playerBase.forward;
                f.y = 0f;
                return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.zero;
            }
        }

        /// Centre of the round edge: on the straight edge behind the base in half circle mode.
        public Vector3 ArcCenter => playerBase == null ? center : playerBase.position - Forward * backOffset;

        public ZoneShape Shape => new ZoneShape(center, CurrentRadius, Forward);
        public ZoneShape ShapeAt(float radius) => new ZoneShape(ArcCenter, radius, Forward);

        CharacterController playerController;
        Transform player;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            CurrentRadius = startRadius;
            if (playerBase != null) center = ArcCenter;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            var playerGo = PlayerHealth.Instance != null ? PlayerHealth.Instance.gameObject : GameObject.FindGameObjectWithTag(GameTags.Player);
            if (playerGo != null)
            {
                player = playerGo.transform;
                playerController = playerGo.GetComponent<CharacterController>();
            }
            ArenaGround.Ignore = boundaryVisual;
            if (deployPlayerAtCenter) DeployPlayer();
            ApplyVisual();
            RaiseChanged();
        }

        void DeployPlayer()
        {
            if (player == null) return;
            var at = playerBase != null ? playerBase.position : center;
            if (!ArenaGround.TryFindClearSpot(at, at.y, startRadius * 0.8f, out var spot))
            {
                Debug.LogWarning("ZoneManager: no clear ground near the zone centre; leaving the player where it is.", this);
                return;
            }
            if (playerController != null) playerController.enabled = false;
            player.position = spot + Vector3.up * 0.05f;
            if (playerController != null) playerController.enabled = true;
        }

        void Update()
        {
            if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;
            if (GameInput.ConsumeExpandZone()) ExpandZone();
        }

        // LateUpdate so this clamps after PlayerController has already moved the
        // player for this frame.
        void LateUpdate()
        {
            if (player == null) return;

            var shape = Shape;
            if (shape.Contains(player.position)) return;

            Vector3 clamped = shape.ClampInside(player.position);

            if (playerController != null) playerController.enabled = false;
            player.position = clamped;
            if (playerController != null) playerController.enabled = true;
        }

        public void ExpandZone()
        {
            if (!CanExpand) return;

            if (PlayerCurrency.Instance == null || !PlayerCurrency.Instance.TrySpendCoins(expandCost))
            {
                GameEvents.RaiseZoneExpandFailed();
                return;
            }

            CurrentRadius = Mathf.Min(maxRadius, CurrentRadius + expandStep);
            ApplyVisual(animate: true);
            RaiseChanged();
        }

        void RaiseChanged() => GameEvents.RaiseZoneChanged(CurrentRadius, maxRadius, expandCost);

        void ApplyVisual(bool animate = false)
        {
            if (boundaryVisual == null) return;
            if (boundaryVisual.TryGetComponent<ZoneBoundaryFX>(out var fx))
            {
                fx.SetShape(Forward, straightEdge: true);
                fx.SetZone(center, CurrentRadius, animate);
                return;
            }
            boundaryVisual.position = new Vector3(center.x, boundaryVisual.position.y, center.z);
            float diameter = CurrentRadius * 2f;
            var scale = boundaryVisual.localScale;
            boundaryVisual.localScale = new Vector3(diameter, scale.y, diameter);
        }
    }
}
