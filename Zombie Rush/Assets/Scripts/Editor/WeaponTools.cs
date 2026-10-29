using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using ZombieRush.Core;
using ZombieRush.Data;
using ZombieRush.Player;
using ZombieRush.Weapons;

namespace ZombieRush.EditorTools
{
    /// Zombie Rush > Weapons > Install Guns + Turret (open MainLevel)
    ///
    ///   1. Pistol (default weapon, slot 1): the M1911 model from Nokobot "Modern Guns - Handgun"
    ///      replaces the grey placeholder blocks.
    ///   2. Rifle (free from the start, slot 2): new weapon with the Delthor Games MCX model,
    ///      automatic fire. Shotgun moves to slot 3 and stays a pickup in the base.
    ///   3. Turret (paid item): Assets/Prefabs/Turret.prefab from "Sci-fi Turrets" Canon_lvl_3,
    ///      and a TurretDeployer on the player (T key / HUD button).
    ///
    /// The asset packs ship Built-in pipeline materials (pink in URP). URP copies are made in
    /// Assets/Weapons/Materials and used instead - the packs' own files are not changed.
    /// Gun models are turned barrel-forward, grip-down automatically from their mesh shape.
    /// Undoable with Ctrl+Z (created assets stay on disk; re-running reuses/replaces them).
    public static class WeaponTools
    {
        const string WeaponFolder = "Assets/Weapons";
        const string MaterialFolder = "Assets/Weapons/Materials";
        const string TurretPrefabPath = "Assets/Prefabs/Turret.prefab";
        const string RifleDataPath = "Assets/Data/Weapon_Rifle.asset";

        const string PistolModelPath = "Assets/Nokobot/Modern Guns - Handgun/_Prefabs/Handgun Black/M1911 Handgun_Black.prefab";
        const string MuzzleFlashPath = "Assets/Nokobot/Modern Guns - Handgun/Effects/MuzzleFlash.prefab";
        const string RifleModelPath = "Assets/DelthorGames/MCX/Prefabs/MCX.prefab";
        const string TurretModelPath = "Assets/Sci-fi turrets/Prefabs/Canon_lvl_3.prefab";

        // First-person placement under the camera (metres, camera space).
        static readonly Vector3 PistolPosition = new Vector3(0.2f, -0.19f, 0.5f);
        static readonly Vector3 RiflePosition = new Vector3(0.17f, -0.2f, 0.52f);
        const float PistolLength = 0.22f;
        const float RifleLength = 0.72f;
        const float TurretHeight = 1.15f;
        const float CameraNearClip = 0.1f; // guns sit closer than the old 0.3 m

        [MenuItem("Zombie Rush/Weapons/Install Guns + Turret (open MainLevel)", priority = 1)]
        public static void Install()
        {
            var controller = Object.FindAnyObjectByType<PlayerWeaponController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                EditorUtility.DisplayDialog("Zombie Rush", "No PlayerWeaponController in the open scene. Open Assets/Scenes/MainLevel.unity first.", "OK");
                return;
            }
            if (controller.playerCamera == null)
            {
                EditorUtility.DisplayDialog("Zombie Rush", "The PlayerWeaponController has no Player Camera assigned.", "OK");
                return;
            }

            var missing = new[] { PistolModelPath, RifleModelPath, TurretModelPath }
                .Where(p => AssetDatabase.LoadAssetAtPath<GameObject>(p) == null).ToArray();
            if (missing.Length > 0)
            {
                EditorUtility.DisplayDialog("Zombie Rush", "These asset-store files are missing (import the packs first):\n\n" + string.Join("\n", missing), "OK");
                return;
            }

            UIAssetGenerator.EnsureFolder(MaterialFolder);
            Undo.SetCurrentGroupName("Install guns + turret");
            int group = Undo.GetCurrentGroup();
            var report = new List<string>();

            var cam = controller.playerCamera;
            if (cam.nearClipPlane > CameraNearClip)
            {
                Undo.RecordObject(cam, "Camera near clip");
                cam.nearClipPlane = CameraNearClip;
                report.Add($"Camera near clip 0.3 -> {CameraNearClip} m (guns are held closer).");
            }

            var weapons = (controller.weapons ?? new WeaponBase[0]).Where(w => w != null).ToList();
            var pistol = weapons.FirstOrDefault(w => Named(w, "pistol"));
            var shotgun = weapons.FirstOrDefault(w => Named(w, "shotgun"));
            var rifle = weapons.FirstOrDefault(w => Named(w, "rifle"));

