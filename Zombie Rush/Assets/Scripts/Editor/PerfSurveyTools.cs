using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZombieRush.EditorTools
{
    /// Zombie Rush > Level > Performance Survey (marker: Temp/ZR_RunPerfSurvey, report Temp/ZR_PerfSurvey.log).
    /// What the open scene costs to draw: renderers, triangles, how many are marked static for batching, materials
    /// without GPU instancing, shadow casters, the heaviest meshes (triangles x copies), cameras' far planes,
    /// LOD groups and whether occlusion culling is baked. Read only: it changes nothing.
    [InitializeOnLoad]
    static class PerfSurveyTools
    {
        const string Marker = "Temp/ZR_RunPerfSurvey";

        static PerfSurveyTools() => EditorApplication.update += Poll;

        static double next;
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 2.0;
            if (!File.Exists(Marker) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            File.Delete(Marker);
            string r;
            try { r = Survey(); } catch (System.Exception e) { r = "FAILED: " + e; }
            File.WriteAllText("Temp/ZR_PerfSurvey.log", r);
        }

        [MenuItem("Zombie Rush/Level/Performance Survey")]
        static void Menu() => Debug.Log(Survey());

        static string Survey()
        {
            var sb = new StringBuilder();
            var rends = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                              .Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            long tris = 0, staticTris = 0; int staticCount = 0, shadowCasters = 0, multiMat = 0;
            var meshUse = new System.Collections.Generic.Dictionary<Mesh, (int copies, long tris, int staticCopies)>();
            var mats = new System.Collections.Generic.HashSet<Material>();
            foreach (var r in rends)
            {
                var mf = r.GetComponent<MeshFilter>();
                var m = mf != null ? mf.sharedMesh : null;
                if (m == null) continue;
                long t = 0;
                for (int s = 0; s < m.subMeshCount; s++) t += m.GetIndexCount(s) / 3;
                tris += t;
                bool st = GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.BatchingStatic);
                if (st) { staticCount++; staticTris += t; }
                if (r.shadowCastingMode != ShadowCastingMode.Off) shadowCasters++;
                if (r.sharedMaterials.Length > 1) multiMat++;
                foreach (var mat in r.sharedMaterials) if (mat != null) mats.Add(mat);
                meshUse.TryGetValue(m, out var u);
                meshUse[m] = (u.copies + 1, t, u.staticCopies + (st ? 1 : 0));
            }
            int noInst = mats.Count(m => !m.enableInstancing);
            sb.AppendLine($"Scene: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
            sb.AppendLine($"Mesh renderers: {rends.Length} ({multiMat} with several materials), {tris / 1000}k triangles in all");
            sb.AppendLine($"Batching static: {staticCount} renderers, {staticTris / 1000}k triangles ({100f * staticCount / Mathf.Max(1, rends.Length):0}% of renderers)");
            sb.AppendLine($"Shadow casters: {shadowCasters}; materials: {mats.Count}, without GPU instancing: {noInst}");
            sb.AppendLine($"Skinned meshes: {Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None).Length}, LOD groups: {Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None).Length}, mesh colliders: {Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Length}");
            sb.AppendLine($"Lights: {string.Join(", ", Object.FindObjectsByType<Light>(FindObjectsSortMode.None).GroupBy(l => l.type + (l.shadows != LightShadows.None ? " (shadows)" : "")).Select(g => g.Count() + " " + g.Key))}");
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                sb.AppendLine($"Camera '{c.name}': far {c.farClipPlane:0} m, near {c.nearClipPlane:0.00}, occlusion culling {(c.useOcclusionCulling ? "on" : "off")}");
            sb.AppendLine($"Occlusion data baked: {(StaticOcclusionCulling.umbraDataSize > 0 ? StaticOcclusionCulling.umbraDataSize / 1024 + " KB" : "no")}; fog: {(RenderSettings.fog ? RenderSettings.fogMode + " " + RenderSettings.fogStartDistance + "-" + RenderSettings.fogEndDistance + " density " + RenderSettings.fogDensity : "off")}");
            sb.AppendLine("Heaviest meshes (triangles x copies):");
            foreach (var kv in meshUse.OrderByDescending(k => k.Value.tris * k.Value.copies).Take(18))
                sb.AppendLine($"  {kv.Key.name}: {kv.Value.tris / 1000f:0.0}k tris x {kv.Value.copies} = {kv.Value.tris * kv.Value.copies / 1000}k ({kv.Value.staticCopies} static)");
            // biggest groups of renderers under one parent (likely props that could batch)
            sb.AppendLine("Most renderers under one top-level object:");
            foreach (var g in rends.GroupBy(r => r.transform.root).OrderByDescending(g => g.Count()).Take(10))
                sb.AppendLine($"  {g.Key.name}: {g.Count()} renderers, static {g.Count(r => GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.BatchingStatic))}");
            return sb.ToString();
        }
    }
}
