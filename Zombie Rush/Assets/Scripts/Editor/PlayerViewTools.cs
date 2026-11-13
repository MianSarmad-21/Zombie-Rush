using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZombieRush.Data;
using ZombieRush.Player;
using ZombieRush.Weapons;

namespace ZombieRush.EditorTools
{
    /// Zombie Rush > Player > Set Up First/Third Person (open scene):
    /// puts the Survivor body on the Player, adds PlayerViewSwitcher (camera switch, V key) and
    /// PlayerBodyLink (gun in hand, body animations), makes Assets/Data/PlayerViewConfig, and adds
    /// the view buttons to the existing HUD. Safe to run again: it replaces its own pieces.
    static class PlayerViewTools
    {
        const string SurvivorPrefab = "Assets/Prefabs/Characters/Survivor.prefab";
        const string ConfigPath = "Assets/Data/PlayerViewConfig.asset";
        const string HoldConfigPath = "Assets/Data/WeaponHoldConfig.asset";
        const string BodyName = "Body";

        [MenuItem("Zombie Rush/Player/Set Up First-Third Person (open scene)")]
        static void Menu() => Debug.Log("Zombie Rush: " + Run(null));

        /// Returns the report.
        public static string Run() => Run(null);

        /// `bodyPrefab` null = the Meshy body when it exists, otherwise the code-built Survivor.
        public static string Run(string bodyPrefab)
        {
            if (string.IsNullOrEmpty(bodyPrefab))
                bodyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MeshyBodyTools.PrefabPath) != null ? MeshyBodyTools.PrefabPath : SurvivorPrefab;
            var player = Object.FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
            if (player == null) return "FAILED: no PlayerController in the open scene.";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(bodyPrefab);
            if (prefab == null) return "FAILED: " + bodyPrefab + " not found.";
            var weapons = player.GetComponent<PlayerWeaponController>();
            var cam = weapons != null && weapons.playerCamera != null ? weapons.playerCamera : player.GetComponentInChildren<Camera>(true);
            if (cam == null || player.cameraPivot == null) return "FAILED: Player has no camera or camera pivot.";

            // Body: Survivor prefab instance at the Player's feet, facing the Player's forward.
            var old = player.transform.Find(BodyName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            var body = (GameObject)PrefabUtility.InstantiatePrefab(prefab, player.transform);
            Undo.RegisterCreatedObjectUndo(body, "Add Player Body");
            body.name = BodyName;
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = Vector3.one;
            foreach (var t in body.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = player.gameObject.layer;
            foreach (var c in body.GetComponentsInChildren<Collider>(true)) Undo.DestroyObjectImmediate(c); // the CharacterController is the hitbox

            var viewMode = body.GetComponent<CharacterViewMode>();
            if (viewMode == null) viewMode = Undo.AddComponent<CharacterViewMode>(body);
            var bodyAnimator = body.GetComponentInChildren<CharacterBodyAnimator>(true);

            var config = LoadOrCreate<PlayerViewConfig>(ConfigPath);
            var holdConfig = LoadOrCreate<WeaponHoldConfig>(HoldConfigPath);

            // Aim rig on the model (next to its Animator), with the gun hold under it.
            WeaponAimRig rig = null;
            if (bodyAnimator != null)
            {
                rig = bodyAnimator.GetComponent<WeaponAimRig>();
                if (rig == null) rig = Undo.AddComponent<WeaponAimRig>(bodyAnimator.gameObject);
                var hold = bodyAnimator.transform.Find("WeaponHold");
                if (hold == null)
                {
                    hold = new GameObject("WeaponHold").transform;
                    Undo.RegisterCreatedObjectUndo(hold.gameObject, "Weapon Hold");
                    hold.SetParent(bodyAnimator.transform, false);
                    hold.gameObject.layer = player.gameObject.layer;
                }
                Undo.RecordObject(rig, "Set Up Player View");
                rig.config = holdConfig;
                rig.aimCamera = cam;
                rig.ignoreRoot = player.transform;
                rig.hold = hold;
                Undo.RecordObject(bodyAnimator, "Set Up Player View");
                bodyAnimator.leftHandTarget = null;
            }

            var switcher = player.GetComponent<PlayerViewSwitcher>();
            if (switcher == null) switcher = Undo.AddComponent<PlayerViewSwitcher>(player.gameObject);
            Undo.RecordObject(switcher, "Set Up Player View");
            switcher.config = config;
            switcher.cameraPivot = player.cameraPivot;
            switcher.playerCamera = cam;
            switcher.body = viewMode;
            switcher.firstPersonModels = weapons != null
                ? weapons.weapons.Where(w => w != null).Select(w => w.transform).ToArray()
                : new Transform[0];

            var link = player.GetComponent<PlayerBodyLink>();
            if (link == null) link = Undo.AddComponent<PlayerBodyLink>(player.gameObject);
            Undo.RecordObject(link, "Set Up Player View");
            link.weapons = weapons;
            link.bodyAnimator = bodyAnimator;
            link.aimRig = rig;
            link.viewMode = viewMode;

            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
            string ui = ZombieRushUIBuilder.AddViewButtons();
            EditorSceneManager.SaveScene(player.gameObject.scene);

            var b = new Bounds(body.transform.position, Vector3.zero);
            foreach (var r in body.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            return $"Body: {prefab.name} under Player, {b.size.y:0.00} m tall, animator {(bodyAnimator != null ? "found" : "MISSING")}, " +
                   $"aim rig {(rig != null ? "added" : "MISSING")}, humanoid {(bodyAnimator != null && bodyAnimator.GetComponent<Animator>().isHuman ? "yes" : "NO")}.\n" +
                   $"Switcher: camera '{cam.name}', {switcher.firstPersonModels.Length} first-person guns, config {ConfigPath}.\n" +
                   ui + "\nScene saved.";
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            return asset;
        }
    }
}
