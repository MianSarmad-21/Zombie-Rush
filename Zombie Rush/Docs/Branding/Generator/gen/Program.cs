using SkiaSharp;

static class P {
  public static string Proj = @"D:\Unity\Games\Zombie-Rush\Zombie Rush\";
  public static string Svg;
  public static SKColor C(string h, byte a = 255) => SKColor.Parse(h).WithAlpha(a);
  public static SKColor Lerp(SKColor a, SKColor b, float t) { t = Math.Clamp(t, 0, 1);
    return new SKColor((byte)(a.Red + (b.Red - a.Red) * t), (byte)(a.Green + (b.Green - a.Green) * t), (byte)(a.Blue + (b.Blue - a.Blue) * t), (byte)(a.Alpha + (b.Alpha - a.Alpha) * t)); }
  public static SKBitmap Img(string rel) => SKBitmap.Decode(Proj + rel);
  public static SKTypeface Font(string f) => SKTypeface.FromFile(Proj + @"Assets\UI\Fonts\" + f);

  // game-icons.net svg -> SKPath (512 box)
  public static SKPath Icon(string name) {
    var txt = File.ReadAllText(Path.Combine(Svg, name + ".svg"));
    var res = new SKPath();
    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(txt, "<path[^>]*?d=\"([^\"]+)\"")) {
      if (m.Groups[1].Value == "M0 0h512v512H0z") continue;
      var p = SKPath.ParseSvgPathData(m.Groups[1].Value); res.AddPath(p);
    }
    return res;
  }

  static SKPath StrokeFill(SKPath p, float w) {
    using var pt = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = w, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    var o = new SKPath(); pt.GetFillPath(p, o); return o;
  }
  static SKPoint Pol(SKPoint o, float deg, float len) { float r = deg * MathF.PI / 180; return new SKPoint(o.X + MathF.Sin(r) * len, o.Y + MathF.Cos(r) * len); }


  // tapered limb segment with round joints
  static SKPath Seg(SKPoint a, SKPoint b, float wa, float wb) {
    var d = new SKPoint(b.X - a.X, b.Y - a.Y); float l = Math.Max(0.001f, d.Length); var n = new SKPoint(-d.Y / l, d.X / l);
    var p = new SKPath();
    p.MoveTo(a.X + n.X * wa / 2, a.Y + n.Y * wa / 2); p.LineTo(b.X + n.X * wb / 2, b.Y + n.Y * wb / 2);
    p.LineTo(b.X - n.X * wb / 2, b.Y - n.Y * wb / 2); p.LineTo(a.X - n.X * wa / 2, a.Y - n.Y * wa / 2); p.Close();
    using var ca = new SKPath(); ca.AddCircle(a.X, a.Y, wa / 2);
    using var cb = new SKPath(); cb.AddCircle(b.X, b.Y, wb / 2);
    var r = p.Op(ca, SKPathOp.Union) ?? p; return r.Op(cb, SKPathOp.Union) ?? r;
  }

