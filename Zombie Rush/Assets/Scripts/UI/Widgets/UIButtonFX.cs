using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ZombieRush.UI
{
    /// Menu button feel: a red highlight slides in, the label brightens and nudges
    /// right on hover/selection, and the button squashes slightly when pressed.
    /// Hover selects the button, so mouse, keyboard and touch share one highlight.
    /// Runs on unscaled time and sleeps when idle.
    public class UIButtonFX : MonoBehaviour,
        IPointerEnterHandler, IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        public Graphic highlight;
        public Text label;
        public RectTransform content;
        public Color labelNormal = new Color(0.93f, 0.9f, 0.85f);
        public Color labelActive = Color.white;
        public Color labelDisabled = new Color(0.3f, 0.31f, 0.34f);
        public float shift = 16f;
        public float pressScale = 0.96f;
        public float speed = 14f;

        Selectable selectable;
        Vector2 contentBase;
        float hot, targetHot, press, targetPress;
        bool wasInteractable = true;

        void Awake()
        {
            selectable = GetComponent<Selectable>();
            if (content != null) contentBase = content.anchoredPosition;
            Apply();
        }

        void OnDisable()
        {
            hot = targetHot = press = targetPress = 0f;
            Apply();
        }

        bool Interactable => selectable == null || selectable.IsInteractable();

        public void OnPointerEnter(PointerEventData e)
        {
            if (!Interactable) return;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(gameObject);
            else Wake(ref targetHot, 1f);
        }

        public void OnSelect(BaseEventData e) { if (Interactable) Wake(ref targetHot, 1f); }
        public void OnDeselect(BaseEventData e) => Wake(ref targetHot, 0f);
        public void OnPointerDown(PointerEventData e) { if (Interactable) Wake(ref targetPress, 1f); }
        public void OnPointerUp(PointerEventData e) => Wake(ref targetPress, 0f);

        void Wake(ref float field, float value)
        {
            field = value;
            enabled = true;
        }

        void Update()
        {
            float step = speed * Time.unscaledDeltaTime;
            hot = Mathf.MoveTowards(hot, targetHot, step);
            press = Mathf.MoveTowards(press, targetPress, step * 1.5f);

            bool interactable = Interactable;
            bool settled = Mathf.Approximately(hot, targetHot) && Mathf.Approximately(press, targetPress) && interactable == wasInteractable;
            wasInteractable = interactable;
            Apply();
            if (settled) enabled = false;
        }

        void Apply()
        {
            float eased = hot * hot * (3f - 2f * hot);
            if (highlight != null) UITheme.SetAlpha(highlight, eased);
            if (label != null) label.color = Interactable ? Color.Lerp(labelNormal, labelActive, eased) : labelDisabled;
            if (content != null) content.anchoredPosition = contentBase + new Vector2(shift * eased, 0f);
            transform.localScale = Vector3.one * Mathf.Lerp(1f, pressScale, press);
        }
    }
}
