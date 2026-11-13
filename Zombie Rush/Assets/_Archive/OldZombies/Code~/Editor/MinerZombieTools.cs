using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZombieRush.Data;
using ZombieRush.Waves;
using ZombieRush.Zombie;

namespace ZombieRush.EditorTools
{
    /// Zombie Rush > Zombies > Add Miner Juggernaut To The Waves
    ///
    /// The Miner comes as three clean Meshy downloads (one armature, one mesh each), the same
    /// character with one animation each: Running, Double Combo Attack, Fall Dead. This tool:
    ///   1. imports the Running FBX as Humanoid (explicit bone map) and the other two as
    ///      Humanoid copying its avatar, with root motion baked so nothing slides or floats;
    ///   2. makes Miner.overrideController: Zombie_Yaku.controller with the Miner's own run,
    ///      attack and death (idle, walk and flinch stay the shared Yaku clips);
    ///   3. fills Mat_Miner with the Meshy textures;
    ///   4. saves Assets/Prefabs/Zombie_Miner.prefab (a copy of Zombie_Yaku.prefab: same AI,
    ///      health, rise) as a giant: 3.2 m tall, slow, lots of health, hits hard, more coins;
    ///   5. adds it to the open scene's WaveManager from wave 2 (undoable; save the scene).
    public static class MinerZombieTools
    {
        const string Folder = "Assets/Models/2026-10-18_MinerJuggernaut";
        const string Prefix = Folder + "/Meshy_AI_Undead_Miner_Juggerna_biped_Animation_";
        const string RunFbx = Prefix + "Running_withSkin.fbx";
        const string AttackFbx = Prefix + "Double_Combo_Attack_withSkin.fbx";
        const string DeathFbx = Prefix + "Fall_Dead_from_Abdominal_Injury_withSkin.fbx";
        const string MaterialPath = Folder + "/Mat_Miner.mat";
        const string OverridePath = Folder + "/Miner.overrideController";
        // Left over from the old Red_hammer.fbx setup (two armatures, hand-built avatar, baked attack).
        static readonly string[] OldGenerated = { Folder + "/Miner_Avatar.asset", Folder + "/Miner_Attack.anim" };
        const string Controller = "Assets/Models/2026-09-25_Yaku/Zombie_Yaku.controller";
        const string YakuPrefab = "Assets/Prefabs/Zombie_Yaku.prefab";
        const string MinerPrefab = "Assets/Prefabs/Zombie_Miner.prefab";
        const string ConfigPath = "Assets/Data/ZombieConfig_Miner.asset";
        const float Height = 3.2f;                       // a giant: the Yaku is about 2 m
        const float AlbedoBoost = 1.7f;
        const float EnhancedAlbedoBoost = 1.4f;
        const int MinerFirstWave = 5;   // matches the GDD
        const float MinerWeight = 0.5f; // Yaku is 1, so roughly 1 in 3 zombies once he appears

        // Unity humanoid bone -> Miner bone (Mixamo-style names: Hips > Spine > Spine1 > Spine2 > Neck > Head).
        static readonly (string human, string bone, bool required)[] BoneMap =
        {
            ("Hips", "Hips", true), ("Spine", "Spine", true), ("Chest", "Spine1", false), ("UpperChest", "Spine2", false),
            ("Neck", "Neck", false), ("Head", "Head", true),
            ("LeftShoulder", "LeftShoulder", false), ("LeftUpperArm", "LeftArm", true), ("LeftLowerArm", "LeftForeArm", true), ("LeftHand", "LeftHand", true),
            ("RightShoulder", "RightShoulder", false), ("RightUpperArm", "RightArm", true), ("RightLowerArm", "RightForeArm", true), ("RightHand", "RightHand", true),
            ("LeftUpperLeg", "LeftUpLeg", true), ("LeftLowerLeg", "LeftLeg", true), ("LeftFoot", "LeftFoot", true), ("LeftToes", "LeftToeBase", false),
            ("RightUpperLeg", "RightUpLeg", true), ("RightLowerLeg", "RightLeg", true), ("RightFoot", "RightFoot", true), ("RightToes", "RightToeBase", false),
        };

