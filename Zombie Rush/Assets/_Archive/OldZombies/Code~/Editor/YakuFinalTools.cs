using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using ZombieRush.Data;
using ZombieRush.Zombie;

namespace ZombieRush.EditorTools
{
    /// Zombie Rush > Zombies > Set Up Yaku (final model, motion capture):
    ///   - Assets/Models/2026-11-08_Yaku_Final/Yaku.fbx as a Humanoid (standard Mixamo skeleton),
    ///     one URP material from its Yaku_* textures,
    ///   - real motion-capture zombie clips (Assets/Models/2026-09-25_Yaku/Animations: idle, walk, run,
    ///     punch, hit, dying) in its own controller, walk and run sped to match the Yaku's
    ///     move and chase speeds so the feet do not slide,
    ///   - Zombie_Yaku.prefab gets the new body (the previous one is kept in Prefabs/Legacy).
    /// Marker: Temp/ZR_RunYakuFinal (report: Temp/ZR_YakuFinal.log).
    static class YakuFinalTools
    {
        const string Folder = "Assets/Models/2026-11-08_Yaku_Final";
        const string Fbx = Folder + "/Yaku.fbx";
        const string MocapFolder = "Assets/Models/2026-09-25_Yaku/Animations";
        const string BaseController = "Assets/Models/2026-09-25_Yaku/Zombie_Yaku.controller";
        const string ControllerPath = Folder + "/Yaku_Final.controller";
        const string MaterialPath = Folder + "/Mat_Yaku_Final.mat";
        const string Prefab = "Assets/Prefabs/Zombie_Yaku.prefab";
        const string LegacyCopy = "Assets/Prefabs/Legacy/Zombie_Yaku_Blender.prefab";
        const string ConfigPath = "Assets/Data/ZombieConfig_Yaku.asset";
        const float TargetHeight = 1.85f;
        const float GlowIntensity = 0.1f; // normal green glow (1 = the old full glow); frenzy still flares

        // Controller state -> motion-capture clip file (in MocapFolder).
        static readonly Dictionary<string, string> Mocap = new Dictionary<string, string>
        {
            { "Idle", "Zombie Idle" }, { "Walk", "Walking" }, { "Run", "Zombie Run" },
            { "Attack", "Zombie Punching" }, { "Hit", "Zombie Reaction Hit" }, { "Die", "Zombie Dying" },
        };
        static readonly HashSet<string> Loops = new HashSet<string> { "Zombie Idle", "Walking", "Zombie Run" };

        // Mixamo bone (after "mixamorig:") -> Unity humanoid bone.
        static readonly (string bone, string human)[] BoneMap =
        {
            ("Hips", "Hips"), ("Spine", "Spine"), ("Spine1", "Chest"), ("Spine2", "UpperChest"), ("Neck", "Neck"), ("Head", "Head"),
            ("LeftShoulder", "LeftShoulder"), ("LeftArm", "LeftUpperArm"), ("LeftForeArm", "LeftLowerArm"), ("LeftHand", "LeftHand"),
            ("RightShoulder", "RightShoulder"), ("RightArm", "RightUpperArm"), ("RightForeArm", "RightLowerArm"), ("RightHand", "RightHand"),
            ("LeftUpLeg", "LeftUpperLeg"), ("LeftLeg", "LeftLowerLeg"), ("LeftFoot", "LeftFoot"), ("LeftToeBase", "LeftToes"),
            ("RightUpLeg", "RightUpperLeg"), ("RightLeg", "RightLowerLeg"), ("RightFoot", "RightFoot"), ("RightToeBase", "RightToes"),
            ("LeftHandThumb1", "Left Thumb Proximal"), ("LeftHandThumb2", "Left Thumb Intermediate"), ("LeftHandThumb3", "Left Thumb Distal"),
            ("LeftHandIndex1", "Left Index Proximal"), ("LeftHandIndex2", "Left Index Intermediate"), ("LeftHandIndex3", "Left Index Distal"),
            ("LeftHandMiddle1", "Left Middle Proximal"), ("LeftHandMiddle2", "Left Middle Intermediate"), ("LeftHandMiddle3", "Left Middle Distal"),
            ("LeftHandRing1", "Left Ring Proximal"), ("LeftHandRing2", "Left Ring Intermediate"), ("LeftHandRing3", "Left Ring Distal"),
            ("LeftHandPinky1", "Left Little Proximal"), ("LeftHandPinky2", "Left Little Intermediate"), ("LeftHandPinky3", "Left Little Distal"),
            ("RightHandThumb1", "Right Thumb Proximal"), ("RightHandThumb2", "Right Thumb Intermediate"), ("RightHandThumb3", "Right Thumb Distal"),
            ("RightHandIndex1", "Right Index Proximal"), ("RightHandIndex2", "Right Index Intermediate"), ("RightHandIndex3", "Right Index Distal"),
            ("RightHandMiddle1", "Right Middle Proximal"), ("RightHandMiddle2", "Right Middle Intermediate"), ("RightHandMiddle3", "Right Middle Distal"),
            ("RightHandRing1", "Right Ring Proximal"), ("RightHandRing2", "Right Ring Intermediate"), ("RightHandRing3", "Right Ring Distal"),
            ("RightHandPinky1", "Right Little Proximal"), ("RightHandPinky2", "Right Little Intermediate"), ("RightHandPinky3", "Right Little Distal"),
        };

