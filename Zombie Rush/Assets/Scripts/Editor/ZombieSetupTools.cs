using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZombieRush.Data;
using ZombieRush.Waves;
using ZombieRush.Zombie;

namespace ZombieRush.EditorTools
{
    /// Sets up a zombie model rigged on the standard Mixamo skeleton (mixamorig: bone names):
    ///   - Humanoid avatar, with the file's rest pose straightened into a T-pose reference,
    ///   - one URP material from its <Name>_Albedo / _Normal / _MetalSmooth textures,
    ///   - its own copy of the motion-capture zombie controller (Assets/Models/Zombie_Mocap: real
    ///     Mixamo idle, walk, run, attack, hit, dying), walk and run sped to match its config speeds,
    ///   - Assets/Prefabs/Zombie_<Name>.prefab (AI, health, spawning; basic zombie, no specials).
    ///
    /// Zombie Rush > Zombies > Start Over With The Wanderer: moves the motion-capture clips to
    /// Zombie_Mocap, sets up the Wanderer, makes it the only zombie in the waves and moves every old
    /// zombie (prefabs, models, configs, special-zombie assets) to Assets/_Archive/OldZombies.
    /// Markers: Temp/ZR_RunZombieStartOver; Temp/ZR_RunZombieSetup with the zombie name inside
    /// (report: Temp/ZR_ZombieSetup.log).
    public static class ZombieSetupTools
    {
        const string MocapFolder = "Assets/Models/Zombie_Mocap";
        const string MocapController = MocapFolder + "/Zombie_Mocap.controller";
        const string OldMocapFolder = "Assets/Models/2026-09-25_Yaku/Animations";
        const string OldMocapController = "Assets/Models/2026-09-25_Yaku/Zombie_Yaku.controller";
        const string TemplatePrefab = "Assets/Prefabs/Zombie_Yaku.prefab";
        const string ArchivedTemplate = "Assets/_Archive/OldZombies/Prefabs/Zombie_Yaku.prefab";
        const string TemplateConfig = "Assets/Data/ZombieConfig_Yaku.asset";
        const string GlowMaterial = "Assets/Art/Mat_ZombieGlow.mat";
        const string Archive = "Assets/_Archive/OldZombies";

        /// One zombie model and its tuning.
        public class Spec
        {
            public string Name, Folder;
            /// Name shown in game (health bars); empty = Name.
            public string DisplayName;
            public float Height = 1.85f, MoveSpeed = 0.85f, ChaseSpeed = 3.2f;
            /// Green rim glow strength (0 = none: the model looks exactly as made).
            public float Glow = 0.1f;
            /// Screen height (share of the screen) below which the low-detail <Name>_LOD1.fbx is drawn
            /// instead, and below which the zombie is not drawn at all.
            public float Lod1Below = 0.22f, CullBelow = 0.012f;
            /// Gameplay: health, how far away it starts running, wave it first appears in, spawn weight.
            public float Health = 110f, RunDistance = 12f, Weight = 1f;
            public int MinWave = 1;
            /// Most a clip may be sped up to match the speeds (fast zombies may run frantic).
            public float MaxRate = 1.8f;
            /// Use the model's own clips (Idle, Walk, Run, Attack, Hit, Death) instead of the shared motion capture.
            public bool OwnClips;
            /// Attack: damage, reach (gap between the bodies, m), seconds into the attack the hit lands, seconds between attacks. 0 = keep the config's.
            public float Damage, Reach, HitDelay, Cooldown;
            /// Poison zombie: its own Spit clip, ZombieSpitter and ToxicBurst with Assets/Data/PoisonConfig.asset.
            public bool Poison;
            /// Look under the night lighting (strong moonlight + post exposure wash pale textures to white):
            /// Tone darkens the base colour (1 = as painted), ColorBoost raises the colour texture's saturation
            /// (1 = as painted; above 1 writes <Name>_Albedo_Tuned.png and uses that).
            /// Contrast spreads the colour texture's brightness around its own average (1 = as painted), so
            /// blood, wounds and cloth folds read instead of one flat grey.
            /// Tint multiplies the colour texture (white = none): pale grey skin painted by Meshy reads as chalk
            /// under moonlight; a grimy warm tint turns it into dirty, rotting flesh.
            public float Tone = 0.64f, ColorBoost = 1.4f, Contrast = 1.25f;
            public Color Tint = new Color(0.96f, 0.90f, 0.78f);
        }

