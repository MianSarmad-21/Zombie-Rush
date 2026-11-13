using UnityEngine;
using UnityEngine.UI;

namespace ZombieRush.Net
{
    /// The lobby screen's parts (Resources/ZombieRush/UI/Lobby prefab, editable in the editor). NetLobbyUI
    /// wires the buttons and shows the status; the squad tag stays on screen during a squad match.
    public class LobbyView : MonoBehaviour
    {
        public GameObject panel;
        public Button soloButton;
        public Button hostButton;
        public Button joinButton;
        public InputField codeField;
        public Text status;
        [Tooltip("Shown at the top of the screen during a squad match: the code and how many are in.")]
        public GameObject squadTag;
        public Text squadText;
    }
}
