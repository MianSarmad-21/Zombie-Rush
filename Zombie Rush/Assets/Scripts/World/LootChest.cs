using System.Collections.Generic;
using UnityEngine;
using ZombieRush.Core;
using ZombieRush.Data;
using ZombieRush.Player;

namespace ZombieRush.World
{
    /// A locked loot chest out in the fight zone (ChestSpawner puts new ones down every wave).
    /// Walk up and hold E (touch: hold the prompt) for `holdTime` seconds:
    ///   1. the padlock rattles harder and harder, then snaps off: sparks, it flies off and tumbles to the ground;
    ///   2. the lid swings open on its hinge with a little overshoot, a puff of dust, a warm glow from inside;
    ///   3. the loot pops out in arcs (ChestLootFlight) and lands around the chest: health, armour, ammo.
    /// Opened chests sink into the ground after `openLifetime` seconds. Each player opens their own copy (personal
    /// loot: every machine spawns the same chests from the wave's seed).
    public class LootChest : MonoBehaviour
    {
        public static readonly List<LootChest> All = new List<LootChest>();

        [Header("Parts (set by Loot Chests setup)")]
        public Transform lid;
        public Transform padlock;
        public ParticleSystem sparkle;     // gold motes over a closed chest: easy to spot
        public Light glow;                 // small beacon light while closed, bright from inside when opened
        public Material fxMaterial;        // additive glow for the sparks, dust and loot trails

        [Header("Opening")]
        public float useDistance = 2.3f;
        public float holdTime = 1f;
        public float lidOpenAngle = 108f;
        public float openLifetime = 25f;

        [Header("Loot (set by Loot Chests setup)")]
        public GameObject medkitPrefab;
        public GameObject armorPrefab;
        [Tooltip("How many things come out.")]
        public int lootCount = 3;
        public int healthAmount = 50;
        public int armorAmount = 50;

        public bool Opened { get; private set; }
        public float Progress { get; private set; }

        enum Stage { Locked, Breaking, Opening, Open, Sinking }
        Stage stage;
        float stageTime, openAt;
        Vector3 lockStartPos; Quaternion lockStartRot;
        Vector3 lockVel, lockSpin; bool lockLanded;
        float groundY;
        float glowBase;
        Vector3 glowHigh;   // beacon spot over the closed chest; it drops inside when the lid opens
        System.Random rng;

        void OnEnable() => All.Add(this);
        void OnDisable() { All.Remove(this); if (ChestPrompt.Target == this) ChestPrompt.Hide(this); }

        void Awake()
        {
            if (fxMaterial != null) FxMat = fxMaterial;
            if (padlock != null) { lockStartPos = padlock.localPosition; lockStartRot = padlock.localRotation; }
            if (glow != null) { glowBase = glow.intensity; glowHigh = glow.transform.localPosition; }
            groundY = transform.position.y;
            rng = new System.Random(Mathf.RoundToInt(transform.position.x * 13f + transform.position.z * 7f));
        }

        void Update()
        {
            float dt = Time.deltaTime;
            stageTime += dt;
            switch (stage)
            {
                case Stage.Locked: UpdateLocked(dt); break;
                case Stage.Breaking: UpdateBreaking(dt); break;
                case Stage.Opening: UpdateOpening(); break;
                case Stage.Open:
                    UpdateLockFall(dt);
                    if (glow != null) glow.intensity = Mathf.Lerp(glow.intensity, glowBase * 0.9f, dt * 1.5f);
                    if (Time.time > openAt + openLifetime) SetStage(Stage.Sinking);
                    break;
                case Stage.Sinking:
                    transform.position += Vector3.down * dt * 0.35f;
                    if (glow != null) glow.intensity = Mathf.MoveTowards(glow.intensity, 0f, dt * 2f);
                    if (stageTime > 2.5f) Destroy(gameObject);
                    break;
            }
        }

        void SetStage(Stage s) { stage = s; stageTime = 0f; }

        /// Left unopened too long (ChestSpawner): it sinks away, the same on every machine.
        public void Expire()
        {
            if (stage != Stage.Locked) return;
            if (ChestPrompt.Target == this) ChestPrompt.Hide(this);
            Progress = 0f;
            SetStage(Stage.Sinking);
        }

        // ---------------------------------------------------------------- 1. hold to open, the lock rattles

