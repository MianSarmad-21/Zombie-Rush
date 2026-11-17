using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZombieRush.Core;

namespace ZombieRush.World
{
    /// "HOLD E  OPEN CHEST" under the crosshair while the player stands at a locked chest, with a bar that fills as
    /// the key is held. On touch the prompt itself is the button: hold it. Builds its own overlay the first time
    /// (HUD font borrowed from the wave banner), so the scene needs no setup.
    public class ChestPrompt : MonoBehaviour
    {
        public static LootChest Target { get; private set; }
        static ChestPrompt instance;

        GameObject panel;
        Text label;
        RectTransform fill;
        float fade;

        public static void Show(LootChest chest)
        {
            if (instance == null) Create();
            Target = chest;
        }

        public static void Hide(LootChest chest)
        {
            if (Target != chest) return;
            Target = null;
            GameInput.TouchRelease(TouchAction.Interact);
        }

        static void Create()
        {
            var go = new GameObject("ChestPrompt");
            DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 390;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            var p = new GameObject("Panel", typeof(RectTransform));
            p.transform.SetParent(go.transform, false);
            var rt = (RectTransform)p.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.36f);
            rt.sizeDelta = new Vector2(440f, 92f);
            var bg = p.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.05f, 0.72f);
            instance = go.AddComponent<ChestPrompt>();   // on the always-active root: it shows and hides the panel
            instance.panel = p;
            p.AddComponent<ChestPromptPress>();

            var banner = FindAnyObjectByType<UI.WaveBannerUI>(FindObjectsInactive.Include);
            var font = banner != null && banner.titleText != null ? banner.titleText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var t = new GameObject("Label", typeof(RectTransform));
            t.transform.SetParent(p.transform, false);
            var trt = (RectTransform)t.transform;
            trt.anchorMin = new Vector2(0f, 0.3f); trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            var label = t.AddComponent<Text>();
            label.font = font; label.fontSize = 36; label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(1f, 0.82f, 0.35f); label.raycastTarget = false;
            instance.label = label;

            var barBg = new GameObject("Bar", typeof(RectTransform));
            barBg.transform.SetParent(p.transform, false);
            var brt = (RectTransform)barBg.transform;
            brt.anchorMin = new Vector2(0.06f, 0.12f); brt.anchorMax = new Vector2(0.94f, 0.24f); brt.offsetMin = brt.offsetMax = Vector2.zero;
            var bi = barBg.AddComponent<Image>(); bi.color = new Color(1f, 1f, 1f, 0.15f); bi.raycastTarget = false;
            var f = new GameObject("Fill", typeof(RectTransform));
            f.transform.SetParent(barBg.transform, false);
            var frt = (RectTransform)f.transform;
            frt.anchorMin = Vector2.zero; frt.anchorMax = new Vector2(0f, 1f); frt.offsetMin = frt.offsetMax = Vector2.zero;
            var fi = f.AddComponent<Image>(); fi.color = new Color(1f, 0.75f, 0.25f); fi.raycastTarget = false;
            instance.fill = frt;
            p.SetActive(false);
        }

        void LateUpdate()
        {
            bool show = Target != null && !Target.Opened;
            fade = Mathf.MoveTowards(fade, show ? 1f : 0f, Time.unscaledDeltaTime * 8f);
            if (panel.activeSelf != fade > 0.01f) panel.SetActive(fade > 0.01f);
            if (!panel.activeSelf) return;
            panel.transform.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, fade);
            label.text = GameSettings.UseTouchControls ? "HOLD HERE  ·  OPEN CHEST" : $"HOLD [{GameInput.InteractKey}]  ·  OPEN CHEST";
            float prog = Target != null ? Target.Progress : 1f;
            fill.anchorMax = new Vector2(prog, 1f);
        }

    }

    /// Touch: holding the prompt panel is holding "open".
    public class ChestPromptPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public void OnPointerDown(PointerEventData e) => GameInput.TouchPress(TouchAction.Interact);
        public void OnPointerUp(PointerEventData e) => GameInput.TouchRelease(TouchAction.Interact);
        public void OnPointerExit(PointerEventData e) => GameInput.TouchRelease(TouchAction.Interact);
    }
}