        [MenuItem("Zombie Rush/Zombies/Add Miner Juggernaut To The Waves (or update it)", priority = 20)]
        static void Run()
        {
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Controller);
            var missingFiles = new[] { RunFbx, AttackFbx, DeathFbx, Controller, YakuPrefab }
                .Where(p => AssetDatabase.LoadMainAssetAtPath(p) == null).ToList();
            if (missingFiles.Count > 0)
            {
                EditorUtility.DisplayDialog("Miner zombie", "Missing:\n" + string.Join("\n", missingFiles), "OK");
                return;
            }

            var material = FillMaterial();

            // 1. Rig: Humanoid from the Running model, shared by the other two.
            var avatar = ImportHumanoid(RunFbx, null, material, loop: true, bakeXZ: false, out string error);
            if (avatar == null)
            {
                Debug.LogError("[Zombie Rush] Miner setup stopped: " + error);
                EditorUtility.DisplayDialog("Miner zombie", error + "\n\nNothing was added to the waves.", "OK");
                return;
            }
            ImportHumanoid(AttackFbx, avatar, material, loop: false, bakeXZ: true, out _);
            ImportHumanoid(DeathFbx, avatar, material, loop: false, bakeXZ: true, out _);
            AnimationClip death = ClipOf(DeathFbx);
            ZombieAnimationTools.EnableIKPass(Controller); // feet on uneven ground (ZombieFootIK)
            foreach (var p in OldGenerated)
                if (AssetDatabase.LoadMainAssetAtPath(p) != null) AssetDatabase.DeleteAsset(p);

