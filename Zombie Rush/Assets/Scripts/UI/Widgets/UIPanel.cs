using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZombieRush.Core;

namespace ZombieRush.UI
{
    /// Fades a screen in/out with a CanvasGroup using unscaled time (so it works
    /// while the game is paused). When fully hidden, its own Canvas is disabled,
    /// so a hidden menu costs nothing to render or raycast.
    [RequireComponent(typeof(CanvasGroup))]
    public class UIPanel : MonoBehaviour
    {
        public float fadeSpeed = 7f;
        public bool startVisible;
        [Tooltip("Selected when shown, for keyboard/gamepad navigation (skipped on touch).")]
        public Selectable firstSelected;

        CanvasGroup group;
        Canvas canvas;
        float target;
        bool initialized;

        public bool IsVisible => target > 0.5f;

        void Awake() => Init();

        void Init()
        {
            if (initialized) return;
            initialized = true;
            group = GetComponent<CanvasGroup>();
            canvas = GetComponent<Canvas>();
            SetImmediate(startVisible);
        }

        public void Show()
        {
            Init();
            target = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;
            if (canvas != null) canvas.enabled = true;
            enabled = true;
            SelectFirst();
        }

        public void Hide()
        {
            Init();
            target = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            enabled = true;
        }

        public void SetImmediate(bool visible)
        {
            Init();
            target = visible ? 1f : 0f;
            group.alpha = target;
            group.interactable = visible;
            group.blocksRaycasts = visible;
            if (canvas != null) canvas.enabled = visible;
            enabled = false;
            if (visible) SelectFirst();
        }

        void Update()
        {
            group.alpha = Mathf.MoveTowards(group.alpha, target, fadeSpeed * Time.unscaledDeltaTime);
            if (!Mathf.Approximately(group.alpha, target)) return;

            if (target <= 0f && canvas != null) canvas.enabled = false;
            enabled = false;
        }

        void SelectFirst()
        {
            if (firstSelected == null || EventSystem.current == null || GameSettings.UseTouchControls) return;
            EventSystem.current.SetSelectedGameObject(firstSelected.gameObject);
        }
    }
}
