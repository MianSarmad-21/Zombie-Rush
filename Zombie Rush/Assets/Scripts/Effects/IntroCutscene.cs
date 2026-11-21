using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using ZombieRush.Core;
using ZombieRush.Data;
using ZombieRush.Player;
using ZombieRush.World;

namespace ZombieRush.Effects
{
    /// What the intro says and how it is paced. Edit the lines and times here.
    [Serializable]
    public class IntroConfig
    {
        /// Caption: under the picture. Title: the big name. Orders / OrdersSub: centred on the black screen after the landing.
        public enum Style { Caption, Title, Orders, OrdersSub }
        [Serializable] public class Line { public float at, until; public string text; public Style style; }

        public Line[] lines =
        {
            new Line { at = 0.8f, until = 3.6f, text = "ZOMBIE RUSH", style = Style.Title },
            new Line { at = 3.9f, until = 7.6f, text = "DAY 47 OF THE OUTBREAK. THE CITY FELL IN ONE NIGHT." },
            new Line { at = 8.0f, until = 12.8f, text = "ONE OUTPOST STILL STANDS: HAVEN." },
            new Line { at = 13.8f, until = 18.8f, text = "TAKE THE CITY BACK, STREET BY STREET." },
            new Line { at = 21.9f, until = 25.0f, text = "YOUR ORDERS: HOLD THE ZONE. PUSH THE DEAD BACK.", style = Style.Orders },
            new Line { at = 22.6f, until = 25.0f, text = "GEAR UP, SURVIVOR.", style = Style.OrdersSub },
        };

        [Tooltip("When each shot starts, seconds: outside the cabin door on the player (sitting in it), over their shoulder " +
                 "out of the door, the landing. The first shot (the helicopter coming in over the city) starts at 0.")]
        public float shotCabin = 6f, shotOverShoulder = 10.2f, shotLanding = 13.6f;
        [Tooltip("The screen fades to black from `fadeOut` over `fadeOutTime` seconds; the orders show on black; the intro ends at `end` " +
                 "and play fades in from black over `fadeInTime` seconds.")]
        public float fadeOut = 20f, fadeOutTime = 1.4f, end = 25.4f, fadeInTime = 1f;
        [Tooltip("Helicopter timeline, seconds: arrives in a hover over the pad, skids down.")]
        public float heliHover = 16.6f, heliTouchdown = 19.4f;
        [Tooltip("Seconds it spends slowing from cruise speed into the hover (nose up, the flare).")]
        public float slowDown = 6.5f;
        [Tooltip("Heights above the pad, metres: where it comes in, and the hover before it settles.")]
        public float flightAltitude = 26f, hoverHeight = 4f;
        [Tooltip("How far outside the zone wall it starts, metres (small: it is in sight from the first frame).")]
        public float startBeyondZone = 6f;
        [Tooltip("Length of the straight final approach into the pad, metres.")]
        public float finalApproach = 30f;
        [Tooltip("How far off the final approach line it enters, degrees, measured round from straight behind the pad. " +
                 "Under 90 it comes in from behind the pad and banks once to line up; never past it and back.")]
        [Range(0f, 80f)] public float entryAngle = 40f;
        [Tooltip("Rotor blur: how solid the spinning blades are, and how dark the main rotor's blur disc is.")]
        [Range(0f, 1f)] public float bladeAlpha = 0.45f, discAlpha = 0.18f;
        [Tooltip("Main and tail rotor speed, degrees per second (lower than real, so it reads on screen).")]
        public float mainRotorSpeed = 760f, tailRotorSpeed = 1500f;
        [Tooltip("Which cabin door the player sits in when the base has no helipad stairs: +1 right of the nose, -1 left. " +
                 "With stairs it is the side they are on.")]
        public float doorSide = 1f;
        [Tooltip("The troop seats (Blender/heli_refit.py), in the helicopter's own frame (metres from its origin under the skids): " +
                 "the middle of each seat's front edge, at the seat's top. Two rows of two, all facing the cockpit. The player " +
                 "takes the front seat on the side the opening camera sees; in a squad match the others fill the rest.")]
        public Vector3[] seats =
        {
            new Vector3(0.45f, 1.20f, 2.55f), new Vector3(0.45f, 1.20f, 1.55f),
            new Vector3(-0.45f, 1.20f, 2.55f), new Vector3(-0.45f, 1.20f, 1.55f),
        };
        [Tooltip("How many ride in the helicopter in a squad match (solo: the player alone). Seats with no squadmate yet " +
                 "get a stand-in operative.")]
        public int squadSeats = 4;
        [Tooltip("Seconds of black before the picture fades in, so the first frames (still loading) are never seen.")]
        public float holdBlack = 0.4f;
        [Tooltip("How far beside the flight path the opening fly-by camera waits, metres.")]
        public float flyByDistance = 18f;
        [Range(0f, 1f)] public float rotorVolume = 0.85f;
        [Tooltip("Share of the rotor volume left once the screen is black (the engine winding down).")]
        [Range(0f, 1f)] public float rotorVolumeOnBlack = 0.3f;
    }

    /// The match intro, played when a match starts (every match, skippable): a helicopter comes in over the
    /// mist wall from outside the city, crosses the zone and lands on the fortress helipad (FortressHaven/Helipad,
    /// or the yard in front of the base when there is none). The player rides sitting in the open cabin door, legs
    /// over the edge, pistol held low (IntroMoves.sit, made in Blender/player_anim.py SEATS["Sit_Pistol"]): one shot
    /// from outside the door, one over their shoulder out of it, then the landing. After touchdown the screen goes
    /// black, the orders show, and play fades in with the player at the weapon table. Letterbox bars and story lines
    /// carry the "why". Built in code from Resources/ZombieRush/Cutscene (the helicopter, prepared by
    /// Blender/heli_prepare.py); nothing in the scene is changed and everything it makes is removed at the end. Runs
    /// while the game is in its Lobby state, so the HUD is hidden and nothing else moves; GameManager starts play
    /// when it ends.
    public class IntroCutscene : MonoBehaviour
    {
        public static IntroConfig Config = new IntroConfig();
        /// Off in automated tests (PlayCaptureTools turns it on only for the "intro" test).
        public static bool Enabled = true;
        public static bool Playing { get; private set; }
        public static IntroCutscene Current { get; private set; }
        /// Seconds into the intro (tests read it).
        public float Clock => clock;

        const string HeliPath = "ZombieRush/Cutscene/Helicopter", HeliTexPath = "ZombieRush/Cutscene/Helicopter_Albedo";
        // materials made by IntroHeliTools from Blender/heli_refit.py's textures
        const string PaintPath = "ZombieRush/Cutscene/Helicopter_Paint", SeatPaintPath = "ZombieRush/Cutscene/Seat_Paint";

        // every Play session starts with the intro on (a test may have switched it off in the last one)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Enabled = true; PreviewSquad = false; Playing = false; Current = null; }
        /// Tests only: seat a full squad as in a squad match, without a network.
        public static bool PreviewSquad;

