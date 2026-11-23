using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Player;
using ZombieRush.Zombie;

namespace ZombieRush.UI.Kit
{
    /// The biomechanical compass: the rose disc turns with the camera so N always points to world north,
    /// the base marker on the rim points to the safe base, and the eyeball on the frame glows red and
    /// twitches when a zombie gets close. Built by Zombie Rush/UI/Install Bio HUD; references are optional.
    public class BioCompass : MonoBehaviour
    {
        [Tooltip("The compass rose disc; rotated so its N faces world north.")]
        public RectTransform dial;
        [Tooltip("Optional marker on the rim pointing to the safe base.")]
        public RectTransform baseMarker;
        [Tooltip("Red glow over the eyeball.")]
        public Image eyeGlow;
        public RectTransform eye;
        [Tooltip("A zombie closer than this (metres) makes the eye glow; full glow at a third of it.")]
        public float dangerRange = 14f;
        [Range(0f, 30f)] public float dialSmoothing = 14f;

        Camera cam;
        float yaw, danger;

        void Update()
        {
            if (cam == null || !cam.isActiveAndEnabled) cam = Camera.main;
            if (cam == null) return;
            float dt = Time.unscaledDeltaTime;
            var view = cam.transform;

            // dial: rotating the UI counter-clockwise by the camera yaw keeps N on world north
            float target = view.eulerAngles.y;
            yaw = dialSmoothing <= 0f ? target : Mathf.LerpAngle(yaw, target, 1f - Mathf.Exp(-dialSmoothing * dt));
            if (dial != null) dial.localEulerAngles = new Vector3(0f, 0f, yaw);

            Vector3 origin = PlayerHealth.Instance != null ? PlayerHealth.Instance.transform.position : view.position;
            Vector3 fwd = Vector3.ProjectOnPlane(view.forward, Vector3.up);

            // base marker: angle between where we look and the safe base
            if (baseMarker != null)
            {
                var sb = World.SafeBase.Instance;
                baseMarker.gameObject.SetActive(sb != null);
                if (sb != null)
                {
                    Vector3 to = Vector3.ProjectOnPlane(sb.transform.position - origin, Vector3.up);
                    if (to.sqrMagnitude > 0.01f && fwd.sqrMagnitude > 0.0001f)
                        baseMarker.localEulerAngles = new Vector3(0f, 0f, -Vector3.SignedAngle(fwd, to, Vector3.up));
                }
            }

            // eye: nearest living zombie
            float nearest = float.MaxValue;
            var list = ZombieHealth.Active;
            for (int i = 0; i < list.Count; i++)
            {
                var z = list[i];
                if (z == null || z.IsDead) continue;
                float d = (z.transform.position - origin).sqrMagnitude;
                if (d < nearest) nearest = d;
            }
            float dist = nearest < float.MaxValue ? Mathf.Sqrt(nearest) : float.MaxValue;
            float targetDanger = Mathf.Clamp01(Mathf.InverseLerp(dangerRange, dangerRange / 3f, dist));
            danger = Mathf.MoveTowards(danger, targetDanger, dt * 3f);
            if (eyeGlow != null)
            {
                float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * Mathf.Lerp(3f, 14f, danger));
                UITheme.SetAlpha(eyeGlow, danger * pulse);
            }
            if (eye != null)
            {
                float twitch = danger > 0.5f ? Mathf.Sin(Time.unscaledTime * 37f) * 2.5f * danger : 0f;
                eye.localEulerAngles = new Vector3(0f, 0f, twitch);
                eye.localScale = Vector3.one * (1f + 0.06f * danger);
            }
        }
    }
}
