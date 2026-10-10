using UnityEngine;
using UnityEngine.UI;

namespace ZombieRush.UI
{
    /// The game's colour language (see GDD "Art and Video"):
    /// cyan = safety, red = danger, gold = reward, on a desaturated dark base.
    public static class UITheme
    {
        public static readonly Color Ink = Hex(0x07080A);
        public static readonly Color Panel = Hex(0x0C0E12, 0.88f);
        public static readonly Color PanelSoft = Hex(0x0C0E12, 0.62f);
        public static readonly Color Line = new Color(1f, 1f, 1f, 0.08f);

        public static readonly Color Bone = Hex(0xEDE6D8);
        public static readonly Color Muted = Hex(0x8D9099);
        public static readonly Color Dim = Hex(0x4A4E57);

        public static readonly Color Blood = Hex(0xD0201E);
        public static readonly Color BloodDark = Hex(0x4A0707);
        public static readonly Color Cyan = Hex(0x3FD6E8);
        public static readonly Color Gold = Hex(0xF5B83D);

        public static Color Hex(int rgb, float alpha = 1f) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);

        public static Color WithAlpha(this Color c, float a) { c.a = a; return c; }

        /// Resizes a bar by moving its right anchor (works with any sprite/Image type).
        public static void SetBarFill(Image bar, float t01)
        {
            if (bar == null) return;
            t01 = Mathf.Clamp01(t01);
            var rt = bar.rectTransform;
            if (Mathf.Approximately(rt.anchorMax.x, t01)) return;
            rt.anchorMin = new Vector2(0f, rt.anchorMin.y);
            rt.anchorMax = new Vector2(t01, rt.anchorMax.y);
        }

        /// Sets alpha and turns the Graphic off entirely at 0, so invisible
        /// full-screen overlays cost no fill-rate (important on mobile GPUs).
        public static void SetAlpha(Graphic g, float a)
        {
            if (g == null) return;
            var c = g.color;
            if (!Mathf.Approximately(c.a, a)) { c.a = a; g.color = c; }
            bool visible = a > 0.001f;
            if (g.enabled != visible) g.enabled = visible;
        }
    }
}
