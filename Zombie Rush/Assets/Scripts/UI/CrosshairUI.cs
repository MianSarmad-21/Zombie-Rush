using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Core;

namespace ZombieRush.UI
{
    /// Centre crosshair plus a hit marker that flashes white on a hit and red on
    /// a kill. Hidden while paused or dead.
    public class CrosshairUI : MonoBehaviour
    {
        public CanvasGroup crosshairGroup;
        public RectTransform hitMarker;
        public Graphic[] hitMarkerGraphics;
        public float fadeSpeed = 5f;

        float hitAlpha;
        Color hitColor = Color.white;

        // Awake/OnDestroy, not OnEnable/OnDisable: this component disables itself when idle.
        void Awake()
        {
            GameEvents.OnEnemyHit += HandleHit;
            GameEvents.OnZombieKilled += HandleKill;
            GameEvents.OnGameStateChanged += HandleState;
            SetHitAlpha(0f);
            enabled = false;
        }

        void OnDestroy()
        {
            GameEvents.OnEnemyHit -= HandleHit;
            GameEvents.OnZombieKilled -= HandleKill;
            GameEvents.OnGameStateChanged -= HandleState;
        }

        void HandleState(GameState state)
        {
            if (crosshairGroup != null) crosshairGroup.alpha = state == GameState.Playing ? 1f : 0f;
        }

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
            enabled = true;
        }

        void Update()
        {
            hitAlpha = Mathf.MoveTowards(hitAlpha, 0f, fadeSpeed * Time.deltaTime);
            if (hitMarker != null) hitMarker.localScale = Vector3.Lerp(hitMarker.localScale, Vector3.one, Time.deltaTime * 12f);
            SetHitAlpha(hitAlpha);
            if (hitAlpha <= 0f) enabled = false;
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
