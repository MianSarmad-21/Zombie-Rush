using UnityEngine;

namespace ZombieRush.UI.Kit
{
    /// Slaps a piece onto the screen like tape: it comes in big and turned, then settles flat with a tiny
    /// overshoot. Plays when enabled, or when Play() is called (e.g. a new wave number).
    public class SlapIn : MonoBehaviour
    {
        public float fromScale = 1.35f;
        public float fromAngle = -9f;
        public float restAngle = -1.5f;
        public bool playOnEnable = true;

        RectTransform rt;
        float t = 1f;

        void Awake() => rt = (RectTransform)transform;
        void OnEnable() { if (playOnEnable) Play(); }

        public void Play() { t = 0f; Step(0f); }

        void Update() { if (t < 1f) Step(Time.unscaledDeltaTime); }

        void Step(float dt)
        {
            float dur = Mathf.Max(0.05f, KitStyle.Current.slapTime);
            t = Mathf.Min(1f, t + dt / dur);
            // ease out back: overshoots a hair then settles
            float s = 1.70158f, u = t - 1f;
            float e = 1f + (s + 1f) * u * u * u + s * u * u;
            rt.localScale = Vector3.one * Mathf.LerpUnclamped(fromScale, 1f, e);
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpUnclamped(fromAngle, restAngle, e));
        }
    }
}
