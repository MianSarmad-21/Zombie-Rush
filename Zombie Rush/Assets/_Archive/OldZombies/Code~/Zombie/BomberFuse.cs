using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZombieRush.Audio;
using ZombieRush.Core;
using ZombieRush.Data;
using ZombieRush.Effects;
using ZombieRush.Player;

namespace ZombieRush.Zombie
{
    /// Bomber: a walking time bomb. Its chest light blinks slowly while it hunts; once a player
    /// is within triggerRange it lights the fuse, beeps faster and faster, runs at them and
    /// explodes, hurting players and zombies around it. Shot dead, it still goes off.
    [RequireComponent(typeof(ZombieAI))]
    public class BomberFuse : MonoBehaviour, IZombieAbility
    {
        public BomberConfig config;
        [Tooltip("The blinking light. Found by name (BombLight) when left empty.")]
        public Renderer bombLight;

        ZombieHealth health;
        ZombieAI ai;
        Renderer[] renderers;
        MaterialPropertyBlock block;
        bool lit, exploded;
        float fuse;
        float blinkTimer;
        bool blinkOn;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly List<ZombieHealth> victims = new List<ZombieHealth>();

        public bool BlocksMelee => true;
        public float SpeedMultiplier => lit ? config.litSpeedMultiplier : 1f;
        public bool IsLit => lit;

        void Awake()
        {
            health = GetComponent<ZombieHealth>();
            ai = GetComponent<ZombieAI>();
            if (config == null) config = ScriptableObject.CreateInstance<BomberConfig>();
            renderers = GetComponentsInChildren<Renderer>(true);
            if (bombLight == null)
                foreach (var r in renderers)
                    if (r.name == "BombLight") { bombLight = r; break; }
            block = new MaterialPropertyBlock();
            health.Died += HandleDied;
        }

        void OnDestroy()
        {
            if (health != null) health.Died -= HandleDied;
        }

        // Pooled zombies come back here: put the body back together and the fuse out.
        void OnEnable()
        {
            lit = false;
            exploded = false;
            fuse = 0f;
            blinkTimer = Random.value * config.blinkIdle;
            if (renderers != null)
                foreach (var r in renderers) if (r != null) r.enabled = true;
            SetLight(false);
        }

        public bool Tick(PlayerHealth target, Vector3 toTarget, float distance)
        {
            if (!lit && distance <= config.triggerRange) Light();
            return false; // never stops: it runs at the player until it blows
        }

        void Light()
        {
            lit = true;
            fuse = config.fuseTime;
            blinkTimer = 0f;
        }

        void Update()
        {
            if (exploded) return;

            // Blink: slow while hunting, faster and faster once lit.
            float interval = lit ? Mathf.Lerp(config.blinkFast, config.blinkIdle * 0.4f, fuse / config.fuseTime) : config.blinkIdle;
            blinkTimer -= Time.deltaTime;
            if (blinkTimer <= 0f)
            {
                blinkOn = !blinkOn;
                blinkTimer = blinkOn ? Mathf.Min(0.08f, interval * 0.5f) : interval;
                SetLight(blinkOn);
                if (blinkOn && lit && !health.IsDead)
                {
                    var lib = SoundManager.Library;
                    if (lib != null) SoundManager.PlayAt(lib.bombBeep, transform.position + Vector3.up * 1.3f, 0.8f, 25f);
                }
            }

            if (!lit || health.IsDead) return;
            fuse -= Time.deltaTime;
            if (fuse <= 0f) Explode(selfDetonated: true);
        }

        void SetLight(bool on)
        {
            if (bombLight == null) return;
            bombLight.GetPropertyBlock(block);
            var c = on ? config.lightColor : config.lightColor * 0.05f;
            block.SetColor(EmissionId, c);
            block.SetColor(BaseColorId, on ? Color.white : new Color(0.3f, 0.05f, 0.05f));
            bombLight.SetPropertyBlock(block);
        }

        void HandleDied(ZombieHealth _)
        {
            if (!exploded && isActiveAndEnabled) StartCoroutine(BlowAfter(config.deathDelay));
        }

        IEnumerator BlowAfter(float delay)
        {
            SetLight(true);
            yield return new WaitForSeconds(delay);
            if (!exploded) Explode(selfDetonated: false);
        }

        void Explode(bool selfDetonated)
        {
            exploded = true;
            Vector3 c = transform.position + Vector3.up * 0.9f;
            float radius = config.radius;

            ExplosionEffect.Spawn(transform.position, radius);
            BloodEffect.Spawn(c);
            var lib = SoundManager.Library;
            if (lib != null) SoundManager.PlayAt(lib.explosion, c, 1f, 70f);
            CameraShake.AddAt(c, config.cameraShake, radius * 4f);

            float scale = ai != null ? ai.difficultyMultiplier : 1f;
            foreach (var p in PlayerHealth.All)
            {
                if (p == null || p.IsDead) continue;
                float d = Vector3.Distance(p.transform.position + Vector3.up, c);
                if (d > radius) continue;
                p.TakeDamage(config.damage * scale * Mathf.Lerp(1f, config.edgeDamage, d / radius));
            }

            // Chain damage to the horde (copied first: kills change the list).
            victims.Clear();
            victims.AddRange(ZombieHealth.Active);
            foreach (var z in victims)
            {
                if (z == null || z == health || z.IsDead) continue;
                float d = Vector3.Distance(z.transform.position, transform.position);
                if (d > radius) continue;
                z.TakeDamage(config.zombieDamage * Mathf.Lerp(1f, 0.3f, d / radius), false);
            }
            victims.Clear();

            foreach (var r in renderers) if (r != null) r.enabled = false; // blown apart
            if (selfDetonated) health.Kill(reward: false);
        }
    }
}
