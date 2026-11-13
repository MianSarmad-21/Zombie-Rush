using UnityEngine;
using ZombieRush.Data;

namespace ZombieRush.Player
{
    /// Calms the player body's walk and run bounce. The locomotion clips lift and drop the hips by
    /// 11-17 cm per step, so in third person the whole body, head and gun bob hard. In the IK pass this
    /// keeps only a share of the bounce around the body's average height (PlayerViewConfig.bounceKeep)
    /// and pins both feet where the clip put them, so they never float or sink.
    /// Added at runtime by BodyGunPose; needs "IK Pass" on the controller's base layer (it has it).
    [RequireComponent(typeof(Animator))]
    public class PlayerBounceDamper : MonoBehaviour
    {
        Animator animator;
        PlayerViewSwitcher view;
        float average;
        bool ready;

        void Awake()
        {
            animator = GetComponent<Animator>();
            view = GetComponentInParent<PlayerViewSwitcher>();
        }

        void OnEnable() => ready = false;

        void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != 0 || animator == null || !animator.isHuman) return;
            var cfg = view != null ? view.config : null;
            float keep = cfg != null ? cfg.bounceKeep : 0.4f;
            if (keep >= 1f) { ready = false; return; }

            Vector3 up = transform.up;
            float h = Vector3.Dot(animator.bodyPosition - transform.position, up);
            float smooth = cfg != null ? Mathf.Max(0.01f, cfg.bounceSmooth) : 0.3f;
            if (!ready) { average = h; ready = true; }
            average = Mathf.Lerp(average, h, 1f - Mathf.Exp(-Time.deltaTime / smooth));
            float shift = average + (h - average) * keep - h;
            if (Mathf.Abs(shift) < 1e-5f) return;

            // the feet where the clip put them, before the body moves
            Vector3 lp = animator.GetIKPosition(AvatarIKGoal.LeftFoot), rp = animator.GetIKPosition(AvatarIKGoal.RightFoot);
            Quaternion lr = animator.GetIKRotation(AvatarIKGoal.LeftFoot), rr = animator.GetIKRotation(AvatarIKGoal.RightFoot);
            animator.bodyPosition += up * shift;
            Pin(AvatarIKGoal.LeftFoot, lp, lr);
            Pin(AvatarIKGoal.RightFoot, rp, rr);
        }

        void Pin(AvatarIKGoal goal, Vector3 pos, Quaternion rot)
        {
            animator.SetIKPositionWeight(goal, 1f);
            animator.SetIKPosition(goal, pos);
            animator.SetIKRotationWeight(goal, 1f);
            animator.SetIKRotation(goal, rot);
        }
    }
}
