using UnityEditor;
using UnityEngine;
using ZombieRush.Player;
using ZombieRush.Zombie;

namespace ZombieRush.EditorTools
{
    /// Zombie Rush > Test > ... (Play mode only)
    ///
    /// Look at a zombie up close: spawns it 4 m in front of the player, facing you, with its AI
    /// off (it stands still and never attacks). Its Animator runs on unscaled time, so it keeps
    /// moving while the game is paused - pause (Esc), then orbit around it in the Scene view.
    /// The menu items below play each animation on the last spawned test zombie.
    /// Shooting it still works (hit / death). Test zombies disappear when Play mode stops.
    public static class ZombiePreviewTools
    {
        const string MinerPrefab = "Assets/Prefabs/Zombie_Miner.prefab";
        const string YakuPrefab = "Assets/Prefabs/Zombie_Yaku.prefab";

        static ZombieHealth current;

        [MenuItem("Zombie Rush/Test/Spawn Miner In Front Of Me", priority = 1)]
        static void SpawnMiner() => Spawn(MinerPrefab);

        [MenuItem("Zombie Rush/Test/Spawn Yaku In Front Of Me", priority = 2)]
        static void SpawnYaku() => Spawn(YakuPrefab);

        [MenuItem("Zombie Rush/Test/Spawn Miner In Front Of Me", true)]
        [MenuItem("Zombie Rush/Test/Spawn Yaku In Front Of Me", true)]
        static bool CanSpawn() => Application.isPlaying;

        [MenuItem("Zombie Rush/Test/Animation: Idle", priority = 20)] static void Idle() => Speed(0f);
        [MenuItem("Zombie Rush/Test/Animation: Walk", priority = 21)] static void Walk() => Speed(0.5f);
        [MenuItem("Zombie Rush/Test/Animation: Run", priority = 22)] static void Run() => Speed(1f);
        [MenuItem("Zombie Rush/Test/Animation: Attack", priority = 23)] static void Attack() { if (Ready()) current.Anim.TriggerAttack(); }
        [MenuItem("Zombie Rush/Test/Animation: Hit", priority = 24)] static void Hit() { if (Ready()) current.Anim.TriggerHit(); }
        [MenuItem("Zombie Rush/Test/Animation: Die", priority = 25)] static void Die() { if (Ready()) current.Anim.TriggerDie(); }

        [MenuItem("Zombie Rush/Test/Animation: Idle", true)]
        [MenuItem("Zombie Rush/Test/Animation: Walk", true)]
        [MenuItem("Zombie Rush/Test/Animation: Run", true)]
        [MenuItem("Zombie Rush/Test/Animation: Attack", true)]
        [MenuItem("Zombie Rush/Test/Animation: Hit", true)]
        [MenuItem("Zombie Rush/Test/Animation: Die", true)]
        static bool HasZombie() => Application.isPlaying && current != null;

        [MenuItem("Zombie Rush/Test/Remove Test Zombie", priority = 40)]
        static void Remove()
        {
            if (current != null) Object.Destroy(current.gameObject);
            current = null;
        }

        [MenuItem("Zombie Rush/Test/Remove Test Zombie", true)]
        static bool CanRemove() => HasZombie();

        static void Spawn(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var player = PlayerHealth.Instance != null ? PlayerHealth.Instance.transform : null;
            if (prefab == null) { EditorUtility.DisplayDialog("Test zombie", $"Not found: {path}\nRun the zombie setup tool first.", "OK"); return; }
            if (player == null) { EditorUtility.DisplayDialog("Test zombie", "No player in the scene - press Play in MainLevel first.", "OK"); return; }

            Vector3 fwd = Vector3.ProjectOnPlane(player.forward, Vector3.up).normalized;
            Vector3 pos = player.position + fwd * 4f;
            if (Physics.Raycast(pos + Vector3.up * 3f, Vector3.down, out var hit, 10f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point;

            if (current != null) Object.Destroy(current.gameObject);
            var go = Object.Instantiate(prefab, pos, Quaternion.LookRotation(-fwd, Vector3.up));
            go.name = prefab.name + " (TEST)";

            // Stand still, never attack; keep animating while the game is paused.
            var ai = go.GetComponent<ZombieAI>();
            if (ai != null) ai.enabled = false;
            var rise = go.GetComponent<ZombieSpawnRise>();
            if (rise != null) rise.enabled = false;
            foreach (var animator in go.GetComponentsInChildren<Animator>(true))
                animator.updateMode = AnimatorUpdateMode.UnscaledTime;

            current = go.GetComponent<ZombieHealth>();
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
            Debug.Log($"[Zombie Rush] Test zombie '{go.name}' spawned. Zombie Rush > Test > Animation: ... to play its animations; it keeps animating while paused.");
        }

        static void Speed(float value)
        {
            if (Ready()) current.Anim.SetSpeed(value);
        }

        static bool Ready()
        {
            if (current == null) return false;
            current.Anim.Resolve();
            return true;
        }
    }
}
