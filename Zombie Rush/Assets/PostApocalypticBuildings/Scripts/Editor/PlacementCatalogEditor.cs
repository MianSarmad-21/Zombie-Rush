using UnityEditor;
using UnityEngine;

namespace AutoPlacer
{
    /// <summary>
    /// Runtime flag telling the shipping UI whether the authoring tools are present. The authoring file
    /// (PlacementCatalogEditor.Authoring.cs) flips this to true on load. Delete that one file and this
    /// stays false, so the tool presents its user-facing, add-one-prefab-at-a-time UI with no folder
    /// scanning and no Create menu.
    /// </summary>
    public static class PlacementAuthoringGate
    {
        public static bool Enabled;
    }

    /// <summary>
    /// Inspector for <see cref="PlacementCatalog"/>. This half is the end-user UI: it lists the catalog's
    /// pieces and lets the user add or remove prefabs ONE AT A TIME, and tweak each piece's kind / size /
    /// tilt / weight. It has no folder-scan feature.
    ///
    /// The authoring-only extras (folder Browse + Scan, batch Reclassify-by-name, and the
    /// "Assets > Create" menu) live in the sibling partial file PlacementCatalogEditor.Authoring.cs.
    /// Delete that file before shipping and this inspector keeps working as the user version: the
    /// authoring hook below is a partial method, so its call site compiles to nothing once the
    /// implementation is gone.
    /// </summary>
    [CustomEditor(typeof(PlacementCatalog))]
    public partial class PlacementCatalogEditor : Editor
    {
        Vector2 _scroll;
        GameObject _pendingPrefab;

        // Implemented only in the authoring file. When that file is deleted, this call becomes a no-op
        // and no scanning UI is drawn.
        partial void DrawAuthoringSection(PlacementCatalog catalog);

        public override void OnInspectorGUI()
        {
            var catalog = (PlacementCatalog)target;

            // Authoring-only tools (folder scan, size thresholds, auto-classify) are drawn here and
            // vanish once PlacementCatalogEditor.Authoring.cs is removed for shipping.
            DrawAuthoringSection(catalog);

            DrawAddPrefab(catalog);

            if (GUILayout.Button("Open Generator", GUILayout.Height(24f)))
                PlacementWindow.Open(catalog);

            EditorGUILayout.HelpBox(
                "To turn off same-type grouping, uncheck 'Cluster Buildings' in the Destroyed City Generator " +
                "window (Clustering section) - the city will then mix all building types everywhere.",
                MessageType.None);

            EditorGUILayout.Space();
            DrawPieces(catalog);

            if (GUI.changed) EditorUtility.SetDirty(catalog);
        }

        // --- add / remove a single prefab ------------------------------------

        void DrawAddPrefab(PlacementCatalog catalog)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Add a prefab", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    _pendingPrefab = (GameObject)EditorGUILayout.ObjectField(_pendingPrefab, typeof(GameObject), false);
                    using (new EditorGUI.DisabledScope(_pendingPrefab == null))
                    {
                        if (GUILayout.Button("Add", GUILayout.Width(60f)))
                        {
                            AddPrefab(catalog, _pendingPrefab);
                            _pendingPrefab = null;
                            GUI.FocusControl(null);
                        }
                    }
                }
                EditorGUILayout.LabelField(
                    "Select your prefab and click Add. It's added as Ignored at the end of the list — set its " +
                    "Kind (Building / Rubble / Small Debris / Street Debris), Size, Tilt and Weight.",
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        void AddPrefab(PlacementCatalog catalog, GameObject prefab)
        {
            string path = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(path) || PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.NotAPrefab)
            {
                EditorUtility.DisplayDialog("Not a prefab", "Pick a prefab asset from the Project window.", "OK");
                return;
            }
            if (catalog.pieces.Exists(p => p != null && p.prefab == prefab))
            {
                EditorUtility.DisplayDialog("Already added", prefab.name + " is already in this catalog.", "OK");
                return;
            }

            // Add the prefab only. No auto kind/size/tilt: the user classifies it themselves. The
            // generator measures the prefab's footprint automatically when it runs, so no measuring here.
            catalog.pieces.Add(new PlacementPiece
            {
                prefab = prefab,
                displayName = ObjectNames.NicifyVariableName(prefab.name),
                id = AssetDatabase.AssetPathToGUID(path),
                family = PlacementCatalog.FamilyKey(prefab.name),
            });
            Save(catalog);
        }

