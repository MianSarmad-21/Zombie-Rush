using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZombieRush.Audio;
using ZombieRush.Data;

namespace ZombieRush.EditorTools
{
    /// Zombie Rush > Audio > Auto-Fill Sound Library
    ///
    /// Creates Assets/Resources/ZombieRush/SoundLibrary.asset (the SoundManager loads it)
    /// and fills every EMPTY slot by matching audio file names in the project
    /// ("zombie_groan_03", "Pistol Shot", "reload", ...). Slots you filled by hand are
    /// never changed. Also gives each WeaponData its own fire sound when one matches
    /// (pistol / rifle / shotgun). Re-run after importing a new sound pack.
    /// Import settings for Assets/Audio: music streams from disk (a decompressed 2-minute
    /// track would cost ~40 MB of RAM on a phone); short effects load decompressed so they
    /// play with no delay.
    class AudioImportRules : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/Audio/")) return;
            var importer = (AudioImporter)assetImporter;
            var s = importer.defaultSampleSettings;
            if (assetPath.Contains("/Music/"))
            {
                s.loadType = AudioClipLoadType.Streaming;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.6f;
            }
            else
            {
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.ADPCM;
            }
            importer.defaultSampleSettings = s;
        }
    }

    public static class SoundTools
    {
        const string LibraryPath = "Assets/Resources/ZombieRush/SoundLibrary.asset";

        /// A slot matches a clip when ALL words of one rule appear in the clip's name or folder.
        /// Rules are tried in order; earlier rules are better matches.
        static readonly Dictionary<string, string[][]> Rules = new Dictionary<string, string[][]>
        {
            ["menuMusic"] = R("menu music", "main menu", "menu theme", "title music", "menu"),
            ["gameMusic"] = R("battle music", "combat music", "horror music", "action music", "game music", "gameplay music", "ambient music", "music"),
            ["gunshot"] = R("pistol shot", "pistol fire", "handgun", "gunshot", "gun shot", "shot"),
            ["reload"] = R("reload"),
            ["emptyClick"] = R("empty", "dry fire", "dryfire", "out of ammo"),
            ["pickup"] = R("pickup", "pick up", "collect", "item"),
            ["turretShot"] = R("turret shot", "turret fire", "turret", "minigun", "machine gun", "machinegun", "laser"),
            ["turretDeploy"] = R("turret deploy", "deploy", "power up", "powerup", "activate", "spawn"),
            ["turretPowerDown"] = R("power down", "powerdown", "deactivate", "shut down", "shutdown"),
            ["zombieGroan"] = R("zombie groan", "zombie growl", "zombie moan", "zombie idle", "zombie breath", "groan", "growl", "moan"),
            ["zombieAttack"] = R("zombie attack", "zombie bite", "zombie scream", "zombie roar", "bite", "roar"),
            ["zombieHurt"] = R("zombie hurt", "zombie pain", "zombie hit", "zombie damage"),
            ["zombieDeath"] = R("zombie death", "zombie die", "zombie dying", "zombie dead"),
            ["bulletImpact"] = R("bullet impact", "bullet flesh", "flesh impact", "body hit", "flesh", "impact"),
            ["playerHurt"] = R("player hurt", "player pain", "player hit", "male hurt", "male pain", "grunt", "hurt", "pain"),
            ["playerDeath"] = R("player death", "player die", "male death", "male die", "death scream"),
            ["waveStart"] = R("wave start", "horn", "siren", "alarm", "warning"),
            ["waveCleared"] = R("wave clear", "wave complete", "victory", "success", "win", "jingle"),
            ["coin"] = R("coin", "money", "cash"),
            ["zoneExpand"] = R("expand", "level up", "levelup", "upgrade", "unlock"),
            ["denied"] = R("denied", "error", "negative", "fail", "buzz"),
            ["purchase"] = R("purchase", "buy", "cash register", "kaching", "shop"),
            ["gameOver"] = R("game over", "gameover", "defeat", "lose", "lost"),
            ["uiClick"] = R("ui click", "button click", "menu click", "click", "select", "tap"),
        };

        static string[][] R(params string[] rules) => rules.Select(r => r.Split(' ')).ToArray();

        [MenuItem("Zombie Rush/Audio/Auto-Fill Sound Library", priority = 1)]
        public static void AutoFill()
        {
            var lib = EnsureLibrary();
            var clips = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(p => (path: p, key: Key(p), clip: AssetDatabase.LoadAssetAtPath<AudioClip>(p)))
                .Where(c => c.clip != null)
                .ToList();

            Undo.RecordObject(lib, "Auto-fill sound library");
            var filled = new List<string>();
            var empty = new List<string>();
            var so = new SerializedObject(lib);
            foreach (var rule in Rules)
            {
                var prop = so.FindProperty(rule.Key);
                if (prop == null) continue;
                bool isArray = prop.isArray;
                if (isArray ? prop.arraySize > 0 : prop.objectReferenceValue != null) continue; // hand-picked: keep

                var matches = Match(clips, rule.Value, isArray ? 6 : 1, ExcludeFor(rule.Key));
                if (matches.Count == 0) { empty.Add(rule.Key); continue; }

                if (isArray)
                {
                    prop.arraySize = matches.Count;
                    for (int i = 0; i < matches.Count; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = matches[i];
                }
                else prop.objectReferenceValue = matches[0];
                filled.Add($"{rule.Key} <- {string.Join(", ", matches.Select(m => m.name))}");
            }
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(lib);

            // Per-weapon fire / reload sounds.
            foreach (var guid in AssetDatabase.FindAssets("t:WeaponData"))
            {
                var data = AssetDatabase.LoadAssetAtPath<WeaponData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null || (data.fireSounds != null && data.fireSounds.Length > 0)) continue;
                string n = (data.weaponName + " " + data.name).ToLowerInvariant();
                string[][] rules = n.Contains("shotgun") ? R("shotgun shot", "shotgun fire", "shotgun")
                                 : n.Contains("rifle") ? R("rifle shot", "rifle fire", "assault", "rifle", "smg", "automatic", "m4", "ak")
                                 : n.Contains("pistol") ? R("pistol shot", "pistol fire", "pistol", "handgun", "9mm")
                                 : null;
                if (rules == null) continue;
                var shots = Match(clips, rules, 4, new[] { "reload", "empty", "dry", "cock", "shell", "casing" });
                if (shots.Count == 0) continue;
                Undo.RecordObject(data, "Weapon sounds");
                data.fireSounds = shots.ToArray();
                var reload = Match(clips, rules.Select(r => r.Take(1).Append("reload").ToArray()).ToArray(), 1, null);
                if (data.reloadSound == null && reload.Count > 0) data.reloadSound = reload[0];
                EditorUtility.SetDirty(data);
                filled.Add($"{data.name}.fireSounds <- {string.Join(", ", shots.Select(s => s.name))}");
            }
            AssetDatabase.SaveAssets();

            string report = $"Sound library: {LibraryPath}\n{clips.Count} audio clips in the project.\n\n" +
                            (filled.Count > 0 ? "FILLED:\n" + string.Join("\n", filled) : "Nothing new to fill.") +
                            (empty.Count > 0 ? "\n\nSTILL EMPTY (no matching file name):\n" + string.Join(", ", empty) : "");
            Debug.Log("Zombie Rush audio:\n" + report);
            Selection.activeObject = lib;
            EditorUtility.DisplayDialog("Zombie Rush - sounds", report.Length > 1800 ? report.Substring(0, 1800) + "\n... (full list in the Console)" : report, "OK");
        }

        [MenuItem("Zombie Rush/Audio/Select Sound Library", priority = 2)]
        static void SelectLibrary() => Selection.activeObject = EnsureLibrary();

        static SoundLibrary EnsureLibrary()
        {
            var lib = AssetDatabase.LoadAssetAtPath<SoundLibrary>(LibraryPath);
            if (lib != null) return lib;
            UIAssetGenerator.EnsureFolder(Path.GetDirectoryName(LibraryPath).Replace('\\', '/'));
            lib = ScriptableObject.CreateInstance<SoundLibrary>();
            AssetDatabase.CreateAsset(lib, LibraryPath);
            AssetDatabase.SaveAssets();
            return lib;
        }

        /// Words that must NOT be in a clip for this slot (keeps "zombie hurt" out of player hurt, etc.).
        static string[] ExcludeFor(string slot)
        {
            switch (slot)
            {
                case "gunshot": return new[] { "shotgun", "reload", "empty", "dry", "turret", "shell", "casing", "cock" };
                case "playerHurt": case "playerDeath": return new[] { "zombie", "monster", "creature" };
                case "bulletImpact": return new[] { "zombie", "metal", "wood", "glass", "concrete" };
                case "turretShot": return new[] { "reload", "deploy" };
                case "menuMusic": case "gameMusic": return new[] { "sfx", "stinger", "jingle" };
                case "uiClick": return new[] { "gun", "trigger", "empty", "dry" };
                default: return null;
            }
        }

        static List<AudioClip> Match(List<(string path, string key, AudioClip clip)> clips, string[][] rules, int max, string[] exclude)
        {
            foreach (var words in rules)
            {
                var hits = clips.Where(c => words.All(w => HasWord(c.key, w)) && (exclude == null || !exclude.Any(x => HasWord(c.key, x))))
                                .OrderBy(c => c.key.Length) // shorter, more specific names first
                                .Take(max)
                                .Select(c => c.clip)
                                .ToList();
                if (hits.Count > 0) return hits;
            }
            return new List<AudioClip>();
        }

        /// Lower-case file name + parent folder, separators turned into spaces, padded for word tests.
        static string Key(string path)
        {
            string file = Path.GetFileNameWithoutExtension(path);
            string folder = Path.GetFileName(Path.GetDirectoryName(path));
            string s = (folder + " " + file);
            // split CamelCase ("ZombieGroan" -> "Zombie Groan")
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (i > 0 && char.IsUpper(c) && char.IsLower(s[i - 1])) sb.Append(' ');
                sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
            }
            return " " + string.Join(" ", sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) + " ";
        }

        // Whole word, plural or followed by digits ("groans", "groan2"); "ak" won't match "break".
        static bool HasWord(string key, string word)
        {
            int i = 0;
            while ((i = key.IndexOf(" " + word, i, StringComparison.Ordinal)) >= 0)
            {
                int end = i + 1 + word.Length;
                if (end < key.Length && key[end] == 's') end++;
                if (end >= key.Length || key[end] == ' ' || char.IsDigit(key[end])) return true;
                i = end;
            }
            return false;
        }
    }
}
