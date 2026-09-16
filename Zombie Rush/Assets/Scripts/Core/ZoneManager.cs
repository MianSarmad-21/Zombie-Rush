using UnityEngine;
using ZombieRush.Player;

namespace ZombieRush.Core
{
    /// The playable area starts small and grows as the player expands it by
    /// spending coins. The boundary is a hard wall - you physically cannot walk
    /// past it until it's expanded.
    public class ZoneManager : MonoBehaviour
    {
        public static ZoneManager Instance { get; private set; }

        [Header("Zone")]
        public Vector3 center = new Vector3(500f, 0f, 500f);
        public float startRadius = 30f;
        public float maxRadius = 90f;
        public float expandStep = 15f;

        [Header("Cost")]
        public int expandCost = 20;

        [Header("Visual")]
        public Transform boundaryVisual; // a large translucent cylinder scaled to match the current radius

        [Header("Temporary testing")]
        public KeyCode debugExpandKey = KeyCode.P;

        public float CurrentRadius { get; private set; }
        public System.Action<float, float> OnZoneChanged; // current, max
        public System.Action OnExpandFailed; // not enough coins

        CharacterController playerController;
        Transform player;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            CurrentRadius = startRadius;
        }

        void Start()
        {
            var playerGo = GameObject.FindGameObjectWithTag("Player");
            if (playerGo != null)
            {
                player = playerGo.transform;
                playerController = playerGo.GetComponent<CharacterController>();
            }
            ApplyVisual();
            OnZoneChanged?.Invoke(CurrentRadius, maxRadius);
        }

        void Update()
        {
            if (Input.GetKeyDown(debugExpandKey))
                ExpandZone();
        }

        // LateUpdate so this clamps after PlayerController has already moved the
        // player for this frame.
        void LateUpdate()
        {
            if (player == null) return;

            Vector3 flat = player.position - center;
            flat.y = 0f;
            if (flat.magnitude <= CurrentRadius) return;

            Vector3 clamped = center + flat.normalized * CurrentRadius;
            clamped.y = player.position.y;

            if (playerController != null) playerController.enabled = false;
            player.position = clamped;
            if (playerController != null) playerController.enabled = true;
        }

        public bool CanExpand => CurrentRadius < maxRadius;

        public void ExpandZone()
        {
            if (!CanExpand) return;

            if (PlayerCurrency.Instance == null || !PlayerCurrency.Instance.TrySpendCoins(expandCost))
            {
                OnExpandFailed?.Invoke();
                return;
            }

            CurrentRadius = Mathf.Min(maxRadius, CurrentRadius + expandStep);
            ApplyVisual();
            OnZoneChanged?.Invoke(CurrentRadius, maxRadius);
        }

        void ApplyVisual()
        {
            if (boundaryVisual == null) return;
            float diameter = CurrentRadius * 2f;
            var scale = boundaryVisual.localScale;
            boundaryVisual.localScale = new Vector3(diameter, scale.y, diameter);
        }
    }
}
