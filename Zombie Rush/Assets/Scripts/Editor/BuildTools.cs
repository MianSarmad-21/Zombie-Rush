using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ZombieRush.EditorTools
{
    /// Player builds for testing on real devices:
    ///   Windows: Builds/Windows/ZombieRush.exe (zip the folder to share);
    ///   Android: Builds/Android/ZombieRush.apk (install on a phone).
    /// Marker: Temp/ZR_RunMapCleanup with "buildwin" or "buildandroid" (report in Temp/ZR_MapCleanup.log).
    public static class BuildTools
    {
        [MenuItem("Zombie Rush/Build/Windows", priority = 60)]
        static void WinMenu() => Debug.Log("Zombie Rush: " + Windows());

        [MenuItem("Zombie Rush/Build/Android APK", priority = 61)]
        static void AndroidMenu() => Debug.Log("Zombie Rush: " + Android());

        /// The Android package name (one per app on a phone).
        const string AndroidId = "com.miansarmad.zombierush";

        static string[] Scenes()
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (var s in EditorBuildSettings.scenes) if (s.enabled) list.Add(s.path);
            return list.ToArray();
        }

        public static string Windows()
        {
            // Switching platform INSIDE BuildPlayer keeps URP's shader stripping on the Android
            // (Mobile) settings -> PC post-processing / SSAO shaders got stripped -> black screen.
            // So switch first as its own step and build on the next request.
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                return EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64)
                    ? "SWITCHED to Windows: run buildwin again to build." : "FAILED to switch to Windows.";
            Directory.CreateDirectory("Builds/Windows");
            var opts = new BuildPlayerOptions
            {
                scenes = Scenes(),
                locationPathName = "Builds/Windows/ZombieRush.exe",
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            return Report("Windows", BuildPipeline.BuildPlayer(opts));
        }

        public static string Android()
        {
            Directory.CreateDirectory("Builds/Android");
            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, AndroidId);
            PlayerSettings.Android.forceInternetPermission = true;   // squads play over the internet
            // a landscape game: turns between the two landscape sides with the phone, never portrait
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            var opts = new BuildPlayerOptions
            {
                scenes = Scenes(),
                locationPathName = "Builds/Android/ZombieRush.apk",
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            };
            return Report("Android", BuildPipeline.BuildPlayer(opts));
        }

        static string Report(string what, BuildReport r)
        {
            var s = r.summary;
            string msg = $"{what} build: {s.result}, {s.totalSize / (1024f * 1024f):0.0} MB, {s.totalTime.TotalMinutes:0.0} min, {s.totalErrors} errors, {s.totalWarnings} warnings -> {s.outputPath}";
            if (s.result != BuildResult.Succeeded)
            {
                foreach (var step in r.steps)
                    foreach (var m in step.messages)
                        if (m.type == LogType.Error || m.type == LogType.Exception) msg += "\n  " + m.content;
            }
            else msg += SizeBreakdown(r);
            return msg;
        }

        /// Read-only: hidden (inactive) objects in the open level that still carry meshes - they ship in the build
        /// anyway. Lists the inactive roots of such branches with their renderer count and mesh triangles.
        public static string InactiveSurvey()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            var sb = new System.Text.StringBuilder($"Inactive branches with meshes in {scene.name}:");
            int shown = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    var go = t.gameObject;
                    if (go.activeSelf) continue;
                    if (t.parent != null && !t.parent.gameObject.activeInHierarchy) continue;   // only the topmost inactive one
                    var rs = go.GetComponentsInChildren<Renderer>(true);
                    if (rs.Length == 0) continue;
                    long tris = 0; var meshes = new System.Collections.Generic.HashSet<string>();
                    foreach (var r in rs)
                    {
                        Mesh m = r is SkinnedMeshRenderer sk ? sk.sharedMesh : (r.TryGetComponent<MeshFilter>(out var mf) ? mf.sharedMesh : null);
                        if (m == null) continue;
                        tris += m.triangles.LongLength / 3; meshes.Add(AssetDatabase.GetAssetPath(m));
                    }
                    string path = go.name; for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
                    if (shown++ < 80) sb.Append($"\n  {path}: {rs.Length} renderers, {tris / 1000f:0.0}k tris, from {string.Join(", ", System.Linq.Enumerable.Take(meshes, 3))}");
                }
            sb.Append($"\n  total: {shown} inactive branches");
            return sb.ToString();
        }

        /// The size breakdown of the last build (Library/LastBuild.buildreport), without building again.
        public static string LastBuildSizes()
        {
            var objs = UnityEditorInternal.InternalEditorUtility.LoadSerializedFileAndForget("Library/LastBuild.buildreport");
            foreach (var o in objs)
                if (o is BuildReport r) return $"Last build: {r.summary.platform}, {r.summary.result}, {r.summary.outputPath}" + SizeBreakdown(r);
            return "FAILED: no Library/LastBuild.buildreport";
        }

        /// What the build's packed data is made of: by kind of asset, and the biggest single assets.
        static string SizeBreakdown(BuildReport r)
        {
            var byType = new System.Collections.Generic.Dictionary<string, ulong>();
            var byAsset = new System.Collections.Generic.Dictionary<string, ulong>();
            foreach (var pa in r.packedAssets)
                foreach (var c in pa.contents)
                {
                    string type = c.type != null ? c.type.Name : "Other";
                    byType.TryGetValue(type, out var t); byType[type] = t + c.packedSize;
                    string p = string.IsNullOrEmpty(c.sourceAssetPath) ? "(built-in)" : c.sourceAssetPath;
                    byAsset.TryGetValue(p, out var a); byAsset[p] = a + c.packedSize;
                }
            var sb = new System.Text.StringBuilder("\n  Packed data by kind (before APK compression):");
            foreach (var kv in System.Linq.Enumerable.Take(System.Linq.Enumerable.OrderByDescending(byType, k => k.Value), 8))
                sb.Append($"\n    {kv.Key}: {kv.Value / (1024f * 1024f):0.0} MB");
            // by folder (first 3 levels): which packs/folders really reach the build
            var byFolder = new System.Collections.Generic.Dictionary<string, ulong>();
            foreach (var kv in byAsset)
            {
                var parts = kv.Key.Split('/');
                string f = parts.Length > 3 ? string.Join("/", parts, 0, 3) : kv.Key;
                byFolder.TryGetValue(f, out var v); byFolder[f] = v + kv.Value;
            }
            sb.Append("\n  By folder:");
            foreach (var kv in System.Linq.Enumerable.Take(System.Linq.Enumerable.OrderByDescending(byFolder, k => k.Value), 30))
                sb.Append($"\n    {kv.Value / (1024f * 1024f):0.0} MB  {kv.Key}");
            sb.Append("\n  Biggest assets:");
            foreach (var kv in System.Linq.Enumerable.Take(System.Linq.Enumerable.OrderByDescending(byAsset, k => k.Value), 12))
                sb.Append($"\n    {kv.Value / (1024f * 1024f):0.0} MB  {kv.Key}");
            // every packed asset, biggest first (Temp/ZR_BuildAssets.txt)
            var all = new System.Text.StringBuilder();
            foreach (var kv in System.Linq.Enumerable.OrderByDescending(byAsset, k => k.Value))
                all.Append($"{kv.Value / 1024f:0}\t{kv.Key}\n");
            File.WriteAllText("Temp/ZR_BuildAssets.txt", all.ToString());
            return sb.ToString();
        }
    }
}
