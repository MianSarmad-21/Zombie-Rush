using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ZombieRush.Player;

namespace ZombieRush.EditorTools
{
    /// Zombie Rush > Player > Capture Third Person (Play): enters Play mode, switches the player to
    /// third person and saves screenshots to Docs/Look/Play_*.png: over the shoulder looking level,
    /// up and down, plus close side and front shots of the body holding each gun. Then leaves Play
    /// mode. Started from outside with the Temp/ZR_RunPlayCapture marker (report: Temp/ZR_PlayCapture.log).
    [InitializeOnLoad]
    static class PlayCaptureTools
    {
        const string Key = "ZR_PlayCaptureActive";
        const string ModeKey = "ZR_PlayCaptureMode";
        const string Folder = "Docs/Look";
        static bool running;
        static double startTime;
        static int step;
        static readonly StringBuilder log = new StringBuilder();

        static PlayCaptureTools()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (SessionState.GetBool(Key, false) && EditorApplication.isPlaying) Begin();
        }

        [MenuItem("Zombie Rush/Player/Capture Third Person (Play)")]
        public static void Run() => Run("");

        /// mode "" = standing shots per gun; "firemove" = moving with and without firing.
        public static void Run(string mode)
        {
            SessionState.SetString(ModeKey, mode ?? "");
            SessionState.SetBool(Key, true);
            if (EditorApplication.isPlaying) Begin();
            else EditorApplication.EnterPlaymode();
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (s == PlayModeStateChange.EnteredPlayMode) Begin();
            else if (s == PlayModeStateChange.EnteredEditMode) SessionState.EraseBool(Key);
        }

        static void Begin()
        {
            if (running) return;
            running = true;
            step = 0;
            nextTime = 0;
            haveStart = false;
            relayTask = null; relayLog.Clear();
            Application.runInBackground = true; // the game pauses itself when the editor loses focus
            plan = null;
            log.Clear();
            startTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }

        static void Finish(string line)
        {
            log.AppendLine(line);
            EditorApplication.update -= Tick;
            running = false;
            File.WriteAllText("Temp/ZR_PlayCapture.log", log.ToString());
            SessionState.EraseBool(Key);
            EditorApplication.ExitPlaymode();
        }

        static List<(float wait, System.Action act)> plan;
        static double nextTime;
        static double extraWait;   // an action can ask for more time before the next one
        static System.Threading.Tasks.Task<string> relayTask;
        static readonly StringBuilder relayLog = new StringBuilder();

        // Runs the plan one action at a time; each action waits its own time (real seconds) after it.
        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            double t = EditorApplication.timeSinceStartup - startTime;
            if (t < 3.0 || t < nextTime) return;

