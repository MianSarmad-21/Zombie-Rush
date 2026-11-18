using UnityEngine;
using ZombieRush.Data;
using ZombieRush.Player;

namespace ZombieRush.World
{
    /// An ammo box for one gun. Walk into it to take it (only if that gun is yours and not already full):
    ///   - on a base table: it vanishes and comes back after `respawnTime` seconds;
    ///   - dropped by a zombie (SpawnDrop): it sits on the ground, turning slowly, blinks near the end and is gone
    ///     after `lifetime` seconds.
    /// Each player sees their own boxes (a box taken by a teammate stays for you): ammo is never short in the base.
    [RequireComponent(typeof(SphereCollider))]
    public class AmmoPickup : MonoBehaviour
    {
        public WeaponData weapon;
        [Tooltip("Rounds it gives.")]
        public int amount = 30;
        [Tooltip("Base table: seconds until it is back after being taken. 0 = a one-off drop.")]
        public float respawnTime = 30f;
        [Tooltip("Drops only: seconds before it disappears.")]
        public float lifetime = 40f;
        [Tooltip("The visible box (hidden while it is away).")]
        public GameObject display;

        float readyAt, dieAt = -1f;
        bool isDrop;
        Vector3 displayBase;

        bool Ready => Time.time >= readyAt;

        void Reset()
        {
            var col = GetComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 0.9f;
        }

        void Awake()
        {
            GetComponent<SphereCollider>().isTrigger = true;
            if (display != null) displayBase = display.transform.localPosition;
        }

        /// A zombie's ammo pack on the ground where it fell. Null if the gun has no pack model.
        public static AmmoPickup SpawnDrop(Vector3 position, WeaponData weapon, int amount)
        {
            if (weapon == null || weapon.ammoPack == null) return null;
            if (Physics.Raycast(position + Vector3.up * 1.5f, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore))
                position = hit.point;
            var go = Instantiate(weapon.ammoPack, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            go.name = $"AmmoDrop_{weapon.weaponName}";
            var p = go.GetComponent<AmmoPickup>();
            if (p == null) { Destroy(go); return null; }
            p.weapon = weapon;
            p.amount = amount;
            p.isDrop = true;
            p.respawnTime = 0f;
            p.dieAt = Time.time + p.lifetime;
            go.transform.localScale = Vector3.one * 1.35f;   // a bit bigger than the table boxes: easy to spot
            return p;
        }

        void Update()
        {
            bool ready = Ready;
            if (display != null && display.activeSelf != ready) display.SetActive(ready);
            if (!isDrop || display == null) return;

            // drops: turn and bob a little so they catch the eye, blink in the last 5 seconds
            float t = Time.time;
            display.transform.localRotation = Quaternion.Euler(0f, t * 60f, 0f);
            display.transform.localPosition = displayBase + Vector3.up * (0.12f + Mathf.Sin(t * 2.5f) * 0.05f);
            float left = dieAt - t;
            if (left <= 0f) { Destroy(gameObject); return; }
            if (left < 5f) display.SetActive(Mathf.Repeat(t * (left < 2f ? 8f : 4f), 1f) > 0.3f);
        }

        void OnTriggerStay(Collider other)
        {
            if (!Ready) return;
            var player = other.GetComponentInParent<PlayerHealth>();
            if (player == null || player != PlayerHealth.Instance || player.IsDead) return;   // each player picks up their own
            var owner = player.GetComponentInChildren<PlayerWeaponController>();
            if (owner == null || weapon == null) return;
            int added = owner.AddAmmo(weapon, amount);
            if (added <= 0) return;   // not your gun, or already full: it stays

            ZombieRush.UI.PickupToast.Show($"+{added} {weapon.AmmoLabel} AMMO");
            var lib = Audio.SoundManager.Library;
            if (lib != null) Audio.SoundManager.Play(lib.AmmoPickup, 0.85f, 0.04f);

            if (isDrop || respawnTime <= 0f) { Destroy(gameObject); return; }
            readyAt = Time.time + respawnTime;
        }
    }
}
