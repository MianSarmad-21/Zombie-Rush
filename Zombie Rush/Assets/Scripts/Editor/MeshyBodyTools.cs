using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using ZombieRush.Player;

namespace ZombieRush.EditorTools
{
    /// Zombie Rush > Player > Set Up Meshy Body: turns a Meshy AI "unity humanoid" export into the
    /// player's body and puts it on the Player:
    ///   - Humanoid avatar with an explicit bone map and a T-pose reference (Meshy exports often
    ///     stand in an A-pose, which would make every retargeted animation hold its arms too low),
    ///   - URP material from the Meshy textures,
    ///   - its own player controller built from the Meshy clips (see Slots), strafe/back clips sped up to the player's speed,
    ///   - prefab in Assets/Prefabs/Characters, scaled to `TargetHeight`, facing +Z,
    ///   - then PlayerViewTools.Run puts it on the Player with the aim rig.
    /// Marker: Temp/ZR_RunMeshyBody (report: Temp/ZR_MeshyBody.log).
    static class MeshyBodyTools
    {
        public const string Folder = "Assets/Models/2026-11-08_WastelandSurvivor_Final";
        public const string Name = "WastelandSurvivor";
        public const string PrefabPath = "Assets/Prefabs/Characters/WastelandSurvivor.prefab";
        /// The body's Animator controller (rebuilt by each body setup run).
        public static string ControllerPath => $"{Folder}/{Name}.controller";
        const float TargetHeight = 1.9f;

        // Meshy bone name -> Unity humanoid bone.
        static readonly (string bone, string human)[] BoneMap =
        {
            ("Hips", "Hips"), ("Chest", "Spine"), ("UpperChest", "Chest"), ("Neck", "Neck"), ("Head", "Head"),
            ("LeftShoulder", "LeftShoulder"), ("LeftUpperArm", "LeftUpperArm"), ("LeftLowerArm", "LeftLowerArm"), ("LeftHand", "LeftHand"),
            ("RightShoulder", "RightShoulder"), ("RightUpperArm", "RightUpperArm"), ("RightLowerArm", "RightLowerArm"), ("RightHand", "RightHand"),
            ("LeftUpperLeg", "LeftUpperLeg"), ("LeftLowerLeg", "LeftLowerLeg"), ("LeftFoot", "LeftFoot"), ("LeftToes", "LeftToes"),
            ("RightUpperLeg", "RightUpperLeg"), ("RightLowerLeg", "RightLowerLeg"), ("RightFoot", "RightFoot"), ("RightToes", "RightToes"),
            // Blender/survivor_fingers.py: a 3-joint thumb and a 3-joint chain for the (one-piece) finger block
            ("LeftHandThumb1", "Left Thumb Proximal"), ("LeftHandThumb2", "Left Thumb Intermediate"), ("LeftHandThumb3", "Left Thumb Distal"),
            ("LeftHandIndex1", "Left Index Proximal"), ("LeftHandIndex2", "Left Index Intermediate"), ("LeftHandIndex3", "Left Index Distal"),
            ("RightHandThumb1", "Right Thumb Proximal"), ("RightHandThumb2", "Right Thumb Intermediate"), ("RightHandThumb3", "Right Thumb Distal"),
            ("RightHandIndex1", "Right Index Proximal"), ("RightHandIndex2", "Right Index Intermediate"), ("RightHandIndex3", "Right Index Distal"),
        };

        // Clips that cycle.
        static readonly HashSet<string> LoopClips = new HashSet<string>
            { "Idle", "Idle_Aim", "Walk", "Run", "Sprint", "Crouch_Idle", "Crouch_Walk", "Fall", "Downed", "Run_Reload", "Strafe_Left", "Strafe_Right", "Run_Back",
              "Run_FwdLeft", "Run_FwdRight", "Run_BackLeft", "Run_BackRight" };

