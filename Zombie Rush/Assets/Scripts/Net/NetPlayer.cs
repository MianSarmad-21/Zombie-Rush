using Unity.Netcode;
using UnityEngine;
using ZombieRush.Player;
using ZombieRush.Weapons;

namespace ZombieRush.Net
{
    /// One per connected player, owned by that player. The owner's own character (the scene's Player)
    /// stays fully local; this object carries its state to everyone else:
    ///   owner     : writes position, facing, look angle, crouch, gun, reload, aim and health every tick,
    ///               and tells the others about each shot;
    ///   everyone else: shows a teammate copy (NetConfig.remotePlayerPrefab) driven by that state.
    /// On the host the copy is also a zombie target: damage to it is sent to the player who owns it.
    public class NetPlayer : NetworkBehaviour
    {
        public struct State : INetworkSerializable, System.IEquatable<State>
        {
            public Vector3 position;
            public float yaw, pitch, aim, health, maxHealth;
            public byte weapon;
            public byte flags;   // 1 crouch, 2 reloading, 4 dead

            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref position); s.SerializeValue(ref yaw); s.SerializeValue(ref pitch);
                s.SerializeValue(ref aim); s.SerializeValue(ref health); s.SerializeValue(ref maxHealth);
                s.SerializeValue(ref weapon); s.SerializeValue(ref flags);
            }

