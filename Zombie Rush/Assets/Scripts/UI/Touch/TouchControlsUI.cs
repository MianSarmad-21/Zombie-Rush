using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using ZombieRush.Core;

namespace ZombieRush.UI
{
    /// Shows the on-screen controls only when the touch scheme is active and the
    /// game is being played (or the HUD editor is open). Hiding deactivates `controlsRoot`,
    /// so every widget releases its finger and no input stays stuck "held".
    ///
    /// Layout: every button (not Pause) and the move stick is a Ctl with a home spot and size (the built
    /// layout shrunk by buttonScale, pulled in towards its corner) and the player's own spot and size on top
    /// (HudEditor; saved in PlayerPrefs "zr.hud.&lt;name&gt;" = x;y;size), size limited to MinSize..MaxSize of home.
    public class TouchControlsUI : MonoBehaviour
    {
        public GameObject controlsRoot;
        [Tooltip("Size of the on-screen buttons and stick (1 = as built). The cluster also moves in towards its corner.")]
        [Range(0.4f, 1f)] public float buttonScale = 0.75f;

        public const float MinSize = 0.6f, MaxSize = 1.6f;

        public class Ctl
        {
            public string key;
            public RectTransform rt;
            public TouchButton button;
            public VirtualJoystick stick;
            public Vector2 homePos, pos;
            public float homeScale, size = 1f;   // size = the player's factor on top of homeScale
        }

        public static TouchControlsUI Instance { get; private set; }
        public readonly List<Ctl> Controls = new List<Ctl>();

        static bool editing;
        /// The HUD editor is open: the controls show (even while paused) so they can be moved.
        public static bool Editing
        {
            get => editing;
            set { editing = value; if (Instance != null) Instance.Refresh(); }
        }

        GameState state = GameState.Playing;

        void Awake()
        {
            Instance = this;
            BuildLayout();   // before the controls first switch on (the stick remembers its home spot then)
            GameSettings.Changed += Refresh;
            GameEvents.OnGameStateChanged += HandleState;
            Refresh();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            GameSettings.Changed -= Refresh;
            GameEvents.OnGameStateChanged -= HandleState;
        }

        void HandleState(GameState newState)
        {
            state = newState;
            Refresh();
        }

        void Refresh()
        {
            bool show = GameSettings.UseTouchControls && (state == GameState.Playing || editing);
            if (controlsRoot != null && controlsRoot.activeSelf != show) controlsRoot.SetActive(show);
            if (!show) GameInput.ResetTouchState();
        }

        // ---------------------------------------------------------------- layout

        void BuildLayout()
        {
            Controls.Clear();
            if (controlsRoot == null) return;
            float s = buttonScale;
            foreach (var b in controlsRoot.GetComponentsInChildren<TouchButton>(true))
            {
                var rt = (RectTransform)b.transform;
                var c = new Ctl { key = b.name, rt = rt, button = b, homePos = rt.anchoredPosition * s, homeScale = s };
                if (b.action == TouchAction.Pause) { c.pos = c.homePos; Place(c); continue; }   // always in its corner, not editable
                Controls.Add(c);
            }
            var stick = controlsRoot.GetComponentInChildren<VirtualJoystick>(true);
            if (stick != null && stick.stickBase != null)
                Controls.Add(new Ctl { key = "MoveStick", rt = stick.stickBase, stick = stick, homePos = stick.stickBase.anchoredPosition * s, homeScale = s });
            foreach (var c in Controls)
            {
                c.pos = c.homePos; c.size = 1f;
                string v = PlayerPrefs.GetString("zr.hud." + c.key, "");
                var parts = v.Split(';');
                if (parts.Length == 3
                    && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                    && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                    && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                {
                    c.pos = new Vector2(x, y); c.size = Mathf.Clamp(z, MinSize, MaxSize);
                }
                Place(c);
            }
        }

        /// Puts a control at its spot and size.
        public static void Place(Ctl c)
        {
            float sc = c.homeScale * c.size;
            if (c.button != null) c.button.restScale = sc;
            c.rt.localScale = Vector3.one * sc;
            c.rt.anchoredPosition = c.pos;
            if (c.stick != null) c.stick.SetHome(c.pos);
        }

        public void Save()
        {
            foreach (var c in Controls)
            {
                bool home = (c.pos - c.homePos).sqrMagnitude < 0.25f && Mathf.Abs(c.size - 1f) < 0.001f;
                if (home) PlayerPrefs.DeleteKey("zr.hud." + c.key);
                else PlayerPrefs.SetString("zr.hud." + c.key, string.Format(CultureInfo.InvariantCulture, "{0:0.#};{1:0.#};{2:0.##}", c.pos.x, c.pos.y, c.size));
            }
            PlayerPrefs.Save();
        }

        public void ResetAll()
        {
            foreach (var c in Controls) { c.pos = c.homePos; c.size = 1f; Place(c); }
        }
    }
}
