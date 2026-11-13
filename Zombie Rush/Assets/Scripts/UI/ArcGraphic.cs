using UnityEngine;
using UnityEngine.UI;

namespace ZombieRush.UI
{
    /// A smooth circular arc for the HUD (the shotgun crosshair's brackets). The stroke comes from a small
    /// generated texture laid across the arc's thickness: a white core with a dark outline and soft edges,
    /// so the arc stays clean at any radius instead of breaking into jagged segments.
    [RequireComponent(typeof(CanvasRenderer))]
    public class ArcGraphic : MaskableGraphic
    {
        [Tooltip("Distance from the centre to the middle of the stroke, canvas units.")]
        [SerializeField] float radius = 30f;
        [Tooltip("Stroke width including the outline, canvas units.")]
        [SerializeField] float thickness = 7f;
        [Tooltip("Where the arc's middle points, degrees clockwise from straight up.")]
        [SerializeField] float centreAngle = 45f;
        [Tooltip("How much of the circle the arc covers, degrees.")]
        [SerializeField] float sweep = 60f;
        [Tooltip("Mesh steps per 10 degrees of arc.")]
        [SerializeField] int stepsPer10 = 2;
        [Tooltip("Share of the stroke (each side) that is dark outline.")]
        [Range(0f, 0.45f)] [SerializeField] float outlineShare = 0.26f;

        static Texture2D stroke;
        static float strokeOutline = -1f;

        public float Radius
        {
            get => radius;
            set { if (Mathf.Abs(value - radius) > 0.01f) { radius = value; SetVerticesDirty(); } }
        }

        public void Setup(float r, float width, float centre, float arc, float outline)
        {
            radius = r; thickness = width; centreAngle = centre; sweep = arc; outlineShare = outline;
            SetVerticesDirty(); SetMaterialDirty();
        }

        public override Texture mainTexture
        {
            get
            {
                if (stroke == null || !Mathf.Approximately(strokeOutline, outlineShare)) stroke = MakeStroke(outlineShare);
                return stroke;
            }
        }

        /// The stroke texture. v runs across the stroke; u runs along it only in the end caps (u 1 = full
        /// stroke, u 0 = the cap's tip), so the ends come out round and outlined too.
        static Texture2D MakeStroke(float outline)
        {
            strokeOutline = outline;
            const int N = 32;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "ArcStroke" };
            float core = 1f - 2f * outline;          // share of the full width that is white
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dy = Mathf.Abs((y + 0.5f) / N - 0.5f) * 2f;
                    float dx = Mathf.Clamp01(1f - (x + 0.5f) / (N - 1));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);                 // 0 on the stroke's middle line, 1 at its edge
                    float white = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(core - 0.08f, core + 0.08f, d));
                    float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.82f, 1f, d));
                    t.SetPixel(x, y, Color.Lerp(new Color(0f, 0f, 0f, 0.85f * alpha), new Color(1f, 1f, 1f, alpha), white));
                }
            t.Apply(false, true);
            return t;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            int steps = Mathf.Max(2, Mathf.CeilToInt(sweep / 10f * stepsPer10));
            float half = thickness * 0.5f;
            float inner = Mathf.Max(0f, radius - half), outer = radius + half;
            float a0 = centreAngle - sweep * 0.5f;
            var col = (Color32)color;
            Vector2 Dir(float deg) { float a = deg * Mathf.Deg2Rad; return new Vector2(Mathf.Sin(a), Mathf.Cos(a)); }   // clockwise from up
            for (int i = 0; i <= steps; i++)
            {
                var dir = Dir(a0 + sweep * i / steps);
                vh.AddVert(dir * inner, col, new Vector2(1f, 0f));
                vh.AddVert(dir * outer, col, new Vector2(1f, 1f));
                if (i > 0)
                {
                    int k = i * 2;
                    vh.AddTriangle(k - 2, k - 1, k + 1);
                    vh.AddTriangle(k - 2, k + 1, k);
                }
            }
            // round caps: a square past each end, faded by the texture's cap columns
            foreach (var (deg, sign) in new[] { (a0, -1f), (a0 + sweep, 1f) })
            {
                var dir = Dir(deg);
                var tang = new Vector2(dir.y, -dir.x) * sign;           // along the circle, away from the arc
                int b = vh.currentVertCount;
                vh.AddVert(dir * inner, col, new Vector2(1f, 0f));
                vh.AddVert(dir * outer, col, new Vector2(1f, 1f));
                vh.AddVert(dir * outer + tang * half, col, new Vector2(0f, 1f));
                vh.AddVert(dir * inner + tang * half, col, new Vector2(0f, 0f));
                vh.AddTriangle(b, b + 1, b + 2);
                vh.AddTriangle(b, b + 2, b + 3);
            }
        }
    }
}
