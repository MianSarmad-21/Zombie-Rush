using UnityEngine;

namespace ZombieRush.Data
{
    /// Tuning for the Spitter: lobs sticky goo that hurts, slows the player and leaves a slowing puddle.
    [CreateAssetMenu(fileName = "SpitterConfig", menuName = "Zombie Rush/Special Zombies/Spitter Config")]
    public class SpitterConfig : ScriptableObject
    {
        [Header("Range")]
        [Tooltip("Closer than this it stops spitting and fights in melee.")]
        public float minRange = 4f;
        [Tooltip("Farthest it will spit from.")]
        public float maxRange = 18f;
        [Tooltip("Inside this distance (and beyond minRange) it stands its ground instead of closing in.")]
        public float keepDistance = 11f;
        public bool needsLineOfSight = true;

        [Header("Spit")]
        [Tooltip("Seconds from rearing back to the goo leaving its mouth.")]
        public float windup = 0.55f;
        public float cooldown = 3.2f;
        [Tooltip("Average flight speed, metres per second (sets the flight time).")]
        public float projectileSpeed = 14f;
        public float projectileGravity = 18f;
        public float projectileRadius = 0.16f;
        [Range(0f, 1f)] [Tooltip("How much of the player's movement it leads the shot by.")]
        public float lead = 0.6f;

        [Header("On hit")]
        public float directDamage = 8f;
        [Range(0.1f, 1f)] [Tooltip("Player speed multiplier while slimed.")]
        public float slowMultiplier = 0.55f;
        public float slowDuration = 2.5f;

        [Header("Puddle")]
        public float puddleRadius = 1.4f;
        public float puddleDuration = 6f;
        [Range(0.1f, 1f)] public float puddleSlow = 0.6f;
        [Tooltip("Damage per second while standing in the goo.")]
        public float puddleDps = 0f;
    }
}
