using SkiaSharp;
using static P;

static class IconArt {
  const int S = 1024;
  public static int Seed = 3;
  public static string ZombiePng;

  public static void Draw(string outDir) {
    if (Environment.GetEnvironmentVariable("ICON_PREVIEW") == "1") { Preview(outDir); return; }
    using var full = Render(Seed, false);
    using var title = Render(Seed, true);
    SaveImg(full, Path.Combine(outDir, "ZombieRush_Icon_1024.png"), S, false);
    SaveImg(full, Path.Combine(outDir, "ZombieRush_Icon_Rounded_1024.png"), S, true);
    SaveImg(title, Path.Combine(outDir, "ZombieRush_Icon_Title_1024.png"), S, true);
    foreach (var n in new[] { 512, 256, 128, 64 }) SaveImg(full, Path.Combine(outDir, $"ZombieRush_Icon_{n}.png"), n, true);
  }

  static void Preview(string outDir) {
    using var s = SKSurface.Create(new SKImageInfo(2048, 1024));
    for (int i = 0; i < 8; i++) { using var img = Render(i * 7 + 3, false); s.Canvas.DrawImage(img, SKRect.Create((i % 4) * 512, (i / 4) * 512, 512, 512)); }
    Save(s, Path.Combine(outDir, "icon_preview.png"));
  }

  static void SaveImg(SKImage img, string path, int size, bool round) {
    using var s = SKSurface.Create(new SKImageInfo(size, size)); var c = s.Canvas; c.Clear(SKColors.Transparent);
    if (round) { using var rr = new SKPath(); rr.AddRoundRect(new SKRect(0, 0, size, size), size * 0.18f, size * 0.18f); c.ClipPath(rr, antialias: true); }
    using var p = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
    c.DrawImage(img, new SKRect(0, 0, size, size), p);
    Save(s, path);
  }

