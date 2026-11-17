using System;
using UnityEngine;
using ZombieRush.Core;
using ZombieRush.Data;

namespace ZombieRush.Player
{
    /// Switches the local player between first person and third person (V key or the HUD button).
    /// Third person: an over-the-shoulder camera from a pivot on the player's capsule (not a bone),
    /// right shoulder by default (Q / shoulder button swaps), kept out of walls (pulls in at once, eases
    /// back out), and moving in with a narrower view while aiming (right mouse / aim button). It also
    /// sets the field of view for both views, shows the body (CharacterViewMode) and hides the
    /// first-person gun models under the camera. Shots still go through the screen centre.
    ///
    /// Runs its LateUpdate before CameraShake, so shake still adds on top of the camera position.
    [DefaultExecutionOrder(-1100)]
    public class PlayerViewSwitcher : MonoBehaviour
    {
        public PlayerViewConfig config;
        public Transform cameraPivot;
        public Camera playerCamera;
        public CharacterViewMode body;
        [Tooltip("First-person gun objects (under the camera). Their renderers are hidden in third person.")]
        public Transform[] firstPersonModels;

        /// The local player's switcher (HUD reads it).
        public static PlayerViewSwitcher Local { get; private set; }
        public static event Action<CharacterView> Changed;

        CharacterView view;
        float blend;           // 0 = first person, 1 = third person
        Vector3 fpLocalPos;    // camera's own first-person spot under the pivot
        Renderer[][] fpRenderers;
        readonly RaycastHit[] hits = new RaycastHit[16];
        Vector3 smoothHead, lagVelocity;
        bool lagReady;
        float side = 1f, sideNow = 1f;   // +1 right shoulder, -1 left
        float aimBlend;                  // 0..1 how far into aiming
        float wallLength = -1f;          // allowed camera distance behind the shoulder (eases back out after a wall)
        bool scoped;                     // looking through a scope: camera at the eye, body and arms out of view
        PlayerWeaponController weapons;

        public CharacterView View => view;
        public bool IsThirdPerson => view == CharacterView.ThirdPerson;
        /// First person with the camera fully back at the head (no third-person blend left).
        public bool FullyFirstPerson => view == CharacterView.FirstPerson && blend <= 0f;
        /// 0..1: how far into aiming (zoomed) the view is.
        public float AimAmount => aimBlend * aimBlend * (3f - 2f * aimBlend);
        /// +1 camera over the right shoulder, -1 over the left (eased).
        public float ShoulderSide => sideNow;
        /// Looking through the held gun's scope (aiming with a scoped gun): the scope overlay shows,
        /// the camera sits at the eye even in third person, and the body and first-person arms are hidden.
        public bool Scoped => scoped;
        /// Scoped with the camera fully at the eye (also from third person): the first-person arms and gun show.
        public bool ScopedAtEye => scoped && blend <= 0f;
        /// Aiming at the eye with a SCOPED gun (the scope sight picture shows); iron sights are Scoped without it.
        public bool ThroughScope => scoped && throughScope;
        bool throughScope;
        /// The first-person arms-and-gun camera's zoom (field of view / first-person field of view). A scope
        /// magnifies only the world through its lens: the gun below it gets just the light aim zoom.
        public float ViewmodelZoom { get; private set; } = 1f;
        bool fpGunsShown;

        PlayerController player;

        [Tooltip("A teammate's copy (multiplayer): always third person, no camera, no input, no view events.")]
        public bool remote;

        void Awake()
        {
            if (remote)
            {
                player = GetComponent<PlayerController>();
                weapons = TryGetComponent(out PlayerWeaponController remoteOwn) ? remoteOwn : GetComponentInChildren<PlayerWeaponController>(true);
                fpRenderers = new Renderer[0][];
                view = CharacterView.ThirdPerson; blend = 1f;
                if (body != null) body.SetView(CharacterView.ThirdPerson);
                return;
            }
            if (Local == null) Local = this;
            player = GetComponent<PlayerController>();
            weapons = TryGetComponent(out PlayerWeaponController own) ? own : GetComponentInChildren<PlayerWeaponController>(true);
            if (playerCamera != null) fpLocalPos = playerCamera.transform.localPosition;

            fpRenderers = new Renderer[firstPersonModels != null ? firstPersonModels.Length : 0][];
            for (int i = 0; i < fpRenderers.Length; i++)
                fpRenderers[i] = firstPersonModels[i] != null ? firstPersonModels[i].GetComponentsInChildren<Renderer>(true) : new Renderer[0];

            view = config != null ? config.startView : CharacterView.FirstPerson;
            blend = view == CharacterView.ThirdPerson ? 1f : 0f;
            Apply();
        }

        void OnDestroy()
        {
            if (Local == this) Local = null;
        }

