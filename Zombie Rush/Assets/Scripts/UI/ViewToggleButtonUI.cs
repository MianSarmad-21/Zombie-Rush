using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Core;
using ZombieRush.Player;

namespace ZombieRush.UI
{
    /// HUD button that switches first person / third person (PC key hint or touch button).
    /// The press itself goes through the TouchButton on the same button (TouchAction.ToggleView);
    /// this only shows the right control scheme and names the view a press switches to.
    ///
    /// Keep this component on an always-active object; it shows/hides `content`.
    public class ViewToggleButtonUI : MonoBehaviour
    {
        public GameObject content;
        public SchemeFilter showOn = SchemeFilter.Any;
        public Text label;
        public string toThirdPersonText = "3RD PERSON";
        public string toFirstPersonText = "1ST PERSON";

        void Awake()
        {
            GameSettings.Changed += Refresh;
            PlayerViewSwitcher.Changed += HandleChanged;
        }

        void OnDestroy()
        {
            GameSettings.Changed -= Refresh;
            PlayerViewSwitcher.Changed -= HandleChanged;
        }

        void Start() => Refresh();

        void HandleChanged(CharacterView _) => Refresh();

        bool SchemeMatches => showOn == SchemeFilter.Any || (showOn == SchemeFilter.TouchOnly) == GameSettings.UseTouchControls;

        void Refresh()
        {
            var switcher = PlayerViewSwitcher.Local;
            bool visible = switcher != null && SchemeMatches;
            if (content != null && content.activeSelf != visible) content.SetActive(visible);
            if (label != null && switcher != null) label.text = switcher.IsThirdPerson ? toFirstPersonText : toThirdPersonText;
        }
    }
}
