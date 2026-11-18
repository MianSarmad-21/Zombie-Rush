using UnityEngine;
using ZombieRush.Data;
using ZombieRush.Player;
using ZombieRush.Weapons;

namespace ZombieRush.World
{
    /// A grenade lying on the ground (out of a loot chest): walk over it to take it, unless you already carry the
    /// most you can. Turns and bobs, blinks before it is gone. Personal loot, like the chest's other things.
    [RequireComponent(typeof(SphereCollider))]
    public class GrenadePickup : MonoBehaviour
    {
        [Tooltip("The visible model (turns and bobs).")]
        public Transform display;
        public int amount = 1;

        float dieAt, nextFullNote;
        Vector3 displayBase;

        /// A grenade pickup from the config's prefab (null if the grenade isn't set up).
        public static GameObject Spawn(Vector3 position)
        {
            var cfg = GrenadeConfig.Get();
            if (cfg == null || cfg.pickupPrefab == null) return null;
            var go = Instantiate(cfg.pickupPrefab, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            var p = go.GetComponent<GrenadePickup>();
            if (p != null) p.amount = Mathf.Max(1, cfg.pickupAmount);
            return go;
        }

        void Awake()
        {
            GetComponent<SphereCollider>().isTrigger = true;
            var cfg = GrenadeConfig.Get();
            dieAt = Time.time + (cfg != null ? cfg.pickupLifetime : 45f);
            if (display != null) displayBase = display.localPosition;
        }

        void Update()
        {
            float t = Time.time;
            if (display != null)
            {
                display.localRotation = Quaternion.Euler(0f, t * 70f, 0f);
                display.localPosition = displayBase + Vector3.up * (0.14f + Mathf.Sin(t * 2.6f) * 0.05f);
                float left = dieAt - t;
                if (left < 5f) display.gameObject.SetActive(Mathf.Repeat(t * (left < 2f ? 8f : 4f), 1f) > 0.3f);
            }
            if (t >= dieAt) Destroy(gameObject);
        }

        void OnTriggerStay(Collider other)
        {
            var player = other.GetComponentInParent<PlayerHealth>();
            if (player == null || player != PlayerHealth.Instance || player.IsDead) return;
            var thrower = GrenadeThrower.Local;
            if (thrower == null) return;
            int got = thrower.Add(amount);
            if (got <= 0)
            {
                if (Time.time >= nextFullNote) { nextFullNote = Time.time + 3f; UI.PickupToast.Show("GRENADES FULL", new Color(1f, 0.8f, 0.4f)); }
                return;
            }
            UI.PickupToast.Show(got == 1 ? "+1 GRENADE" : $"+{got} GRENADES", new Color(1f, 0.82f, 0.35f));
            var lib = Audio.SoundManager.Library;
            if (lib != null) Audio.SoundManager.Play(lib.GearPickup, 0.85f, 0.04f);
            Destroy(gameObject);
        }
    }
}
