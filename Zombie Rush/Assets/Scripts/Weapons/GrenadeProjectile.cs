using System.Collections.Generic;
using UnityEngine;
using ZombieRush.Core;
using ZombieRush.Data;
using ZombieRush.Effects;
using ZombieRush.Player;
using ZombieRush.Zombie;

namespace ZombieRush.Weapons
{
    /// A thrown frag grenade: bounces and rolls (Rigidbody), then goes off when its fuse runs out.
    /// The thrower's machine runs the real one (authoritative: it hurts zombies, and the hits travel to the host
    /// through ZombieHealth.TakeHit like gun hits). Teammates' machines fly a copy (GrenadeNet) that only shows
    /// the throw and the blast; the owner's blast spot is sent to them so the copy goes off in the same place.
    /// Every machine checks its own player against every blast (own grenades always, teammates' with friendly fire).
    [RequireComponent(typeof(Rigidbody))]
    public class GrenadeProjectile : MonoBehaviour
    {
        public static readonly List<GrenadeProjectile> All = new List<GrenadeProjectile>();

        [Tooltip("The lever: flies off when the grenade leaves the hand.")]
        public Transform spoon;
        [Tooltip("Pull ring and pin: gone (pulled) on a thrown grenade.")]
        public Transform pin;

        public int NetId { get; private set; }
        public bool Authoritative { get; private set; }
        public bool OwnedByLocal { get; private set; }
        public bool Exploded { get; private set; }

        GrenadeConfig cfg;
        Rigidbody rb;
        float explodeAt;
        float nextBounceSound;
        bool grounded;