        /// 1: the normal zombie. Its own Blender clips (Blender/clips_wanderer.py): a limping shambler, bite lands at 0.65 s.
        public static readonly Spec Wanderer = new Spec { Name = "Wanderer", Folder = "Assets/Models/2026-11-08_Wanderer_Final", Glow = 0f, OwnClips = true,
            Damage = 8f, Cooldown = 1.5f, HitDelay = 0.65f,
        };
        /// 2: the fast zombie with less health. Runs from far away, a little faster than the player's jog.
        /// Its own Blender clips (Blender/clips_runner.py): crouched prowl, sprint, leaping rake that lands at 0.65 s.
        public static readonly Spec Runner = new Spec
        {
            Name = "Runner", Folder = "Assets/Models/2026-11-08_Runner_Final", Glow = 0f, OwnClips = true, HitDelay = 0.65f, Tint = new Color(1.0f, 0.86f, 0.80f),
            Height = 1.72f,   // measured in its crouched idle (a normal-sized man, hunched)
            MoveSpeed = 1f, ChaseSpeed = 4.2f, Health = 55f, RunDistance = 30f, MinWave = 2, Weight = 0.6f, MaxRate = 2.2f,
            Damage = 6f, Cooldown = 1.2f,
        };
        /// 3: the giant. Its own Blender clips (Blender/clips_horror.py): stomping walk, charge, two-fisted overhead slam (impact at frame 25 of 30 fps).
        public static readonly Spec Horror = new Spec
        {
            Name = "Horror", Folder = "Assets/Models/2026-11-08_Horror_Final", Glow = 0f, OwnClips = true, Height = 2.6f,
            MoveSpeed = 1.1f, ChaseSpeed = 2.6f, Health = 400f, RunDistance = 10f, MinWave = 3, Weight = 0.3f,
            Damage = 22f, Reach = 0.75f, HitDelay = 25f / 30f, Cooldown = 2.6f,
        };
        /// 4: the poison zombie (Toxic Ghoul). Its own Blender clips (Blender/clips_ghoul.py): heavy waddle, claw
        /// that lands at 0.64 s, a spit from 6-15 m, and a belly that bursts into gas when it dies.
        public static readonly Spec Ghoul = new Spec
        {
            Name = "Ghoul", DisplayName = "Toxic Ghoul", Folder = "Assets/Models/2026-11-09_Ghoul_Final", Glow = 0f, OwnClips = true, Poison = true, Height = 1.9f, ColorBoost = 1.15f, Tint = new Color(0.97f, 0.95f, 0.88f),
            MoveSpeed = 0.8f, ChaseSpeed = 2.3f, Health = 140f, RunDistance = 5f, MinWave = 2, Weight = 0.45f,
            Damage = 10f, Reach = 0.5f, HitDelay = 0.64f, Cooldown = 1.6f,
        };
        static readonly Dictionary<string, Spec> Specs = new Dictionary<string, Spec> { { "Wanderer", Wanderer }, { "Runner", Runner }, { "Horror", Horror }, { "Ghoul", Ghoul } };
        const string PoisonConfigPath = "Assets/Data/PoisonConfig.asset";

        [MenuItem("Zombie Rush/Zombies/Set Up Ghoul (poison)", priority = 23)]
        static void GhoulMenu() => Debug.Log("Zombie Rush: " + Setup(Ghoul));

        [MenuItem("Zombie Rush/Zombies/Set Up Horror (giant)", priority = 22)]
        static void HorrorMenu() => Debug.Log("Zombie Rush: " + Setup(Horror));

        [MenuItem("Zombie Rush/Zombies/Set Up Runner", priority = 21)]
        static void RunnerMenu() => Debug.Log("Zombie Rush: " + Setup(Runner));

        // Controller state -> motion-capture clip file in MocapFolder.
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

        // ------------------------------------------------------------------------ menu

        [MenuItem("Zombie Rush/Zombies/Start Over With The Wanderer", priority = 19)]
        static void StartOverMenu() => Debug.Log("Zombie Rush: " + StartOver());

        [MenuItem("Zombie Rush/Zombies/Set Up Wanderer", priority = 20)]
        static void WandererMenu() => Debug.Log("Zombie Rush: " + Setup(Wanderer));

