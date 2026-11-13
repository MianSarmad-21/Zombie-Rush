using UnityEngine;

namespace ZombieRush.Data
{
    /// The poison zombie's ranged spit, the toxic puddle it leaves, and the gas cloud its belly
    /// bursts into when it dies. Times marked "clip" must match Blender/clips_ghoul.py.
    [CreateAssetMenu(fileName = "PoisonConfig", menuName = "Zombie Rush/Poison Config")]
    public class PoisonConfig : ScriptableObject
    {
        [Header("Behaviour")]
        [Tooltip("Fights only from range: never claws; it stops and spits once it is within holdRange with a clear shot.")]
        public bool rangedOnly = true;
        [Tooltip("Ranged only: it walks up to this distance from the target, then stands and spits.")]
        public float holdRange = 10f;

        [Header("Spit")]
        [Tooltip("It spits only when the target is between these distances.")]
        public float spitMinRange = 6f;
        public float spitMaxRange = 15f;
        public float spitCooldown = 5f;
        [Tooltip("Seconds after it first sees a target before its first spit.")]
        public float firstSpitDelay = 1.5f;
        [Tooltip("Clip: seconds into the Spit clip when the glob leaves the mouth.")]
        public float releaseTime = 1.0f;
        [Tooltip("Clip: length of the Spit clip. The zombie stands still this long.")]
        public float spitDuration = 1.8f;

        [Header("Acid glob")]
        [Tooltip("Flight time grows with distance, between these (seconds).")]
        public Vector2 flightTime = new Vector2(0.55f, 1.15f);
        [Tooltip("Aims this share of the way towards where the target is heading.")]
        [Range(0f, 1f)] public float lead = 0.6f;
        public float directDamage = 12f;
        [Tooltip("A direct hit counts within this many metres of the target's body.")]
        public float directRadius = 0.9f;
        public float globSize = 0.22f;

        [Header("Toxic puddle (where a glob lands)")]
        public float puddleRadius = 1.6f;
        public float puddleDuration = 7f;
        public float puddleDamagePerSecond = 7f;

        [Header("Death burst")]
        [Tooltip("Clip: seconds into the Death clip when the belly bursts.")]
        public float burstDelay = 1.4f;
        public float cloudRadius = 3.2f;
        public float cloudDuration = 6f;
        public float cloudDamagePerSecond = 9f;

        [Header("Look")]
        public Color acidColor = new Color(0.55f, 1f, 0.15f, 1f);
        public Color cloudColor = new Color(0.40f, 0.85f, 0.15f, 0.35f);
        [Tooltip("Damage is dealt in ticks this many seconds apart.")]
        public float damageTick = 0.5f;
    }
}
