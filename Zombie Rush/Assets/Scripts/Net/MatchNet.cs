using Unity.Netcode;
using UnityEngine;
using ZombieRush.Core;

namespace ZombieRush.Net
{
    /// The match itself, spawned by the host when a match starts. The host's wave loop raises game events
    /// as usual; this passes them on to everyone else, who raise the same events locally so their HUDs,
    /// banners and wave health work unchanged. Late joiners get the current wave and zombie count.
    public class MatchNet : NetworkBehaviour
    {
        public static MatchNet Instance { get; private set; }

        readonly NetworkVariable<int> wave = new NetworkVariable<int>();
        readonly NetworkVariable<int> remaining = new NetworkVariable<int>();
        readonly NetworkVariable<bool> battleStarted = new NetworkVariable<bool>();
        readonly NetworkVariable<float> zoneRadius = new NetworkVariable<float>();
        bool over;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (IsServer)
            {
                GameEvents.OnWaveAnnounced += ServerWaveAnnounced;
                GameEvents.OnWaveStarted += ServerWaveStarted;
                GameEvents.OnWaveCompleted += ServerWaveCompleted;
                GameEvents.OnZombiesRemainingChanged += ServerRemaining;
                GameEvents.OnBattleStarted += ServerBattleStarted;
                GameEvents.OnZoneChanged += ServerZoneChanged;
                var zm = World.ZoneManager.Instance;
                if (zm != null) zoneRadius.Value = zm.CurrentRadius;
            }
            else
            {
                zoneRadius.OnValueChanged += (_, r) => ApplyZone(r);
                zoneFund.OnValueChanged += (_, f) => { var z = World.ZoneManager.Instance; if (z != null) z.SetNetFund(f); };
                { var z0 = World.ZoneManager.Instance; if (z0 != null) z0.SetNetFund(zoneFund.Value); }
                ApplyZone(zoneRadius.Value);
                takenPickups.OnListChanged += OnTakenPickupsChanged;
                foreach (int id in takenPickups) World.BasePickup.MarkTaken(id);   // guns already gone from the tables
                // late join: catch up with the match
                if (battleStarted.Value) GameEvents.RaiseBattleStarted();
                if (wave.Value > 0)
                {
                    GameEvents.RaiseWaveAnnounced(wave.Value);
                    GameEvents.RaiseWaveStarted(wave.Value);
                    GameEvents.RaiseZombiesRemainingChanged(remaining.Value);
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            takenPickups.OnListChanged -= OnTakenPickupsChanged;
            GameEvents.OnWaveAnnounced -= ServerWaveAnnounced;
            GameEvents.OnWaveStarted -= ServerWaveStarted;
            GameEvents.OnWaveCompleted -= ServerWaveCompleted;
            GameEvents.OnZombiesRemainingChanged -= ServerRemaining;
            GameEvents.OnBattleStarted -= ServerBattleStarted;
            GameEvents.OnZoneChanged -= ServerZoneChanged;
        }

        void ServerZoneChanged(float radius, float max, int cost)
        {
            zoneRadius.Value = radius;
            var zm = World.ZoneManager.Instance;
            if (zm != null) zoneFund.Value = zm.Fund;
        }

        static void ApplyZone(float r)
        {
            var zm = World.ZoneManager.Instance;
            if (zm != null && r > 0f) zm.SetNetRadius(r);
        }

        readonly System.Collections.Generic.Dictionary<ulong, System.Collections.Generic.List<Weapons.Turret>> turrets =
            new System.Collections.Generic.Dictionary<ulong, System.Collections.Generic.List<Weapons.Turret>>();

        /// A player placed a turret: the host spawns it for everyone (each player keeps its own limit).
        public void RequestTurret(Vector3 spot, Quaternion rot) => TurretRpc(spot, rot);

        [Rpc(SendTo.Server)]
        void TurretRpc(Vector3 spot, Quaternion rot, RpcParams p = default)
        {
            var dep = Weapons.TurretDeployer.Local;
            if (dep == null || dep.turretPrefab == null) return;
            if (Weapons.TurretDeployer.InBase(spot)) return;   // no turrets inside the safe base (checked on the host too)
            ulong who = p.Receive.SenderClientId;
            if (!turrets.TryGetValue(who, out var list)) turrets[who] = list = new System.Collections.Generic.List<Weapons.Turret>();
            Weapons.TurretDeployer.Place(dep.turretPrefab, spot, rot, list, dep.maxActive);
        }

        /// Guns taken from the base tables (BasePickup ids), kept by the host so late joiners see them gone too.
        readonly NetworkList<int> takenPickups = new NetworkList<int>();

        /// This player took a gun from a table: it goes from the table for everyone.
        public void ReportPickupTaken(int id) => PickupTakenRpc(id);

        [Rpc(SendTo.Server)]
        void PickupTakenRpc(int id)
        {
            if (takenPickups.Contains(id)) return;
            takenPickups.Add(id);
            World.BasePickup.MarkTaken(id);
        }

        void OnTakenPickupsChanged(NetworkListEvent<int> e)
        {
            if (e.Type == NetworkListEvent<int>.EventType.Add) World.BasePickup.MarkTaken(e.Value);
        }

        /// The shared zone fund (coins paid in toward the next expansion), kept by the host.
        readonly NetworkVariable<int> zoneFund = new NetworkVariable<int>();

        /// A joined player paid coins into the zone fund: the host adds them and grows the zone for everyone.
        public void RequestContribute(int coins) => ContributeRpc(coins);

        [Rpc(SendTo.Server)]
        void ContributeRpc(int coins)
        {
            var zm = World.ZoneManager.Instance;
            if (zm != null) zm.Contribute(Mathf.Clamp(coins, 0, 100000));
        }

        void Update()
        {
            // host: the match ends when every player is down
            if (!IsServer || over || !battleStarted.Value || NetPlayer.All.Count == 0) return;
            foreach (var p in NetPlayer.All)
                if ((p.Current.flags & 4) == 0 || p.Current.maxHealth <= 0f) return;
            over = true;
            GameOverRpc();
        }

        [Rpc(SendTo.Everyone)]
        void GameOverRpc()
        {
            if (GameManager.Instance != null) GameManager.Instance.EndMatch();
        }

        void ServerWaveAnnounced(int w) { wave.Value = w; WaveAnnouncedRpc(w); }
        void ServerWaveStarted(int w) => WaveStartedRpc(w);
        void ServerWaveCompleted(int w, float breather) => WaveCompletedRpc(w, breather);
        void ServerRemaining(int n) { remaining.Value = n; RemainingRpc(n); }
        void ServerBattleStarted() { battleStarted.Value = true; BattleStartedRpc(); }

        [Rpc(SendTo.NotServer)] void WaveAnnouncedRpc(int w) => GameEvents.RaiseWaveAnnounced(w);
        [Rpc(SendTo.NotServer)] void WaveStartedRpc(int w) => GameEvents.RaiseWaveStarted(w);
        [Rpc(SendTo.NotServer)] void WaveCompletedRpc(int w, float breather) => GameEvents.RaiseWaveCompleted(w, breather);
        [Rpc(SendTo.NotServer)] void RemainingRpc(int n) => GameEvents.RaiseZombiesRemainingChanged(n);
        [Rpc(SendTo.NotServer)] void BattleStartedRpc() => GameEvents.RaiseBattleStarted();

        /// Host: make the match object (once, when hosting starts).
        public static void ServerCreate()
        {
            var nm = NetworkManager.Singleton;
            var cfg = NetConfig.Current;
            if (nm == null || !nm.IsServer || Instance != null || cfg == null || cfg.matchPrefab == null) return;
            var go = Instantiate(cfg.matchPrefab);
            go.GetComponent<NetworkObject>().Spawn(true);   // gone with the level: a restart makes a fresh one
        }
    }
}
