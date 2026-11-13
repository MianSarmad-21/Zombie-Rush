using UnityEngine;

namespace ZombieRush.Net
{
    /// Multiplayer setup: the prefabs the network spawns and connection settings.
    /// Lives at Resources/ZombieRush/Net/NetConfig (built by Zombie Rush > Multiplayer > Build Network Prefabs).
    [CreateAssetMenu(fileName = "NetConfig", menuName = "Zombie Rush/Net Config")]
    public class NetConfig : ScriptableObject
    {
        public const string ResourcePath = "ZombieRush/Net/NetConfig";

        [Tooltip("One per connected player (owner = that player): carries its position, aim, gun and health.")]
        public GameObject netPlayerPrefab;
        [Tooltip("How a teammate looks on everyone else's screen: a copy of the player's body, guns and aim rig, with no input or camera.")]
        public GameObject remotePlayerPrefab;
        [Tooltip("The match object (waves, zombie count) the host spawns when a match starts.")]
        public GameObject matchPrefab;
        [Tooltip("Other objects the host spawns for everyone (zombies, turrets).")]
        public GameObject[] networkPrefabs;

        [Header("Connection")]
        public int maxPlayers = 4;
        [Tooltip("Port for direct (same network / test) connections.")]
        public ushort directPort = 7777;
        [Tooltip("Network updates per second.")]
        public uint tickRate = 30;

        [Tooltip("Distance between squad members where they start in the base, metres.")]
        public float spawnSpacing = 1.6f;

        [Header("Teammates on screen")]
        [Tooltip("How quickly a teammate's copy catches up with where it really is (higher = snappier, lower = smoother).")]
        public float followSharpness = 14f;
        [Tooltip("Further behind than this (metres) and the copy jumps straight there.")]
        public float snapDistance = 4f;

        static NetConfig current;
        public static NetConfig Current
        {
            get
            {
                if (current == null) current = Resources.Load<NetConfig>(ResourcePath);
                return current;
            }
        }
    }
}
