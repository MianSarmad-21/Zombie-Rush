using UnityEngine;
using ZombieRush.Core;
using ZombieRush.Data;
using ZombieRush.Weapons;

namespace ZombieRush.Player
{
    /// Holds the player's equipped weapons and routes fire/switch/reload input to them.
    [RequireComponent(typeof(PlayerInventory))]
    public class PlayerWeaponController : MonoBehaviour
    {
        public Camera playerCamera;
        public WeaponBase[] weapons;
        [Tooltip("How many weapons (from the top of the list) the player starts with. The rest are picked up from the racks in the safe base.")]
        public int startingWeapons = 1;

        [Tooltip("Seconds after changing weapon before it can fire or reload (the draw animation).")]
        public float equipTime = 0.4f;

        int currentIndex;
        float readyAt;
        float nextNoFireNote;
        PlayerController mover;
        bool[] unlocked;
        PlayerInventory inventory;

        // Last values pushed to the HUD, so the event only fires on real changes.
        WeaponBase lastWeapon;
        int lastMag = -1, lastReserve = -1;
        bool lastReloading;

        public WeaponBase CurrentWeapon => weapons != null && weapons.Length > 0 ? weapons[currentIndex] : null;
        /// Index of the gun in hand.
        public int CurrentIndex => currentIndex;
        [Tooltip("A teammate's copy (multiplayer): no input, no ammo pickups, no HUD events; the network picks the gun.")]
        public bool remote;

        void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            mover = GetComponent<PlayerController>();
            if (weapons == null) weapons = new WeaponBase[0];
            unlocked = new bool[weapons.Length];
            for (int i = 0; i < weapons.Length; i++) unlocked[i] = i < Mathf.Max(1, startingWeapons);

            foreach (var weapon in weapons)
                if (weapon != null) weapon.Init(playerCamera, inventory);

            UpdateWeaponVisibility();
        }

        void OnEnable() => GameEvents.OnZombieKilled += HandleZombieKilled;
        void OnDisable() => GameEvents.OnZombieKilled -= HandleZombieKilled;

        void Start() { if (!remote) RaiseAmmoStatus(force: true); }

        void Update()
        {
            if (remote) return;
            if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;
            if (PlayerHealth.Instance != null && PlayerHealth.Instance.IsDead) return;
            if (weapons.Length == 0) return;

            int slot = GameInput.WeaponSlotPressed(weapons.Length);
            if (slot >= 0) SelectWeapon(slot);
            if (GameInput.ConsumeSwapWeapon()) SelectWeapon(NextUnlocked(currentIndex));

            var weapon = CurrentWeapon;
            if (weapon != null)
            {
                // The gun is being drawn: no shooting or reloading until it is up.
                bool ready = Time.time >= readyAt;
                if (GameInput.FireHeld && ready)
                {
                    if (!World.SafeBase.FiringAllowedAt(transform.position))
                    {
                        // the safe base is a no-fire zone: a short reminder, not one every frame
                        if (Time.time >= nextNoFireNote) { nextNoFireNote = Time.time + 2.5f; UI.PickupToast.Show("NO FIRING IN THE SAFE BASE", new Color(1f, 0.45f, 0.35f)); }
                    }
                    else if (weapon.TryUse()) Kick(weapon);
                }
                if (GameInput.ConsumeReload() && ready) weapon.TryReload();
            }

            RaiseAmmoStatus(force: false);
        }

        /// A shot's view kick: the camera climbs a little (and drifts a touch sideways), then settles back.
        /// Less while aiming down the sights.
        void Kick(WeaponBase weapon)
        {
            if (mover == null || weapon.data == null) return;
            var view = PlayerViewSwitcher.Local;
            float aim = view != null ? view.AimAmount : 0f;
            float k = Mathf.Lerp(1f, weapon.data.recoilAimScale, aim);
            mover.AddRecoil(weapon.data.recoilUp * k * Random.Range(0.85f, 1.15f), weapon.data.recoilSide * k * Random.Range(-1f, 1f));
        }

        public void SelectWeapon(int index) => SelectWeapon(index, drawSound: true);

        public void SelectWeapon(int index, bool drawSound)
        {
            if (index < 0 || index >= weapons.Length || index == currentIndex || !unlocked[index]) return;
            currentIndex = index;
            readyAt = Time.time + equipTime;
            // Draw sound for this player's own switches; none on the level's first equip or on a teammate's copy.
            var lib = Audio.SoundManager.Library;
            if (drawSound && !remote && lib != null && Time.timeSinceLevelLoad > 0.5f) Audio.SoundManager.Play(lib.weaponDraw, lib.weaponDrawVolume, 0.05f);
            UpdateWeaponVisibility();
            RaiseAmmoStatus(force: true);
        }

