using UnityEngine;

namespace ZombieRush.World
{
    /// Makes a light flicker: fire (soft, constant wobble) or a failing street lamp (mostly on,
    /// with short drop-outs).
    [RequireComponent(typeof(Light))]
    public class FlickerLight : MonoBehaviour
    {
        public enum Style { Fire, FailingLamp }

        public Style style = Style.Fire;
        [Tooltip("Lowest brightness as a share of the light's normal intensity.")]
        [Range(0f, 1f)] public float minShare = 0.55f;
        [Tooltip("How fast the fire wobbles.")]
        public float speed = 7f;
        [Tooltip("Failing lamp: average seconds between drop-outs.")]
        public float dropEvery = 2.5f;
        [Tooltip("Failing lamp: how long one drop-out lasts, seconds.")]
        public float dropLength = 0.25f;

        Light lamp;
        float baseIntensity, seed, nextDrop, dropUntil;

        void Awake()
        {
            lamp = GetComponent<Light>();
            baseIntensity = lamp.intensity;
            seed = Random.value * 100f;
            nextDrop = Time.time + Random.Range(0.5f, dropEvery * 2f);
        }

        void Update()
        {
            float share;
            if (style == Style.Fire)
            {
                float n = Mathf.PerlinNoise(seed, Time.time * speed);
                share = Mathf.Lerp(minShare, 1f, n);
            }
            else
            {
                if (Time.time >= nextDrop)
                {
                    dropUntil = Time.time + dropLength * Random.Range(0.4f, 1.6f);
                    nextDrop = dropUntil + Random.Range(dropEvery * 0.3f, dropEvery * 1.7f);
                }
                share = Time.time < dropUntil ? (Mathf.PerlinNoise(seed, Time.time * 30f) < 0.5f ? 0f : minShare) : 1f;
            }
            lamp.intensity = baseIntensity * share;
        }
    }
}
