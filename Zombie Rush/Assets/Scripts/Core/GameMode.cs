using UnityEngine;

namespace ZombieRush.Core
{
    public enum MatchType { Solo, Squad }

    /// The mode picked on the main menu (Play > Solo / Squad). Lives for the session,
    /// so the gameplay scene knows how it was started.
    public static class GameMode
    {
        public const int SquadSize = 4;

        public static MatchType Mode { get; set; } = MatchType.Solo;
    }

    /// Keeps a squad's fight as tense as a solo run without multiplying it by 4:
    /// more zombies and tougher zombies per extra player, but each player's share
    /// of the horde shrinks as the squad grows (teammates cover each other).
    public static class Difficulty
    {
        // Extra zombies per wave for each player after the first (0.6 = +60%).
        public const float ExtraZombiesPerPlayer = 0.6f;
        // Extra zombie health for each player after the first (0.2 = +20%).
        public const float ExtraHealthPerPlayer = 0.2f;
        // Spawning speeds up by this much per extra player so big hordes still arrive on time.
        public const float FasterSpawnsPerPlayer = 0.25f;

        /// Solo 1.0x, duo 1.6x, trio 2.2x, squad of four 2.8x.
        public static float ZombieCountMultiplier(int players) => 1f + ExtraZombiesPerPlayer * Extra(players);

        /// Solo 1.0x ... squad of four 1.6x.
        public static float HealthMultiplier(int players) => 1f + ExtraHealthPerPlayer * Extra(players);

        /// Solo 1.0x ... squad of four 0.57x time between spawns.
        public static float SpawnIntervalMultiplier(int players) => 1f / (1f + FasterSpawnsPerPlayer * Extra(players));

        static int Extra(int players) => Mathf.Max(0, players - 1);
    }
}
