using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZombieRush.Core;
using ZombieRush.Data;
using ZombieRush.UI;

namespace ZombieRush.EditorTools
{
    /// Builds the complete Zombie Rush UI from the art kit in Assets/UI:
    ///   Zombie Rush > UI > Build Gameplay UI (open scene)
    ///   Zombie Rush > UI > Create Main Menu Scene
    /// Everything it creates is ordinary GameObjects you can tweak afterwards.
    /// Re-running replaces the previous build (Undo restores it).
    public static class ZombieRushUIBuilder
    {
        const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
        const string GameplayScenePath = "Assets/Scenes/MainLevel.unity";
        static readonly Vector2 Reference = new Vector2(1920f, 1080f);

        static UIAssetGenerator.Set A;

        // =====================================================================
        // Menu items
        // =====================================================================

        [MenuItem("Zombie Rush/UI/Build Gameplay UI (open scene)", priority = 1)]
        public static void BuildGameplayUI()
        {
            A = UIAssetGenerator.Ensure();
            AssignWeaponIcons();
            RemoveOldUI();
            EnsureEventSystem();

            var root = CreateRootCanvas("GameUI", 10);
            Undo.RegisterCreatedObjectUndo(root, "Build Zombie Rush UI");

            BuildScreenEffects(root.transform);
            var safe = SafeArea(root.transform);
            BuildHUD(safe);
            BuildTouchControls(safe);
            BuildWaveBanner(root.transform);
            var options = BuildOptions(root.transform);
            BuildPauseMenu(root.transform, options);
            BuildGameOver(root.transform);
            options.transform.SetAsLastSibling(); // options draws above the pause menu

            Finish(root, "Gameplay UI built. Save the scene (Ctrl+S) and press Play.");
        }

        [MenuItem("Zombie Rush/UI/Create Main Menu Scene", priority = 2)]
        public static void CreateMainMenuScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            if (System.IO.File.Exists(MainMenuScenePath) &&
                !EditorUtility.DisplayDialog("Main Menu exists", $"{MainMenuScenePath} already exists. Replace it?", "Replace", "Cancel"))
                return;

            A = UIAssetGenerator.Ensure();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildMenuBackdrop();
            EnsureEventSystem();
            var root = CreateRootCanvas("MainMenuUI", 10);
            BuildMainMenu(root.transform);

            EditorSceneManager.SaveScene(scene, MainMenuScenePath);
            AddScenesToBuildSettings();
            Selection.activeGameObject = root;
            Debug.Log("Zombie Rush: Main Menu scene created at " + MainMenuScenePath + " and added to Build Settings (index 0).");
        }

        // =====================================================================
        // Scene plumbing
        // =====================================================================

