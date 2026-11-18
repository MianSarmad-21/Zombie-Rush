using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Data;

namespace ZombieRush.UI
{
    /// Red markers round the crosshair pointing at whatever just hurt the player (top = in front, bottom = behind):
    /// a glowing crescent, thick in the middle and tapering to sharp tips, with a small chevron pointing out
    /// towards the attacker, as modern shooters draw it. The picture is drawn in code once (smooth edges at any
    /// screen size, no art file). Up to four at once; a new hit from about the same side refreshes that marker.
    /// Made on first use on its own overlay canvas (sized by screen height); it turns itself off when idle.
    public class DamageIndicatorUI : MonoBehaviour
    {
        const int Count = 4;
        const float SameSideDegrees = 35f;

        // the drawing, in texture pixels: a slice of a ring of radius R round the screen centre, which lies
        // below the picture; the crescent's middle sits at CentreY up the picture
        const int TexW = 640, TexH = 200;
        const float R = 720f, CentreY = 0.38f * TexH, HalfSweep = 0.42f, MaxThick = 72f, Glow = 26f, Outline = 3.5f;

        class Marker { public RectTransform rt; public Image image; public Vector3 source; public float age = float.MaxValue, strength; }

        static DamageIndicatorUI instance;
        static Sprite sprite;
        readonly Marker[] markers = new Marker[Count];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        /// The player was hurt by something at `source`; `damage` makes bigger hits flash harder.
        public static void Show(Vector3 source, float damage = 15f)
        {
            if (instance == null) Create();
            instance.Add(source, damage);
        }

