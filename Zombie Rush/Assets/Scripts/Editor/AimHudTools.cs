using System.IO;
using UnityEditor;

namespace ZombieRush.EditorTools
{
    /// Runs Zombie Rush > UI > Upgrade Aim HUD (open scene) from outside the editor: write the marker
    /// Temp/ZR_RunAimHud; the report lands in Temp/ZR_AimHud.log.
    [InitializeOnLoad]
    static class AimHudTools
    {
        const string Marker = "Temp/ZR_RunAimHud";
        const string Report = "Temp/ZR_AimHud.log";
        static double nextCheck;

        static AimHudTools() => EditorApplication.update += Poll;

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextCheck) return;
            nextCheck = EditorApplication.timeSinceStartup + 2.0;
            if (!File.Exists(Marker)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            File.Delete(Marker);
            string result;
            try { result = ZombieRushUIBuilder.UpgradeAimHud(); }
            catch (System.Exception e) { result = "FAILED: " + e; }
            File.WriteAllText(Report, result);
        }
    }
}