            // ---- Pistol -------------------------------------------------------------
            if (pistol != null)
            {
                Undo.RecordObject(pistol.transform, "Pistol position");
                pistol.transform.localPosition = PistolPosition;
                pistol.transform.localRotation = Quaternion.identity;
                SetGunModel(pistol, PistolModelPath, PistolLength, kick: 0.05f, pitch: 6f);
                report.Add("Pistol: M1911 model.");
            }
            else report.Add("No pistol found in the weapon list - skipped.");

            // ---- Rifle --------------------------------------------------------------
            var rifleData = EnsureRifleData();
            if (rifle == null)
            {
                var go = new GameObject("Rifle");
                Undo.RegisterCreatedObjectUndo(go, "Create rifle");
                go.transform.SetParent(cam.transform, false);
                rifle = go.AddComponent<RangedWeapon>();
                rifle.data = rifleData;
                if (pistol != null) rifle.hitMask = pistol.hitMask;
                if (pistol != null) go.layer = pistol.gameObject.layer;
            }
            Undo.RecordObject(rifle.transform, "Rifle position");
            rifle.transform.localPosition = RiflePosition;
            rifle.transform.localRotation = Quaternion.identity;
            SetGunModel(rifle, RifleModelPath, RifleLength, kick: 0.025f, pitch: 2.2f);
            report.Add("Rifle: new automatic weapon (MCX model), free from the start.");

            // ---- Shotgun: keep its model, give it the same recoil/reload feel ----------
            if (shotgun != null && shotgun.GetComponent<WeaponViewModel>() == null)
            {
                var holder = WrapChildrenInModel(shotgun.transform);
                var vm = Undo.AddComponent<WeaponViewModel>(shotgun.gameObject);
                vm.model = holder;
                vm.kickBack = 0.08f;
                vm.kickPitch = 9f;
            }

            // ---- Weapon order: pistol, rifle (both owned), then everything else (base pickups)
            var order = new List<WeaponBase>();
            if (pistol != null) order.Add(pistol);
            order.Add(rifle);
            foreach (var w in weapons) if (!order.Contains(w)) order.Add(w);
            Undo.RecordObject(controller, "Weapon order");
            controller.weapons = order.ToArray();
            controller.startingWeapons = pistol != null ? 2 : 1;
            EditorUtility.SetDirty(controller);
            report.Add("Weapon slots: " + string.Join(", ", order.Select((w, i) => $"{i + 1} {w.name}")) + $"  (first {controller.startingWeapons} owned at start).");

            // ---- Turret ---------------------------------------------------------------
            var turretPrefab = BuildTurretPrefab(out string turretNote);
            report.Add(turretNote);
            var deployer = controller.GetComponent<TurretDeployer>();
            if (deployer == null) deployer = Undo.AddComponent<TurretDeployer>(controller.gameObject);
            Undo.RecordObject(deployer, "Turret deployer");
            deployer.turretPrefab = turretPrefab;
            EditorUtility.SetDirty(deployer);

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            AssetDatabase.SaveAssets();

            string msg = string.Join("\n", report);
            Debug.Log("Zombie Rush weapons installed:\n" + msg);
            EditorUtility.DisplayDialog("Zombie Rush - weapons installed", msg +
                "\n\nNext: Zombie Rush > UI > Build Gameplay UI (adds the turret button), then save the scene (Ctrl+S) and press Play.", "OK");
        }

        [MenuItem("Zombie Rush/Store/Reset Purchases (turret locked again)", priority = 1)]
        static void ResetPurchases()
        {
            StoreService.Revoke(StoreService.Turret);
            Debug.Log("Zombie Rush: purchases reset - the turret is locked again.");
        }

        [MenuItem("Zombie Rush/Store/Unlock Turret (testing)", priority = 2)]
        static void UnlockTurret()
        {
            StoreService.Grant(StoreService.Turret);
            Debug.Log("Zombie Rush: turret unlocked for testing.");
        }

        static bool Named(WeaponBase w, string key)
        {
            string n = (w.name + " " + (w.data != null ? w.data.name + " " + w.data.weaponName : "")).ToLowerInvariant();
            return n.Contains(key);
        }

        // =====================================================================
        // Weapon data
        // =====================================================================

