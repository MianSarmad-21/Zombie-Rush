using UnityEngine;

namespace ZombieRush.World
{
    /// A line of burning coals across a base gate (built by BaseCompoundTools "gatefires"): the reason the dead
    /// stop at the gates (the safe base keeps them out - ZombieAI / SafeBase - this is what the player sees).
    /// No collider: players walk straight over it, and nothing hurts them inside the base.
    /// Here: the coal bed breathes (its glow rises and sinks slowly, a little differently along each gate).
    public class GateFire : MonoBehaviour
    {
        public Renderer embers;
        [Tooltip("Glow swing: 0.25 = between 75 % and 125 % brightness.")]
        public float pulse = 0.22f;
        public float speed = 0.9f;

        MaterialPropertyBlock block;
        float seed;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            block = new MaterialPropertyBlock();
            seed = Random.value * 100f;
        }

        void Update()
        {
            if (embers == null || !embers.isVisible) return;
            float t = Time.time * speed + seed;
            // two slow waves and a little flutter: coals breathing in the wind
            float k = 1f + pulse * (0.6f * Mathf.Sin(t) + 0.3f * Mathf.Sin(t * 2.3f + 1.7f) + 0.1f * (Mathf.PerlinNoise(t * 3f, seed) * 2f - 1f));
            embers.GetPropertyBlock(block);
            block.SetColor(BaseColor, new Color(k, k, k, 1f));   // premultiplied: scales the glow, the char's darkening (alpha) stays
            embers.SetPropertyBlock(block);
        }
    }
}
