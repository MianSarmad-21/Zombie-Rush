using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ZombieRush.Core;

namespace ZombieRush.UI
{
    /// Title screen: Play (opens "Select the mode": Solo / Squad), Options, Quit, plus
    /// quick Sound / Music / Vibration toggles. Squad opens the matchmaking lobby.
    public class MainMenuUI : MonoBehaviour
    {
        public string gameplayScene = "MainLevel";

        public UIPanel mainPanel;
        public OptionsMenuUI options;
        public Button playButton;
        public Button multiplayerButton;
        public Button optionsButton;
        public Button quitButton;
        public Text bestWaveText;
        public Text versionText;
        [Tooltip("Full-screen black image used to fade out when starting a game.")]
        public CanvasGroup fader;

        [Header("Select the mode")]
        [Tooltip("Optional. Without it, Play starts the game straight away.")]
        public UIPanel modePanel;
        public Button soloButton;
        public Button squadButton;
        public Button modeBackButton;

        [Header("Squad lobby")]
        public UIPanel squadPanel;
        public Image[] squadSlots;
        public Text squadStatusText;
        public Button squadCancelButton;
        [Tooltip("Slot icon colour while the slot is still empty.")]
        public Color emptySlotColor = new Color(1f, 1f, 1f, 0.15f);

        [Header("Quick toggles")]
        public Button soundToggle;
        public Image soundIcon;
        public Sprite soundOnSprite;
        public Sprite soundOffSprite;
        public Button musicToggle;
        public Image musicIcon;
        public Button vibrationToggle;
        public Image vibrationIcon;
        [Tooltip("Icon colour while a toggle is off.")]
        public Color offColor = new Color(1f, 1f, 1f, 0.3f);

        bool loading;

        void Awake()
        {
            if (playButton != null) playButton.onClick.AddListener(OpenModes);
            if (optionsButton != null) optionsButton.onClick.AddListener(OpenOptions);
            if (quitButton != null) quitButton.onClick.AddListener(Quit);
            if (multiplayerButton != null) multiplayerButton.interactable = false; // co-op is planned (GDD)

            if (soloButton != null) soloButton.onClick.AddListener(PlaySolo);
            if (squadButton != null) squadButton.onClick.AddListener(OpenSquad);
            if (squadCancelButton != null) squadCancelButton.onClick.AddListener(CloseSquad);
            if (modeBackButton != null) modeBackButton.onClick.AddListener(CloseModes);

            if (soundToggle != null) soundToggle.onClick.AddListener(() => GameSettings.Muted = !GameSettings.Muted);
            if (musicToggle != null) musicToggle.onClick.AddListener(() => GameSettings.MusicVolume = GameSettings.MusicVolume > 0.001f ? 0f : 0.7f);
            if (vibrationToggle != null) vibrationToggle.onClick.AddListener(() => GameSettings.Vibration = !GameSettings.Vibration);
            if (vibrationToggle != null && !Application.isMobilePlatform && !Application.isEditor) vibrationToggle.gameObject.SetActive(false);

            // There's no "quit" on iOS, and Android players use the system back/home gesture.
            if (quitButton != null && Application.platform == RuntimePlatform.IPhonePlayer) quitButton.gameObject.SetActive(false);

            GameSettings.Changed += RefreshToggles;
        }

        void OnDestroy() => GameSettings.Changed -= RefreshToggles;

        void Start()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            GameSettings.ApplyAll();

            int best = GameSettings.BestWave;
            if (bestWaveText != null) bestWaveText.text = best > 0 ? $"BEST  ·  WAVE {best}" : "";
            if (versionText != null) versionText.text = $"v{Application.version}";

            if (fader != null) { fader.alpha = 1f; StartCoroutine(Fade(fader, 0f, 1.2f)); }
            if (mainPanel != null) mainPanel.SetImmediate(true);
            if (modePanel != null) modePanel.SetImmediate(false);
            if (squadPanel != null) squadPanel.SetImmediate(false);
            RefreshToggles();
        }

        void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Escape)) return;
            if (options != null && options.IsOpen) options.Close();
            else if (squadPanel != null && squadPanel.IsVisible) CloseSquad();
            else if (modePanel != null && modePanel.IsVisible) CloseModes();
        }

        void RefreshToggles()
        {
            if (soundIcon != null)
            {
                bool on = !GameSettings.Muted;
                if (soundOnSprite != null && soundOffSprite != null) soundIcon.sprite = on ? soundOnSprite : soundOffSprite;
                soundIcon.color = on ? Color.white : offColor;
            }
            if (musicIcon != null) musicIcon.color = GameSettings.MusicVolume > 0.001f ? Color.white : offColor;
            if (vibrationIcon != null) vibrationIcon.color = GameSettings.Vibration ? Color.white : offColor;
        }

        void OpenOptions()
        {
            if (options == null) return;
            if (mainPanel != null) mainPanel.Hide();
            options.Open(() => { if (mainPanel != null) mainPanel.Show(); });
        }

        void OpenModes()
        {
            if (modePanel == null) { PlaySolo(); return; }
            if (mainPanel != null) mainPanel.Hide();
            modePanel.Show();
        }

        void CloseModes()
        {
            if (loading) return;
            if (modePanel != null) modePanel.Hide();
            if (mainPanel != null) mainPanel.Show();
        }

        void PlaySolo()
        {
            if (loading) return;
            loading = true;
            GameMode.Mode = MatchType.Solo;
            GameSettings.Save();
            StartCoroutine(LoadGame());
        }

        /// Squad: show the lobby with four slots (you + three empty). Online matchmaking
        /// fills the slots and starts the match once the squad is full.
        void OpenSquad()
        {
            if (loading || squadPanel == null) return;
            GameMode.Mode = MatchType.Squad;
            if (modePanel != null) modePanel.Hide();
            SetSquadSlots(1);
            if (squadStatusText != null) squadStatusText.text = "ONLINE SQUADS ARRIVE WITH THE MULTIPLAYER UPDATE";
            squadPanel.Show();
        }

        void CloseSquad()
        {
            if (loading) return;
            GameMode.Mode = MatchType.Solo;
            if (squadPanel != null) squadPanel.Hide();
            if (modePanel != null) modePanel.Show();
        }

        /// Lights up the first `joined` slots.
        public void SetSquadSlots(int joined)
        {
            if (squadSlots == null) return;
            for (int i = 0; i < squadSlots.Length; i++)
                if (squadSlots[i] != null) squadSlots[i].color = i < joined ? Color.white : emptySlotColor;
        }

        IEnumerator LoadGame()
        {
            if (mainPanel != null) mainPanel.Hide();
            var op = SceneManager.LoadSceneAsync(gameplayScene);
            if (op == null) { loading = false; yield break; }
            op.allowSceneActivation = false;
            if (fader != null) yield return Fade(fader, 1f, 0.5f);
            op.allowSceneActivation = true;
        }

        static IEnumerator Fade(CanvasGroup group, float to, float duration)
        {
            group.blocksRaycasts = to > 0f;
            float from = group.alpha;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                group.alpha = Mathf.Lerp(from, to, t / duration);
                yield return null;
            }
            group.alpha = to;
        }

        static void Quit()
        {
            GameSettings.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
