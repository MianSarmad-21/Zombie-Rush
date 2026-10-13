using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZombieRush.EditorTools
{
    /// Zombie Rush > Project > Organize Model Folders (by date)
    ///
    /// One folder per batch of models, named by the date it was added to the project, so they sort in order:
    ///   Models/2026-09-15_UndeadConstruction/   first zombie: model + prefab, Material_1 + textures, Zombie_Walk.controller
    ///   Models/2026-09-25_Yaku/                 Yaku model, Mat_Yaku + textures, Zombie_Yaku.controller
    ///   Models/2026-09-25_Yaku/Animations/      the Mixamo clips added with the Yaku
    ///   Models/2026-10-11_NewDownload/          the WinRAR download (.zip + its unzipped folder with the .blend)
    /// Existing assets move through AssetDatabase.MoveAsset, so GUIDs and every prefab/scene link stay intact.
    /// Works from the original flat Models folder or from the earlier Zombies/... layout, and is safe to run again.
    public static class ModelFolderTools
    {
        const string Root = "Assets/Models";
        const string First = Root + "/2026-09-15_UndeadConstruction";
        const string Yaku = Root + "/2026-09-25_Yaku";
        const string YakuAnims = Yaku + "/Animations";
        const string Download = Root + "/2026-10-11_NewDownload";

        // Older layouts the files may currently be in
        static readonly string[] OldFolders = { Root, Root + "/Zombies/Yaku", Root + "/Zombies/Yaku/Animations", Root + "/Zombies/UndeadConstruction" };

        static readonly (string file, string folder)[] Moves =
        {
            ("Meshy_AI_Undead_Construction_W_biped_Animation_Walking_withSkin.fbx", First),
            ("Meshy_AI_Undead_Construction_W_biped_Animation_Walking_withSkin.prefab", First),
            ("Meshy_AI_Undead_Construction_W_biped_texture_0.png", First),
            ("Meshy_AI_Undead_Construction_W_biped_texture_0_metallic.png", First),
            ("Meshy_AI_Undead_Construction_W_biped_texture_0_normal.png", First),
            ("Meshy_AI_Undead_Construction_W_biped_texture_0_roughness.png", First),
            ("Material_1.mat", First),
            ("Zombie_Walk.controller", First),

            ("Meshy_AI_Yaku_J_Ignite_0925161629_texture.fbx", Yaku),
            ("Mat_Yaku.mat", Yaku),
            ("texture_1.png", Yaku),
            ("texture_1_normal.png", Yaku),
            ("Zombie_Yaku.controller", Yaku),
            ("Walking.fbx", YakuAnims),
            ("Zombie Run.fbx", YakuAnims),
            ("Zombie Idle.fbx", YakuAnims),
            ("Zombie Punching.fbx", YakuAnims),
            ("Zombie Reaction Hit.fbx", YakuAnims),
            ("Zombie Dying.fbx", YakuAnims),

            ("Meshy_AI_Yaku_J_Ignite_0925161629_texture_fbx (2).zip", Download),
            ("Meshy_AI_Yaku_J_Ignite_0925161629_texture_fbx", Download),
        };

        [MenuItem("Zombie Rush/Project/Organize Model Folders (by date)")]
        static void Organize()
        {
            if (!EditorUtility.DisplayDialog("Organize Model Folders (by date)",
                    "Sort Assets/Models into one folder per date added:\n\n" +
                    "2026-09-15_UndeadConstruction\n2026-09-25_Yaku (+ Animations)\n2026-10-11_NewDownload\n\nPrefab and scene links are kept.",
                    "Organize", "Cancel"))
                return;

            EnsureFolder(First); EnsureFolder(Yaku); EnsureFolder(YakuAnims); EnsureFolder(Download);
            BringBackSourceFiles();

            int moved = 0, inPlace = 0, missing = 0;
            foreach (var (file, folder) in Moves)
            {
                string to = $"{folder}/{file}";
                if (Exists(to)) { inPlace++; continue; }
                string from = OldFolders.Select(f => $"{f}/{file}").FirstOrDefault(Exists);
                if (from == null) { missing++; Debug.LogWarning($"[Zombie Rush] Not found: {file}"); continue; }
                string error = AssetDatabase.MoveAsset(from, to);
                if (string.IsNullOrEmpty(error)) moved++;
                else Debug.LogError($"[Zombie Rush] Could not move {from}: {error}");
            }

            foreach (var old in new[] { Root + "/Zombies/Yaku/Animations", Root + "/Zombies/Yaku", Root + "/Zombies/UndeadConstruction", Root + "/Zombies" })
                if (AssetDatabase.IsValidFolder(old) && AssetDatabase.FindAssets("", new[] { old }).Length == 0)
                    AssetDatabase.DeleteAsset(old);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Zombie Rush] Model folders organized by date: {moved} moved, {inPlace} already in place, {missing} not found.");
        }

        // The first version of this tool moved the download out to <project>/SourceFiles/Models/Yaku; put it back in the project.
        static void BringBackSourceFiles()
        {
            string project = Directory.GetParent(Application.dataPath).FullName;
            string src = Path.Combine(project, "SourceFiles", "Models", "Yaku");
            if (!Directory.Exists(src)) return;
            string dest = Path.Combine(project, Download);
            foreach (var entry in Directory.GetFileSystemEntries(src))
            {
                string to = Path.Combine(dest, Path.GetFileName(entry));
                if (File.Exists(to) || Directory.Exists(to)) continue;
                if (Directory.Exists(entry)) Directory.Move(entry, to); else File.Move(entry, to);
            }
            for (var d = new DirectoryInfo(src); d != null && d.FullName != project && d.Exists && !d.EnumerateFileSystemInfos().Any(); d = d.Parent)
                d.Delete();
            AssetDatabase.Refresh();
        }

        static bool Exists(string assetPath) =>
            AssetDatabase.IsValidFolder(assetPath) || AssetDatabase.LoadMainAssetAtPath(assetPath) != null;

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
