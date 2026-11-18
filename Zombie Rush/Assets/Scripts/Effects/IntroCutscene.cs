using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using ZombieRush.Core;
using ZombieRush.Player;
using ZombieRush.World;

namespace ZombieRush.Effects
{
    /// What the intro says and how it is paced. Edit the lines and times here.
    [Serializable]
    public class IntroConfig
    {
        [Serializable] public class Line { public float at, until; public string text; public bool title; }

        public Line[] lines =
        {
            new Line { at = 0.8f, until = 3.6f, text = "ZOMBIE RUSH", title = true },
            new Line { at = 3.9f, until = 7.0f, text = "DAY 47 OF THE OUTBREAK. THE CITY FELL IN ONE NIGHT." },
            new Line { at = 7.4f, until = 12.6f, text = "ONE OUTPOST STILL STANDS: HAVEN." },
            new Line { at = 13.2f, until = 19.4f, text = "YOUR ORDERS: HOLD THE ZONE. PUSH THE DEAD BACK." },
            new Line { at = 19.9f, until = 24.2f, text = "TAKE THE CITY BACK, STREET BY STREET." },
            new Line { at = 24.6f, until = 28.6f, text = "GEAR UP, SURVIVOR." },
        };

        [Tooltip("When each shot starts, seconds: the base from above as it flies in, chasing it round the base, the landing, stepping out, at the weapons.")]
        public float shotAlongside = 7.2f, shotLanding = 12.4f, shotStepOut = 20f, shotWeapons = 24.5f, end = 29f;
        [Tooltip("Helicopter timeline, seconds: arrives in a hover over the pad, skids down.")]
        public float heliHover = 16.6f, heliTouchdown = 19.4f;
        [Tooltip("Seconds it spends slowing from cruise speed into the hover (nose up, the flare).")]
        public float slowDown = 6.5f;
        [Tooltip("Heights above the pad, metres: where it comes in, and the hover before it settles.")]
        public float flightAltitude = 26f, hoverHeight = 4f;
        [Tooltip("How far outside the zone wall it starts (hidden in the mist), metres.")]
        public float startBeyondZone = 35f;
        [Tooltip("Length of the straight final approach into the pad, metres.")]
        public float finalApproach = 30f;
        [Tooltip("How far round the base the entry point sits from the final approach line, degrees: it banks round to line up.")]
        public float entryAngle = 115f;
        [Tooltip("Rotor blur: how solid the spinning blades are, and how dark the blur disc is.")]
        [Range(0f, 1f)] public float bladeAlpha = 0.45f, discAlpha = 0.28f;
        [Tooltip("Main and tail rotor speed, degrees per second (lower than real, so it reads on screen).")]
        public float mainRotorSpeed = 760f, tailRotorSpeed = 1500f;
        [Tooltip("Which side the cabin door opens on: +1 right of the nose, -1 left.")]
        public float doorSide = 1f;
        public float walkSpeed = 1.7f;
        [Range(0f, 1f)] public float rotorVolume = 0.85f;
    }

    /// The match intro, played when a match starts (every match, skippable): a helicopter comes in over the
    /// mist wall from outside the city, crosses the zone and lands on the fortress helipad (FortressHaven/Helipad,
    /// or the yard in front of the base when there is none), the player steps out, walks off, and in a last shot
    /// walks up to the weapon table. Letterbox bars and story lines carry the "why". Built in code from
    /// Resources/ZombieRush/Cutscene (the helicopter, prepared by Blender/heli_prepare.py); nothing in the scene
    /// is changed and everything it makes is removed at the end. Runs while the game is in its Lobby state, so the
    /// HUD is hidden and nothing else moves; GameManager starts play when it ends.
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

        // every Play session starts with the intro on (a test may have switched it off in the last one)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Enabled = true; Playing = false; Current = null; }

        Action done;
        float clock;
        bool finished;

        // the set: where it lands, which way it faces, the walk to the weapons
        Vector3 pad, padFwd, walkFrom, walkTo, tableFrom, tableTo, tableSpot;
        bool hasTable;
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
        Text title, caption, skipHint;
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