  static SKImage Render(int seed, bool withTitle) {
    using var s = SKSurface.Create(new SKImageInfo(S, S)); var c = s.Canvas;
    var all = new SKRect(0, 0, S, S);
    var moon = new SKPoint(660, 330); float mr = 270;

    // night sky backdrop: deep blue-black with a blood-red horizon glow
    using (var p = new SKPaint { Shader = SKShader.CreateLinearGradient(new(0, 0), new(0, S), new[] { C("#070b16"), C("#111a30"), C("#3a0c0c"), C("#120304") }, new[] { 0, 0.5f, 0.82f, 1f }, SKShaderTileMode.Clamp) })
      c.DrawRect(all, p);
    Noise(c, all, 0.012f, 0.35f, 4);
    // moon
    Glow(c, moon.X, moon.Y, 620, C("#9fb6e6", 80));
    using (var p = new SKPaint { IsAntialias = true, Shader = SKShader.CreateRadialGradient(new(moon.X - 70, moon.Y - 80), mr * 1.4f, new[] { C("#fffdf6"), C("#efe6cf"), C("#b9b29c") }, new[] { 0, 0.55f, 1f }, SKShaderTileMode.Clamp) })
      c.DrawCircle(moon, mr, p);
    c.Save(); using (var clip = new SKPath()) { clip.AddCircle(moon.X, moon.Y, mr); c.ClipPath(clip, antialias: true); }
    Noise(c, new SKRect(moon.X - mr, moon.Y - mr, moon.X + mr, moon.Y + mr), 0.015f, 0.14f, 9, SKBlendMode.Multiply);
    var rc = new Random(5);
    for (int i = 0; i < 14; i++) using (var p = new SKPaint { IsAntialias = true, Color = C("#a29c86", (byte)rc.Next(25, 55)), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8) })
      c.DrawCircle(moon.X + rc.Next(-250, 250), moon.Y + rc.Next(-250, 250), rc.Next(12, 48), p);
    c.Restore();
    // thin clouds over the moon
    for (int i = 0; i < 3; i++) using (var p = new SKPaint { Color = C("#1a2236", (byte)rc.Next(60, 110)), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 14) })
      c.DrawOval(moon.X + rc.Next(-300, 300), moon.Y + rc.Next(-120, 260), rc.Next(160, 340), rc.Next(10, 22), p);

    // skyline silhouette
    var rs = new Random(21); float x = -20;
    using (var bp = new SKPaint { IsAntialias = true, Color = C("#07090f") })
      while (x < S) { float bw = rs.Next(50, 120), bh = rs.Next(90, 260); c.DrawRect(x, 820 - bh, bw, bh + 300, bp);
        for (int k = 0; k < 4; k++) if (rs.NextDouble() < 0.5) using (var wp = new SKPaint { Color = C("#ff9a3a", 200) }) { float wx = x + rs.Next(8, (int)bw - 14), wy = 820 - bh + rs.Next(12, (int)bh - 10); c.DrawRect(wx, wy, 7, 9, wp); Glow(c, wx + 3, wy + 4, 18, C("#ff8a2a", 60)); }
        x += bw + rs.Next(0, 18); }
    // ground glow + fog
    using (var p = new SKPaint { Shader = SKShader.CreateLinearGradient(new(0, 760), new(0, S), new[] { C("#ff3b1a", 0), C("#b3200f", 120), C("#200505", 255) }, new[] { 0, 0.45f, 1f }, SKShaderTileMode.Clamp) })
      c.DrawRect(0, 760, S, S - 760, p);

    // the zombie: hand-painted procedural head (ZombieFace.cs)
    using (var p = new SKPaint { Color = C("#000000", 190), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 30) }) c.DrawOval(512, 560, 300, 420, p);
    c.Save(); if (withTitle) { c.Translate(512, 70); c.Scale(0.72f); c.Translate(-512, -70); }
    ZombieFace.Draw(c);
    c.Restore();
    Glow(c, 900, 1000, 380, C("#ff4a14", 60));


    if (withTitle) {
      using (var p = new SKPaint { Shader = SKShader.CreateLinearGradient(new(0, 660), new(0, S), new[] { C("#000000", 0), C("#000000", 220) }, null, SKShaderTileMode.Clamp) }) c.DrawRect(0, 660, S, S - 660, p);
      using var logo = Img(@"Assets\UI\Art\logo_zombie_rush.png");
      float lw = 720, lh = lw * logo.Height / logo.Width; var dst = SKRect.Create((S - lw) / 2, S - lh - 34, lw, lh);
      using (var p = new SKPaint { ColorFilter = SKColorFilter.CreateBlendMode(C("#000000"), SKBlendMode.SrcIn), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 12) }) c.DrawBitmap(logo, dst, p);
      using (var p = new SKPaint { FilterQuality = SKFilterQuality.High }) c.DrawBitmap(logo, dst, p);
    }

    // vignette + grain + bevelled frame
    using (var p = new SKPaint { Shader = SKShader.CreateRadialGradient(new(S / 2f, S / 2f), S * 0.75f, new[] { C("#000000", 0), C("#000000", 0), C("#000000", 200) }, new[] { 0, 0.55f, 1f }, SKShaderTileMode.Clamp) }) c.DrawRect(all, p);
    Noise(c, all, 0.8f, 0.15f, 77);
    using (var rr = new SKPath()) { rr.AddRoundRect(new SKRect(14, 14, S - 14, S - 14), S * 0.16f, S * 0.16f);
      using var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 10, Shader = SKShader.CreateLinearGradient(new(0, 0), new(S, S), new[] { C("#ffd27a", 200), C("#8a1a10", 160), C("#ffb347", 180) }, null, SKShaderTileMode.Clamp) };
      c.DrawPath(rr, p); }
    return s.Snapshot();
  }

  static void Reticle(SKCanvas c, SKPoint p, SKPaint pt) {
    float R = 250;
    c.DrawCircle(p, R, pt);
    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
      c.DrawLine(p.X + dx * (R - 22), p.Y + dy * (R - 22), p.X + dx * (R + 36), p.Y + dy * (R + 36), pt);
  }
}
