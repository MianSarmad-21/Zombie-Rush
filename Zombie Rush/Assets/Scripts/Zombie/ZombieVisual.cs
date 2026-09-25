using UnityEngine;

namespace ZombieRush.Zombie
{
    /// Configures the rigged model placed under this zombie in the prefab: assigns
    /// the Animator Controller and (optionally) one material for every renderer.
    /// Lets each zombie type reuse the same AI/health root with its own look/animations.
    public class ZombieVisual : MonoBehaviour
    {
        public RuntimeAnimatorController controller;
        public Material materialOverride;

        void Awake()
        {
            var animator = GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                Debug.LogError("ZombieVisual: no Animator found under " + name + " - is the model missing from the prefab?", this);
                return;
            }

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            if (materialOverride == null) return;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = materialOverride;
                r.sharedMaterials = mats;
            }
        }
    }
}
