using UnityEngine;

namespace ZombieRush.UI
{
    /// De-syncs idle animations of the main menu zombies so they don't move in lockstep.
    public class RandomAnimatorOffset : MonoBehaviour
    {
        void Start()
        {
            var animator = GetComponentInChildren<Animator>();
            if (animator == null) return;
            animator.speed = Random.Range(0.85f, 1.1f);
            animator.Update(Random.Range(0f, 3f));
        }
    }
}
