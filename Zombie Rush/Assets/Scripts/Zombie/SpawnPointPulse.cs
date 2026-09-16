using UnityEngine;

namespace ZombieRush.Zombie
{
    /// Slow pulsing glow on a spawn-point ground marker, so players can see where
    /// zombies are about to come up from.
    public class SpawnPointPulse : MonoBehaviour
    {
        public float minAlpha = 0.25f;
        public float maxAlpha = 0.65f;
        public float speed = 2f;

        Renderer rend;
        MaterialPropertyBlock block;
        Color baseColor;

        void Awake()
        {
            rend = GetComponent<Renderer>();
            block = new MaterialPropertyBlock();
            baseColor = rend.sharedMaterial.GetColor("_BaseColor");
        }

        void Update()
        {
            float t = (Mathf.Sin(Time.time * speed) + 1f) * 0.5f;
            var c = baseColor;
            c.a = Mathf.Lerp(minAlpha, maxAlpha, t);
            rend.GetPropertyBlock(block);
            block.SetColor("_BaseColor", c);
            rend.SetPropertyBlock(block);
        }

        /// Briefly flares brighter right as a wave starts spawning nearby.
        public void Flare()
        {
            // handled by the natural pulse loop for now - kept as an extension point
            // for the wave manager to call without needing to know pulse internals.
        }
    }
}