        void UpdateLocked(float dt)
        {
            var player = PlayerHealth.Instance;
            bool near = player != null && !player.IsDead && Flat(player.transform.position - transform.position).magnitude < useDistance
                        && (GameManager.Instance == null || GameManager.Instance.State == GameState.Playing);
            if (near && ChestPrompt.Target != this && (ChestPrompt.Target == null || Closer(player.transform.position))) ChestPrompt.Show(this);
            if (!near) { if (ChestPrompt.Target == this) ChestPrompt.Hide(this); Progress = Mathf.MoveTowards(Progress, 0f, dt * 3f); }
            else if (ChestPrompt.Target == this)
            {
                if (GameInput.InteractHeld)
                {
                    float before = Progress;
                    Progress = Mathf.MoveTowards(Progress, 1f, dt / Mathf.Max(0.05f, holdTime));
                    // a metallic tick each quarter of the way: the lock being forced
                    if (Mathf.FloorToInt(before * 4f) != Mathf.FloorToInt(Progress * 4f)) Sound(Lib?.emptyClick, 0.7f, 0.8f + Progress * 0.6f);
                }
                else Progress = Mathf.MoveTowards(Progress, 0f, dt * 2f);
            }

            // the padlock shakes harder as the hold goes on
            if (padlock != null)
            {
                float k = Progress * Progress;
                float t = Time.time * 38f;
                padlock.localRotation = lockStartRot * Quaternion.Euler(Mathf.Sin(t * 1.3f) * 9f * k, 0f, Mathf.Sin(t) * 22f * k);
                padlock.localPosition = lockStartPos + new Vector3(Mathf.Sin(t * 1.7f), 0f, 0f) * 0.008f * k;
            }
            if (Progress >= 1f) BreakLock();
        }

        bool Closer(Vector3 p) => ChestPrompt.Target == null ||
            Flat(p - transform.position).sqrMagnitude < Flat(p - ChestPrompt.Target.transform.position).sqrMagnitude;

        // ---------------------------------------------------------------- 2. the lock snaps off

        void BreakLock()
        {
            Opened = true;
            ChestPrompt.Hide(this);
            SetStage(Stage.Breaking);
            if (padlock != null)
            {
                padlock.SetParent(null, true);
                lockVel = transform.forward * 1.6f + Vector3.up * 2.4f + transform.right * Random.Range(-0.6f, 0.6f);
                lockSpin = new Vector3(Random.Range(300f, 600f), Random.Range(-200f, 200f), Random.Range(-400f, 400f));
                Burst(padlock.position, new Color(1f, 0.75f, 0.3f), 22, 3.2f, 0.05f, 0.35f);   // sparks
            }
            var lib = Lib;
            if (lib != null) { Sound(lib.bulletImpact != null && lib.bulletImpact.Length > 0 ? lib.bulletImpact[0] : null, 1f, 0.7f); Sound(lib.emptyClick, 1f, 0.5f); }
            Haptics.Pulse();
        }

        void UpdateBreaking(float dt)
        {
            UpdateLockFall(dt);
            if (stageTime > 0.22f) { SetStage(Stage.Opening); Sound(Lib?.weaponDraw != null && Lib.weaponDraw.Length > 0 ? Lib.weaponDraw[0] : null, 1f, 0.55f); }
        }

        void UpdateLockFall(float dt)
        {
            if (padlock == null || lockLanded) return;
            lockVel += Physics.gravity * dt;
            padlock.position += lockVel * dt;
            padlock.rotation = Quaternion.Euler(lockSpin * dt) * padlock.rotation;
            if (padlock.position.y <= groundY + 0.03f && lockVel.y < 0f)
            {
                var p = padlock.position; p.y = groundY + 0.03f; padlock.position = p;
                if (lockVel.y < -2.2f) { lockVel = new Vector3(lockVel.x * 0.5f, -lockVel.y * 0.35f, lockVel.z * 0.5f); lockSpin *= 0.4f; Sound(Lib?.emptyClick, 0.6f, 0.6f); }
                else
                {
                    lockLanded = true;
                    var e = padlock.eulerAngles;
                    padlock.rotation = Quaternion.Euler(Random.value < 0.5f ? 90f : -90f, e.y, 0f);   // lies on its side
                }
            }
        }

        // ---------------------------------------------------------------- 3. the lid opens, the loot pops out

        void UpdateOpening()
        {
            UpdateLockFall(Time.deltaTime);
            const float dur = 0.6f;
            float t = Mathf.Clamp01(stageTime / dur);
            // ease out with a small overshoot past the stop, then settle back (a heavy lid hitting its hinge)
            float c1 = 1.9f, c3 = c1 + 1f;
            float e = 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
            if (lid != null) lid.localRotation = Quaternion.Euler(lidOpenAngle * e, 0f, 0f);
            if (glow != null)
            {
                glow.intensity = glowBase * (1f + 3.5f * Mathf.Sin(t * Mathf.PI * 0.5f));
                glow.transform.localPosition = Vector3.Lerp(glowHigh, new Vector3(0f, 0.28f, 0f), Mathf.Clamp01(t * 3f));   // the glow comes from inside
            }
            if (sparkle != null && sparkle.isPlaying && t > 0.1f)
            {
                var em = sparkle.emission; em.rateOverTime = 0f;   // the motes fade out
            }
            if (t >= 0.3f && stageTime - Time.deltaTime < dur * 0.3f)
            {
                Vector3 seam = transform.position + Vector3.up * 0.32f;
                Burst(seam, new Color(0.55f, 0.5f, 0.42f, 0.6f), 16, 1.0f, 0.12f, 0.9f);              // dust
                Burst(seam + Vector3.up * 0.1f, new Color(1f, 0.85f, 0.4f), 30, 2.2f, 0.06f, 0.8f);  // golden burst
                SpawnLoot();
                var lib = Lib;
                if (lib != null) Sound(lib.purchase != null ? lib.purchase : lib.pickup, 0.9f, 1f);
            }
            if (t >= 1f) { openAt = Time.time; SetStage(Stage.Open); }
        }

