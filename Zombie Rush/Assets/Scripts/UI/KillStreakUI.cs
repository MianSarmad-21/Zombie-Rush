using UnityEngine;
using UnityEngine.UI;

namespace ZombieRush.UI
{
    /// The big call-out above the crosshair for kill streaks ("DOUBLE KILL", "RAMPAGE"): punches in large,
    /// settles, holds, fades. In the HUD's banner font, made in code on first use on its own overlay canvas
    /// (sized by screen height); it turns itself off when idle.
    public class KillStreakUI : MonoBehaviour
    {
        const float Hold = 1.3f, Fade = 0.45f;

        static KillStreakUI instance;
        Text title, sub;
        RectTransform box;
        CanvasGroup group;
        float age = float.MaxValue;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        public static void Show(string text, string subtitle, Color color)
        {
            if (instance == null) Create();
            instance.Play(text, subtitle, color);
        }

        static void Create()
        {
            var go = new GameObject("KillStreak", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 45;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            instance = go.AddComponent<KillStreakUI>();
            instance.Build();
        }

        void Build()
        {
            var banner = FindAnyObjectByType<WaveBannerUI>(FindObjectsInactive.Include);
            Font font = banner != null && banner.titleText != null ? banner.titleText.font : null;
            Font detail = banner != null && banner.detailText != null ? banner.detailText.font : font;
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (detail == null) detail = font;

            var b = new GameObject("Box", typeof(RectTransform), typeof(CanvasGroup));
            box = (RectTransform)b.transform;
            box.SetParent(transform, false);
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.69f);
            box.sizeDelta = new Vector2(1200f, 160f);
            group = b.GetComponent<CanvasGroup>();
            group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;

            title = MakeText("Title", font, 76, new Vector2(0f, 18f));
            sub = MakeText("Sub", detail, 30, new Vector2(0f, -44f));
        }

        Text MakeText(string name, Font font, int size, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(box, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1200f, size * 1.4f);
            rt.anchoredPosition = pos;
            var t = go.AddComponent<Text>();
            t.font = font; t.fontSize = size; t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(3f, -3f);
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(0f, -6f);
            return t;
        }

        void Play(string text, string subtitle, Color color)
        {
            title.text = text;
            title.color = color;
            sub.text = subtitle ?? "";
            sub.color = new Color(0.92f, 0.89f, 0.84f, 1f);
            age = 0f;
            enabled = true;
        }

        void Update()
        {
            age += Time.unscaledDeltaTime;
            if (age >= Hold + Fade) { group.alpha = 0f; enabled = false; return; }
            float punch = age < 0.14f ? Mathf.Lerp(1.7f, 1f, 1f - (1f - age / 0.14f) * (1f - age / 0.14f)) : 1f;
            box.localScale = new Vector3(punch, punch, 1f);
            group.alpha = age < 0.06f ? age / 0.06f : age > Hold ? 1f - (age - Hold) / Fade : 1f;
        }
    }
}
