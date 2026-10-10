using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZombieRush.Core;

namespace ZombieRush.UI
{
    /// On-screen action button. Fire can also be dragged to aim while shooting,
    /// which is how most mobile shooters let you track a target.
    public class TouchButton : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public TouchAction action;
        [Tooltip("Dragging a finger that started on this button also turns the camera.")]
        public bool dragToLook;
        public Graphic visual;
        public Color normalColor = new Color(1f, 1f, 1f, 0.55f);
        public Color pressedColor = new Color(1f, 1f, 1f, 0.95f);
        [Tooltip("Colour while a toggle (Crouch) is on.")]
        public Color toggledColor = new Color(0.25f, 0.84f, 0.91f, 0.95f);
        public float pressedScale = 0.9f;

        const int NoPointer = int.MinValue;
        int pointerId = NoPointer;

        void OnEnable() => ApplyVisual(false);

        void OnDisable()
        {
            if (pointerId != NoPointer) GameInput.TouchRelease(action);
            pointerId = NoPointer;
            ApplyVisual(false);
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (pointerId != NoPointer) return;
            pointerId = e.pointerId;
            GameInput.TouchPress(action);
            ApplyVisual(true);
        }

        public void OnDrag(PointerEventData e)
        {
            if (dragToLook && e.pointerId == pointerId) GameInput.AddTouchLook(e.delta);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != pointerId) return;
            pointerId = NoPointer;
            GameInput.TouchRelease(action);
            ApplyVisual(false);
        }

        void ApplyVisual(bool pressed)
        {
            transform.localScale = Vector3.one * (pressed ? pressedScale : 1f);
            if (visual == null) return;

            bool toggledOn = action == TouchAction.Crouch && GameInput.TouchCrouchActive;
            visual.color = pressed ? pressedColor : (toggledOn ? toggledColor : normalColor);
        }
    }
}