        static void Create()
        {
            var go = new GameObject("DamageIndicators", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;   // sized by the screen height on every aspect ratio
            instance = go.AddComponent<DamageIndicatorUI>();
            instance.Build();
        }

        void Build()
        {
            var cfg = FeelConfig.Current;
            if (sprite == null) sprite = MakeSprite();
            float radius = 1080f * cfg.indicatorRadius;   // canvas units from the screen centre to the crescent
            float k = radius / R;                          // texture pixels -> canvas units
            for (int i = 0; i < Count; i++)
            {
                var go = new GameObject("Marker" + i, typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(transform, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(TexW * k, TexH * k);
                // the pivot is the screen centre (far below the picture), so turning the rect swings the marker round it
                rt.pivot = new Vector2(0.5f, -(R - CentreY) / TexH);
                rt.anchoredPosition = Vector2.zero;
                var img = go.AddComponent<Image>();
                img.sprite = sprite;
                img.raycastTarget = false;
                img.color = new Color(1f, 1f, 1f, 0f);
                markers[i] = new Marker { rt = rt, image = img };
            }
        }

        /// The crescent with its glow and chevron, white-tinted reds on transparent, smooth (anti-aliased) edges.
        static Sprite MakeSprite()
        {
            var px = new Color32[TexW * TexH];
            float cx = TexW * 0.5f, cy = CentreY - R;   // screen centre in texture pixels
            var core = new Color(1f, 0.22f, 0.14f);     // bright inner edge
            var deep = new Color(0.78f, 0.02f, 0.02f);  // outer edge of the stroke
            var glow = new Color(0.55f, 0f, 0f);
            float chevronBase = CentreY + MaxThick * 0.5f + 8f, chevronTip = chevronBase + 44f, chevronHalf = 36f;
            var rim = new Color(0.1f, 0f, 0f);          // thin dark outline: readable over the red damage flash
            for (int y = 0; y < TexH; y++)
                for (int x = 0; x < TexW; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float rho = Mathf.Sqrt(dx * dx + dy * dy);
                    float theta = Mathf.Atan2(dx, dy);                  // 0 = straight up the picture
                    float u = Mathf.Clamp01(Mathf.Abs(theta) / HalfSweep);
                    float half = 0.5f * MaxThick * Mathf.Pow(1f - u * u, 0.8f);   // tapers to sharp tips
                    float d = rho - R;                                  // + = outward
                    float edge = Mathf.Abs(d) - half;
                    float a = half > 0.2f ? Mathf.Clamp01(0.5f - edge) : 0f;        // anti-aliased stroke
                    float o = half > 0.2f ? Mathf.Clamp01(Outline + 0.5f - edge) : 0f;   // stroke + its dark rim
                    float beyond = Mathf.Max(0f, edge - Outline);
                    float g = half > 0.2f ? Mathf.Exp(-beyond * beyond / (Glow * Glow)) * 0.5f * (1f - u) : 0f;
                    // chevron: a small triangle pointing outward (towards the attacker) above the middle
                    float cyy = y + 0.5f, cxx = Mathf.Abs(x + 0.5f - cx);
                    float c = 0f;
                    if (cyy > chevronBase - 1f && cyy < chevronTip + 1f)
                    {
                        float allowed = chevronHalf * (chevronTip - cyy) / (chevronTip - chevronBase);
                        c = Mathf.Clamp01(Mathf.Min(allowed - cxx + 0.5f, cyy - chevronBase + 0.5f));
                    }
                    float t = Mathf.Clamp01((d + half) / Mathf.Max(0.01f, 2f * half));   // 0 inner edge .. 1 outer edge
                    Color stroke = Color.Lerp(core, deep, t);
                    float alpha = Mathf.Max(Mathf.Max(a, c), Mathf.Max(o * 0.9f, g));
                    Color col = Color.Lerp(glow, rim, o);                // glow, then the dark rim
                    col = Color.Lerp(col, c > a ? core : stroke, Mathf.Max(a, c));
                    px[y * TexW + x] = new Color32((byte)(col.r * 255), (byte)(col.g * 255), (byte)(col.b * 255), (byte)(Mathf.Clamp01(alpha) * 255));
                }
            var tex = new Texture2D(TexW, TexH, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, name = "DamageIndicator" };
            tex.SetPixels32(px);
            tex.Apply(true, true);   // mipmaps for small screens, then drop the CPU copy
            return Sprite.Create(tex, new Rect(0, 0, TexW, TexH), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        void Add(Vector3 source, float damage)
        {
            float angle = AngleTo(source);
            float window = FeelConfig.Current.indicatorTime;
            Marker pick = null;
            foreach (var m in markers)
                if (m.age < window && Mathf.Abs(Mathf.DeltaAngle(AngleTo(m.source), angle)) < SameSideDegrees) { pick = m; break; }
            if (pick == null)
            {
                pick = markers[0];
                foreach (var m in markers) if (m.age > pick.age) pick = m;   // the oldest (or unused) one
            }
            pick.source = source;
            pick.age = 0f;
            pick.strength = Mathf.Clamp(damage / 20f, 0.6f, 1.4f);
            enabled = true;
        }

        /// Degrees clockwise from straight ahead to `source`, seen from the camera, flat on the ground.
        static float AngleTo(Vector3 source)
        {
            var cam = Camera.main;
            if (cam == null) return 0f;
            Vector3 to = source - cam.transform.position; to.y = 0f;
            Vector3 fwd = cam.transform.forward; fwd.y = 0f;
            if (to.sqrMagnitude < 1e-4f || fwd.sqrMagnitude < 1e-4f) return 0f;
            return Vector3.SignedAngle(fwd, to, Vector3.up);
        }

        void Update()
        {
            var cfg = FeelConfig.Current;
            bool any = false;
            foreach (var m in markers)
            {
                if (m.age >= cfg.indicatorTime) continue;
                m.age += Time.unscaledDeltaTime;   // slow motion must not keep it on screen
                float k = 1f - Mathf.Clamp01(m.age / cfg.indicatorTime);
                float alpha = cfg.indicatorColor.a * Mathf.Min(1f, k * 2.5f) * Mathf.Clamp01(m.strength);   // holds, then fades
                m.image.color = new Color(1f, 1f, 1f, alpha);   // the picture carries its own reds
                m.rt.localEulerAngles = new Vector3(0f, 0f, -AngleTo(m.source));   // follows the attacker as the player turns
                float pop = 1f + 0.1f * m.strength * Mathf.Clamp01(1f - m.age / 0.12f);   // punches outward on the hit
                m.rt.localScale = new Vector3(pop, pop, 1f);
                any = true;
            }
            if (!any)
            {
                foreach (var m in markers) m.image.color = new Color(1f, 1f, 1f, 0f);
                enabled = false;
            }
        }
    }
}
