using UnityEngine;
using ZombieRush.Data;

namespace ZombieRush.Effects
{
    /// A bright streak flying from the gun to where the bullet landed (the player's pistol and rifle).
    /// A small pool of line renderers moved by one hidden updater: no allocations per shot.
    public class TracerEffect : MonoBehaviour
    {
        const int PoolSize = 10;

        class Streak { public LineRenderer line; public Vector3 from, dir; public float length, travelled; public bool live; }

        static TracerEffect instance;
        readonly Streak[] streaks = new Streak[PoolSize];
        int next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        /// A tracer from `from` to `to`.
        public static void Fire(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float length = d.magnitude;
            if (length < 1.5f) return;   // too close to see a streak
            if (instance == null)
            {
                var go = new GameObject("Tracers");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<TracerEffect>();
            }
            instance.Launch(from, d / length, length);
        }

        void Launch(Vector3 from, Vector3 dir, float length)
        {
            var s = streaks[next];
            if (s == null) streaks[next] = s = Create();
            next = (next + 1) % PoolSize;
            s.from = from; s.dir = dir; s.length = length; s.travelled = 0f; s.live = true;
            s.line.enabled = true;
            Place(s, FeelConfig.Current);
            enabled = true;
        }

        Streak Create()
        {
            var cfg = FeelConfig.Current;
            var go = new GameObject("Tracer");
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.widthMultiplier = cfg.tracerWidth;
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 0.25f), new Keyframe(1f, 1f));   // thin tail, full head
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = FxMaterials.Fx("Fx_Drop", Color.white, false);
            var c = cfg.tracerColor;
            line.startColor = new Color(c.r, c.g, c.b, 0f);   // tail fades out
            line.endColor = c;
            line.enabled = false;
            return new Streak { line = line };
        }

        void Update()
        {
            var cfg = FeelConfig.Current;
            float step = cfg.tracerSpeed * Time.deltaTime;
            bool any = false;
            foreach (var s in streaks)
            {
                if (s == null || !s.live) continue;
                s.travelled += step;
                if (s.travelled - cfg.tracerLength >= s.length) { s.live = false; s.line.enabled = false; continue; }
                Place(s, cfg);
                any = true;
            }
            if (!any) enabled = false;
        }

        static void Place(Streak s, FeelConfig cfg)
        {
            float head = Mathf.Min(s.travelled + cfg.tracerLength * 0.5f, s.length);
            float tail = Mathf.Clamp(head - cfg.tracerLength, 0f, s.length);
            s.line.SetPosition(0, s.from + s.dir * tail);
            s.line.SetPosition(1, s.from + s.dir * head);
        }
    }
}