        // Player controller slot -> Meshy clip. Walk speed (PlayerJogSpeed) is a jog and sprint speed (8 m/s)
        // a sprint, so the walk and run slots take Run and Sprint.
        static readonly Dictionary<string, string> Slots = new Dictionary<string, string>
        {
            { "Idle", "Idle_Aim" }, { "Walk", "Run" }, { "Run", "Sprint" },
            { "StrafeLeft", "Strafe_Left" }, { "StrafeRight", "Strafe_Right" },
            { "FwdLeft", "Run_FwdLeft" }, { "FwdRight", "Run_FwdRight" }, { "BackLeft", "Run_BackLeft" }, { "BackRight", "Run_BackRight" },
            { "CrouchIdle", "Crouch_Idle" }, { "CrouchWalk", "Crouch_Walk" },
            { "Air", "Fall" }, { "Die", "Death" }, { "Fire", "Fire" }, { "Reload", "Reload" }, { "Hit", "Hit_Reaction" },
        };
        // Slots that are simply left out when the model has no clip for them.
        static readonly HashSet<string> Optional = new HashSet<string> { "FwdLeft", "FwdRight", "BackLeft", "BackRight" };
        // The player's normal move speed (PlayerController.walkSpeed), m/s.
        const float PlayerJogSpeed = 4f;
        const string SurvivorClips = "Assets/Models/2026-11-05_Characters/ZR_Survivor/Animations";

        /// "Armature|Idle" -> "Idle".
        static string ClipName(string n) => n.Contains("|") ? n.Substring(n.LastIndexOf('|') + 1) : n;

        [MenuItem("Zombie Rush/Player/Set Up Meshy Body (Wasteland Survivor)")]
        static void Menu() => Debug.Log("Zombie Rush: " + Run());

        public static string Run()
        {
            var report = new StringBuilder();
            string fbx = $"{Folder}/{Name}.fbx";
            AssetDatabase.Refresh();
            SetTexture($"{Folder}/{Name}_Normal.png", normal: true, srgb: false);
            SetTexture($"{Folder}/{Name}_MetalSmooth.png", normal: false, srgb: false);
            SetTexture($"{Folder}/{Name}_Albedo.png", normal: false, srgb: true);

            var imp = AssetImporter.GetAtPath(fbx) as ModelImporter;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (imp == null || asset == null) return "FAILED: " + fbx + " not found.";

            // ---- Avatar: explicit bone map, T-pose reference --------------------------------
            var skeleton = TPoseSkeleton(asset, report);
            var names = new HashSet<string>(skeleton.Select(s => s.name));
            var missing = BoneMap.Where(m => !names.Contains(m.bone)).Select(m => m.bone).ToArray();
            if (missing.Length > 0) report.AppendLine("Bones not found: " + string.Join(", ", missing));
            var hd = imp.humanDescription;
            hd.human = BoneMap.Where(m => names.Contains(m.bone))
                .Select(m => new HumanBone { boneName = m.bone, humanName = m.human, limit = new HumanLimit { useDefaultValues = true } }).ToArray();
            hd.skeleton = skeleton;
            hd.upperArmTwist = 0.5f; hd.lowerArmTwist = 0.5f; hd.upperLegTwist = 0.5f; hd.lowerLegTwist = 0.5f;
            hd.armStretch = 0.05f; hd.legStretch = 0.05f; hd.feetSpacing = 0f; hd.hasTranslationDoF = false;
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.humanDescription = hd;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;

            // ---- Clips: loop the cycles, keep everything in place --------------------------------
            var clips = imp.defaultClipAnimations;
            foreach (var c in clips)
            {
                bool loop = LoopClips.Contains(ClipName(c.name));
                c.loopTime = loop; c.loopPose = loop;
                c.lockRootRotation = true; c.keepOriginalOrientation = true;
                c.lockRootHeightY = true; c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
            }
            imp.clipAnimations = clips;
            imp.SaveAndReimport();

            var avatar = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman) return report + "FAILED: humanoid avatar is not valid (see Console).";
            var meshyClips = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();
            report.AppendLine($"Avatar: humanoid, {hd.human.Length} bones. Clips: {string.Join(", ", meshyClips.Select(c => c.name))}.");

            // ---- Material ----------------------------------------------------------------------
            var mat = BuildMaterial();

