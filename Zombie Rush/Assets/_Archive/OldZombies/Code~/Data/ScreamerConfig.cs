using UnityEngine;

namespace ZombieRush.Data
{
    /// Tuning for the Screamer: hangs back and shrieks, sending every zombie around it into a frenzy.
    [CreateAssetMenu(fileName = "ScreamerConfig", menuName = "Zombie Rush/Special Zombies/Screamer Config")]
    public class ScreamerConfig : ScriptableObject
    {
        [Header("Range")]
        [Tooltip("Shrieks when the player is within this distance.")]
        public float triggerRange = 16f;
        [Tooltip("Inside this distance (and beyond meleeRange) it stands its ground instead of closing in.")]
        public float keepDistance = 12f;
        [Tooltip("Closer than this it stops holding back and claws in melee.")]
        public float meleeRange = 3f;

        [Header("Shriek")]
        [Tooltip("Seconds from rearing back to the shriek going off.")]
        public float windup = 0.35f;
        [Tooltip("How long the shriek pose lasts.")]
        public float duration = 1.5f;
        public float cooldown = 9f;
        [Tooltip("Zombies inside this radius are frenzied.")]
        public float radius = 14f;

        [Header("Frenzy")]
        [Tooltip("Move and attack speed multiplier for frenzied zombies.")]
        public float speedMultiplier = 1.45f;
        public float frenzyDuration = 6f;

        [Header("Feel")]
        public float cameraShake = 0.5f;
        public float shakeRange = 25f;
    }
}
