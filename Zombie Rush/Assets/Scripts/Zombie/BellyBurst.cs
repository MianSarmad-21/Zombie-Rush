using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZombieRush.Audio;
using ZombieRush.Core;
using ZombieRush.Effects;
using ZombieRush.Player;

namespace ZombieRush.Zombie
{
    /// The Bloater ("Zombie 6"): waddles straight at the player and never claws. Once a player is within
    /// triggerRange it stops and swells (its Attack clip: arms flung up, arching, convulsing, gurgling, the
    /// body puffing up), and after fuseTime its belly bursts: damage to players and zombies around it, a
    /// blast of gore and goo, a camera shake. Shot dead first, it bursts where it falls after deathDelay,
    /// which still hurts the horde around it, so it is best shot from a distance.
    [RequireComponent(typeof(ZombieHealth))]
    [RequireComponent(typeof(ZombieAI))]
    public class BellyBurst : MonoBehaviour
    {
        [Header("Fuse")]
        [Tooltip("Starts to swell when a player is this close (flat distance between the centres, m).")]
        public float triggerRange = 2.6f;
        [Tooltip("Seconds from starting to swell to the burst (its Attack clip is the swell, 1.2 s).")]
        public float fuseTime = 1.2f;
        [Tooltip("Seconds before one that was shot dead bursts.")]
        public float deathDelay = 0.25f;

        [Header("Burst")]
        public float radius = 4f;
        [Tooltip("Damage to a player at the centre (scaled by the wave's difficulty).")]
        public float damage = 40f;
        [Range(0f, 1f)] [Tooltip("Share of the damage left at the edge of the radius.")]
        public float edgeDamage = 0.3f;
        [Tooltip("Damage to other zombies caught in it.")]
        public float zombieDamage = 90f;
        public float cameraShake = 0.7f;
        public Color gooColor = new Color(0.55f, 0.85f, 0.15f);

        ZombieHealth health;
        ZombieAI ai;
        Renderer[] renderers;
        Transform body;          // the model (Animator), puffed up while it swells
        Vector3 bodyScale;
        bool swelling, burst;
        float fuse, gurgleTimer;
        static readonly List<ZombieHealth> victims = new List<ZombieHealth>();

        public bool IsSwelling => swelling;

        void Awake()
        {
            health = GetComponent<ZombieHealth>();
            ai = GetComponent<ZombieAI>();
            health.bleedsOnDeath = false;    // it bursts instead of leaving a pool of blood
            renderers = GetComponentsInChildren<Renderer>(true);
            var an = GetComponentInChildren<Animator>(true);
            if (an != null) { body = an.transform; bodyScale = body.localScale; }
        }

        // Also runs for a pooled zombie that is reused: whole again, fuse out.
        void OnEnable()
        {
            swelling = false;
            burst = false;
            fuse = 0f;
            if (ai != null) ai.noMelee = true;
            if (renderers != null) foreach (var r in renderers) if (r != null) r.enabled = true;
            if (body != null) body.localScale = bodyScale;
            if (health != null) health.Died += OnDied;
        }

        void OnDisable()
        {
            if (health != null) health.Died -= OnDied;
        }

        /// A joined player's copy of a host zombie: shows the burst, the host deals the damage.
        bool Mirror => health.net != null && health.net.IsSpawned && !health.net.IsServer;

        void Update()
        {
            if (burst || health.IsDead) return;
            if (!GameManager.IsWorldRunning) return;

            if (!swelling)
            {
                if (Mirror) return;
                var target = ai.Target;
                if (target == null || ai.IsGuarding) return;
                Vector3 d = target.position - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude <= triggerRange * triggerRange) StartSwell();
                return;
            }

            gurgleTimer -= Time.deltaTime;
            if (gurgleTimer <= 0f)
            {
                gurgleTimer = Mathf.Lerp(0.12f, 0.35f, fuse / fuseTime);   // faster and faster
                var lib = SoundManager.Library;
                if (lib != null && lib.zombieHurt != null)
                    SoundManager.PlayAt(lib.zombieHurt, transform.position + Vector3.up * 1.2f, 0.7f, 25f, 2);
            }
            fuse -= Time.deltaTime;
            if (fuse <= 0f) Burst(selfDetonated: true);
        }

        void LateUpdate()
        {
            if (!swelling || body == null) return;
            // the whole body puffs up, wobbling faster as it is about to go
            float k = 1f - Mathf.Clamp01(fuse / fuseTime);
            float wobble = Mathf.Sin(Time.time * Mathf.Lerp(14f, 40f, k)) * 0.025f * k;
            body.localScale = bodyScale * (1f + 0.10f * k * k + wobble);
        }

        void StartSwell()
        {
            swelling = true;
            fuse = fuseTime;
            gurgleTimer = 0f;
            ai.Hold(fuseTime + 2f);              // stops and faces the player while it swells
            health.Anim.TriggerAttack();         // its Attack clip is the swell
            var lib = SoundManager.Library;
            if (lib != null) SoundManager.PlayAt(lib.zombieAttack, transform.position + Vector3.up * 1.4f, 1f, 30f, 3);
        }

        void OnDied(ZombieHealth _)
        {
            if (!burst && isActiveAndEnabled) StartCoroutine(BurstAfter(deathDelay));
        }

        IEnumerator BurstAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (!burst) Burst(selfDetonated: false);
        }

        void Burst(bool selfDetonated)
        {
            burst = true;
            swelling = false;
            if (body != null) body.localScale = bodyScale;
            Vector3 c = transform.position + Vector3.up * 0.9f;

            // gore and goo
            PoisonFx.Burst(c, gooColor);
            BloodEffect.SpawnDeath(c, Vector3.up);
            BloodEffect.Spawn(c + Vector3.up * 0.2f, Vector3.forward);
            BloodEffect.Spawn(c + Vector3.up * 0.2f, Vector3.back);
            ShockwaveEffect.Spawn(transform.position + Vector3.up * 0.1f, radius, gooColor, 0.5f, true);
            var lib = SoundManager.Library;
            if (lib != null)
            {
                SoundManager.PlayAt(lib.explosion, c, 0.9f, 60f, 1);
                if (lib.gooSplat != null) SoundManager.PlayAt(lib.gooSplat, c, 1f, 35f);
            }
            CameraShake.AddAt(c, cameraShake, radius * 4f);

            if (!Mirror)
            {
                float scale = ai != null ? ai.difficultyMultiplier : 1f;
                foreach (var p in PlayerHealth.All)
                {
                    if (p == null || p.IsDead) continue;
                    float d = Vector3.Distance(p.transform.position + Vector3.up, c);
                    if (d > radius) continue;
                    p.TakeDamage(damage * scale * Mathf.Lerp(1f, edgeDamage, d / radius), c);
                }
                // the horde around it gets hurt too (copied first: kills change the list)
                victims.Clear();
                victims.AddRange(ZombieHealth.Active);
                foreach (var z in victims)
                {
                    if (z == null || z == health || z.IsDead) continue;
                    float d = Vector3.Distance(z.transform.position, transform.position);
                    if (d > radius) continue;
                    z.TakeDamage(zombieDamage * Mathf.Lerp(1f, 0.3f, d / radius), false);
                }
                victims.Clear();
            }

            foreach (var r in renderers) if (r != null) r.enabled = false;   // blown apart
            if (selfDetonated && !Mirror) health.Kill(reward: false);
        }
    }
}
