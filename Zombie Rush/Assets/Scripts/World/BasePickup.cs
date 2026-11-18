using System.Collections.Generic;
using UnityEngine;
using ZombieRush.Core;
using ZombieRush.Data;
using ZombieRush.Player;

namespace ZombieRush.World
{
    public enum PickupKind { Weapon, Armor, Ammo, Medkit }

    /// Gear in the safe base: walk into it to take it. Only the local player picks things up on this machine
    /// (each player's own game handles them).
    ///   - Weapon on a gun table (takeOnce): the gun is gone from the table for the whole match, for the whole squad
    ///     (MatchNet tells the others). A player who already has that gun leaves it there.
    ///   - Armour vest (oncePerLife): never disappears; every player can take one per life.
    ///   - Old-style beam pickups (armour/ammo/medkit) recharge after `rechargeTime`.
    /// Visual: `display` (the model on the table), or the older glowing beam + floating icon.
    [RequireComponent(typeof(SphereCollider))]
    public class BasePickup : MonoBehaviour
    {
        public PickupKind kind = PickupKind.Armor;
        [Tooltip("Weapons: the weapon handed out (must be in the player's weapon list).")]
        public WeaponData weapon;
        [Tooltip("Armor / medkit points.")]
        public float amount = 50f;
        [Tooltip("Seconds until it can be used again. 0 = never runs out.")]
        public float rechargeTime = 25f;
        public string label = "ARMOR";

        [Header("Base tables")]
        [Tooltip("Network id, the same on every machine (set by Base Furnish). -1 = not synced.")]
        public int id = -1;
        [Tooltip("A gun on a table: taken, it leaves the table for the whole squad and comes back after regrabDelay, " +
                 "so every player can take one (a player who already has the gun leaves it there).")]
        public bool takeOnce;
        [Tooltip("Seconds until a taken table gun is back on the table (0 = gone for good).")]
        public float regrabDelay = 3f;
        [Tooltip("Each player can take it once per life; it never disappears (the armour vest).")]
        public bool oncePerLife;
        [Tooltip("The model shown while it can be taken.")]
        public GameObject display;

        [Header("Visual (beam pickups)")]
        public Transform icon;      // floating sprite, faces the camera
        public ZoneBoundaryFX beam;
        public float bobHeight = 0.12f;

        static readonly Dictionary<int, BasePickup> byId = new Dictionary<int, BasePickup>();

        float readyAt;
        bool taken;
        int takenLife = -1;
        Vector3 iconBase;
        Camera cam;
        SphereCollider trigger;

        bool Ready => !taken && Time.time >= readyAt;

        void Reset()
        {
            var col = GetComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 1.1f;
            col.center = Vector3.up * 0.9f;
        }

        void Awake()
        {
            trigger = GetComponent<SphereCollider>();
            trigger.isTrigger = true;
            if (icon != null) iconBase = icon.localPosition;
        }

        void OnEnable() { if (id >= 0) byId[id] = this; }
        void OnDisable() { if (id >= 0 && byId.TryGetValue(id, out var p) && p == this) byId.Remove(id); }

        /// A teammate took this gun (MatchNet): it goes from the table here too.
        public static void MarkTaken(int id)
        {
            if (byId.TryGetValue(id, out var p) && p != null) p.Take();
        }

        /// The id's pickup comes back after a short while (its regrabDelay), or 0 if it is gone for good.
        public static float RegrabDelayOf(int id) => byId.TryGetValue(id, out var p) && p != null ? p.regrabDelay : 0f;

        float backAt = -1f;

        void Take()
        {
            taken = true;
            backAt = takeOnce && regrabDelay > 0f ? Time.time + regrabDelay : -1f;
            if (display != null) display.SetActive(false);
            if (trigger != null) trigger.enabled = false;
        }

        void Update()
        {
            if (taken && backAt > 0f && Time.time >= backAt)
            {
                // back on the table for the players who don't have this gun yet
                taken = false;
                backAt = -1f;
                if (display != null) display.SetActive(true);
                if (trigger != null) trigger.enabled = true;
            }
            bool ready = Ready;
            if (display != null && !taken && display.activeSelf != ready) display.SetActive(ready);
            if (icon != null)
            {
                if (icon.gameObject.activeSelf != ready) icon.gameObject.SetActive(ready);
                if (ready)
                {
                    icon.localPosition = iconBase + Vector3.up * Mathf.Sin(Time.time * 2.2f) * bobHeight;
                    if (cam == null) cam = Camera.main;
                    if (cam != null)
                    {
                        var look = icon.position - cam.transform.position;
                        look.y = 0f;
                        if (look.sqrMagnitude > 0.001f) icon.rotation = Quaternion.LookRotation(look);
                    }
                }
            }
            if (beam != null && beam.gameObject.activeSelf != ready) beam.gameObject.SetActive(ready);
        }

        void OnTriggerStay(Collider other)
        {
            if (!Ready) return;
            var player = other.GetComponentInParent<PlayerHealth>();
            if (player == null || player != PlayerHealth.Instance || player.IsDead) return;
            if (oncePerLife && takenLife == player.Life) return;

            string gained = TryGive(player);
            if (gained == null) return;

            if (oncePerLife) takenLife = player.Life;
            else if (takeOnce)
            {
                Take();
                if (id >= 0 && Net.MatchNet.Instance != null) Net.MatchNet.Instance.ReportPickupTaken(id);
            }
            else if (rechargeTime > 0f) readyAt = Time.time + rechargeTime;

            if (kind == PickupKind.Weapon) GameEvents.RaiseNotice(label, gained);
            else UI.PickupToast.Show(gained, kind == PickupKind.Armor ? new Color(0.55f, 0.8f, 1f) : (Color?)null);
            var lib = Audio.SoundManager.Library;
            if (lib != null)
                Audio.SoundManager.Play(kind == PickupKind.Weapon ? lib.WeaponPickup : kind == PickupKind.Ammo ? lib.AmmoPickup : lib.GearPickup,
                                        kind == PickupKind.Weapon ? 0.95f : 0.85f, 0.03f);
        }

        /// What the player got (for the on-screen message), or null if they didn't need it.
        string TryGive(PlayerHealth player)
        {
            switch (kind)
            {
                case PickupKind.Armor:
                    float armor = player.AddArmor(amount);
                    return armor > 0f ? $"+{Mathf.RoundToInt(armor)} ARMOR" : null;

                case PickupKind.Medkit:
                    if (player.CurrentHealth >= player.maxHealth) return null;
                    float before = player.CurrentHealth;
                    player.Heal(amount);
                    return $"+{Mathf.RoundToInt(player.CurrentHealth - before)} HEALTH";

                case PickupKind.Ammo:
                    var ammoOwner = player.GetComponentInChildren<PlayerWeaponController>();
                    return ammoOwner != null && ammoOwner.RefillAmmo() ? "AMMO REFILLED" : null;

                case PickupKind.Weapon:
                    var owner = player.GetComponentInChildren<PlayerWeaponController>();
                    if (owner == null || weapon == null) return null;
                    // a gun never held before starts with full spare ammo (PlayerInventory); no refill of the others
                    if (owner.Unlock(weapon)) return "WEAPON TAKEN";
                    return null;
            }
            return null;
        }
    }
}
