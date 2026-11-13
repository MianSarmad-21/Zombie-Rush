using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Core;
using ZombieRush.Data;
using ZombieRush.Player;
using ZombieRush.Weapons;

namespace ZombieRush.UI
{
    /// Centre crosshair in the held gun's own shape (WeaponData.crosshair), drawn at the gun's real
    /// shot spread so it opens up while moving or firing and tightens while aiming:
    ///   Lines  (rifle)  : four lines that move apart with the spread;
    ///   Dot    (pistol) : a dot with four short ticks that ease out a little;
    ///   Circle (shotgun): four bold arc brackets round a centre dot, on the pellet cone.
    /// Plus a hit marker that flashes white on a hit and red on a kill. Hidden while paused, dead or
    /// looking through a scope (the scope has its own sight).
    public class CrosshairUI : MonoBehaviour
    {
        public CanvasGroup crosshairGroup;
        public RectTransform hitMarker;
        public Graphic[] hitMarkerGraphics;
        public float fadeSpeed = 5f;

        [Header("Shapes (built by the HUD builder)")]
        public GameObject linesRoot;
        [Tooltip("Top, bottom, left, right: pushed out by the spread.")]
        public RectTransform[] lines;
        public GameObject dotRoot;
        [Tooltip("Top, bottom, left, right ticks round the dot.")]
        public RectTransform[] dotTicks;
        public GameObject circleRoot;
        [Tooltip("Arc brackets on the spread circle.")]
        public ArcGraphic[] ringArcs;

        [Header("Tuning (canvas units on the 1080-high reference screen)")]
        [Tooltip("Closest the rifle lines come to the centre.")]
        public float minLineGap = 8f;
        [Tooltip("Pistol ticks sit this far out at rest.")]
        public float dotTickGap = 10f;
        [Tooltip("Smallest shotgun bracket radius.")]
        public float minRing = 18f;
        [Tooltip("How quickly the drawn spread follows the real one (higher = snappier).")]
        public float follow = 18f;

        float hitAlpha;
        Color hitColor = Color.white;
        float shownSpread = -1f;
        float hideAlpha = 1f;
        bool playing = true;
        PlayerWeaponController weapons;

        void Awake()
        {
            GameEvents.OnEnemyHit += HandleHit;
            GameEvents.OnZombieKilled += HandleKill;
            GameEvents.OnGameStateChanged += HandleState;
            SetHitAlpha(0f);
        }

        void OnDestroy()
        {
            GameEvents.OnEnemyHit -= HandleHit;
            GameEvents.OnZombieKilled -= HandleKill;
            GameEvents.OnGameStateChanged -= HandleState;
        }

        void HandleState(GameState state) => playing = state == GameState.Playing;

        void HandleHit()
        {
            // A kill in the same frame keeps its red colour.
            if (hitAlpha <= 0f || hitColor != UITheme.Blood) hitColor = Color.white;
            Flash(1f);
        }

        void HandleKill(ZombieKilledInfo info)
        {
            hitColor = UITheme.Blood;
            Flash(1.35f);
        }

        void Flash(float scale)
        {
            if (hitMarker != null) hitMarker.localScale = Vector3.one * scale;
            SetHitAlpha(1f);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (hitAlpha > 0f)
            {
                hitAlpha = Mathf.MoveTowards(hitAlpha, 0f, fadeSpeed * dt);
                if (hitMarker != null) hitMarker.localScale = Vector3.Lerp(hitMarker.localScale, Vector3.one, dt * 12f);
                SetHitAlpha(hitAlpha);
            }

            var view = PlayerViewSwitcher.Local;
            if (weapons == null && view != null) weapons = view.TryGetComponent(out PlayerWeaponController own) ? own : view.GetComponentInChildren<PlayerWeaponController>(true);
            var gun = weapons != null ? weapons.CurrentWeapon : null;
            bool scoped = view != null && view.Scoped;
            hideAlpha = Mathf.MoveTowards(hideAlpha, playing && !scoped && gun != null ? 1f : 0f, dt * 12f);
            if (crosshairGroup != null) crosshairGroup.alpha = hideAlpha;
            if (gun == null || gun.data == null) return;

            // the gun's spread (degrees) as canvas units at the camera's current field of view
            var cam = view != null && view.playerCamera != null ? view.playerCamera : Camera.main;
            float fov = cam != null ? cam.fieldOfView : 60f;
            float halfHeight = 540f;   // the HUD canvas matches the 1080-high reference screen by height
            float spread = Mathf.Tan(gun.SpreadDegrees * Mathf.Deg2Rad) / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * halfHeight;
            shownSpread = shownSpread < 0f ? spread : Mathf.Lerp(shownSpread, spread, 1f - Mathf.Exp(-follow * dt));

            var style = gun.data.crosshair;
            if (linesRoot != null) linesRoot.SetActive(style == CrosshairStyle.Lines);
            if (dotRoot != null) dotRoot.SetActive(style == CrosshairStyle.Dot);
            if (circleRoot != null) circleRoot.SetActive(style == CrosshairStyle.Circle);

            switch (style)
            {
                case CrosshairStyle.Lines: PlaceCross(lines, minLineGap + shownSpread); break;
                case CrosshairStyle.Dot: PlaceCross(dotTicks, dotTickGap + shownSpread * 0.5f); break;
                case CrosshairStyle.Circle: PlaceRing(Mathf.Max(minRing, shownSpread)); break;
            }
        }

        /// Puts four marks (top, bottom, left, right) `gap` from the centre, measured to their inner end.
        static void PlaceCross(RectTransform[] marks, float gap)
        {
            if (marks == null || marks.Length < 4) return;
            Vector2[] dirs = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
            for (int i = 0; i < 4; i++)
            {
                var m = marks[i];
                if (m == null) continue;
                float half = Mathf.Max(m.sizeDelta.x, m.sizeDelta.y) * 0.5f;
                m.anchoredPosition = dirs[i] * (gap + half);
            }
        }

        void PlaceRing(float radius)
        {
            if (ringArcs == null) return;
            foreach (var a in ringArcs)
                if (a != null) a.Radius = radius;
        }

        void SetHitAlpha(float a)
        {
            hitAlpha = a;
            if (hitMarkerGraphics == null) return;
            foreach (var g in hitMarkerGraphics)
            {
                if (g == null) continue;
                var c = hitColor; c.a = a;
                g.color = c;
                g.enabled = a > 0.001f;
            }
        }
    }
}
