using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZombieRush.Audio;
using ZombieRush.Data;
using ZombieRush.Waves;
using ZombieRush.Zombie;

namespace ZombieRush.EditorTools
{
    /// Zombie Rush > Characters > Set Up New Characters
    ///
    /// Takes the Blender-built characters in Assets/Models/2026-11-05_Characters (made by
    /// Tools/Blender/build_characters.py) and wires them into the game:
    ///   1. import settings: Humanoid avatar mapped bone by bone, no imported materials;
    ///   2. one URP material per character (baked albedo + glow map);
    ///   3. Yaku and Miner get their new bodies (the old prefabs are copied to Prefabs/Legacy first);
    ///   4. new Zombie_Spitter / Zombie_Bomber / Zombie_Screamer prefabs with their abilities and configs;
    ///   5. a Survivor prefab (the player character) in Prefabs/Characters;
    ///   6. the new sound effects in the sound library;
    ///   7. the new zombies in the open scene's WaveManager.
    /// Safe to run again: it rebuilds models and prefabs but never overwrites tuned config values.
    public static class CharacterSetupTools
    {
        const string ModelRoot = "Assets/Models/2026-11-05_Characters";
        const string PrefabDir = "Assets/Prefabs";
        const string LegacyDir = "Assets/Prefabs/Legacy";
        const string DataDir = "Assets/Data";
        const string BaseController = "Assets/Models/2026-09-25_Yaku/Zombie_Yaku.controller";
        const string SoundLibraryPath = "Assets/Resources/ZombieRush/SoundLibrary.asset";
        const string SfxDir = "Assets/Audio/SFX/Specials";

        /// Glow strength of each character's emission map (eyes, lamps, goo).
        static readonly Dictionary<string, float> Glow = new Dictionary<string, float>
        {
            ["Yaku"] = 4f, ["Miner"] = 4f, ["Spitter"] = 3.5f, ["Bomber"] = 4f, ["Screamer"] = 4.5f, ["Survivor"] = 0f,
        };

        /// Texture size on PC; Android gets half.
        const int TextureSize = 2048;

        /// Blender bone -> Unity humanoid bone.
        static readonly (string bone, string human)[] BoneMap =
        {
            ("Hips", "Hips"), ("Spine", "Spine"), ("Spine1", "Chest"), ("Spine2", "UpperChest"),
            ("Neck", "Neck"), ("Head", "Head"),
            ("LeftShoulder", "LeftShoulder"), ("LeftArm", "LeftUpperArm"), ("LeftForeArm", "LeftLowerArm"), ("LeftHand", "LeftHand"),
            ("RightShoulder", "RightShoulder"), ("RightArm", "RightUpperArm"), ("RightForeArm", "RightLowerArm"), ("RightHand", "RightHand"),
            ("LeftUpLeg", "LeftUpperLeg"), ("LeftLeg", "LeftLowerLeg"), ("LeftFoot", "LeftFoot"), ("LeftToeBase", "LeftToes"),
            ("RightUpLeg", "RightUpperLeg"), ("RightLeg", "RightLowerLeg"), ("RightFoot", "RightFoot"), ("RightToeBase", "RightToes"),
        };

        static readonly List<string> report = new List<string>();

        const string ReportPath = "Temp/ZR_CharacterSetup.log";

        [MenuItem("Zombie Rush/Characters/Set Up New Characters", priority = 30)]
        static void RunFromMenu() => Run(true);

        /// interactive = false: no dialogs (run by CharacterSetupAutoRun). The report goes to
        /// the Console and Temp/ZR_CharacterSetup.log either way.
        public static void Run(bool interactive)
        {
            report.Clear();
            try { RunSteps(interactive); }
            catch (System.Exception e)
            {
                report.Add("FAILED: " + e);
                Debug.LogException(e);
            }
            File.WriteAllText(ReportPath, string.Join("\n", report));
        }

        static void RunSteps(bool interactive)
        {
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var n in Glow.Keys) ImportSettings(n);
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh();

            var models = new Dictionary<string, (GameObject model, Material mat)>();
            foreach (var n in Glow.Keys)
            {
                var model = SetUpAvatar(n);
                if (model == null) continue;
                models[n] = (model, BuildMaterial(n));
            }
            if (models.Count == 0)
            {
                report.Add("No character models found.");
                if (interactive) EditorUtility.DisplayDialog("Characters", "No character models found in " + ModelRoot + ".\nRun Tools/Blender/build_characters.py first.", "OK");
                return;
            }
            MakeEffectMaterials();
            EnsureFolder(LegacyDir);
            EnsureFolder(PrefabDir + "/Characters");

            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(BaseController);