        Action done;
        float clock;
        bool finished;

        // the set: where it lands and which way it faces, which door the player sits in, where play starts
        Vector3 pad, padFwd, endSpot, endFace;
        float doorSign = 1f;
        // who rides: each on a troop seat, the seated clip (IntroMoves) over their own Animator, the gun on the
        // animated right hand; the player first, then squadmates (stand-in operatives, removed at the end)
        class Sitter { public Transform who; public CharacterController body; public BodyGunPose gun; public PlayableGraph graph; public Vector3 seat; public bool standIn; }
        readonly List<Sitter> sitters = new List<Sitter>();
        readonly List<GameObject> hiddenTeammates = new List<GameObject>();
        IntroMoveClips moves;
        bool seated, squad;
        Vector3 playerSeat;
        Transform heli, mainRotor, tailRotor;
        ParticleSystem dust;
        AudioSource rotorSound;
        // the flight: a flat path (in over the mist, a banked turn round the base, a straight final into the pad)
        Vector3[] path; float[] pathLen; float pathTotal, cruiseSpeed, slowTime, turnSign;
        Vector3 entry; float entryDist;   // where the path comes out of the mist into the zone, and how far along it that is
        readonly List<UnityEngine.Object> made = new List<UnityEngine.Object>();

        // the camera and the player
        Camera cam, mainCam;
        AudioListener mainListener;
        PlayerViewSwitcher view;
        CharacterView viewBefore;
        CharacterController body;
        Transform player;
        Vector3 camPos, camLook; bool camSet;

        // the overlay
        Canvas canvas;
        RectTransform barTop, barBottom;
        Text title, caption, orders, ordersSub, skipHint;
        Image fader;
        readonly List<Canvas> hiddenCanvases = new List<Canvas>();

        /// Starts the intro if it can play; `onDone` runs when it ends (or right away if it can't).
        public static bool TryPlay(Action onDone)
        {
            if (!Enabled || Playing || Resources.Load<GameObject>(HeliPath) == null || PlayerHealth.Instance == null) return false;
            var go = new GameObject("IntroCutscene");
            Current = go.AddComponent<IntroCutscene>();
            Current.done = onDone;
            Playing = true;
            return true;
        }

        void Start()
        {
            try { Build(); }
            catch (Exception e) { Debug.LogException(e); Finish(); }
        }

        void OnDestroy()
        {
            if (Current == this) { Current = null; Playing = false; }
            StopMoves();
            foreach (var o in made) if (o != null) Destroy(o);
            made.Clear();
        }

        // ------------------------------------------------------------------ the set