            FindLanding();
            FindWeapons();
            PlanFlight();
            SpawnHelicopter();
            MakeCamera();
            MakeOverlay();
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
                var stairs = helipad != null ? FindNamed("Helipad_Stairs") : null;
                walkFrom = pad + Quaternion.AngleAxis(90f * Config.doorSide, Vector3.up) * padFwd * 2.6f;
                walkTo = stairs != null ? stairs.position : walkFrom + (Flat(baseCentre - pad).sqrMagnitude > 1f ? Flat(baseCentre - pad).normalized : padFwd) * 5f;
                return;
            }

            // no helipad: the yard in front of the base door, on open ground big enough for the rotor
            Vector3 door = sb != null && sb.playerStart != null ? Flat(sb.playerStart.forward).normalized : Vector3.forward;
            if (door.sqrMagnitude < 0.01f) door = Vector3.forward;
            float r = sb != null ? sb.radius : 10f;
            pad = Ground(baseCentre + door * (r + 12f));
            for (int i = 0; i < 16; i++)
            {
                Vector3 probe = Ground(baseCentre + Quaternion.AngleAxis((i % 2 == 0 ? 1 : -1) * (i / 2) * 15f, Vector3.up) * door * (r + 12f + (i / 4) * 4f));
                if (!Physics.CheckBox(probe + Vector3.up * 3.2f, new Vector3(7f, 2.6f, 7f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) { pad = probe; door = Flat(probe - baseCentre).normalized; break; }
            }
            padFwd = Quaternion.AngleAxis(-90f * Config.doorSide, Vector3.up) * -door;   // side-on to the base: the door faces it
            walkFrom = pad + -door * 2.6f;
            walkTo = walkFrom + -door * 6f;
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
            hasTable = best != null;
            if (!hasTable) return;
            tableSpot = best.transform.position;
            Vector3 approach = Flat(tableSpot - from);
            approach = approach.sqrMagnitude > 0.25f ? approach.normalized : Flat(tableSpot - player.position).normalized;
            tableTo = Ground(tableSpot - approach * 1.6f, 2f);                  // stops just short of the table (not picking up)
            tableFrom = Ground(tableTo - approach * 4.5f, 2f);
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

            // the entry: out in the mist, off to one side, heading in toward the middle of the zone
            Vector3 s = c + Quaternion.AngleAxis(cfg.entryAngle, Vector3.up) * -fwd * (rad + cfg.startBeyondZone);
            float k = Vector3.Distance(s, f);
            Vector3 b1 = s + Flat(c - s).normalized * k * 0.4f, b2 = f - fwd * Mathf.Min(k * 0.45f, 16f);
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
            return Vector3.Lerp(path[lo], path[hi], Mathf.InverseLerp(pathLen[lo], pathLen[hi], d));
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
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null)
            {
                var mat = new Material(shader) { name = "IntroHelicopter" };
                if (tex != null) { mat.SetTexture("_BaseMap", tex); mat.mainTexture = tex; }
                mat.SetFloat("_Smoothness", 0.28f);
                mat.SetFloat("_Metallic", 0.25f);
                foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;
                made.Add(mat);
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

            foreach (var (rotor, normal) in new[] { (mainRotor, Vector3.up), (tailRotor, Vector3.right) })
            {
                if (rotor == null) continue;
                foreach (var r in rotor.GetComponentsInChildren<Renderer>()) r.sharedMaterial = blades;
                var mf = rotor.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                // the sweep radius: the farthest blade tip from the hub (the rotor's pivot), across the spin plane
                Bounds b = mf.sharedMesh.bounds;
                Vector3 far = Vector3.Max(Abs(b.min), Abs(b.max));
                float radius = normal == Vector3.up ? Mathf.Max(far.x, far.z) : Mathf.Max(far.y, far.z);
                var q = new GameObject("RotorBlur");
                q.transform.SetParent(rotor, false);
                q.transform.localRotation = Quaternion.FromToRotation(Vector3.back, normal);   // the quad faces -Z
                q.transform.localScale = Vector3.one * radius * 2f;
                q.AddComponent<MeshFilter>().sharedMesh = FxMaterials.Primitive(PrimitiveType.Quad);
                var mr = q.AddComponent<MeshRenderer>();
                mr.sharedMaterial = disc;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
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

        float heliYaw, heliPitch, heliRoll; bool heliPlaced;

        /// Puts the helicopter where the timeline says: heading along its path, nose down at speed, banking into
        /// turns, flaring nose-up as it slows over the pad, then sitting square on its skids.
        void Place(float t)
        {
            Vector3 p = HeliAt(t), ahead = HeliAt(t + 0.25f), behind = HeliAt(Mathf.Max(0f, t - 0.25f));
            Vector3 v = (ahead - behind) / 0.5f, a = (ahead - 2f * p + behind) / (0.25f * 0.25f);
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
            heliPitch = Mathf.Lerp(heliPitch, pitchTarget, 1f - Mathf.Exp(-2.5f * Time.deltaTime));
            heliRoll = Mathf.Lerp(heliRoll, rollTarget, 1f - Mathf.Exp(-2.5f * Time.deltaTime));
            if (!heliPlaced) { heliPitch = pitchTarget; heliRoll = rollTarget; }
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

            var banner = FindAnyObjectByType<ZombieRush.UI.WaveBannerUI>(FindObjectsInactive.Include);
            Font display = banner != null && banner.titleText != null ? banner.titleText.font : null;
            Font detail = banner != null && banner.detailText != null ? banner.detailText.font : display;
            if (display == null) display = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (detail == null) detail = display;

            title = MakeText("Title", display, 150, new Vector2(0.5f, 0.56f), new Color(0.93f, 0.9f, 0.84f), 0.18f);
            caption = MakeText("Caption", detail, 46, new Vector2(0.5f, 0.165f), new Color(0.93f, 0.9f, 0.84f), 0.12f);
            skipHint = MakeText("Skip", detail, 26, new Vector2(0.91f, 0.045f), new Color(0.7f, 0.7f, 0.7f), 0.1f);
            skipHint.text = GameSettings.UseTouchControls ? "TAP TO SKIP" : "PRESS ANY KEY TO SKIP";
            skipHint.alignment = TextAnchor.MiddleRight;

            var f = new GameObject("Fader", typeof(RectTransform));
            f.transform.SetParent(canvas.transform, false);
            var frt = (RectTransform)f.transform; frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = frt.offsetMax = Vector2.zero;
            fader = f.AddComponent<Image>(); fader.color = Color.black; fader.raycastTarget = false;
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

        // ------------------------------------------------------------------ the timeline

        int shot = -1;

        void Update()
        {
            if (finished || heli == null) return;
            clock += Time.deltaTime;
            var cfg = Config;

            if (clock > 0.5f && SkipPressed()) { Finish(); return; }

            Place(clock);
            if (mainRotor != null) mainRotor.localRotation *= Quaternion.AngleAxis(cfg.mainRotorSpeed * Time.deltaTime, Vector3.up);
            if (tailRotor != null) tailRotor.localRotation *= Quaternion.AngleAxis(cfg.tailRotorSpeed * Time.deltaTime, Vector3.right);

            int now = clock < cfg.shotAlongside ? 0 : clock < cfg.shotLanding ? 1 : clock < cfg.shotStepOut ? 2 : clock < cfg.shotWeapons || !hasTable ? 3 : 4;
            if (now != shot) { shot = now; camSet = false; OnCut(now); }
            Vector3 hp = heli.position, hf = heli.forward;
            switch (now)
            {
                // Flying shots look IN toward the base and down, so streets and roofs fill the background:
                // looking up or out only ever shows the 140 m mist wall, and the helicopter seems to float in space.
                case 0:   // high just inside the zone edge, looking the way it will fly (down over the base); it comes out of the mist, passes beside the camera and flies on into the shot
                {
                    Vector3 ahead = PathPoint(entryDist + 40f);
                    Vector3 heading = Flat(ahead - entry).sqrMagnitude > 1f ? Flat(ahead - entry).normalized : padFwd;
                    Vector3 away = Vector3.Cross(Vector3.up, heading) * -turnSign;   // the side it doesn't turn to
                    Vector3 at = entry + heading * (6f + clock * 0.4f) + away * 9f;
                    at.y = pad.y + cfg.flightAltitude + 6f;
                    at = InsideZone(at, 4f);
                    Vector3 view = Vector3.Lerp(new Vector3(ahead.x, pad.y, ahead.z), pad, 0.4f);   // where it's heading, and the base
                    float past = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Vector3.Dot(Flat(hp - at), heading) + 2f) / 14f));   // 0 until it's in front
                    Shoot(at, Vector3.Lerp(view, hp, 0.6f * past), 52f, 2f);
                    break;
                }
                case 1:   // chasing it from behind and above as it banks round, the base ahead and below
                {
                    Vector3 f = Flat(hf).sqrMagnitude > 0.01f ? Flat(hf).normalized : padFwd, side = Vector3.Cross(Vector3.up, f);
                    Vector3 at = InsideZone(hp - f * 16f + Vector3.up * 6f + side * 4f * -turnSign, 3f);
                    Shoot(Clear(hp, at), Vector3.Lerp(hp + f * 8f, pad, 0.35f), 48f, 2.5f);
                    break;
                }
                case 2:   // above the deck, in front and to the door side: it comes in nose-on, flares, hovers and settles
                {
                    Vector3 side = Vector3.Cross(Vector3.up, padFwd) * Mathf.Sign(cfg.doorSide);
                    Vector3 at = pad + padFwd * 11f + side * 9f + Vector3.up * 7f;
                    at -= Flat(at - pad).normalized * Mathf.InverseLerp(cfg.shotLanding, cfg.shotStepOut, clock) * 3f;   // slow push in
                    Shoot(Clear(pad + Vector3.up * 1.5f, InsideZone(at, 3f)), hp + Vector3.up * 0.8f, 50f, 2.5f);
                    break;
                }
                case 3:   // the player steps out and walks toward the camera, the helicopter behind them
                {
                    float u = Mathf.InverseLerp(cfg.shotStepOut + 0.6f, (hasTable ? cfg.shotWeapons : cfg.end) - 0.3f, clock);
                    Walk(walkFrom, walkTo, u);
                    Vector3 fwd = Flat(walkTo - walkFrom).sqrMagnitude > 0.01f ? Flat(walkTo - walkFrom).normalized : padFwd;
                    Vector3 side = Vector3.Cross(Vector3.up, fwd);
                    if (Vector3.Dot(side, Flat(player.position - hp)) < 0f) side = -side;   // the camera on the side away from the helicopter
                    Vector3 head = player.position + Vector3.up * 1.3f;
                    Vector3 at = OutOfHelicopter(player.position + fwd * 4.4f + side * 2.2f + Vector3.up * 1.45f, 3.2f);
                    Shoot(Clear(head, at), head - fwd * 1.5f, 48f, 4f);
                    break;
                }
                case 4:   // inside, by the weapons: they walk up to the table
                {
                    float u = Mathf.InverseLerp(cfg.shotWeapons + 0.3f, cfg.end - 1.4f, clock);
                    Walk(tableFrom, tableTo, u);
                    Vector3 fwd = Flat(tableTo - tableFrom).normalized, side = Vector3.Cross(Vector3.up, fwd);
                    Vector3 at = tableTo + fwd * 0.6f + side * 2.4f + Vector3.up * 1.55f;
                    Shoot(at, player.position + Vector3.up * 1.3f, 48f, 3f);
                    break;
                }
            }

            UpdateOverlay();
            if (clock >= cfg.end) Finish();
        }

        Vector3 ZoneCentre => ZoneManager.Instance != null ? ZoneManager.Instance.center : pad;

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

        /// Pushes a camera spot out of the parked helicopter's body (it has no colliders, so Clear can't see it):
        /// the fuselage taken as a line from nose to tail, `clearance` metres round it.
        Vector3 OutOfHelicopter(Vector3 p, float clearance)
        {
            if (heli == null || !heli.gameObject.activeInHierarchy) return p;
            Vector3 c = Flat(heli.position), axis = Flat(heli.forward).normalized;
            Vector3 q = Flat(p);
            Vector3 onAxis = c + axis * Mathf.Clamp(Vector3.Dot(q - c, axis), -6.5f, 3f);   // the tail boom is long, the nose short
            Vector3 d = q - onAxis;
            if (d.magnitude >= clearance) return p;
            Vector3 push = d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.Cross(Vector3.up, axis);
            Vector3 r = onAxis + push * clearance;
            return new Vector3(r.x, p.y, r.z);
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

        void OnCut(int s)
        {
            if (s == 0) HideSafeZoneSign(true);
            if (s == 3) Teleport(walkFrom, Flat(walkTo - walkFrom));
            if (s == 4) { Teleport(tableFrom, Flat(tableTo - tableFrom)); if (heli != null) heli.gameObject.SetActive(false); }
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

        /// Walks the player along a -> b by u (0..1), through its own CharacterController so the body plays its walk.
        void Walk(Vector3 a, Vector3 b, float u)
        {
            if (body == null || !body.enabled) return;
            Vector3 target = Vector3.Lerp(a, b, Mathf.SmoothStep(0f, 1f, u));
            Vector3 step = Flat(target - player.position);
            float max = Config.walkSpeed * 1.4f * Time.deltaTime;
            if (step.magnitude > max) step = step.normalized * max;
            body.Move(step + Vector3.down * 2f * Time.deltaTime);
            Vector3 face = Flat(b - a);
            if (face.sqrMagnitude > 0.01f)
                player.rotation = Quaternion.Slerp(player.rotation, Quaternion.LookRotation(face.normalized, Vector3.up), 1f - Mathf.Exp(-6f * Time.deltaTime));
        }

        void UpdateOverlay()
        {
            var cfg = Config;
            float bars = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(clock / 1.2f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((cfg.end - clock) / 0.8f));
            float h = 1080f * 0.115f * bars;
            barTop.sizeDelta = new Vector2(0f, h); barBottom.sizeDelta = new Vector2(0f, h);
            fader.color = new Color(0f, 0f, 0f, Mathf.Max(1f - Mathf.Clamp01(clock / 1.4f), Mathf.Clamp01((clock - (cfg.end - 0.6f)) / 0.6f)));

            string titleText = "", captionText = ""; float titleA = 0f, captionA = 0f;
            foreach (var line in cfg.lines)
            {
                if (clock < line.at || clock > line.until) continue;
                float a = Mathf.Clamp01((clock - line.at) / 0.6f) * Mathf.Clamp01((line.until - clock) / 0.6f);
                if (line.title) { titleText = line.text; titleA = a; } else { captionText = line.text; captionA = a; }
            }
            title.text = titleText; caption.text = captionText;
            SetAlpha(title, titleA); SetAlpha(caption, captionA);
            title.rectTransform.localScale = Vector3.one * (1f + 0.04f * (1f - titleA));   // settles in as it fades up
            SetAlpha(skipHint, Mathf.Clamp01((clock - 1.5f) / 1f) * 0.75f * bars);

            // the landing thump
            if (!touched && clock >= cfg.heliTouchdown) { touched = true; CameraShake.AddAt(pad, 0.25f, 40f); }
        }

        static void SetAlpha(Text t, float a) { var c = t.color; c.a = a; t.color = c; }

        static bool SkipPressed()
        {
            if (Input.anyKeyDown) return true;
            for (int i = 0; i < Input.touchCount; i++) if (Input.GetTouch(i).phase == TouchPhase.Began) return true;
            return false;
        }

        /// Ends the intro (at the end, or skipped): the player stands at the weapon table (or by the helicopter),
        /// the view, cameras and screens are back as they were, and play starts.
        public void Finish()
        {
            if (finished) return;
            finished = true;
            if (player != null)
            {
                if (hasTable) Teleport(tableTo, Flat(tableSpot - tableTo));
                else if (walkTo != Vector3.zero) Teleport(walkTo, Flat(walkTo - walkFrom));
            }
            HideSafeZoneSign(false);
            if (view != null) view.SetView(viewBefore);
            if (mainCam != null) mainCam.enabled = true;
            if (mainListener != null) mainListener.enabled = true;
            foreach (var c in hiddenCanvases) if (c != null) c.enabled = true;
            hiddenCanvases.Clear();
            if (heli != null) Destroy(heli.gameObject);
            Playing = false;
            var cb = done; done = null;
            Destroy(gameObject);
            cb?.Invoke();
        }
    }
}