        void Update()
        {
            if (remote) return;
            if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;
            if (GameInput.ConsumeToggleView()) Toggle();
            if (GameInput.ConsumeShoulderSwap() && IsThirdPerson) side = -side;
        }

        public void Toggle() => SetView(IsThirdPerson ? CharacterView.FirstPerson : CharacterView.ThirdPerson);

        public void SetView(CharacterView value)
        {
            if (value == view) return;
            view = value;
            Apply();
            Changed?.Invoke(view);
        }

        void Apply()
        {
            if (body != null) body.SetView(scoped ? CharacterView.FirstPerson : view);
            if (player != null)
            {
                // Pitch is the camera's angle, not the body's: in third person less of it is useful.
                if (view == CharacterView.ThirdPerson && config != null) player.SetPitchLimits(-config.pitchMax, -config.pitchMin);
                else player.ResetPitchLimits();
            }
            ShowFpGuns(view == CharacterView.FirstPerson || ScopedAtEye);
        }

        void ShowFpGuns(bool show)
        {
            fpGunsShown = show;
            foreach (var set in fpRenderers)
                foreach (var r in set)
                    if (r != null) r.enabled = show;
        }

        /// A teammate's copy: how far into aiming it is (drives its arms and gun).
        public void SetRemoteAim(float amount) => aimBlend = Mathf.Clamp01(amount);

        void LateUpdate()
        {
            if (remote) return;
            if (playerCamera == null || cameraPivot == null) return;
            float dt = Time.deltaTime;

            var gun = weapons != null ? weapons.CurrentWeapon : null;
            var gunData = gun != null ? gun.data : null;
            float aimTime = config != null ? Mathf.Max(0.01f, config.aimBlendTime) : 0.15f;
            bool aiming = GameInput.AimHeld && (GameManager.Instance == null || GameManager.Instance.State == GameState.Playing)
                          && (gun == null || !gun.IsReloading);   // a reload drops the aim
            aimBlend = Mathf.MoveTowards(aimBlend, aiming ? 1f : 0f, dt / aimTime);
            float aim = AimAmount;

            // a scoped gun switches to the scope view half way into the aim
            bool hasScope = gunData != null && gunData.hasScope;
            bool atEye = gunData != null && gunData.AimsAtEye;   // scope or iron sights: aiming brings the gun to the eye
            throughScope = hasScope;
            bool nowScoped = atEye && aimBlend >= 0.5f;
            if (nowScoped != scoped)
            {
                scoped = nowScoped;
                if (body != null) body.SetView(scoped ? CharacterView.FirstPerson : view);
            }

            float time = config != null ? Mathf.Max(0.01f, config.blendTime) : 0.25f;
            if (scoped) time = Mathf.Min(time, aimTime);
            blend = Mathf.MoveTowards(blend, IsThirdPerson && !scoped ? 1f : 0f, dt / time);
            float t = blend * blend * (3f - 2f * blend);

            float swapTime = config != null ? Mathf.Max(0.01f, config.shoulderSwapTime) : 0.2f;
            sideNow = Mathf.MoveTowards(sideNow, side, 2f * dt / swapTime);

            // field of view: each view its own, narrowed while aiming
            float fov = Mathf.Lerp(config != null ? config.firstPersonFov : 72f, config != null ? config.thirdPersonFov : 70f, t);
            // a scope's zoom is a true magnification (2x = everything twice as big): halve the tangent of the
            // half-angle, not the angle itself (halving the angle gave about 2.2x)
            float aimScale = hasScope
                ? 2f * Mathf.Atan(Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1f, gunData.scopeZoom)) * Mathf.Rad2Deg / Mathf.Max(1f, fov)
                : gunData != null ? gunData.aimZoom : config != null ? config.aimFovScale : 0.8f;
            playerCamera.fieldOfView = fov * Mathf.Lerp(1f, aimScale, aim);
            float baseFov = config != null ? Mathf.Max(1f, config.firstPersonFov) : 72f;
            ViewmodelZoom = fov * Mathf.Lerp(1f, hasScope ? gunData.aimZoom : aimScale, aim) / baseFov;
            bool wantGuns = view == CharacterView.FirstPerson || ScopedAtEye;
            if (wantGuns != fpGunsShown) ShowFpGuns(wantGuns);

            if (blend <= 0f)
            {
                playerCamera.transform.localPosition = fpLocalPos;
                wallLength = -1f;
                return;
            }

