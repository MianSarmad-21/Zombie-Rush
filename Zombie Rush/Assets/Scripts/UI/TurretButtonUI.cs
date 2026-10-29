using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Core;
using ZombieRush.Weapons;

namespace ZombieRush.UI
{
    /// HUD turret button (PC key hint or touch button): padlock + price while the
    /// turret is not bought, a draining dark sweep while it recharges, gold when ready.
    /// Polls TurretDeployer.Local; a few float compares per frame, no allocations.
    ///
    /// Keep this component on an always-active object; it shows/hides `content`.
    public class TurretButtonUI : MonoBehaviour
    {
        public GameObject content;
        public SchemeFilter showOn = SchemeFilter.Any;
        public Image icon;
        public Image cooldownFill;   // radial/filled image drawn over the icon
        public GameObject lockBadge;
        public Text label;           // "TURRET" / "$1.99" / "12s"
        public Graphic frame;
        public string readyText = "TURRET";

        int lastSeconds = -1;
        bool lastOwned, lastReady, first = true;

        void Awake() => GameSettings.Changed += RefreshVisibility;
        void OnDestroy() => GameSettings.Changed -= RefreshVisibility;
        void Start() => RefreshVisibility();

        bool SchemeMatches => showOn == SchemeFilter.Any || (showOn == SchemeFilter.TouchOnly) == GameSettings.UseTouchControls;

        void RefreshVisibility()
        {
            bool visible = TurretDeployer.Local != null && SchemeMatches;
            if (content != null && content.activeSelf != visible) content.SetActive(visible);
        }

        void Update()
        {
            var d = TurretDeployer.Local;
            if (d == null) { RefreshVisibility(); return; }
            if (content != null && !content.activeSelf) return;

            bool owned = d.Owned;
            float left = d.CooldownLeft;
            bool ready = owned && left <= 0f;
            int seconds = Mathf.CeilToInt(left);

            if (cooldownFill != null) cooldownFill.fillAmount = owned ? d.Cooldown01 : 0f;
            if (!first && owned == lastOwned && ready == lastReady && seconds == lastSeconds) return;
            first = false;
            lastOwned = owned; lastReady = ready; lastSeconds = seconds;

            if (lockBadge != null) lockBadge.SetActive(!owned);
            if (label != null) label.text = !owned ? d.Price : ready ? readyText : seconds + "s";
            Color c = !owned ? UITheme.Bone : ready ? UITheme.Gold : UITheme.Muted;
            if (label != null) label.color = c;
            if (frame != null) frame.color = c;
            if (icon != null) icon.color = ready || !owned ? Color.white : new Color(0.6f, 0.6f, 0.6f, 1f);
        }
    }
}
