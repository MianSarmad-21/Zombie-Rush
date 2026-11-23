using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using ZombieRush.UI.Kit;

namespace ZombieRush.EditorTools
{
    /// Installs the "bio" HUD pieces into the gameplay UI prefab (Assets/UI/Kit/Prefabs/KitGameUI.prefab):
    ///   top left  : the INFECTION METER (beating heart, veined glass tube = health, HP read-out, 3 mutation orbs)
    ///   top right : the biomechanical COMPASS (rose turns with the view, eye glows when zombies are close)
    /// It hides the old Vitals and Radar (kept, not deleted) and nudges the armour / scrap / expand prompt down.
    /// Art lives in Assets/UI/Bio (replace a PNG with the same name and run this again). Safe to re-run.
    public static class BioHudInstaller
    {
        const string ArtDir = "Assets/UI/Bio";
        const string GamePrefab = "Assets/UI/Kit/Prefabs/KitGameUI.prefab";
        static readonly string[] Painted = { "compass_frame", "compass_dial", "heart", "tube", "orb" };

        // where things sit inside the trimmed art (fractions of the sprite), tuned to the current PNGs
        // compass_frame.png is 995x920; its round hole is centred at (0.483, 0.484), ~650 px across
        const float FrameW = 250f, FrameH = 250f * 920f / 995f;
        static readonly Rect DialInFrame = new Rect(0.136f, 0.109f, 0.693f, 0.75f);  // x, y (from bottom-left), w, h
        static readonly Vector2 EyeInFrame = new Vector2(0.852f, 0.848f);
        const float EyeSize = 0.22f;
        // tube.png is 1713x252; the dark glass runs between the end caps (right end has growths inside)
        const float TubeW = 470f, TubeH = 470f * 252f / 1713f;
        const float MeterH = 10f + TubeH + 40f;   // tube plus the HP read-out under it
        static readonly Vector4 TubeInner = new Vector4(0.10f, 0.25f, 0.16f, 0.26f); // left, bottom, right, top

        /// Overall size of the meter and compass (1 = the original layout).
        const float HudScale = 0.75f;

        static readonly Color Toxic = new Color(0.55f, 1f, 0.25f);
        static readonly Color Bone = new Color(0.93f, 0.91f, 0.85f);

        [MenuItem("Zombie Rush/UI/Install Bio HUD (compass + infection meter)", priority = 1)]
        static void Menu() => Debug.Log("Zombie Rush: " + Install());

        public static string Install()
        {
            if (!AssetDatabase.IsValidFolder(ArtDir)) return "Bio HUD: FAILED, art folder missing: " + ArtDir;
            foreach (var n in Painted)
                if (!File.Exists($"{ArtDir}/{n}.png")) return $"Bio HUD: FAILED, missing {ArtDir}/{n}.png";
            foreach (var n in Painted) TrimTransparent($"{ArtDir}/{n}.png");
            MakeLiquid($"{ArtDir}/liquid.png");
            MakeGlow($"{ArtDir}/glow.png");
            AssetDatabase.Refresh();
            foreach (var n in new[] { "compass_frame", "compass_dial", "heart", "tube", "orb", "liquid", "glow" }) ImportAsSprite($"{ArtDir}/{n}.png");

            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/BebasNeue-Regular.ttf");
            var root = PrefabUtility.LoadPrefabContents(GamePrefab);
            try
            {
                var hud = root.transform.Find("SafeArea/HUD");
                if (hud == null) return "Bio HUD: FAILED, SafeArea/HUD not found in " + GamePrefab;
                foreach (var old in new[] { "BioVitals", "BioCompass" })
                {
                    var t = hud.Find(old);
                    if (t != null) Object.DestroyImmediate(t.gameObject);
                }
                SetActive(hud, "Vitals", false);
                SetActive(hud, "Radar", false);
                // stack the old pieces just under the meter
                float below = -8f - MeterH * HudScale;
                Move(hud, "Armor", new Vector2(110, below - 10f));
                Move(hud, "Pockets", new Vector2(60, below - 42f));
                Move(hud, "ExpandTouch", new Vector2(44, below - 122f));

                BuildMeter(hud, font);
                BuildCompass(hud, font);
                PrefabUtility.SaveAsPrefabAsset(root, GamePrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            return "Bio HUD installed into " + GamePrefab + " (old Vitals and Radar hidden).";
        }

        // ------------------------------------------------------------------ the infection meter
        static void BuildMeter(Transform hud, Font font)
        {
            var go = Node("BioVitals", hud);
            Place(go, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -8), new Vector2(TubeW + 20f, MeterH));
            go.transform.localScale = new Vector3(HudScale, HudScale, 1f);
            var meter = go.AddComponent<InfectionMeter>();

            // tube + liquid (just the health bar: no heart, title or mutation orbs)
            var tube = Img(go.transform, "Tube", Sp("tube"), Color.white);
            Place(tube.gameObject, new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -10), new Vector2(TubeW, TubeH));
            var tubeRt = tube.rectTransform;
            var trail = Img(tubeRt, "Trail", Sp("liquid"), new Color(1f, 0.25f, 0.15f, 0.55f));
            Inner(trail.rectTransform, tubeRt.sizeDelta);
            Filled(trail);
            var liquid = Img(tubeRt, "Liquid", Sp("liquid"), meter.healthy);
            Inner(liquid.rectTransform, tubeRt.sizeDelta);
            Filled(liquid);
            var shine = Img(tubeRt, "Shine", null, new Color(1f, 1f, 1f, 0.07f));
            Inner(shine.rectTransform, tubeRt.sizeDelta);
            shine.rectTransform.anchorMin = new Vector2(shine.rectTransform.anchorMin.x, 0.55f);
            meter.liquid = liquid; meter.trail = trail;

            var hp = Txt(go.transform, "HP", "100/100 HP", font, 38, Bone, TextAnchor.MiddleRight);
            Place(hp.gameObject, new Vector2(0, 1), new Vector2(1, 1), new Vector2(10 + TubeW - 70f, -10 - TubeH + 4), new Vector2(240, 44));
            Glow(hp, new Color(0f, 0f, 0f, 0.9f));
            meter.hpText = hp;
            meter.orbs = new Image[0];
        }

