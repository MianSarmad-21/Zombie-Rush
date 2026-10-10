using UnityEngine;
using UnityEngine.EventSystems;
using ZombieRush.Core;

namespace ZombieRush.UI
{
    /// Floating left-thumb stick: the base appears wherever the thumb lands
    /// inside this area, so it never needs precise placement. Pushing it fully
    /// forward sprints (handled by GameInput.SprintHeld).
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform stickBase;
        public RectTransform knob;
        public CanvasGroup visualGroup;
        public float radius = 110f;
        [Range(0f, 0.5f)] public float deadZone = 0.12f;
        public float idleAlpha = 0.45f;

        RectTransform area;
        int pointerId = NoPointer;
        Vector2 baseDefaultPosition;

        const int NoPointer = int.MinValue;

        void Awake()
        {
            area = (RectTransform)transform;
            if (stickBase != null) baseDefaultPosition = stickBase.anchoredPosition;
            SetVisualActive(false);
        }

        void OnDisable() => Release();

        public void OnPointerDown(PointerEventData e)
        {
            if (pointerId != NoPointer || stickBase == null) return;
            pointerId = e.pointerId;

            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(area, e.position, e.pressEventCamera, out var world))
                stickBase.position = world;

            SetVisualActive(true);
            UpdateStick(e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId == pointerId) UpdateStick(e);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId == pointerId) Release();
        }

        void UpdateStick(PointerEventData e)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(stickBase, e.position, e.pressEventCamera, out var local)) return;

            Vector2 offset = Vector2.ClampMagnitude(local, radius);
            if (knob != null) knob.anchoredPosition = offset;

            Vector2 value = offset / radius;
            float mag = value.magnitude;
            value = mag < deadZone ? Vector2.zero : value * ((mag - deadZone) / (1f - deadZone) / mag);
            GameInput.SetTouchMove(value);
        }

        void Release()
        {
            pointerId = NoPointer;
            if (knob != null) knob.anchoredPosition = Vector2.zero;
            if (stickBase != null) stickBase.anchoredPosition = baseDefaultPosition;
            SetVisualActive(false);
            GameInput.SetTouchMove(Vector2.zero);
        }

        void SetVisualActive(bool active)
        {
            if (visualGroup != null) visualGroup.alpha = active ? 1f : idleAlpha;
        }
    }
}