        void Build()
        {
            var ph = PlayerHealth.Instance;
            player = ph.transform;
            body = player.GetComponent<CharacterController>();
            view = player.GetComponent<PlayerViewSwitcher>();
            if (view != null) { viewBefore = view.View; view.SetView(CharacterView.ThirdPerson); }

            moves = Resources.Load<IntroMoveClips>(IntroMoveClips.ResourcePath);
            squad = ZombieRush.Net.NetSession.InSquad || PreviewSquad;

            FindLanding();
            FindWeapons();
            PlanFlight();
            PlanFlyBy();
            SpawnHelicopter();
            SitDown();
            HideSafeZoneSign(true);
            MakeCamera();
            MakeOverlay();
            Debug.Log($"[Intro] door side {(doorSign > 0f ? "right" : "left")}, seated {seated} ({(moves != null && moves.sit != null ? moves.sit.name : "no sit clip")}), " +
                      $"{(squad ? "squad" : "solo")}: {sitters.Count} aboard, " +
                      $"pad {pad:F1}, play starts at {endSpot:F1}, ends {Config.end:0.0} s");
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        static Vector3 Ground(Vector3 p, float above = 30f)
        {
            var hits = Physics.RaycastAll(p + Vector3.up * above, Vector3.down, above + 40f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (var h in hits)
            {
                if (h.collider is CharacterController || h.collider.GetComponentInParent<PlayerHealth>() != null) continue;
                best = Mathf.Max(best, h.point.y);
            }
            if (!float.IsNegativeInfinity(best)) p.y = best;
            return p;
        }

        static Transform FindNamed(string name)
        {
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (t.name == name) return t;
            return null;
        }

        void FindLanding()
        {
            var sb = SafeBase.Instance;
            Vector3 baseCentre = sb != null ? sb.Center : player.position;
            var helipad = FindNamed("Helipad");
            bool roof = helipad == null && sb != null && sb.building != null && TryRoof(sb.building, out pad, out padFwd);
            if (helipad != null || roof)
            {
                if (helipad != null)
                {
                    pad = helipad.position;
                    padFwd = Flat(helipad.forward).sqrMagnitude > 0.01f ? Flat(helipad.forward).normalized : Vector3.forward;
                }
                // the way down (the steel stairs' path) when the base has it: the player sits in the door on that side,
                // and play starts at its foot when there is no weapon table
                var way = new List<Vector3>();
                for (int i = 0; helipad != null && i < 8; i++)
                {
                    var t = FindNamed($"Helipad_Path_{i}");
                    if (t == null) break;
                    way.Add(t.position);
                }
                Vector3 right = Vector3.Cross(Vector3.up, padFwd);
                doorSign = way.Count > 0 ? Mathf.Sign(Vector3.Dot(Flat(way[0] - pad), right)) : Mathf.Sign(Config.doorSide);
                if (way.Count >= 2) { endSpot = way[way.Count - 1]; endFace = Flat(way[way.Count - 1] - way[way.Count - 2]); }
                else
                {
                    Vector3 away = Flat(baseCentre - pad).sqrMagnitude > 1f ? Flat(baseCentre - pad).normalized : right * doorSign;
                    endSpot = Ground(pad + right * doorSign * 3.5f + away * 3f, 2f); endFace = away;
                }
                return;
            }

            // no helipad: the yard in front of the base door, on open ground big enough for the rotor
            Vector3 front = sb != null && sb.playerStart != null ? Flat(sb.playerStart.forward).normalized : Vector3.forward;
            if (front.sqrMagnitude < 0.01f) front = Vector3.forward;
            float r = sb != null ? sb.radius : 10f;
            pad = Ground(baseCentre + front * (r + 12f));
            for (int i = 0; i < 16; i++)
            {
                Vector3 probe = Ground(baseCentre + Quaternion.AngleAxis((i % 2 == 0 ? 1 : -1) * (i / 2) * 15f, Vector3.up) * front * (r + 12f + (i / 4) * 4f));
                if (!Physics.CheckBox(probe + Vector3.up * 3.2f, new Vector3(7f, 2.6f, 7f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) { pad = probe; front = Flat(probe - baseCentre).normalized; break; }
            }
            padFwd = Quaternion.AngleAxis(-90f * Config.doorSide, Vector3.up) * -front;   // side-on to the base: the door faces it
            doorSign = Mathf.Sign(Config.doorSide);
            endSpot = Ground(pad + Vector3.Cross(Vector3.up, padFwd) * doorSign * 3.5f - front * 3f, 2f);
            endFace = -front;
        }

        /// The top deck of the base building: the highest flat spot near the middle of its roof, found by casting
        /// down over a small grid round its centre (masts and thin junk are skipped: they aren't flat or wide).
        static bool TryRoof(Transform building, out Vector3 spot, out Vector3 fwd)
        {
            spot = default; fwd = Vector3.forward;
            var rs = building.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return false;
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            if (b.size.y < 4f) return false;
            float best = float.NegativeInfinity;
            for (int x = -2; x <= 2; x++)
                for (int z = -2; z <= 2; z++)
                {
                    Vector3 from = new Vector3(b.center.x + x * 2f, b.max.y + 5f, b.center.z + z * 2f);
                    if (!Physics.Raycast(from, Vector3.down, out var h, b.size.y + 10f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (h.normal.y < 0.92f || h.point.y < b.min.y + b.size.y * 0.4f) continue;
                    // the deck must be wide enough for the skids
                    if (Physics.CheckBox(h.point + Vector3.up * 1.6f, new Vector3(1.6f, 1.2f, 2.6f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                    // prefer the middle of the deck: a higher spot wins only by a clear step
                    float score = h.point.y - (Mathf.Abs(x) + Mathf.Abs(z)) * 0.15f;
                    if (score > best) { best = score; spot = h.point; }
                }
            if (float.IsNegativeInfinity(best)) return false;
            fwd = Flat(building.forward).sqrMagnitude > 0.01f ? Flat(building.forward).normalized : Vector3.forward;
            return true;
        }

        /// Play starts just short of the weapon table nearest the base's start, facing it (when there is one).
        void FindWeapons()
        {
            var sb = SafeBase.Instance;
            Vector3 from = sb != null && sb.playerStart != null ? sb.playerStart.position : player.position;
            BasePickup best = null; float bestD = float.MaxValue;
            foreach (var p in FindObjectsByType<BasePickup>(FindObjectsSortMode.None))
            {
                if (p.kind != PickupKind.Weapon) continue;
                float d = (p.transform.position - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = p; }
            }
            if (best == null) return;
            Vector3 table = best.transform.position;
            Vector3 approach = Flat(table - from);
            approach = approach.sqrMagnitude > 0.25f ? approach.normalized : Flat(table - player.position).normalized;
            if (approach.sqrMagnitude < 0.01f) approach = padFwd;
            endSpot = Ground(table - approach * 1.6f, 2f);   // just short of the table (not picking up)
            endFace = approach;
        }

        /// The flight as a real helicopter lands: out of the mist at cruise speed, a banked turn round the base to
        /// line up, a straight final along the pad's nose line while it slows and descends, a hover, then down.
        /// One smooth speed curve over one path, so it never stalls or drifts back and forth.
        void PlanFlight()
        {
            var cfg = Config;
            var zm = ZoneManager.Instance;
            Vector3 c = zm != null ? Flat(zm.center) : Flat(pad);
            float rad = zm != null ? zm.CurrentRadius : 50f;
            Vector3 p = Flat(pad), fwd = padFwd;

            // the straight final: behind the pad along its nose line, kept inside the zone
            float fin = cfg.finalApproach;
            while (fin > 10f && Vector3.Distance(p - fwd * fin, c) > rad - 6f) fin -= 2f;
            Vector3 f = p - fwd * fin;

            // the entry: out in the mist behind the pad, off to one side, heading straight for the start of the
            // final, so the one banked turn lines it up without ever flying past the pad and coming back
            Vector3 s = c + Quaternion.AngleAxis(Mathf.Clamp(cfg.entryAngle, 0f, 80f), Vector3.up) * -fwd * (rad + cfg.startBeyondZone);
            float k = Vector3.Distance(s, f);
            Vector3 b1 = s + Flat(f - s).normalized * k * 0.35f, b2 = f - fwd * Mathf.Min(k * 0.35f, 16f);
            turnSign = Mathf.Sign(Vector3.Cross(b1 - s, fwd).y);   // +1: it turns right to line up

            var pts = new List<Vector3>();
            const int curve = 120, line = 30;
            for (int i = 0; i <= curve; i++)
            {
                float u = i / (float)curve, w = 1f - u;
                pts.Add(w * w * w * s + 3f * w * w * u * b1 + 3f * w * u * u * b2 + u * u * u * f);
            }
            for (int i = 1; i <= line; i++) pts.Add(Vector3.Lerp(f, p, i / (float)line));

            // once in, the turn stays inside the zone (out of the mist): pull any bulge in, then smooth the kinks
            int inside = pts.FindIndex(q => Vector3.Distance(q, c) <= rad - 3f);
            if (inside < 0) inside = 0;
            for (int i = inside + 1; i < pts.Count; i++)
            {
                Vector3 d = pts[i] - c;
                if (d.magnitude > rad - 6f) pts[i] = c + d.normalized * (rad - 6f);
            }
            for (int pass = 0; pass < 8; pass++)
                for (int i = Mathf.Max(1, inside); i < pts.Count - line - 1; i++)   // the start of the final stays put
                    pts[i] = (pts[i - 1] + pts[i] * 2f + pts[i + 1]) * 0.25f;

            path = pts.ToArray();
            pathLen = new float[path.Length];
            for (int i = 1; i < path.Length; i++) pathLen[i] = pathLen[i - 1] + Vector3.Distance(path[i - 1], path[i]);
            pathTotal = Mathf.Max(1f, pathLen[path.Length - 1]);
            entry = path[inside];
            entryDist = pathLen[inside];

            // cruise speed so it covers the path at speed, then slows to a stop over the pad on time
            slowTime = Mathf.Clamp(cfg.slowDown, 0.5f, cfg.heliHover - 0.5f);
            cruiseSpeed = pathTotal / (cfg.heliHover - slowTime * 0.5f);
        }

        Vector3 PathPoint(float d)
        {
            if (d <= 0f) return path[0];
            if (d >= pathTotal) return path[path.Length - 1];
            int lo = 0, hi = pathLen.Length - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (pathLen[mid] < d) lo = mid; else hi = mid; }
            // a smooth curve through the points (Catmull-Rom), not straight pieces: the corners between pieces
            // made the bank and pitch, worked out from the acceleration, twitch
            float u = Mathf.InverseLerp(pathLen[lo], pathLen[hi], d), u2 = u * u, u3 = u2 * u;
            Vector3 p0 = path[Mathf.Max(lo - 1, 0)], p1 = path[lo], p2 = path[hi], p3 = path[Mathf.Min(hi + 1, path.Length - 1)];
            return 0.5f * (2f * p1 + (p2 - p0) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (3f * p1 - p0 - 3f * p2 + p3) * u3);
        }

        Vector3 HeliAt(float t)
        {
            var cfg = Config;
            float T = cfg.heliHover;
            // distance flown: steady cruise, then an even slow-down to a stop over the pad
            float d = t <= T - slowTime ? cruiseSpeed * Mathf.Max(0f, t)
                    : t < T ? pathTotal - cruiseSpeed * (T - t) * (T - t) / (2f * slowTime)
                    : pathTotal;
            Vector3 flat = PathPoint(d);
            // height: an even glide down as it closes on the pad, then the last few metres straight down
            float h = t < T ? cfg.hoverHeight + (cfg.flightAltitude - cfg.hoverHeight) * Mathf.Pow((pathTotal - d) / pathTotal, 0.85f)
                            : cfg.hoverHeight * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(T, cfg.heliTouchdown, t)));
            // a little life in the hover: a helicopter never sits perfectly still in the air
            float sway = Mathf.Clamp01((t - (T - 1.5f)) / 1.5f) * Mathf.Clamp01((cfg.heliTouchdown - 0.2f - t) / 0.8f);
            Vector3 drift = new Vector3(Mathf.Sin(t * 1.3f) * 0.18f, Mathf.Sin(t * 1.9f + 1f) * 0.1f, Mathf.Cos(t * 1.1f) * 0.18f) * sway;
            return new Vector3(flat.x, pad.y + h, flat.z) + drift;
        }

        void SpawnHelicopter()
        {
            var prefab = Resources.Load<GameObject>(HeliPath);
            var go = Instantiate(prefab);
            go.name = "IntroHelicopter";
            heli = go.transform;
            mainRotor = FindChild(heli, "MainRotor");
            tailRotor = FindChild(heli, "TailRotor");
            var tex = Resources.Load<Texture2D>(HeliTexPath);
            var mat = Resources.Load<Material>(PaintPath);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (mat == null && shader != null)
            {
                // no saved paint yet (IntroHeliTools not run): the plain albedo
                mat = new Material(shader) { name = "IntroHelicopter" };
                if (tex != null) { mat.SetTexture("_BaseMap", tex); mat.mainTexture = tex; }
                mat.SetFloat("_Smoothness", 0.28f);
                mat.SetFloat("_Metallic", 0.15f);
                made.Add(mat);
            }
            var seatMat = Resources.Load<Material>(SeatPaintPath);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r.name == "Seats" && seatMat != null) r.sharedMaterial = seatMat;
                else if (mat != null) r.sharedMaterial = mat;
            }
            foreach (var col in go.GetComponentsInChildren<Collider>()) col.enabled = false;
            MakeRotorBlur(tex);

            rotorSound = go.AddComponent<AudioSource>();
            rotorSound.clip = ZombieRush.Audio.ProceduralSfx.RotorLoop;
            rotorSound.loop = true;
            rotorSound.spatialBlend = 1f;
            rotorSound.rolloffMode = AudioRolloffMode.Linear;
            rotorSound.minDistance = 12f; rotorSound.maxDistance = 320f;
            rotorSound.dopplerLevel = 0.4f;
            rotorSound.volume = Config.rotorVolume * GameSettings.SfxVolume;
            rotorSound.Play();

            dust = MakeDust();
            Place(0f);
        }

        /// Spinning rotors read as a blur on film, not as solid blades: the blades go part see-through and a faint
        /// dark disc fills their sweep.
        void MakeRotorBlur(Texture2D heliTex)
        {
            var blades = SeeThrough("IntroRotorBlades", heliTex, new Color(1f, 1f, 1f, Config.bladeAlpha));
            if (blades == null) return;

            var discTex = new Texture2D(128, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "IntroRotorDisc" };
            var px = new Color32[128 * 128];
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                {
                    float r = Mathf.Sqrt((x - 63.5f) * (x - 63.5f) + (y - 63.5f) * (y - 63.5f)) / 63.5f;
                    float a = r >= 1f ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.35f, r)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.9f, 1f, r))) * (0.7f + 0.3f * r);
                    px[y * 128 + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            discTex.SetPixels32(px);
            discTex.Apply(false, true);
            made.Add(discTex);
            var disc = SeeThrough("IntroRotorDisc", discTex, new Color(0.12f, 0.12f, 0.13f, Config.discAlpha));
            var discMesh = DiscMesh(48);

            foreach (var (rotor, normal) in new[] { (mainRotor, Vector3.up), (tailRotor, Vector3.right) })
            {
                if (rotor == null) continue;
                foreach (var r in rotor.GetComponentsInChildren<Renderer>()) r.sharedMaterial = blades;
                // the small tail rotor reads as a blur from its see-through blades alone; its mesh's pivot is off the
                // hub, so a disc sized from its bounds came out far too big
                if (rotor == tailRotor) continue;
                var mf = rotor.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                // the sweep radius: the farthest blade tip from the hub (the rotor's pivot), across the spin plane
                Bounds b = mf.sharedMesh.bounds;
                Vector3 far = Vector3.Max(Abs(b.min), Abs(b.max));
                float radius = normal == Vector3.up ? Mathf.Max(far.x, far.z) : Mathf.Max(far.y, far.z);
                var q = new GameObject("RotorBlur");
                q.transform.SetParent(rotor, false);
                q.transform.localRotation = Quaternion.FromToRotation(Vector3.back, normal);   // the disc faces -Z
                q.transform.localScale = Vector3.one * radius * 2f;
                q.AddComponent<MeshFilter>().sharedMesh = discMesh;
                var mr = q.AddComponent<MeshRenderer>();
                mr.sharedMaterial = disc;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
        }

        /// A flat round disc, diameter 1, in the XY plane facing -Z (like Unity's quad), UVs across it 0..1. Round
        /// itself, so the blur never shows a square edge whatever the texture's alpha does.
        Mesh DiscMesh(int segments)
        {
            var v = new Vector3[segments + 1];
            var uv = new Vector2[segments + 1];
            var tri = new int[segments * 3];
            v[0] = Vector3.zero; uv[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                v[i + 1] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.5f;
                uv[i + 1] = new Vector2(0.5f + v[i + 1].x, 0.5f + v[i + 1].y);
                tri[i * 3] = 0; tri[i * 3 + 1] = i + 1; tri[i * 3 + 2] = (i + 1) % segments + 1;
            }
            var m = new Mesh { name = "IntroRotorDisc", vertices = v, uv = uv, triangles = tri };
            m.RecalculateNormals(); m.RecalculateBounds();
            if (Vector3.Dot(m.normals[0], Vector3.back) < 0f) { System.Array.Reverse(tri); m.triangles = tri; m.RecalculateNormals(); }
            made.Add(m);
            return m;
        }

        /// A see-through URP Lit material, set up exactly like the zone wall's (premultiplied alpha), so the
        /// shader variant it needs is already in every build.
        Material SeeThrough(string name, Texture tex, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return null;
            var m = new Material(shader) { name = name };
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 1f);   // premultiply
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.SetFloat("_AlphaClip", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetFloat("_Smoothness", 0.2f);
            if (tex != null) { m.mainTexture = tex; m.SetTexture("_BaseMap", tex); }
            SetColor(m, color);
            made.Add(m);
            return m;
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        static void SetColor(Material m, Color c)
        {
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }

        static Transform FindChild(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        ParticleSystem MakeDust()
        {
            var go = new GameObject("RotorWash");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.loop = true; m.playOnAwake = false; m.maxParticles = 160;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(3f, 7f);
            m.startSize = new ParticleSystem.MinMaxCurve(1.6f, 3.4f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startColor = new Color(0.42f, 0.38f, 0.32f, 0.5f);
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            var e = ps.emission; e.rateOverTime = 0f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 2.5f; sh.radiusThickness = 0.2f;
            sh.rotation = new Vector3(90f, 0f, 0f);   // flat on the ground, blowing outward
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 2.2f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = FxMaterials.Fx("Fx_Smoke", Color.white, true);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        float heliYaw, heliPitch, heliRoll, pitchVel, rollVel; bool heliPlaced;
        // how steady it flies: the mean change in its pitch and roll rates per frame, deg/s (logged at the end)
        float lastPitchRate, lastRollRate, wobbleSum; int wobbleFrames;

        /// Puts the helicopter where the timeline says: heading along its path, nose down at speed, banking into
        /// turns, flaring nose-up as it slows over the pad, then sitting square on its skids.
        void Place(float t)
        {
            // velocity and acceleration over a wide window (H seconds each way): a real helicopter's attitude follows
            // the flight's trend, not every small turn of the path
            const float H = 0.6f;
            Vector3 p = HeliAt(t), ahead = HeliAt(t + H), behind = HeliAt(Mathf.Max(0f, t - H));
            Vector3 v = (ahead - behind) / (2f * H), a = (ahead - 2f * p + behind) / (H * H);
            Vector3 fv = Flat(v);
            float landed = Mathf.InverseLerp(Config.heliHover - 2.5f, Config.heliHover, t);    // 0 in flight .. 1 over the pad
            float yawTarget = fv.sqrMagnitude > 1f ? Mathf.Atan2(fv.x, fv.z) * Mathf.Rad2Deg : heliYaw;
            float padYaw = Mathf.Atan2(padFwd.x, padFwd.z) * Mathf.Rad2Deg;
            yawTarget = Mathf.LerpAngle(yawTarget, padYaw, landed);
            if (!heliPlaced) heliYaw = yawTarget;
            heliYaw = Mathf.LerpAngle(heliYaw, yawTarget, 1f - Mathf.Exp(-3f * Time.deltaTime));
            Quaternion yaw = Quaternion.Euler(0f, heliYaw, 0f);
            float fwdAccel = Vector3.Dot(a, yaw * Vector3.forward), sideAccel = Vector3.Dot(a, yaw * Vector3.right);
            float speed = fv.magnitude;
            float onGround = t >= Config.heliTouchdown - 0.05f ? 1f : 0f;
            // nose down at speed, nose up while it slows (the flare), banked into the turn
            float pitchTarget = (Mathf.Clamp(speed * 0.6f, 0f, 10f) + Mathf.Clamp(fwdAccel * 3.5f, -12f, 6f)) * (1f - onGround);
            float rollTarget = Mathf.Clamp(-sideAccel * 3.5f, -22f, 22f) * (1f - onGround);
            float pitchWas = heliPitch, rollWas = heliRoll;
            heliPitch = Mathf.SmoothDamp(heliPitch, pitchTarget, ref pitchVel, 0.5f);
            heliRoll = Mathf.SmoothDamp(heliRoll, rollTarget, ref rollVel, 0.5f);
            if (!heliPlaced) { heliPitch = pitchTarget; heliRoll = rollTarget; pitchWas = heliPitch; rollWas = heliRoll; }
            float dt = Mathf.Max(Time.deltaTime, 1e-4f), pitchRate = (heliPitch - pitchWas) / dt, rollRate = (heliRoll - rollWas) / dt;
            if (heliPlaced && t < Config.heliTouchdown) { wobbleSum += Mathf.Abs(pitchRate - lastPitchRate) + Mathf.Abs(rollRate - lastRollRate); wobbleFrames++; }
            lastPitchRate = pitchRate; lastRollRate = rollRate;
            heli.SetPositionAndRotation(p, yaw * Quaternion.Euler(heliPitch, 0f, heliRoll));
            heliPlaced = true;

            // rotor wash: dust kicks up under it when it comes low
            float height = p.y - pad.y;
            var e = dust.emission;
            e.rateOverTime = Mathf.Lerp(90f, 0f, Mathf.InverseLerp(2f, 12f, height));
            dust.transform.position = new Vector3(p.x, pad.y + 0.2f, p.z);
        }

        // ------------------------------------------------------------------ camera and overlay

        void MakeCamera()
        {
            mainCam = Camera.main;
            var go = new GameObject("IntroCamera") { tag = "MainCamera" };   // Camera.main for shakes and the like while it runs
            go.transform.SetParent(transform, false);
            cam = go.AddComponent<Camera>();
            if (mainCam != null)
            {
                cam.CopyFrom(mainCam);
                cam.targetTexture = null;
                mainCam.enabled = false;
                mainListener = mainCam.GetComponent<AudioListener>();
                if (mainListener != null) mainListener.enabled = false;
            }
            cam.depth = 50f;
            cam.nearClipPlane = 0.05f;   // the shot from inside the cabin
            cam.fieldOfView = 50f;
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, 900f);
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.renderType = CameraRenderType.Base;
            go.AddComponent<AudioListener>();
        }

        void MakeOverlay()
        {
            // hide the other screens (HUD, notices) for the length of the intro
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.isRootCanvas && c.enabled && c.renderMode != RenderMode.WorldSpace) { c.enabled = false; hiddenCanvases.Add(c); }

            var go = new GameObject("IntroOverlay", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 600;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            barTop = Bar("BarTop", new Vector2(0f, 1f));
            barBottom = Bar("BarBottom", new Vector2(0f, 0f));

            // the fade sits under the words, so the title and the orders read on black
            var f = new GameObject("Fader", typeof(RectTransform));
            f.transform.SetParent(canvas.transform, false);
            var frt = (RectTransform)f.transform; frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = frt.offsetMax = Vector2.zero;
            fader = f.AddComponent<Image>(); fader.color = Color.black; fader.raycastTarget = false;

            var banner = FindAnyObjectByType<ZombieRush.UI.WaveBannerUI>(FindObjectsInactive.Include);
            Font display = banner != null && banner.titleText != null ? banner.titleText.font : null;
            Font detail = banner != null && banner.detailText != null ? banner.detailText.font : display;
            if (display == null) display = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (detail == null) detail = display;

            var ink = new Color(0.93f, 0.9f, 0.84f);
            title = MakeText("Title", display, 150, new Vector2(0.5f, 0.56f), ink, 0.18f);
            caption = MakeText("Caption", detail, 46, new Vector2(0.5f, 0.165f), ink, 0.12f);
            orders = MakeText("Orders", display, 64, new Vector2(0.5f, 0.53f), ink, 0.14f);
            ordersSub = MakeText("OrdersSub", detail, 40, new Vector2(0.5f, 0.45f), new Color(0.78f, 0.62f, 0.36f), 0.12f);
            skipHint = MakeText("Skip", detail, 26, new Vector2(0.91f, 0.045f), new Color(0.7f, 0.7f, 0.7f), 0.1f);
            skipHint.text = GameSettings.UseTouchControls ? "TAP TO SKIP" : "PRESS ANY KEY TO SKIP";
            skipHint.alignment = TextAnchor.MiddleRight;
        }

        RectTransform Bar(string name, Vector2 anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, anchor.y); rt.anchorMax = new Vector2(1f, anchor.y);
            rt.pivot = new Vector2(0.5f, anchor.y);
            rt.sizeDelta = new Vector2(0f, 0f);
            var img = go.AddComponent<Image>(); img.color = Color.black; img.raycastTarget = false;
            return rt;
        }

        Text MakeText(string name, Font font, int size, Vector2 anchor, Color color, float spacing)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = new Vector2(1700f, size * 1.6f);
            var t = go.AddComponent<Text>();
            t.font = font; t.fontSize = size; t.color = new Color(color.r, color.g, color.b, 0f);
            t.alignment = TextAnchor.MiddleCenter; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            var sh = go.AddComponent<Shadow>(); sh.effectColor = new Color(0f, 0f, 0f, 0.85f); sh.effectDistance = new Vector2(0f, -4f);
            return t;
        }

        void Shoot(Vector3 pos, Vector3 look, float fov, float smooth)
        {
            if (!camSet || smooth <= 0f) { camPos = pos; camLook = look; camSet = true; }
            else
            {
                float k = 1f - Mathf.Exp(-smooth * Time.deltaTime);
                camPos = Vector3.Lerp(camPos, pos, k);
                camLook = Vector3.Lerp(camLook, look, k);
            }
            cam.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(camLook - camPos, Vector3.up));
            cam.fieldOfView = fov;
        }

        /// A camera riding with the helicopter (no lag, which at its speed would leave it behind): `roll` is how much of
        /// its bank the camera takes, so the horizon tilts as it turns.
        void Ride(Vector3 pos, Vector3 look, float fov, float roll)
        {
            camSet = false;
            Vector3 up = Vector3.Slerp(Vector3.up, heli.up, roll);
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, up));
            cam.fieldOfView = fov;
        }

        // ------------------------------------------------------------------ the timeline

        int shot = -1;
        // the fly-by: where the camera waits, and where it is looking (eased after the helicopter)
        Vector3 flyBy, flyLook, flyLookVel; bool flyBySet;

        void Update()
        {
            if (finished || heli == null) return;
            clock += Time.deltaTime;
            var cfg = Config;

            if (clock > 0.5f && SkipPressed()) { Debug.Log($"[Intro] skipped at {clock:0.0} s by {SkipCause()}; {Wobble()}"); Finish(); return; }

            Place(clock);
            if (mainRotor != null) mainRotor.localRotation *= Quaternion.AngleAxis(cfg.mainRotorSpeed * Time.deltaTime, Vector3.up);
            if (tailRotor != null) tailRotor.localRotation *= Quaternion.AngleAxis(cfg.tailRotorSpeed * Time.deltaTime, Vector3.right);
            Seat();
            if (squad) HideTeammates();

            int now = clock < cfg.shotCabin ? 0 : clock < cfg.shotOverShoulder ? 1 : clock < cfg.shotLanding ? 2 : 3;
            if (now != shot) { shot = now; camSet = false; }
            Vector3 hp = heli.position, hf = heli.forward;
            float side = Mathf.Sign(playerSeat.x);
            Vector3 outward = new Vector3(side, 0f, 0f);
            // what the cabin shots look at: the player, or the two on the door side in a squad
            Vector3 focus = playerSeat + new Vector3(0f, 0.5f, squad && sitters.Count > 1 ? -0.8f : -0.3f);
            switch (now)
            {
                case 0:   // a fly-by: the camera waits high over the streets just inside the zone; the helicopter comes at it out of the haze, passes close by and flies on toward the base
                {
                    if (!flyBySet) { flyBySet = true; flyLook = hp; flyLookVel = Vector3.zero; }
                    flyLook = Vector3.SmoothDamp(flyLook, hp + Vector3.up * 0.6f, ref flyLookVel, 0.35f);
                    camSet = false;
                    cam.transform.SetPositionAndRotation(flyBy, Quaternion.LookRotation(flyLook - flyBy, Vector3.up));
                    cam.fieldOfView = 46f;
                    break;
                }
                case 1:   // alongside, outside the open door and a little toward the nose: the player on their seat, the city sliding past below
                {
                    if (!seated) { Chase(hp, hf); break; }
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(cfg.shotCabin, cfg.shotOverShoulder, clock));   // a slow push in
                    Vector3 at = heli.TransformPoint(focus + outward * Mathf.Lerp(4.4f, 3.6f, k) + new Vector3(0f, 0.15f, 1.4f));
                    Ride(at, heli.TransformPoint(focus), 44f, 0.5f);
                    break;
                }
                case 2:   // close, outside by the cockpit door: the faces of those on the door side, the cabin behind them
                {
                    if (!seated) { Chase(hp, hf); break; }
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(cfg.shotOverShoulder, cfg.shotLanding, clock));
                    Vector3 at = heli.TransformPoint(new Vector3(side * Mathf.Lerp(3.0f, 2.7f, k), 1.7f, 3.3f));   // clear of the cockpit door
                    Ride(at, heli.TransformPoint(focus + new Vector3(0f, 0.12f, 0f)), 50f, 0.6f);
                    break;
                }
                case 3:   // above the deck, in front and on the door side: it comes in nose-on with them in the cabin, flares, hovers and settles
                {
                    Vector3 sideDir = Vector3.Cross(Vector3.up, padFwd) * doorSign;
                    Vector3 at = pad + padFwd * 11f + sideDir * 9f + Vector3.up * 7f;
                    at -= Flat(at - pad).normalized * Mathf.InverseLerp(cfg.shotLanding, cfg.fadeOut, clock) * 3f;   // slow push in
                    Shoot(Clear(pad + Vector3.up * 1.5f, InsideZone(at, 3f)), hp + Vector3.up * 0.8f, 50f, 2.5f);
                    break;
                }
            }

