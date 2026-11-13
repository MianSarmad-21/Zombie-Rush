using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using ZombieRush.Data;
using ZombieRush.Weapons;

namespace ZombieRush.EditorTools
{
    /// Zombie Rush > Weapons > Install Blender Guns (marker: Temp/ZR_RunMapCleanup with "guns")
    ///
    /// Sets up the three guns made by Blender/gun_build.py (Assets/Models/2026-11-09_Guns/<Name>/):
    ///   - import: generic animation, Idle loops; URP material from the gun's textures, plain ones for
    ///     the magazine body and the shotgun shells;
    ///   - an Animator controller (Idle, Fire, Reload, Draw) and a prefab (Assets/Prefabs/Guns/) with GunRig;
    ///   - puts each gun on the matching weapon in the open scene (first person, under the camera; the
    ///     body's copy is made from it at runtime), with the muzzle flash on its Muzzle bone;
    ///   - the shotgun becomes a double barrel: 2 shells, both barrels can fire quickly, 2.4 s reload.
    static class GunSetupTools
    {
        const string ModelRoot = "Assets/Models/2026-11-09_Guns";
        const string PrefabDir = "Assets/Prefabs/Guns";
        /// First person: where the right hand's grip sits in front of the camera (right, up, forward in
        /// metres) and a small turn (degrees: yaw towards the middle, roll) so it reads well on screen.
        /// Close enough that the left arm reaches the fore-end of the long guns.
        static readonly Dictionary<string, (Vector3 grip, Vector3 turn)> FirstPerson = new Dictionary<string, (Vector3, Vector3)>
        {
            { "Pistol", (new Vector3(0.15f, -0.21f, 0.52f), new Vector3(2f, -8f, -8f)) },
            { "Rifle", (new Vector3(0.09f, -0.17f, 0.04f), new Vector3(1f, -2f, -3f)) },
            { "Shotgun", (new Vector3(0.09f, -0.18f, 0.03f), new Vector3(1f, -2f, -3f)) },
        };
        static readonly (string name, string weaponKey, GunStyle style)[] Guns =
        {
            ("Pistol", "pistol", GunStyle.Pistol), ("Shotgun", "shotgun", GunStyle.Shotgun), ("Rifle", "rifle", GunStyle.Rifle),
        };

        [MenuItem("Zombie Rush/Weapons/Install Blender Guns", priority = 3)]
        static void Menu() => Debug.Log("Zombie Rush: " + Run());

