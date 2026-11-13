using UnityEngine;
using ZombieRush.Core;
using ZombieRush.Weapons;

namespace ZombieRush.Player
{
    /// Ties the player's full body to gameplay: a copy of each gun goes into the body's
    /// WeaponAimRig (the one in use is shown and held), shots kick it, reloading tips it down,
    /// and getting hit or dying plays the matching body animation.
    /// Seen in third person and in first-person shadows.
    [DefaultExecutionOrder(-50)] // turns the body before CharacterBodyAnimator reads its direction
    public class PlayerBodyLink : MonoBehaviour
    {
        public PlayerWeaponController weapons;
        public CharacterBodyAnimator bodyAnimator;
        public WeaponAimRig aimRig;
        public CharacterViewMode viewMode;

        PlayerViewSwitcher view;
        CharacterController controller;
        float lastShot = -99f;
        GameObject[] bodyGuns;
        WeaponBase shown;
        bool first = true;
        float lastHealth = -1f;

        void Awake()
        {
            view = GetComponent<PlayerViewSwitcher>();
            controller = GetComponent<CharacterController>();
            BuildBodyGuns();
            if (viewMode != null) viewMode.RefreshRenderers();
        }

        bool Remote => view != null && view.remote;

        void OnEnable()
        {
            if (view == null) view = GetComponent<PlayerViewSwitcher>();
            if (!Remote)
            {
                // the local player's own health and death (a teammate's copy is told by the network instead)
                GameEvents.OnPlayerHealthChanged += HandleHealth;
                GameEvents.OnPlayerDied += HandleDied;
            }
            if (weapons == null) return;
            foreach (var w in weapons.weapons)
                if (w != null) w.Fired += HandleFired;
        }

        void OnDisable()
        {
            GameEvents.OnPlayerHealthChanged -= HandleHealth;
            GameEvents.OnPlayerDied -= HandleDied;
            if (weapons == null) return;
            foreach (var w in weapons.weapons)
                if (w != null) w.Fired -= HandleFired;
        }

        void Update()
        {
            TurnBody();
            var current = weapons != null ? weapons.CurrentWeapon : null;
            if (aimRig != null)
            {
                aimRig.Lowered = current != null && current.IsReloading;
                aimRig.ReloadProgress = current != null ? current.ReloadProgress : 0f;
                // First person: the hands grip the gun drawn in front of the camera.
                var vm = current != null && view != null && !view.IsThirdPerson ? current.GetComponent<WeaponViewModel>() : null;
                // a gun with body clips is placed by BodyGunPose from the body's hands, not the other way round
                var vr = vm != null && vm.model != null ? vm.model.GetComponentInChildren<GunRig>(true) : null;
                if (vr != null && vr.HasBodyClips) vm = null;
                aimRig.FirstPersonGun = vm != null ? vm.model : null;
                Vector3 vel = controller != null ? controller.velocity : Vector3.zero;
                vel.y = 0f;
                aimRig.MoveAmount = Mathf.Clamp01(vel.magnitude / 5f);
            }
            if ((current == shown && !first) || bodyGuns == null) return;
            first = false;
            shown = current;
            Transform held = null;
            for (int i = 0; i < bodyGuns.Length; i++)
                if (bodyGuns[i] != null && weapons.weapons[i] == current) held = bodyGuns[i].transform;

            // The rig lowers the old gun, calls ShowBodyGun at the bottom, then raises the new one.
            if (aimRig != null) aimRig.Equip(held, current != null ? current.Style : GunStyle.Rifle, () => ShowBodyGun(current));
            else ShowBodyGun(current);
        }

        void ShowBodyGun(WeaponBase current)
        {
            for (int i = 0; i < bodyGuns.Length; i++)
                if (bodyGuns[i] != null) bodyGuns[i].SetActive(weapons.weapons[i] == current);
        }