  // procedural shambling zombie silhouette. deg 0 = straight down, 90 = right, 180 = up; dir +1 faces right
  public static Fig Zombie(float x, float foot, float h, Random r, out SKPoint head, out float u, int dir = 0) {
    u = h / 100f; if (dir == 0) dir = r.Next(2) == 0 ? 1 : -1;
    float U = u;
    float R(float a, float b) => a + (float)r.NextDouble() * (b - a);
    var parts = new List<SKPath>();
    float lean = R(10, 32);
    var hip = new SKPoint(x, foot - R(46, 50) * u);
    // legs: front leg forward, back leg trailing on its toes
    float step = R(10, 30);
    { var knee = Pol(hip, step * dir, 24 * u); var ankle = Pol(knee, (step - R(5, 25)) * dir, 23 * u);
      ankle.Y = Math.Min(ankle.Y, foot); parts.Add(Seg(hip, knee, 9 * u, 7 * u)); parts.Add(Seg(knee, ankle, 7 * u, 4.5f * u));
      parts.Add(Seg(ankle, new SKPoint(ankle.X + 6 * u * dir, foot - 0.5f * u), 4.5f * u, 3 * u)); }
    { var knee = Pol(hip, -R(5, 25) * dir, 24 * u); var ankle = Pol(knee, -R(25, 45) * dir, 22 * u);
      parts.Add(Seg(hip, knee, 9 * u, 6.5f * u)); parts.Add(Seg(knee, ankle, 6.5f * u, 4 * u));
      parts.Add(Seg(ankle, Pol(ankle, 30 * dir, 6 * u), 4 * u, 2.5f * u)); }
    // torso: hips -> chest -> shoulders, hunched
    var chest = Pol(hip, 180 - lean * dir, 18 * u);
    var neck = Pol(chest, 180 - (lean + R(8, 20)) * dir, 15 * u);
    parts.Add(Seg(hip, chest, 14 * u, 17 * u)); parts.Add(Seg(chest, neck, 17 * u, 15 * u));
    // torn shirt hem flaps
    for (int i = 0; i < 4; i++) { var b = new SKPoint(hip.X + R(-7, 7) * u, hip.Y + R(-1, 1) * u);
      var tp = new SKPath(); tp.MoveTo(b.X - 2.5f * u, b.Y); tp.LineTo(b.X + 2.5f * u, b.Y); tp.LineTo(b.X + R(-1.5f, 1.5f) * u, b.Y + R(4, 9) * u); tp.Close(); parts.Add(tp); }
    // head: thrust forward and drooping
    var hpos = Pol(neck, 180 - (lean + R(20, 55)) * dir, 8 * u);
    head = hpos;
    { var hd = new SKPath(); float tilt = R(-25, 25);
      hd.AddOval(new SKRect(hpos.X - 5.8f * u, hpos.Y - 6.8f * u, hpos.X + 5.8f * u, hpos.Y + 6.8f * u));
      hd.Transform(SKMatrix.CreateRotationDegrees(tilt, hpos.X, hpos.Y)); parts.Add(hd);
      // jaw hanging
      parts.Add(Seg(hpos, new SKPoint(hpos.X + 4.5f * u * dir, hpos.Y + 5.5f * u), 6 * u, 3.5f * u)); }
    parts.Add(Seg(neck, hpos, 7 * u, 6 * u));
    // arms: lead arm reaches toward the target, the other reaches lower or hangs
    var shF = Pol(neck, 90 * dir, 2 * u); var shB = Pol(neck, -90 * dir, 4 * u);
    float aF = (R(70, 112)) * dir;
    Arm(parts, shF, aF, aF + R(-20, 12) * dir, R(15, 17) * u, R(14, 17) * u, u, dir, R);
    if (r.NextDouble() < 0.6) { float aB = aF + R(-28, 10) * dir; Arm(parts, shB, aB, aB + R(-20, 20) * dir, R(13, 16) * u, R(12, 15) * u, u, dir, R); }
    else { float aB = R(-5, 25) * dir; Arm(parts, shB, aB, aB + R(25, 70) * dir, 16 * u, 14 * u, u, dir, R); }
    var res = new Fig(parts);
    return res;
  }
  static void Arm(List<SKPath> parts, SKPoint sh, float a1, float a2, float l1, float l2, float u, int dir, Func<float, float, float> R) {
    var el = Pol(sh, a1, l1); var wr = Pol(el, a2, l2);
    parts.Add(Seg(sh, el, 6 * u, 4.6f * u)); parts.Add(Seg(el, wr, 4.6f * u, 3.4f * u));
    // ragged sleeve
    var sp = new SKPath(); var mid = Pol(sh, a1, l1 * 0.7f); sp.MoveTo(Pol(mid, a1 + 90, 3.6f * u)); sp.LineTo(Pol(mid, a1 - 90, 3.6f * u)); sp.LineTo(Pol(mid, a1 + R(-40, 40), 7 * u)); sp.Close(); parts.Add(sp);
    // clawed hand
    var palm = Pol(wr, a2, 2.5f * u); parts.Add(Seg(wr, palm, 3.6f * u, 4.2f * u));
    for (int f = 0; f < 4; f++) { float fa = a2 + (f - 1.5f) * 14 * dir + R(-6, 6); var k = Pol(palm, fa, 3.2f * u); var tip = Pol(k, fa + 25 * dir, 2.4f * u);
      parts.Add(Seg(palm, k, 1.4f * u, 1.1f * u)); parts.Add(Seg(k, tip, 1.1f * u, 0.5f * u)); }
  }
  static SKPath LineP(SKPoint a, SKPoint b) { var p = new SKPath(); p.MoveTo(a); p.LineTo(b); return p; }