            UpdateOverlay();
            if (clock >= cfg.end) { Debug.Log($"[Intro] ended at {clock:0.0} s; {Wobble()}"); Finish(); }
        }

        /// Where the fly-by camera waits: beside the flight path a little past halfway through the shot, on the side
        /// toward the zone's middle (the other side is in the mist, and pulling the spot back in from there would put
        /// it on the path), a little above the helicopter as it passes. It never moves; only its look eases after the
        /// helicopter, so the picture is steady.
        void PlanFlyBy()
        {
            float pass = Config.shotCabin * 0.55f;
            Vector3 p = HeliAt(pass), ahead = HeliAt(pass + 0.5f);
            Vector3 dir = Flat(ahead - p).sqrMagnitude > 0.01f ? Flat(ahead - p).normalized : padFwd;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            var zm = ZoneManager.Instance;
            Vector3 middle = zm != null ? zm.center : pad;
            if (Vector3.Dot(Flat(middle - p), side) < 0f) side = -side;
            flyBy = InsideZone(p + side * Config.flyByDistance + dir * 4f, 4f);
            flyBy.y = p.y + 3f;
            // the player sits on the side that passes the camera: seen from the first shot, not hidden behind the cabin
            doorSign = Mathf.Sign(Vector3.Dot(side, Vector3.Cross(Vector3.up, dir)));
        }