            // ---- Animator: the player controller, built from the Meshy clips --------------------
            AnimationClip Find(string name) => meshyClips.FirstOrDefault(c => ClipName(c.name) == name);
            var slots = new Dictionary<string, AnimationClip>();
            foreach (var kv in Slots)
            {
                var clip = Find(kv.Value);
                if (clip == null && Optional.Contains(kv.Key)) continue;
                if (clip == null)
                {
                    // No Meshy clip: fall back to the code-built Survivor clip for that slot.
                    clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{SurvivorClips}/Survivor_{kv.Key}.anim");
                    report.AppendLine($"{kv.Key}: no '{kv.Value}' clip, using Survivor_{kv.Key}.");
                }
                if (clip != null) slots[kv.Key] = clip;
            }
            var back = Find("Run_Back");
            if (back != null) slots["Back"] = back;
            // Playback speed per slot = the player's speed for that slot / the ground speed the clip's
            // planted foot really covers, so the feet stick to the ground instead of sliding.
            var pc = Object.FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
            if (pc != null && !Mathf.Approximately(pc.walkSpeed, PlayerJogSpeed))
            {
                // A natural jog; faster than this the stride looks rushed.
                Undo.RecordObject(pc, "Player jog speed");
                pc.walkSpeed = PlayerJogSpeed;
                EditorUtility.SetDirty(pc);
                report.AppendLine($"Player jog speed set to {PlayerJogSpeed} m/s.");
            }
            float jog = pc != null ? pc.walkSpeed : 5f, sprint = pc != null ? pc.sprintSpeed : 8f, crouch = pc != null ? pc.crouchSpeed : 2.5f;
            var target = new Dictionary<string, float>
            {
                { "Walk", jog }, { "Run", sprint }, { "StrafeLeft", jog }, { "StrafeRight", jog }, { "Back", jog }, { "CrouchWalk", crouch },
                { "FwdLeft", jog }, { "FwdRight", jog }, { "BackLeft", jog }, { "BackRight", jog },
            };
            var speed = new Dictionary<string, float>();
            foreach (var kv in target)
            {
                if (!slots.TryGetValue(kv.Key, out var clip) || clip == null) continue;
                float ground = MeasureGroundSpeed(asset, clip);
                float rate = ground > 0.2f ? Mathf.Clamp(kv.Value / ground, 0.6f, 1.8f) : 1f;
                speed[kv.Key] = rate;
                report.AppendLine($"Speed: {clip.name} covers {ground:0.00} m/s, player {kv.Value} m/s -> plays x{rate:0.00}.");
            }
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(SurvivorRigTools.UpperBodyMaskPath);
            string ctrlPath = $"{Folder}/{Name}.controller";
            var ctrl = SurvivorRigTools.BuildController(ctrlPath, slots, mask, speed);
            AssetDatabase.DeleteAsset($"{Folder}/{Name}.overrideController"); // replaced by the controller above
            report.AppendLine($"Controller: {ctrlPath}, {slots.Count} slots, backward = {(back != null ? "Run_Back" : "Run reversed")}.");

            // ---- Prefab ------------------------------------------------------------------------
            var root = new GameObject(Name);
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, root.transform);
                model.name = "Model";
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                {
                    r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
                    if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true; // shadow-only in first person
                }

                // Face +Z: right hand to the right of the left one.
                var anim = model.GetComponent<Animator>();
                if (anim == null) anim = model.AddComponent<Animator>();
                anim.avatar = avatar;
                Transform B(string n) => model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
                var l = B("LeftUpperArm"); var rt = B("RightUpperArm");
                if (l != null && rt != null)
                {
                    Vector3 fwd = Vector3.Cross(rt.position - l.position, Vector3.up);
                    fwd.y = 0f;
                    float yaw = Vector3.SignedAngle(fwd, Vector3.forward, Vector3.up);
                    model.transform.localRotation = Quaternion.Euler(0f, Mathf.Round(yaw / 90f) * 90f, 0f);
                    report.AppendLine($"Facing: turned {Mathf.Round(yaw / 90f) * 90f:0} degrees to face +Z.");
                }