            Vector3 offset = config != null ? config.shoulderOffset : new Vector3(0.5f, 0.25f, -2.4f);
            if (config != null) offset = Vector3.Lerp(offset, config.aimOffset, aim);
            if (config != null && player != null)
            {
                // the boom: in and down looking up, up and a little back looking down (pitch: down is positive)
                float p = player.Pitch;
                float up = config.pitchMax > 0f ? Mathf.Clamp01(-p / config.pitchMax) : 0f;
                float down = config.pitchMin < 0f ? Mathf.Clamp01(p / -config.pitchMin) : 0f;
                up *= up; down *= down;
                offset.z *= Mathf.Lerp(1f, config.lookUpDistance, up) * Mathf.Lerp(1f, config.lookDownDistance, down);
                offset.y += config.lookUpHeight * up + config.lookDownHeight * down;
                offset.x += config.lookDownSide * down;   // multiplied by the shoulder side below
            }
            offset.x *= sideNow;
            Vector3 wanted = Vector3.Lerp(fpLocalPos, offset, t);

            // Trail the player a little: the camera follows a smoothed copy of the pivot position, so
            // starts, stops and bumps feel weighty instead of bolted on. Rotation stays instant (aim).
            float lagTime = config != null ? config.followLag : 0.12f;
            if (lagTime > 0f)
            {
                Vector3 head = cameraPivot.position;
                if (!lagReady) { smoothHead = head; lagReady = true; }
                smoothHead = Vector3.SmoothDamp(smoothHead, head, ref lagVelocity, lagTime);
                Vector3 trail = Vector3.ClampMagnitude(smoothHead - head, config != null ? config.maxLag : 0.6f) * t;
                smoothHead = head + trail / Mathf.Max(0.001f, t);
                wanted += cameraPivot.InverseTransformVector(trail);
            }
            playerCamera.transform.localPosition = KeepOutOfWalls(wanted, dt); // wall check last, trail included
        }

        /// Keeps the camera out of walls in two steps: first out to the shoulder (sideways and up),
        /// then back from there. A wall at the side only narrows the shoulder offset, so the camera
        /// keeps its distance instead of jumping onto the back of the head. A wall behind pulls the
        /// camera in at once; when it clears, the camera eases back out instead of snapping.
        Vector3 KeepOutOfWalls(Vector3 localPos, float dt)
        {
            Vector3 head = cameraPivot.position;
            Vector3 shoulder = Cast(head, cameraPivot.TransformPoint(new Vector3(localPos.x, localPos.y, 0f)), 0f);
            float minDist = config != null ? config.minDistance : 0.4f;
            Vector3 back = cameraPivot.TransformDirection(new Vector3(0f, 0f, localPos.z));
            float want = back.magnitude;
            float free = (Cast(shoulder, shoulder + back, minDist) - shoulder).magnitude;
            float speed = config != null ? config.wallReturnSpeed : 3f;
            if (wallLength < 0f) wallLength = free;
            else if (free < wallLength)
            {
                // glide in fast (about 0.08 s) instead of snapping a metre in one frame, never more than 25 cm into the wall
                wallLength = Mathf.Lerp(wallLength, free, 1f - Mathf.Exp(-dt / 0.04f));
                wallLength = Mathf.Min(wallLength, free + 0.25f);
            }
            else wallLength = Mathf.MoveTowards(wallLength, free, speed * dt);           // ease back out
            wallLength = Mathf.Min(wallLength, want);
            Vector3 cam = shoulder + (want > 1e-4f ? back / want : Vector3.zero) * wallLength;
            return cameraPivot.InverseTransformPoint(cam);
        }

        /// Sphere-casts from `from` to `to` and returns the furthest free point (never closer than `minDist`).
        Vector3 Cast(Vector3 from, Vector3 to, float minDist)
        {
            Vector3 dir = to - from;
            float dist = dir.magnitude;
            if (dist < 0.001f) return to;
            dir /= dist;

            float radius = config != null ? config.collisionRadius : 0.2f;
            int mask = config != null ? config.collisionMask.value : ~0;
            int count = Physics.SphereCastNonAlloc(from, radius, dir, hits, dist, mask, QueryTriggerInteraction.Ignore);

            float nearest = dist;
            for (int i = 0; i < count; i++)
            {
                var c = hits[i].collider;
                if (hits[i].distance <= 0f) continue;                       // started inside it
                if (c.transform.IsChildOf(transform)) continue;             // own capsule and body
                if (c.GetComponentInParent<IDamageable>() != null) continue; // zombies walking past
                if (c.bounds.size.magnitude < 1.2f) continue;               // small props (sandbags, debris, grass): no camera pop
                nearest = Mathf.Min(nearest, hits[i].distance);
            }
            return from + dir * Mathf.Max(Mathf.Min(minDist, dist), nearest);
        }

        /// Where a shot along `direction` from the camera should start: in third person it skips
        /// the stretch between the camera and the player, so the player can't shoot itself or a
        /// zombie standing behind it.
        public Vector3 AimOrigin(Vector3 cameraPos, Vector3 direction)
        {
            if (blend <= 0f || cameraPivot == null) return cameraPos;
            float skip = Vector3.Dot(cameraPivot.position - cameraPos, direction);
            return skip > 0f ? cameraPos + direction * skip : cameraPos;
        }
    }
}