            public bool Equals(State o) => (position - o.position).sqrMagnitude < 1e-6f && Mathf.Abs(yaw - o.yaw) < 0.05f
                && Mathf.Abs(pitch - o.pitch) < 0.05f && Mathf.Abs(aim - o.aim) < 0.01f && Mathf.Abs(health - o.health) < 0.01f
                && Mathf.Abs(maxHealth - o.maxHealth) < 0.01f && weapon == o.weapon && flags == o.flags;
        }

        readonly NetworkVariable<State> state = new NetworkVariable<State>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// Every player in the match (for HUDs and tests).
        public static readonly System.Collections.Generic.List<NetPlayer> All = new System.Collections.Generic.List<NetPlayer>();
        public State Current => state.Value;
        /// The teammate copy shown for this player (null on the owner's own machine).
        public GameObject Copy => copy;

        // owner side
        PlayerViewSwitcher local;
        PlayerController localController;
        PlayerWeaponController localWeapons;
        PlayerHealth localHealth;

        // copy side
        GameObject copy;
        CharacterController copyBody;
        PlayerController copyController;
        PlayerWeaponController copyWeapons;
        PlayerViewSwitcher copyView;
        PlayerHealth copyHealth;
        PlayerBodyLink copyLink;
        bool copyDead;
        float copyHealthShown = -1f;

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            if (IsOwner) { BindLocal(); SpreadOut(); }   // everyone else makes the copy once the owner's first state arrives
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            UnbindLocal();
            if (copy != null) Destroy(copy);
        }

        // ---------------------------------------------------------------- owner

        void BindLocal()
        {
            local = PlayerViewSwitcher.Local;
            if (local == null) return;
            localController = local.GetComponent<PlayerController>();
            localWeapons = local.GetComponent<PlayerWeaponController>();
            localHealth = local.GetComponent<PlayerHealth>();
            if (localWeapons != null)
                for (int i = 0; i < localWeapons.weapons.Length; i++)
                {
                    int idx = i;
                    var w = localWeapons.weapons[i];
                    if (w != null) w.Fired += () => { if (IsSpawned && localWeapons.CurrentIndex == idx) ShotRpc((byte)idx); };
                }
        }

        void UnbindLocal() { local = null; }

        /// Squad members start side by side in the base, not inside each other.
        void SpreadOut()
        {
            if (local == null || OwnerClientId == 0) return;
            var cfg = NetConfig.Current;
            float gap = cfg != null ? cfg.spawnSpacing : 1.6f;
            int slot = (int)(OwnerClientId % 4);
            Vector3 dir = Quaternion.Euler(0f, 90f * slot, 0f) * local.transform.right;
            var cc = local.GetComponent<CharacterController>();
            for (float d = gap; d <= gap * 3f; d += gap * 0.5f)
            {
                Vector3 to = local.transform.position + dir * d;
                Vector3 bottom = to + Vector3.up * 0.5f, top = to + Vector3.up * 1.5f;
                if (Physics.CheckCapsule(bottom, top, 0.35f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (cc != null) cc.enabled = false;
                local.transform.position = to;
                if (cc != null) cc.enabled = true;
                return;
            }
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (IsOwner) WriteState();
            else DriveCopy();
        }

        void WriteState()
        {
            if (local == null) { BindLocal(); if (local == null) return; }
            var gun = localWeapons != null ? localWeapons.CurrentWeapon : null;
            byte flags = 0;
            if (localController != null && localController.IsCrouching) flags |= 1;
            if (gun != null && gun.IsReloading) flags |= 2;
            if (localHealth != null && localHealth.IsDead) flags |= 4;
            var s = new State
            {
                position = local.transform.position,
                yaw = local.transform.eulerAngles.y,
                pitch = localController != null ? localController.Pitch : 0f,
                aim = local.AimAmount,
                health = localHealth != null ? localHealth.CurrentHealth : 100f,
                maxHealth = localHealth != null ? localHealth.maxHealth : 100f,
                weapon = (byte)(localWeapons != null ? localWeapons.CurrentIndex : 0),
                flags = flags,
            };
            if (!s.Equals(state.Value)) state.Value = s;
        }

        /// The owner fired: everyone else plays the shot on its copy.
        [Rpc(SendTo.NotOwner)]
        void ShotRpc(byte weapon)
        {
            if (copyWeapons == null || weapon >= copyWeapons.weapons.Length) return;
            if (copyWeapons.CurrentIndex != weapon) copyWeapons.SelectWeapon(weapon);
            var w = copyWeapons.weapons[weapon];
            if (w != null) w.PlayRemoteShot();
        }

        /// Damage dealt to this player's copy on the host goes to the player itself.
        [Rpc(SendTo.Owner)]
        void DamageRpc(float amount, Vector3 source, bool hasSource)
        {
            if (localHealth == null) BindLocal();
            if (localHealth != null) localHealth.TakeDamage(amount, hasSource ? source : (Vector3?)null);
        }

        // ---------------------------------------------------------------- copy

        void MakeCopy()
        {
            var cfg = NetConfig.Current;
            if (cfg == null || cfg.remotePlayerPrefab == null) { Debug.LogWarning("[Net] no remote player prefab in NetConfig"); return; }
            var s = state.Value;
            copy = Instantiate(cfg.remotePlayerPrefab, s.position, Quaternion.Euler(0f, s.yaw, 0f));
            copy.name = $"Teammate {OwnerClientId}";
            copyBody = copy.GetComponent<CharacterController>();
            copyController = copy.GetComponent<PlayerController>();
            copyWeapons = copy.GetComponent<PlayerWeaponController>();
            copyView = copy.GetComponent<PlayerViewSwitcher>();
            copyHealth = copy.GetComponent<PlayerHealth>();
            copyLink = copy.GetComponent<PlayerBodyLink>();
            if (copyHealth != null) copyHealth.forwardDamage = (amount, source) => { if (IsSpawned) DamageRpc(amount, source ?? Vector3.zero, source.HasValue); };
            if (copyWeapons != null)
                foreach (var w in copyWeapons.weapons) if (w != null) copyWeapons.Unlock(w.data);
        }

        void DriveCopy()
        {
            if (copy == null)
            {
                // wait for the owner's real state (a default one would put the copy at the world origin)
                if (state.Value.maxHealth > 0f) MakeCopy();
                return;
            }
            var cfg = NetConfig.Current;
            var s = state.Value;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);

            // position: chase the latest state smoothly; far behind (or just spawned) = jump there
            Vector3 to = s.position - copy.transform.position;
            if (to.magnitude > (cfg != null ? cfg.snapDistance : 4f))
            {
                if (copyBody != null) copyBody.enabled = false;
                copy.transform.position = s.position;
                if (copyBody != null) copyBody.enabled = true;
            }
            else
            {
                Vector3 step = to * (1f - Mathf.Exp(-(cfg != null ? cfg.followSharpness : 14f) * dt));
                if (copyBody != null && copyBody.enabled) copyBody.Move(step);   // Move, so the body animator sees the speed
                else copy.transform.position += step;
            }
            float yaw = Mathf.LerpAngle(copy.transform.eulerAngles.y, s.yaw, 1f - Mathf.Exp(-20f * dt));
            copy.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            if (copyController != null) copyController.SetRemoteState((s.flags & 1) != 0, s.pitch);
            if (copyView != null) copyView.SetRemoteAim(s.aim);
            if (copyWeapons != null && s.weapon < copyWeapons.weapons.Length && copyWeapons.CurrentIndex != s.weapon) copyWeapons.SelectWeapon(s.weapon);
            var gun = copyWeapons != null ? copyWeapons.CurrentWeapon : null;
            if (gun != null) gun.SetRemoteReload((s.flags & 2) != 0);

            bool dead = (s.flags & 4) != 0;
            if (copyHealth != null) copyHealth.SetRemoteState(s.health, s.maxHealth, dead);
            if (copyLink != null && !Mathf.Approximately(copyHealthShown, s.health))
            {
                if (copyHealthShown >= 0f) copyLink.RemoteHealth(s.health, s.maxHealth);
                copyHealthShown = s.health;
            }
            if (dead && !copyDead && copyLink != null) copyLink.RemoteDied();
            copyDead = dead;
        }
    }
}
