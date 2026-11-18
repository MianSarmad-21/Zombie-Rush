using Unity.Netcode;
using UnityEngine;
using ZombieRush.Weapons;

namespace ZombieRush.Net
{
    /// Grenades in a squad (on the NetPlayer object, next to NetPlayer): the owner's throws and blasts go to
    /// everyone else, who fly a copy of the grenade from the teammate's hand and set it off where the owner's went
    /// off. Zombie damage needs nothing here: the owner's blast hits zombies through ZombieHealth.TakeHit, which
    /// already sends a joined player's hits to the host.
    public class GrenadeNet : NetworkBehaviour
    {
        static GrenadeNet localOwner;
        int nextId = 1;

        public override void OnNetworkSpawn()
        {
            if (IsOwner) localOwner = this;
        }

        public override void OnNetworkDespawn()
        {
            if (localOwner == this) localOwner = null;
        }

        /// The local player threw: returns the id the copies will use (0 outside a squad).
        public static int SendThrow(Vector3 position, Vector3 velocity, float fuseLeft)
        {
            if (localOwner == null || !localOwner.IsSpawned || !NetSession.InSquad) return 0;
            int id = localOwner.nextId++;
            localOwner.ThrowRpc(id, position, velocity, fuseLeft);
            return id;
        }

        /// The local player's grenade went off here.
        public static void SendExplode(int id, Vector3 at)
        {
            if (id == 0 || localOwner == null || !localOwner.IsSpawned) return;
            localOwner.ExplodeRpc(id, at);
        }

        /// The local player's grenade went off in their hand (cooked too long).
        public static void SendHandBlast(Vector3 at)
        {
            if (localOwner == null || !localOwner.IsSpawned || !NetSession.InSquad) return;
            localOwner.HandBlastRpc(at);
        }

        int CopyId(int id) => (int)(OwnerClientId * 100000UL) + id;

        [Rpc(SendTo.NotOwner)]
        void ThrowRpc(int id, Vector3 position, Vector3 velocity, float fuseLeft)
        {
            var thrower = TeammateThrower();
            if (thrower != null) thrower.PlayRemoteThrow();
            GrenadeProjectile.Launch(position, velocity, fuseLeft, authoritative: false, ownedByLocal: false, CopyId(id),
                                     thrower != null ? thrower.GetComponentsInChildren<Collider>() : null);
        }

        [Rpc(SendTo.NotOwner)]
        void ExplodeRpc(int id, Vector3 at)
        {
            int cid = CopyId(id);
            foreach (var g in GrenadeProjectile.All)
                if (g != null && g.NetId == cid && !g.Exploded) { g.Explode(at); return; }
            // the copy already went off by itself (or never existed): show the blast where it really was
            GrenadeProjectile.Blast(at, Data.GrenadeConfig.Get(), false, Data.GrenadeConfig.Get() != null && Data.GrenadeConfig.Get().friendlyFire);
        }

        [Rpc(SendTo.NotOwner)]
        void HandBlastRpc(Vector3 at)
        {
            var cfg = Data.GrenadeConfig.Get();
            GrenadeProjectile.Blast(at, cfg, false, cfg != null && cfg.friendlyFire);
        }

        /// The teammate copy of this player on this machine (its GrenadeThrower plays the arm swing).
        GrenadeThrower TeammateThrower()
        {
            var np = GetComponent<NetPlayer>();
            var copy = np != null ? np.Copy : null;
            return copy != null ? copy.GetComponentInChildren<GrenadeThrower>() : null;
        }
    }
}
