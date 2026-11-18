using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Core;
using ZombieRush.Data;
using ZombieRush.Zombie;

namespace ZombieRush.UI
{
    /// Health bars over zombies and a floating damage number for every shot that hits.
    /// One overlay canvas serves every zombie (far cheaper on mobile than a canvas each):
    /// bars and numbers are pooled clones of the two templates, placed on screen each frame
    /// from the zombie's head. Built by Zombie Rush > UI > Build Zombie Health Bars into
    /// Resources, and loaded by the first zombie that appears, so no scene needs editing.
    public class ZombieOverheadUI : MonoBehaviour
    {
        public const string ResourcePath = "ZombieRush/ZombieOverheadUI";

        public ZombieHudConfig config;
        [Tooltip("Inactive bar to clone: a dark back with a Track child holding Chip and Fill images.")]
        public RectTransform barTemplate;
        [Tooltip("Inactive damage number to clone.")]
        public Text popupTemplate;

        public static ZombieOverheadUI Instance { get; private set; }
        static bool missingWarned;

        class Bar
        {
            public RectTransform rt;
            public CanvasGroup group;
            public Image fill, chip;
            public ZombieHealth zombie;
            public float chipValue, chipWait, alpha, nextWallCheck;
            public bool wallHidden;
            public int seenFrame;
        }

        class Popup
        {
            public RectTransform rt;
            public Text text;
            public ZombieHealth zombie;
            public Vector3 world;
            public Vector2 scatter;
            public float amount, age;
            public bool headshot;
        }

        readonly Dictionary<ZombieHealth, Bar> bars = new Dictionary<ZombieHealth, Bar>();
        readonly Stack<Bar> freeBars = new Stack<Bar>();
        readonly List<Bar> barScratch = new List<Bar>();
        readonly List<Popup> popups = new List<Popup>();
        readonly Stack<Popup> freePopups = new Stack<Popup>();
        RectTransform root;
        Camera cam;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; missingWarned = false; }

        /// Makes sure the overlay exists while playing. Called by every zombie as it appears.
        public static void Ensure()
        {
            if (Instance != null || !Application.isPlaying) return;
            var prefab = Resources.Load<ZombieOverheadUI>(ResourcePath);
            if (prefab == null)
            {
                if (!missingWarned) Debug.LogWarning($"Zombie Rush: no zombie health bar prefab at Resources/{ResourcePath}. Run Zombie Rush > UI > Build Zombie Health Bars.");
                missingWarned = true;
                return;
            }
            Instantiate(prefab).name = prefab.name;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            root = (RectTransform)transform;
            if (barTemplate != null) barTemplate.gameObject.SetActive(false);
            if (popupTemplate != null) popupTemplate.gameObject.SetActive(false);
            ZombieHealth.AnyDamaged += OnZombieDamaged;
        }

        void OnDestroy()
        {
            ZombieHealth.AnyDamaged -= OnZombieDamaged;
            if (Instance == this) Instance = null;
        }

        void LateUpdate()
        {
            if (config == null) return;
            if (cam == null || !cam.isActiveAndEnabled) cam = Camera.main;
            bool playing = GameManager.Instance == null || GameManager.Instance.State == GameState.Playing;
            if (cam == null || !playing)
            {
                HideEverything();
                return;
            }
            UpdateBars();
            UpdatePopups();
        }

        // ---------------- Health bars ----------------