        static WeaponData EnsureRifleData()
        {
            var data = AssetDatabase.LoadAssetAtPath<WeaponData>(RifleDataPath);
            if (data != null) return data;
            data = ScriptableObject.CreateInstance<WeaponData>();
            data.weaponName = "Rifle";
            data.damage = 20f;
            data.fireRate = 9f;
            data.range = 120f;
            data.magazineSize = 30;
            data.maxReserveAmmo = 180;
            data.reloadTime = 2.1f;
            data.fireVolume = 0.7f;
            data.icon = UIAssetGenerator.Ensure().IconX("rifle");
            AssetDatabase.CreateAsset(data, RifleDataPath);
            return data;
        }

        // =====================================================================
        // Held gun models
        // =====================================================================

        /// Replaces whatever the weapon showed with `modelPath`, fitted barrel-forward to `length`
        /// metres, plus a muzzle point, muzzle flash, flash light and the recoil component.
        static void SetGunModel(WeaponBase weapon, string modelPath, float length, float kick, float pitch)
        {
            var root = weapon.transform;
            // Old visuals: placeholder blocks or a previous install.
            for (int i = root.childCount - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(root.GetChild(i).gameObject);

            var holder = new GameObject("Model");
            Undo.RegisterCreatedObjectUndo(holder, "Gun model");
            holder.layer = weapon.gameObject.layer;
            holder.transform.SetParent(root, false);

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source, holder.transform);
            Undo.RegisterCreatedObjectUndo(model, "Gun model");
            // Unpacked: components below are removed, which a linked prefab instance forbids.
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;

            // Pack scripts/animators/colliders are not wanted on a held gun.
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var c in model.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(c);
            foreach (var c in model.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(c);

            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                r.sharedMaterials = r.sharedMaterials.Select(ToUrp).ToArray();
                r.shadowCastingMode = ShadowCastingMode.Off; // a held gun's shadow on the ground looks wrong
                r.receiveShadows = false;
                r.gameObject.layer = weapon.gameObject.layer;
            }

            Vector3 muzzleLocal = FitGun(holder.transform, model.transform, length);

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(holder.transform, false);
            muzzle.localPosition = muzzleLocal;

            ParticleSystem flash = null;
            var flashSource = AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleFlashPath);
            if (flashSource != null)
            {
                var fx = (GameObject)PrefabUtility.InstantiatePrefab(flashSource, muzzle);
                PrefabUtility.UnpackPrefabInstance(fx, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                fx.transform.localPosition = Vector3.zero;
                fx.transform.localRotation = Quaternion.identity;
                fx.transform.localScale = Vector3.one * (length / 0.22f) * 0.6f;
                foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.loop = false;
                    main.playOnAwake = false;
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
                    var pr = ps.GetComponent<ParticleSystemRenderer>();
                    if (pr != null) pr.sharedMaterial = ParticleToUrp(pr.sharedMaterial);
                }
                flash = fx.GetComponent<ParticleSystem>();
            }

            var lightGo = new GameObject("FlashLight");
            lightGo.transform.SetParent(muzzle, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 5f;
            light.intensity = 2.5f;
            light.color = new Color(1f, 0.75f, 0.4f);
            light.shadows = LightShadows.None;
            light.enabled = false;

            var vm = weapon.GetComponent<WeaponViewModel>();
            if (vm == null) vm = Undo.AddComponent<WeaponViewModel>(weapon.gameObject);
            Undo.RecordObject(vm, "Gun view model");
            vm.model = holder.transform;
            vm.muzzle = muzzle;
            vm.muzzleFlash = flash;
            vm.flashLight = light;
            vm.kickBack = kick;
            vm.kickPitch = pitch;
            EditorUtility.SetDirty(vm);
        }

