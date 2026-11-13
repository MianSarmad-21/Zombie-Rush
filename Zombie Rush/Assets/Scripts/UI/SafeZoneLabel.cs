using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Data;
using ZombieRush.World;

namespace ZombieRush.UI
{
    /// A floating "SAFE ZONE" sign high over the base building: a small world-space canvas that
    /// turns to face the camera, bobs gently, keeps a readable size from far away and fades out
    /// at long range. Built by Zombie Rush > UI > Build Safe Zone Label into Resources and
    /// spawned by SafeBase, so no scene needs editing.
    public class SafeZoneLabel : MonoBehaviour
    {
        public const string ResourcePath = "ZombieRush/SafeZoneLabel";

        public SafeZoneLabelConfig config;
        public CanvasGroup group;
        public Text title;
        [Tooltip("Height of the canvas in its own units (its RectTransform height).")]
        public float canvasHeight = 260f;

        public static SafeZoneLabel Instance { get; private set; }
        static bool missingWarned;

        Vector3 anchor;
        Camera cam;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; missingWarned = false; }

        /// Puts the label over this base (once). Called by SafeBase when the level starts.
        public static void Ensure(SafeBase safeBase)
        {
            if (Instance != null || safeBase == null || !Application.isPlaying) return;
            var prefab = Resources.Load<SafeZoneLabel>(ResourcePath);
            if (prefab == null)
            {
                if (!missingWarned) Debug.LogWarning($"Zombie Rush: no safe zone label prefab at Resources/{ResourcePath}. Run Zombie Rush > UI > Build Safe Zone Label.");
                missingWarned = true;
                return;
            }
            var label = Instantiate(prefab);
            label.name = prefab.name;
            label.Place(safeBase);
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (title != null && config != null) title.text = config.text;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// Over the middle of the base building's roof, or over the base centre without one.
        void Place(SafeBase safeBase)
        {
            Vector3 top = safeBase.Center;
            if (safeBase.building != null)
            {
                var renderers = safeBase.building.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    var b = renderers[0].bounds;
                    foreach (var r in renderers) b.Encapsulate(r.bounds);
                    top = new Vector3(b.center.x, b.max.y, b.center.z);
                }
            }
            anchor = top + Vector3.up * (config != null ? config.heightAboveRoof : 6f);
            transform.position = anchor;
        }

        void LateUpdate()
        {
            if (config == null) return;
            if (cam == null || !cam.isActiveAndEnabled) cam = Camera.main;
            if (cam == null) { group.alpha = 0f; return; }

            Vector3 pos = anchor + Vector3.up * (Mathf.Sin(Time.time * config.bobSpeed) * config.bobHeight);
            transform.position = pos;

            // A world-space canvas reads correctly when its forward points away from the viewer.
            Vector3 away = pos - cam.transform.position;
            float distance = away.magnitude;
            if (config.uprightOnly) away.y = 0f;
            if (away.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);

            // Normal size in metres, held between a minimum and maximum share of the screen.
            float screenWorldHeight = 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float height = Mathf.Clamp(config.worldHeight, config.minScreenShare * screenWorldHeight, config.maxScreenShare * screenWorldHeight);
            float scale = height / Mathf.Max(1f, canvasHeight);
            transform.localScale = new Vector3(scale, scale, scale);

            float fadeStart = config.maxDistance - Mathf.Max(0.01f, config.fadeBand);
            group.alpha = 1f - Mathf.Clamp01((distance - fadeStart) / Mathf.Max(0.01f, config.fadeBand));
        }
    }
}