        static void Inner(RectTransform rt, Vector2 parentSize)
        {
            rt.anchorMin = new Vector2(TubeInner.x, TubeInner.y);
            rt.anchorMax = new Vector2(1f - TubeInner.z, 1f - TubeInner.w);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static void Filled(Image img)
        {
            img.type = Image.Type.Filled; img.fillMethod = Image.FillMethod.Horizontal; img.fillOrigin = 0; img.fillAmount = 1f;
        }

        // ------------------------------------------------------------------ the compass
        static void BuildCompass(Transform hud, Font font)
        {
            var go = Node("BioCompass", hud);
            Place(go, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-22, -14), new Vector2(FrameW, FrameH));
            go.transform.localScale = new Vector3(HudScale, HudScale, 1f);
            var compass = go.AddComponent<BioCompass>();

            // the rose sits UNDER the frame so the bezel covers its edge
            var dialHolder = Node("DialHolder", go.transform);
            var dh = (RectTransform)dialHolder.transform;
            dh.anchorMin = new Vector2(DialInFrame.x, DialInFrame.y);
            dh.anchorMax = new Vector2(DialInFrame.xMax, DialInFrame.yMax);
            dh.offsetMin = dh.offsetMax = Vector2.zero;
            var dial = Img(dialHolder.transform, "Dial", Sp("compass_dial"), Color.white); dial.preserveAspect = true;
            Stretch(dial.rectTransform);
            compass.dial = dial.rectTransform;

            var marker = Node("BaseMarker", dialHolder.transform); Stretch((RectTransform)marker.transform);
            var pip = Img(marker.transform, "Pip", null, Toxic);
            var pr = pip.rectTransform; pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 1f); pr.pivot = new Vector2(0.5f, 1f);
            pr.anchoredPosition = new Vector2(0, -2); pr.sizeDelta = new Vector2(10, 18);
            compass.baseMarker = (RectTransform)marker.transform;

            var frame = Img(go.transform, "Frame", Sp("compass_frame"), Color.white);
            Stretch(frame.rectTransform);

            // eye glow: a red bloom over the painted eyeball
            var eye = Node("Eye", go.transform);
            var er = (RectTransform)eye.transform;
            er.anchorMin = er.anchorMax = EyeInFrame; er.pivot = new Vector2(0.5f, 0.5f);
            er.sizeDelta = new Vector2(FrameW * EyeSize, FrameW * EyeSize);
            var glow = Img(eye.transform, "Glow", Sp("glow"), new Color(1f, 0.12f, 0.08f, 0f));
            Stretch(glow.rectTransform, -30);
            glow.enabled = false;
            compass.eye = er; compass.eyeGlow = glow;
        }

