using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZombieRush.Data;

namespace ZombieRush.EditorTools
{
    /// The match intro's body clip. After Blender/player_anim.py --export adds takes to ShadowOperative_Anims.fbx,
    /// this re-reads the file's takes (same settings as MeshyBodyTools: in place, looping, clip ids kept) and points
    /// Resources/ZombieRush/Cutscene/IntroMoves.asset at Sit_Pistol.
    /// Marker: Temp/ZR_RunIntroClips, report Temp/ZR_IntroClips.log.
    [InitializeOnLoad]
    static class IntroClipsTools
    {
        const string Marker = "Temp/ZR_RunIntroClips";
        const string Report = "Temp/ZR_IntroClips.log";
        const string AssetPath = "Assets/Resources/" + IntroMoveClips.ResourcePath + ".asset";
        const string SitClip = "Sit_Pistol";
        /// Blender/player_anim.py SEAT_Z and SEAT_EDGE (model metres) x GAME_SCALE 1.052: the seated clip's ledge in game.
        const float SeatHeight = 0.323f * 1.052f, SeatEdge = 0.20f * 1.052f;
        static double next;

        static IntroClipsTools() => EditorApplication.update += Poll;

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 2.0;
            if (!File.Exists(Marker)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            File.Delete(Marker);
            File.WriteAllText(Report, "(running)\n");
            string r;
            try { r = Run(); }
            catch (System.Exception e) { r = "FAILED: " + e; }
            File.WriteAllText(Report, r);
        }

        [MenuItem("Zombie Rush/Player/Intro Move Clips")]
        static void Menu() => Debug.Log("Zombie Rush: " + Run());

        static string Run()
        {
            string src = MeshyBodyTools.ClipSource;
            if (!(AssetImporter.GetAtPath(src) is ModelImporter imp)) return "FAILED: " + src + " not found.";
            System.Collections.Generic.List<AnimationClip> Clips() =>
                AssetDatabase.LoadAllAssetsAtPath(src).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToList();
            var clips = Clips();
            if (!clips.Any(c => c.name == SitClip))
            {
                // new takes: re-read the clip list (only then: it can hand existing clips new ids)
                MeshyBodyTools.ConfigureClips(imp);
                imp.SaveAndReimport();
                clips = Clips();
            }
            var sit = clips.FirstOrDefault(c => c.name == SitClip);
            if (sit == null)
                return $"FAILED: {SitClip} not in {src} ({string.Join(", ", clips.Select(c => c.name))}). Run Blender/player_anim.py --export.";

            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            var moves = AssetDatabase.LoadAssetAtPath<IntroMoveClips>(AssetPath);
            if (moves == null) { moves = ScriptableObject.CreateInstance<IntroMoveClips>(); AssetDatabase.CreateAsset(moves, AssetPath); }
            moves.sit = sit;
            moves.seatHeight = SeatHeight; moves.seatEdge = SeatEdge;
            EditorUtility.SetDirty(moves);
            AssetDatabase.SaveAssets();
            return $"{src}: {clips.Count} clips. {AssetPath}: sit {sit.name} ({sit.length:0.00} s, loop {sit.isLooping}, humanoid {sit.humanMotion}), " +
                   $"seat {SeatHeight:0.000} m up, edge {SeatEdge:0.000} m ahead.\n" + Relink(clips) + "\n" + CheckController();
        }

        /// Points every state and blend-tree slot of the body's controller at the freshly imported clip of the same name
        /// and saves it: after a re-export the editor can still show the old clip objects while the game, loading the
        /// controller from disk, gets none (the body then plays nothing and sinks a metre in third person).
        static string Relink(System.Collections.Generic.List<AnimationClip> clips)
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(MeshyBodyTools.ControllerPath);
            if (ctrl == null) return "Relink: no controller.";
            var byName = clips.GroupBy(c => c.name).ToDictionary(g => g.Key, g => g.First());
            int n = 0;
            Motion Fresh(Motion m)
            {
                if (m is UnityEditor.Animations.BlendTree bt)
                {
                    var ch = bt.children;
                    for (int i = 0; i < ch.Length; i++) ch[i].motion = Fresh(ch[i].motion);
                    bt.children = ch;
                    EditorUtility.SetDirty(bt);
                    return bt;
                }
                if (m is AnimationClip c && byName.TryGetValue(c.name, out var f)) { n++; return f; }
                return m;
            }
            foreach (var layer in ctrl.layers)
                foreach (var st in layer.stateMachine.states)
                    if (st.state.motion != null) { st.state.motion = Fresh(st.state.motion); EditorUtility.SetDirty(st.state); }
            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(MeshyBodyTools.ControllerPath, ImportAssetOptions.ForceUpdate);
            return $"Relink: {n} clip slots pointed at the fresh clips, controller saved.";
        }

        /// Every state and blend-tree slot of the body's controller still has its clip (re-exports must not unlink them).
        static string CheckController()
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(MeshyBodyTools.ControllerPath);
            if (ctrl == null) return "Controller " + MeshyBodyTools.ControllerPath + " not found.";
            int ok = 0;
            var missing = new System.Collections.Generic.List<string>();
            var detail = new System.Collections.Generic.List<string>();
            void Check(string where, Motion m)
            {
                if (m is UnityEditor.Animations.BlendTree bt)
                {
                    foreach (var ch in bt.children) Check($"{where}/{bt.name}", ch.motion);
                    return;
                }
                if (m == null) missing.Add(where);
                else
                {
                    ok++;
                    if (m is AnimationClip cl && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(cl, out string g, out long id))
                        detail.Add($"{where}={cl.name}#{id}{(g == AssetDatabase.AssetPathToGUID(MeshyBodyTools.ClipSource) ? "" : " (other file " + AssetDatabase.GUIDToAssetPath(g) + ")")} {cl.length:0.00}s human {cl.humanMotion}");
                    if (m is AnimationClip c && (!c.humanMotion || c.length <= 0f || c.empty))
                        missing.Add($"{where}={c.name}(human {c.humanMotion}, {c.length:0.00} s, empty {c.empty})");
                }
            }
            foreach (var layer in ctrl.layers)
                foreach (var st in layer.stateMachine.states)
                    Check($"{layer.name}/{st.state.name}", st.state.motion);
            return $"Controller {ctrl.name}: {ok} clips linked" + (missing.Count > 0 ? $", MISSING: {string.Join(", ", missing)}" : ", none missing.") +
                   "\n  " + string.Join("\n  ", detail);
        }
    }
}
