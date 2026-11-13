using UnityEngine;
using ZombieRush.Core;
using ZombieRush.Player;

namespace ZombieRush.UI
{
    /// The rifle scope sight: a full-screen overlay (the sight image in the middle, black filling the
    /// sides on wide screens) shown while looking through a scope (PlayerViewSwitcher.Scoped). The zoom
    /// itself is the camera's field of view (WeaponData.scopeZoom). Built by the HUD builder.
    public class ScopeUI : MonoBehaviour
    {
        public CanvasGroup group;
        [Tooltip("How fast the sight fades in and out (alpha per second).")]
        public float fadeSpeed = 14f;

        bool playing = true;

        void Awake()
        {
            GameEvents.OnGameStateChanged += HandleState;
            Set(0f);
        }

        void OnDestroy() => GameEvents.OnGameStateChanged -= HandleState;

        void HandleState(GameState state) => playing = state == GameState.Playing;

        void Update()
        {
            var view = PlayerViewSwitcher.Local;
            bool on = playing && view != null && view.Scoped;
            Set(Mathf.MoveTowards(group != null ? group.alpha : 0f, on ? 1f : 0f, fadeSpeed * Time.unscaledDeltaTime));
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
