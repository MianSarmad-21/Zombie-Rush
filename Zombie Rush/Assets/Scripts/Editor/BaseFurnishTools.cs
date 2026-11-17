using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZombieRush.Data;
using ZombieRush.Player;
using ZombieRush.Weapons;
using ZombieRush.World;

namespace ZombieRush.EditorTools
{
    /// The safe base's furniture (2026-11-15): gun tables with the AK and shotgun, armour vests, ammo boxes, and
    /// flame lanterns around the base building.
    ///   - models: Blender/build_base_props.py -> Assets/Models/2026-11-15_BaseProps (materials = the city props' ZR_*);
    ///   - prefabs: Assets/Prefabs/Base (table, lantern post with particle flame + flickering light, vest, ammo boxes,
    ///     static "table guns" baked from the gun rigs);
    ///   - each gun's WeaponData gets its ammo box (zombie drops use it too) and pick-up amounts;
    ///   - the player starts with the pistol only (the AK and shotgun come from the tables).
    /// Marker: Temp/ZR_RunMapCleanup with "basefurnish" (build + place, saves the scene) or "basesurvey" (read-only).
    public static class BaseFurnishTools
    {
        const string ModelDir = "Assets/Models/2026-11-15_BaseProps";
        const string PrefabDir = "Assets/Prefabs/Base";
        const string RootName = "BaseFurniture";
        const float BenchTop = 0.92f;

        // ------------------------------------------------------------------ build + place