        [MenuItem("Zombie Rush/Zombies/Set Up Yaku (final model, motion capture)", priority = 20)]
        static void Menu() => Debug.Log("Zombie Rush: " + Run());

        public static string Run()
        {
            var report = new StringBuilder();
            AssetDatabase.Refresh();

            // ---- Model: Humanoid, explicit Mixamo map, no imported materials ----------------------
            var imp = AssetImporter.GetAtPath(Fbx) as ModelImporter;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            if (imp == null || asset == null) return "FAILED: " + Fbx + " not found.";
            var transforms = asset.GetComponentsInChildren<Transform>(true);
            string Find(string suffix) => transforms.Select(t => t.name).FirstOrDefault(n => n == "mixamorig:" + suffix || n == suffix);
            var human = BoneMap.Select(m => (name: Find(m.bone), m.human)).Where(m => m.name != null)
                .Select(m => new HumanBone { boneName = m.name, humanName = m.human, limit = new HumanLimit { useDefaultValues = true } }).ToArray();
            var hd = imp.humanDescription;
            hd.human = human;
            hd.skeleton = TPoseSkeleton(asset);
            hd.upperArmTwist = 0.5f; hd.lowerArmTwist = 0.5f; hd.upperLegTwist = 0.5f; hd.lowerLegTwist = 0.5f;
            hd.armStretch = 0.05f; hd.legStretch = 0.05f; hd.feetSpacing = 0f; hd.hasTranslationDoF = false;
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.humanDescription = hd;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman) return "FAILED: Yaku humanoid avatar not valid (see Console).";
            report.AppendLine($"Avatar: humanoid, {human.Length} bones mapped.");
            // Rest pose check: Unity treats the file's rest pose as the T-pose for retargeting.
            {
                var temp = Object.Instantiate(asset);
                try
                {
                    temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    Transform B(string n) => temp.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "mixamorig:" + n);
                    float Below(Transform a, Transform b) => a && b ? Vector3.Angle(b.position - a.position, Vector3.ProjectOnPlane(b.position - a.position, Vector3.up)) * Mathf.Sign(a.position.y - b.position.y) : float.NaN;
                    float Lean(Transform a, Transform b) => a && b ? Vector3.Angle(b.position - a.position, Vector3.up) : float.NaN;
                    report.AppendLine($"Rest pose: left arm {Below(B("LeftArm"), B("LeftForeArm")):0}° below horizontal, right arm {Below(B("RightArm"), B("RightForeArm")):0}°, " +
                                      $"spine {Lean(B("Hips"), B("Spine2")):0}° off vertical, neck/head {Lean(B("Neck"), B("Head")):0}° off vertical, legs {Lean(B("LeftFoot"), B("LeftUpLeg")):0}° off vertical.");
                }
                finally { Object.DestroyImmediate(temp); }
            }

