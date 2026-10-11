using System.Collections.Generic;
using UnityEngine;

namespace AutoPlacer
{
    /// <summary>
    /// The library of prefabs the generator draws from. Point <see cref="sourceFolder"/> at your
    /// prefabs folder and hit Scan in the inspector - the scanner classifies each prefab (building /
    /// rubble / debris), measures its footprint and height and writes a direct prefab reference, so
    /// nothing has to live in a Resources folder. The generation <see cref="settings"/> ride along on
    /// the catalog so a tuned preset travels with the asset.
    ///
    /// Editor-only: this file lives under an Editor folder, so the type is never compiled into a build.
    ///
    /// There is deliberately no [CreateAssetMenu] here: creating catalogs is an authoring task, so the
    /// "Assets > Create > Buildings and Debris Pack > Catalog" menu is provided only by the authoring
    /// file (PlacementCatalogEditor.Authoring.cs) and disappears once that file is removed for shipping.
    /// </summary>
    public class PlacementCatalog : ScriptableObject
    {
        [Tooltip("Authoring-only: project folder scanned for prefabs, e.g. Assets/DestroyedCity/Prefabs. Unused in the shipped, scan-free tool.")]
        public string sourceFolder = "";

        [Tooltip("Also scan subfolders.")]
        public bool includeSubfolders = true;

        // Authoring-only: these feed the name/height auto-classifier in the dev inspector. Kept as data
        // so a scanned catalog stays byte-identical when the authoring file is removed; end users don't
        // see or use them (they set each piece's Size by hand).
        [Header("Size thresholds (measured height, world units)")]
        [Tooltip("A building at least this tall counts as Medium.")]
        public float mediumHeight = 6f;
        [Tooltip("A building at least this tall counts as Large (and may tilt).")]
        public float largeHeight = 14f;
        [Tooltip("A building at least this tall counts as a Skyscraper (downtown, may tilt).")]
        public float skyscraperHeight = 30f;
        [Tooltip("Rubble/debris whose largest footprint is under this is treated as Small.")]
        public float smallDebrisMaxSize = 3f;

        public List<PlacementPiece> pieces = new List<PlacementPiece>();

        [Tooltip("Generation settings for this catalog. Edited from the Destroyed City Generator window.")]
        public PlacementSettings settings = new PlacementSettings();

        readonly List<PlacementPiece> _buf = new List<PlacementPiece>();

        public void EnsureMeasured(bool force = false)
        {
            for (int i = 0; i < pieces.Count; i++)
                if (pieces[i] != null) pieces[i].EnsureMeasured(force);
        }

        /// <summary>Placeable pieces of one kind, in catalog order. Returned buffer is reused - copy if kept.</summary>
        public List<PlacementPiece> GetKind(PieceKind kind)
        {
            EnsureMeasured();
            _buf.Clear();
            for (int i = 0; i < pieces.Count; i++)
            {
                PlacementPiece p = pieces[i];
                if (p != null && p.IsPlaceable && p.kind == kind) _buf.Add(p);
            }
            return _buf;
        }

        public bool HasPlaceable(PieceKind kind)
        {
            for (int i = 0; i < pieces.Count; i++)
            {
                PlacementPiece p = pieces[i];
                if (p != null && p.IsPlaceable && p.kind == kind) return true;
            }
            return false;
        }

        // --- grouping ---------------------------------------------------------
        //
        // Name-based Kind guessing and size-threshold classification are AUTHORING-only rules, so they
        // live in PlacementCatalogEditor.Authoring.cs (deleted before shipping). End users set each
        // piece's Kind and Size by hand. FamilyKey stays here because the generator (Placer) uses it at
        // placement time to cluster same-type buildings.

        /// <summary>
        /// Family key for grouping: a prefab's name with any trailing "-variant"/"-variation" (+ number)
        /// stripped, so "building6", "building6-variant1" and "building6-variation2" all key to "building6"
        /// while "building7" stays distinct. Used to cluster same-type buildings together.
        /// </summary>
        public static string FamilyKey(string prefabName)
        {
            string n = prefabName.ToLowerInvariant().Trim();
            return System.Text.RegularExpressions.Regex.Replace(n, @"[ _\-]*(variant|variation)\s*\d*$", "").Trim();
        }
    }
}