        /// Every transform of a gun prefab: local position/rotation/scale and position relative to the root.
        public static string Dump(string name)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelRoot}/{name}/{name}.fbx");
            if (go == null) return "no fbx";
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(go);
            var sb = new StringBuilder();
            try
            {
                foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                {
                    var r = t.GetComponent<Renderer>();
                    sb.AppendLine($"  {t.name} (parent {(t.parent ? t.parent.name : "-")}): local {t.localPosition.ToString("F3")} rot {t.localEulerAngles.ToString("F0")} scale {t.localScale.ToString("F3")} | root-space {inst.transform.InverseTransformPoint(t.position).ToString("F3")}" +
                              (r != null ? $" bounds c{inst.transform.InverseTransformPoint(r.bounds.center).ToString("F3")} s{r.bounds.size.ToString("F3")}" : ""));
                }
            }
            finally { Object.DestroyImmediate(inst); }
            return sb.ToString();
        }

        public static string Run()
        {
            var sb = new StringBuilder();
            var prefabs = new Dictionary<string, GameObject>();
            foreach (var (name, _, style) in Guns)
            {
                var p = BuildPrefab(name, style, sb);
                if (p != null) prefabs[name] = p;
            }
            AssetDatabase.SaveAssets();
            sb.AppendLine(Install(prefabs));
            return sb.ToString();
        }

        // ------------------------------------------------------------ import + prefab

        static Material Lit(string path, Color color, float metal, float smooth, Texture2D albedo = null, Texture2D normal = null, Texture2D ms = null)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", color);
            m.SetTexture("_BaseMap", albedo);
            m.SetFloat("_Metallic", metal);
            m.SetFloat("_Smoothness", smooth);
            if (normal != null) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); }
            if (ms != null) { m.SetTexture("_MetallicGlossMap", ms); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_Smoothness", 1f); }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D Tex(string path, bool normal, bool srgb)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return null;
            var want = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (imp.textureType != want || imp.sRGBTexture != srgb || imp.maxTextureSize != 2048)
            {
                imp.textureType = want; imp.sRGBTexture = srgb; imp.maxTextureSize = 2048;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static GameObject BuildPrefab(string name, GunStyle style, StringBuilder sb)
        {
            string dir = $"{ModelRoot}/{name}";
            string fbx = $"{dir}/{name}.fbx";
            var imp = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (imp == null) { sb.AppendLine($"{name}: SKIPPED, {fbx} not found (run Blender/gun_build.py {name})."); return null; }
            imp.animationType = ModelImporterAnimationType.Generic;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.importNormals = ModelImporterNormals.Import;
            var takes = imp.defaultClipAnimations;
            foreach (var c in takes)
            {
                bool loop = c.name.EndsWith("Idle");
                c.loopTime = loop; c.loopPose = loop;
            }
            imp.clipAnimations = takes;
            imp.SaveAndReimport();

            var clips = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();
            AnimationClip Clip(string n) => clips.FirstOrDefault(c => c.name == n || c.name.EndsWith("|" + n));

            // controller: one state per clip; GunRig picks the state and the time
            string ctrlPath = $"{dir}/{name}.controller";
            if (File.Exists(ctrlPath)) AssetDatabase.DeleteAsset(ctrlPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            var sm = ctrl.layers[0].stateMachine;
            foreach (var st in new[] { "Idle", "Fire", "Reload", "Draw" })
            {
                var s = sm.AddState(st);
                s.motion = Clip(st);
                if (s.motion == null) sb.AppendLine($"  {name}: MISSING clip {st}");
                if (st == "Idle") sm.defaultState = s;
            }

            // materials
            string matDir = $"{dir}/Materials";
            Directory.CreateDirectory(matDir);
            var body = Lit($"{matDir}/{name}.mat", Color.white, 1f, 0.5f,
                Tex($"{dir}/{name}_Albedo.png", false, true), Tex($"{dir}/{name}_Normal.png", true, false), Tex($"{dir}/{name}_MetalSmooth.png", false, false));
            var dark = Lit($"{matDir}/Gun_DarkMetal.mat", new Color(0.07f, 0.07f, 0.075f), 0.8f, 0.45f);
            var hull = Lit($"{matDir}/Gun_ShellHull.mat", new Color(0.55f, 0.07f, 0.05f), 0f, 0.35f);
            var brass = Lit($"{matDir}/Gun_ShellBrass.mat", new Color(0.8f, 0.6f, 0.25f), 0.9f, 0.6f);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                go.name = name;
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                // the file's default pose is a clip frame: measure on the rest pose (Idle's first frame)
                var idleClip = Clip("Idle");
                if (idleClip != null) idleClip.SampleAnimation(go, 0f);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var n = r.gameObject.name;
                    int slots = r.sharedMaterials.Length;
                    Material[] mats = n.StartsWith("MagBody") ? new[] { dark }
                        : n.StartsWith("Shell") ? (slots >= 2 ? new[] { hull, brass } : new[] { hull })
                        : Enumerable.Repeat(body, Mathf.Max(1, slots)).ToArray();
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }
                var anim = go.GetComponent<Animator>();
                if (anim == null) anim = go.AddComponent<Animator>();
                anim.runtimeAnimatorController = ctrl;
                anim.applyRootMotion = false;
                Transform Find(string n) => go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
                var rig = go.GetComponent<GunRig>();
                if (rig == null) rig = go.AddComponent<GunRig>();
                rig.style = style;
                rig.controller = ctrl;
                rig.idleClip = Clip("Idle"); rig.fireClip = Clip("Fire"); rig.reloadClip = Clip("Reload"); rig.drawClip = Clip("Draw");
                rig.handR = Find("HandR"); rig.handL = Find("HandL"); rig.muzzle = Find("Muzzle");
                rig.idleLength = Clip("Idle") != null ? Clip("Idle").length : 2.4f;
                rig.fireLength = Clip("Fire") != null ? Clip("Fire").length : 0.2f;
                rig.drawLength = Clip("Draw") != null ? Clip("Draw").length : 0.45f;
                // the gun's frame from its Gun bone (bone axes are Blender's: -Y muzzle, +Z top)
                var gunBone = Find("Gun");
                Vector3 fwd = go.transform.InverseTransformDirection(gunBone.TransformDirection(Vector3.down)).normalized;
                Vector3 top = go.transform.InverseTransformDirection(gunBone.TransformDirection(Vector3.forward)).normalized;
                rig.frame = Quaternion.LookRotation(fwd, top);
                if (rig.handR != null) rig.restRight = Quaternion.Inverse(go.transform.rotation) * rig.handR.rotation;
                if (rig.handL != null) rig.restLeft = Quaternion.Inverse(go.transform.rotation) * rig.handL.rotation;
                // the body has no finger bones: an open hand lying along the gun would cover it, so the
                // fingers point down the grip and the hand sits below the slide / receiver
                rig.rightFingers = style == GunStyle.Pistol ? new Vector3(0f, -0.75f, 0.65f) : new Vector3(0f, -0.55f, 0.85f);
                if (style == GunStyle.Pistol)
                {
                    rig.leftFingers = new Vector3(0.4f, -0.3f, 0.8f);
                    rig.leftPalm = new Vector3(1f, 0f, 0f);
                }
                sb.AppendLine("  " + BodyClips(name, rig));
                sb.AppendLine("  " + FirstPersonClips(name, rig));
                // orientation check: muzzle ahead (+Z), grip at the origin
                Vector3 m = rig.muzzle != null ? Quaternion.Inverse(rig.frame) * go.transform.InverseTransformPoint(rig.muzzle.position) : Vector3.zero;
                string facing = m.z > Mathf.Abs(m.x) * 3f && m.z > Mathf.Abs(m.y) * 2f ? "muzzle forward" : $"CHECK FACING (muzzle at {m})";
                Directory.CreateDirectory(PrefabDir);
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{name}.prefab");
                sb.AppendLine($"{name}: {clips.Length} clips ({string.Join(", ", clips.Select(c => $"{c.name.Split('|').Last()} {c.length:0.00}s"))}), " +
                              $"{go.GetComponentsInChildren<Renderer>(true).Length} parts, {facing}, muzzle {m.z:0.00} m ahead of the grip.");
                return prefab;
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ------------------------------------------------------------ body clips

        static readonly string[] DeltaBoneNames = { "Chest", "UpperChest", "Neck", "Head" };

        /// Survivor_<Gun>.fbx (Blender/grip_<gun>.py): the body's arms, hands and fingers made with the
        /// gun in hand. Imported as Generic; its <Gun>_Idle/Fire/Reload/Draw takes go on the GunRig with
        /// the bones they move (BodyGunPose plays them on the player).
        static string BodyClips(string name, GunRig rig)
        {
            string fbx = $"{ModelRoot}/{name}/Survivor_{name}.fbx";
            var imp = AssetImporter.GetAtPath(fbx) as ModelImporter;
            rig.bodyProxy = null;
            rig.bodyClips = new AnimationClip[4];
            rig.bodyRest = null;
            rig.copyBones = rig.deltaBones = new string[0];
            if (imp == null) return $"{name}: no body clips (arm IK holds it).";
            imp.animationType = ModelImporterAnimationType.Generic;
            imp.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            imp.importBlendShapes = false;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.animationCompression = ModelImporterAnimationCompression.Off;
            var takes = imp.defaultClipAnimations.Where(c => c.name.Split('|').Last().StartsWith(name + "_")).ToArray();
            foreach (var c in takes)
            {
                bool loop = c.name.EndsWith("_Idle");
                c.loopTime = loop; c.loopPose = false;
                c.name = c.name.Split('|').Last();
            }
            imp.clipAnimations = takes;
            imp.SaveAndReimport();
            var clips = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();
            AnimationClip Take(string n) => clips.FirstOrDefault(c => c.name == $"{name}_{n}");
            rig.bodyProxy = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            rig.bodyClips = new[] { Take("Idle"), Take("Fire"), Take("Reload"), Take("Draw") };
            rig.bodyRest = Take("Rest");
            // the bones the clips move: arms, hands, fingers copied; chest, neck, head as a change from rest
            var moved = new HashSet<string>();
            foreach (var c in rig.bodyClips.Where(c => c != null))
                foreach (var b in AnimationUtility.GetCurveBindings(c))
                    if (b.propertyName.StartsWith("m_LocalRotation"))
                        moved.Add(b.path.Split('/').Last());
            rig.copyBones = moved.Where(n => n.Contains("Shoulder") || n.Contains("Arm") || n.Contains("Hand")).OrderBy(n => n).ToArray();
            rig.deltaBones = moved.Where(n => DeltaBoneNames.Contains(n)).ToArray();
            int missing = rig.bodyClips.Count(c => c == null);
            return $"{name}: body clips {string.Join(", ", rig.bodyClips.Where(c => c != null).Select(c => $"{c.name} {c.length:0.00}s"))}" +
                   (missing > 0 ? $", {missing} MISSING" : "") + $"; {rig.copyBones.Length} bones copied, {rig.deltaBones.Length} turned" +
                   (rig.deltaBones.Length > 0 && rig.bodyRest == null ? " (no _Rest take: rest taken from the file's default pose)" : "") + ".";
        }

        const string FirstPersonRoot = "Assets/Models/2026-11-10_FPArms";

        /// FP_<Gun>.fbx (Blender/fp_viewmodel.py): first-person arms and the gun's skeleton, posed against
        /// the camera (the file's origin is the eye). Imported as Generic; its FP<Gun>_* takes go on the GunRig.
        static string FirstPersonClips(string name, GunRig rig)
        {
            string fbx = $"{FirstPersonRoot}/FP_{name}.fbx";
            var imp = AssetImporter.GetAtPath(fbx) as ModelImporter;
            rig.fpArms = null;
            rig.fpClips = new AnimationClip[4];
            if (imp == null) return $"{name}: no first-person arms (the body's arms show).";
            imp.animationType = ModelImporterAnimationType.Generic;
            imp.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            imp.importBlendShapes = false;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;   // the body's material is used at runtime
            imp.animationCompression = ModelImporterAnimationCompression.Off;
            var takes = imp.defaultClipAnimations.Where(c => c.name.Split('|').Last().StartsWith($"FP{name}_")).ToArray();
            foreach (var c in takes)
            {
                c.loopTime = c.name.EndsWith("_Idle"); c.loopPose = false;
                c.name = c.name.Split('|').Last();
            }
            imp.clipAnimations = takes;
            imp.SaveAndReimport();
            var clips = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();
            AnimationClip Take(string n) => clips.FirstOrDefault(c => c.name == $"FP{name}_{n}");
            rig.fpArms = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            rig.fpClips = new[] { Take("Idle"), Take("Fire"), Take("Reload"), Take("Draw") };
            rig.fpMaterials = FirstPersonMaterials();
            int missing = rig.fpClips.Count(c => c == null);
            bool gunBone = rig.fpArms.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Gun");
            return $"{name}: first-person arms {string.Join(", ", rig.fpClips.Where(c => c != null).Select(c => $"{c.name} {c.length:0.00}s"))}" +
                   (missing > 0 ? $", {missing} MISSING" : "") + (gunBone ? "" : ", NO GUN BONE") + ".";
        }

        /// The first-person arms' own materials (slots 1..3 of FP_Arms after the survivor slot, Blender/fp_gloves.py):
        /// dark synthetic-leather tactical gloves, the olive canvas jacket sleeve, and the gloves' rubber strap and
        /// knuckle pad. Tileable maps from Tools/Fx/make_glove_textures.py.
        static Material[] FirstPersonMaterials()
        {
            string tex = $"{FirstPersonRoot}/Textures";
            var gA = Tex($"{tex}/Glove_Albedo.png", false, true); var gN = Tex($"{tex}/Glove_Normal.png", true, false);
            var cA = Tex($"{tex}/Cloth_Albedo.png", false, true); var cN = Tex($"{tex}/Cloth_Normal.png", true, false);
            var glove = Lit($"{FirstPersonRoot}/FP_Glove.mat", new Color(1f, 1f, 1f), 0f, 0.3f, gA, gN);
            var cloth = Lit($"{FirstPersonRoot}/FP_Cloth.mat", new Color(1f, 1f, 1f), 0f, 0.12f, cA, cN);
            var trim = Lit($"{FirstPersonRoot}/FP_Trim.mat", new Color(0.55f, 0.55f, 0.55f), 0f, 0.5f, gA, gN);
            glove.SetTextureScale("_BaseMap", new Vector2(2f, 2f));
            cloth.SetTextureScale("_BaseMap", new Vector2(2f, 2f));
            trim.SetTextureScale("_BaseMap", new Vector2(6f, 6f));
            foreach (var m in new[] { glove, cloth, trim }) EditorUtility.SetDirty(m);
            return new[] { glove, cloth, trim };
        }

        /// Compares the body-clip skeleton (rest take) with the game's survivor skeleton, bone by bone.
        public static string BodyCheck()
        {
            var sb = new StringBuilder();
            var surv = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/2026-11-08_WastelandSurvivor_Final/WastelandSurvivor.fbx");
            var prox = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelRoot}/Pistol/Survivor_Pistol.fbx");
            var clips = AssetDatabase.LoadAllAssetsAtPath($"{ModelRoot}/Pistol/Survivor_Pistol.fbx").OfType<AnimationClip>().ToArray();
            var a = Object.Instantiate(surv); var b = Object.Instantiate(prox);
            try
            {
                var rest = clips.FirstOrDefault(c => c.name == "Pistol_Rest");
                if (rest != null) rest.SampleAnimation(b, 0f);
                var ta = a.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                var tb = b.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                sb.AppendLine($"survivor root rot {a.transform.rotation.eulerAngles}, proxy root rot {b.transform.rotation.eulerAngles}, rest clip {(rest != null)}");
                foreach (var n in new[] { "Armature.002", "Hips", "Chest", "UpperChest", "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand", "RightHandIndex1", "LeftHand", "Pistol_Rig", "Root", "Gun" })
                {
                    ta.TryGetValue(n, out var x); tb.TryGetValue(n, out var y);
                    string S(Transform t) => t == null ? "none" : $"lp {t.localPosition.ToString("F3")} lr {t.localRotation.eulerAngles.ToString("F1")} ls {t.localScale.ToString("F2")} wp {t.position.ToString("F3")}";
                    sb.AppendLine($"  {n}: game {S(x)}");
                    sb.AppendLine($"  {new string(' ', n.Length)}  clip {S(y)}{(x != null && y != null ? $"  rot diff {Quaternion.Angle(x.localRotation, y.localRotation):0.0}" : "")}");
                }
                var idle = clips.FirstOrDefault(c => c.name == "Pistol_Idle");
                if (idle != null)
                {
                    idle.SampleAnimation(b, 0f);
                    sb.AppendLine($"Idle: RightHand at {tb["RightHand"].position.ToString("F3")}, Gun at {tb["Gun"].position.ToString("F3")}, Gun axes x {tb["Gun"].right.ToString("F2")} y {tb["Gun"].up.ToString("F2")} z {tb["Gun"].forward.ToString("F2")}, Head at {tb["Head"].position.ToString("F3")}");
                }
            }
            finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
            return sb.ToString();
        }

        /// "pistol 0 0.03 -0.06" sets that gun's first-person camera spot (WeaponData); "pistol off" clears it.
        public static string SetEye(string args)
        {
            var a = args.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (a.Length < 2) return "eye: use 'eye <weapon> x y z' or 'eye <weapon> off'.";
            var controller = Object.FindAnyObjectByType<ZombieRush.Player.PlayerWeaponController>(FindObjectsInactive.Include);
            var w = controller != null ? controller.weapons.FirstOrDefault(x => x != null && WeaponTools.Named(x, a[0])) : null;
            if (w == null || w.data == null) return $"eye: no weapon '{a[0]}' with data.";
            Undo.RecordObject(w.data, "Eye offset");
            if (a[1] == "off") w.data.overrideEyeOffset = false;
            else
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                w.data.eyeOffset = new Vector3(float.Parse(a[1], inv), float.Parse(a[2], inv), float.Parse(a[3], inv));
                w.data.overrideEyeOffset = true;
            }
            EditorUtility.SetDirty(w.data);
            AssetDatabase.SaveAssets();
            return $"eye: {w.name} ({w.data.name}) override {w.data.overrideEyeOffset}, offset {w.data.eyeOffset.ToString("F3")}.";
        }

        /// "pistol 0.05 0.11 0" sets that gun's first-person lift (WeaponData.fpLift); "pistol 0 0 0" clears it.
        public static string SetLift(string args)
        {
            var a = args.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (a.Length < 4) return "lift: use 'lift <weapon> x y z'.";
            var controller = Object.FindAnyObjectByType<ZombieRush.Player.PlayerWeaponController>(FindObjectsInactive.Include);
            var w = controller != null ? controller.weapons.FirstOrDefault(x => x != null && WeaponTools.Named(x, a[0])) : null;
            if (w == null || w.data == null) return $"lift: no weapon '{a[0]}' with data.";
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Undo.RecordObject(w.data, "FP lift");
            w.data.fpLift = new Vector3(float.Parse(a[1], inv), float.Parse(a[2], inv), float.Parse(a[3], inv));
            EditorUtility.SetDirty(w.data);
            AssetDatabase.SaveAssets();
            return $"lift: {w.name} ({w.data.name}) first-person lift {w.data.fpLift.ToString("F3")}.";
        }

        /// The player's body gets BodyGunPose (plays the body clips) wired to its aim rig, weapons and camera.
        static string InstallBodyPose(ZombieRush.Player.PlayerWeaponController controller)
        {
            var aim = Object.FindAnyObjectByType<ZombieRush.Player.WeaponAimRig>(FindObjectsInactive.Include);
            if (aim == null) return "  BodyGunPose: no WeaponAimRig, skipped.";
            // The body setup rebuilds the controller file, and a body placed in the same run can be left
            // pointing at the old one (null): no animation, no IK pass, the gun never comes up. Re-link it.
            string fix = "";
            var anim = aim.GetComponent<Animator>();
            if (anim != null && anim.runtimeAnimatorController == null)
            {
                var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(MeshyBodyTools.ControllerPath);
                if (ctrl != null)
                {
                    Undo.RecordObject(anim, "Body controller");
                    anim.runtimeAnimatorController = ctrl;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(anim);
                    // and the body prefab, so a new body placed from it is right too
                    var contents = PrefabUtility.LoadPrefabContents(MeshyBodyTools.PrefabPath);
                    try
                    {
                        var pa = contents.GetComponentInChildren<Animator>(true);
                        if (pa != null && pa.runtimeAnimatorController == null)
                        {
                            pa.runtimeAnimatorController = ctrl;
                            PrefabUtility.SaveAsPrefabAsset(contents, MeshyBodyTools.PrefabPath);
                        }
                    }
                    finally { PrefabUtility.UnloadPrefabContents(contents); }
                    fix = $" Body animator had no controller: set to {ctrl.name}.";
                }
                else fix = $" Body animator has NO controller ({MeshyBodyTools.ControllerPath} missing).";
            }
            var pose = aim.GetComponent<ZombieRush.Player.BodyGunPose>();
            if (pose == null) pose = Undo.AddComponent<ZombieRush.Player.BodyGunPose>(aim.gameObject);
            Undo.RecordObject(pose, "Body gun pose");
            pose.aimRig = aim;
            pose.weapons = controller;
            pose.view = controller.GetComponentInParent<ZombieRush.Player.PlayerViewSwitcher>() ?? Object.FindAnyObjectByType<ZombieRush.Player.PlayerViewSwitcher>();
            pose.eyeCamera = pose.view != null ? pose.view.playerCamera : aim.aimCamera;
            EditorUtility.SetDirty(pose);
            return $"  BodyGunPose on '{aim.name}' (camera '{(pose.eyeCamera != null ? pose.eyeCamera.name : "none")}').{fix}";
        }

        // ------------------------------------------------------------ scene install

        static string Install(Dictionary<string, GameObject> prefabs)
        {
            var controller = Object.FindAnyObjectByType<ZombieRush.Player.PlayerWeaponController>(FindObjectsInactive.Include);
            if (controller == null) return "Scene: no PlayerWeaponController (open MainLevel).";
            var sb = new StringBuilder("Scene:\n");
            var weapons = (controller.weapons ?? new WeaponBase[0]).Where(w => w != null).ToList();
            Undo.SetCurrentGroupName("Install Blender guns");
            int undo = Undo.GetCurrentGroup();
            foreach (var (name, key, _) in Guns)
            {
                var w = weapons.FirstOrDefault(x => WeaponTools.Named(x, key));
                if (w == null || !prefabs.TryGetValue(name, out var prefab)) { sb.AppendLine($"  {name}: no weapon or no prefab, skipped."); continue; }
                var root = w.transform;
                for (int i = root.childCount - 1; i >= 0; i--) Undo.DestroyObjectImmediate(root.GetChild(i).gameObject);
                var holder = new GameObject("Model");
                Undo.RegisterCreatedObjectUndo(holder, "Gun model");
                holder.layer = w.gameObject.layer;
                holder.transform.SetParent(root, false);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder.transform);
                inst.transform.localRotation = Quaternion.identity;
                inst.transform.localScale = Vector3.one;
                inst.transform.localPosition = Vector3.zero;
                var idle = AssetDatabase.LoadAllAssetsAtPath($"{ModelRoot}/{name}/{name}.fbx").OfType<AnimationClip>().FirstOrDefault(c => c.name.EndsWith("Idle"));
                if (idle != null) idle.SampleAnimation(inst, 0f);
                // turn the model so the gun's frame lines up with the weapon: muzzle +Z, top +Y
                inst.transform.localRotation = Quaternion.Inverse(inst.GetComponent<GunRig>().frame);
                foreach (var t in inst.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = w.gameObject.layer;
                // first person: the grip (the model's root) at a set spot in front of the camera
                var fp = FirstPerson[name];
                Undo.RecordObject(root, "Gun place");
                root.localPosition = fp.grip;
                root.localRotation = Quaternion.Euler(fp.turn.x, fp.turn.y, fp.turn.z);
                inst.transform.localPosition = Vector3.zero;
                var rs = inst.GetComponentsInChildren<Renderer>(true);
                var b = new Bounds(holder.transform.InverseTransformPoint(rs[0].bounds.center), Vector3.zero);
                foreach (var r in rs) { b.Encapsulate(holder.transform.InverseTransformPoint(r.bounds.min)); b.Encapsulate(holder.transform.InverseTransformPoint(r.bounds.max)); }

                var rig = inst.GetComponent<GunRig>();
                Undo.RecordObject(rig, "Gun source");
                rig.source = w;
                PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
                // muzzle flash and its light ride the Muzzle bone
                var muzzle = new GameObject("MuzzleFX").transform;
                muzzle.SetParent(rig.muzzle != null ? rig.muzzle : inst.transform, false);
                ParticleSystem flash = null;
                var flashSource = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponTools.MuzzleFlashPath);
                float len = b.size.z;
                if (flashSource != null)
                {
                    var fx = (GameObject)PrefabUtility.InstantiatePrefab(flashSource, muzzle);
                    PrefabUtility.UnpackPrefabInstance(fx, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    fx.transform.localPosition = Vector3.zero;
                    fx.transform.rotation = inst.transform.rotation * rig.frame;   // flash out of the muzzle (+Z of the gun's frame), not along the rig root's axes
                    fx.transform.localScale = Vector3.one * Mathf.Clamp(len / 0.22f, 1f, 3f) * 0.6f;
                    foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        var main = ps.main; main.loop = false; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.Local;
                        var pr = ps.GetComponent<ParticleSystemRenderer>();
                        if (pr != null) pr.sharedMaterial = WeaponTools.ParticleToUrp(pr.sharedMaterial);
                        ps.gameObject.layer = w.gameObject.layer;
                    }
                    flash = fx.GetComponent<ParticleSystem>();
                }
                var lightGo = new GameObject("FlashLight");
                lightGo.transform.SetParent(muzzle, false);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point; light.range = 5f; light.intensity = 2.5f; light.color = new Color(1f, 0.75f, 0.4f);
                light.shadows = LightShadows.None; light.enabled = false;

                var vm = w.GetComponent<WeaponViewModel>();
                if (vm == null) vm = Undo.AddComponent<WeaponViewModel>(w.gameObject);
                Undo.RecordObject(vm, "Gun view model");
                vm.model = holder.transform; vm.muzzle = muzzle; vm.muzzleFlash = flash; vm.flashLight = light;
                EditorUtility.SetDirty(vm);

                if (name == "Shotgun" && w.data != null)
                {
                    Undo.RecordObject(w.data, "Double barrel");
                    w.data.weaponName = "Double Barrel";
                    w.data.magazineSize = 2;
                    w.data.fireRate = 2.5f;
                    w.data.reloadTime = 2.4f;
                    w.data.maxReserveAmmo = 24;
                    EditorUtility.SetDirty(w.data);
                }
                sb.AppendLine($"  {name}: on '{w.name}' ({b.size.z:0.00} m long), reload {(w.data != null ? w.data.reloadTime : 0f)} s.");
            }
            sb.AppendLine(InstallBodyPose(controller));
            Undo.CollapseUndoOperations(undo);
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            EditorSceneManager.SaveScene(controller.gameObject.scene);
            return sb + "  Scene saved.";
        }
    }
}