        /// Turns the model so its barrel points +Z and its grip hangs -Y, scales it to `length`
        /// and centres it on the holder. Returns the muzzle point in holder space.
        ///
        /// Shape rules (no per-model setup needed): the longest side is the barrel line; across
        /// it, the side with the long tail of vertices (grip, magazine) is down; along it, the
        /// grip/magazine sit behind the middle, so the muzzle is the other end.
        static Vector3 FitGun(Transform holder, Transform model, float length)
        {
            var pts = Vertices(model, holder);
            if (pts.Count == 0) return Vector3.forward * length * 0.5f;

            var b = BoundsOf(pts);
            Vector3 size = b.size;
            int longAxis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            int thinAxis = Enumerable.Range(0, 3).Where(a => a != longAxis).OrderBy(a => size[a]).First();
            int upAxis = 3 - longAxis - thinAxis;

            float upMedian = Median(pts.Select(p => p[upAxis]));
            float above = b.max[upAxis] - upMedian, below = upMedian - b.min[upAxis];
            float upSign = below > above ? 1f : -1f; // long tail below the median = grip side = down

            float range = b.size[upAxis];
            var low = pts.Where(p => (p[upAxis] - upMedian) * upSign < -0.3f * range).ToList();
            float lowMean = low.Count > 0 ? low.Average(p => p[longAxis]) : b.center[longAxis];
            float muzzleSign = lowMean < b.center[longAxis] ? 1f : -1f;

            Vector3 forward = Vector3.zero; forward[longAxis] = muzzleSign;
            Vector3 up = Vector3.zero; up[upAxis] = upSign;
            model.localRotation = Quaternion.Inverse(Quaternion.LookRotation(forward, up)) * model.localRotation;
            model.localScale *= length / Mathf.Max(1e-4f, size[longAxis]);

            pts = Vertices(model, holder);
            b = BoundsOf(pts);
            model.localPosition -= b.center;

            // Muzzle: middle of the front-most slice of the barrel.
            float front = b.max.z, slice = b.size.z * 0.03f;
            var tip = pts.Where(p => p.z >= front - slice).ToList();
            Vector3 m = tip.Count > 0 ? new Vector3(tip.Average(p => p.x), tip.Average(p => p.y), front) : new Vector3(b.center.x, b.center.y, front);
            Debug.Log($"Zombie Rush: fitted {model.name} - barrel along local {"XYZ"[longAxis]}{(muzzleSign > 0 ? "+" : "-")}, up {"XYZ"[upAxis]}{(upSign > 0 ? "+" : "-")}. " +
                      "If it looks backwards or upside down, rotate the 'Model' child (Y 180 / Z 180) - recoil keeps whatever you set.");
            return m - b.center;
        }

