using UnityEngine;
using UnityEngine.EventSystems;
using ZombieRush.Core;

namespace ZombieRush.UI
{
    /// Drag anywhere on (the right half of) the screen to look around.
    /// Put an EmptyGraphic on the same object so it catches touches without drawing.
    public class TouchLookArea : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        const int NoPointer = int.MinValue;
        int pointerId = NoPointer;

        void OnDisable() => pointerId = NoPointer;

        public void OnPointerDown(PointerEventData e)
        {
            if (pointerId == NoPointer) pointerId = e.pointerId;
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId == pointerId) GameInput.AddTouchLook(e.delta);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId == pointerId) pointerId = NoPointer;
        }
    }
}