        /// Without the seated clip there is no one in the door: the cabin shots chase the helicopter from behind and
        /// above as it banks round, the base ahead and below.
        void Chase(Vector3 hp, Vector3 hf)
        {
            Vector3 f = Flat(hf).sqrMagnitude > 0.01f ? Flat(hf).normalized : padFwd, side = Vector3.Cross(Vector3.up, f);
            Vector3 at = InsideZone(hp - f * 16f + Vector3.up * 6f + side * 4f * -turnSign, 3f);
            Shoot(Clear(hp, at), Vector3.Lerp(hp + f * 8f, pad, 0.35f), 48f, 2.5f);
        }

        /// Pulls a camera spot back inside the fight zone, `margin` metres in from its wall (out of the mist).
        Vector3 InsideZone(Vector3 p, float margin)
        {
            var zm = ZoneManager.Instance;
            if (zm == null) return p;
            Vector3 d = Flat(p - zm.center);
            float max = Mathf.Max(2f, zm.CurrentRadius - margin);
            if (d.magnitude > max) p = new Vector3(zm.center.x, p.y, zm.center.z) + d.normalized * max;
            return p;
        }

        static readonly RaycastHit[] clearHits = new RaycastHit[16];

        /// The camera spot moved in toward `target` until nothing solid stands between them (no wall or rotor
        /// filling the frame). The player and the helicopter (no colliders) don't count.
        static Vector3 Clear(Vector3 target, Vector3 desired)
        {
            Vector3 d = desired - target;
            float dist = d.magnitude;
            if (dist < 0.5f) return desired;
            d /= dist;
            int n = Physics.SphereCastNonAlloc(target, 0.35f, d, clearHits, dist, ~0, QueryTriggerInteraction.Ignore);
            float best = dist;
            for (int i = 0; i < n; i++)
            {
                var h = clearHits[i];
                if (h.distance <= 0f || h.collider is CharacterController || h.collider.GetComponentInParent<PlayerHealth>() != null) continue;
                best = Mathf.Min(best, h.distance);
            }
            return target + d * Mathf.Max(1.2f, best - 0.3f);
        }

