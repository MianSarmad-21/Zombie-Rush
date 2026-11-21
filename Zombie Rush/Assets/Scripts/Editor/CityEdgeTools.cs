using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZombieRush.World;

namespace ZombieRush.EditorTools
{
    /// The city past the zone edge. The tall mist wall (ZoneBoundaryFX fog curtain) hid it completely; it is replaced
    /// by night fog: the real city stays and fades into the dark with distance.
    /// Marker: Temp/ZR_RunCityEdge, report Temp/ZR_CityEdge.log. Modes:
    ///   "survey [density ...]": read-only. What stands where round the zone, and pictures with the mist wall off at each
    ///       fog density given (Docs/Look/CityEdge_<n>_<view>.png; "0.011@0.04,0.055,0.09" also tries a fog colour).
    ///   "apply": the mist wall off for good (fog curtain removed from every zone wall), the scene fog set from
    ///       Assets/Data/NightLookConfig.asset (fogColor, fogDensity: edit the look there), scene saved.
    [InitializeOnLoad]
    static class CityEdgeTools
    {
        const string Marker = "Temp/ZR_RunCityEdge";
        const string Report = "Temp/ZR_CityEdge.log";
        const string OutDir = "Docs/Look";
        static double next;

        static CityEdgeTools() => EditorApplication.update += Poll;

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 2.0;
            if (!File.Exists(Marker)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            string[] args = File.ReadAllText(Marker).Trim().ToLowerInvariant().Split(new[] { ' ', '\n', '\r', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
            File.Delete(Marker);
            File.WriteAllText(Report, "(running)\n");
            string r;
            try
            {
                string mode = args.Length > 0 ? args[0] : "survey";
                var rest = args.Skip(1).ToArray();
                r = mode == "survey" ? Survey(rest) : mode == "apply" ? Apply() : "FAILED: unknown mode " + mode;
            }
            catch (System.Exception e) { r = "FAILED: " + e; }
            File.WriteAllText(Report, r);
        }

        static ZoneManager Zone() => Object.FindAnyObjectByType<ZoneManager>(FindObjectsInactive.Include);

        // ------------------------------------------------------------------ apply

        const string NightLookPath = "Assets/Data/NightLookConfig.asset";

        static string Apply()
        {
            var sb = new StringBuilder();
            foreach (var w in Object.FindObjectsByType<ZoneBoundaryFX>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (w.fogMaterial == null && w.transform.Find("ZoneFog") == null) continue;
                Undo.RecordObject(w, "City edge: mist wall off");
                w.fogMaterial = null;
                var f = w.transform.Find("ZoneFog");
                if (f != null) Undo.DestroyObjectImmediate(f.gameObject);
                w.enabled = false; w.enabled = true;   // rebuilds its meshes without the curtain
                EditorUtility.SetDirty(w);
                sb.AppendLine($"Mist wall removed from '{w.name}'.");
            }

            AssetDatabase.ImportAsset(NightLookPath, ImportAssetOptions.ForceUpdate);
            var look = AssetDatabase.LoadAssetAtPath<ZombieRush.Data.NightLookConfig>(NightLookPath);
            if (look == null) return "FAILED: no " + NightLookPath;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = look.fogColor;
            RenderSettings.fogDensity = look.fogDensity;
            sb.AppendLine($"Scene fog: exp2 density {look.fogDensity}, colour {look.fogColor} (from {NightLookPath}).");

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            sb.AppendLine($"Saved {scene.path}.");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ survey

        static string Survey(string[] densities)
        {
            var sb = new StringBuilder();
            var zm = Zone();
            if (zm == null) return "FAILED: no ZoneManager in the open scene.";
            Vector3 c = zm.playerBase != null ? zm.playerBase.position : zm.center;
            c.y = 0f;
            float r0 = zm.startRadius;
            sb.AppendLine($"Zone centre {c:F0}, start radius {r0:0}, max {zm.maxRadius:0}, step {zm.expandStep:0}, half circle {zm.halfCircle}.");
            sb.AppendLine($"Fog: {RenderSettings.fog} {RenderSettings.fogMode} density {RenderSettings.fogDensity} colour {RenderSettings.fogColor}.");

            var walls = Object.FindObjectsByType<ZoneBoundaryFX>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var w in walls)
                sb.AppendLine($"  ZoneBoundaryFX '{w.name}' fixedRadius {w.fixedRadius} fog {(w.fogMaterial != null ? w.fogMaterial.name : "none")} height {w.fogHeight}");

            var t = Terrain.activeTerrain;
            if (t != null)
            {
                Vector3 tp = t.transform.position, ts = t.terrainData.size;
                sb.AppendLine($"Terrain '{t.name}' x {tp.x:0}..{tp.x + ts.x:0}, z {tp.z:0}..{tp.z + ts.z:0}; from the zone centre to its edge: " +
                              $"-x {c.x - tp.x:0} m, +x {tp.x + ts.x - c.x:0} m, -z {c.z - tp.z:0} m, +z {tp.z + ts.z - c.z:0} m.");
            }

            // what is drawn, by distance from the zone centre
            float[] bands = { 50f, 100f, 150f, 200f, 300f, 400f, 1e9f };
            var count = new int[bands.Length]; var tris = new long[bands.Length];
            float tallest = 0f; string tallestName = "";
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!mr.enabled || !mr.gameObject.activeInHierarchy) continue;
                var b = mr.bounds;
                float d = Vector2.Distance(new Vector2(b.center.x, b.center.z), new Vector2(c.x, c.z));
                int i = System.Array.FindIndex(bands, x => d < x);
                count[i]++;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null && mf.sharedMesh.isReadable) tris[i] += mf.sharedMesh.triangles.Length / 3;
                if (d > zm.maxRadius && b.max.y > tallest) { tallest = b.max.y; tallestName = mr.name; }
            }
            float lo = 0f;
            for (int i = 0; i < bands.Length; i++)
            {
                sb.AppendLine($"  {lo:0}-{(bands[i] > 1e8f ? "far" : bands[i].ToString("0"))} m: {count[i]} renderers{(tris[i] > 0 ? $", ~{tris[i] / 1000}k readable tris" : "")}");
                lo = bands[i];
            }
            sb.AppendLine($"  tallest thing outside the biggest zone: {tallestName} top {tallest:0.0} m");

