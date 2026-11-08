using UnityEngine;
using ZombieRush.Player;

namespace ZombieRush.Zombie
{
    /// A special zombie's own move (spit, shriek, fuse). ZombieAI finds it on the same
    /// GameObject and asks it every frame before chasing or attacking.
    public interface IZombieAbility
    {
        /// Called every frame the zombie hunts a player outside the safe base.
        /// Return true to hold still this frame (the ability is busy or keeping its distance).
        bool Tick(PlayerHealth target, Vector3 toTarget, float distance);

        /// True: the zombie never melee-attacks (the ability is its attack).
        bool BlocksMelee { get; }

        /// Extra move speed factor from the ability (1 = none).
        float SpeedMultiplier { get; }
    }
}
