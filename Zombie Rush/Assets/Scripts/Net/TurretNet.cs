using Unity.Netcode;
using UnityEngine;
using ZombieRush.Weapons;

namespace ZombieRush.Net
{
    /// A turret in a match. The host's turret aims and shoots; everyone else's copy rises at the same spot,
    /// turns its head the same way and shows the same shots (tracer, flash, sound), then sinks when it does.
    [RequireComponent(typeof(Turret))]
    public class TurretNet : NetworkBehaviour
    {
        readonly NetworkVariable<Vector3> ground = new NetworkVariable<Vector3>();
        readonly NetworkVariable<Quaternion> facing = new NetworkVariable<Quaternion>();
        readonly NetworkVariable<float> yaw = new NetworkVariable<float>();

        Turret turret;
        public float Yaw => yaw.Value;

        void Awake()
        {
            turret = GetComponent<Turret>();
            turret.net = this;
        }

        /// Host: where it stands (set before spawning).
        public void ServerSetup(Vector3 at, Quaternion rot) { ground.Value = at; facing.Value = rot; }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) turret.Deploy(ground.Value, facing.Value);
        }

        /// Host: the head turned.
        public void PushYaw(float value) { if (IsServer && IsSpawned && Mathf.Abs(Mathf.DeltaAngle(value, yaw.Value)) > 0.2f) yaw.Value = value; }

        public void BroadcastShot(int muzzle, Vector3 end) { if (IsServer && IsSpawned) ShotRpc((byte)muzzle, end); }

        [Rpc(SendTo.NotServer)]
        void ShotRpc(byte muzzle, Vector3 end) => turret.ShotFx(muzzle, end);

        public void BroadcastSink() { if (IsServer && IsSpawned) SinkRpc(); }

        [Rpc(SendTo.NotServer)]
        void SinkRpc() => turret.Retire();

        /// Host: it has sunk away; remove it everywhere.
        public void ServerRemove() { if (IsServer && NetworkObject.IsSpawned) NetworkObject.Despawn(true); }
    }
}
