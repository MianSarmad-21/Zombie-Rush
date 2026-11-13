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
                ApplyZone(zoneRadius.Value);
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
            GameEvents.OnWaveAnnounced -= ServerWaveAnnounced;
            GameEvents.OnWaveStarted -= ServerWaveStarted;
            GameEvents.OnWaveCompleted -= ServerWaveCompleted;
            GameEvents.OnZombiesRemainingChanged -= ServerRemaining;
            GameEvents.OnBattleStarted -= ServerBattleStarted;
            GameEvents.OnZoneChanged -= ServerZoneChanged;
        }

        void ServerZoneChanged(float radius, float max, int cost) => zoneRadius.Value = radius;

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
            ulong who = p.Receive.SenderClientId;
            if (!turrets.TryGetValue(who, out var list)) turrets[who] = list = new System.Collections.Generic.List<Weapons.Turret>();
            Weapons.TurretDeployer.Place(dep.turretPrefab, spot, rot, list, dep.maxActive);
        }

        /// A joined player paid for a bigger zone: the host grows it for everyone.
        public void RequestExpand() => ExpandRpc();

        [Rpc(SendTo.Server)]
        void ExpandRpc()
        {
            var zm = World.ZoneManager.Instance;
            if (zm != null) zm.ExpandPaid();
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
