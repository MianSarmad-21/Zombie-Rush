using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Data;
using ZombieRush.UI;

namespace ZombieRush.EditorTools
{
    /// Builds the zombie health bar + damage number overlay as a prefab in Resources
    /// (ZombieOverheadUI loads it when the first zombie appears), plus its tuning asset.
    /// Rebuilding keeps the tuning asset as it is.
    /// Start from outside the editor with the marker Temp/ZR_RunZombieHud (report: Temp/ZR_ZombieHud.log).
    [InitializeOnLoad]
    public static class ZombieHudTools
    {
        const string ConfigPath = "Assets/Data/ZombieHudConfig.asset";
        const string PrefabPath = "Assets/Resources/" + ZombieOverheadUI.ResourcePath + ".prefab";
        const string FontPath = "Assets/UI/Fonts/Oswald-Bold.ttf";
        const string Marker = "Temp/ZR_RunZombieHud";
        const string Report = "Temp/ZR_ZombieHud.log";
        // Under the gameplay HUD (GameUI is 10), so buttons and the crosshair draw on top.
        const int SortOrder = 5;

        static ZombieHudTools() => EditorApplication.delayCall += TryAutoRun;

        static void TryAutoRun()
        {
            if (!File.Exists(Marker)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryAutoRun;
                return;
            }
            File.Delete(Marker);
            string result;
            try { result = Build(); }
            catch (System.Exception e) { result = "FAILED: " + e; }
            File.WriteAllText(Report, result);
        }

        [MenuItem("Zombie Rush/UI/Build Zombie Health Bars")]
        static void BuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            var config = AssetDatabase.LoadAssetAtPath<ZombieHudConfig>(ConfigPath);
            bool newConfig = config == null;
            if (newConfig)
            {
                config = ScriptableObject.CreateInstance<ZombieHudConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);

            var go = new GameObject("ZombieOverheadUI", typeof(RectTransform));
            try
            {
                go.layer = 5;
                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = SortOrder;
                canvas.pixelPerfect = false;
                var scaler = go.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 1f;
                // No GraphicRaycaster: the overlay never blocks touches or clicks.

                var bars = Stretch(Node("Bars", go.transform));
                var popups = Stretch(Node("Popups", go.transform));

                // Bar: dark back (the border), a Track inset by the border, and the Chip and Fill inside it.
                var bar = Node("BarTemplate", bars);
                bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0.5f);
                bar.pivot = new Vector2(0.5f, 0f);
                bar.sizeDelta = config.barSize;
                bar.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
                Img(bar, config.backColor);
                var track = Stretch(Node("Track", bar));
                track.offsetMin = new Vector2(config.border, config.border);
                track.offsetMax = new Vector2(-config.border, -config.border);
                Img(Stretch(Node("Chip", track)), config.chipColor);
                Img(Stretch(Node("Fill", track)), config.fillColor);
                bar.gameObject.SetActive(false);

                var pop = Node("PopupTemplate", popups);
                pop.anchorMin = pop.anchorMax = new Vector2(0.5f, 0.5f);
                pop.pivot = new Vector2(0.5f, 0f);
                pop.sizeDelta = new Vector2(220f, 70f);
                var text = pop.gameObject.AddComponent<Text>();
                text.font = font;
                text.fontSize = config.fontSize;
                text.color = config.hitColor;
                text.alignment = TextAnchor.LowerCenter;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.raycastTarget = false;
                text.text = "25";
                var outline = pop.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
                outline.effectDistance = new Vector2(2f, -2f);
                pop.gameObject.SetActive(false);

                var ui = go.AddComponent<ZombieOverheadUI>();
                ui.config = config;
                ui.barTemplate = bar;
                ui.popupTemplate = text;

                Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
                PrefabUtility.SaveAsPrefabAsset(go, PrefabPath, out bool saved);
                AssetDatabase.SaveAssets();
                if (!saved) return $"FAILED: could not save {PrefabPath}.";
            }
            finally
            {
                Object.DestroyImmediate(go);
            }

            return $"Zombie health bars built: {PrefabPath}. Tuning: {ConfigPath} ({(newConfig ? "new" : "kept as it was")}). " +
                   $"Font {(font != null ? font.name : "MISSING (" + FontPath + ")")}.";
        }

        static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = 5 };
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static RectTransform Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static void Img(RectTransform rt, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }
    }
}
