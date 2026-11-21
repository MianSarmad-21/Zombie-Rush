using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using ZombieRush.World;

namespace ZombieRush.EditorTools
{
    /// Loot chests + the zone fog (2026-11-16).
    ///   - prefabs (Assets/Prefabs/Loot): LootChest (Blender/build_chest.py: body, hinged lid, padlock; gold motes and
    ///     a small beacon light while closed), ChestMedkit (health), ChestArmor (the plate-carrier vest: armour);
    ///   - a ChestSpawner on the ZoneManager (3 chests a wave, +1 per extra squad member, max 6 closed);
    ///   - (no fog curtain: the city outside fades into the night fog, see CityEdgeTools)
    /// Marker: Temp/ZR_RunMapCleanup with "chests" (report in Temp/ZR_MapCleanup.log; saves the scene).
    public static class LootChestTools
    {
        const string ModelDir = "Assets/Models/2026-11-16_Chest";
        const string PrefabDir = "Assets/Prefabs/Loot";

        public static string Build()
        {
            var sb = new StringBuilder();
            if (!File.Exists($"{ModelDir}/ZR_Chest.fbx")) return "FAILED: run Blender/build_chest.py first.";
            var zone = Object.FindAnyObjectByType<ZoneManager>(FindObjectsInactive.Include);
            if (zone == null) return "FAILED: open MainLevel (no ZoneManager).";
            Directory.CreateDirectory(PrefabDir);

            var fx = FxMaterial();
            var medkit = RewardPrefab("ChestMedkit", Model("ZR_Medkit"), ChestReward.Kind.Health, 1.3f, sb);
            var vestModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/2026-11-15_BaseProps/ZR_ArmorVest.fbx");
            var armor = RewardPrefab("ChestArmor", vestModel, ChestReward.Kind.Armor, 0.75f, sb);
            var chest = ChestPrefab(medkit, armor, fx, sb);
            if (chest == null || medkit == null || armor == null) return "FAILED building prefabs:\n" + sb;

            // spawner
            var sp = zone.GetComponent<ChestSpawner>();
            if (sp == null) sp = Undo.AddComponent<ChestSpawner>(zone.gameObject);
            Undo.RecordObject(sp, "Loot chests");
            sp.chestPrefab = chest;
            EditorUtility.SetDirty(sp);
            sb.AppendLine($"ChestSpawner on '{zone.name}': {sp.perWave} chests each wave (+1 per extra squad member), max {sp.maxClosed} closed.");

            // no fog curtain on the zone wall: the city outside fades into the night fog (CityEdgeTools "apply")

            // price check: the first expansions, solo and a squad of four
            var wm = Object.FindAnyObjectByType<ZombieRush.Waves.WaveManager>(FindObjectsInactive.Include);
            int baseN = wm != null ? wm.baseZombiesPerWave : 5, extra = wm != null ? wm.extraZombiesPerWave : 2;
            foreach (int players in new[] { 1, 4 })
            {
                var line = new StringBuilder($"  zone price, {players} player{(players > 1 ? "s" : "")}:");
                float mult = ZombieRush.Core.Difficulty.ZombieCountMultiplier(players);
                for (int k = 0; k < 3; k++)
                {
                    float z = 0f; int first = k * zone.wavesPerExpansion + 1;
                    for (int w = first; w < first + zone.wavesPerExpansion; w++) z += Mathf.Ceil((baseN + (w - 1) * extra) * mult);
                    int cost = Mathf.Max(10, Mathf.RoundToInt(z * zone.coinsPerKill * zone.costShare / 10f) * 10);
                    line.Append($" #{k + 1} {cost} (waves {first}-{first + zone.wavesPerExpansion - 1}: {z:0} zombies ~{z * zone.coinsPerKill:0} coins)");
                }
                sb.AppendLine(line.ToString());
            }

            EditorSceneManager.MarkSceneDirty(zone.gameObject.scene);
            EditorSceneManager.SaveScene(zone.gameObject.scene);
            AssetDatabase.SaveAssets();
            sb.AppendLine("Scene saved.");
            return sb.ToString();
        }

