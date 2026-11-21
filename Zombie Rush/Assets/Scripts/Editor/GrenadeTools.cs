using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Core;
using ZombieRush.Data;
using ZombieRush.Player;
using ZombieRush.UI;
using ZombieRush.UI.Kit;
using ZombieRush.Weapons;
using ZombieRush.World;
using ZombieRush.Zombie;

namespace ZombieRush.EditorTools
{
    /// Frag grenade setup (2026-11-18): model Blender/build_grenade.py, sounds Blender/sfx_grenade.py, icon
    /// Tools/UI/make_icon_grenade.py. This tool: import settings + materials, GrenadeConfig and the three prefabs in
    /// Resources/ZombieRush/Grenade (thrown, held in first person, pickup), GrenadeThrower on the scene Player and the
    /// RemotePlayer copy, GrenadeNet on NetPlayer, the HUD counter next to the ammo, a touch grenade button.
    /// Marker Temp/ZR_RunGrenade: "setup" (default) or "test" (Play: pickup, a cooked throw at zombies, a third-person
    /// throw, pictures Docs/Look/Grenade_*.png); report Temp/ZR_Grenade.log.
    [InitializeOnLoad]
    public static class GrenadeTools
    {
        const string ModelDir = "Assets/Models/2026-11-18_Grenade";
        const string FbxPath = ModelDir + "/Grenade.fbx";
        const string ResDir = "Assets/Resources/ZombieRush/Grenade";
        const string ConfigPath = ResDir + "/GrenadeConfig.asset";
        const string AudioDir = "Assets/Audio/SFX/Grenade";
        const string IconPath = "Assets/UI/Outbreak/Icons/ic_grenade.png";
        const string RingPath = "Assets/UI/Outbreak/Icons/ic_fusering.png";
        const string NetPlayerPath = "Assets/Resources/ZombieRush/Net/NetPlayer.prefab";
        const string RemotePlayerPath = "Assets/Resources/ZombieRush/Net/RemotePlayer.prefab";
        const string Marker = "Temp/ZR_RunGrenade";
        const string Report = "Temp/ZR_Grenade.log";
        const string TestKey = "ZR_GrenadeTest";
        static double nextCheck;

