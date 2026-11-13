using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZombieRush.Core;
using ZombieRush.Net;
using ZombieRush.UI;
using ZombieRush.UI.Kit;

namespace ZombieRush.EditorTools
{
    /// Builds the "Outbreak" UI from the art in Assets/UI/Outbreak (Tools/UI/make_outbreak_art.py) and the
    /// icons in Assets/UI/Outbreak/Icons (Tools/UI/make_outbreak_icons.py):
    ///   Assets/UI/Kit/Prefabs/KitGameUI.prefab : HUD, touch controls, wave banner, pause, options, game over
    ///                                            (placed in the open scene, replacing the old UI);
    ///   Assets/Resources/ZombieRush/UI/Lobby.prefab : the first screen (Solo / Host / Join).
    /// The look: paint and blood on a dark night. Brush strokes carry the numbers and buttons, a big painted
    /// wave number sits in a corner, menus are dark screens with a blood vignette.
    /// Both are ordinary prefabs: open and edit them in the editor afterwards. Re-running rebuilds them.
    /// Marker: Temp/ZR_RunMapCleanup with "kitui".
    public static class KitUIBuilder
    {
        const string Art = "Assets/UI/Outbreak";
        const string IconDir = "Assets/UI/Outbreak/Icons";
        const string PrefabDir = "Assets/UI/Kit/Prefabs";
        const string GamePrefab = PrefabDir + "/KitGameUI.prefab";
        const string LobbyPrefab = "Assets/Resources/ZombieRush/UI/Lobby.prefab";
        const string StylePath = "Assets/Resources/ZombieRush/UI/KitStyle.asset";

        static KitStyle S;
        static Font Title, Num;

        [MenuItem("Zombie Rush/UI/Build Outbreak UI (open scene)", priority = 0)]
        static void Menu() => Debug.Log("Zombie Rush: " + Run());

        // ------------------------------------------------------------------ sprites
        static readonly Dictionary<string, Vector4> Borders = new Dictionary<string, Vector4>
        {   // left, bottom, right, top (pixels of the source image)
            { "stroke", new Vector4(180, 0, 180, 0) }, { "stroke_b", new Vector4(180, 0, 180, 0) },
            { "line", new Vector4(120, 0, 120, 0) }, { "panel", new Vector4(96, 96, 96, 96) },
            { "bar", new Vector4(24, 0, 24, 0) }, { "key", new Vector4(32, 32, 32, 32) },
        };

