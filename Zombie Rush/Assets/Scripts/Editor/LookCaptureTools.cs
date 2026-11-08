using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ZombieRush.World;

namespace ZombieRush.EditorTools
{
    /// Zombie Rush > Level > Capture Look (game camera + zombie)
    ///
    /// Renders what the player sees, with the scene's real lighting and post effects, from just
    /// outside the base door: Docs/Look/Street.png (empty street) and Docs/Look/Zombie.png (a Yaku
    /// 7 m ahead, to judge its glow). Also writes Docs/Look/Lighting.txt: the ambient light, fog,
    /// lights and post-processing volumes that make up the look. The zombie is removed again;
    /// nothing in the scene changes.
    public static class LookCaptureTools
    {
        const string OutDir = "Docs/Look";
        const string ZombiePrefab = "Assets/Prefabs/Zombie_Yaku.prefab";

        [MenuItem("Zombie Rush/Level/Capture Look (game camera + zombie)", priority = 41)]
        static void FromMenu() => EditorUtility.DisplayDialog("Capture Look", Run(), "OK");

        public static string Run(string suffix = "")
        {
            var zone = Object.FindAnyObjectByType<ZoneManager>(FindObjectsInactive.Include);
            var safe = Object.FindAnyObjectByType<SafeBase>(FindObjectsInactive.Include);
            var main = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>(FindObjectsInactive.Include);
            if (zone == null || zone.playerBase == null || main == null) return "Open MainLevel first (needs a PlayerBase and a camera).";

            Directory.CreateDirectory(OutDir);
            var baseT = zone.playerBase;
            // The most open street around the base: stand just outside it, looking down that street.
            float out_ = (safe != null ? safe.radius : 15f) + 3f;
            Vector3 fwd = Vector3.forward, eye = baseT.position + Vector3.up * 1.6f;
            float bestScore = -1f;
            for (int i = 0; i < 24; i++)
            {
                float a = i / 24f * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (float r = out_; r < out_ + 25f; r += 3f)
                {
                    var g = Ground(baseT.position + d * r);
                    if (!ArenaGround.IsClear(g, 0.6f, 2f)) continue;
                    var e = g + Vector3.up * 1.6f;
                    float open = Open(e, d, 60f);
                    if (open > bestScore) { bestScore = open; fwd = d; eye = e; }
                    break;
                }
            }

            var camGo = new GameObject("_LookCaptureCam") { hideFlags = HideFlags.HideAndDontSave };
            GameObject zombie = null;
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            try
            {
                var cam = camGo.AddComponent<Camera>();
                cam.CopyFrom(main);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(fwd + Vector3.down * 0.05f));
                cam.fieldOfView = main.fieldOfView;
                var src = main.GetUniversalAdditionalCameraData();
                var dst = cam.GetUniversalAdditionalCameraData();
                dst.renderPostProcessing = src.renderPostProcessing;
                dst.antialiasing = src.antialiasing;
                dst.volumeLayerMask = src.volumeLayerMask;
                dst.volumeTrigger = cam.transform;
                cam.targetTexture = rt;

                Shot(cam, rt, Path.Combine(OutDir, $"Street{suffix}.png"));

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ZombiePrefab);
                if (prefab != null)
                {
                    var at = Ground(eye + fwd * (suffix.Contains("close") ? 3f : 7f)); // "close" in the suffix: point-blank shot
                    zombie = Object.Instantiate(prefab, at, Quaternion.LookRotation(-fwd));
                    zombie.hideFlags = HideFlags.HideAndDontSave;
                    foreach (var mb in zombie.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
                    var anim = zombie.GetComponentInChildren<Animator>();
                    if (anim != null) { anim.enabled = true; anim.Update(0f); }
                    cam.transform.rotation = Quaternion.LookRotation((at + Vector3.up * 1.1f) - eye);
                    Shot(cam, rt, Path.Combine(OutDir, $"Zombie{suffix}.png"));
                }
            }
            finally
            {
                if (zombie != null) Object.DestroyImmediate(zombie);
                Object.DestroyImmediate(camGo);
                rt.Release();
                Object.DestroyImmediate(rt);
            }

            File.WriteAllText(Path.Combine(OutDir, "Lighting.txt"), Describe());
            return $"Captured Docs/Look/Street{suffix}.png and Zombie{suffix}.png from ({eye.x:0}, {eye.z:0}).";
        }

        static float Open(Vector3 from, Vector3 dir, float max)
        {
            float best = max;
            foreach (var h in Physics.RaycastAll(from, dir, max, ~0, QueryTriggerInteraction.Ignore))
                if (h.distance < best) best = h.distance;
            foreach (var h in Physics.RaycastAll(from + Vector3.down * 1.2f, dir, max, ~0, QueryTriggerInteraction.Ignore))
                if (h.distance < best) best = h.distance;
            return best;
        }

        static Vector3 Ground(Vector3 p)
        {
            var hits = Physics.RaycastAll(p + Vector3.up * 30f, Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (var h in hits)
                if (h.point.y > best && h.point.y < p.y + 6f) best = h.point.y;
            if (!float.IsNegativeInfinity(best)) p.y = best;
            return p;
        }

        static void Shot(Camera cam, RenderTexture rt, string path)
        {
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        static string Describe()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Ambient: mode {RenderSettings.ambientMode}, sky {RenderSettings.ambientSkyColor}, equator {RenderSettings.ambientEquatorColor}, ground {RenderSettings.ambientGroundColor}, intensity {RenderSettings.ambientIntensity}");
            sb.AppendLine($"Skybox: {(RenderSettings.skybox != null ? RenderSettings.skybox.name : "none")}, reflection intensity {RenderSettings.reflectionIntensity}");
            sb.AppendLine($"Fog: {RenderSettings.fog} {RenderSettings.fogMode} colour {RenderSettings.fogColor} density {RenderSettings.fogDensity} start {RenderSettings.fogStartDistance} end {RenderSettings.fogEndDistance}");
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(l => l.type == LightType.Directional || l.intensity > 5f).Take(15))
                sb.AppendLine($"Light '{PathOf(l.transform)}': {l.type} intensity {l.intensity} colour {l.color} enabled {l.enabled && l.gameObject.activeInHierarchy}");
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                sb.AppendLine($"Volume '{PathOf(v.transform)}': active {v.isActiveAndEnabled}, global {v.isGlobal}, priority {v.priority}, weight {v.weight}, profile {(v.sharedProfile != null ? AssetDatabase.GetAssetPath(v.sharedProfile) : "none")}");
                if (v.sharedProfile == null) continue;
                foreach (var c in v.sharedProfile.components)
                {
                    sb.Append($"    {c.GetType().Name} (active {c.active}):");
                    foreach (var p in c.parameters.Select((p, i) => (p, i)).Where(x => x.p.overrideState))
                        sb.Append($" {c.GetType().GetFields().Where(f => typeof(VolumeParameter).IsAssignableFrom(f.FieldType)).ElementAtOrDefault(p.i)?.Name}={p.p.GetType().GetProperty("value")?.GetValue(p.p)}");
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }

        static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
    }
}
