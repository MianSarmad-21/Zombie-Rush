using UnityEngine;
using ZombieRush.Core;
using ZombieRush.Data;
using ZombieRush.Player;

namespace ZombieRush.World
{
    public enum PickupKind { Weapon, Armor, Ammo, Medkit }

    /// Starting gear in the safe base: walk into it to take it. Weapon racks never run
    /// out (every squad member can grab one); armor, ammo and medkits recharge after use.
    /// Visual: a small glowing beam (ZoneBoundaryFX child) with a floating icon.
    [RequireComponent(typeof(SphereCollider))]
    public class BasePickup : MonoBehaviour
    {
        public PickupKind kind = PickupKind.Armor;
        [Tooltip("Weapon racks: the weapon handed out (must be in the player's weapon list).")]
        public WeaponData weapon;
        [Tooltip("Armor / medkit points.")]
        public float amount = 50f;
        [Tooltip("Seconds until it can be used again. 0 = never runs out (weapon racks).")]
        public float rechargeTime = 25f;
        public string label = "ARMOR";

        [Header("Visual")]
        public Transform icon;      // floating sprite, faces the camera
        public ZoneBoundaryFX beam;
        public float bobHeight = 0.12f;

        float readyAt;
        Vector3 iconBase;
        Camera cam;

        bool Ready => Time.time >= readyAt;

        void Reset()
        {
            var col = GetComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 1.1f;
            col.center = Vector3.up * 0.9f;
        }

        void Awake()
        {
            GetComponent<SphereCollider>().isTrigger = true;
            if (icon != null) iconBase = icon.localPosition;
        }

        void Update()
        {
            bool ready = Ready;
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
            if (player == null || player.IsDead) return;

            string gained = TryGive(player);
            if (gained == null) return;

            if (rechargeTime > 0f) readyAt = Time.time + rechargeTime;
            if (player != PlayerHealth.Instance) return;
            GameEvents.RaiseNotice(label, gained);
            var lib = Audio.SoundManager.Library;
            if (lib != null) Audio.SoundManager.Play(lib.pickup, 0.8f);
        }

        /// What the player got (for the on-screen notice), or null if they didn't need it.
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
                    if (owner.Unlock(weapon)) { owner.RefillAmmo(); return "WEAPON UNLOCKED"; }
                    return null;
            }
            return null;
        }
    }
}
