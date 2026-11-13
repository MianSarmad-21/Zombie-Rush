using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace ZombieRush.Net
{
    public enum NetMode { None, Solo, Host, Client }

    /// Starting and leaving a match. Every match runs on Netcode, so the game has one code path:
    ///   Solo  : a local host with no network at all;
    ///   Host  : a Unity Relay session others join with a short code (internet, phone or PC);
    ///   Client: joins a host's code.
    /// Test switches (command line): -zrhost (direct host on NetConfig.directPort), -zrjoin <ip> (direct join).
    public static class NetSession
    {
        public static NetMode Mode { get; private set; } = NetMode.None;
        /// The code teammates type to join (Host only).
        public static string JoinCode { get; private set; } = "";
        public static bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
        public static bool IsRunning => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        /// Players in the match (1 when solo or not started).
        public static int PlayerCount => IsRunning ? Mathf.Max(1, NetworkManager.Singleton.ConnectedClientsIds.Count) : 1;
        /// True in a squad (pausing does not freeze time, more zombies).
        public static bool InSquad => Mode == NetMode.Host || Mode == NetMode.Client;

        public static event Action<string> Status;
        static ISession session;

        static void Say(string s) { Debug.Log("[Net] " + s); Status?.Invoke(s); }

        /// The NetworkManager (made from code: Unity Transport, the player prefab, every network prefab).
        public static NetworkManager Manager()
        {
            if (NetworkManager.Singleton != null) return NetworkManager.Singleton;
            // a match keeps running when the window loses focus (others are still playing)
            Application.runInBackground = true;
            var cfg = NetConfig.Current;
            var go = new GameObject("NetworkManager");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var transport = go.AddComponent<UnityTransport>();
            var nm = go.AddComponent<NetworkManager>();
            nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                PlayerPrefab = cfg != null ? cfg.netPlayerPrefab : null,
                TickRate = cfg != null ? cfg.tickRate : 30,
                ConnectionApproval = false,
                EnableSceneManagement = true,
            };
            if (cfg != null)
            {
                if (cfg.netPlayerPrefab != null) nm.AddNetworkPrefab(cfg.netPlayerPrefab);
                if (cfg.matchPrefab != null) nm.AddNetworkPrefab(cfg.matchPrefab);
                if (cfg.networkPrefabs != null)
                    foreach (var p in cfg.networkPrefabs) if (p != null) nm.AddNetworkPrefab(p);
            }
            GameManagerHooks();
            nm.OnServerStarted += () =>
            {
                MatchNet.ServerCreate();
                if (nm.SceneManager != null) nm.SceneManager.OnLoadEventCompleted += (_, __, ___, ____) => MatchNet.ServerCreate();
            };
            nm.OnClientDisconnectCallback += id =>
            {
                // the host left (or the connection dropped): back to the first screen
                if (Mode == NetMode.Client && (id == nm.LocalClientId || id == NetworkManager.ServerClientId))
                {
                    Say("Lost the host.");
                    Leave();
                    Core.GameManager.StartInLobby = true;
                    UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
                }
            };
            return nm;
        }

        static void GameManagerHooks()
        {
            Core.GameManager.CanFreezeTime = () => !InSquad;
        }

        public static bool StartSolo()
        {
            var nm = Manager();
            var t = nm.GetComponent<UnityTransport>();
            t.SetConnectionData("127.0.0.1", NetConfig.Current != null ? NetConfig.Current.directPort : (ushort)7777, "127.0.0.1");
            Mode = NetMode.Solo;
            bool ok = nm.StartHost();
            Say(ok ? "Solo started." : "Could not start the game.");
            if (!ok) Mode = NetMode.None;
            return ok;
        }

        /// Direct host (same network or testing on one PC): listens on every address.
        public static bool StartDirectHost()
        {
            var nm = Manager();
            var t = nm.GetComponent<UnityTransport>();
            t.SetConnectionData("0.0.0.0", NetConfig.Current != null ? NetConfig.Current.directPort : (ushort)7777, "0.0.0.0");
            Mode = NetMode.Host;
            bool ok = nm.StartHost();
            JoinCode = "direct";
            Say(ok ? "Hosting directly." : "Could not host.");
            if (!ok) Mode = NetMode.None;
            return ok;
        }

        public static bool JoinDirect(string ip)
        {
            var nm = Manager();
            var t = nm.GetComponent<UnityTransport>();
            t.SetConnectionData(ip, NetConfig.Current != null ? NetConfig.Current.directPort : (ushort)7777);
            Mode = NetMode.Client;
            bool ok = nm.StartClient();
            Say(ok ? $"Joining {ip}..." : "Could not join.");
            if (!ok) Mode = NetMode.None;
            return ok;
        }

        static async Task SignIn()
        {
            // a fresh anonymous player per run, so two copies of the game on one PC are two players
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync(new InitializationOptions().SetProfile("zr" + Guid.NewGuid().ToString("N").Substring(0, 12)));
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        /// Hosts a squad over the internet (Unity Relay). Returns the join code, or null on failure.
        public static async Task<string> HostSquadAsync()
        {
            try
            {
                Manager();
                Say("Connecting to Unity services...");
                await SignIn();
                Say("Creating the squad...");
                var cfg = NetConfig.Current;
                var options = new SessionOptions { MaxPlayers = cfg != null ? cfg.maxPlayers : 4 }.WithRelayNetwork();
                session = await MultiplayerService.Instance.CreateSessionAsync(options);
                Mode = NetMode.Host;
                JoinCode = session.Code;
                Say($"Squad ready. Code: {JoinCode}");
                return JoinCode;
            }
            catch (Exception e)
            {
                Mode = NetMode.None;
                Say("Could not host: " + Friendly(e));
                Debug.LogException(e);
                return null;
            }
        }

        /// Joins a squad by its code. Returns true once connected.
        public static async Task<bool> JoinSquadAsync(string code)
        {
            try
            {
                Manager();
                Say("Connecting to Unity services...");
                await SignIn();
                Say("Joining " + code.Trim().ToUpperInvariant() + "...");
                session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant());
                Mode = NetMode.Client;
                JoinCode = session.Code;
                Say("Joined.");
                return true;
            }
            catch (Exception e)
            {
                Mode = NetMode.None;
                Say("Could not join: " + Friendly(e));
                Debug.LogException(e);
                return false;
            }
        }

        static string Friendly(Exception e)
        {
            string m = e.Message ?? "";
            if (m.Contains("not found", StringComparison.OrdinalIgnoreCase) || m.Contains("404")) return "no squad with that code.";
            if (m.Contains("full", StringComparison.OrdinalIgnoreCase)) return "that squad is full.";
            if (Application.internetReachability == NetworkReachability.NotReachable) return "no internet connection.";
            return m.Length > 120 ? m.Substring(0, 120) : m;
        }

        /// Stops the local match without leaving any online session (solo restart).
        public static void Shutdown()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) NetworkManager.Singleton.Shutdown();
            Mode = NetMode.None;
        }

        /// Host: reload the level on every machine (a new match with the same squad).
        public static void ReloadLevelForAll()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || nm.SceneManager == null) return;
            nm.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }

        public static async void Leave()
        {
            try { if (session != null) await session.LeaveAsync(); } catch (Exception e) { Debug.LogWarning("[Net] leave: " + e.Message); }
            session = null;
            if (NetworkManager.Singleton != null) NetworkManager.Singleton.Shutdown();
            Mode = NetMode.None; JoinCode = "";
        }
    }
}
