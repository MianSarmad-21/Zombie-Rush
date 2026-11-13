using System.IO;
using UnityEditor;

namespace ZombieRush.EditorTools
{
    /// Runs an editor tool when its marker file appears in Temp/ (checked after every script reload
    /// and every 2 seconds), so it can be started from outside the editor. Each tool writes a report to Temp/.
    /// Waits while Play mode is on: tools that edit prefabs or the scene never run mid-game.
    ///   Temp/ZR_RunZombieStartOver -> Zombies > Start Over With The Wanderer (Temp/ZR_ZombieSetup.log)
    ///   Temp/ZR_RunZombieSetup    -> set up one zombie, its name inside, e.g. "Wanderer" (Temp/ZR_ZombieSetup.log)
    ///   Temp/ZR_RunMapSurvey      -> Level > Survey Map; "city" inside = city walk plan (Temp/ZR_MapSurvey.log)
    ///   Temp/ZR_RunCityBase       -> Level > Base At City Centre (Temp/ZR_CityBase.log)
    ///   Temp/ZR_RunCapture        -> Level > Capture Look; text inside = file suffix (Temp/ZR_Capture.log)
    ///   Temp/ZR_RunPlayerView     -> Player > Set Up First-Third Person (Temp/ZR_PlayerView.log)
    ///   Temp/ZR_RunClutter        -> Level > Survey/Clear Ground Clutter; "clear" inside = clear (Temp/ZR_Clutter.log)
    ///   Temp/ZR_RunMapCleanup     -> Level > Survey Map Debris; mode inside, e.g. "survey" (Temp/ZR_MapCleanup.log)
    ///   Temp/ZR_RunMeshyBody      -> Player > Set Up Meshy Body (Temp/ZR_MeshyBody.log)
    ///   Temp/ZR_RunPlayCapture    -> Player > Capture Third Person (Play); "firemove" inside = moving/firing shots (Temp/ZR_PlayCapture.log); runs last
    [InitializeOnLoad]
    static class CharacterSetupAutoRun
    {
        const double PollSeconds = 2.0;
        static double nextPoll;

        static CharacterSetupAutoRun()
        {
            EditorApplication.delayCall += TryRun;
            EditorApplication.update += Poll;
        }

        /// A marker written while no script changes would otherwise wait for the next reload.
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + PollSeconds;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            TryRun();
        }

        static void TryRun()
        {
            bool setup = File.Exists("Temp/ZR_RunZombieStartOver"), survey = File.Exists("Temp/ZR_RunMapSurvey"),
                 cityBase = File.Exists("Temp/ZR_RunCityBase"), capture = File.Exists("Temp/ZR_RunCapture"),
                 playerView = File.Exists("Temp/ZR_RunPlayerView"), playCapture = File.Exists("Temp/ZR_RunPlayCapture"),
                 meshyBody = File.Exists("Temp/ZR_RunMeshyBody"), clutter = File.Exists("Temp/ZR_RunClutter"),
                 yakuFinal = File.Exists("Temp/ZR_RunZombieSetup"), mapCleanup = File.Exists("Temp/ZR_RunMapCleanup");
            if (!setup && !survey && !cityBase && !capture && !playerView && !playCapture && !meshyBody && !clutter && !yakuFinal && !mapCleanup) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryRun;
                return;
            }
            if (setup)
            {
                File.Delete("Temp/ZR_RunZombieStartOver");
                Report("Temp/ZR_ZombieSetup.log", ZombieSetupTools.StartOver);
            }
            if (cityBase)
            {
                File.Delete("Temp/ZR_RunCityBase");
                Report("Temp/ZR_CityBase.log", BaseBuildingTools.MakeCityCentreBase);
            }
            if (clutter)
            {
                bool clear = File.ReadAllText("Temp/ZR_RunClutter").Contains("clear");
                File.Delete("Temp/ZR_RunClutter");
                Report("Temp/ZR_Clutter.log", () => GroundClutterTools.Run(clear));
            }
            if (mapCleanup)
            {
                // one step per line, run in order
                var steps = File.ReadAllLines("Temp/ZR_RunMapCleanup");
                File.Delete("Temp/ZR_RunMapCleanup");
                Report("Temp/ZR_MapCleanup.log", () =>
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var s in steps)
                        if (s.Trim().Length > 0) sb.AppendLine($"== {s.Trim()}\n{MapCleanupTools.Run(s.Trim())}");
                    return sb.ToString();
                });
            }
            if (yakuFinal)
            {
                string zombie = File.ReadAllText("Temp/ZR_RunZombieSetup").Trim();
                File.Delete("Temp/ZR_RunZombieSetup");
                Report("Temp/ZR_ZombieSetup.log", () => ZombieSetupTools.Setup(zombie));
            }
            if (meshyBody)
            {
                File.Delete("Temp/ZR_RunMeshyBody");
                Report("Temp/ZR_MeshyBody.log", MeshyBodyTools.Run);
            }
            if (playerView)
            {
                File.Delete("Temp/ZR_RunPlayerView");
                Report("Temp/ZR_PlayerView.log", PlayerViewTools.Run);
            }
            if (capture)
            {
                string suffix = File.ReadAllText("Temp/ZR_RunCapture").Trim();
                File.Delete("Temp/ZR_RunCapture");
                Report("Temp/ZR_Capture.log", () => LookCaptureTools.Run(suffix));
            }
            if (survey)
            {
                bool city = File.ReadAllText("Temp/ZR_RunMapSurvey").Contains("city");
                File.Delete("Temp/ZR_RunMapSurvey");
                Report("Temp/ZR_MapSurvey.log", () => MapSurveyTools.Run(city));
            }
            if (playCapture)
            {
                string mode = File.ReadAllText("Temp/ZR_RunPlayCapture").Trim();
                File.Delete("Temp/ZR_RunPlayCapture");
                PlayCaptureTools.Run(mode); // enters Play mode, so it goes last
            }
        }

        static void Report(string path, System.Func<string> tool)
        {
            string msg;
            try { msg = tool(); }
            catch (System.Exception e) { msg = "FAILED: " + e; }
            File.WriteAllText(path, msg);
        }
    }
}
