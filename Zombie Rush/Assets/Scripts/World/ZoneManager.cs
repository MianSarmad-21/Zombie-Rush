using UnityEngine;
using ZombieRush.Core;
using ZombieRush.Player;

namespace ZombieRush.World
{
    /// The playable area starts small and grows as the player expands it by
    /// spending coins. The boundary is a hard wall - you physically cannot walk
    /// past it until it's expanded.
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

        [Header("Cost")]
        public int expandCost = 20;

        [Header("Visual")]
        public Transform boundaryVisual; // ZoneBoundaryFX wall, or a plain cylinder scaled to match the current radius

        [Header("Deploy")]
        [Tooltip("Place the player on clear ground at the zone centre when the level starts.")]
        public bool deployPlayerAtCenter = true;

        public float CurrentRadius { get; private set; }
        public bool CanExpand => CurrentRadius < maxRadius;

        CharacterController playerController;
        Transform player;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            CurrentRadius = startRadius;
            if (playerBase != null) center = playerBase.position;
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
            if (!ArenaGround.TryFindClearSpot(center, center.y, startRadius * 0.8f, out var spot))
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

            Vector3 flat = player.position - center;
            flat.y = 0f;
            if (flat.sqrMagnitude <= CurrentRadius * CurrentRadius) return;

            Vector3 clamped = center + flat.normalized * CurrentRadius;
            clamped.y = player.position.y;

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
