using UnityEngine;
using ZombieRush.Core;
using ZombieRush.Player;

namespace ZombieRush.UI
{
    /// The rifle scope sight, shown while looking through a scope (PlayerViewSwitcher.Scoped): a sight tube in
    /// the middle of the screen with the (zoomed) world visible all round it, as in most shooters - not a
    /// full-screen black mask. The zoom itself is the camera's field of view (WeaponData.scopeZoom).
    /// Built by the HUD builder; the builder's black side bars are switched off here.
    public class ScopeUI : MonoBehaviour
    {
        public CanvasGroup group;
        [Tooltip("How fast the sight fades in and out (alpha per second).")]
        public float fadeSpeed = 14f;
        [Tooltip("Height of the sight (tube and its shadow) as a share of the screen height.")]
        [Range(0.3f, 1f)] public float sightHeight = 0.8f;

        bool playing = true;
        float maskSent = -1f;

        /// Glass radius (plus half the metal bevel) as a share of the sight image's side: see Tools/UI/make_scope_real.py
        /// (R = 0.355 S, bevel 0.022 S).
        const float GlassOfImage = 0.366f;
        /// The glass radius on screen as a share of half the screen height.
        public float LensFraction => GlassOfImage * 2f * Mathf.Clamp01(sightHeight);

        void Awake()
        {
            GameEvents.OnGameStateChanged += HandleState;
            LayOut();
            Set(0f);
        }

        /// The sight centred at sightHeight of the screen; no black bars beside it.
        void LayOut()
        {
            if (group == null) return;
            foreach (Transform t in group.transform)
            {
                if (t.name.StartsWith("Bar")) { t.gameObject.SetActive(false); continue; }
                if (t.name != "Lens" || !(t is RectTransform r)) continue;
                float h = Mathf.Clamp01(sightHeight);
                r.anchorMin = new Vector2(0.5f, 0.5f - h * 0.5f);
                r.anchorMax = new Vector2(0.5f, 0.5f + h * 0.5f);
                r.sizeDelta = Vector2.zero; r.anchoredPosition = Vector2.zero;
            }
        }

        void OnDestroy() => GameEvents.OnGameStateChanged -= HandleState;

        void HandleState(GameState state) => playing = state == GameState.Playing;

        void Update()
        {
            var view = PlayerViewSwitcher.Local;
            bool on = playing && view != null && view.ThroughScope;   // iron sights: no scope picture
            Set(Mathf.MoveTowards(group != null ? group.alpha : 0f, on ? 1f : 0f, fadeSpeed * Time.unscaledDeltaTime));
            // the first-person gun is cut out inside the glass: through the lens only the world shows
            float mask = on && view.ScopedAtEye ? LensFraction : 0f;
            if (mask != maskSent && view != null && view.playerCamera != null)
            {
                var vm = view.playerCamera.GetComponent<FirstPersonViewmodel>();
                if (vm != null) { vm.SetLensMask(mask); maskSent = mask; }
            }
        }

        void Set(float a)
        {
            if (group == null) return;
            group.alpha = a;
            bool visible = a > 0.001f;
            if (group.gameObject.activeSelf != visible) group.gameObject.SetActive(visible);
        }
    }
}