        void SpawnLoot()
        {
            var player = PlayerHealth.Instance;
            var owner = player != null ? player.GetComponentInChildren<PlayerWeaponController>() : null;
            var kinds = new List<int> { 2 };                       // always some ammo
            for (int i = 1; i < lootCount; i++)
            {
                int roll = rng.Next(100);
                kinds.Add(roll < 40 ? 0 : roll < 70 ? 1 : 2);      // health 40%, armour 30%, ammo 30%
            }
            Vector3 start = transform.position + Vector3.up * 0.45f;
            for (int i = 0; i < kinds.Count; i++)
            {
                // fan out in front of the chest
                float a = Mathf.Lerp(-55f, 55f, kinds.Count == 1 ? 0.5f : i / (float)(kinds.Count - 1));
                Vector3 dir = Quaternion.Euler(0f, a, 0f) * transform.forward;
                Vector3 land = transform.position + dir * (1.3f + (float)rng.NextDouble() * 0.5f);
                if (Physics.Raycast(land + Vector3.up * 2f, Vector3.down, out var hit, 5f, ~0, QueryTriggerInteraction.Ignore)) land = hit.point;
                GameObject go = null;
                switch (kinds[i])
                {
                    case 0: go = Make(medkitPrefab, ChestReward.Kind.Health, healthAmount); break;
                    case 1: go = Make(armorPrefab, ChestReward.Kind.Armor, armorAmount); break;
                    case 2:
                        var gun = PickGun(owner);
                        if (gun != null)
                        {
                            var ammo = AmmoPickup.SpawnDrop(start, gun, gun.tableAmmo);
                            go = ammo != null ? ammo.gameObject : null;
                        }
                        if (go == null) go = Make(medkitPrefab, ChestReward.Kind.Health, healthAmount);
                        break;
                }
                if (go == null) continue;
                ChestLootFlight.Launch(go, start, land, 0.55f + i * 0.08f, 1.1f + i * 0.15f);
            }

            // a frag grenade too, in about one chest in three (GrenadeConfig.chestChance). Rolled after the other loot,
            // so their rolls (the same on every machine, seeded from the chest's spot) don't change.
            var grenadeCfg = GrenadeConfig.Get();
            if (grenadeCfg != null && rng.NextDouble() < grenadeCfg.chestChance)
            {
                Vector3 gdir = Quaternion.Euler(0f, 85f, 0f) * transform.forward;
                Vector3 gland = transform.position + gdir * 1.5f;
                if (Physics.Raycast(gland + Vector3.up * 2f, Vector3.down, out var ghit, 5f, ~0, QueryTriggerInteraction.Ignore)) gland = ghit.point;
                var grenade = GrenadePickup.Spawn(transform.position);
                if (grenade != null) ChestLootFlight.Launch(grenade, start, gland, 0.55f + kinds.Count * 0.08f, 1.1f + kinds.Count * 0.15f);
            }
        }

        GameObject Make(GameObject prefab, ChestReward.Kind kind, int amount)
        {
            if (prefab == null) return null;
            var go = Instantiate(prefab, transform.position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            var r = go.GetComponent<ChestReward>();
            if (r != null) { r.kind = kind; r.amount = amount; }
            return go;
        }

        static WeaponData PickGun(PlayerWeaponController owner)
        {
            if (owner == null) return null;
            var guns = new List<WeaponData>();
            foreach (var w in owner.weapons)
                if (w != null && w.data != null && !w.data.unlimitedUses && w.data.ammoPack != null && owner.IsUnlocked(w.data)) guns.Add(w.data);
            if (owner.CurrentWeapon != null && owner.CurrentWeapon.data != null && guns.Contains(owner.CurrentWeapon.data)) guns.Add(owner.CurrentWeapon.data);
            return guns.Count > 0 ? guns[Random.Range(0, guns.Count)] : null;
        }

        // ---------------------------------------------------------------- helpers

        static Audio.SoundLibrary Lib => Audio.SoundManager.Library;

        void Sound(AudioClip clip, float volume, float pitch)
        {
            if (clip == null) return;
            Audio.SoundManager.PlayAtPitched(clip, transform.position + Vector3.up * 0.4f, volume, pitch, 25f);
        }

        /// A one-shot burst of glowing bits (sparks, dust, gold), cleaned up by itself.
        static void Burst(Vector3 at, Color color, int count, float speed, float size, float life)
        {
            var go = new GameObject("ChestBurst");
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.2f; main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.5f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
            main.startColor = color;
            main.gravityModifier = 0.6f;
            main.maxParticles = count;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var em = ps.emission; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.08f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = FxMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
        }

        /// The additive glow material (the sparkle's own material, shared).
        public static Material FxMat;

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
