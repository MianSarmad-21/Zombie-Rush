using UnityEngine;

namespace ZombieRush.Data
{
    /// Look of the blood and poison effects. Lives at Resources/ZombieRush/Fx/EffectsConfig (loaded at
    /// runtime; the defaults below are used when it is missing).
    [CreateAssetMenu(fileName = "EffectsConfig", menuName = "Zombie Rush/Effects Config")]
    public class EffectsConfig : ScriptableObject
    {
        public const string ResourcePath = "ZombieRush/Fx/EffectsConfig";

        [Header("Blood (on every hit)")]
        public Color bloodColor = new Color(0.20f, 0.012f, 0.015f, 1f);
        [Tooltip("Droplets thrown out of the wound, along the shot.")]
        public Vector2Int hitDrops = new Vector2Int(10, 16);
        public Vector2 dropSpeed = new Vector2(2.5f, 6f);
        public Vector2 dropSize = new Vector2(0.025f, 0.065f);
        [Tooltip("Width of the spray cone round the shot direction, degrees.")]
        public float sprayCone = 35f;
        [Tooltip("Puffs of blood mist at the wound.")]
        public int mistPuffs = 3;
        public float mistSize = 0.5f;
        [Range(0f, 1f)] public float mistAlpha = 0.75f;
        [Tooltip("Chance a hit leaves a splat on the ground under the zombie.")]
        [Range(0f, 1f)] public float hitSplatChance = 0.45f;
        public Vector2 hitSplatSize = new Vector2(0.45f, 0.9f);
        public float deathSplatSize = 1.6f;
        [Tooltip("Seconds a ground splat stays before fading (the fade lasts splatFade).")]
        public float splatLife = 14f;
        public float splatFade = 3f;

        [Header("Poison")]
        [Tooltip("Goo puddle size as a share of the toxic zone's diameter.")]
        public float puddleScale = 1.1f;
        [Tooltip("Gas puff alpha in the cloud.")]
        [Range(0f, 1f)] public float gasAlpha = 0.35f;
        [Tooltip("Goo chunks flung out by the death burst.")]
        public int burstChunks = 26;
        public int burstDrops = 60;
        public float burstSpeed = 7f;
        public float burstSplatSize = 3.2f;

        static EffectsConfig current;
        public static EffectsConfig Current
        {
            get
            {
                if (current != null) return current;
                current = Resources.Load<EffectsConfig>(ResourcePath);
                if (current == null) { current = CreateInstance<EffectsConfig>(); current.hideFlags = HideFlags.HideAndDontSave; }
                return current;
            }
        }
    }
}