        static GrenadeTools()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(TestKey, false)) Test.Begin();
                if (s == PlayModeStateChange.EnteredEditMode) SessionState.EraseBool(TestKey);
            };
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextCheck) return;
            nextCheck = EditorApplication.timeSinceStartup + 2.0;
            if (!File.Exists(Marker)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            string mode = File.ReadAllText(Marker).Trim().ToLowerInvariant();
            File.Delete(Marker);
            if (mode == "test") { File.WriteAllText(Report, "(running)\n"); SessionState.SetBool(TestKey, true); EditorApplication.EnterPlaymode(); return; }
            string result;
            try { result = Setup(); }
            catch (System.Exception e) { result = "FAILED: " + e; }
            File.WriteAllText(Report, result);
        }

        [MenuItem("Zombie Rush/Weapons/Set Up Grenade")]
        static void SetupMenu() => Debug.Log(Setup());

        // ------------------------------------------------------------------ setup

        public static string Setup()
        {
            var sb = new StringBuilder();
            if (AssetImporter.GetAtPath(FbxPath) == null) return "FAILED: no " + FbxPath + " (run Blender/build_grenade.py --write).";
            Directory.CreateDirectory(ResDir);

            // textures, materials, model
            void Tex(string file, bool normal, bool srgb)
            {
                if (!(AssetImporter.GetAtPath($"{ModelDir}/{file}") is TextureImporter ti)) return;
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = srgb && !normal; ti.maxTextureSize = 512; ti.mipmapEnabled = true;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
            Tex("Grenade_Albedo.png", false, true); Tex("Grenade_Normal.png", true, false); Tex("Grenade_MetalSmooth.png", false, false);
            T2 Load<T2>(string p) where T2 : Object => AssetDatabase.LoadAssetAtPath<T2>(p);
            Material Mat(string name)
            {
                string path = $"{ModelDir}/{name}.mat";
                var m = Load<Material>(path);
                if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
                m.enableInstancing = true;
                return m;
            }
            var body = Mat("Grenade");
            body.SetTexture("_BaseMap", Load<Texture2D>($"{ModelDir}/Grenade_Albedo.png")); body.SetColor("_BaseColor", Color.white);
            body.SetTexture("_BumpMap", Load<Texture2D>($"{ModelDir}/Grenade_Normal.png")); body.SetFloat("_BumpScale", 0.6f); body.EnableKeyword("_NORMALMAP");
            body.SetTexture("_MetallicGlossMap", Load<Texture2D>($"{ModelDir}/Grenade_MetalSmooth.png")); body.SetFloat("_Metallic", 1f); body.SetFloat("_Smoothness", 1f);
            body.SetFloat("_SmoothnessTextureChannel", 0f); body.EnableKeyword("_METALLICSPECGLOSSMAP");
            EditorUtility.SetDirty(body);
            var metal = Mat("GrenadeMetal");
            metal.SetColor("_BaseColor", new Color(0.36f, 0.37f, 0.35f)); metal.SetFloat("_Metallic", 0.85f); metal.SetFloat("_Smoothness", 0.55f);
            EditorUtility.SetDirty(metal);
            var imp = (ModelImporter)AssetImporter.GetAtPath(FbxPath);
            imp.importAnimation = false; imp.animationType = ModelImporterAnimationType.None; imp.addCollider = false;
            imp.importCameras = false; imp.importLights = false;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Grenade"), body);
            imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "GrenadeMetal"), metal);
            imp.SaveAndReimport();
            var model = Load<GameObject>(FbxPath);

            // sounds
            AudioClip Clip(string n)
            {
                string p = $"{AudioDir}/{n}.wav";
                if (AssetImporter.GetAtPath(p) is AudioImporter ai)
                {
                    ai.forceToMono = true;
                    var s = ai.defaultSampleSettings;
                    s.loadType = n.StartsWith("grenade_explode") ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
                    s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = 0.6f;
                    ai.defaultSampleSettings = s; ai.SaveAndReimport();
                }
                return Load<AudioClip>(p);
            }

            // config
            var cfg = Load<GrenadeConfig>(ConfigPath);
            if (cfg == null) { cfg = ScriptableObject.CreateInstance<GrenadeConfig>(); AssetDatabase.CreateAsset(cfg, ConfigPath); }
            cfg.pinPull = Clip("grenade_pin"); cfg.spoon = Clip("grenade_spoon"); cfg.throwSwing = Clip("grenade_throw");
            cfg.bounce = new[] { Clip("grenade_bounce_1"), Clip("grenade_bounce_2"), Clip("grenade_bounce_3") }.Where(c => c != null).ToArray();
            cfg.explode = new[] { Clip("grenade_explode_1"), Clip("grenade_explode_2"), Clip("grenade_explode_3") }.Where(c => c != null).ToArray();

            // prefabs
            GameObject Part(string name, Transform parent)
            {
                var src = model.transform.Find(name);
                if (src == null) return null;
                var g = new GameObject(name);
                g.transform.SetParent(parent, false);
                g.transform.localPosition = src.localPosition; g.transform.localRotation = src.localRotation; g.transform.localScale = src.localScale;
                g.AddComponent<MeshFilter>().sharedMesh = src.GetComponent<MeshFilter>().sharedMesh;
                g.AddComponent<MeshRenderer>().sharedMaterials = src.GetComponent<MeshRenderer>().sharedMaterials;
                return g;
            }

            // thrown
            var proj = new GameObject("Grenade_Projectile");
            Part("Grenade_Body", proj.transform);
            var spoon = Part("Grenade_Spoon", proj.transform);
            var pin = Part("Grenade_Pin", proj.transform);
            var rb = proj.AddComponent<Rigidbody>();
            rb.mass = 0.4f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; rb.angularDamping = 1.5f;
            var sc = proj.AddComponent<SphereCollider>(); sc.radius = 0.042f;
            string pmPath = ResDir + "/Grenade.physicMaterial";
            var pm = Load<PhysicsMaterial>(pmPath);
            if (pm == null) { pm = new PhysicsMaterial("Grenade"); AssetDatabase.CreateAsset(pm, pmPath); }
            pm.bounciness = cfg.bounciness; pm.dynamicFriction = cfg.friction; pm.staticFriction = cfg.friction;
            pm.bounceCombine = PhysicsMaterialCombine.Maximum; pm.frictionCombine = PhysicsMaterialCombine.Average;
            EditorUtility.SetDirty(pm);
            sc.sharedMaterial = pm;
            var gp = proj.AddComponent<GrenadeProjectile>();
            gp.spoon = spoon != null ? spoon.transform : null; gp.pin = pin != null ? pin.transform : null;
            cfg.projectilePrefab = PrefabUtility.SaveAsPrefabAsset(proj, ResDir + "/Grenade_Projectile.prefab");
            Object.DestroyImmediate(proj);

            // held in first person (pin already out): FirstPersonView layer, no shadows
            int fpLayer = LayerMask.NameToLayer(FirstPersonViewmodel.LayerName);
            var held = new GameObject("Grenade_Held");
            Part("Grenade_Body", held.transform); Part("Grenade_Spoon", held.transform);
            foreach (var t in held.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = fpLayer >= 0 ? fpLayer : 0;
            foreach (var r in held.GetComponentsInChildren<MeshRenderer>()) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
            cfg.heldPrefab = PrefabUtility.SaveAsPrefabAsset(held, ResDir + "/Grenade_Held.prefab");
            Object.DestroyImmediate(held);

            // pickup: bigger model so it reads on the ground, a small warm glow at night
            var pick = new GameObject("Grenade_Pickup");
            var trig = pick.AddComponent<SphereCollider>(); trig.isTrigger = true; trig.radius = 0.7f; trig.center = Vector3.up * 0.4f;
            var disp = new GameObject("Display").transform; disp.SetParent(pick.transform, false);
            var scale = new GameObject("Model").transform; scale.SetParent(disp, false); scale.localScale = Vector3.one * 2.2f;
            Part("Grenade_Body", scale); Part("Grenade_Spoon", scale); Part("Grenade_Pin", scale);
            var lg = new GameObject("Glow"); lg.transform.SetParent(pick.transform, false); lg.transform.localPosition = Vector3.up * 0.5f;
            var light = lg.AddComponent<Light>(); light.type = LightType.Point; light.color = new Color(1f, 0.8f, 0.4f);
            light.range = 2.2f; light.intensity = 1.6f; light.shadows = LightShadows.None;
            var pu = pick.AddComponent<GrenadePickup>(); pu.display = disp;
            cfg.pickupPrefab = PrefabUtility.SaveAsPrefabAsset(pick, ResDir + "/Grenade_Pickup.prefab");
            Object.DestroyImmediate(pick);
            sb.Append(SetupAnims(cfg, body, metal));
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();
            sb.AppendLine($"Config {ConfigPath}: fuse {cfg.fuse} s, carry {cfg.maxCarry}, start {cfg.startCount}, chest chance {cfg.chestChance:0.00}, " +
                          $"radius {cfg.radius} m, zombie {cfg.zombieDamage} / player {cfg.playerDamage} at the centre; sounds: pin {(cfg.pinPull != null)}, " +
                          $"bounce {cfg.bounce.Length}, explode {cfg.explode.Length}. Prefabs: projectile, held (layer {fpLayer}), pickup.");

            // the player, its teammate copy and the net player
            var player = Object.FindObjectsByType<PlayerHealth>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(p => p.gameObject.scene.IsValid() && (p.GetComponentInChildren<PlayerWeaponController>() == null || !p.GetComponentInChildren<PlayerWeaponController>().remote));
            if (player == null) return "FAILED: no Player in the open scene.\n" + sb;
            if (!player.TryGetComponent<GrenadeThrower>(out _)) Undo.AddComponent<GrenadeThrower>(player.gameObject);
            sb.AppendLine($"GrenadeThrower on '{player.name}'.");
            AddToPrefab<GrenadeThrower>(RemotePlayerPath, sb);
            AddToPrefab<Net.GrenadeNet>(NetPlayerPath, sb);

            sb.Append(BuildHud());
            sb.Append(BuildTouchButton());

            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
            EditorSceneManager.SaveScene(player.gameObject.scene);
            AssetDatabase.SaveAssets();
            sb.AppendLine("Scene saved.");
            return sb.ToString();
        }

        const string BodyMatPath = "Assets/Models/2026-11-14_ShadowOperative_Final/Mat_ShadowOperative.mat";
        static readonly string[] DeltaBones = { "Chest", "UpperChest" };

        /// Blender/grenade_anim.py: FP_Grenade.fbx (first-person arm) and Operative_Grenade.fbx (third-person clips),
        /// both Generic like the guns' body clips; the arm wears the body's material, the grenade its own.
        static string SetupAnims(GrenadeConfig cfg, Material body, Material metal)
        {
            var sb = new StringBuilder();
            var bodyMat = AssetDatabase.LoadAssetAtPath<Material>(BodyMatPath);
            AnimationClip[] Import(string path)
            {
                if (!(AssetImporter.GetAtPath(path) is ModelImporter imp)) { sb.AppendLine($"No {path} (run Blender/grenade_anim.py --export)."); return null; }
                imp.animationType = ModelImporterAnimationType.Generic;
                imp.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
                imp.importBlendShapes = false; imp.importCameras = false; imp.importLights = false;
                imp.animationCompression = ModelImporterAnimationCompression.Off;
                imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                foreach (var m in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Select(m => m.name).Distinct().ToList())
                {
                    var to = m.StartsWith("GrenadeMetal") ? metal : m.StartsWith("Grenade") ? body : bodyMat;
                    if (to != null) imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m), to);
                }
                var takes = imp.defaultClipAnimations;
                foreach (var c in takes) { c.name = c.name.Split('|').Last(); c.loopTime = false; c.loopPose = false; }
                imp.clipAnimations = takes;
                imp.SaveAndReimport();
                return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();
            }
            // the clip moments, as Blender/grenade_anim.py keys them (30 fps): pin out at frame 8, arm cocked at 16,
            // release at 0.36 of the throw
            cfg.pinPullAt = 8f / 30f; cfg.cookRaise = 16f / 30f; cfg.releaseAt = 0.36f;
            string fpPath = ModelDir + "/FP_Grenade.fbx", tpPath = ModelDir + "/Operative_Grenade.fbx";
            var fp = Import(fpPath);
            if (fp != null)
            {
                cfg.fpArms = AssetDatabase.LoadAssetAtPath<GameObject>(fpPath);
                cfg.fpCook = fp.FirstOrDefault(c => c.name == "FPGrenade_Cook");
                cfg.fpThrow = fp.FirstOrDefault(c => c.name == "FPGrenade_Throw");
                sb.AppendLine($"First person: {fpPath}, clips {string.Join(", ", fp.Select(c => $"{c.name} {c.length:0.00}s"))}.");
            }
            var tp = Import(tpPath);
            if (tp != null)
            {
                cfg.tpProxy = AssetDatabase.LoadAssetAtPath<GameObject>(tpPath);
                cfg.tpRest = tp.FirstOrDefault(c => c.name == "Grenade_Rest");
                cfg.tpCook = tp.FirstOrDefault(c => c.name == "Grenade_Cook");
                cfg.tpThrow = tp.FirstOrDefault(c => c.name == "Grenade_Throw");
                var moved = new HashSet<string>();
                foreach (var c in new[] { cfg.tpCook, cfg.tpThrow }.Where(c => c != null))
                    foreach (var b in AnimationUtility.GetCurveBindings(c))
                        if (b.propertyName.StartsWith("m_LocalRotation")) moved.Add(b.path.Split('/').Last());
                // both arms: the right throws, the left pulls the pin and points ahead
                cfg.tpCopyBones = moved.Where(n => (n.StartsWith("Right") || n.StartsWith("Left")) && (n.Contains("Shoulder") || n.Contains("Arm") || n.Contains("Hand"))).OrderBy(n => n).ToArray();
                cfg.tpDeltaBones = moved.Where(n => DeltaBones.Contains(n)).ToArray();
                sb.AppendLine($"Third person: {tpPath}, clips {string.Join(", ", tp.Select(c => $"{c.name} {c.length:0.00}s"))}; " +
                              $"{cfg.tpCopyBones.Length} arm bones copied, {cfg.tpDeltaBones.Length} chest bones turned.");
            }
            return sb.ToString();
        }

        static void AddToPrefab<T>(string path, StringBuilder sb) where T : Component
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { sb.AppendLine($"No {path}."); return; }
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (!root.TryGetComponent<T>(out _)) { root.AddComponent<T>(); PrefabUtility.SaveAsPrefabAsset(root, path); sb.AppendLine($"{typeof(T).Name} added to {path}."); }
                else sb.AppendLine($"{typeof(T).Name} already on {path}.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// The counter: grenade icon + "x0", above the right end of the ammo block, with a fuse ring round the icon.
        static string BuildHud()
        {
            var kit = Object.FindAnyObjectByType<KitHUD>(FindObjectsInactive.Include);
            var hud = Object.FindAnyObjectByType<HUDController>(FindObjectsInactive.Include);
            Text mag = kit != null && kit.magazineText != null ? kit.magazineText : hud != null ? hud.magazineText : null;
            if (mag == null) return "HUD: no KitHUD / HUDController with a magazine text; counter skipped.\n";
            var canvas = mag.canvas != null ? mag.canvas.rootCanvas : null;
            if (canvas == null) return "HUD: no canvas; counter skipped.\n";
            var canvasRT = (RectTransform)canvas.transform;
            // the ammo panel: the magazine text's biggest ancestor that is still narrower than 60% of the screen
            var blockRT = mag.rectTransform;
            while (blockRT.parent is RectTransform up && up != canvasRT && up.rect.width < canvasRT.rect.width * 0.6f) blockRT = up;
            var parent = (RectTransform)blockRT.parent;
            foreach (var g in canvas.GetComponentsInChildren<GrenadeHUD>(true).ToList()) Undo.DestroyObjectImmediate(g.gameObject);

            var corners = new Vector3[4]; blockRT.GetWorldCorners(corners);
            Vector2 bottomLeft = parent.InverseTransformPoint(corners[0]);
            float h = Mathf.Clamp(blockRT.rect.height * 0.42f, 44f, 90f);

            var root = new GameObject("GrenadeHUD", typeof(RectTransform)).GetComponent<RectTransform>();
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Grenade HUD");
            root.SetParent(parent, false);
            // hang it off the same screen corner as the ammo panel, just left of the panel's bottom (clear of the key hints above it)
            var anchor = new Vector2(blockRT.anchorMax.x, blockRT.anchorMin.y);
            root.anchorMin = root.anchorMax = anchor; root.pivot = new Vector2(1f, 0f);
            root.sizeDelta = new Vector2(h * 2.3f, h);
            Vector2 anchorPoint = parent.rect.min + Vector2.Scale(parent.rect.size, anchor);
            root.anchoredPosition = bottomLeft - anchorPoint + new Vector2(-h * 0.25f, h * 0.1f);

            var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            icon.rectTransform.SetParent(root, false);
            icon.rectTransform.anchorMin = new Vector2(0f, 0f); icon.rectTransform.anchorMax = new Vector2(0f, 1f);
            icon.rectTransform.pivot = new Vector2(0f, 0.5f); icon.rectTransform.sizeDelta = new Vector2(h, 0f);
            icon.sprite = Sprite(IconPath); icon.preserveAspect = true; icon.raycastTarget = false;

            var ring = new GameObject("FuseRing", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            ring.rectTransform.SetParent(icon.rectTransform, false);
            ring.rectTransform.anchorMin = Vector2.zero; ring.rectTransform.anchorMax = Vector2.one;
            ring.rectTransform.sizeDelta = new Vector2(h * 0.35f, h * 0.35f);
            ring.sprite = Sprite(RingPath); ring.type = Image.Type.Filled; ring.fillMethod = Image.FillMethod.Radial360;
            ring.fillOrigin = (int)Image.Origin360.Top; ring.fillClockwise = false; ring.raycastTarget = false; ring.enabled = false;

            var text = new GameObject("Count", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            var src = mag;
            text.rectTransform.SetParent(root, false);
            text.rectTransform.anchorMin = new Vector2(0f, 0f); text.rectTransform.anchorMax = new Vector2(1f, 1f);
            text.rectTransform.offsetMin = new Vector2(h * 1.12f, 0f); text.rectTransform.offsetMax = Vector2.zero;
            text.font = src.font; text.fontStyle = src.fontStyle;
            text.fontSize = Mathf.RoundToInt(h * 0.62f); text.alignment = TextAnchor.MiddleLeft; text.text = "x0";
            text.horizontalOverflow = HorizontalWrapMode.Overflow; text.raycastTarget = false;
            var outline = src.GetComponent<Outline>();
            if (outline != null) { var o = text.gameObject.AddComponent<Outline>(); o.effectColor = outline.effectColor; o.effectDistance = outline.effectDistance; }

            var gh = root.gameObject.AddComponent<GrenadeHUD>();
            gh.icon = icon; gh.countText = text; gh.fuseRing = ring;

            // the centre fuse timer: a ring round the screen centre, the seconds under it, a dot in the middle
            foreach (Transform old in canvasRT) if (old.name == "GrenadeTimer") { Undo.DestroyObjectImmediate(old.gameObject); break; }
            const float D = 120f;
            var timer = new GameObject("GrenadeTimer", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
            Undo.RegisterCreatedObjectUndo(timer.gameObject, "Grenade timer");
            timer.SetParent(canvasRT, false);
            timer.anchorMin = timer.anchorMax = timer.pivot = new Vector2(0.5f, 0.5f);
            timer.sizeDelta = new Vector2(D, D); timer.anchoredPosition = Vector2.zero;
            var group = timer.GetComponent<CanvasGroup>(); group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;
            Image Img(string name, Transform parent, Sprite sprite, Vector2 size)
            {
                var im = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                im.rectTransform.SetParent(parent, false);
                im.rectTransform.anchorMin = im.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                im.rectTransform.sizeDelta = size; im.sprite = sprite; im.raycastTarget = false; im.preserveAspect = true;
                return im;
            }
            var ringSprite = Sprite(RingPath);
            var back = Img("Back", timer, ringSprite, new Vector2(D, D));
            var fill = Img("Fuse", timer, ringSprite, new Vector2(D, D));
            fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = (int)Image.Origin360.Top; fill.fillClockwise = false;
            var dot = Img("Dot", timer, AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"), new Vector2(9f, 9f));
            var dotOutline = dot.gameObject.AddComponent<Outline>(); dotOutline.effectColor = new Color(0f, 0f, 0f, 0.7f);
            var secs = new GameObject("Seconds", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            secs.rectTransform.SetParent(timer, false);
            secs.rectTransform.anchorMin = secs.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            secs.rectTransform.pivot = new Vector2(0.5f, 1f);
            secs.rectTransform.sizeDelta = new Vector2(D * 1.4f, 44f); secs.rectTransform.anchoredPosition = new Vector2(0f, -6f);
            secs.font = src.font; secs.fontStyle = src.fontStyle; secs.fontSize = 36; secs.alignment = TextAnchor.UpperCenter;
            secs.text = "3.5"; secs.raycastTarget = false; secs.horizontalOverflow = HorizontalWrapMode.Overflow;
            var so = secs.gameObject.AddComponent<Outline>();
            so.effectColor = outline != null ? outline.effectColor : new Color(0f, 0f, 0f, 0.8f);
            so.effectDistance = outline != null ? outline.effectDistance : new Vector2(2f, -2f);
            gh.timerGroup = group; gh.timerBack = back; gh.timerRing = fill; gh.timerText = secs; gh.timerDot = dot;
            return $"HUD: grenade counter left of the ammo panel '{blockRT.name}' ({root.sizeDelta.x:0} x {root.sizeDelta.y:0}); fuse timer at the screen centre ({D:0}).\n";
        }

        /// A copy of the touch Reload button, set to the grenade action, in a free spot beside it.
        static string BuildTouchButton()
        {
            var buttons = Object.FindObjectsByType<TouchButton>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var reload = buttons.FirstOrDefault(b => b.action == TouchAction.Reload);
            if (reload == null) return "Touch: no Reload button to copy; grenade button skipped.\n";
            foreach (var b in buttons.Where(b => b.action == TouchAction.Grenade).ToList()) Undo.DestroyObjectImmediate(b.gameObject);
            var rt = (RectTransform)reload.transform;
            var copy = (GameObject)Object.Instantiate(reload.gameObject, rt.parent);
            Undo.RegisterCreatedObjectUndo(copy, "Grenade button");
            copy.name = "GrenadeButton";
            var cb = copy.GetComponent<TouchButton>(); cb.action = TouchAction.Grenade;
            var crt = (RectTransform)copy.transform;
            // the icon: the child image that is not the button's own visual
            var sprite = Sprite(IconPath);
            foreach (var img in copy.GetComponentsInChildren<Image>(true))
                if (img != cb.visual && img.sprite != null && img.gameObject != copy) img.sprite = sprite;
            foreach (var txt in copy.GetComponentsInChildren<Text>(true)) txt.text = "";
            // a free spot: up, up-left, left, down-left of the reload button
            var others = buttons.Where(b => b != null && b.gameObject != copy).Select(b => (RectTransform)b.transform).ToList();
            Vector2 size = rt.rect.size * 1.12f;
            Vector2[] tries = { new Vector2(0, 1), new Vector2(-1, 1), new Vector2(-1, 0), new Vector2(-1, -1), new Vector2(0, 2), new Vector2(-2, 0) };
            Vector2 home = rt.anchoredPosition; string where = "on top of reload (no free spot)";
            foreach (var d in tries)
            {
                crt.anchoredPosition = home + Vector2.Scale(d, size);
                if (!others.Any(o => Overlaps(crt, o))) { where = $"offset {d}"; break; }
            }
            return $"Touch: grenade button made from '{reload.name}', {where}.\n";
        }

        static bool Overlaps(RectTransform a, RectTransform b)
        {
            var ca = new Vector3[4]; var cbs = new Vector3[4]; a.GetWorldCorners(ca); b.GetWorldCorners(cbs);
            var ra = Rect.MinMaxRect(ca[0].x, ca[0].y, ca[2].x, ca[2].y); var rb = Rect.MinMaxRect(cbs[0].x, cbs[0].y, cbs[2].x, cbs[2].y);
            return ra.Overlaps(rb);
        }

        static Sprite Sprite(string path)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter ti && (ti.textureType != TextureImporterType.Sprite || ti.spriteImportMode != SpriteImportMode.Single))
            {
                ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true; ti.mipmapEnabled = false; ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // ------------------------------------------------------------------ Play test

        static class Test
        {
            static readonly StringBuilder log = new StringBuilder();
            static List<(float wait, System.Func<bool> act)> plan;
            static int step; static double next; static double started; static float nextGame;
            static ZombieHealth target; static float targetBefore;
            static bool sawGrenade;
            static Vector3 predicted;
            static readonly Dictionary<ZombieHealth, float> before = new Dictionary<ZombieHealth, float>();

            public static void Begin()
            {
                log.Clear(); step = 0; started = EditorApplication.timeSinceStartup; next = started + 3.0; nextGame = 0f;
                GrenadeThrower.LogEvents = true;
                plan = Plan();
                EditorApplication.update -= Tick; EditorApplication.update += Tick;
            }

            static void Shot(string name)
            {
                Directory.CreateDirectory("Docs/Look");
                ScreenCapture.CaptureScreenshot($"Docs/Look/Grenade_{name}.png");
                log.AppendLine($"  picture Grenade_{name}.png");
            }

            static void Finish(string line)
            {
                log.AppendLine(line);
                EditorApplication.update -= Tick;
                File.WriteAllText(Report, log.ToString());
                SessionState.EraseBool(TestKey);
                EditorApplication.ExitPlaymode();
            }

            static void Tick()
            {
                if (!EditorApplication.isPlaying) return;
                // undo the pause the game makes when the editor window loses focus, before the game-clock wait
                // (a paused game clock never reaches nextGame)
                GameManager.Instance?.Resume();
                if (EditorApplication.timeSinceStartup - started > 420) { Finish($"FAILED: timed out at step {step}."); return; }
                if (EditorApplication.timeSinceStartup < next || Time.time < nextGame) return;
                try
                {
                    if (step >= plan.Count) { Finish("Done."); return; }
                    var (wait, act) = plan[step];
                    // waits run on the game clock (Time.time): an unfocused editor plays at a few frames a second
                    if (act()) { step++; nextGame = Time.time + wait; next = EditorApplication.timeSinceStartup; }
                    else next = EditorApplication.timeSinceStartup + 0.1;   // not ready yet: try again shortly
                }
                catch (System.Exception e) { Finish("FAILED: " + e); }
            }

            static GrenadeThrower T => GrenadeThrower.Local;
            static PlayerViewSwitcher V => PlayerViewSwitcher.Local;

            static List<(float, System.Func<bool>)> Plan() => new List<(float, System.Func<bool>)>
            {
                (2.5f, () =>
                {
                    if (V == null) return false;
                    if (!Net.NetSession.IsRunning)
                    {
                        if (!Net.NetSession.StartSolo()) { log.AppendLine("FAILED: could not start solo."); return true; }
                        Effects.IntroCutscene.Enabled = false;   // the helicopter intro would carry the player off the test spot
                        GameManager.Instance?.BeginMatch();
                        var lobby = Object.FindAnyObjectByType<Net.NetLobbyUI>(); if (lobby != null) lobby.gameObject.SetActive(false);
                    }
                    return true;
                }),
                (1.0f, () =>
                {
                    if (T == null) { log.AppendLine("FAILED: no GrenadeThrower on the player."); return true; }
                    GameManager.Instance?.Resume();
                    // out of the safe base (no grenades there) onto open ground
                    var safe = SafeBase.Instance;
                    Vector3 c = safe != null ? safe.Center : V.transform.position;
                    Vector3 spot = c + new Vector3(1f, 0f, 0.3f).normalized * ((safe != null ? safe.radius : 20f) + 10f);
                    if (Physics.Raycast(spot + Vector3.up * 30f, Vector3.down, out var hit, 60f, ~0, QueryTriggerInteraction.Ignore)) spot = hit.point;
                    var cc = V.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;
                    V.transform.position = spot + Vector3.up * 0.05f;
                    if (cc != null) cc.enabled = true;
                    log.AppendLine($"Start: {T.Count} grenades; player moved out of the base to ({spot.x:0.0}, {spot.y:0.0}, {spot.z:0.0}).");
                    return true;
                }),
                (0.3f, () =>
                {
                    Vector3 at = V.transform.position;
                    log.AppendLine($"  player now at ({at.x:0.0}, {at.y:0.0}, {at.z:0.0}); grenades allowed here: {SafeBase.FiringAllowedAt(at)}.");
                    return true;
                }),
                (1.0f, () =>
                {
                    int was = T.Count;
                    var p = GrenadePickup.Spawn(V.transform.position + V.transform.forward * 0.3f);
                    log.AppendLine($"Pickup spawned: {(p != null ? "yes" : "NO (prefab missing)")}, count was {was}.");
                    return true;
                }),
                (0.5f, () =>
                {
                    log.AppendLine($"After walking onto the pickup: {T.Count} grenades.");
                    T.Add(5);
                    log.AppendLine($"Add 5 more: {T.Count} (max {GrenadeConfig.Get().maxCarry}).");
                    return true;
                }),
                (0.6f, () =>
                {
                    // the nearest zombie within 30 m (wait for the wave if none yet)
                    Vector3 me = V.transform.position;
                    target = Object.FindObjectsByType<ZombieHealth>(FindObjectsSortMode.None)
                        .Where(z => !z.IsDead && Vector3.Distance(z.transform.position, me) < 30f && Vector3.Distance(z.transform.position, me) > 6f)
                        .OrderBy(z => Vector3.Distance(z.transform.position, me)).FirstOrDefault();
                    if (target == null) return EditorApplication.timeSinceStartup - started > 90;   // give up on zombies after 90 s
                    return true;
                }),
                (0.8f, () =>
                {
                    Vector3 me = V.transform.position;
                    Vector3 aimAt = target != null ? target.transform.position : me + V.transform.forward * 14f;
                    float lead = 1.0f;
                    if (target != null && target.TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var ag)) aimAt += ag.velocity * (lead + 0.4f);
                    before.Clear();
                    foreach (var z in Object.FindObjectsByType<ZombieHealth>(FindObjectsSortMode.None)) if (!z.IsDead) before[z] = z.CurrentHealth;
                    Vector3 aim = Aim(me + Vector3.up * 1.5f, aimAt, GrenadeConfig.Get());
                    // face the throw so the pictures show it
                    V.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(aimAt - me, Vector3.up), Vector3.up);
                    T.TestThrow(aim, 1.6f);
                    log.AppendLine($"Throw: cooking 1.6 s at {(target != null ? $"zombie '{target.name}' {Vector3.Distance(target.transform.position, me):0.0} m away" : "open ground 14 m ahead")}; count now {T.Count}; fire blocked {GameInput.FireBlocked}.");
                    return true;
                }),
                (0.22f, () => { Shot("FP_1Pin"); return true; }),
                (0.15f, () => { Shot("FP_2PinOut"); return true; }),
                (0.6f, () =>
                {
                    Shot("FP_3Ready");
                    predicted = T.PredictedLanding;
                    log.AppendLine($"  cooking: {T.Cooking}, fuse left {T.FuseLeft:0.0} s, arc lands at ({predicted.x:0.0}, {predicted.y:0.0}, {predicted.z:0.0}) " +
                                   $"{Vector3.Distance(predicted, V.transform.position):0.0} m away, danger {T.LandingDanger}");
                    return true;
                }),
                (0.73f, () => { Shot("FP_4Throw"); return true; }),
                (0.35f, () => { log.AppendLine($"  after the throw: grenades in flight {GrenadeProjectile.All.Count}, fire blocked {GameInput.FireBlocked}"); Shot("Flight"); return true; }),
                (0.1f, () =>
                {
                    // the first bounce against the arc's landing ring
                    if (GrenadeProjectile.LastFirstHit == null) return GrenadeProjectile.All.Count == 0;
                    Vector3 h = GrenadeProjectile.LastFirstHit.Value;
                    log.AppendLine($"  first touch at ({h.x:0.0}, {h.y:0.0}, {h.z:0.0}): {Vector3.Distance(h, predicted):0.00} m from the arc's landing ring.");
                    return true;
                }),
                (0.6f, () =>
                {
                    // the moment it goes off: a grenade was in the air and now none is
                    if (GrenadeProjectile.All.Count > 0) { sawGrenade = true; return false; }
                    if (!sawGrenade) return false;
                    sawGrenade = false; Shot("Blast"); return true;
                }),
                (0.6f, () =>
                {
                    int hit = 0, killed = 0; float dealt = 0f;
                    foreach (var kv in before)
                    {
                        if (kv.Key == null) { killed++; continue; }
                        float now = kv.Key.IsDead ? 0f : kv.Key.CurrentHealth;
                        if (now < kv.Value - 0.5f) { hit++; dealt += kv.Value - now; if (kv.Key.IsDead) killed++; }
                    }
                    var ph = PlayerHealth.Instance;
                    log.AppendLine($"Blast: {hit} zombies hurt ({dealt:0} damage, {killed} dead); grenades still flying {GrenadeProjectile.All.Count}; player health {ph?.CurrentHealth:0}; fire blocked {GameInput.FireBlocked}.");
                    return true;
                }),
                (1.0f, () => { V.SetView(CharacterView.ThirdPerson); return true; }),
                (0.5f, () =>
                {
                    Vector3 me = V.transform.position;
                    T.TestThrow(Aim(me + Vector3.up * 1.5f, me + V.transform.forward * 18f, GrenadeConfig.Get()), 1.2f);
                    return true;
                }),
                (0.22f, () => { Shot("TP_1Pin"); return true; }),
                (0.7f, () => { Shot("TP_2Ready"); return true; }),
                (0.36f, () => { Shot("TP_3Throw"); return true; }),
                (0.2f, () => { Shot("TP_4Follow"); return true; }),
                (0.6f, () =>
                {
                    if (GrenadeProjectile.All.Count > 0) { sawGrenade = true; return false; }
                    if (!sawGrenade) return false;
                    sawGrenade = false; Shot("Blast_TP"); log.AppendLine($"Third-person throw done; count {T.Count}."); return true;
                }),
                (0.5f, () =>
                {
                    // cooked too long: it goes off in the hand
                    PlayerHealth.Instance?.Heal(1000f);   // full health, so the drop shows the blast
                    float hp = PlayerHealth.Instance != null ? PlayerHealth.Instance.CurrentHealth : 0f;
                    T.TestThrow(V.transform.forward, 9f);
                    log.AppendLine($"Over-cook test started at health {hp:0}.");
                    return true;
                }),
                (4.5f, () =>
                {
                    var ph = PlayerHealth.Instance;
                    log.AppendLine($"  after the fuse ran out in the hand: health {ph?.CurrentHealth:0}, dead {ph?.IsDead}, busy {T.Busy}, cooking {T.Cooking}, " +
                                   $"fuse left {T.FuseLeft:0.00}, fire blocked {GameInput.FireBlocked}; time scale {Time.timeScale:0.00}, game state {GameManager.Instance?.State}, " +
                                   $"thrower enabled {T.isActiveAndEnabled}.");
                    return true;
                }),
            };

            /// Aim direction (before the thrower's own lift) that lands a throw on `target`.
            static Vector3 Aim(Vector3 from, Vector3 target, GrenadeConfig cfg)
            {
                Vector3 d = target - from; Vector3 flat = new Vector3(d.x, 0f, d.z);
                float x = flat.magnitude, y = d.y, v = cfg.throwSpeed, g = -Physics.gravity.y;
                float best = 0.3f, err = float.MaxValue;
                for (float a = -0.6f; a <= 1.2f; a += 0.01f)
                {
                    // velocity = dir * v + up * lift, dir at elevation a
                    float vx = Mathf.Cos(a) * v, vy = Mathf.Sin(a) * v + cfg.throwLift;
                    float t = x / Mathf.Max(0.1f, vx);
                    float e = Mathf.Abs(vy * t - 0.5f * g * t * t - y);
                    if (e < err) { err = e; best = a; }
                }
                return (flat.normalized * Mathf.Cos(best) + Vector3.up * Mathf.Sin(best)).normalized;
            }
        }
    }
}
