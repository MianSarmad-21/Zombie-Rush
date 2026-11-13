using ZombieRush.Data;

namespace ZombieRush.Core
{
    /// The current wave's balance, for code outside the wave loop (guns, player health).
    /// WaveManager sets it before each wave is announced; outside a wave it is wave 1 (base values).
    public static class WaveScaling
    {
        static WaveBalanceConfig balance;

        public static WaveBalanceConfig Balance
        {
            get => balance != null ? balance : WaveBalanceConfig.Defaults;
            set => balance = value;
        }

        /// The wave being fought (1 before the first wave starts).
        public static int Wave { get; set; } = 1;

        /// Multiplier on every gun's WeaponData.damage this wave.
        public static float PlayerDamage => Balance.PlayerDamage(Wave);
    }
}