            var config = EnsureConfig();
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RunFbx));
            Dictionary<string, AnimationClip> authored;
            try
            {
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                model.name = "Miner";
                model.transform.position = Vector3.zero;
                model.transform.rotation = Quaternion.identity;

                // 2. Its own authored idle / walk / run / attack / hit (+ the Meshy death) on the
                //    shared zombie controller. Falls back to the Meshy run + attack if authoring fails.
                authored = MinerAnimationAuthoring.BakeAll(model, avatar);
                var states = new Dictionary<string, AnimationClip>(authored);
                if (!states.ContainsKey("Run")) states["Run"] = ClipOf(RunFbx);
                if (!states.ContainsKey("Attack")) states["Attack"] = ClipOf(AttackFbx);
                states["Die"] = death;
                var useController = MinerOverride(controller, states);

                // Cooldown = the whole slam; the hit lands at the slam's impact frame (JuggernautMotion
                // lands it at the same moment when it sees the hammer stop).
                bool own = authored.ContainsKey("Attack");
                float length = own ? MinerAnimationAuthoring.AttackLength : states["Attack"] != null ? states["Attack"].length : 2f;
                config.attackCooldown = length + 0.3f;
                config.attackHitDelay = own ? MinerAnimationAuthoring.AttackImpact : length * 0.5f;
                if (config.attackRange < 2.8f) config.attackRange = 2.8f; // a 3.2 m giant reaches further
                EditorUtility.SetDirty(config);

                var animator = model.GetComponent<Animator>();
                if (animator == null) animator = model.AddComponent<Animator>();
                animator.runtimeAnimatorController = useController;
                animator.avatar = avatar;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (material != null) r.sharedMaterials = Enumerable.Repeat(material, r.sharedMaterials.Length).ToArray();
                    r.updateWhenOffscreen = false;
                }

                var bounds = RendererBounds(model);
                float scale = Height / Mathf.Max(0.1f, bounds.size.y);
                model.transform.localScale *= scale;
                bounds = RendererBounds(model);
                model.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

                // 4. Prefab: Yaku's root (AI, health, rise) with the Miner as its body.
                BuildPrefab(model, config, useController, authored.Count > 0);
            }
            finally
            {
                if (model != null) Object.DestroyImmediate(model);
            }

            // 5. Waves.
            string waveNote = AddToWaves();
            AssetDatabase.SaveAssets();

            string clips = authored.Count > 0
                ? $"Animations: its own authored {string.Join(", ", authored.Keys)} (in {MinerAnimationAuthoring.ClipFolder}); death from Meshy\n"
                : "Animations: authoring FAILED - using the Meshy run / attack / death (see Console)\n";
            clips += "Juggernaut motion: stomps, camera shake, roar, hit lands on impact";
            string msg = $"{clips}\nPrefab: {MinerPrefab}\nConfig: {ConfigPath} (health {config.maxHealth}, damage {config.attackDamage}, slow)\n{waveNote}";
            Debug.Log("[Zombie Rush] Miner Juggernaut added.\n" + msg);
            EditorUtility.DisplayDialog("Miner zombie", "Done.\n\n" + msg, "OK");
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(MinerPrefab);
        }

        /// Humanoid import. source == null: build the avatar from this model (returns it);
        /// otherwise copy source. Root rotation and height are always baked into the pose;
        /// XZ only for one-shots (attack, death), so the run doesn't slide and snap back.
        /// Every FBX material is remapped to Mat_Miner: Meshy names its texture just "texture_0.png"
        /// in every download, so the model must never be left to find a texture by name.
        static Avatar ImportHumanoid(string path, Avatar source, Material material, bool loop, bool bakeXZ, out string error)
        {
            error = null;
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.importCameras = false;
            importer.importLights = false;
            if (material != null)
                foreach (var m in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name), material);
            importer.animationType = ModelImporterAnimationType.Human;
            if (source == null)
            {
                // The FBX's default pose is a frame of the run (left leg lifted). Built from that, the
                // avatar's reference is wrong and every animation twists the left leg. Build it from
                // the bind pose instead, straightened into the T-pose Unity expects.
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var model = Object.Instantiate(asset);
                model.name = asset.name; // skeleton entries must match the asset's names
                HumanDescription description;
                string missing;
                try
                {
                    ToBindPose(model);
                    EnforceTPose(model);
                    if (!ZombieAnimationTools.TryBuildDescription(model.transform, importer.humanDescription, out description, out missing, BoneMap))
                    {
                        error = "The Miner model is missing bones for a Humanoid rig:\n" + missing;
                        return null;
                    }
                }
                finally { Object.DestroyImmediate(model); }
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.humanDescription = description;
            }
            else
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = source;
            }
            importer.SaveAndReimport(); // clip list depends on the rig, so set it after this import

            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.loopTime = loop;
                clip.lockRootRotation = true;
                clip.keepOriginalOrientation = true;
                clip.lockRootHeightY = true;
                clip.keepOriginalPositionY = true; // the death keeps its fall to the floor
                clip.heightFromFeet = false;
                clip.lockRootPositionXZ = bakeXZ;
                clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();

            if (source != null) return source;
            var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                error = "Unity could not build a Humanoid avatar for the Miner.\nOpen " + RunFbx + " > Rig > Configure to see which bones are red.";
                return null;
            }
            return avatar;
        }

        /// Puts every skinned bone back where it was when the mesh was bound to it.
        static void ToBindPose(GameObject go)
        {
            var world = new Dictionary<Transform, Matrix4x4>();
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var bindposes = smr.sharedMesh != null ? smr.sharedMesh.bindposes : new Matrix4x4[0];
                for (int i = 0; i < smr.bones.Length && i < bindposes.Length; i++)
                    if (smr.bones[i] != null && !world.ContainsKey(smr.bones[i]))
                        world[smr.bones[i]] = smr.transform.localToWorldMatrix * bindposes[i].inverse;
            }
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) // parents before children
                if (world.TryGetValue(t, out var m)) { t.position = m.GetColumn(3); t.rotation = m.rotation; }
        }

        /// Legs straight down, arms straight out to the sides: Unity's humanoid reference pose.
        static void EnforceTPose(GameObject go)
        {
            var bones = new Dictionary<string, Transform>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                string key = t.name.Substring(t.name.LastIndexOf(':') + 1);
                if (!bones.ContainsKey(key)) bones[key] = t;
            }
            void Aim(string bone, string child, Vector3 dir)
            {
                if (!bones.TryGetValue(bone, out var b) || !bones.TryGetValue(child, out var c)) return;
                Vector3 cur = c.position - b.position;
                if (cur.sqrMagnitude > 1e-10f) b.rotation = Quaternion.FromToRotation(cur.normalized, dir) * b.rotation;
            }
            if (!bones.TryGetValue("LeftUpLeg", out var lUp) || !bones.TryGetValue("RightUpLeg", out var rUp)) return;
            Vector3 right = rUp.position - lUp.position; right.y = 0f; right.Normalize();
            Aim("LeftUpLeg", "LeftLeg", Vector3.down); Aim("LeftLeg", "LeftFoot", Vector3.down);
            Aim("RightUpLeg", "RightLeg", Vector3.down); Aim("RightLeg", "RightFoot", Vector3.down);
            Aim("LeftArm", "LeftForeArm", -right); Aim("LeftForeArm", "LeftHand", -right);
            Aim("RightArm", "RightForeArm", right); Aim("RightForeArm", "RightHand", right);
        }

        static AnimationClip ClipOf(string fbx) => AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));

        /// Zombie_Yaku.controller with each named state's clip swapped for the Miner's
        /// (state name -> clip; states not listed keep the Yaku clip).
        static RuntimeAnimatorController MinerOverride(RuntimeAnimatorController baseController, Dictionary<string, AnimationClip> states)
        {
            var swaps = new Dictionary<AnimationClip, AnimationClip>();
            if (baseController is UnityEditor.Animations.AnimatorController controller)
                foreach (var layer in controller.layers)
                    foreach (var s in layer.stateMachine.states)
                        if (s.state.motion is AnimationClip c && states.TryGetValue(s.state.name, out var mine) && mine != null)
                            swaps[c] = mine;

            var ov = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(OverridePath);
            if (ov == null)
            {
                ov = new AnimatorOverrideController(baseController) { name = "Miner" };
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

        static void BuildPrefab(GameObject model, ZombieConfigData config, RuntimeAnimatorController controller, bool authoredClips)
        {
            var root = PrefabUtility.LoadPrefabContents(YakuPrefab);
            try
            {
                // Remove Yaku's body (everything visual under the root).
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                    Object.DestroyImmediate(root.transform.GetChild(i).gameObject);

                var body = Object.Instantiate(model, root.transform);
                body.name = "Miner";
                body.transform.localPosition = model.transform.position;
                body.transform.localRotation = Quaternion.identity;

                root.name = "Zombie_Miner";
                var cc = root.GetComponent<CharacterController>();
                if (cc != null)
                {
                    cc.height = Height;
                    cc.radius = 0.7f;
                    cc.stepOffset = 0.5f;
                    cc.center = new Vector3(0f, Height * 0.5f, 0f);
                }
                var health = root.GetComponent<ZombieHealth>();
                if (health != null)
                {
                    health.config = config;
                    health.minCoins = 20;
                    health.maxCoins = 35;
                    health.ammoDropChance = 0.6f;
                }
                var ai = root.GetComponent<ZombieAI>();
                if (ai != null) ai.config = config;
                var rise = root.GetComponent<ZombieSpawnRise>();
                if (rise != null) { rise.riseHeight = Height + 0.3f; rise.riseDuration = 1.4f; }
                // ZombieVisual assigns its controller on Awake - it must be the Miner's, not the Yaku's.
                var visual = root.GetComponent<ZombieVisual>();
                if (visual != null) { visual.controller = controller; visual.materialOverride = null; }
                // Heavy-giant layer: stomps, shake, roar, hit on impact (+ hunch/limp/timing
                // only when it plays the Meshy clips instead of its authored ones).
                var motion = root.GetComponent<JuggernautMotion>();
                if (motion == null) motion = root.AddComponent<JuggernautMotion>();
                motion.authoredClips = authoredClips;

                PrefabUtility.SaveAsPrefabAsset(root, MinerPrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static string AddToWaves()
        {
            var waves = Object.FindAnyObjectByType<WaveManager>(FindObjectsInactive.Include);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MinerPrefab);
            if (waves == null) return "No WaveManager in the open scene - open MainLevel and run this again to add it to the waves.";
            var list = (waves.zombieTypes ?? new ZombieSpawnEntry[0]).ToList();
            Undo.RecordObject(waves, "Add Miner to waves");
            var entry = list.FirstOrDefault(e => e != null && e.prefab == prefab);
            if (entry == null)
            {
                entry = new ZombieSpawnEntry { prefab = prefab };
                list.Add(entry);
            }
            entry.minWave = MinerFirstWave;
            entry.weight = MinerWeight;
            waves.zombieTypes = list.ToArray();
            EditorUtility.SetDirty(waves);
            EditorSceneManager.MarkSceneDirty(waves.gameObject.scene);
            return $"WaveManager: Miner joins from wave {MinerFirstWave} (then about 1 in 3 zombies). Save the scene (Ctrl+S).";
        }

        static ZombieConfigData EnsureConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<ZombieConfigData>(ConfigPath);
            if (config != null) return config;
            config = ScriptableObject.CreateInstance<ZombieConfigData>();
            config.zombieName = "Miner Juggernaut";
            config.maxHealth = 320f;
            config.moveSpeed = 1.5f;
            config.chaseSpeed = 2.5f;
            config.runDistance = 10f;
            config.attackRange = 2.1f;
            config.attackDamage = 30f;
            config.attackCooldown = 2.1f;
            config.sightRange = 40f;
            AssetDatabase.CreateAsset(config, ConfigPath);
            return config;
        }

        /// Mat_Miner: the Meshy textures (albedo + normal + metallic/smoothness).
        static Material FillMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Mat_Miner" };
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            var textures = AssetDatabase.FindAssets("t:Texture2D", new[] { Folder })
                .Select(AssetDatabase.GUIDToAssetPath).ToList();
            // The enhanced set (Docs/Textures/MinerTexGen: grime, faded plaid, rotting skin, blood,
            // glowing headlamp) wins over the plain Meshy textures when it's there.
            string enhanced = textures.FirstOrDefault(p => p.EndsWith("texture_0_Enhanced.png"));
            string albedo = enhanced ?? textures.FirstOrDefault(p => p.EndsWith("texture_0.png"));
            string normal = textures.FirstOrDefault(p => p.EndsWith("_normal.png"));
            string emission = textures.FirstOrDefault(p => p.EndsWith("_Emission.png"));

            if (normal != null && AssetImporter.GetAtPath(normal) is TextureImporter ti && ti.textureType != TextureImporterType.NormalMap)
            {
                ti.textureType = TextureImporterType.NormalMap;
                ti.SaveAndReimport();
            }

            string metalSmooth = textures.FirstOrDefault(p => p.EndsWith("_MetallicSmoothness_Enhanced.png"))
                                 ?? textures.FirstOrDefault(p => p.EndsWith("_MetallicSmoothness.png"));

            // High-quality import at 2048: it's a big model seen up close, but 4K maps aren't worth 4x the memory.
            foreach (var p in new[] { albedo, normal, metalSmooth, emission })
            {
                if (p == null || !(AssetImporter.GetAtPath(p) is TextureImporter imp)) continue;
                bool linear = p == metalSmooth;
                if (imp.maxTextureSize == 2048 && imp.textureCompression == TextureImporterCompression.CompressedHQ && imp.sRGBTexture != linear) continue;
                imp.maxTextureSize = 2048; // plenty for a character; 4096 cost 4x the memory
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                if (linear) imp.sRGBTexture = false;
                imp.SaveAndReimport();
            }

            if (albedo != null)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(albedo);
                mat.SetTexture("_BaseMap", tex);
                mat.SetTexture("_MainTex", tex);
                // The Meshy texture is very dark (about half as bright as the Yaku's): lift it so the
                // plaid shirt, grey skin and helmet read at night instead of a black silhouette. The
                // enhanced texture already has its mid-tones lifted, so it needs less.
                float boost = enhanced != null ? EnhancedAlbedoBoost : AlbedoBoost;
                mat.SetColor("_BaseColor", new Color(boost, boost, boost, 1f));
            }
            if (normal != null)
            {
                mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
                mat.SetFloat("_BumpScale", 1.2f);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (emission != null)
            {
                // The miner's headlamp glows (HDR, so it blooms in the dark).
                mat.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(emission));
                mat.SetColor("_EmissionColor", new Color(1f, 0.92f, 0.75f) * 3f);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            if (metalSmooth != null)
            {
                // Meshy's metallic + roughness maps combined into URP's format (R metal, A smoothness).
                mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(metalSmooth));
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetFloat("_Smoothness", 0.5f); // half the gloss: full gloss mirrored the blue sky (a plastic sheen)
                mat.SetFloat("_SmoothnessTextureChannel", 0f);
            }
            else
            {
                mat.SetFloat("_Metallic", 0.1f);
                mat.SetFloat("_Smoothness", 0.25f);
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Bounds RendererBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