        /// Third person: while moving and not shooting, the body turns to face the way it moves and
        /// carries the gun lowered; when shooting, recently shot, or standing still it faces the
        /// crosshair and aims. First person: always facing the crosshair.
        void TurnBody()
        {
            if (viewMode == null) return;
            var cfg = view != null ? view.config : null;
            if (!Remote && GameInput.FireHeld) lastShot = Time.time;
            bool combat = Time.time - lastShot < (cfg != null ? cfg.combatHoldTime : 1.2f);
            Vector3 v = controller != null ? controller.velocity : Vector3.zero;
            v.y = 0f;
            bool moving = v.sqrMagnitude > 0.5f;
            bool free = cfg != null && cfg.faceMoveDirection && view != null && view.IsThirdPerson && moving && !combat;

            Quaternion target = free
                ? Quaternion.Inverse(transform.rotation) * Quaternion.LookRotation(v.normalized, Vector3.up) // local yaw toward travel
                : Quaternion.identity;                                                                    // face the crosshair
            var body = viewMode.transform;
            // Ease into the turn (fast when far off, gentle at the end), capped at turnSpeed.
            float maxStep = (cfg != null ? cfg.turnSpeed : 360f) * Time.deltaTime;
            float sharp = cfg != null ? cfg.turnSharpness : 10f;
            float angle = Quaternion.Angle(body.localRotation, target);
            float step = Mathf.Min(maxStep, angle * (1f - Mathf.Exp(-sharp * Time.deltaTime)));
            body.localRotation = Quaternion.RotateTowards(body.localRotation, target, step);
            if (aimRig != null) aimRig.Aiming = !free;
        }

        /// Copies each gun's visible model into the aim rig's hold, posed exactly as it sits under
        /// its weapon (barrel forward), without its scripts, lights or colliders.
        void BuildBodyGuns()
        {
            if (weapons == null || aimRig == null || aimRig.hold == null) return;
            bodyGuns = new GameObject[weapons.weapons.Length];
            for (int i = 0; i < bodyGuns.Length; i++)
            {
                var w = weapons.weapons[i];
                var view = w != null ? w.GetComponent<WeaponViewModel>() : null;
                if (view == null || view.model == null) continue;

                var copy = Instantiate(view.model.gameObject, aimRig.hold, false);
                copy.name = "BodyGun_" + w.name;
                copy.transform.localPosition = view.model.localPosition;
                copy.transform.localRotation = view.model.localRotation;
                copy.transform.localScale = view.model.localScale;
                // a gun with body clips flashes on the body in third person (BodyGunPose), so it keeps its flash
                var cr = copy.GetComponentInChildren<GunRig>(true);
                bool keepFlash = cr != null && cr.HasBodyClips;
                if (!keepFlash)
                {
                    foreach (var ps in copy.GetComponentsInChildren<ParticleSystem>(true)) Destroy(ps.gameObject);
                    foreach (var l in copy.GetComponentsInChildren<Light>(true)) Destroy(l);
                }
                else
                    foreach (var l in copy.GetComponentsInChildren<Light>(true)) l.enabled = false;
                foreach (var c in copy.GetComponentsInChildren<Collider>(true)) Destroy(c);
                foreach (var m in copy.GetComponentsInChildren<MonoBehaviour>(true))
                    if (!(m is GunRig)) Destroy(m);   // a Blender-animated gun keeps animating in the hands
                foreach (var r in copy.GetComponentsInChildren<Renderer>(true))
                {
                    r.enabled = true;
                    r.gameObject.layer = gameObject.layer;
                }
                copy.SetActive(false);
                bodyGuns[i] = copy;
            }
        }

        void HandleFired() { lastShot = Time.time; if (aimRig != null) aimRig.Kick(); }

        /// A teammate's copy (multiplayer): its health went down / it died / it came back.
        public void RemoteHealth(float current, float max) => HandleHealth(current, max);
        public void RemoteDied() => HandleDied();

        void HandleHealth(float current, float max)
        {
            if (bodyAnimator != null && lastHealth > 0f && current < lastHealth && current > 0f) bodyAnimator.PlayHit();
            if (bodyAnimator != null && lastHealth == 0f && current > 0f) bodyAnimator.ResetBody();
            lastHealth = current;
        }

        void HandleDied() { if (bodyAnimator != null) bodyAnimator.PlayDie(); }
    }
}
