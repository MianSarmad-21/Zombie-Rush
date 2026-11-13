using UnityEngine;
using UnityEngine.UI;

namespace ZombieRush.UI.Kit
{
    /// Tally marks scratched on the duct tape: one stroke per zombie still standing, every fifth struck
    /// through, like counting on a wall. Past `maxMarks` only the number beside it tells the count.
    [RequireComponent(typeof(CanvasRenderer))]
    public class TallyGraphic : MaskableGraphic
    {
        public int count;
        public int maxMarks = 20;
        public float stroke = 4f;
        public float gap = 9f;
        public float groupGap = 12f;

        public void SetCount(int value)
        {
            if (value == count) return;
            count = Mathf.Max(0, value);
            SetVerticesDirty();
        }

        void Line(VertexHelper vh, Vector2 a, Vector2 b, Color32 c)
        {
            Vector2 d = (b - a).normalized, n = new Vector2(-d.y, d.x) * stroke * 0.5f;
            int k = vh.currentVertCount;
            vh.AddVert(a - n, c, Vector2.zero); vh.AddVert(a + n, c, Vector2.zero);
            vh.AddVert(b + n, c, Vector2.zero); vh.AddVert(b - n, c, Vector2.zero);
            vh.AddTriangle(k, k + 1, k + 2); vh.AddTriangle(k, k + 2, k + 3);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            int n = Mathf.Min(count, maxMarks);
            float x = r.xMin + stroke, top = r.yMax - 2f, bottom = r.yMin + 2f;
            var c = (Color32)color;
            var rnd = new System.Random(7);
            for (int i = 0; i < n; i++)
            {
                if (i > 0 && i % 5 == 0) x += groupGap;
                if (i % 5 == 4)
                {
                    // the fifth: struck diagonally through the four before it
                    float x0 = x - gap * 4f - stroke, x1 = x + stroke;
                    Line(vh, new Vector2(x0, bottom + 4f), new Vector2(x1, top - 4f), c);
                }
                else
                {
                    float wob = (float)rnd.NextDouble() * 3f - 1.5f;
                    Line(vh, new Vector2(x + wob, bottom), new Vector2(x - wob, top), c);
                }
                x += gap;
                if (x > r.xMax) break;
            }
        }
    }
}