        /// Seats everyone who rides, for the whole flight: the player on the front seat of their door side; in a squad
        /// match the other seats (up to squadSeats) get stand-in operatives (the teammate prefab), and the real teammate
        /// copies are hidden until the end. Each plays the seated clip over their own Animator (its controller is left
        /// alone and takes over again when the graph goes), gun on the animated right hand, CharacterController off
        /// (the helicopter carries them). Without the clip the player waits where play starts.
        void SitDown()
        {
            var cfg = Config;
            if (moves == null || moves.sit == null || cfg.seats == null || cfg.seats.Length == 0)
            {
                Debug.LogWarning("[Intro] no seated clip (IntroMoves.sit): run Zombie Rush/Player/Intro Move Clips. The cabin shots chase the helicopter instead.");
                Teleport(endSpot, endFace);
                return;
            }
            // the door side first, front seat before back, then the far side
            var order = new List<Vector3>(cfg.seats);
            order.Sort((a, b) => SeatRank(a).CompareTo(SeatRank(b)));
            playerSeat = order[0];
            seated = Sit(player, playerSeat, 0f, false);
            if (!seated) { Teleport(endSpot, endFace); return; }
            if (!squad) return;
            HideTeammates();
            var prefab = ZombieRush.Net.NetConfig.Current != null ? ZombieRush.Net.NetConfig.Current.remotePlayerPrefab : null;
            if (prefab == null) { Debug.LogWarning("[Intro] no teammate prefab (NetConfig.remotePlayerPrefab): the squad seats stay empty."); return; }
            for (int i = 1; i < Mathf.Min(cfg.squadSeats, order.Count); i++)
            {
                var mate = Instantiate(prefab, heli.TransformPoint(order[i]), heli.rotation);
                mate.name = $"IntroSquadmate {i}";
                Sit(mate.transform, order[i], i * 1.37f, true);   // each a little further into the clip: they don't breathe in step
            }
        }

