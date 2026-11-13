using System.Collections;
using UnityEngine;
using ZombieRush.Audio;
using ZombieRush.Data;

namespace ZombieRush.Zombie
{
    /// The poison zombie's belly bursts when it dies (timed to its Death clip): goo chunks and droplets fly
    /// out, a gas cloud that hurts forms, and a goo splat stays on the ground.
    [RequireComponent(typeof(ZombieHealth))]
    public class ToxicBurst : MonoBehaviour
    {
        public PoisonConfig config;
        ZombieHealth health;

        void Awake()
        {
            health = GetComponent<ZombieHealth>();
            health.bleedsOnDeath = false;   // it bursts into goo and gas instead of a pool of blood
        }
        void OnEnable() { if (health != null) health.Died += OnDied; }
        void OnDisable() { if (health != null) health.Died -= OnDied; }

        void OnDied(ZombieHealth h)
        {
            if (config != null) StartCoroutine(Burst());
        }

        IEnumerator Burst()
        {
            yield return new WaitForSeconds(config.burstDelay);
            Vector3 belly = transform.position + Vector3.up * 0.9f;
            PoisonFx.Burst(belly, config.acidColor);
            var lib = SoundManager.Library;
            if (lib != null && lib.gooSplat != null) SoundManager.PlayAt(lib.gooSplat, belly, 1f, 30f);
            ToxicZone.Spawn(transform.position, config.cloudRadius, config.cloudDuration, config.cloudDamagePerSecond, config, true);
        }
    }
}
