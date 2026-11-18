using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using ZombieRush.Net;
using ZombieRush.Player;

namespace ZombieRush.EditorTools
{
    /// Multiplayer assets (Assets/Resources/ZombieRush/Net):
    ///   - RemotePlayer.prefab: a copy of the scene's Player (body, guns, aim rig) with no input, camera,
    ///     coins or turret placing: how a teammate looks on everyone else's screen. Rebuild it whenever the
    ///     player's body or guns change.
    ///   - NetPlayer.prefab: the per-player network object (NetworkObject + NetPlayer).
    ///   - NetConfig.asset: both prefabs plus every prefab the host spawns for everyone.
    /// Marker: Temp/ZR_RunMapCleanup with "net".
    public static class NetSetupTools
    {
        const string Folder = "Assets/Resources/ZombieRush/Net";

        [MenuItem("Zombie Rush/Multiplayer/Build Network Prefabs", priority = 50)]
        static void Menu() => Debug.Log("Zombie Rush: " + Run());

        public static string Run()
        {
            var player = Object.FindObjectsByType<PlayerViewSwitcher>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(v => !v.remote && v.gameObject.scene.IsValid());
            if (player == null) return "Net: FAILED, no Player in the open scene.";

            // ---- teammate copy
            var copy = Object.Instantiate(player.gameObject);
            copy.name = "RemotePlayer";
            var removed = new List<string>();
            foreach (var t in new[] { typeof(PlayerCurrency), typeof(Weapons.TurretDeployer) })
                foreach (var c in copy.GetComponentsInChildren(t, true)) { removed.Add(t.Name); Object.DestroyImmediate(c); }
            foreach (var c in copy.GetComponentsInChildren<AudioListener>(true)) { removed.Add("AudioListener"); Object.DestroyImmediate(c); }
            foreach (var c in copy.GetComponentsInChildren<Camera>(true)) { c.enabled = false; c.tag = "Untagged"; }
            copy.GetComponent<PlayerController>().enabled = false;
            copy.GetComponent<PlayerViewSwitcher>().remote = true;
            copy.GetComponent<PlayerWeaponController>().remote = true;
            string remotePath = $"{Folder}/RemotePlayer.prefab";
            var remotePrefab = PrefabUtility.SaveAsPrefabAsset(copy, remotePath);
            Object.DestroyImmediate(copy);

            // ---- network player
            string netPath = $"{Folder}/NetPlayer.prefab";
            var netGo = new GameObject("NetPlayer");
            netGo.AddComponent<NetworkObject>();
            netGo.AddComponent<NetPlayer>();
            netGo.AddComponent<GrenadeNet>();   // grenade throws and blasts for teammates
            var netPrefab = PrefabUtility.SaveAsPrefabAsset(netGo, netPath);
            Object.DestroyImmediate(netGo);

            // ---- match object
            string matchPath = $"{Folder}/MatchNet.prefab";
            var matchGo = new GameObject("MatchNet");
            matchGo.AddComponent<NetworkObject>();
            matchGo.AddComponent<MatchNet>();
            var matchPrefab = PrefabUtility.SaveAsPrefabAsset(matchGo, matchPath);
            Object.DestroyImmediate(matchGo);

            // ---- zombies: a network object, server-driven movement, and the zombie's network side
            var zombieNotes = new List<string>();
            foreach (var path in AssetDatabase.FindAssets("t:Prefab Zombie_", new[] { "Assets/Prefabs" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                if (path.Contains("/Legacy/")) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (root.GetComponent<Zombie.ZombieHealth>() == null) continue;
                    if (root.GetComponent<NetworkObject>() == null) root.AddComponent<NetworkObject>();
                    var nt = root.GetComponent<Unity.Netcode.Components.NetworkTransform>() ?? root.AddComponent<Unity.Netcode.Components.NetworkTransform>();
                    nt.SyncPositionX = nt.SyncPositionY = nt.SyncPositionZ = true;
                    nt.SyncRotAngleX = false; nt.SyncRotAngleY = true; nt.SyncRotAngleZ = false;
                    nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
                    nt.Interpolate = true;
                    nt.InLocalSpace = false;
                    if (root.GetComponent<ZombieNet>() == null) root.AddComponent<ZombieNet>();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    zombieNotes.Add(System.IO.Path.GetFileNameWithoutExtension(path));
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }

            // ---- turret: a network object with its network side (it moves itself, so no NetworkTransform)
            foreach (var path in AssetDatabase.FindAssets("t:Prefab Turret", new[] { "Assets/Prefabs" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (root.GetComponent<Weapons.Turret>() == null) continue;
                    if (root.GetComponent<NetworkObject>() == null) root.AddComponent<NetworkObject>();
                    if (root.GetComponent<TurretNet>() == null) root.AddComponent<TurretNet>();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    zombieNotes.Add(System.IO.Path.GetFileNameWithoutExtension(path));
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }

            // ---- config
            string cfgPath = $"{Folder}/NetConfig.asset";
            var cfg = AssetDatabase.LoadAssetAtPath<NetConfig>(cfgPath);
            if (cfg == null) { cfg = ScriptableObject.CreateInstance<NetConfig>(); AssetDatabase.CreateAsset(cfg, cfgPath); }
            cfg.netPlayerPrefab = netPrefab;
            cfg.remotePlayerPrefab = remotePrefab;
            cfg.matchPrefab = matchPrefab;
            cfg.networkPrefabs = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !p.Contains("/Legacy/"))
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(g => g != null && g.GetComponent<NetworkObject>() != null)
                .ToArray();
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();
            return $"Net: RemotePlayer from '{player.name}' (removed {string.Join(", ", removed.Distinct())}), NetPlayer, MatchNet, zombies networked [{string.Join(", ", zombieNotes)}], NetConfig with {cfg.networkPrefabs.Length} spawnable prefabs.";
        }
    }
}
