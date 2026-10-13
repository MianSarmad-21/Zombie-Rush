using SkiaSharp;
using static P;

// Hand-built painted zombie head for the app icon (1024 space, face centred on x=512)
static class ZombieFace {
  static readonly SKColor SkinL = C("#c2c7ad"), Skin = C("#6c7760"), SkinD = C("#283024"), SkinDD = C("#101410");
  static readonly SKColor Bruise = C("#4b3346"), Blood = C("#5e0606");

  static float UpperLip(float x) => x <= 648 ? 690 + MathF.Pow((x - 512) / 140f, 2) * 52 : 742 - (x - 648) * 0.45f;
  static float LowerLip(float x) => x <= 640 ? 868 - MathF.Pow((x - 512) / 140f, 2) * 100 : 790 - (x - 640) * 0.55f;

  static SKPath HeadPath() {
    var p = new SKPath();
    p.MoveTo(512, 128);
    p.CubicTo(650, 128, 742, 215, 744, 335);   // cranium R
    p.CubicTo(746, 420, 728, 462, 738, 520);   // temple -> cheekbone R
    p.CubicTo(748, 585, 700, 622, 688, 668);   // cheek hollow R
    p.CubicTo(678, 728, 688, 780, 656, 822);   // jaw R
    p.CubicTo(616, 880, 580, 940, 512, 946);   // chin
    p.CubicTo(444, 940, 408, 880, 368, 822);
    p.CubicTo(336, 780, 346, 728, 336, 668);
    p.CubicTo(324, 622, 276, 585, 286, 520);
    p.CubicTo(296, 462, 278, 420, 280, 335);
    p.CubicTo(282, 215, 374, 128, 512, 128);
    p.Close(); return p;
  }
  static SKPath MouthPath() {
    var p = new SKPath();
    p.MoveTo(372, 760);
    p.CubicTo(410, 705, 462, 688, 512, 690);
    p.CubicTo(566, 688, 612, 700, 648, 744);
    p.LineTo(668, 724); p.LineTo(684, 732); p.LineTo(700, 706); p.LineTo(714, 718); p.LineTo(726, 700);
    p.LineTo(720, 742); p.LineTo(706, 760); p.LineTo(688, 770); p.LineTo(672, 790); p.LineTo(646, 792);
    p.CubicTo(608, 846, 560, 868, 512, 868);
    p.CubicTo(460, 868, 410, 840, 372, 760);
    p.Close(); return p;
  }
  // roughen an outline: resample and push points along the normal by smooth noise
  static SKPath Jitter(SKPath src, float amp, int seed) {
    var r = new Random(seed); using var pm = new SKPathMeasure(src, true); float len = pm.Length; var o = new SKPath();
    float ph1 = (float)r.NextDouble() * 6, ph2 = (float)r.NextDouble() * 6;
    for (float d = 0; d < len; d += 3) { pm.GetPositionAndTangent(d, out var pos, out var tan);
      float k = amp * (0.6f * MathF.Sin(d * 0.09f + ph1) + 0.4f * MathF.Sin(d * 0.23f + ph2)) + (float)(r.NextDouble() - 0.5) * amp * 0.5f;
      var q = new SKPoint(pos.X - tan.Y * k, pos.Y + tan.X * k); if (d == 0) o.MoveTo(q); else o.LineTo(q); }
    o.Close(); return o;
  }
  static void Shade(SKCanvas c, float x, float y, float rx, float ry, SKColor col, float blur, SKBlendMode m = SKBlendMode.SrcOver) {
    // soft ellipse as a radial gradient (blurred shapes get hard-clipped with non-SrcOver blends)
    float ex = rx + blur * 2, ey = ry + blur * 2; float k = Math.Min(rx / ex, ry / ey);
    c.Save(); c.Translate(x, y); c.Scale(ex, ey);
    using var p = new SKPaint { IsAntialias = true, BlendMode = m, Shader = SKShader.CreateRadialGradient(new SKPoint(0, 0), 1, new[] { col, col.WithAlpha((byte)(col.Alpha * 0.85f)), col.WithAlpha(0) }, new[] { 0, k * 0.7f, 1f }, SKShaderTileMode.Clamp) };
    c.DrawCircle(0, 0, 1, p); c.Restore(); }

