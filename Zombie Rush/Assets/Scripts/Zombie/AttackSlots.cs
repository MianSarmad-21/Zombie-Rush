using System.Collections.Generic;
using UnityEngine;
using ZombieRush.Core;
using ZombieRush.Player;

namespace ZombieRush.Zombie
{
    /// How many zombies may swing at one player at the same moment (WaveBalanceConfig.MaxAttackers). A zombie
    /// takes a slot for the length of its swing; with every slot taken it waits a moment and tries again.
    /// Keeps a surrounded player from losing half their health in one instant, while a crowd stays dangerous.
    public static class AttackSlots
    {
        static readonly Dictionary<PlayerHealth, List<float>> busyUntil = new Dictionary<PlayerHealth, List<float>>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => busyUntil.Clear();

        /// True (and the slot is held for `hold` seconds) if this zombie may swing at `target` now.
        public static bool TryTake(PlayerHealth target, float hold)
        {
            if (target == null) return true;
            if (!busyUntil.TryGetValue(target, out var slots))
            {
                PruneDestroyed();
                busyUntil[target] = slots = new List<float>();
            }
            float now = Time.time;
            for (int i = slots.Count - 1; i >= 0; i--)
                if (slots[i] <= now) slots.RemoveAt(i);
            if (slots.Count >= WaveScaling.Balance.MaxAttackers(WaveScaling.Wave)) return false;
            slots.Add(now + Mathf.Max(0.1f, hold));
            return true;
        }

        // Players from a reloaded level compare equal to null: drop them.
        static readonly List<PlayerHealth> dead = new List<PlayerHealth>();
        static void PruneDestroyed()
        {
            dead.Clear();
            foreach (var key in busyUntil.Keys) if (key == null) dead.Add(key);
            foreach (var key in dead) busyUntil.Remove(key);
        }
    }
}