        float SeatRank(Vector3 seat) => (Mathf.Sign(seat.x) == doorSign ? 0f : 100f) - seat.z;

        bool Sit(Transform who, Vector3 seat, float clipStart, bool standIn)
        {
            var bodyAnim = who.GetComponentInChildren<CharacterBodyAnimator>();
            var anim = bodyAnim != null ? bodyAnim.Animator : null;
            if (anim == null) { if (standIn) Destroy(who.gameObject); return false; }
            var s = new Sitter { who = who, body = who.GetComponent<CharacterController>(), gun = who.GetComponentInChildren<BodyGunPose>(), seat = seat, standIn = standIn };
            s.graph = PlayableGraph.Create("IntroSit");
            s.graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(s.graph, "IntroSit", anim);
            var sit = AnimationClipPlayable.Create(s.graph, moves.sit);
            sit.SetApplyFootIK(false);
            sit.SetTime(clipStart);
            output.SetSourcePlayable(sit);
            s.graph.Play();
            if (s.gun != null) s.gun.HandsFromBody = true;
            if (s.body != null) s.body.enabled = false;
            sitters.Add(s);
            PlaceSitter(s);
            return true;
        }

        /// Everyone on their troop seat, facing the cockpit, riding every tilt of the helicopter.
        void Seat()
        {
            if (heli == null) return;
            foreach (var s in sitters) PlaceSitter(s);
        }

        /// The clip sits on a seat `seatHeight` above the body's origin with its front edge `seatEdge` ahead of it.
        void PlaceSitter(Sitter s)
        {
            if (s.who == null || heli == null) return;
            Vector3 local = s.seat - Vector3.forward * moves.seatEdge - Vector3.up * moves.seatHeight;   // facing the cockpit
            s.who.SetPositionAndRotation(heli.TransformPoint(local), heli.rotation);
        }

