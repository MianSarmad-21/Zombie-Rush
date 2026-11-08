using System.IO;
using UnityEditor;

namespace ZombieRush.EditorTools
{
    /// Runs an editor tool once after a script reload when its marker file exists in Temp/,
    /// so it can be started from outside the editor. Each tool writes a report to Temp/.
    /// Waits while Play mode is on: tools that edit prefabs or the scene never run mid-game.
    ///   Temp/ZR_RunCharacterSetup -> Characters > Set Up New Characters (Temp/ZR_CharacterSetup.log)
    ///   Temp/ZR_RunMapSurvey      -> Level > Survey Map; "city" inside = city walk plan (Temp/ZR_MapSurvey.log)
    ///   Temp/ZR_RunCityBase       -> Level > Base At City Centre (Temp/ZR_CityBase.log)
    ///   Temp/ZR_RunCapture        -> Level > Capture Look; text inside = file suffix (Temp/ZR_Capture.log)
    [InitializeOnLoad]
    static class CharacterSetupAutoRun
    {
        static CharacterSetupAutoRun() => EditorApplication.delayCall += TryRun;

        static void TryRun()
        {
            bool setup = File.Exists("Temp/ZR_RunCharacterSetup"), survey = File.Exists("Temp/ZR_RunMapSurvey"),
                 cityBase = File.Exists("Temp/ZR_RunCityBase"), capture = File.Exists("Temp/ZR_RunCapture");
            if (!setup && !survey && !cityBase && !capture) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryRun;
                return;
            }
            if (setup)
            {
                File.Delete("Temp/ZR_RunCharacterSetup");
                CharacterSetupTools.Run(false);
            }
            if (cityBase)
            {
                File.Delete("Temp/ZR_RunCityBase");
                Report("Temp/ZR_CityBase.log", BaseBuildingTools.MakeCityCentreBase);
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
