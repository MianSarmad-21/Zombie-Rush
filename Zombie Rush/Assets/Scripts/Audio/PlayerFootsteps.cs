using UnityEngine;
using ZombieRush.Player;

namespace ZombieRush.Audio
{
    /// Footsteps for the player this machine controls. SoundManager adds it when a level loads.
    /// One step per stride of ground covered (longer strides when sprinting), quieter while
    /// crouching, soft steps on the terrain and hard ones on everything else (roads, floors).
    /// Clips, volume and stride lengths live in the SoundLibrary.
    [RequireComponent(typeof(CharacterController))]
    public class PlayerFootsteps : MonoBehaviour
    {
        const float MinSpeed = 0.6f;        // m/s; slower than this is standing still
        const float GroundProbe = 0.6f;     // metres below the feet to look for the surface

        CharacterController body;
        PlayerController mover;
        float travelled;
        bool wasMoving;

        void Awake()
        {
            body = GetComponent<CharacterController>();
            mover = GetComponent<PlayerController>();
        }

        void Update()
        {
            var lib = SoundManager.Library;
            if (lib == null || body == null || !body.enabled) return;

            Vector3 v = body.velocity; v.y = 0f;
            float speed = v.magnitude;
            bool moving = body.isGrounded && speed > MinSpeed;
            if (!moving) { wasMoving = false; return; }

            float walk = mover != null ? mover.walkSpeed : 4f;
            float sprint = mover != null ? Mathf.Max(walk + 0.01f, mover.sprintSpeed) : 8f;
            float stride = Mathf.Lerp(lib.strideWalk, lib.strideSprint, Mathf.InverseLerp(walk, sprint, speed));

            // The first step lands half a stride after setting off, not a full one.
            if (!wasMoving) travelled = stride * 0.5f;
            wasMoving = true;
            travelled += speed * Time.deltaTime;
            if (travelled < stride) return;
            travelled -= stride;

            var set = OnTerrain() ? lib.footstepSoft : lib.footstepHard;
            if (set == null || set.Length == 0) set = lib.footstepHard != null && lib.footstepHard.Length > 0 ? lib.footstepHard : lib.footstepSoft;
            float volume = lib.footstepVolume * (mover != null && mover.IsCrouching ? lib.crouchStepVolume : 1f);
            SoundManager.Play(set, volume, 0.08f);
        }

        bool OnTerrain()
        {
            Vector3 from = transform.position + Vector3.up * 0.3f;
            return Physics.Raycast(from, Vector3.down, out var hit, 0.3f + GroundProbe, ~0, QueryTriggerInteraction.Ignore)
                   && hit.collider is TerrainCollider;
        }
    }
}
