using Unity.Netcode;
using UnityEngine;
using ZombieRush.Core;
using ZombieRush.Effects;
using ZombieRush.Zombie;

namespace ZombieRush.Net
{
    /// A zombie in a match. The host runs it (AI, health, death); everyone else mirrors it:
    ///   movement     : NetworkTransform on the same object;
    ///   animation    : walk/run speed as a value, attacks/hits/spits as one-off messages;
    ///   health       : current and max, for the health bars;
    ///   hits         : a player's shot on another machine is sent here and applied by the host;
    ///   death        : sent to everyone (death animation, blood or poison burst); the kill reward goes
    ///                  to the player who landed the killing shot.
    [RequireComponent(typeof(ZombieHealth))]
    public class ZombieNet : NetworkBehaviour
    {
        public const byte AnimAttack = 1, AnimHit = 2, AnimSpit = 3;

        readonly NetworkVariable<float> health = new NetworkVariable<float>();
        readonly NetworkVariable<float> maxHealth = new NetworkVariable<float>();
        readonly NetworkVariable<byte> speed = new NetworkVariable<byte>();
        readonly NetworkVariable<bool> headPopped = new NetworkVariable<bool>();   // before `dead`: set first, read when it dies
        readonly NetworkVariable<bool> dead = new NetworkVariable<bool>();

        ZombieHealth zh;
        /// Who landed the last hit (gets the kill reward). ulong.MaxValue = a turret or nobody.
        public ulong LastAttacker { get; set; } = ulong.MaxValue;

        void Awake()
        {
            zh = GetComponent<ZombieHealth>();
            zh.net = this;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                zh.Anim.networkTap = kind => { if (IsSpawned) AnimRpc(kind); };
                zh.Anim.speedTap = v => { if (IsSpawned) speed.Value = (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f); };
                PushHealth();
                dead.Value = zh.IsDead;
            }
            else
            {
                // mirrors only: no thinking of its own
                var ai = GetComponent<ZombieAI>(); if (ai != null) ai.enabled = false;
                var sp = GetComponent<ZombieSpitter>(); if (sp != null) sp.enabled = false;
                health.OnValueChanged += (_, __) => PullHealth();
                maxHealth.OnValueChanged += (_, __) => PullHealth();
                speed.OnValueChanged += (_, v) => zh.Anim.SetSpeed(v / 255f);
                dead.OnValueChanged += (_, v) => { if (v) zh.NetDie(headPopped.Value); };
                PullHealth();
                zh.Anim.SetSpeed(speed.Value / 255f);
                if (dead.Value) zh.NetDie(headPopped.Value);
                else
                {
                    // just spawned for its rise (still under the ground): the ground breaks open here too
                    var rise = GetComponent<ZombieSpawnRise>();
                    float depth = rise != null ? rise.riseHeight : 2.2f;
                    if (Physics.Raycast(transform.position + Vector3.up * (depth + 0.6f), Vector3.down, out var g, depth + 1.2f, ~0, QueryTriggerInteraction.Ignore)
                        && g.point.y > transform.position.y + depth * 0.5f)
                        SpawnBreachEffect.Play(g.point);
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            zh.Anim.networkTap = null;
            zh.Anim.speedTap = null;
        }

        /// Host: health changed (call after any damage or reset).
        public void PushHealth()
        {
            if (!IsServer || !IsSpawned) return;
            health.Value = zh.CurrentHealth;
            maxHealth.Value = zh.MaxHealth;
        }

        void PullHealth() => zh.SetNetHealth(health.Value, maxHealth.Value);

        /// Host: the zombie died.
        public void ServerDied(ZombieKilledInfo reward)
        {
            if (!IsServer || !IsSpawned) return;
            PushHealth();
            headPopped.Value = zh.LastHitWasHeadshot;   // a killing headshot takes the head off everywhere
            dead.Value = true;
            // the reward (coins, ammo, kill count) goes to whoever landed the killing shot
            ulong killer = LastAttacker;
            if (killer == NetworkManager.ServerClientId || killer == ulong.MaxValue) GameEvents.RaiseZombieKilled(reward);
            else RewardRpc(reward.Position, reward.Coins, reward.Ammo, RpcTarget.Single(killer, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void RewardRpc(Vector3 position, int coins, int ammo, RpcParams p = default)
        {
            GameEvents.RaiseZombieKilled(new ZombieKilledInfo { Position = position, Coins = coins, Ammo = ammo });
        }

        /// A player on another machine hit this zombie.
        public void ClientHit(float amount, Vector3 point, Vector3 direction, bool headshot) => HitRpc(amount, point, direction, headshot);

        [Rpc(SendTo.Server)]
        void HitRpc(float amount, Vector3 point, Vector3 direction, bool headshot, RpcParams p = default)
        {
            if (zh.IsDead) return;
            LastAttacker = p.Receive.SenderClientId;
            zh.TakeHit(amount, false, point, direction, headshot);   // headshot: a longer stagger on the host's zombie
        }

        /// Host: show a hit to everyone else (blood, hurt sound), except the player who fired it (already shown).
        public void BroadcastHit(Vector3 point, Vector3 direction)
        {
            if (IsServer && IsSpawned) HitFxRpc(point, direction, LastAttacker);
        }

        [Rpc(SendTo.NotServer)]
        void HitFxRpc(Vector3 point, Vector3 direction, ulong shooter)
        {
            if (NetworkManager.LocalClientId == shooter) return;
            zh.LocalHitFx(point, direction);
        }

        [Rpc(SendTo.NotServer)]
        void AnimRpc(byte kind)
        {
            switch (kind)
            {
                case AnimAttack: zh.Anim.TriggerAttack(); break;
                case AnimHit: zh.Anim.TriggerHit(); break;
                case AnimSpit: zh.Anim.TriggerSpit(); break;
            }
        }

        /// Host: a poison glob left this zombie's mouth; everyone else sees the same glob (harmless there).
        public void BroadcastGlob(Vector3 from, Vector3 to, float flightTime)
        {
            if (IsServer && IsSpawned) GlobRpc(from, to, flightTime);
        }

        [Rpc(SendTo.NotServer)]
        void GlobRpc(Vector3 from, Vector3 to, float flightTime)
        {
            var sp = GetComponent<ZombieSpitter>();
            if (sp != null && sp.config != null) AcidGlob.Launch(from, to, flightTime, sp.config, transform);
        }

        /// Host: put this zombie into the match (after it is placed for its rise).
        public void ServerSpawn()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
            if (!NetworkObject.IsSpawned) NetworkObject.Spawn(true);
            LastAttacker = ulong.MaxValue;
            headPopped.Value = false;
            dead.Value = false;
            PushHealth();
        }

        /// Host: take it out of the match (back to the pool).
        public void ServerDespawn()
        {
            if (NetworkObject != null && NetworkObject.IsSpawned) NetworkObject.Despawn(false);
        }
    }
}