        /// Throws a grenade. fuseLeft = seconds until it goes off (the cook already used the rest).
        public static GrenadeProjectile Launch(Vector3 position, Vector3 velocity, float fuseLeft, bool authoritative,
                                               bool ownedByLocal, int netId, Collider[] ignore)
        {
            var cfg = GrenadeConfig.Get();
            if (cfg == null || cfg.projectilePrefab == null) return null;
            var go = Instantiate(cfg.projectilePrefab, position, Quaternion.LookRotation(velocity.sqrMagnitude > 0.01f ? velocity : Vector3.forward) * Quaternion.Euler(90f, 0f, 0f));
            var g = go.GetComponent<GrenadeProjectile>();
            if (ownedByLocal) LastFirstHit = null;
            g.cfg = cfg;
            g.NetId = netId;
            g.Authoritative = authoritative;
            g.OwnedByLocal = ownedByLocal;
            g.explodeAt = Time.time + Mathf.Max(0.05f, fuseLeft);
            g.rb = go.GetComponent<Rigidbody>();
            g.rb.linearVelocity = velocity;
            g.rb.angularVelocity = Random.insideUnitSphere * 9f;
            var mine = go.GetComponentsInChildren<Collider>();
            if (ignore != null)
                foreach (var a in mine) foreach (var b in ignore) if (a != null && b != null) Physics.IgnoreCollision(a, b);
            if (g.pin != null) g.pin.gameObject.SetActive(false);
            g.ThrowSpoon(velocity);
            return g;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void ThrowSpoon(Vector3 velocity)
        {
            if (spoon == null) return;
            var s = spoon;
            s.SetParent(null, true);
            var srb = s.gameObject.AddComponent<Rigidbody>();
            srb.mass = 0.02f;
            srb.linearVelocity = velocity * 0.6f + Random.insideUnitSphere * 2.5f + Vector3.up * 2f;
            srb.angularVelocity = Random.insideUnitSphere * 25f;
            var col = s.gameObject.AddComponent<BoxCollider>();
            col.size = new Vector3(0.012f, 0.06f, 0.01f);
            Destroy(s.gameObject, 4f);
            if (cfg.spoon != null) Audio.SoundManager.PlayAt(cfg.spoon, transform.position, 0.6f, 18f);
        }

        void FixedUpdate()
        {
            if (rb == null) return;
            // once it is rolling on the ground it slows down instead of sliding forever
            rb.linearDamping = grounded ? cfg.rollDrag : 0.05f;
            grounded = false;
        }

        void OnCollisionStay(Collision c) { if (c.contactCount > 0 && c.GetContact(0).normal.y > 0.5f) grounded = true; }

        /// Where it first touched something (tests compare it with the aim arc's landing ring).
        public Vector3? FirstHit { get; private set; }
        public static Vector3? LastFirstHit { get; private set; }

        void OnCollisionEnter(Collision c)
        {
            if (FirstHit == null && c.contactCount > 0) LastFirstHit = FirstHit = c.GetContact(0).point;
            float speed = c.relativeVelocity.magnitude;
            if (speed < 1.2f || Time.time < nextBounceSound || cfg.bounce == null || cfg.bounce.Length == 0) return;
            nextBounceSound = Time.time + 0.09f;
            Audio.SoundManager.PlayAt(cfg.bounce[Random.Range(0, cfg.bounce.Length)], transform.position,
                                      Mathf.Clamp01(speed / 9f) * 0.8f, 22f);
        }

        void Update()
        {
            if (Exploded) return;
            // a teammate's copy waits a moment for the owner's blast spot before going off by itself
            float wait = Authoritative ? 0f : 0.6f;
            if (Time.time >= explodeAt + wait) Explode(transform.position);
        }

        /// The blast: effects for everyone; damage to zombies from the owner's grenade; damage to this machine's player.
        public void Explode(Vector3 at)
        {
            if (Exploded) return;
            Exploded = true;
            transform.position = at;
            Blast(at, cfg, Authoritative, OwnedByLocal || cfg.friendlyFire);
            if (Authoritative && OwnedByLocal) Net.GrenadeNet.SendExplode(NetId, at);
            Destroy(gameObject);
        }

        /// The explosion itself (also used for a grenade that goes off in the hand).
        public static void Blast(Vector3 at, GrenadeConfig cfg, bool hurtZombies, bool hurtLocalPlayer)
        {
            if (cfg == null) return;
            ExplosionEffect.Spawn(at + Vector3.up * 0.2f, cfg.radius * 0.8f);
            ShockwaveEffect.Spawn(at + Vector3.up * 0.08f, cfg.radius, cfg.shockwaveColor, 0.5f, false);
            if (cfg.scorchSize > 0f)
                GroundDecals.Place(at, cfg.scorchSize * Random.Range(0.85f, 1.15f), FxMaterials.Fx("Fx_Puddle", Color.white, false),
                                   new Color(0.05f, 0.04f, 0.035f, 0.9f), 25f, 4f);
            if (cfg.explode != null && cfg.explode.Length > 0)
                Audio.SoundManager.PlayAt(cfg.explode[Random.Range(0, cfg.explode.Length)], at, cfg.explodeVolume, cfg.explodeDistance);
            CameraShake.AddAt(at, cfg.cameraShake, cfg.shakeRange);

            var player = PlayerHealth.Instance;
            if (player != null && !player.IsDead)
            {
                float d = Vector3.Distance(player.transform.position + Vector3.up, at);
                if (d < cfg.shakeRange * 0.5f) Haptics.Tap(Mathf.RoundToInt(cfg.haptics.x * (1f - d / (cfg.shakeRange * 0.5f))), cfg.haptics.y);
            }

            float r = cfg.radius;
            if (hurtZombies)
            {
                var done = new HashSet<ZombieHealth>();
                foreach (var col in Physics.OverlapSphere(at, r, ~0, QueryTriggerInteraction.Ignore))
                {
                    var z = col.GetComponentInParent<ZombieHealth>();
                    if (z == null || z.IsDead || !done.Add(z)) continue;
                    Vector3 chest = z.transform.position + Vector3.up * 1.0f;
                    float d = Vector3.Distance(chest, at);
                    if (d > r || (cfg.wallsBlock && Blocked(at, chest, z.transform))) continue;
                    float k = Mathf.Lerp(1f, cfg.edgeDamage, d / r);
                    Vector3 dir = (chest - at); dir.y = Mathf.Max(dir.y, 0.25f); dir.Normalize();
                    z.TakeHit(cfg.zombieDamage * k, true, chest, dir);
                }
            }
            if (hurtLocalPlayer && player != null && !player.IsDead)
            {
                Vector3 chest = player.transform.position + Vector3.up * 1.1f;
                float d = Vector3.Distance(chest, at);
                if (d < r && !(cfg.wallsBlock && Blocked(at, chest, player.transform)))
                    player.TakeDamage(cfg.playerDamage * Mathf.Lerp(1f, cfg.edgeDamage, d / r), at);
            }
        }

        /// Something solid between the blast and the target (zombies and players don't shield each other).
        static bool Blocked(Vector3 from, Vector3 to, Transform target)
        {
            Vector3 a = from + Vector3.up * 0.25f;
            Vector3 d = to - a;
            foreach (var h in Physics.RaycastAll(a, d.normalized, d.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.transform.IsChildOf(target)) continue;
                if (h.collider.GetComponentInParent<ZombieHealth>() != null) continue;
                if (h.collider.GetComponentInParent<PlayerHealth>() != null) continue;
                if (h.collider.GetComponentInParent<GrenadeProjectile>() != null) continue;
                return true;
            }
            return false;
        }
    }
}