            // Redesigned originals: same AI, animations and tuning, new body.
            if (models.TryGetValue("Yaku", out var yaku)) Reskin(PrefabDir + "/Zombie_Yaku.prefab", yaku.model, yaku.mat);
            if (models.TryGetValue("Miner", out var miner)) Reskin(PrefabDir + "/Zombie_Miner.prefab", miner.model, miner.mat);

            // New specials, built on the Yaku prefab's root (AI, health, controller).
            var template = File.Exists(LegacyDir + "/Zombie_Yaku_Meshy.prefab") ? LegacyDir + "/Zombie_Yaku_Meshy.prefab" : PrefabDir + "/Zombie_Yaku.prefab";
            var made = new Dictionary<string, GameObject>();
            if (models.TryGetValue("Spitter", out var sp))
                made["Spitter"] = Special("Spitter", template, sp.model, sp.mat, controller,
                    Config("Spitter", 50f, 1.9f, 2.8f, 14f, 1.6f, 9f, 1.3f, 0.35f),
                    root => root.AddComponent<SpitterAttack>().config = Asset<SpitterConfig>("SpitterConfig"));
            if (models.TryGetValue("Bomber", out var bo))
                made["Bomber"] = Special("Bomber", template, bo.model, bo.mat, controller,
                    Config("Bomber", 40f, 2.6f, 4.6f, 0f, 1.4f, 0f, 2f, 0f),
                    root => root.AddComponent<BomberFuse>().config = Asset<BomberConfig>("BomberConfig"));
            if (models.TryGetValue("Screamer", out var sc))
                made["Screamer"] = Special("Screamer", template, sc.model, sc.mat, controller,
                    Config("Screamer", 70f, 2.0f, 2.9f, 14f, 1.7f, 10f, 1.3f, 0.35f),
                    root => root.AddComponent<ScreamerShriek>().config = Asset<ScreamerConfig>("ScreamerConfig"));
            if (models.TryGetValue("Survivor", out var sv)) Survivor(sv.model, sv.mat);

            FillSounds();
            AddToWaves(made);
            AssetDatabase.SaveAssets();

            string msg = string.Join("\n", report);
            Debug.Log("[Zombie Rush] Character setup done.\n" + msg);
            if (interactive) EditorUtility.DisplayDialog("Characters", "Done.\n\n" + msg +
                "\n\nTry them: Play, then Zombie Rush > Test > Spawn ... In Front Of Me.", "OK");
        }

        // ------------------------------------------------------------------ import

        static string Fbx(string n) => $"{ModelRoot}/ZR_{n}/ZR_{n}.fbx";

