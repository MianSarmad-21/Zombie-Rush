using UnityEngine;
using ZombieRush.Audio;
using ZombieRush.Core;
using ZombieRush.Data;
using ZombieRush.Player;

namespace ZombieRush.Weapons
{
    /// Shared magazine / fire-rate / reload logic. Subclasses only decide what a
    /// single shot does in Fire().
    public enum GunStyle { Pistol, Rifle, Shotgun }

    public abstract class WeaponBase : MonoBehaviour
    {
        /// How the weapon is handled in animation (reload, recoil). Pistols are told apart by size.
        public virtual GunStyle Style => GunStyle.Rifle;

        /// 0..1 through the current reload, 0 when not reloading.
        public float ReloadProgress => reloading && data != null && data.reloadTime > 0f
            ? Mathf.Clamp01(1f - (reloadEndTime - Time.time) / data.reloadTime) : 0f;

        public WeaponData data;
        public LayerMask hitMask = ~0;

        protected Camera playerCamera;
        protected PlayerInventory inventory;
        PlayerViewSwitcher viewSwitcher; // third person: shots start at the player, not behind it

        /// Raised after every shot (muzzle flash, recoil - see WeaponViewModel).
        public event System.Action Fired;
        /// Raised when a reload starts.
        public event System.Action ReloadStarted;

        int magazine;
        float nextFireTime;
        float nextEmptyClick;
        bool reloading;
        float reloadEndTime;
        float emptySince = -1f;   // when the magazine ran dry (for the automatic reload), -1 = not empty
        float bloom;              // spread added by recent shots, degrees
        CharacterController mover;

        public int CurrentMagazine => magazine;
        public bool IsReloading => reloading;

        /// 0..1 how far into aiming the view is (narrows the spread).
        protected float AimAmount => viewSwitcher != null ? viewSwitcher.AimAmount : 0f;
        /// 0..1 how fast the player is moving (full at running speed).
        protected float MoveAmount => mover != null ? Mathf.Clamp01(new Vector3(mover.velocity.x, 0f, mover.velocity.z).magnitude / 4f) : 0f;

        /// Current shot spread, degrees from the crosshair: rest + recent shots + moving, tighter while
        /// aiming. The crosshair draws exactly this.
        public virtual float SpreadDegrees => data == null ? 0f
            : (Mathf.Min(data.spreadMax, data.spreadBase + bloom) + data.spreadMoving * MoveAmount) * Mathf.Lerp(1f, data.aimSpreadScale, AimAmount);

        /// A direction from the camera within `degrees` of the crosshair (even over the disc).
        protected Vector3 SpreadDirection(float degrees)
        {
            Vector2 o = degrees > 0f ? Random.insideUnitCircle * degrees : Vector2.zero;
            return playerCamera.transform.rotation * Quaternion.Euler(o.y, o.x, 0f) * Vector3.forward;
        }

        public virtual void Init(Camera cam, PlayerInventory inv)
        {
            playerCamera = cam;
            inventory = inv;
            viewSwitcher = inv != null ? inv.GetComponent<PlayerViewSwitcher>() : null;
            mover = inv != null ? inv.GetComponent<CharacterController>() : null;
            magazine = data != null ? data.magazineSize : 0;
        }

        /// Returns true if the weapon actually fired (had ammo/wasn't on cooldown).
        public bool TryUse()
        {
            if (data == null || reloading || playerCamera == null) return false;
            if (Time.time < nextFireTime) return false;

            if (!data.unlimitedUses && magazine <= 0)
            {
                TryReload();
                if (!reloading && Time.time >= nextEmptyClick)
                {
                    // Out of ammo completely: dry click, but not every frame while the trigger is held.
                    nextEmptyClick = Time.time + 0.35f;
                    SoundManager.Play(SoundManager.Library != null ? SoundManager.Library.emptyClick : null, 0.7f);
                }
                return false;
            }

            // With the trigger held, the next shot is timed from the last one, not from this frame: otherwise a
            // frame rate that doesn't divide the fire rate (30 fps) loses part of every interval and slows the gun.
            float interval = 1f / Mathf.Max(0.01f, data.fireRate);
            nextFireTime = Time.time - nextFireTime < interval ? nextFireTime + interval : Time.time + interval;
            if (!data.unlimitedUses) magazine--;

            shotNumber++;
            Fire();
            bloom = Mathf.Min(bloom + data.spreadPerShot, data.spreadMax);
            var lib = SoundManager.Library;
            var shot = data.fireSounds != null && data.fireSounds.Length > 0 ? data.fireSounds : lib != null ? lib.gunshot : null;
            SoundManager.Play(shot, data.fireVolume, 0.05f);
            Fired?.Invoke();
            // the shot is felt: the camera jolts, the view kicks outward, the phone taps (TryUse is the local player's own gun)
            CameraShake.Add(data.shakePerShot);
            PlayerViewSwitcher.KickFov(data.fovKick);
            Haptics.Tap(FeelConfig.Current.shotTap);
            return true;
        }

