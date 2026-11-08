using UnityEngine;

namespace ZombieRush.Zombie
{
    /// Drives the green zombie glow (the "Zombie Rush/Zombie Rim Glow" material the setup tool
    /// adds to every zombie): a slow sickly pulse, a bright flare while frenzied by a Screamer,
    /// and a fade-out when the zombie dies. One property block per renderer, no material copies.
    [RequireComponent(typeof(ZombieHealth))]
    public class ZombieGlow : MonoBehaviour
    {
        [Tooltip("Glow strength while hunting.")]
        public float intensity = 1f;
        [Tooltip("How much the glow breathes in and out (0 = steady).")]
        [Range(0f, 1f)] public float pulse = 0.25f;
        public float pulseSpeed = 1.6f;
        [Tooltip("Glow strength while frenzied by a Screamer's shriek.")]
        public float frenzyIntensity = 2.2f;
        [Tooltip("Seconds for the glow to die away after the zombie does.")]
        public float deathFade = 1.2f;

        static readonly int IntensityId = Shader.PropertyToID("_RimIntensity");

        ZombieHealth health;
        ZombieAI ai;
        Renderer[] renderers;
        MaterialPropertyBlock block;
        float phase;
        float current;
        float deadFor;

        void Awake()
        {
            health = GetComponent<ZombieHealth>();
            ai = GetComponent<ZombieAI>();
            block = new MaterialPropertyBlock();
            var list = new System.Collections.Generic.List<Renderer>();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.HasProperty(IntensityId)) { list.Add(r); break; }
            renderers = list.ToArray();
        }

        void OnEnable()
        {
            phase = Random.value * 10f; // zombies don't pulse in step
            deadFor = 0f;
            current = intensity;
        }

        void Update()
        {
            if (renderers.Length == 0) return;
            phase += Time.deltaTime * pulseSpeed;

            float target;
            if (health.IsDead)
            {
                deadFor += Time.deltaTime;
                target = intensity * Mathf.Clamp01(1f - deadFor / Mathf.Max(0.01f, deathFade));
                current = target;
            }
            else
            {
                bool frenzied = ai != null && ai.FrenzyMultiplier > 1.01f;
                target = (frenzied ? frenzyIntensity : intensity) * (1f + pulse * Mathf.Sin(phase * Mathf.PI * 2f) * 0.5f);
                current = Mathf.MoveTowards(current, target, Time.deltaTime * 4f);
            }

            foreach (var r in renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetFloat(IntensityId, current);
                r.SetPropertyBlock(block);
            }
        }
    }
}