        static void ImportSettings(string n)
        {
            if (!(AssetImporter.GetAtPath(Fbx(n)) is ModelImporter imp)) return;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.importAnimation = false;
            imp.importCameras = false;
            imp.importLights = false;
            imp.importBlendShapes = false;
            imp.bakeAxisConversion = true;
            imp.SaveAndReimport();

            foreach (var suffix in new[] { "_Albedo", "_Emission", "_Normal", "_MetalSmooth" })
            {
                string p = $"{ModelRoot}/ZR_{n}/ZR_{n}{suffix}.png";
                if (!(AssetImporter.GetAtPath(p) is TextureImporter t)) continue;
                t.textureType = suffix == "_Normal" ? TextureImporterType.NormalMap : TextureImporterType.Default;
                t.sRGBTexture = suffix == "_Albedo" || suffix == "_Emission";
                t.alphaSource = suffix == "_MetalSmooth" ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                t.alphaIsTransparency = false;
                t.maxTextureSize = TextureSize;
                t.mipmapEnabled = true;
                t.textureCompression = TextureImporterCompression.CompressedHQ;
                // Phones get half size: a zombie fills little of a small screen.
                t.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                {
                    name = "Android", overridden = true, maxTextureSize = TextureSize / 2,
                    format = TextureImporterFormat.ASTC_6x6, compressionQuality = 50,
                });
                t.SaveAndReimport();
            }
        }

        /// Maps the Blender skeleton onto Unity's humanoid bones explicitly (no guessing),
        /// so the zombie animations retarget onto every character.
        static GameObject SetUpAvatar(string n)
        {
            string path = Fbx(n);
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (imp == null || asset == null) { report.Add($"ZR_{n}: model not found, skipped."); return null; }

            var skeleton = new List<SkeletonBone>();
            foreach (var t in asset.GetComponentsInChildren<Transform>(true))
                skeleton.Add(new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale });
            var names = new HashSet<string>(skeleton.Select(b => b.name));
            var human = BoneMap.Where(m => names.Contains(m.bone))
                .Select(m => new HumanBone { boneName = m.bone, humanName = m.human, limit = new HumanLimit { useDefaultValues = true } })
                .ToArray();

            var hd = imp.humanDescription;
            hd.human = human;
            hd.skeleton = skeleton.ToArray();
            hd.upperArmTwist = 0.5f; hd.lowerArmTwist = 0.5f;
            hd.upperLegTwist = 0.5f; hd.lowerLegTwist = 0.5f;
            hd.armStretch = 0.05f; hd.legStretch = 0.05f;
            hd.feetSpacing = 0f;
            hd.hasTranslationDoF = false;
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.humanDescription = hd;
            imp.SaveAndReimport();

            var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                report.Add($"ZR_{n}: Humanoid avatar FAILED - check the Console.");
                Debug.LogError($"[Zombie Rush] ZR_{n}: the Humanoid avatar is not valid. Mapped {human.Length} of {BoneMap.Length} bones.");
                return null;
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        static Material BuildMaterial(string n)
        {
            string dir = $"{ModelRoot}/ZR_{n}";
            string path = $"{dir}/Mat_ZR_{n}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/ZR_{n}_Albedo.png");
            var glow = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/ZR_{n}_Emission.png");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/ZR_{n}_Normal.png");
            var metalSmooth = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/ZR_{n}_MetalSmooth.png");
            mat.SetTexture("_BaseMap", albedo);
            mat.SetTexture("_MainTex", albedo);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Metallic", 0f);
            if (normal != null)
            {
                // Fine relief (pores, veins, weave, leather grain) so the surface catches light.
                mat.SetTexture("_BumpMap", normal);
                mat.SetFloat("_BumpScale", 1f);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (metalSmooth != null)
            {
                // R = metal, A = smoothness: wet blood and buckles shine, cloth stays matte.
                mat.SetTexture("_MetallicGlossMap", metalSmooth);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetFloat("_SmoothnessTextureChannel", 0f);
                mat.SetFloat("_Smoothness", 0.85f);
            }
            else mat.SetFloat("_Smoothness", 0.25f);
            if (glow != null && Glow[n] > 0f)
            {
                mat.SetTexture("_EmissionMap", glow);
                mat.SetColor("_EmissionColor", Color.white * Glow[n]);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                mat.SetColor("_EmissionColor", Color.black);
                mat.DisableKeyword("_EMISSION");
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material LitAsset(string path, Color color, Color emission, float smooth)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smooth);
            mat.SetColor("_EmissionColor", emission);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void MakeEffectMaterials()
        {
            EnsureFolder("Assets/Resources/ZombieRush");
            LitAsset("Assets/Resources/ZombieRush/GooBlob.mat", new Color(0.45f, 0.95f, 0.15f), new Color(0.5f, 2.2f, 0.15f), 0.85f);
            LitAsset("Assets/Resources/ZombieRush/GooPuddle.mat", new Color(0.2f, 0.5f, 0.05f), new Color(0.12f, 0.55f, 0.03f), 0.92f);
            LitAsset($"{ModelRoot}/ZR_Bomber/Mat_ZR_BombLight.mat", new Color(1f, 0.25f, 0.2f), new Color(4f, 0.25f, 0.15f), 0.6f);
        }

        // ------------------------------------------------------------------ prefabs

        /// Puts a new body into an existing zombie prefab. The first time, the original is
        /// kept in Prefabs/Legacy (e.g. Zombie_Yaku_Meshy.prefab) so it can be restored.
        static void Reskin(string prefabPath, GameObject model, Material mat)
        {
            if (!File.Exists(prefabPath)) { report.Add(Path.GetFileName(prefabPath) + " not found, skipped."); return; }
            string legacy = $"{LegacyDir}/{Path.GetFileNameWithoutExtension(prefabPath)}_Meshy.prefab";
            if (!File.Exists(legacy)) AssetDatabase.CopyAsset(prefabPath, legacy);

            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                SwapModel(root, model, mat);
                // The old model's material swap would paint the new body with the old texture at runtime.
                var visual = root.GetComponent<ZombieVisual>();
                if (visual != null) visual.materialOverride = null;
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                report.Add($"{Path.GetFileName(prefabPath)}: new body (old one kept as {Path.GetFileName(legacy)}).");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static GameObject Special(string n, string template, GameObject model, Material mat, RuntimeAnimatorController controller,
                                  ZombieConfigData config, System.Action<GameObject> addAbility)
        {
            string path = $"{PrefabDir}/Zombie_{n}.prefab";
            if (!File.Exists(path)) AssetDatabase.CopyAsset(template, path);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                root.name = "Zombie_" + n;
                foreach (var old in root.GetComponents<IZombieAbility>().OfType<Component>().ToArray()) Object.DestroyImmediate(old);
                var jm = root.GetComponent<JuggernautMotion>();
                if (jm != null) Object.DestroyImmediate(jm);

                SwapModel(root, model, mat);
                var visual = root.GetComponent<ZombieVisual>();
                if (visual != null) { visual.controller = controller; visual.materialOverride = null; }
                var health = root.GetComponent<ZombieHealth>();
                if (health != null) health.config = config;
                var ai = root.GetComponent<ZombieAI>();
                if (ai != null) ai.config = config;
                addAbility(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                report.Add($"Zombie_{n}.prefab: built.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        static void Survivor(GameObject model, Material mat)
        {
            string path = $"{PrefabDir}/Characters/Survivor.prefab";
            var root = new GameObject("Survivor");
            try
            {
                var inst = SwapModel(root, model, mat, glow: false);
                inst.GetComponent<Animator>().applyRootMotion = false;
                report.Add(SurvivorRigTools.Build(model, root, inst));
                PrefabUtility.SaveAsPrefabAsset(root, path);
                report.Add("Characters/Survivor.prefab: player character built.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// Replaces the prefab's model child (the object with the Animator) with `model`.
        const string GlowMaterialPath = "Assets/Art/Mat_ZombieGlow.mat";

        /// The shared green rim-glow material (shader: Zombie Rush/Zombie Rim Glow). Created once;
        /// tune its colour, edge sharpness and body glow in the Inspector.
        static Material GlowMaterial()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(GlowMaterialPath);
            if (m != null) return m;
            var shader = Shader.Find("Zombie Rush/Zombie Rim Glow");
            if (shader == null) { report.Add("Glow shader not found (Assets/Shaders/ZombieRim.shader) - zombies have no glow."); return null; }
            m = new Material(shader);
            m.SetColor("_RimColor", new Color(0.35f, 1.6f, 0.2f, 1f));
            m.SetFloat("_RimPower", 2.6f);
            m.SetFloat("_Fill", 0.1f);
            m.SetFloat("_RimIntensity", 1f);
            AssetDatabase.CreateAsset(m, GlowMaterialPath);
            return m;
        }

        static GameObject SwapModel(GameObject root, GameObject model, Material mat, bool glow = true)
        {
            var old = root.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a.gameObject != root);
            var cull = AnimatorCullingMode.CullUpdateTransforms;
            var ctrl = (RuntimeAnimatorController)null;
            Vector3 localPos = Vector3.zero;
            if (old != null)
            {
                cull = old.cullingMode;
                ctrl = old.runtimeAnimatorController;
                localPos = old.transform.localPosition;
                Object.DestroyImmediate(old.gameObject);
            }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            inst.name = "Model";
            inst.transform.localPosition = new Vector3(0f, localPos.y, 0f);
            inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale = Vector3.one;

            var animator = inst.GetComponent<Animator>();
            if (animator == null) animator = inst.AddComponent<Animator>();
            animator.avatar = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(model)).OfType<Avatar>().FirstOrDefault();
            animator.applyRootMotion = false;
            animator.cullingMode = cull;
            if (ctrl != null) animator.runtimeAnimatorController = ctrl;

            // The rest pose must face the prefab's forward (+Z), or the zombie walks backwards.
            Transform Bone(string n) => inst.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
            var lh = Bone("LeftHand");
            var rh = Bone("RightHand");
            if (lh != null && rh != null)
            {
                float z = root.transform.InverseTransformDirection(Vector3.Cross(rh.position - lh.position, Vector3.up)).z;
                if (z < 0.5f)
                {
                    report.Add($"WARNING {model.name}: rest pose faces BACKWARDS (forward z = {z:0.##}). Re-export from Blender.");
                    Debug.LogError($"[Zombie Rush] {model.name} faces backwards in its rest pose.");
                }
                else report.Add($"{model.name}: faces forward.");
            }

            var bombMat = AssetDatabase.LoadAssetAtPath<Material>($"{ModelRoot}/ZR_Bomber/Mat_ZR_BombLight.mat");
            var glowMat = glow ? GlowMaterial() : null;
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "BombLight" && bombMat != null) { r.sharedMaterials = new[] { bombMat }; continue; }
                // Body material, plus the green zombie glow drawn over it as a second pass.
                r.sharedMaterials = glowMat != null ? new[] { mat, glowMat } : new[] { mat };
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = false;
            }
            if (glow && root.GetComponent<ZombieGlow>() == null) root.AddComponent<ZombieGlow>();
            return inst;
        }

        // ------------------------------------------------------------------ data

        /// The zombie's base stats. Created once; later runs keep any values tuned since.
        static ZombieConfigData Config(string n, float health, float walk, float run, float runDistance,
                                       float range, float damage, float cooldown, float hitDelay)
        {
            string path = $"{DataDir}/ZombieConfig_{n}.asset";
            var c = AssetDatabase.LoadAssetAtPath<ZombieConfigData>(path);
            if (c != null) return c;
            c = ScriptableObject.CreateInstance<ZombieConfigData>();
            c.zombieName = n;
            c.maxHealth = health;
            c.moveSpeed = walk;
            c.chaseSpeed = run;
            c.runDistance = runDistance;
            c.attackRange = range;
            c.attackDamage = damage;
            c.attackCooldown = cooldown;
            c.attackHitDelay = hitDelay;
            c.sightRange = 45f;
            AssetDatabase.CreateAsset(c, path);
            return c;
        }

        static T Asset<T>(string name) where T : ScriptableObject
        {
            string path = $"{DataDir}/{name}.asset";
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        static void FillSounds()
        {
            var lib = AssetDatabase.LoadAssetAtPath<SoundLibrary>(SoundLibraryPath);
            if (lib == null) { report.Add("SoundLibrary not found: run Zombie Rush > Audio > Auto-Fill Sound Library."); return; }
            AudioClip Clip(string f) => AssetDatabase.LoadAssetAtPath<AudioClip>($"{SfxDir}/{f}.wav");
            if (lib.bombBeep == null) lib.bombBeep = Clip("bomb_beep");
            if (lib.explosion == null || lib.explosion.Length == 0) lib.explosion = new[] { Clip("explosion_1"), Clip("explosion_2") }.Where(c => c != null).ToArray();
            if (lib.gooSpit == null) lib.gooSpit = Clip("goo_spit");
            if (lib.gooSplat == null) lib.gooSplat = Clip("goo_splat");
            if (lib.shriek == null) lib.shriek = Clip("shriek");
            EditorUtility.SetDirty(lib);
            report.Add("Sound library: beep, explosions, goo spit/splat and shriek added.");
        }

        /// Adds the new zombies to the WaveManager of the open scene (once each).
        static void AddToWaves(Dictionary<string, GameObject> made)
        {
            var wm = Object.FindFirstObjectByType<WaveManager>();
            if (wm == null) { report.Add("No WaveManager in the open scene: open MainLevel and run this again to add the new zombies to the waves."); return; }
            var waves = new Dictionary<string, (int minWave, float weight)>
            {
                ["Spitter"] = (3, 0.55f), ["Bomber"] = (4, 0.45f), ["Screamer"] = (6, 0.25f),
            };
            Undo.RecordObject(wm, "Add special zombies to waves");
            if (wm.nearPlayerMinDistance > 8f || wm.nearPlayerMaxDistance > 15f)
            {
                wm.nearPlayerMinDistance = 8f;
                wm.nearPlayerMaxDistance = 15f;
                report.Add("Spawns: zombies now surface 8-15 m from you (or just outside the base while you're inside).");
            }
            var list = (wm.zombieTypes ?? new ZombieSpawnEntry[0]).ToList();
            foreach (var kv in made)
            {
                if (kv.Value == null || list.Any(e => e != null && e.prefab == kv.Value)) continue;
                var (minWave, weight) = waves[kv.Key];
                list.Add(new ZombieSpawnEntry { prefab = kv.Value, minWave = minWave, weight = weight });
                report.Add($"Waves: {kv.Key} from wave {minWave} (weight {weight}).");
            }
            wm.zombieTypes = list.ToArray();
            EditorUtility.SetDirty(wm);
            EditorSceneManager.MarkSceneDirty(wm.gameObject.scene);
            report.Add("Save the scene (Ctrl+S) to keep the wave changes.");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