        void UpdateBars()
        {
            int frame = Time.frameCount;
            float dt = Time.deltaTime;
            Vector3 camPos = cam.transform.position;
            float maxSqr = config.maxDistance * config.maxDistance;

            foreach (var z in ZombieHealth.Active)
            {
                if (z == null || z.IsDead) continue;
                float fraction = z.HealthFraction;
                if (!config.showAtFullHealth && fraction >= 0.999f) continue;
                Vector3 head = z.HeadPosition + Vector3.up * config.headClearance;
                if ((head - camPos).sqrMagnitude > maxSqr) continue;

                if (!bars.TryGetValue(z, out var bar))
                {
                    bar = TakeBar();
                    if (bar == null) continue; // pool full: nearer zombies already have bars
                    bar.zombie = z;
                    bar.chipValue = fraction;
                    bar.chipWait = 0f;
                    bar.alpha = 0f;
                    bar.wallHidden = false;
                    bar.nextWallCheck = 0f;
                    bar.rt.SetAsLastSibling();
                    bars.Add(z, bar);
                }
                bar.seenFrame = frame;

                // Chip: holds for a moment after a hit, then drains down to the fill.
                if (bar.chipValue < fraction) bar.chipValue = fraction;
                else if (bar.chipWait > 0f) bar.chipWait -= dt;
                else bar.chipValue = Mathf.MoveTowards(bar.chipValue, fraction, config.chipSpeed * dt);
                UITheme.SetBarFill(bar.fill, fraction);
                UITheme.SetBarFill(bar.chip, bar.chipValue);

                if (config.hideBehindWalls && Time.time >= bar.nextWallCheck)
                {
                    bar.nextWallCheck = Time.time + config.wallCheckInterval * Random.Range(0.8f, 1.2f);
                    bar.wallHidden = Blocked(camPos, head, z);
                }

                Vector3 screen = cam.WorldToScreenPoint(head);
                bool onScreen = screen.z > 0.1f && !bar.wallHidden;
                bar.alpha = Mathf.MoveTowards(bar.alpha, onScreen ? 1f : 0f, config.fadeSpeed * dt);
                bar.group.alpha = bar.alpha;
                if (screen.z <= 0.1f) continue;

                float dist = screen.z;
                float scale = Mathf.Lerp(1f, config.farScale, Mathf.InverseLerp(config.nearDistance, config.farDistance, dist));
                bar.rt.localScale = new Vector3(scale * (z.MaxHealth >= config.bigZombieHealth ? config.bigBarWidthScale : 1f), scale, 1f);
                bar.rt.anchoredPosition = ToCanvas(screen);
            }

            // Bars whose zombie died, left, or walked out of range fade out, then go back to the pool.
            barScratch.Clear();
            foreach (var kv in bars)
            {
                var bar = kv.Value;
                if (bar.seenFrame == frame) continue;
                bar.alpha = kv.Key == null || kv.Key.IsDead ? 0f : Mathf.MoveTowards(bar.alpha, 0f, config.fadeSpeed * dt);
                bar.group.alpha = bar.alpha;
                if (bar.alpha <= 0f) barScratch.Add(bar);
            }
            foreach (var bar in barScratch) ReleaseBar(bar);
        }