        int shotNumber;     // shots fired, for "a tracer every N shots"
        int volleyImpacts;  // impacts shown for the current shotgun blast

        /// A tracer from the gun to where this shot ended (every data.tracerEvery shots; never for shotgun pellets).
        void Tracer(Vector3 origin, Vector3 direction, Vector3 end)
        {
            if (inVolley || data.tracerEvery <= 0 || shotNumber % data.tracerEvery != 0) return;
            Vector3 from;
            if (viewSwitcher != null && viewSwitcher.IsThirdPerson) from = origin + direction * 0.6f;
            else
            {
                // first person: the gun is drawn by its own camera, so start just below and right of the view, where it sits
                var t = playerCamera.transform;
                from = t.position + t.forward * 0.7f + t.right * 0.09f - t.up * 0.08f;
            }
            ZombieRush.Effects.TracerEffect.Fire(from, end);
        }

        /// Sparks, dust and a bullet hole where a shot hit the world (a shotgun blast shows only a few).
        void Impact(RaycastHit hit, Vector3 direction)
        {
            if (inVolley && volleyImpacts++ >= FeelConfig.Current.shotgunImpacts) return;
            ZombieRush.Effects.ImpactEffect.Spawn(hit, direction);
        }

        protected abstract void Fire();

        /// A teammate's shot seen on its copy (multiplayer): muzzle flash, recoil and the sound in 3D.
        public void PlayRemoteShot()
        {
            var lib = SoundManager.Library;
            var shot = data != null && data.fireSounds != null && data.fireSounds.Length > 0 ? data.fireSounds : lib != null ? lib.gunshot : null;
            if (shot != null && shot.Length > 0) SoundManager.PlayAt(shot, transform.position, data != null ? data.fireVolume : 1f, 40f, 4);
            Fired?.Invoke();
        }

        /// A teammate's reload seen on its copy: starts or stops the reload animation.
        public void SetRemoteReload(bool on)
        {
            if (on == reloading || data == null) return;
            reloading = on;
            if (on) { reloadEndTime = Time.time + data.reloadTime; ReloadStarted?.Invoke(); }
        }

        public void TryReload()
        {
            if (reloading || data == null || data.unlimitedUses) return;
            if (magazine >= data.magazineSize) return;
            if (inventory != null && inventory.GetReserveAmmo(data) <= 0) return; // nothing to load

            reloading = true;
            reloadEndTime = Time.time + data.reloadTime;
            var lib = SoundManager.Library;
            SoundManager.Play(data.reloadSound != null ? data.reloadSound : lib != null ? lib.reload : null, 0.8f, 0f);
            ReloadStarted?.Invoke();
        }

        // Timer-based instead of a coroutine: no allocation, and it pauses with timeScale.
        void Update()
        {
            AutoReload();
            if (data != null) bloom = Mathf.MoveTowards(bloom, 0f, data.spreadRecovery * Time.deltaTime);
            if (!reloading || Time.time < reloadEndTime) return;

            int needed = data.magazineSize - magazine;
            magazine += inventory != null ? inventory.TakeReserveAmmo(data, needed) : needed;
            reloading = false;
        }

