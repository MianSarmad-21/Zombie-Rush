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
            WaitForPlayerOnGameOver();
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

            EnvironmentTools.EnsureDayProfile(); // the DAY card needs it
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
                if (data == null) continue;
                // Fill empty icons and upgrade the old flat ones to the styled kit; keep custom art.
                bool replaceable = data.icon == null || AssetDatabase.GetAssetPath(data.icon).StartsWith(UIAssetGenerator.IconFolder + "/");
                if (!replaceable) continue;
                string n = (data.weaponName + data.name).ToLowerInvariant();
                var icon = n.Contains("shotgun") ? A.IconX("shotgun") : n.Contains("pistol") ? A.IconX("pistol") : null;
                if (icon == null || icon == data.icon) continue;
                Undo.RecordObject(data, "Assign weapon icon");
                data.icon = icon;
                EditorUtility.SetDirty(data);
            }
        }

        /// The results screen has Replay / Home buttons, so the run no longer auto-restarts.
        static void WaitForPlayerOnGameOver()
        {
            foreach (var gm in Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include))
            {
                if (gm.autoRestartDelay <= 0f) continue;
                Undo.RecordObject(gm, "Build Zombie Rush UI");
                gm.autoRestartDelay = 0f;
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

            // ---- Top centre: carbon wave plate, zombies left, zone ---------------
            var top = Node("WaveStatus", hudGo.transform);
            Place(top, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(640f, 190f));
            var plate = SlicedSlant(top.transform, "Plate", A.BtnSlant, new Vector2(0f, 0f), new Vector2(560f, 96f));
            Place(plate.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(560f, 96f));

            hud.waveText = Title(top.transform, "WaveText", "WAVE 1", 58, TitleGold);
            Place(hud.waveText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(520f, 76f));

            var statsBg = Img(top.transform, "StatsBar", A.BarSlantFrame, Color.white.WithAlpha(0.9f), Image.Type.Sliced);
            Place(statsBg.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(600f, 46f));
            var stats = Node("Stats", top.transform);
            Place(stats, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(600f, 46f));
            var row = stats.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleCenter;
            row.spacing = 8f;
            row.childControlWidth = true;
            row.childControlHeight = false;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            IconInRow(stats.transform, "ZombieIcon", A.IconX("zombie"), Color.white, 42f);
            hud.zombiesLeftText = RowText(stats.transform, "ZombiesLeft", "5 ZOMBIES LEFT", A.Bold, 24, UITheme.Bone);
            var sep = RowText(stats.transform, "Sep", "   ", A.Medium, 22, UITheme.Dim);
            sep.name = "Separator";
            IconInRow(stats.transform, "ZoneIcon", A.IconX("zone"), Color.white, 38f);
            hud.zoneText = RowText(stats.transform, "ZoneText", "ZONE  30m / 90m", A.Medium, 22, UITheme.Cyan);

            var zoneBar = Img(top.transform, "ZoneBar", A.BarSlantFrame, Color.white, Image.Type.Sliced);
            Place(zoneBar.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -152f), new Vector2(340f, 18f));
            zoneBar.pixelsPerUnitMultiplier = 2.6f;
            var zoneInner = Node("Inner", zoneBar.transform);
            Stretch(zoneInner, 4f, 3f, 4f, 3f);
            hud.zoneFill = Img(zoneInner.transform, "Fill", A.BarSlantFill, UITheme.Cyan, Image.Type.Sliced);
            hud.zoneFill.pixelsPerUnitMultiplier = 3f;
            Stretch(hud.zoneFill.gameObject);

            // ---- Health plate (top-left) ------------------------------------------
            var health = Plate(hudGo.transform, "Health", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -24f), new Vector2(560f, 108f));
            var heart = Img(health, "HeartIcon", A.IconX("health"), Color.white);
            Place(heart.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(34f, 0f), new Vector2(96f, 96f));
            var hpLabel = Txt(health, "Label", "HEALTH", A.Bold, 20, UITheme.Muted, TextAnchor.UpperLeft);
            Place(hpLabel.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(140f, -12f), new Vector2(200f, 26f));
            hud.healthText = Txt(health, "Value", "100 / 100", A.Bold, 26, UITheme.Bone, TextAnchor.UpperRight);
            Place(hud.healthText.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -8f), new Vector2(200f, 32f));
            Shadowed(hud.healthText);

            var barFrame = Img(health, "Bar", A.BarSlantFrame, Color.white, Image.Type.Sliced);
            Place(barFrame.gameObject, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(134f, 30f), new Vector2(390f, 36f));
            var barInner = Node("Inner", barFrame.transform);
            Stretch(barInner, 6f, 5f, 6f, 5f);
            hud.healthTrail = Img(barInner.transform, "Trail", A.BarSlantFill, UITheme.Bone.WithAlpha(0.85f), Image.Type.Sliced);
            hud.healthTrail.pixelsPerUnitMultiplier = 1.2f;
            Stretch(hud.healthTrail.gameObject);
            hud.healthFill = Img(barInner.transform, "Fill", A.BarSlantFill, UITheme.Blood, Image.Type.Sliced);
            hud.healthFill.pixelsPerUnitMultiplier = 1.2f;
            Stretch(hud.healthFill.gameObject);

            // Armor: thin steel-blue bar under the health bar (filled from the base's armor crates).
            var armorFrame = Img(health, "ArmorBar", A.BarSlantFrame, Color.white, Image.Type.Sliced);
            Place(armorFrame.gameObject, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(128f, 8f), new Vector2(330f, 20f));
            armorFrame.pixelsPerUnitMultiplier = 2.6f;
            var armorInner = Node("Inner", armorFrame.transform);
            Stretch(armorInner, 4f, 3f, 4f, 3f);
            hud.armorFill = Img(armorInner.transform, "Fill", A.BarSlantFill, UITheme.Cyan, Image.Type.Sliced);
            hud.armorFill.pixelsPerUnitMultiplier = 3f;
            Stretch(hud.armorFill.gameObject);
            hud.armorText = Txt(health, "ArmorValue", "", A.Bold, 18, UITheme.Cyan, TextAnchor.MiddleLeft);
            Place(hud.armorText.gameObject, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(464f, 8f), new Vector2(70f, 22f));
            Shadowed(hud.armorText);

            // ---- Coins + kills (under health) ---------------------------------------
            var coins = Plate(hudGo.transform, "Coins", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -142f), new Vector2(270f, 84f));
            var coinIcon = Img(coins, "CoinIcon", A.IconX("coin"), Color.white);
            Place(coinIcon.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(30f, 0f), new Vector2(70f, 70f));
            hud.coinsText = Txt(coins, "CoinsText", "0", A.Bold, 44, UITheme.Gold, TextAnchor.MiddleLeft);
            Place(hud.coinsText.gameObject, new Vector2(0f, 0.5f), new Vector2(0.1f, 0.5f), new Vector2(112f, 0f), new Vector2(150f, 60f));
            Shadowed(hud.coinsText);
            hud.coinsPunchTarget = hud.coinsText.rectTransform;

            var killsPlate = Plate(hudGo.transform, "Kills", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(314f, -142f), new Vector2(230f, 84f));
            var skull = Img(killsPlate, "KillsIcon", A.IconX("kills"), Color.white);
            Place(skull.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(30f, 0f), new Vector2(64f, 64f));
            hud.killsText = Txt(killsPlate, "KillsText", "0", A.Bold, 44, UITheme.Bone, TextAnchor.MiddleLeft);
            Place(hud.killsText.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(106f, 0f), new Vector2(120f, 60f));
            Shadowed(hud.killsText);

            // ---- Expand prompts -------------------------------------------------------
            ExpandPromptPC(hudGo.transform);
            ExpandPromptTouch(hudGo.transform);

            // ---- Weapon plate (PC bottom-right / touch top-right) ------------------
            var weapon = Plate(hudGo.transform, "Weapon", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-32f, 30f), new Vector2(520f, 160f));
            hud.weaponIcon = Img(weapon, "WeaponIcon", A.IconX("pistol"), Color.white);
            Place(hud.weaponIcon.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(40f, 0f), new Vector2(150f, 150f));
            hud.weaponIcon.preserveAspect = true;

            hud.weaponNameText = Txt(weapon, "WeaponName", "PISTOL", A.Bold, 24, UITheme.Muted, TextAnchor.UpperRight);
            Place(hud.weaponNameText.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-30f, -16f), new Vector2(300f, 32f));
            hud.magazineText = Txt(weapon, "Magazine", "12", A.Bold, 88, UITheme.Bone, TextAnchor.LowerRight);
            Place(hud.magazineText.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-160f, 14f), new Vector2(170f, 108f));
            Shadowed(hud.magazineText, 0.8f, new Vector2(3f, -3f));
            var ammoIcon = Img(weapon, "AmmoIcon", A.IconX("ammo"), Color.white);
            Place(ammoIcon.gameObject, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), new Vector2(-132f, 84f), new Vector2(44f, 44f));
            hud.reserveText = Txt(weapon, "Reserve", "/ 12   60", A.Bold, 26, UITheme.Muted, TextAnchor.LowerLeft);
            Place(hud.reserveText.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-16f, 30f), new Vector2(140f, 40f));

            var reload = Node("Reload", weapon);
            Place(reload, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-30f, 12f), new Vector2(320f, 16f));
            var reloadTrack = Img(reload.transform, "Track", A.BarSlantFrame, Color.white, Image.Type.Sliced);
            reloadTrack.pixelsPerUnitMultiplier = 2.6f;
            Stretch(reloadTrack.gameObject);
            var reloadInner = Node("Inner", reload.transform);
            Stretch(reloadInner, 4f, 3f, 4f, 3f);
            hud.reloadFill = Img(reloadInner.transform, "Fill", A.BarSlantFill, UITheme.Gold, Image.Type.Sliced);
            hud.reloadFill.pixelsPerUnitMultiplier = 3f;
            Stretch(hud.reloadFill.gameObject);
            var reloadLabel = Txt(reload.transform, "Label", "RELOADING", A.Bold, 20, UITheme.Gold, TextAnchor.LowerLeft);
            Place(reloadLabel.gameObject, new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(0f, 4f), new Vector2(200f, 26f));
            Shadowed(reloadLabel);
            hud.reloadRoot = reload;
            reload.SetActive(false);

            var swapHint = Node("SwapHint", weapon);
            Place(swapHint, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-10f, -6f), new Vector2(230f, 34f));
            var swapIcon = Img(swapHint.transform, "Icon", A.IconX("restart"), Color.white);
            Place(swapIcon.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(34f, 34f));
            var swapText = Txt(swapHint.transform, "Text", "TAP TO SWAP", A.Bold, 20, UITheme.Gold, TextAnchor.MiddleLeft);
            Place(swapText.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(40f, 0f), new Vector2(180f, 30f));
            Shadowed(swapText);
            touchOnly.Add(swapHint);

            var swapArea = Node("SwapTouchArea", weapon);
            Stretch(swapArea);
            swapArea.AddComponent<EmptyGraphic>().raycastTarget = true;
            swapArea.AddComponent<TouchButton>().action = TouchAction.SwapWeapon;
            touchOnly.Add(swapArea);
            Responsive(weapon.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-180f, -24f));

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
            Place(holder, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -240f), new Vector2(470f, 70f));
            var content = Node("Content", holder.transform);
            Stretch(content);
            var bg = SlicedSlant(content.transform, "Bg", A.BtnSlant, Vector2.zero, new Vector2(470f, 70f));
            Stretch(bg.gameObject);
            var frame = SlicedSlant(content.transform, "Frame", A.BtnSlantGlow, Vector2.zero, new Vector2(470f, 70f));
            frame.color = UITheme.Gold;
            Stretch(frame.gameObject);

            var key = Img(content.transform, "KeyCap", A.BtnSquare, Color.white);
            Place(key.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(54f, 0f), new Vector2(56f, 46f));
            var keyText = Txt(key.transform, "Key", "P", A.Bold, 24, UITheme.Gold, TextAnchor.MiddleCenter);
            Stretch(keyText.gameObject);

            var icon = Img(content.transform, "Icon", A.IconX("expand"), Color.white);
            Place(icon.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(116f, 0f), new Vector2(40f, 40f));
            var label = Txt(content.transform, "Label", "EXPAND ZONE  ·  20 COINS", A.Bold, 22, UITheme.Gold, TextAnchor.MiddleLeft);
            Place(label.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(164f, 1f), new Vector2(280f, 40f));
            Shadowed(label);

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
            Place(holder, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -240f), new Vector2(300f, 100f));
            var content = Node("Content", holder.transform);
            Stretch(content);
            var bg = SlicedSlant(content.transform, "Bg", A.BtnSlant, Vector2.zero, new Vector2(300f, 100f));
            Stretch(bg.gameObject);
            bg.raycastTarget = true;
            var frame = SlicedSlant(content.transform, "Frame", A.BtnSlantGlow, Vector2.zero, new Vector2(300f, 100f));
            frame.color = UITheme.Gold;
            Stretch(frame.gameObject);
            var icon = Img(content.transform, "Icon", A.IconX("expand"), Color.white);
            Place(icon.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(66f, 0f), new Vector2(58f, 58f));
            var label = Txt(content.transform, "Label", "EXPAND\n20 COINS", A.Bold, 22, UITheme.Gold, TextAnchor.MiddleLeft);
            Place(label.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(132f, 0f), new Vector2(150f, 80f));
            label.lineSpacing = 0.9f;
            Shadowed(label);
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
            var baseDisc = Img(stickBase.transform, "Disc", A.TouchDisc, Color.white.WithAlpha(0.55f));
            Stretch(baseDisc.gameObject, 14f, 14f, 14f, 14f);
            var baseImg = Img(stickBase.transform, "Base", A.StickBase, UITheme.Bone);
            Stretch(baseImg.gameObject);
            var knob = Img(stickBase.transform, "Knob", A.StickKnob, Color.white);
            Place(knob.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(122f, 122f));
            stick.stickBase = stickBase.GetComponent<RectTransform>();
            stick.knob = knob.rectTransform;
            stick.radius = 112f;

            // Action buttons: thumb arc in the bottom-right.
            var fire = RoundButton(controls.transform, "Fire", "FIRE", "fire", 250f, new Vector2(-215f, 225f), UITheme.Blood, TouchAction.Fire);
            fire.dragToLook = true;
            RoundButton(controls.transform, "Reload", "RELOAD", "reload", 138f, new Vector2(-470f, 110f), UITheme.Gold, TouchAction.Reload);
            RoundButton(controls.transform, "Jump", "JUMP", "jump", 138f, new Vector2(-462f, 330f), UITheme.Bone, TouchAction.Jump);
            RoundButton(controls.transform, "Crouch", "CROUCH", "crouch", 126f, new Vector2(-270f, 485f), UITheme.Cyan, TouchAction.Crouch);

            // Pause, top-right.
            var pause = Node("Pause", controls.transform);
            Place(pause, Vector2.one, Vector2.one, new Vector2(-28f, -24f), new Vector2(130f, 100f));
            var pauseBg = Img(pause.transform, "Bg", A.BtnSquare, Color.white);
            Stretch(pauseBg.gameObject);
            pauseBg.raycastTarget = true;
            var pauseGlow = Img(pause.transform, "Glow", A.BtnSquareGlow, Color.white.WithAlpha(0f));
            Stretch(pauseGlow.gameObject);
            var pauseIcon = Img(pause.transform, "Icon", A.IconX("pause"), Color.white);
            Place(pauseIcon.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(62f, 62f));
            var pauseBtn = pause.AddComponent<TouchButton>();
            pauseBtn.action = TouchAction.Pause;
            pauseBtn.visual = pauseGlow;
            pauseBtn.normalColor = Color.white.WithAlpha(0f);
            pauseBtn.pressedColor = Color.white;

            controls.SetActive(false); // shown at runtime only when the touch scheme is active
        }

        static TouchButton RoundButton(Transform parent, string name, string caption, string icon, float size, Vector2 pos, Color accent, TouchAction action)
        {
            var go = Node(name, parent);
            Place(go, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), pos, new Vector2(size, size));
            var disc = Img(go.transform, "Disc", A.TouchDisc, Color.white.WithAlpha(0.95f));
            Stretch(disc.gameObject);
            disc.raycastTarget = true;
            var ring = Img(go.transform, "Ring", A.RingTicks, accent.WithAlpha(0.6f));
            Stretch(ring.gameObject, 12f, 12f, 12f, 12f);
            var glyph = Img(go.transform, "Icon", A.IconX(icon), Color.white);
            Place(glyph.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, size * 0.07f), Vector2.one * size * 0.5f);
            var text = Txt(go.transform, "Caption", caption, A.Bold, Mathf.RoundToInt(Mathf.Clamp(size * 0.1f, 14f, 26f)), UITheme.Bone.WithAlpha(0.9f), TextAnchor.MiddleCenter);
            Place(text.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, size * 0.2f), new Vector2(size, 30f));
            Shadowed(text);

            var btn = go.AddComponent<TouchButton>();
            btn.action = action;
            btn.visual = ring;
            btn.normalColor = accent.WithAlpha(0.6f);
            btn.pressedColor = Color.Lerp(accent, Color.white, 0.35f);
            btn.toggledColor = UITheme.Cyan;
            return btn;
        }

        static void BuildWaveBanner(Transform parent)
        {
            var go = Node("WaveBanner", parent);
            Place(go, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -380f), new Vector2(1500f, 320f));
            NestedCanvas(go, false);
            var ui = go.AddComponent<WaveBannerUI>();
            ui.group = go.AddComponent<CanvasGroup>();
            ui.group.blocksRaycasts = false;
            ui.group.alpha = 0f;

            var anim = Node("AnimRoot", go.transform);
            Stretch(anim);
            ui.animRoot = anim.GetComponent<RectTransform>();

            var splat = Img(anim.transform, "Blood", A.Blood, UITheme.BloodDark.WithAlpha(0.85f));
            Place(splat.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-400f, 30f), new Vector2(440f, 440f));
            var brush = Img(anim.transform, "Brush", A.Brush, UITheme.Ink.WithAlpha(0.92f), Image.Type.Sliced);
            Place(brush.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 12f), new Vector2(1180f, 250f));

            ui.titleText = Title(anim.transform, "Title", "WAVE 1", 132, TitleGold);
            Place(ui.titleText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 46f), new Vector2(1200f, 160f));

            var lineL = Img(anim.transform, "LineL", A.Divider, UITheme.Blood);
            Place(lineL.gameObject, new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-160f, -56f), new Vector2(260f, 8f));
            var lineR = Img(anim.transform, "LineR", A.Divider, UITheme.Blood);
            Place(lineR.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(160f, -56f), new Vector2(260f, 8f));

            ui.subtitleText = Txt(anim.transform, "Subtitle", "INCOMING", A.Bold, 44, UITheme.Blood, TextAnchor.MiddleCenter);
            Place(ui.subtitleText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -56f), new Vector2(300f, 56f));
            Outlined(ui.subtitleText);

            ui.detailText = Txt(anim.transform, "Detail", "", A.Bold, 28, UITheme.Bone.WithAlpha(0.85f), TextAnchor.MiddleCenter);
            Place(ui.detailText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -124f), new Vector2(1200f, 40f));
            Shadowed(ui.detailText);
        }

        static void BuildPauseMenu(Transform parent, OptionsMenuUI options)
        {
            var go = MenuScreen(parent, "PauseMenu", 0.62f);
            var ui = go.AddComponent<PauseMenuUI>();
            ui.panel = go.GetComponent<UIPanel>();
            ui.options = options;

            var card = GrungePanel(go.transform, new Vector2(880f, 820f), Vector2.zero);
            var title = Title(card, "Title", "PAUSED", 124, TitleGold);
            Place(title.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -150f), new Vector2(800f, 150f));

            var size = new Vector2(540f, 104f);
            var top = new Vector2(0.5f, 1f);
            ui.resumeButton = SlantButton(card, "Resume", "RESUME", top, new Vector2(0f, -300f), size, 50);
            ui.restartButton = SlantButton(card, "Restart", "RESTART", top, new Vector2(0f, -418f), size, 50);
            ui.optionsButton = SlantButton(card, "Options", "OPTIONS", top, new Vector2(0f, -536f), size, 50);
            ui.menuButton = SlantButton(card, "Home", "HOME", top, new Vector2(0f, -654f), size, 50);
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
            ui.starFull = A.StarFull;
            ui.starEmpty = A.StarEmpty;

            var tint = Img(go.transform, "Tint", A.White, UITheme.Ink.WithAlpha(0.8f));
            Stretch(tint.gameObject);
            tint.raycastTarget = true;
            var frame = Img(go.transform, "BloodFrame", A.BloodFrame, Color.white);
            Stretch(frame.gameObject);
            var vignette = Img(go.transform, "Vignette", A.Vignette, Color.black.WithAlpha(0.9f));
            Stretch(vignette.gameObject, -100f, -100f, -100f, -100f);

            var card = GrungePanel(go.transform, new Vector2(1180f, 920f), new Vector2(0f, 10f));

            var title = Node("TitleRoot", card);
            Place(title, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -150f), new Vector2(1000f, 160f));
            ui.titleRoot = title.GetComponent<RectTransform>();
            var titleText = Title(title.transform, "Title", "YOU DIED", 136, TitleBlood);
            Stretch(titleText.gameObject);
            var skullL = Img(title.transform, "SkullL", A.IconX("skull"), Color.white);
            Place(skullL.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-400f, 0f), new Vector2(96f, 96f));
            var skullR = Img(title.transform, "SkullR", A.IconX("skull"), Color.white);
            Place(skullR.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(400f, 0f), new Vector2(96f, 96f));

            // Star rating
            var starRow = Node("Stars", card);
            Place(starRow, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -284f), new Vector2(620f, 120f));
            ui.stars = new Image[5];
            for (int i = 0; i < 5; i++)
            {
                var s = Img(starRow.transform, "Star" + (i + 1), A.StarEmpty, Color.white);
                float lift = i == 2 ? 14f : (i == 1 || i == 3 ? 6f : 0f);
                Place(s.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2((i - 2) * 118f, lift), Vector2.one * (i == 2 ? 124f : 112f));
                ui.stars[i] = s;
            }

            // Results table
            var rows = Node("Results", card);
            Place(rows, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -360f), new Vector2(760f, 300f));
            ui.waveText = ResultRow(rows.transform, "Wave", "WAVE REACHED", "zombie", 0);
            ui.killsText = ResultRow(rows.transform, "Kills", "KILLS", "kills", 1);
            ui.coinsText = ResultRow(rows.transform, "Coins", "COINS EARNED", "coin", 2);
            ui.bestText = ResultRow(rows.transform, "Best", "BEST WAVE", "trophy", 3);

            var buttonSize = new Vector2(400f, 104f);
            ui.restartButton = SlantButton(card, "Replay", "REPLAY", new Vector2(0.5f, 0f), new Vector2(-230f, 120f), buttonSize, 50);
            ui.menuButton = SlantButton(card, "Home", "HOME", new Vector2(0.5f, 0f), new Vector2(230f, 120f), buttonSize, 50);
            panel.firstSelected = ui.restartButton;

            ui.countdownText = Txt(go.transform, "Countdown", "PRESS R TO RESTART", A.Bold, 24, UITheme.Muted, TextAnchor.MiddleCenter);
            Place(ui.countdownText.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 36f), new Vector2(800f, 36f));
            Shadowed(ui.countdownText);
        }

        static Text ResultRow(Transform parent, string name, string label, string icon, int index)
        {
            var row = Node("Row_" + name, parent);
            Place(row, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -index * 72f), new Vector2(760f, 66f));
            var strip = Img(row.transform, "Strip", A.BarSlantFrame, Color.white.WithAlpha(0.55f), Image.Type.Sliced);
            Stretch(strip.gameObject);
            var img = Img(row.transform, "Icon", A.IconX(icon), Color.white);
            Place(img.gameObject, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(54f, 0f), new Vector2(58f, 58f));
            var t = Txt(row.transform, "Label", label, A.Bold, 34, UITheme.Bone, TextAnchor.MiddleLeft);
            Place(t.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(100f, 0f), new Vector2(400f, 50f));
            Shadowed(t);
            var v = Txt(row.transform, "Value", "0", A.Bold, 40, UITheme.Gold, TextAnchor.MiddleRight);
            Place(v.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-40f, 0f), new Vector2(260f, 56f));
            Shadowed(v, 0.8f, new Vector2(2f, -2f));
            return v;
        }

        // =====================================================================
        // Options (shared by main menu and pause)
        // =====================================================================

        static OptionsMenuUI BuildOptions(Transform parent)
        {
            var go = MenuScreen(parent, "OptionsMenu", 0.7f);
            var ui = go.AddComponent<OptionsMenuUI>();
            ui.panel = go.GetComponent<UIPanel>();

            var card = GrungePanel(go.transform, new Vector2(1440f, 940f), Vector2.zero);
            var title = Title(card, "Title", "OPTIONS", 104, TitleGold);
            Place(title.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -130f), new Vector2(900f, 130f));

            var left = Column(card, "Left", new Vector2(-350f, -210f));
            var right = Column(card, "Right", new Vector2(350f, -210f));

            Section(left, "GRAPHICS", "graphics");
            ui.quality = Cycler(Row(left, "QUALITY"), "Quality");
            ui.frameRate = Cycler(Row(left, "FRAME RATE"), "FrameRate");
            Section(left, "CONTROLS", "controls");
            ui.sensitivity = SliderControl(Row(left, "LOOK SENSITIVITY"), "Sensitivity", out ui.sensitivityValue);
            ui.invertY = Cycler(Row(left, "INVERT LOOK Y"), "InvertY");
            ui.controlScheme = Cycler(Row(left, "CONTROLS"), "Controls");

            Section(right, "AUDIO", "audio");
            ui.masterVolume = SliderControl(Row(right, "MASTER VOLUME"), "Master", out ui.masterValue);
            ui.sfxVolume = SliderControl(Row(right, "SOUND EFFECTS"), "Sfx", out ui.sfxValue);
            ui.musicVolume = SliderControl(Row(right, "MUSIC"), "Music", out ui.musicValue);
            Section(right, "FEEDBACK", "feedback");
            ui.vibration = Cycler(Row(right, "VIBRATION"), "Vibration");

            ui.backButton = SlantButton(card, "Back", "BACK", new Vector2(0.5f, 0f), new Vector2(0f, 118f), new Vector2(380f, 100f), 48);
            ui.panel.firstSelected = ui.backButton;
            return ui;
        }

        static Transform Column(Transform card, string name, Vector2 pos)
        {
            var col = Node(name, card);
            Place(col, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), pos, new Vector2(620f, 540f));
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
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(620f, 64f);
            var img = Img(go.transform, "Icon", A.IconX(icon), Color.white);
            Place(img.gameObject, Vector2.zero, Vector2.zero, new Vector2(-4f, 4f), new Vector2(50f, 50f));
            var t = Txt(go.transform, "Title", title, A.Bold, 30, UITheme.Gold, TextAnchor.LowerLeft);
            Stretch(t.gameObject, 54f, 0f, 0f, 10f);
            Shadowed(t);
            var line = Img(go.transform, "Line", A.Divider, UITheme.Blood.WithAlpha(0.9f));
            Place(line.gameObject, Vector2.zero, Vector2.zero, new Vector2(0f, -2f), new Vector2(620f, 8f));
        }

        static Transform Row(Transform column, string label)
        {
            var row = Node("Row_" + label, column);
            row.GetComponent<RectTransform>().sizeDelta = new Vector2(620f, 70f);
            var t = Txt(row.transform, "Label", label, A.Bold, 26, UITheme.Bone, TextAnchor.MiddleLeft);
            Place(t.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(260f, 60f));
            Shadowed(t);
            var control = Node("Control", row.transform);
            Place(control, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(350f, 58f));
            return control.transform;
        }

        static OptionCycler Cycler(Transform slot, string name)
        {
            var go = Node(name, slot);
            Stretch(go);
            var bg = Img(go.transform, "Bg", A.BarSlantFrame, Color.white, Image.Type.Sliced);
            Stretch(bg.gameObject);

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
            btn.colors = Tint(UITheme.Gold);
            return btn;
        }

        static Slider SliderControl(Transform slot, string name, out Text valueText)
        {
            var go = Node(name, slot);
            Stretch(go, 0f, 0f, 64f, 0f);
            var slider = go.AddComponent<Slider>();

            var track = Img(go.transform, "Track", A.BarSlantFrame, Color.white, Image.Type.Sliced);
            var trackRt = track.rectTransform;
            trackRt.anchorMin = new Vector2(0f, 0.5f);
            trackRt.anchorMax = new Vector2(1f, 0.5f);
            trackRt.sizeDelta = new Vector2(0f, 26f);
            track.pixelsPerUnitMultiplier = 1.8f;
            track.raycastTarget = true;

            var fillArea = Node("FillArea", go.transform);
            var faRt = fillArea.GetComponent<RectTransform>();
            faRt.anchorMin = new Vector2(0f, 0.5f);
            faRt.anchorMax = new Vector2(1f, 0.5f);
            faRt.sizeDelta = new Vector2(-8f, 16f);
            var fill = Img(fillArea.transform, "Fill", A.BarSlantFill, UITheme.Blood, Image.Type.Sliced);
            Stretch(fill.gameObject);
            fill.pixelsPerUnitMultiplier = 2.2f;

            var handleArea = Node("HandleArea", go.transform);
            Stretch(handleArea, 20f, 0f, 20f, 0f);
            var handle = Img(handleArea.transform, "Handle", A.StickKnob, Color.white);
            handle.preserveAspect = true;
            handle.raycastTarget = true;
            var hRt = handle.rectTransform;
            hRt.sizeDelta = new Vector2(46f, 0f);

            slider.fillRect = fill.rectTransform;
            slider.handleRect = hRt;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.colors = Tint(UITheme.Gold);

            valueText = Txt(slot, name + "Value", "0", A.Bold, 28, UITheme.Gold, TextAnchor.MiddleRight);
            Place(valueText.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(56f, 50f));
            Shadowed(valueText);
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
            var shadeLeft = Img(root, "ShadeLeft", A.GradientH, UITheme.Ink.WithAlpha(0.94f));
            var slRt = shadeLeft.rectTransform;
            slRt.anchorMin = Vector2.zero;
            slRt.anchorMax = new Vector2(0.62f, 1f);
            slRt.offsetMin = slRt.offsetMax = Vector2.zero;
            var vignette = Img(root, "Vignette", A.Vignette, Color.black.WithAlpha(0.85f));
            Stretch(vignette.gameObject, -120f, -120f, -120f, -120f);
            var bloodFrame = Img(root, "BloodFrame", A.BloodFrame, Color.white);
            Stretch(bloodFrame.gameObject);
            var grain = Img(root, "Grain", A.Grain, Color.black.WithAlpha(0.2f), Image.Type.Tiled);
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

            // Logo
            var logo = Img(main.transform, "Logo", A.LogoImage, Color.white);
            Place(logo.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(70f, -26f), new Vector2(700f, 320f));
            logo.preserveAspect = true;

            // Slanted carbon buttons, left column (like the reference layout)
            var size = new Vector2(600f, 104f);
            var tl = new Vector2(0f, 1f);
            ui.playButton = SlantButton(main.transform, "Play", "PLAY", tl, new Vector2(110f + size.x / 2f, -404f), size, 56);
            ui.multiplayerButton = SlantButton(main.transform, "Multiplayer", "MULTIPLAYER", tl, new Vector2(110f + size.x / 2f, -520f), size, 56);
            var soon = Title(ui.multiplayerButton.transform, "Soon", "SOON", 30, TitleGold);
            Place(soon.gameObject, new Vector2(1f, 0.5f), new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(120f, 40f));
            soon.rectTransform.localEulerAngles = new Vector3(0f, 0f, 8f);
            ui.optionsButton = SlantButton(main.transform, "Options", "OPTIONS", tl, new Vector2(110f + size.x / 2f, -636f), size, 56);
            ui.quitButton = SlantButton(main.transform, "Quit", "QUIT", tl, new Vector2(110f + size.x / 2f, -752f), size, 56);
            panel.firstSelected = ui.playButton;

            // Quick toggles, bottom-left
            var bl = Vector2.zero;
            var sq = new Vector2(150f, 112f);
            ui.soundToggle = IconButton(main.transform, "Sound", A.IconX("sound-on"), bl, new Vector2(110f + 75f, 112f), sq, out ui.soundIcon);
            ui.soundOnSprite = A.IconX("sound-on");
            ui.soundOffSprite = A.IconX("sound-off");
            ui.musicToggle = IconButton(main.transform, "Music", A.IconX("music"), bl, new Vector2(110f + 75f + 168f, 112f), sq, out ui.musicIcon);
            ui.vibrationToggle = IconButton(main.transform, "Vibration", A.IconX("vibration"), bl, new Vector2(110f + 75f + 336f, 112f), sq, out ui.vibrationIcon);

            // Best wave + version, bottom-right
            var best = Node("Best", main.transform);
            Place(best, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-60f, 70f), new Vector2(520f, 64f));
            var trophy = Img(best.transform, "Trophy", A.IconX("trophy"), Color.white);
            Place(trophy.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-400f, 0f), new Vector2(64f, 64f));
            ui.bestWaveText = Txt(best.transform, "Text", "BEST  ·  WAVE 0", A.Bold, 34, UITheme.Gold, TextAnchor.MiddleRight);
            Place(ui.bestWaveText.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(390f, 56f));
            Shadowed(ui.bestWaveText);

            ui.versionText = Txt(main.transform, "Version", "v0.1", A.Medium, 20, UITheme.Muted, TextAnchor.LowerRight);
            Place(ui.versionText.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-60f, 30f), new Vector2(400f, 30f));

            BuildModeSelect(root, ui);

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

        /// "SELECT THE MODE": torn red panel with a SOLO and a SQUAD card.
        static void BuildModeSelect(Transform root, MainMenuUI ui)
        {
            var go = MenuScreen(root, "ModeSelect", 0.55f);
            ui.modePanel = go.GetComponent<UIPanel>();

            var card = GrungePanel(go.transform, new Vector2(1360f, 860f), new Vector2(0f, 20f));
            var title = Title(card, "Title", "SELECT THE MODE", 96, TitleGold);
            Place(title.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -130f), new Vector2(1200f, 130f));

            ui.soloButton = ModeCard(card, "Solo", "SOLO", A.ModeSolo, "crosshair", new Vector2(-310f, -30f));
            ui.squadButton = ModeCard(card, "Squad", "SQUAD", A.ModeSquad, "multiplayer", new Vector2(310f, -30f));
            ui.modePanel.firstSelected = ui.soloButton;

            var safe = SafeArea(go.transform);
            ui.modeBackButton = SlantButton(safe, "Back", "BACK", new Vector2(1f, 0f), new Vector2(-260f, 90f), new Vector2(380f, 104f), 50);

            BuildSquadLobby(root, ui);
        }

        /// SQUAD matchmaking screen: four player slots, a status line and Cancel.
        /// MainMenuUI drives it; the online matchmaking plugs into it.
        static void BuildSquadLobby(Transform root, MainMenuUI ui)
        {
            var go = MenuScreen(root, "SquadLobby", 0.6f);
            ui.squadPanel = go.GetComponent<UIPanel>();

            var card = GrungePanel(go.transform, new Vector2(1360f, 760f), new Vector2(0f, 20f));
            var title = Title(card, "Title", "SQUAD", 110, TitleGold);
            Place(title.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -120f), new Vector2(1200f, 140f));

            ui.squadSlots = new Image[GameMode.SquadSize];
            for (int i = 0; i < ui.squadSlots.Length; i++)
            {
                float x = (i - (ui.squadSlots.Length - 1) * 0.5f) * 260f;
                var slot = SlicedSlant(card, $"Slot{i + 1}", A.BtnSquare, new Vector2(x, 10f), new Vector2(220f, 220f));
                var icon = Img(slot.transform, "Icon", A.IconX("multiplayer"), Color.white);
                Place(icon.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(140f, 140f));
                ui.squadSlots[i] = icon;
            }

            ui.squadStatusText = Txt(card, "Status", "SEARCHING FOR PLAYERS...", A.Bold, 40, UITheme.Bone, TextAnchor.MiddleCenter);
            Place(ui.squadStatusText.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(1200f, 60f));
            Shadowed(ui.squadStatusText);

            var safe = SafeArea(go.transform);
            ui.squadCancelButton = SlantButton(safe, "Cancel", "CANCEL", new Vector2(1f, 0f), new Vector2(-260f, 90f), new Vector2(380f, 104f), 50);
            ui.squadPanel.firstSelected = ui.squadCancelButton;
        }

        static Button ModeCard(Transform parent, string name, string label, Sprite picture, string icon, Vector2 pos)
        {
            var go = Node(name, parent);
            Place(go, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(520f, 540f));

            var pic = Img(go.transform, "Picture", picture, Color.white);
            Place(pic.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(520f, 390f));
            pic.raycastTarget = true;
            var glow = Img(go.transform, "Glow", A.ModeGlow, Color.white.WithAlpha(0f));
            Place(glow.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(520f, 390f));
            var badge = Img(go.transform, "Icon", A.IconX(icon), Color.white);
            Place(badge.gameObject, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(84f, -34f), new Vector2(104f, 104f));

            var plate = SlicedSlant(go.transform, "Plate", A.BtnSlant, Vector2.zero, new Vector2(400f, 100f));
            Place(plate.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(400f, 100f));
            plate.raycastTarget = true;
            var text = Txt(plate.transform, "Label", label, A.Bold, 52, UITheme.Bone, TextAnchor.MiddleCenter);
            Stretch(text.gameObject);
            Outlined(text);

            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = pic;
            var fx = go.AddComponent<UIButtonFX>();
            fx.highlight = glow;
            fx.label = text;
            fx.shift = 0f;
            fx.pressScale = 0.97f;
            fx.labelNormal = UITheme.Bone;
            fx.labelActive = Color.white;
            return btn;
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

        static readonly Color[] TitleGold = { UITheme.Hex(0xFFF0A0), UITheme.Hex(0xFFB020), UITheme.Hex(0xD9480F) };
        static readonly Color[] TitleBlood = { UITheme.Hex(0xFF8A6A), UITheme.Hex(0xE0201C), UITheme.Hex(0x7A0505) };

        /// Full-screen modal: dims the game, blocks clicks, fades via UIPanel.
        static GameObject MenuScreen(Transform parent, string name, float dim)
        {
            var go = Node(name, parent);
            Stretch(go);
            NestedCanvas(go, true);
            var group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f; // hidden in the editor too; set alpha to 1 to edit it visually
            go.AddComponent<UIPanel>();

            var shade = Img(go.transform, "Dim", A.White, Color.black.WithAlpha(dim));
            Stretch(shade.gameObject);
            shade.raycastTarget = true; // block clicks to whatever is behind
            var frame = Img(go.transform, "BloodFrame", A.BloodFrame, Color.white.WithAlpha(0.85f));
            Stretch(frame.gameObject);
            return go;
        }

        /// Torn blood-red grunge sheet (the backing of every menu card).
        static Transform GrungePanel(Transform parent, Vector2 size, Vector2 pos)
        {
            var card = Img(parent, "Card", A.PanelGrunge, Color.white, Image.Type.Sliced);
            Place(card.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
            card.raycastTarget = true;
            return card.transform;
        }

        /// Big grungy title (Rubik Dirt) with a gold or blood gradient and a dark outline.
        static Text Title(Transform parent, string name, string text, int size, Color[] gradient)
        {
            var t = Txt(parent, name, text, A.Logo, size, Color.white, TextAnchor.MiddleCenter);
            var g = t.gameObject.AddComponent<UIGradient>();
            g.Set(gradient[0], gradient[1], gradient[2]);
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0.1f, 0.01f, 0f, 0.95f);
            o.effectDistance = new Vector2(Mathf.Max(2f, size * 0.025f), -Mathf.Max(2f, size * 0.025f));
            Shadowed(t, 0.85f, new Vector2(size * 0.04f, -size * 0.05f));
            return t;
        }

        /// A 9-sliced slanted sprite whose borders scale with the height, so the
        /// slant and red caps keep their shape at any button size.
        static Image SlicedSlant(Transform parent, string name, Sprite sprite, Vector2 pos, Vector2 size)
        {
            var img = Img(parent, name, sprite, Color.white, Image.Type.Sliced);
            Place(img.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
            if (sprite != null) img.pixelsPerUnitMultiplier = sprite.rect.height / size.y;
            return img;
        }

        /// Slanted carbon button with red hatched caps; glows red on hover/selection.
        static Button SlantButton(Transform parent, string name, string label, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize)
        {
            var go = Node(name, parent);
            Place(go, anchor, new Vector2(0.5f, 0.5f), pos, size);

            var plate = SlicedSlant(go.transform, "Plate", A.BtnSlant, Vector2.zero, size);
            Stretch(plate.gameObject);
            plate.raycastTarget = true;
            var glow = SlicedSlant(go.transform, "Glow", A.BtnSlantGlow, Vector2.zero, size);
            Stretch(glow.gameObject);
            glow.color = Color.white.WithAlpha(0f);

            var content = Node("Content", go.transform);
            Stretch(content);
            var text = Txt(content.transform, "Label", label, A.Bold, fontSize, UITheme.Bone, TextAnchor.MiddleCenter);
            Stretch(text.gameObject, size.y * 0.9f, 0f, size.y * 0.9f, 4f);
            Outlined(text);

            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = plate;

            var fx = go.AddComponent<UIButtonFX>();
            fx.highlight = glow;
            fx.label = text;
            fx.content = content.GetComponent<RectTransform>();
            fx.shift = 6f;
            fx.labelNormal = UITheme.Bone;
            fx.labelActive = Color.white;
            return btn;
        }

        /// Small slanted icon button (bottom-row toggles).
        static Button IconButton(Transform parent, string name, Sprite icon, Vector2 anchor, Vector2 pos, Vector2 size, out Image iconImage)
        {
            var go = Node(name, parent);
            Place(go, anchor, new Vector2(0.5f, 0.5f), pos, size);
            var plate = Img(go.transform, "Plate", A.BtnSquare, Color.white);
            Stretch(plate.gameObject);
            plate.raycastTarget = true;
            var glow = Img(go.transform, "Glow", A.BtnSquareGlow, Color.white.WithAlpha(0f));
            Stretch(glow.gameObject);
            iconImage = Img(go.transform, "Icon", icon, Color.white);
            Place(iconImage.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * size.y * 0.66f);

            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = plate;
            var fx = go.AddComponent<UIButtonFX>();
            fx.highlight = glow;
            fx.shift = 0f;
            return btn;
        }

        /// Carbon HUD plate with a red accent edge, used as a backing for HUD groups.
        static Transform Plate(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var img = Img(parent, name, A.HudPlate, Color.white, Image.Type.Sliced);
            img.pixelsPerUnitMultiplier = A.HudPlate != null ? A.HudPlate.rect.height / size.y : 1f;
            Place(img.gameObject, anchor, pivot, pos, size);
            return img.transform;
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

        static void Outlined(Graphic g)
        {
            var o = g.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 0.85f);
            o.effectDistance = new Vector2(2f, -2f);
            Shadowed(g, 0.6f, new Vector2(3f, -4f));
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
