using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ZombieRush.Core;

namespace ZombieRush.UI
{
    /// Title screen: Play, Multiplayer (coming soon), Options, Quit.
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

        bool loading;

        void Awake()
        {
            if (playButton != null) playButton.onClick.AddListener(Play);
            if (optionsButton != null) optionsButton.onClick.AddListener(OpenOptions);
            if (quitButton != null) quitButton.onClick.AddListener(Quit);
            if (multiplayerButton != null) multiplayerButton.interactable = false; // co-op is planned (GDD)

            // There's no "quit" on iOS, and Android players use the system back/home gesture.
            if (quitButton != null && Application.platform == RuntimePlatform.IPhonePlayer) quitButton.gameObject.SetActive(false);
        }

        void Start()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            int best = GameSettings.BestWave;
            if (bestWaveText != null) bestWaveText.text = best > 0 ? $"BEST  ·  WAVE {best}" : "";
            if (versionText != null) versionText.text = $"v{Application.version}";

            if (fader != null) { fader.alpha = 1f; StartCoroutine(Fade(fader, 0f, 1.2f)); }
            if (mainPanel != null) mainPanel.SetImmediate(true);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && options != null && options.IsOpen) options.Close();
        }

        void OpenOptions()
        {
            if (options == null) return;
            if (mainPanel != null) mainPanel.Hide();
            options.Open(() => { if (mainPanel != null) mainPanel.Show(); });
        }

        void Play()
        {
            if (loading) return;
            loading = true;
            StartCoroutine(LoadGame());
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
