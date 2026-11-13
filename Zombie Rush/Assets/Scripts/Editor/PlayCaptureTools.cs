using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
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

        static List<(float, System.Action)> BuildPlan(PlayerViewSwitcher switcher, string mode)
        {
            var player = switcher.GetComponent<PlayerController>();
            var weapons = switcher.GetComponent<PlayerWeaponController>();
            var cam = switcher.playerCamera;
            var p = new List<(float, System.Action)>
            {
                (0.7f, () => MoveToOpenSpot(switcher.transform)),
                (0.7f, () => switcher.SetView(CharacterView.ThirdPerson)),
                (0.3f, () => SetPitch(player, 0f)),
            };

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
                };
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
                           $"player yaw jitter {yawj / rn:0.00} deg, body yaw jitter {byj / rn:0.00} deg, frame time {dtMin * 1000:0}-{dtMax * 1000:0} ms, hand-gun distance {gmin * 1000:0} to {gmax * 1000:0} mm";
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

            if (mode == "tpppitch")
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
                int rifle = System.Array.FindIndex(weapons.weapons, w => w.name.Contains("Rifle"));
                p.Add((0.9f, () => { if (rifle >= 0) { weapons.Unlock(weapons.weapons[rifle].data); weapons.SelectWeapon(rifle); } }));
                foreach (float pitch in new[] { -60f, -30f, 0f, 30f, 55f })
                {
                    float pp = pitch;
                    string tag = pp < 0 ? $"Up{-pp:0}" : pp > 0 ? $"Down{pp:0}" : "Level";
                    p.Add((0.5f, () => { SetPitch(player, pp); weapons.RefillAmmo(); ZombieRush.Core.GameInput.TouchPress(ZombieRush.Core.TouchAction.Fire); }));
                    p.Add((0.25f, () => { SetPitch(player, pp); ZombieRush.Core.GameInput.TouchRelease(ZombieRush.Core.TouchAction.Fire); }));
                    p.Add((0.3f, () => { ScreenCapture.CaptureScreenshot($"{Folder}/TPP_{tag}_Game.png"); UnityEditorInternal.InternalEditorUtility.RepaintAllViews(); }));
                    p.Add((0.2f, () => Side(switcher.transform, $"TPP_{tag}_Side", 60f)));
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
                    p.Add((0.9f, () => { weapons.Unlock(weapons.weapons[idx].data); weapons.SelectWeapon(idx); weapons.RefillAmmo(); }));
                    p.Add((0.05f, () =>
                    {
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
                           $"cc.grounded {(cc != null && cc.isGrounded)} vel {(cc != null ? cc.velocity.magnitude : 0f):0.00} gameState {(ZombieRush.Core.GameManager.Instance != null ? ZombieRush.Core.GameManager.Instance.State.ToString() : "none")}");
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