            try
            {
                var switcher = PlayerViewSwitcher.Local;
                if (switcher == null && SessionState.GetString(ModeKey, "") == "retry" && step > 0 && t < 200) { nextTime = t + 0.5; return; }   // the level is reloading
                if (switcher == null) { Finish("FAILED: no PlayerViewSwitcher in play."); return; }
                ZombieRush.Core.GameManager.Instance?.Resume(); // undo a focus-loss pause
                if (step == 0 && !ZombieRush.Net.NetSession.IsRunning && SessionState.GetString(ModeKey, "") == "uishots")
                {
                    // the lobby first, as the player sees it, before any match starts
                    Directory.CreateDirectory(Folder);
                    ScreenCapture.CaptureScreenshot($"{Folder}/UI_Lobby.png");
                    SessionState.SetString(ModeKey, "uishots2");
                    nextTime = t + 1.5; return;
                }
                if (step == 0 && !ZombieRush.Net.NetSession.IsRunning)
                {
                    // tests start a match themselves: "net..." modes host directly (a built client can join), the rest play solo
                    string m = SessionState.GetString(ModeKey, "");
                    if (m == "netrelay" && relayTask != null) { step = 1; nextTime = t + 1.0; return; }   // already asked
                    if (m == "netrelay")
                    {
                        // a real online squad through Unity Relay: the plan reports the code or the error
                        ZombieRush.Net.NetSession.Status += s => relayLog.AppendLine("  status: " + s);
                        relayTask = ZombieRush.Net.NetSession.HostSquadAsync();
                        ZombieRush.Core.GameManager.Instance?.BeginMatch();
                        var lob = Object.FindAnyObjectByType<ZombieRush.Net.NetLobbyUI>();
                        if (lob != null) lob.gameObject.SetActive(false);
                        nextTime = t + 1.0; return;
                    }
                    bool ok = m.StartsWith("net") ? ZombieRush.Net.NetSession.StartDirectHost() : ZombieRush.Net.NetSession.StartSolo();
                    if (!ok) { Finish("FAILED: could not start the match."); return; }
                    ZombieRush.Core.GameManager.Instance?.BeginMatch();
                    var lobby = Object.FindAnyObjectByType<ZombieRush.Net.NetLobbyUI>();
                    if (lobby != null) lobby.gameObject.SetActive(false);
                    nextTime = t + 1.0; return;
                }
                if (plan == null || step == 0) plan = BuildPlan(switcher, SessionState.GetString(ModeKey, ""));
                if (step >= plan.Count) { plan = null; Finish($"Done: {log}"); return; }
                plan[step].act();
                nextTime = t + plan[step].wait + extraWait;
                extraWait = 0;
                step++;
            }
            catch (System.Exception e) { plan = null; Finish("FAILED: " + e); }
        }

        static Vector3 startSpawn; static float startSpawnYaw;

        static List<(float, System.Action)> BuildPlan(PlayerViewSwitcher switcher, string mode)
        {
            var player = switcher.GetComponent<PlayerController>();
            var weapons = switcher.GetComponent<PlayerWeaponController>();
            var cam = switcher.playerCamera;
            var p = new List<(float, System.Action)>
            {
                (0.7f, () => { startSpawn = switcher.transform.position; startSpawnYaw = switcher.transform.eulerAngles.y; MoveToOpenSpot(switcher.transform); }),
                (0.7f, () => switcher.SetView(CharacterView.ThirdPerson)),
                (0.3f, () => SetPitch(player, 0f)),
            };

            if (mode == "retry")
            {
                // user: "when I click retry after dying the zombies don't respawn". Fight a little, die, press the
                // game over RESTART button (GameManager.RestartLevel), then watch the reloaded level: does the solo
                // match start again, does the wave loop run, do zombies come?
                p.Clear();
                System.Action<string> probe = tag =>
                {
                    var wm = Object.FindAnyObjectByType<ZombieRush.Waves.WaveManager>();
                    var gm2 = ZombieRush.Core.GameManager.Instance;
                    var nm = Unity.Netcode.NetworkManager.Singleton;
                    log.AppendLine($"  {tag} t={Time.time:0.0}: running {ZombieRush.Net.NetSession.IsRunning} server {ZombieRush.Net.NetSession.IsServer} mode {ZombieRush.Net.NetSession.Mode} " +
                        $"nm {(nm == null ? "none" : $"{nm.GetEntityId()} listening {nm.IsListening} shutting {nm.ShutdownInProgress}")} state {(gm2 != null ? gm2.State.ToString() : "-")} " +
                        $"match {(ZombieRush.Net.MatchNet.Instance != null)} battle {(ZombieRush.World.SafeBase.Instance != null && ZombieRush.World.SafeBase.Instance.BattleStarted)} " +
                        $"wave {(wm != null ? wm.CurrentWave : -1)} alive {(wm != null ? wm.AliveCount : -1)} zombies {Object.FindObjectsByType<ZombieRush.Zombie.ZombieHealth>(FindObjectsSortMode.None).Length} " +
                        $"player {(PlayerViewSwitcher.Local != null ? PlayerViewSwitcher.Local.transform.position.ToString("F0") : "none")}");
                };
                System.Action stepOut = () => { var s = PlayerViewSwitcher.Local; if (s != null) MoveToOpenSpot(s.transform, true); };
                p.Add((1.0f, () => probe("start")));
                p.Add((1.0f, stepOut));
                for (int k = 0; k < 6; k++) p.Add((2.5f, () => probe("before death")));
                p.Add((2.0f, () => { var gm2 = ZombieRush.Core.GameManager.Instance; gm2.autoRestartDelay = 0f; var ph = PlayerViewSwitcher.Local.GetComponent<PlayerHealth>(); ph.TakeDamage(99999f); }));
                p.Add((0.5f, () => probe("dead")));
                p.Add((0.0f, () => { log.AppendLine("  -- RESTART pressed"); ZombieRush.Core.GameManager.Instance.RestartLevel(); }));
                for (int k = 0; k < 4; k++) p.Add((2.0f, () => probe("after reload, in base")));
                p.Add((1.0f, stepOut));
                for (int k = 0; k < 8; k++) p.Add((2.5f, () => probe("after reload, stepped out")));
                return p;
            }

            if (mode == "perf")
            {
                // 40 Wanderers from 6 to 60 m: frame time and triangles with distance detail on, then
                // with every zombie forced to full detail.
                var crowd = new List<GameObject>();
                p.Add((0.5f, () =>
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Zombie_Wanderer.prefab");
                    for (int i = 0; i < 40; i++)
                    {
                        float dist = Mathf.Lerp(6f, 60f, i / 39f);
                        float ang = i * 47f;
                        Vector3 pos = switcher.transform.position + Quaternion.Euler(0f, ang, 0f) * Vector3.forward * dist;
                        if (Physics.Raycast(pos + Vector3.up * 30f, Vector3.down, out var hit, 60f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point;
                        var z = Object.Instantiate(prefab, pos, Quaternion.LookRotation(switcher.transform.position - pos));
                        var rise = z.GetComponent<ZombieRush.Zombie.ZombieSpawnRise>();
                        if (rise != null) rise.enabled = false;
                        var ai = z.GetComponent<ZombieRush.Zombie.ZombieAI>();
                        if (ai != null) ai.enabled = false;
                        z.GetComponent<ZombieRush.Zombie.ZombieHealth>().Anim.SetSpeed(0.5f);
                        crowd.Add(z);
                    }
                    log.AppendLine($"  Spawned {crowd.Count} Wanderers, 6-60 m away.");
                }));
                p.Add((2.0f, () => StartPerf()));
                p.Add((3.0f, () => StopPerf("Distance detail ON ")));
                p.Add((0.1f, () => { foreach (var z in crowd) { var g = z.GetComponentInChildren<LODGroup>(); if (g != null) g.ForceLOD(0); } }));
                p.Add((1.0f, () => StartPerf()));
                p.Add((3.0f, () => StopPerf("Full detail forced")));
                p.Add((0.1f, () => { foreach (var z in crowd) Object.Destroy(z); }));
                return p;
            }

            if (mode == "fpp")
            {
                // First person: is each gun visible, and where is it?
                void State(string name)
                {
                    foreach (var w in weapons.weapons)
                    {
                        var vm = w.GetComponent<ZombieRush.Weapons.WeaponViewModel>();
                        var rs = w.GetComponentsInChildren<Renderer>(true);
                        int on = rs.Count(r => r.enabled && r.gameObject.activeInHierarchy);
                        Vector3 lp = vm != null && vm.model != null ? vm.model.localPosition : Vector3.zero;
                        Vector3 vp = cam.WorldToViewportPoint(w.transform.position);
                        log.AppendLine($"  {name} {w.name}: active {w.gameObject.activeInHierarchy}, renderers on {on}/{rs.Length}, model local {lp}, on screen at {vp}");
                    }
                    log.AppendLine($"  {name} camera local {cam.transform.localPosition}, view {(switcher.IsThirdPerson ? "TPP" : "FPP")}, near clip {cam.nearClipPlane}");
                }
                p.Add((1.0f, () => switcher.SetView(CharacterView.FirstPerson)));
                p.Add((0.2f, () => { Shot(cam, "FPP_Start"); State("start"); }));
                for (int i = weapons.weapons.Length - 1; i >= 0; i--)
                {
                    int idx = i;
                    p.Add((1.0f, () => { weapons.Unlock(weapons.weapons[idx].data); weapons.SelectWeapon(idx); }));
                    p.Add((0.2f, () => { Shot(cam, "FPP_" + weapons.weapons[idx].name); State(weapons.weapons[idx].name); }));
                }
                return p;
            }

            if (mode == "weapons")
            {
                // Each gun: hold, firing burst, a reload in six frames, then switching to the next gun.
                // No zombies, so nothing interrupts.
                p.Add((0.1f, () =>
                {
                    var wm = Object.FindAnyObjectByType<ZombieRush.Waves.WaveManager>(); if (wm != null) wm.enabled = false;
                    foreach (var z in Object.FindObjectsByType<ZombieRush.Zombie.ZombieHealth>(FindObjectsSortMode.None)) Object.Destroy(z.gameObject);
                    var ph0 = switcher.GetComponent<PlayerHealth>(); if (ph0 != null) ph0.enabled = false;
                }));
                int n = weapons.weapons.Length;
                for (int i = 0; i < n; i++)
                {
                    int idx = i;
                    string g = weapons.weapons[idx].name;
                    p.Add((0.9f, () => { weapons.Unlock(weapons.weapons[idx].data); weapons.SelectWeapon(idx); weapons.RefillAmmo(); }));
                    p.Add((0.2f, () => { Side(switcher.transform, $"Gun_{g}_Hold", 70f); var ar = switcher.GetComponentInChildren<WeaponAimRig>(); if (ar != null) log.AppendLine($"  {g} hold: {ar.Describe()}"); }));
                    p.Add((0.05f, () => ZombieRush.Core.GameInput.TouchPress(ZombieRush.Core.TouchAction.Fire)));
                    for (int k = 0; k < 3; k++) { int f = k; p.Add((0.08f, () => Side(switcher.transform, $"Gun_{g}_Fire{f}", 70f))); }
                    p.Add((0.3f, () => ZombieRush.Core.GameInput.TouchRelease(ZombieRush.Core.TouchAction.Fire)));
                    p.Add((0.05f, () => { var w = weapons.CurrentWeapon; w.TryReload(); log.AppendLine($"  {g}: reload {(w.IsReloading ? "started" : "NOT started")}, {w.data.reloadTime:0.0} s."); }));
                    float rt = Mathf.Max(0.6f, weapons.weapons[idx].data.reloadTime);
                    for (int k = 0; k < 6; k++)
                    {
                        int f = k;
                        p.Add((rt / 6f - 0.05f, () => { Side(switcher.transform, $"Gun_{g}_Reload{f}", 70f); log.AppendLine($"  {g} reload frame {f}: progress {weapons.CurrentWeapon.ReloadProgress:0.00}"); }));
                    }
                    p.Add((0.4f, () => { }));
                }
                // Switching: frames while the old gun goes down and the new one comes up.
                p.Add((0.1f, () => weapons.SelectWeapon(0)));
                for (int k = 0; k < 5; k++) { int f = k; p.Add((0.09f, () => Side(switcher.transform, $"Gun_Swap{f}", 70f))); }
                // First person: reload and draw as the player sees it.
                p.Add((0.6f, () => switcher.SetView(CharacterView.FirstPerson)));
                p.Add((0.05f, () => { weapons.SelectWeapon(n - 1); }));
                p.Add((0.15f, () => Shot(cam, "Gun_FPP_Draw")));
                p.Add((0.6f, () => { weapons.CurrentWeapon.TryUse(); }));
                p.Add((0.1f, () => weapons.CurrentWeapon.TryReload()));
                for (int k = 0; k < 4; k++) { int f = k; p.Add((0.45f, () => Shot(cam, $"Gun_FPP_Reload{f}"))); }
                return p;
            }

            if (mode == "safelabel")
            {
                // The floating SAFE ZONE sign: Game view shots looking at it from inside the base,
                // from just outside, and from far out in the city (world-space canvases do show in
                // camera renders, but the Game view also proves its size on the real screen).
                var sbase = ZombieRush.World.SafeBase.Instance;
                var pc = switcher.GetComponent<PlayerController>();
                var pcc = switcher.GetComponent<CharacterController>();
                var terrain = Terrain.activeTerrain;
                var pitchField = typeof(PlayerController).GetField("pitch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (sbase == null) { p.Add((0.1f, () => log.AppendLine("  FAILED: no safe base."))); return p; }
                var spots = new[] { ("Inside", sbase.radius - 4f, 200f), ("Edge", sbase.radius + 12f, 200f), ("Far", 90f, 120f), ("VeryFar", 200f, 300f) };
                foreach (var (tag, dist, ang) in spots)
                {
                    p.Add((0.6f, () =>
                    {
                        Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
                        Vector3 v = sbase.Center + dir * dist;
                        v.y = (terrain != null ? terrain.SampleHeight(v) + terrain.transform.position.y : v.y) + 0.05f;
                        if (pcc != null) pcc.enabled = false;
                        switcher.transform.position = v;
                        if (pcc != null) pcc.enabled = true;
                        var label = ZombieRush.UI.SafeZoneLabel.Instance;
                        Vector3 target = label != null ? label.transform.position : sbase.Center + Vector3.up * 20f;
                        Vector3 flat = target - v; flat.y = 0f;
                        switcher.transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
                        // Tilt the view up toward the sign (pitch is negative when looking up).
                        float up = Mathf.Atan2(target.y - (v.y + 1.7f), flat.magnitude) * Mathf.Rad2Deg;
                        if (pitchField != null && pc != null) { pitchField.SetValue(pc, -Mathf.Clamp(up * 0.8f, 0f, 60f)); pc.ResetPitchLimits(); }
                    }));
                    p.Add((0.5f, () =>
                    {
                        Directory.CreateDirectory(Folder);
                        ScreenCapture.CaptureScreenshot($"{Folder}/Play_SafeLabel_{tag}.png");
                        var label = ZombieRush.UI.SafeZoneLabel.Instance;
                        var g = label != null ? label.GetComponent<CanvasGroup>() : null;
                        float d = label != null ? Vector3.Distance(label.transform.position, cam.transform.position) : -1f;
                        log.AppendLine($"  {tag}: {dist:0} m from base centre; label {(label != null ? $"at {label.transform.position}, {d:0} m from camera, scale {label.transform.localScale.x:0.000} ({label.canvasHeight * label.transform.localScale.x:0.0} m tall), alpha {(g != null ? g.alpha : -1f):0.00}" : "MISSING")}");
                    }));
                }
                return p;
            }

            if (mode == "basefire")
            {
                // Can a player inside the safe base shoot zombies waiting outside it? In 8 directions:
                // a zombie just outside the edge, the player 3 m inside; log what the shot meets first
                // (same ray and mask as WeaponBase.HitScan), then fire the real gun and check the damage.
                var sbase = ZombieRush.World.SafeBase.Instance;
                var pwc = switcher.GetComponent<PlayerWeaponController>();
                if (sbase == null || pwc == null) { p.Add((0.1f, () => log.AppendLine("  FAILED: no safe base or no weapons."))); return p; }
                var pcc = switcher.GetComponent<CharacterController>();
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Zombie_Wanderer.prefab");
                var terrain = Terrain.activeTerrain;
                float Ground(Vector3 v) => terrain != null ? terrain.SampleHeight(v) + terrain.transform.position.y : v.y;
                void Put(Vector3 v)
                {
                    v.y = Ground(v) + 0.05f;
                    if (pcc != null) pcc.enabled = false;
                    switcher.transform.position = v;
                    if (pcc != null) pcc.enabled = true;
                }
                int blocked = 0, hits = 0;
                p.Add((0.5f, () => log.AppendLine($"  base centre {sbase.Center}, radius {sbase.radius:0.0}, view {(switcher.IsThirdPerson ? "third" : "first")} person, gun {pwc.CurrentWeapon?.name}")));
                for (int k = 0; k < 8; k++)
                {
                    float ang = k * 45f;
                    ZombieRush.Zombie.ZombieHealth z = null;
                    p.Add((0.4f, () =>
                    {
                        Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
                        Vector3 zpos = sbase.Center + dir * (sbase.radius + 1.8f);
                        zpos.y = Ground(zpos);
                        var go = Object.Instantiate(pf, zpos, Quaternion.LookRotation(-dir, Vector3.up));
                        var rz = go.GetComponent<ZombieRush.Zombie.ZombieSpawnRise>(); if (rz != null) rz.enabled = false;
                        var ai = go.GetComponent<ZombieRush.Zombie.ZombieAI>(); if (ai != null) ai.enabled = false;
                        z = go.GetComponent<ZombieRush.Zombie.ZombieHealth>();
                        Put(sbase.Center + dir * Mathf.Max(1f, sbase.radius - 3f));
                        switcher.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                    }));
                    p.Add((0.3f, () =>
                    {
                        var weapon = pwc.CurrentWeapon;
                        var cam2 = pwc.playerCamera != null ? pwc.playerCamera : cam;
                        Vector3 chest = z.transform.position + Vector3.up * 1.3f;
                        Vector3 aim = (chest - cam2.transform.position).normalized;
                        Vector3 origin = cam2.transform.position;
                        var vs = switcher;
                        origin = vs.AimOrigin(origin, aim);
                        aim = (chest - origin).normalized;
                        string first = "nothing";
                        bool clear = false;
                        if (Physics.Raycast(origin, aim, out var hit, 200f, weapon.hitMask, QueryTriggerInteraction.Ignore))
                        {
                            clear = hit.collider.GetComponentInParent<ZombieRush.Zombie.ZombieHealth>() == z;
                            first = $"{hit.collider.name} ({hit.collider.GetType().Name}, layer {LayerMask.LayerToName(hit.collider.gameObject.layer)}, parent {(hit.collider.transform.parent != null ? hit.collider.transform.parent.name : "-")}) at {hit.distance:0.0} m";
                        }
                        if (!clear) blocked++;
                        if (ang == 0f || ang == 180f) { Directory.CreateDirectory(Folder); ScreenCapture.CaptureScreenshot($"{Folder}/Play_BaseFire_{ang:000}.png"); }
                        float before = z.CurrentHealth;
                        cam2.transform.rotation = Quaternion.LookRotation(chest - cam2.transform.position, Vector3.up);
                        bool fired = weapon.TryUse();
                        float after = z.CurrentHealth;
                        if (after < before) hits++;
                        log.AppendLine($"  dir {ang:000}: player in base {(sbase.Contains(switcher.transform.position) ? "yes" : "NO")}, first hit {first} -> {(clear ? "ZOMBIE" : "BLOCKED")}; real shot fired {fired}, zombie hp {before:0} -> {after:0}");
                        Object.Destroy(z.gameObject);
                    }));
                }
                p.Add((0.2f, () => log.AppendLine($"  summary: {8 - blocked}/8 clear lines, {hits}/8 real shots hurt the zombie")));
                return p;
            }

            if (mode == "chests")
            {
                // Loot chests + zone fog: the fog from inside the zone; chests spawned for wave 1; one opened for real
                // (hold E through the touch path), shots of every stage; the loot picked up; the zone fund paid into.
                var sbase = ZombieRush.World.SafeBase.Instance;
                var zm = ZombieRush.World.ZoneManager.Instance;
                var ph = switcher.GetComponent<PlayerHealth>();
                var inv = switcher.GetComponent<PlayerInventory>();
                var pwc = switcher.GetComponent<PlayerWeaponController>();
                var pcc = switcher.GetComponent<CharacterController>();
                var pc = switcher.GetComponent<PlayerController>();
                var pitchField = typeof(PlayerController).GetField("pitch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var spawner = zm != null ? zm.GetComponent<ZombieRush.World.ChestSpawner>() : null;
                if (sbase == null || zm == null || spawner == null) { p.Add((0.1f, () => log.AppendLine("  FAILED: no safe base, zone or ChestSpawner (run chests)."))); return p; }
                void Put(Vector3 at, Vector3 look, float pitchDown)
                {
                    if (pcc != null) pcc.enabled = false;
                    switcher.transform.position = at + Vector3.up * 0.05f;
                    if (pcc != null) pcc.enabled = true;
                    var f = look - at; f.y = 0f;
                    if (f.sqrMagnitude > 0.001f) switcher.transform.rotation = Quaternion.LookRotation(f.normalized, Vector3.up);
                    if (pitchField != null && pc != null) { pitchField.SetValue(pc, pitchDown); pc.ResetPitchLimits(); }
                }
                void Snap(string tag) { Directory.CreateDirectory(Folder); ScreenCapture.CaptureScreenshot($"{Folder}/Chest_{tag}.png"); }
                ZombieRush.World.LootChest chest = null;
                p.Add((0.6f, () =>
                {
                    switcher.SetView(CharacterView.FirstPerson);
                    var wm = Object.FindAnyObjectByType<ZombieRush.Waves.WaveManager>(); if (wm != null) wm.enabled = false;
                    foreach (var z in Object.FindObjectsByType<ZombieRush.Zombie.ZombieHealth>(FindObjectsSortMode.None)) Object.Destroy(z.gameObject);
                    var fx = zm.boundaryVisual != null ? zm.boundaryVisual.GetComponent<ZombieRush.World.ZoneBoundaryFX>() : null;
                    log.AppendLine($"  zone radius {zm.CurrentRadius:0}, fog {(fx != null && fx.fogMaterial != null ? fx.fogMaterial.name : "OFF")}; price {zm.ExpandCost}, fund {zm.Fund}");
                }));
                // fog: from 8 m inside the zone wall looking out, and from the base door looking out
                Vector3 outDir = (sbase.Center - zm.center).sqrMagnitude > 1f ? (sbase.Center - zm.center).normalized : Vector3.forward;
                outDir = Quaternion.Euler(0f, 60f, 0f) * Vector3.forward;
                p.Add((0.8f, () => Put(zm.center + outDir * (zm.CurrentRadius - 8f), zm.center + outDir * (zm.CurrentRadius + 30f), -4f)));
                p.Add((0.4f, () => Snap("FogEdge")));
                p.Add((0.8f, () => Put(zm.center + outDir * (sbase.radius + 2f), zm.center + outDir * (zm.CurrentRadius + 30f), -6f)));
                p.Add((0.4f, () => Snap("FogFromBase")));
                // chests for wave 1
                p.Add((0.4f, () =>
                {
                    int n = spawner.Spawn(1, 3);
                    log.AppendLine($"  spawned {n} chests: " + string.Join(", ", ZombieRush.World.LootChest.All.Select(c => $"{c.name} at {c.transform.position:F0}")));
                    chest = ZombieRush.World.LootChest.All.FirstOrDefault();
                }));
                p.Add((0.5f, () => { if (chest != null) Put(chest.transform.position + chest.transform.forward * 4.5f + chest.transform.right * 1.5f, chest.transform.position, 18f); }));
                p.Add((0.5f, () => Snap("Closed")));
                p.Add((0.3f, () =>
                {
                    if (chest == null) return;
                    Put(chest.transform.position + chest.transform.forward * 1.7f, chest.transform.position, 30f);
                    ph.TakeDamage(40f);   // out of the safe base: hurt, so a medkit is wanted
                    inv.TakeReserveAmmo(pwc.CurrentWeapon.data, inv.GetReserveAmmo(pwc.CurrentWeapon.data) - 5);
                }));
                p.Add((0.3f, () => { log.AppendLine($"  prompt target {(ZombieRush.World.ChestPrompt.Target == chest ? "THIS chest" : "none")}"); Snap("Prompt"); ZombieRush.Core.GameInput.TouchPress(ZombieRush.Core.TouchAction.Interact); }));
                p.Add((0.45f, () => { log.AppendLine($"  holding: progress {chest?.Progress:0.00}"); Snap("Shaking"); }));
                // keep holding until the lock breaks, then snap the break, the lid and the loot in flight at set delays
                float brokeAt = -1f; int shots = 0;
                for (int s = 0; s < 40; s++)
                    p.Add((0.07f, () =>
                    {
                        if (chest == null || shots >= 3) return;
                        if (brokeAt < 0f && chest.Opened)
                        {
                            brokeAt = Time.time;
                            ZombieRush.Core.GameInput.TouchRelease(ZombieRush.Core.TouchAction.Interact);
                            log.AppendLine($"  lock broke after the hold");
                        }
                        if (brokeAt < 0f) return;
                        float since = Time.time - brokeAt;
                        if (shots == 0 && since >= 0.05f) { Snap("LockBreak"); shots++; }
                        else if (shots == 1 && since >= 0.45f) { Snap("LidOpening"); shots++; }
                        else if (shots == 2 && since >= 0.75f) { Snap("LootFlying"); shots++; }
                    }));
                p.Add((0.1f, () => { ZombieRush.Core.GameInput.TouchRelease(ZombieRush.Core.TouchAction.Interact); log.AppendLine($"  opened {chest?.Opened}, stage shots {shots}"); }));
                p.Add((0.9f, () =>
                {
                    Put(chest.transform.position + chest.transform.forward * 4f + chest.transform.right * 1.2f, chest.transform.position + chest.transform.forward * 1f, 22f);
                    log.AppendLine("  loot on the ground: " + string.Join(", ", Object.FindObjectsByType<ZombieRush.World.ChestReward>(FindObjectsSortMode.None).Select(r => r.kind + " " + r.amount)
                        .Concat(Object.FindObjectsByType<ZombieRush.World.AmmoPickup>(FindObjectsSortMode.None).Where(a => a.name.StartsWith("AmmoDrop")).Select(a => "Ammo " + a.weapon.weaponName + " " + a.amount))));
                }));
                p.Add((0.4f, () => Snap("Open")));
                // walk over the loot (health dropped first so a medkit is wanted)
                float hp = 0, ar = 0; int ammo = 0;
                p.Add((0.2f, () =>
                {
                    hp = ph.CurrentHealth; ar = ph.CurrentArmor; ammo = inv.GetReserveAmmo(pwc.CurrentWeapon.data);
                }));
                p.Add((0.1f, () => { }));
                foreach (int k in new[] { 0, 1, 2, 3 })
                {
                    p.Add((0.5f, () =>
                    {
                        var items = Object.FindObjectsByType<ZombieRush.World.ChestReward>(FindObjectsSortMode.None).Select(r => r.transform)
                            .Concat(Object.FindObjectsByType<ZombieRush.World.AmmoPickup>(FindObjectsSortMode.None).Where(a => a.name.StartsWith("AmmoDrop")).Select(a => a.transform)).ToList();
                        if (items.Count > 0) Put(items[0].position, items[0].position + chest.transform.forward, 30f);
                    }));
                }
                p.Add((0.5f, () => { Snap("Collected"); log.AppendLine($"  after walking over the loot: health {hp:0} -> {ph.CurrentHealth:0}, armour {ar:0} -> {ph.CurrentArmor:0}, reserve {ammo} -> {inv.GetReserveAmmo(pwc.CurrentWeapon.data)}"); }));
                // zone fund: give the player coins and press P twice (part, then the rest)
                p.Add((0.3f, () =>
                {
                    var wallet = PlayerCurrency.Instance;
                    int price = zm.ExpandCost;
                    wallet.AddCoins(price / 2);
                    float r0 = zm.CurrentRadius;
                    zm.ExpandZone();
                    log.AppendLine($"  zone fund: price {price}, paid {price / 2} -> fund {zm.Fund}, remaining {zm.Remaining}, radius {r0:0} -> {zm.CurrentRadius:0}");
                    Snap("FundToast");
                    wallet.AddCoins(price);
                    zm.ExpandZone();
                    log.AppendLine($"  paid the rest -> radius {zm.CurrentRadius:0}, fund {zm.Fund}, coins left {wallet.Coins}, next price {zm.ExpandCost}");
                }));
                return p;
            }

            if (mode == "baselook")
            {
                // The furnished base: the gun tables from the hall, a table close up, the lanterns outside at night,
                // a lantern flame close up; then real pick-ups: an AK from a table, rifle ammo from a box (reserve
                // lowered first), an armour vest, and a zombie-style ammo drop on the ground.
                var sbase = ZombieRush.World.SafeBase.Instance;
                var pwc = switcher.GetComponent<PlayerWeaponController>();
                var inv = switcher.GetComponent<PlayerInventory>();
                var ph = switcher.GetComponent<PlayerHealth>();
                var pcc = switcher.GetComponent<CharacterController>();
                var pc = switcher.GetComponent<PlayerController>();
                var pitchField = typeof(PlayerController).GetField("pitch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var furn = sbase != null ? sbase.transform.Find("BaseFurniture") : null;
                if (furn == null || pwc == null) { p.Add((0.1f, () => log.AppendLine("  FAILED: no BaseFurniture (run basefurnish) or no weapons."))); return p; }
                var tables = furn.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("Bench_")).OrderBy(t => t.name).ToArray();
                var lanterns = furn.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("Lantern_")).ToArray();
                var rifle = pwc.weapons.First(w => w.name == "Rifle").data;
                void Put(Vector3 at, Vector3 look, float pitchDown)
                {
                    if (pcc != null) pcc.enabled = false;
                    switcher.transform.position = at + Vector3.up * 0.05f;
                    if (pcc != null) pcc.enabled = true;
                    var f = look - at; f.y = 0f;
                    if (f.sqrMagnitude > 0.001f) switcher.transform.rotation = Quaternion.LookRotation(f.normalized, Vector3.up);
                    if (pitchField != null && pc != null) { pitchField.SetValue(pc, pitchDown); pc.ResetPitchLimits(); }
                }
                void Snap(string tag) { Directory.CreateDirectory(Folder); ScreenCapture.CaptureScreenshot($"{Folder}/Base_{tag}.png"); }
                Vector3 Front(Transform t, float d) => t.position + t.forward * d;   // tables face the hall (+Z)

                p.Add((0.6f, () =>
                {
                    switcher.SetView(CharacterView.FirstPerson);
                    var wm = Object.FindAnyObjectByType<ZombieRush.Waves.WaveManager>(); if (wm != null) wm.enabled = false;
                    foreach (var z in Object.FindObjectsByType<ZombieRush.Zombie.ZombieHealth>(FindObjectsSortMode.None)) Object.Destroy(z.gameObject);
                    log.AppendLine($"  {tables.Length} tables, {lanterns.Length} lanterns; start: rifle unlocked {pwc.IsUnlocked(rifle)}, gun in hand {pwc.CurrentWeapon?.name}");
                }));
                // views: the row of tables from the hall, table 1 close up
                Vector3 mid = (tables[0].position + tables[1].position) * 0.5f;   // the two benches on one side wall
                p.Add((0.8f, () => Put(mid + tables[0].forward * 6f, mid, 10f)));
                p.Add((0.3f, () => Snap("Tables")));
                p.Add((0.6f, () => Put(Front(tables[0], 2.2f) + tables[0].right * 0.2f, tables[0].position, 28f)));
                p.Add((0.3f, () => Snap("Table1")));
                p.Add((0.6f, () => Put(Front(tables[2], 2.2f) + tables[2].right * 0.4f, tables[2].position, 28f)));
                p.Add((0.3f, () => Snap("Table2")));
                // lanterns: from out in the yard, then one close up
                if (lanterns.Length > 0)
                {
                    var l0 = lanterns[0];
                    p.Add((0.8f, () => Put(l0.position + l0.forward * 6f + l0.right * 3f, l0.position, -6f)));   // from the middle of the hall
                    p.Add((0.4f, () => Snap("Lanterns")));
                    p.Add((0.6f, () => Put(l0.position + l0.forward * 2.2f + l0.right * 0.9f, l0.position + l0.forward * 0.55f, -18f)));
                    p.Add((0.4f, () => Snap("LanternClose")));
                }
                // pick-ups: walk up to table 1 (AK + shotgun on it)
                var gunPick = tables[0].GetComponentsInChildren<ZombieRush.World.BasePickup>().FirstOrDefault(b => b.weapon == rifle);
                p.Add((0.8f, () => Put(Front(tables[0], 0.75f) - tables[0].right * 0.3f, tables[0].position, 20f)));
                p.Add((0.3f, () =>
                {
                    log.AppendLine($"  at table 1: rifle unlocked {pwc.IsUnlocked(rifle)}, in hand {pwc.CurrentWeapon?.name}, AK on the table shown {(gunPick != null && gunPick.display != null && gunPick.display.activeSelf)}");
                    Snap("TakeGun");
                }));
                // ammo box: rifle reserve lowered, then the rifle box on table 1
                var box = tables[0].GetComponentsInChildren<ZombieRush.World.AmmoPickup>().FirstOrDefault(a => a.weapon == rifle);
                int before = 0;
                p.Add((0.3f, () =>
                {
                    Put(sbase.Center + (sbase.Center - tables[0].position).normalized * 3f, sbase.Center, 0f);   // step away first
                    inv.TakeReserveAmmo(rifle, inv.GetReserveAmmo(rifle) - 20);
                    before = inv.GetReserveAmmo(rifle);
                }));
                if (box != null)
                {
                    p.Add((0.6f, () => Put(box.transform.position + tables[0].forward * 0.8f - Vector3.up * box.transform.position.y + Vector3.up * tables[0].position.y, box.transform.position, 25f)));
                    p.Add((0.25f, () =>
                    {
                        log.AppendLine($"  rifle ammo box: reserve {before} -> {inv.GetReserveAmmo(rifle)}, box shown {(box.display != null && box.display.activeSelf)}");
                        Snap("AmmoToast");
                    }));
                }
                // a zombie-style drop out in the hall
                ZombieRush.World.AmmoPickup drop = null;
                p.Add((0.5f, () =>
                {
                    Put(sbase.Center, sbase.Center + tables[1].forward, 30f);
                    inv.TakeReserveAmmo(rifle, inv.GetReserveAmmo(rifle) - 10);
                    before = inv.GetReserveAmmo(rifle);
                    drop = ZombieRush.World.AmmoPickup.SpawnDrop(sbase.Center + tables[1].forward * 2.6f, rifle, 22);
                    log.AppendLine($"  drop spawned {(drop != null ? drop.name : "NULL")} (rifle.ammoPack {(rifle.ammoPack != null ? rifle.ammoPack.name : "null")})");
                }));
                p.Add((0.6f, () => Snap("Drop")));
                p.Add((0.5f, () => Put(sbase.Center + tables[1].forward * 2.6f, sbase.Center + tables[1].forward * 5f, 10f)));
                p.Add((0.4f, () => log.AppendLine($"  drop picked: reserve {before} -> {inv.GetReserveAmmo(rifle)}, drop gone {drop == null}")));
                return p;
            }

            if (mode == "hud")
            {
                // Zombie health bars and damage numbers: real Game view captures (the overlay canvas
                // is not in camera renders). Single hits, a merged 8-pellet shotgun shot, a kill, then the fade.
                var crowd = new List<ZombieRush.Zombie.ZombieHealth>();
                void HudStatus(string tag)
                {
                    var ui = ZombieRush.UI.ZombieOverheadUI.Instance;
                    var sb = new StringBuilder($"  {tag}: overlay {(ui != null ? "yes" : "MISSING")}");
                    if (ui != null)
                    {
                        int bars = 0, pops = 0;
                        foreach (Transform t in ui.barTemplate.parent) if (t.gameObject.activeSelf && t != ui.barTemplate) bars++;
                        foreach (Transform t in ui.popupTemplate.transform.parent) if (t.gameObject.activeSelf && t != ui.popupTemplate.transform) pops++;
                        sb.Append($", bars shown {bars}, numbers shown {pops}");
                        foreach (Transform t in ui.popupTemplate.transform.parent)
                            if (t.gameObject.activeSelf && t != ui.popupTemplate.transform) sb.Append($" [{t.GetComponent<UnityEngine.UI.Text>().text}]");
                    }
                    foreach (var z in crowd) sb.Append($" | {z.name}: {z.CurrentHealth:0}/{z.MaxHealth:0}{(z.IsDead ? " DEAD" : "")}");
                    log.AppendLine(sb.ToString());
                }
                void GameShot(string name)
                {
                    Directory.CreateDirectory(Folder);
                    ScreenCapture.CaptureScreenshot($"{Folder}/Play_{name}.png");
                }
                p.Add((0.8f, () => MoveToOpenSpot(switcher.transform, true)));
                p.Add((0.5f, () =>
                {
                    var plan2 = new[] { ("Wanderer", 7f, -18f), ("Runner", 10f, 14f), ("Horror", 13f, 0f) };
                    foreach (var (n, dist, ang) in plan2)
                    {
                        var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Zombie_{n}.prefab");
                        Vector3 f = Quaternion.Euler(0f, ang, 0f) * Vector3.ProjectOnPlane(switcher.transform.forward, Vector3.up).normalized;
                        Vector3 pos = switcher.transform.position + f * dist;
                        if (Physics.Raycast(pos + Vector3.up * 20f, Vector3.down, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point;
                        var z = Object.Instantiate(pf, pos, Quaternion.LookRotation(-f, Vector3.up));
                        z.name = n;
                        var rz = z.GetComponent<ZombieRush.Zombie.ZombieSpawnRise>(); if (rz != null) rz.enabled = false;
                        var ai = z.GetComponent<ZombieRush.Zombie.ZombieAI>(); if (ai != null) ai.enabled = false; // stand still for the pictures
                        crowd.Add(z.GetComponent<ZombieRush.Zombie.ZombieHealth>());
                    }
                }));
                p.Add((0.6f, () => { GameShot("Hud_0_Full"); HudStatus("full health"); }));
                p.Add((0.05f, () => { foreach (var z in crowd) z.TakeDamage(25f); }));
                p.Add((0.12f, () => { GameShot("Hud_1_Hit"); HudStatus("each hit for 25"); }));
                p.Add((0.05f, () => { for (int k = 0; k < 8; k++) crowd[2].TakeDamage(12f); })); // one shotgun shot: 8 pellets, same frame
                p.Add((0.12f, () => { GameShot("Hud_2_Shotgun"); HudStatus("Horror hit by 8 pellets x 12"); }));
                p.Add((0.05f, () => crowd[0].TakeDamage(500f)));
                p.Add((0.12f, () => { GameShot("Hud_3_Kill"); HudStatus("Wanderer killed"); }));
                p.Add((0.8f, () => { GameShot("Hud_4_Drain"); HudStatus("after 0.8 s"); }));
                p.Add((1.2f, () => HudStatus("after 2 s")));
                return p;
            }

            if (mode == "burst")
            {
                // The Bloater (BellyBurst): 1) one walks at the player from 10 m with its AI on, swells and bursts
                // (player health logged, shots every 0.3 s); 2) a second one is shot dead with a Wanderer beside it:
                // it must still burst and hurt the Wanderer. Shots Burst_<nn>.png and Burst_Kill_<n>.png.
                var ph = switcher.GetComponent<PlayerHealth>();
                GameObject b1 = null, b2 = null, w = null;
                string St(GameObject go)
                {
                    if (go == null) return "removed";
                    var h = go.GetComponent<ZombieRush.Zombie.ZombieHealth>();
                    var bb = go.GetComponent<ZombieRush.Zombie.BellyBurst>();
                    var a = go.GetComponentInChildren<Animator>();
                    var ci = a != null ? a.GetCurrentAnimatorClipInfo(0) : null;
                    float d = Vector3.Distance(new Vector3(go.transform.position.x, 0f, go.transform.position.z), new Vector3(switcher.transform.position.x, 0f, switcher.transform.position.z));
                    bool shown = go.GetComponentsInChildren<Renderer>().Any(r => r.enabled);
                    return $"{d:0.0} m, hp {(h != null ? h.CurrentHealth : 0f):0}, {(ci != null && ci.Length > 0 ? ci[0].clip.name : "-")}{(bb != null && bb.IsSwelling ? ", SWELLING" : "")}{(h != null && h.IsDead ? ", DEAD" : "")}{(shown ? "" : ", body hidden (burst)")}";
                }
                void Status(string tag) => log.AppendLine($"  {tag}: player hp {(ph != null ? ph.CurrentHealth : -1f):0} | bloater1 {St(b1)}{(b2 != null || w != null ? $" | bloater2 {St(b2)} | wanderer {St(w)}" : "")}");
                GameObject SpawnAt(string n, Vector3 pos, Vector3 face)
                {
                    if (Physics.Raycast(pos + Vector3.up * 20f, Vector3.down, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point;
                    var z = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Zombie_{n}.prefab"), pos, Quaternion.LookRotation(face, Vector3.up));
                    var rz = z.GetComponent<ZombieRush.Zombie.ZombieSpawnRise>(); if (rz != null) rz.enabled = false;
                    return z;
                }
                p.Add((0.8f, () =>
                {
                    MoveToOpenSpot(switcher.transform, true);
                    var sbase = ZombieRush.World.SafeBase.Instance;
                    if (sbase != null)
                    {
                        Vector3 away = switcher.transform.position - sbase.Center; away.y = 0f;
                        if (away.sqrMagnitude > 0.01f) switcher.transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
                    }
                }));
                p.Add((0.5f, () =>
                {
                    if (ph != null) ph.FullHeal();
                    Vector3 f = Vector3.ProjectOnPlane(switcher.transform.forward, Vector3.up).normalized;
                    b1 = SpawnAt("Bloater", switcher.transform.position + f * 10f, -f);
                    Status("spawned");
                }));
                for (int k = 0; k < 26; k++)
                {
                    int f = k;
                    p.Add((0.3f, () => { Shot(cam, $"Burst_{f:00}"); if (b1 != null && f % 3 == 0) Side(b1.transform, $"Burst_Side_{f:00}", 70f); Status($"t{f:00}"); }));
                }
                p.Add((0.5f, () =>
                {
                    if (ph != null) ph.FullHeal();
                    Vector3 f = Vector3.ProjectOnPlane(switcher.transform.forward, Vector3.up).normalized;
                    Vector3 r = Vector3.Cross(Vector3.up, f);
                    b2 = SpawnAt("Bloater", switcher.transform.position + f * 9f, -f);
                    w = SpawnAt("Wanderer", switcher.transform.position + f * 9f + r * 1.2f, -f);
                    foreach (var go in new[] { b2, w }) { var ai = go.GetComponent<ZombieRush.Zombie.ZombieAI>(); if (ai != null) ai.enabled = false; }
                    Status("second pair spawned (AI off)");
                }));
                p.Add((0.6f, () => { b2.GetComponent<ZombieRush.Zombie.ZombieHealth>().TakeDamage(1000f); Status("bloater2 shot dead"); }));
                for (int k = 0; k < 4; k++) { int f = k; p.Add((0.15f, () => { Shot(cam, $"Burst_Kill_{f}"); Status($"kill{f}"); })); }
                p.Add((1.0f, () => Status("later")));
                return p;
            }

            if (mode == "fight")
            {
                // A real fight: 2 Wanderers, 2 Runners and a Horror come at the player with their AI on.
                // Third-person shots and a status line every 0.6 s; then they are shot (hit, then killed).
                var crowd = new List<(string name, GameObject go)>();
                var ph = switcher.GetComponent<PlayerHealth>();
                var lastDist = new Dictionary<string, float>();
                void Status(string tag)
                {
                    var sb = new StringBuilder($"  {tag}: player hp {(ph != null ? ph.CurrentHealth : -1f):0}");
                    foreach (var (n, go) in crowd)
                    {
                        if (go == null) { sb.Append($" | {n}: removed"); continue; }
                        var h = go.GetComponent<ZombieRush.Zombie.ZombieHealth>();
                        var a = go.GetComponentInChildren<Animator>();
                        var ci = a != null ? a.GetCurrentAnimatorClipInfo(0) : null;
                        var cc = go.GetComponent<CharacterController>();
                        float d = Vector3.Distance(new Vector3(go.transform.position.x, 0f, go.transform.position.z), new Vector3(switcher.transform.position.x, 0f, switcher.transform.position.z));
                        string clip = ci != null && ci.Length > 0 ? ci[0].clip.name : "-";
                        var zai = go.GetComponent<ZombieRush.Zombie.ZombieAI>();
                        sb.Append($" | {n}: {d:0.0} m, {clip}, {(cc != null ? new Vector3(cc.velocity.x, 0, cc.velocity.z).magnitude : 0f):0.0} m/s{(h != null && h.IsDead ? ", DEAD" : "")}");
                        if (zai != null && (h == null || !h.IsDead))
                            sb.Append($", route: {zai.RouteNote}, corner {zai.RouteCorner}/{zai.RouteCornerCount} at {Vector3.Distance(go.transform.position, zai.RouteCornerPoint):0.0} m");
                        bool notClosing = lastDist.TryGetValue(n + crowd.FindIndex(c => c.go == go), out var prevD) && prevD - d < 0.3f;
                        lastDist[n + crowd.FindIndex(c => c.go == go)] = d;
                        if (cc != null && (new Vector3(cc.velocity.x, 0, cc.velocity.z).magnitude < 0.3f || notClosing) && d > 4f && (h == null || !h.IsDead))
                        {
                            // Why is it not moving? What is in front, what it stands in, its AI state.
                            var ai = zai;
                            object F(string f) => typeof(ZombieRush.Zombie.ZombieAI).GetField(f, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(ai);
                            Vector3 to = (ai != null ? ai.RouteCornerPoint : switcher.transform.position) - go.transform.position; to.y = 0f;
                            string front = Physics.SphereCast(go.transform.position + Vector3.up, 0.4f, to.normalized, out var fh, 2f, ~0, QueryTriggerInteraction.Ignore)
                                ? $"{fh.collider.name} ({fh.collider.GetType().Name}) at {fh.distance:0.0} m, walk map under it {(UnityEngine.AI.NavMesh.SamplePosition(fh.point, out var nh, 0.4f, UnityEngine.AI.NavMesh.AllAreas) ? $"YES ({Vector3.Distance(nh.position, fh.point):0.00} m)" : "no")}"
                                : "nothing";
                            sb.Append(notClosing ? " [NOT CLOSING IN" : " [STOPPED");
                            var inside = Physics.OverlapCapsule(go.transform.position + Vector3.up * (cc.radius + 0.1f), go.transform.position + Vector3.up * (cc.height - cc.radius), cc.radius * 0.9f, ~0, QueryTriggerInteraction.Ignore)
                                .Where(c => !c.transform.IsChildOf(go.transform)).Select(c => c.name).Distinct().ToArray();
                            sb.Append($": ahead {front}; overlapping {(inside.Length > 0 ? string.Join(",", inside) : "none")}; grounded {cc.isGrounded}; escape {F("escapeTimer"):0.0}, fails {F("failedEscapes")}, guarding {F("guarding")}, ai on {ai.enabled}]");
                        }
                    }
                    log.AppendLine(sb.ToString());
                }
                p.Add((0.8f, () =>
                {
                    MoveToOpenSpot(switcher.transform, true);
                    // Face away from the base, so the zombies come from open street.
                    var sbase = ZombieRush.World.SafeBase.Instance;
                    if (sbase != null)
                    {
                        Vector3 away = switcher.transform.position - sbase.Center; away.y = 0f;
                        if (away.sqrMagnitude > 0.01f) switcher.transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
                    }
                    log.AppendLine($"  player outside base: {(sbase == null || !sbase.Contains(switcher.transform.position) ? "yes" : "NO")}");
                    // Which walk map is loaded, where it lies, and how far the nearest walkable spot is.
                    var tri = UnityEngine.AI.NavMesh.CalculateTriangulation();
                    var nb = new Bounds(tri.vertices.Length > 0 ? tri.vertices[0] : Vector3.zero, Vector3.zero);
                    foreach (var v in tri.vertices) nb.Encapsulate(v);
                    var anm = Object.FindAnyObjectByType<ZombieRush.World.ArenaNavMesh>();
                    Vector3 pp = switcher.transform.position;
                    string near = UnityEngine.AI.NavMesh.SamplePosition(pp, out var nh2, 60f, UnityEngine.AI.NavMesh.AllAreas)
                        ? $"{nh2.position} ({Vector3.Distance(nh2.position, pp):0.0} m away)" : "none within 60 m";
                    log.AppendLine($"  walk map: component {(anm != null ? $"{anm.name}, data {(anm.data != null ? anm.data.name : "NONE")}, enabled {anm.isActiveAndEnabled}" : "MISSING")}, ready {ZombieRush.World.ArenaNavMesh.Ready}, " +
                                   $"{tri.vertices.Length} verts / {tri.indices.Length / 3} tris, bounds min {nb.min} max {nb.max}; player at {pp}, nearest walkable {near}");
                }));
                p.Add((0.5f, () =>
                {
                    if (ph != null) ph.FullHeal();
                    var plan2 = new[] { ("Wanderer", 14f, -30f), ("Wanderer", 16f, 25f), ("Runner", 24f, -10f), ("Runner", 26f, 15f), ("Horror", 18f, 0f) };
                    foreach (var (n, dist, ang) in plan2)
                    {
                        var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Zombie_{n}.prefab");
                        Vector3 f = Quaternion.Euler(0f, ang, 0f) * Vector3.ProjectOnPlane(switcher.transform.forward, Vector3.up).normalized;
                        Vector3 pos = switcher.transform.position + f * dist;
                        if (Physics.Raycast(pos + Vector3.up * 20f, Vector3.down, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point;
                        // Same rule as real waves: open ground with a full walking route to the player.
                        var zm = ZombieRush.World.ZoneManager.Instance;
                        if (zm != null && ZombieRush.World.ArenaSpawn.TryNear(pos, zm.Shape, switcher.transform.position, out var good, 8f, 30)) pos = good;
                        else log.AppendLine($"  {n}: no good spawn near {pos} ({ZombieRush.World.ArenaSpawn.LastReject}), placed anyway");
                        var z = Object.Instantiate(pf, pos, Quaternion.LookRotation(-f, Vector3.up));
                        var rz = z.GetComponent<ZombieRush.Zombie.ZombieSpawnRise>(); if (rz != null) rz.enabled = false;
                        crowd.Add((n, z));
                    }
                    Status("spawned");
                }));
                for (int k = 0; k < 12; k++)
                {
                    int f = k;
                    p.Add((0.6f, () => { Shot(cam, $"Fight_{f:00}"); Status($"t{f:00}"); }));
                }
                // Shoot each one: a hit first (flinch), then enough to kill.
                p.Add((0.3f, () => { foreach (var (n, go) in crowd) if (go != null) go.GetComponent<ZombieRush.Zombie.ZombieHealth>().TakeDamage(10f); Status("all hit once"); }));
                p.Add((0.25f, () => { Shot(cam, "Fight_Hit"); Status("after hit"); }));
                p.Add((0.6f, () => { foreach (var (n, go) in crowd) if (go != null) go.GetComponent<ZombieRush.Zombie.ZombieHealth>().TakeDamage(1000f); Status("all killed"); }));
                for (int k = 0; k < 4; k++) { int f = k; p.Add((0.5f, () => { Shot(cam, $"Fight_Dead{f}"); Status($"dead{f}"); })); }
                p.Add((2.5f, () => Status("later")));
                return p;
            }

            if (mode == "breach")
            {
                // The zombie spawn "ground breach": two zombies surface 6-9 m in front of the player (third person);
                // shots as the ground tears open, while they climb out and as the scar fades.
                var crowd = new List<GameObject>();
                p.Add((0.5f, () =>
                {
                    var wm = Object.FindAnyObjectByType<ZombieRush.Waves.WaveManager>(); if (wm != null) { wm.StopAllCoroutines(); wm.enabled = false; }
                    foreach (var zz in Object.FindObjectsByType<ZombieRush.Zombie.ZombieHealth>(FindObjectsSortMode.None)) Object.Destroy(zz.gameObject);
                    MoveToOpenSpot(switcher.transform, true);
                    switcher.SetView(CharacterView.ThirdPerson);
                    var ph0 = switcher.GetComponent<PlayerHealth>(); if (ph0 != null) ph0.enabled = false;
                    var pads = wm != null ? wm.spawnPoints.Count(t => t != null && t.GetComponentsInChildren<Renderer>().Any(r => r.enabled)) : -1;
                    log.AppendLine($"  old spawn discs still drawn: {pads}");
                }));
                p.Add((0.6f, () =>
                {
                    Vector3 f = Vector3.ProjectOnPlane(switcher.transform.forward, Vector3.up).normalized, rt = Vector3.Cross(Vector3.up, f);
                    foreach (var (n, off) in new[] { ("Wanderer", f * 7f - rt * 1.8f), ("Horror", f * 9f + rt * 2.5f) })
                    {
                        var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Zombie_{n}.prefab");
                        Vector3 pos = switcher.transform.position + off;
                        if (Physics.Raycast(pos + Vector3.up * 20f, Vector3.down, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point;
                        var z = Object.Instantiate(pf, pos, Quaternion.LookRotation(-f, Vector3.up));
                        var ai = z.GetComponent<ZombieRush.Zombie.ZombieAI>(); if (ai != null) ai.enabled = false;
                        (z.GetComponent<ZombieRush.Zombie.ZombieSpawnRise>() ?? z.AddComponent<ZombieRush.Zombie.ZombieSpawnRise>()).Begin(pos);
                        crowd.Add(z);
                    }
                }));
                foreach (var (dt, tag) in new[] { (0.12f, "0_tear"), (0.25f, "1_open"), (0.35f, "2_rising"), (0.6f, "3_out"), (1.2f, "4_cooling"), (1.5f, "5_scar"), (1.3f, "6_gone") })
                {
                    string tg = tag;
                    p.Add((dt, () => { Shot(cam, $"Breach_{tg}"); foreach (var z in crowd) if (z != null) { var ai = z.GetComponent<ZombieRush.Zombie.ZombieAI>(); if (ai != null) ai.enabled = false; } }));
                }
                p.Add((0.1f, () => { foreach (var z in crowd) if (z != null) Object.Destroy(z); }));
                return p;
            }

            if (mode == "chase")
            {
                // Far-off zombies chasing a player who walks away and strafes (user: "zombies move randomly instead
                // of following, like something pulls them, mostly when the player is far"). Per zombie: frames
                // walking more than 70 deg off its route, sideways escapes started, and how many the OLD rule
                // (must get 0.6 m closer to the player every 2 s) would have fired.
                var crowd = new List<(string name, GameObject go)>();
                var ph = switcher.GetComponent<PlayerHealth>();
                var stats = new Dictionary<GameObject, (int frames, int off, int escapes, int oldRule, float startD, float winD, float winT, bool esc)>();
                System.Action<ScriptableRenderContext, Camera> rec = (ctx, c) =>
                {
                    if (c != cam) return;
                    foreach (var (n, go) in crowd)
                    {
                        if (go == null || !stats.TryGetValue(go, out var s)) continue;
                        var ai = go.GetComponent<ZombieRush.Zombie.ZombieAI>(); var cc = go.GetComponent<CharacterController>();
                        Vector3 v = cc.velocity; v.y = 0f;
                        Vector3 to = ai.RouteCornerPoint - go.transform.position; to.y = 0f;
                        float d = Vector3.Distance(new Vector3(go.transform.position.x, 0, go.transform.position.z), new Vector3(switcher.transform.position.x, 0, switcher.transform.position.z));
                        s.frames++;
                        if (v.magnitude > 0.3f && to.sqrMagnitude > 0.04f && Vector3.Angle(v, to) > 70f && d > 5f) s.off++;
                        float esc = (float)typeof(ZombieRush.Zombie.ZombieAI).GetField("escapeTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(ai);
                        if (esc > 0f && !s.esc)
                        {
                            s.escapes++;
                            string ahead = to.sqrMagnitude > 0.01f && Physics.SphereCast(go.transform.position + Vector3.up, 0.4f, to.normalized, out var fh, 2f, ~0, QueryTriggerInteraction.Ignore)
                                ? $"{fh.collider.name} at {fh.distance:0.0} m" : "nothing";
                            log.AppendLine($"    {n} escape at {Time.time:0.0}s: {d:0.0} m from player, speed {v.magnitude:0.00} m/s, route corner {ai.RouteCorner}/{ai.RouteCornerCount} {to.magnitude:0.0} m away, ahead: {ahead}");
                        }
                        s.esc = esc > 0f;
                        s.winT += Time.deltaTime;
                        if (d < 6f) { s.winT = 0f; s.winD = d; }
                        else if (s.winT >= 2f) { if (s.winD - d < 0.6f) s.oldRule++; s.winT = 0f; s.winD = d; }
                        stats[go] = s;
                    }
                };
                p.Add((0.5f, () =>
                {
                    var wm = Object.FindAnyObjectByType<ZombieRush.Waves.WaveManager>(); if (wm != null) { wm.StopAllCoroutines(); wm.enabled = false; }
                    foreach (var zz in Object.FindObjectsByType<ZombieRush.Zombie.ZombieHealth>(FindObjectsSortMode.None)) Object.Destroy(zz.gameObject);
                    MoveToOpenSpot(switcher.transform, true);
                    var sbase = ZombieRush.World.SafeBase.Instance;
                    if (sbase != null)
                    {
                        Vector3 away = switcher.transform.position - sbase.Center; away.y = 0f;
                        if (away.sqrMagnitude > 0.01f) switcher.transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
                    }
                }));
                p.Add((0.5f, () =>
                {
                    if (ph != null) ph.FullHeal();
                    var plan2 = new[] { ("Wanderer", 26f, 160f), ("Wanderer", 30f, -150f), ("Horror", 28f, 180f), ("Ghoul", 32f, 135f), ("Runner", 34f, -135f), ("Wanderer", 25f, 110f) };
                    foreach (var (n, dist, ang) in plan2)
                    {
                        var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Zombie_{n}.prefab");
                        if (pf == null) { log.AppendLine($"  no prefab Zombie_{n}"); continue; }
                        Vector3 f = Quaternion.Euler(0f, ang, 0f) * Vector3.ProjectOnPlane(switcher.transform.forward, Vector3.up).normalized;
                        Vector3 pos = switcher.transform.position + f * dist;
                        if (Physics.Raycast(pos + Vector3.up * 20f, Vector3.down, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point;
                        var zm = ZombieRush.World.ZoneManager.Instance;
                        if (zm != null && ZombieRush.World.ArenaSpawn.TryNear(pos, zm.Shape, switcher.transform.position, out var good, 8f, 30)) pos = good;
                        var z = Object.Instantiate(pf, pos, Quaternion.LookRotation(-f, Vector3.up));
                        var rz = z.GetComponent<ZombieRush.Zombie.ZombieSpawnRise>(); if (rz != null) rz.enabled = false;
                        crowd.Add((n, z));
                        float d0 = Vector3.Distance(pos, switcher.transform.position);
                        stats[z] = (0, 0, 0, 0, d0, d0, 0f, false);
                    }
                    RenderPipelineManager.beginCameraRendering += rec;
                }));
                foreach (var (dir, secs) in new[] { (new Vector2(0f, 0.5f), 3f), (new Vector2(1f, 0f), 3f), (new Vector2(-1f, 0f), 3f), (new Vector2(0f, 1f), 3f) })
                {
                    var dd = dir;
                    p.Add((secs, () => { if (ph != null) ph.FullHeal(); ZombieRush.Core.GameInput.SetTouchMove(dd); }));
                }
                p.Add((0.2f, () =>
                {
                    ZombieRush.Core.GameInput.SetTouchMove(Vector2.zero);
                    RenderPipelineManager.beginCameraRendering -= rec;
                    foreach (var (n, go) in crowd)
                    {
                        if (go == null || !stats.TryGetValue(go, out var s)) continue;
                        var ai = go.GetComponent<ZombieRush.Zombie.ZombieAI>();
                        float d = Vector3.Distance(go.transform.position, switcher.transform.position);
                        log.AppendLine($"  {n}: {s.startD:0} -> {d:0} m, off its route {100f * s.off / Mathf.Max(1, s.frames):0}% of {s.frames} frames, sideways escapes {s.escapes} (old rule would have fired {s.oldRule}), route {ai.RouteNote}");
                    }
                    foreach (var (n, go) in crowd) if (go != null) Object.Destroy(go);
                }));
                return p;
            }

            if (mode.StartsWith("attackseq:"))
            {
                // One zombie held 3 m in front of the player, AI off: films its attack every 0.1 s from the
                // side (Seq_<Name>_Attack_<nn>.png) and notes which frame its damage lands on (attackHitDelay).
                string nm = mode.Substring(mode.IndexOf(':') + 1);
                GameObject z = null; float t0 = 0f;
                p.Add((0.3f, () =>
                {
                    var wm = Object.FindAnyObjectByType<ZombieRush.Waves.WaveManager>(); if (wm != null) { wm.StopAllCoroutines(); wm.enabled = false; }
                    foreach (var zz in Object.FindObjectsByType<ZombieRush.Zombie.ZombieHealth>(FindObjectsSortMode.None)) Object.Destroy(zz.gameObject);
                    var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Zombie_{nm}.prefab");
                    Vector3 f = Vector3.ProjectOnPlane(switcher.transform.forward, Vector3.up).normalized;
                    z = Object.Instantiate(pf, switcher.transform.position + f * 3f, Quaternion.LookRotation(-f, Vector3.up));
                    z.name = "Zombie (ATTACKSEQ)";
                    var rz = z.GetComponent<ZombieRush.Zombie.ZombieSpawnRise>(); if (rz != null) rz.enabled = false;
                    var ai = z.GetComponent<ZombieRush.Zombie.ZombieAI>(); if (ai != null) ai.enabled = false;
                    var cc = z.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
                    switcher.transform.position -= f * 2.5f;     // player out of the shot
                }));
                p.Add((1.0f, () => { var h = z.GetComponent<ZombieRush.Zombie.ZombieHealth>(); h.Anim.SetSpeed(0f); }));
                p.Add((0.05f, () =>
                {
                    var h = z.GetComponent<ZombieRush.Zombie.ZombieHealth>();
                    h.Anim.TriggerAttack(); t0 = Time.time;
                    log.AppendLine($"{nm}: attack started, damage lands at {h.Config.attackHitDelay:0.00} s (frame {Mathf.RoundToInt(h.Config.attackHitDelay / 0.1f):00})");
                }));
                for (int k = 0; k <= 18; k++)
                {
                    int kk = k;
                    p.Add((0.1f, () =>
                    {
                        Side(z.transform, $"Seq_{nm}_Attack_{kk:00}", 70f);
                        var an = z.GetComponentInChildren<Animator>();
                        var st = an.GetCurrentAnimatorStateInfo(0);
                        log.AppendLine($"  frame {kk:00} at {Time.time - t0:0.00} s: state {(st.IsName("Attack") ? "Attack" : st.IsName("Idle") ? "Idle" : st.shortNameHash.ToString())} {st.normalizedTime:0.00}");
                    }));
                }
                p.Add((0.1f, () => { if (z != null) Object.Destroy(z); }));
                return p;
            }

            if (mode == "uishots2")
            {
                // every Survivor's Kit screen as a Game view capture
                void Shot2(string n)
                {
                    Directory.CreateDirectory(Folder);
                    ScreenCapture.CaptureScreenshot($"{Folder}/UI_{n}.png");
                    // zombies would kill the player between shots and end the match early
                    foreach (var z in ZombieRush.Zombie.ZombieHealth.Active.ToArray()) if (z != null) z.Kill(false);
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();   // the capture is written on the Game view's next paint
                }
                var gm = ZombieRush.Core.GameManager.Instance;
                p.Add((1.5f, () => { ZombieRush.Core.GameSettings.Controls = ZombieRush.Core.ControlScheme.KeyboardMouse; switcher.SetView(CharacterView.FirstPerson); }));
                p.Add((1.0f, () => { ZombieRush.Core.GameEvents.RaiseWaveAnnounced(4); }));
                p.Add((0.6f, () => Shot2("Banner")));
                p.Add((3.5f, () =>
                {
                    ZombieRush.Core.GameEvents.RaiseWaveStarted(4);
                    ZombieRush.Core.GameEvents.RaiseZombiesRemainingChanged(12);
                    ZombieRush.Core.GameEvents.RaiseCoinsChanged(120);
                    var ph = switcher.GetComponent<PlayerHealth>(); if (ph != null) ph.TakeDamage(28f);
                }));
                p.Add((1.0f, () => Shot2("HUD_PC")));
                p.Add((0.5f, () => { var ph = switcher.GetComponent<PlayerHealth>(); if (ph != null) ph.TakeDamage(50f); }));
                p.Add((1.0f, () => Shot2("HUD_PC_Hurt")));
                p.Add((1.5f, () => { var ph = switcher.GetComponent<PlayerHealth>(); if (ph != null) { if (ph.IsDead) ph.Revive(); ph.FullHeal(); } ZombieRush.Core.GameSettings.Controls = ZombieRush.Core.ControlScheme.Touch; }));
                p.Add((2.0f, () => Shot2("HUD_Touch")));
                p.Add((2.0f, () => { ZombieRush.Core.GameSettings.Controls = ZombieRush.Core.ControlScheme.KeyboardMouse; gm.TogglePause(); }));
                p.Add((2.0f, () => Shot2("Pause")));
                p.Add((2.0f, () => { }));
                p.Add((0.3f, () => { var o = Object.FindAnyObjectByType<ZombieRush.UI.OptionsMenuUI>(FindObjectsInactive.Include); if (o != null) o.Open(); }));
                p.Add((2.0f, () => Shot2("Options")));
                p.Add((2.0f, () => { }));
                p.Add((0.3f, () => { var o = Object.FindAnyObjectByType<ZombieRush.UI.OptionsMenuUI>(FindObjectsInactive.Include); if (o != null) o.Close(); gm.TogglePause(); }));
                p.Add((0.5f, () => { gm.autoRestartDelay = 0f; var ph = switcher.GetComponent<PlayerHealth>(); if (ph != null) ph.TakeDamage(99999f); }));   // the game over screen opens on death
                p.Add((2.5f, () => Shot2("GameOver")));
                return p;
            }

            if (mode == "netrelay")
            {
                for (int k = 0; k < 40; k++)
                    p.Add((0f, () =>
                    {
                        if (relayTask != null && !relayTask.IsCompleted) extraWait = 1.0;
                    }));
                p.Add((0.5f, () =>
                {
                    log.Append(relayLog.ToString());
                    log.AppendLine($"relay host: {(relayTask == null ? "not started" : relayTask.IsCompleted ? $"code {relayTask.Result ?? "NONE"}" : "still waiting")}, running {ZombieRush.Net.NetSession.IsRunning}, server {ZombieRush.Net.NetSession.IsServer}, match {(ZombieRush.Net.MatchNet.Instance != null)}");
                }));
                return p;
            }

            if (mode == "nettest")
            {
                // Hosts directly (started before the plan) and waits up to 3 minutes for a client
                // (a Windows build run with -zrjoin 127.0.0.1); then films the client's copy for 30 s.
                bool seen = false;
                for (int k = 0; k < 180; k++)
                    p.Add((0f, () =>
                    {
                        if (seen) return;
                        extraWait = 1.0;   // wait a second per check until the client is in
                        if (ZombieRush.Net.NetPlayer.All.Count >= 2) { seen = true; log.AppendLine($"client joined: {ZombieRush.Net.NetSession.PlayerCount} players"); }
                    }));
                for (int k = 0; k < 15; k++)
                {
                    int kk = k;
                    p.Add((2f, () =>
                    {
                        var other = ZombieRush.Net.NetPlayer.All.Find(n => !n.IsOwner);
                        if (other == null || other.Copy == null) { log.AppendLine($"  t{kk}: no teammate copy (players {ZombieRush.Net.NetPlayer.All.Count})"); return; }
                        var c = other.Copy;
                        var st = other.Current;
                        log.AppendLine($"  t{kk}: teammate at {c.transform.position:F2} (state {st.position:F2}), yaw {st.yaw:0}, pitch {st.pitch:0}, gun {st.weapon}, flags {st.flags}, health {st.health:0}");
                        switcher.transform.position = c.transform.position - c.transform.forward * 4f + c.transform.right * 1.5f;
                        switcher.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(c.transform.position - switcher.transform.position, Vector3.up), Vector3.up);
                        Side(c.transform, $"Net_Teammate_{kk:00}", 60f);
                    }));
                }
                return p;
            }

            if (mode == "fxlook")
            {
                // Close-ups of the effects: blood spray on hits and the blood pool on a kill (a Wanderer),
                // then the Ghoul at range (stands and spits, the goo puddle) and its death burst.
                GameObject z = null; Vector3 f = Vector3.forward;
                GameObject Spawn(string n, float dist, bool aiOn)
                {
                    var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Zombie_{n}.prefab");
                    var go = Object.Instantiate(pf, switcher.transform.position + f * dist, Quaternion.LookRotation(-f, Vector3.up));
                    go.name = $"Zombie (FXLOOK {n})";
                    var rz = go.GetComponent<ZombieRush.Zombie.ZombieSpawnRise>(); if (rz != null) rz.enabled = false;
                    var ai = go.GetComponent<ZombieRush.Zombie.ZombieAI>();
                    if (ai != null) { ai.enabled = aiOn; ai.alwaysHunt = true; }
                    if (!aiOn) { var cc = go.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false; }
                    return go;
                }
                void Close(string name, Vector3 at, Vector3 from)
                {
                    var go = new GameObject("FxCam");
                    try
                    {
                        var c = go.AddComponent<Camera>();
                        c.CopyFrom(cam); c.fieldOfView = 40f;
                        var src = cam.GetUniversalAdditionalCameraData(); var dst = c.GetUniversalAdditionalCameraData();
                        dst.renderPostProcessing = src.renderPostProcessing; dst.volumeLayerMask = src.volumeLayerMask;
                        go.transform.position = from; go.transform.LookAt(at);
                        Shot(c, name);
                    }
                    finally { Object.Destroy(go); }
                }
                p.Add((0.8f, () => MoveToOpenSpot(switcher.transform, true)));
                p.Add((0.3f, () =>
                {
                    var wm = Object.FindAnyObjectByType<ZombieRush.Waves.WaveManager>(); if (wm != null) { wm.StopAllCoroutines(); wm.enabled = false; }
                    foreach (var zz in Object.FindObjectsByType<ZombieRush.Zombie.ZombieHealth>(FindObjectsSortMode.None)) Object.Destroy(zz.gameObject);
                    f = Vector3.ProjectOnPlane(switcher.transform.forward, Vector3.up).normalized;
                    z = Spawn("Wanderer", 4f, false);
                }));
                Vector3 Side() => Vector3.Cross(Vector3.up, f).normalized;
                for (int k = 0; k < 4; k++)
                {
                    int kk = k;
                    p.Add((0.5f, () =>
                    {
                        var h = z.GetComponent<ZombieRush.Zombie.ZombieHealth>();
                        Vector3 chest = z.transform.position + Vector3.up * (1.2f + 0.1f * kk) + Side() * 0.1f * (kk - 1.5f);
                        h.TakeHit(5f, true, chest, (chest - (switcher.transform.position + Vector3.up * 1.6f)).normalized);
                    }));
                    p.Add((0.06f, () => Close($"Fx_Blood_{kk}", z.transform.position + Vector3.up * 1.1f, z.transform.position + Side() * 2.6f + Vector3.up * 1.4f - f * 0.6f)));
                }
                p.Add((0.3f, () => { var h = z.GetComponent<ZombieRush.Zombie.ZombieHealth>(); Vector3 chest = z.transform.position + Vector3.up * 1.2f; h.TakeHit(9999f, true, chest, f); }));
                p.Add((1.2f, () => Close("Fx_BloodPool", z.transform.position, z.transform.position - f * 2.2f + Side() * 1.2f + Vector3.up * 2.4f)));
                p.Add((2.0f, () => Close("Fx_BloodPool2", z.transform.position, z.transform.position - f * 2.2f + Side() * 1.2f + Vector3.up * 2.4f)));
                // the Ghoul at range: it must stand and spit, not walk up and claw
                p.Add((0.2f, () => { if (z != null) Object.Destroy(z); z = Spawn("Ghoul", 14f, true); }));
                for (int k = 0; k < 24; k++)
                {
                    int kk = k;
                    p.Add((0.35f, () =>
                    {
                        if (z == null) return;
                        var php = switcher.GetComponent<PlayerHealth>(); if (php != null) php.FullHeal();   // the player must not die mid-test
                        Vector3 d = z.transform.position - switcher.transform.position; d.y = 0f;
                        var an = z.GetComponentInChildren<Animator>();
                        var st = an.GetCurrentAnimatorStateInfo(0);
                        string state = st.IsName("Spit") ? "Spit" : st.IsName("Attack") ? "ATTACK (claw)" : st.IsName("Idle") ? "Idle" : st.IsName("Walk") ? "Walk" : st.IsName("Run") ? "Run" : "other";
                        int globs = Object.FindObjectsByType<ZombieRush.Zombie.AcidGlob>(FindObjectsSortMode.None).Length;
                        int puddles = Object.FindObjectsByType<ZombieRush.Zombie.ToxicZone>(FindObjectsSortMode.None).Length;
                        log.AppendLine($"  ghoul t{kk * 0.35f:0.0}: {d.magnitude:0.0} m away, {state}, globs {globs}, puddles {puddles}");
                        if (kk % 3 == 0) Close($"Fx_Ghoul_{kk:00}", (z.transform.position + switcher.transform.position) * 0.5f + Vector3.up * 0.5f,
                                              (z.transform.position + switcher.transform.position) * 0.5f + Side() * 9f + Vector3.up * 3f);
                    }));
                }
                p.Add((0.1f, () =>
                {
                    var pz = Object.FindFirstObjectByType<ZombieRush.Zombie.ToxicZone>();
                    if (pz != null) Close("Fx_Puddle", pz.transform.position, pz.transform.position - f * 2.5f + Side() * 1.5f + Vector3.up * 2.2f);
                }));
                // its death: the belly bursts
                p.Add((0.2f, () => { var h = z.GetComponent<ZombieRush.Zombie.ZombieHealth>(); h.TakeHit(9999f, true, z.transform.position + Vector3.up * 1.1f, f); }));
                var burstAt = new[] { 1.35f, 1.5f, 1.65f, 1.9f, 2.4f, 3.4f };
                float prev = 0f;
                foreach (var tt in burstAt)
                {
                    float wait = tt - prev; prev = tt; float lbl = tt;
                    p.Add((wait, () => { if (z != null) Close($"Fx_Burst_{lbl:0.00}", z.transform.position + Vector3.up * 0.8f, z.transform.position + Side() * 4.5f + Vector3.up * 1.8f - f * 1.5f); }));
                }
                p.Add((0.1f, () => { if (z != null) Object.Destroy(z); }));
                return p;
            }

            if (mode == "reach")
            {
                // Each zombie type walks up to the player (who cannot die): logs the gap between the
                // bodies (capsule edge to edge) where it stops to swing, and when each hit lands.
                PlayerHealth ph = null; float lastHp = 0f;
                GameObject z = null; string zn = "";
                float Gap()
                {
                    if (z == null) return -1f;
                    var d = z.transform.position - switcher.transform.position; d.y = 0f;
                    var zc = z.GetComponent<CharacterController>(); var pc = switcher.GetComponent<CharacterController>();
                    return d.magnitude - (zc != null ? zc.radius : 0f) - (pc != null ? pc.radius * switcher.transform.lossyScale.x : 0f);
                }
                p.Add((0.3f, () =>
                {
                    var wm = Object.FindAnyObjectByType<ZombieRush.Waves.WaveManager>(); if (wm != null) { wm.StopAllCoroutines(); wm.enabled = false; }
                    foreach (var zz in Object.FindObjectsByType<ZombieRush.Zombie.ZombieHealth>(FindObjectsSortMode.None)) Object.Destroy(zz.gameObject);
                    ph = switcher.GetComponent<PlayerHealth>();
                }));
                foreach (var name in new[] { "Wanderer", "Runner", "Ghoul", "Horror" })
                {
                    string nm = name;
                    p.Add((0.2f, () =>
                    {
                        foreach (var zz in Object.FindObjectsByType<ZombieRush.Zombie.ZombieHealth>(FindObjectsSortMode.None)) Object.Destroy(zz.gameObject);
                        if (ph != null) { ph.FullHeal(); lastHp = ph.CurrentHealth; }
                        var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Zombie_{nm}.prefab");
                        Vector3 f = Vector3.ProjectOnPlane(switcher.transform.forward, Vector3.up).normalized;
                        z = Object.Instantiate(pf, switcher.transform.position + f * 6f, Quaternion.LookRotation(-f, Vector3.up));
                        z.name = "Zombie (REACH)"; zn = nm;
                        var rz = z.GetComponent<ZombieRush.Zombie.ZombieSpawnRise>(); if (rz != null) rz.enabled = false;
                        var ai = z.GetComponent<ZombieRush.Zombie.ZombieAI>(); if (ai != null) ai.alwaysHunt = true;
                        log.AppendLine($"{nm}: spawned 6 m away, reach setting {z.GetComponent<ZombieRush.Zombie.ZombieHealth>().Config.attackReach:0.00} m");
                    }));
                    float minGap = 99f; int hits = 0;
                    for (float t = 0f; t < 7f; t += 0.1f)
                        p.Add((0.1f, () =>
                        {
                            if (z == null || ph == null) return;
                            float g = Gap();
                            if (g >= 0f) minGap = Mathf.Min(minGap, g);
                            if (ph.CurrentHealth < lastHp - 0.01f)
                            {
                                hits++;
                                log.AppendLine($"  hit {hits}: {lastHp - ph.CurrentHealth:0.0} damage with the bodies {g:0.00} m apart");
                                if (ph.CurrentHealth < 30f) ph.FullHeal();
                                lastHp = ph.CurrentHealth;
                            }
                        }));
                    p.Add((0.05f, () => log.AppendLine($"  closest gap {minGap:0.00} m, {hits} hits in 7 s")));
                }
                p.Add((0.1f, () => { if (z != null) Object.Destroy(z); }));
                return p;
            }

            if (mode == "autoreload")
            {
                // Empty each gun's magazine by holding the trigger: it must start reloading by itself and refill.
                p.Add((0.3f, () =>
                {
                    var wm = Object.FindAnyObjectByType<ZombieRush.Waves.WaveManager>(); if (wm != null) wm.enabled = false;
                    foreach (var zz in Object.FindObjectsByType<ZombieRush.Zombie.ZombieHealth>(FindObjectsSortMode.None)) Object.Destroy(zz.gameObject);
                    var ph0 = switcher.GetComponent<PlayerHealth>(); if (ph0 != null) ph0.enabled = false;
                }));
                for (int i = 0; i < weapons.weapons.Length; i++)
                {
                    int idx = i;
                    string g = weapons.weapons[idx].name;
                    float emptyAt = 0f;
                    p.Add((0.2f, () => { weapons.Unlock(weapons.weapons[idx].data); weapons.SelectWeapon(idx); weapons.RefillAmmo(); }));
                    p.Add((0.8f, () => ZombieRush.Core.GameInput.TouchPress(ZombieRush.Core.TouchAction.Fire)));
                    // hold the trigger until the magazine is empty (fire rate x magazine, capped)
                    var wd = weapons.weapons[idx].data;
                    float hold = Mathf.Min(6f, wd.magazineSize / Mathf.Max(0.5f, wd.fireRate) + 0.3f);
                    for (float tt = 0f; tt < hold; tt += 0.1f)
                        p.Add((0.1f, () => { if (emptyAt == 0f && weapons.CurrentWeapon.CurrentMagazine == 0) emptyAt = Time.time; }));
                    p.Add((0.02f, () => ZombieRush.Core.GameInput.TouchRelease(ZombieRush.Core.TouchAction.Fire)));
                    // release the trigger, then watch: reload starts on its own, magazine refills
                    p.Add((0.6f, () =>
                    {
                        var w = weapons.CurrentWeapon;
                        log.AppendLine($"  {g}: magazine {w.CurrentMagazine} / {wd.magazineSize}, emptied {(emptyAt > 0f ? "yes" : "NO")}, reloading {(w.IsReloading ? $"yes ({w.ReloadProgress:0.00})" : "no")}");
                    }));
                    for (float tt = 0.4f; tt < wd.reloadTime + 0.8f; tt += 0.4f)
                    {
                        float at = tt;
                        p.Add((0.4f, () =>
                        {
                            var w = weapons.CurrentWeapon;
                            var inv = switcher.GetComponent<PlayerInventory>();
                            log.AppendLine($"    +{at:0.0} s: {w.name} active {w.isActiveAndEnabled}, magazine {w.CurrentMagazine} / {wd.magazineSize}, reloading {w.IsReloading} ({w.ReloadProgress:0.00}), reserve {(inv != null ? inv.GetReserveAmmo(wd) : -1)}, state {(ZombieRush.Core.GameManager.Instance != null ? ZombieRush.Core.GameManager.Instance.State.ToString() : "-")}, timeScale {Time.timeScale}");
                        }));
                    }
                }
                return p;
            }

            if (mode == "jitter")
            {
                // Shake of the held gun while standing and walking, per rendered frame: the gun's Gun bone in
                // camera space (what the eye sees) and the right hand's distance from it (hand slipping on the grip).
                var samples = new List<(Vector3 gunCam, float handGap, Vector3 camWorld)>();
                var heads = new List<Vector3>(); Transform headBone = null;
                var onScreen = new List<Vector3>(); var camLocal = new List<Vector3>();
                var roots = new List<(Vector3 pos, float yaw, float bodyYaw, float dt)>();
                int inactive = 0, frames = 0; float minW = 1f; string heldName = "";
                Transform gunBone = null, hand = null;
                System.Action<ScriptableRenderContext, Camera> rec = (ctx, c) =>
                {
                    if (c != cam || gunBone == null) return;
                    // first person: as the eye sees it; third person: on the body (the camera trails and dodges walls)
                    Transform refT = switcher.IsThirdPerson ? switcher.transform : cam.transform;
                    var bodyT = Object.FindAnyObjectByType<CharacterViewMode>().transform;
                    var bgp = Object.FindAnyObjectByType<BodyGunPose>(); var ar = Object.FindAnyObjectByType<WeaponAimRig>();
                    frames++; if (bgp == null || !bgp.Active) inactive++; if (ar != null) { minW = Mathf.Min(minW, ar.HoldWeight); heldName = ar.HeldGun != null ? ar.HeldGun.name : "none"; }
                    if (ar != null && ar.HeldGun != null && !switcher.IsThirdPerson == false) { var rg = ar.HeldGun.GetComponentInChildren<ZombieRush.Weapons.GunRig>(true); if (rg != null) gunBone = rg.GunBone; }
                    roots.Add((switcher.transform.position, switcher.transform.eulerAngles.y, bodyT.localEulerAngles.y, Time.deltaTime));
                    samples.Add((refT.InverseTransformPoint(gunBone.position), hand != null ? Vector3.Distance(hand.position, gunBone.position) : 0f, cam.transform.position));
                    if (headBone != null) heads.Add(switcher.transform.InverseTransformPoint(headBone.position));
                    if (headBone != null && switcher.IsThirdPerson) onScreen.Add(cam.transform.InverseTransformPoint(headBone.parent != null ? headBone.parent.position : headBone.position));
                    if (switcher.IsThirdPerson) camLocal.Add(cam.transform.localPosition);
                };
                // what the eye sees in third person: the upper body's place in the camera's view
                string OnScreenStats()
                {
                    if (onScreen.Count < 6) return "";
                    var s = onScreen.Skip(onScreen.Count / 5).ToList();
                    float j = 0f; for (int k = 2; k < s.Count; k++) j += (s[k] - 2f * s[k - 1] + s[k - 2]).magnitude * 1000f;
                    string r = $", body on screen: drift x {(s.Max(v => v.x) - s.Min(v => v.x)) * 1000:0} y {(s.Max(v => v.y) - s.Min(v => v.y)) * 1000:0} z {(s.Max(v => v.z) - s.Min(v => v.z)) * 1000:0} mm, shake {j / (s.Count - 2):0.0} mm/frame";
                    if (camLocal.Count > 6)
                    {
                        var c = camLocal.Skip(camLocal.Count / 5).ToList();
                        int jumps = 0; for (int k = 1; k < c.Count; k++) if ((c[k] - c[k - 1]).magnitude > 0.05f) jumps++;
                        r += $", camera boom z {c.Min(v => v.z):0.00} to {c.Max(v => v.z):0.00} m ({jumps} jumps over 5 cm; every 4th: {string.Join(" ", c.Where((v, i) => i % 4 == 0).Select(v => v.z.ToString("0.00")))})";
                    }
                    onScreen.Clear(); camLocal.Clear(); return r;
                }
                string Stats(string what)
                {
                    if (samples.Count < 5) return $"    {what}: {samples.Count} frames";
                    float acc = 0f, accMax = 0f, cacc = 0f;
                    for (int k = 2; k < samples.Count; k++)
                    {
                        float a2 = (samples[k].gunCam - 2f * samples[k - 1].gunCam + samples[k - 2].gunCam).magnitude * 1000f;
                        acc += a2; accMax = Mathf.Max(accMax, a2);
                        cacc += (samples[k].camWorld - 2f * samples[k - 1].camWorld + samples[k - 2].camWorld).magnitude * 1000f;
                    }
                    int n = samples.Count - 2;
                    float gmin = samples.Min(x => x.handGap), gmax = samples.Max(x => x.handGap);
                    var sw = samples.Skip(samples.Count / 5).ToList();   // after the first steps
                    string hsw = heads.Count > 10 ? $"head swing y {(heads.Skip(heads.Count / 5).Max(h => h.y) - heads.Skip(heads.Count / 5).Min(h => h.y)) * 1000:0} mm (head y every 4th frame, mm: {string.Join(" ", heads.Where((h, i) => i % 4 == 0).Select(h => (h.y * 1000).ToString("0")))}), " : "";
                    heads.Clear();
                    string swing = hsw + $"gun swing x {(sw.Max(x => x.gunCam.x) - sw.Min(x => x.gunCam.x)) * 1000:0} y {(sw.Max(x => x.gunCam.y) - sw.Min(x => x.gunCam.y)) * 1000:0} z {(sw.Max(x => x.gunCam.z) - sw.Min(x => x.gunCam.z)) * 1000:0} mm, ";
                    float racc = 0f, yawj = 0f, byj = 0f, dtMin = 1f, dtMax = 0f;
                    float vjh = 0f, vjv = 0f;
                    for (int k = 2; k < roots.Count; k++)
                    {
                        racc += (roots[k].pos - 2f * roots[k - 1].pos + roots[k - 2].pos).magnitude * 1000f;
                        // speed change per frame, frame time taken out (steady walking = 0)
                        Vector3 v1 = (roots[k - 1].pos - roots[k - 2].pos) / Mathf.Max(1e-4f, roots[k - 1].dt);
                        Vector3 v2 = (roots[k].pos - roots[k - 1].pos) / Mathf.Max(1e-4f, roots[k].dt);
                        Vector3 dv = v2 - v1;
                        vjv += Mathf.Abs(dv.y); dv.y = 0f; vjh += dv.magnitude;
                        yawj += Mathf.Abs(Mathf.DeltaAngle(roots[k - 1].yaw, roots[k].yaw) - Mathf.DeltaAngle(roots[k - 2].yaw, roots[k - 1].yaw));
                        byj += Mathf.Abs(Mathf.DeltaAngle(roots[k - 1].bodyYaw, roots[k].bodyYaw) - Mathf.DeltaAngle(roots[k - 2].bodyYaw, roots[k - 1].bodyYaw));
                        dtMin = Mathf.Min(dtMin, roots[k].dt); dtMax = Mathf.Max(dtMax, roots[k].dt);
                    }
                    int rn = Mathf.Max(1, roots.Count - 2);
                    roots.Clear();
                    string act = $"pose off {inactive}/{frames} frames, min hold {minW:0.00}, held {heldName}, ";
                    inactive = 0; frames = 0; minW = 1f;
                    return $"    {what}: {samples.Count} frames, {act}{swing}gun shake in view {acc / n:0.0} mm/frame (max {accMax:0.0}), camera shake {cacc / n:0.0} mm/frame, player root shake {racc / rn:0.0} mm/frame (speed change per frame: level {vjh / rn:0.00} m/s, up-down {vjv / rn:0.00} m/s), " +
                           $"player yaw jitter {yawj / rn:0.00} deg, body yaw jitter {byj / rn:0.00} deg, frame time {dtMin * 1000:0}-{dtMax * 1000:0} ms, hand-gun distance {gmin * 1000:0} to {gmax * 1000:0} mm" + OnScreenStats();
                }
                p.Add((0.3f, () =>
                {
                    var wm = Object.FindAnyObjectByType<ZombieRush.Waves.WaveManager>(); if (wm != null) wm.enabled = false;
                    foreach (var zz in Object.FindObjectsByType<ZombieRush.Zombie.ZombieHealth>(FindObjectsSortMode.None)) Object.Destroy(zz.gameObject);
                    var ph0 = switcher.GetComponent<PlayerHealth>(); if (ph0 != null) ph0.enabled = false;
                    RenderPipelineManager.beginCameraRendering += rec;
                }));
                foreach (var view in new[] { CharacterView.FirstPerson, CharacterView.ThirdPerson })
                {
                    var v = view;
                    for (int i = 0; i < weapons.weapons.Length; i++)
                    {
                        int idx = i;
                        string g = weapons.weapons[idx].name;
                        p.Add((0.2f, () => { switcher.SetView(v); weapons.Unlock(weapons.weapons[idx].data); weapons.SelectWeapon(idx); weapons.RefillAmmo(); }));
                        p.Add((1.0f, () =>
                        {
                            var aim = Object.FindAnyObjectByType<WeaponAimRig>();
                            var an = aim.GetComponent<Animator>();
                            hand = an.GetBoneTransform(HumanBodyBones.RightHand); headBone = an.GetBoneTransform(HumanBodyBones.Head);
                            Transform gunRoot = v == CharacterView.FirstPerson ? weapons.CurrentWeapon.GetComponent<ZombieRush.Weapons.WeaponViewModel>()?.model : aim.HeldGun;
                            var rig = gunRoot != null ? gunRoot.GetComponentInChildren<ZombieRush.Weapons.GunRig>(true) : null;
                            gunBone = rig != null ? rig.GunBone : null;
                            samples.Clear(); roots.Clear();
                        }));
                        p.Add((1.5f, () => { log.AppendLine($"  {v} {g}:"); log.AppendLine(Stats("standing")); samples.Clear(); ZombieRush.Core.GameInput.SetTouchMove(new Vector2(0f, 1f)); }));
                        p.Add((2.0f, () => { log.AppendLine(Stats("walking forward")); samples.Clear(); ZombieRush.Core.GameInput.SetTouchMove(new Vector2(1f, 0f)); }));
                        p.Add((1.5f, () => { log.AppendLine(Stats("strafing")); samples.Clear(); ZombieRush.Core.GameInput.SetTouchMove(new Vector2(0f, -1f)); }));
                        p.Add((1.5f, () => { log.AppendLine(Stats("walking back")); samples.Clear(); ZombieRush.Core.GameInput.SetTouchMove(Vector2.zero); Shot(cam, $"Jit_{v}_{g}"); }));
                    }
                }
                p.Add((0.1f, () => { RenderPipelineManager.beginCameraRendering -= rec; ZombieRush.Core.GameInput.SetTouchMove(Vector2.zero); }));
                return p;
            }

            if (mode.StartsWith("tpppitch"))
            {
                // Third person with the rifle, looking far up to far down: the camera boom (player view) and the
                // body bend seen from the side (the backpack must stay on the back, not sink into the body).
                p.Add((0.3f, () =>
                {
                    var wm = Object.FindAnyObjectByType<ZombieRush.Waves.WaveManager>(); if (wm != null) wm.enabled = false;
                    foreach (var zz in Object.FindObjectsByType<ZombieRush.Zombie.ZombieHealth>(FindObjectsSortMode.None)) Object.Destroy(zz.gameObject);
                    var ph0 = switcher.GetComponent<PlayerHealth>(); if (ph0 != null) ph0.enabled = false;
                    switcher.SetView(CharacterView.ThirdPerson);
                }));
                string pitchGun = mode.Contains(":") ? mode.Substring(mode.IndexOf(':') + 1) : "Rifle";
                int rifle = System.Array.FindIndex(weapons.weapons, w => w.name.Contains(pitchGun));
                p.Add((0.9f, () => { if (rifle >= 0) { weapons.Unlock(weapons.weapons[rifle].data); weapons.SelectWeapon(rifle); } }));
                // Top and bottom of the real look range from the view config, so a limit change is tested as it ships.
                float upMax = switcher.config != null ? switcher.config.pitchMax : 60f;
                float downMax = switcher.config != null ? -switcher.config.pitchMin : 55f;
                foreach (float pitch in new[] { -upMax, -30f, 0f, 30f, downMax })
                {
                    float pp = pitch;
                    string tag = pp < 0 ? $"Up{-pp:0}" : pp > 0 ? $"Down{pp:0}" : "Level";
                    p.Add((0.5f, () => { SetPitch(player, pp); weapons.RefillAmmo(); ZombieRush.Core.GameInput.TouchPress(ZombieRush.Core.TouchAction.Fire); }));
                    p.Add((0.25f, () => { SetPitch(player, pp); ZombieRush.Core.GameInput.TouchRelease(ZombieRush.Core.TouchAction.Fire); }));
                    p.Add((0.3f, () => { ScreenCapture.CaptureScreenshot($"{Folder}/TPP_{pitchGun}_{tag}_Game.png"); UnityEditorInternal.InternalEditorUtility.RepaintAllViews(); }));
                    p.Add((0.2f, () =>
                    {
                        Side(switcher.transform, $"TPP_{pitchGun}_{tag}_Side", 60f);
                        var bgp = Object.FindAnyObjectByType<ZombieRush.Player.BodyGunPose>();
                        if (bgp != null) log.AppendLine($"  {pitchGun} {tag}: torso bend {bgp.DebugFix:0} deg (view pitch {bgp.DebugWantPitch:0}), steady move {bgp.DebugMove * 100f:0.0} cm, right hand from right shoulder (right, up, fwd) {bgp.DebugHandFromShoulder.x:0.00} {bgp.DebugHandFromShoulder.y:0.00} {bgp.DebugHandFromShoulder.z:0.00} m");
                    }));
                }
                p.Add((0.2f, () => SetPitch(player, 0f)));
                return p;
            }

            if (mode == "fppitch")
            {
                // First person, each gun, looking down, level and up: the gun must stay in the hands.
                p.Add((0.3f, () =>
                {
                    var wm = Object.FindAnyObjectByType<ZombieRush.Waves.WaveManager>(); if (wm != null) wm.enabled = false;
                    foreach (var zz in Object.FindObjectsByType<ZombieRush.Zombie.ZombieHealth>(FindObjectsSortMode.None)) Object.Destroy(zz.gameObject);
                    var ph0 = switcher.GetComponent<PlayerHealth>(); if (ph0 != null) ph0.enabled = false;
                    switcher.SetView(CharacterView.FirstPerson);
                }));
                for (int i = 0; i < weapons.weapons.Length; i++)
                {
                    int idx = i;
                    string g = weapons.weapons[idx].name;
                    p.Add((0.9f, () => { weapons.Unlock(weapons.weapons[idx].data); weapons.SelectWeapon(idx); weapons.RefillAmmo(); }));
                    foreach (float pitch in new[] { 40f, 0f, -35f })
                    {
                        float pp = pitch;
                        p.Add((0.6f, () => SetPitch(player, pp)));
                        p.Add((0.4f, () => Shot(cam, $"FPP_{g}_{(pp > 0 ? "Down" : pp < 0 ? "Up" : "Level")}")));
                    }
                }
                p.Add((0.1f, () => SetPitch(player, 0f)));
                return p;
            }

            if (mode == "gunhits")
            {
                // Does each gun hurt a zombie standing 6 m ahead? Three shots each, health logged, first and third person.
                GameObject z = null;
                p.Add((0.3f, () =>
                {
                    var wm = Object.FindAnyObjectByType<ZombieRush.Waves.WaveManager>(); if (wm != null) wm.enabled = false;
                    foreach (var zz in Object.FindObjectsByType<ZombieRush.Zombie.ZombieHealth>(FindObjectsSortMode.None)) Object.Destroy(zz.gameObject);
                    var ph0 = switcher.GetComponent<PlayerHealth>(); if (ph0 != null) ph0.enabled = false;
                }));
                foreach (var view in new[] { CharacterView.FirstPerson, CharacterView.ThirdPerson })
                {
                    var v = view;
                    p.Add((0.5f, () => switcher.SetView(v)));
                    for (int i = 0; i < weapons.weapons.Length; i++)
                    {
                        int idx = i;
                        string g = weapons.weapons[idx].name;
                        p.Add((0.8f, () =>
                        {
                            weapons.Unlock(weapons.weapons[idx].data); weapons.SelectWeapon(idx); weapons.RefillAmmo();
                            if (z != null) Object.Destroy(z);
                            var pf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Zombie_Wanderer.prefab");
                            Vector3 f = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                            z = Object.Instantiate(pf, switcher.transform.position + f * 6f, Quaternion.LookRotation(-f, Vector3.up));
                            var rz = z.GetComponent<ZombieRush.Zombie.ZombieSpawnRise>(); if (rz != null) rz.enabled = false;
                            var ai = z.GetComponent<ZombieRush.Zombie.ZombieAI>(); if (ai != null) ai.enabled = false;
                            // aim at its chest
                            var chest = z.transform.position + Vector3.up * 1.2f;
                            player.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(chest - player.transform.position, Vector3.up), Vector3.up);
                        }));
                        // steer until the camera's centre (the crosshair) is on the chest: in third person the camera sits off to the side
                        float pitchNow = 0f;
                        for (int it = 0; it < 4; it++)
                            p.Add((0.15f, () =>
                            {
                                var chest = z.transform.position + Vector3.up * 1.2f;
                                Vector3 want = (chest - cam.transform.position).normalized;
                                Vector3 have = cam.transform.forward;
                                float dyaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(have, Vector3.up), Vector3.ProjectOnPlane(want, Vector3.up), Vector3.up);
                                player.transform.rotation = Quaternion.Euler(0f, dyaw, 0f) * player.transform.rotation;
                                float dp = (Mathf.Asin(have.y) - Mathf.Asin(want.y)) * Mathf.Rad2Deg;
                                pitchNow += dp;
                                SetPitch(player, pitchNow);
                            }));
                        float before = 0f;
                        p.Add((0.3f, () => before = z.GetComponent<ZombieRush.Zombie.ZombieHealth>().CurrentHealth));
                        for (int k = 0; k < 3; k++)
                        {
                            p.Add((0.02f, () => ZombieRush.Core.GameInput.TouchPress(ZombieRush.Core.TouchAction.Fire)));
                            int kk = k;
                            p.Add((0.03f, () => { ZombieRush.Core.GameInput.TouchRelease(ZombieRush.Core.TouchAction.Fire); if (kk == 0) Shot(cam, $"HitFlash_{v}_{g}"); }));
                            p.Add((0.45f, () => { }));
                        }
                        p.Add((0.1f, () =>
                        {
                            var h = z.GetComponent<ZombieRush.Zombie.ZombieHealth>();
                            var w = weapons.CurrentWeapon;
                            var ray = new Ray(cam.transform.position, cam.transform.forward);
                            string first = Physics.Raycast(ray, out var hh, 50f, w.hitMask, QueryTriggerInteraction.Ignore) ? $"{hh.collider.name} at {hh.distance:0.0} m" : "nothing";
                            log.AppendLine($"  {v} {g}: zombie health {before:0} -> {h.CurrentHealth:0}{(h.IsDead ? " (dead)" : "")}, magazine {w.CurrentMagazine}, camera ray hits {first}");
                            Shot(cam, $"Hit_{v}_{g}");
                        }));
                    }
                }
                p.Add((0.1f, () => { if (z != null) Object.Destroy(z); }));
                return p;
            }

            if (mode == "gatefire")
            {
                // 2026-11-16: the burning coal line across the main gate: the player walks over it; zombies chasing a
                // player inside the base stop outside it.
                var sbG = ZombieRush.World.SafeBase.Instance;
                var fire = GameObject.Find("GateFires") != null ? GameObject.Find("GateFires").transform.Cast<Transform>().OrderBy(t =>
                    sbG != null && sbG.playerStart != null ? Vector3.Angle(sbG.playerStart.forward, Vector3.ProjectOnPlane(t.position - sbG.Center, Vector3.up)) : 0f).FirstOrDefault() : null;
                var phG = switcher.GetComponent<PlayerHealth>();
                Vector3 outDir = Vector3.forward; Vector3 cG = Vector3.zero;
                void Put(float r, bool faceOut)
                {
                    var cc = switcher.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;
                    var p2 = cG + outDir * r; p2.y = fire.position.y + 0.1f;
                    switcher.transform.SetPositionAndRotation(p2, Quaternion.LookRotation(faceOut ? outDir : -outDir));
                    if (cc != null) cc.enabled = true;
                }
                float Radius() => Vector3.ProjectOnPlane(switcher.transform.position - cG, Vector3.up).magnitude;
                void Pic(string n) { Directory.CreateDirectory(Folder); ScreenCapture.CaptureScreenshot($"{Folder}/GateFire_{n}.png"); }
                var zs = new List<GameObject>();
                p.Add((0.8f, () =>
                {
                    if (fire == null || sbG == null) { log.AppendLine("  no GateFires / SafeBase"); return; }
                    var wm = Object.FindAnyObjectByType<ZombieRush.Waves.WaveManager>(); if (wm != null) { wm.StopAllCoroutines(); wm.enabled = false; }
                    foreach (var zz in Object.FindObjectsByType<ZombieRush.Zombie.ZombieHealth>(FindObjectsSortMode.None)) Object.Destroy(zz.gameObject);
                    if (phG != null) phG.enabled = false;
                    cG = sbG.Center; outDir = Vector3.ProjectOnPlane(fire.position - cG, Vector3.up).normalized;
                    log.AppendLine($"  gate fire {fire.name} at {Vector3.ProjectOnPlane(fire.position - cG, Vector3.up).magnitude:0.0} m, safe radius {sbG.radius:0.0}");
                    switcher.SetView(CharacterView.FirstPerson);
                    Put(21f, true);
                }));
                p.Add((0.02f, () => Pic("Inside")));
                // walk out over it (third person)
                p.Add((0.8f, () => { switcher.SetView(CharacterView.ThirdPerson); Put(22.5f, true); }));
                p.Add((1.3f, () => ZombieRush.Core.GameInput.SetTouchMove(new Vector2(0f, 0.6f))));
                p.Add((0.02f, () => { Pic("Crossing"); log.AppendLine($"  crossing: {Radius():0.0} m from centre"); }));
                p.Add((1.6f, () => { }));
                p.Add((0.02f, () => { ZombieRush.Core.GameInput.SetTouchMove(Vector2.zero); log.AppendLine($"  walked out: {Radius():0.0} m from centre (fire line at {Vector3.ProjectOnPlane(fire.position - cG, Vector3.up).magnitude:0.0}) -> {(Radius() > sbG.radius + 0.5f ? "PASSED" : "STOPPED")}"); }));
                // back in, zombies come for the player
                p.Add((0.5f, () =>
                {
                    switcher.SetView(CharacterView.FirstPerson); Put(20f, true);
                    if (phG != null) { phG.enabled = true; phG.FullHeal(); }   // zombies only hunt players on the PlayerHealth list (inside the base nothing hurts)
                    var right = Vector3.Cross(Vector3.up, outDir);
                    var kinds = new[] { "Wanderer", "Runner", "Horror", "Wanderer", "Ghoul" };
                    for (int i = 0; i < kinds.Length; i++)
                    {
                        var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Zombie_{kinds[i]}.prefab");
                        if (pf == null) continue;
                        var pos = cG + outDir * (36f + i) + right * ((i - 2) * 2.5f);
                        if (Physics.Raycast(pos + Vector3.up * 20f, Vector3.down, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point;
                        var z = Object.Instantiate(pf, pos, Quaternion.LookRotation(-outDir));
                        var rz = z.GetComponent<ZombieRush.Zombie.ZombieSpawnRise>(); if (rz != null) rz.enabled = false;
                        zs.Add(z);
                    }
                    log.AppendLine($"  {zs.Count} zombies spawned 36-40 m out beyond the gate");
                }));
                p.Add((10f, () => { }));
                p.Add((0.02f, () =>
                {
                    Pic("Zombies_FP");
                    var rs = zs.Where(z => z != null).Select(z => Vector3.ProjectOnPlane(z.transform.position - cG, Vector3.up).magnitude).OrderBy(r => r).ToList();
                    log.AppendLine($"  zombies after 10 s: closest {(rs.Count > 0 ? rs[0] : 0f):0.0} m from centre (all: {string.Join(", ", rs.Select(r => r.ToString("0.0")))}), fire line at {Vector3.ProjectOnPlane(fire.position - cG, Vector3.up).magnitude:0.0}, safe radius {sbG.radius:0.0}");
                }));
                p.Add((0.6f, () => { switcher.SetView(CharacterView.ThirdPerson); Put(21f, true); }));
                p.Add((0.02f, () => Pic("Zombies_TP")));
                return p;
            }

            if (mode == "akhold")
            {
                // 2026-11-16: the AK held still (after it has settled) and during a quick look turn: how much the gun
                // moves in view and how far each hand slides on it, first and third person.
                int rifleI = System.Array.FindIndex(weapons.weapons, w => w != null && w.data != null && w.data.hasScope);
                var rows = new List<(Vector3 gun, Vector3 lh, Vector3 rh, float gunYaw)>();
                bool rec = false; float turn = 0f;
                Transform gunB = null, lH = null, rH = null; Transform refT = null;
                System.Action<ScriptableRenderContext, Camera> sample = (ctx, c) =>
                {
                    if (c != cam) return;
                    if (turn > 0f) switcher.transform.Rotate(0f, 90f * Time.deltaTime, 0f);   // the look turn, like the mouse
                    if (!rec || gunB == null || refT == null) return;
                    var gl = gunB.InverseTransformPoint(lH != null ? lH.position : gunB.position);
                    var gr = gunB.InverseTransformPoint(rH != null ? rH.position : gunB.position);
                    Vector3 f = refT.InverseTransformDirection(gunB.forward);
                    rows.Add((refT.InverseTransformPoint(gunB.position), gl, gr, Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg));
                };
                void Find(bool fp)
                {
                    gunB = lH = rH = null;
                    var w = weapons.CurrentWeapon;
                    if (fp)
                    {
                        refT = cam.transform;
                        var vm = w.GetComponent<ZombieRush.Weapons.WeaponViewModel>();
                        var rig = vm != null && vm.model != null ? vm.model.GetComponentInChildren<ZombieRush.Weapons.GunRig>(true) : null;
                        gunB = rig != null ? rig.GunBone : null;
                        var arms = cam.transform.GetComponentsInChildren<Transform>(false).FirstOrDefault(t => t.name.StartsWith("FirstPersonArms_") && t.gameObject.activeInHierarchy);
                        if (arms != null) foreach (var t in arms.GetComponentsInChildren<Transform>()) { if (t.name == "LeftHand") lH = t; if (t.name == "RightHand") rH = t; }
                    }
                    else
                    {
                        refT = switcher.transform;
                        var ar = Object.FindAnyObjectByType<WeaponAimRig>();
                        var rig = ar != null && ar.HeldGun != null ? ar.HeldGun.GetComponentInChildren<ZombieRush.Weapons.GunRig>(true) : null;
                        gunB = rig != null ? rig.GunBone : null;
                        var an = ar != null ? ar.GetComponent<Animator>() : null;
                        if (an != null) { lH = an.GetBoneTransform(HumanBodyBones.LeftHand); rH = an.GetBoneTransform(HumanBodyBones.RightHand); }
                    }
                    log.AppendLine($"    found gun bone {(gunB != null ? gunB.name : "NONE")}, left hand {(lH != null)}, right hand {(rH != null)}");
                }
                string Range(string what)
                {
                    if (rows.Count < 5) return $"    {what}: {rows.Count} frames";
                    string R(System.Func<(Vector3 gun, Vector3 lh, Vector3 rh, float gunYaw), Vector3> sel)
                    {
                        var v = rows.Select(sel).ToList();
                        return $"x {(v.Max(a => a.x) - v.Min(a => a.x)) * 1000:0} y {(v.Max(a => a.y) - v.Min(a => a.y)) * 1000:0} z {(v.Max(a => a.z) - v.Min(a => a.z)) * 1000:0}";
                    }
                    float jit = 0f; for (int k = 2; k < rows.Count; k++) jit += (rows[k].gun - 2f * rows[k - 1].gun + rows[k - 2].gun).magnitude * 1000f;
                    var s = $"    {what}: {rows.Count} frames, gun moves {R(r => r.gun)} mm (shake {jit / (rows.Count - 2):0.00} mm/frame), gun turns {rows.Max(r => r.gunYaw) - rows.Min(r => r.gunYaw):0.0} deg, LEFT hand slides on gun {R(r => r.lh)} mm, RIGHT hand slides {R(r => r.rh)} mm";
                    rows.Clear(); return s;
                }
                p.Add((0.6f, () =>
                {
                    var wm = Object.FindAnyObjectByType<ZombieRush.Waves.WaveManager>(); if (wm != null) wm.enabled = false;
                    foreach (var z in Object.FindObjectsByType<ZombieRush.Zombie.ZombieHealth>(FindObjectsSortMode.None)) Object.Destroy(z.gameObject);
                    var ph = switcher.GetComponent<PlayerHealth>(); if (ph != null) ph.enabled = false;
                    MoveToOpenSpot(switcher.transform, true);
                    RenderPipelineManager.beginCameraRendering += sample;
                }));
                foreach (var view in new[] { CharacterView.FirstPerson, CharacterView.ThirdPerson })
                {
                    bool fp = view == CharacterView.FirstPerson;
                    string v = fp ? "FP" : "TP";
                    p.Add((2.5f, () => { switcher.SetView(view); weapons.Unlock(weapons.weapons[rifleI].data); weapons.SelectWeapon(rifleI); log.AppendLine($"  {v} AK:"); }));
                    p.Add((3.0f, () => { Find(fp); rec = true; }));
                    p.Add((0.02f, () => { rec = false; log.AppendLine(Range("standing still 3 s")); }));
                    p.Add((1.0f, () => { rec = true; turn = 1f; }));
                    p.Add((0.02f, () => { rec = false; turn = 0f; log.AppendLine(Range("turning 90 deg/s")); }));
                    p.Add((0.6f, () => { Directory.CreateDirectory(Folder); ScreenCapture.CaptureScreenshot($"{Folder}/AkHold_{v}.png"); }));
                }
                p.Add((0.1f, () => RenderPipelineManager.beginCameraRendering -= sample));
                return p;
            }

            if (mode == "hudui")
            {
                // 2026-11-17: simple side-feed messages, the phone HUD editor (Pause > CUSTOMIZE HUD) and the
                // mobile performance trims (forced on in the editor for this run).
                var was = ZombieRush.Core.GameSettings.Controls;
                void HShot(string n) { Directory.CreateDirectory(Folder); ScreenCapture.CaptureScreenshot($"{Folder}/Hud_{n}.png"); }
                var fi = typeof(ZombieRush.UI.HudEditor).GetField("instance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                System.Func<ZombieRush.UI.HudEditor> ed = () => fi.GetValue(null) as ZombieRush.UI.HudEditor;
                System.Action<string, object[]> call = (m, a) => typeof(ZombieRush.UI.HudEditor).GetMethod(m, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).Invoke(ed(), a);
                PointerEventData Pe(Vector2 at) => new PointerEventData(UnityEngine.EventSystems.EventSystem.current) { position = at };
                p.Add((1.0f, () =>
                {
                    MoveToOpenSpot(switcher.transform, true);
                    ZombieRush.Core.GameSettings.Controls = ZombieRush.Core.ControlScheme.Touch;
                    ZombieRush.Core.MobilePerformance.ForceInEditor = true;
                    log.AppendLine("  " + ZombieRush.Core.MobilePerformance.Apply());
                }));
                p.Add((1.0f, () =>
                {
                    var lc = Object.FindAnyObjectByType<ZombieRush.Core.MobileLightCuller>();
                    log.AppendLine($"  light culler: {(lc != null ? lc.LitCount : -1)} of {(lc != null ? lc.Managed : -1)} scene lights on");
                    ZombieRush.Core.GameEvents.RaiseWaveAnnounced(3);
                    ZombieRush.Core.GameEvents.RaiseNotice("SAFE BASE", "GEAR UP  ·  STEP OUT TO FIGHT");
                    ZombieRush.UI.PickupToast.Show("+12 RIFLE AMMO");
                    ZombieRush.Core.GameEvents.RaiseWaveCompleted(2, 8f);
                }));
                p.Add((0.6f, () => HShot("Feed")));
                p.Add((0.5f, () => { if (ZombieRush.Core.GameManager.Instance.State == ZombieRush.Core.GameState.Playing) ZombieRush.Core.GameManager.Instance.TogglePause(); }));
                p.Add((0.8f, () =>
                {
                    HShot("Pause");
                    var feed = GameObject.Find("SideFeed");
                    log.AppendLine($"  paused: state {ZombieRush.Core.GameManager.Instance.State}, timeScale {Time.timeScale}, feed lines active {(feed != null && feed.transform.Find("Lines") != null ? feed.transform.Find("Lines").gameObject.activeSelf.ToString() : "no feed")}, feed thinks playing {typeof(ZombieRush.UI.PickupToast).GetField("gamePlaying", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null)}, feeds {Object.FindObjectsByType<ZombieRush.UI.PickupToast>(FindObjectsSortMode.None).Length}, pause panel active {Object.FindAnyObjectByType<ZombieRush.UI.PauseMenuUI>(FindObjectsInactive.Include)?.panel?.gameObject.activeInHierarchy}");
                }));
                p.Add((0.3f, () =>
                {
                    var pm = Object.FindAnyObjectByType<ZombieRush.UI.PauseMenuUI>();
                    typeof(ZombieRush.UI.PauseMenuUI).GetMethod("OpenHudEditor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(pm, null);
                }));
                p.Add((0.6f, () => HShot("Editor")));
                p.Add((0.3f, () =>
                {
                    // drag FIRE 300 px left and 150 px up; make JUMP bigger
                    var tc = ZombieRush.UI.TouchControlsUI.Instance;
                    var fire = tc.Controls.Find(c => c.key == "Fire");
                    var c0 = new Vector3[4]; fire.rt.GetWorldCorners(c0);
                    Vector2 mid = RectTransformUtility.WorldToScreenPoint(null, (c0[0] + c0[2]) * 0.5f);
                    call("OnPointerDown", new object[] { Pe(mid) });
                    call("OnDrag", new object[] { Pe(mid + new Vector2(-300f, 150f)) });
                    call("OnPointerUp", new object[] { Pe(mid + new Vector2(-300f, 150f)) });
                    var jump = tc.Controls.Find(c => c.key == "Jump");
                    jump.rt.GetWorldCorners(c0);
                    call("OnPointerDown", new object[] { Pe(RectTransformUtility.WorldToScreenPoint(null, (c0[0] + c0[2]) * 0.5f)) });
                    call("OnPointerUp", new object[] { Pe(Vector2.zero) });
                    call("Resize", new object[] { 0.3f });
                    log.AppendLine($"  edit: fire {fire.homePos} -> {fire.pos}, jump size {jump.size:0.00}");
                }));
                p.Add((0.5f, () => HShot("Edited")));
                p.Add((0.2f, () =>
                {
                    call("Close", new object[] { true });
                    log.AppendLine($"  saved: fire '{PlayerPrefs.GetString("zr.hud.Fire", "")}', jump '{PlayerPrefs.GetString("zr.hud.Jump", "")}', editor open {ZombieRush.UI.HudEditor.IsOpen}");
                    ZombieRush.Core.GameManager.Instance.Resume();
                }));
                p.Add((0.8f, () => HShot("AfterEdit")));
                p.Add((0.3f, () =>
                {
                    // leave the editor's layout as it was
                    var tc = ZombieRush.UI.TouchControlsUI.Instance; tc.ResetAll(); tc.Save();
                    ZombieRush.Core.GameSettings.Controls = was;
                    ZombieRush.Core.MobilePerformance.ForceInEditor = false;
                }));
                return p;
            }

            if (mode == "touchui")
            {
                // 2026-11-17: the phone's on-screen controls (user: "buttons too large"), shown in the Game view
                var was = ZombieRush.Core.GameSettings.Controls;
                p.Add((1.0f, () => { MoveToOpenSpot(switcher.transform, true); ZombieRush.Core.GameSettings.Controls = ZombieRush.Core.ControlScheme.Touch; }));
                p.Add((0.1f, () =>
                {
                    Directory.CreateDirectory(Folder);
                    ScreenCapture.CaptureScreenshot($"{Folder}/TouchUI.png");
                    log.AppendLine($"  screen {Screen.width}x{Screen.height}");
                    foreach (var t in Object.FindObjectsByType<ZombieRush.UI.TouchControlsUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                        log.AppendLine($"  TouchControlsUI on '{t.name}' active {t.isActiveAndEnabled} scale {t.buttonScale} root '{(t.controlsRoot != null ? t.controlsRoot.name : "null")}' fire scale {(t.controlsRoot != null && t.controlsRoot.transform.Find("Fire") != null ? t.controlsRoot.transform.Find("Fire").localScale.x : -1f)}");
                    foreach (var b in Object.FindObjectsByType<ZombieRush.UI.TouchButton>(FindObjectsSortMode.None))
                    {
                        var rt = (RectTransform)b.transform; var c = new Vector3[4]; rt.GetWorldCorners(c);
                        var canvas = rt.GetComponentInParent<Canvas>().rootCanvas;
                        Camera uc = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                        Vector2 a = RectTransformUtility.WorldToScreenPoint(uc, c[0]), z = RectTransformUtility.WorldToScreenPoint(uc, c[2]);
                        log.AppendLine($"  {b.name}: {(z.y - a.y) / Screen.height * 100f:0}% of screen height, centre at ({(a.x + z.x) * 0.5f / Screen.width * 100f:0}%, {(a.y + z.y) * 0.5f / Screen.height * 100f:0}%)");
                    }
                }));
                p.Add((0.5f, () => ZombieRush.Core.GameSettings.Controls = was));
                return p;
            }

            if (mode == "duck")
            {
                // 2026-11-16: crouching ducks the giant (Horror) zombie's swing (ZombieConfig highSwing);
                // and no turret can be put down inside the safe base.
                var ph = switcher.GetComponent<PlayerHealth>();
                GameObject z = null;
                float hp0 = 0f, lastHp = 0f;
                p.Add((0.6f, () =>
                {
                    var wm = Object.FindAnyObjectByType<ZombieRush.Waves.WaveManager>(); if (wm != null) wm.enabled = false;
                    foreach (var zz in Object.FindObjectsByType<ZombieRush.Zombie.ZombieHealth>(FindObjectsSortMode.None)) Object.Destroy(zz.gameObject);
                    MoveToOpenSpot(switcher.transform, true);
                    switcher.SetView(CharacterView.ThirdPerson);
                }));
                foreach (bool crouch in new[] { false, true })
                {
                    string tag = crouch ? "crouched" : "standing";
                    p.Add((0.8f, () =>
                    {
                        if (ZombieRush.Core.GameInput.TouchCrouchActive != crouch) ZombieRush.Core.GameInput.TouchPress(ZombieRush.Core.TouchAction.Crouch);
                        ph.Revive();
                        hp0 = ph.CurrentHealth + ph.CurrentArmor; lastHp = hp0;
                        var pf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Zombie_Horror.prefab");
                        Vector3 f = Vector3.ProjectOnPlane(switcher.transform.forward, Vector3.up).normalized;
                        z = Object.Instantiate(pf, switcher.transform.position + f * 3f, Quaternion.LookRotation(-f, Vector3.up));
                        var rz = z.GetComponent<ZombieRush.Zombie.ZombieSpawnRise>(); if (rz != null) rz.enabled = false;
                        var ai = z.GetComponent<ZombieRush.Zombie.ZombieAI>(); if (ai != null) ai.alwaysHunt = true;
                    }));
                    for (int k = 0; k < 17; k++)
                    {
                        int kk = k;
                        p.Add((0.3f, () =>
                        {
                            float hpNow = ph.CurrentHealth + ph.CurrentArmor;
                            if (hpNow < lastHp - 0.5f)
                            {
                                var near = Object.FindObjectsByType<ZombieRush.Zombie.ZombieHealth>(FindObjectsSortMode.None)
                                    .Where(q => !q.IsDead && Vector3.Distance(q.transform.position, switcher.transform.position) < 4.5f)
                                    .Select(q => $"{q.name} {Vector3.Distance(q.transform.position, switcher.transform.position):0.0}m");
                                log.AppendLine($"    {tag} hit -{lastHp - hpNow:0} at step {kk}, crouching {switcher.GetComponent<PlayerController>().IsCrouching}, near: {string.Join(", ", near)}");
                            }
                            lastHp = hpNow;
                            if (kk == 12) { Directory.CreateDirectory(Folder); ScreenCapture.CaptureScreenshot($"{Folder}/Duck_{tag}.png"); }
                        }));
                    }
                    p.Add((0.5f, () =>
                    {
                        var cc = switcher.GetComponent<CharacterController>();
                        log.AppendLine($"  {tag}: capsule height {(cc != null ? cc.height : -1f):0.00}, crouching {switcher.GetComponent<PlayerController>().IsCrouching}, 5 s next to a giant: health+armour lost {hp0 - (ph.CurrentHealth + ph.CurrentArmor):0}");
                        if (z != null) Object.Destroy(z);
                        foreach (var t in Object.FindObjectsByType<ZombieRush.Zombie.ToxicZone>(FindObjectsSortMode.None)) Object.Destroy(t.gameObject);
                    }));
                }
                p.Add((0.3f, () => { if (ZombieRush.Core.GameInput.TouchCrouchActive) ZombieRush.Core.GameInput.TouchPress(ZombieRush.Core.TouchAction.Crouch); }));
                // turret: inside the base (centre, by a gate) and outside
                foreach (var (where, r) in new[] { ("base centre", 0f), ("in a gate", 26f), ("outside", 34f) })
                {
                    p.Add((0.6f, () =>
                    {
                        var sb = ZombieRush.World.SafeBase.Instance;
                        var cc = switcher.GetComponent<CharacterController>();
                        if (cc != null) cc.enabled = false;
                        switcher.transform.SetPositionAndRotation(sb.Center + Quaternion.Euler(0f, 14.5f, 0f) * Vector3.forward * r + Vector3.up * 0.2f,
                            Quaternion.Euler(0f, 14.5f, 0f));
                        if (cc != null) cc.enabled = true;
                    }));
                    p.Add((0.5f, () =>
                    {
                        var dep = ZombieRush.Weapons.TurretDeployer.Local;
                        int before = Object.FindObjectsByType<ZombieRush.Weapons.Turret>(FindObjectsSortMode.None).Length;
                        string got = "";
                        System.Action<string, string> on = (a, b) => got = a;
                        ZombieRush.Core.GameEvents.OnNotice += on;
                        try
                        {
                            if (dep != null)
                            {
                                var f = typeof(ZombieRush.Weapons.TurretDeployer).GetField("readyAt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                f?.SetValue(dep, 0f);
                                dep.Use();
                            }
                        }
                        finally { ZombieRush.Core.GameEvents.OnNotice -= on; }
                        int after = Object.FindObjectsByType<ZombieRush.Weapons.Turret>(FindObjectsSortMode.None).Length;
                        log.AppendLine($"  turret {where} ({r:0} m, base radius {ZombieRush.World.SafeBase.Instance.radius:0.0}): owned {dep?.Owned}, turrets {before} -> {after}, notice '{got}'");
                    }));
                }
                return p;
            }

            if (mode == "irons")
            {
                // 2026-11-16: iron-sight aim (pistol, shotgun): right click raises the gun to the eye, sights on the
                // middle of the screen; from first and third person; hip, aimed, and just after a shot while aimed.
                p.Add((0.6f, () =>
                {
                    var wm = Object.FindAnyObjectByType<ZombieRush.Waves.WaveManager>(); if (wm != null) wm.enabled = false;
                    foreach (var z in Object.FindObjectsByType<ZombieRush.Zombie.ZombieHealth>(FindObjectsSortMode.None)) Object.Destroy(z.gameObject);
                    var ph = switcher.GetComponent<PlayerHealth>(); if (ph != null) ph.enabled = false;
                    MoveToOpenSpot(switcher.transform, true);
                }));
                void IShot(string name)
                {
                    Directory.CreateDirectory(Folder);
                    ScreenCapture.CaptureScreenshot($"{Folder}/Irons_{name}.png");
                    log.AppendLine($"  {name}: fov {cam.fieldOfView:0.0}, vmZoom {switcher.ViewmodelZoom:0.00}, aim {switcher.AimAmount:0.00}, scoped {switcher.Scoped}, atEye {switcher.ScopedAtEye}, throughScope {switcher.ThroughScope}");
                }
                foreach (var style in new[] { ZombieRush.Weapons.GunStyle.Pistol, ZombieRush.Weapons.GunStyle.Shotgun })
                {
                    int gi = System.Array.FindIndex(weapons.weapons, w => w != null && w.data != null && w.data.ironSights && (w.Style == ZombieRush.Weapons.GunStyle.Shotgun) == (style == ZombieRush.Weapons.GunStyle.Shotgun));
                    if (gi < 0) continue;
                    foreach (var view in new[] { CharacterView.FirstPerson, CharacterView.ThirdPerson })
                    {
                        string v = (view == CharacterView.FirstPerson ? "FP_" : "TP_") + style;
                        p.Add((0.5f, () => switcher.SetView(view)));
                        p.Add((1.0f, () => { weapons.Unlock(weapons.weapons[gi].data); weapons.SelectWeapon(gi); weapons.RefillAmmo(); }));
                        p.Add((0.02f, () => IShot($"{v}_Hip")));
                        p.Add((0.8f, () => ZombieRush.Core.GameInput.TouchPress(ZombieRush.Core.TouchAction.Aim)));
                        p.Add((0.02f, () => IShot($"{v}_Aimed")));
                        p.Add((0.06f, () => ZombieRush.Core.GameInput.TouchPress(ZombieRush.Core.TouchAction.Fire)));
                        p.Add((0.02f, () => IShot($"{v}_AimedShot")));
                        p.Add((0.6f, () => ZombieRush.Core.GameInput.TouchRelease(ZombieRush.Core.TouchAction.Fire)));
                        p.Add((0.02f, () => IShot($"{v}_AimedSettled")));
                        p.Add((0.6f, () => ZombieRush.Core.GameInput.TouchPress(ZombieRush.Core.TouchAction.Aim)));   // aim off
                    }
                }
                return p;
            }

            if (mode == "scope")
            {
                // 2026-11-16: the realistic scope (gun showing below the sight), the view kick, and no firing in the base.
                var pc = switcher.GetComponent<PlayerController>();
                p.Add((0.3f, () =>
                {
                    var sb0 = ZombieRush.World.SafeBase.Instance;
                    var pos = startSpawn;
                    log.AppendLine($"  start: game put the player at {pos:F1} facing {startSpawnYaw:0}, PlayerStart {(sb0 != null && sb0.playerStart != null ? sb0.playerStart.position.ToString("F1") : "none")}, {(sb0 != null ? Vector3.ProjectOnPlane(pos - sb0.Center, Vector3.up).magnitude : -1f):0.0} m from base centre, safe radius {(sb0 != null ? sb0.radius : 0f):0.0}");
                    // back to that spot, first person, for a look
                    switcher.SetView(CharacterView.FirstPerson);
                    var cc0 = switcher.GetComponent<CharacterController>();
                    if (cc0 != null) cc0.enabled = false;
                    switcher.transform.SetPositionAndRotation(startSpawn, Quaternion.Euler(0f, startSpawnYaw, 0f));
                    if (cc0 != null) cc0.enabled = true;
                }));
                p.Add((0.3f, () => { Directory.CreateDirectory(Folder); ScreenCapture.CaptureScreenshot($"{Folder}/Scope_Start.png"); }));
                p.Add((0.6f, () =>
                {
                    var wm = Object.FindAnyObjectByType<ZombieRush.Waves.WaveManager>(); if (wm != null) wm.enabled = false;
                    foreach (var z in Object.FindObjectsByType<ZombieRush.Zombie.ZombieHealth>(FindObjectsSortMode.None)) Object.Destroy(z.gameObject);
                    var ph = switcher.GetComponent<PlayerHealth>(); if (ph != null) ph.enabled = false;
                    MoveToOpenSpot(switcher.transform, true);
                }));
                void Shot(string name, string note)
                {
                    Directory.CreateDirectory(Folder);
                    ScreenCapture.CaptureScreenshot($"{Folder}/Scope_{name}.png");
                    float kick = pc != null && pc.cameraPivot != null ? Mathf.DeltaAngle(pc.cameraPivot.localEulerAngles.x, pc.Pitch) : 0f;
                    var w = weapons.CurrentWeapon;
                    log.AppendLine($"  {name}: {note}; view kick {kick:0.00} deg up, mag {(w != null ? w.CurrentMagazine : -1)}, fov {cam.fieldOfView:0.0}, vmZoom {switcher.ViewmodelZoom:0.00}, scoped {switcher.Scoped}, atEye {switcher.ScopedAtEye}");
                }
                int rifle = System.Array.FindIndex(weapons.weapons, w => w != null && w.data != null && w.data.hasScope);
                int shotgun = System.Array.FindIndex(weapons.weapons, w => w != null && w.Style == ZombieRush.Weapons.GunStyle.Shotgun);
                foreach (var view in new[] { CharacterView.FirstPerson, CharacterView.ThirdPerson })
                {
                    string v = view == CharacterView.FirstPerson ? "FP" : "TP";
                    p.Add((0.5f, () => switcher.SetView(view)));
                    if (rifle >= 0)
                    {
                        // each step: the action, then the wait
                        p.Add((0.9f, () => { weapons.Unlock(weapons.weapons[rifle].data); weapons.SelectWeapon(rifle); weapons.RefillAmmo(); }));
                        p.Add((0.7f, () => ZombieRush.Core.GameInput.TouchPress(ZombieRush.Core.TouchAction.Aim)));
                        p.Add((0.02f, () => Shot($"{v}_Rifle_Scoped", "scoped, still")));
                        p.Add((0.3f, () => ZombieRush.Core.GameInput.TouchPress(ZombieRush.Core.TouchAction.Fire)));
                        p.Add((0.02f, () => Shot($"{v}_Rifle_ScopedBurst", "scoped, 0.3 s into a burst")));
                        p.Add((0.6f, () => ZombieRush.Core.GameInput.TouchRelease(ZombieRush.Core.TouchAction.Fire)));
                        p.Add((0.02f, () => Shot($"{v}_Rifle_ScopedSettled", "scoped, 0.6 s after the burst")));
                        p.Add((0.5f, () => ZombieRush.Core.GameInput.TouchPress(ZombieRush.Core.TouchAction.Aim)));   // aim off
                        p.Add((0.25f, () => ZombieRush.Core.GameInput.TouchPress(ZombieRush.Core.TouchAction.Fire)));
                        p.Add((0.02f, () => Shot($"{v}_Rifle_HipBurst", "hip, 0.25 s into a burst")));
                        p.Add((0.3f, () => ZombieRush.Core.GameInput.TouchRelease(ZombieRush.Core.TouchAction.Fire)));
                    }
                    if (shotgun >= 0)
                    {
                        p.Add((0.9f, () => { weapons.Unlock(weapons.weapons[shotgun].data); weapons.SelectWeapon(shotgun); weapons.RefillAmmo(); }));
                        p.Add((0.07f, () => ZombieRush.Core.GameInput.TouchPress(ZombieRush.Core.TouchAction.Fire)));
                        p.Add((0.02f, () => Shot($"{v}_Shotgun_Kick", "hip, 0.07 s after the shot")));
                        p.Add((0.8f, () => ZombieRush.Core.GameInput.TouchRelease(ZombieRush.Core.TouchAction.Fire)));
                        p.Add((0.02f, () => Shot($"{v}_Shotgun_Settled", "0.8 s after")));
                    }
                }
                // no firing inside the safe base
                p.Add((0.8f, () =>
                {
                    switcher.SetView(CharacterView.FirstPerson);
                    var sb = ZombieRush.World.SafeBase.Instance;
                    if (sb == null) { log.AppendLine("  base: no SafeBase"); return; }
                    var cc = switcher.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;
                    switcher.transform.position = sb.Center + Vector3.up * 0.3f;
                    if (cc != null) cc.enabled = true;
                    weapons.SelectWeapon(0); weapons.RefillAmmo();
                }));
                int magBefore = 0;
                p.Add((0.6f, () =>
                {
                    var sb = ZombieRush.World.SafeBase.Instance;
                    magBefore = weapons.CurrentWeapon.CurrentMagazine;
                    log.AppendLine($"  base: ring active {(sb != null && sb.ring != null && sb.ring.gameObject.activeInHierarchy)}, inside {(sb != null && sb.Contains(switcher.transform.position))}, gun {weapons.CurrentWeapon.name}, mag before {magBefore}");
                    ZombieRush.Core.GameInput.TouchPress(ZombieRush.Core.TouchAction.Fire);
                }));
                p.Add((0.02f, () => { Shot("Base_NoFire", "inside the base, trigger held 0.6 s"); log.AppendLine($"  base: shots fired {magBefore - weapons.CurrentWeapon.CurrentMagazine}"); }));
                p.Add((0.1f, () => ZombieRush.Core.GameInput.TouchRelease(ZombieRush.Core.TouchAction.Fire)));
                // at the wall: just inside it, and standing in a gate opening (both inside the base: no firing);
                // then one step outside (firing again). Facing out, a shot of the wall.
                foreach (var (tag, r, ang) in new[] { ("WallInside", 24.6f, 60f), ("Gate", 25.9f, 14.5f), ("Outside", 28.5f, 14.5f) })
                {
                    p.Add((0.5f, () =>
                    {
                        var sb2 = ZombieRush.World.SafeBase.Instance;
                        var dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
                        var at = sb2.Center + dir * r;
                        var cc = switcher.GetComponent<CharacterController>();
                        if (cc != null) cc.enabled = false;
                        switcher.transform.SetPositionAndRotation(at + Vector3.up * 0.1f, Quaternion.LookRotation(tag == "WallInside" ? dir : -dir));
                        if (cc != null) cc.enabled = true;
                        weapons.RefillAmmo();
                    }));
                    p.Add((0.6f, () => { magBefore = weapons.CurrentWeapon.CurrentMagazine; ZombieRush.Core.GameInput.TouchPress(ZombieRush.Core.TouchAction.Fire); }));
                    p.Add((0.02f, () => { Shot($"Edge_{tag}", "trigger held 0.6 s"); log.AppendLine($"  {tag}: {Vector3.ProjectOnPlane(switcher.transform.position - ZombieRush.World.SafeBase.Instance.Center, Vector3.up).magnitude:0.0} m from centre, inside base {ZombieRush.World.SafeBase.Instance.Contains(switcher.transform.position)}, shots fired {magBefore - weapons.CurrentWeapon.CurrentMagazine}"); }));
                    p.Add((0.3f, () => ZombieRush.Core.GameInput.TouchRelease(ZombieRush.Core.TouchAction.Fire)));
                }
                return p;
            }

            if (mode == "aim")
            {
                // Crosshairs and scope, as the player sees them (Game view, HUD included): each gun in first and
                // third person: standing, running (the spread opens), after a burst (it blooms), and aimed
                // (zoom, or the rifle's scope). No zombies, waves stopped, player can't be hurt.
                p.Add((0.6f, () =>
                {
                    var wm = Object.FindAnyObjectByType<ZombieRush.Waves.WaveManager>(); if (wm != null) wm.enabled = false;
                    foreach (var z in Object.FindObjectsByType<ZombieRush.Zombie.ZombieHealth>(FindObjectsSortMode.None)) Object.Destroy(z.gameObject);
                    var ph = switcher.GetComponent<PlayerHealth>(); if (ph != null) ph.enabled = false;
                    MoveToOpenSpot(switcher.transform, true);
                }));
                void Game(string name, string note)
                {
                    Directory.CreateDirectory(Folder);
                    ScreenCapture.CaptureScreenshot($"{Folder}/Aim_{name}.png");
                    var w = weapons.CurrentWeapon;
                    log.AppendLine($"  {name}: {note}; spread {(w != null ? w.SpreadDegrees : 0f):0.00} deg, fov {cam.fieldOfView:0.0}, aim {switcher.AimAmount:0.00}, scoped {switcher.Scoped}");
                }
                foreach (var view in new[] { CharacterView.FirstPerson, CharacterView.ThirdPerson })
                {
                    string v = view == CharacterView.FirstPerson ? "FP" : "TP";
                    p.Add((0.5f, () => switcher.SetView(view)));
                    for (int i = 0; i < weapons.weapons.Length; i++)
                    {
                        int idx = i;
                        string g = weapons.weapons[idx].name;
                        p.Add((0.9f, () => { weapons.Unlock(weapons.weapons[idx].data); weapons.SelectWeapon(idx); weapons.RefillAmmo(); }));
                        p.Add((0.3f, () => Game($"{v}_{g}_Stand", "standing")));
                        p.Add((0.02f, () => ZombieRush.Core.GameInput.SetTouchMove(new Vector2(0f, 0.75f))));
                        p.Add((0.6f, () => Game($"{v}_{g}_Run", "running")));
                        p.Add((0.02f, () => { ZombieRush.Core.GameInput.SetTouchMove(Vector2.zero); ZombieRush.Core.GameInput.TouchPress(ZombieRush.Core.TouchAction.Fire); }));
                        p.Add((0.45f, () => Game($"{v}_{g}_Burst", "firing")));
                        p.Add((0.02f, () => ZombieRush.Core.GameInput.TouchRelease(ZombieRush.Core.TouchAction.Fire)));
                        p.Add((0.6f, () => { weapons.RefillAmmo(); ZombieRush.Core.GameInput.TouchPress(ZombieRush.Core.TouchAction.Aim); }));   // aim on (touch toggle)
                        p.Add((0.5f, () => Game($"{v}_{g}_Aim", "aimed")));
                        p.Add((0.02f, () => ZombieRush.Core.GameInput.TouchPress(ZombieRush.Core.TouchAction.Aim)));   // aim off
                        p.Add((0.4f, () => { }));
                    }
                }
                return p;
            }

            ZombieRush.Player.FirstPersonViewmodel.DebugOff = mode == "fpgunsmain";   // test: no overlay camera
            if (mode == "fpgunsmain") mode = "fpguns";
            if (mode == "fpguns")
            {
                // First person, each gun: held, a shot, and a reload in five frames, as the player sees them.
                // No zombies: the waves are stopped and the ones out there removed, so nothing interrupts.
                p.Add((0.6f, () =>
                {
                    switcher.SetView(CharacterView.FirstPerson);
                    var wm = Object.FindAnyObjectByType<ZombieRush.Waves.WaveManager>(); if (wm != null) wm.enabled = false;
                    foreach (var z in Object.FindObjectsByType<ZombieRush.Zombie.ZombieHealth>(FindObjectsSortMode.None)) Object.Destroy(z.gameObject);
                    var ph = switcher.GetComponent<PlayerHealth>(); if (ph != null) ph.enabled = false;
                }));
                int n = weapons.weapons.Length;
                for (int i = 0; i < n; i++)
                {
                    int idx = i;
                    string g = weapons.weapons[idx].name;
                    p.Add((0.9f, () =>
                    {
                        int before = weapons.CurrentIndex;
                        weapons.Unlock(weapons.weapons[idx].data); weapons.SelectWeapon(idx); weapons.RefillAmmo();
                        log.AppendLine($"  select {g} (slot {idx}): was slot {before}, now slot {weapons.CurrentIndex} ({weapons.CurrentWeapon?.name}), unlocked {weapons.IsUnlocked(weapons.weapons[idx].data)}, controllers {Object.FindObjectsByType<PlayerWeaponController>(FindObjectsSortMode.None).Length}");
                    }));
                    p.Add((0.05f, () =>
                    {
                        log.AppendLine($"  {g} before shot: slot {weapons.CurrentIndex} ({weapons.CurrentWeapon?.name}), active guns: " +
                                       string.Join(",", System.Linq.Enumerable.Select(System.Linq.Enumerable.Where(weapons.weapons, w => w != null && w.gameObject.activeInHierarchy), w => w.name)));
                        Shot(cam, $"FPG_{g}_Hold");
                        // the real Game view as well: it shows every camera the way the player sees it (the overlay included)
                        ScreenCapture.CaptureScreenshot($"{Folder}/FPG_{g}_Hold_Game.png");
                        var ar = switcher.GetComponentInChildren<WeaponAimRig>(); log.AppendLine($"  {g} hold: {(ar != null ? ar.Describe() : "")}");
                        log.AppendLine("  " + g + " viewmodel: " + ViewmodelReport(switcher, cam));
                        var vm = weapons.CurrentWeapon.GetComponent<ZombieRush.Weapons.WeaponViewModel>();
                        if (vm != null && vm.model != null)
                            foreach (var r in vm.model.GetComponentsInChildren<Renderer>(true))
                                log.AppendLine($"    renderer {r.name}: active {r.gameObject.activeInHierarchy}, enabled {r.enabled}, layer {LayerMask.LayerToName(r.gameObject.layer)}, " +
                                               $"cam culls {(cam.cullingMask & (1 << r.gameObject.layer)) != 0}, in camera {cam.transform.InverseTransformPoint(r.bounds.center).ToString("F2")}, size {r.bounds.size.ToString("F2")}, scale {r.transform.lossyScale.ToString("F3")}");
                    }));
                    p.Add((0.02f, () => ZombieRush.Core.GameInput.TouchPress(ZombieRush.Core.TouchAction.Fire)));
                    p.Add((0.06f, () => Shot(cam, $"FPG_{g}_Fire")));
                    p.Add((0.3f, () => ZombieRush.Core.GameInput.TouchRelease(ZombieRush.Core.TouchAction.Fire)));
                    p.Add((0.3f, () => weapons.CurrentWeapon.TryReload()));
                    float rt = Mathf.Max(0.6f, weapons.weapons[idx].data.reloadTime);
                    for (int k = 0; k < 5; k++) { int f = k; p.Add((rt / 5.5f, () => { Shot(cam, $"FPG_{g}_Reload{f}"); var ar = switcher.GetComponentInChildren<WeaponAimRig>(); log.AppendLine($"  {g} reload {f}: progress {weapons.CurrentWeapon.ReloadProgress:0.00}, {(ar != null ? ar.Describe() : "")}"); })); }
                    p.Add((0.5f, () => { }));
                }
                return p;
            }

            if (mode == "poison")
            {
                // The poison zombie for real: a Ghoul 11 m away with its AI on spits at the player.
                // Wide side shots of the glob, the puddle and later the death cloud, with the player's health.
                GameObject z = null;
                var ph = switcher.GetComponent<PlayerHealth>();
                Vector3 mid = Vector3.zero, side = Vector3.right;
                void Wide(string tag)
                {
                    var go = new GameObject("CaptureCam");
                    try
                    {
                        var cam = go.AddComponent<Camera>();
                        cam.fieldOfView = 50f;
                        go.transform.position = mid + side * 11f + Vector3.up * 3.5f;
                        go.transform.LookAt(mid + Vector3.up * 0.8f);
                        Shot(cam, $"Poison_{tag}");
                    }
                    finally { Object.DestroyImmediate(go); }
                    var a = z != null ? z.GetComponentInChildren<Animator>() : null;
                    var ci = a != null ? a.GetCurrentAnimatorClipInfo(0) : null;
                    log.AppendLine($"  {tag}: player hp {(ph != null ? ph.CurrentHealth : -1f):0}, ghoul {(ci != null && ci.Length > 0 ? ci[0].clip.name : "-")}, " +
                                   $"globs {Object.FindObjectsByType<ZombieRush.Zombie.AcidGlob>(FindObjectsSortMode.None).Length}, " +
                                   $"zones {string.Join(" ", Object.FindObjectsByType<ZombieRush.Zombie.ToxicZone>(FindObjectsSortMode.None).Select(t => t.name))}");
                }
                p.Add((0.5f, () =>
                {
                    var pf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Zombie_Ghoul.prefab");
                    Vector3 f = Vector3.ProjectOnPlane(switcher.transform.forward, Vector3.up).normalized;
                    z = Object.Instantiate(pf, switcher.transform.position + f * 11f, Quaternion.LookRotation(-f, Vector3.up));
                    var rz = z.GetComponent<ZombieRush.Zombie.ZombieSpawnRise>(); if (rz != null) rz.enabled = false;
                    var ai = z.GetComponent<ZombieRush.Zombie.ZombieAI>(); if (ai != null) ai.alwaysHunt = true;
                    mid = switcher.transform.position + f * 5.5f;
                    side = Vector3.Cross(Vector3.up, f).normalized;
                }));
                for (int k = 0; k < 20; k++) { int kk = k; p.Add((0.3f, () => Wide($"Spit_{kk:00}"))); }
                p.Add((0.3f, () => { if (z != null) z.GetComponent<ZombieRush.Zombie.ZombieHealth>().Kill(false); }));
                for (int k = 0; k < 8; k++) { int kk = k; p.Add((0.4f, () => Wide($"Death_{kk:00}"))); }
                p.Add((0.1f, () => { if (z != null) Object.Destroy(z); }));
                return p;
            }

            if (mode.StartsWith("anims"))
            {
                // Each animation as a sequence: a held zombie 3 m in front, frames over the clip.
                string zname = mode.Contains(":") ? mode.Substring(mode.IndexOf(':') + 1) : "Wanderer";
                GameObject z = null;
                p.Add((0.5f, () =>
                {
                    var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Zombie_{zname}.prefab");
                    Vector3 f = Vector3.ProjectOnPlane(switcher.transform.forward, Vector3.up).normalized;
                    z = Object.Instantiate(pf, switcher.transform.position + f * 4f, Quaternion.LookRotation(-f, Vector3.up));
                    var rz = z.GetComponent<ZombieRush.Zombie.ZombieSpawnRise>(); if (rz != null) rz.enabled = false;
                    var ai = z.GetComponent<ZombieRush.Zombie.ZombieAI>(); if (ai != null) ai.enabled = false;
                    var cc = z.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
                    switcher.transform.position -= f * 3f; // player out of the shot
                }));
                var seq = new (string label, System.Action<ZombieRush.Zombie.ZombieHealth> act, int frames, float step)[]
                {
                    ("Idle", h => h.Anim.SetSpeed(0f), 3, 0.6f),
                    ("Walk", h => h.Anim.SetSpeed(0.5f), 5, 0.22f),
                    ("Run", h => h.Anim.SetSpeed(1f), 5, 0.15f),
                    ("Attack", h => { h.Anim.SetSpeed(0f); h.Anim.TriggerAttack(); }, 7, 0.2f),
                    ("Spit", h => { h.Anim.SetSpeed(0f); h.Anim.TriggerSpit(); }, 7, 0.24f),
                    ("Hit", h => h.Anim.TriggerHit(), 6, 0.08f),
                    ("Die", h => h.Anim.TriggerDie(), 9, 0.32f),
                };
                foreach (var (label, act, frames, step) in seq)
                {
                    bool oneShot = label == "Attack" || label == "Spit" || label == "Hit" || label == "Die";
                    if (oneShot) p.Add((0.8f, () => z.GetComponent<ZombieRush.Zombie.ZombieHealth>().Anim.SetSpeed(0f))); // settle in idle first
                    p.Add((label == "Idle" ? 0.8f : oneShot ? 0.05f : 0.6f, () => act(z.GetComponent<ZombieRush.Zombie.ZombieHealth>())));
                    for (int k = 0; k < frames; k++)
                    {
                        int f = k;
                        p.Add((step, () =>
                        {
                            Side(z.transform, $"Anim_{zname}_{label}_{f}_Side", 90f);
                            Side(z.transform, $"Anim_{zname}_{label}_{f}_Front", 15f);
                            var a = z.GetComponentInChildren<Animator>();
                            var ci = a.GetCurrentAnimatorClipInfo(0);
                            log.AppendLine($"  {label} {f}: {(ci.Length > 0 ? ci[0].clip.name : "-")} t={a.GetCurrentAnimatorStateInfo(0).normalizedTime:0.00}");
                        }));
                    }
                }
                p.Add((0.1f, () => Object.Destroy(z)));
                return p;
            }

            if (mode == "yaku" || mode.StartsWith("zombie"))
            {
                // A real Yaku walks at the player (foot slide measured), then a held one shows each animation.
                GameObject z = null;
                GameObject Fresh(float dist)
                {
                    var pf = AssetDatabase.LoadAssetAtPath<GameObject>(mode == "yaku" ? "Assets/_Archive/OldZombies/Prefabs/Zombie_Yaku.prefab"
                        : $"Assets/Prefabs/Zombie_{(mode.Contains(":") ? mode.Substring(mode.IndexOf(':') + 1) : "Wanderer")}.prefab");
                    Vector3 f = Vector3.ProjectOnPlane(switcher.transform.forward, Vector3.up).normalized;
                    var go = Object.Instantiate(pf, switcher.transform.position + f * dist, Quaternion.LookRotation(-f, Vector3.up));
                    go.name = "Zombie (CAPTURE)";
                    var rz = go.GetComponent<ZombieRush.Zombie.ZombieSpawnRise>();
                    if (rz != null) rz.enabled = false;
                    return go;
                }
                p.Add((0.5f, () =>
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(mode == "yaku" ? "Assets/_Archive/OldZombies/Prefabs/Zombie_Yaku.prefab"
                        : $"Assets/Prefabs/Zombie_{(mode.Contains(":") ? mode.Substring(mode.IndexOf(':') + 1) : "Wanderer")}.prefab");
                    Vector3 fwd = Vector3.ProjectOnPlane(switcher.transform.forward, Vector3.up).normalized;
                    Vector3 pos = switcher.transform.position + fwd * 18f; // beyond run distance: it walks first
                    if (Physics.Raycast(pos + Vector3.up * 3f, Vector3.down, out var hit, 10f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point;
                    z = Object.Instantiate(prefab, pos, Quaternion.LookRotation(-fwd, Vector3.up));
                    z.name = "Zombie_Yaku (CAPTURE)";
                    var rise = z.GetComponent<ZombieRush.Zombie.ZombieSpawnRise>();
                    if (rise != null) rise.enabled = false;
                }));
                p.Add((0.6f, () => StartSlide(z.transform)));
                for (int k = 0; k < 4; k++)
                {
                    int f = k;
                    p.Add((0.3f, () => { if (z != null) { Side(z.transform, $"Yaku_Approach_{f}", 90f); LogZombie(z, $"Yaku_Approach_{f}"); } }));
                }
                p.Add((0.1f, () => StopSlide("Yaku_Approach")));
                // Held in place on the open ground 3 m in front of the player: rest pose first
                // (animator off = exactly what the model file holds), then each animation.
                p.Add((0.8f, () =>
                {
                    if (z == null) z = Fresh(3f); // the first one reached the player and was killed
                    var ai = z.GetComponent<ZombieRush.Zombie.ZombieAI>();
                    if (ai != null) ai.enabled = false;
                    var cc = z.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;
                    Vector3 fwd = Vector3.ProjectOnPlane(switcher.transform.forward, Vector3.up).normalized;
                    z.transform.SetPositionAndRotation(switcher.transform.position + fwd * 3f, Quaternion.LookRotation(-fwd, Vector3.up));
                    switcher.transform.position -= fwd * 2f; // player out of the shot
                    z.GetComponentInChildren<Animator>().enabled = false;
                }));
                p.Add((0.3f, () => { Side(z.transform, "Yaku_RestPose_Front", 0f); Side(z.transform, "Yaku_RestPose_Side", 90f); }));
                p.Add((0.3f, () =>
                {
                    z.GetComponentInChildren<Animator>().enabled = true;
                    z.GetComponent<ZombieRush.Zombie.ZombieHealth>().Anim.SetSpeed(0f);
                }));
                foreach (var (label, act, wait) in new (string, System.Action<ZombieRush.Zombie.ZombieHealth>, float)[]
                {
                    ("Idle", h => h.Anim.SetSpeed(0f), 1.0f), ("Walk", h => h.Anim.SetSpeed(0.5f), 1.0f), ("Run", h => h.Anim.SetSpeed(1f), 1.0f),
                    ("Attack", h => { h.Anim.SetSpeed(0f); h.Anim.TriggerAttack(); }, 0.6f), ("Hit", h => h.Anim.TriggerHit(), 0.3f),
                    ("Die", h => h.Anim.TriggerDie(), 1.6f),
                })
                {
                    p.Add((wait, () => act(z.GetComponent<ZombieRush.Zombie.ZombieHealth>())));
                    p.Add((0.2f, () => { Side(z.transform, $"Yaku_{label}", 50f); Side(z.transform, $"Yaku_{label}_Front", 0f); LogZombie(z, $"Yaku_{label}"); }));
                }
                p.Add((0.1f, () => Object.Destroy(z)));
                return p;
            }

            if (mode == "runlook")
            {
                // The legs while running, as the player sees them: 8 third-person frames across one stride, then
                // 6 from the side, for the run (joystick 75%) and the sprint (full forward auto-sprints). No zombies.
                int rifle = System.Array.FindIndex(weapons.weapons, w => w != null && w.name.Contains("Rifle"));
                if (rifle < 0) rifle = weapons.weapons.Length - 1;
                p.Add((0.6f, () =>
                {
                    var wm = Object.FindAnyObjectByType<ZombieRush.Waves.WaveManager>(); if (wm != null) wm.enabled = false;
                    foreach (var z in Object.FindObjectsByType<ZombieRush.Zombie.ZombieHealth>(FindObjectsSortMode.None)) Object.Destroy(z.gameObject);
                    var ph = switcher.GetComponent<PlayerHealth>(); if (ph != null) ph.enabled = false;
                    weapons.Unlock(weapons.weapons[rifle].data); weapons.SelectWeapon(rifle);
                }));
                foreach (var (label, dir) in new[] { ("Run", new Vector2(0f, 0.75f)), ("Sprint", new Vector2(0f, 1f)) })
                {
                    p.Add((0.3f, () => { MoveToOpenSpot(switcher.transform, true); ZombieRush.Core.GameInput.SetTouchMove(dir); }));
                    p.Add((1.2f, () => { }));
                    for (int k = 0; k < 8; k++) { int f = k; p.Add((0.085f, () => { Shot(cam, $"RunLook_{label}_TP{f}"); if (f == 0) LogBody(switcher.transform, $"RunLook_{label}"); })); }
                    for (int k = 0; k < 6; k++) { int f = k; p.Add((0.11f, () => Side(switcher.transform, $"RunLook_{label}_Side{f}", 90f))); }
                    p.Add((0.3f, () => ZombieRush.Core.GameInput.SetTouchMove(Vector2.zero)));
                }
                return p;
            }

            if (mode == "strafe")
            {
                // A, then D, held for 3 s each: does the body keep facing forward the whole time?
                int rifle = System.Array.FindIndex(weapons.weapons, w => w != null && w.name.Contains("Rifle"));
                if (rifle < 0) rifle = weapons.weapons.Length - 1;
                p.Add((0.7f, () => { weapons.Unlock(weapons.weapons[rifle].data); weapons.SelectWeapon(rifle); }));
                foreach (var (label, dir) in new[] { ("A", new Vector2(-1f, 0f)), ("D", new Vector2(1f, 0f)) })
                {
                    p.Add((0.2f, () => { MoveToOpenSpot(switcher.transform); ZombieRush.Core.GameInput.SetTouchMove(dir); }));
                    foreach (float t in new[] { 0.3f, 0.7f, 1.0f, 1.0f })
                    {
                        float wait = t;
                        p.Add((wait, () => { string n = $"Play_Strafe{label}_{Time.time:0.0}s"; Shot(cam, n); LogBody(switcher.transform, n); }));
                    }
                    p.Add((0.3f, () => ZombieRush.Core.GameInput.SetTouchMove(Vector2.zero)));
                }
                return p;
            }

            if (mode == "firemove")
            {
                // Rifle, then move in each direction with and without the trigger held; four quick
                // frames of each from the side to see the legs.
                int rifle = System.Array.FindIndex(weapons.weapons, w => w != null && w.name.Contains("Rifle"));
                if (rifle < 0) rifle = weapons.weapons.Length - 1;
                p.Add((0.7f, () => { weapons.Unlock(weapons.weapons[rifle].data); weapons.SelectWeapon(rifle); }));
                var moves = new[] { ("Fwd", new Vector2(0f, 1f)), ("Right", new Vector2(1f, 0f)), ("DiagFR", new Vector2(0.7f, 0.7f)), ("Back", new Vector2(0f, -1f)) };
                foreach (bool fire in new[] { false, true })
                    foreach (var (label, dir) in moves)
                    {
                        string name = $"Play_{(fire ? "Fire" : "Move")}_{label}";
                        p.Add((0.4f, () =>
                        {
                            MoveToOpenSpot(switcher.transform);
                            weapons.RefillAmmo();
                            ZombieRush.Core.GameInput.SetTouchMove(dir);
                            if (fire) ZombieRush.Core.GameInput.TouchPress(ZombieRush.Core.TouchAction.Fire);
                        }));
                        p.Add((0.2f, () => StartSlide(switcher.transform)));
                        for (int k = 0; k < 4; k++)
                        {
                            int f = k;
                            p.Add((0.12f, () => { Shot(cam, $"{name}_{f}"); LogBody(switcher.transform, $"{name}_{f}"); }));
                        }
                        p.Add((0.4f, () =>
                        {
                            StopSlide(name);
                            ZombieRush.Core.GameInput.TouchRelease(ZombieRush.Core.TouchAction.Fire);
                            ZombieRush.Core.GameInput.SetTouchMove(Vector2.zero);
                        }));
                    }
                return p;
            }

            p.Add((0.7f, () => Shot(cam, "Play_TPP_level")));
            p.Add((0.3f, () => SetPitch(player, -25f)));
            p.Add((0.7f, () => Shot(cam, "Play_TPP_up")));
            p.Add((0.3f, () => SetPitch(player, 30f)));
            p.Add((0.7f, () => Shot(cam, "Play_TPP_down")));
            p.Add((0.3f, () => SetPitch(player, 0f)));
            for (int i = weapons.weapons.Length - 1; i >= 0; i--) // rifle first
            {
                int idx = i;
                p.Add((0.7f, () => { weapons.Unlock(weapons.weapons[idx].data); weapons.SelectWeapon(idx); }));
                p.Add((0.7f, () => Shot(cam, "Play_TPP_gun" + idx)));
                p.Add((0.7f, () => Side(switcher.transform, "Play_Side_gun" + idx, 90f)));
                p.Add((0.7f, () => Side(switcher.transform, "Play_Front_gun" + idx, 35f)));
            }
            return p;
        }

        /// Moves the player to the nearby spot with the most free space around it, so the close-up
        /// cameras are not inside a wall.
        static Vector3 startPos;
        static bool haveStart;

        static void MoveToOpenSpot(Transform player) => MoveToOpenSpot(player, false);

        /// outsideBase: only spots well outside the safe base (zombies never go into the base).
        static void MoveToOpenSpot(Transform player, bool outsideBase)
        {
            if (!haveStart) { startPos = player.position; haveStart = true; }
            Vector3 centre = startPos; // the player's spawn, in the middle of the play area
            Vector3 best = player.position;
            float bestFree = -1f;
            int rings = outsideBase ? 20 : 8;
            for (int ring = 0; ring <= rings; ring++)
                for (int a = 0; a < 24; a++)
                {
                    Vector3 p = centre + Quaternion.Euler(0f, a * 15f, 0f) * Vector3.forward * (ring * 4f);
                    if (!Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var ground, 60f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (Mathf.Abs(ground.point.y - centre.y) > 1.5f || ground.normal.y < 0.97f) continue; // flat street level
                    if (outsideBase && ZombieRush.World.SafeBase.Instance != null && ZombieRush.World.SafeBase.Instance.Contains(ground.point, 8f)) continue;
                    float free = 0f;
                    for (float r = 0.5f; r <= 8f; r += 0.5f)
                    {
                        if (Physics.CheckSphere(ground.point + Vector3.up * (r + 0.25f), r, ~0, QueryTriggerInteraction.Ignore)) break;
                        free = r;
                    }
                    if (free > bestFree) { bestFree = free; best = ground.point; }
                }
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.position = best + Vector3.up * 0.05f;
            if (cc != null) cc.enabled = true;
            log.AppendLine($"Moved to open spot {best} ({bestFree:0.0} m clear).");
        }

        // ---- Performance: frame time and what the camera drew, over a few seconds of play ----
        static float perfStart; static int perfFrames, perfLast; static double perfTris, perfSkins, perfBatches; static int perfSamples;

        static void StartPerf()
        {
            perfStart = Time.realtimeSinceStartup; perfFrames = 0; perfLast = Time.frameCount;
            perfTris = perfSkins = perfBatches = 0; perfSamples = 0;
            EditorApplication.update += SamplePerf;
        }

        static void SamplePerf()
        {
            if (Time.frameCount == perfLast) return;
            perfFrames += Time.frameCount - perfLast; perfLast = Time.frameCount;
            perfTris += UnityStats.triangles; perfSkins += UnityStats.visibleSkinnedMeshes; perfBatches += UnityStats.drawCalls; perfSamples++;
        }

        static void StopPerf(string label)
        {
            EditorApplication.update -= SamplePerf;
            float secs = Time.realtimeSinceStartup - perfStart;
            int n = Mathf.Max(1, perfSamples);
            log.AppendLine($"  {label}: {perfFrames / Mathf.Max(0.01f, secs):0} fps ({secs * 1000f / Mathf.Max(1, perfFrames):0.0} ms/frame), " +
                           $"{perfTris / n / 1000:0}k triangles, {perfSkins / n:0} skinned meshes, {perfBatches / n:0} draw calls per frame.");
        }

        // ---- Foot slide: how fast the planted foot moves over the ground (0 = perfect) ----
        static bool sampling;
        static int lastFrame;
        static float lastTime;
        static Vector3 lastL, lastR;
        static float slideSum, bodySpeedSum; static int slideCount, ticks;
        static float lowFoot = 99f;
        static Animator sampleAnim;
        static CharacterController sampleCC;

        static void StartSlide(Transform player)
        {
            var cba = player.GetComponentInChildren<CharacterBodyAnimator>();
            sampleAnim = cba != null ? cba.Animator : player.GetComponentInChildren<Animator>();
            sampleCC = player.GetComponent<CharacterController>();
            slideSum = bodySpeedSum = 0f; slideCount = 0; ticks = 0; lowFoot = 99f; lastFrame = -1;
            sampling = sampleAnim != null;
            if (sampling) EditorApplication.update += SampleSlide;
        }

        static void SampleSlide()
        {
            if (!sampling || !EditorApplication.isPlaying || sampleAnim == null) return;
            if (Time.frameCount == lastFrame) return;
            ticks++;
            var l = sampleAnim.GetBoneTransform(HumanBodyBones.LeftFoot).position;
            var r = sampleAnim.GetBoneTransform(HumanBodyBones.RightFoot).position;
            if (lastFrame >= 0)
            {
                bool left = l.y <= r.y;
                Vector3 d = left ? l - lastL : r - lastR; d.y = 0f;
                float ground = sampleCC != null ? sampleCC.transform.position.y : 0f;
                float dt = Time.time - lastTime;
                lowFoot = Mathf.Min(lowFoot, Mathf.Min(l.y, r.y) - ground);
                if (dt > 0f && (left ? l.y : r.y) < ground + 0.15f) // the planted foot
                {
                    slideSum += d.magnitude / dt;
                    bodySpeedSum += sampleCC != null ? new Vector3(sampleCC.velocity.x, 0f, sampleCC.velocity.z).magnitude : 0f;
                    slideCount++;
                }
            }
            lastL = l; lastR = r; lastFrame = Time.frameCount; lastTime = Time.time;
        }

        static void StopSlide(string name)
        {
            sampling = false;
            EditorApplication.update -= SampleSlide;
            log.AppendLine(slideCount > 0
                ? $"  {name}: planted foot slides {slideSum / slideCount:0.00} m/s while the body moves {bodySpeedSum / slideCount:0.00} m/s ({slideCount} of {ticks} frames)."
                : $"  {name}: no planted-foot frames ({ticks} frames sampled, lowest foot {lowFoot:0.00} m above the ground, animator {(sampleAnim != null ? "found" : "missing")}).");
        }

        static void SetPitch(PlayerController player, float pitch)
        {
            var f = typeof(PlayerController).GetField("pitch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            f.SetValue(player, pitch);
            player.cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        /// Notes what the body animator is doing (state, inputs), for the report.
        static void LogBody(Transform player, string name)
        {
            var body = player.GetComponentInChildren<CharacterBodyAnimator>();
            var cc = player.GetComponent<CharacterController>();
            if (body == null || body.Animator == null) return;
            var a = body.Animator;
            var s = a.GetCurrentAnimatorStateInfo(0);
            string state = s.IsName("Locomotion") ? "Locomotion" : s.IsName("Crouch") ? "Crouch" : s.IsName("Air") ? "Air" : s.IsName("Die") ? "Die" : "?";
            var clips = a.GetCurrentAnimatorClipInfo(0);
            string top = clips.Length > 0 ? string.Join("+", System.Linq.Enumerable.Select(clips, c => $"{c.clip.name}:{c.weight:0.00}")) : "-";
            string upper = "";
            if (a.layerCount > 1)
            {
                var uc = a.GetCurrentAnimatorClipInfo(1);
                upper = $" | upper w{a.GetLayerWeight(1):0.00} {(uc.Length > 0 ? uc[0].clip.name : "-")}";
            }
            log.AppendLine($"  {name}: state {state}, clips {top}{upper}, MoveX {a.GetFloat("MoveX"):0.00} MoveY {a.GetFloat("MoveY"):0.00} Grounded {a.GetBool("Grounded")} " +
                           $"cc.grounded {(cc != null && cc.isGrounded)} vel {(cc != null ? cc.velocity.magnitude : 0f):0.00} gameState {(ZombieRush.Core.GameManager.Instance != null ? ZombieRush.Core.GameManager.Instance.State.ToString() : "none")}" +
                           $" | facing: model {Yaw(player, a.transform.forward):0} deg, hips {(a.isHuman ? Yaw(player, a.GetBoneTransform(HumanBodyBones.Hips).rotation * Quaternion.Inverse(a.transform.rotation) * a.transform.forward) : 0f):0} deg, chest {(a.isHuman && a.GetBoneTransform(HumanBodyBones.UpperChest) != null ? Yaw(player, a.GetBoneTransform(HumanBodyBones.UpperChest).rotation * Quaternion.Inverse(a.transform.rotation) * a.transform.forward) : 0f):0} deg, move dir {(cc != null && cc.velocity.sqrMagnitude > 0.1f ? Yaw(player, cc.velocity) : 0f):0} deg");
        }

        /// Signed yaw of a world direction from the player's facing (+ = to the right).
        static float Yaw(Transform player, Vector3 dir)
        {
            dir.y = 0f;
            return dir.sqrMagnitude < 1e-6f ? 0f : Vector3.SignedAngle(Vector3.ProjectOnPlane(player.forward, Vector3.up), dir, Vector3.up);
        }

        static void LogZombie(GameObject z, string name)
        {
            var a = z != null ? z.GetComponentInChildren<Animator>() : null;
            if (a == null) { log.AppendLine($"  {name}: no animator"); return; }
            var clips = a.GetCurrentAnimatorClipInfo(0);
            var cc = z.GetComponent<CharacterController>();
            string vel = cc != null ? $"{new Vector3(cc.velocity.x, 0f, cc.velocity.z).magnitude:0.00}" : "-";
            log.AppendLine($"  {name}: clips {string.Join("+", System.Linq.Enumerable.Select(clips, c => $"{c.clip.name}:{c.weight:0.00}"))}, speed param {a.GetFloat("Speed"):0.00}, state speed x{a.GetCurrentAnimatorStateInfo(0).speed:0.00}, moving {vel} m/s");
        }

        /// Close shot of the body from the side (yaw 90 = from its right) at chest height.
        static void Side(Transform player, string name, float yaw)
        {
            var go = new GameObject("CaptureCam");
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.fieldOfView = 35f;
                cam.nearClipPlane = 0.05f;
                // Frame the whole body: aim at its middle, back off with its height.
                float h = 1.8f;
                var rs = player.GetComponentsInChildren<SkinnedMeshRenderer>();
                if (rs.Length > 0) { var bb = rs[0].bounds; foreach (var r in rs) bb.Encapsulate(r.bounds); h = Mathf.Clamp(bb.size.y, 1f, 4f); }
                Vector3 target = player.position + Vector3.up * (0.72f * h);
                Vector3 dir = Quaternion.Euler(0f, player.eulerAngles.y + yaw, 0f) * Vector3.forward;
                go.transform.position = target + dir * (1.35f * h) + Vector3.up * (0.08f * h);
                go.transform.LookAt(target);
                Shot(cam, name);
                LogBody(player, name);
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// First-person arms and the overlay camera, for checking the viewmodel: layers, which camera draws them, where they are.
        static string ViewmodelReport(PlayerViewSwitcher sw, Camera cam)
        {
            var sb = new StringBuilder();
            int layer = LayerMask.NameToLayer(ZombieRush.Player.FirstPersonViewmodel.LayerName);
            sb.Append($"layer {layer}, main draws it {(layer >= 0 && (cam.cullingMask & (1 << layer)) != 0)}");
            var stack = cam.GetUniversalAdditionalCameraData()?.cameraStack;
            if (stack != null) foreach (var o in stack) if (o != null) sb.Append($"; overlay '{o.name}' on {o.isActiveAndEnabled}, mask {o.cullingMask}, fov {o.fieldOfView:0}, {Vector3.Distance(o.transform.position, cam.transform.position) * 100:0} cm from main");
            if (sw.body != null && sw.body.firstPersonOnly != null)
                foreach (var r in sw.body.firstPersonOnly)
                    if (r != null)
                    {
                        sb.Append($"; arms '{r.name}' enabled {r.enabled}, active {r.gameObject.activeInHierarchy}, layer {r.gameObject.layer}, shadows {r.shadowCastingMode}, bounds centre {cam.transform.InverseTransformPoint(r.bounds.center).ToString("F2")} (camera space), size {r.bounds.size.ToString("F2")}");
                        if (r is SkinnedMeshRenderer smr)
                        {
                            var m = smr.sharedMesh;
                            int nullBones = smr.bones.Count(b => b == null);
                            sb.Append($", mesh '{(m != null ? m.name : "NONE")}' {(m != null ? m.vertexCount : 0)} verts {(m != null ? m.triangles.Length / 3 : 0)} tris, bindposes {(m != null ? m.bindposes.Length : 0)}, bones {smr.bones.Length} ({nullBones} missing), root '{(smr.rootBone != null ? smr.rootBone.name : "-")}', material '{(smr.sharedMaterial != null ? smr.sharedMaterial.name : "NONE")}'");
                            var anim = sw.GetComponentsInChildren<Animator>(true).FirstOrDefault(x => x.isHuman);
                            var rh = anim != null ? anim.GetBoneTransform(HumanBodyBones.RightHand) : null;
                            if (rh != null) sb.Append($", right hand bone at {cam.transform.InverseTransformPoint(rh.position).ToString("F2")} (camera space), in arms' bones {System.Array.IndexOf(smr.bones, rh) >= 0}");
                            var baked = new Mesh(); smr.BakeMesh(baked, true);
                            var bb = new Bounds(); bool first = true;
                            var w2l = smr.transform.localToWorldMatrix;
                            foreach (var v in baked.vertices) { var wv = cam.transform.InverseTransformPoint(w2l.MultiplyPoint3x4(v)); if (first) { bb = new Bounds(wv, Vector3.zero); first = false; } else bb.Encapsulate(wv); }
                            sb.Append($", baked skin spans {bb.min.ToString("F2")}..{bb.max.ToString("F2")} (camera space)");
                            // where the triangles are (only vertices they use), and how big they are
                            var tris = baked.triangles; var bv = baked.vertices;
                            var tb = new Bounds(); bool tf = true; double area = 0; int big = 0;
                            for (int ti = 0; ti < tris.Length; ti += 3)
                            {
                                Vector3 a0 = cam.transform.InverseTransformPoint(w2l.MultiplyPoint3x4(bv[tris[ti]])), a1 = cam.transform.InverseTransformPoint(w2l.MultiplyPoint3x4(bv[tris[ti + 1]])), a2 = cam.transform.InverseTransformPoint(w2l.MultiplyPoint3x4(bv[tris[ti + 2]]));
                                foreach (var q in new[] { a0, a1, a2 }) { if (tf) { tb = new Bounds(q, Vector3.zero); tf = false; } else tb.Encapsulate(q); }
                                float ar = Vector3.Cross(a1 - a0, a2 - a0).magnitude * 0.5f; area += ar;
                                if ((a1 - a0).magnitude > 0.3f || (a2 - a0).magnitude > 0.3f) big++;
                            }
                            sb.Append($", triangles span {tb.min.ToString("F2")}..{tb.max.ToString("F2")}, area {area:0.000} m2, {big} with an edge over 30 cm");
                            Object.DestroyImmediate(baked);
                        }
                    }
            else sb.Append("; no firstPersonOnly renderers");
            return sb.ToString();
        }

        /// Overlay cameras stacked on `cam` (FirstPersonViewmodel: first-person arms and guns) can't render
        /// alone, so each is rendered for a moment as a normal camera, twice: over black and over white.
        /// Where the two match, the overlay covers the shot; where they differ, the shot shows through by
        /// that much (materials' alpha is not trusted: the body texture's alpha is not coverage, and muzzle
        /// flashes add light). That matches how the game draws the overlay on top.
        static void LayerOverlays(Camera cam, Texture2D tex)
        {
            var stack = cam.GetUniversalAdditionalCameraData()?.cameraStack;
            if (stack == null) return;
            foreach (var o in stack)
            {
                if (o == null || !o.isActiveAndEnabled) continue;
                var od = o.GetUniversalAdditionalCameraData();
                var oldType = od.renderType; var oldClear = o.clearFlags; var oldBg = o.backgroundColor; var oldPost = od.renderPostProcessing;
                try
                {
                    od.renderType = CameraRenderType.Base;
                    od.renderPostProcessing = false;
                    o.clearFlags = CameraClearFlags.SolidColor;
                    var overBlack = RenderOver(o, Color.black, tex.width, tex.height);
                    var overWhite = RenderOver(o, Color.white, tex.width, tex.height);
                    var a = tex.GetPixels32();
                    for (int i = 0; i < a.Length; i++)
                    {
                        Color32 k = overBlack[i], w = overWhite[i];
                        int see = Mathf.Max(w.r - k.r, Mathf.Max(w.g - k.g, w.b - k.b));   // 255 = background shows fully
                        see = Mathf.Clamp(see, 0, 255);
                        if (see >= 254) continue;
                        a[i].r = (byte)Mathf.Min(255, k.r + a[i].r * see / 255);
                        a[i].g = (byte)Mathf.Min(255, k.g + a[i].g * see / 255);
                        a[i].b = (byte)Mathf.Min(255, k.b + a[i].b * see / 255);
                    }
                    tex.SetPixels32(a); tex.Apply();
                }
                finally
                {
                    od.renderType = oldType; od.renderPostProcessing = oldPost; o.clearFlags = oldClear; o.backgroundColor = oldBg;
                }
            }
        }

        static Color32[] RenderOver(Camera o, Color background, int width, int height)
        {
            o.backgroundColor = background;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            try
            {
                var req = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(o, req)) RenderPipeline.SubmitRenderRequest(o, req);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var t = new Texture2D(width, height, TextureFormat.RGBA32, false);
                t.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                t.Apply();
                RenderTexture.active = prev;
                var px = t.GetPixels32();
                Object.DestroyImmediate(t);
                return px;
            }
            finally { rt.Release(); Object.DestroyImmediate(rt); }
        }

        static void Shot(Camera cam, string name)
        {
            Directory.CreateDirectory(Folder);
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            var oldTarget = cam.targetTexture;
            try
            {
                var req = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                else { cam.targetTexture = rt; cam.Render(); }
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                LayerOverlays(cam, tex);   // the first-person arms-and-gun camera draws on top in the game
                File.WriteAllBytes($"{Folder}/{name}.png", tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                log.AppendLine(name);
                File.WriteAllText("Temp/ZR_PlayCapture.log", log.ToString() + "(running)");
            }
            finally
            {
                cam.targetTexture = oldTarget;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }
    }
}
