using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZombieRush.EditorTools
{
    /// Dead texture slots in materials: a material keeps every texture it was ever given, also in slots its
    /// shader does not read (a property the shader doesn't have, or URP Lit's specular map in the metallic
    /// workflow). The build packs those textures anyway (e.g. 14 unused roughness maps from the building pack).
    /// Marker: Temp/ZR_RunMapCleanup with "matslots" (dry run) or "matslots fix" (clears them; backups in
    /// Backups/Materials_before_matslots).
    public static class MaterialSlotTools
    {
        public static string Run(bool fix)
        {
            var sb = new System.Text.StringBuilder(fix ? "Material slots (FIX):" : "Material slots (dry run):");
            var cleared = new Dictionary<string, List<string>>();   // material -> slots
            var dropped = new HashSet<Texture>();
            foreach (var path in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                if (!path.EndsWith(".mat")) continue;
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null) continue;
                var so = new SerializedObject(mat);
                var envs = so.FindProperty("m_SavedProperties.m_TexEnvs");
                if (envs == null) continue;
                bool lit = mat.shader.name.StartsWith("Universal Render Pipeline/") && mat.HasProperty("_WorkflowMode");
                float workflow = lit ? mat.GetFloat("_WorkflowMode") : -1f;
                for (int i = 0; i < envs.arraySize; i++)
                {
                    var e = envs.GetArrayElementAtIndex(i);
                    string name = e.FindPropertyRelative("first").stringValue;
                    var texProp = e.FindPropertyRelative("second.m_Texture");
                    var tex = texProp.objectReferenceValue as Texture;
                    if (tex == null) continue;
                    bool dead = mat.shader.FindPropertyIndex(name) < 0
                        || (lit && workflow >= 0.5f && name == "_SpecGlossMap")
                        || (lit && workflow < 0.5f && name == "_MetallicGlossMap");
                    if (!dead) continue;
                    if (!cleared.TryGetValue(path, out var l)) cleared[path] = l = new List<string>();
                    l.Add($"{name}={tex.name}");
                    dropped.Add(tex);
                    if (fix) texProp.objectReferenceValue = null;
                }
                if (fix && cleared.ContainsKey(path))
                {
                    string bk = Path.Combine("Backups/Materials_before_matslots", path.Substring("Assets/".Length));
                    Directory.CreateDirectory(Path.GetDirectoryName(bk));
                    if (!File.Exists(bk)) File.Copy(path, bk);
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(mat);
                }
            }
            if (fix) AssetDatabase.SaveAssets();

            // textures no material uses any more (after the fix) = what leaves the build
            var stillUsed = new HashSet<Texture>();
            foreach (var path in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null) continue;
                foreach (var n in mat.GetTexturePropertyNames())
                {
                    if (cleared.TryGetValue(path, out var l) && l.Any(s => s.StartsWith(n + "="))) continue;
                    var t = mat.GetTexture(n); if (t != null) stillUsed.Add(t);
                }
            }
            var freed = dropped.Where(t => !stillUsed.Contains(t)).ToList();
            long bytes = freed.Sum(t => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t));
            sb.Append($"\n  {cleared.Count} materials, {cleared.Values.Sum(l => l.Count)} dead slots; {freed.Count} textures then unused by any material (~{bytes / (1024f * 1024f):0} MB in editor memory):");
            foreach (var t in freed.OrderBy(t => t.name)) sb.Append($"\n    {AssetDatabase.GetAssetPath(t)}");
            sb.Append("\n  slots by material (first 40):");
            foreach (var kv in cleared.Take(40)) sb.Append($"\n    {kv.Key}: {string.Join(", ", kv.Value)}");
            return sb.ToString();
        }
    }
}
