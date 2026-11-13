using UnityEngine;
using UnityEngine.UI;

namespace ZombieRush.UI.Kit
{
    /// The zone radar: range rings, a sweeping beam with a fading tail, and a solid ring at the safe zone's
    /// current size (its share of the largest zone). Drawn as triangles, redrawn each frame while visible.
    [RequireComponent(typeof(CanvasRenderer))]
    public class RadarGraphic : MaskableGraphic
    {
        [Range(0f, 1f)] public float zone01 = 0.3f;
        public float sweepSpeed = 120f;     // degrees per second
        public float tail = 70f;            // degrees of fading tail behind the beam
        public int rings = 3;
        [Range(12, 96)] public int segments = 48;

        float angle;

        public void SetZone(float value01) { zone01 = Mathf.Clamp01(value01); SetVerticesDirty(); }

        void Update()
        {
            angle = Mathf.Repeat(angle + sweepSpeed * Time.unscaledDeltaTime, 360f);
            SetVerticesDirty();
        }

        void Ring(VertexHelper vh, Vector2 c, float radius, float width, Color32 col)
        {
            int start = vh.currentVertCount;
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                vh.AddVert(c + d * (radius - width * 0.5f), col, Vector2.zero);
                vh.AddVert(c + d * (radius + width * 0.5f), col, Vector2.zero);
                if (i > 0) { int k = start + i * 2; vh.AddTriangle(k - 2, k - 1, k + 1); vh.AddTriangle(k - 2, k + 1, k); }
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            Vector2 c = r.center;
            float R = Mathf.Min(r.width, r.height) * 0.5f - 2f;
            Color32 baseCol = color;
            var faint = baseCol; faint.a = (byte)(baseCol.a * 0.25f);
            for (int i = 1; i <= rings; i++) Ring(vh, c, R * i / rings, 1.5f, faint);
            // the safe zone, solid
            var zone = baseCol; zone.a = (byte)(baseCol.a * 0.9f);
            Ring(vh, c, Mathf.Max(4f, R * zone01), 3f, zone);
            // beam with a fading tail
            int steps = 18;
            int ci = vh.currentVertCount;
            var cc = baseCol; cc.a = (byte)(baseCol.a * 0.5f);
            vh.AddVert(c, cc, Vector2.zero);
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float a = (angle - tail * (1f - t)) * Mathf.Deg2Rad;
                var col = baseCol; col.a = (byte)(baseCol.a * 0.55f * t * t);
                vh.AddVert(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R, col, Vector2.zero);
                if (i > 0) vh.AddTriangle(ci, ci + i, ci + i + 1);
            }
        }
    }
}
