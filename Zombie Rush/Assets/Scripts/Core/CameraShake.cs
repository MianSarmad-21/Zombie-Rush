using UnityEngine;

namespace ZombieRush.Core
{
    /// Short camera shake for heavy hits and footsteps. Added to the main camera on first use.
    /// Each frame it applies a small noise offset in LateUpdate and takes it off again at the
    /// start of the next frame - but only if nothing else moved the camera meanwhile, so it can
    /// never fight the player's mouse-look or crouch.
    [DefaultExecutionOrder(-1000)]
    public class CameraShake : MonoBehaviour
    {
        const float MaxAngle = 3.5f;     // degrees at full trauma
        const float MaxOffset = 0.06f;   // metres at full trauma
        const float Recovery = 1.6f;     // trauma lost per second
        const float Frequency = 22f;

        static CameraShake instance;

        float trauma;
        float seed;
        bool applied;
        Vector3 posOffset;
        Quaternion rotOffset = Quaternion.identity;
        Vector3 leftPos;
        Quaternion leftRot;

        /// Shake from something at `source`: full strength on top of the camera, none beyond `range`.
        public static void AddAt(Vector3 source, float amount, float range)
        {
            var cam = Camera.main;
            if (cam == null || range <= 0f) return;
            float d = Vector3.Distance(cam.transform.position, source);
            if (d >= range) return;
            float k = 1f - d / range;
            var shake = Get(cam);
            shake.trauma = Mathf.Min(1f, shake.trauma + amount * k * k);
        }

        static CameraShake Get(Camera cam)
        {
            if (instance != null && instance.gameObject == cam.gameObject) return instance;
            instance = cam.GetComponent<CameraShake>();
            if (instance == null) instance = cam.gameObject.AddComponent<CameraShake>();
            return instance;
        }

        void Awake() => seed = Random.value * 100f;

        void Update() => Undo();
        void OnDisable() => Undo();

        void LateUpdate()
        {
            if (trauma <= 0f || Time.deltaTime <= 0f) return;
            trauma = Mathf.Max(0f, trauma - Recovery * Time.deltaTime);
            float s = trauma * trauma;
            float t = Time.time * Frequency;
            float N(float o) => Mathf.PerlinNoise(seed + o, t) * 2f - 1f;

            rotOffset = Quaternion.Euler(MaxAngle * s * N(0f), MaxAngle * s * 0.6f * N(10f), MaxAngle * s * 0.8f * N(20f));
            posOffset = new Vector3(N(30f), N(40f), 0f) * (MaxOffset * s);
            transform.localPosition += posOffset;
            transform.localRotation *= rotOffset;
            leftPos = transform.localPosition;
            leftRot = transform.localRotation;
            applied = true;
        }

        void Undo()
        {
            if (!applied) return;
            applied = false;
            if (transform.localPosition == leftPos) transform.localPosition -= posOffset;
            if (transform.localRotation == leftRot) transform.localRotation *= Quaternion.Inverse(rotOffset);
        }
    }
}
