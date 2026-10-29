using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ZombieRush.EditorTools
{
    /// Zombie Rush > Zombies > Give Walker The Full Animation Set
    ///
    /// The Walker (Undead Construction model) only had one walking clip, so it could not
    /// attack, flinch or die - on death the code froze its walk pose and tipped it over.
    /// This switches its model to a Humanoid rig and gives it Zombie_Yaku.controller, so it
    /// shares the Yaku's Mixamo clips: idle, walk, run, attack, hit and die.
    ///   1. Walker FBX rig -> Humanoid, avatar created from the model (reverted if Unity
    ///      can't build a valid humanoid avatar from its bones).
    ///   2. Assets/Prefabs/Zombie.prefab -> Animator uses Zombie_Yaku.controller + the new avatar.
    ///   3. Walkers placed directly in the open scene get the same (undoable; save the scene).
    public static class ZombieAnimationTools
    {
        const string WalkerFbx = "Assets/Models/2026-09-15_UndeadConstruction/Meshy_AI_Undead_Construction_W_biped_Animation_Walking_withSkin.fbx";
        const string WalkerController = "Assets/Models/2026-09-15_UndeadConstruction/Zombie_Walk.controller";
        const string FullController = "Assets/Models/2026-09-25_Yaku/Zombie_Yaku.controller";
        const string WalkerPrefab = "Assets/Prefabs/Zombie.prefab";

        [MenuItem("Zombie Rush/Zombies/Give Walker The Full Animation Set")]
        static void Run()
        {
            var importer = AssetImporter.GetAtPath(WalkerFbx) as ModelImporter;
            var full = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(FullController);
            var oldController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(WalkerController);
            if (importer == null || full == null)
            {
                EditorUtility.DisplayDialog("Walker animations", $"Could not find:\n{(importer == null ? WalkerFbx : FullController)}", "OK");
                return;
            }

            // 1. Humanoid rig, with the Meshy bone names mapped explicitly (Unity's auto-mapper
            //    doesn't recognise Spine01/Spine02/neck and gives up).
            var previousType = importer.animationType;
            var previousSetup = importer.avatarSetup;
            var previousDescription = importer.humanDescription;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(WalkerFbx);
            HumanDescription description = default;
            string missing = "(the model could not be loaded)";
            if (model == null || !TryBuildDescription(model.transform, previousDescription, out description, out missing))
            {
                EditorUtility.DisplayDialog("Walker animations", $"The Walker model is missing bones needed for a Humanoid rig:\n{missing}\n\nNothing was changed.", "OK");
                return;
            }
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.humanDescription = description;
            importer.SaveAndReimport();

            Avatar avatar = null;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(WalkerFbx))
                if (asset is Avatar a) { avatar = a; break; }
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                importer.animationType = previousType;
                importer.avatarSetup = previousSetup;
                importer.humanDescription = previousDescription;
                importer.SaveAndReimport();
                EditorUtility.DisplayDialog("Walker animations",
                    "Unity could not build a valid Humanoid avatar from the Walker's bones, so nothing was changed.\n\n" +
                    "Open the Walker FBX > Rig > Humanoid > Configure and map the red bones by hand, then run this again.", "OK");
                return;
            }

            // 2. The Walker prefab
            int prefabAnimators = 0;
            var root = PrefabUtility.LoadPrefabContents(WalkerPrefab);
            try
            {
                foreach (var animator in root.GetComponentsInChildren<Animator>(true))
                {
                    animator.runtimeAnimatorController = full;
                    animator.avatar = avatar;
                    animator.applyRootMotion = false;
                    prefabAnimators++;
                }
                PrefabUtility.SaveAsPrefabAsset(root, WalkerPrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            // 3. Walkers placed straight into the open scene (not prefab instances)
            int sceneAnimators = 0;
            foreach (var animator in Object.FindObjectsByType<Animator>(FindObjectsInactive.Include))
            {
                if (animator.runtimeAnimatorController != oldController || oldController == null) continue;
                Undo.RecordObject(animator, "Give Walker Full Animation Set");
                animator.runtimeAnimatorController = full;
                animator.avatar = avatar;
                animator.applyRootMotion = false;
                EditorUtility.SetDirty(animator);
                sceneAnimators++;
            }
            if (sceneAnimators > 0) EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());

            AssetDatabase.SaveAssets();
            Debug.Log($"[Zombie Rush] Walker now uses Zombie_Yaku.controller (Humanoid): {prefabAnimators} animator(s) in Zombie.prefab, {sceneAnimators} in the open scene." +
                      (sceneAnimators > 0 ? " Save the scene (Ctrl+S)." : ""));
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(WalkerFbx);
            EditorUtility.DisplayDialog("Walker animations",
                $"Done. The Walker now has idle, walk, run, attack, hit and die.\n\nZombie.prefab: {prefabAnimators} animator(s)\nOpen scene: {sceneAnimators} animator(s)" +
                (sceneAnimators > 0 ? "\n\nSave the scene with Ctrl+S." : ""), "OK");
        }

        /// Zombie Rush > Zombies > Keep Zombie Feet On The Ground (foot IK)
        ///
        /// ZombieFootIK (added to every zombie at runtime) plants the feet on uneven ground, but Unity
        /// only calls it when the controller's base layer has "IK Pass" on. The Miner's controller is
        /// an override of this one, so it inherits the setting.
        [MenuItem("Zombie Rush/Zombies/Keep Zombie Feet On The Ground (foot IK)")]
        static void EnableFootIK()
        {
            bool changed = EnableIKPass(FullController);
            EditorUtility.DisplayDialog("Zombie feet", changed
                ? "Done: IK Pass is on for Zombie_Yaku.controller (and the Miner, which uses it).\nZombies now keep their feet on kerbs, ramps and rubble."
                : "IK Pass was already on - nothing to change.", "OK");
        }

        internal static bool EnableIKPass(string controllerPath)
        {
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(controllerPath);
            if (controller == null || controller.layers.Length == 0) return false;
            var layers = controller.layers;
            if (layers[0].iKPass) return false;
            layers[0].iKPass = true;
            controller.layers = layers;
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return true;
        }

        const string ClipFolder = "Assets/Models/2026-09-25_Yaku/Animations";

        /// Zombie Rush > Zombies > Fix Death Animation Height (bake root motion)
        ///
        /// The zombies don't use root motion, so by default Unity throws away the clips' root
        /// movement - including the drop in height while dying, which left corpses lying flat
        /// at standing height ("dying in the air"). Baking root rotation, height and position
        /// into the pose (based on the original clip) keeps that movement in the animation.
        [MenuItem("Zombie Rush/Zombies/Fix Zombie Animations (death on ground, no run jitter)")]
        static void BakeRootMotion()
        {
            int fixedClips = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ClipFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) continue;

                var clips = importer.clipAnimations;
                if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
                bool dying = path.Contains("Dying");
                foreach (var clip in clips)
                {
                    clip.lockRootRotation = true;      // Root Transform Rotation: Bake Into Pose
                    clip.keepOriginalOrientation = true;
                    clip.lockRootHeightY = true;       // Root Transform Position (Y): Bake Into Pose
                    clip.keepOriginalPositionY = true; //   Based Upon: Original - keeps the fall to the floor
                    clip.heightFromFeet = false;
                    // Root Transform Position (XZ): only the death bakes it (so the body falls where the
                    // clip puts it). Walk/run must NOT: Mixamo locomotion travels forward, and baked into
                    // the pose the body slides ahead and snaps back every loop - the "to and fro" glitch.
                    clip.lockRootPositionXZ = dying;
                    clip.keepOriginalPositionXZ = true;
                    if (dying) clip.loopTime = false;  // die once and stay down
                }
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
                fixedClips += clips.Length;
            }
            Debug.Log($"[Zombie Rush] Baked root motion into {fixedClips} zombie animation clip(s) in {ClipFolder}.");
            EditorUtility.DisplayDialog("Zombie animations", $"Done: {fixedClips} clip(s) updated.\n\nDead zombies sink to the ground, and walking/running stays in place without jitter.", "OK");
        }

        // Unity humanoid bone -> Walker (Meshy biped) bone. Neck/chest/shoulders/toes are optional.
        // The Meshy spine is numbered top-down: Hips > Spine02 > Spine01 > Spine > neck/shoulders.
        static readonly (string human, string bone, bool required)[] BoneMap =
        {
            ("Hips", "Hips", true), ("Spine", "Spine02", true), ("Chest", "Spine01", false), ("UpperChest", "Spine", false),
            ("Neck", "neck", false), ("Head", "Head", true),
            ("LeftShoulder", "LeftShoulder", false), ("LeftUpperArm", "LeftArm", true), ("LeftLowerArm", "LeftForeArm", true), ("LeftHand", "LeftHand", true),
            ("RightShoulder", "RightShoulder", false), ("RightUpperArm", "RightArm", true), ("RightLowerArm", "RightForeArm", true), ("RightHand", "RightHand", true),
            ("LeftUpperLeg", "LeftUpLeg", true), ("LeftLowerLeg", "LeftLeg", true), ("LeftFoot", "LeftFoot", true), ("LeftToes", "LeftToeBase", false),
            ("RightUpperLeg", "RightUpLeg", true), ("RightLowerLeg", "RightLeg", true), ("RightFoot", "RightFoot", true), ("RightToes", "RightToeBase", false),
        };

        internal static bool TryBuildDescription(Transform root, HumanDescription template, out HumanDescription description, out string missing,
                                                 (string human, string bone, bool required)[] boneMap = null)
        {
            description = template;
            var map = boneMap ?? BoneMap;
            // Keyed by the name without any "prefix:" (Mixamo rigs use "mixamorig:Hips").
            var byName = new System.Collections.Generic.Dictionary<string, Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string key = t.name.Substring(t.name.LastIndexOf(':') + 1);
                if (!byName.ContainsKey(key)) byName.Add(key, t);
            }

            var human = new System.Collections.Generic.List<HumanBone>();
            var lacking = new System.Collections.Generic.List<string>();
            foreach (var (humanName, bone, required) in map)
            {
                if (!byName.TryGetValue(bone, out var found)) { if (required) lacking.Add(bone); continue; }
                var hb = new HumanBone { humanName = humanName, boneName = found.name };
                hb.limit.useDefaultValues = true;
                human.Add(hb);
            }
            missing = string.Join(", ", lacking);
            if (lacking.Count > 0) return false;

            // Skeleton = the model's own rest pose, every transform in the file.
            var skeleton = new System.Collections.Generic.List<SkeletonBone>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                skeleton.Add(new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale });

            description.human = human.ToArray();
            description.skeleton = skeleton.ToArray();
            description.upperArmTwist = 0.5f; description.lowerArmTwist = 0.5f;
            description.upperLegTwist = 0.5f; description.lowerLegTwist = 0.5f;
            description.armStretch = 0.05f; description.legStretch = 0.05f;
            description.feetSpacing = 0f;
            description.hasTranslationDoF = false;
            return true;
        }
    }
}