        /// True when something solid (not a zombie, not the player) sits between the camera and the head.
        static bool Blocked(Vector3 from, Vector3 to, ZombieHealth zombie)
        {
            if (!Physics.Linecast(from, to, out var hit, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (hit.collider.CompareTag(GameTags.Player) || ZombieRush.World.ShootThrough.Is(hit.collider)) return false;
            return hit.collider.GetComponentInParent<ZombieHealth>() == null;
        }

        Bar TakeBar()
        {
            if (freeBars.Count > 0)
            {
                var b = freeBars.Pop();
                b.rt.gameObject.SetActive(true);
                return b;
            }
            if (barTemplate == null || bars.Count >= config.maxBars) return null;
            var rt = Instantiate(barTemplate, barTemplate.parent);
            rt.name = "Bar";
            rt.gameObject.SetActive(true);
            var track = rt.Find("Track");
            return new Bar
            {
                rt = rt,
                group = rt.GetComponent<CanvasGroup>(),
                chip = track.Find("Chip").GetComponent<Image>(),
                fill = track.Find("Fill").GetComponent<Image>(),
            };
        }

        void ReleaseBar(Bar bar)
        {
            if (bar.zombie != null) bars.Remove(bar.zombie);
            else RemoveDestroyedKey(bar);
            bar.zombie = null;
            bar.rt.gameObject.SetActive(false);
            freeBars.Push(bar);
        }

        // A destroyed zombie compares equal to null, so find its entry by value.
        void RemoveDestroyedKey(Bar bar)
        {
            ZombieHealth key = null;
            bool found = false;
            foreach (var kv in bars) if (kv.Value == bar) { key = kv.Key; found = true; break; }
            if (found) bars.Remove(key);
        }

        // ---------------- Damage numbers ----------------

        void OnZombieDamaged(ZombieHealth zombie, float amount, bool byPlayer)
        {
            if (config == null) return;
            if (bars.TryGetValue(zombie, out var bar)) bar.chipWait = config.chipDelay;
            if (!byPlayer && !config.showTurretDamage) return;

            // Every pellet of one shotgun shot lands in the same frame: add them into one number.
            foreach (var p in popups)
            {
                if (p.zombie != zombie || p.age > config.mergeWindow) continue;
                p.amount += amount;
                p.age = 0f;
                p.headshot |= byPlayer && zombie.LastHitWasHeadshot;
                Style(p, zombie.IsDead);
                return;
            }

            var popup = TakePopup();
            if (popup == null) return;
            popup.zombie = zombie;
            popup.world = zombie.HeadPosition + Vector3.up * config.headClearance;
            popup.scatter = Random.insideUnitCircle * config.popupScatter;
            popup.scatter.y = Mathf.Abs(popup.scatter.y) * 0.5f + 18f; // sit just above the bar
            popup.amount = amount;
            popup.age = 0f;
            popup.headshot = byPlayer && zombie.LastHitWasHeadshot;
            popup.rt.SetAsLastSibling();
            Style(popup, zombie.IsDead);
            popups.Add(popup);
        }

        void Style(Popup p, bool kill)
        {
            p.text.text = Mathf.Max(1, Mathf.RoundToInt(p.amount)).ToString();
            p.text.fontSize = kill ? config.killFontSize : p.headshot ? config.headshotFontSize : config.fontSize;
            p.text.color = kill ? config.killColor : p.headshot ? config.headshotColor : config.hitColor;
        }

        void UpdatePopups()
        {
            float dt = Time.deltaTime;
            for (int i = popups.Count - 1; i >= 0; i--)
            {
                var p = popups[i];
                p.age += dt;
                float t = p.age / config.popupDuration;
                if (t >= 1f)
                {
                    p.rt.gameObject.SetActive(false);
                    p.zombie = null;
                    freePopups.Push(p);
                    popups.RemoveAt(i);
                    continue;
                }

                Vector3 screen = cam.WorldToScreenPoint(p.world);
                if (screen.z <= 0.1f) { SetPopupAlpha(p, 0f); continue; }

                float rise = config.popupRise * (1f - (1f - t) * (1f - t)); // ease out
                p.rt.anchoredPosition = ToCanvas(screen) + p.scatter + new Vector2(0f, rise);
                float punch = p.age < config.popupPunchTime
                    ? Mathf.Lerp(config.popupPunch, 1f, p.age / config.popupPunchTime) : 1f;
                p.rt.localScale = new Vector3(punch, punch, 1f);
                SetPopupAlpha(p, t < config.popupFadeStart ? 1f : 1f - Mathf.InverseLerp(config.popupFadeStart, 1f, t));
            }
        }

        static void SetPopupAlpha(Popup p, float a)
        {
            var c = p.text.color;
            c.a = a;
            p.text.color = c;
        }

        Popup TakePopup()
        {
            Popup p;
            if (freePopups.Count > 0) p = freePopups.Pop();
            else
            {
                if (popupTemplate == null) return null;
                if (popups.Count >= config.maxPopups)
                {
                    // Recycle the oldest number rather than drop a new hit.
                    p = popups[0];
                    popups.RemoveAt(0);
                    return p;
                }
                var text = Instantiate(popupTemplate, popupTemplate.transform.parent);
                text.name = "Popup";
                p = new Popup { rt = text.rectTransform, text = text };
            }
            p.rt.gameObject.SetActive(true);
            return p;
        }

        // ---------------- Shared ----------------

        Vector2 ToCanvas(Vector3 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out var local);
            return local;
        }

        void HideEverything()
        {
            barScratch.Clear();
            barScratch.AddRange(bars.Values);
            foreach (var bar in barScratch) ReleaseBar(bar);
            foreach (var p in popups) { p.rt.gameObject.SetActive(false); p.zombie = null; freePopups.Push(p); }
            popups.Clear();
        }
    }
}
