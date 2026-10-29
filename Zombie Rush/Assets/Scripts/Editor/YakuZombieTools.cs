using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZombieRush.Data;
using ZombieRush.Zombie;

namespace ZombieRush.EditorTools
{
    /// Zombie Rush > Zombies > Refine Yaku (own animations + grimy texture)
    ///
    /// Gives the Yaku the same treatment as the Miner:
    ///   1. its own animations (YakuAnimationAuthoring: twitchy idle, drunken stagger, clawing
    ///      sprint, lunge-grab-bite, head-snap flinch) baked on its own skeleton, in
    ///      Yaku.overrideController (the Mixamo death stays);
    ///   2. the enhanced texture set (Docs/Textures/MinerTexGen, "yaku" mode): grime, rot under the
    ///      tattoos, wet blood, plus a smoothness map it never had;
    ///   3. the bite lands its damage at the moment of the bite, not when the lunge starts;
    ///   4. foot IK pass on (feet stay on uneven ground).
    /// Run again any time; it rebuilds everything. Mat_Yaku keeps working without the new files.
    public static class YakuZombieTools
    {
        const string Folder = "Assets/Models/2026-09-25_Yaku";
        const string Fbx = Folder + "/Meshy_AI_Yaku_J_Ignite_0925161629_texture.fbx";
        const string Controller = Folder + "/Zombie_Yaku.controller";
        const string OverridePath = Folder + "/Yaku.overrideController";
        const string MaterialPath = Folder + "/Mat_Yaku.mat";
        const string Albedo = Folder + "/texture_1_Enhanced.png";
        const string MetalSmooth = Folder + "/texture_1_MetallicSmoothness.png";
        const string Normal = Folder + "/texture_1_normal.png";
        const string Prefab = "Assets/Prefabs/Zombie_Yaku.prefab";
        const string ConfigPath = "Assets/Data/ZombieConfig_Yaku.asset";

        [MenuItem("Zombie Rush/Zombies/Refine Yaku (own animations + grimy texture)", priority = 21)]
        static void Run()
        {
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Controller);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var avatar = AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<Avatar>().FirstOrDefault();
            if (controller == null || asset == null || avatar == null || !avatar.isHuman)
            {
                EditorUtility.DisplayDialog("Yaku", $"Missing or not Humanoid:\n{Fbx}\n{Controller}", "OK");
                return;
            }
            ZombieAnimationTools.EnableIKPass(Controller);

            // 1. Animations.
            var model = Object.Instantiate(asset);
            Dictionary<string, AnimationClip> clips;
            try
            {
                model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                clips = YakuAnimationAuthoring.BakeAll(model, avatar);
            }
            finally { Object.DestroyImmediate(model); }
            if (clips.Count == 0)
            {
                EditorUtility.DisplayDialog("Yaku", "Animation authoring failed - see the Console. Nothing was changed.", "OK");
                return;
            }
            var ov = Override(controller, clips);

            string prefabNote = "Zombie_Yaku.prefab not found.";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Prefab) != null)
            {
                var root = PrefabUtility.LoadPrefabContents(Prefab);
                try
                {
                    var visual = root.GetComponent<ZombieVisual>();
                    if (visual != null) visual.controller = ov;
                    else foreach (var a in root.GetComponentsInChildren<Animator>(true)) a.runtimeAnimatorController = ov;
                    PrefabUtility.SaveAsPrefabAsset(root, Prefab);
                    prefabNote = "Zombie_Yaku.prefab uses Yaku.overrideController.";
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }

            // 2. Texture.
            string texNote = FillMaterial();

            // 3. The bite.
            var config = AssetDatabase.LoadAssetAtPath<ZombieConfigData>(ConfigPath);
            if (config != null)
            {
                config.attackHitDelay = YakuAnimationAuthoring.AttackBite;
                config.attackCooldown = Mathf.Max(config.attackCooldown, YakuAnimationAuthoring.AttackLength + 0.2f);
                EditorUtility.SetDirty(config);
            }
            AssetDatabase.SaveAssets();

            string msg = $"Animations: its own {string.Join(", ", clips.Keys)} (in {YakuAnimationAuthoring.ClipFolder}); death stays Mixamo.\n" +
                         $"{prefabNote}\n{texNote}\nBite damage lands {YakuAnimationAuthoring.AttackBite:0.00} s into the attack.";
            Debug.Log("[Zombie Rush] Yaku refined.\n" + msg);
            EditorUtility.DisplayDialog("Yaku", "Done.\n\n" + msg + "\n\nTry it: Play > Zombie Rush > Test > Spawn Yaku In Front Of Me.", "OK");
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
        }

        /// Zombie_Yaku.controller with each authored state's clip swapped in.
        static AnimatorOverrideController Override(RuntimeAnimatorController baseController, Dictionary<string, AnimationClip> states)
        {
            var swaps = new Dictionary<AnimationClip, AnimationClip>();
            if (baseController is UnityEditor.Animations.AnimatorController ac)
                foreach (var layer in ac.layers)
                    foreach (var s in layer.stateMachine.states)
                        if (s.state.motion is AnimationClip c && states.TryGetValue(s.state.name, out var mine) && mine != null)
                            swaps[c] = mine;

            var ov = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(OverridePath);
            if (ov == null)
            {
                ov = new AnimatorOverrideController(baseController) { name = "Yaku" };
                AssetDatabase.CreateAsset(ov, OverridePath);
            }
            ov.runtimeAnimatorController = baseController;
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            ov.GetOverrides(pairs);
            for (int i = 0; i < pairs.Count; i++)
                pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(pairs[i].Key, swaps.TryGetValue(pairs[i].Key, out var m) ? m : null);
            ov.ApplyOverrides(pairs);
            EditorUtility.SetDirty(ov);
            return ov;
        }

        static string FillMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(Albedo);
            if (mat == null) return "Mat_Yaku not found - texture unchanged.";
            if (albedo == null) return "texture_1_Enhanced.png not found - texture unchanged.";

            foreach (var (path, linear) in new[] { (Albedo, false), (MetalSmooth, true) })
            {
                if (!(AssetImporter.GetAtPath(path) is TextureImporter imp)) continue;
                if (imp.sRGBTexture == !linear && imp.textureCompression == TextureImporterCompression.CompressedHQ) continue;
                imp.sRGBTexture = !linear;
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.SaveAndReimport();
            }

            Undo.RecordObject(mat, "Refine Yaku texture");
            mat.SetTexture("_BaseMap", albedo);
            mat.SetTexture("_MainTex", albedo);
            var ms = AssetDatabase.LoadAssetAtPath<Texture2D>(MetalSmooth);
            if (ms != null)
            {
                // R metal, A smoothness: wet blood shines, grime is matte, skin a little clammy.
                mat.SetTexture("_MetallicGlossMap", ms);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetFloat("_Smoothness", 0.6f);
                mat.SetFloat("_SmoothnessTextureChannel", 0f);
            }
            if (mat.GetTexture("_BumpMap") == null)
            {
                var n = AssetDatabase.LoadAssetAtPath<Texture2D>(Normal);
                if (n != null) { mat.SetTexture("_BumpMap", n); mat.EnableKeyword("_NORMALMAP"); }
            }
            mat.SetFloat("_BumpScale", 1.1f);
            EditorUtility.SetDirty(mat);
            return "Texture: grimy enhanced set on Mat_Yaku (original texture_1.png kept).";
        }
    }
}