                // Height.
                var b = new Bounds(model.transform.position, Vector3.zero);
                bool any = false;
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                {
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                if (any && b.size.y > 0.01f)
                {
                    float s = TargetHeight / b.size.y;
                    model.transform.localScale = Vector3.one * s;
                    model.transform.localPosition = new Vector3(0f, -b.min.y * s, 0f);
                    report.AppendLine($"Height: {b.size.y:0.00} m as exported, scaled x{s:0.###} to {TargetHeight} m.");
                }

                anim.runtimeAnimatorController = ctrl;

                // Stand on the ground in the real standing pose: the animator places the hips itself,
                // so the rest-pose lift above would float the body. Sample the idle clip, bake the
                // skinned mesh and drop the model until its lowest point touches the root.
                if (slots.TryGetValue("Idle", out var idleClip) && idleClip != null)
                {
                    float before = model.transform.localPosition.y;
                    model.transform.localPosition = new Vector3(0f, 0f, 0f);
                    idleClip.SampleAnimation(model, 0f);
                    float lowest = float.MaxValue;
                    foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        var baked = new Mesh();
                        smr.BakeMesh(baked, true);
                        foreach (var v in baked.vertices)
                            lowest = Mathf.Min(lowest, (smr.transform.position + smr.transform.rotation * v).y);
                        Object.DestroyImmediate(baked);
                    }
                    if (lowest < float.MaxValue)
                    {
                        float drop = lowest - model.transform.position.y; // sampling may move the model itself
                        model.transform.localPosition = new Vector3(0f, -drop, 0f);
                        report.AppendLine($"Ground: rest-pose lift was {before:0.000} m; in the idle pose the soles sit {drop:0.000} m off the model origin, model offset now {-drop:0.000} m.");
                    }
                }
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var bodyAnim = model.GetComponent<CharacterBodyAnimator>();
                if (bodyAnim == null) bodyAnim = model.AddComponent<CharacterBodyAnimator>();
                // Real diagonal clips: no need to push diagonal input outward.
                bodyAnim.diagonalPush = slots.ContainsKey("FwdRight") ? 0f : 0.15f;
                var viewMode = root.GetComponent<CharacterViewMode>();
                if (viewMode == null) viewMode = root.AddComponent<CharacterViewMode>();
                report.AppendLine(AddFirstPersonArms(model, anim, viewMode));

                System.IO.Directory.CreateDirectory("Assets/Prefabs/Characters");
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                report.AppendLine("Prefab: " + PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }

