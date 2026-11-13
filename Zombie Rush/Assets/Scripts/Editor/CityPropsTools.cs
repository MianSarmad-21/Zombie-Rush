using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using ZombieRush.Data;
using ZombieRush.World;

namespace ZombieRush.EditorTools
{
    /// City props made in Blender (Blender/build_city_props.py): wrecked cars, a bus, barricades,
    /// sandbags, street lamps, traffic lights, barrels, dumpsters.
    ///   Zombie Rush > Level > Build City Prop Prefabs: URP materials (grunge textures from Blender),
    ///     prefabs with colliders, lamp and fire lights, fire particles. Assets/Prefabs/CityProps/.
    ///   Zombie Rush > Level > Place City Props: spreads them over the play area by CityPropsConfig
    ///     (Assets/Data/CityPropsConfig.asset) under Map/CityProps, re-bakes zombie paths, saves.
    ///     Running it again replaces the old layout.
    /// Marker: Temp/ZR_RunMapCleanup with "props build" / "props place" (see MapCleanupTools).
    static class CityPropsTools
    {
        const string ModelDir = "Assets/Models/2026-11-09_CityProps";
        static readonly string[] ModelDirs = { ModelDir, "Assets/Models/2026-11-09_SurvivalProps", "Assets/Models/2026-11-09_OvergrowthProps" };
        const string TexDir = ModelDir + "/Textures";
        const string MatDir = ModelDir + "/Materials";
        const string PrefabDir = "Assets/Prefabs/CityProps";
        const string ConfigPath = "Assets/Data/CityPropsConfig.asset";
        const string RootName = "CityProps";

        // ---------------------------------------------------------------- materials

        struct MatSpec
        {
            public Color color; public float metal, smooth; public string tex; public Color emit; public bool cutout, twoSided;
            public MatSpec(Color c, float m, float s, string t = "ZR_Grunge", Color e = default, bool cut = false, bool both = false) { color = c; metal = m; smooth = s; tex = t; emit = e; cutout = cut; twoSided = both || cut; }
        }