            // pictures with the mist wall off
            var fogParts = walls.Select(w => w.transform.Find("ZoneFog")).Where(x => x != null).Select(x => x.GetComponent<Renderer>()).Where(x => x != null).ToList();
            var fogOn = fogParts.Select(x => x.enabled).ToList();
            bool fogWas = RenderSettings.fog; float densityWas = RenderSettings.fogDensity; Color colourWas = RenderSettings.fogColor;
            var main = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();
            var camGo = new GameObject("_EdgeCam") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            Directory.CreateDirectory(OutDir);
            if (densities.Length == 0) densities = new[] { RenderSettings.fogDensity.ToString(CultureInfo.InvariantCulture) };
            try
            {
                foreach (var f in fogParts) f.enabled = false;
                var cam = camGo.AddComponent<Camera>();
                if (main != null) cam.CopyFrom(main);
                cam.targetTexture = rt; cam.farClipPlane = 400f; cam.fieldOfView = 55f;
                Vector3 fwd = zm.playerBase != null ? Flat(zm.playerBase.forward) : Vector3.forward;
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                var views = new (string name, Vector3 eye, Vector3 at)[]
                {
                    ("heli_in", c - fwd * (r0 + 60f) + right * 30f + Vector3.up * 36f, c + Vector3.up * 8f),
                    ("high_out", c + fwd * 10f + Vector3.up * 40f, c + fwd * 160f),
                    ("high_side", c + right * 10f + Vector3.up * 40f, c + right * 160f + fwd * 40f),
                    ("edge_out", c + fwd * (r0 - 4f) + Vector3.up * 1.7f, c + fwd * 120f + Vector3.up * 6f),
                    ("deck_out", c + Vector3.up * 14f, c - fwd * 120f + right * 60f + Vector3.up * 4f),
                };

                int n = 0;
                foreach (var token in densities)
                {
                    // "density" or "density@r,g,b" (a fog colour to try)
                    var parts = token.Split('@');
                    if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float d)) continue;
                    Color col = colourWas;
                    if (parts.Length > 1)
                    {
                        var ch = parts[1].Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                        if (ch.Length >= 3) col = new Color(ch[0], ch[1], ch[2]);
                    }
                    RenderSettings.fog = true; RenderSettings.fogDensity = d; RenderSettings.fogColor = col;
                    string tag = $"{++n}";
                    foreach (var v in views)
                    {
                        cam.transform.SetPositionAndRotation(v.eye, Quaternion.LookRotation(v.at - v.eye, Vector3.up));
                        Shot(cam, rt, $"{OutDir}/CityEdge_{tag}_{v.name}.png");
                    }
                    sb.AppendLine($"Pictures {tag}: fog {d} colour {col}: {string.Join(", ", views.Select(v => v.name))}");
                }

            }
            finally
            {
                for (int i = 0; i < fogParts.Count; i++) fogParts[i].enabled = fogOn[i];
                RenderSettings.fog = fogWas; RenderSettings.fogDensity = densityWas; RenderSettings.fogColor = colourWas;
                Object.DestroyImmediate(camGo); rt.Release(); Object.DestroyImmediate(rt);
            }
            return sb.ToString();
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward; }

        static void Shot(Camera cam, RenderTexture rt, string path)
        {
            var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
            if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, req)) UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req);
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
    }
}