        /// One zombie by name, "all", or "look" (only rebuild every zombie's material: tone and colour).
        public static string Setup(string name)
        {
            if (name == "look")
            {
                var lines = Specs.Values.Select(sp => { var m = BuildMaterial(sp); return $"{sp.Name}: {m.name} tone {sp.Tone:0.00}, colour boost {sp.ColorBoost:0.00}, contrast {sp.Contrast:0.00}, tint {sp.Tint.r:0.00}/{sp.Tint.g:0.00}/{sp.Tint.b:0.00}"; }).ToList();
                AssetDatabase.SaveAssets();
                return "Zombie look: " + string.Join("; ", lines) + ".";
            }
            if (name == "all") return string.Join(System.Environment.NewLine, Specs.Values.Select(s => Setup(s)));
            return Specs.TryGetValue(name, out var s) ? Setup(s) : $"FAILED: unknown zombie '{name}'.";
        }

        // ------------------------------------------------------------------------ start over

        public static string StartOver()
        {
            var report = new StringBuilder();
            AssetDatabase.Refresh();

            // 1. The motion-capture clips and their controller get a home of their own.
            Folder(MocapFolder);
            if (AssetDatabase.IsValidFolder(OldMocapFolder))
                foreach (var guid in AssetDatabase.FindAssets("", new[] { OldMocapFolder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (AssetDatabase.IsValidFolder(path)) continue;
                    Move(path, $"{MocapFolder}/{Path.GetFileName(path)}", report);
                }
            if (File.Exists(OldMocapController)) Move(OldMocapController, MocapController, report);

            // 2. The Wanderer.
            report.AppendLine(Setup(Wanderer));

            // 3. Waves: the Wanderer only.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(Wanderer));
            var waves = Object.FindAnyObjectByType<WaveManager>(FindObjectsInactive.Include);
            if (waves != null && prefab != null)
            {
                Undo.RecordObject(waves, "Start over with the Wanderer");
                waves.zombieTypes = new[] { new ZombieSpawnEntry { prefab = prefab, minWave = 1, weight = 1f } };
                EditorUtility.SetDirty(waves);
                EditorSceneManager.MarkSceneDirty(waves.gameObject.scene);
                EditorSceneManager.SaveScene(waves.gameObject.scene);
                report.AppendLine("Waves: Zombie_Wanderer only, from wave 1. Scene saved.");
            }
            else report.AppendLine("Waves: NOT changed (no WaveManager in the open scene, or no Wanderer prefab).");

            // 4. Old zombies to the archive (moved inside Unity, so nothing loses its references).
            string[] old =
            {
                "Assets/Prefabs/Zombie.prefab", "Assets/Prefabs/Zombie_Yaku.prefab", "Assets/Prefabs/Zombie_Miner.prefab",
                "Assets/Prefabs/Zombie_Spitter.prefab", "Assets/Prefabs/Zombie_Bomber.prefab", "Assets/Prefabs/Zombie_Screamer.prefab",
                "Assets/Prefabs/Legacy/Zombie_Yaku_Meshy.prefab", "Assets/Prefabs/Legacy/Zombie_Miner_Meshy.prefab", "Assets/Prefabs/Legacy/Zombie_Yaku_Blender.prefab",
            };
            foreach (var p in old) Move(p, $"{Archive}/Prefabs/{Path.GetFileName(p)}", report);
            string[] oldModels =
            {
                "Assets/Models/2026-09-25_Yaku", "Assets/Models/2026-10-18_MinerJuggernaut", "Assets/Models/2026-09-15_UndeadConstruction",
                "Assets/Models/2026-11-05_Characters/ZR_Yaku", "Assets/Models/2026-11-05_Characters/ZR_Miner", "Assets/Models/2026-11-05_Characters/ZR_Spitter",
                "Assets/Models/2026-11-05_Characters/ZR_Bomber", "Assets/Models/2026-11-05_Characters/ZR_Screamer", "Assets/Models/2026-11-08_Yaku_Final",
            };
            foreach (var p in oldModels) Move(p, $"{Archive}/Models/{Path.GetFileName(p)}", report);
            string[] oldData =
            {
                "Assets/Data/ZombieConfig_Yaku.asset", "Assets/Data/ZombieConfig_Miner.asset", "Assets/Data/ZombieConfig_Spitter.asset",
                "Assets/Data/ZombieConfig_Bomber.asset", "Assets/Data/ZombieConfig_Screamer.asset",
                "Assets/Data/SpitterConfig.asset", "Assets/Data/BomberConfig.asset", "Assets/Data/ScreamerConfig.asset",
            };
            foreach (var p in oldData) Move(p, $"{Archive}/Data/{Path.GetFileName(p)}", report);
            Move("Assets/Audio/SFX/Specials", $"{Archive}/Audio/Specials", report);
            Move("Assets/Resources/ZombieRush/GooBlob.mat", $"{Archive}/Materials/GooBlob.mat", report);
            Move("Assets/Resources/ZombieRush/GooPuddle.mat", $"{Archive}/Materials/GooPuddle.mat", report);

            AssetDatabase.SaveAssets();
            return report.ToString();
        }

        static void Move(string from, string to, StringBuilder report)
        {
            if (!File.Exists(from) && !AssetDatabase.IsValidFolder(from)) return;
            Folder(Path.GetDirectoryName(to).Replace('\\', '/'));
            string err = AssetDatabase.MoveAsset(from, to);
            report.AppendLine(string.IsNullOrEmpty(err) ? $"Moved {from} -> {to}" : $"COULD NOT MOVE {from}: {err}");
        }

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Folder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static string PrefabPath(Spec s) => $"Assets/Prefabs/Zombie_{s.Name}.prefab";

        // ------------------------------------------------------------------------ one zombie

        public static string Setup(Spec spec)
        {
            var report = new StringBuilder();
            string fbx = $"{spec.Folder}/{spec.Name}.fbx";
            var imp = AssetImporter.GetAtPath(fbx) as ModelImporter;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (imp == null || asset == null) return "FAILED: " + fbx + " not found.";

            // Avatar: explicit Mixamo map, T-pose reference.
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
            imp.importNormals = ModelImporterNormals.Import;   // the model's own normals, not recalculated
            imp.importTangents = ModelImporterTangents.CalculateMikk;
            imp.meshCompression = ModelImporterMeshCompression.Off;
            imp.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman) return $"FAILED: {spec.Name} humanoid avatar not valid (see Console).";
            int tris = asset.GetComponentsInChildren<SkinnedMeshRenderer>(true).Sum(s => s.sharedMesh != null ? s.sharedMesh.triangles.Length / 3 : 0);
            report.AppendLine($"{spec.Name}: humanoid, {human.Length} bones mapped, {tris} triangles, glow {(spec.Glow > 0f ? spec.Glow.ToString() : "off")}.");

            // Motion-capture clips: loop the cycles, keep forward travel as (ignored) root motion.
            var clips = new Dictionary<string, AnimationClip>();
            if (spec.OwnClips)
            {
                // The model's own clips: loop the cycles, keep them in place.
                var takes = imp.defaultClipAnimations;
                foreach (var c in takes)
                {
                    string n = c.name.Contains("|") ? c.name.Substring(c.name.LastIndexOf('|') + 1) : c.name;
                    bool loop = n == "Idle" || n == "Walk" || n == "Run";
                    c.loopTime = loop; c.loopPose = loop;
                    c.lockRootRotation = true; c.keepOriginalOrientation = true;
                    c.lockRootHeightY = true; c.keepOriginalPositionY = true;
                    c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
                }
                imp.clipAnimations = takes;
                imp.SaveAndReimport();
                var own = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();
                AnimationClip Own(string n) => own.FirstOrDefault(c => c.name == n || c.name.EndsWith("|" + n));
                foreach (var (state, clipName) in new[] { ("Idle", "Idle"), ("Walk", "Walk"), ("Run", "Run"), ("Attack", "Attack"), ("Hit", "Hit"), ("Die", "Death") })
                {
                    var c = Own(clipName);
                    if (c != null) clips[state] = c; else report.AppendLine($"  MISSING own clip {clipName}");
                }
                if (spec.Poison)
                {
                    var sp = Own("Spit");
                    if (sp != null) clips["Spit"] = sp; else report.AppendLine("  MISSING own clip Spit");
                }
                report.AppendLine($"  Clips: its own ({string.Join(", ", own.Select(c => c.name))}).");
            }
            else foreach (var kv in Mocap)
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
                        c.lockRootPositionXZ = false;
                    }
                    mi.clipAnimations = takes;
                    mi.SaveAndReimport();
                }
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
                if (clip == null) { report.AppendLine($"MISSING clip {path}"); continue; }
                clips[kv.Key] = clip;
            }

