using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZombieRush.Data;
using ZombieRush.World;

namespace ZombieRush.EditorTools
{
    /// The Fortress Haven base (2026-11-17): the user's Meshy fortress replaces the HQ hall (building2).
    ///   - model + textures: Blender/fortress_base.py -> Assets/Models/2026-11-17_FortressHaven (FBX, PNGs, JSON points);
    ///   - this tool: texture import settings, URP materials, the prefab (colliders, room lamps, gate lights, deck
    ///     floodlights, door marker), then in MainLevel: hides the hall, places the fortress with its room on the
    ///     safe base centre, lowers the terrain poking into the yard, hides props standing inside it, refurnishes the
    ///     room (BaseFurnishTools), moves the player start into the room, re-bakes the zombie walk map, saves.
    ///   - tuning: Assets/Data/FortressBaseConfig.asset. First run backs up the scene and terrain to
    ///     Backups/2026-11-17_BeforeFortress/.
    /// Marker: Temp/ZR_RunFortress (empty or "build"; "assets" = materials + prefab only), report Temp/ZR_Fortress.log.
    [InitializeOnLoad]
    public static class FortressBaseTools
    {
        const string ModelDir = "Assets/Models/2026-11-17_FortressHaven";
        const string FbxPath = ModelDir + "/FortressHaven.fbx";
        const string JsonPath = ModelDir + "/FortressHaven.json";
        const string MatDir = ModelDir + "/Materials";
        const string PrefabPath = "Assets/Prefabs/Base/FortressHaven.prefab";
        const string ConfigPath = "Assets/Data/FortressBaseConfig.asset";
        const string FloodPrefab = "Assets/Prefabs/CityProps/ZR_Floodlight.prefab";
        const string PropsFbx = ModelDir + "/FortressProps.fbx";
        const string CratePrefab = "Assets/Prefabs/Base/CrateStack.prefab";
        const string WallLampPrefab = "Assets/Prefabs/Base/WallLamp.prefab";
        const string TerrainCopyPath = "Assets/Data/MainLevel_Terrain.asset";
        const string BackupDir = "Backups/2026-11-17_BeforeFortress";
        const string RootName = "FortressHaven";
        const string OldHall = "building2";
        const string Marker = "Temp/ZR_RunFortress";
        const string Report = "Temp/ZR_Fortress.log";
        static double nextCheck;

        static FortressBaseTools() => EditorApplication.update += Poll;

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextCheck) return;
            nextCheck = EditorApplication.timeSinceStartup + 2.0;
            if (!File.Exists(Marker)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            string mode = File.ReadAllText(Marker).Trim().ToLowerInvariant();
            File.Delete(Marker);
            File.WriteAllText(Report, "(running)\n");
            string result;
            try
            {
                result = mode == "assets" ? BuildAssets(new StringBuilder())
                       : mode.StartsWith("views") ? Views(mode.Substring(5).Trim())
                       : mode == "survey" ? Survey()
                       : mode == "camera" ? SceneCamera()
                       : Build();
            }
            catch (System.Exception e) { result = "FAILED: " + e; }
            File.WriteAllText(Report, result);
        }

        [MenuItem("Zombie Rush/Level/Fortress Base (replace the hall)")]
        static void BuildMenu() => Debug.Log(Build());

        // ------------------------------------------------------------------ data from Blender

        class Spot { public string kind; public Vector3 pos, aim; }

        class FortData
        {
            public Vector3 roomMin, roomMax; public float roomHeight;
            public List<Vector3> doors = new List<Vector3>(), gates = new List<Vector3>(), lamps = new List<Vector3>();
            public List<Spot> spots = new List<Spot>();
            public List<string> exterior = new List<string>();
            public bool hasHelipad; public Vector3 padCentre, padNose; public bool hasStairs; public Vector3 padStairs;
            public List<Vector3> stairsPath = new List<Vector3>();   // landing, flight top, flight bottom, walk-off end
            public Vector3 RoomCentre => new Vector3((roomMin.x + roomMax.x) * 0.5f, 0f, (roomMin.z + roomMax.z) * 0.5f);
        }

