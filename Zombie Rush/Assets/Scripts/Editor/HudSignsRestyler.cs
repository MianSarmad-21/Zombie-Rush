using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using ZombieRush.UI;

namespace ZombieRush.EditorTools
{
    /// Re-skins two parts of the gameplay HUD (Assets/UI/Kit/Prefabs/KitGameUI.prefab) with the art in Assets/UI/HudKit:
    ///   PC key hints (V / T / P) : round metal key badges on rusty label plates; the turret plate is red with a padlock;
    ///   wave panel               : a hazard-taped metal WAVE sign with a red skull, top centre, and the
    ///                              "GEAR UP / 12 LEFT / CLEARED" line on a strip of hazard tape under it.
    /// Scripts, texts and wiring are kept; only art and placement change. Safe to re-run.
    public static class HudSignsRestyler
    {
        const string ArtDir = "Assets/UI/HudKit";
        const string GamePrefab = "Assets/UI/Kit/Prefabs/KitGameUI.prefab";
        static readonly string[] Cutouts = { "key_badge", "plate_dark", "plate_red", "padlock", "wave_sign", "tape", "skull_red" };

        static readonly Color Bone = new Color(0.93f, 0.91f, 0.85f);
        static readonly Color Blood = new Color(0.78f, 0.08f, 0.06f);
        static readonly Color Ink = new Color(0.07f, 0.06f, 0.05f);

        [MenuItem("Zombie Rush/UI/Restyle HUD key hints + wave sign", priority = 3)]
        static void Menu() => Debug.Log("Zombie Rush: " + Run());