        int NextUnlocked(int from)
        {
            for (int step = 1; step <= weapons.Length; step++)
            {
                int i = (from + step) % weapons.Length;
                if (unlocked[i]) return i;
            }
            return from;
        }

        public bool IsUnlocked(WeaponData data) => IndexOf(data) is int i && i >= 0 && unlocked[i];

        /// Gives the player this weapon (from a base rack) and equips it. False if they already had it.
        public bool Unlock(WeaponData data)
        {
            int i = IndexOf(data);
            if (i < 0 || unlocked[i]) return false;
            unlocked[i] = true;
            SelectWeapon(i, drawSound: false);   // the pickup plays its own grab-and-rack sound
            return true;
        }

        /// Tops reserve ammo up to the max for every weapon the player has. False if all were already full.
        public bool RefillAmmo()
        {
            bool changed = false;
            for (int i = 0; i < weapons.Length; i++)
            {
                var data = weapons[i] != null ? weapons[i].data : null;
                if (!unlocked[i] || data == null || data.unlimitedUses) continue;
                int before = inventory.GetReserveAmmo(data);
                inventory.AddReserveAmmo(data, data.maxReserveAmmo);
                changed |= inventory.GetReserveAmmo(data) != before;
            }
            RaiseAmmoStatus(force: true);
            return changed;
        }

        /// Adds reserve ammo for one gun the player has (an ammo box). Returns the rounds actually added:
        /// 0 if the gun isn't theirs, uses no ammo, or is already full.
        public int AddAmmo(WeaponData data, int amount)
        {
            int i = IndexOf(data);
            if (i < 0 || !unlocked[i] || data.unlimitedUses || amount <= 0) return 0;
            int before = inventory.GetReserveAmmo(data);
            inventory.AddReserveAmmo(data, amount);
            int added = inventory.GetReserveAmmo(data) - before;
            if (added > 0) RaiseAmmoStatus(force: true);
            return added;
        }

        int IndexOf(WeaponData data)
        {
            for (int i = 0; i < weapons.Length; i++)
                if (weapons[i] != null && weapons[i].data == data) return i;
            return -1;
        }

        void RaiseAmmoStatus(bool force)
        {
            if (remote) return;
            var weapon = CurrentWeapon;
            if (weapon == null || weapon.data == null) return;

            int reserve = inventory.GetReserveAmmo(weapon.data);
            int mag = weapon.CurrentMagazine;
            bool reloading = weapon.IsReloading;
            if (!force && weapon == lastWeapon && mag == lastMag && reserve == lastReserve && reloading == lastReloading) return;

            lastWeapon = weapon; lastMag = mag; lastReserve = reserve; lastReloading = reloading;
            GameEvents.RaiseAmmoChanged(new AmmoInfo
            {
                WeaponName = weapon.data.weaponName,
                Icon = weapon.data.icon,
                Magazine = mag,
                MagazineSize = weapon.data.magazineSize,
                Reserve = reserve,
                IsReloading = reloading,
                ReloadDuration = weapon.data.reloadTime,
                Unlimited = weapon.data.unlimitedUses,
            });
        }

        void UpdateWeaponVisibility()
        {
            for (int i = 0; i < weapons.Length; i++)
                if (weapons[i] != null) weapons[i].gameObject.SetActive(i == currentIndex);
        }

        /// A zombie that drops ammo (rarely - ZombieHealth.ammoDropChance) leaves an ammo pack on the ground for one of
        /// the guns this player carries (the one in hand a little more often); walking over it picks it up.
        void HandleZombieKilled(ZombieKilledInfo info)
        {
            if (remote) return;
            if (info.Ammo <= 0) return;
            var choices = new System.Collections.Generic.List<WeaponData>();
            for (int i = 0; i < weapons.Length; i++)
            {
                var d = weapons[i] != null ? weapons[i].data : null;
                if (!unlocked[i] || d == null || d.unlimitedUses || d.ammoPack == null) continue;
                choices.Add(d);
                if (i == currentIndex) choices.Add(d);
            }
            if (choices.Count == 0) return;
            var pick = choices[Random.Range(0, choices.Count)];
            World.AmmoPickup.SpawnDrop(info.Position, pick, Random.Range(pick.dropAmmo.x, pick.dropAmmo.y + 1));
        }
    }
}