        static GameObject Model(string name)
        {
            string path = $"{ModelDir}/{name}.fbx";
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) return null;
            foreach (var src in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Select(x => x.name).Distinct().ToList())
            {
                Material m = src == "ZR_Chest" ? Lit("ZR_Chest", Color.white, 0.15f, 0.3f, AssetDatabase.LoadAssetAtPath<Texture2D>($"{ModelDir}/ZR_Chest.png"))
                           : src == "ZR_ChestInside" ? Lit("ZR_ChestInside", new Color(0.05f, 0.04f, 0.035f), 0f, 0.1f)
                           : src == "ZR_MedWhite" ? Lit("ZR_MedWhite", new Color(0.82f, 0.8f, 0.76f), 0f, 0.5f)
                           : src == "ZR_MedRed" ? Lit("ZR_MedRed", new Color(0.7f, 0.05f, 0.04f), 0f, 0.5f)
                           : CityPropsTools.HasMaterial(src) ? CityPropsTools.EnsureMaterial(src) : null;
                if (m != null) imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), src), m);
            }
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        static Material Lit(string name, Color color, float metal, float smooth, Texture2D tex = null)
        {
            Directory.CreateDirectory($"{ModelDir}/Materials");
            string path = $"{ModelDir}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", color); m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material FxMaterial() => CityPropsTools.FireParticleMaterial();   // additive soft dot

        /// Root at the origin, Blender's +Y turned to the prefab's +Z, model under it.
        static GameObject Wrap(string name, GameObject asset, out GameObject model)
        {
            var go = new GameObject(name);
            model = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            model.name = "Model";
            model.transform.SetParent(go.transform, false);
            float yaw = Vector3.SignedAngle(CityPropsTools.BlenderForward(), Vector3.forward, Vector3.up);
            model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * model.transform.localRotation;
            return go;
        }

        static GameObject RewardPrefab(string name, GameObject asset, ChestReward.Kind kind, float scale, StringBuilder sb)
        {
            if (asset == null) { sb.AppendLine($"  {name}: no model"); return null; }
            var go = Wrap(name, asset, out var model);
            try
            {
                var holder = new GameObject("Display").transform;
                holder.SetParent(go.transform, false);
                model.transform.SetParent(holder, false);
                model.transform.localScale *= scale;
                foreach (var r in model.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var col = go.AddComponent<SphereCollider>();
                col.isTrigger = true; col.radius = 0.8f; col.center = new Vector3(0f, 0.3f, 0f);
                var rw = go.AddComponent<ChestReward>();
                rw.kind = kind; rw.amount = 50; rw.display = holder;
                // a soft coloured glow under it, so it reads at night
                var glow = Glow(holder, kind == ChestReward.Kind.Health ? new Color(0.4f, 1f, 0.45f, 0.35f) : new Color(0.4f, 0.7f, 1f, 0.35f));
                glow.transform.localPosition = new Vector3(0f, 0.15f, 0f);
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{name}.prefab");
                sb.AppendLine($"  {name}: {kind}, model x{scale}");
                return prefab;
            }
            finally { Object.DestroyImmediate(go); }
        }

        static ParticleSystem Glow(Transform parent, Color color)
        {
            var g = new GameObject("Glow");
            g.transform.SetParent(parent, false);
            var ps = g.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 0.8f; main.startSpeed = 0f; main.startSize = new ParticleSystem.MinMaxCurve(0.7f, 0.9f);
            main.startColor = color; main.maxParticles = 4;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var em = ps.emission; em.rateOverTime = 4f;
            var sh = ps.shape; sh.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            var gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = gr;
            var r = g.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = FxMaterial(); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return ps;
        }

        static GameObject ChestPrefab(GameObject medkit, GameObject armor, Material fx, StringBuilder sb)
        {
            var asset = Model("ZR_Chest");
            if (asset == null) { sb.AppendLine("  LootChest: no model"); return null; }
            var go = Wrap("LootChest", asset, out var model);
            try
            {
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                Transform Part(string n) => model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith(n));
                var lid = Part("ChestLid"); var lck = Part("ChestLock"); var body = Part("ChestBody");
                if (lid == null || lck == null || body == null) { sb.AppendLine("  LootChest: parts missing (ChestBody/ChestLid/ChestLock)"); return null; }
                // the lid's hinge axis: its local X (Blender X) - check which way "open" goes: the front (+Z) must rise
                var bb = new Bounds(body.GetComponent<Renderer>().bounds.center, Vector3.zero);
                foreach (var r in model.GetComponentsInChildren<Renderer>()) bb.Encapsulate(r.bounds);
                var box = go.AddComponent<BoxCollider>();
                box.center = go.transform.InverseTransformPoint(bb.center); box.size = bb.size * 0.95f;
                var obst = go.AddComponent<NavMeshObstacle>(); obst.carving = false;
                obst.shape = NavMeshObstacleShape.Box; obst.center = box.center; obst.size = box.size;

                // closed-chest beacon: gold motes rising + a small warm light (bright from inside once opened)
                var motes = new GameObject("Sparkle");
                motes.transform.SetParent(go.transform, false);
                motes.transform.localPosition = new Vector3(0f, bb.max.y - go.transform.position.y + 0.05f, 0f);
                motes.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                var ps = motes.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
                main.startColor = new Color(1f, 0.8f, 0.35f, 0.9f);
                main.maxParticles = 40;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var em = ps.emission; em.rateOverTime = 12f;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(bb.size.x * 0.8f, bb.size.z * 0.6f, 0.02f);
                var col = ps.colorOverLifetime; col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
                var noise = ps.noise; noise.enabled = true; noise.strength = 0.15f; noise.frequency = 1.2f;
                var pr = motes.GetComponent<ParticleSystemRenderer>();
                pr.sharedMaterial = fx; pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; pr.receiveShadows = false;

                var lg = new GameObject("Glow");
                lg.transform.SetParent(go.transform, false);
                lg.transform.localPosition = new Vector3(0f, bb.max.y - go.transform.position.y + 0.9f, 0.35f);   // high and in front: lights the ground round it, never washes the lid out
                var light = lg.AddComponent<Light>();
                light.type = LightType.Point; light.color = new Color(1f, 0.72f, 0.35f);
                light.intensity = 0.7f; light.range = 4.5f; light.shadows = LightShadows.None;

                var lc = go.AddComponent<LootChest>();
                lc.lid = lid; lc.padlock = lck; lc.sparkle = ps; lc.glow = light;
                lc.medkitPrefab = medkit; lc.armorPrefab = armor; lc.fxMaterial = fx;

                // which way round the lid opens: try +angle, the front edge (towards +Z) must go UP
                var probe = lid.TransformPoint(new Vector3(0f, 0f, 0f));
                var front = lid.position + go.transform.forward * 0.5f;
                var localFront = lid.InverseTransformPoint(front);
                var rotated = Quaternion.Euler(30f, 0f, 0f) * localFront;
                float rise = lid.TransformPoint(rotated).y - front.y;
                lc.lidOpenAngle = rise > 0f ? 108f : -108f;

                var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/LootChest.prefab");
                sb.AppendLine($"  LootChest: {bb.size.x:0.00} x {bb.size.y:0.00} x {bb.size.z:0.00} m, lid opens {lc.lidOpenAngle:0} deg about its local X");
                LootChest.FxMat = fx;
                return prefab;
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