        static void ImportSprites()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Art, IconDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                string n = System.IO.Path.GetFileNameWithoutExtension(path);
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.maxTextureSize = n == "backdrop" ? 2048 : 1024;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.spriteBorder = Borders.TryGetValue(n, out var v) ? v : Vector4.zero;
                ti.SaveAndReimport();
            }
        }

        static Sprite Sp(string name)
        {
            string path = name.StartsWith("ic_") ? $"{IconDir}/{name}.png" : $"{Art}/{name}.png";
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (s == null) Debug.LogWarning("Outbreak UI: missing sprite " + path);
            return s;
        }

        // ------------------------------------------------------------------ helpers
        static GameObject Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            return go;
        }

        static RectTransform Place(GameObject go, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot; rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        static RectTransform Stretch(GameObject go, float l = 0, float b = 0, float r = 0, float t = 0)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }

        static Image Img(Transform parent, string name, Sprite sprite, Color color, bool sliced = false)
        {
            var go = Node(name, parent);
            var img = go.AddComponent<Image>();
            img.sprite = sprite; img.color = color; img.raycastTarget = false;
            if (sliced && sprite != null && sprite.border != Vector4.zero) img.type = Image.Type.Sliced;
            return img;
        }

        static Text Txt(Transform parent, string name, string text, Font font, int size, Color color, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var go = Node(name, parent);
            var t = go.AddComponent<Text>();
            t.font = font; t.text = text; t.fontSize = size; t.color = color; t.alignment = align;
            t.raycastTarget = false; t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        static void Shade(Graphic g, float a = 0.8f, float d = 3f)
        {
            var sh = g.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, a); sh.effectDistance = new Vector2(d, -d);
        }

        static void OwnCanvas(GameObject go)
        {
            go.AddComponent<Canvas>();
            go.AddComponent<GraphicRaycaster>();
        }

        static Color A(Color c, float a) { c.a = a; return c; }

        static void Tilt(Component c, float deg) => c.transform.localRotation = Quaternion.Euler(0, 0, deg);

        /// A rough painted bar: dark back, filled front.
        static Image Bar(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color fill)
        {
            var back = Img(parent, name, Sp("bar"), A(Color.black, 0.6f), true);
            Place(back.gameObject, anchor, pivot, pos, size);
            var f = Img(back.transform, "Fill", Sp("bar"), fill);
            Stretch(f.gameObject, 2, 2, 2, 2);
            f.type = Image.Type.Filled; f.fillMethod = Image.FillMethod.Horizontal; f.fillOrigin = 0; f.fillAmount = 1f;
            return f;
        }

        /// A brush-stroke button: a painted stroke behind a Bebas label, with an optional icon.
        static Button StrokeBtn(Transform parent, string name, string label, string icon, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size,
                                Color paint, int fontSize = 56, bool center = false, string sprite = "stroke")
        {
            var go = Node(name, parent);
            Place(go, anchor, pivot, pos, size);
            var hit = go.AddComponent<EmptyGraphic>(); hit.raycastTarget = true;
            var content = Node("Content", go.transform); Stretch(content);
            var ink = Img(content.transform, "Paint", Sp(sprite), paint, true); Stretch(ink.gameObject, -size.x * 0.04f, -size.y * 0.06f, -size.x * 0.04f, -size.y * 0.06f);
            float x = size.y * 0.45f;
            if (!string.IsNullOrEmpty(icon))
            {
                var ic = Img(content.transform, "Icon", Sp("ic_" + icon), S.bone);
                Place(ic.gameObject, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(size.y * 0.62f, 0f), Vector2.one * size.y * 0.44f);
                x = size.y * 1.05f;
            }
            var t = Txt(content.transform, "Label", label, Num, fontSize, S.bone, center ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft);
            Stretch(t.gameObject, center ? 10 : x, 0, 10, 0);
            Shade(t, 0.6f, 2f);
            var b = go.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            var fx = go.AddComponent<StampButton>();
            fx.content = (RectTransform)content.transform; fx.ink = ink;
            fx.inkNormal = paint; fx.inkHover = Color.Lerp(paint, S.blood, paint == S.blood ? 0f : 0.75f); fx.inkPressed = Color.Lerp(paint, Color.black, 0.35f);
            if (paint == S.blood) fx.inkHover = Color.Lerp(S.blood, Color.white, 0.15f);
            fx.restTilt = 0f; fx.hoverTilt = -1f;
            return b;
        }

        /// A glass touch disc: icon in the middle, a Bebas caption under it. Fire gets a blood ring and splat.
        static TouchButton Disc(Transform parent, string name, string caption, string icon, float size, Vector2 pos, Color accent, TouchAction action, bool fire = false)
        {
            var go = Node(name, parent);
            Place(go, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), pos, new Vector2(size, size));
            if (fire)
            {
                var sp = Img(go.transform, "Splat", Sp("splat"), A(S.ink, 0.75f));
                Place(sp.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * size * 1.45f); Tilt(sp, 30f);
            }
            var disc = Img(go.transform, "Disc", Sp("disc"), Color.white);
            Stretch(disc.gameObject); disc.raycastTarget = true;
            if (fire) { var r = Img(go.transform, "Ring", Sp("ring"), S.blood); Stretch(r.gameObject); }
            var glyph = Img(go.transform, "Icon", Sp("ic_" + icon), accent);
            Place(glyph.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, size * (fire ? 0.07f : 0.1f)), Vector2.one * size * (fire ? 0.34f : 0.38f));
            var cap = Txt(go.transform, "Caption", caption, Num, Mathf.RoundToInt(Mathf.Clamp(size * 0.18f, 20f, 40f)), A(S.bone, 0.95f));
            Place(cap.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, size * (fire ? 0.22f : 0.2f)), new Vector2(size * 1.3f, size * 0.24f));
            Shade(cap, 0.8f, 2f);
            var btn = go.AddComponent<TouchButton>();
            btn.action = action; btn.visual = glyph;
            btn.normalColor = accent; btn.pressedColor = Color.white; btn.toggledColor = S.radar; btn.pressedScale = 0.88f;
            return btn;
        }

        // ------------------------------------------------------------------ run
        public static string Run()
        {
            System.IO.Directory.CreateDirectory(PrefabDir);
            System.IO.Directory.CreateDirectory("Assets/Resources/ZombieRush/UI");
            S = AssetDatabase.LoadAssetAtPath<KitStyle>(StylePath);
            if (S == null) { S = ScriptableObject.CreateInstance<KitStyle>(); AssetDatabase.CreateAsset(S, StylePath); }
            ImportSprites();
            Title = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/RubikDirt-Regular.ttf");
            Num = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/BebasNeue-Regular.ttf");
            if (Title == null || Num == null) return "Outbreak UI: FAILED, fonts missing in Assets/UI/Fonts.";
            Random.InitState(5);

            string removed = RemoveOldUI();
            EnsureEventSystem();

            // ---- the gameplay UI
            var root = new GameObject("KitGameUI", typeof(RectTransform)); root.layer = 5;
            var canvas = root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10; canvas.pixelPerfect = false;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; scaler.matchWidthOrHeight = 1f;
            root.AddComponent<GraphicRaycaster>();

            var safe = Node("SafeArea", root.transform); Stretch(safe); safe.AddComponent<SafeAreaFitter>();
            var hud = BuildHUD(safe.transform);
            ZombieRushUIBuilder.BuildKeptPieces(root.transform, hud.transform);
            BuildTouch(safe.transform);
            BuildBanner(root.transform);
            var options = BuildOptions(root.transform);
            BuildPause(root.transform, options);
            BuildGameOver(root.transform);
            options.transform.SetAsLastSibling();

            root.AddComponent<HideInLobby>();
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, GamePrefab, InteractionMode.AutomatedAction);
            var scene = root.scene;

            // ---- the lobby
            var lobby = BuildLobby();
            PrefabUtility.SaveAsPrefabAsset(lobby, LobbyPrefab);
            Object.DestroyImmediate(lobby);

            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            return $"Outbreak UI built: {GamePrefab} placed in the scene{(removed.Length > 0 ? $" (removed old: {removed})" : "")}, {LobbyPrefab}. Scene {(saved ? "saved" : "NOT saved")}.";
        }

        static string RemoveOldUI()
        {
            var roots = new HashSet<GameObject>();
            void Collect<T>() where T : Component
            {
                foreach (var c in Object.FindObjectsByType<T>(FindObjectsInactive.Include))
                {
                    if (!c.gameObject.scene.IsValid()) continue;
                    var cv = c.GetComponentInParent<Canvas>(true);
                    roots.Add(cv != null ? cv.rootCanvas.gameObject : c.gameObject);
                }
            }
            Collect<HUDController>(); Collect<KitHUD>(); Collect<DamageFlashUI>(); Collect<WaveBannerUI>();
            Collect<GameOverUI>(); Collect<PauseMenuUI>(); Collect<TouchControlsUI>();
            var names = roots.Select(r => r.name).ToList();
            foreach (var go in roots) Object.DestroyImmediate(go);
            return string.Join(", ", names);
        }

        static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include) != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        // ================================================================== HUD
        static GameObject BuildHUD(Transform parent)
        {
            var go = Node("HUD", parent); Stretch(go);
            var hud = go.AddComponent<KitHUD>();
            var layout = go.AddComponent<ResponsiveLayout>();
            var targets = new List<RectTransform>(); var pc = new List<RectPose>(); var touch = new List<RectPose>();
            void Pose(RectTransform rt, Vector2 touchAnchor, Vector2 touchPivot, Vector2 touchPos, float touchScale = 1f)
            {
                targets.Add(rt); pc.Add(RectPose.Of(rt));
                touch.Add(new RectPose { anchorMin = touchAnchor, anchorMax = touchAnchor, pivot = touchPivot, anchoredPosition = touchPos, scale = touchScale });
            }

            // ---- vitals, top left: a dark brush stroke with the heart, the health number, an ECG trace and the bar
            var vit = Node("Vitals", go.transform);
            Place(vit, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -28), new Vector2(560, 130));
            var vBack = Img(vit.transform, "Paint", Sp("stroke"), S.charcoal, true); Stretch(vBack.gameObject); Tilt(vBack, -0.8f);
            var heart = Img(vit.transform, "Heart", Sp("ic_health"), S.blood);
            Place(heart.gameObject, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(78, 2), new Vector2(64, 64));
            hud.healthText = Txt(vit.transform, "Health", "100", Num, 92, S.bone, TextAnchor.MiddleRight);
            Place(hud.healthText.gameObject, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(252, 4), new Vector2(150, 100));
            Shade(hud.healthText);
            hud.healthMaxText = Txt(vit.transform, "HealthMax", "/100", Num, 34, S.muted, TextAnchor.MiddleLeft);
            Place(hud.healthMaxText.gameObject, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(258, -20), new Vector2(90, 40));
            var beatGo = Node("Heartbeat", vit.transform); OwnCanvas(beatGo);
            Place(beatGo, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(344, 16), new Vector2(180, 46));
            hud.heartbeat = beatGo.AddComponent<HeartbeatGraphic>(); hud.heartbeat.thickness = 3f;
            hud.healthBar = Bar(vit.transform, "HealthBar", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(344, -24), new Vector2(180, 18), S.bone);
            // armour: a thin bar under the vitals, only while there is armour
            var arm = Node("Armor", go.transform);
            Place(arm, new Vector2(0, 1), new Vector2(0, 1), new Vector2(96, -158), new Vector2(330, 28));
            var armIc = Img(arm.transform, "Icon", Sp("ic_armor"), S.armor);
            Place(armIc.gameObject, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(14, 0), new Vector2(26, 26));
            hud.armorBar = Bar(arm.transform, "Bar", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(36, 0), new Vector2(280, 12), S.armor);
            hud.armorRoot = arm;

            // ---- pockets: scrap and kills, just icon + number
            var pockets = Node("Pockets", go.transform);
            Place(pockets, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -192), new Vector2(360, 64));
            var pBack = Img(pockets.transform, "Paint", Sp("stroke_b"), S.charcoal, true); Stretch(pBack.gameObject, -40, -6, -10, -6); Tilt(pBack, 0.6f);
            RectTransform Pocket(string n, string icon, Color ic, float x, out Text value)
            {
                var c = Node(n, pockets.transform);
                Place(c, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(160, 64));
                var i = Img(c.transform, "Icon", Sp("ic_" + icon), ic);
                Place(i.gameObject, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(22, 0), new Vector2(42, 42));
                Shade(i, 0.7f, 2f);
                value = Txt(c.transform, "Value", "0", Num, 56, S.bone, TextAnchor.MiddleLeft);
                Place(value.gameObject, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(52, 2), new Vector2(110, 64));
                Shade(value);
                return (RectTransform)c.transform;
            }
            hud.scrapPunch = Pocket("Scrap", "scrap", S.amber, 0, out hud.scrapText);
            hud.killsPunch = Pocket("Kills", "skull", S.bone, 170, out hud.killsText);

            // ---- the wave: a big painted number on a dark splat, bottom left (touch: top centre, smaller)
            var wave = Node("Wave", go.transform);
            Place(wave, new Vector2(0, 0), new Vector2(0, 0), new Vector2(34, 30), new Vector2(380, 270));
            var wSplat = Img(wave.transform, "Splat", Sp("splat_b"), A(S.charcoal, 0.62f));
            Place(wSplat.gameObject, new Vector2(0, 0), new Vector2(0.5f, 0.5f), new Vector2(150, 140), new Vector2(330, 330)); Tilt(wSplat, 24f);
            var wl = Txt(wave.transform, "Label", "WAVE", Num, 44, S.bone, TextAnchor.MiddleLeft);
            Place(wl.gameObject, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -8), new Vector2(200, 48));
            Shade(wl);
            hud.waveText = Txt(wave.transform, "Number", "", Title, 176, S.blood, TextAnchor.MiddleLeft);
            Place(hud.waveText.gameObject, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(30, 12), new Vector2(340, 190));
            Shade(hud.waveText, 0.95f, 5f);
            hud.waveSlap = wave.AddComponent<SlapIn>(); hud.waveSlap.playOnEnable = false; hud.waveSlap.restAngle = 0f; hud.waveSlap.fromAngle = -8f;
            var leftRow = Node("Left", wave.transform);
            Place(leftRow, new Vector2(0, 0), new Vector2(0, 0), new Vector2(36, 8), new Vector2(330, 50));
            var lIc = Img(leftRow.transform, "Skull", Sp("ic_skull"), S.bone);
            Place(lIc.gameObject, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(18, 0), new Vector2(34, 34));
            hud.leftText = Txt(leftRow.transform, "Text", "GEAR UP", Num, 42, S.bone, TextAnchor.MiddleLeft);
            Place(hud.leftText.gameObject, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(44, 2), new Vector2(280, 50));
            Shade(hud.leftText);
            hud.tapeSlap = leftRow.AddComponent<SlapIn>(); hud.tapeSlap.playOnEnable = false; hud.tapeSlap.restAngle = 0f; hud.tapeSlap.fromAngle = 4f;
            Pose((RectTransform)wave.transform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -6), 0.7f);

            // ---- radar (zone), top right: a glass disc with a tick ring
            var rad = Node("Radar", go.transform);
            Place(rad, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-34, -28), new Vector2(180, 222));
            var rDisc = Img(rad.transform, "Disc", Sp("disc"), Color.white);
            Place(rDisc.gameObject, new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(180, 180));
            var rTicks = Img(rDisc.transform, "Ticks", Sp("ring_ticks"), A(S.bone, 0.5f)); Stretch(rTicks.gameObject);
            var radGo = Node("Scope", rDisc.transform); Stretch(radGo, 18, 18, 18, 18); OwnCanvas(radGo);
            hud.radar = radGo.AddComponent<RadarGraphic>(); hud.radar.color = S.radar;
            hud.zoneText = Txt(rad.transform, "Zone", "ZONE", Num, 36, S.radar, TextAnchor.MiddleCenter);
            Place(hud.zoneText.gameObject, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, -2), new Vector2(200, 40));
            Shade(hud.zoneText);
            Pose((RectTransform)rad.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-160, -28));

            // ---- weapon, bottom right: gun silhouette, big rounds number, reserve, the magazine as cartridges
            var wpn = Node("Weapon", go.transform);
            Place(wpn, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-30, 26), new Vector2(520, 190));
            var wBack = Img(wpn.transform, "Paint", Sp("stroke_b"), S.charcoal, true);
            Place(wBack.gameObject, new Vector2(0.5f, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(520, 136)); Tilt(wBack, 0.8f);
            hud.weaponIcon = Img(wpn.transform, "Icon", null, S.bone); hud.weaponIcon.preserveAspect = true;
            Place(hud.weaponIcon.gameObject, new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(44, 68), new Vector2(170, 76));
            hud.weaponName = Txt(wpn.transform, "Name", "PISTOL", Num, 40, S.amber, TextAnchor.MiddleLeft);
            Place(hud.weaponName.gameObject, new Vector2(0, 1), new Vector2(0, 1), new Vector2(44, -2), new Vector2(240, 46));
            Shade(hud.weaponName);
            hud.magazineText = Txt(wpn.transform, "Mag", "12", Num, 128, S.bone, TextAnchor.MiddleRight);
            Place(hud.magazineText.gameObject, new Vector2(1, 0), new Vector2(1, 0.5f), new Vector2(-104, 70), new Vector2(200, 130));
            Shade(hud.magazineText, 0.9f, 4f);
            var resIc = Img(wpn.transform, "ReserveIcon", Sp("bullet"), Color.white); resIc.preserveAspect = true;
            Place(resIc.gameObject, new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(-88, 48), new Vector2(14, 34));
            hud.reserveText = Txt(wpn.transform, "Reserve", "60", Num, 44, S.muted, TextAnchor.MiddleLeft);
            Place(hud.reserveText.gameObject, new Vector2(1, 0), new Vector2(0, 0.5f), new Vector2(-76, 48), new Vector2(76, 46));
            var magGo = Node("Magazine", wpn.transform);
            Place(magGo, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-30, -8), new Vector2(250, 32));
            hud.magazine = magGo.AddComponent<MagazineView>(); hud.magazine.bullet = Sp("bullet"); hud.magazine.rowWidth = 250f;
            hud.magazine.spent = new Color(0.1f, 0.1f, 0.1f, 0.7f);
            var rl = Node("Reload", wpn.transform);
            Place(rl, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-30, -8), new Vector2(250, 32));
            hud.reloadBar = Bar(rl.transform, "Bar", new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(250, 26), S.amber);
            var rlT = Txt(rl.transform, "Text", "RELOADING", Num, 28, S.charcoal); Stretch(rlT.gameObject);
            hud.reloadRoot = rl;
            Pose((RectTransform)wpn.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(40, 10), 0.8f);

            // ---- PC key hints, right side above the weapon: expand zone, turret, view
            BuildHints(go.transform, out var pcHints);
            // touch expand prompt (top left, under the pockets)
            var tExp = TouchExpand(go.transform);

            layout.targets = targets.ToArray(); layout.pcPoses = pc.ToArray(); layout.touchPoses = touch.ToArray();
            layout.pcOnly = pcHints; layout.touchOnly = new[] { tExp };
            return go;
        }

        static void BuildHints(Transform parent, out GameObject[] pcOnly)
        {
            var list = new List<GameObject>();
            GameObject Hint(string name, string key, string icon, float y, out Text label, out Image iconImg)
            {
                var holder = Node(name, parent);
                Place(holder, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40, y), new Vector2(400, 62));
                var content = Node("Content", holder.transform); Stretch(content);
                var bg = Img(content.transform, "Paint", Sp("stroke"), S.charcoal, true); Stretch(bg.gameObject, -20, 0, -10, 0);
                var cap = Img(content.transform, "Key", Sp("key"), Color.white, true);
                Place(cap.gameObject, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(50, 50));
                var k = Txt(cap.transform, "K", key, Num, 38, S.bone); Stretch(k.gameObject, 0, 0, 0, -3);
                iconImg = Img(content.transform, "Icon", Sp("ic_" + icon), S.bone);
                Place(iconImg.gameObject, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(92, 0), new Vector2(34, 34));
                label = Txt(content.transform, "Label", "", Num, 36, S.bone, TextAnchor.MiddleLeft);
                Place(label.gameObject, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(120, 2), new Vector2(270, 50));
                Shade(label, 0.7f, 2f);
                list.Add(holder);
                return holder;
            }
            var exp = Hint("ExpandHint", "P", "expand", 232, out var expLabel, out var expIcon);
            var ep = exp.AddComponent<ExpandPromptUI>(); ep.content = exp.transform.Find("Content").gameObject; ep.label = expLabel; ep.frame = expIcon;
            ep.showOn = SchemeFilter.KeyboardMouseOnly; ep.format = "EXPAND ZONE  {0} SCRAP";
            var tur = Hint("TurretHint", "T", "turret", 300, out var turLabel, out var turIcon);
            var tu = tur.AddComponent<TurretButtonUI>(); tu.content = tur.transform.Find("Content").gameObject; tu.label = turLabel;
            tu.showOn = SchemeFilter.KeyboardMouseOnly; tu.icon = turIcon;
            var sweep = Img(turIcon.transform, "Cooldown", Sp("disc"), new Color(0, 0, 0, 0.7f)); Stretch(sweep.gameObject, -6, -6, -6, -6);
            sweep.type = Image.Type.Filled; sweep.fillMethod = Image.FillMethod.Radial360; sweep.fillOrigin = (int)Image.Origin360.Top; sweep.fillClockwise = false; sweep.fillAmount = 0f;
            tu.cooldownFill = sweep;
            var lockB = Img(turIcon.transform, "Lock", Sp("ic_lock"), S.blood); Place(lockB.gameObject, new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 20));
            tu.lockBadge = lockB.gameObject;
            var view = Hint("ViewHint", "V", "eye", 368, out var viewLabel, out _);
            var vu = view.AddComponent<ViewToggleButtonUI>(); vu.content = view.transform.Find("Content").gameObject; vu.label = viewLabel; vu.showOn = SchemeFilter.KeyboardMouseOnly;
            pcOnly = list.ToArray();
        }

        static GameObject TouchExpand(Transform parent)
        {
            var holder = Node("ExpandTouch", parent);
            Place(holder, new Vector2(0, 1), new Vector2(0, 1), new Vector2(44, -270), new Vector2(290, 84));
            var content = Node("Content", holder.transform); Stretch(content);
            var bg = Img(content.transform, "Paint", Sp("stroke"), S.charcoal, true); Stretch(bg.gameObject, -16, 0, -16, 0); bg.raycastTarget = true;
            var ic = Img(content.transform, "Icon", Sp("ic_expand"), S.amber);
            Place(ic.gameObject, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(42, 0), new Vector2(46, 46));
            var label = Txt(content.transform, "Label", "EXPAND\n20 SCRAP", Num, 34, S.bone, TextAnchor.MiddleLeft);
            label.lineSpacing = 0.85f;
            Place(label.gameObject, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(80, 0), new Vector2(200, 80));
            var b = content.AddComponent<TouchButton>(); b.action = TouchAction.ExpandZone; b.visual = ic; b.normalColor = S.amber; b.pressedColor = Color.white;
            var ep = holder.AddComponent<ExpandPromptUI>(); ep.content = content; ep.label = label; ep.frame = ic; ep.showOn = SchemeFilter.TouchOnly; ep.format = "EXPAND\n{0} SCRAP";
            content.SetActive(false);
            return holder;
        }

        // ================================================================== touch controls
        static void BuildTouch(Transform parent)
        {
            var go = Node("TouchControls", parent); Stretch(go); OwnCanvas(go);
            var ui = go.AddComponent<TouchControlsUI>();
            var controls = Node("Controls", go.transform); Stretch(controls);
            ui.controlsRoot = controls;

            var look = Node("LookArea", controls.transform);
            var lrt = (RectTransform)look.transform; lrt.anchorMin = new Vector2(0.42f, 0); lrt.anchorMax = Vector2.one; lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            look.AddComponent<EmptyGraphic>().raycastTarget = true; look.AddComponent<TouchLookArea>();

            var move = Node("MoveArea", controls.transform);
            var mrt = (RectTransform)move.transform; mrt.anchorMin = Vector2.zero; mrt.anchorMax = new Vector2(0.42f, 0.7f); mrt.offsetMin = mrt.offsetMax = Vector2.zero;
            move.AddComponent<EmptyGraphic>().raycastTarget = true;
            var stick = move.AddComponent<VirtualJoystick>();
            var sb = Node("StickBase", move.transform);
            Place(sb, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(270, 230), new Vector2(290, 290));
            stick.visualGroup = sb.AddComponent<CanvasGroup>(); stick.visualGroup.blocksRaycasts = false;
            var baseDisc = Img(sb.transform, "Glass", Sp("disc"), A(Color.white, 0.55f)); Stretch(baseDisc.gameObject);
            var baseImg = Img(sb.transform, "Ticks", Sp("ring_ticks"), A(S.bone, 0.6f)); Stretch(baseImg.gameObject);
            var knob = Img(sb.transform, "Knob", Sp("disc"), Color.white);
            Place(knob.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120, 120));
            var knobRing = Img(knob.transform, "Ring", Sp("ring"), A(S.blood, 0.9f)); Stretch(knobRing.gameObject);
            stick.stickBase = (RectTransform)sb.transform; stick.knob = knob.rectTransform; stick.radius = 116f;

            var fire = Disc(controls.transform, "Fire", "FIRE", "fire", 250, new Vector2(-215, 225), S.bone, TouchAction.Fire, true);
            fire.dragToLook = true;
            Disc(controls.transform, "Reload", "RELOAD", "reload", 136, new Vector2(-470, 110), S.amber, TouchAction.Reload);
            Disc(controls.transform, "Jump", "JUMP", "jump", 136, new Vector2(-462, 330), S.bone, TouchAction.Jump);
            Disc(controls.transform, "Crouch", "CROUCH", "crouch", 124, new Vector2(-270, 485), S.bone, TouchAction.Crouch);
            Disc(controls.transform, "Aim", "AIM", "aim", 124, new Vector2(-440, 560), S.bone, TouchAction.Aim);
            Disc(controls.transform, "Swap", "SWAP", "swap", 110, new Vector2(-640, 230), S.bone, TouchAction.SwapWeapon);
            Disc(controls.transform, "Shoulder", "SIDE", "shoulder", 98, new Vector2(-92, 805), S.bone, TouchAction.ShoulderSwap);

            var turH = Node("TurretTouch", controls.transform); Stretch(turH);
            var tb = Disc(turH.transform, "Turret", "TURRET", "turret", 130, new Vector2(-92, 478), S.amber, TouchAction.Turret);
            var tu = turH.AddComponent<TurretButtonUI>();
            tu.content = tb.gameObject; tu.showOn = SchemeFilter.TouchOnly; tu.label = tb.transform.Find("Caption").GetComponent<Text>();
            tu.icon = tb.transform.Find("Icon").GetComponent<Image>();
            var sweep = Img(tb.transform, "Cooldown", Sp("disc"), new Color(0, 0, 0, 0.65f)); Stretch(sweep.gameObject, 4, 4, 4, 4);
            sweep.type = Image.Type.Filled; sweep.fillMethod = Image.FillMethod.Radial360; sweep.fillOrigin = (int)Image.Origin360.Top; sweep.fillClockwise = false; sweep.fillAmount = 0f;
            tu.cooldownFill = sweep;
            var lockB = Img(tb.transform, "Lock", Sp("ic_lock"), S.blood); Place(lockB.gameObject, new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-14, -14), new Vector2(34, 34));
            tu.lockBadge = lockB.gameObject;

            var viewH = Node("ViewTouch", controls.transform); Stretch(viewH);
            var vu = viewH.AddComponent<ViewToggleButtonUI>();
            var vb = Disc(viewH.transform, "View", vu.toThirdPersonText, "eye", 112, new Vector2(-92, 650), S.bone, TouchAction.ToggleView);
            vu.content = vb.gameObject; vu.label = vb.transform.Find("Caption").GetComponent<Text>(); vu.showOn = SchemeFilter.TouchOnly;

            var pause = Node("Pause", controls.transform);
            Place(pause, Vector2.one, Vector2.one, new Vector2(-28, -26), new Vector2(104, 104));
            var pBg = Img(pause.transform, "Disc", Sp("disc"), Color.white); Stretch(pBg.gameObject); pBg.raycastTarget = true;
            var pIc = Img(pause.transform, "Icon", Sp("ic_pause"), S.bone); Place(pIc.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(44, 44));
            var pBtn = pause.AddComponent<TouchButton>(); pBtn.action = TouchAction.Pause; pBtn.visual = pIc; pBtn.normalColor = S.bone; pBtn.pressedColor = S.blood;

            controls.SetActive(false);
        }

        // ================================================================== wave banner: a dark brush smear, claw marks, a coloured subtitle stroke
        static void BuildBanner(Transform parent)
        {
            var go = Node("WaveBanner", parent);
            Place(go, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -330), new Vector2(1300, 380));
            OwnCanvas(go);
            var ui = go.AddComponent<WaveBannerUI>();
            ui.group = go.AddComponent<CanvasGroup>(); ui.group.blocksRaycasts = false; ui.group.alpha = 0f;
            var anim = Node("AnimRoot", go.transform); Stretch(anim);
            ui.animRoot = (RectTransform)anim.transform;
            Tilt(anim.transform, -1.5f);
            var claw = Img(anim.transform, "Claw", Sp("claw"), A(S.blood, 0.7f));
            Place(claw.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(390, 40), new Vector2(330, 330)); Tilt(claw, -8f);
            var back = Img(anim.transform, "Paint", Sp("stroke"), S.charcoal, true);
            Place(back.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 50), new Vector2(980, 210));
            ui.titleText = Txt(anim.transform, "Title", "WAVE 1", Title, 150, S.bone);
            Place(ui.titleText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 54), new Vector2(1000, 190));
            Shade(ui.titleText, 0.9f, 5f);
            var sub = Img(anim.transform, "SubPaint", Sp("stroke_b"), S.blood, true);
            Place(sub.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -88), new Vector2(560, 100)); Tilt(sub, 2f);
            ui.subtitleText = Txt(sub.transform, "Subtitle", "INCOMING", Num, 58, S.bone); Stretch(ui.subtitleText.gameObject, 0, 0, 0, -4);
            Shade(ui.subtitleText, 0.6f, 2f);
            ui.subtitleBack = sub; ui.incomingColor = S.blood; ui.noticeColor = S.inkDark; ui.clearedColor = S.olive;
            ui.detailText = Txt(anim.transform, "Detail", "", Num, 40, S.bone);
            Place(ui.detailText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -164), new Vector2(1100, 50));
            Shade(ui.detailText, 0.9f, 2f);
        }

        // ================================================================== menus: a dark screen with a vignette
        static GameObject MenuScreen(Transform parent, string name, float dim, float blood)
        {
            var go = Node(name, parent); Stretch(go); OwnCanvas(go);
            var group = go.AddComponent<CanvasGroup>(); group.alpha = 0f;
            go.AddComponent<UIPanel>();
            var shade = Img(go.transform, "Dim", null, new Color(0.01f, 0.01f, 0.012f, dim)); Stretch(shade.gameObject); shade.raycastTarget = true;
            var vig = Img(go.transform, "Vignette", Sp("vignette"), A(Color.black, 0.9f)); Stretch(vig.gameObject);
            if (blood > 0f) { var bv = Img(go.transform, "Blood", Sp("vignette"), A(S.ink, blood)); Stretch(bv.gameObject, -60, -60, -60, -60); }
            return go;
        }

        /// A big distressed title with a blood scratch under it (left-aligned at x).
        static Text BigTitle(Transform parent, string text, Vector2 anchor, Vector2 pivot, Vector2 pos, int size, TextAnchor align, float lineWidth)
        {
            var t = Txt(parent, "Title", text, Title, size, S.bone, align);
            Place(t.gameObject, anchor, pivot, pos, new Vector2(1100, size * 1.2f));
            Shade(t, 0.9f, 5f);
            var line = Img(parent, "Scratch", Sp("line"), S.blood, true);
            bool left = align == TextAnchor.MiddleLeft;
            Place(line.gameObject, anchor, new Vector2(left ? 0f : 0.5f, 0.5f), new Vector2(pos.x - (left ? 10 : 0), pos.y - size * 0.62f), new Vector2(lineWidth, 34));
            return t;
        }

        static void BuildPause(Transform parent, OptionsMenuUI options)
        {
            var go = MenuScreen(parent, "PauseMenu", 0.93f, 0.35f);
            var ui = go.AddComponent<PauseMenuUI>();
            ui.panel = go.GetComponent<UIPanel>(); ui.options = options;
            var claw = Img(go.transform, "Claw", Sp("claw"), A(S.blood, 0.4f));
            Place(claw.gameObject, new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-420, 20), new Vector2(640, 640)); Tilt(claw, 6f);
            var left = new Vector2(0, 0.5f);
            BigTitle(go.transform, "PAUSED", left, new Vector2(0, 0.5f), new Vector2(170, 300), 150, TextAnchor.MiddleLeft, 560);
            var note = Txt(go.transform, "Note", "THE HORDE IS WAITING", Num, 44, S.muted, TextAnchor.MiddleLeft);
            Place(note.gameObject, left, new Vector2(0, 0.5f), new Vector2(174, 150), new Vector2(700, 54));
            var size = new Vector2(600, 112); var piv = new Vector2(0, 0.5f);
            ui.resumeButton = StrokeBtn(go.transform, "Resume", "RESUME", "play", left, piv, new Vector2(150, 50), size, S.blood);
            ui.restartButton = StrokeBtn(go.transform, "Restart", "RESTART", "reload", left, piv, new Vector2(150, -80), size, S.inkDark);
            ui.optionsButton = StrokeBtn(go.transform, "Options", "SETTINGS", "gear", left, piv, new Vector2(150, -210), size, S.inkDark);
            ui.menuButton = StrokeBtn(go.transform, "Home", "LEAVE", "home", left, piv, new Vector2(150, -340), size, S.inkDark);
            ui.panel.firstSelected = ui.resumeButton;
        }

        static void BuildGameOver(Transform parent)
        {
            var go = MenuScreen(parent, "GameOver", 0.95f, 0.75f);
            var panel = go.GetComponent<UIPanel>(); panel.fadeSpeed = 3f;
            var ui = go.AddComponent<GameOverUI>();
            ui.panel = panel; ui.starFull = Sp("skull_on"); ui.starEmpty = Sp("skull_off");
            ui.bestColor = S.bone; ui.newBestColor = S.amber;
            var top = new Vector2(0.5f, 1);

            // OVERRUN painted on a dripping blood stroke
            var title = Node("TitleRoot", go.transform);
            Place(title, top, new Vector2(0.5f, 0.5f), new Vector2(0, -190), new Vector2(1060, 332));
            ui.titleRoot = (RectTransform)title.transform;
            Tilt(title.transform, -1.5f);
            var drip = Img(title.transform, "Paint", Sp("stroke_drip"), S.blood); Stretch(drip.gameObject);
            var tt = Txt(title.transform, "Text", "OVERRUN", Title, 168, S.bone);
            Place(tt.gameObject, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -93), new Vector2(1060, 200));
            Shade(tt, 0.85f, 5f);

            var stars = Node("Rating", go.transform);
            Place(stars, top, new Vector2(0.5f, 0.5f), new Vector2(0, -410), new Vector2(520, 96));
            ui.stars = new Image[5];
            for (int i = 0; i < 5; i++)
            {
                var st = Img(stars.transform, "Skull" + (i + 1), Sp("skull_off"), Color.white);
                Place(st.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2((i - 2) * 96, 0), new Vector2(80, 80));
                ui.stars[i] = st;
            }

            Text Row(string n, string label, string icon, int i)
            {
                var row = Node("Row_" + n, go.transform);
                Place(row, top, new Vector2(0.5f, 1), new Vector2(0, -478 - i * 82), new Vector2(720, 76));
                var ic = Img(row.transform, "Icon", Sp("ic_" + icon), S.muted);
                Place(ic.gameObject, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(26, 0), new Vector2(42, 42));
                var l = Txt(row.transform, "Label", label, Num, 46, S.muted, TextAnchor.MiddleLeft);
                Place(l.gameObject, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(66, 2), new Vector2(420, 60));
                var v = Txt(row.transform, "Value", "0", Num, 64, S.bone, TextAnchor.MiddleRight);
                Place(v.gameObject, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-8, 2), new Vector2(260, 76));
                Shade(v);
                var div = Img(row.transform, "Divider", Sp("line"), A(S.bone, 0.14f), true);
                Place(div.gameObject, new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720, 16));
                return v;
            }
            ui.waveText = Row("Wave", "WAVE REACHED", "hand", 0);
            ui.killsText = Row("Kills", "ZOMBIES KILLED", "skull", 1);
            ui.coinsText = Row("Scrap", "SCRAP FOUND", "scrap", 2);
            ui.bestText = Row("Best", "BEST WAVE", "star", 3);

            var size = new Vector2(440, 112); var bottom = new Vector2(0.5f, 0); var mid = new Vector2(0.5f, 0.5f);
            ui.restartButton = StrokeBtn(go.transform, "Replay", "TRY AGAIN", "reload", bottom, mid, new Vector2(-240, 130), size, S.blood, 58, true);
            ui.menuButton = StrokeBtn(go.transform, "Home", "LOBBY", "home", bottom, mid, new Vector2(240, 130), size, S.inkDark, 58, true);
            panel.firstSelected = ui.restartButton;
            ui.countdownText = Txt(go.transform, "Countdown", "", Num, 34, S.muted);
            Place(ui.countdownText.gameObject, bottom, mid, new Vector2(0, 50), new Vector2(900, 40));
        }

        // ================================================================== options
        static OptionsMenuUI BuildOptions(Transform parent)
        {
            var go = MenuScreen(parent, "OptionsMenu", 0.93f, 0.3f);
            var ui = go.AddComponent<OptionsMenuUI>();
            ui.panel = go.GetComponent<UIPanel>();
            var board = Img(go.transform, "Panel", Sp("panel"), Color.white, true);
            Place(board.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1420, 900)); board.raycastTarget = true;
            var p = board.transform;
            var splat = Img(p, "Splat", Sp("splat"), A(S.ink, 0.45f));
            Place(splat.gameObject, new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-60, -50), new Vector2(260, 260));
            BigTitle(p, "SETTINGS", new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -96), 100, TextAnchor.MiddleCenter, 460);

            Transform Column(string n, float x)
            {
                var col = Node(n, p);
                Place(col, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(x, -200), new Vector2(600, 560));
                var v = col.AddComponent<VerticalLayoutGroup>();
                v.spacing = 8; v.childControlHeight = false; v.childControlWidth = true; v.childForceExpandHeight = false; v.childForceExpandWidth = true;
                return col.transform;
            }
            void Section(Transform col, string title, string icon)
            {
                var s = Node("Section_" + title, col); ((RectTransform)s.transform).sizeDelta = new Vector2(600, 64);
                var ic = Img(s.transform, "Icon", Sp("ic_" + icon), S.blood); Place(ic.gameObject, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(20, -2), new Vector2(34, 34));
                var t = Txt(s.transform, "Title", title, Num, 48, S.blood, TextAnchor.MiddleLeft); Place(t.gameObject, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(48, 0), new Vector2(400, 60));
            }
            Transform Row(Transform col, string label)
            {
                var row = Node("Row_" + label, col); ((RectTransform)row.transform).sizeDelta = new Vector2(600, 66);
                var t = Txt(row.transform, "Label", label, Num, 40, S.bone, TextAnchor.MiddleLeft);
                Place(t.gameObject, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(6, 2), new Vector2(240, 60));
                var c = Node("Control", row.transform);
                Place(c, new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(340, 54));
                return c.transform;
            }
            OptionCycler Cycler(Transform slot, string n)
            {
                var c = Node(n, slot); Stretch(c);
                var bg = Img(c.transform, "Back", Sp("bar"), A(Color.black, 0.7f), true); Stretch(bg.gameObject);
                var oc = c.AddComponent<OptionCycler>();
                Button Arrow(string an, bool left)
                {
                    var a = Node(an, c.transform);
                    Place(a, new Vector2(left ? 0 : 1, 0.5f), new Vector2(left ? 0 : 1, 0.5f), Vector2.zero, new Vector2(60, 54));
                    var hit = a.AddComponent<EmptyGraphic>(); hit.raycastTarget = true;
                    var ar = Img(a.transform, "Arrow", Sp("ic_play"), S.blood);
                    Place(ar.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26, 26));
                    if (left) ar.rectTransform.localEulerAngles = new Vector3(0, 0, 180);
                    var b = a.AddComponent<Button>(); b.targetGraphic = ar;
                    var cb = ColorBlock.defaultColorBlock; cb.highlightedColor = new Color(1.4f, 1.4f, 1.4f); cb.pressedColor = new Color(0.6f, 0.6f, 0.6f); b.colors = cb;
                    return b;
                }
                oc.prevButton = Arrow("Prev", true); oc.nextButton = Arrow("Next", false);
                oc.valueText = Txt(c.transform, "Value", "VALUE", Num, 36, S.bone); Stretch(oc.valueText.gameObject, 60, 0, 60, -3);
                return oc;
            }
            Slider Fader(Transform slot, string n, out Text value)
            {
                var g = Node(n, slot); Stretch(g, 0, 0, 70, 0);
                var sl = g.AddComponent<Slider>();
                var track = Img(g.transform, "Track", Sp("bar"), A(Color.black, 0.7f), true);
                var tr = track.rectTransform; tr.anchorMin = new Vector2(0, 0.5f); tr.anchorMax = new Vector2(1, 0.5f); tr.sizeDelta = new Vector2(0, 22); track.raycastTarget = true;
                var fa = Node("FillArea", g.transform); var far = (RectTransform)fa.transform; far.anchorMin = new Vector2(0, 0.5f); far.anchorMax = new Vector2(1, 0.5f); far.sizeDelta = new Vector2(-8, 16);
                var fill = Img(fa.transform, "Fill", Sp("bar"), S.blood, true); Stretch(fill.gameObject);
                var ha = Node("HandleArea", g.transform); Stretch(ha, 10, 0, 10, 0);
                var h = Img(ha.transform, "Handle", null, S.bone); h.raycastTarget = true;
                h.rectTransform.sizeDelta = new Vector2(12, 0);
                sl.fillRect = fill.rectTransform; sl.handleRect = h.rectTransform; sl.targetGraphic = h; sl.direction = Slider.Direction.LeftToRight;
                value = Txt(slot, n + "Value", "0", Num, 40, S.bone, TextAnchor.MiddleRight);
                Place(value.gameObject, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(0, 2), new Vector2(64, 54));
                return sl;
            }

            var left = Column("Left", -340); var right = Column("Right", 340);
            Section(left, "GRAPHICS", "eye");
            ui.quality = Cycler(Row(left, "QUALITY"), "Quality");
            ui.frameRate = Cycler(Row(left, "FRAME RATE"), "FrameRate");
            Section(left, "CONTROLS", "aim");
            ui.sensitivity = Fader(Row(left, "LOOK SPEED"), "Sensitivity", out ui.sensitivityValue);
            ui.invertY = Cycler(Row(left, "INVERT LOOK"), "InvertY");
            ui.controlScheme = Cycler(Row(left, "CONTROLS"), "Controls");
            Section(right, "AUDIO", "sound");
            ui.masterVolume = Fader(Row(right, "MASTER"), "Master", out ui.masterValue);
            ui.sfxVolume = Fader(Row(right, "EFFECTS"), "Sfx", out ui.sfxValue);
            ui.musicVolume = Fader(Row(right, "MUSIC"), "Music", out ui.musicValue);
            Section(right, "FEEDBACK", "hand");
            ui.vibration = Cycler(Row(right, "VIBRATION"), "Vibration");
            ui.backButton = StrokeBtn(p, "Back", "DONE", "close", new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), new Vector2(0, 90), new Vector2(380, 104), S.blood, 54, true);
            ui.panel.firstSelected = ui.backButton;
            return ui;
        }

        // ================================================================== lobby: night city, the horde, a dripping logo
        static GameObject BuildLobby()
        {
            var root = new GameObject("Lobby", typeof(RectTransform)); root.layer = 5;
            var canvas = root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 500;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; scaler.matchWidthOrHeight = 1f;
            root.AddComponent<GraphicRaycaster>();
            var view = root.AddComponent<LobbyView>();

            var panel = Node("Menu", root.transform); Stretch(panel);
            view.panel = panel;
            var bd = Img(panel.transform, "Backdrop", Sp("backdrop"), Color.white); bd.raycastTarget = true;
            Place(bd.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920, 1080));
            var fit = bd.gameObject.AddComponent<AspectRatioFitter>(); fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent; fit.aspectRatio = 16f / 9f;
            var fade = Img(panel.transform, "FadeLeft", Sp("fade_left"), A(Color.black, 0.85f));
            var fr = Stretch(fade.gameObject); fr.anchorMax = new Vector2(0.62f, 1f);
            var vig = Img(panel.transform, "Vignette", Sp("vignette"), A(Color.black, 0.85f)); Stretch(vig.gameObject);
            var safe = Node("SafeArea", panel.transform); Stretch(safe); safe.AddComponent<SafeAreaFitter>();
            var tl = new Vector2(0, 1);

            // the logo: ZOMBIE over RUSH painted on a dripping blood stroke
            var logo = Node("Logo", safe.transform);
            Place(logo, tl, tl, new Vector2(80, -40), new Vector2(820, 400));
            var z = Txt(logo.transform, "Zombie", "ZOMBIE", Title, 150, S.bone, TextAnchor.MiddleLeft);
            Place(z.gameObject, tl, new Vector2(0, 0.5f), new Vector2(14, -80), new Vector2(800, 170));
            Shade(z, 0.9f, 5f);
            var drip = Img(logo.transform, "Paint", Sp("stroke_drip"), S.blood);
            Place(drip.gameObject, tl, tl, new Vector2(-24, -150), new Vector2(760, 238)); Tilt(drip, -1.5f);
            var r = Txt(logo.transform, "Rush", "RUSH", Title, 176, S.bone, TextAnchor.MiddleLeft);
            Place(r.gameObject, tl, new Vector2(0, 0.5f), new Vector2(40, -218), new Vector2(760, 190)); Tilt(r, -1.5f);
            Shade(r, 0.85f, 5f);

            var tag = Txt(safe.transform, "Tagline", "SURVIVE THE NIGHT  ·  ALONE OR AS A SQUAD OF 4", Num, 42, S.muted, TextAnchor.MiddleLeft);
            Place(tag.gameObject, tl, new Vector2(0, 0.5f), new Vector2(96, -440), new Vector2(900, 50));

            var size = new Vector2(620, 116); var piv = new Vector2(0, 0.5f);
            view.soloButton = StrokeBtn(safe.transform, "Solo", "PLAY SOLO", "solo", tl, piv, new Vector2(80, -545), size, S.blood, 60);
            var soloNote = Txt(safe.transform, "SoloNote", "NO INTERNET NEEDED", Num, 30, S.muted, TextAnchor.MiddleLeft);
            Place(soloNote.gameObject, tl, piv, new Vector2(118, -615), new Vector2(600, 36));
            view.hostButton = StrokeBtn(safe.transform, "Host", "HOST SQUAD", "squad", tl, piv, new Vector2(80, -690), size, S.inkDark, 60);
            var hostNote = Txt(safe.transform, "HostNote", "GET A CODE  ·  FRIENDS JOIN WITH IT", Num, 30, S.muted, TextAnchor.MiddleLeft);
            Place(hostNote.gameObject, tl, piv, new Vector2(118, -760), new Vector2(600, 36));

            // join: type the code, then JOIN
            var codeGo = Node("Code", safe.transform);
            Place(codeGo, tl, piv, new Vector2(100, -850), new Vector2(390, 96));
            var codeBg = Img(codeGo.transform, "Back", Sp("bar"), A(Color.black, 0.75f), true); Stretch(codeBg.gameObject); codeBg.raycastTarget = true;
            var codeLine = Img(codeGo.transform, "Line", Sp("line"), A(S.bone, 0.5f), true);
            Place(codeLine.gameObject, new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), new Vector2(0, 14), new Vector2(330, 14));
            var input = codeGo.AddComponent<InputField>();
            var it = Txt(codeGo.transform, "Text", "", Num, 56, S.bone); Stretch(it.gameObject, 40, 0, 40, -4); it.supportRichText = false;
            it.horizontalOverflow = HorizontalWrapMode.Wrap;
            var ph = Txt(codeGo.transform, "Placeholder", "SQUAD CODE", Num, 44, A(S.muted, 0.6f)); Stretch(ph.gameObject, 40, 0, 40, -4);
            input.textComponent = it; input.placeholder = ph; input.characterLimit = 12; input.targetGraphic = codeBg;
            input.characterValidation = InputField.CharacterValidation.Alphanumeric;
            view.codeField = input;
            view.joinButton = StrokeBtn(safe.transform, "Join", "JOIN", "link", tl, piv, new Vector2(510, -850), new Vector2(230, 96), S.inkDark, 52);

            view.status = Txt(safe.transform, "Status", "", Num, 38, S.amber, TextAnchor.MiddleLeft);
            Place(view.status.gameObject, tl, piv, new Vector2(100, -950), new Vector2(900, 60));
            view.status.horizontalOverflow = HorizontalWrapMode.Wrap;
            Shade(view.status);
            var foot = Txt(safe.transform, "Footer", "PHONE AND PC PLAY TOGETHER", Num, 30, A(S.muted, 0.8f), TextAnchor.MiddleRight);
            Place(foot.gameObject, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-50, 30), new Vector2(700, 40));

            // squad tag during a squad match, top centre
            var sq = Node("SquadTag", root.transform);
            Place(sq, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -160), new Vector2(400, 64));
            var sqBack = Img(sq.transform, "Paint", Sp("stroke"), S.charcoal, true); Stretch(sqBack.gameObject, -20, 0, -20, 0);
            view.squadText = Txt(sq.transform, "Text", "SQUAD", Num, 36, S.bone); Stretch(view.squadText.gameObject, 20, 0, 20, -3);
            view.squadTag = sq;
            return root;
        }
    }
}