        // ------------------------------------------------------------------ art processing
        /// Crops fully transparent margins off a PNG so the sprite's rect is the art itself.
        internal static void TrimTransparent(string path)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(File.ReadAllBytes(path))) { Object.DestroyImmediate(tex); return; }
            int w = tex.width, h = tex.height, minX = w, minY = h, maxX = -1, maxY = -1;
            var px = tex.GetPixels32();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (px[y * w + x].a > 10) { if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
            if (maxX < 0) { Object.DestroyImmediate(tex); return; }
            int pad = 2;
            minX = Mathf.Max(0, minX - pad); minY = Mathf.Max(0, minY - pad);
            maxX = Mathf.Min(w - 1, maxX + pad); maxY = Mathf.Min(h - 1, maxY + pad);
            int cw = maxX - minX + 1, ch = maxY - minY + 1;
            if (cw < w || ch < h)
            {
                var crop = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
                crop.SetPixels(tex.GetPixels(minX, minY, cw, ch));
                crop.Apply();
                File.WriteAllBytes(path, crop.EncodeToPNG());
                Object.DestroyImmediate(crop);
            }
            Object.DestroyImmediate(tex);
        }

        /// A bubbly glowing liquid strip, white so the meter can tint it.
        static void MakeLiquid(string path)
        {
            const int W = 512, H = 64;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var rnd = new System.Random(7);
            var bubbles = new Vector3[70];
            for (int i = 0; i < bubbles.Length; i++) bubbles[i] = new Vector3(rnd.Next(W), rnd.Next(H), 1.5f + (float)rnd.NextDouble() * 4f);
            for (int y = 0; y < H; y++)
            {
                float v = (y + 0.5f) / H;
                float body = 0.55f + 0.45f * Mathf.Sin(v * Mathf.PI);          // brighter core
                float rim = Mathf.Clamp01(1f - Mathf.Abs(v - 0.78f) * 14f) * 0.5f; // a highlight band
                for (int x = 0; x < W; x++)
                {
                    float swirl = 0.08f * Mathf.Sin(x * 0.045f + y * 0.21f) + 0.06f * Mathf.Sin(x * 0.013f - y * 0.09f);
                    float b = Mathf.Clamp01(body + swirl + rim);
                    foreach (var bb in bubbles)
                    {
                        float d = Vector2.Distance(new Vector2(x, y), new Vector2(bb.x, bb.y));
                        if (d < bb.z) b = Mathf.Max(b, d > bb.z - 1.2f ? 1f : b * 0.82f);
                    }
                    tex.SetPixel(x, y, new Color(b, b, b, 0.96f));
                }
            }
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        static void MakeGlow(string path)
        {
            if (File.Exists(path)) return;
            const int S = 128;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(S / 2f, S / 2f)) / (S / 2f);
                    float a = Mathf.Clamp01(1f - d); a *= a;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        static void ImportAsSprite(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.maxTextureSize = 1024;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
        }

        // ------------------------------------------------------------------ helpers
        static Sprite Sp(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtDir}/{name}.png");

        static GameObject Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = 5 };
            go.transform.SetParent(parent, false);
            return go;
        }

        static void Place(GameObject go, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot; rt.anchoredPosition = pos; rt.sizeDelta = size;
        }

        static void Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
        }

        static Image Img(Transform parent, string name, Sprite sprite, Color color)
        {
            var img = Node(name, parent).AddComponent<Image>();
            img.sprite = sprite; img.color = color; img.raycastTarget = false;
            return img;
        }

        static Text Txt(Transform parent, string name, string text, Font font, int size, Color color, TextAnchor align)
        {
            var t = Node(name, parent).AddComponent<Text>();
            t.font = font; t.text = text; t.fontSize = size; t.color = color; t.alignment = align; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        static void Glow(Graphic g, Color? c = null)
        {
            var o = g.gameObject.AddComponent<Outline>();
            o.effectColor = c ?? new Color(0.1f, 0.35f, 0.05f, 0.6f); o.effectDistance = new Vector2(2, -2);
            var sh = g.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.85f); sh.effectDistance = new Vector2(3, -3);
        }

        static void SetActive(Transform hud, string child, bool on)
        {
            var t = hud.Find(child);
            if (t != null) t.gameObject.SetActive(on);
        }

        static void Move(Transform hud, string child, Vector2 pos)
        {
            var t = hud.Find(child) as RectTransform;
            if (t != null) t.anchoredPosition = pos;
        }
    }
}