            AssetDatabase.SaveAssets();
            report.AppendLine(PlayerViewTools.Run(PrefabPath));
            return report.ToString();
        }

        /// Arm parts kept in the first-person arms (fingers are added on top). Collarbones and upper arms
        /// are kept so the cut sits at the collar, out of view, not at the elbow or shoulder where it shows.
        static readonly HumanBodyBones[] FirstPersonArmBones =
        {
            HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
            HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
        };

        /// First-person arms: a copy of the body mesh keeping only the arms, hands and fingers,
        /// on the same skeleton. Shown only in first person (CharacterViewMode.firstPersonOnly), where
        /// the aim rig puts those hands on the gun in front of the camera.
        static string AddFirstPersonArms(GameObject model, Animator anim, CharacterViewMode viewMode)
        {
            var smr = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.sharedMesh != null)
                .OrderByDescending(s => s.sharedMesh.vertexCount).FirstOrDefault();
            if (smr == null) return "First-person arms: no skinned mesh.";

            var keep = new HashSet<Transform>();
            foreach (var hb in FirstPersonArmBones)
                if (anim.GetBoneTransform(hb) != null) keep.Add(anim.GetBoneTransform(hb));
            for (var hb = HumanBodyBones.LeftThumbProximal; hb <= HumanBodyBones.RightLittleDistal; hb++)
                if (anim.GetBoneTransform(hb) != null) keep.Add(anim.GetBoneTransform(hb));
            // Bones under the hands (e.g. hand end bones) count too.
            foreach (var t in smr.bones)
                if (t != null && keep.Any(k => t.IsChildOf(k))) keep.Add(t);

            var src = smr.sharedMesh;
            var boneKeep = smr.bones.Select(b => b != null && keep.Contains(b)).ToArray();
            var weights = src.boneWeights;
            bool Kept(int v)
            {
                var w = weights[v];
                int top = w.boneIndex0; float best = w.weight0;
                if (w.weight1 > best) { top = w.boneIndex1; best = w.weight1; }
                if (w.weight2 > best) { top = w.boneIndex2; best = w.weight2; }
                if (w.weight3 > best) { top = w.boneIndex3; }
                return top < boneKeep.Length && boneKeep[top];
            }

            var arms = Object.Instantiate(src);
            arms.name = src.name + "_FirstPersonArms";
            int kept = 0;
            for (int s = 0; s < src.subMeshCount; s++)
            {
                var tris = src.GetTriangles(s);
                var outTris = new List<int>();
                for (int i = 0; i + 2 < tris.Length; i += 3)
                    if (Kept(tris[i]) && Kept(tris[i + 1]) && Kept(tris[i + 2])) { outTris.Add(tris[i]); outTris.Add(tris[i + 1]); outTris.Add(tris[i + 2]); }
                arms.SetTriangles(outTris, s);
                kept += outTris.Count / 3;
            }
            // Overwrite an existing mesh file in place (keeps every link to it); create it the first time.
            string path = $"{Folder}/{Name}_FirstPersonArms.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                // Rebuilt through the Mesh API, not CopySerialized: a serialized copy over a mesh with a
                // different vertex count or layout (e.g. after the body mesh changed) leaves vertex data that
                // no longer matches its stride, and the SkinnedMeshRenderer silently stops drawing it.
                CopyMesh(arms, existing);
                existing.name = arms.name;
                Object.DestroyImmediate(arms);
                arms = existing;
                EditorUtility.SetDirty(existing);
            }
            else AssetDatabase.CreateAsset(arms, path);

            var old = smr.transform.parent.Find("FirstPersonArms");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var go = new GameObject("FirstPersonArms");
            go.transform.SetParent(smr.transform.parent, false);
            go.transform.localPosition = smr.transform.localPosition;
            go.transform.localRotation = smr.transform.localRotation;
            go.transform.localScale = smr.transform.localScale;
            go.layer = smr.gameObject.layer;
            var r = go.AddComponent<SkinnedMeshRenderer>();
            r.sharedMesh = arms;
            r.bones = smr.bones;
            r.rootBone = smr.rootBone;
            r.sharedMaterials = smr.sharedMaterials;
            r.localBounds = smr.localBounds;
            r.updateWhenOffscreen = true;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.enabled = false;
            viewMode.firstPersonOnly = new Renderer[] { r };
            return $"First-person arms: {kept} triangles (collarbones, upper arms, forearms, hands, fingers).";
        }

        /// Replaces dst's whole content with src's (vertices, normals, tangents, colours, every UV set, bone
        /// weights, bind poses, blend shapes, sub-meshes), keeping dst itself so links to it survive.
        static void CopyMesh(Mesh src, Mesh dst)
        {
            dst.Clear();
            dst.indexFormat = src.indexFormat;
            dst.SetVertices(src.vertices);
            if (src.normals.Length == src.vertexCount) dst.SetNormals(src.normals);
            if (src.tangents.Length == src.vertexCount) dst.SetTangents(src.tangents);
            if (src.colors32.Length == src.vertexCount) dst.SetColors(src.colors32);
            var uv = new List<Vector4>();
            for (int ch = 0; ch < 8; ch++)
            {
                uv.Clear(); src.GetUVs(ch, uv);
                if (uv.Count == src.vertexCount) dst.SetUVs(ch, uv);
            }
            dst.bindposes = src.bindposes;
            var perVertex = src.GetBonesPerVertex();
            var all = src.GetAllBoneWeights();
            if (perVertex.Length == src.vertexCount) dst.SetBoneWeights(perVertex, all);
            dst.ClearBlendShapes();
            var dv = new Vector3[src.vertexCount]; var dn = new Vector3[src.vertexCount]; var dt = new Vector3[src.vertexCount];
            for (int b = 0; b < src.blendShapeCount; b++)
                for (int f = 0; f < src.GetBlendShapeFrameCount(b); f++)
                {
                    src.GetBlendShapeFrameVertices(b, f, dv, dn, dt);
                    dst.AddBlendShapeFrame(src.GetBlendShapeName(b), src.GetBlendShapeFrameWeight(b, f), dv, dn, dt);
                }
            dst.subMeshCount = src.subMeshCount;
            for (int s = 0; s < src.subMeshCount; s++) dst.SetTriangles(src.GetTriangles(s), s);
            dst.RecalculateBounds();
        }

        /// Ground speed (m/s) an in-place cycle implies: how fast the planted foot slides backwards
        /// under the body, averaged over the frames where a foot is on the ground.
        internal static float MeasureGroundSpeed(GameObject asset, AnimationClip clip) => MeasureGroundSpeed(asset, clip, TargetHeight);

        internal static float MeasureGroundSpeed(GameObject asset, AnimationClip clip, float targetHeight)
        {
            var temp = (GameObject)Object.Instantiate(asset);
            try
            {
                temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var anim = temp.GetComponent<Animator>();
                if (anim == null) return 0f;
                var l = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
                var r = anim.GetBoneTransform(HumanBodyBones.RightFoot);
                if (l == null || r == null) return 0f;
                // The prefab is scaled to TargetHeight, so the game speed scales the same way.
                var rs = temp.GetComponentsInChildren<Renderer>(true);
                if (rs.Length == 0) return 0f;
                var hb = rs[0].bounds; foreach (var rr in rs) hb.Encapsulate(rr.bounds);
                float scale = hb.size.y > 0.01f ? targetHeight / hb.size.y : 1f;

                const float dt = 1f / 60f;
                int n = Mathf.Max(8, Mathf.RoundToInt(clip.length / dt));
                var lp = new Vector3[n + 1]; var rp = new Vector3[n + 1];
                for (int i = 0; i <= n; i++)
                {
                    clip.SampleAnimation(temp, clip.length * i / n);
                    lp[i] = temp.transform.InverseTransformPoint(l.position);
                    rp[i] = temp.transform.InverseTransformPoint(r.position);
                }
                float floor = Mathf.Min(lp.Min(p => p.y), rp.Min(p => p.y));
                float sum = 0f; int count = 0;
                float step = clip.length / n;
                for (int i = 0; i < n; i++)
                {
                    // The lower foot, when it is within 3 cm of the lowest point, is the planted one.
                    bool left = lp[i].y <= rp[i].y;
                    Vector3 a = left ? lp[i] : rp[i], b = left ? lp[i + 1] : rp[i + 1];
                    if (a.y > floor + 0.03f) continue;
                    Vector3 d = b - a; d.y = 0f;
                    sum += d.magnitude / step; count++;
                }
                return count > 0 ? sum / count * scale : 0f;
            }
            finally { Object.DestroyImmediate(temp); }
        }

        /// The model's rest skeleton with the arms straightened out sideways and the legs straight
        /// down, which is what Unity's humanoid treats as the zero pose.
        static SkeletonBone[] TPoseSkeleton(GameObject asset, StringBuilder report)
        {
            var temp = Object.Instantiate(asset);
            try
            {
                temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Transform B(string n) => temp.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
                var la = B("LeftUpperArm"); var ra = B("RightUpperArm");
                if (la != null && ra != null)
                {
                    Vector3 right = (ra.position - la.position); right.y = 0f; right.Normalize();
                    float before = Vector3.Angle(B("LeftLowerArm").position - la.position, -right);
                    foreach (var side in new[] { ("Left", -right), ("Right", right) })
                    {
                        Aim(B(side.Item1 + "UpperArm"), B(side.Item1 + "LowerArm"), side.Item2);
                        Aim(B(side.Item1 + "LowerArm"), B(side.Item1 + "Hand"), side.Item2);
                        Aim(B(side.Item1 + "UpperLeg"), B(side.Item1 + "LowerLeg"), Vector3.down);
                        Aim(B(side.Item1 + "LowerLeg"), B(side.Item1 + "Foot"), Vector3.down);
                        // the model's finger block is curled at rest: straight out along the arm in the reference pose
                        Aim(B(side.Item1 + "HandIndex1"), B(side.Item1 + "HandIndex2"), side.Item2);
                        Aim(B(side.Item1 + "HandIndex2"), B(side.Item1 + "HandIndex3"), side.Item2);
                    }
                    report.AppendLine($"T-pose: arms were {before:0} degrees off horizontal, straightened.");
                }
                return temp.GetComponentsInChildren<Transform>(true)
                    .Select(t => new SkeletonBone { name = t == temp.transform ? asset.name : t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale })
                    .ToArray();
            }
            finally { Object.DestroyImmediate(temp); }
        }

        /// Turns `bone` so its child lies along `dir`.
        static void Aim(Transform bone, Transform child, Vector3 dir)
        {
            if (bone == null || child == null) return;
            Vector3 cur = child.position - bone.position;
            if (cur.sqrMagnitude < 1e-8f) return;
            bone.rotation = Quaternion.FromToRotation(cur, dir) * bone.rotation;
        }

        static Material BuildMaterial()
        {
            string path = $"{Folder}/Mat_{Name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Folder}/{Name}_Albedo.png");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Folder}/{Name}_Normal.png");
            var ms = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Folder}/{Name}_MetalSmooth.png");
            mat.SetTexture("_BaseMap", albedo);
            mat.SetTexture("_MainTex", albedo);
            mat.SetColor("_BaseColor", Color.white);
            if (normal != null) { mat.SetTexture("_BumpMap", normal); mat.SetFloat("_BumpScale", 1f); mat.EnableKeyword("_NORMALMAP"); }
            if (ms != null)
            {
                // R = metallic, A = smoothness (1 - Meshy roughness).
                mat.SetTexture("_MetallicGlossMap", ms);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetFloat("_SmoothnessTextureChannel", 0f);
                mat.SetFloat("_Smoothness", 1f);
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void SetTexture(string path, bool normal, bool srgb)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = srgb;
            ti.maxTextureSize = 2048;
            ti.SaveAndReimport();
        }
    }
}