        public static string Build()
        {
            var sb = new StringBuilder();
            if (!Directory.Exists(ModelDir)) return "FAILED: run Blender/build_base_props.py first (no " + ModelDir + ").";
            var safe = Object.FindAnyObjectByType<SafeBase>(FindObjectsInactive.Include);
            if (safe == null || safe.building == null) return "FAILED: no SafeBase with a building (open MainLevel).";
            var pwc = Object.FindObjectsByType<PlayerWeaponController>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(w => !w.remote);
            if (pwc == null) return "FAILED: no player weapon controller in the scene.";
            WeaponBase Gun(string n) => pwc.weapons.FirstOrDefault(w => w != null && w.name == n);
            var pistol = Gun("Pistol"); var rifle = Gun("Rifle"); var shotgun = Gun("Shotgun");
            if (pistol == null || rifle == null || shotgun == null) return "FAILED: the player needs Pistol, Rifle and Shotgun.";
            Directory.CreateDirectory(PrefabDir);
            Directory.CreateDirectory(PrefabDir + "/Meshes");

            // ---- prefabs
            var bench = BenchPrefab(sb);
            var lantern = LanternPrefab(sb);
            var ammo = new Dictionary<WeaponData, GameObject>
            {
                { pistol.data, AmmoPrefab("ZR_Ammo_9mm", pistol.data, sb) },
                { shotgun.data, AmmoPrefab("ZR_Ammo_Shotgun", shotgun.data, sb) },
                { rifle.data, AmmoPrefab("ZR_Ammo_Rifle", rifle.data, sb) },
            };
            var rifleShow = TableGunPrefab("Rifle", sb);
            var shotgunShow = TableGunPrefab("Shotgun", sb);
            // the table-gun prefabs point at freshly baked mesh assets: re-import them or the editor keeps a copy
            // with empty meshes (the guns were invisible on the benches)
            AssetDatabase.SaveAssets();
            foreach (var gname in new[] { "TableGun_Rifle", "TableGun_Shotgun" })
                AssetDatabase.ImportAsset($"{PrefabDir}/{gname}.prefab", ImportAssetOptions.ForceUpdate);
            rifleShow = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/TableGun_Rifle.prefab");
            shotgunShow = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/TableGun_Shotgun.prefab");
            if (bench == null || lantern == null || rifleShow == null || shotgunShow == null || ammo.Values.Any(a => a == null))
                return "FAILED building prefabs:\n" + sb;
            AssetDatabase.DeleteAsset(PrefabDir + "/GunTable.prefab");   // the wooden table (user: steel, not wood)

            // ---- ammo amounts (bench box / zombie drop) and the box model, per gun
            SetAmmo(pistol.data, ammo[pistol.data], "PISTOL", 24, new Vector2Int(8, 14));
            SetAmmo(rifle.data, ammo[rifle.data], "RIFLE", 60, new Vector2Int(15, 30));
            SetAmmo(shotgun.data, ammo[shotgun.data], "SHOTGUN", 10, new Vector2Int(3, 6));
            sb.AppendLine("Ammo: bench boxes pistol 24 / rifle 60 / shotgun 10 rounds, back after 30 s; zombie drops 8-14 / 15-30 / 3-6 (8% of kills).");

            // ---- start with the pistol only
            Undo.RecordObject(pwc, "Base furnish");
            pwc.startingWeapons = 1;
            EditorUtility.SetDirty(pwc);

            // ---- the hall
            var b = safe.building;
            Physics.SyncTransforms();
            var local = BaseBuildingTools.LocalBounds(b);
            var room = BaseBuildingTools.MeasureRoom(b, local);
            Vector3 size = Vector3.Scale(local.size, b.lossyScale);
            Vector3 c = b.TransformPoint(local.center); c.y = room.floor;
            Vector3 ax = Flat(b.right).normalized, az = Flat(b.forward).normalized;
            Vector3 along = room.longX ? ax : az, across = room.longX ? az : ax;          // long axis / side walls
            float halfLong = (room.longX ? size.x : size.z) * 0.5f, halfAcross = (room.longX ? size.z : size.x) * 0.5f;
            Vector3 doorW = b.TransformPoint(local.center + room.doorLocal);
            float s = room.hasDoor && Vector3.Dot(doorW - c, along) < 0f ? -1f : 1f;      // the door's end of the hall
            float doorAcross = Vector3.Dot(Flat(doorW - c), across);

            // the inside face of a wall: the nearest thing a ray from the hall's centre line meets, at chest height
            float Wall(Vector3 from, Vector3 dir, float fallback)
            {
                var hits = Physics.RaycastAll(from + Vector3.up * 1.1f, dir, fallback + 4f, ~0, QueryTriggerInteraction.Ignore);
                float best = fallback;
                foreach (var h in hits) if (h.distance > 1.5f && h.distance < best) best = h.distance;
                return best;
            }

            var old = safe.transform.Find(RootName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject(RootName).transform;
            root.SetParent(safe.transform, false);
            root.position = c;

            // old floating-beam gear: the gun, armour and ammo beams go; the medkit stays, by the far end wall
            var gear = safe.transform.Find("BaseGear");
            if (gear != null)
                foreach (Transform g in gear.Cast<Transform>().ToList())
                {
                    if (g.name.Contains("MEDKIT")) g.position = Ground(c - along * s * (Wall(c, -along * s, halfLong) - 2.2f), room.floor);
                    else Object.DestroyImmediate(g.gameObject);
                }

            // ---- steel workbenches: two against each side wall, backs to the wall, fronts to the hall
            int id = 0, benches = 0;
            const float BenchDepth = 0.75f;
            float[] slots = { -0.5f * s * halfLong, 0.15f * s * halfLong };
            float[] tries = { 0f, 1.2f, -1.2f, 2.4f, -2.4f, 3.6f, -3.6f, 4.8f, -4.8f };
            var benchSpots = new List<Vector3>();
            foreach (var side in new[] { -1f, 1f })
            {
                Vector3 dir = across * side;
                foreach (var x in slots)
                {
                    bool placed = false;
                    foreach (var off in tries)
                    {
                        Vector3 line = c + along * (x + off);
                        float wd = Wall(line, dir, halfAcross);
                        var p = Ground(line + dir * (wd - BenchDepth * 0.5f - 0.06f), room.floor);
                        var rot = Quaternion.LookRotation(-dir, Vector3.up);
                        if (Physics.CheckBox(p + Vector3.up * 0.6f, new Vector3(1.0f, 0.5f, 0.4f), rot, ~0, QueryTriggerInteraction.Ignore)) continue;
                        var t = (GameObject)PrefabUtility.InstantiatePrefab(bench, root);
                        t.name = $"Bench_{++benches}";
                        t.transform.SetPositionAndRotation(p, rot);
                        Vector3 On(float bx, float bz) => t.transform.TransformPoint(new Vector3(bx, BenchTop, bz));
                        PlaceGun(t.transform, rifleShow, rifle.data, "RIFLE", On(-0.32f, -0.1f), id++);
                        PlaceGun(t.transform, shotgunShow, shotgun.data, "DOUBLE BARREL", On(-0.32f, 0.22f), id++);
                        PlaceAmmo(t.transform, ammo[pistol.data], On(0.42f, 0.2f), rot * Quaternion.Euler(0f, 8f, 0f));
                        PlaceAmmo(t.transform, ammo[shotgun.data], On(0.42f, -0.12f), rot * Quaternion.Euler(0f, -6f, 0f));
                        PlaceAmmo(t.transform, ammo[rifle.data], On(0.75f, 0.04f), rot * Quaternion.Euler(0f, 90f, 0f));
                        benchSpots.Add(p);
                        placed = true;
                        break;
                    }
                    if (!placed) sb.AppendLine($"  NO clear spot for a bench on side {side} near {x:0.0} m");
                }
            }
            sb.AppendLine($"Steel workbenches: {benches} against the side walls, each with an AK, a shotgun (taken = gone for the match, synced) " +
                          $"and 9mm / shotgun / 7.62 ammo boxes. {id} guns.");

            // ---- lanterns INSIDE the hall: along both side walls between the benches, the far end wall and by the door;
            // posts just off the wall, arms (and lanterns) toward the middle
            var spots = new List<(Vector3 line, Vector3 dir, float max)>();
            foreach (var side in new[] { -1f, 1f })
                foreach (var f in new[] { -0.85f, -0.18f, 0.45f })
                    spots.Add((c + along * (f * s * halfLong), across * side, halfAcross));
            foreach (var f in new[] { -0.4f, 0.4f })
                spots.Add((c + across * (f * halfAcross), -along * s, halfLong));
            if (room.hasDoor)
                foreach (var d in new[] { -2.6f, 2.6f })
                    spots.Add((c + across * (doorAcross + d), along * s, halfLong));
            int lanterns = 0;
            var lroot = new GameObject("Lanterns").transform;
            lroot.SetParent(root, false);
            foreach (var (line, dir, max) in spots)
            {
                Vector3 slide = Vector3.Cross(Vector3.up, dir);
                foreach (var off in new[] { 0f, 0.8f, -0.8f, 1.6f, -1.6f })
                {
                    Vector3 l = line + slide * off;
                    float wd = Wall(l, dir, max);
                    var q = Ground(l + dir * (wd - 0.35f), room.floor);
                    if (Physics.CheckBox(q + Vector3.up * 1.2f, new Vector3(0.2f, 0.9f, 0.2f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (benchSpots.Any(bp => Flat(bp - q).magnitude < 1.6f)) continue;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(lantern, lroot);
                    go.name = $"Lantern_{++lanterns}";
                    go.transform.SetPositionAndRotation(q, Quaternion.LookRotation(-dir, Vector3.up));
                    break;
                }
            }
            sb.AppendLine($"Lanterns: {lanterns} of {spots.Count} inside the hall (side walls, far end, by the door), " +
                          "particle flame + halo + flickering warm light (range 7.5 m, no shadows).");

            EditorSceneManager.MarkSceneDirty(safe.gameObject.scene);
            EditorSceneManager.SaveScene(safe.gameObject.scene);
            AssetDatabase.SaveAssets();
            sb.AppendLine("Player starts with the pistol only. Scene saved.");
            return sb.ToString();
        }

        static void SetAmmo(WeaponData d, GameObject pack, string name, int table, Vector2Int drop)
        {
            Undo.RecordObject(d, "Ammo packs");
            d.ammoPack = pack; d.ammoName = name; d.tableAmmo = table; d.dropAmmo = drop;
            EditorUtility.SetDirty(d);
        }

        static void PlaceGun(Transform table, GameObject show, WeaponData data, string label, Vector3 at, int id)
        {
            var go = new GameObject($"Pickup_{data.weaponName}");
            go.transform.SetParent(table, false);
            go.transform.SetPositionAndRotation(at, table.rotation);
            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true; col.radius = 0.85f; col.center = new Vector3(0f, 0.05f, 0f);
            var shown = (GameObject)PrefabUtility.InstantiatePrefab(show, go.transform);
            shown.transform.localPosition = Vector3.zero;
            shown.transform.localRotation = Quaternion.identity;
            var p = go.AddComponent<BasePickup>();
            p.kind = PickupKind.Weapon; p.weapon = data; p.label = label; p.rechargeTime = 0f;
            p.takeOnce = true; p.id = id; p.display = shown;
        }

        static void PlaceAmmo(Transform table, GameObject prefab, Vector3 at, Quaternion rot)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, table);
            go.transform.SetPositionAndRotation(at, rot);
        }

        static Vector3 Ground(Vector3 p, float fromY)
        {
            var start = new Vector3(p.x, fromY + 1.5f, p.z);
            return Physics.Raycast(start, Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore) ? hit.point : new Vector3(p.x, fromY, p.z);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        // ------------------------------------------------------------------ prefabs

        /// The FBX, materials mapped (ZR_* = the city props' materials, ZR_Ammo_* = their own texture).
        static GameObject Model(string name)
        {
            string path = $"{ModelDir}/{name}.fbx";
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) return null;
            foreach (var src in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Select(x => x.name).Distinct().ToList())
            {
                Material m = CityPropsTools.HasMaterial(src) ? CityPropsTools.EnsureMaterial(src)
                           : src.StartsWith("ZR_Ammo") ? AmmoMaterial(src) : null;
                if (m != null) imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), src), m);
            }
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        static Material AmmoMaterial(string name)
        {
            string dir = ModelDir + "/Materials";
            Directory.CreateDirectory(dir);
            string path = $"{dir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{ModelDir}/{name}.png"));
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Metallic", 0.1f);
            m.SetFloat("_Smoothness", 0.3f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// A root at the origin with the model under it, turned so Blender's +Y is the prefab's +Z.
        static GameObject Wrap(string name, out GameObject model)
        {
            model = null;
            var asset = Model(name);
            if (asset == null) return null;
            var go = new GameObject(name);
            model = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            model.name = "Model";
            model.transform.SetParent(go.transform, false);
            float yaw = Vector3.SignedAngle(CityPropsTools.BlenderForward(), Vector3.forward, Vector3.up);
            model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * model.transform.localRotation;
            return go;
        }

        static Bounds RendererBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToArray();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static GameObject Save(GameObject go, string name, StringBuilder sb)
        {
            try
            {
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{name}.prefab");
                var bb = RendererBounds(go);
                sb.AppendLine($"  {name}: {bb.size.x:0.00} x {bb.size.y:0.00} x {bb.size.z:0.00} m");
                return prefab;
            }
            finally { Object.DestroyImmediate(go); }
        }

        static void MakeStatic(GameObject model)
        {
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);   // not OccludeeStatic: the level's occlusion bake predates them (they vanished from some spots)
        }

