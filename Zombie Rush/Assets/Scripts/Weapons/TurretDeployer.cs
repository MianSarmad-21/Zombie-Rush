using System.Collections.Generic;
using UnityEngine;
using ZombieRush.Audio;
using ZombieRush.Core;

namespace ZombieRush.Weapons
{
    /// The player's turret ability (paid item, StoreService.Turret). Press T or tap
    /// the HUD turret button: if not bought yet it opens the purchase, otherwise it
    /// places a turret on the ground in front of the player. Cooldown between
    /// placements, at most `maxActive` turrets standing at once.
    public class TurretDeployer : MonoBehaviour
    {
        /// The local player's deployer, for the HUD button.
        public static TurretDeployer Local { get; private set; }

        public Turret turretPrefab;
        public float cooldown = 30f;
        public int maxActive = 2;
        public float placeDistance = 2.6f;

        public bool Owned => StoreService.IsOwned(StoreService.Turret);
        public string Price => StoreService.PriceLabel(StoreService.Turret);
        public float CooldownLeft => Mathf.Max(0f, readyAt - Time.time);
        public float Cooldown01 => cooldown > 0f ? Mathf.Clamp01(CooldownLeft / cooldown) : 0f;
        public int ActiveCount { get { Prune(); return active.Count; } }

        readonly List<Turret> active = new List<Turret>();
        float readyAt;

        void Awake() => Local = this;

        void OnDestroy()
        {
            if (Local == this) Local = null;
        }

        void Update()
        {
            if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;
            if (GameInput.ConsumeTurret()) Use();
        }

        /// What the HUD button does: buy if locked, otherwise place.
        public void Use()
        {
            if (!Owned)
            {
                StoreService.Buy(StoreService.Turret, ok =>
                {
                    if (ok) GameEvents.RaiseNotice("TURRET UNLOCKED", GameSettings.UseTouchControls ? "TAP THE TURRET BUTTON TO DEPLOY" : "PRESS T TO DEPLOY");
                    else Deny("STORE UNAVAILABLE", "TRY AGAIN LATER");
                });
                return;
            }

            if (CooldownLeft > 0f) { Deny("TURRET RECHARGING", $"READY IN {Mathf.CeilToInt(CooldownLeft)}s"); return; }
            if (turretPrefab == null) { Debug.LogWarning("[Zombie Rush] TurretDeployer has no turret prefab - run Zombie Rush > Weapons > Install Guns + Turret."); return; }
            if (!FindSpot(out var spot)) { Deny("NO ROOM HERE", "FACE AN OPEN PIECE OF GROUND"); return; }

            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            var rot = Quaternion.LookRotation(fwd, Vector3.up);
            readyAt = Time.time + cooldown;
            if (ZombieRush.Net.NetSession.IsRunning && ZombieRush.Net.MatchNet.Instance != null)
            {
                // in a match the host places it for everyone
                ZombieRush.Net.MatchNet.Instance.RequestTurret(spot, rot);
                return;
            }
            Place(turretPrefab, spot, rot, active, maxActive);
        }

        /// Puts a turret down at `spot`, retiring the oldest of `owned` past `max`. With a match running
        /// (host only) it is spawned for everyone.
        public static Turret Place(Turret prefab, Vector3 spot, Quaternion rot, List<Turret> owned, int max)
        {
            owned.RemoveAll(t => t == null || t.Expired);
            if (owned.Count >= max) { owned[0].Retire(); owned.RemoveAt(0); }
            var turret = Instantiate(prefab);
            turret.Deploy(spot, rot);
            var tn = turret.GetComponent<ZombieRush.Net.TurretNet>();
            if (tn != null && Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsServer)
            {
                tn.ServerSetup(spot, rot);
                tn.NetworkObject.Spawn(true);
            }
            owned.Add(turret);
            return turret;
        }

        void Deny(string title, string subtitle)
        {
            GameEvents.RaiseNotice(title, subtitle);
            var lib = SoundManager.Library;
            if (lib != null) SoundManager.PlayUI(lib.denied, 0.8f);
        }

        void Prune() => active.RemoveAll(t => t == null || t.Expired);

        /// Open, fairly flat ground a couple of metres ahead (falls back to closer spots).
        bool FindSpot(out Vector3 spot)
        {
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            foreach (float d in new[] { placeDistance, placeDistance * 0.65f, placeDistance * 1.35f })
            {
                Vector3 probe = transform.position + fwd * d + Vector3.up * 2.5f;
                if (!Physics.Raycast(probe, Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (hit.normal.y < 0.8f) continue;
                if (hit.collider.GetComponentInParent<CharacterController>() != null) continue; // a zombie or a player

                // Something solid where the turret would stand?
                Vector3 bottom = hit.point + Vector3.up * 0.45f, top = hit.point + Vector3.up * 1.1f;
                if (Physics.CheckCapsule(bottom, top, 0.4f, ~0, QueryTriggerInteraction.Ignore)) continue;

                spot = hit.point;
                return true;
            }
            spot = default;
            return false;
        }
    }
}