        /// Empty magazine: reload on its own a moment after the last shot (only while this gun is in hand).
        void AutoReload()
        {
            if (data == null || !data.autoReload || data.unlimitedUses || reloading || magazine > 0) { emptySince = -1f; return; }
            if (emptySince < 0f) emptySince = Time.time;
            if (Time.time - emptySince < data.autoReloadDelay || Time.time < nextFireTime) return;
            if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;
            TryReload();   // does nothing without spare ammo
        }

        // Switching weapons deactivates this object. Cancel any reload in progress
        // so the gun isn't left stuck in "reloading" forever.
        void OnDisable() { reloading = false; emptySince = -1f; }

        /// Hitscan along `direction` from the camera, damaging the first IDamageable hit.
        /// Flies through the base compound (ShootThrough), so players in the safe base can hit zombies outside.
        /// A shot to the head does data.headshotMultiplier times the damage.
        protected void HitScan(Vector3 direction, float damage)
        {
            var origin = playerCamera.transform.position;
            if (viewSwitcher != null) origin = viewSwitcher.AimOrigin(origin, direction);
            direction = direction.normalized;
            bool solid = ZombieRush.World.ShootThrough.Raycast(origin, direction, out var hit, data.range, hitMask);
            float solidDistance = solid ? Vector3.Distance(origin, hit.point) : data.range;

            // A head in front of whatever the ray hit (a hunched zombie's head sticks out of its body capsule).
            var headZombie = ZombieRush.Zombie.ZombieHealth.FirstHeadAlong(origin, direction, solidDistance, out float headDistance);
            if (headZombie != null)
            {
                float headDamage = damage * Mathf.Max(1f, data.headshotMultiplier);
                Vector3 at = origin + direction * headDistance;
                Tracer(origin, direction, at);
                if (inVolley) AddToVolley(headZombie, headDamage, at, direction, true);
                else headZombie.TakeHit(headDamage, true, at, direction, true);
                return;
            }
            Tracer(origin, direction, origin + direction * solidDistance);
            if (!solid) return;

            var zombie = hit.collider.GetComponentInParent<ZombieRush.Zombie.ZombieHealth>();
            if (zombie != null)
            {
                bool head = zombie.IsHeadshot(origin, direction);
                if (head) damage *= Mathf.Max(1f, data.headshotMultiplier);
                if (inVolley) AddToVolley(zombie, damage, hit.point, direction, head);
                else zombie.TakeHit(damage, true, hit.point, direction, head);
                return;
            }
            var target = hit.collider.GetComponentInParent<IDamageable>();
            if (target != null) target.TakeDamage(damage);
            Impact(hit, direction);
        }

        // ---- volleys: every pellet of one shotgun shot that hits the same zombie lands as ONE hit (one damage
        // number, one sound and blood burst, one network message), with the pellets' damage added up.
        struct VolleyHit { public float damage; public Vector3 point, direction; public bool head; }
        static readonly System.Collections.Generic.Dictionary<ZombieRush.Zombie.ZombieHealth, VolleyHit> volley =
            new System.Collections.Generic.Dictionary<ZombieRush.Zombie.ZombieHealth, VolleyHit>();
        static readonly System.Collections.Generic.List<(ZombieRush.Zombie.ZombieHealth zombie, VolleyHit hit)> volleyApply =
            new System.Collections.Generic.List<(ZombieRush.Zombie.ZombieHealth, VolleyHit)>();
        bool inVolley;

        protected void BeginVolley() { volley.Clear(); inVolley = true; volleyImpacts = 0; }

        void AddToVolley(ZombieRush.Zombie.ZombieHealth zombie, float damage, Vector3 point, Vector3 direction, bool head)
        {
            if (volley.TryGetValue(zombie, out var v)) { v.damage += damage; v.head |= head; volley[zombie] = v; }
            else volley[zombie] = new VolleyHit { damage = damage, point = point, direction = direction, head = head };
        }

        protected void EndVolley()
        {
            inVolley = false;
            volleyApply.Clear();
            foreach (var kv in volley) volleyApply.Add((kv.Key, kv.Value));
            volley.Clear();
            foreach (var (zombie, v) in volleyApply)
                if (zombie != null) zombie.TakeHit(v.damage, true, v.point, v.direction, v.head);
            volleyApply.Clear();
        }
    }
}