        static readonly Dictionary<string, MatSpec> Mats = new Dictionary<string, MatSpec>
        {
            { "ZR_BurntPaint", new MatSpec(new Color(0.16f, 0.14f, 0.13f), 0.3f, 0.2f) },
            { "ZR_FadedPaint_Blue", new MatSpec(new Color(0.22f, 0.30f, 0.38f), 0.3f, 0.3f) },
            { "ZR_FadedPaint_Red", new MatSpec(new Color(0.45f, 0.13f, 0.09f), 0.3f, 0.3f) },
            { "ZR_FadedPaint_Cream", new MatSpec(new Color(0.62f, 0.58f, 0.47f), 0.2f, 0.25f) },
            { "ZR_BusYellow", new MatSpec(new Color(0.66f, 0.47f, 0.10f), 0.2f, 0.3f) },
            { "ZR_PoliceWhite", new MatSpec(new Color(0.74f, 0.74f, 0.72f), 0.3f, 0.35f) },
            { "ZR_PoliceBlue", new MatSpec(new Color(0.06f, 0.09f, 0.32f), 0.3f, 0.35f) },
            { "ZR_Rust", new MatSpec(new Color(0.40f, 0.19f, 0.09f), 0.5f, 0.1f) },
            { "ZR_DarkMetal", new MatSpec(new Color(0.11f, 0.12f, 0.13f), 0.7f, 0.35f) },
            { "ZR_Tire", new MatSpec(new Color(0.045f, 0.045f, 0.045f), 0f, 0.08f) },
            { "ZR_GlassBroken", new MatSpec(new Color(0.10f, 0.12f, 0.15f), 0.4f, 0.92f, null) },
            { "ZR_Concrete", new MatSpec(new Color(0.58f, 0.57f, 0.54f), 0f, 0.1f, "ZR_ConcreteGrunge") },
            { "ZR_HazardStripe", new MatSpec(new Color(0.70f, 0.52f, 0.09f), 0f, 0.2f) },
            { "ZR_Sandbag", new MatSpec(new Color(0.62f, 0.50f, 0.30f), 0f, 0f, "ZR_Burlap") },
            { "ZR_GreenMetal", new MatSpec(new Color(0.12f, 0.21f, 0.13f), 0.5f, 0.3f) },
            { "ZR_LampGlow", new MatSpec(new Color(1f, 0.85f, 0.6f), 0f, 0.5f, null, new Color(1f, 0.72f, 0.38f) * 3f) },
            { "ZR_LampDead", new MatSpec(new Color(0.2f, 0.2f, 0.19f), 0f, 0.6f, null) },
            { "ZR_SignalRed", new MatSpec(new Color(0.6f, 0.05f, 0.03f), 0f, 0.6f, null, new Color(1f, 0.05f, 0.02f) * 2f) },
            { "ZR_SignalDark", new MatSpec(new Color(0.05f, 0.05f, 0.05f), 0f, 0.6f, null) },
            { "ZR_Embers", new MatSpec(new Color(0.3f, 0.06f, 0.01f), 0f, 0.1f, null, new Color(1f, 0.35f, 0.05f) * 3f) },
            { "ZR_Ash", new MatSpec(new Color(0.07f, 0.06f, 0.055f), 0f, 0f) },
            { "ZR_Sign", new MatSpec(new Color(0.06f, 0.30f, 0.15f), 0.4f, 0.4f) },
            { "ZR_PoliceLight", new MatSpec(new Color(0.05f, 0.08f, 0.5f), 0f, 0.6f, null, new Color(0.1f, 0.25f, 1f) * 1.5f) },
            { "ZR_Canvas", new MatSpec(new Color(0.36f, 0.38f, 0.25f), 0f, 0.05f, "ZR_CanvasGrunge") },
            { "ZR_CanvasDark", new MatSpec(new Color(0.16f, 0.17f, 0.12f), 0f, 0.05f, "ZR_CanvasGrunge") },
            { "ZR_Wood", new MatSpec(new Color(0.45f, 0.30f, 0.17f), 0f, 0.15f, "ZR_WoodGrain") },
            { "ZR_WoodOld", new MatSpec(new Color(0.34f, 0.28f, 0.21f), 0f, 0.1f, "ZR_WoodGrain") },
            { "ZR_Stone", new MatSpec(new Color(0.36f, 0.35f, 0.33f), 0f, 0.1f, "ZR_ConcreteGrunge") },
            { "ZR_Brick", new MatSpec(new Color(0.48f, 0.22f, 0.14f), 0f, 0.1f, "ZR_ConcreteGrunge") },
            { "ZR_Leaves", new MatSpec(new Color(0.16f, 0.30f, 0.10f), 0f, 0.15f, "ZR_LeafNoise") },
            { "ZR_LeavesDry", new MatSpec(new Color(0.34f, 0.32f, 0.14f), 0f, 0.1f, "ZR_LeafNoise") },
            { "ZR_Bark", new MatSpec(new Color(0.20f, 0.15f, 0.11f), 0f, 0.05f, "ZR_WoodGrain") },
            { "ZR_Ivy", new MatSpec(new Color(0.13f, 0.27f, 0.09f), 0f, 0.2f, "ZR_LeafNoise") },
            { "ZR_SearchGlow", new MatSpec(new Color(1f, 0.97f, 0.9f), 0f, 0.6f, null, new Color(1f, 0.93f, 0.8f) * 6f) },
            { "ZR_Wire", new MatSpec(new Color(0.25f, 0.25f, 0.25f), 0.9f, 0.4f, null) },
            { "ZR_ArmyGreen", new MatSpec(new Color(0.20f, 0.26f, 0.15f), 0.4f, 0.3f) },
            // leaf cards: see-through edges, both sides drawn
            { "ZR_Foliage", new MatSpec(new Color(0.95f, 1f, 0.92f), 0f, 0.15f, "ZR_LeafCluster", default, true) },
            { "ZR_FoliageDry", new MatSpec(new Color(1f, 0.97f, 0.9f), 0f, 0.1f, "ZR_LeafClusterDry", default, true) },
            { "ZR_IvyCard", new MatSpec(new Color(0.95f, 1f, 0.92f), 0f, 0.2f, "ZR_IvyCard", default, true) },
            { "ZR_GrassCard", new MatSpec(new Color(1f, 1f, 0.95f), 0f, 0.05f, "ZR_GrassCard", default, true) },
            { "ZR_Hesco", new MatSpec(new Color(0.62f, 0.55f, 0.40f), 0f, 0.02f, "ZR_Burlap") },
            { "ZR_Sand", new MatSpec(new Color(0.58f, 0.50f, 0.36f), 0f, 0.02f, "ZR_ConcreteGrunge") },
            { "ZR_DryGrass", new MatSpec(Color.white, 0f, 0.08f, "ZR_StalkGreen", default, false, true) },
            { "ZR_LeafA", new MatSpec(new Color(0.16f, 0.30f, 0.09f), 0f, 0.25f, "ZR_LeafNoise", default, false, true) },
            { "ZR_LeafB", new MatSpec(new Color(0.24f, 0.40f, 0.12f), 0f, 0.25f, "ZR_LeafNoise", default, false, true) },
            { "ZR_LeafC", new MatSpec(new Color(0.36f, 0.44f, 0.15f), 0f, 0.2f, "ZR_LeafNoise", default, false, true) },
            { "ZR_LeafCore", new MatSpec(new Color(0.06f, 0.11f, 0.04f), 0f, 0.05f, "ZR_LeafNoise") },
            { "ZR_DryGrassDark", new MatSpec(Color.white, 0f, 0.05f, "ZR_StalkStraw", default, false, true) },
        };