            // Config: its own copy, with this zombie's speeds.
            string cfgPath = $"Assets/Data/ZombieConfig_{spec.Name}.asset";
            var cfg = AssetDatabase.LoadAssetAtPath<ZombieConfigData>(cfgPath);
            if (cfg == null)
            {
                string src = File.Exists(TemplateConfig) ? TemplateConfig : $"{Archive}/Data/ZombieConfig_Yaku.asset";
                if (File.Exists(src)) AssetDatabase.CopyAsset(src, cfgPath);
                else AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<ZombieConfigData>(), cfgPath);
                cfg = AssetDatabase.LoadAssetAtPath<ZombieConfigData>(cfgPath);
            }
            cfg.zombieName = string.IsNullOrEmpty(spec.DisplayName) ? spec.Name : spec.DisplayName;   // the template's was "Yaku"
            cfg.moveSpeed = spec.MoveSpeed;
            cfg.chaseSpeed = spec.ChaseSpeed;
            cfg.maxHealth = spec.Health;
            cfg.runDistance = spec.RunDistance;
            if (spec.Damage > 0f) cfg.attackDamage = spec.Damage;
            if (spec.Reach > 0f) cfg.attackReach = spec.Reach;
            if (spec.HitDelay > 0f) cfg.attackHitDelay = spec.HitDelay;
            if (spec.Cooldown > 0f) cfg.attackCooldown = spec.Cooldown;
            EditorUtility.SetDirty(cfg);

            // Controller: a copy of the motion-capture controller, walk and run matched to the speeds.
            string ctrlPath = $"{spec.Folder}/Zombie_{spec.Name}.controller";
            if (!File.Exists(ctrlPath)) AssetDatabase.CopyAsset(MocapController, ctrlPath);
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
            if (ctrl == null) return report + $"FAILED: no controller (is {MocapController} there?).";
            foreach (var layer in ctrl.layers)
                foreach (var s in layer.stateMachine.states)
                {
                    if (!clips.TryGetValue(s.state.name, out var clip)) continue;
                    s.state.motion = clip;
                    float target = s.state.name == "Walk" ? cfg.moveSpeed : s.state.name == "Run" ? cfg.chaseSpeed : 0f;
                    if (target <= 0f) { s.state.speed = 1f; continue; }
                    // Own clips are made in place: their foot speed is the true ground speed.
                    float foot = MeshyBodyTools.MeasureGroundSpeed(asset, clip, spec.Height);
                    float travel = RootSpeed(asset, clip, spec.Height);
                    float ground = travel > 0.5f * foot ? travel : foot; // "in place" clips barely travel
                    float rate = ground > 0.2f ? Mathf.Clamp(target / ground, 0.6f, spec.MaxRate) : 1f;
                    s.state.speed = rate;
                    report.AppendLine($"  {s.state.name}: covers {ground:0.00} m/s, zombie {target} m/s -> plays x{rate:0.00}.");
                }
            if (clips.TryGetValue("Spit", out var spitClip)) report.AppendLine(AddSpitState(ctrl, spitClip));
            EditorUtility.SetDirty(ctrl);

            var mat = BuildMaterial(spec);

            // Prefab: from the basic zombie template the first time, then just a new body.
            string prefabPath = PrefabPath(spec);
            if (!File.Exists(prefabPath))
            {
                string template = File.Exists(TemplatePrefab) ? TemplatePrefab : ArchivedTemplate;
                if (!File.Exists(template)) return report + "FAILED: no zombie template prefab.";
                AssetDatabase.CopyAsset(template, prefabPath);
            }
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                root.name = "Zombie_" + spec.Name;
                var model = SwapModel(root, asset, avatar, mat, spec.Glow > 0f, report);
                model.GetComponent<Animator>().runtimeAnimatorController = ctrl;
                var visual = root.GetComponent<ZombieVisual>();
                if (visual != null) { visual.controller = ctrl; visual.materialOverride = null; }
                var health = root.GetComponent<ZombieHealth>();
                if (health != null) health.config = cfg;
                var ai = root.GetComponent<ZombieAI>();
                if (ai != null) ai.config = cfg;
                if (spec.Poison)
                {
                    var pc = AssetDatabase.LoadAssetAtPath<PoisonConfig>(PoisonConfigPath);
                    if (pc == null) { pc = ScriptableObject.CreateInstance<PoisonConfig>(); AssetDatabase.CreateAsset(pc, PoisonConfigPath); }
                    var spitter = root.GetComponent<ZombieSpitter>();
                    if (spitter == null) spitter = root.AddComponent<ZombieSpitter>();
                    spitter.config = pc;
                    var burst = root.GetComponent<ToxicBurst>();
                    if (burst == null) burst = root.AddComponent<ToxicBurst>();
                    burst.config = pc;
                    report.AppendLine($"  Poison: spits from {pc.spitMinRange}-{pc.spitMaxRange} m every {pc.spitCooldown} s, puddle {pc.puddleRadius} m, death cloud {pc.cloudRadius} m ({PoisonConfigPath}).");
                }
                var glowFx = root.GetComponent<ZombieGlow>();
                if (glowFx != null) { glowFx.intensity = spec.Glow; glowFx.pulse = 0.15f; }

                // Height, then stand on the ground in the idle pose.
                // How tall it stands in game: the mesh baked in its Idle pose (renderer and mesh bounds of a
                // skinned mesh are padded for its animations, so they depend on the clips and shrink the zombie).
                var rs = model.GetComponentsInChildren<Renderer>(true);
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                if (clips.TryGetValue("Idle", out var idle0)) idle0.SampleAnimation(model, 0f);
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr.sharedMesh == null) continue;
                    var bk = new Mesh(); smr.BakeMesh(bk, true);
                    foreach (var v in bk.vertices) { float y = (smr.transform.position + smr.transform.rotation * v).y; lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y); }
                    Object.DestroyImmediate(bk);
                }
                if (hi > lo) b = new Bounds(b.center, new Vector3(b.size.x, hi - lo, b.size.z));
                float scale = b.size.y > 0.01f ? spec.Height / b.size.y : 1f;
                model.transform.localScale = Vector3.one * scale;
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
                report.AppendLine($"  Body: {b.size.y:0.00} m standing (idle) as exported, scaled to {spec.Height} m.");
                var cc = root.GetComponent<CharacterController>();
                if (cc != null)
                {
                    float k = spec.Height / 1.85f; // capsule sized for a 1.85 m zombie, scaled to this one
                    cc.height = 1.8f * k;
                    cc.radius = 0.35f * k;
                    cc.center = new Vector3(0f, cc.height * 0.5f, 0f);
                }
                report.AppendLine(AddLod(spec, model, mat));
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                report.AppendLine($"  Prefab: {prefabPath}, config {cfgPath} (health {cfg.maxHealth}, walk {cfg.moveSpeed}, chase {cfg.chaseSpeed} m/s from {cfg.runDistance} m).");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            AssetDatabase.SaveAssets();
            report.AppendLine(AddToWaves(spec));
            return report.ToString();
        }

        /// A Spit state: any state goes to it on the Spit trigger, and it goes back to the default state when done.
        static string AddSpitState(AnimatorController ctrl, AnimationClip clip)
        {
            if (!ctrl.parameters.Any(p => p.name == "Spit")) ctrl.AddParameter("Spit", AnimatorControllerParameterType.Trigger);
            var sm = ctrl.layers[0].stateMachine;
            var st = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "Spit");
            if (st == null) st = sm.AddState("Spit", new Vector3(500f, 300f, 0f));
            st.motion = clip;
            st.speed = 1f;
            foreach (var t in sm.anyStateTransitions.Where(t => t.destinationState == st).ToArray()) sm.RemoveAnyStateTransition(t);
            var into = sm.AddAnyStateTransition(st);
            into.AddCondition(AnimatorConditionMode.If, 0f, "Spit");
            into.hasExitTime = false; into.duration = 0.15f; into.canTransitionToSelf = false;
            foreach (var t in st.transitions.ToArray()) st.RemoveTransition(t);
            var back = st.AddTransition(sm.defaultState);
            back.hasExitTime = true; back.exitTime = 0.92f; back.duration = 0.2f;
            return $"  Spit: state with {clip.name} ({clip.length:0.00} s), back to {sm.defaultState.name}.";
        }

        /// Puts the zombie in the open scene's waves (or updates its entry): first wave and weight.
        static string AddToWaves(Spec spec)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(spec));
            var waves = Object.FindAnyObjectByType<WaveManager>(FindObjectsInactive.Include);
            if (waves == null || prefab == null) return "  Waves: not changed (no WaveManager in the open scene).";
            Undo.RecordObject(waves, "Zombie waves");
            var list = (waves.zombieTypes ?? new ZombieSpawnEntry[0]).Where(e => e != null && e.prefab != null && e.prefab != prefab).ToList();
            list.Add(new ZombieSpawnEntry { prefab = prefab, minWave = spec.MinWave, weight = spec.Weight });
            waves.zombieTypes = list.OrderBy(e => e.minWave).ToArray();
            EditorUtility.SetDirty(waves);
            EditorSceneManager.MarkSceneDirty(waves.gameObject.scene);
            EditorSceneManager.SaveScene(waves.gameObject.scene);
            return $"  Waves: {string.Join(", ", waves.zombieTypes.Select(e => $"{e.prefab.name} from wave {e.minWave} (weight {e.weight})"))}. Scene saved.";
        }

        /// Replaces the prefab's body with a new model instance, facing +Z, with the body material and
        /// the (faint) zombie glow as a second pass.
        static GameObject SwapModel(GameObject root, GameObject model, Avatar avatar, Material mat, bool glow, StringBuilder report)
        {
            var old = root.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a.gameObject != root);
            var cull = old != null ? old.cullingMode : AnimatorCullingMode.CullUpdateTransforms;
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            inst.name = "Model";
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale = Vector3.one;
            var animator = inst.GetComponent<Animator>();
            if (animator == null) animator = inst.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = cull;

            // Face +Z: the right hand must be to the right of the left one.
            Transform Bone(string n) => inst.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "mixamorig:" + n);
            var la = Bone("LeftArm"); var ra = Bone("RightArm");
            if (la != null && ra != null)
            {
                float z = root.transform.InverseTransformDirection(Vector3.Cross(ra.position - la.position, Vector3.up)).z;
                if (z < 0f) { inst.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); report.AppendLine("  Facing: turned 180° to face forward."); }
                else report.AppendLine("  Facing: forward.");
            }

            var glowMat = glow ? AssetDatabase.LoadAssetAtPath<Material>(GlowMaterial) : null;
            if (!glow)
            {
                var g = root.GetComponent<ZombieGlow>();
                if (g != null) Object.DestroyImmediate(g, true);
            }
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                r.sharedMaterials = glowMat != null ? new[] { mat, glowMat } : new[] { mat };
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = false;
            }
            if (glowMat != null && root.GetComponent<ZombieGlow>() == null) root.AddComponent<ZombieGlow>();
            return inst;
        }

        /// Distance detail: <Name>_LOD1.fbx (a low-poly copy on the same skeleton) is skinned to the
        /// model's own bones and a LOD Group swaps it in when the zombie is small on screen, then stops
        /// drawing it altogether when it is tiny. Far zombies cost a fraction to skin and draw.
        static string AddLod(Spec spec, GameObject model, Material mat)
        {
            var oldGroup = model.GetComponent<LODGroup>();
            if (oldGroup != null) Object.DestroyImmediate(oldGroup);
            var oldLod = model.transform.Find("LOD1");
            if (oldLod != null) Object.DestroyImmediate(oldLod.gameObject);

            var near = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Cast<Renderer>().ToArray();
            string path = $"{spec.Folder}/{spec.Name}_LOD1.fbx";
            SkinnedMeshRenderer far = null;
            if (AssetImporter.GetAtPath(path) is ModelImporter li)
            {
                li.importAnimation = false;
                li.animationType = ModelImporterAnimationType.Generic;
                li.materialImportMode = ModelImporterMaterialImportMode.None;
                li.importNormals = ModelImporterNormals.Import;
                li.meshCompression = ModelImporterMeshCompression.Off;
                li.SaveAndReimport();
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(path)?.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(s => s.sharedMesh != null).OrderByDescending(s => s.sharedMesh.vertexCount).FirstOrDefault();
                if (src != null)
                {
                    var byName = model.GetComponentsInChildren<Transform>(true).GroupBy(x => x.name).ToDictionary(g => g.Key, g => g.First());
                    var bones = src.bones.Select(bn => bn != null && byName.TryGetValue(bn.name, out var mine) ? mine : null).ToArray();
                    if (bones.All(x => x != null))
                    {
                        var host = near.Length > 0 ? near[0].transform : model.transform;
                        var go = new GameObject("LOD1");
                        go.transform.SetParent(host.parent, false);
                        go.transform.localPosition = host.localPosition;
                        go.transform.localRotation = host.localRotation;
                        go.transform.localScale = host.localScale;
                        go.layer = host.gameObject.layer;
                        far = go.AddComponent<SkinnedMeshRenderer>();
                        far.sharedMesh = src.sharedMesh;
                        far.bones = bones;
                        far.rootBone = src.rootBone != null && byName.TryGetValue(src.rootBone.name, out var rb) ? rb : null;
                        far.sharedMaterials = new[] { mat };
                        far.quality = SkinQuality.Bone2;          // far away two bones per vertex is plenty
                        far.updateWhenOffscreen = false;
                        far.localBounds = src.localBounds;
                    }
                }
            }

            var group = model.AddComponent<LODGroup>();
            var lods = far != null
                ? new[] { new LOD(spec.Lod1Below, near), new LOD(spec.CullBelow, new Renderer[] { far }) }
                : new[] { new LOD(spec.CullBelow, near) };
            group.SetLODs(lods);
            group.fadeMode = LODFadeMode.None;
            group.RecalculateBounds();
            if (far == null) return "  LOD: no LOD1 model found; full detail until culled when tiny.";
            int nearTris = near.OfType<SkinnedMeshRenderer>().Sum(s => s.sharedMesh.triangles.Length / 3);
            return $"  LOD: {nearTris} triangles close up, {far.sharedMesh.triangles.Length / 3} below {spec.Lod1Below:P0} of screen height, not drawn below {spec.CullBelow:P1}.";
        }

        /// The rest skeleton straightened into a T-pose (spine and neck upright, arms out, legs down):
        /// the pose Unity measures every humanoid animation from.
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

        /// A clip's forward travel in game metres per second.
        static float RootSpeed(GameObject asset, AnimationClip clip, float height)
        {
            var temp = Object.Instantiate(asset);
            try
            {
                var anim = temp.GetComponent<Animator>();
                if (anim == null || !anim.isHuman) return 0f;
                var rs = temp.GetComponentsInChildren<Renderer>(true);
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                float scale = b.size.y > 0.01f ? height / b.size.y : 1f;
                Vector3 v = clip.averageSpeed; v.y = 0f;
                return v.magnitude * anim.humanScale * scale;
            }
            finally { Object.DestroyImmediate(temp); }
        }

        /// <Name>_Albedo_Tuned.png: the colour texture with its saturation raised by spec.ColorBoost, so blood,
        /// skin and cloth keep their colour under the blue moonlight. The painted texture stays untouched.
        static Texture2D TunedAlbedo(Spec spec)
        {
            string src = $"{spec.Folder}/{spec.Name}_Albedo.png", dst = $"{spec.Folder}/{spec.Name}_Albedo_Tuned.png";
            if (!System.IO.File.Exists(src)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(System.IO.File.ReadAllBytes(src))) { Object.DestroyImmediate(tex); return null; }
            var px = tex.GetPixels32();
            float k = spec.ColorBoost, ct = spec.Contrast;
            double sum = 0; for (int i = 0; i < px.Length; i++) sum += 0.299f * px[i].r + 0.587f * px[i].g + 0.114f * px[i].b;
            float mean = (float)(sum / Mathf.Max(1, px.Length));
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                float lum = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
                float l2 = mean + (lum - mean) * ct;                       // contrast round the texture's own average
                var tn = spec.Tint;
                px[i] = new Color32((byte)Mathf.Clamp(Mathf.RoundToInt((l2 + (c.r - lum) * k) * tn.r), 0, 255),
                                    (byte)Mathf.Clamp(Mathf.RoundToInt((l2 + (c.g - lum) * k) * tn.g), 0, 255),
                                    (byte)Mathf.Clamp(Mathf.RoundToInt((l2 + (c.b - lum) * k) * tn.b), 0, 255), c.a);
            }
            tex.SetPixels32(px); tex.Apply(false);
            System.IO.File.WriteAllBytes(dst, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(dst) is TextureImporter ti)
            {
                ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.maxTextureSize = 2048;
                ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
        }

        static Material BuildMaterial(Spec spec)
        {
            string path = $"{spec.Folder}/Mat_{spec.Name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            void Tex(string file, bool normal, bool srgb)
            {
                if (!(AssetImporter.GetAtPath($"{spec.Folder}/{file}") is TextureImporter ti)) return;
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = srgb;
                ti.maxTextureSize = 2048;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
            Tex($"{spec.Name}_Albedo.png", false, true);
            Tex($"{spec.Name}_Normal.png", true, false);
            Tex($"{spec.Name}_MetalSmooth.png", false, false);
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{spec.Folder}/{spec.Name}_Albedo.png");
            var normalMap = AssetDatabase.LoadAssetAtPath<Texture2D>($"{spec.Folder}/{spec.Name}_Normal.png");
            var ms = AssetDatabase.LoadAssetAtPath<Texture2D>($"{spec.Folder}/{spec.Name}_MetalSmooth.png");
            if ((spec.ColorBoost > 1.001f || Mathf.Abs(spec.Contrast - 1f) > 0.001f || spec.Tint != Color.white) && albedo != null) albedo = TunedAlbedo(spec) ?? albedo;
            mat.SetTexture("_BaseMap", albedo);
            mat.SetTexture("_MainTex", albedo);
            mat.SetColor("_BaseColor", new Color(spec.Tone, spec.Tone, spec.Tone, 1f));
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
