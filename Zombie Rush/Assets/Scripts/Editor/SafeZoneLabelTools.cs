using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Data;
using ZombieRush.UI;

namespace ZombieRush.EditorTools
{
    /// Builds the floating "SAFE ZONE" sign as a prefab in Resources (SafeBase spawns it over the
    /// base building) plus its tuning asset, which a rebuild keeps as it is.
    /// Start from outside with the marker Temp/ZR_RunSafeZoneLabel (report: Temp/ZR_SafeZoneLabel.log).
    [InitializeOnLoad]
    public static class SafeZoneLabelTools
    {
        const string ConfigPath = "Assets/Data/SafeZoneLabelConfig.asset";
        const string PrefabPath = "Assets/Resources/" + SafeZoneLabel.ResourcePath + ".prefab";
        const string IconPath = "Assets/UI/IconsPro/iconx_safezone.png"; // drawn by Tools/UI/make_icon_safezone.py
        const string FontPath = "Assets/UI/Fonts/Oswald-Bold.ttf";
        const string Marker = "Temp/ZR_RunSafeZoneLabel";
        const string Report = "Temp/ZR_SafeZoneLabel.log";
        static readonly Vector2 CanvasSize = new Vector2(900f, 260f);
        static double nextCheck;

        static SafeZoneLabelTools() => EditorApplication.update += Poll;

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextCheck) return;
            nextCheck = EditorApplication.timeSinceStartup + 2.0;
            if (!File.Exists(Marker)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            File.Delete(Marker);
            string result;
            try { result = Build(); }
            catch (System.Exception e) { result = "FAILED: " + e; }
            File.WriteAllText(Report, result);
        }

        [MenuItem("Zombie Rush/UI/Build Safe Zone Label")]
        static void BuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            var config = AssetDatabase.LoadAssetAtPath<SafeZoneLabelConfig>(ConfigPath);
            bool newConfig = config == null;
            if (newConfig)
            {
                config = ScriptableObject.CreateInstance<SafeZoneLabelConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            var icon = ImportIcon();
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            var plate = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            var go = new GameObject("SafeZoneLabel", typeof(RectTransform));
            try
            {
                go.layer = 5;
                var rt = (RectTransform)go.transform;
                rt.sizeDelta = CanvasSize;
                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                var group = go.AddComponent<CanvasGroup>();
                group.blocksRaycasts = false;
                group.interactable = false;

                var panel = Node("Plate", rt);
                Stretch(panel);
                var panelImg = panel.gameObject.AddComponent<Image>();
                panelImg.sprite = plate;
                panelImg.type = Image.Type.Sliced;
                panelImg.pixelsPerUnitMultiplier = 0.2f; // big rounded corners on a big plate
                panelImg.color = config.panelColor;
                panelImg.raycastTarget = false;

                var accent = Node("Accent", rt);
                accent.anchorMin = new Vector2(0.06f, 0f);
                accent.anchorMax = new Vector2(0.94f, 0f);
                accent.pivot = new Vector2(0.5f, 0f);
                accent.anchoredPosition = new Vector2(0f, 14f);
                accent.sizeDelta = new Vector2(0f, 10f);
                var accentImg = accent.gameObject.AddComponent<Image>();
                accentImg.color = config.accentColor;
                accentImg.raycastTarget = false;

                var iconRt = Node("Icon", rt);
                iconRt.anchorMin = iconRt.anchorMax = new Vector2(0f, 0.5f);
                iconRt.pivot = new Vector2(0f, 0.5f);
                iconRt.anchoredPosition = new Vector2(34f, 4f);
                iconRt.sizeDelta = new Vector2(210f, 210f);
                var iconImg = iconRt.gameObject.AddComponent<Image>();
                iconImg.sprite = icon;
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;

                var textRt = Node("Title", rt);
                textRt.anchorMin = new Vector2(0f, 0f);
                textRt.anchorMax = new Vector2(1f, 1f);
                textRt.offsetMin = new Vector2(262f, 20f);
                textRt.offsetMax = new Vector2(-30f, -10f);
                var text = textRt.gameObject.AddComponent<Text>();
                text.font = font;
                text.text = config.text;
                text.fontSize = 132;
                text.alignment = TextAnchor.MiddleCenter;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.color = config.textColor;
                text.raycastTarget = false;
                var outline = textRt.gameObject.AddComponent<Outline>();
                outline.effectColor = config.outlineColor;
                outline.effectDistance = new Vector2(5f, -5f);

                var label = go.AddComponent<SafeZoneLabel>();
                label.config = config;
                label.group = group;
                label.title = text;
                label.canvasHeight = CanvasSize.y;
                rt.localScale = Vector3.one * (config.worldHeight / CanvasSize.y);

                Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
                PrefabUtility.SaveAsPrefabAsset(go, PrefabPath, out bool saved);
                AssetDatabase.SaveAssets();
                if (!saved) return $"FAILED: could not save {PrefabPath}.";
            }
            finally
            {
                Object.DestroyImmediate(go);
            }

            return $"Safe zone label built: {PrefabPath}. Tuning: {ConfigPath} ({(newConfig ? "new" : "kept as it was")}). " +
                   $"Icon {(icon != null ? icon.name : "MISSING (" + IconPath + ")")}, font {(font != null ? font.name : "MISSING")}.";
        }

        /// The icon is seen in the world at many sizes: import it as a sprite with mipmaps.
        static Sprite ImportIcon()
        {
            var importer = AssetImporter.GetAtPath(IconPath) as TextureImporter;
            if (importer == null) return null;
            if (importer.textureType != TextureImporterType.Sprite || !importer.mipmapEnabled || !importer.alphaIsTransparency)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = true;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
        }

        static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = 5 };
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
