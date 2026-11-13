using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Core;

namespace ZombieRush.Net
{
    /// The first screen: SOLO, HOST SQUAD (shows a code to share) or JOIN (type a friend's code).
    /// Built from code at runtime on its own canvas, scaled for phones and PCs. Once a match runs it
    /// shrinks to a small corner label with the squad code and how many players are in.
    /// Test switches on the command line: -zrhost, -zrjoin <ip>, -zrsolo.
    public class NetLobbyUI : MonoBehaviour
    {
        static NetLobbyUI instance;

        GameObject panel, corner;
        Text status, cornerText;
        InputField codeField;
        Button soloButton, hostButton, joinButton;
        Font font;
        bool busy;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (instance != null || GameManager.Instance == null) return;
            var go = new GameObject("NetLobbyUI");
            instance = go.AddComponent<NetLobbyUI>();
        }

        void Start()
        {
            var any = FindAnyObjectByType<Text>();
            font = any != null ? any.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Build();
            NetSession.Status += OnStatus;

            // already running (the level was restarted inside a match): straight into play
            if (NetSession.IsRunning) { EnterMatch(); return; }
            if (!GameManager.StartInLobby) { Solo(); return; }

            var args = System.Environment.GetCommandLineArgs();
            foreach (var a in args) if (a.StartsWith("-zr")) { Application.runInBackground = true; break; }   // test runs: several windows at once
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-zrhost") { if (NetSession.StartDirectHost()) EnterMatch(); return; }
                if (args[i] == "-zrsolo") { Solo(); return; }
                if (args[i] == "-zrjoin" && i + 1 < args.Length) { if (NetSession.JoinDirect(args[i + 1])) EnterMatch(); StartTestShots(args); return; }
                if (args[i] == "-zrrelayhost" && i + 1 < args.Length) { RelayHostTest(args[i + 1]); StartTestShots(args); return; }
                if (args[i] == "-zrcodefile" && i + 1 < args.Length) { RelayJoinTest(args[i + 1]); StartTestShots(args); return; }
            }
        }

        /// Test switches for an online (Relay) squad between two builds: -zrrelayhost <file> hosts and writes
        /// the join code to the file; -zrcodefile <file> waits for that file and joins with its code.
        async void RelayHostTest(string file)
        {
            string code = await NetSession.HostSquadAsync();
            if (code != null) { System.IO.File.WriteAllText(file, code); EnterMatch(); }
        }

        async void RelayJoinTest(string file)
        {
            for (int i = 0; i < 120 && !System.IO.File.Exists(file); i++) await System.Threading.Tasks.Task.Delay(500);
            if (!System.IO.File.Exists(file)) { OnStatus("no code file"); return; }
            if (await NetSession.JoinSquadAsync(System.IO.File.ReadAllText(file).Trim())) EnterMatch();
        }

        /// Test switch: -zrshots <folder> takes a screenshot every 2 s (20 shots) and walks forward a little,
        /// so a test can see what a joined client sees.
        void StartTestShots(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-zrshots") { StartCoroutine(TestShots(args[i + 1])); return; }
        }

        System.Collections.IEnumerator TestShots(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            for (int k = 0; k < 20; k++)
            {
                yield return new WaitForSeconds(2f);
                GameInput.SetTouchMove(k >= 4 && k < 7 ? new Vector2(0f, 1f) : (k >= 9 && k < 11 ? new Vector2(1f, 0f) : Vector2.zero));
                if (k == 12) GameInput.TouchPress(TouchAction.Fire);
                if (k == 13) GameInput.TouchRelease(TouchAction.Fire);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"client_{k:00}.png"));
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "client.log"),
                    $"{k}: running {NetSession.IsRunning} server {NetSession.IsServer} players {NetSession.PlayerCount} netplayers {NetPlayer.All.Count} " +
                    $"copies {string.Join(",", NetPlayer.All.ConvertAll(p => p.Copy != null ? p.Copy.name + "@" + p.Copy.transform.position.ToString("F1") : "-"))} " +
                    $"state {(GameManager.Instance != null ? GameManager.Instance.State.ToString() : "-")}\n");
            }
            GameInput.SetTouchMove(Vector2.zero);
            Application.Quit();
        }

        void OnDestroy()
        {
            NetSession.Status -= OnStatus;
            if (instance == this) instance = null;
        }

        void OnStatus(string s) { if (status != null) status.text = s.ToUpperInvariant(); }

        // ---------------------------------------------------------------- actions

        void Solo()
        {
            if (busy) return;
            if (NetSession.StartSolo()) EnterMatch();
        }

        async void Host()
        {
            if (busy) return;
            busy = true; SetButtons(false);
            string code = await NetSession.HostSquadAsync();
            busy = false;
            if (code != null) EnterMatch(); else SetButtons(true);
        }

        async void Join()
        {
            if (busy) return;
            string code = codeField != null ? codeField.text.Trim() : "";
            if (code.Length < 4) { OnStatus("Type the squad code first."); return; }
            busy = true; SetButtons(false);
            bool ok = await NetSession.JoinSquadAsync(code);
            busy = false;
            if (ok) EnterMatch(); else SetButtons(true);
        }

        void EnterMatch()
        {
            panel.SetActive(false);
            corner.SetActive(NetSession.InSquad);
            if (GameManager.Instance != null) GameManager.Instance.BeginMatch();
        }

        void Update()
        {
            if (corner != null && corner.activeSelf)
                cornerText.text = $"SQUAD {NetSession.JoinCode}   {NetSession.PlayerCount}/{(NetConfig.Current != null ? NetConfig.Current.maxPlayers : 4)}";
        }

        void SetButtons(bool on)
        {
            soloButton.interactable = on; hostButton.interactable = on; joinButton.interactable = on;
        }

        // ---------------------------------------------------------------- layout (anchors only: scales on any screen)

        /// The lobby prefab (Resources/ZombieRush/UI/Lobby, built by the UI kit tool and editable); without it
        /// a plain screen is built from code.
        const string PrefabPath = "ZombieRush/UI/Lobby";

        void Build()
        {
            var prefab = Resources.Load<LobbyView>(PrefabPath);
            if (prefab != null)
            {
                var v = Instantiate(prefab, transform);
                panel = v.panel; corner = v.squadTag; status = v.status; cornerText = v.squadText; codeField = v.codeField;
                soloButton = v.soloButton; hostButton = v.hostButton; joinButton = v.joinButton;
                soloButton.onClick.AddListener(Solo); hostButton.onClick.AddListener(Host); joinButton.onClick.AddListener(Join);
                if (codeField != null) codeField.onValueChanged.AddListener(t => { string u = t.ToUpperInvariant(); if (u != t) codeField.text = u; });
                if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                    new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));
                corner.SetActive(false);
                return;
            }
            var canvasGo = new GameObject("LobbyCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 500;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 1f;
            if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));

            panel = Box(canvasGo.transform, "Lobby", new Vector2(0f, 0f), new Vector2(1f, 1f), new Color(0.02f, 0.02f, 0.03f, 0.82f));
            var card = Box(panel.transform, "Card", new Vector2(0.3f, 0.16f), new Vector2(0.7f, 0.84f), new Color(0.07f, 0.07f, 0.08f, 0.96f));
            Label(card.transform, "ZOMBIE RUSH", new Vector2(0.05f, 0.82f), new Vector2(0.95f, 0.97f), 74, new Color(0.95f, 0.75f, 0.25f));
            Label(card.transform, "PLAY ALONE OR WITH UP TO 3 FRIENDS", new Vector2(0.05f, 0.74f), new Vector2(0.95f, 0.82f), 26, new Color(0.8f, 0.8f, 0.8f));
            soloButton = MakeButton(card.transform, "SOLO", new Vector2(0.1f, 0.58f), new Vector2(0.9f, 0.71f), new Color(0.55f, 0.08f, 0.07f), Solo);
            hostButton = MakeButton(card.transform, "HOST SQUAD", new Vector2(0.1f, 0.42f), new Vector2(0.9f, 0.55f), new Color(0.12f, 0.35f, 0.15f), Host);
            codeField = MakeInput(card.transform, new Vector2(0.1f, 0.26f), new Vector2(0.55f, 0.39f));
            joinButton = MakeButton(card.transform, "JOIN", new Vector2(0.58f, 0.26f), new Vector2(0.9f, 0.39f), new Color(0.12f, 0.25f, 0.45f), Join);
            status = Label(card.transform, "", new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.22f), 26, new Color(0.85f, 0.85f, 0.85f));

            corner = Box(canvasGo.transform, "SquadCode", new Vector2(0.38f, 0.955f), new Vector2(0.62f, 0.995f), new Color(0f, 0f, 0f, 0.55f));
            cornerText = Label(corner.transform, "", Vector2.zero, Vector2.one, 24, Color.white);
            corner.SetActive(false);
        }

        GameObject Box(Transform parent, string name, Vector2 min, Vector2 max, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = color;
            return go;
        }

        Text Label(Transform parent, string text, Vector2 min, Vector2 max, int size, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = go.GetComponent<Text>();
            t.font = font; t.text = text; t.fontSize = size; t.color = color; t.alignment = TextAnchor.MiddleCenter;
            t.resizeTextForBestFit = true; t.resizeTextMaxSize = size; t.resizeTextMinSize = 10;
            t.raycastTarget = false;
            return t;
        }

        Button MakeButton(Transform parent, string text, Vector2 min, Vector2 max, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var go = Box(parent, text, min, max, color);
            var b = go.AddComponent<Button>();
            b.onClick.AddListener(onClick);
            Label(go.transform, text, Vector2.zero, Vector2.one, 40, Color.white);
            return b;
        }

        InputField MakeInput(Transform parent, Vector2 min, Vector2 max)
        {
            var go = Box(parent, "Code", min, max, new Color(0.9f, 0.9f, 0.9f, 1f));
            var input = go.AddComponent<InputField>();
            var txt = Label(go.transform, "", new Vector2(0.05f, 0f), new Vector2(0.95f, 1f), 40, Color.black);
            txt.supportRichText = false; txt.raycastTarget = false;
            var hint = Label(go.transform, "SQUAD CODE", new Vector2(0.05f, 0f), new Vector2(0.95f, 1f), 30, new Color(0f, 0f, 0f, 0.4f));
            input.textComponent = txt; input.placeholder = hint;
            input.characterLimit = 12;
            input.characterValidation = InputField.CharacterValidation.Alphanumeric;
            input.onValueChanged.AddListener(v => { string u = v.ToUpperInvariant(); if (u != v) input.text = u; });
            return input;
        }
    }
}
