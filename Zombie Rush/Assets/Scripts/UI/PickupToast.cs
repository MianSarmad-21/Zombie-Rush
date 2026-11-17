using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ZombieRush.UI
{
    /// The game's one place for messages (2026-11-17, user: "simple text notifications on the side"): a short
    /// feed of plain lines on the left side of the screen, under the health and score. Pick-ups ("+12 RIFLE
    /// AMMO"), notices (safe base, turret...) and wave news (WaveBannerUI forwards them here instead of its big
    /// banner) each get one line; a line slides in, stays a few seconds and fades. Up to 5 at once, newest at
    /// the bottom. Builds its own overlay canvas the first time it is used, in the HUD's font.
    public class PickupToast : MonoBehaviour
    {
        const int MaxLines = 5;
        const float Life = 3.6f, LineGap = 40f, FontSize = 30;
        static PickupToast instance;
        static bool gamePlaying = true;   // follows the same state event the pause / game-over screens use

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { instance = null; gamePlaying = true; hooked = false; }
        static bool hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            if (hooked) return;
            hooked = true;
            ZombieRush.Core.GameEvents.OnGameStateChanged += st => gamePlaying = st == ZombieRush.Core.GameState.Playing;
        }

        class Line { public Text text; public CanvasGroup group; public float age; public float y; }
        readonly List<Line> lines = new List<Line>();
        Font font;
        RectTransform root;

        /// One line in the given colour.
        public static void Show(string message, Color? color = null)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (instance == null) Create();
            instance.Add(message, color ?? new Color(1f, 0.82f, 0.35f));
        }

        /// A notice: the title in its colour, then the subtitle in plain white ("SAFE BASE  ·  STEP OUT TO FIGHT").
        public static void Notice(string title, string subtitle, Color titleColor)
        {
            // theme colours are dark (made for big banners): lift them so a thin line stays readable
            Color tc = Color.Lerp(titleColor, Color.white, 0.35f);
            string t = string.IsNullOrEmpty(title) ? "" : $"<color=#{ColorUtility.ToHtmlStringRGB(tc)}>{title}</color>";
            string s = string.IsNullOrEmpty(subtitle) ? "" : $"<color=#E8E2D6>{subtitle}</color>";
            Show(t.Length > 0 && s.Length > 0 ? $"{t}  <color=#8A8A8A>·</color>  {s}" : t + s, Color.white);
        }

        static void Create()
        {
            var go = new GameObject("SideFeed");
            DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 400;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            instance = go.AddComponent<PickupToast>();
            var r = new GameObject("Lines", typeof(RectTransform));
            r.transform.SetParent(go.transform, false);
            instance.root = (RectTransform)r.transform;
            // left side, under the health / score block
            instance.root.anchorMin = instance.root.anchorMax = new Vector2(0f, 0.655f);   // under the health / score / expand block
            instance.root.pivot = new Vector2(0f, 1f);
            instance.root.anchoredPosition = new Vector2(44f, 0f);
            var banner = FindAnyObjectByType<WaveBannerUI>(FindObjectsInactive.Include);
            instance.font = banner != null && banner.detailText != null ? banner.detailText.font
                          : banner != null && banner.titleText != null ? banner.titleText.font : null;
            if (instance.font == null) instance.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        void Add(string message, Color color)
        {
            // the same message again (e.g. two ammo boxes): refresh it instead of stacking a copy
            foreach (var l in lines)
                if (l.text.text == message) { l.age = Mathf.Min(l.age, 0.3f); return; }
            if (lines.Count >= MaxLines) { Destroy(lines[0].text.gameObject); lines.RemoveAt(0); }
            var go = new GameObject("Line", typeof(RectTransform));
            go.transform.SetParent(root, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = (int)FontSize;
            t.supportRichText = true;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.color = color;
            t.text = message;
            var o = go.AddComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 0.8f);
            o.effectDistance = new Vector2(2f, -2f);
            var g = go.AddComponent<CanvasGroup>();   // fades colour-tagged text too (tags ignore the text's alpha)
            g.alpha = 0f; g.blocksRaycasts = false; g.interactable = false;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(1000f, LineGap);
            var line = new Line { text = t, group = g };
            line.y = -(lines.Count + 0.5f) * LineGap;
            lines.Add(line);
        }

        void Update()
        {
            // only over the game itself: not over the pause / game-over screens or the HUD editor
            bool show = gamePlaying && !HudEditor.IsOpen;
            if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
            float dt = Time.unscaledDeltaTime;
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                var l = lines[i];
                l.age += dt;
                if (l.age >= Life) { Destroy(l.text.gameObject); lines.RemoveAt(i); }
            }
            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                float slide = 1f - Mathf.Clamp01(l.age / 0.18f);           // slides in from the left
                float fade = 1f - Mathf.Clamp01((l.age - (Life - 0.6f)) / 0.6f);
                float a = Mathf.Min(fade, 1f - slide);
                l.group.alpha = a;
                // oldest on top; when one goes, the rest close up smoothly
                float wantY = -(i + 0.5f) * LineGap;
                l.y = Mathf.Lerp(l.y, wantY, 1f - Mathf.Exp(-14f * dt));
                l.text.rectTransform.anchoredPosition = new Vector2(-40f * slide * slide, l.y);
            }
        }
    }
}
