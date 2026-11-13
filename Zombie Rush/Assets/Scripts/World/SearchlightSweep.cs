using UnityEngine;

namespace ZombieRush.World
{
    /// Swings a searchlight left and right around its starting direction.
    public class SearchlightSweep : MonoBehaviour
    {
        [Tooltip("Degrees either side of the starting direction.")]
        public float arc = 45f;
        [Tooltip("Full swings per second.")]
        public float speed = 0.08f;

        Quaternion start;
        float phase;

        void Awake()
        {
            start = transform.localRotation;
            phase = Random.value * Mathf.PI * 2f;
        }

        void Update()
        {
            float yaw = Mathf.Sin(Time.time * speed * Mathf.PI * 2f + phase) * arc;
            transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * start;
        }
    }
}