        static Material EnsureMaterial(string name)
        {
            Directory.CreateDirectory(MatDir);
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            var s = Mats[name];
            m.SetColor("_BaseColor", s.color);
            m.SetFloat("_Metallic", s.metal);
            m.SetFloat("_Smoothness", s.smooth);
            var tex = s.tex != null ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{s.tex}.png") : null;
            m.SetTexture("_BaseMap", tex);
            if (s.emit != default)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", s.emit);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else m.DisableKeyword("_EMISSION");
            if (s.cutout)
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.45f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Cull", 0f); // both sides
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                m.SetOverrideTag("RenderType", "TransparentCutout");
                if (tex != null)
                {
                    // keep leaves solid when the texture shrinks with distance
                    var ti = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex));
                    if (ti != null && (!ti.mipMapsPreserveCoverage || !ti.alphaIsTransparency || ti.wrapMode != TextureWrapMode.Clamp))
                    {
                        ti.alphaIsTransparency = true;
                        ti.mipMapsPreserveCoverage = true;
                        ti.alphaTestReferenceValue = 0.45f;
                        ti.wrapMode = TextureWrapMode.Clamp;
                        ti.SaveAndReimport();
                    }
                }
            }
            m.SetFloat("_Cull", s.twoSided ? 0f : 2f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material FireParticleMaterial()
        {
            string path = $"{MatDir}/ZR_FireParticle.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            // additive, see-through, no depth write
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 2f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BaseMap", SoftDot());
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Texture2D SoftDot()
        {
            string path = $"{TexDir}/ZR_SoftDot.png";
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t != null) return t;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---------------------------------------------------------------- prefabs

        public static string BuildPrefabs()
        {
            var sb = new StringBuilder();
            if (!Directory.Exists(ModelDir)) return "FAILED: run Blender/build_city_props.py first (no " + ModelDir + ").";
            foreach (var n in Mats.Keys) EnsureMaterial(n);
            var fire = FireParticleMaterial();
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(PrefabDir);

            foreach (var fbx in ModelDirs.Where(Directory.Exists).SelectMany(d => Directory.GetFiles(d, "*.fbx")))
            {
                string path = fbx.Replace('\\', '/');
                string name = Path.GetFileNameWithoutExtension(path);
                // materials: ours, by name
                var imp = (ModelImporter)AssetImporter.GetAtPath(path);
                bool changed = false;
                foreach (var src in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Select(x => x.name).Distinct().ToList())
                {
                    if (!Mats.ContainsKey(src)) continue;
                    imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), src), EnsureMaterial(src));
                    changed = true;
                }
                if (changed) imp.SaveAndReimport();

                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                // a clean parent at the origin: the FBX root keeps whatever axis turn the importer gave it
                var go = new GameObject(name);
                var mesh = (GameObject)PrefabUtility.InstantiatePrefab(model);
                mesh.transform.SetParent(go.transform, false);
                try
                {
                    var b = Bounds(go);
                    if (b.size.y < 0.05f || b.size.y > 50f)
                    {
                        sb.AppendLine($"  {name}: SKIPPED, odd size {b.size} (check the Blender export units)");
                        continue;
                    }
                    AddColliders(go, name, b);
                    AddEffects(go, name, b, fire);
                    foreach (var t in go.GetComponentsInChildren<Transform>(true))
                        if (t.GetComponent<Light>() == null && t.GetComponent<ParticleSystem>() == null)
                            GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
                    if (NoShadow.Any(name.Contains))
                        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    foreach (var lg in go.GetComponents<LODGroup>()) Object.DestroyImmediate(lg);
                    var cull = CullSmall.FirstOrDefault(c => name.Contains(c.name));
                    if (cull.name != null)
                    {
                        var lod = go.AddComponent<LODGroup>();
                        lod.SetLODs(new[] { new LOD(cull.cull, go.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToArray()) });
                        lod.RecalculateBounds();
                    }
                    PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{name}.prefab");
                    sb.AppendLine($"  {name}: {b.size.x:0.0} x {b.size.y:0.0} x {b.size.z:0.0} m");
                }
                finally { Object.DestroyImmediate(go); }
            }
            AssetDatabase.SaveAssets();
            return "City prop prefabs (Assets/Prefabs/CityProps):\n" + sb;
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static readonly string[] Climbable = { "ScaffoldTower", "SniperNest", "PlankBridge", "PlankRamp", "SearchlightTower" };
        static readonly string[] WalkThrough = { "GrassTuft", "GrassClump", "Ivy", "Bush", "Campfire", "DryWeeds", "DeadShrub", "WildGrass" };
        /// Small plants vanish when they get this small on screen (share of screen height): saves phones a lot.
        static readonly (string name, float cull)[] CullSmall = { ("WildGrass", 0.025f), ("DryWeeds", 0.025f), ("LeafyBush", 0.02f), ("DeadShrub", 0.02f), ("IvyCard", 0.015f) };
        /// Cheap flat foliage: no shadows (they cost a lot for very little on cards).
        static readonly string[] NoShadow = { "GrassClump", "IvyCard", "GrassTuft", "DryWeeds", "WildGrass" };

        static void AddColliders(GameObject go, string name, Bounds b)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            if (WalkThrough.Any(name.Contains)) return;
            if (Climbable.Any(name.Contains))
            {
                // exact shape: stairs, decks and rails are walked on by players and zombies
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                    mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                return;
            }
            if (name.Contains("Lamp") || name.Contains("TrafficLight") || name.Contains("Floodlight"))
            {
                var box = go.AddComponent<BoxCollider>();
                float h = name.Contains("Broken") ? 2.6f : b.size.y;
                float w = name.Contains("Floodlight") ? 0.8f : 0.4f;
                box.center = new Vector3(0f, h / 2f, 0f);
                box.size = new Vector3(w, h, w);
            }
            else if (name.Contains("Tree"))
            {
                var cap = go.AddComponent<CapsuleCollider>();
                cap.radius = 0.3f;
                cap.height = b.size.y * 0.6f;
                cap.center = new Vector3(0f, cap.height / 2f, 0f);
            }
            else if (name.Contains("Barrel") || name.Contains("Bonfire"))
            {
                bool bonfire = name.Contains("Bonfire");
                var cap = go.AddComponent<CapsuleCollider>();
                cap.radius = bonfire ? 1.0f : Mathf.Min(b.extents.x, b.extents.z);
                cap.height = Mathf.Max(b.size.y, cap.radius * 2f);
                cap.center = bonfire ? new Vector3(0f, cap.height / 2f, 0f) : b.center;
            }
            else
            {
                var box = go.AddComponent<BoxCollider>();
                box.center = b.center;
                box.size = new Vector3(b.size.x * 0.96f, b.size.y, b.size.z * 0.96f);
            }
        }

        /// Direction of the lamp arm (the bulb side) in prefab space.
        internal static Vector3 ArmDir(Bounds b) =>
            Mathf.Abs(b.center.x) > Mathf.Abs(b.center.z) ? new Vector3(Mathf.Sign(b.center.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(b.center.z));

        static Vector3? blenderForward;
        /// Blender's +Y (the way every prop was built to face) in Unity prefab space, read off the street lamp's arm.
        internal static Vector3 BlenderForward()
        {
            if (blenderForward.HasValue) return blenderForward.Value;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/ZR_StreetLamp_On.fbx");
            var go = new GameObject("_probe");
            try
            {
                var m = (GameObject)PrefabUtility.InstantiatePrefab(model);
                m.transform.SetParent(go.transform, false);
                blenderForward = ArmDir(Bounds(go));
            }
            finally { Object.DestroyImmediate(go); }
            return blenderForward.Value;
        }

        static Light AddLight(GameObject go, string child, LightType type, Vector3 pos, Quaternion rot, Color color, float intensity, float range, float angle = 0f)
        {
            var l = new GameObject(child);
            l.transform.SetParent(go.transform, false);
            l.transform.localPosition = pos;
            l.transform.localRotation = rot;
            var light = l.AddComponent<Light>();
            light.type = type;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            if (type == LightType.Spot) { light.spotAngle = angle; light.innerSpotAngle = angle * 0.6f; }
            light.shadows = LightShadows.None;
            return light;
        }

        static void AddFire(GameObject go, Vector3 pos, float scale, Material fire)
        {
            var f = new GameObject("Fire");
            f.transform.SetParent(go.transform, false);
            f.transform.localPosition = pos;
            f.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = f.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f * Mathf.Sqrt(scale), 0.9f * Mathf.Sqrt(scale));
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.7f * scale, 1.5f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f * scale, 0.65f * scale);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.3f, 0.9f), new Color(1f, 0.45f, 0.1f, 0.9f));
            main.maxParticles = Mathf.RoundToInt(60 * scale);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 28f * scale;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 10f; sh.radius = 0.2f * scale;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.4f), 0f), new GradientColorKey(new Color(1f, 0.35f, 0.05f), 0.5f), new GradientColorKey(new Color(0.4f, 0.05f, 0.02f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.15f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
            var r = f.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = fire;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        static void AddEffects(GameObject go, string name, Bounds b, Material fire)
        {
            var cfg = Config();
            foreach (Transform t in go.transform.Cast<Transform>().ToList())
                if (t.name == "Light" || t.name == "Fire" || t.name == "Beam") Object.DestroyImmediate(t.gameObject);
            var fwd = BlenderForward();
            if (name == "ZR_StreetLamp_On")
            {
                var arm = ArmDir(b);
                float reach = Mathf.Max(Mathf.Abs(Vector3.Dot(b.max, arm)), Mathf.Abs(Vector3.Dot(b.min, arm)));
                AddLight(go, "Light", LightType.Spot, arm * (reach - 0.5f) + Vector3.up * (b.max.y - 0.25f), Quaternion.Euler(90f, 0f, 0f),
                         cfg.lampColor, cfg.lampIntensity, cfg.lampRange, cfg.lampSpotAngle);
            }
            else if (name == "ZR_Barrel_Fire" || name == "ZR_Campfire" || name == "ZR_Bonfire")
            {
                float k = name == "ZR_Bonfire" ? cfg.bonfireScale : 1f;
                bool barrel = name == "ZR_Barrel_Fire";
                var l = AddLight(go, "Light", LightType.Point, Vector3.up * (barrel ? b.max.y + 0.4f : 1.0f * k), Quaternion.identity,
                                 cfg.fireColor, cfg.fireIntensity * k, cfg.fireRange * k);
                l.gameObject.AddComponent<FlickerLight>().style = FlickerLight.Style.Fire;
                AddFire(go, Vector3.up * (barrel ? b.max.y * 0.82f : 0.1f), barrel ? 1f : 1.3f * k, fire);
            }
            else if (name == "ZR_SearchlightTower")
            {
                // beam from the lens, a little below the horizon, swept left and right
                var pivot = new GameObject("Beam");
                pivot.transform.SetParent(go.transform, false);
                pivot.transform.localPosition = Vector3.up * (b.max.y - 0.3f) + fwd * 0.6f;
                pivot.transform.localRotation = Quaternion.LookRotation(fwd, Vector3.up);
                pivot.AddComponent<SearchlightSweep>();
                AddLight(pivot, "Light", LightType.Spot, Vector3.zero, Quaternion.Euler(cfg.searchlightTilt, 0f, 0f),
                         cfg.searchlightColor, cfg.searchlightIntensity, cfg.searchlightRange, cfg.searchlightAngle);
            }
            else if (name == "ZR_Floodlight")
            {
                var down = Quaternion.AngleAxis(35f, Vector3.Cross(Vector3.up, fwd)) * fwd;
                AddLight(go, "Light", LightType.Spot, Vector3.up * 3.2f + fwd * 0.25f, Quaternion.LookRotation(down, Vector3.up),
                         cfg.floodlightColor, cfg.floodlightIntensity, cfg.floodlightRange, cfg.floodlightAngle);
            }
        }

        // ---------------------------------------------------------------- config

        internal static CityPropsConfig Config()
        {
            var c = AssetDatabase.LoadAssetAtPath<CityPropsConfig>(ConfigPath);
            if (c != null) return c;
            c = ScriptableObject.CreateInstance<CityPropsConfig>();
            AssetDatabase.CreateAsset(c, ConfigPath);
            AssetDatabase.SaveAssets();
            return c;
        }

        // ---------------------------------------------------------------- placement

        internal class Placer
        {
            public Vector3 baseC; public float baseR, minR, maxR; public System.Random rnd; public CityPropsConfig cfg;
            public Transform root; public List<(Vector3 p, float r)> placed = new List<(Vector3, float)>();
            public HashSet<Collider> ignore = new HashSet<Collider>();

            public float U() => (float)rnd.NextDouble();
            public float Range(float a, float b) => a + (b - a) * U();

            public Vector3 RandomPoint(float min, float max)
            {
                float a = U() * Mathf.PI * 2f;
                float r = Mathf.Sqrt(Mathf.Lerp(min * min, max * max, U()));
                return baseC + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
            }

            /// Ground point when the top surface here is the terrain (not a roof or a rubble heap).
            public bool Ground(Vector3 p, out Vector3 g)
            {
                g = p;
                var hits = Physics.RaycastAll(new Vector3(p.x, 300f, p.z), Vector3.down, 600f, ~0, QueryTriggerInteraction.Ignore);
                if (hits.Length == 0) return false;
                var top = hits.OrderByDescending(h => h.point.y).First();
                if (!(top.collider is TerrainCollider)) return false;
                g = top.point;
                return true;
            }

            public float Open(Vector3 from, Vector3 dir, float max)
            {
                float best = max;
                foreach (var h in Physics.RaycastAll(from + Vector3.up * 1.0f, dir, max, ~0, QueryTriggerInteraction.Ignore))
                    if (!(h.collider is TerrainCollider) && h.distance < best) best = h.distance;
                return best;
            }

            /// The footprint (plus the walk gap) is free of everything but the terrain, and the ground is flat enough.
            public bool Clear(Vector3 g, Quaternion rot, Vector3 size, float gap)
            {
                var half = new Vector3(size.x / 2f + gap, size.y / 2f, size.z / 2f + gap);
                foreach (var c in Physics.OverlapBox(g + Vector3.up * (size.y / 2f + 0.25f), half, rot, ~0, QueryTriggerInteraction.Ignore))
                    if (!(c is TerrainCollider) && !ignore.Contains(c)) return false;
                foreach (var corner in new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1) })
                {
                    var q = g + rot * Vector3.Scale(corner, size / 2f);
                    if (!Ground(q, out var gq) || Mathf.Abs(gq.y - g.y) > 0.7f) return false;
                }
                return true;
            }

            public bool FarFromOthers(Vector3 p, float r)
            {
                foreach (var (q, qr) in placed)
                    if (Flat(p - q) < r + qr) return false;
                return true;
            }

            /// Street axis here: the line with the most open space both ways.
            public Vector3 StreetAxis(Vector3 g)
            {
                Vector3 best = Vector3.forward; float score = -1f;
                for (int i = 0; i < 18; i++)
                {
                    var d = Quaternion.Euler(0f, i * 10f, 0f) * Vector3.forward;
                    float s = Open(g, d, 60f) + Open(g, -d, 60f);
                    if (s > score) { score = s; best = d; }
                }
                return best;
            }

            /// Nearest wall within reach: its direction from the point and distance.
            public bool NearWall(Vector3 g, float reach, out Vector3 dir, out float dist)
            {
                dir = Vector3.zero; dist = reach;
                for (int i = 0; i < 16; i++)
                {
                    var d = Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward;
                    float o = Open(g, d, reach);
                    if (o < dist) { dist = o; dir = d; }
                }
                return dir != Vector3.zero;
            }

            public GameObject Put(GameObject prefab, Vector3 g, Quaternion rot, string group, float keepR)
            {
                var parent = root.Find(group);
                if (parent == null) { parent = new GameObject(group).transform; parent.SetParent(root, false); }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                go.transform.SetPositionAndRotation(g, rot);
                if (keepR > 0f) placed.Add((g, keepR)); // grass and ivy (keepR 0) never block later props
                Physics.SyncTransforms();
                return go;
            }
        }

        internal static GameObject Prefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{name}.prefab");

        internal static Vector3 PrefabSize(GameObject prefab)
        {
            var box = prefab.GetComponent<BoxCollider>();
            if (box != null) return box.size;
            var cap = prefab.GetComponent<CapsuleCollider>();
            if (cap != null) return new Vector3(cap.radius * 2f, cap.height, cap.radius * 2f);
            return Vector3.one;
        }

        /// Turns a prefab so its long side (vehicles, walls) lies along `axis`.
        internal static Quaternion AlongAxis(GameObject prefab, Vector3 axis)
        {
            var s = PrefabSize(prefab);
            var look = Quaternion.LookRotation(axis, Vector3.up);
            return s.x > s.z ? look * Quaternion.Euler(0f, 90f, 0f) : look;
        }

        public static string Place()
        {
            var cfg = Config();
            var zone = Object.FindAnyObjectByType<ZoneManager>(FindObjectsInactive.Include);
            var safe = Object.FindAnyObjectByType<SafeBase>(FindObjectsInactive.Include);
            var mapT = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(t => t.parent == null && t.name == "Map");
            if (zone == null || safe == null || mapT == null) return "FAILED: open MainLevel first (needs ZoneManager, SafeBase and Map).";
            if (Prefab("ZR_Car_Sedan_Blue") == null) return "FAILED: build the prefabs first (props build).";

            Undo.SetCurrentGroupName("Place city props");
            int undo = Undo.GetCurrentGroup();
            var old = mapT.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            Physics.SyncTransforms();
            var root = new GameObject(RootName).transform;
            root.SetParent(mapT, false);
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Place city props");

            var P = new Placer
            {
                baseC = safe.Center, baseR = safe.radius, cfg = cfg, root = root, rnd = new System.Random(cfg.seed),
                minR = safe.radius + 6f, maxR = zone.maxRadius - cfg.edgeMargin,
            };
            // the safe base's own trigger/walls never block a prop next to it
            foreach (var c in safe.GetComponentsInChildren<Collider>(true)) if (c.isTrigger) P.ignore.Add(c);
            var sb = new StringBuilder();

            // ---- base defences: sandbag ring with gaps, barrier runs further out, fire barrels
            var sandbags = Prefab("ZR_Sandbags");
            int bags = 0;
            for (int i = 0; i < cfg.sandbagSegments; i++)
            {
                float a = (i + 0.5f) / cfg.sandbagSegments * Mathf.PI * 2f;
                var outDir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var tangent = Vector3.Cross(Vector3.up, outDir);
                var size = PrefabSize(sandbags);
                for (int k = -1; k <= 1; k += 2) // two sandbag pieces side by side
                {
                    var p = P.baseC + outDir * (P.baseR + cfg.sandbagRingOffset) + tangent * (k * size.z * 0.5f);
                    if (!P.Ground(p, out var g)) continue;
                    var rot = AlongAxis(sandbags, tangent);
                    if (!P.Clear(g, rot, size, 0.1f)) continue;
                    P.Put(sandbags, g, rot, "BaseDefences", 0.5f); bags++;
                }
            }
            var barrier = Prefab("ZR_Barrier_Concrete");
            int barriers = 0;
            for (int i = 0; i < cfg.barrierRuns; i++)
            {
                float a = (float)i / cfg.barrierRuns * Mathf.PI * 2f + 0.2f;
                var outDir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var tangent = Vector3.Cross(Vector3.up, outDir);
                var size = PrefabSize(barrier);
                for (int k = -1; k <= 1; k++)
                {
                    var p = P.baseC + outDir * (P.baseR + cfg.barrierRingOffset) + tangent * (k * (size.z + 0.15f));
                    if (!P.Ground(p, out var g)) continue;
                    var rot = AlongAxis(barrier, Quaternion.Euler(0f, P.Range(-6f, 6f), 0f) * tangent);
                    if (!P.Clear(g, rot, size, 0.05f)) continue;
                    P.Put(barrier, g, rot, "BaseDefences", 0.5f); barriers++;
                }
            }
            var fireBarrel = Prefab("ZR_Barrel_Fire");
            int baseFires = 0;
            for (int i = 0; i < cfg.burningBarrelsAtBase; i++)
            {
                float a = (i + 0.25f) / cfg.burningBarrelsAtBase * Mathf.PI * 2f;
                var p = P.baseC + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (P.baseR + cfg.sandbagRingOffset + 3f);
                if (P.Ground(p, out var g) && P.Clear(g, Quaternion.identity, PrefabSize(fireBarrel), 0.3f))
                { P.Put(fireBarrel, g, Quaternion.identity, "BaseDefences", 1f); baseFires++; }
            }
            sb.AppendLine($"Base defences: {bags} sandbag walls, {barriers} concrete barriers, {baseFires} burning barrels.");

            // ---- vehicles
            int Vehicles(string[] names, int count, string label)
            {
                int done = 0;
                for (int tries = 0; tries < count * 80 && done < count; tries++)
                {
                    var prefab = Prefab(names[P.rnd.Next(names.Length)]);
                    var p = P.RandomPoint(Mathf.Max(P.minR, cfg.vehicleMinFromBase), P.maxR);
                    if (!P.FarFromOthers(p, cfg.vehicleSpacing) || !P.Ground(p, out var g)) continue;
                    var axis = P.StreetAxis(g);
                    bool parked = P.U() < cfg.parkedShare;
                    var dir = Quaternion.Euler(0f, parked ? P.Range(-6f, 6f) + (P.U() < 0.5f ? 180f : 0f) : P.Range(-70f, 70f), 0f) * axis;
                    var rot = AlongAxis(prefab, dir);
                    if (!parked) rot = rot * Quaternion.Euler(P.Range(-3f, 3f), 0f, P.Range(-4f, 4f)); // crashed: a little off level
                    if (!P.Clear(g, rot, PrefabSize(prefab), cfg.walkGap)) continue;
                    P.Put(prefab, g, rot, "Vehicles", cfg.vehicleSpacing * 0.5f);
                    done++;
                }
                sb.AppendLine($"{label}: {done} of {count}.");
                return done;
            }
            Vehicles(new[] { "ZR_Bus_Wreck" }, cfg.buses, "Buses");
            Vehicles(new[] { "ZR_Car_Police" }, cfg.policeCars, "Police cars");
            Vehicles(new[] { "ZR_Car_Sedan_Burnt", "ZR_Car_Sedan_Blue", "ZR_Car_Hatch_Red", "ZR_Car_Hatch_Cream" }, cfg.cars, "Cars");

            // ---- against walls: lamps, traffic lights, dumpsters
            int Walls(string label, int count, float spacing, System.Func<Vector3, Vector3, float, bool> accept, System.Action<Vector3, Vector3> put)
            {
                int done = 0;
                for (int tries = 0; tries < count * 120 && done < count; tries++)
                {
                    var p = P.RandomPoint(P.minR, P.maxR);
                    if (!P.FarFromOthers(p, spacing * 0.5f) || !P.Ground(p, out var g)) continue;
                    if (!P.NearWall(g, cfg.wallDistance.y, out var wall, out float dist) || dist < cfg.wallDistance.x) continue;
                    if (!accept(g, wall, dist)) continue;
                    put(g, -wall);
                    done++;
                }
                sb.AppendLine($"{label}: {done} of {count}.");
                return done;
            }
            var lampOn = Prefab("ZR_StreetLamp_On");
            var lampBroken = Prefab("ZR_StreetLamp_Broken");
            var armOn = ArmDir(PrefabBounds(lampOn));
            int working = 0;
            Walls("Street lamps", cfg.streetLamps, cfg.lampSpacing,
                (g, wall, d) => P.Open(g, -wall, 30f) > 12f && P.Clear(g, Quaternion.identity, new Vector3(0.5f, 6f, 0.5f), 0.4f),
                (g, open) =>
                {
                    bool on = P.U() < cfg.lampsWorkingShare;
                    var prefab = on ? lampOn : lampBroken;
                    // turn the arm towards the street
                    var rot = Quaternion.FromToRotation(armOn, open);
                    rot = Quaternion.Euler(0f, rot.eulerAngles.y, 0f);
                    var go = P.Put(prefab, g, rot, "StreetLamps", cfg.lampSpacing * 0.5f);
                    if (on)
                    {
                        working++;
                        if (P.U() < cfg.workingLampsFlickerShare)
                        {
                            var l = go.GetComponentInChildren<Light>();
                            if (l != null) l.gameObject.AddComponent<FlickerLight>().style = FlickerLight.Style.FailingLamp;
                        }
                    }
                });
            sb.AppendLine($"  working lamps (with light): {working}.");

            var traffic = Prefab("ZR_TrafficLight");
            var armT = ArmDir(PrefabBounds(traffic));
            Walls("Traffic lights", cfg.trafficLights, 40f,
                (g, wall, d) =>
                {
                    int open = 0;
                    for (int i = 0; i < 8; i++) if (P.Open(g, Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward, 30f) >= 29f) open++;
                    return open >= 4 && P.Clear(g, Quaternion.identity, new Vector3(0.5f, 4.6f, 0.5f), 0.4f);
                },
                (g, open) =>
                {
                    var rot = Quaternion.FromToRotation(armT, open);
                    P.Put(traffic, g, Quaternion.Euler(0f, rot.eulerAngles.y, 0f), "StreetLamps", 20f);
                });

            var dumpster = Prefab("ZR_Dumpster");
            Walls("Dumpsters", cfg.dumpsters, 25f,
                (g, wall, d) => d < cfg.wallDistance.x + 1.5f,
                (g, open) =>
                {
                    var rot = AlongAxis(dumpster, Vector3.Cross(Vector3.up, open));
                    if (P.Clear(g, rot, PrefabSize(dumpster), 0.3f)) P.Put(dumpster, g, rot, "Junk", 6f);
                });

            // ---- barrels: small clusters, some burning
            var barrels = new[] { Prefab("ZR_Barrel_Rust"), Prefab("ZR_Barrel_Blue") };
            int clusters = 0, cityFires = 0;
            for (int tries = 0; tries < cfg.barrelClusters * 80 && clusters < cfg.barrelClusters; tries++)
            {
                var p = P.RandomPoint(P.minR + 10f, P.maxR);
                if (!P.FarFromOthers(p, 6f) || !P.Ground(p, out var g)) continue;
                if (!P.Clear(g, Quaternion.identity, new Vector3(2.4f, 1f, 2.4f), cfg.walkGap * 0.5f)) continue;
                int n = 2 + P.rnd.Next(3);
                for (int k = 0; k < n; k++)
                {
                    var q = g + Quaternion.Euler(0f, P.Range(0f, 360f), 0f) * Vector3.forward * P.Range(0f, 1.1f);
                    if (!P.Ground(q, out var gq) || !P.Clear(gq, Quaternion.identity, new Vector3(0.6f, 0.9f, 0.6f), 0.02f)) continue;
                    var b = barrels[P.rnd.Next(barrels.Length)];
                    var rot = Quaternion.Euler(0f, P.Range(0f, 360f), 0f);
                    if (P.U() < 0.2f) // knocked over
                    {
                        rot = rot * Quaternion.Euler(90f, 0f, 0f);
                        gq += Vector3.up * 0.3f;
                    }
                    P.Put(b, gq, rot, "Junk", 0.4f);
                }
                clusters++;
            }
            for (int tries = 0; tries < cfg.burningBarrelsInCity * 80 && cityFires < cfg.burningBarrelsInCity; tries++)
            {
                var p = P.RandomPoint(P.minR + 20f, P.maxR);
                if (!P.FarFromOthers(p, 30f) || !P.Ground(p, out var g)) continue;
                if (!P.Clear(g, Quaternion.identity, PrefabSize(fireBarrel), cfg.walkGap)) continue;
                P.Put(fireBarrel, g, Quaternion.identity, "Junk", 20f);
                cityFires++;
            }
            sb.AppendLine($"Barrel clusters: {clusters}; burning barrels in the city: {cityFires}.");

            Undo.CollapseUndoOperations(undo);
            int lights = root.GetComponentsInChildren<Light>(true).Length;
            sb.Insert(0, $"Placed {root.GetComponentsInChildren<Transform>().Count(t => t.parent != null && t.parent.parent == root)} props under Map/{RootName} ({lights} lights).\n");
            return sb + MapCleanupTools.RebakeAndSave(mapT);
        }

        internal static Bounds PrefabBounds(GameObject prefab)
        {
            var rs = prefab.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToArray();
            var mf = rs[0].GetComponent<MeshFilter>();
            var b = mf.sharedMesh.bounds;
            // mesh space -> prefab space (the FBX root may carry a rotation)
            var m = prefab.transform.worldToLocalMatrix * rs[0].transform.localToWorldMatrix;
            var c = m.MultiplyPoint3x4(b.center);
            var e = m.MultiplyVector(b.extents);
            return new Bounds(c, new Vector3(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z)) * 2f);
        }

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        [MenuItem("Zombie Rush/Level/Build City Prop Prefabs")]
        static void BuildMenu() => Debug.Log("Zombie Rush: " + BuildPrefabs());

        [MenuItem("Zombie Rush/Level/Place City Props")]
        static void PlaceMenu() => Debug.Log("Zombie Rush: " + Place());
    }
}