        /// Moves a weapon's existing visual children under a new "Model" holder (for recoil).
        static Transform WrapChildrenInModel(Transform root)
        {
            var holder = new GameObject("Model").transform;
            Undo.RegisterCreatedObjectUndo(holder.gameObject, "Wrap model");
            holder.gameObject.layer = root.gameObject.layer;
            holder.SetParent(root, false);
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i);
                if (child == holder) continue;
                Undo.SetTransformParent(child, holder, "Wrap model");
            }
            return holder;
        }

        static List<Vector3> Vertices(Transform root, Transform space)
        {
            var list = new List<Vector3>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var toSpace = space.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var verts = mf.sharedMesh.vertices;
                int step = Mathf.Max(1, verts.Length / 4000); // plenty for shape analysis
                for (int i = 0; i < verts.Length; i += step) list.Add(toSpace.MultiplyPoint3x4(verts[i]));
            }
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                var toSpace = space.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                foreach (var v in smr.sharedMesh.vertices) list.Add(toSpace.MultiplyPoint3x4(v));
            }
            return list;
        }

        static Bounds BoundsOf(List<Vector3> pts)
        {
            var b = new Bounds(pts[0], Vector3.zero);
            foreach (var p in pts) b.Encapsulate(p);
            return b;
        }

        static float Median(IEnumerable<float> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            return sorted.Count == 0 ? 0f : sorted[sorted.Count / 2];
        }

        // =====================================================================
        // Turret prefab
        // =====================================================================

        static Turret BuildTurretPrefab(out string note)
        {
            var root = new GameObject("Turret");
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(TurretModelPath);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;

                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                    r.sharedMaterials = r.sharedMaterials.Select(ToUrp).ToArray();
                foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

                // Height to TurretHeight, standing on y = 0.
                var pts = Vertices(model.transform, root.transform);
                var b = BoundsOf(pts);
                float scale = TurretHeight / Mathf.Max(0.01f, b.size.y);
                model.transform.localScale *= scale;
                pts = Vertices(model.transform, root.transform);
                b = BoundsOf(pts);
                model.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
                pts = Vertices(model.transform, root.transform);
                b = BoundsOf(pts);

                var all = model.GetComponentsInChildren<Transform>(true);
                var muzzles = all.Where(t => t.name.ToLowerInvariant().StartsWith("gunpoint")).ToArray();
                var head = all.FirstOrDefault(t => t != model.transform && t.name.ToLowerInvariant().StartsWith("canon"));
                var animator = model.GetComponentInChildren<Animator>(true);
                if (animator != null) animator.applyRootMotion = false;

                var turret = root.AddComponent<Turret>();
                turret.head = head != null ? head : model.transform;
                turret.animator = animator;
                turret.tracerMaterial = TracerMaterial();
                turret.barrelAxis = BarrelAxis(turret.head, muzzles, out var tips);
                turret.muzzles = tips;

                var col = root.AddComponent<CapsuleCollider>();
                col.center = new Vector3(0f, b.size.y * 0.5f, 0f);
                col.height = b.size.y;
                col.radius = Mathf.Clamp(Mathf.Min(b.size.x, b.size.z) * 0.35f, 0.25f, 0.6f);

                UIAssetGenerator.EnsureFolder("Assets/Prefabs");
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, TurretPrefabPath);
                note = $"Turret: prefab saved to {TurretPrefabPath} (head '{turret.head.name}', {muzzles.Length} gun points). Paid item - T key / HUD button.";
                return prefab.GetComponent<Turret>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [MenuItem("Zombie Rush/Weapons/Rebuild Turret (fix aim)", priority = 2)]
        static void RebuildTurret()
        {
            UIAssetGenerator.EnsureFolder(MaterialFolder);
            var prefab = BuildTurretPrefab(out string note);
            var deployer = Object.FindAnyObjectByType<TurretDeployer>(FindObjectsInactive.Include);
            if (deployer != null && deployer.turretPrefab != prefab)
            {
                Undo.RecordObject(deployer, "Turret prefab");
                deployer.turretPrefab = prefab;
                EditorUtility.SetDirty(deployer);
                EditorSceneManager.MarkSceneDirty(deployer.gameObject.scene);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Zombie Rush: " + note);
            EditorUtility.DisplayDialog("Turret rebuilt", note + "\n\nThe turret now aims with its gun barrels and fires from their tips. Press Play to check.", "OK");
        }

        /// Barrel direction in the head's local space, taken from the gun barrels themselves
        /// (the "gunpoint" meshes): a barrel sticks OUT of the turret, so its end farthest from
        /// the head's pivot is the muzzle. (Using the whole head picked its big back plate.)
        /// Also returns a "Tip" point at each barrel's muzzle end, where shots start.
        static Vector3 BarrelAxis(Transform head, Transform[] barrels, out Transform[] tips)
        {
            Vector3 pivot = head.position;
            Vector3 dir = Vector3.zero;
            var tipList = new List<Transform>();
            foreach (var barrel in barrels)
            {
                var mf = barrel.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) { tipList.Add(barrel); continue; }

                var m = barrel.localToWorldMatrix;
                var pts = mf.sharedMesh.vertices.Select(v => m.MultiplyPoint3x4(v)).ToList();
                Vector3 centre = pts.Aggregate(Vector3.zero, (a, p) => a + p) / pts.Count;
                float Flat(Vector3 p) => Vector3.ProjectOnPlane(p - pivot, Vector3.up).sqrMagnitude;
                var far = pts.OrderByDescending(Flat).Take(Mathf.Max(4, pts.Count / 20)).ToList();
                Vector3 muzzle = far.Aggregate(Vector3.zero, (a, p) => a + p) / far.Count;
                dir += Vector3.ProjectOnPlane(muzzle - centre, Vector3.up);

                var tip = new GameObject("Tip").transform;
                tip.SetParent(barrel, false);
                tip.position = muzzle;
                tipList.Add(tip);
            }
            tips = tipList.ToArray();

            if (dir.sqrMagnitude < 1e-6f)
            {
                // No barrel meshes: the side the model's bulk leans to is the front.
                dir = Vector3.ProjectOnPlane(head.root.forward, Vector3.up);
            }
            Vector3 local = head.InverseTransformDirection(dir.normalized);
            Debug.Log($"Zombie Rush: turret barrel axis (head local) = {local}. Select the Turret prefab to see it as a red line.");
            return local;
        }

        // =====================================================================
        // URP materials
        // =====================================================================

        static readonly Dictionary<Material, Material> converted = new Dictionary<Material, Material>();

        /// URP/Lit copy of a Built-in Standard material (cached in Assets/Weapons/Materials).
        static Material ToUrp(Material src)
        {
            if (src == null) return null;
            if (src.shader != null && src.shader.name.StartsWith("Universal Render Pipeline")) return src;
            if (converted.TryGetValue(src, out var cached) && cached != null) return cached;

            string path = $"{MaterialFolder}/{src.name}_URP.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = src.name + "_URP" };
                CopyStandardToLit(src, mat);
                AssetDatabase.CreateAsset(mat, path);
            }
            converted[src] = mat;
            return mat;
        }

        static void CopyStandardToLit(Material src, Material dst)
        {
            Texture Tex(string p) => src.HasProperty(p) ? src.GetTexture(p) : null;
            float F(string p, float d) => src.HasProperty(p) ? src.GetFloat(p) : d;

            var albedo = Tex("_MainTex");
            dst.SetTexture("_BaseMap", albedo);
            dst.SetColor("_BaseColor", src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white);
            if (src.HasProperty("_MainTex"))
            {
                dst.SetTextureScale("_BaseMap", src.GetTextureScale("_MainTex"));
                dst.SetTextureOffset("_BaseMap", src.GetTextureOffset("_MainTex"));
            }

            var normal = Tex("_BumpMap");
            if (normal != null)
            {
                dst.SetTexture("_BumpMap", normal);
                dst.SetFloat("_BumpScale", F("_BumpScale", 1f));
                dst.EnableKeyword("_NORMALMAP");
            }

            var occlusion = Tex("_OcclusionMap");
            if (occlusion != null)
            {
                dst.SetTexture("_OcclusionMap", occlusion);
                dst.SetFloat("_OcclusionStrength", F("_OcclusionStrength", 1f));
                dst.EnableKeyword("_OCCLUSIONMAP");
            }

            bool specular = src.shader != null && src.shader.name.Contains("Specular");
            if (specular)
            {
                dst.SetFloat("_WorkflowMode", 0f);
                dst.EnableKeyword("_SPECULAR_SETUP");
                if (src.HasProperty("_SpecColor")) dst.SetColor("_SpecColor", src.GetColor("_SpecColor"));
                var spec = Tex("_SpecGlossMap");
                if (spec != null)
                {
                    dst.SetTexture("_SpecGlossMap", spec);
                    dst.EnableKeyword("_METALLICSPECGLOSSMAP");
                    dst.SetFloat("_Smoothness", F("_GlossMapScale", 1f));
                }
                else dst.SetFloat("_Smoothness", F("_Glossiness", 0.5f));
            }
            else
            {
                dst.SetFloat("_WorkflowMode", 1f);
                var metal = Tex("_MetallicGlossMap");
                // "Standard (Roughness setup)" keeps roughness, not smoothness: use the pack's
                // own MetallicSmoothness texture when there is one next to the albedo.
                if (src.shader != null && src.shader.name.Contains("Roughness")) metal = FindSibling(albedo, "MetallicSmoothness") ?? metal;
                if (metal != null)
                {
                    dst.SetTexture("_MetallicGlossMap", metal);
                    dst.EnableKeyword("_METALLICSPECGLOSSMAP");
                    dst.SetFloat("_Smoothness", F("_GlossMapScale", 1f));
                }
                else
                {
                    dst.SetFloat("_Metallic", F("_Metallic", 0f));
                    dst.SetFloat("_Smoothness", F("_Glossiness", 0.5f));
                }
            }

            if (src.IsKeywordEnabled("_EMISSION"))
            {
                dst.SetColor("_EmissionColor", src.GetColor("_EmissionColor"));
                dst.SetTexture("_EmissionMap", Tex("_EmissionMap"));
                dst.EnableKeyword("_EMISSION");
                dst.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
        }

        /// A texture in the same folder whose name has `suffix` and the albedo's first name token.
        static Texture FindSibling(Texture albedo, string suffix)
        {
            if (albedo == null) return null;
            string path = AssetDatabase.GetAssetPath(albedo);
            string folder = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string prefix = albedo.name.Split('_')[0];
            foreach (var guid in AssetDatabase.FindAssets("t:Texture", new[] { folder }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                string n = System.IO.Path.GetFileNameWithoutExtension(p);
                if (n.Contains(suffix) && n.StartsWith(prefix)) return AssetDatabase.LoadAssetAtPath<Texture>(p);
            }
            return null;
        }

        /// Additive URP particle copy of the pack's muzzle flash material.
        static Material ParticleToUrp(Material src)
        {
            if (src == null) return null;
            if (src.shader != null && src.shader.name.StartsWith("Universal Render Pipeline")) return src;
            string path = $"{MaterialFolder}/{src.name}_URP.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            mat = AdditiveParticle(src.name + "_URP", src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null, Color.white);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static Material TracerMaterial()
        {
            string path = $"{MaterialFolder}/Turret_Tracer.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = AdditiveParticle("Turret_Tracer", null, new Color(1f, 0.8f, 0.45f));
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static Material AdditiveParticle(string name, Texture tex, Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = name };
            if (tex != null) mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Surface", 1f); // transparent
            mat.SetFloat("_Blend", 2f);   // additive
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.One);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            return mat;
        }
    }
}
