using UnityEngine;

namespace ZombieRush.Zombie
{
    public static class ZombieAnimatorUtil
    {
        public const string SpeedParam = "Speed";
        public const string AttackParam = "Attack";
        public const string HitParam = "Hit";
        public const string DieParam = "Die";

        public static bool Has(Animator animator, string name, AnimatorControllerParameterType type)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return false;
            foreach (var p in animator.parameters)
                if (p.type == type && p.name == name) return true;
            return false;
        }
    }
}
