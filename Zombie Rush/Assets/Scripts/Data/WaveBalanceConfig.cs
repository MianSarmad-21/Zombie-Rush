using UnityEngine;

namespace ZombieRush.Data
{
    /// How the fight grows wave by wave, for both sides. Every value is "per wave after the first",
    /// so wave 1 always plays with the base numbers (zombie configs, PlayerHealth.maxHealth, WeaponData.damage).
    ///
    /// The balance: zombies get tougher faster than the player's guns get stronger, so each wave takes
    /// longer to clear (plus WaveManager adds more zombies each wave). Zombie hits grow slower than the
    /// player's health, so a late-wave zombie still needs about as many hits to kill as an early one:
    /// the pressure comes from numbers and toughness, never from being one-shot.
    [CreateAssetMenu(fileName = "WaveBalanceConfig", menuName = "Zombie Rush/Wave Balance Config")]
    public class WaveBalanceConfig : ScriptableObject
    {
        [Header("Zombies")]
        [Tooltip("Extra zombie health per wave (0.12 = +12% of base each wave).")]
        public float zombieHealthPerWave = 0.12f;
        [Tooltip("Most zombie health can grow to (times base).")]
        public float zombieHealthMax = 4f;
        [Tooltip("Extra zombie hit damage per wave.")]
        public float zombieDamagePerWave = 0.06f;
        [Tooltip("Most zombie hit damage can grow to (times base).")]
        public float zombieDamageMax = 2.2f;
        [Tooltip("Extra zombie walk/run speed per wave.")]
        public float zombieSpeedPerWave = 0.03f;
        [Tooltip("Most zombie speed can grow to (times base).")]
        public float zombieSpeedMax = 1.3f;

        [Header("Player")]
        [Tooltip("Extra max health per wave (health points). The player is healed to full at each wave start.")]
        public float playerHealthPerWave = 6f;
        [Tooltip("Most max health the player can reach.")]
        public float playerHealthMax = 200f;
        [Tooltip("Extra gun damage per wave.")]
        public float playerDamagePerWave = 0.05f;
        [Tooltip("Most gun damage can grow to (times WeaponData.damage).")]
        public float playerDamageMax = 1.6f;

        static float Grow(int wave, float perWave, float max) => Mathf.Min(max, 1f + Mathf.Max(0, wave - 1) * perWave);

        public float ZombieHealth(int wave) => Grow(wave, zombieHealthPerWave, zombieHealthMax);
        public float ZombieDamage(int wave) => Grow(wave, zombieDamagePerWave, zombieDamageMax);
        public float ZombieSpeed(int wave) => Grow(wave, zombieSpeedPerWave, zombieSpeedMax);
        public float PlayerDamage(int wave) => Grow(wave, playerDamagePerWave, playerDamageMax);
        public float PlayerMaxHealth(float baseHealth, int wave) =>
            Mathf.Min(Mathf.Max(baseHealth, playerHealthMax), baseHealth + Mathf.Max(0, wave - 1) * playerHealthPerWave);

        static WaveBalanceConfig defaults;

        /// Fallback when no asset is assigned, so gameplay code never needs its own copy of these numbers.
        public static WaveBalanceConfig Defaults
        {
            get
            {
                if (defaults == null)
                {
                    defaults = CreateInstance<WaveBalanceConfig>();
                    defaults.hideFlags = HideFlags.HideAndDontSave;
                }
                return defaults;
            }
        }
    }
}