        static FortData LoadData()
        {
            if (!File.Exists(JsonPath)) return null;
            var root = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(JsonPath));
            Vector3 V(object o) { var l = (List<object>)o; return new Vector3(F(l[0]), F(l[1]), F(l[2])); }
            float F(object o) => System.Convert.ToSingle(o, CultureInfo.InvariantCulture);
            var room = (Dictionary<string, object>)root["room"];
            var d = new FortData { roomMin = V(room["min"]), roomMax = V(room["max"]), roomHeight = F(room["height"]) };
            foreach (var o in (List<object>)root["doors"]) d.doors.Add(V(o));
            foreach (var o in (List<object>)root["gates"]) d.gates.Add(V(o));
            foreach (var o in (List<object>)root["lamps"]) d.lamps.Add(V(o));
            foreach (var o in (List<object>)root["spots"])
            {
                var s = (Dictionary<string, object>)o;
                d.spots.Add(new Spot { kind = (string)s["kind"], pos = V(s["pos"]), aim = V(s["aim"]) });
            }
            if (root.TryGetValue("exterior", out var ext) && ext is List<object> el) d.exterior = el.Select(x => (string)x).ToList();
            if (root.TryGetValue("helipad", out var hp) && hp is Dictionary<string, object> h)
            {
                d.hasHelipad = true; d.padCentre = V(h["centre"]); d.padNose = V(h["nose"]);
                if (h.TryGetValue("stairs", out var st) && st is List<object>) { d.hasStairs = true; d.padStairs = V(st); }
                if (h.TryGetValue("stairsPath", out var sp) && sp is List<object> spl) d.stairsPath = spl.Select(V).ToList();
                if (d.stairsPath.Count > 0) { d.hasStairs = true; d.padStairs = d.stairsPath[0]; }   // the steel flight is the way down
            }
            return d;
        }

        static FortressBaseConfig Config()
        {
            var c = AssetDatabase.LoadAssetAtPath<FortressBaseConfig>(ConfigPath);
            if (c != null) return c;
            c = ScriptableObject.CreateInstance<FortressBaseConfig>();
            AssetDatabase.CreateAsset(c, ConfigPath);
            AssetDatabase.SaveAssets();
            return c;
        }

        // ------------------------------------------------------------------ textures, materials, prefab

        static void ImportTexture(string file, bool srgb, bool normal, int pcMax, int androidMax)
        {
            string path = $"{ModelDir}/{file}";
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = srgb && !normal;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = false;
            ti.mipmapEnabled = true;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.anisoLevel = 4;
            ti.maxTextureSize = pcMax;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            var android = ti.GetPlatformTextureSettings("Android");
            android.overridden = true; android.maxTextureSize = Mathf.Min(pcMax, androidMax);
            android.format = TextureImporterFormat.ASTC_6x6;
            ti.SetPlatformTextureSettings(android);
            ti.SaveAndReimport();
        }

        static Texture2D Tex(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{ModelDir}/{file}");

        static Material Lit(string name, string albedo, string normalMap, string metalSmooth, string occlusion, float normalScale, float occStrength, Vector2 tiling)
        {
            Directory.CreateDirectory(MatDir);
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetTexture("_BaseMap", Tex(albedo)); m.SetTextureScale("_BaseMap", tiling);
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BumpMap", Tex(normalMap)); m.SetFloat("_BumpScale", normalScale);
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap", Tex(metalSmooth));
            m.SetFloat("_Metallic", 1f); m.SetFloat("_Smoothness", 1f);          // both come from the map
            m.SetFloat("_SmoothnessTextureChannel", 0f);                          // smoothness in the map's alpha
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            if (occlusion != null && Tex(occlusion) != null)
            {
                m.SetTexture("_OcclusionMap", Tex(occlusion)); m.SetFloat("_OcclusionStrength", occStrength);
                m.EnableKeyword("_OCCLUSIONMAP");
            }
            else { m.SetTexture("_OcclusionMap", null); m.DisableKeyword("_OCCLUSIONMAP"); }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Glow(string name, Color color, float hdr)
        {
            Directory.CreateDirectory(MatDir);
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", color);
            m.SetColor("_EmissionColor", color * hdr);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.SetFloat("_Smoothness", 0.6f);
            EditorUtility.SetDirty(m);
            return m;
        }

        public static string BuildAssets(StringBuilder sb)
        {
            var cfg = Config();
            var data = LoadData();
            if (data == null || AssetImporter.GetAtPath(FbxPath) == null)
                return "FAILED: run Blender/fortress_base.py --write first (no " + FbxPath + " / json).";

            // textures + materials (names match the FBX's Blender materials). The outside is either the per-surface
            // tiling set (Ext_*, listed in the JSON) or, from an older export, Meshy's enhanced atlas (Fortress_*).
            var mats = new Dictionary<string, Material> { { "Room_LampGlow", Glow("Room_LampGlow", cfg.lampColor, cfg.lampGlow) } };
            var tiled = data.exterior.Select(c => "Ext_" + c).Concat(new[] { "Room_Wall", "Room_Floor", "Room_Ceiling", "Room_Steel", "Crate", "Ext_Helipad" });
            mats["Helipad_Light"] = Glow("Helipad_Light", new Color(0.45f, 1f, 0.55f), 4f);
            foreach (var name in tiled)
            {
                if (Tex($"{name}_Albedo.png") == null && !File.Exists($"{ModelDir}/{name}_Albedo.png")) continue;
                bool isRoom = name.StartsWith("Room_");
                ImportTexture($"{name}_Albedo.png", true, false, 1024, 1024);
                ImportTexture($"{name}_Normal.png", false, true, 1024, 1024);
                ImportTexture($"{name}_MetalSmooth.png", false, false, 512, 512);
                mats[name] = Lit(name, $"{name}_Albedo.png", $"{name}_Normal.png", $"{name}_MetalSmooth.png", null,
                                 isRoom || name == "Crate" ? cfg.roomNormalScale : cfg.fortressNormalScale, 1f, Vector2.one);
            }
            if (data.exterior.Count == 0)
            {
                ImportTexture("Fortress_Albedo.png", true, false, 4096, cfg.androidMaxSize);
                ImportTexture("Fortress_Normal.png", false, true, 4096, cfg.androidMaxSize);
                ImportTexture("Fortress_MetalSmooth.png", false, false, 2048, Mathf.Min(1024, cfg.androidMaxSize));
                ImportTexture("Fortress_Occlusion.png", false, false, 2048, Mathf.Min(1024, cfg.androidMaxSize));
                mats["Fortress"] = Lit("Fortress", "Fortress_Albedo.png", "Fortress_Normal.png", "Fortress_MetalSmooth.png", "Fortress_Occlusion.png", cfg.fortressNormalScale, cfg.fortressOcclusion, Vector2.one);
            }
            BuildProps(cfg, mats, sb);

            // model import
            var imp = (ModelImporter)AssetImporter.GetAtPath(FbxPath);
            imp.globalScale = 1f; imp.useFileScale = true;
            imp.importAnimation = false; imp.animationType = ModelImporterAnimationType.None;
            imp.importBlendShapes = false; imp.importCameras = false; imp.importLights = false;
            imp.isReadable = true;                       // terrain + prop checks read the vertices
            imp.addCollider = false;
            imp.importNormals = ModelImporterNormals.Import;
            imp.meshCompression = ModelImporterMeshCompression.Off;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            foreach (var kv in mats) imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            imp.SaveAndReimport();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            var unmapped = model.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials)
                .Where(x => x == null || !mats.ContainsValue(x)).Select(x => x == null ? "null" : x.name).Distinct().ToList();
            sb.AppendLine($"Outside: {(data.exterior.Count > 0 ? "tiling surfaces " + string.Join(", ", data.exterior) : "Meshy atlas")}. " +
                          $"Materials: {string.Join(", ", mats.Keys)}." +
                          (unmapped.Count > 0 ? $" UNMAPPED: {string.Join(", ", unmapped)}" : " All FBX materials mapped."));

            // prefab
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = RootName;
            var fortress = go.transform.Find("Fortress");
            var room = go.transform.Find("Room");
            if (fortress == null || room == null) { Object.DestroyImmediate(go); return "FAILED: FBX has no Fortress / Room mesh.\n" + sb; }
            var deck = go.transform.Find("HelipadDeck");
            foreach (var t in new[] { fortress, room, deck }.Where(t => t != null))
            {
                var mc = t.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = t.GetComponent<MeshFilter>().sharedMesh;
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic);
            }
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                if (r.name.StartsWith("Lamp_")) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // helipad stairs: the steel flight is looks only; the walkable ramp beside it is a collider with no renderer
            var stairsMesh = go.transform.Find("HelipadStairs");
            if (stairsMesh != null)
                GameObjectUtility.SetStaticEditorFlags(stairsMesh.gameObject, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic);
            var ramp = go.transform.Find("HelipadStairsRamp");
            if (ramp != null)
            {
                var rc = ramp.gameObject.AddComponent<MeshCollider>();
                rc.sharedMesh = ramp.GetComponent<MeshFilter>().sharedMesh;
                Object.DestroyImmediate(ramp.GetComponent<MeshRenderer>());
                GameObjectUtility.SetStaticEditorFlags(ramp.gameObject, 0);
            }

            // door marker (BaseFurnishTools reads the first "door*" child to know the hall's entrance end)
            var main = data.doors.OrderByDescending(d => d.x).First();
            var door = new GameObject("door_main").transform;
            door.SetParent(room, false); door.localPosition = room.InverseTransformPoint(go.transform.TransformPoint(main));
            var back = new GameObject("Exit_Back").transform;
            back.SetParent(room, false); back.localPosition = room.InverseTransformPoint(go.transform.TransformPoint(data.doors.OrderBy(d => d.x).First()));

            // helipad markers (read by the intro cutscene): pad centre on its top surface, forward = the way a landed
            // helicopter's nose points (its tail strip is clear behind); the top of the way down from the deck
            if (data.hasHelipad)
            {
                var pad = new GameObject("Helipad").transform; pad.SetParent(go.transform, false);
                pad.localPosition = data.padCentre;
                pad.localRotation = Quaternion.LookRotation(new Vector3(data.padNose.x, 0f, data.padNose.z).normalized, Vector3.up);
                if (data.hasStairs) { var st = new GameObject("Helipad_Stairs").transform; st.SetParent(go.transform, false); st.localPosition = data.padStairs; }
                // walk path off the deck (Helipad_Path_0..3), each facing the next point
                for (int k = 0; k < data.stairsPath.Count; k++)
                {
                    var pt = new GameObject($"Helipad_Path_{k}").transform; pt.SetParent(go.transform, false);
                    pt.localPosition = data.stairsPath[k];
                    int last = data.stairsPath.Count - 1;
                    var dir = k < last ? data.stairsPath[k + 1] - data.stairsPath[k] : data.stairsPath[k] - data.stairsPath[Mathf.Max(0, k - 1)];
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 1e-4f) pt.localRotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                }
                sb.AppendLine($"Helipad markers: centre {data.padCentre}, nose {data.padNose}, stairs {(data.hasStairs ? data.padStairs.ToString() : "none")}, " +
                              $"path {data.stairsPath.Count} points, steel stairs {(stairsMesh != null ? "yes" : "NO")}, ramp collider {(ramp != null ? "yes" : "NO")}.");
            }

            // lights
            var lights = new GameObject("Lights").transform;
            lights.SetParent(go.transform, false);
            int i = 0, shadows = 0;
            foreach (var p in data.lamps)
            {
                var l = AddLight(lights, $"RoomLamp_{++i}", LightType.Point, p + Vector3.down * 0.3f, Quaternion.identity,
                                 cfg.lampColor, cfg.lampIntensity, cfg.lampRange, 0f);
                if (shadows < cfg.lampShadowCasters) { l.shadows = LightShadows.Soft; l.shadowStrength = 0.8f; shadows++; }
            }
            var flood = AssetDatabase.LoadAssetAtPath<GameObject>(FloodPrefab);
            int floods = 0, gatesLit = 0;
            foreach (var s in data.spots)
            {
                var aim = Quaternion.LookRotation(s.aim.normalized, Vector3.up);
                if (s.kind == "corner")
                {
                    if (cfg.floodFixtures && flood != null)
                    {
                        var f = (GameObject)PrefabUtility.InstantiatePrefab(flood, lights);
                        f.name = $"DeckFlood_{++floods}";
                        f.transform.localPosition = s.pos + Vector3.down * 2.2f;      // on the deck
                        var fl = f.GetComponentInChildren<Light>();
                        if (fl != null)
                        {
                            // turn the whole fixture so its lamp head looks along the aim (flattened)
                            Vector3 have = Vector3.ProjectOnPlane(fl.transform.forward, Vector3.up);
                            Vector3 want = Vector3.ProjectOnPlane(go.transform.TransformDirection(s.aim), Vector3.up);
                            if (have.sqrMagnitude > 1e-4f && want.sqrMagnitude > 1e-4f)
                                f.transform.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(have, want, Vector3.up), Vector3.up) * f.transform.rotation;
                            fl.color = cfg.floodColor; fl.intensity = cfg.floodIntensity; fl.range = cfg.floodRange; fl.spotAngle = cfg.floodAngle;
                            fl.shadows = LightShadows.None;
                        }
                    }
                    else
                    {
                        AddLight(lights, $"DeckFlood_{++floods}", LightType.Spot, s.pos, aim, cfg.floodColor, cfg.floodIntensity, cfg.floodRange, cfg.floodAngle);
                    }
                }
                else
                {
                    bool outside = s.kind == "gate_out";
                    if (!outside) aim = Quaternion.LookRotation(new Vector3(s.aim.x, -cfg.gateInsideDrop, s.aim.z).normalized, Vector3.up);
                    AddLight(lights, $"Gate_{(outside ? "Out" : "In")}_{++gatesLit}", LightType.Spot, s.pos, aim,
                             outside ? cfg.gateOutsideColor : cfg.gateInsideColor, outside ? cfg.gateOutsideIntensity : cfg.gateInsideIntensity,
                             outside ? cfg.gateOutsideRange : cfg.gateInsideRange, cfg.gateAngle);
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            sb.AppendLine($"Prefab {PrefabPath}: Fortress + Room mesh colliders, {data.lamps.Count} room lamps ({shadows} with shadows), " +
                          $"{gatesLit} gate lights, {floods} deck floodlights{(cfg.floodFixtures && flood != null ? " (ZR_Floodlight fixtures)" : "")}, door marker.");
            return sb.ToString();
        }

        /// Crate stack + wall lamp prefabs from Blender/build_fortress_props.py.
        static void BuildProps(FortressBaseConfig cfg, Dictionary<string, Material> mats, StringBuilder sb)
        {
            if (!(AssetImporter.GetAtPath(PropsFbx) is ModelImporter imp)) { sb.AppendLine("No " + PropsFbx + " (run Blender/build_fortress_props.py --write): furniture swap skipped."); return; }
            imp.importAnimation = false; imp.animationType = ModelImporterAnimationType.None;
            imp.importCameras = false; imp.importLights = false; imp.addCollider = false;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            foreach (var kv in mats) imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            imp.SaveAndReimport();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(PropsFbx);
            Directory.CreateDirectory(Path.GetDirectoryName(CratePrefab));

            GameObject Part(string child, string rootName)
            {
                var src = model.transform.Find(child);
                if (src == null) return null;
                var root = new GameObject(rootName);
                var g = new GameObject(child);
                g.transform.SetParent(root.transform, false);
                g.transform.localPosition = src.localPosition; g.transform.localRotation = src.localRotation; g.transform.localScale = src.localScale;
                g.AddComponent<MeshFilter>().sharedMesh = src.GetComponent<MeshFilter>().sharedMesh;
                g.AddComponent<MeshRenderer>().sharedMaterials = src.GetComponent<MeshRenderer>().sharedMaterials;
                return root;
            }

            var stack = Part("CrateStack", "CrateStack");
            if (stack != null)
            {
                var b = stack.GetComponentInChildren<MeshRenderer>().bounds;
                var box = stack.AddComponent<BoxCollider>(); box.center = b.center; box.size = b.size;
                GameObjectUtility.SetStaticEditorFlags(stack.transform.GetChild(0).gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
                PrefabUtility.SaveAsPrefabAsset(stack, CratePrefab);
                sb.AppendLine($"Prefab {CratePrefab}: {b.size.x:0.00} x {b.size.y:0.00} x {b.size.z:0.00} m, top {b.max.y:0.00} m.");
                Object.DestroyImmediate(stack);
            }
            var lamp = Part("WallLamp", "WallLamp");
            if (lamp != null)
            {
                var lr = lamp.GetComponentInChildren<MeshRenderer>();
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var glass = Glow("WallLamp_Glass", cfg.wallLampColor, cfg.wallLampGlow);   // softer than the ceiling lamps
                lr.sharedMaterials = lr.sharedMaterials.Select(x => x != null && x.name == "Room_LampGlow" ? glass : x).ToArray();
                lamp.transform.GetChild(0).localScale *= cfg.wallLampScale;
                AddLight(lamp.transform, "Light", LightType.Point, new Vector3(0f, 0f, 0.3f * cfg.wallLampScale), Quaternion.identity,
                         cfg.wallLampColor, cfg.wallLampIntensity, cfg.wallLampRange, 0f);
                PrefabUtility.SaveAsPrefabAsset(lamp, WallLampPrefab);
                sb.AppendLine($"Prefab {WallLampPrefab}: caged wall lamp, point light {cfg.wallLampIntensity} / {cfg.wallLampRange} m.");
                Object.DestroyImmediate(lamp);
            }
        }

        /// After BaseFurnishTools: benches become crate stacks (the guns and ammo boxes move onto them, same names so
        /// the pickups, the baselook test and the look presets keep working) and lantern posts become wall lamps.
        static string SwapFurniture(SafeBase safe, FortressBaseConfig cfg)
        {
            var furn = safe.transform.Find("BaseFurniture");
            if (furn == null) return "No BaseFurniture to swap.\n";
            var crate = AssetDatabase.LoadAssetAtPath<GameObject>(CratePrefab);
            var lamp = AssetDatabase.LoadAssetAtPath<GameObject>(WallLampPrefab);
            int stacks = 0, lamps = 0, missed = 0;
            if (cfg.crateStacks && crate != null)
                foreach (var bench in furn.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Bench_")).ToList())
                {
                    var holder = new GameObject(bench.name).transform;
                    Undo.RegisterCreatedObjectUndo(holder.gameObject, "Fortress base");
                    holder.SetParent(bench.parent, false);
                    holder.SetPositionAndRotation(bench.position, bench.rotation);
                    var c = (GameObject)PrefabUtility.InstantiatePrefab(crate, holder);
                    c.transform.localPosition = Vector3.zero; c.transform.localRotation = Quaternion.identity;
                    foreach (Transform child in bench.Cast<Transform>().ToList())
                        if (PrefabUtility.IsAddedGameObjectOverride(child.gameObject) || child.GetComponent<BasePickup>() != null || child.name.StartsWith("Ammo"))
                            Undo.SetTransformParent(child, holder, "Fortress base");
                    Undo.DestroyObjectImmediate(bench.gameObject);
                    stacks++;
                }
            if (cfg.wallLamps && lamp != null)
                foreach (var post in furn.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Lantern_")).ToList())
                {
                    // the post stands just off a wall, facing into the room: find that wall behind it at lamp height
                    Vector3 back = -Vector3.ProjectOnPlane(post.forward, Vector3.up).normalized;
                    Vector3 from = post.position + Vector3.up * cfg.wallLampHeight - back * 0.2f;
                    if (!Physics.Raycast(from, back, out var hit, 2.5f, ~0, QueryTriggerInteraction.Ignore)) { missed++; continue; }
                    var l = (GameObject)PrefabUtility.InstantiatePrefab(lamp, post.parent);
                    Undo.RegisterCreatedObjectUndo(l, "Fortress base");
                    l.name = post.name;
                    l.transform.SetPositionAndRotation(hit.point + hit.normal * 0.005f, Quaternion.LookRotation(hit.normal, Vector3.up));
                    Undo.DestroyObjectImmediate(post.gameObject);
                    lamps++;
                }
            return $"Furniture: {stacks} benches swapped for steel crate stacks (guns and ammo on top), {lamps} lantern posts for caged wall lamps" +
                   (missed > 0 ? $", {missed} lanterns kept (no wall behind them)" : "") + ".\n";
        }

        static Light AddLight(Transform parent, string name, LightType type, Vector3 localPos, Quaternion localRot, Color color, float intensity, float range, float angle)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos; g.transform.localRotation = localRot;
            var l = g.AddComponent<Light>();
            l.type = type; l.color = color; l.intensity = intensity; l.range = range;
            if (type == LightType.Spot) { l.spotAngle = angle; l.innerSpotAngle = angle * 0.55f; }
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.ForcePixel;
            l.lightmapBakeType = LightmapBakeType.Realtime;
            return l;
        }

        // ------------------------------------------------------------------ scene

        public static string Build()
        {
            var sb = new StringBuilder();
            var cfg = Config();
            var data = LoadData();
            var safe = Object.FindAnyObjectByType<SafeBase>(FindObjectsInactive.Include);
            if (safe == null) return "FAILED: open MainLevel (no SafeBase).";
            var scene = safe.gameObject.scene;
            var map = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Map");
            if (map == null) return "FAILED: no Map in the scene.";

            string assets = BuildAssets(sb);
            if (assets.StartsWith("FAILED")) return assets;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            // backup once
            if (!Directory.Exists(BackupDir))
            {
                Directory.CreateDirectory(BackupDir);
                if (scene.isDirty) EditorSceneManager.SaveScene(scene);
                File.Copy(scene.path, $"{BackupDir}/MainLevel.unity", true);
                if (File.Exists(TerrainCopyPath)) File.Copy(TerrainCopyPath, $"{BackupDir}/MainLevel_Terrain.asset", true);
                sb.AppendLine($"Backup: {BackupDir}/MainLevel.unity + MainLevel_Terrain.asset.");
            }

            // the old hall: its rotation and floor give the fortress its place
            var hall = map.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == OldHall && t.GetComponent<MeshFilter>() != null);
            if (hall == null) return "FAILED: no '" + OldHall + "' under Map.\n" + sb;
            var baseT = safe.transform;
            Vector3 centre = safe.Center; float floorY = baseT.position.y + cfg.floorOffset;
            var rot = Quaternion.Euler(0f, hall.eulerAngles.y + cfg.yawOffset, 0f);

            var old = map.transform.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            if (hall.gameObject.activeSelf) { Undo.RecordObject(hall.gameObject, "Fortress base"); hall.gameObject.SetActive(false); }

            var fort = (GameObject)PrefabUtility.InstantiatePrefab(prefab, map.transform);
            Undo.RegisterCreatedObjectUndo(fort, "Fortress base");
            Vector3 anchor = cfg.centreOnRoom ? data.RoomCentre : Vector3.zero;
            Vector3 pos = new Vector3(centre.x, floorY, centre.z) - rot * anchor;
            pos.y = floorY;
            fort.transform.SetPositionAndRotation(pos, rot);
            Physics.SyncTransforms();
            var room = fort.transform.Find("Room");
            var fortMesh = fort.transform.Find("Fortress").GetComponent<MeshFilter>().sharedMesh;
            float reach = fortMesh.vertices.Max(v => new Vector2(v.x, v.z).magnitude);
            var fortColliders = new HashSet<Collider>(fort.GetComponentsInChildren<Collider>());
            sb.AppendLine($"Hall '{OldHall}' hidden (kept, inactive). Fortress at ({pos.x:0.0}, {pos.y:0.00}, {pos.z:0.0}), turn {rot.eulerAngles.y:0}°, " +
                          $"footprint radius {reach:0.0} m from its middle; room centre on the base centre {(cfg.centreOnRoom ? "yes" : "no")}; " +
                          $"safe circle {safe.radius:0.0} m.");

            // ground: lower what pokes up, then lift the fortress if the ground is still at or over the yard floor
            if (cfg.flattenTerrain) sb.Append(LowerTerrain(map, fort.transform, reach, floorY - cfg.groundBelowFloor, cfg, safe));
            float groundTop = GroundTop(map, fort.transform, reach);
            if (groundTop > fort.transform.position.y - cfg.groundBelowFloor)
            {
                float lift = groundTop + cfg.groundBelowFloor - fort.transform.position.y;
                fort.transform.position += Vector3.up * lift;
                Physics.SyncTransforms();
                sb.AppendLine($"Ground under the yard tops out at {groundTop:0.00}: fortress lifted {lift:0.00} m so the yard floor stays {cfg.groundBelowFloor:0.00} m above it.");
            }
            else sb.AppendLine($"Ground under the yard tops out at {groundTop:0.00}, yard floor {fort.transform.position.y:0.00}.");

            // props standing inside the fortress or its room
            if (cfg.hideOverlappingProps) sb.Append(HideInside(map, safe, fort.transform, hall, fortColliders, data, reach));

            // furnish the room: point the base at the room while BaseFurnishTools measures it, then at the fortress
            Undo.RecordObject(safe, "Fortress base");
            safe.building = room;
            string furnish;
            try { furnish = BaseFurnishTools.Build(); }
            finally { safe.building = fort.transform; EditorUtility.SetDirty(safe); }
            sb.AppendLine("Furnish: " + furnish.Trim().Replace("\n", "\n  "));
            Physics.SyncTransforms();
            sb.Append(SwapFurniture(safe, cfg));

            // the medkit: the furnish code puts it by the far end wall on the room's centre line, which here is the
            // back door, so it lands out in the yard; set it inside, beside the back door
            var gear = baseT.Find("BaseGear");
            if (gear != null)
                foreach (Transform g in gear)
                {
                    if (!g.name.Contains("MEDKIT")) continue;
                    Undo.RecordObject(g, "Fortress base");
                    var local = new Vector3(data.roomMin.x + 2.2f, 0f, data.RoomCentre.z + cfg.medkitSideOffset);
                    var p = fort.transform.TransformPoint(local);
                    if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
                    g.position = p;
                    sb.AppendLine($"Medkit pickup set inside the room by the back door ({p.x:0.0}, {p.y:0.00}, {p.z:0.0}).");
                }

            // player start: middle of the room, facing the main door
            if (safe.playerStart != null)
            {
                Undo.RecordObject(safe.playerStart, "Fortress base");
                Vector3 rc = fort.transform.TransformPoint(data.RoomCentre) + Vector3.up * 0.05f;
                Vector3 doorW = room.Find("door_main").position;
                safe.playerStart.SetPositionAndRotation(rc, Quaternion.LookRotation(Vector3.ProjectOnPlane(doorW - rc, Vector3.up).normalized, Vector3.up));
                sb.AppendLine($"Player start: room centre ({rc.x:0.0}, {rc.y:0.00}, {rc.z:0.0}), facing the main door.");
            }
            else sb.AppendLine("No SafeBase.playerStart set: players deploy at a clear spot near the base centre.");

            // zombie walk map
            var zone = Object.FindAnyObjectByType<ZoneManager>(FindObjectsInactive.Include);
            if (zone != null)
            {
                var nav = ZoneTools.BakeNavMesh(map, baseT.position, zone.maxRadius + 15f, out string navNote);
                if (nav != null)
                {
                    var loader = zone.GetComponent<ArenaNavMesh>();
                    if (loader == null) loader = Undo.AddComponent<ArenaNavMesh>(zone.gameObject);
                    Undo.RecordObject(loader, "Fortress base");
                    loader.data = nav;
                    EditorUtility.SetDirty(loader);
                    // check: no walkable spot on the fortress above its yard (decks, stairs, roof)
                    var inst = UnityEngine.AI.NavMesh.AddNavMeshData(nav);
                    var tri = UnityEngine.AI.NavMesh.CalculateTriangulation();
                    UnityEngine.AI.NavMesh.RemoveNavMeshData(inst);
                    var fp = fort.transform.position;
                    var high = tri.vertices.Where(v => v.y > fp.y + 1.0f && new Vector2(v.x - fp.x, v.z - fp.z).magnitude < reach).ToList();
                    int onFort = 0; var others = new List<string>();
                    foreach (var v in high)
                    {
                        string under = Physics.Raycast(v + Vector3.up * 0.5f, Vector3.down, out var h, 2f, ~0, QueryTriggerInteraction.Ignore) ? h.collider.name : "nothing";
                        if (Physics.Raycast(v + Vector3.up * 0.5f, Vector3.down, out var h2, 2f, ~0, QueryTriggerInteraction.Ignore) && h2.transform.IsChildOf(fort.transform)) onFort++;
                        else others.Add($"{under} at {v.y - fp.y:0.0} m");
                    }
                    sb.AppendLine($"Walk map check: {onFort} walkable points on the fortress itself (should be 0); " +
                                  $"{others.Count} other raised points inside its radius{(others.Count > 0 ? ": " + string.Join(", ", others.Distinct()) : "")}.");
                }
                sb.AppendLine("Walk map: " + navNote.Trim());
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            sb.AppendLine("Scene saved.");
            return sb.ToString();
        }

        /// Lowers terrain that pokes above the yard floor inside the footprint (never raises it), blending outward,
        /// but stops short of the compound's ring wall. Also clears grass and trees inside.
        static string LowerTerrain(GameObject map, Transform fort, float reach, float target, FortressBaseConfig cfg, SafeBase safe)
        {
            Vector3 c = fort.position;
            var terrain = map.GetComponentsInChildren<Terrain>().FirstOrDefault(tr =>
                new Rect(tr.transform.position.x, tr.transform.position.z, tr.terrainData.size.x, tr.terrainData.size.z).Contains(new Vector2(c.x, c.z)));
            if (terrain == null) return "No terrain under the fortress.\n";
            var data = terrain.terrainData;
            if (AssetDatabase.GetAssetPath(data) != TerrainCopyPath) return $"Terrain data is not {TerrainCopyPath}; ground left alone.\n";
            Undo.RegisterCompleteObjectUndo(data, "Fortress base");

            float inner = reach + cfg.flatMargin;
            float wallR = safe.radius - 2.5f - Vector3.ProjectOnPlane(c - safe.Center, Vector3.up).magnitude;   // keep off the ring wall
            float blend = Mathf.Max(0f, Mathf.Min(cfg.blend, wallR - inner));
            var tp = terrain.transform.position; var size = data.size; int res = data.heightmapResolution;
            float outer = inner + blend;
            int x0 = Mathf.Clamp(Mathf.FloorToInt((c.x - outer - tp.x) / size.x * (res - 1)), 0, res - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((c.x + outer - tp.x) / size.x * (res - 1)), 0, res - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((c.z - outer - tp.z) / size.z * (res - 1)), 0, res - 1);
            int z1 = Mathf.Clamp(Mathf.CeilToInt((c.z + outer - tp.z) / size.z * (res - 1)), 0, res - 1);
            int w = x1 - x0 + 1, h = z1 - z0 + 1;
            var heights = data.GetHeights(x0, z0, w, h);
            float targetN = Mathf.Clamp01((target - tp.y) / size.y);
            float maxCut = 0f, minBelow = float.MaxValue, maxBelow = 0f; int cut = 0;
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    var world = new Vector3(tp.x + (x0 + x) / (float)(res - 1) * size.x, 0f, tp.z + (z0 + z) / (float)(res - 1) * size.z);
                    float d = new Vector2(world.x - c.x, world.z - c.z).magnitude;
                    if (d > outer) continue;
                    float k = d <= inner ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (d - inner) / Mathf.Max(0.01f, blend));
                    float hv = heights[z, x];
                    if (d <= reach)
                    {
                        float below = (targetN - hv) * size.y;
                        if (below > 0f) { minBelow = Mathf.Min(minBelow, below); maxBelow = Mathf.Max(maxBelow, below); }
                    }
                    if (hv <= targetN || k <= 0f) continue;
                    float nv = Mathf.Lerp(hv, targetN, k);
                    maxCut = Mathf.Max(maxCut, (hv - nv) * size.y); cut++;
                    heights[z, x] = nv;
                }
            data.SetHeights(x0, z0, heights);
            data.SyncHeightmap();

            int removedGrass = 0;
            int dres = data.detailResolution;
            if (dres > 0 && data.detailPrototypes.Length > 0)
            {
                int dx0 = Mathf.Clamp(Mathf.FloorToInt((c.x - reach - tp.x) / size.x * dres), 0, dres - 1);
                int dx1 = Mathf.Clamp(Mathf.CeilToInt((c.x + reach - tp.x) / size.x * dres), 0, dres - 1);
                int dz0 = Mathf.Clamp(Mathf.FloorToInt((c.z - reach - tp.z) / size.z * dres), 0, dres - 1);
                int dz1 = Mathf.Clamp(Mathf.CeilToInt((c.z + reach - tp.z) / size.z * dres), 0, dres - 1);
                for (int layer = 0; layer < data.detailPrototypes.Length; layer++)
                {
                    var dl = data.GetDetailLayer(dx0, dz0, dx1 - dx0 + 1, dz1 - dz0 + 1, layer);
                    for (int z = 0; z <= dz1 - dz0; z++)
                        for (int x = 0; x <= dx1 - dx0; x++)
                        {
                            var world = new Vector3(tp.x + (dx0 + x + 0.5f) / dres * size.x, 0f, tp.z + (dz0 + z + 0.5f) / dres * size.z);
                            if (new Vector2(world.x - c.x, world.z - c.z).magnitude <= reach && dl[z, x] > 0) { removedGrass += dl[z, x]; dl[z, x] = 0; }
                        }
                    data.SetDetailLayer(dx0, dz0, layer, dl);
                }
            }
            var trees = data.treeInstances;
            var kept = trees.Where(t => new Vector2(t.position.x * size.x + tp.x - c.x, t.position.z * size.z + tp.z - c.z).magnitude > reach + 0.5f).ToArray();
            if (kept.Length != trees.Length) data.treeInstances = kept;
            EditorUtility.SetDirty(data);
            Physics.SyncTransforms();
            return $"Ground: {cut} height samples lowered (most {maxCut:0.00} m) to {cfg.groundBelowFloor:0.00} m under the yard floor inside {inner:0.0} m, " +
                   $"blend {blend:0.0} m; ground lower than the floor inside the footprint: " +
                   (maxBelow > 0f ? $"{minBelow:0.00}..{maxBelow:0.00} m (the 1.1 m buried slab covers up to that)" : "none") +
                   $"; {removedGrass} grass, {trees.Length - kept.Length} trees removed.\n";
        }

        /// Highest terrain point inside the fortress footprint (circle of `reach` round its middle).
        static float GroundTop(GameObject map, Transform fort, float reach)
        {
            float top = float.NegativeInfinity;
            foreach (var t in map.GetComponentsInChildren<Terrain>())
                for (float x = -reach; x <= reach; x += 1f)
                    for (float z = -reach; z <= reach; z += 1f)
                    {
                        if (x * x + z * z > reach * reach) continue;
                        var p = fort.position + new Vector3(x, 0f, z);
                        var tp = t.transform.position; var size = t.terrainData.size;
                        if (p.x < tp.x || p.z < tp.z || p.x > tp.x + size.x || p.z > tp.z + size.z) continue;
                        top = Mathf.Max(top, t.SampleHeight(p) + tp.y);
                    }
            return top;
        }

        /// Hides (undoably) props that stand inside the fortress: touching its walls or inside the room.
        static string HideInside(GameObject map, SafeBase safe, Transform fort, Transform hall, HashSet<Collider> fortColliders, FortData data, float reach)
        {
            var roomBox = new Bounds((data.roomMin + data.roomMax) * 0.5f, data.roomMax - data.roomMin);
            var keepUnder = new[] { fort, hall, safe.transform.Find("BaseFurniture"), safe.transform.Find("BaseGear") }.Where(t => t != null).ToArray();
            var done = new HashSet<GameObject>();
            var hidden = new List<string>();
            var candidates = map.GetComponentsInChildren<Renderer>().Concat(safe.GetComponentsInChildren<Renderer>());
            foreach (var r in candidates)
            {
                if (r is ParticleSystemRenderer) continue;
                if (keepUnder.Any(k => r.transform.IsChildOf(k))) continue;
                if (r.GetComponentInParent<Terrain>() != null) continue;
                var root = PrefabUtility.GetOutermostPrefabInstanceRoot(r.gameObject);
                var go = root != null && root != map ? root : r.gameObject;
                if (go == map || go == safe.gameObject || !done.Add(go) || !go.activeInHierarchy) continue;
                if (keepUnder.Any(k => k.IsChildOf(go.transform))) continue;     // a container holding the fortress
                var rs = go.GetComponentsInChildren<Renderer>();
                var b = rs[0].bounds; foreach (var x in rs) b.Encapsulate(x.bounds);
                if (b.size.y < 0.3f || Mathf.Max(b.size.x, b.size.z) > 20f) continue;
                if (new Vector2(b.center.x - fort.position.x, b.center.z - fort.position.z).magnitude > reach + 2f) continue;
                bool inRoom = roomBox.Contains(fort.InverseTransformPoint(b.center));
                bool touches = Physics.OverlapBox(b.center, b.extents * 0.9f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Any(c => fortColliders.Contains(c));
                if (!inRoom && !touches) continue;
                Undo.RecordObject(go, "Fortress base");
                go.SetActive(false);
                hidden.Add(go.name + (inRoom ? " (in room)" : ""));
            }
            return $"Props inside the fortress hidden: {hidden.Count}{(hidden.Count > 0 ? " -> " + string.Join(", ", hidden.GroupBy(n => n).Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key)) : "")}.\n";
        }

        /// Read-only: what stands round the fortress (outside it, inside the safe circle): path, distance, local
        /// position (fortress axes), and whether it glows or is see-through.
        public static string Survey()
        {
            var safe = Object.FindAnyObjectByType<SafeBase>(FindObjectsInactive.Include);
            var map = safe != null ? safe.gameObject.scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Map") : null;
            var fort = map != null ? map.transform.Find(RootName) : null;
            if (fort == null) return "FAILED: no FortressHaven under Map.";
            var sb = new StringBuilder($"Around the fortress (within {safe.radius:0} m of the base centre, outside the fortress):\n");
            var seen = new HashSet<GameObject>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r.transform.IsChildOf(fort) || r.GetComponentInParent<Terrain>() != null) continue;
                var root = PrefabUtility.GetOutermostPrefabInstanceRoot(r.gameObject);
                var go = root != null ? root : r.gameObject;
                if (!seen.Add(go)) continue;
                var d = Vector3.ProjectOnPlane(r.bounds.center - safe.Center, Vector3.up).magnitude;
                if (d > safe.radius) continue;
                bool glow = r.sharedMaterials.Any(m => m != null && (m.renderQueue > 2500 || m.IsKeywordEnabled("_EMISSION")));
                var l = fort.InverseTransformPoint(r.bounds.center);
                sb.AppendLine($"  {PathOf(go.transform)}  d {d:0.0}  local ({l.x:0.0}, {l.y:0.0}, {l.z:0.0})  size {r.bounds.size.x:0.0}x{r.bounds.size.y:0.0}x{r.bounds.size.z:0.0}{(glow ? "  GLOW/TRANSPARENT" : "")}");
            }
            return sb.ToString();
        }

        static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

        // ------------------------------------------------------------------ pictures

        /// Edit-mode pictures in the scene's own night lighting (no helper light): Docs/Look/Fortress_<tag>_<view>.png.
        /// "views <tag>". Views: street, gate, high, back, yard, room_door, room_in, room_far.
        public static string Views(string tag)
        {
            if (string.IsNullOrEmpty(tag)) tag = "Now";
            var safe = Object.FindAnyObjectByType<SafeBase>(FindObjectsInactive.Include);
            var map = safe != null ? safe.gameObject.scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Map") : null;
            var fort = map != null ? map.transform.Find(RootName) : null;
            var data = LoadData();
            if (fort == null || data == null) return "FAILED: no FortressHaven under Map (run the build first).";
            var main = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();
            const string outDir = "Docs/Look";
            Directory.CreateDirectory(outDir);
            var camGo = new GameObject("_FortCam") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            var shots = new List<string>();
            Vector3 W(Vector3 local) => fort.TransformPoint(local);
            Vector3 gate = data.gates.OrderByDescending(g => g.x).First();
            Vector3 door = data.doors.OrderByDescending(d => d.x).First();
            Vector3 rc = data.RoomCentre;
            var views = new (string name, Vector3 eye, Vector3 at, float fov)[]
            {
                ("street", new Vector3(gate.x + 40f, 1.7f, gate.z + 8f), new Vector3(0f, 6f, 0f), 60f),
                ("gate", new Vector3(gate.x + 9f, 1.7f, gate.z - 3f), new Vector3(gate.x, 2.2f, gate.z), 70f),
                ("high", new Vector3(32f, 20f, -26f), new Vector3(0f, 4f, 0f), 60f),
                ("back", new Vector3(-30f, 16f, 28f), new Vector3(0f, 5f, 0f), 60f),
                ("yard", new Vector3(gate.x - 1.5f, 1.7f, gate.z + 1f), new Vector3(door.x, 2f, door.z), 75f),
                ("room_door", new Vector3(door.x + 2.5f, 1.7f, door.z), new Vector3(rc.x - 6f, 1.6f, rc.z), 75f),
                ("room_in", new Vector3(door.x - 2f, 1.7f, door.z - 1.5f), new Vector3(data.roomMin.x + 2f, 1.2f, rc.z + 2f), 80f),
                ("room_far", new Vector3(data.roomMin.x + 2f, 1.7f, data.roomMax.z - 2f), new Vector3(data.roomMax.x - 3f, 1.0f, data.roomMin.z + 3f), 80f),
            };
            try
            {
                var cam = camGo.AddComponent<Camera>(); cam.CopyFrom(main); cam.targetTexture = rt; cam.farClipPlane = 400f;
                foreach (var v in views)
                {
                    Vector3 eye = W(v.eye), at = W(v.at);
                    cam.fieldOfView = v.fov;
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye, Vector3.up));
                    string path = $"{outDir}/Fortress_{tag}_{v.name}.png";
                    Shot(cam, rt, path); shots.Add(v.name);
                }
            }
            finally { Object.DestroyImmediate(camGo); rt.Release(); Object.DestroyImmediate(rt); }
            return $"Fortress views {tag}: {string.Join(", ", shots)} in {outDir}";
        }

        /// "camera": where the Scene view camera is, in fortress-local Unity and Blender metres (to find a spot the
        /// user is looking at in fortress_base.py), plus a picture from it: Docs/Look/Fortress_SceneCam.png.
        public static string SceneCamera()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null || sv.camera == null) return "FAILED: no Scene view open.";
            var safe = Object.FindAnyObjectByType<SafeBase>(FindObjectsInactive.Include);
            var map = safe != null ? safe.gameObject.scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Map") : null;
            var fort = map != null ? map.transform.Find(RootName) : null;
            if (fort == null) return "FAILED: no FortressHaven under Map.";
            // the Scene view camera transform is only live while that window draws; pivot + rotation always hold the view
            Vector3 fw = sv.rotation * Vector3.forward;
            Vector3 eye = sv.pivot - fw * sv.cameraDistance;
            sv.camera.transform.SetPositionAndRotation(eye, sv.rotation);
            var t = sv.camera.transform;
            Vector3 p = fort.InverseTransformPoint(eye), f = fort.InverseTransformDirection(fw);
            // Unity local (x, y, z) -> Blender (-x, -z, y); see fortress_base.unity_point
            string B(Vector3 v) => $"({-v.x:F2}, {-v.z:F2}, {v.y:F2})";
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            var camGo = new GameObject("_FortCam") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var main = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();
                var cam = camGo.AddComponent<Camera>(); cam.CopyFrom(main); cam.targetTexture = rt; cam.farClipPlane = 400f;
                cam.fieldOfView = sv.camera.fieldOfView; cam.nearClipPlane = 0.05f;
                cam.transform.SetPositionAndRotation(eye, sv.rotation);
                Directory.CreateDirectory("Docs/Look");
                Shot(cam, rt, "Docs/Look/Fortress_SceneCam.png");
            }
            finally { Object.DestroyImmediate(camGo); rt.Release(); Object.DestroyImmediate(rt); }
            // what sits in the middle of the view: the first fortress collider along the view line
            string hit = "nothing on the fortress in the middle of the view";
            var hits = Physics.RaycastAll(eye, fw, 200f, ~0, QueryTriggerInteraction.Ignore)
                .Where(h => h.collider.transform.IsChildOf(fort)).OrderBy(h => h.distance).ToList();
            if (hits.Count > 0)
            {
                var hl = fort.InverseTransformPoint(hits[0].point);
                hit = $"middle of the view hits {hits[0].collider.name} at {hits[0].distance:F1} m: fortress-local {hl:F2}, Blender {B(hl)}";
            }
            return $"Scene camera: world {eye}, fortress-local {p:F2} looking {f:F2}, fov {sv.camera.fieldOfView:F0}; " +
                   $"Blender position {B(p)} looking {B(f)}. {hit}. Picture Docs/Look/Fortress_SceneCam.png";
        }

        static void Shot(Camera cam, RenderTexture rt, string path)
        {
            var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
            if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, req)) UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req);
            else cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        // ------------------------------------------------------------------ tiny JSON reader (numbers, strings, lists, objects)

        static class MiniJson
        {
            public static object Parse(string s) { int i = 0; return Value(s, ref i); }

            static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

            static object Value(string s, ref int i)
            {
                Ws(s, ref i);
                char c = s[i];
                if (c == '{')
                {
                    var d = new Dictionary<string, object>(); i++;
                    while (true)
                    {
                        Ws(s, ref i); if (s[i] == '}') { i++; return d; }
                        var key = (string)Value(s, ref i); Ws(s, ref i); i++;   // ':'
                        d[key] = Value(s, ref i); Ws(s, ref i);
                        if (s[i] == ',') i++;
                    }
                }
                if (c == '[')
                {
                    var l = new List<object>(); i++;
                    while (true)
                    {
                        Ws(s, ref i); if (s[i] == ']') { i++; return l; }
                        l.Add(Value(s, ref i)); Ws(s, ref i);
                        if (s[i] == ',') i++;
                    }
                }
                if (c == '"')
                {
                    var sb = new StringBuilder(); i++;
                    while (s[i] != '"') { if (s[i] == '\\') i++; sb.Append(s[i++]); }
                    i++; return sb.ToString();
                }
                int st = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                if (i == st) { if (s.Substring(i).StartsWith("true")) { i += 4; return true; } if (s.Substring(i).StartsWith("false")) { i += 5; return false; } i += 4; return null; }
                return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
            }
        }
    }
}
