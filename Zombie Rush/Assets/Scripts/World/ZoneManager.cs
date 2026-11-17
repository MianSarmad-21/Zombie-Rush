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

        [Header("Cost (a shared fund)")]
        [Tooltip("Each expansion costs about the coins the whole team earns over this many waves.")]
        public int wavesPerExpansion = 5;
        [Tooltip("Share of those coins it costs (0.9 = a team that collects most of its coins can expand right after the 5th wave).")]
        [Range(0.3f, 1.5f)] public float costShare = 0.9f;
        [Tooltip("Average coins a zombie kill gives (ZombieHealth min/max coins, 5-15).")]
        public float coinsPerKill = 10f;

        /// Coins paid into the next expansion so far (every player pays in with P; the zone grows when it is full).
        public int Fund { get; private set; }
        /// Expansions bought so far.
        public int Expansions => Mathf.Max(0, Mathf.RoundToInt((CurrentRadius - startRadius) / Mathf.Max(0.01f, expandStep)));

        /// Price of the next expansion: the coins the squad's zombies are worth over the next `wavesPerExpansion`
        /// waves (expansion k pays for waves 5k+1..5k+5), times costShare. More players = more zombies = a bigger
        /// price, so a squad of four pays it together in the same ~5 waves a solo player does alone.
        public int ExpandCost
        {
            get
            {
                var wm = Object.FindAnyObjectByType<ZombieRush.Waves.WaveManager>();
                int baseN = wm != null ? wm.baseZombiesPerWave : 5, extra = wm != null ? wm.extraZombiesPerWave : 2;
                int players = Mathf.Max(1, PlayerHealth.All.Count);
                float mult = Difficulty.ZombieCountMultiplier(players);
                int first = Expansions * wavesPerExpansion + 1;
                float zombies = 0f;
                for (int w = first; w < first + wavesPerExpansion; w++) zombies += Mathf.Ceil((baseN + (w - 1) * extra) * mult);
                return Mathf.Max(10, Mathf.RoundToInt(zombies * coinsPerKill * costShare / 10f) * 10);
            }
        }

        /// Coins still needed for the next expansion.
        public int Remaining => Mathf.Max(0, ExpandCost - Fund);

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
            // the base's start point inside the hall (the clear-spot search from above lands outside: the roof is in the way)
            if (SafeBase.Instance != null && SafeBase.Instance.playerStart != null)
            {
                if (playerController != null) playerController.enabled = false;   // off first: its own capsule isn't "in the way"
                if (SafeBase.Instance.TryGetStart(out var startPos, out var startRot)) player.SetPositionAndRotation(startPos, startRot);
                if (playerController != null) playerController.enabled = true;
                return;
            }
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

        /// P: this player pays all their coins (up to what is still needed) into the zone fund.
        public void ExpandZone()
        {
            if (!CanExpand) return;
            var wallet = PlayerCurrency.Instance;
            int give = wallet != null ? Mathf.Min(wallet.Coins, Remaining) : 0;
            if (give <= 0 || !wallet.TrySpendCoins(give))
            {
                GameEvents.RaiseZoneExpandFailed();
                return;
            }
            int left = Remaining - give;
            if (left > 0) UI.PickupToast.Show($"+{give} TO THE ZONE  ·  {left} LEFT", new Color(1f, 0.82f, 0.35f));

            // a joined player: the host holds the fund and grows the zone for everyone
            if (ZombieRush.Net.NetSession.IsRunning && !ZombieRush.Net.NetSession.IsServer)
            {
                if (ZombieRush.Net.MatchNet.Instance != null) ZombieRush.Net.MatchNet.Instance.RequestContribute(give);
                return;
            }
            Contribute(give);
        }

        /// Host / solo: coins paid into the fund; the zone grows each time the fund covers the price.
        public void Contribute(int coins)
        {
            Fund += Mathf.Max(0, coins);
            while (CanExpand && Fund >= ExpandCost)
            {
                Fund -= ExpandCost;
                ExpandPaid();
            }
            RaiseChanged();
        }

        /// Grows the zone one step (already paid for).
        public void ExpandPaid()
        {
            if (!CanExpand) return;
            CurrentRadius = Mathf.Min(maxRadius, CurrentRadius + expandStep);
            ApplyVisual(animate: true);
            RaiseChanged();
        }

        /// A joined player's machine: the host's fund.
        public void SetNetFund(int fund)
        {
            if (fund == Fund) return;
            Fund = fund;
            RaiseChanged();
        }

        void OnEnable() => GameEvents.OnWaveAnnounced += HandleWave;
        void OnDisable() => GameEvents.OnWaveAnnounced -= HandleWave;
        void HandleWave(int wave) => RaiseChanged();   // squad size may have changed the price

        /// A joined player's machine: the zone size the host has.
        public void SetNetRadius(float radius)
        {
            if (Mathf.Approximately(radius, CurrentRadius)) return;
            CurrentRadius = Mathf.Clamp(radius, startRadius, maxRadius);
            ApplyVisual(animate: true);
            RaiseChanged();
        }

        void RaiseChanged() => GameEvents.RaiseZoneChanged(CurrentRadius, maxRadius, Remaining);   // the HUD shows what is still needed

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
