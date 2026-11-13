using UnityEngine;

namespace ZombieRush.UI.Kit
{
    /// Colours and motion of the "Outbreak" UI (Resources/ZombieRush/UI/KitStyle). The kit builder bakes
    /// the colours into the prefabs; the live widgets (heartbeat, ammo, tally, radar) read them at runtime.
    [CreateAssetMenu(fileName = "KitStyle", menuName = "Zombie Rush/UI Kit Style")]
    public class KitStyle : ScriptableObject
    {
        public const string ResourcePath = "ZombieRush/UI/KitStyle";

        [Header("Palette")]
        public Color amber = new Color(1f, 0.72f, 0.22f);
        public Color bone = new Color(0.95f, 0.92f, 0.86f);
        public Color olive = new Color(0.16f, 0.38f, 0.14f);
        public Color charcoal = new Color(0.05f, 0.05f, 0.05f);
        public Color blood = new Color(0.8f, 0.07f, 0.06f);
        public Color ink = new Color(0.45f, 0.03f, 0.03f);
        public Color inkDark = new Color(0.1f, 0.095f, 0.095f);
        public Color radar = new Color(0.52f, 0.95f, 0.38f);
        public Color muted = new Color(0.66f, 0.63f, 0.58f);
        public Color armor = new Color(0.45f, 0.75f, 0.95f);

        [Header("Heartbeat")]
        [Tooltip("Beats per minute at full health and at almost none.")]
        public Vector2 bpm = new Vector2(68f, 150f);
        [Tooltip("Below this share of health the trace turns amber, below lowHealth red.")]
        [Range(0f, 1f)] public float hurtHealth = 0.6f;
        [Range(0f, 1f)] public float lowHealth = 0.3f;

        [Header("Motion")]
        [Tooltip("How long a slapped-on tape or plate takes to settle, seconds.")]
        public float slapTime = 0.22f;
        [Tooltip("Punch size when a number changes (share of its size).")]
        public float punch = 0.25f;

        static KitStyle current;
        public static KitStyle Current
        {
            get
            {
                if (current != null) return current;
                current = Resources.Load<KitStyle>(ResourcePath);
                if (current == null) { current = CreateInstance<KitStyle>(); current.hideFlags = HideFlags.HideAndDontSave; }
                return current;
            }
        }
    }
}
