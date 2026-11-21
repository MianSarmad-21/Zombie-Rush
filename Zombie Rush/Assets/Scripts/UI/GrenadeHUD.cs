using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Weapons;

namespace ZombieRush.UI
{
    /// Grenade counter on the HUD (grenade icon + "x2"), dimmed at zero, with a small fuse ring round the icon, and
    /// the fuse timer in the centre of the screen while a grenade is cooked (the crosshair hides meanwhile): a ring
    /// that empties as the fuse burns, the seconds left under it, yellow turning red, pulsing in the last second.
    /// Built by Zombie Rush > Weapons > Set Up Grenade (GrenadeTools).
    public class GrenadeHUD : MonoBehaviour
    {
        public Image icon;
        public Text countText;
        [Tooltip("Radial fill round the counter icon that empties as the fuse runs out while cooking.")]
        public Image fuseRing;
        public Color fullColor = new Color(1f, 0.82f, 0.35f, 1f);
        public Color emptyColor = new Color(1f, 1f, 1f, 0.3f);
        public Color fuseSafe = new Color(1f, 0.85f, 0.3f, 0.95f);
        public Color fuseDanger = new Color(1f, 0.25f, 0.15f, 1f);

        [Header("Centre timer")]
        [Tooltip("The centre timer's root (hidden while not cooking).")]
        public CanvasGroup timerGroup;
        [Tooltip("Faint full ring behind the fuse ring.")]
        public Image timerBack;
        [Tooltip("Radial fill that empties as the fuse burns.")]
        public Image timerRing;
        [Tooltip("Seconds left, one decimal.")]
        public Text timerText;
        [Tooltip("Small dot in the middle (stands in for the crosshair).")]
        public Image timerDot;
        [Tooltip("Pulse size in the last second (1 = none).")]
        public float dangerPulse = 1.12f;

        float timerAlpha;

        void OnEnable()
        {
            GrenadeThrower.CountChanged += Show;
            var t = GrenadeThrower.Local;
            var cfg = Data.GrenadeConfig.Get();
            Show(t != null ? t.Count : 0, cfg != null ? cfg.maxCarry : 0);
            if (timerGroup != null) { timerAlpha = 0f; timerGroup.alpha = 0f; }
        }

        void OnDisable() => GrenadeThrower.CountChanged -= Show;

        void Show(int count, int max)
        {
            if (countText != null) { countText.text = "x" + count; countText.color = count > 0 ? fullColor : emptyColor; }
            if (icon != null) icon.color = count > 0 ? Color.white : emptyColor;
        }

        void Update()
        {
            var t = GrenadeThrower.Local;
            var cfg = Data.GrenadeConfig.Get();
            bool cooking = t != null && t.Cooking && cfg != null && cfg.fuse > 0f;
            float k = cooking ? t.FuseFraction : 0f;
            // yellow while there is time, warming to red as the last second comes (red from then on)
            Color c = fuseSafe;
            if (cooking) c = t.Danger ? fuseDanger : Color.Lerp(fuseDanger, fuseSafe, Mathf.Clamp01((t.FuseLeft - cfg.dangerUnder) / Mathf.Max(0.1f, cfg.fuse * 0.5f)));

            if (fuseRing != null)
            {
                if (fuseRing.enabled != cooking) fuseRing.enabled = cooking;
                if (cooking) { fuseRing.fillAmount = k; fuseRing.color = c; }
            }

            if (timerGroup == null) return;
            bool show = cooking && cfg.centerTimer;
            timerAlpha = Mathf.MoveTowards(timerAlpha, show ? 1f : 0f, Time.unscaledDeltaTime * (show ? 10f : 6f));
            timerGroup.alpha = timerAlpha;
            if (!show) return;
            if (timerRing != null) { timerRing.fillAmount = k; timerRing.color = c; }
            if (timerBack != null) { var b = c; b.a = 0.22f; timerBack.color = b; }
            if (timerDot != null) timerDot.color = t.LandingDanger ? fuseDanger : Color.white;
            if (timerText != null) { timerText.text = t.FuseLeft.ToString("0.0"); timerText.color = c; }
            float s = 1f;
            if (t.Danger) s = 1f + (dangerPulse - 1f) * (0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.PI * 2f * 4f));
            timerGroup.transform.localScale = new Vector3(s, s, 1f);
        }
    }
}
