using UnityEngine;
using UnityEngine.UI;

namespace ZombieRush.UI.Kit
{
    /// A live ECG trace for the vitals tag: the beat scrolls across the little monitor, faster as health
    /// drops, green -> amber -> red, and a flat line when the player is down. Drawn as one strip of quads.
    [RequireComponent(typeof(CanvasRenderer))]
    public class HeartbeatGraphic : MaskableGraphic
    {
        [Tooltip("Line thickness, canvas units.")]
        public float thickness = 3f;
        [Tooltip("Points across the monitor.")]
        [Range(16, 160)] public int samples = 72;
        [Tooltip("How many beats fit across the monitor at once.")]
        public float beatsShown = 2.2f;

        float health01 = 1f;
        float phase;
        bool dead;

        /// 0..1 health; dead = flat line.
        public void SetHealth(float value01, bool isDead)
        {
            health01 = Mathf.Clamp01(value01);
            dead = isDead;
            var s = KitStyle.Current;
            color = dead || health01 <= s.lowHealth ? s.blood : health01 <= s.hurtHealth ? s.amber : s.radar;
        }

        void Update()
        {
            var s = KitStyle.Current;
            float bpm = Mathf.Lerp(s.bpm.y, s.bpm.x, health01);
            phase += Time.unscaledDeltaTime * bpm / 60f;
            if (phase > 1000f) phase -= 1000f;
            SetVerticesDirty();
        }

        /// One heartbeat shape over x in 0..1 (P wave, QRS spike, T wave).
        static float Beat(float x)
        {
            float p = Mathf.Exp(-Mathf.Pow((x - 0.18f) / 0.035f, 2f)) * 0.12f;
            float q = -Mathf.Exp(-Mathf.Pow((x - 0.30f) / 0.012f, 2f)) * 0.18f;
            float r = Mathf.Exp(-Mathf.Pow((x - 0.33f) / 0.014f, 2f)) * 1.0f;
            float sW = -Mathf.Exp(-Mathf.Pow((x - 0.36f) / 0.014f, 2f)) * 0.3f;
            float t = Mathf.Exp(-Mathf.Pow((x - 0.56f) / 0.06f, 2f)) * 0.22f;
            return p + q + r + sW + t;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            float mid = r.yMin + r.height * 0.42f, amp = r.height * 0.48f;
            var col = (Color32)color;
            Vector2 prev = default;
            for (int i = 0; i <= samples; i++)
            {
                float u = i / (float)samples;
                float b = dead ? 0f : Beat(Mathf.Repeat(u * beatsShown - phase, 1f));
                var pt = new Vector2(r.xMin + u * r.width, mid + b * amp);
                if (i > 0)
                {
                    Vector2 d = (pt - prev).normalized, n = new Vector2(-d.y, d.x) * thickness * 0.5f;
                    // fade the trace in from the left, bright at the newest end (right)
                    var c = col; c.a = (byte)(col.a * Mathf.Lerp(0.15f, 1f, u));
                    int k = vh.currentVertCount;
                    vh.AddVert(prev - n, c, Vector2.zero); vh.AddVert(prev + n, c, Vector2.zero);
                    vh.AddVert(pt + n, c, Vector2.zero); vh.AddVert(pt - n, c, Vector2.zero);
                    vh.AddTriangle(k, k + 1, k + 2); vh.AddTriangle(k, k + 2, k + 3);
                }
                prev = pt;
            }
        }
    }
}
