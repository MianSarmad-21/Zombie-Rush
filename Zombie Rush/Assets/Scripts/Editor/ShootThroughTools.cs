using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZombieRush.World;

namespace ZombieRush.EditorTools
{
    /// Puts ShootThrough on the base compound in the open scene, so bullets fly through its
    /// walls, crates and lights (Build Survival Layout adds it on every rebuild too).
    /// Start from outside with the marker Temp/ZR_RunShootThrough (report: Temp/ZR_ShootThrough.log).
    [InitializeOnLoad]
    public static class ShootThroughTools
    {
        const string RootName = "BaseCompound";
        const string Marker = "Temp/ZR_RunShootThrough";
        const string Report = "Temp/ZR_ShootThrough.log";
        static double nextCheck;

        static ShootThroughTools() => EditorApplication.update += Poll;

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextCheck) return;
            nextCheck = EditorApplication.timeSinceStartup + 2.0;
            if (!File.Exists(Marker)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            File.Delete(Marker);
            string result;
            try { result = Apply(); }
            catch (System.Exception e) { result = "FAILED: " + e; }
            File.WriteAllText(Report, result);
        }

        [MenuItem("Zombie Rush/Level/Make Base Compound Shoot-Through")]
        static void ApplyMenu() => Debug.Log(Apply());

        public static string Apply()
        {
            GameObject root = null;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name == RootName && t.gameObject.scene.IsValid()) { root = t.gameObject; break; }
            if (root == null) return $"FAILED: no {RootName} in the open scene.";

            int colliders = root.GetComponentsInChildren<Collider>(true).Length;
            if (root.GetComponent<ShootThrough>() != null)
                return $"{RootName} already shoots through ({colliders} colliders). Nothing changed.";

            Undo.AddComponent<ShootThrough>(root);
            EditorSceneManager.MarkSceneDirty(root.scene);
            bool saved = EditorSceneManager.SaveScene(root.scene);
            return $"ShootThrough added to {RootName} ({colliders} colliders); bullets now fly through it. Scene {(saved ? "saved" : "NOT saved")}.";
        }
    }
}
