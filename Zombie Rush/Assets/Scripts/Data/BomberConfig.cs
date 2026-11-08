using UnityEngine;

namespace ZombieRush.Data
{
    /// Tuning for the Bomber: rushes the player, lights its fuse when close and blows up.
    /// Killing it also sets it off, so it is best shot from a distance.
    [CreateAssetMenu(fileName = "BomberConfig", menuName = "Zombie Rush/Special Zombies/Bomber Config")]
    public class BomberConfig : ScriptableObject
    {
        [Header("Fuse")]
        [Tooltip("Lights the fuse when the player is this close.")]
        public float triggerRange = 3.5f;
        [Tooltip("Seconds from lighting the fuse to the blast.")]
        public float fuseTime = 1.6f;
        [Tooltip("Move speed multiplier once the fuse is lit.")]
        public float litSpeedMultiplier = 1.35f;
        [Tooltip("Delay before a bomber that was shot dead goes off.")]
        public float deathDelay = 0.15f;

        [Header("Blast")]
        public float radius = 5f;
        [Tooltip("Damage to a player at the centre of the blast (scaled by wave difficulty).")]
        public float damage = 45f;
        [Range(0f, 1f)] [Tooltip("Share of the damage left at the edge of the radius.")]
        public float edgeDamage = 0.25f;
        [Tooltip("Damage to other zombies caught in the blast.")]
        public float zombieDamage = 120f;
        public float cameraShake = 0.9f;

        [Header("Warning light")]
        [Tooltip("Blink interval before the fuse is lit.")]
        public float blinkIdle = 0.9f;
        [Tooltip("Blink interval just before the blast.")]
        public float blinkFast = 0.07f;
        [ColorUsage(false, true)] public Color lightColor = new Color(4f, 0.25f, 0.15f);
    }
}
