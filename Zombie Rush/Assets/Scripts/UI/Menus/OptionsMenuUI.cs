using System;
using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Core;

namespace ZombieRush.UI
{
    /// Options screen shared by the main menu and the pause menu. Every control
    /// writes straight into GameSettings (applied live); prefs are flushed to disk on close.
    public class OptionsMenuUI : MonoBehaviour
    {
        public UIPanel panel;

        [Header("Graphics")]
        public OptionCycler quality;
        public OptionCycler frameRate;

        [Header("Controls")]
        public Slider sensitivity;
        public Text sensitivityValue;
        public OptionCycler invertY;
        public OptionCycler controlScheme;

        [Header("Audio & feedback")]
        public Slider masterVolume;
        public Text masterValue;
        public Slider sfxVolume;
        public Text sfxValue;
        public Slider musicVolume;
        public Text musicValue;
        public OptionCycler vibration;

        public Button backButton;

        static readonly string[] OffOn = { "OFF", "ON" };
        static readonly string[] Schemes = { "AUTO", "TOUCH", "KEYBOARD & MOUSE" };

        Action onClosed;
        int[] fpsValues;

        public bool IsOpen => panel != null && panel.IsVisible;

        void Awake()
        {
            if (backButton != null) backButton.onClick.AddListener(Close);

            if (quality != null) quality.Changed += i => GameSettings.Quality = i;
            if (frameRate != null) frameRate.Changed += i => GameSettings.TargetFps = fpsValues[i];
            if (invertY != null) invertY.Changed += i => GameSettings.InvertY = i == 1;
            if (controlScheme != null) controlScheme.Changed += i => GameSettings.Controls = (ControlScheme)i;
            if (vibration != null) vibration.Changed += i => GameSettings.Vibration = i == 1;

            SetupSlider(sensitivity, GameSettings.MinSensitivity, GameSettings.MaxSensitivity, false, v =>
            {
                GameSettings.LookSensitivity = v;
                SetText(sensitivityValue, v.ToString("0.0"));
            });
            SetupVolume(masterVolume, masterValue, v => GameSettings.MasterVolume = v);
            SetupVolume(sfxVolume, sfxValue, v => GameSettings.SfxVolume = v);
            SetupVolume(musicVolume, musicValue, v => GameSettings.MusicVolume = v);
        }

        public void Open(Action closed = null)
        {
            onClosed = closed;
            Refresh();
            if (panel != null) panel.Show();
        }

        public void Close()
        {
            if (!IsOpen) return;
            GameSettings.Save();
            if (panel != null) panel.Hide();
            var callback = onClosed;
            onClosed = null;
            callback?.Invoke();
        }

        /// Hide without running the close callback (e.g. the game resumed underneath).
        public void CloseSilently()
        {
            if (!IsOpen) return;
            GameSettings.Save();
            onClosed = null;
            if (panel != null) panel.Hide();
        }

        void Refresh()
        {
            if (quality != null)
            {
                var names = QualitySettings.names;
                var labels = new string[names.Length];
                for (int i = 0; i < names.Length; i++) labels[i] = names[i].ToUpperInvariant();
                quality.SetOptions(labels, GameSettings.Quality);
            }

            if (frameRate != null)
            {
                fpsValues = GameSettings.FrameRateOptions;
                var labels = new string[fpsValues.Length];
                int current = 0;
                for (int i = 0; i < fpsValues.Length; i++)
                {
                    labels[i] = fpsValues[i] > 0 ? $"{fpsValues[i]} FPS" : "VSYNC";
                    if (fpsValues[i] == GameSettings.TargetFps) current = i;
                }
                frameRate.SetOptions(labels, current);
            }

            if (invertY != null) invertY.SetOptions(OffOn, GameSettings.InvertY ? 1 : 0);
            if (controlScheme != null) controlScheme.SetOptions(Schemes, (int)GameSettings.Controls);
            if (vibration != null) vibration.SetOptions(OffOn, GameSettings.Vibration ? 1 : 0);

            SetSlider(sensitivity, GameSettings.LookSensitivity, sensitivityValue, GameSettings.LookSensitivity.ToString("0.0"));
            SetVolume(masterVolume, masterValue, GameSettings.MasterVolume);
            SetVolume(sfxVolume, sfxValue, GameSettings.SfxVolume);
            SetVolume(musicVolume, musicValue, GameSettings.MusicVolume);
        }

        // Volumes are shown as 0-10 bars (GDD), stored as 0-1.
        static void SetupVolume(Slider slider, Text valueText, Action<float> apply)
        {
            SetupSlider(slider, 0f, 10f, true, v =>
            {
                apply(v / 10f);
                SetText(valueText, Mathf.RoundToInt(v).ToString());
            });
        }

        static void SetVolume(Slider slider, Text valueText, float value01)
        {
            int bars = Mathf.RoundToInt(value01 * 10f);
            SetSlider(slider, bars, valueText, bars.ToString());
        }

        static void SetupSlider(Slider slider, float min, float max, bool whole, Action<float> onChanged)
        {
            if (slider == null) return;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = whole;
            slider.onValueChanged.AddListener(v => onChanged(v));
        }

        static void SetSlider(Slider slider, float value, Text valueText, string text)
        {
            if (slider != null) slider.SetValueWithoutNotify(value);
            SetText(valueText, text);
        }

        static void SetText(Text t, string s)
        {
            if (t != null) t.text = s;
        }
    }
}
