using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using ZombieRush.UI.Kit;

namespace ZombieRush.EditorTools
{
    /// Re-skins the lobby prefab (Assets/Resources/ZombieRush/UI/Lobby.prefab) with the "blood moon" art in
    /// Assets/UI/Lobby: painted backdrop, logo image, a red rusted PLAY SOLO plate, rusty HOST SQUAD plate,
    /// a bolted squad-code slot and a chained JOIN plate. Buttons, labels, icons and their wiring are kept;
    /// only the art and spacing change. Safe to re-run; replace a PNG with the same name and run it again.
    public static class LobbyRestyler
    {
        const string ArtDir = "Assets/UI/Lobby";
        const string LobbyPrefab = "Assets/Resources/ZombieRush/UI/Lobby.prefab";
        static readonly string[] Cutouts = { "logo", "btn_solo", "btn_host", "code_slot", "btn_join" };
        const float JoinPlateShare = 0.56f;   // the plate is the top ~56% of btn_join.png, the chain hangs below

        [MenuItem("Zombie Rush/UI/Restyle Lobby (blood moon)", priority = 2)]
        static void Menu() => Debug.Log("Zombie Rush: " + Run());

        public static string Run()
        {
            foreach (var n in Cutouts) if (!System.IO.File.Exists($"{ArtDir}/{n}.png")) return $"Lobby restyle: FAILED, missing {ArtDir}/{n}.png";
            foreach (var n in Cutouts) BioHudInstaller.TrimTransparent($"{ArtDir}/{n}.png");
            AssetDatabase.Refresh();
            Import("lobby_bg", 2048);
            foreach (var n in Cutouts) Import(n, 1024);

            var root = PrefabUtility.LoadPrefabContents(LobbyPrefab);
            try
            {
                var menu = root.transform.Find("Menu");
                var safe = menu != null ? menu.Find("SafeArea") : null;
                if (safe == null) return "Lobby restyle: FAILED, Menu/SafeArea not found in " + LobbyPrefab;

                // backdrop: the painted scene, with lighter shading so the moon and horde show
                var bd = menu.Find("Backdrop")?.GetComponent<Image>();
                if (bd != null) { bd.sprite = Sp("lobby_bg"); bd.color = Color.white; }
                SetAlpha(menu.Find("FadeLeft"), 0.55f);
                SetAlpha(menu.Find("Vignette"), 0.5f);

                // logo: the painted ZOMBIE RUSH replaces the text logo (kept, hidden)
                var logo = safe.Find("Logo");
                if (logo != null)
                {
                    foreach (Transform c in logo) c.gameObject.SetActive(c.name == "LogoArt");
                    var art = logo.Find("LogoArt")?.GetComponent<Image>();
                    if (art == null)
                    {
                        var go = new GameObject("LogoArt", typeof(RectTransform)) { layer = 5 };
                        go.transform.SetParent(logo, false);
                        art = go.AddComponent<Image>();
                    }
                    art.sprite = Sp("logo"); art.preserveAspect = true; art.raycastTarget = false; art.color = Color.white;
                    var rt = art.rectTransform;
                    rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(0, 0); rt.offsetMax = new Vector2(0, 0);
                    ((RectTransform)logo).anchoredPosition = new Vector2(60, -24);
                    ((RectTransform)logo).sizeDelta = new Vector2(860, 400);
                }

                // spacing: the plates are taller than the old brush strokes
                Y(safe, "Tagline", -446);
                Y(safe, "Solo", -552); Y(safe, "SoloNote", -646);
                Y(safe, "Host", -742); Y(safe, "HostNote", -836);
                Y(safe, "Code", -928); Y(safe, "Join", -916);
                Y(safe, "Status", -1018);

                Plate(safe.Find("Solo"), "btn_solo", 1.0f);
                Plate(safe.Find("Host"), "btn_host", 1.0f);
                JoinPlate(safe.Find("Join"));

                // the code field sits in a bolted slot
                var code = safe.Find("Code");
                if (code != null)
                {
                    var back = code.Find("Back")?.GetComponent<Image>();
                    if (back != null) { back.sprite = Sp("code_slot"); back.type = Image.Type.Simple; back.color = Color.white; Fit(back.rectTransform, (RectTransform)code, 1.06f); }
                    var line = code.Find("Line"); if (line != null) line.gameObject.SetActive(false);
                }

                PrefabUtility.SaveAsPrefabAsset(root, LobbyPrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            return "Lobby restyled: " + LobbyPrefab;
        }

        /// Swaps a StrokeBtn's painted stroke for a metal plate (keeping its aspect) and retunes the hover tint.
        static void Plate(Transform btn, string sprite, float widthMul)
        {
            if (btn == null) return;
            var paint = btn.Find("Content/Paint")?.GetComponent<Image>();
            if (paint == null) return;
            paint.sprite = Sp(sprite); paint.type = Image.Type.Simple; paint.preserveAspect = false;
            Fit(paint.rectTransform, (RectTransform)btn, widthMul);
            Tint(btn, paint);
        }

        static void JoinPlate(Transform btn)
        {
            if (btn == null) return;
            var paint = btn.Find("Content/Paint")?.GetComponent<Image>();
            if (paint == null || paint.sprite == null && Sp("btn_join") == null) return;
            paint.sprite = Sp("btn_join"); paint.type = Image.Type.Simple; paint.preserveAspect = false;
            var b = (RectTransform)btn; var rt = paint.rectTransform;
            float aspect = paint.sprite.rect.width / paint.sprite.rect.height;
            float h = b.sizeDelta.y * 1.12f / JoinPlateShare, w = h * aspect;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(w, h); rt.anchoredPosition = new Vector2(0, b.sizeDelta.y * 0.06f);
            Tint(btn, paint);
        }

        static void Tint(Transform btn, Image paint)
        {
            var fx = btn.GetComponent<StampButton>();
            if (fx == null) return;
            fx.ink = paint;
            fx.inkNormal = new Color(0.9f, 0.9f, 0.9f, 1f);
            fx.inkHover = Color.white;
            fx.inkPressed = new Color(0.6f, 0.6f, 0.6f, 1f);
            paint.color = fx.inkNormal;
        }

        /// Centres 'rt' on 'host', 'widthMul' times as wide, with the sprite's own aspect for the height.
        static void Fit(RectTransform rt, RectTransform host, float widthMul)
        {
            var img = rt.GetComponent<Image>();
            float aspect = img != null && img.sprite != null ? img.sprite.rect.width / img.sprite.rect.height : host.sizeDelta.x / host.sizeDelta.y;
            float w = host.sizeDelta.x * widthMul;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, w / aspect); rt.anchoredPosition = Vector2.zero;
        }

        static void Y(Transform parent, string child, float y)
        {
            var t = parent.Find(child) as RectTransform;
            if (t != null) t.anchoredPosition = new Vector2(t.anchoredPosition.x, y);
        }

        static void SetAlpha(Transform t, float a)
        {
            var g = t != null ? t.GetComponent<Graphic>() : null;
            if (g != null) { var c = g.color; c.a = a; g.color = c; }
        }

        static Sprite Sp(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtDir}/{name}.png");

        static void Import(string name, int maxSize)
        {
            if (!(AssetImporter.GetAtPath($"{ArtDir}/{name}.png") is TextureImporter ti)) return;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.maxTextureSize = maxSize;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
        }
    }
}
