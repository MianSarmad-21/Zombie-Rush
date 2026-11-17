using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ZombieRush.UI
{
    /// Phone HUD editor (Pause > CUSTOMIZE HUD, touch controls only): drag any on-screen button or the move
    /// stick to a new spot; tap one to select it and use - / + to make it smaller or bigger (limited to
    /// TouchControlsUI.MinSize..MaxSize of its normal size). With nothing selected, - / + change every
    /// button at once. RESET puts everything back; DONE saves. Built from code on its own canvas, above the HUD.
    public class HudEditor : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        static HudEditor instance;
        public static bool IsOpen => instance != null;

        const float Step = 0.1f;
        static readonly Color Accent = new Color(0.55f, 1f, 0.35f), Faint = new Color(1f, 1f, 1f, 0.35f);

        System.Action onClose;
        TouchControlsUI tc;
        TouchControlsUI.Ctl selected, dragging;
        Vector2 grabLocal, grabPos;
        Text sizeLabel;
        Font font;
        readonly List<RectTransform[]> frames = new List<RectTransform[]>();
        RectTransform canvasRt;

        public static void Open(System.Action onClose)
        {
            if (instance != null) return;
            var tc = TouchControlsUI.Instance;
            if (tc == null || tc.Controls.Count == 0) { PickupToast.Show("HUD EDITING NEEDS THE TOUCH CONTROLS", Color.white); onClose?.Invoke(); return; }
            TouchControlsUI.Editing = true;
            var go = new GameObject("HudEditor");
            instance = go.AddComponent<HudEditor>();
            instance.tc = tc;
            instance.onClose = onClose;
            instance.Build();
        }

        void Close(bool save)
        {
            if (save) tc.Save();
            TouchControlsUI.Editing = false;
            instance = null;
            Destroy(gameObject);
            onClose?.Invoke();
        }

        // ---------------------------------------------------------------- UI

        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            gameObject.AddComponent<GraphicRaycaster>();
            canvasRt = (RectTransform)transform;
            var anyText = FindAnyObjectByType<WaveBannerUI>(FindObjectsInactive.Include);
            font = anyText != null && anyText.detailText != null ? anyText.detailText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // the whole screen catches the fingers (the real buttons underneath don't fire while editing)
            var catcher = gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0.2f);
            catcher.raycastTarget = true;

            // a thin frame round every control; the selected one in green
            foreach (var c in tc.Controls) frames.Add(Frame());

            // the bar at the top
            var bar = MakeRect("Bar", transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(1500f, 150f));
            var barBg = bar.gameObject.AddComponent<Image>(); barBg.color = new Color(0.04f, 0.05f, 0.04f, 0.88f); barBg.raycastTarget = true;
            Label(bar, "CUSTOMIZE HUD  ·  DRAG A BUTTON TO MOVE IT  ·  TAP ONE, THEN - / + TO RESIZE", new Vector2(0f, 38f), 30, new Color(0.9f, 0.88f, 0.82f));
            Button(bar, "-", new Vector2(-560f, -32f), new Vector2(110f, 70f), () => Resize(-Step));
            sizeLabel = Label(bar, "", new Vector2(-300f, -32f), 30, Accent);
            Button(bar, "+", new Vector2(-40f, -32f), new Vector2(110f, 70f), () => Resize(+Step));
            Button(bar, "RESET", new Vector2(250f, -32f), new Vector2(220f, 70f), () => { tc.ResetAll(); selected = null; });
            Button(bar, "DONE", new Vector2(530f, -32f), new Vector2(240f, 70f), () => Close(true), true);
        }

        RectTransform MakeRect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot; rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        Text Label(Transform parent, string text, Vector2 pos, int size, Color color)
        {
            var rt = MakeRect("Label", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(1400f, 50f));
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font; t.fontSize = size; t.color = color; t.text = text; t.raycastTarget = false;
            t.alignment = TextAnchor.MiddleCenter; t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }

        void Button(Transform parent, string text, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction act, bool main = false)
        {
            var rt = MakeRect("Btn_" + text, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = main ? new Color(0.62f, 0.08f, 0.06f) : new Color(0.2f, 0.21f, 0.2f);
            var b = rt.gameObject.AddComponent<Button>(); b.targetGraphic = img; b.onClick.AddListener(act);
            var t = Label(rt, text, Vector2.zero, 34, Color.white);
            t.rectTransform.sizeDelta = size;
        }

        RectTransform[] Frame()
        {
            var f = new RectTransform[4];
            for (int i = 0; i < 4; i++)
            {
                var rt = MakeRect("Edge", transform, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one);
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                var img = rt.gameObject.AddComponent<Image>(); img.color = Faint; img.raycastTarget = false;
                f[i] = rt;
            }
            return f;
        }

        // ---------------------------------------------------------------- editing

        static Camera CamOf(RectTransform rt)
        {
            var c = rt.GetComponentInParent<Canvas>();
            if (c == null) return null;
            c = c.rootCanvas;
            return c.renderMode == RenderMode.ScreenSpaceOverlay ? null : c.worldCamera;
        }

        public void OnPointerDown(PointerEventData e)
        {
            dragging = null;
            for (int i = tc.Controls.Count - 1; i >= 0; i--)
            {
                var c = tc.Controls[i];
                if (!c.rt.gameObject.activeInHierarchy) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(c.rt, e.position, CamOf(c.rt))) continue;
                dragging = selected = c;
                RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)c.rt.parent, e.position, CamOf(c.rt), out grabLocal);
                grabPos = c.pos;
                return;
            }
            selected = null;   // tapped empty space: - / + work on every button
        }

        public void OnDrag(PointerEventData e)
        {
            if (dragging == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)dragging.rt.parent, e.position, CamOf(dragging.rt), out var now)) return;
            dragging.pos = grabPos + (now - grabLocal);
            TouchControlsUI.Place(dragging);
            KeepOnScreen(dragging);
        }

        public void OnPointerUp(PointerEventData e) => dragging = null;

        void Resize(float delta)
        {
            var list = selected != null ? new List<TouchControlsUI.Ctl> { selected } : tc.Controls;
            foreach (var c in list)
            {
                c.size = Mathf.Clamp(Mathf.Round((c.size + delta) * 20f) / 20f, TouchControlsUI.MinSize, TouchControlsUI.MaxSize);
                TouchControlsUI.Place(c);
                KeepOnScreen(c);
            }
        }

        /// Nudges a control back so all of it stays on the screen.
        static void KeepOnScreen(TouchControlsUI.Ctl c)
        {
            var r = ScreenRect(c.rt);
            Vector2 shift = Vector2.zero;
            if (r.xMin < 0f) shift.x = -r.xMin; else if (r.xMax > Screen.width) shift.x = Screen.width - r.xMax;
            if (r.yMin < 0f) shift.y = -r.yMin; else if (r.yMax > Screen.height) shift.y = Screen.height - r.yMax;
            if (shift == Vector2.zero) return;
            float k = c.rt.parent != null ? c.rt.parent.lossyScale.x : 1f;   // screen pixels per local unit (overlay canvas)
            c.pos += shift / Mathf.Max(1e-4f, k);
            TouchControlsUI.Place(c);
        }

        static Rect ScreenRect(RectTransform rt)
        {
            var w = new Vector3[4]; rt.GetWorldCorners(w);
            var cam = CamOf(rt);
            Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, w[0]), b = RectTransformUtility.WorldToScreenPoint(cam, w[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        void LateUpdate()
        {
            // the frames follow the controls (in this canvas' units)
            float k = canvasRt.lossyScale.x;
            for (int i = 0; i < tc.Controls.Count && i < frames.Count; i++)
            {
                var c = tc.Controls[i];
                bool on = c.rt.gameObject.activeInHierarchy;
                var r = ScreenRect(c.rt);
                Color col = c == selected ? Accent : Faint;
                float th = c == selected ? 5f : 2.5f;
                var f = frames[i];
                Edge(f[0], new Vector2(r.xMin, r.yMin) / k, new Vector2(r.width / k, th), col, on);
                Edge(f[1], new Vector2(r.xMin, r.yMax) / k - new Vector2(0, th), new Vector2(r.width / k, th), col, on);
                Edge(f[2], new Vector2(r.xMin, r.yMin) / k, new Vector2(th, r.height / k), col, on);
                Edge(f[3], new Vector2(r.xMax, r.yMin) / k - new Vector2(th, 0), new Vector2(th, r.height / k), col, on);
            }
            if (sizeLabel != null)
                sizeLabel.text = selected != null ? $"{Nice(selected.key)}  {Mathf.RoundToInt(selected.size * 100f)}%" : "ALL BUTTONS";
        }

        static void Edge(RectTransform e, Vector2 at, Vector2 size, Color col, bool on)
        {
            if (e.gameObject.activeSelf != on) e.gameObject.SetActive(on);
            e.pivot = Vector2.zero; e.anchoredPosition = at; e.sizeDelta = size;
            e.GetComponent<Image>().color = col;
        }

        static string Nice(string key) => key == "MoveStick" ? "MOVE STICK" : key.ToUpperInvariant();

        void OnDestroy() { if (instance == this) { instance = null; TouchControlsUI.Editing = false; } }
    }
}
