using SkiaSharp;
using static P;

static class Thumb {
  const int W = 1920, H = 1080; const float HZ = 640; // horizon
  static readonly SKPoint Moon = new(1127, 207);
  static readonly SKPoint VP = new(900, HZ);           // road vanishing point
  public static SKPoint Muzzle = new(1268, 742);

  public static void Draw(string outDir) {
    using var s = SKSurface.Create(new SKImageInfo(W, H)); var c = s.Canvas;
    var full = new SKRect(0, 0, W, H);
    var rnd = new Random(7); RiflePath();

    // ---- sky (game skybox crop) + grade
    using (var sky = Img(@"Assets\Environment\Sky\Sky_Night.png")) {
      var src = new SKRect(60, 330, 60 + 1990, 330 + 1120);
      using var p = new SKPaint { FilterQuality = SKFilterQuality.High };
      c.DrawBitmap(sky, src, new SKRect(0, 0, W, HZ + 60), p);
    }
    using (var p = new SKPaint { Shader = SKShader.CreateLinearGradient(new(0, 0), new(0, HZ), new[] { C("#02040a", 150), C("#0b1424", 0), C("#2a3a58", 120) }, new[] { 0, 0.55f, 1f }, SKShaderTileMode.Clamp) })
      c.DrawRect(0, 0, W, HZ + 60, p);
    // moon
    Glow(c, Moon.X, Moon.Y, 520, C("#8fa8d8", 70));
    Glow(c, Moon.X, Moon.Y, 210, C("#c9d8f5", 110));
    using (var p = new SKPaint { IsAntialias = true, Shader = SKShader.CreateRadialGradient(new(Moon.X - 18, Moon.Y - 20), 95, new[] { C("#fbfdff"), C("#dfe6f2"), C("#aab6cc") }, new[] { 0, 0.6f, 1f }, SKShaderTileMode.Clamp) })
      c.DrawCircle(Moon, 74, p);
    c.Save(); using (var clip = new SKPath()) { clip.AddCircle(Moon.X, Moon.Y, 74); c.ClipPath(clip, antialias: true); }
    Noise(c, new SKRect(Moon.X - 80, Moon.Y - 80, Moon.X + 80, Moon.Y + 80), 0.035f, 0.55f, 3, SKBlendMode.Multiply);
    foreach (var (dx, dy, rr) in new[] { (-22f, -10f, 16f), (18f, 22f, 11f), (8f, -32f, 8f), (-30f, 26f, 9f), (32f, -6f, 7f) })
      using (var p = new SKPaint { IsAntialias = true, Color = C("#7d8aa3", 70), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.5f) }) c.DrawCircle(Moon.X + dx, Moon.Y + dy, rr, p);
    c.Restore();
    // drifting cloud wisps in front of moon
    for (int i = 0; i < 7; i++) using (var p = new SKPaint { Color = C("#7f93ba", (byte)rnd.Next(18, 40)), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 18) })
      c.DrawOval(Moon.X + rnd.Next(-420, 380), Moon.Y + rnd.Next(-60, 140), rnd.Next(140, 320), rnd.Next(10, 22), p);
    // god rays
    c.Save(); c.Translate(Moon.X, Moon.Y);
    for (int i = 0; i < 9; i++) { float ang = 100 + i * 9 + rnd.Next(-3, 3); using var rp = new SKPath(); var a1 = ang * MathF.PI / 180; var a2 = (ang + 2.2f + rnd.Next(0, 3)) * MathF.PI / 180;
      rp.MoveTo(0, 0); rp.LineTo(MathF.Cos(a1) * 1500, MathF.Sin(a1) * 1500); rp.LineTo(MathF.Cos(a2) * 1500, MathF.Sin(a2) * 1500); rp.Close();
      using var p = new SKPaint { Shader = SKShader.CreateRadialGradient(new(0, 0), 900, new[] { C("#b8c8ea", 26), C("#b8c8ea", 0) }, null, SKShaderTileMode.Clamp), BlendMode = SKBlendMode.Plus, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 10) };
      c.DrawPath(rp, p); }
    c.Restore();

    // ---- city layers
    City(c, rnd, HZ + 6, 120, 330, C("#1b2740"), C("#2c3c5c"), 0.012f, false);
    Smoke(c, rnd);
    Fog(c, HZ - 120, HZ + 20, C("#3a4c70", 70));
    City(c, rnd, HZ + 14, 180, 470, C("#0d1424"), C("#1d2b47"), 0.02f, true);
    Fog(c, HZ - 50, HZ + 30, C("#43567c", 80));

    // ---- ground
    using (var p = new SKPaint { Shader = SKShader.CreateLinearGradient(new(0, HZ), new(0, H), new[] { C("#232f45"), C("#0c111b"), C("#05070b") }, new[] { 0, 0.35f, 1f }, SKShaderTileMode.Clamp) })
      c.DrawRect(0, HZ, W, H - HZ, p);
    Noise(c, new SKRect(0, HZ, W, H), 0.09f, 0.35f, 11);
    using (var road = new SKPath()) { road.MoveTo(VP.X - 40, HZ); road.LineTo(VP.X + 40, HZ); road.LineTo(W + 500, H); road.LineTo(-700, H); road.Close();
      using var p = new SKPaint { IsAntialias = true, Shader = SKShader.CreateLinearGradient(new(0, HZ), new(0, H), new[] { C("#2b3850", 140), C("#0a0e16", 200) }, null, SKShaderTileMode.Clamp) };
      c.DrawPath(road, p); }
    for (float t = 0.02f; t < 1f; t += 0.09f) {
      float t2 = t + 0.045f; float y1 = HZ + (H - HZ) * t * t, y2 = HZ + (H - HZ) * t2 * t2;
      float x1 = VP.X + (W * 0.5f - VP.X + 120) * t * t, x2 = VP.X + (W * 0.5f - VP.X + 120) * t2 * t2;
      float w1 = 1 + 14 * t * t, w2 = 1 + 14 * t2 * t2;
      using var lp = new SKPath(); lp.MoveTo(x1 - w1, y1); lp.LineTo(x1 + w1, y1); lp.LineTo(x2 + w2, y2); lp.LineTo(x2 - w2, y2); lp.Close();
      using var p = new SKPaint { IsAntialias = true, Color = C("#c9b77a", (byte)(40 + 70 * t)) }; c.DrawPath(lp, p);
    }
    using (var p = new SKPaint { Shader = SKShader.CreateLinearGradient(new(0, HZ), new(0, H), new[] { C("#9fb5e0", 60), C("#9fb5e0", 0) }, null, SKShaderTileMode.Clamp), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 30), BlendMode = SKBlendMode.Plus })
      c.DrawOval(Moon.X - 60, HZ + 160, 90, 170, p);
    for (int i = 0; i < 90; i++) { float t = (float)rnd.NextDouble(); float y = HZ + 8 + (H - HZ) * t * t; float sc = 1 + 30 * t * t;
      using var p = new SKPaint { IsAntialias = true, Color = C("#05070c", (byte)rnd.Next(120, 220)) }; c.DrawOval(rnd.Next(0, W), y, sc * rnd.Next(2, 6), sc * 0.6f, p); }
    Fire(c, rnd, 330, HZ + 18, 0.6f); Fire(c, rnd, 1650, HZ + 30, 0.8f); Fire(c, rnd, 1460, HZ + 10, 0.4f);
    Car(c, 1620, HZ + 92, 1.6f, C("#070a10")); Car(c, 175, HZ + 70, 1.2f, C("#0a0e16"));

    // ---- fight-zone wall (game feature): glowing amber arc behind the horde
    ZoneWall(c);

    // ---- horde
    var zs = new List<(float z, float x)>();
    for (int i = 0; i < 260; i++) { float z = MathF.Pow((float)rnd.NextDouble(), 2.3f); float x = rnd.Next(-60, W + 60);
      if (z > 0.5f && x > 860 && x < 1380) continue;    // keep the line of fire readable
      if (z > 0.75f && x > 560 && x < 1450) continue;
      zs.Add((z, x)); }
    zs.Add((0.6f, 760)); zs.Add((0.46f, 1000)); zs.Add((0.55f, 600)); zs.Add((0.42f, 1180)); zs.Add((0.68f, 1560));
    zs.Sort((p, q) => p.z.CompareTo(q.z));
    bool fogged = false;
    foreach (var (z, x) in zs) {
      if (!fogged && z > 0.35f) { Fog(c, HZ + 10, HZ + 120, C("#4a5f88", 55)); fogged = true; }
      float foot = HZ + 12 + z * z * 330 + z * 40; float h = 22 + MathF.Pow(z, 2.1f) * 380;
      int dir = x < Muzzle.X ? 1 : -1; if (rnd.NextDouble() < 0.2) dir = -dir;
      var fig = Zombie(x, foot, h, rnd, out var head, out var u, dir);
      var body = Lerp(C("#2b3a58"), C("#040509"), MathF.Pow(z, 0.6f));
      using (var p = new SKPaint { Color = C("#000000", (byte)(60 + 100 * z)), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2 + 6 * z) })
        c.DrawOval(x + 6 * u, foot, 22 * u, 3.5f * u, p);
      float rim = 0.7f + 0.35f * u;
      fig.Draw(c, Lerp(C("#3b4d70"), C("#8ea6d4"), z), 0.4f, rim * 0.8f, -rim);
      float dm = SKPoint.Distance(new(x, foot - h * 0.6f), Muzzle);
      if (z > 0.3f && dm < 750) fig.Draw(c, C("#ffb347", (byte)(230 * (1 - dm / 750))), 1 + z, Math.Sign(Muzzle.X - x) * rim, rim * 0.3f);
      fig.Draw(c, body);
      if (z > 0.3f) { float er = Math.Max(0.7f, 0.75f * u); float ex = head.X + dir * 3f * u;
        for (int e = 0; e < 2; e++) { float xx = ex + (e == 0 ? -1.2f : 1.2f) * u; float yy = head.Y - 1.2f * u;
          Glow(c, xx, yy, er * 4, C("#ff3b2e", (byte)(70 + 110 * z)));
          using var p = new SKPaint { IsAntialias = true, Color = C("#ffcdb0") }; c.DrawCircle(xx, yy, er * 0.5f, p); } }
    }
    Fog(c, HZ + 160, H, C("#23314f", 35));

    // ---- rifle + tracers + muzzle flash
    Glow(c, Muzzle.X, Muzzle.Y, 520, C("#ff8a2a", 40));
    Rifle(c, rnd);

    // ---- grade, vignette, grain
    using (var p = new SKPaint { Shader = SKShader.CreateRadialGradient(new(W * 0.55f, H * 0.5f), W * 0.72f, new[] { C("#000000", 0), C("#000000", 0), C("#000000", 215) }, new[] { 0, 0.5f, 1f }, SKShaderTileMode.Clamp) })
      c.DrawRect(full, p);
    using (var p = new SKPaint { Shader = SKShader.CreateRadialGradient(new(420, 220), 700, new[] { C("#000000", 170), C("#000000", 0) }, null, SKShaderTileMode.Clamp) })
      c.DrawRect(full, p);
    using (var b = Img(@"Assets\UI\Art\blood_splatter.png")) using (var p = new SKPaint { ColorFilter = SKColorFilter.CreateBlendMode(C("#5e0707", 200), SKBlendMode.Modulate), FilterQuality = SKFilterQuality.High })
      { c.Save(); c.Translate(W - 300, -90); c.RotateDegrees(25); c.DrawBitmap(b, new SKRect(0, 0, 420, 420 * b.Height / (float)b.Width), p); c.Restore(); }
    Noise(c, full, 0.9f, 0.18f, 99);

    // ---- logo + tagline (NO_LOGO=1 renders the bare scene, used for the GDD preface)
    bool noLogo = Environment.GetEnvironmentVariable("NO_LOGO") == "1";
    if (!noLogo)
    using (var logo = Img(@"Assets\UI\Art\logo_zombie_rush.png")) {
      float lw = 820, lh = lw * logo.Height / logo.Width; var dst = SKRect.Create(70, 40, lw, lh);
      using (var p = new SKPaint { ColorFilter = SKColorFilter.CreateBlendMode(C("#000000"), SKBlendMode.SrcIn), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 16) }) { c.Save(); c.Translate(0, 10); c.DrawBitmap(logo, dst, p); c.Restore(); }
      using (var p = new SKPaint { ColorFilter = SKColorFilter.CreateBlendMode(C("#ff5a1f", 110), SKBlendMode.SrcIn), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Outer, 22), BlendMode = SKBlendMode.Plus }) c.DrawBitmap(logo, dst, p);
      using (var p = new SKPaint { FilterQuality = SKFilterQuality.High }) c.DrawBitmap(logo, dst, p);
      float ty = dst.Bottom + 34;
      using var brush = Img(@"Assets\UI\Art\brush_stroke.png");
      using (var p = new SKPaint { ColorFilter = SKColorFilter.CreateBlendMode(C("#9e1010", 235), SKBlendMode.SrcIn), FilterQuality = SKFilterQuality.High })
        c.DrawBitmap(brush, SKRect.Create(150, ty - 50, 660, 78), p);
      using var f = new SKFont(Font("Oswald-Bold.ttf"), 40);
      Spaced(c, "SURVIVE THE ENDLESS NIGHT", 480, ty + 4, f, 6, C("#f3e7d0"));
    }

    if (noLogo) { Save(s, Path.Combine(outDir, "ZombieRush_Scene_1920x1080.png")); return; }
    Save(s, Path.Combine(outDir, "ZombieRush_Thumbnail_1920x1080.png"));
    using (var img = s.Snapshot()) {
      using var s2 = SKSurface.Create(new SKImageInfo(1280, 720)); using var p = new SKPaint { FilterQuality = SKFilterQuality.High };
      s2.Canvas.DrawImage(img, new SKRect(0, 0, 1280, 720), p); Save(s2, Path.Combine(outDir, "ZombieRush_Thumbnail_1280x720.png")); }
  }

  public static void Spaced(SKCanvas c, string t, float cx, float y, SKFont f, float sp, SKColor col) {
    float total = 0; foreach (var ch in t) total += f.MeasureText(ch.ToString()) + sp; total -= sp;
    float x = cx - total / 2;
    using var sh = new SKPaint { IsAntialias = true, Color = C("#000000", 180), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3) };
    using var p = new SKPaint { IsAntialias = true, Color = col };
    foreach (var ch in t) { c.DrawText(ch.ToString(), x + 2, y + 3, f, sh); c.DrawText(ch.ToString(), x, y, f, p); x += f.MeasureText(ch.ToString()) + sp; }
  }

  static void City(SKCanvas c, Random r, float baseY, float minH, float maxH, SKColor col, SKColor edge, float litChance, bool near) {
    float x = -40;
    while (x < W + 40) {
      float bw = r.Next(near ? 70 : 50, near ? 190 : 130), bh = r.Next((int)minH, (int)maxH);
      if (Math.Abs(x + bw / 2 - Moon.X) < 260) bh *= 0.55f;
      if (near && x > 380 && x < 1150) bh *= 0.7f;
      var path = new SKPath(); float top = baseY - bh;
      path.MoveTo(x, baseY); path.LineTo(x, top);
      int kind = r.Next(4);
      if (kind == 0) { int n = r.Next(4, 8); for (int i = 1; i <= n; i++) path.LineTo(x + bw * i / n, top + r.Next(0, (int)(bh * 0.25f))); }
      else if (kind == 1) { path.LineTo(x + bw * 0.2f, top); path.LineTo(x + bw * 0.2f, top - 25); path.LineTo(x + bw * 0.6f, top - 25); path.LineTo(x + bw * 0.6f, top); path.LineTo(x + bw, top); }
      else path.LineTo(x + bw, top);
      path.LineTo(x + bw, baseY); path.Close();
      using (var p = new SKPaint { IsAntialias = true, Shader = SKShader.CreateLinearGradient(new(x, top), new(x + bw, top), new[] { col, col, edge }, new[] { 0, 0.82f, 1f }, SKShaderTileMode.Clamp) }) c.DrawPath(path, p);
      if (kind == 3 || r.NextDouble() < 0.2) using (var p = new SKPaint { IsAntialias = true, Color = col, StrokeWidth = 3, Style = SKPaintStyle.Stroke }) { float ax = x + bw * (float)r.NextDouble(); c.DrawLine(ax, top, ax, top - r.Next(30, 90), p); }
      float ws = near ? 9 : 6, gap = near ? 15 : 10;
      c.Save(); c.ClipPath(path);
      for (float wy = top + 14; wy < baseY - 12; wy += gap) for (float wx = x + 8; wx < x + bw - 10; wx += gap) {
        double d = r.NextDouble();
        if (d < litChance) { var wc = r.NextDouble() < 0.5 ? C("#f2b45a", 220) : C("#ff7a2e", 200); using var p = new SKPaint { Color = wc }; c.DrawRect(wx, wy, ws, ws * 1.3f, p); Glow(c, wx + ws / 2, wy + ws / 2, ws * 3, wc.WithAlpha(50)); }
        else if (d < 0.45) using (var p = new SKPaint { Color = edge.WithAlpha(near ? (byte)60 : (byte)45) }) c.DrawRect(wx, wy, ws, ws * 1.3f, p);
      }
      c.Restore();
      x += bw + r.Next(-10, near ? 30 : 12);
    }
  }
  static void Fog(SKCanvas c, float y0, float y1, SKColor col) {
    using var p = new SKPaint { Shader = SKShader.CreateLinearGradient(new(0, y0), new(0, y1), new[] { col.WithAlpha(0), col, col.WithAlpha(0) }, null, SKShaderTileMode.Clamp) };
    c.DrawRect(0, y0, W, y1 - y0, p);
  }
  static void Smoke(SKCanvas c, Random r) {
    foreach (var sx in new[] { 330f, 1650f, 1460f, 760f }) {
      float y = HZ; float x = sx;
      for (int i = 0; i < 26; i++) { y -= r.Next(12, 24); x += r.Next(-6, 16); float rad = 20 + i * 5;
        using var p = new SKPaint { Color = C("#3d4a63", (byte)Math.Max(0, 50 - i * 1.6f)), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, rad * 0.6f) }; c.DrawCircle(x, y, rad, p); }
    }
  }
  static void Fire(SKCanvas c, Random r, float x, float y, float s) {
    Glow(c, x, y - 20 * s, 260 * s, C("#ff6a1a", 110)); Glow(c, x, y - 10 * s, 80 * s, C("#ffc35a", 200));
    for (int i = 0; i < 9; i++) { using var p = new SKPath(); float fx = x + r.Next(-30, 30) * s, fh = r.Next(30, 70) * s;
      p.MoveTo(fx - 10 * s, y); p.QuadTo(fx - 6 * s, y - fh * 0.6f, fx + r.Next(-6, 6) * s, y - fh); p.QuadTo(fx + 6 * s, y - fh * 0.5f, fx + 10 * s, y); p.Close();
      using var pt = new SKPaint { IsAntialias = true, Color = i % 2 == 0 ? C("#ff8a2a", 200) : C("#ffd27a", 220), BlendMode = SKBlendMode.Plus, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2) }; c.DrawPath(p, pt); }
  }
  static void Car(SKCanvas c, float x, float y, float s, SKColor col) {
    using var p = new SKPath(); p.MoveTo(x - 60 * s, y); p.LineTo(x - 62 * s, y - 16 * s); p.LineTo(x - 30 * s, y - 20 * s); p.LineTo(x - 16 * s, y - 34 * s); p.LineTo(x + 22 * s, y - 33 * s);
    p.LineTo(x + 38 * s, y - 19 * s); p.LineTo(x + 62 * s, y - 15 * s); p.LineTo(x + 60 * s, y); p.Close();
    using var pt = new SKPaint { IsAntialias = true, Color = col }; c.DrawPath(p, pt);
    using var rim = new SKPaint { IsAntialias = true, Color = C("#8aa0c8", 90), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
    using var top = new SKPath(); top.MoveTo(x - 30 * s, y - 20 * s); top.LineTo(x - 16 * s, y - 34 * s); top.LineTo(x + 22 * s, y - 33 * s); top.LineTo(x + 38 * s, y - 19 * s); c.DrawPath(top, rim);
  }
  static void ZoneWall(SKCanvas c) {
    float cx = 960, cy = HZ + 150, rx = 1250, ry = 128, wallH = 46;
    using var wall = new SKPath(); int n = 120;
    for (int i = 0; i <= n; i++) { float a = MathF.PI + MathF.PI * i / n; float px = cx + MathF.Cos(a) * rx, py = cy + MathF.Sin(a) * ry; if (i == 0) wall.MoveTo(px, py); else wall.LineTo(px, py); }
    for (int i = n; i >= 0; i--) { float a = MathF.PI + MathF.PI * i / n; float px = cx + MathF.Cos(a) * rx, py = cy + MathF.Sin(a) * ry - wallH * (0.6f + 0.4f * MathF.Sin(MathF.PI * i / n)); wall.LineTo(px, py); }
    wall.Close();
    using (var p = new SKPaint { Shader = SKShader.CreateLinearGradient(new(0, cy - ry - wallH), new(0, cy), new[] { C("#ffb02e", 0), C("#ffa22a", 120), C("#ff8a1e", 40) }, new[] { 0, 0.7f, 1f }, SKShaderTileMode.Clamp), BlendMode = SKBlendMode.Plus, IsAntialias = true }) c.DrawPath(wall, p);
    c.Save(); c.ClipPath(wall, antialias: true);
    using (var lp = new SKPaint { Color = C("#ffd27a", 50), StrokeWidth = 2, BlendMode = SKBlendMode.Plus })
      for (float xx = -400; xx < W + 400; xx += 22) c.DrawLine(xx, cy - 200, xx + 60, cy + 40, lp);
    c.Restore();
    using var arc = new SKPath(); arc.AddArc(new SKRect(cx - rx, cy - ry, cx + rx, cy + ry), 180, 180);
    using (var p = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 10, Color = C("#ff9a24", 140), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8), IsAntialias = true, BlendMode = SKBlendMode.Plus }) c.DrawPath(arc, p);
    using (var p = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f, Color = C("#ffe0a0", 230), IsAntialias = true }) c.DrawPath(arc, p);
  }
  static SKPath RiflePath() {
    var ak = Icon("skoll_ak47");
    // icon: barrel top-right, stock bottom-left -> mirror so the barrel aims into the scene (up-left)
    var m = SKMatrix.CreateScale(-1, 1, 256, 256);
    m = m.PostConcat(SKMatrix.CreateRotationDegrees(RifleRot, 256, 256));
    m = m.PostConcat(SKMatrix.CreateScale(RifleScale, RifleScale));
    ak.Transform(m);
    var b = ak.Bounds;
    ak.Transform(SKMatrix.CreateTranslation(W + RifleOff.X - b.Right, H + RifleOff.Y - b.Bottom));
    // barrel tip = left-most point
    var pts = ak.Points; var tip = pts[0]; foreach (var q in pts) if (q.X < tip.X) tip = q;
    Muzzle = new SKPoint(tip.X - 6, tip.Y + 3);
    return ak;
  }
  public static float RifleRot = -28, RifleScale = 1.45f; public static SKPoint RifleOff = new(150, 110);

  static void Tracers(SKCanvas c, Random r) {
    foreach (var (tx, ty) in new[] { (720f, 650f), (880f, 668f), (590f, 705f) }) {
      var dir = new SKPoint(tx - Muzzle.X, ty - Muzzle.Y); float len = dir.Length; dir = new SKPoint(dir.X / len, dir.Y / len);
      float s0 = 90 + r.Next(0, 120), s1 = s0 + 150 + r.Next(0, 90);
      var a = new SKPoint(Muzzle.X + dir.X * s0, Muzzle.Y + dir.Y * s0); var b = new SKPoint(Muzzle.X + dir.X * s1, Muzzle.Y + dir.Y * s1);
      using var g = new SKPaint { StrokeWidth = 7, StrokeCap = SKStrokeCap.Round, Color = C("#ff9a30", 120), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6), BlendMode = SKBlendMode.Plus };
      using var l = new SKPaint { StrokeWidth = 2.2f, StrokeCap = SKStrokeCap.Round, IsAntialias = true, Shader = SKShader.CreateLinearGradient(a, b, new[] { C("#fff2c0", 0), C("#fff2c0", 255) }, null, SKShaderTileMode.Clamp) };
      c.DrawLine(a, b, g); c.DrawLine(a, b, l);
    }
  }
  static void Rifle(SKCanvas c, Random rnd) {
    var ak = RiflePath(); var b = ak.Bounds;
    Glow(c, Muzzle.X, Muzzle.Y, 300, C("#ff7a1a", 170));
    Tracers(c, rnd);
    using (var p = new SKPaint { Color = C("#000000", 210), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 18), IsAntialias = true }) { c.Save(); c.Translate(14, 18); c.DrawPath(ak, p); c.Restore(); }
    using (var p = new SKPaint { Color = C("#ffa040", 220), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1.5f), IsAntialias = true }) { c.Save(); c.Translate(-2.5f, -1.5f); c.DrawPath(ak, p); c.Restore(); }
    using (var p = new SKPaint { Color = C("#a9bde3", 170), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1), IsAntialias = true }) { c.Save(); c.Translate(2, -2.5f); c.DrawPath(ak, p); c.Restore(); }
    using (var p = new SKPaint { IsAntialias = true, Shader = SKShader.CreateLinearGradient(new(b.Left, b.Top), new(b.Right, b.Bottom), new[] { C("#0d0f13"), C("#060709"), C("#020203") }, new[] { 0, 0.45f, 1f }, SKShaderTileMode.Clamp) })
      c.DrawPath(ak, p);
    c.Save(); c.ClipPath(ak, antialias: true);
    // warm light from the flash across the receiver
    using (var p = new SKPaint { Shader = SKShader.CreateRadialGradient(Muzzle, 420, new[] { C("#ff9a3a", 55), C("#ff9a3a", 0) }, null, SKShaderTileMode.Clamp), BlendMode = SKBlendMode.Plus }) c.DrawRect(b, p);
    c.Restore();
    Console.WriteLine($"rifle bounds {b} muzzle {Muzzle}");
    var mz = Muzzle;
    Glow(c, mz.X, mz.Y, 90, C("#ffd27a", 255));
    var rr = new Random(3);
    float axis = MathF.Atan2(670 - mz.Y, 760 - mz.X);
    using (var star = new SKPath()) { int n = 16; for (int i = 0; i < n * 2; i++) { float ang = axis + i * MathF.PI / n; float rad = i % 2 == 0 ? rr.Next(40, 95) : rr.Next(10, 18);
        if (i % 2 == 0 && MathF.Cos(ang - axis) > 0.92f) rad *= 2.1f;   // long forward tongue
        float px = mz.X + MathF.Cos(ang) * rad, py = mz.Y + MathF.Sin(ang) * rad; if (i == 0) star.MoveTo(px, py); else star.LineTo(px, py); } star.Close();
      using var p = new SKPaint { IsAntialias = true, Shader = SKShader.CreateRadialGradient(mz, 190, new[] { C("#ffffff"), C("#ffe08a"), C("#ff8a2a", 0) }, new[] { 0, 0.3f, 1f }, SKShaderTileMode.Clamp), BlendMode = SKBlendMode.Plus };
      c.DrawPath(star, p); }
    for (int i = 0; i < 26; i++) { float ang = axis + (float)(rr.NextDouble() - 0.5) * 2.2f; float d = rr.Next(40, 240);
      using var p = new SKPaint { Color = C("#ffcf70", (byte)rr.Next(120, 255)), StrokeWidth = 2, StrokeCap = SKStrokeCap.Round, IsAntialias = true, BlendMode = SKBlendMode.Plus };
      c.DrawLine(mz.X + MathF.Cos(ang) * d, mz.Y + MathF.Sin(ang) * d, mz.X + MathF.Cos(ang) * (d + 9), mz.Y + MathF.Sin(ang) * (d + 9), p); }
    // ejected brass casing
    using (var p = new SKPaint { IsAntialias = true, Color = C("#e0a84a") }) { c.Save(); c.Translate(b.MidX + 60, b.Top - 30); c.RotateDegrees(35); c.DrawRoundRect(-9, -3.5f, 18, 7, 2, 2, p); c.Restore(); }
  }
}