        static GameObject BenchPrefab(StringBuilder sb)
        {
            var go = Wrap("ZR_SteelBench", out var model);
            if (go == null) return null;
            var bb = RendererBounds(go);
            var box = go.AddComponent<BoxCollider>();
            box.center = bb.center; box.size = bb.size;
            MakeStatic(model);
            return Save(go, "SteelBench", sb);
        }

        static GameObject LanternPrefab(StringBuilder sb)
        {
            var go = Wrap("ZR_LanternPost", out var model);
            if (go == null) return null;
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var socket = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "FlameSocket");
            Vector3 flameAt = socket != null ? go.transform.InverseTransformPoint(socket.position) : new Vector3(0f, 1.51f, 0.55f);
            if (socket != null) Object.DestroyImmediate(socket.gameObject);
            MakeStatic(model);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            var box = go.AddComponent<BoxCollider>();      // the post only: walk round it, the lantern hangs free
            box.center = new Vector3(0f, 1.1f, 0f); box.size = new Vector3(0.18f, 2.2f, 0.18f);

            AddFlame(go.transform, flameAt);
            var lg = new GameObject("Light");
            lg.transform.SetParent(go.transform, false);
            lg.transform.localPosition = flameAt + Vector3.up * 0.08f;
            var light = lg.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.6f, 0.28f);
            light.intensity = 3.6f;
            light.range = 7.5f;
            light.shadows = LightShadows.None;
            var fl = lg.AddComponent<FlickerLight>();
            fl.style = FlickerLight.Style.Fire; fl.minShare = 0.6f; fl.speed = 9f;
            return Save(go, "LanternPost", sb);
        }

        /// A small oil-lamp flame: a tight cone of glowing sparks that shrink and fade as they rise.
        static void AddFlame(Transform parent, Vector3 at)
        {
            var f = new GameObject("Flame");
            f.transform.SetParent(parent, false);
            f.transform.localPosition = at;
            f.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = f.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.42f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.18f, 0.38f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.78f, 0.35f, 0.95f), new Color(1f, 0.5f, 0.12f, 0.95f));
            main.maxParticles = 40;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = ps.emission; em.rateOverTime = 45f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 5f; sh.radius = 0.012f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.92f, 0.6f), 0f), new GradientColorKey(new Color(1f, 0.5f, 0.1f), 0.45f), new GradientColorKey(new Color(0.6f, 0.08f, 0.02f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.7f, 0.55f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.15f));
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.08f; noise.frequency = 3f; noise.scrollSpeed = 1.5f;
            var r = f.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = CityPropsTools.FireParticleMaterial();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.maxParticleSize = 0.2f;

            // a soft warm halo round the flame, pulsing a little: makes the lanterns read from across the yard
            var h = new GameObject("Halo");
            h.transform.SetParent(parent, false);
            h.transform.localPosition = at + Vector3.up * 0.06f;
            var hp = h.AddComponent<ParticleSystem>();
            var hm = hp.main;
            hm.startLifetime = 0.6f;
            hm.startSpeed = 0f;
            hm.startSize = new ParticleSystem.MinMaxCurve(0.55f, 0.75f);
            hm.startColor = new Color(1f, 0.55f, 0.2f, 0.22f);
            hm.maxParticles = 3;
            hm.simulationSpace = ParticleSystemSimulationSpace.Local;
            hm.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var he = hp.emission; he.rateOverTime = 4f;
            var hs = hp.shape; hs.enabled = false;
            var hc = hp.colorOverLifetime; hc.enabled = true;
            var hg = new Gradient();
            hg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.4f), new GradientAlphaKey(0f, 1f) });
            hc.color = hg;
            var hr = h.GetComponent<ParticleSystemRenderer>();
            hr.sharedMaterial = CityPropsTools.FireParticleMaterial();
            hr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            hr.receiveShadows = false;
            hr.maxParticleSize = 1f;
        }

        static GameObject VestPrefab(StringBuilder sb)
        {
            var go = Wrap("ZR_ArmorVest", out var model);
            if (go == null) return null;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true; col.radius = 0.85f; col.center = new Vector3(0f, 0.1f, 0f);
            var p = go.AddComponent<BasePickup>();
            p.kind = PickupKind.Armor; p.amount = 100f; p.rechargeTime = 0f; p.oncePerLife = true; p.label = "ARMOUR";
            p.display = null;   // never disappears
            return Save(go, "ArmorVest", sb);
        }

        static GameObject AmmoPrefab(string name, WeaponData weapon, StringBuilder sb)
        {
            var go = Wrap(name, out var model);
            if (go == null) return null;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true; col.radius = 0.85f; col.center = new Vector3(0f, 0.15f, 0f);
            var p = go.AddComponent<AmmoPickup>();
            p.weapon = weapon; p.amount = weapon.tableAmmo; p.respawnTime = 30f; p.lifetime = 40f; p.display = model;
            return Save(go, name.Replace("ZR_", ""), sb);
        }

        /// A gun lying on its side on a table: the gun rig's meshes baked in their rest pose into static meshes
        /// (nothing animates on a table), muzzle along +X, top of the gun toward +Z, resting on y = 0.
        static GameObject TableGunPrefab(string gun, StringBuilder sb)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Guns/{gun}.prefab");
            if (src == null) { sb.AppendLine($"  {gun}: no gun prefab"); return null; }
            var inst = Object.Instantiate(src);
            var go = new GameObject("TableGun_" + gun);
            try
            {
                var rig = inst.GetComponent<GunRig>();
                Quaternion frame = rig != null ? rig.frame : Quaternion.identity;
                var holder = new GameObject("Gun").transform;
                holder.SetParent(go.transform, false);
                int k = 0;
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    if (!r.gameObject.activeInHierarchy || !r.enabled) continue;
                    if (r.name.StartsWith("Shell") || r.name.StartsWith("Muzzle") || r is ParticleSystemRenderer) continue;
                    Mesh mesh;
                    if (r is SkinnedMeshRenderer smr)
                    {
                        if (smr.sharedMesh == null) continue;
                        mesh = new Mesh { name = $"TableGun_{gun}_{r.name}" };
                        smr.BakeMesh(mesh, true);
                    }
                    else if (r.GetComponent<MeshFilter>() is MeshFilter mf && mf.sharedMesh != null) mesh = Object.Instantiate(mf.sharedMesh);
                    else continue;
                    if (mesh.bounds.size.magnitude < 0.01f) continue;
                    mesh.name = $"TableGun_{gun}_{r.name}";
                    // update the mesh asset in place (deleting + recreating it left the placed guns with no mesh)
                    string mp = $"{PrefabDir}/Meshes/{mesh.name}.asset";
                    var existing = AssetDatabase.LoadAssetAtPath<Mesh>(mp);
                    if (existing != null) { EditorUtility.CopySerialized(mesh, existing); EditorUtility.SetDirty(existing); Object.DestroyImmediate(mesh); mesh = existing; }
                    else AssetDatabase.CreateAsset(mesh, mp);
                    var part = new GameObject(r.name);
                    part.transform.SetParent(holder, false);
                    part.transform.localPosition = inst.transform.InverseTransformPoint(r.transform.position);
                    part.transform.localRotation = Quaternion.Inverse(inst.transform.rotation) * r.transform.rotation;
                    part.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = part.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = r.sharedMaterials;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    k++;
                }
                // lying on its side: muzzle -> +X, top -> +Z
                holder.localRotation = Quaternion.LookRotation(Vector3.right, Vector3.forward) * Quaternion.Inverse(frame);
                var bb = RendererBounds(go);
                holder.localPosition -= new Vector3(bb.center.x, bb.min.y, bb.center.z);
                sb.AppendLine($"  TableGun_{gun}: {k} parts baked");
            }
            finally { Object.DestroyImmediate(inst); }
            return Save(go, "TableGun_" + gun, sb);
        }

        // ------------------------------------------------------------------ survey

        /// Room size, door, what is inside and a top-down map of free floor ('.' free, '#' blocked, 'D' door, 'P' player).
        public static string Survey()
        {
            var safe = Object.FindAnyObjectByType<SafeBase>(FindObjectsInactive.Include);
            if (safe == null || safe.building == null) return "FAILED: no SafeBase with a building.";
            var b = safe.building;
            Physics.SyncTransforms();
            var local = BaseBuildingTools.LocalBounds(b);
            var room = BaseBuildingTools.MeasureRoom(b, local);
            var sb = new StringBuilder();
            Vector3 size = Vector3.Scale(local.size, b.lossyScale);
            sb.AppendLine($"building '{b.name}' at {b.position}, rot {b.eulerAngles.y:0}, scale {b.lossyScale}, size {size.x:0.0} x {size.y:0.0} x {size.z:0.0} m (local x,y,z)");
            sb.AppendLine($"room floor {room.floor:0.00}, ceiling {room.ceiling:0.00}, long axis {(room.longX ? "x" : "z")}, door {(room.hasDoor ? b.TransformPoint(local.center + room.doorLocal).ToString("F1") : "none")} (local {room.doorLocal:F1})");
            sb.AppendLine($"safe base centre {safe.Center}, radius {safe.radius:0.0}, half {safe.halfCircle}, forward {safe.transform.forward:F2}");
            var player = Object.FindAnyObjectByType<PlayerHealth>(FindObjectsInactive.Include);
            if (player != null) sb.AppendLine($"player start {player.transform.position:F1}");
            var gear = safe.transform.Find("BaseGear");
            if (gear != null) foreach (Transform g in gear) sb.AppendLine($"  gear {g.name} at {g.position:F1}");
            var furn = safe.transform.Find(RootName);
            if (furn != null) foreach (var t in furn.GetComponentsInChildren<Transform>().Where(t => t.parent == furn || t.parent?.name == "Lanterns")) sb.AppendLine($"  {t.name} at {t.position:F1}");
            foreach (var gname in new[] { "TableGun_Rifle", "TableGun_Shotgun" })
            {
                AssetDatabase.ImportAsset($"{PrefabDir}/{gname}.prefab", ImportAssetOptions.ForceUpdate);
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{gname}.prefab");
                sb.AppendLine($"  prefab {gname}: " + (pf == null ? "MISSING" : string.Join(", ", pf.GetComponentsInChildren<MeshFilter>(true).Select(mf => $"{mf.name} mesh {(mf.sharedMesh != null ? mf.sharedMesh.name + " " + mf.sharedMesh.vertexCount + "v" : "NONE")}"))));
            }
            foreach (var mp in Directory.GetFiles(PrefabDir + "/Meshes", "*.asset"))
            {
                var m = AssetDatabase.LoadAssetAtPath<Mesh>(mp.Replace('\\', '/'));
                sb.AppendLine($"  mesh file {Path.GetFileName(mp)}: {(m != null ? m.vertexCount + " verts, bounds " + m.bounds.size.ToString("F2") : "LOADS AS NULL (" + AssetDatabase.GetMainAssetTypeAtPath(mp.Replace('\\', '/')) + ")")}");
            }
            if (furn != null)
                foreach (var p in furn.GetComponentsInChildren<BasePickup>(true))
                {
                    var rs = p.display != null ? p.display.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
                    sb.AppendLine($"  pickup {p.transform.parent.name}/{p.name} id {p.id} at {p.transform.position:F2}, display {(p.display != null ? $"{p.display.name} active {p.display.activeSelf}, {rs.Length} renderers, bounds {(rs.Length > 0 ? rs[0].bounds.center.ToString("F2") + " size " + rs[0].bounds.size.ToString("F2") : "-")}, mesh {(rs.Length > 0 && rs[0].GetComponent<MeshFilter>()?.sharedMesh != null ? rs[0].GetComponent<MeshFilter>().sharedMesh.name : "NONE")}" : "none")}");
                }
            return sb.ToString();
        }
    }
}
