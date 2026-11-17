using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Core;

namespace ZombieRush.UI
{
    /// Pause screen: Resume, Restart (wave 1), Options, Quit to Menu.
    public class PauseMenuUI : MonoBehaviour
    {
        public UIPanel panel;
        public OptionsMenuUI options;
        public Button resumeButton;
        public Button restartButton;
        public Button optionsButton;
        public Button menuButton;

        GameObject hudButton;   // CUSTOMIZE HUD (touch controls only), made from the SETTINGS button

        void Awake()
        {
            GameEvents.OnGameStateChanged += HandleState;
            if (resumeButton != null) resumeButton.onClick.AddListener(() => { if (GameManager.Instance != null) GameManager.Instance.Resume(); });
            if (restartButton != null) restartButton.onClick.AddListener(() => { if (GameManager.Instance != null) GameManager.Instance.RestartLevel(); });
            if (menuButton != null) menuButton.onClick.AddListener(() => { if (GameManager.Instance != null) GameManager.Instance.QuitToMenu(); });
            if (optionsButton != null) optionsButton.onClick.AddListener(OpenOptions);
            MakeHudButton();
        }

        void MakeHudButton()
        {
            if (optionsButton == null) return;
            var go = Instantiate(optionsButton.gameObject, optionsButton.transform.parent);
            go.name = "CustomizeHud";
            var rt = (RectTransform)go.transform;
            var below = menuButton != null ? (RectTransform)menuButton.transform : (RectTransform)optionsButton.transform;
            float gap = 130f;
            if (restartButton != null && optionsButton != null)
                gap = Mathf.Abs(((RectTransform)restartButton.transform).anchoredPosition.y - ((RectTransform)optionsButton.transform).anchoredPosition.y);
            rt.anchoredPosition = below.anchoredPosition - new Vector2(0f, gap);
            foreach (var t in go.GetComponentsInChildren<Text>(true)) if (t.text.Contains("SETTINGS")) t.text = "CUSTOMIZE HUD";
            var b = go.GetComponent<Button>();
            b.onClick = new Button.ButtonClickedEvent();
            b.onClick.AddListener(OpenHudEditor);
            hudButton = go;
            hudButton.SetActive(false);
        }

        void OpenHudEditor()
        {
            if (panel != null) panel.Hide();
            HudEditor.Open(() =>
            {
                if (GameManager.Instance != null && GameManager.Instance.State == GameState.Paused && panel != null) panel.Show();
            });
        }

        void OnDestroy() => GameEvents.OnGameStateChanged -= HandleState;

        void HandleState(GameState state)
        {
            if (state == GameState.Paused)
            {
                if (hudButton != null) hudButton.SetActive(GameSettings.UseTouchControls);
                if (panel != null) panel.Show();
                return;
            }

            if (options != null) options.CloseSilently();
            if (panel != null) panel.Hide();
        }

        void OpenOptions()
        {
            if (options == null) return;
            if (panel != null) panel.Hide();
            options.Open(() =>
            {
                if (GameManager.Instance != null && GameManager.Instance.State == GameState.Paused && panel != null) panel.Show();
            });
        }
    }
}
