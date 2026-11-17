using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using ZombieRush.Player;

namespace ZombieRush.EditorTools
{
    /// Makes the Survivor (player character) ready for first and third person:
    /// bakes its animations (SurvivorAnimationAuthoring), builds Survivor.controller and the
    /// upper-body mask, and fits the prefab with CharacterBodyAnimator, CharacterViewMode and
    /// sockets for the gun (both hands) and a first-person eye. BuildController is also used by MeshyBodyTools.
    static class SurvivorRigTools
    {
        const string Folder = "Assets/Models/2026-11-05_Characters/ZR_Survivor";
        const string ControllerPath = Folder + "/Survivor.controller";
        const string MaskPath = Folder + "/Survivor_UpperBody.mask";

        /// Returns a line for the setup report.
        public static string Build(GameObject modelAsset, GameObject prefabRoot, GameObject modelInstance)
        {
            var avatar = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(modelAsset)).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isHuman) return "Survivor: no Humanoid avatar, animations skipped.";

            var temp = Object.Instantiate(modelAsset);
            Dictionary<string, AnimationClip> clips;
            try
            {
                temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                clips = SurvivorAnimationAuthoring.BakeAll(temp, avatar);
            }
            finally { Object.DestroyImmediate(temp); }
            string[] needed = { "Idle", "Walk", "Run", "StrafeLeft", "StrafeRight", "CrouchIdle", "CrouchWalk", "Air", "Die", "Fire", "Reload", "Hit" };
            var missing = needed.Where(n => !clips.ContainsKey(n) || clips[n] == null).ToArray();
            if (missing.Length > 0) return "Survivor: animation baking failed for " + string.Join(", ", missing) + " (see Console).";