  public static void Draw(SKCanvas c) {
    var rnd = new Random(1234);
    c.Save();
    c.RotateDegrees(-4, 512, 560);

    // neck + torn shirt
    using (var neck = new SKPath()) {
      neck.MoveTo(410, 830); neck.LineTo(614, 830); neck.LineTo(650, 1010); neck.LineTo(374, 1010); neck.Close();
      using var p = new SKPaint { IsAntialias = true, Shader = SKShader.CreateLinearGradient(new(380, 0), new(650, 0), new[] { C("#3a4434"), SkinDD }, null, SKShaderTileMode.Clamp) };
      c.DrawPath(neck, p);
      using var tp = new SKPaint { IsAntialias = true, Color = C("#56634a", 150), StrokeWidth = 16, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6) };
      c.DrawLine(452, 890, 424, 1010, tp);
      using var sp = new SKPaint { Color = C("#000000", 200), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 20) }; c.DrawRect(400, 830, 230, 50, sp);
    }
    using (var sh = new SKPath()) {
      sh.MoveTo(40, 1120); sh.CubicTo(110, 950, 250, 910, 384, 915); sh.LineTo(420, 972); sh.LineTo(446, 940); sh.LineTo(474, 1010);
      sh.LineTo(512, 1050); sh.LineTo(552, 1006); sh.LineTo(578, 944); sh.LineTo(604, 976); sh.LineTo(640, 915);
      sh.CubicTo(774, 910, 914, 950, 984, 1120); sh.Close();
      using var p = new SKPaint { IsAntialias = true, Shader = SKShader.CreateLinearGradient(new(0, 910), new(0, 1060), new[] { C("#34333a"), C("#09080b") }, null, SKShaderTileMode.Clamp) };
      c.DrawPath(sh, p);
      c.Save(); c.ClipPath(sh, antialias: true); Noise(c, new SKRect(0, 880, 1024, 1120), 0.05f, 0.5f, 31, SKBlendMode.Multiply);
      using (var bp = new SKPaint { Color = C("#4a0505", 200), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 12) }) { c.DrawOval(440, 1000, 70, 46, bp); c.DrawOval(650, 970, 44, 30, bp); }
      c.Restore();
    }
    // ears (mostly in shadow)
    foreach (var s in new[] { -1, 1 }) using (var e = new SKPath()) {
      float ex = 512 + s * 236;
      e.MoveTo(ex - s * 10, 455); e.CubicTo(ex + s * 40, 435, ex + s * 50, 525, ex + s * 18, 578); e.CubicTo(ex + s * 4, 596, ex - s * 14, 586, ex - s * 14, 562); e.Close();
      using var p = new SKPaint { IsAntialias = true, Shader = SKShader.CreateLinearGradient(new(ex, 440), new(ex + s * 50, 560), new[] { s < 0 ? C("#4d5744") : C("#1f261c"), SkinDD }, null, SKShaderTileMode.Clamp) };
      c.DrawPath(e, p);
    }

    var head = HeadPath();
    using (var p = new SKPaint { IsAntialias = true, Shader = SKShader.CreateRadialGradient(new(430, 360), 600, new[] { SkinL, Skin, SkinD, SkinDD }, new[] { 0f, 0.32f, 0.72f, 1f }, SKShaderTileMode.Clamp) })
      c.DrawPath(head, p);

    c.Save(); c.ClipPath(head, antialias: true);
    var hb = head.Bounds; hb.Inflate(20, 20);
    // skin texture
    Noise(c, hb, 0.010f, 0.6f, 7, SKBlendMode.Multiply);
    Noise(c, hb, 0.05f, 0.35f, 8, SKBlendMode.Overlay);
    Noise(c, hb, 0.22f, 0.25f, 9, SKBlendMode.Multiply);   // pores
    for (int i = 0; i < 18; i++) Shade(c, rnd.Next(300, 724), rnd.Next(170, 920), rnd.Next(20, 70), rnd.Next(15, 50), Bruise.WithAlpha((byte)rnd.Next(50, 110)), rnd.Next(14, 30), SKBlendMode.Multiply);
    for (int i = 0; i < 10; i++) Shade(c, rnd.Next(300, 724), rnd.Next(170, 920), rnd.Next(30, 80), rnd.Next(20, 60), C("#8f9a40", (byte)rnd.Next(20, 45)), 20, SKBlendMode.Overlay);
    Veins(c, rnd);

    // sculpting
    Shade(c, 330, 670, 70, 115, C("#0c100b", 170), 34); Shade(c, 696, 670, 70, 115, C("#0c100b", 200), 34);   // cheek hollows
    Shade(c, 286, 400, 46, 110, C("#0c100b", 150), 30); Shade(c, 740, 400, 46, 110, C("#0c100b", 180), 30);   // temples
    Shade(c, 350, 552, 70, 32, C("#e2e8c8", 90), 20, SKBlendMode.Screen); Shade(c, 680, 552, 56, 26, C("#e2e8c8", 40), 20, SKBlendMode.Screen);
    Shade(c, 440, 250, 120, 80, C("#e6edd0", 80), 40, SKBlendMode.Screen);
    Shade(c, 500, 900, 60, 30, C("#d0d8b4", 50), 16, SKBlendMode.Screen);
    Shade(c, 512, 410, 220, 36, C("#0c100b", 130), 24);                     // brow shadow band
    using (var bp = new SKPaint { IsAntialias = true, StrokeWidth = 34, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, Color = C("#d4dbb6", 100), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 12), BlendMode = SKBlendMode.Screen }) {
      using var l = new SKPath(); l.MoveTo(312, 392); l.QuadTo(400, 352, 490, 418); c.DrawPath(l, bp);
      using var r = new SKPath(); r.MoveTo(712, 392); r.QuadTo(624, 352, 534, 418); c.DrawPath(r, bp); }
    using (var fp = new SKPaint { IsAntialias = true, StrokeWidth = 6, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, Color = C("#151a12", 150), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3.5f) }) {
      c.DrawLine(498, 432, 486, 376, fp); c.DrawLine(526, 432, 538, 378, fp);
      using var nl = new SKPath(); nl.MoveTo(452, 650); nl.QuadTo(404, 700, 384, 752); c.DrawPath(nl, fp);
      using var nr = new SKPath(); nr.MoveTo(572, 650); nr.QuadTo(620, 700, 640, 740); c.DrawPath(nr, fp);
    }

    Eye(c, new SKPoint(410, 480), 1); Eye(c, new SKPoint(614, 480), -1);
    Nose(c);
    Wounds(c);
    Mouth(c, rnd);

    // key lighting pass: right half falls into deep shadow, top-left catches the moon
    using (var p = new SKPaint { Shader = SKShader.CreateLinearGradient(new(440, 0), new(760, 0), new[] { C("#000000", 0), C("#05070a", 115) }, null, SKShaderTileMode.Clamp) }) c.DrawRect(hb, p);
    using (var p = new SKPaint { Shader = SKShader.CreateLinearGradient(new(0, 640), new(0, 960), new[] { C("#000000", 0), C("#000000", 120) }, null, SKShaderTileMode.Clamp) }) c.DrawRect(hb, p);
    // wet specular glints
    foreach (var (x, y, rx, ry) in new[] { (430f, 236f, 30f, 9f), (500f, 548f, 6f, 22f), (352f, 548f, 16f, 5f), (470f, 900f, 12f, 4f) })
      Shade(c, x, y, rx, ry, C("#f2f6ff", 75), 4, SKBlendMode.Screen);
    // rim lights: fire on the right edge, moon on the upper-left edge
    // (gradient-faded, no hard clips)
    using (var rp = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 36, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 11), BlendMode = SKBlendMode.Plus,
      Shader = SKShader.CreateLinearGradient(new(540, 0), new(745, 0), new[] { C("#d0400e", 0), C("#e04a10", 255) }, null, SKShaderTileMode.Clamp) }) {
      c.Save(); c.Translate(12, 4); c.DrawPath(head, rp); c.Restore(); }
    using (var rp = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 20, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8), BlendMode = SKBlendMode.Plus,
      Shader = SKShader.CreateLinearGradient(new(300, 260), new(560, 560), new[] { C("#7f97c8", 150), C("#7f97c8", 0) }, null, SKShaderTileMode.Clamp) }) {
      c.Save(); c.Translate(-7, -6); c.DrawPath(head, rp); c.Restore(); }
    c.Restore();

    Hair(c, rnd);
    c.Restore();
  }

  static void Veins(SKCanvas c, Random r) {
    var starts = new[] { new SKPoint(310, 380), new SKPoint(716, 360), new SKPoint(340, 760), new SKPoint(690, 610), new SKPoint(470, 200) };
    foreach (var s in starts) for (int k = 0; k < 3; k++) {
      using var path = new SKPath(); var p = s; path.MoveTo(p); float ang = (float)(r.NextDouble() * Math.PI * 2);
      for (int i = 0; i < 12; i++) { ang += (float)(r.NextDouble() - 0.5) * 1.1f; p = new SKPoint(p.X + MathF.Cos(ang) * 8, p.Y + MathF.Sin(ang) * 8); path.LineTo(p); }
      using var vp = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.4f, Color = C("#35284a", 90), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1.6f), StrokeJoin = SKStrokeJoin.Round };
      c.DrawPath(path, vp);
    }
  }

  static void Eye(SKCanvas c, SKPoint e, int side) {
    // deep socket
    using (var p = new SKPaint { Shader = SKShader.CreateRadialGradient(new(e.X, e.Y + 2), 118, new[] { C("#050204", 250), C("#140b10", 200), C("#140b10", 0) }, new[] { 0, 0.55f, 1f }, SKShaderTileMode.Clamp) })
      c.DrawOval(e.X, e.Y + 4, 118, 82, p);
    using var lid = new SKPath();
    lid.MoveTo(e.X - 70, e.Y - 8 - side * 10); lid.LineTo(e.X + 70, e.Y - 8 + side * 10); lid.LineTo(e.X + 70, e.Y + 50); lid.LineTo(e.X - 70, e.Y + 50); lid.Close();
    using var ball = new SKPath(); ball.AddOval(new SKRect(e.X - 54, e.Y - 30, e.X + 54, e.Y + 32));
    using var vis = ball.Op(lid, SKPathOp.Intersect) ?? ball;
    using (var p = new SKPaint { IsAntialias = true, Shader = SKShader.CreateRadialGradient(new(e.X, e.Y + 4), 58, new[] { C("#d6c88e"), C("#9a8a56"), C("#3a2f1e") }, new[] { 0, 0.6f, 1f }, SKShaderTileMode.Clamp) })
      c.DrawPath(vis, p);
    c.Save(); c.ClipPath(vis, antialias: true);
    var vr = new Random((int)e.X);
    for (int i = 0; i < 12; i++) { using var vp = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f, Color = C("#a01a10", 180) };
      float a = (float)(vr.NextDouble() * Math.PI * 2); using var l = new SKPath(); l.MoveTo(e.X + MathF.Cos(a) * 54, e.Y + MathF.Sin(a) * 30); l.QuadTo(e.X + MathF.Cos(a + 0.3f) * 34, e.Y + MathF.Sin(a + 0.3f) * 18, e.X + MathF.Cos(a) * 22, e.Y + MathF.Sin(a) * 12); c.DrawPath(l, vp); }
    var iris = new SKPoint(e.X + side * 5, e.Y + 4);
    using (var p = new SKPaint { IsAntialias = true, Shader = SKShader.CreateRadialGradient(iris, 26, new[] { C("#fffbe0"), C("#ffb02a"), C("#e04010"), C("#7a0a04") }, new[] { 0, 0.3f, 0.7f, 1f }, SKShaderTileMode.Clamp) }) c.DrawCircle(iris, 25, p);
    using (var p = new SKPaint { IsAntialias = true, Color = C("#1a0200") }) c.DrawOval(iris.X, iris.Y, 4.5f, 12, p);
    using (var p = new SKPaint { Shader = SKShader.CreateLinearGradient(new(0, e.Y - 30), new(0, e.Y + 8), new[] { C("#000000", 210), C("#000000", 0) }, null, SKShaderTileMode.Clamp) }) c.DrawRect(e.X - 60, e.Y - 34, 120, 44, p);
    c.Restore();
    Glow(c, iris.X, iris.Y, 95, C("#ff4a12", 190)); Glow(c, iris.X, iris.Y, 34, C("#ffd27a", 220));
    using (var p = new SKPaint { IsAntialias = true, Color = C("#ffffff", 240) }) c.DrawCircle(iris.X - 8, iris.Y - 7, 4, p);
    using (var lp = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 8, StrokeCap = SKStrokeCap.Round, Color = C("#0e0909", 235) })
      { using var lidp = new SKPath(); lidp.MoveTo(e.X - 64, e.Y - 6 - side * 11); lidp.QuadTo(e.X, e.Y - 14, e.X + 64, e.Y - 6 + side * 11); lidp.QuadTo(e.X, e.Y - 2, e.X - 64, e.Y - 6 - side * 11); lidp.Close(); using var fp2 = new SKPaint { IsAntialias = true, Color = C("#0e0909", 235) }; c.DrawPath(lidp, fp2); }
    using (var bp = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 12, Color = C("#36263a", 170), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6) }) {
      using var l = new SKPath(); l.MoveTo(e.X - 58, e.Y + 30); l.QuadTo(e.X, e.Y + 60, e.X + 58, e.Y + 30); c.DrawPath(l, bp); }
  }

  static void Nose(SKCanvas c) {
    using (var p = new SKPaint { IsAntialias = true, StrokeWidth = 20, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, Color = C("#d5ddb8", 80), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 9), BlendMode = SKBlendMode.Screen })
      c.DrawLine(504, 470, 498, 590, p);
    Shade(c, 540, 565, 24, 62, C("#0b0f0a", 150), 14);
    using var cav = new SKPath();
    cav.MoveTo(512, 590); cav.CubicTo(486, 600, 458, 636, 472, 662); cav.CubicTo(482, 676, 500, 666, 512, 654);
    cav.CubicTo(524, 666, 542, 676, 552, 662); cav.CubicTo(566, 636, 538, 600, 512, 590); cav.Close();
    Shade(c, 512, 640, 60, 44, C("#1a0606", 150), 14);
    using (var p = new SKPaint { IsAntialias = true, Shader = SKShader.CreateRadialGradient(new(512, 645), 52, new[] { C("#030101"), C("#200a0a"), C("#3e2220") }, new[] { 0, 0.7f, 1f }, SKShaderTileMode.Clamp) }) c.DrawPath(cav, p);
    using (var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3.5f, Color = C("#bdb595", 150) }) c.DrawLine(512, 606, 513, 652, p);
  }

  static void Wounds(SKCanvas c) {
    // forehead gash with crude stitches
    using (var g = new SKPath()) {
      g.MoveTo(556, 196); g.LineTo(586, 228); g.LineTo(582, 242); g.LineTo(618, 276); g.LineTo(644, 322); g.LineTo(634, 326); g.LineTo(606, 288); g.LineTo(572, 254); g.LineTo(574, 240); g.Close();
      Shade(c, 600, 262, 60, 70, C("#2a0505", 150), 18);
      using (var p = new SKPaint { IsAntialias = true, Shader = SKShader.CreateLinearGradient(new(556, 196), new(644, 322), new[] { C("#2a0303"), C("#7a0c0a"), C("#200202") }, null, SKShaderTileMode.Clamp) }) c.DrawPath(g, p);
      using var sp = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4, StrokeCap = SKStrokeCap.Round, Color = C("#120c08", 230) };
      for (int i = 0; i < 5; i++) { float t = i / 4f; float x = 562 + t * 76, y = 208 + t * 104; c.DrawLine(x - 15, y + 9, x + 15, y - 9, sp); }
    }
    // torn rot patch on the left cheek: jagged hole, raw muscle, a glimpse of cheekbone
    { var wr = new Random(77); var cx = 368f; var cy = 628f;
      using var w = new SKPath(); int n = 44;
      for (int i = 0; i < n; i++) { float a = i * MathF.PI * 2 / n; float rad = 40 + 9 * MathF.Sin(a * 3 + 1) + wr.Next(-7, 8) - (i % 5 == 0 ? 10 : 0);
        float px = cx + MathF.Cos(a) * rad * 0.82f, py = cy + MathF.Sin(a) * rad * 1.15f; if (i == 0) w.MoveTo(px, py); else w.LineTo(px, py); }
      w.Close();
      Shade(c, cx, cy, 62, 76, C("#2a0b12", 170), 18);          // bruised rim of rotting skin
      Shade(c, cx + 4, cy + 4, 50, 64, C("#4a3a20", 120), 10);  // yellowed decay ring
      using (var p = new SKPaint { IsAntialias = true, Shader = SKShader.CreateRadialGradient(new(cx - 6, cy - 6), 62, new[] { C("#b4301e"), C("#6a0c0a"), C("#1a0202") }, new[] { 0, 0.55f, 1f }, SKShaderTileMode.Clamp) }) c.DrawPath(w, p);
      c.Save(); c.ClipPath(w, antialias: true);
      using (var mp = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f, Color = C("#f0a090", 35), StrokeCap = SKStrokeCap.Round })
        for (int i = 0; i < 9; i++) { using var f = new SKPath(); float fx = cx - 40 + i * 10; f.MoveTo(fx, cy - 60); f.QuadTo(fx + 10, cy, fx - 4, cy + 60); c.DrawPath(f, mp); }
      using (var bone = new SKPath()) { bone.MoveTo(cx - 34, cy - 30); bone.QuadTo(cx, cy - 52, cx + 30, cy - 26); bone.QuadTo(cx, cy - 36, cx - 34, cy - 30); bone.Close();
        using var bp = new SKPaint { IsAntialias = true, Shader = SKShader.CreateLinearGradient(new(0, cy - 50), new(0, cy - 26), new[] { C("#e4dcc0"), C("#8a7c5c") }, null, SKShaderTileMode.Clamp) }; c.DrawPath(bone, bp); }
      Shade(c, cx + 8, cy + 26, 24, 18, C("#0a0101", 200), 8);  // dark cavity
      using (var ip = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 10, Color = C("#000000", 150), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5) }) c.DrawPath(w, ip);  // inner shadow
      c.Restore();
      using (var ep = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4, Color = C("#3a2a22", 150), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2), StrokeJoin = SKStrokeJoin.Round }) c.DrawPath(w, ep);  // ragged decayed edge
    }
    // blood trickles
    foreach (var (x, y, len) in new[] { (636f, 326f, 140f), (378f, 686f, 80f) }) Drip(c, x, y, len, 5);
  }
  static void Drip(SKCanvas c, float x, float y, float len, float w) {
    using var d = new SKPath(); d.MoveTo(x - w, y); d.CubicTo(x - w - 1, y + len * 0.5f, x + 2, y + len * 0.8f, x, y + len); d.CubicTo(x + w + 4, y + len - 4, x + w + 2, y + len * 0.6f, x + w, y); d.Close();
    using (var p = new SKPaint { IsAntialias = true, Shader = SKShader.CreateLinearGradient(new(0, y), new(0, y + len), new[] { C("#3e0404"), C("#8a0e0a") }, null, SKShaderTileMode.Clamp) }) c.DrawPath(d, p);
    using (var p = new SKPaint { IsAntialias = true, Color = C("#ff9a80", 150) }) c.DrawCircle(x + 2, y + len - 6, 2.2f, p);
  }

  static void Mouth(SKCanvas c, Random r) {
    var m = Jitter(MouthPath(), 3.5f, 9);
    using (var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 40, Color = C("#140c10", 160), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 16) }) c.DrawPath(m, p);
    using (var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 24, Color = C("#22161b"), StrokeJoin = SKStrokeJoin.Round }) c.DrawPath(m, p);
    using (var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 7, Color = C("#4e262c"), StrokeJoin = SKStrokeJoin.Round }) c.DrawPath(m, p);
    using (var p = new SKPaint { IsAntialias = true, Shader = SKShader.CreateRadialGradient(new(520, 790), 150, new[] { C("#020000"), C("#180304"), C("#3a0909") }, new[] { 0, 0.6f, 1f }, SKShaderTileMode.Clamp) }) c.DrawPath(m, p);
    c.Save(); c.ClipPath(m, antialias: true);
    using (var gp = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4, Color = C("#2c0a0c") }) {
      for (float x = 372; x < 728; x += 2) { c.DrawLine(x, UpperLip(x) - 8, x, UpperLip(x) + 11, gp); c.DrawLine(x, LowerLip(x) + 8, x, LowerLip(x) - 11, gp); } }
    Teeth(c, r, true); Teeth(c, r, false);
    Shade(c, 506, 836, 80, 22, C("#4a1014", 210), 12);
    using (var sp = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f, Color = C("#d8cfc0", 130) }) {
      using var s1 = new SKPath(); s1.MoveTo(462, 728); s1.QuadTo(468, 780, 458, 830); c.DrawPath(s1, sp);
      using var s2 = new SKPath(); s2.MoveTo(584, 732); s2.QuadTo(576, 776, 588, 820); c.DrawPath(s2, sp); }
    // depth: dark falloff toward the throat
    Shade(c, 520, 784, 90, 40, C("#000000", 170), 22);
    c.Restore();
    using (var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3, StrokeCap = SKStrokeCap.Round, Color = C("#e8d8d0", 120) }) {
      using var h = new SKPath(); h.MoveTo(440, 700); h.QuadTo(512, 680, 580, 698); c.DrawPath(h, p);
      using var h2 = new SKPath(); h2.MoveTo(460, 878); h2.QuadTo(512, 888, 566, 876); c.DrawPath(h2, p); }
    foreach (var (x, y, len) in new[] { (412f, 840f, 130f), (566f, 870f, 70f), (712f, 750f, 110f) }) Drip(c, x, y, len, 7);
  }

  static void Teeth(SKCanvas c, Random r, bool upper) {
    float x = 384, xEnd = upper ? 722 : 700;
    while (x < xEnd) {
      float w = 13 + (float)r.NextDouble() * 9; if (Math.Abs(x - 512) < 50) w += 5;
      float rootY = upper ? UpperLip(x + w / 2) + 10 : LowerLip(x + w / 2) - 10;
      if (r.NextDouble() < 0.1) { x += w + 2; continue; }
      float len = (upper ? 30 : 24) + (float)r.NextDouble() * 16; if (r.NextDouble() < 0.18) len *= 0.55f;
      if (Math.Abs(x - 448) < 9 || Math.Abs(x - 570) < 9) len += 18;
      if (x > 640) len *= 0.8f;
      float dir = upper ? 1 : -1;
      using var tp = new SKPath();
      tp.MoveTo(x, rootY); tp.LineTo(x + w, rootY);
      tp.LineTo(x + w - 1.5f, rootY + dir * len * 0.75f); tp.QuadTo(x + w * 0.6f, rootY + dir * (len + 3), x + w * 0.35f, rootY + dir * len);
      tp.LineTo(x + 1, rootY + dir * len * 0.7f); tp.Close();
      using (var p = new SKPaint { IsAntialias = true, Shader = SKShader.CreateLinearGradient(new(0, rootY), new(0, rootY + dir * len), new[] { C("#5e4626"), C("#c4b27c"), C("#ece0b8") }, new[] { 0, 0.4f, 1f }, SKShaderTileMode.Clamp) }) c.DrawPath(tp, p);
      using (var p = new SKPaint { IsAntialias = true, Shader = SKShader.CreateLinearGradient(new(x, 0), new(x + w, 0), new[] { C("#000000", 130), C("#000000", 0), C("#000000", 150) }, new[] { 0, 0.4f, 1f }, SKShaderTileMode.Clamp) }) c.DrawPath(tp, p);
      using (var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, Color = C("#24160c", 210) }) c.DrawPath(tp, p);
      if (r.NextDouble() < 0.45) Shade(c, x + w / 2, rootY + dir * 7, w / 3, 5, C("#4a2e12", 140), 3);
      x += w + 1.2f;
    }
  }

  static void Strand(SKCanvas c, SKPoint s, SKPoint ctrl, SKPoint e, float w, SKColor col) {
    var d = new SKPoint(ctrl.X - s.X, ctrl.Y - s.Y); float l = Math.Max(0.01f, d.Length); var n = new SKPoint(-d.Y / l * w / 2, d.X / l * w / 2);
    using var p = new SKPath();
    p.MoveTo(s.X + n.X, s.Y + n.Y); p.QuadTo(ctrl.X + n.X * 0.6f, ctrl.Y + n.Y * 0.6f, e.X, e.Y);
    p.QuadTo(ctrl.X - n.X * 0.6f, ctrl.Y - n.Y * 0.6f, s.X - n.X, s.Y - n.Y); p.Close();
    using var pt = new SKPaint { IsAntialias = true, Color = col }; c.DrawPath(p, pt);
  }

  static void Hair(SKCanvas c, Random r) {
    // stubbled, receding scalp on the sides (bald, scabbed crown)
    using (var head = HeadPath()) {
      c.Save(); c.ClipPath(head, antialias: true);
      foreach (var s in new[] { -1, 1 }) {
        Shade(c, 512 + s * 200, 300, 70, 170, C("#14110d", 150), 26, SKBlendMode.Multiply);
        Shade(c, 512 + s * 150, 200, 80, 60, C("#14110d", 90), 24, SKBlendMode.Multiply);
      }
      Shade(c, 470, 170, 26, 16, C("#5a1a12", 150), 8);   // scabs on the crown
      Shade(c, 548, 160, 16, 10, C("#5a1a12", 130), 6);
      c.Restore();
    }
    // matted clumps hanging from the sides, a few strands across the forehead
    for (int i = 0; i < 70; i++) {
      float side = i % 2 == 0 ? -1 : 1; float a = (float)(0.6 + r.NextDouble() * 0.75);
      var start = new SKPoint(512 + side * MathF.Sin(a) * 222, 335 - MathF.Cos(a) * 192);
      float len = 70 + (float)r.NextDouble() * 210;
      var end = new SKPoint(start.X + side * (16 + (float)r.NextDouble() * 50) + (float)(r.NextDouble() - 0.5) * 24, start.Y + len);
      var ctrl = new SKPoint(start.X + side * (32 + (float)r.NextDouble() * 30), start.Y + len * 0.35f);
      float w = 3 + (float)r.NextDouble() * 7;
      Strand(c, start, ctrl, end, w, C("#0d0b09", (byte)r.Next(200, 250)));
      if (side < 0 && r.NextDouble() < 0.5)   // moonlit sheen on the left clumps
        Strand(c, new SKPoint(start.X + 1, start.Y + 4), new SKPoint(ctrl.X + 1, ctrl.Y), new SKPoint(end.X, end.Y - len * 0.3f), w * 0.3f, C("#7a7c80", 110));
    }
    for (int i = 0; i < 7; i++) {
      var s = new SKPoint(430 + r.Next(0, 150), 176 + r.Next(0, 26));
      var e = new SKPoint(s.X + r.Next(-40, 30), s.Y + 150 + r.Next(0, 70));
      Strand(c, s, new SKPoint(s.X - 24, s.Y + 70), e, 2.5f + (float)r.NextDouble() * 2, C("#0d0b09", 220));
    }
  }
}
