using System.Numerics;
using SkiaSharp;

class Mesh {
  public Vector3[] P; public Vector3[] N; public Vector2[] UV; public int[] Mat; // per triangle-corner arrays (3 per tri)
  public int Tris => P.Length / 3;
}

class Program {
  static void Main(string[] a) {
    if (a[0] == "dump") { var r = Fbx.Load(a[1]); foreach (var n in r.Kid("Objects").Kids) if (n.Name is "Geometry" or "Model") Fbx.Dump(n, 0, 3, Console.Out); return; }
    // render <fbx> <albedo> <normal> <out.png> <size> yaw pitch dist targetFrac fov
    var mesh = LoadMesh(a[1], "Yaku_zombie");
    var alb = SKBitmap.Decode(a[2]); var nrm = SKBitmap.Decode(a[3]);
    int size = int.Parse(a[5]);
    float yaw = float.Parse(a[6]), pitch = float.Parse(a[7]), dist = float.Parse(a[8]), tfrac = float.Parse(a[9]), fov = float.Parse(a[10]);
    float tx = a.Length > 11 ? float.Parse(a[11]) : 0;
    Render(mesh, alb, nrm, a[4], size, yaw, pitch, dist, tfrac, fov, tx);
  }

  static Mesh LoadMesh(string path, string geoName) {
    var root = Fbx.Load(path);
    var up = root.Kid("GlobalSettings").Kid("Properties70").Kids.FirstOrDefault(p => (string)p.Props[0] == "UpAxis");
    Console.WriteLine("UpAxis " + (up != null ? up.Props[4] : "?"));
    var g = root.Kid("Objects").All("Geometry").First(x => ((string)x.Props[1]).StartsWith(geoName));
    var v = (double[])g.Kid("Vertices").Props[0]; var pvi = (int[])g.Kid("PolygonVertexIndex").Props[0];
    var ln = g.Kid("LayerElementNormal"); var nd = (double[])ln.Kid("Normals").Props[0]; var ni = ln.Kid("NormalsIndex") != null ? (int[])ln.Kid("NormalsIndex").Props[0] : null;
    var lu = g.Kid("LayerElementUV"); var ud = (double[])lu.Kid("UV").Props[0]; var ui = (int[])lu.Kid("UVIndex").Props[0];
    var lm = g.Kid("LayerElementMaterial"); var md = lm != null ? (int[])lm.Kid("Materials").Props[0] : null;
    var P = new List<Vector3>(); var N = new List<Vector3>(); var UV = new List<Vector2>(); var M = new List<int>();
    var poly = new List<int>(); int poly_i = 0;
    Vector3 V(int i) => new((float)v[i * 3], (float)v[i * 3 + 1], (float)v[i * 3 + 2]);
    for (int k = 0; k < pvi.Length; k++) {
      poly.Add(k);
      if (pvi[k] < 0) {
        for (int t = 1; t + 1 < poly.Count; t++) foreach (var c in new[] { poly[0], poly[t], poly[t + 1] }) {
          int vi = pvi[c] < 0 ? ~pvi[c] : pvi[c]; P.Add(V(vi));
          int nIdx = ni != null ? ni[c] : c; N.Add(Vector3.Normalize(new((float)nd[nIdx * 3], (float)nd[nIdx * 3 + 1], (float)nd[nIdx * 3 + 2])));
          int uIdx = ui[c]; UV.Add(new((float)ud[uIdx * 2], (float)ud[uIdx * 2 + 1]));
          M.Add(md == null ? 0 : md.Length == 1 ? md[0] : md[poly_i]);
        }
        poly.Clear(); poly_i++;
      }
    }
    var m = new Mesh { P = P.ToArray(), N = N.ToArray(), UV = UV.ToArray(), Mat = M.ToArray() };
    var mn = new Vector3(float.MaxValue); var mx = new Vector3(float.MinValue); foreach (var p in m.P) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
    Console.WriteLine($"tris {m.Tris} bounds {mn} .. {mx}  mats {string.Join(",", m.Mat.Distinct())}");
    return m;
  }

