using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ZombieRush.UI.Kit
{
    /// Button feel for the kit: hovering lifts and tilts it a little, pressing stamps it down (shrinks,
    /// rotates, the ink darkens), releasing springs it back. Works for mouse and touch.
    public class StampButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        public RectTransform content;
        public Graphic ink;
        public Color inkNormal = Color.white;
        public Color inkHover = Color.white;
        public Color inkPressed = Color.gray;
        [Tooltip("Resting tilt, degrees (a stamp is never quite straight).")]
        public float restTilt = -1.5f;
        public float hoverTilt = 1.5f;
        public float pressScale = 0.92f;
        public float speed = 18f;

        bool hover, down;
        Button button;

        void Awake()
        {
            button = GetComponent<Button>();
            if (content == null) content = (RectTransform)transform;
        }

        void OnEnable() { hover = down = false; Apply(1f); }

        public void OnPointerEnter(PointerEventData e) => hover = true;
        public void OnPointerExit(PointerEventData e) { hover = false; down = false; }
        public void OnPointerDown(PointerEventData e) => down = true;
        public void OnPointerUp(PointerEventData e) => down = false;
        public void OnSelect(BaseEventData e) => hover = true;
        public void OnDeselect(BaseEventData e) => hover = false;

        void Update() => Apply(1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));

        void Apply(float k)
        {
            bool off = button != null && !button.interactable;
            float tilt = down ? restTilt - 3f : hover ? hoverTilt : restTilt;
            float scale = down ? pressScale : hover ? 1.05f : 1f;
            var rot = Quaternion.Euler(0f, 0f, tilt);
            content.localRotation = Quaternion.Slerp(content.localRotation, rot, k);
            content.localScale = Vector3.Lerp(content.localScale, Vector3.one * scale, k);
            if (ink != null)
            {
                var c = off ? inkNormal * new Color(0.5f, 0.5f, 0.5f, 0.6f) : down ? inkPressed : hover ? inkHover : inkNormal;
                ink.color = Color.Lerp(ink.color, c, k);
            }
        }
    }
}