        public static string Run()
        {
            foreach (var n in Cutouts) if (!System.IO.File.Exists($"{ArtDir}/{n}.png")) return $"HUD signs: FAILED, missing {ArtDir}/{n}.png";
            foreach (var n in Cutouts) BioHudInstaller.TrimTransparent($"{ArtDir}/{n}.png");
            AssetDatabase.Refresh();
            foreach (var n in Cutouts) Import(n);

            var title = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/RubikDirt-Regular.ttf");
            var num = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/BebasNeue-Regular.ttf");
            var root = PrefabUtility.LoadPrefabContents(GamePrefab);
            try
            {
                var hud = root.transform.Find("SafeArea/HUD");
                if (hud == null) return "HUD signs: FAILED, SafeArea/HUD not found";
                Hint(hud.Find("ViewHint"), "plate_dark");
                Hint(hud.Find("TurretHint"), "plate_red");
                Hint(hud.Find("ExpandHint"), "plate_dark");
                TurretLock(hud.Find("TurretHint"));
                var ep = hud.Find("ExpandHint")?.GetComponent<ExpandPromptUI>();
                if (ep != null) ep.format = "EXPAND ZONE  <color=#F5B83D>{0} SCRAP</color>";
                Wave(hud, title, num);
                WeaponClearOfGrenades(hud);
                Touch(root.transform.Find("SafeArea/TouchControls/Controls"));
                PrefabUtility.SaveAsPrefabAsset(root, GamePrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            return "HUD key hints + wave sign restyled in " + GamePrefab;
        }

        // ------------------------------------------------------------------ key hints
        static void Hint(Transform holder, string plate)
        {
            if (holder == null) return;
            var h = (RectTransform)holder;
            h.sizeDelta = new Vector2(460, 66);
            var content = holder.Find("Content");
            if (content == null) return;

            var paint = content.Find("Paint")?.GetComponent<Image>();
            if (paint != null)
            {
                paint.sprite = Sp(plate); paint.type = Image.Type.Simple; paint.color = Color.white;
                var rt = paint.rectTransform;
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(30, -4); rt.offsetMax = new Vector2(0, 4);
            }
            var key = content.Find("Key")?.GetComponent<Image>();
            if (key != null)
            {
                key.sprite = Sp("key_badge"); key.type = Image.Type.Simple; key.preserveAspect = true; key.color = Color.white;
                var rt = key.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f); rt.pivot = new Vector2(0, 0.5f);
                rt.anchoredPosition = new Vector2(-6, 0); rt.sizeDelta = new Vector2(78, 78);
                key.transform.SetAsLastSibling();   // the badge sits on top of the plate's end
                var k = key.transform.Find("K")?.GetComponent<Text>();
                if (k != null) { k.fontSize = 42; k.color = Bone; }
            }
            var icon = content.Find("Icon") as RectTransform;
            if (icon != null) icon.anchoredPosition = new Vector2(110, 0);
            var label = content.Find("Label") as RectTransform;
            if (label != null) { label.anchoredPosition = new Vector2(136, 2); label.sizeDelta = new Vector2(310, 50); }
        }

        static void TurretLock(Transform holder)
        {
            if (holder == null) return;
            var lockT = holder.GetComponentInChildren<Transform>(true);
            foreach (var t in holder.GetComponentsInChildren<Transform>(true)) if (t.name == "Lock") lockT = t;
            if (lockT == null || lockT.name != "Lock") return;
            var content = holder.Find("Content");
            lockT.SetParent(content, false);
            var img = lockT.GetComponent<Image>();
            if (img != null) { img.sprite = Sp("padlock"); img.color = Color.white; img.preserveAspect = true; }
            var rt = (RectTransform)lockT;
            rt.anchorMin = rt.anchorMax = new Vector2(1, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(-34, -2); rt.sizeDelta = new Vector2(50, 50); rt.localRotation = Quaternion.Euler(0, 0, -8);
        }

        // ------------------------------------------------------------------ wave sign
        static void Wave(Transform hud, Font title, Font num)
        {
            var wave = hud.Find("Wave") as RectTransform;
            if (wave == null) return;

            // top centre on PC (touch already had it there); the layout component re-applies these poses at runtime
            var pcPose = new RectPose { anchorMin = new Vector2(0.5f, 1), anchorMax = new Vector2(0.5f, 1), pivot = new Vector2(0.5f, 1), anchoredPosition = new Vector2(0, -14), scale = 0.8f };
            var layout = hud.GetComponent<ResponsiveLayout>();
            if (layout != null)
                for (int i = 0; i < layout.targets.Length; i++)
                    if (layout.targets[i] == wave)
                    {
                        layout.pcPoses[i] = pcPose;
                        var tp = layout.touchPoses[i]; tp.scale = 0.6f; tp.anchoredPosition = new Vector2(0, -8); layout.touchPoses[i] = tp;
                    }
            pcPose.ApplyTo(wave);
            wave.sizeDelta = new Vector2(560, 290);

            var splat = wave.Find("Splat"); if (splat != null) splat.gameObject.SetActive(false);

            // the sign
            var signT = wave.Find("Sign");
            Image sign;
            if (signT == null) { sign = NewImg("Sign", wave); sign.transform.SetAsFirstSibling(); }
            else sign = signT.GetComponent<Image>();
            sign.sprite = Sp("wave_sign"); sign.color = Color.white; sign.raycastTarget = false;
            float aspect = sign.sprite.rect.width / sign.sprite.rect.height;
            var srt = sign.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 1); srt.pivot = new Vector2(0.5f, 1);
            srt.anchoredPosition = Vector2.zero; srt.sizeDelta = new Vector2(540, 540 / aspect);

            var skullT = sign.transform.Find("SignSkull");
            var skull = skullT != null ? skullT.GetComponent<Image>() : NewImg("SignSkull", sign.transform);
            skull.sprite = Sp("skull_red"); skull.preserveAspect = true; skull.color = Color.white; skull.raycastTarget = false;
            var kr = skull.rectTransform; kr.anchorMin = kr.anchorMax = new Vector2(0.16f, 0.5f); kr.pivot = new Vector2(0.5f, 0.5f);
            kr.anchoredPosition = new Vector2(0, -4); kr.sizeDelta = new Vector2(112, 112);

            // WAVE + number on the sign
            var label = wave.Find("Label")?.GetComponent<Text>() ?? sign.transform.Find("Label")?.GetComponent<Text>();
            if (label != null)
            {
                label.transform.SetParent(sign.transform, false);
                label.font = title; label.fontSize = 92; label.color = Bone; label.alignment = TextAnchor.MiddleLeft;
                var lr = label.rectTransform; lr.anchorMin = lr.anchorMax = new Vector2(0.3f, 0.48f); lr.pivot = new Vector2(0, 0.5f);
                lr.anchoredPosition = Vector2.zero; lr.sizeDelta = new Vector2(270, 110);
            }
            var number = wave.Find("Number")?.GetComponent<Text>() ?? sign.transform.Find("Number")?.GetComponent<Text>();
            if (number != null)
            {
                number.transform.SetParent(sign.transform, false);
                number.font = title; number.fontSize = 104; number.color = Blood; number.alignment = TextAnchor.MiddleRight;
                var nr = number.rectTransform; nr.anchorMin = nr.anchorMax = new Vector2(0.92f, 0.46f); nr.pivot = new Vector2(1, 0.5f);
                nr.anchoredPosition = Vector2.zero; nr.sizeDelta = new Vector2(130, 120);
            }

            // GEAR UP / N LEFT on hazard tape, overlapping the sign's lower edge
            var left = wave.Find("Left") as RectTransform;
            if (left != null)
            {
                left.anchorMin = left.anchorMax = new Vector2(0.5f, 1); left.pivot = new Vector2(0.5f, 0.5f);
                left.anchoredPosition = new Vector2(40, -srt.sizeDelta.y + 10); left.sizeDelta = new Vector2(330, 70);
                var oldSkull = left.Find("Skull"); if (oldSkull != null) oldSkull.gameObject.SetActive(false);
                var tapeT = left.Find("Tape");
                var tape = tapeT != null ? tapeT.GetComponent<Image>() : NewImg("Tape", left);
                tape.transform.SetAsFirstSibling();
                tape.sprite = Sp("tape"); tape.color = Color.white; tape.raycastTarget = false;
                var tr = tape.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(-30, -8); tr.offsetMax = new Vector2(30, 8);
                tr.localRotation = Quaternion.Euler(0, 0, -2f);
                var text = left.Find("Text")?.GetComponent<Text>();
                if (text != null)
                {
                    text.font = num; text.fontSize = 50; text.color = Ink; text.alignment = TextAnchor.MiddleCenter;
                    var r = text.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = new Vector2(20, 0); r.offsetMax = new Vector2(-20, -2);
                    r.localRotation = Quaternion.Euler(0, 0, -2f);
                    var sh = text.GetComponent<Shadow>(); if (sh != null) sh.effectColor = new Color(1f, 0.9f, 0.3f, 0.35f);
                }
            }
        }

        // ------------------------------------------------------------------ touch layout fixes
        /// On touch the weapon panel sat bottom-centre right on top of the grenade counter; shift it left.
        static void WeaponClearOfGrenades(Transform hud)
        {
            var wpn = hud.Find("Weapon") as RectTransform;
            var layout = hud.GetComponent<ResponsiveLayout>();
            if (wpn == null || layout == null) return;
            for (int i = 0; i < layout.targets.Length; i++)
                if (layout.targets[i] == wpn)
                {
                    var tp = layout.touchPoses[i]; tp.anchoredPosition = new Vector2(-190, 6); tp.scale = 0.72f; layout.touchPoses[i] = tp;
                }
        }

        // ------------------------------------------------------------------ touch controls
        const string TouchDir = ArtDir + "/Touch";
        static readonly string[] TouchIcons = { "t_fire", "t_reload", "t_jump", "t_crouch", "t_aim", "t_swap", "t_side", "t_eye", "t_turret", "t_pause", "t_fire_disc" };

        /// Every touch disc becomes a rusty metal badge (FIRE a red metal disc) with a bold painted stencil icon;
        /// the pause button moves left of the compass so they no longer overlap.
        static void Touch(Transform controls)
        {
            if (controls == null) return;
            foreach (var n in TouchIcons)
            {
                string p = $"{TouchDir}/{n}.png";
                if (!System.IO.File.Exists(p)) continue;
                if (n != "t_fire_disc") LumaKey(p);
                BioHudInstaller.TrimTransparent(p);
            }
            AssetDatabase.Refresh();
            foreach (var n in TouchIcons) ImportAt($"{TouchDir}/{n}.png");

            Btn(controls.Find("Fire"), "t_fire", fire: true);
            Btn(controls.Find("Reload"), "t_reload");
            Btn(controls.Find("Jump"), "t_jump");
            Btn(controls.Find("Crouch"), "t_crouch");
            Btn(controls.Find("Aim"), "t_aim");
            Btn(controls.Find("Swap"), "t_swap");
            Btn(controls.Find("Shoulder"), "t_side");
            Btn(controls.Find("TurretTouch/Turret"), "t_turret");
            Btn(controls.Find("ViewTouch/View"), "t_eye");

            var lockT = controls.Find("TurretTouch/Turret/Lock");
            if (lockT != null)
            {
                var li = lockT.GetComponent<Image>(); if (li != null) { li.sprite = Sp("padlock"); li.color = Color.white; li.preserveAspect = true; }
                var lr = (RectTransform)lockT; lr.sizeDelta = new Vector2(44, 44); lr.anchoredPosition = new Vector2(-10, -10);
            }

            // pause: same badge, and out from under the compass
            var pause = controls.Find("Pause") as RectTransform;
            if (pause != null)
            {
                pause.anchoredPosition = new Vector2(-250, -22);
                pause.sizeDelta = new Vector2(96, 96);
                var disc = pause.Find("Disc")?.GetComponent<Image>();
                if (disc != null) { disc.sprite = Sp("key_badge"); disc.color = Color.white; }
                var ic = pause.Find("Icon")?.GetComponent<Image>();
                if (ic != null) { ic.sprite = TSp("t_pause"); ic.preserveAspect = true; ((RectTransform)ic.transform).sizeDelta = new Vector2(46, 46); }
            }

            // joystick: metal ring base, red metal knob
            var sb = controls.Find("MoveArea/StickBase");
            if (sb != null)
            {
                var glass = sb.Find("Glass")?.GetComponent<Image>();
                if (glass != null) { glass.sprite = Sp("key_badge"); glass.color = new Color(1, 1, 1, 0.55f); }
                var ticks = sb.Find("Ticks")?.GetComponent<Image>();
                if (ticks != null) ticks.color = new Color(0.93f, 0.91f, 0.85f, 0.35f);
                var knob = sb.Find("Knob")?.GetComponent<Image>();
                if (knob != null)
                {
                    knob.sprite = TSp("t_fire_disc"); knob.color = Color.white;
                    var ring = knob.transform.Find("Ring"); if (ring != null) ring.gameObject.SetActive(false);
                }
            }
        }

        static void Btn(Transform b, string icon, bool fire = false)
        {
            if (b == null) return;
            float size = ((RectTransform)b).sizeDelta.x;
            var disc = b.Find("Disc")?.GetComponent<Image>();
            if (disc != null) { disc.sprite = fire ? TSp("t_fire_disc") : Sp("key_badge"); disc.color = Color.white; disc.preserveAspect = true; }
            foreach (var n in new[] { "Splat", "Ring" }) { var t = b.Find(n); if (t != null) t.gameObject.SetActive(false); }
            var ic = b.Find("Icon")?.GetComponent<Image>();
            if (ic != null)
            {
                ic.sprite = TSp(icon); ic.preserveAspect = true;
                var r = (RectTransform)ic.transform; r.sizeDelta = Vector2.one * size * (fire ? 0.42f : 0.44f); r.anchoredPosition = new Vector2(0, size * 0.08f);
            }
            var cap = b.Find("Caption") as RectTransform;
            if (cap != null) cap.anchoredPosition = new Vector2(0, size * 0.2f);
            var tb = b.GetComponent<ZombieRush.UI.TouchButton>();
            if (tb != null) { tb.normalColor = fire ? Color.white : new Color(0.93f, 0.91f, 0.85f); tb.pressedColor = new Color(1f, 0.75f, 0.3f); }
        }

        /// The stencil icons come as bone paint on solid black: turn brightness into alpha (white RGB, so the
        /// button can tint it). Skips files that already have transparency, so re-running is safe.
        static void LumaKey(string path)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(System.IO.File.ReadAllBytes(path))) { Object.DestroyImmediate(tex); return; }
            var px = tex.GetPixels32();
            bool opaque = true;
            for (int i = 0; i < px.Length; i += 97) if (px[i].a < 250) { opaque = false; break; }
            if (opaque)
            {
                for (int i = 0; i < px.Length; i++)
                {
                    float l = (0.299f * px[i].r + 0.587f * px[i].g + 0.114f * px[i].b) / 255f;
                    float a = Mathf.Clamp01((l - 0.12f) / 0.55f);
                    px[i] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
                tex.SetPixels32(px); tex.Apply();
                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            Object.DestroyImmediate(tex);
        }

        static Sprite TSp(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{TouchDir}/{name}.png");

        static void ImportAt(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
            ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single;
            ti.alphaIsTransparency = true; ti.mipmapEnabled = false; ti.wrapMode = TextureWrapMode.Clamp;
            ti.maxTextureSize = 512; ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
        }

        // ------------------------------------------------------------------ helpers
        static Image NewImg(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = 5 };
            go.transform.SetParent(parent, false);
            return go.AddComponent<Image>();
        }

        static Sprite Sp(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtDir}/{name}.png");

        static void Import(string name)
        {
            if (!(AssetImporter.GetAtPath($"{ArtDir}/{name}.png") is TextureImporter ti)) return;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.maxTextureSize = 1024;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
        }
    }
}
