using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Weapons;

namespace ZombieRush.UI
{
    /// Grenade counter on the HUD (grenade icon + "x2"), dimmed at zero, and a fuse ring that runs down while a
    /// grenade is cooked. Built by Zombie Rush > Weapons > Set Up Grenade (GrenadeTools) next to the ammo panel.
    public class GrenadeHUD : MonoBehaviour
    {
        public Image icon;
        public Text countText;
        [Tooltip("Radial fill that empties as the fuse runs out while cooking.")]
        public Image fuseRing;
        public Color fullColor = new Color(1f, 0.82f, 0.35f, 1f);
        public Color emptyColor = new Color(1f, 1f, 1f, 0.3f);
        public Color fuseSafe = new Color(1f, 0.85f, 0.3f, 0.95f);
        public Color fuseDanger = new Color(1f, 0.25f, 0.15f, 1f);

        void OnEnable()
        {
            GrenadeThrower.CountChanged += Show;
            var t = GrenadeThrower.Local;
            var cfg = Data.GrenadeConfig.Get();
            Show(t != null ? t.Count : 0, cfg != null ? cfg.maxCarry : 0);
        }

        void OnDisable() => GrenadeThrower.CountChanged -= Show;

        void Show(int count, int max)
        {
            if (countText != null) { countText.text = "x" + count; countText.color = count > 0 ? fullColor : emptyColor; }
            if (icon != null) icon.color = count > 0 ? Color.white : emptyColor;
        }

        void Update()
        {
            if (fuseRing == null) return;
            var t = GrenadeThrower.Local;
            var cfg = Data.GrenadeConfig.Get();
            bool cooking = t != null && t.Cooking && cfg != null && cfg.fuse > 0f;
            if (fuseRing.enabled != cooking) fuseRing.enabled = cooking;
            if (!cooking) return;
            float k = t.FuseLeft / cfg.fuse;
            fuseRing.fillAmount = k;
            fuseRing.color = Color.Lerp(fuseDanger, fuseSafe, Mathf.Clamp01(k * 1.6f));
        }
    }
}