        /// The real teammates' copies stand wherever their players are: out of the intro's shots until it ends.
        void HideTeammates()
        {
            foreach (var np in ZombieRush.Net.NetPlayer.All)
            {
                var copy = np != null ? np.Copy : null;
                if (copy != null && copy.activeSelf) { copy.SetActive(false); hiddenTeammates.Add(copy); }
            }
        }

        void StopMoves()
        {
            foreach (var s in sitters)
            {
                if (s.graph.IsValid()) s.graph.Destroy();
                if (s.gun != null) s.gun.HandsFromBody = false;
                if (s.standIn && s.who != null) Destroy(s.who.gameObject);
            }
            sitters.Clear();
            foreach (var c in hiddenTeammates) if (c != null) c.SetActive(true);
            hiddenTeammates.Clear();
            seated = false;
        }

        GameObject hiddenSign;

        /// The floating "SAFE ZONE" sign is a gameplay label: kept out of the intro's shots.
        void HideSafeZoneSign(bool hide)
        {
            if (hide)
            {
                var label = ZombieRush.UI.SafeZoneLabel.Instance;
                if (label != null && label.gameObject.activeSelf) { hiddenSign = label.gameObject; hiddenSign.SetActive(false); }
            }
            else if (hiddenSign != null) { hiddenSign.SetActive(true); hiddenSign = null; }
        }

        bool touched;

        void Teleport(Vector3 p, Vector3 face)
        {
            if (body != null) body.enabled = false;
            player.position = p + Vector3.up * 0.05f;
            if (face.sqrMagnitude > 0.01f) player.rotation = Quaternion.LookRotation(face.normalized, Vector3.up);
            if (body != null) body.enabled = true;
        }

        Text[] styleTexts;
        readonly string[] words = new string[4];
        readonly float[] alpha = new float[4];

        void UpdateOverlay()
        {
            var cfg = Config;
            float black = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((clock - cfg.fadeOut) / Mathf.Max(0.01f, cfg.fadeOutTime)));
            float bars = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(clock / 1.2f));
            float h = 1080f * 0.115f * bars;
            barTop.sizeDelta = new Vector2(0f, h); barBottom.sizeDelta = new Vector2(0f, h);
            fader.color = new Color(0f, 0f, 0f, Mathf.Max(1f - Mathf.SmoothStep(0f, 1f, (clock - cfg.holdBlack) / 1.4f), black));
            if (rotorSound != null) rotorSound.volume = cfg.rotorVolume * GameSettings.SfxVolume * Mathf.Lerp(1f, cfg.rotorVolumeOnBlack, black);

            var texts = styleTexts ??= new[] { caption, title, orders, ordersSub };   // by IntroConfig.Style
            for (int i = 0; i < texts.Length; i++) { words[i] = null; alpha[i] = 0f; }
            foreach (var line in cfg.lines)
            {
                if (clock < line.at || clock > line.until) continue;
                int i = (int)line.style;
                words[i] = line.text;
                alpha[i] = Mathf.Clamp01((clock - line.at) / 0.6f) * Mathf.Clamp01((line.until - clock) / 0.6f);
            }
            for (int i = 0; i < texts.Length; i++) { texts[i].text = words[i] ?? ""; SetAlpha(texts[i], alpha[i]); }
            title.rectTransform.localScale = Vector3.one * (1f + 0.04f * (1f - alpha[(int)IntroConfig.Style.Title]));   // settles in as it fades up
            SetAlpha(skipHint, Mathf.Clamp01((clock - 1.5f) / 1f) * 0.75f * bars);

            // the landing thump
            if (!touched && clock >= cfg.heliTouchdown) { touched = true; CameraShake.AddAt(pad, 0.25f, 40f); }
        }

        static void SetAlpha(Text t, float a) { var c = t.color; c.a = a; t.color = c; }

        string Wobble() => $"flight wobble {(wobbleFrames > 0 ? wobbleSum / wobbleFrames : 0f):0.00} deg/s per frame over {wobbleFrames} frames";

        /// What skipped the intro (for the log): the keys typed, mouse buttons, touches.
        static string SkipCause()
        {
            var why = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(Input.inputString)) why.Append($"keys '{Input.inputString}' ");
            for (int b = 0; b < 3; b++) if (Input.GetMouseButtonDown(b)) why.Append($"mouse {b} ");
            if (Input.touchCount > 0) why.Append($"{Input.touchCount} touch ");
            foreach (KeyCode k in System.Enum.GetValues(typeof(KeyCode))) if (Input.GetKeyDown(k)) { why.Append(k).Append(' '); if (why.Length > 80) break; }
            return why.Length > 0 ? why.ToString().Trim() : "an unknown input";
        }

        static bool SkipPressed()
        {
            if (Input.anyKeyDown) return true;
            for (int i = 0; i < Input.touchCount; i++) if (Input.GetTouch(i).phase == TouchPhase.Began) return true;
            return false;
        }

        /// Ends the intro (at the end, or skipped): the player stands where play starts (at the weapon table), the
        /// view, cameras and screens are back as they were, and play starts, fading in from black.
        public void Finish()
        {
            if (finished) return;
            finished = true;
            StopMoves();
            if (player != null && endFace.sqrMagnitude > 0.0001f) Teleport(endSpot, endFace);
            else if (body != null) body.enabled = true;
            HideSafeZoneSign(false);
            if (view != null) view.SetView(viewBefore);
            if (mainCam != null) mainCam.enabled = true;
            if (mainListener != null) mainListener.enabled = true;
            foreach (var c in hiddenCanvases) if (c != null) c.enabled = true;
            hiddenCanvases.Clear();
            if (heli != null) Destroy(heli.gameObject);
            IntroFadeIn.Play(Config.fadeInTime);
            Playing = false;
            var cb = done; done = null;
            Destroy(gameObject);
            cb?.Invoke();
        }
    }

    /// Play fading in from black after the intro: a black screen over everything that clears, then removes itself.
    public class IntroFadeIn : MonoBehaviour
    {
        Image img;
        float seconds, t;

        public static void Play(float seconds)
        {
            if (seconds <= 0f) return;
            var go = new GameObject("IntroFadeIn", typeof(RectTransform));
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 600;
            var f = new GameObject("Fader", typeof(RectTransform));
            f.transform.SetParent(go.transform, false);
            var rt = (RectTransform)f.transform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var fade = go.AddComponent<IntroFadeIn>();
            fade.img = f.AddComponent<Image>();
            fade.img.color = Color.black;
            fade.img.raycastTarget = false;
            fade.seconds = seconds;
        }

        void Update()
        {
            t += Time.unscaledDeltaTime;
            img.color = new Color(0f, 0f, 0f, 1f - Mathf.SmoothStep(0f, 1f, t / seconds));
            if (t >= seconds) Destroy(gameObject);
        }
    }
}