  public static void Noise(SKCanvas c, SKRect r, float freq, float alpha, int seed, SKBlendMode mode = SKBlendMode.Overlay) {
    using var sh = SKShader.CreatePerlinNoiseFractalNoise(freq, freq, 4, seed);
    using var cf = SKColorFilter.CreateColorMatrix(new float[] { 0.33f,0.33f,0.33f,0,0, 0.33f,0.33f,0.33f,0,0, 0.33f,0.33f,0.33f,0,0, 0,0,0,0,alpha });
    using var p = new SKPaint { Shader = sh, ColorFilter = cf, BlendMode = mode }; c.DrawRect(r, p);
  }
  public static void Glow(SKCanvas c, float x, float y, float rad, SKColor col) {
    using var p = new SKPaint { Shader = SKShader.CreateRadialGradient(new SKPoint(x, y), rad, new[] { col, col.WithAlpha((byte)(col.Alpha * 0.35f)), col.WithAlpha(0) }, new[] { 0f, 0.35f, 1f }, SKShaderTileMode.Clamp), BlendMode = SKBlendMode.Plus };
    c.DrawCircle(x, y, rad, p);
  }
  public static void Save(SKSurface s, string path) { using var i = s.Snapshot(); using var d = i.Encode(SKEncodedImageFormat.Png, 100); File.WriteAllBytes(path, d.ToArray()); Console.WriteLine("wrote " + path); }
}

class Program {
  static void Main(string[] a) {
    P.Svg = a[0]; string outDir = a[1]; IconArt.ZombiePng = a.Length > 2 ? a[2] : null; Directory.CreateDirectory(outDir);
    if (Environment.GetEnvironmentVariable("FACE_TEST") == "1") { using var fs = SKSurface.Create(new SKImageInfo(1024, 1024)); fs.Canvas.Clear(SKColors.Magenta); ZombieFace.Draw(fs.Canvas); P.Save(fs, Path.Combine(outDir, "face_test.png")); return; }
    Thumb.Draw(outDir); IconArt.Draw(outDir);
  }
}

// silhouette made of overlapping opaque parts; drawn through a layer so blur/alpha apply to the union
class Fig {
  public List<SKPath> Parts; public SKRect Bounds;
  public Fig(List<SKPath> p) { Parts = p; Bounds = p[0].Bounds; foreach (var q in p) Bounds.Union(q.Bounds); }
  public void Draw(SKCanvas c, SKColor col, float blur = 0, float dx = 0, float dy = 0) {
    var b = Bounds; b.Inflate(blur * 3 + 4, blur * 3 + 4); b.Offset(dx, dy);
    using var lp = new SKPaint { Color = SKColors.White.WithAlpha(col.Alpha), ImageFilter = blur > 0 ? SKImageFilter.CreateBlur(blur, blur) : null };
    c.SaveLayer(b, lp); c.Translate(dx, dy);
    using var p = new SKPaint { IsAntialias = true, Color = col.WithAlpha(255) };
    foreach (var q in Parts) c.DrawPath(q, p);
    c.Restore();
  }
}