        static void AssignWeaponIcons()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:WeaponData"))
            {
                var data = AssetDatabase.LoadAssetAtPath<WeaponData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null || data.icon != null) continue;
                string n = (data.weaponName + data.name).ToLowerInvariant();
                var icon = n.Contains("shotgun") ? A.Icon("sawed-off-shotgun") : n.Contains("pistol") ? A.Icon("pistol-gun") : null;
                if (icon == null) continue;
                Undo.RecordObject(data, "Assign weapon icon");
                data.icon = icon;
                EditorUtility.SetDirty(data);
            }
        }

        static void RemoveOldUI()
        {
            var roots = new HashSet<GameObject>();
            void Collect<T>() where T : Component
            {
                foreach (var c in Object.FindObjectsByType<T>(FindObjectsInactive.Include))
                {
                    var canvas = c.GetComponentInParent<Canvas>(true);
                    roots.Add(canvas != null ? canvas.rootCanvas.gameObject : c.gameObject);
                }
            }
            Collect<HUDController>();
            Collect<DamageFlashUI>();
            Collect<WaveBannerUI>();
            Collect<GameOverUI>();
            Collect<PauseMenuUI>();
            Collect<TouchControlsUI>();

            foreach (var go in roots)
            {
                Debug.Log($"Zombie Rush: removing old UI '{go.name}' (Undo restores it).");
                Undo.DestroyObjectImmediate(go);
            }
        }

        static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
        }

        static GameObject CreateRootCanvas(string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            canvas.pixelPerfect = false; // pixel-perfect re-snaps every animated element: slow on mobile

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = Reference;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f; // match height: buttons stay thumb-sized on wide phones
            return go;
        }

        static Transform SafeArea(Transform parent)
        {
            var go = Node("SafeArea", parent);
            Stretch(go);
            go.AddComponent<SafeAreaFitter>();
            return go.transform;
        }

        static void Finish(GameObject root, string message)
        {
            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log("Zombie Rush: " + message);
        }

        static void AddScenesToBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            scenes.RemoveAll(s => s.path == MainMenuScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(MainMenuScenePath, true));
            if (System.IO.File.Exists(GameplayScenePath) && scenes.All(s => s.path != GameplayScenePath))
                scenes.Add(new EditorBuildSettingsScene(GameplayScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // =====================================================================
        // Gameplay UI
        // =====================================================================

        static void BuildScreenEffects(Transform parent)
        {
            var fx = Node("ScreenFX", parent);
            Stretch(fx);
            NestedCanvas(fx, false);

            var vignette = Img(fx.transform, "LowHealthVignette", A.Vignette, UITheme.Blood.WithAlpha(0f));
            Stretch(vignette.gameObject);
            var flash = Img(fx.transform, "DamageFlash", A.Vignette, UITheme.Blood.WithAlpha(0f));
            Stretch(flash.gameObject, -200f, -200f, -200f, -200f);

            var ui = fx.AddComponent<DamageFlashUI>();
            ui.flashImage = flash;
            ui.lowHealthVignette = vignette;
            ui.maxAlpha = 0.85f;
        }

        static void BuildHUD(Transform parent)
        {
            var hudGo = Node("HUD", parent);
            Stretch(hudGo);
            NestedCanvas(hudGo, true); // raycaster only for the touch swap/expand areas
            var hud = hudGo.AddComponent<HUDController>();
            var layout = hudGo.AddComponent<ResponsiveLayout>();
            var targets = new List<RectTransform>();
            var pcPoses = new List<RectPose>();
            var touchPoses = new List<RectPose>();
            var touchOnly = new List<GameObject>();

            void Responsive(GameObject go, Vector2 touchAnchor, Vector2 touchPivot, Vector2 touchPos)
            {
                var rt = go.GetComponent<RectTransform>();
                targets.Add(rt);
                pcPoses.Add(RectPose.Of(rt));
                touchPoses.Add(new RectPose { anchorMin = touchAnchor, anchorMax = touchAnchor, pivot = touchPivot, anchoredPosition = touchPos });
            }

            // ---- Top centre: wave title on a brush stroke, zombies left, zone -----
            var top = Node("WaveStatus", hudGo.transform);
            Place(top, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(760f, 170f));
            var stroke = Img(top.transform, "Brush", A.Brush, UITheme.Ink.WithAlpha(0.88f), Image.Type.Sliced);
            Place(stroke.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 6f), new Vector2(720f, 150f));

            hud.waveText = Txt(top.transform, "WaveText", "WAVE 1", A.Logo, 62, UITheme.Bone, TextAnchor.MiddleCenter);
            Place(hud.waveText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(700f, 76f));
            Shadowed(hud.waveText, 0.8f, new Vector2(3f, -3f));

            var stats = Node("Stats", top.transform);
            Place(stats, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -88f), new Vector2(700f, 34f));
            var row = stats.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleCenter;
            row.spacing = 10f;
            row.childControlWidth = true;
            row.childControlHeight = false;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            IconInRow(stats.transform, "ZombieIcon", A.Icon("raise-zombie"), UITheme.Blood, 34f);
            hud.zombiesLeftText = RowText(stats.transform, "ZombiesLeft", "5 ZOMBIES LEFT", A.Bold, 24, UITheme.Blood);
            var sep = RowText(stats.transform, "Sep", "  |  ", A.Medium, 22, UITheme.Dim);
            sep.name = "Separator";
            IconInRow(stats.transform, "ZoneIcon", A.Icon("radar-sweep"), UITheme.Cyan, 28f);
            hud.zoneText = RowText(stats.transform, "ZoneText", "ZONE  30m / 90m", A.Medium, 22, UITheme.Cyan);

            var zoneBar = Img(top.transform, "ZoneBar", A.BarFrame, Color.white.WithAlpha(0.6f), Image.Type.Sliced);
            Place(zoneBar.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -132f), new Vector2(300f, 14f));
            var zoneInner = Node("Inner", zoneBar.transform);
            Stretch(zoneInner, 3f, 3f, 3f, 3f);
            hud.zoneFill = Img(zoneInner.transform, "Fill", A.White, UITheme.Cyan);
            Stretch(hud.zoneFill.gameObject);

            // ---- Coins plate (top-left) -------------------------------------------
            var coins = Plate(hudGo.transform, "Coins", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -28f), new Vector2(290f, 84f));
            var coinIcon = Img(coins, "CoinIcon", A.Icon("two-coins"), UITheme.Gold);
            Place(coinIcon.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(58f, 58f));
            var coinsLabel = Txt(coins, "Label", "COINS", A.Medium, 16, UITheme.Muted, TextAnchor.UpperLeft);
            Place(coinsLabel.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(90f, -10f), new Vector2(180f, 22f));
            hud.coinsText = Txt(coins, "CoinsText", "0", A.Bold, 42, UITheme.Gold, TextAnchor.MiddleLeft);
            Place(hud.coinsText.gameObject, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(90f, 6f), new Vector2(190f, 52f));
            hud.coinsText.rectTransform.pivot = new Vector2(0.15f, 0.5f);
            hud.coinsText.rectTransform.anchoredPosition = new Vector2(118f, 32f);
            Shadowed(hud.coinsText);
            hud.coinsPunchTarget = hud.coinsText.rectTransform;

            // ---- Expand prompts -------------------------------------------------------
            ExpandPromptPC(hudGo.transform);
            ExpandPromptTouch(hudGo.transform);

            // ---- Health plate (PC bottom-left / touch under coins) ------------------
            var health = Plate(hudGo.transform, "Health", Vector2.zero, Vector2.zero, new Vector2(32f, 30f), new Vector2(500f, 96f));
            var heart = Img(health, "HeartIcon", A.Icon("heart-plus"), UITheme.Blood);
            Place(heart.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(62f, 62f));
            var hpLabel = Txt(health, "Label", "HEALTH", A.Medium, 18, UITheme.Muted, TextAnchor.UpperLeft);
            Place(hpLabel.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(92f, -12f), new Vector2(200f, 24f));
            hud.healthText = Txt(health, "Value", "100 / 100", A.Bold, 24, UITheme.Bone, TextAnchor.UpperRight);
            Place(hud.healthText.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-22f, -8f), new Vector2(200f, 30f));
            Shadowed(hud.healthText);

            var barFrame = Img(health, "Bar", A.BarFrame, Color.white, Image.Type.Sliced);
            Place(barFrame.gameObject, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(90f, 16f), new Vector2(388f, 34f));
            var barInner = Node("Inner", barFrame.transform);
            Stretch(barInner, 5f, 5f, 5f, 5f);
            hud.healthTrail = Img(barInner.transform, "Trail", A.BarFill, UITheme.Bone.WithAlpha(0.8f), Image.Type.Sliced);
            Stretch(hud.healthTrail.gameObject);
            hud.healthFill = Img(barInner.transform, "Fill", A.BarFill, UITheme.Blood, Image.Type.Sliced);
            Stretch(hud.healthFill.gameObject);
            Responsive(health.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -122f));

            // ---- Weapon plate (PC bottom-right / touch top-right) ------------------
            var weapon = Plate(hudGo.transform, "Weapon", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-32f, 30f), new Vector2(470f, 150f));
            var hazard = Img(weapon, "Hazard", A.Hazard, Color.white.WithAlpha(0.85f), Image.Type.Tiled);
            Place(hazard.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(420f, 8f));
            hazard.pixelsPerUnitMultiplier = 4f;

            hud.weaponIcon = Img(weapon, "WeaponIcon", A.Icon("pistol-gun"), UITheme.Bone.WithAlpha(0.92f));
            Place(hud.weaponIcon.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, -4f), new Vector2(130f, 130f));
            hud.weaponIcon.preserveAspect = true;

            hud.weaponNameText = Txt(weapon, "WeaponName", "PISTOL", A.Medium, 22, UITheme.Muted, TextAnchor.UpperRight);
            Place(hud.weaponNameText.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -20f), new Vector2(300f, 30f));
            hud.magazineText = Txt(weapon, "Magazine", "12", A.Bold, 80, UITheme.Bone, TextAnchor.LowerRight);
            Place(hud.magazineText.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-150f, 14f), new Vector2(170f, 100f));
            Shadowed(hud.magazineText, 0.8f, new Vector2(3f, -3f));
            hud.reserveText = Txt(weapon, "Reserve", "/ 12   60", A.Medium, 26, UITheme.Muted, TextAnchor.LowerLeft);
            Place(hud.reserveText.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-14f, 30f), new Vector2(134f, 40f));

            var reload = Node("Reload", weapon);
            Place(reload, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 14f), new Vector2(300f, 12f));
            var reloadTrack = Img(reload.transform, "Track", A.BarFrame, Color.white.WithAlpha(0.5f), Image.Type.Sliced);
            Stretch(reloadTrack.gameObject);
            var reloadInner = Node("Inner", reload.transform);
            Stretch(reloadInner, 2f, 2f, 2f, 2f);
            hud.reloadFill = Img(reloadInner.transform, "Fill", A.White, UITheme.Blood);
            Stretch(hud.reloadFill.gameObject);
            var reloadLabel = Txt(reload.transform, "Label", "RELOADING", A.Bold, 18, UITheme.Blood, TextAnchor.LowerLeft);
            Place(reloadLabel.gameObject, new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(0f, 4f), new Vector2(200f, 24f));
            hud.reloadRoot = reload;
            reload.SetActive(false);

            var swapHint = Node("SwapHint", weapon);
            Place(swapHint, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-10f, -6f), new Vector2(220f, 30f));
            var swapIcon = Img(swapHint.transform, "Icon", A.Icon("cycle"), UITheme.Gold);
            Place(swapIcon.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(28f, 28f));
            var swapText = Txt(swapHint.transform, "Text", "TAP TO SWAP", A.Medium, 18, UITheme.Gold, TextAnchor.MiddleLeft);
            Place(swapText.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(34f, 0f), new Vector2(180f, 30f));
            touchOnly.Add(swapHint);

            var swapArea = Node("SwapTouchArea", weapon);
            Stretch(swapArea);
            swapArea.AddComponent<EmptyGraphic>().raycastTarget = true;
            swapArea.AddComponent<TouchButton>().action = TouchAction.SwapWeapon;
            touchOnly.Add(swapArea);
            Responsive(weapon.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-150f, -28f));

            BuildCrosshair(hudGo.transform);

            layout.targets = targets.ToArray();
            layout.pcPoses = pcPoses.ToArray();
            layout.touchPoses = touchPoses.ToArray();
            layout.touchOnly = touchOnly.ToArray();
            foreach (var go in touchOnly) go.SetActive(false); // editor preview shows the PC layout
        }

        static void ExpandPromptPC(Transform parent)
        {
            var holder = Node("ExpandPrompt_PC", parent);
            Place(holder, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -124f), new Vector2(400f, 52f));
            var content = Node("Content", holder.transform);
            Stretch(content);
            var bg = Img(content.transform, "Bg", A.ButtonPlate, Color.white.WithAlpha(0.92f), Image.Type.Sliced);
            Stretch(bg.gameObject);
            var frame = Img(content.transform, "Frame", A.Frame, UITheme.Gold, Image.Type.Sliced);
            Stretch(frame.gameObject);
            frame.pixelsPerUnitMultiplier = 2.4f;

            var key = Img(content.transform, "KeyCap", A.ButtonPlate, UITheme.Gold, Image.Type.Sliced);
            Place(key.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(36f, 36f));
            key.pixelsPerUnitMultiplier = 3f;
            var keyText = Txt(key.transform, "Key", "P", A.Bold, 22, UITheme.Ink, TextAnchor.MiddleCenter);
            Stretch(keyText.gameObject);

            var icon = Img(content.transform, "Icon", A.Icon("expand"), UITheme.Gold);
            Place(icon.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(54f, 0f), new Vector2(30f, 30f));
            var label = Txt(content.transform, "Label", "EXPAND ZONE  ·  20 COINS", A.Bold, 21, UITheme.Gold, TextAnchor.MiddleLeft);
            Place(label.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(94f, 1f), new Vector2(300f, 40f));

            var prompt = holder.AddComponent<ExpandPromptUI>();
            prompt.content = content;
            prompt.label = label;
            prompt.frame = frame;
            prompt.showOn = SchemeFilter.KeyboardMouseOnly;
            prompt.format = "EXPAND ZONE  ·  {0} COINS";
        }

        static void ExpandPromptTouch(Transform parent)
        {
            var holder = Node("ExpandPrompt_Touch", parent);
            Place(holder, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -232f), new Vector2(250f, 92f));
            var content = Node("Content", holder.transform);
            Stretch(content);
            var bg = Img(content.transform, "Bg", A.ButtonPlate, Color.white, Image.Type.Sliced);
            Stretch(bg.gameObject);
            bg.raycastTarget = true;
            var frame = Img(content.transform, "Frame", A.Frame, UITheme.Gold, Image.Type.Sliced);
            Stretch(frame.gameObject);
            frame.pixelsPerUnitMultiplier = 2.4f;
            var icon = Img(content.transform, "Icon", A.Icon("expand"), UITheme.Gold);
            Place(icon.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(54f, 54f));
            var label = Txt(content.transform, "Label", "EXPAND\n20 COINS", A.Bold, 22, UITheme.Gold, TextAnchor.MiddleLeft);
            Place(label.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(80f, 0f), new Vector2(160f, 80f));
            label.lineSpacing = 0.9f;
            var btn = content.AddComponent<TouchButton>();
            btn.action = TouchAction.ExpandZone;

            var prompt = holder.AddComponent<ExpandPromptUI>();
            prompt.content = content;
            prompt.label = label;
            prompt.frame = frame;
            prompt.showOn = SchemeFilter.TouchOnly;
            prompt.format = "EXPAND\n{0} COINS";
            content.SetActive(false); // editor preview shows the PC layout
        }

        static void BuildCrosshair(Transform parent)
        {
            var root = Node("Crosshair", parent);
            Place(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(80f, 80f));
            NestedCanvas(root, false); // hit marker animates; keep its rebuilds off the main HUD
            var ui = root.AddComponent<CrosshairUI>();

            var cross = Node("Cross", root.transform);
            Stretch(cross);
            ui.crosshairGroup = cross.AddComponent<CanvasGroup>();
            ui.crosshairGroup.blocksRaycasts = false;
            Color c = Color.white.WithAlpha(0.92f);
            Color shadow = Color.black.WithAlpha(0.5f);
            foreach (var (n, pos, size) in new[]
            {
                ("Top", new Vector2(0f, 14f), new Vector2(3f, 12f)),
                ("Bottom", new Vector2(0f, -14f), new Vector2(3f, 12f)),
                ("Left", new Vector2(-14f, 0f), new Vector2(12f, 3f)),
                ("Right", new Vector2(14f, 0f), new Vector2(12f, 3f)),
                ("Dot", Vector2.zero, new Vector2(4f, 4f)),
            })
            {
                Line(cross.transform, n + "Shadow", pos + new Vector2(1f, -1f), size + Vector2.one, shadow);
                Line(cross.transform, n, pos, size, c);
            }

            var marker = Node("HitMarker", root.transform);
            Place(marker, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60f, 60f));
            var graphics = new List<Graphic>();
            for (int i = 0; i < 4; i++)
            {
                float angle = 45f + 90f * i;
                Vector2 dir = Quaternion.Euler(0f, 0f, angle) * Vector2.up;
                var l = Line(marker.transform, "Tick" + i, dir * 18f, new Vector2(4f, 13f), Color.white);
                l.rectTransform.localEulerAngles = new Vector3(0f, 0f, angle);
                graphics.Add(l);
            }
            ui.hitMarker = marker.GetComponent<RectTransform>();
            ui.hitMarkerGraphics = graphics.ToArray();
        }

        static void BuildTouchControls(Transform parent)
        {
            var go = Node("TouchControls", parent);
            Stretch(go);
            NestedCanvas(go, true);
            var ui = go.AddComponent<TouchControlsUI>();

            var controls = Node("Controls", go.transform);
            Stretch(controls);
            ui.controlsRoot = controls;

            // Look pad: right side, behind the buttons. Invisible, zero overdraw.
            var look = Node("LookArea", controls.transform);
            var lookRt = look.GetComponent<RectTransform>();
            lookRt.anchorMin = new Vector2(0.42f, 0f);
            lookRt.anchorMax = Vector2.one;
            lookRt.offsetMin = lookRt.offsetMax = Vector2.zero;
            look.AddComponent<EmptyGraphic>().raycastTarget = true;
            look.AddComponent<TouchLookArea>();

            // Floating move stick: left side.
            var move = Node("MoveArea", controls.transform);
            var moveRt = move.GetComponent<RectTransform>();
            moveRt.anchorMin = Vector2.zero;
            moveRt.anchorMax = new Vector2(0.42f, 0.7f);
            moveRt.offsetMin = moveRt.offsetMax = Vector2.zero;
            move.AddComponent<EmptyGraphic>().raycastTarget = true;
            var stick = move.AddComponent<VirtualJoystick>();

            var stickBase = Node("StickBase", move.transform);
            Place(stickBase, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(260f, 220f), new Vector2(270f, 270f));
            stick.visualGroup = stickBase.AddComponent<CanvasGroup>();
            stick.visualGroup.blocksRaycasts = false;
            var baseImg = Img(stickBase.transform, "Base", A.StickBase, UITheme.Bone);
            Stretch(baseImg.gameObject);
            var knob = Img(stickBase.transform, "Knob", A.StickKnob, Color.white);
            Place(knob.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(122f, 122f));
            stick.stickBase = stickBase.GetComponent<RectTransform>();
            stick.knob = knob.rectTransform;
            stick.radius = 112f;

            // Action buttons: thumb arc in the bottom-right.
            var fire = RoundButton(controls.transform, "Fire", "FIRE", "gunshot", 250f, new Vector2(-215f, 225f), UITheme.Blood, TouchAction.Fire);
            fire.dragToLook = true;
            RoundButton(controls.transform, "Reload", "RELOAD", "reload-gun-barrel", 138f, new Vector2(-470f, 110f), UITheme.Bone, TouchAction.Reload);
            RoundButton(controls.transform, "Jump", "JUMP", "jump-across", 138f, new Vector2(-462f, 330f), UITheme.Bone, TouchAction.Jump);
            RoundButton(controls.transform, "Crouch", "CROUCH", "kneeling", 126f, new Vector2(-270f, 485f), UITheme.Cyan, TouchAction.Crouch);

            // Pause, top-right.
            var pause = Node("Pause", controls.transform);
            Place(pause, Vector2.one, Vector2.one, new Vector2(-32f, -28f), new Vector2(96f, 96f));
            var pauseBg = Img(pause.transform, "Bg", A.Panel, Color.white, Image.Type.Sliced);
            Stretch(pauseBg.gameObject);
            pauseBg.pixelsPerUnitMultiplier = 2.2f;
            pauseBg.raycastTarget = true;
            var pauseFrame = Img(pause.transform, "Frame", A.Frame, UITheme.Bone.WithAlpha(0.7f), Image.Type.Sliced);
            Stretch(pauseFrame.gameObject);
            pauseFrame.pixelsPerUnitMultiplier = 2.6f;
            Line(pause.transform, "BarL", new Vector2(-11f, 0f), new Vector2(11f, 38f), UITheme.Bone);
            Line(pause.transform, "BarR", new Vector2(11f, 0f), new Vector2(11f, 38f), UITheme.Bone);
            var pauseBtn = pause.AddComponent<TouchButton>();
            pauseBtn.action = TouchAction.Pause;
            pauseBtn.visual = pauseFrame;
            pauseBtn.normalColor = UITheme.Bone.WithAlpha(0.7f);
            pauseBtn.pressedColor = UITheme.Blood;

            controls.SetActive(false); // shown at runtime only when the touch scheme is active
        }

        static TouchButton RoundButton(Transform parent, string name, string caption, string icon, float size, Vector2 pos, Color accent, TouchAction action)
        {
            var go = Node(name, parent);
            Place(go, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), pos, new Vector2(size, size));
            var disc = Img(go.transform, "Disc", A.RoundButton, Color.white.WithAlpha(0.92f));
            Stretch(disc.gameObject);
            disc.raycastTarget = true;
            var ring = Img(go.transform, "Ring", A.RingTicks, accent.WithAlpha(0.75f));
            Stretch(ring.gameObject, 6f, 6f, 6f, 6f);
            var glyph = Img(go.transform, "Icon", A.Icon(icon), UITheme.Bone);
            Place(glyph.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, size * 0.06f), Vector2.one * size * 0.46f);
            var text = Txt(go.transform, "Caption", caption, A.Bold, Mathf.RoundToInt(Mathf.Clamp(size * 0.11f, 14f, 26f)), UITheme.Bone.WithAlpha(0.85f), TextAnchor.MiddleCenter);
            Place(text.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, size * 0.2f), new Vector2(size, 30f));

            var btn = go.AddComponent<TouchButton>();
            btn.action = action;
            btn.visual = ring;
            btn.normalColor = accent.WithAlpha(0.75f);
            btn.pressedColor = Color.Lerp(accent, Color.white, 0.35f);
            btn.toggledColor = UITheme.Cyan;
            return btn;
        }

        static void BuildWaveBanner(Transform parent)
        {
            var go = Node("WaveBanner", parent);
            Place(go, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -360f), new Vector2(1500f, 300f));
            NestedCanvas(go, false);
            var ui = go.AddComponent<WaveBannerUI>();
            ui.group = go.AddComponent<CanvasGroup>();
            ui.group.blocksRaycasts = false;
            ui.group.alpha = 0f;

            var anim = Node("AnimRoot", go.transform);
            Stretch(anim);
            ui.animRoot = anim.GetComponent<RectTransform>();

            var splat = Img(anim.transform, "Blood", A.Blood, UITheme.BloodDark.WithAlpha(0.8f));
            Place(splat.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-380f, 20f), new Vector2(420f, 420f));
            var brush = Img(anim.transform, "Brush", A.Brush, UITheme.Ink.WithAlpha(0.92f), Image.Type.Sliced);
            Place(brush.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 12f), new Vector2(1180f, 240f));

            ui.titleText = Txt(anim.transform, "Title", "WAVE 1", A.Logo, 128, UITheme.Bone, TextAnchor.MiddleCenter);
            Place(ui.titleText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 44f), new Vector2(1200f, 150f));
            Shadowed(ui.titleText, 0.85f, new Vector2(5f, -5f));

            var lineL = Img(anim.transform, "LineL", A.Divider, UITheme.Blood);
            Place(lineL.gameObject, new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-150f, -52f), new Vector2(260f, 8f));
            var lineR = Img(anim.transform, "LineR", A.Divider, UITheme.Blood);
            Place(lineR.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(150f, -52f), new Vector2(260f, 8f));

            ui.subtitleText = Txt(anim.transform, "Subtitle", "INCOMING", A.Bold, 42, UITheme.Blood, TextAnchor.MiddleCenter);
            Place(ui.subtitleText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -52f), new Vector2(300f, 56f));
            Shadowed(ui.subtitleText);

            ui.detailText = Txt(anim.transform, "Detail", "", A.Medium, 26, UITheme.Bone.WithAlpha(0.75f), TextAnchor.MiddleCenter);
            Place(ui.detailText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -120f), new Vector2(1200f, 40f));
            Shadowed(ui.detailText);
        }

        static void BuildPauseMenu(Transform parent, OptionsMenuUI options)
        {
            var go = MenuScreen(parent, "PauseMenu", 0.7f);
            var ui = go.AddComponent<PauseMenuUI>();
            ui.panel = go.GetComponent<UIPanel>();
            ui.options = options;

            var card = Card(go.transform, new Vector2(660f, 700f));
            CardTitle(card, "PAUSED", "THE HORDE WAITS");

            var list = Node("Buttons", card);
            Place(list, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -200f), new Vector2(560f, 420f));
            ui.resumeButton = MenuButton(list.transform, "Resume", "RESUME", "play-button", 42, new Vector2(0f, 0f), new Vector2(560f, 84f));
            ui.restartButton = MenuButton(list.transform, "Restart", "RESTART  ·  WAVE 1", "cycle", 42, new Vector2(0f, -100f), new Vector2(560f, 84f));
            ui.optionsButton = MenuButton(list.transform, "Options", "OPTIONS", "cog", 42, new Vector2(0f, -200f), new Vector2(560f, 84f));
            ui.menuButton = MenuButton(list.transform, "QuitToMenu", "QUIT TO MENU", "exit-door", 42, new Vector2(0f, -300f), new Vector2(560f, 84f));
            ui.panel.firstSelected = ui.resumeButton;
        }

        static void BuildGameOver(Transform parent)
        {
            var go = Node("GameOver", parent);
            Stretch(go);
            NestedCanvas(go, true);
            var group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            var panel = go.AddComponent<UIPanel>();
            panel.fadeSpeed = 3f;
            var ui = go.AddComponent<GameOverUI>();
            ui.panel = panel;

            var tint = Img(go.transform, "Tint", A.White, UITheme.BloodDark.WithAlpha(0.86f));
            Stretch(tint.gameObject);
            tint.raycastTarget = true;
            var grain = Img(go.transform, "Grain", A.Grain, Color.black.WithAlpha(0.35f), Image.Type.Tiled);
            Stretch(grain.gameObject);
            var vignette = Img(go.transform, "Vignette", A.Vignette, Color.black.WithAlpha(0.95f));
            Stretch(vignette.gameObject, -100f, -100f, -100f, -100f);
            var splat = Img(go.transform, "Blood", A.Blood, UITheme.Blood.WithAlpha(0.35f));
            Place(splat.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 140f), new Vector2(1000f, 1000f));

            var title = Node("TitleRoot", go.transform);
            Place(title, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(1400f, 260f));
            ui.titleRoot = title.GetComponent<RectTransform>();
            var skull = Img(title.transform, "Skull", A.Icon("broken-skull"), UITheme.Bone.WithAlpha(0.9f));
            Place(skull.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(130f, 130f));
            var titleText = Txt(title.transform, "Title", "YOU DIED", A.Logo, 200, UITheme.Blood, TextAnchor.MiddleCenter);
            Place(titleText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(1400f, 230f));
            Shadowed(titleText, 0.95f, new Vector2(7f, -7f));

            ui.waveText = Txt(go.transform, "Wave", "YOU SURVIVED TO WAVE 1", A.Bold, 44, UITheme.Bone, TextAnchor.MiddleCenter);
            Place(ui.waveText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(1200f, 60f));
            Shadowed(ui.waveText);

            var best = Node("Best", go.transform);
            Place(best, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -96f), new Vector2(600f, 44f));
            var trophy = Img(best.transform, "Trophy", A.Icon("trophy-cup"), UITheme.Gold);
            Place(trophy.gameObject, new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-130f, 0f), new Vector2(40f, 40f));
            ui.bestText = Txt(best.transform, "Text", "BEST  ·  WAVE 1", A.Medium, 30, UITheme.Muted, TextAnchor.MiddleCenter);
            Stretch(ui.bestText.gameObject);

            ui.restartButton = PlateButton(go.transform, "Restart", "RESTART", "cycle", new Vector2(-180f, -210f), new Vector2(320f, 92f), true);
            ui.menuButton = PlateButton(go.transform, "MainMenu", "MAIN MENU", "exit-door", new Vector2(180f, -210f), new Vector2(320f, 92f), false);
            panel.firstSelected = ui.restartButton;

            ui.countdownText = Txt(go.transform, "Countdown", "RESTARTING IN 3", A.Medium, 24, UITheme.Muted, TextAnchor.MiddleCenter);
            Place(ui.countdownText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -300f), new Vector2(800f, 36f));
        }

        // =====================================================================
        // Options (shared by main menu and pause)
        // =====================================================================

        static OptionsMenuUI BuildOptions(Transform parent)
        {
            var go = MenuScreen(parent, "OptionsMenu", 0.78f);
            var ui = go.AddComponent<OptionsMenuUI>();
            ui.panel = go.GetComponent<UIPanel>();

            var card = Card(go.transform, new Vector2(1360f, 860f));
            CardTitle(card, "OPTIONS", "TUNE YOUR SURVIVAL");

            var left = Column(card, "Left", new Vector2(-340f, -180f));
            var right = Column(card, "Right", new Vector2(340f, -180f));

            Section(left, "GRAPHICS", "tv");
            ui.quality = Cycler(Row(left, "QUALITY"), "Quality");
            ui.frameRate = Cycler(Row(left, "FRAME RATE"), "FrameRate");
            Section(left, "CONTROLS", "gamepad");
            ui.sensitivity = SliderControl(Row(left, "LOOK SENSITIVITY"), "Sensitivity", out ui.sensitivityValue);
            ui.invertY = Cycler(Row(left, "INVERT LOOK Y"), "InvertY");
            ui.controlScheme = Cycler(Row(left, "CONTROLS"), "Controls");

            Section(right, "AUDIO", "speaker");
            ui.masterVolume = SliderControl(Row(right, "MASTER VOLUME"), "Master", out ui.masterValue);
            ui.sfxVolume = SliderControl(Row(right, "SOUND EFFECTS"), "Sfx", out ui.sfxValue);
            ui.musicVolume = SliderControl(Row(right, "MUSIC"), "Music", out ui.musicValue);
            Section(right, "FEEDBACK", "smartphone");
            ui.vibration = Cycler(Row(right, "VIBRATION"), "Vibration");

            ui.backButton = PlateButton(card, "Back", "BACK", "exit-door", Vector2.zero, new Vector2(300f, 84f), false);
            var backRt = ui.backButton.GetComponent<RectTransform>();
            backRt.anchorMin = backRt.anchorMax = new Vector2(0.5f, 0f);
            backRt.anchoredPosition = new Vector2(0f, 76f);
            ui.panel.firstSelected = ui.backButton;
            return ui;
        }

        static Transform Column(Transform card, string name, Vector2 pos)
        {
            var col = Node(name, card);
            Place(col, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), pos, new Vector2(600f, 520f));
            var v = col.AddComponent<VerticalLayoutGroup>();
            v.spacing = 4f;
            v.childControlHeight = false;
            v.childControlWidth = true;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = true;
            v.childAlignment = TextAnchor.UpperCenter;
            return col.transform;
        }

        static void Section(Transform column, string title, string icon)
        {
            var go = Node("Section_" + title, column);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(600f, 58f);
            var img = Img(go.transform, "Icon", A.Icon(icon), UITheme.Blood);
            Place(img.gameObject, Vector2.zero, Vector2.zero, new Vector2(0f, 8f), new Vector2(34f, 34f));
            var t = Txt(go.transform, "Title", title, A.Bold, 26, UITheme.Blood, TextAnchor.LowerLeft);
            Stretch(t.gameObject, 44f, 0f, 0f, 8f);
            var line = Img(go.transform, "Line", A.Divider, UITheme.Bone.WithAlpha(0.18f));
            Place(line.gameObject, Vector2.zero, Vector2.zero, new Vector2(0f, -2f), new Vector2(600f, 8f));
        }

        static Transform Row(Transform column, string label)
        {
            var row = Node("Row_" + label, column);
            row.GetComponent<RectTransform>().sizeDelta = new Vector2(600f, 68f);
            var t = Txt(row.transform, "Label", label, A.Medium, 25, UITheme.Bone, TextAnchor.MiddleLeft);
            Place(t.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(250f, 60f));
            var control = Node("Control", row.transform);
            Place(control, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(340f, 58f));
            return control.transform;
        }

        static OptionCycler Cycler(Transform slot, string name)
        {
            var go = Node(name, slot);
            Stretch(go);
            var bg = Img(go.transform, "Bg", A.ButtonPlate, Color.white, Image.Type.Sliced);
            Stretch(bg.gameObject);
            bg.pixelsPerUnitMultiplier = 1.6f;

            var cycler = go.AddComponent<OptionCycler>();
            cycler.prevButton = ArrowButton(go.transform, "Prev", true);
            cycler.nextButton = ArrowButton(go.transform, "Next", false);
            cycler.valueText = Txt(go.transform, "Value", "VALUE", A.Bold, 24, UITheme.Bone, TextAnchor.MiddleCenter);
            Stretch(cycler.valueText.gameObject, 56f, 0f, 56f, 0f);
            return cycler;
        }

        static Button ArrowButton(Transform parent, string name, bool left)
        {
            var go = Node(name, parent);
            Place(go, new Vector2(left ? 0f : 1f, 0.5f), new Vector2(left ? 0f : 1f, 0.5f), Vector2.zero, new Vector2(58f, 58f));
            var hit = go.AddComponent<EmptyGraphic>();
            hit.raycastTarget = true;
            var arrow = Img(go.transform, "Arrow", A.Chevron, UITheme.Blood);
            Place(arrow.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30f, 30f));
            if (left) arrow.rectTransform.localEulerAngles = new Vector3(0f, 0f, 180f);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = arrow;
            btn.colors = Tint(UITheme.Blood);
            return btn;
        }

        static Slider SliderControl(Transform slot, string name, out Text valueText)
        {
            var go = Node(name, slot);
            Stretch(go, 0f, 0f, 64f, 0f);
            var slider = go.AddComponent<Slider>();

            var track = Img(go.transform, "Track", A.BarFrame, Color.white, Image.Type.Sliced);
            var trackRt = track.rectTransform;
            trackRt.anchorMin = new Vector2(0f, 0.5f);
            trackRt.anchorMax = new Vector2(1f, 0.5f);
            trackRt.sizeDelta = new Vector2(0f, 22f);
            track.raycastTarget = true;

            var fillArea = Node("FillArea", go.transform);
            var faRt = fillArea.GetComponent<RectTransform>();
            faRt.anchorMin = new Vector2(0f, 0.5f);
            faRt.anchorMax = new Vector2(1f, 0.5f);
            faRt.sizeDelta = new Vector2(-8f, 12f);
            var fill = Img(fillArea.transform, "Fill", A.BarFill, UITheme.Blood, Image.Type.Sliced);
            Stretch(fill.gameObject);
            fill.pixelsPerUnitMultiplier = 2f;

            var handleArea = Node("HandleArea", go.transform);
            Stretch(handleArea, 20f, 0f, 20f, 0f);
            var handle = Img(handleArea.transform, "Handle", A.StickKnob, Color.white);
            handle.preserveAspect = true;
            handle.raycastTarget = true;
            var hRt = handle.rectTransform;
            hRt.sizeDelta = new Vector2(44f, 0f);

            slider.fillRect = fill.rectTransform;
            slider.handleRect = hRt;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.colors = Tint(UITheme.Bone);

            valueText = Txt(slot, name + "Value", "0", A.Bold, 26, UITheme.Bone, TextAnchor.MiddleRight);
            Place(valueText.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(56f, 50f));
            return slider;
        }

        static ColorBlock Tint(Color highlight)
        {
            var cb = ColorBlock.defaultColorBlock;
            cb.normalColor = Color.white;
            cb.highlightedColor = Color.Lerp(Color.white, highlight, 0.4f);
            cb.selectedColor = cb.highlightedColor;
            cb.pressedColor = new Color(0.75f, 0.75f, 0.75f);
            cb.fadeDuration = 0.08f;
            return cb;
        }

        // =====================================================================
        // Main menu
        // =====================================================================

        static void BuildMainMenu(Transform root)
        {
            var shadeLeft = Img(root, "ShadeLeft", A.GradientH, UITheme.Ink.WithAlpha(0.96f));
            var slRt = shadeLeft.rectTransform;
            slRt.anchorMin = Vector2.zero;
            slRt.anchorMax = new Vector2(0.66f, 1f);
            slRt.offsetMin = slRt.offsetMax = Vector2.zero;
            var vignette = Img(root, "Vignette", A.Vignette, Color.black.WithAlpha(0.9f));
            Stretch(vignette.gameObject, -120f, -120f, -120f, -120f);
            var grain = Img(root, "Grain", A.Grain, Color.black.WithAlpha(0.22f), Image.Type.Tiled);
            Stretch(grain.gameObject);

            var safe = SafeArea(root);
            var options = BuildOptions(root);

            var main = Node("MainPanel", safe);
            Stretch(main);
            main.AddComponent<CanvasGroup>();
            var panel = main.AddComponent<UIPanel>();
            panel.startVisible = true;

            var ui = root.gameObject.AddComponent<MainMenuUI>();
            ui.mainPanel = panel;
            ui.options = options;

            // Logo: "ZOMBIE" over a blood splatter, "RUSH" in red.
            var logo = Node("Logo", main.transform);
            Place(logo, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(110f, -70f), new Vector2(900f, 380f));
            var splat = Img(logo.transform, "Blood", A.Blood, UITheme.BloodDark.WithAlpha(0.9f));
            Place(splat.gameObject, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(560f, -230f), new Vector2(560f, 560f));
            var hazardIcon = Img(logo.transform, "Biohazard", A.Icon("biohazard"), UITheme.Gold.WithAlpha(0.85f));
            Place(hazardIcon.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(6f, 4f), new Vector2(64f, 64f));
            var tagline = Txt(logo.transform, "Tagline", "W A V E    S U R V I V A L", A.Medium, 24, UITheme.Gold.WithAlpha(0.85f), TextAnchor.MiddleLeft);
            Place(tagline.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(84f, -12f), new Vector2(700f, 40f));
            var zombie = Txt(logo.transform, "Zombie", "ZOMBIE", A.Logo, 172, UITheme.Bone, TextAnchor.UpperLeft);
            Place(zombie.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -56f), new Vector2(900f, 190f));
            Shadowed(zombie, 0.9f, new Vector2(6f, -6f));
            var rush = Txt(logo.transform, "Rush", "RUSH", A.Logo, 172, UITheme.Blood, TextAnchor.UpperLeft);
            Place(rush.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -206f), new Vector2(900f, 190f));
            Shadowed(rush, 0.9f, new Vector2(6f, -6f));

            var hazard = Img(main.transform, "Hazard", A.Hazard, Color.white.WithAlpha(0.9f), Image.Type.Tiled);
            Place(hazard.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(110f, -486f), new Vector2(560f, 10f));
            hazard.pixelsPerUnitMultiplier = 3.2f;

            // Buttons
            var list = Node("Buttons", main.transform);
            Place(list, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(92f, -520f), new Vector2(640f, 420f));
            ui.playButton = MenuButton(list.transform, "Play", "PLAY", "play-button", 56, new Vector2(0f, 0f), new Vector2(640f, 92f));
            ui.multiplayerButton = MenuButton(list.transform, "Multiplayer", "MULTIPLAYER", "backup", 56, new Vector2(0f, -102f), new Vector2(640f, 92f));
            var soon = Txt(ui.multiplayerButton.transform, "Soon", "CO-OP  ·  SOON", A.Bold, 18, UITheme.Gold.WithAlpha(0.85f), TextAnchor.MiddleLeft);
            Place(soon.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(470f, -2f), new Vector2(170f, 30f));
            ui.optionsButton = MenuButton(list.transform, "Options", "OPTIONS", "cog", 56, new Vector2(0f, -204f), new Vector2(640f, 92f));
            ui.quitButton = MenuButton(list.transform, "Quit", "QUIT", "exit-door", 56, new Vector2(0f, -306f), new Vector2(640f, 92f));
            panel.firstSelected = ui.playButton;

            var best = Node("Best", main.transform);
            Place(best, Vector2.zero, Vector2.zero, new Vector2(110f, 44f), new Vector2(600f, 48f));
            var trophy = Img(best.transform, "Trophy", A.Icon("trophy-cup"), UITheme.Gold);
            Place(trophy.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(42f, 42f));
            ui.bestWaveText = Txt(best.transform, "Text", "BEST  ·  WAVE 0", A.Bold, 28, UITheme.Gold, TextAnchor.MiddleLeft);
            Place(ui.bestWaveText.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(54f, 0f), new Vector2(500f, 44f));

            ui.versionText = Txt(main.transform, "Version", "v0.1", A.Medium, 20, UITheme.Dim, TextAnchor.LowerRight);
            Place(ui.versionText.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-48f, 44f), new Vector2(400f, 30f));

            // Fade-from-black overlay (also blocks clicks while loading).
            var fader = Img(root, "Fader", A.White, UITheme.Ink);
            Stretch(fader.gameObject);
            fader.raycastTarget = true;
            ui.fader = fader.gameObject.AddComponent<CanvasGroup>();
            ui.fader.alpha = 0f;
            ui.fader.blocksRaycasts = false;

            options.transform.SetAsLastSibling();
            fader.transform.SetAsLastSibling();
        }

        static void BuildMenuBackdrop()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.05f, 0.05f, 0.07f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.06f, 0.015f, 0.015f);
            RenderSettings.fogDensity = 0.09f;

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetPositionAndRotation(new Vector3(0f, 1.5f, -0.4f), Quaternion.Euler(3f, 0f, 0f));
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.02f, 0.025f);
            cam.fieldOfView = 48f;
            cam.farClipPlane = 60f;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<MenuCameraDrift>();

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(4f, 1f, 4f);
            var groundMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Mat_Ground.mat");
            if (groundMat != null) ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;

            // The horde, standing in the red fog on the right of the frame.
            SpawnMenuZombie("Assets/Prefabs/Zombie.prefab", new Vector3(0.2f, 0f, 5.2f), 170f);
            SpawnMenuZombie("Assets/Prefabs/Zombie_Yaku.prefab", new Vector3(1.35f, 0f, 3.9f), 185f);
            SpawnMenuZombie("Assets/Prefabs/Zombie.prefab", new Vector3(2.7f, 0f, 5.6f), 200f);

            var key = new GameObject("KeyLight").AddComponent<Light>();
            key.type = LightType.Spot;
            key.transform.SetPositionAndRotation(new Vector3(-0.8f, 4.2f, 0.6f), Quaternion.LookRotation(new Vector3(1.4f, 1.2f, 4.8f) - new Vector3(-0.8f, 4.2f, 0.6f)));
            key.color = new Color(1f, 0.86f, 0.72f);
            key.intensity = 6f;
            key.range = 14f;
            key.spotAngle = 48f;
            key.shadows = LightShadows.Soft;

            var rim = new GameObject("RimLight").AddComponent<Light>();
            rim.type = LightType.Point;
            rim.transform.position = new Vector3(1.6f, 1.8f, 8f);
            rim.color = new Color(0.85f, 0.08f, 0.06f);
            rim.intensity = 8f;
            rim.range = 9f;
        }

        static void SpawnMenuZombie(string prefabPath, Vector3 position, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) return;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            // Display only: strip gameplay (order matters: ZombieAI requires the others).
            void Strip<T>() where T : Component { foreach (var c in go.GetComponents<T>()) Object.DestroyImmediate(c); }
            Strip<ZombieRush.Zombie.ZombieAI>();
            Strip<ZombieRush.Zombie.ZombieSpawnRise>();
            Strip<ZombieRush.Zombie.ZombieHealth>();
            Strip<CharacterController>();
            go.AddComponent<RandomAnimatorOffset>();
        }

        // =====================================================================
        // Shared factories
        // =====================================================================

        static GameObject MenuScreen(Transform parent, string name, float dim)
        {
            var go = Node(name, parent);
            Stretch(go);
            NestedCanvas(go, true);
            var group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f; // hidden in the editor too; set alpha to 1 to edit it visually
            go.AddComponent<UIPanel>();

            var shade = Img(go.transform, "Dim", A.White, UITheme.Ink.WithAlpha(dim));
            Stretch(shade.gameObject);
            shade.raycastTarget = true; // block clicks to whatever is behind
            var grain = Img(go.transform, "Grain", A.Grain, Color.black.WithAlpha(0.25f), Image.Type.Tiled);
            Stretch(grain.gameObject);
            return go;
        }

        /// Gunmetal panel with a hazard-stripe header.
        static Transform Card(Transform parent, Vector2 size)
        {
            var card = Img(parent, "Card", A.Panel, Color.white, Image.Type.Sliced);
            Place(card.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var hazard = Img(card.transform, "Hazard", A.Hazard, Color.white.WithAlpha(0.9f), Image.Type.Tiled);
            Place(hazard.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(size.x - 80f, 10f));
            hazard.pixelsPerUnitMultiplier = 3.2f;
            var frame = Img(card.transform, "Frame", A.Frame, UITheme.Bone.WithAlpha(0.25f), Image.Type.Sliced);
            Stretch(frame.gameObject, -6f, -6f, -6f, -6f);
            return card.transform;
        }

        static void CardTitle(Transform card, string title, string subtitle)
        {
            var t = Txt(card, "Title", title, A.Logo, 72, UITheme.Bone, TextAnchor.MiddleCenter);
            Place(t.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -86f), new Vector2(900f, 96f));
            Shadowed(t, 0.85f, new Vector2(4f, -4f));
            var stroke = Img(card, "TitleStroke", A.BrushThin, UITheme.Blood, Image.Type.Sliced);
            Place(stroke.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -138f), new Vector2(320f, 26f));
            stroke.pixelsPerUnitMultiplier = 4f;
            var sub = Txt(card, "Subtitle", subtitle, A.Bold, 17, UITheme.Bone, TextAnchor.MiddleCenter);
            Place(sub.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -138f), new Vector2(600f, 26f));
        }

        /// Gunmetal plate used as a backing for HUD groups.
        static Transform Plate(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var img = Img(parent, name, A.Panel, Color.white.WithAlpha(0.9f), Image.Type.Sliced);
            img.pixelsPerUnitMultiplier = 1.6f;
            Place(img.gameObject, anchor, pivot, pos, size);
            return img.transform;
        }

        /// Icon + label menu entry; a red dry-brush stroke sweeps in on hover/selection.
        static Button MenuButton(Transform parent, string name, string label, string icon, int fontSize, Vector2 pos, Vector2 size)
        {
            var go = Node(name, parent);
            Place(go, new Vector2(0f, 1f), new Vector2(0f, 1f), pos, size);
            var hit = go.AddComponent<EmptyGraphic>();
            hit.raycastTarget = true;

            var highlight = Img(go.transform, "Highlight", A.Brush, UITheme.Blood.WithAlpha(0f), Image.Type.Sliced);
            Stretch(highlight.gameObject, -24f, -6f, 0f, -6f);
            highlight.pixelsPerUnitMultiplier = 1.4f;

            var content = Node("Content", go.transform);
            Stretch(content);
            var glyph = Img(content.transform, "Icon", A.Icon(icon), UITheme.Bone);
            Place(glyph.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24f, 0f), Vector2.one * size.y * 0.58f);
            var text = Txt(content.transform, "Label", label, A.Medium, fontSize, UITheme.Bone, TextAnchor.MiddleLeft);
            Stretch(text.gameObject, 40f + size.y * 0.58f, 0f, 0f, 0f);
            Shadowed(text);

            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = hit;

            var fx = go.AddComponent<UIButtonFX>();
            fx.highlight = highlight;
            fx.label = text;
            fx.content = content.GetComponent<RectTransform>();
            fx.labelNormal = UITheme.Bone;
            fx.labelActive = Color.white;
            return btn;
        }

        /// Chamfered metal button with icon; primary buttons carry a red brush stroke.
        static Button PlateButton(Transform parent, string name, string label, string icon, Vector2 pos, Vector2 size, bool primary)
        {
            var go = Node(name, parent);
            Place(go, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);

            var bg = Img(go.transform, "Plate", A.ButtonPlate, Color.white, Image.Type.Sliced);
            Stretch(bg.gameObject);
            bg.raycastTarget = true;
            if (primary)
            {
                var stroke = Img(go.transform, "Stroke", A.Brush, UITheme.Blood.WithAlpha(0.95f), Image.Type.Sliced);
                Stretch(stroke.gameObject, 6f, 6f, 6f, 6f);
                stroke.pixelsPerUnitMultiplier = 2f;
            }
            var frame = Img(go.transform, "Frame", A.Frame, UITheme.Bone.WithAlpha(0f), Image.Type.Sliced);
            Stretch(frame.gameObject, -4f, -4f, -4f, -4f);
            frame.pixelsPerUnitMultiplier = 2.2f;

            var glyph = Img(go.transform, "Icon", A.Icon(icon), UITheme.Bone);
            Place(glyph.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24f, 0f), Vector2.one * size.y * 0.5f);
            var text = Txt(go.transform, "Label", label, A.Bold, 32, UITheme.Bone, TextAnchor.MiddleCenter);
            Stretch(text.gameObject, size.y * 0.5f + 20f, 0f, 10f, 0f);
            Shadowed(text);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.colors = Tint(Color.white);

            var fx = go.AddComponent<UIButtonFX>();
            fx.highlight = frame;
            fx.label = text;
            fx.shift = 0f;
            fx.labelNormal = UITheme.Bone;
            fx.labelActive = Color.white;
            return btn;
        }

        static void IconInRow(Transform row, string name, Sprite sprite, Color color, float size)
        {
            var img = Img(row, name, sprite, color);
            img.rectTransform.sizeDelta = new Vector2(size, size);
            var le = img.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = size;
            le.preferredHeight = size;
        }

        static Text RowText(Transform row, string name, string text, Font font, int size, Color color)
        {
            var t = Txt(row, name, text, font, size, color, TextAnchor.MiddleLeft);
            t.rectTransform.sizeDelta = new Vector2(0f, 34f);
            Shadowed(t);
            return t;
        }

        static Image Line(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var img = Img(parent, name, A.White, color);
            Place(img.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
            return img;
        }

        static GameObject Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            go.transform.SetParent(parent, false);
            return go;
        }

        static void Place(GameObject go, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        static void Stretch(GameObject go, float left = 0f, float top = 0f, float right = 0f, float bottom = 0f)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        static void NestedCanvas(GameObject go, bool raycaster)
        {
            go.AddComponent<Canvas>();
            if (raycaster) go.AddComponent<GraphicRaycaster>();
        }

        static Image Img(Transform parent, string name, Sprite sprite, Color color, Image.Type type = Image.Type.Simple)
        {
            var go = Node(name, parent);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.type = type;
            img.raycastTarget = false; // default off: fewer raycast targets = cheaper input on mobile
            return img;
        }

        static Text Txt(Transform parent, string name, string text, Font font, int size, Color color, TextAnchor anchor)
        {
            var go = Node(name, parent);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = false;
            t.raycastTarget = false;
            return t;
        }

        static void Shadowed(Graphic g, float alpha = 0.7f, Vector2? distance = null)
        {
            var s = g.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0f, 0f, 0f, alpha);
            s.effectDistance = distance ?? new Vector2(2f, -2f);
        }
    }
}
