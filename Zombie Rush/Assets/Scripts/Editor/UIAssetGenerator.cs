using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ZombieRush.EditorTools
{
    /// Loads the UI art kit (Assets/UI/Art, Assets/UI/Icons, Assets/UI/Fonts),
    /// applying the right import settings (sprite borders for 9-slicing, tiling,
    /// compression). A few tiny utility sprites are generated if missing.
    /// Replace any PNG with your own art (same name) and rebuild the UI.
    public static class UIAssetGenerator
    {
        public const string ArtFolder = "Assets/UI/Art";
        public const string IconFolder = "Assets/UI/Icons";
        public const string ProIconFolder = "Assets/UI/IconsPro";
        public const string GeneratedFolder = "Assets/UI/Generated";
        public const string FontFolder = "Assets/UI/Fonts";
        const string BloodMaterialFolder = "Assets/Resources/ZombieRush";
        const string BloodMaterialPath = BloodMaterialFolder + "/BloodParticle.mat";

        public class Set
        {
            public Sprite White, Circle, GradientH;
            public Sprite Panel, Frame, ButtonPlate, Brush, BrushThin, Blood, Grain, Hazard;
            public Sprite RoundButton, RingTicks, StickBase, StickKnob, BarFrame, BarFill, Glow, Vignette, Divider, Chevron;
            // Blood & carbon kit (v2): slanted carbon buttons, torn red panels, styled icons.
            public Sprite BtnSlant, BtnSlantGlow, BtnSquare, BtnSquareGlow, PanelGrunge, BloodFrame, HudPlate;
            public Sprite BarSlantFrame, BarSlantFill, TouchDisc, StarFull, StarEmpty, LogoImage, ModeDay, ModeNight, ModeSolo, ModeSquad, ModeGlow;
            public Font Bold, Medium, Logo;
            readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>();
            readonly Dictionary<string, Sprite> proIcons = new Dictionary<string, Sprite>();

            /// Pre-coloured styled icon from Assets/UI/IconsPro (use with Image colour white).
            public Sprite IconX(string name)
            {
                if (proIcons.TryGetValue(name, out var s)) return s;
                s = LoadSprite($"{ProIconFolder}/iconx_{name}.png", Vector4.zero, TextureWrapMode.Clamp, 256);
                if (s == null) { Debug.LogWarning($"Zombie Rush UI: icon '{name}' not found in {ProIconFolder}."); }
                proIcons[name] = s;
                return s;
            }

            public Sprite Icon(string name)
            {
                if (icons.TryGetValue(name, out var s)) return s;
                s = LoadSprite($"{IconFolder}/icon_{name}.png", Vector4.zero, TextureWrapMode.Clamp, 256);
                if (s == null) Debug.LogWarning($"Zombie Rush UI: icon '{name}' not found in {IconFolder}.");
                icons[name] = s;
                return s;
            }
        }

        public static Set Ensure()
        {
            EnsureFolder(GeneratedFolder);
            var set = new Set
            {
                White = Generate("ui_white", 8, 8, (x, y) => 1f),
                Circle = Generate("ui_circle", 128, 128, (x, y) => Circle(x, y, 128)),
                GradientH = Generate("ui_gradient_h", 256, 8, (x, y) => SmoothStep(1f, 0f, (x + 0.5f) / 256f)),

                Panel = Art("panel_metal", new Vector4(44, 44, 44, 44)),
                Frame = Art("frame_brackets", new Vector4(48, 48, 48, 48)),
                ButtonPlate = Art("button_plate", new Vector4(32, 30, 32, 30)),
                Brush = Art("brush_stroke", new Vector4(160, 0, 160, 0)),
                BrushThin = Art("brush_stroke_thin", new Vector4(140, 0, 140, 0)),
                Blood = Art("blood_splatter", Vector4.zero),
                Grain = Art("grain", Vector4.zero, TextureWrapMode.Repeat),
                Hazard = Art("hazard_stripes", Vector4.zero, TextureWrapMode.Repeat),
                RoundButton = Art("round_button", Vector4.zero),
                RingTicks = Art("ring_ticks", Vector4.zero),
                StickBase = Art("stick_base", Vector4.zero),
                StickKnob = Art("stick_knob", Vector4.zero),
                BarFrame = Art("bar_frame", new Vector4(16, 16, 16, 16)),
                BarFill = Art("bar_fill", new Vector4(10, 10, 10, 10)),
                Glow = Art("glow", Vector4.zero),
                Vignette = Art("vignette_soft", Vector4.zero),
                Divider = Art("divider", Vector4.zero),
                Chevron = Art("chevron", Vector4.zero),

                BtnSlant = Art("btn_slant", new Vector4(116, 0, 116, 0)),
                BtnSlantGlow = Art("btn_slant_glow", new Vector4(116, 0, 116, 0)),
                BtnSquare = Art("btn_slant_sq", Vector4.zero),
                BtnSquareGlow = Art("btn_slant_sq_glow", Vector4.zero),
                PanelGrunge = Art("panel_grunge", new Vector4(180, 180, 180, 180)),
                BloodFrame = Art("screen_blood_frame", Vector4.zero),
                HudPlate = Art("hud_plate", new Vector4(60, 0, 40, 0)),
                BarSlantFrame = Art("bar_slant_frame", new Vector4(24, 0, 24, 0)),
                BarSlantFill = Art("bar_slant_fill", new Vector4(20, 0, 20, 0)),
                TouchDisc = Art("touch_disc", Vector4.zero),
                StarFull = Art("star_full", Vector4.zero),
                StarEmpty = Art("star_empty", Vector4.zero),
                LogoImage = Art("logo_zombie_rush", Vector4.zero),
                ModeDay = Art("mode_day", Vector4.zero),
                ModeNight = Art("mode_night", Vector4.zero),
                ModeSolo = Art("mode_solo", Vector4.zero),
                ModeSquad = Art("mode_squad", Vector4.zero),
                ModeGlow = Art("mode_card_glow", Vector4.zero),
            };

            set.Bold = LoadFont("Oswald-Bold.ttf");
            set.Medium = LoadFont("Oswald-Medium.ttf");
            set.Logo = LoadFont("RubikDirt-Regular.ttf");
            if (set.Logo == null) set.Logo = set.Bold;

            EnsureBloodMaterial(set.Circle);
            return set;
        }

        static Sprite Art(string name, Vector4 border, TextureWrapMode wrap = TextureWrapMode.Clamp)
        {
            var s = LoadSprite($"{ArtFolder}/{name}.png", border, wrap, 2048);
            if (s == null) Debug.LogWarning($"Zombie Rush UI: art '{name}' missing from {ArtFolder}.");
            return s;
        }

        static Sprite LoadSprite(string path, Vector4 border, TextureWrapMode wrap, int maxSize)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return null;

            bool dirty = importer.textureType != TextureImporterType.Sprite
                || importer.spriteBorder != border
                || importer.wrapMode != wrap
                || importer.mipmapEnabled
                || importer.maxTextureSize != maxSize;
            if (dirty)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.wrapMode = wrap;
                importer.filterMode = FilterMode.Bilinear;
                importer.maxTextureSize = maxSize;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.spriteBorder = border;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static Font LoadFont(string file)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>($"{FontFolder}/{file}");
            if (font != null) return font;
            Debug.LogWarning($"Zombie Rush UI: font {file} not found in {FontFolder}; using Unity's built-in font.");
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        // ---- tiny generated utility sprites --------------------------------------

        static Sprite Generate(string name, int w, int h, System.Func<int, int, float> alpha)
        {
            string path = $"{GeneratedFolder}/{name}.png";
            if (!File.Exists(path))
            {
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                var pixels = new Color32[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        pixels[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(x, y)) * 255f));
                tex.SetPixels32(pixels);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            return LoadSprite(path, Vector4.zero, TextureWrapMode.Clamp, 256);
        }

        static float Circle(int x, int y, int size)
        {
            float r = size * 0.5f - 1.5f;
            float d = (new Vector2(x + 0.5f, y + 0.5f) - Vector2.one * size * 0.5f).magnitude - r;
            return 0.5f - d;
        }

        static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// Blood particles use a material asset (not Shader.Find at runtime, which
        /// can fail in builds when the shader gets stripped).
        static void EnsureBloodMaterial(Sprite circle)
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(BloodMaterialPath) != null) return;

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) { Debug.LogWarning("Zombie Rush: URP particle shader not found; blood material not created."); return; }

            EnsureFolder(BloodMaterialFolder);
            var mat = new Material(shader) { name = "BloodParticle" };
            mat.SetColor("_BaseColor", new Color(0.5f, 0.02f, 0.02f));
            if (circle != null) mat.SetTexture("_BaseMap", circle.texture);
            mat.SetFloat("_AlphaClip", 1f);
            mat.SetFloat("_Cutoff", 0.5f);
            mat.EnableKeyword("_ALPHATEST_ON");
            AssetDatabase.CreateAsset(mat, BloodMaterialPath);
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