        internal static void Save(PlacementCatalog catalog)
        {
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }

        // --- piece list ------------------------------------------------------

        void DrawPieces(PlacementCatalog catalog)
        {
            int placeable = 0, ignored = 0;
            foreach (PlacementPiece p in catalog.pieces)
            {
                if (p == null) continue;
                if (p.kind == PieceKind.Ignored) ignored++; else placeable++;
            }
            EditorGUILayout.LabelField($"Pieces ({catalog.pieces.Count}) - {placeable} used, {ignored} ignored", EditorStyles.boldLabel);
            if (catalog.pieces.Count == 0)
            {
                EditorGUILayout.LabelField("No prefabs yet - add one above.", EditorStyles.miniLabel);
                return;
            }

            PlacementPiece toRemove = null;
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(460f));
            PieceKind? header = null;
            // Draw grouped by kind for readability.
            foreach (PieceKind kind in new[] { PieceKind.Building, PieceKind.Rubble, PieceKind.SmallDebris, PieceKind.StreetDebris, PieceKind.Ignored })
            {
                foreach (PlacementPiece piece in catalog.pieces)
                {
                    if (piece == null || piece.kind != kind) continue;
                    if (header != kind)
                    {
                        header = kind;
                        EditorGUILayout.Space(4f);
                        EditorGUILayout.LabelField(kind.ToString(), EditorStyles.miniBoldLabel);
                    }
                    if (DrawPiece(piece)) toRemove = piece;
                }
            }
            EditorGUILayout.EndScrollView();

            if (toRemove != null)
            {
                catalog.pieces.Remove(toRemove);
                Save(catalog);
                GUIUtility.ExitGUI();
            }
        }

        /// <summary>Draws one piece row. Returns true if the user asked to remove it.</summary>
        static bool DrawPiece(PlacementPiece piece)
        {
            piece.EnsureMeasured();
            bool remove = false;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                Texture preview = piece.prefab != null ? AssetPreview.GetAssetPreview(piece.prefab) : null;
                Rect thumb = GUILayoutUtility.GetRect(42f, 42f, GUILayout.Width(42f), GUILayout.Height(42f));
                if (preview != null) GUI.DrawTexture(thumb, preview, ScaleMode.ScaleToFit);

                using (new EditorGUILayout.VerticalScope())
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        piece.enabled = EditorGUILayout.Toggle(piece.enabled, GUILayout.Width(18f));
                        EditorGUILayout.LabelField(piece.displayName, EditorStyles.boldLabel);
                        // Kind only; Size and Tilt are left to the user (no auto-classification).
                        piece.kind = (PieceKind)EditorGUILayout.EnumPopup(piece.kind, GUILayout.Width(95f));
                        if (GUILayout.Button(new GUIContent("×", "Remove this prefab from the catalog"),
                                EditorStyles.miniButton, GUILayout.Width(22f)))
                            remove = EditorUtility.DisplayDialog("Remove prefab",
                                $"Remove '{piece.displayName}' from this catalog?", "Remove", "Cancel");
                    }

                    string dims = piece.measured
                        ? $"{piece.size.x:0.#} x {piece.size.z:0.#} footprint, {piece.Height:0.#} tall"
                        : "not measurable - prefab has no meshes";
                    if (piece.kind == PieceKind.Building && !string.IsNullOrEmpty(piece.family))
                        dims += $"   ·  family: {piece.family}";
                    EditorGUILayout.LabelField(dims, EditorStyles.miniLabel);

                    if (piece.kind != PieceKind.Ignored)
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUIUtility.labelWidth = 44f;
                            piece.sizeClass = (SizeClass)EditorGUILayout.EnumPopup("Size", piece.sizeClass, GUILayout.Width(150f));
                            piece.allowTilt = EditorGUILayout.ToggleLeft("Tilt", piece.allowTilt, GUILayout.Width(50f));
                            EditorGUIUtility.labelWidth = 50f;
                            piece.weight = Mathf.Max(0f, EditorGUILayout.FloatField("Weight", piece.weight, GUILayout.Width(110f)));
                            EditorGUIUtility.labelWidth = 0f;
                        }
                }
            }
            return remove;
        }
    }
}
