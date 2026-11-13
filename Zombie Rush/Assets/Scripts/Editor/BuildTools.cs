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
                foreach (var step in r.steps)
                    foreach (var m in step.messages)
                        if (m.type == LogType.Error || m.type == LogType.Exception) msg += "\n  " + m.content;
            return msg;
        }
    }
}
