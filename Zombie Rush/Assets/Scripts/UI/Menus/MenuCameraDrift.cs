using UnityEngine;

namespace ZombieRush.UI
{
    /// Slow handheld-style sway for the main menu camera, so the backdrop feels alive.
    public class MenuCameraDrift : MonoBehaviour
    {
        public Vector3 positionAmplitude = new Vector3(0.12f, 0.06f, 0f);
        public Vector3 rotationAmplitude = new Vector3(0.6f, 1.2f, 0.3f);
        public float speed = 0.25f;

        Vector3 basePos;
        Quaternion baseRot;

        void Awake()
        {
            basePos = transform.localPosition;
            baseRot = transform.localRotation;
        }

        void Update()
        {
            float t = Time.unscaledTime * speed;
            transform.localPosition = basePos + new Vector3(
                Mathf.Sin(t * 1.1f) * positionAmplitude.x,
                Mathf.Sin(t * 1.7f) * positionAmplitude.y,
                0f);
            transform.localRotation = baseRot * Quaternion.Euler(
                Mathf.Sin(t * 1.3f) * rotationAmplitude.x,
                Mathf.Sin(t * 0.9f) * rotationAmplitude.y,
                Mathf.Sin(t * 0.7f) * rotationAmplitude.z);
        }
    }
}
