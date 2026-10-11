using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AutoPlacer
{
    /// <summary>
    /// The Destroyed City Generator front-end. Pick a catalog, tune the settings (which are stored on the
    /// catalog) in collapsible sections, and Place.
    /// </summary>
    public class PlacementWindow : EditorWindow
    {
        [SerializeField] PlacementCatalog _catalog;
        [SerializeField] Terrain _terrain;
        [SerializeField] bool[] _sectionOpen;
        SerializedObject _so;
        SerializedProperty _settingsProp;
        Vector2 _scroll;

        // Grouping of the settings fields into tidy collapsible sections.
        static readonly (string title, string[] fields)[] Sections =
        {
            ("General",         new[] { "seed", "rootObjectName", "citySize" }),
            ("City shape",      new[] { "organicShape", "organicEdge", "organicLobes" }),
            ("Streets & blocks", new[] { "blockSize", "sideStreetWidth", "mainStreetWidth", "mainStreetEvery", "blockSetback", "buildingGap" }),
            ("Organic streets", new[] { "blockMergeChance", "avenueCount", "avenueWidth", "avenueCurve", "avenueEndChance" }),
            ("Districts",       new[] { "downtownCenter01", "downtownRadius01", "midtownRadius01", "zoneNoiseScale", "zoneNoiseAmount", "downtownDensity", "outskirtsTallChance" }),
            ("Buildings",       new[] { "density", "alignToGrid", "tiltMaxDegrees", "buildingScaleJitter", "tiltSinkPerDegree", "groundEmbed" }),
            ("Clustering",      new[] { "clusterBuildings", "clusterZoneSize", "clusterStrength" }),
            ("Debris",          new[] { "rubblePerBigBuilding", "rubbleSpread", "smallDebrisPerBuilding", "smallDebrisSpread", "smallDebrisPerArea", "streetDebrisPerArea" }),
            ("Debris scale",    new[] { "debrisScaleMin", "debrisScaleMax", "rubbleScaleMin", "rubbleScaleMax" }),
            ("Background ring", new[] { "backgroundEnabled", "backgroundDepth", "backgroundDensity", "backgroundGap", "backgroundRubbleShare" }),
            ("Terrain streets", new[] { "paintTerrainStreets", "streetLayer", "terrainStreetsSwapXZ", "streetEdgeWobble", "streetNoiseScale", "streetStrengthMin" }),
            ("Ground",          new[] { "groundY", "conformToGround", "groundMask" }),
            ("Output",          new[] { "markStatic", "objectsPerFrame" }),
        };
        // Sections open by default (the everyday knobs); the rest start collapsed.
        static readonly HashSet<string> DefaultOpen = new HashSet<string> { "General", "City shape", "Streets & blocks", "Districts", "Buildings", "Debris" };

        [MenuItem("Tools/Destroyed Buildings and Debris Pack/Destroyed City Generator")]
        public static void Open() => Open(null);

        public static void Open(PlacementCatalog catalog)
        {
            var win = GetWindow<PlacementWindow>();
            win.titleContent = new GUIContent("Destroyed Buildings and Debris Pack");
            if (catalog != null) win._catalog = catalog;
            win.minSize = new Vector2(360f, 500f);
            win.Rebind();
            win.Show();
        }

        void OnEnable() { titleContent = new GUIContent("Destroyed Buildings and Debris Pack"); Rebind(); }

        void Rebind()
        {
            if (_catalog != null)
            {
                _so = new SerializedObject(_catalog);
                _settingsProp = _so.FindProperty("settings");
            }
            else { _so = null; _settingsProp = null; }
            EnsureSections();
        }

        void EnsureSections()
        {
            if (_sectionOpen != null && _sectionOpen.Length == Sections.Length) return;
            _sectionOpen = new bool[Sections.Length];
            for (int i = 0; i < Sections.Length; i++) _sectionOpen[i] = DefaultOpen.Contains(Sections[i].title);
        }

        // --- layout ----------------------------------------------------------

        void OnGUI()
        {
            DrawTitle();

            var picked = (PlacementCatalog)EditorGUILayout.ObjectField("Catalog", _catalog, typeof(PlacementCatalog), false);
            if (picked != _catalog) { _catalog = picked; Rebind(); }

            if (_catalog == null)
            {
                EditorGUILayout.Space();
                if (PlacementAuthoringGate.Enabled)
                {
                    EditorGUILayout.HelpBox(
                        "Create a Catalog, point it at your prefab folder and Scan it, then place from here.\n\n" +
                        "Create it via  Assets ▸ Create ▸ Buildings and Debris Pack ▸ Catalog, or the button below.",
                        MessageType.Info);
                    if (GUILayout.Button("Create Catalog Asset", GUILayout.Height(26f))) CreateCatalog();
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "Assign the Catalog that came with this pack to start.\n\n" +
                        "Drop it into the Catalog field above (or select the Catalog asset in your Project), then " +
                        "Generate. You can add or remove your own prefabs from the catalog's inspector.",
                        MessageType.Info);
                }
                return;
            }

            EnsureSections();
            _so.Update();
            DrawToolbar();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawSettingsSections();
            EditorGUILayout.EndScrollView();

            _so.ApplyModifiedProperties();

            DrawActions();
        }

        void DrawTitle()
        {
            EditorGUILayout.Space(4f);
            var t = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            EditorGUILayout.LabelField("Procedural Destroyed City Generator", t);
            EditorGUILayout.Space(5f);
            EditorGUILayout.HelpBox(
                "Note: The procedural generator is designed to give you a starting point. For the best visual " +
                "results, we recommend manually customizing and refining the generated city rather than relying " +
                "on the generated layout alone.",
                MessageType.Info);
            EditorGUILayout.Space(5f);
            Separator();
        }

        void DrawToolbar()
        {
            int used = 0, buildings = 0;
            foreach (var p in _catalog.pieces)
                if (p != null && p.IsPlaceable) { used++; if (p.kind == PieceKind.Building) buildings++; }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"{used} placeable  ·  {buildings} buildings", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Edit Catalog", EditorStyles.miniButtonLeft, GUILayout.Width(90f))) Selection.activeObject = _catalog;
                if (GUILayout.Button("Expand all", EditorStyles.miniButtonMid, GUILayout.Width(72f))) SetAllSections(true);
                if (GUILayout.Button("Collapse all", EditorStyles.miniButtonRight, GUILayout.Width(78f))) SetAllSections(false);
            }

            if (buildings == 0)
                EditorGUILayout.HelpBox(
                    PlacementAuthoringGate.Enabled
                        ? "No Building pieces yet — click Edit Catalog and Scan your prefab folder."
                        : "No Building pieces yet — click Edit Catalog and add some building prefabs.",
                    MessageType.Warning);

            Separator();
        }

        void DrawSettingsSections()
        {
            for (int i = 0; i < Sections.Length; i++)
            {
                _sectionOpen[i] = EditorGUILayout.BeginFoldoutHeaderGroup(_sectionOpen[i], Sections[i].title);
                if (_sectionOpen[i])
                {
                    EditorGUI.indentLevel++;
                    foreach (string field in Sections[i].fields)
                    {
                        SerializedProperty p = _settingsProp.FindPropertyRelative(field);
                        if (p != null) EditorGUILayout.PropertyField(p, true);
                    }
                    // The terrain is a scene object, so it can't live on the catalog asset - draw it here.
                    if (Sections[i].title == "Terrain streets" && _catalog.settings.paintTerrainStreets)
                        _terrain = (Terrain)EditorGUILayout.ObjectField(
                            new GUIContent("Terrain (scene)", "Terrain to paint streets onto. Leave empty to use the active terrain."),
                            _terrain, typeof(Terrain), true);
                    EditorGUI.indentLevel--;
                    EditorGUILayout.Space(2f);
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
            }
        }

        void DrawActions()
        {
            Separator();

            if (Placer.IsPlacing)
            {
                var r = EditorGUILayout.GetControlRect(false, 22f);
                EditorGUI.ProgressBar(r, Placer.PlacementProgress,
                    $"Placing…  {Placer.PlacementDone:n0} / {Placer.PlacementTotal:n0} objects");
                if (GUILayout.Button("Cancel", GUILayout.Height(26f)))
                    Placer.CancelPlacement();
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = new Color(0.55f, 0.8f, 1f);
                if (GUILayout.Button("Generate", GUILayout.Height(30f)))
                    Placer.Generate(_catalog, _terrain);
                GUI.backgroundColor = Color.white;

                if (GUILayout.Button("Reroll + Generate", GUILayout.Height(30f), GUILayout.Width(150f)))
                {
                    _catalog.settings.seed = Random.Range(1, int.MaxValue);
                    EditorUtility.SetDirty(_catalog);
                    Rebind();
                    Placer.Generate(_catalog, _terrain);
                }
                if (GUILayout.Button("Clear", GUILayout.Height(30f), GUILayout.Width(64f)))
                    Placer.Clear(_catalog);
            }
        }

        /// <summary>Repaints any open Auto Placer window - the runner calls this each batch to refresh progress.</summary>
        public static void RepaintOpen()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<PlacementWindow>()) w.Repaint();
        }

        void SetAllSections(bool open)
        {
            for (int i = 0; i < _sectionOpen.Length; i++) _sectionOpen[i] = open;
        }

        static void Separator()
        {
            var r = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(r, new Color(0.5f, 0.5f, 0.5f, 0.35f));
            EditorGUILayout.Space(2f);
        }

        void CreateCatalog()
        {
            string path = EditorUtility.SaveFilePanelInProject("Create Catalog", "Catalog", "asset",
                "Where to save the catalog asset?");
            if (string.IsNullOrEmpty(path)) return;
            var cat = ScriptableObject.CreateInstance<PlacementCatalog>();
            AssetDatabase.CreateAsset(cat, path);
            AssetDatabase.SaveAssets();
            _catalog = cat;
            Rebind();
            Selection.activeObject = cat;
        }
    }
}
