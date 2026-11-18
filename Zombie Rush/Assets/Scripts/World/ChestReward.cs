using UnityEngine;
using ZombieRush.Player;

namespace ZombieRush.World
{
    /// Health or armour that came out of a loot chest: walk over it to take it (only if you need it - a full
    /// player leaves it lying). Turns and bobs on the ground, blinks before it is gone after `lifetime` seconds.
    [RequireComponent(typeof(SphereCollider))]
    public class ChestReward : MonoBehaviour
    {
        public enum Kind { Health, Armor }

        public Kind kind = Kind.Health;
        public int amount = 50;
        public float lifetime = 40f;
        [Tooltip("The visible model (turns and bobs).")]
        public Transform display;

        float dieAt;
        Vector3 displayBase;

        void Awake()
        {
            GetComponent<SphereCollider>().isTrigger = true;
            dieAt = Time.time + lifetime;
            if (display != null) displayBase = display.localPosition;
        }

        void Update()
        {
            float t = Time.time;
            if (display != null)
            {
                display.localRotation = Quaternion.Euler(0f, t * 60f, 0f);
                display.localPosition = displayBase + Vector3.up * (0.12f + Mathf.Sin(t * 2.5f) * 0.05f);
                float left = dieAt - t;
                if (left < 5f) display.gameObject.SetActive(Mathf.Repeat(t * (left < 2f ? 8f : 4f), 1f) > 0.3f);
            }
            if (t >= dieAt) Destroy(gameObject);
        }

        void OnTriggerStay(Collider other)
        {
            var player = other.GetComponentInParent<PlayerHealth>();
            if (player == null || player != PlayerHealth.Instance || player.IsDead) return;
            string msg = null;
            Color color = Color.white;
            if (kind == Kind.Health)
            {
                if (player.CurrentHealth >= player.maxHealth) return;
                float before = player.CurrentHealth;
                player.Heal(amount);
                msg = $"+{Mathf.RoundToInt(player.CurrentHealth - before)} HEALTH";
                color = new Color(0.5f, 1f, 0.55f);
            }
            else
            {
                float got = player.AddArmor(amount);
                if (got <= 0f) return;
                msg = $"+{Mathf.RoundToInt(got)} ARMOR";
                color = new Color(0.55f, 0.8f, 1f);
            }
            UI.PickupToast.Show(msg, color);
            var lib = Audio.SoundManager.Library;
            if (lib != null) Audio.SoundManager.Play(lib.GearPickup, 0.85f, 0.04f);
            Destroy(gameObject);
        }
    }
}
