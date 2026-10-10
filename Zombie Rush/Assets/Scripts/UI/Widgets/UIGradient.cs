using UnityEngine;
using UnityEngine.UI;

namespace ZombieRush.UI
{
    /// Vertical colour gradient across a Text/Image's whole mesh (the gold
    /// "MISSION COMPLETE" title look). Add it before any Shadow/Outline so the
    /// shadow copies keep their own colour. Costs nothing per frame: it only
    /// runs when the graphic rebuilds.
    [AddComponentMenu("UI/Effects/Zombie Rush Gradient")]
    public class UIGradient : BaseMeshEffect
    {
        public Color top = UITheme.Hex(0xFFE680);
        public Color middle = UITheme.Hex(0xFFB22E);
        public Color bottom = UITheme.Hex(0xD9480F);
        [Range(0.05f, 0.95f)] public float middlePosition = 0.5f;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;

            var v = new UIVertex();
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                min = Mathf.Min(min, v.position.y);
                max = Mathf.Max(max, v.position.y);
            }

            float height = Mathf.Max(0.0001f, max - min);
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                float t = (v.position.y - min) / height; // 0 bottom, 1 top
                Color c = t < middlePosition
                    ? Color.Lerp(bottom, middle, t / middlePosition)
                    : Color.Lerp(middle, top, (t - middlePosition) / (1f - middlePosition));
                v.color = (Color32)(c * (Color)v.color);
                vh.SetUIVertex(v, i);
            }
        }

        public void Set(Color topColor, Color middleColor, Color bottomColor)
        {
            top = topColor;
            middle = middleColor;
            bottom = bottomColor;
            if (graphic != null) graphic.SetVerticesDirty();
        }
    }
}