  static Vector3 Sample(SKBitmap b, Vector2 uv) {
    float u = uv.X - MathF.Floor(uv.X), vv = 1 - (uv.Y - MathF.Floor(uv.Y));
    float x = u * b.Width - 0.5f, y = vv * b.Height - 0.5f; int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y); float fx = x - x0, fy = y - y0;
    Vector3 G(int xx, int yy) { xx = Math.Clamp(xx, 0, b.Width - 1); yy = Math.Clamp(yy, 0, b.Height - 1); var c = b.GetPixel(xx, yy); return new(c.Red / 255f, c.Green / 255f, c.Blue / 255f); }
    return Vector3.Lerp(Vector3.Lerp(G(x0, y0), G(x0 + 1, y0), fx), Vector3.Lerp(G(x0, y0 + 1), G(x0 + 1, y0 + 1), fx), fy);
  }
  static Vector3 SrgbToLin(Vector3 c) => new(MathF.Pow(c.X, 2.2f), MathF.Pow(c.Y, 2.2f), MathF.Pow(c.Z, 2.2f));

  static void Render(Mesh m, SKBitmap alb, SKBitmap nrm, string outPath, int size, float yaw, float pitch, float dist, float tfrac, float fov, float tx) {
    // Y-up assumed after LoadMesh check; model faces +Z (glTF convention) or -Y
    var mn = new Vector3(float.MaxValue); var mx = new Vector3(float.MinValue); foreach (var p in m.P) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
    float hgt = mx.Y - mn.Y;
    var target = new Vector3((mn.X + mx.X) / 2 + tx * hgt, mn.Y + hgt * tfrac, (mn.Z + mx.Z) / 2);
    float yr = yaw * MathF.PI / 180, pr = pitch * MathF.PI / 180;
    var eye = target + new Vector3(MathF.Sin(yr) * MathF.Cos(pr), MathF.Sin(pr), MathF.Cos(yr) * MathF.Cos(pr)) * dist * hgt;
    var view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY);
    var proj = Matrix4x4.CreatePerspectiveFieldOfView(fov * MathF.PI / 180, 1, 0.01f * hgt, 100 * hgt);
    var vp = view * proj;
    int W = size, H = size;
    var col = new Vector4[W * H]; var zb = new float[W * H]; Array.Fill(zb, float.MaxValue);
    // lights (world space)
    var key = Vector3.Normalize(new Vector3(-0.6f, 0.7f, 0.5f));      // cool moonlight, upper-left front
    var warm = Vector3.Normalize(new Vector3(0.9f, -0.15f, -0.35f));  // fire rim from right/behind/below
    var cool = Vector3.Normalize(new Vector3(-0.8f, 0.4f, -0.6f));    // blue back rim, left
    var keyC = new Vector3(0.78f, 0.85f, 1.0f) * 1.5f; var warmC = new Vector3(1.0f, 0.45f, 0.15f) * 1.6f; var coolC = new Vector3(0.45f, 0.6f, 1.0f) * 1.1f;
    var amb = new Vector3(0.05f, 0.055f, 0.08f);

    for (int t = 0; t < m.Tris; t++) {
      int i0 = t * 3;
      var s = new Vector4[3]; bool behind = false;
      for (int k = 0; k < 3; k++) { s[k] = Vector4.Transform(new Vector4(m.P[i0 + k], 1), vp); if (s[k].W <= 0.001f) behind = true; }
      if (behind) continue;
      var sx = new float[3]; var sy = new float[3]; var sz = new float[3]; var iw = new float[3];
      for (int k = 0; k < 3; k++) { iw[k] = 1 / s[k].W; sx[k] = (s[k].X * iw[k] * 0.5f + 0.5f) * W; sy[k] = (1 - (s[k].Y * iw[k] * 0.5f + 0.5f)) * H; sz[k] = s[k].Z * iw[k]; }
      float area = (sx[1] - sx[0]) * (sy[2] - sy[0]) - (sx[2] - sx[0]) * (sy[1] - sy[0]);
      if (MathF.Abs(area) < 1e-6f) continue;
      // per-triangle tangent frame for normal mapping
      var e1 = m.P[i0 + 1] - m.P[i0]; var e2 = m.P[i0 + 2] - m.P[i0];
      var d1 = m.UV[i0 + 1] - m.UV[i0]; var d2 = m.UV[i0 + 2] - m.UV[i0];
      float r = d1.X * d2.Y - d2.X * d1.Y; Vector3 T = Vector3.UnitX, B = Vector3.UnitY;
      if (MathF.Abs(r) > 1e-12f) { r = 1 / r; T = (e1 * d2.Y - e2 * d1.Y) * r; B = (e2 * d1.X - e1 * d2.X) * r; }
      int x0 = Math.Max(0, (int)MathF.Floor(sx.Min())), x1 = Math.Min(W - 1, (int)MathF.Ceiling(sx.Max()));
      int y0 = Math.Max(0, (int)MathF.Floor(sy.Min())), y1 = Math.Min(H - 1, (int)MathF.Ceiling(sy.Max()));
      for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) {
        float px = x + 0.5f, py = y + 0.5f;
        float w0 = ((sx[1] - px) * (sy[2] - py) - (sx[2] - px) * (sy[1] - py)) / area;
        float w1 = ((sx[2] - px) * (sy[0] - py) - (sx[0] - px) * (sy[2] - py)) / area;
        float w2 = 1 - w0 - w1;
        if (w0 < 0 || w1 < 0 || w2 < 0) continue;
        float z = w0 * sz[0] + w1 * sz[1] + w2 * sz[2]; int idx = y * W + x;
        if (z >= zb[idx]) continue; zb[idx] = z;
        float q0 = w0 * iw[0], q1 = w1 * iw[1], q2 = w2 * iw[2], qs = q0 + q1 + q2; q0 /= qs; q1 /= qs; q2 /= qs;
        var uv = m.UV[i0] * q0 + m.UV[i0 + 1] * q1 + m.UV[i0 + 2] * q2;
        var n = Vector3.Normalize(m.N[i0] * q0 + m.N[i0 + 1] * q1 + m.N[i0 + 2] * q2);
        var wp = m.P[i0] * q0 + m.P[i0 + 1] * q1 + m.P[i0 + 2] * q2;
        var vdir = Vector3.Normalize(eye - wp);
        if (Vector3.Dot(n, vdir) < 0) n = -n;   // double-sided
        // normal map
        var nm = Sample(nrm, uv) * 2 - Vector3.One;
        var Tn = Vector3.Normalize(T - n * Vector3.Dot(n, T)); var Bn = Vector3.Normalize(Vector3.Cross(n, Tn)) * MathF.Sign(Vector3.Dot(Vector3.Cross(n, Tn), B) + 1e-9f);
        var nn = Vector3.Normalize(Tn * nm.X + Bn * nm.Y + n * MathF.Max(0.2f, nm.Z));
        if (float.IsNaN(nn.X)) nn = n;
        var a = SrgbToLin(Sample(alb, uv));
        float fres = MathF.Pow(1 - MathF.Max(0, Vector3.Dot(nn, vdir)), 3);
        float dk = MathF.Max(0, Vector3.Dot(nn, key));
        float dw = MathF.Max(0, (Vector3.Dot(nn, warm) + 0.25f) / 1.25f);
        float dc = MathF.Max(0, Vector3.Dot(nn, cool));
        var h = Vector3.Normalize(key + vdir); float spec = MathF.Pow(MathF.Max(0, Vector3.Dot(nn, h)), 24) * 0.18f;
        var c = a * (amb + keyC * dk + warmC * dw * 0.3f) + warmC * dw * fres * 0.45f + coolC * dc * fres * 0.9f + keyC * spec;
        col[idx] = new Vector4(c, 1);
      }
    }
    // tonemap + write RGBA
    using var bmp = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Unpremul));
    for (int i = 0; i < W * H; i++) {
      var c = col[i]; if (c.W == 0) { bmp.SetPixel(i % W, i / W, SKColors.Transparent); continue; }
      var v = new Vector3(c.X, c.Y, c.Z); v = v / (Vector3.One + v) * 1.25f;  // reinhard
      v = new Vector3(MathF.Pow(Math.Clamp(v.X, 0, 1), 1 / 2.2f), MathF.Pow(Math.Clamp(v.Y, 0, 1), 1 / 2.2f), MathF.Pow(Math.Clamp(v.Z, 0, 1), 1 / 2.2f));
      bmp.SetPixel(i % W, i / W, new SKColor((byte)(v.X * 255), (byte)(v.Y * 255), (byte)(v.Z * 255), 255));
    }
    // 2x downsample for anti-aliasing
    using var img = SKImage.FromBitmap(bmp);
    using var surf = SKSurface.Create(new SKImageInfo(W / 2, H / 2, SKColorType.Rgba8888, SKAlphaType.Premul));
    using (var p = new SKPaint { FilterQuality = SKFilterQuality.High }) surf.Canvas.DrawImage(img, new SKRect(0, 0, W / 2, H / 2), p);
    using var o = surf.Snapshot(); File.WriteAllBytes(outPath, o.Encode(SKEncodedImageFormat.Png, 100).ToArray());
    Console.WriteLine("wrote " + outPath);
  }
}
