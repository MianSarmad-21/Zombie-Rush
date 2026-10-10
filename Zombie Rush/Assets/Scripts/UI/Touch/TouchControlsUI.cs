using UnityEngine;
using ZombieRush.Core;

namespace ZombieRush.UI
{
    /// Shows the on-screen controls only when the touch scheme is active and the
    /// game is being played. Hiding deactivates `controlsRoot`, so every widget
    /// releases its finger and no input stays stuck "held".
    public class TouchControlsUI : MonoBehaviour
    {
        public GameObject controlsRoot;

        GameState state = GameState.Playing;

        void Awake()
        {
            GameSettings.Changed += Refresh;
            GameEvents.OnGameStateChanged += HandleState;
            Refresh();
        }

        void OnDestroy()
        {
            GameSettings.Changed -= Refresh;
            GameEvents.OnGameStateChanged -= HandleState;
        }

        void HandleState(GameState newState)
        {
            state = newState;
            Refresh();
        }

        void Refresh()
        {
            bool show = GameSettings.UseTouchControls && state == GameState.Playing;
            if (controlsRoot != null && controlsRoot.activeSelf != show) controlsRoot.SetActive(show);
            if (!show) GameInput.ResetTouchState();
        }
    }
}
