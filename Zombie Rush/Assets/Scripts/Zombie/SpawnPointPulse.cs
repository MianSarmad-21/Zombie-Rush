using UnityEngine;

namespace ZombieRush.Zombie
{
    /// Slow pulsing glow on a spawn-point ground marker, so players can see where
    /// zombies are about to come up from.
    public class SpawnPointPulse : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

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
            var mat = rend != null ? rend.sharedMaterial : null;
            if (mat == null || !mat.HasProperty(BaseColorId)) { enabled = false; return; }
            baseColor = mat.GetColor(BaseColorId);
        }

        void Update()
        {
            float t = (Mathf.Sin(Time.time * speed) + 1f) * 0.5f;
            var c = baseColor;
            c.a = Mathf.Lerp(minAlpha, maxAlpha, t);
            block.SetColor(BaseColorId, c);
            rend.SetPropertyBlock(block);
        }
    }
}
