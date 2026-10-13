using UnityEngine;

namespace ZombieRush.Zombie
{
    public static class ZombieAnimatorUtil
    {
        public static readonly int SpeedParam = Animator.StringToHash("Speed");
        public static readonly int AttackParam = Animator.StringToHash("Attack");
        public static readonly int HitParam = Animator.StringToHash("Hit");
        public static readonly int DieParam = Animator.StringToHash("Die");

        /// Allocates (Animator.parameters copies an array), so only call once and cache the result.
        public static bool Has(Animator animator, int nameHash, AnimatorControllerParameterType type)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return false;
            foreach (var p in animator.parameters)
                if (p.type == type && p.nameHash == nameHash) return true;
            return false;
        }
    }

    /// Resolves a zombie's Animator once and remembers which parameters its
    /// controller supports, so per-hit/per-frame calls cost nothing.
    public class ZombieAnimatorCache
    {
        readonly GameObject owner;
        bool resolved;

        public Animator Animator { get; private set; }
        public bool HasSpeed { get; private set; }
        public bool HasAttack { get; private set; }
        public bool HasHit { get; private set; }
        public bool HasDie { get; private set; }

        public ZombieAnimatorCache(GameObject owner) => this.owner = owner;

        /// Lazy because ZombieVisual may assign the controller in its own Awake,
        /// which can run after ours.
        public bool Resolve()
        {
            if (resolved) return Animator != null;

            Animator = owner.GetComponentInChildren<Animator>(true);
            if (Animator == null || Animator.runtimeAnimatorController == null) return false;

            resolved = true;
            HasSpeed = ZombieAnimatorUtil.Has(Animator, ZombieAnimatorUtil.SpeedParam, AnimatorControllerParameterType.Float);
            HasAttack = ZombieAnimatorUtil.Has(Animator, ZombieAnimatorUtil.AttackParam, AnimatorControllerParameterType.Trigger);
            HasHit = ZombieAnimatorUtil.Has(Animator, ZombieAnimatorUtil.HitParam, AnimatorControllerParameterType.Trigger);
            HasDie = ZombieAnimatorUtil.Has(Animator, ZombieAnimatorUtil.DieParam, AnimatorControllerParameterType.Trigger);
            return true;
        }

        public void SetSpeed(float value)
        {
            if (Resolve() && HasSpeed) Animator.SetFloat(ZombieAnimatorUtil.SpeedParam, value);
        }

        public bool TriggerAttack() => Trigger(HasAttack, ZombieAnimatorUtil.AttackParam);
        public bool TriggerHit() => Trigger(HasHit, ZombieAnimatorUtil.HitParam);

        static readonly int DieState = Animator.StringToHash("Die");

        /// Plays the death. Pending Hit/Attack triggers are cleared first: the controller's
        /// Any State transitions would otherwise pull the zombie straight back out of Die
        /// (several pellets landing in the same frame each set Hit before the killing one).
        public bool TriggerDie()
        {
            if (!Resolve()) return false;
            if (HasHit) Animator.ResetTrigger(ZombieAnimatorUtil.HitParam);
            if (HasAttack) Animator.ResetTrigger(ZombieAnimatorUtil.AttackParam);
            if (Animator.HasState(0, DieState))
            {
                Animator.CrossFadeInFixedTime(DieState, 0.1f, 0);
                return true;
            }
            return Trigger(HasDie, ZombieAnimatorUtil.DieParam);
        }

        bool Trigger(bool has, int hash)
        {
            if (!Resolve() || !has) return false;
            Animator.SetTrigger(hash);
            return true;
        }
    }
}