            var controller = BuildController(clips, BuildMask());
            var animator = modelInstance.GetComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            var driver = modelInstance.GetComponent<CharacterBodyAnimator>();
            if (driver == null) driver = modelInstance.AddComponent<CharacterBodyAnimator>();
            if (prefabRoot.GetComponent<CharacterViewMode>() == null) prefabRoot.AddComponent<CharacterViewMode>();
            string facing = AddSockets(modelInstance, driver);
            return $"Survivor: {clips.Count} animations, Survivor.controller, gun and eye sockets, first/third-person switch. {facing}";
        }

        static AvatarMask BuildMask()
        {
            if (File.Exists(MaskPath)) AssetDatabase.DeleteAsset(MaskPath);
            var mask = new AvatarMask();
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            foreach (var part in new[] { AvatarMaskBodyPart.Head, AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm,
                                         AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers,
                                         AvatarMaskBodyPart.LeftHandIK, AvatarMaskBodyPart.RightHandIK })
                mask.SetHumanoidBodyPartActive(part, true);
            AssetDatabase.CreateAsset(mask, MaskPath);
            return mask;
        }

        /// The Survivor's upper-body mask (humanoid, so it fits any humanoid body).
        public const string UpperBodyMaskPath = MaskPath;

        static AnimatorController BuildController(Dictionary<string, AnimationClip> c, AvatarMask mask) => BuildController(ControllerPath, c, mask, null);

        /// Builds a full-body player controller at `path` from clips keyed Idle, Walk, Run, StrafeLeft,
        /// StrafeRight, CrouchIdle, CrouchWalk, Air, Die, Fire, Reload, Hit, and optionally Back (a
        /// real backward clip; without it Walk plays in reverse) and FwdLeft/FwdRight/BackLeft/BackRight
        /// diagonals. `speed` sets a clip's playback rate
        /// by the same key, to match its stride to the player's move speed.
        public static AnimatorController BuildController(string path, Dictionary<string, AnimationClip> c, AvatarMask mask, Dictionary<string, float> speed)
        {
            float S(string k) => speed != null && speed.TryGetValue(k, out var v) ? v : 1f;
            var backward = c.TryGetValue("Back", out var b) && b != null ? (b, 0f, -1f, S("Back")) : (c["Walk"], 0f, -1f, -S("Walk"));
            // Reuse an existing file (cleared) instead of deleting it: a deleted asset gets a new identity,
            // and bodies or prefabs already pointing at it would be left with no controller.
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (ac != null)
            {
                for (int i = ac.layers.Length - 1; i >= 0; i--) ac.RemoveLayer(i);
                foreach (var p in ac.parameters) ac.RemoveParameter(p);
                // RemoveLayer leaves the old blend trees inside the file: every rebuild added ~10 orphans
                // (146 by 2026-11-16), each with stale playback rates. Clear them out.
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (o is BlendTree) Object.DestroyImmediate(o, true);
                ac.AddLayer("Base Layer");
            }
            else ac = AnimatorController.CreateAnimatorControllerAtPath(path);
            ac.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            ac.AddParameter("MoveY", AnimatorControllerParameterType.Float);
            ac.AddParameter("Crouch", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            foreach (var t in new[] { "Fire", "Reload", "Hit", "Die" }) ac.AddParameter(t, AnimatorControllerParameterType.Trigger);
            var grounded = ac.parameters.First(p => p.name == "Grounded");
            grounded.defaultBool = true;
            ac.parameters = ac.parameters.Select(p => p.name == "Grounded" ? grounded : p).ToArray();

            var sm = ac.layers[0].stateMachine;
            var loco = ac.CreateBlendTreeInController("Locomotion", out var tree, 0);
            var moves = new List<(AnimationClip, float, float, float)>
            {
                (c["Idle"], 0f, 0f, 1f), (c["Walk"], 0f, 1f, S("Walk")), (c["Run"], 0f, 2f, S("Run")), backward,
                (c["StrafeLeft"], -1f, 0f, S("StrafeLeft")), (c["StrafeRight"], 1f, 0f, S("StrafeRight")),
            };
            // Optional real diagonal clips, on the unit circle at 45 degrees.
            const float D = 0.7071f;
            foreach (var (key, x, y) in new[] { ("FwdLeft", -D, D), ("FwdRight", D, D), ("BackLeft", -D, -D), ("BackRight", D, -D) })
                if (c.TryGetValue(key, out var diag) && diag != null) moves.Add((diag, x, y, S(key)));
            // Optional slow walk between standing and the jog (a half-pushed stick), so slow movement is a real walk
            // instead of the standing pose blended with the run (mushy, sliding legs). Its place on the forward axis
            // is its speed as a share of the jog (the MoveY the body animator feeds at that speed).
            if (c.TryGetValue("SlowWalk", out var slow) && slow != null && speed != null && speed.TryGetValue("SlowWalkAt", out var at))
                moves.Add((slow, 0f, at, S("SlowWalk")));
            Directional(tree, moves.ToArray());
            var crouch = ac.CreateBlendTreeInController("Crouch", out var ctree, 0);
            Directional(ctree, (c["CrouchIdle"], 0f, 0f, 1f), (c["CrouchWalk"], 0f, 1f, S("CrouchWalk")), (c["CrouchWalk"], 0f, -1f, -S("CrouchWalk")),
                        (c["CrouchWalk"], -1f, 0f, S("CrouchWalk")), (c["CrouchWalk"], 1f, 0f, S("CrouchWalk")));
            var air = sm.AddState("Air");
            air.motion = c["Air"];
            var die = sm.AddState("Die");
            die.motion = c["Die"];
            sm.defaultState = loco;

            Go(loco, crouch, 0.15f, ("Crouch", AnimatorConditionMode.If));
            Go(crouch, loco, 0.15f, ("Crouch", AnimatorConditionMode.IfNot));
            Go(loco, air, 0.15f, ("Grounded", AnimatorConditionMode.IfNot));
            Go(crouch, air, 0.15f, ("Grounded", AnimatorConditionMode.IfNot));
            Go(air, loco, 0.1f, ("Grounded", AnimatorConditionMode.If));
            var toDie = sm.AddAnyStateTransition(die);
            toDie.AddCondition(AnimatorConditionMode.If, 0f, "Die");
            toDie.duration = 0.1f;
            toDie.canTransitionToSelf = false;

            var layers = ac.layers;
            layers[0].iKPass = true;
            ac.layers = layers;

            // Upper body: one-shot actions over the legs. CharacterBodyAnimator fades its weight in and out.
            var usm = new AnimatorStateMachine { name = "UpperBody", hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(usm, ac);
            var empty = usm.AddState("Empty");
            empty.writeDefaultValues = false;
            usm.defaultState = empty;
            foreach (var n in new[] { "Fire", "Reload", "Hit" })
            {
                var st = usm.AddState(n);
                st.motion = c[n];
                st.writeDefaultValues = false;
                var t = usm.AddAnyStateTransition(st);
                t.AddCondition(AnimatorConditionMode.If, 0f, n);
                t.hasExitTime = false;
                t.duration = 0.05f;
                t.canTransitionToSelf = n == "Fire";
                var back = st.AddTransition(empty);
                back.hasExitTime = true;
                back.exitTime = 0.9f;
                back.duration = 0.1f;
            }
            ac.AddLayer(new AnimatorControllerLayer
            {
                name = "UpperBody", defaultWeight = 0f, avatarMask = mask, stateMachine = usm,
                blendingMode = AnimatorLayerBlendingMode.Override, iKPass = true,
            });
            EditorUtility.SetDirty(ac);
            return ac;
        }

        static void Directional(BlendTree tree, params (AnimationClip clip, float x, float y, float timeScale)[] children)
        {
            tree.blendType = BlendTreeType.FreeformDirectional2D;
            tree.blendParameter = "MoveX";
            tree.blendParameterY = "MoveY";
            foreach (var ch in children) tree.AddChild(ch.clip, new Vector2(ch.x, ch.y));
            var arr = tree.children;
            for (int i = 0; i < arr.Length; i++) arr[i].timeScale = children[i].timeScale;
            tree.children = arr;
        }

        static void Go(AnimatorState from, AnimatorState to, float duration, params (string param, AnimatorConditionMode mode)[] conds)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = false;
            t.duration = duration;
            foreach (var (param, mode) in conds) t.AddCondition(mode, 0f, param);
        }

        /// Gun sockets at the palms and an eye anchor, oriented to the body's own forward.
        static string AddSockets(GameObject model, CharacterBodyAnimator driver)
        {
            Transform Bone(string n) => model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
            var lh = Bone("LeftHand"); var rh = Bone("RightHand");
            var lf = Bone("LeftForeArm"); var rf = Bone("RightForeArm");
            var head = Bone("Head");
            if (lh == null || rh == null || lf == null || rf == null || head == null) return "Sockets skipped: bones not found.";

            Vector3 forward = Vector3.Cross(rh.position - lh.position, Vector3.up).normalized;
            var rot = Quaternion.LookRotation(forward, Vector3.up);
            Transform Socket(Transform parent, string name, Vector3 world)
            {
                var old = parent.Find(name);
                if (old != null) Object.DestroyImmediate(old.gameObject);
                var s = new GameObject(name).transform;
                s.SetParent(parent, false);
                s.SetPositionAndRotation(world, rot);
                return s;
            }
            driver.rightHandSocket = Socket(rh, "WeaponSocket_R", rh.position + (rh.position - rf.position).normalized * 0.07f);
            driver.leftHandSocket = Socket(lh, "WeaponSocket_L", lh.position + (lh.position - lf.position).normalized * 0.07f);
            driver.eyeAnchor = Socket(head, "EyeAnchor", head.position + Vector3.up * 0.12f + forward * 0.11f);
            Vector3 local = model.transform.InverseTransformDirection(forward);
            return $"Rest pose faces local ({local.x:0.##}, {local.y:0.##}, {local.z:0.##}).";
        }
    }
}