            // ---- Motion-capture clips: loop the cycles, keep them in place --------------------------
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var kv in Mocap)
            {
                string path = $"{MocapFolder}/{kv.Value}.fbx";
                if (AssetImporter.GetAtPath(path) is ModelImporter mi)
                {
                    var takes = mi.defaultClipAnimations;
                    bool loop = Loops.Contains(kv.Value);
                    foreach (var c in takes)
                    {
                        c.loopTime = loop; c.loopPose = loop;
                        c.lockRootRotation = true; c.keepOriginalOrientation = true;
                        c.lockRootHeightY = true; c.keepOriginalPositionY = true;
                        c.lockRootPositionXZ = false; // forward travel stays root motion, which the game ignores
                    }
                    mi.clipAnimations = takes;
                    mi.SaveAndReimport();
                }
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
                if (clip == null) { report.AppendLine($"MISSING clip {path}"); continue; }
                clips[kv.Key] = clip;
            }

            // ---- Controller: a copy of the zombie controller with these clips and matched speeds ----
            if (!File.Exists(ControllerPath)) AssetDatabase.CopyAsset(BaseController, ControllerPath);
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            var cfg = AssetDatabase.LoadAssetAtPath<ZombieConfigData>(ConfigPath);
            float walk = cfg != null ? cfg.moveSpeed : 2f, run = cfg != null ? cfg.chaseSpeed : 3.2f;
            foreach (var layer in ctrl.layers)
                foreach (var s in layer.stateMachine.states)
                {
                    if (!clips.TryGetValue(s.state.name, out var clip)) continue;
                    s.state.motion = clip;
                    float target = s.state.name == "Walk" ? walk : s.state.name == "Run" ? run : 0f;
                    if (target > 0f)
                    {
                        // Motion capture travels forward: its own root speed is the true ground speed.
                        float foot = MeshyBodyTools.MeasureGroundSpeed(asset, clip, TargetHeight);
                        float travel = RootSpeed(asset, clip);
                        // Clips downloaded "in place" barely travel; for those the foot measure is right.
                        float ground = travel > 0.5f * foot ? travel : foot;
                        report.AppendLine($"  {s.state.name}: root travel {travel:0.00} m/s, foot measure {foot:0.00} m/s.");
                        float rate = ground > 0.2f ? Mathf.Clamp(target / ground, 0.6f, 1.8f) : 1f;
                        s.state.speed = rate;
                        report.AppendLine($"{s.state.name}: '{clip.name}' covers {ground:0.00} m/s, zombie {target} m/s -> plays x{rate:0.00}.");
                    }
                    else { s.state.speed = 1f; report.AppendLine($"{s.state.name}: '{clip.name}'."); }
                }
            EditorUtility.SetDirty(ctrl);

            // ---- Material ---------------------------------------------------------------------------
            var mat = BuildMaterial();

            // ---- Prefab: new body ---------------------------------------------------------------------
            if (!File.Exists(LegacyCopy)) AssetDatabase.CopyAsset(Prefab, LegacyCopy);
            var root = PrefabUtility.LoadPrefabContents(Prefab);
            try
            {
                var model = CharacterSetupTools.SwapModel(root, asset, mat);
                var anim = model.GetComponent<Animator>();
                anim.runtimeAnimatorController = ctrl;
                var visual = root.GetComponent<ZombieVisual>();
                if (visual != null) { visual.controller = ctrl; visual.materialOverride = null; }
                // The green glow made the realistic body look like a toy: keep it faint, but let a
                // Screamer's frenzy still flare it.
                var glow = root.GetComponent<ZombieGlow>();
                if (glow != null) { glow.intensity = GlowIntensity; glow.pulse = 0.15f; }

                // Height, then stand on the ground in the idle pose.
                var rs = model.GetComponentsInChildren<Renderer>(true);
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                float s = b.size.y > 0.01f ? TargetHeight / b.size.y : 1f;
                model.transform.localScale = Vector3.one * s;
                model.transform.localPosition = Vector3.zero;
                if (clips.TryGetValue("Idle", out var idle))
                {
                    idle.SampleAnimation(model, 0f);
                    float lowest = float.MaxValue;
                    foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        var baked = new Mesh();
                        smr.BakeMesh(baked, true);
                        foreach (var v in baked.vertices) lowest = Mathf.Min(lowest, (smr.transform.position + smr.transform.rotation * v).y);
                        Object.DestroyImmediate(baked);
                    }
                    if (lowest < float.MaxValue) model.transform.localPosition = new Vector3(0f, -(lowest - model.transform.position.y), 0f);
                }
                report.AppendLine($"Body: {b.size.y:0.00} m as exported, scaled x{s:0.###} to {TargetHeight} m, ground offset {model.transform.localPosition.y:0.000} m.");
                PrefabUtility.SaveAsPrefabAsset(root, Prefab);
                report.AppendLine($"Zombie_Yaku.prefab: new body (previous kept as {Path.GetFileName(LegacyCopy)}).");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            AssetDatabase.SaveAssets();
            return report.ToString();
        }

        /// The file's rest skeleton straightened into a T-pose (spine and neck upright, arms straight
        /// out to the sides, legs straight down). Unity treats this pose as the zero pose that every
        /// humanoid animation is measured from; the exported rest pose has the arms hanging and the
        /// head tipped forward, which would bend every animation the same way.
        static SkeletonBone[] TPoseSkeleton(GameObject asset)
        {
            var temp = Object.Instantiate(asset);
            try
            {
                temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Transform B(string n) => temp.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "mixamorig:" + n);
                void Aim(Transform bone, Transform child, Vector3 dir)
                {
                    if (bone == null || child == null) return;
                    Vector3 cur = child.position - bone.position;
                    if (cur.sqrMagnitude > 1e-8f) bone.rotation = Quaternion.FromToRotation(cur, dir) * bone.rotation;
                }
                Aim(B("Spine"), B("Spine1"), Vector3.up);
                Aim(B("Spine1"), B("Spine2"), Vector3.up);
                Aim(B("Spine2"), B("Neck"), Vector3.up);
                Aim(B("Neck"), B("Head"), Vector3.up);
                var la = B("LeftArm"); var ra = B("RightArm");
                Vector3 right = Vector3.right;
                if (la != null && ra != null) { right = ra.position - la.position; right.y = 0f; right.Normalize(); }
                foreach (var (side, dir) in new[] { ("Left", -right), ("Right", right) })
                {
                    Aim(B(side + "Arm"), B(side + "ForeArm"), dir);
                    Aim(B(side + "ForeArm"), B(side + "Hand"), dir);
                    Aim(B(side + "UpLeg"), B(side + "Leg"), Vector3.down);
                    Aim(B(side + "Leg"), B(side + "Foot"), Vector3.down);
                }
                return temp.GetComponentsInChildren<Transform>(true)
                    .Select(x => new SkeletonBone { name = x == temp.transform ? asset.name : x.name, position = x.localPosition, rotation = x.localRotation, scale = x.localScale })
                    .ToArray();
            }
            finally { Object.DestroyImmediate(temp); }
        }

        /// The clip's forward travel in game metres per second: Unity's root-motion average speed
        /// (humanoid units) times the avatar's human scale at the prefab's size.
        static float RootSpeed(GameObject asset, AnimationClip clip)
        {
            var temp = Object.Instantiate(asset);
            try
            {
                var anim = temp.GetComponent<Animator>();
                if (anim == null || !anim.isHuman) return 0f;
                var rs = temp.GetComponentsInChildren<Renderer>(true);
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                float scale = b.size.y > 0.01f ? TargetHeight / b.size.y : 1f;
                Vector3 v = clip.averageSpeed; v.y = 0f;
                return v.magnitude * anim.humanScale * scale;
            }
            finally { Object.DestroyImmediate(temp); }
        }

        static Material BuildMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            void Tex(string file, bool normal, bool srgb)
            {
                if (!(AssetImporter.GetAtPath($"{Folder}/{file}") is TextureImporter ti)) return;
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = srgb;
                ti.SaveAndReimport();
            }
            Tex("Yaku_Albedo.png", false, true);
            Tex("Yaku_Normal.png", true, false);
            Tex("Yaku_MetalSmooth.png", false, false);
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Folder}/Yaku_Albedo.png");
            var normalMap = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Folder}/Yaku_Normal.png");
            var ms = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Folder}/Yaku_MetalSmooth.png");
            mat.SetTexture("_BaseMap", albedo);
            mat.SetTexture("_MainTex", albedo);
            mat.SetColor("_BaseColor", Color.white);
            if (normalMap != null) { mat.SetTexture("_BumpMap", normalMap); mat.SetFloat("_BumpScale", 1f); mat.EnableKeyword("_NORMALMAP"); }
            if (ms != null)
            {
                mat.SetTexture("_MetallicGlossMap", ms);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetFloat("_SmoothnessTextureChannel", 0f);
                mat.SetFloat("_Smoothness", 1f);
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
