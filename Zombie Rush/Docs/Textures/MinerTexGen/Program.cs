// Zombie texture enhancer.
// Usage: TexGen <miner|yaku> <inDir> <outDir> [seed]
// miner: reads the Meshy set (texture_0, _normal, _metallic, _roughness) and writes
//   *_texture_0_Enhanced.png            albedo: lifted, faded plaid, coal grime, worn edges, rotting skin, blood
//   *_texture_0_MetallicSmoothness_Enhanced.png   R metal, A smoothness (wet blood / clammy skin shine, dusty grime matte)
//   *_texture_0_Emission.png            the headlamp lens glowing
// yaku: reads texture_1 + texture_1_normal (light grey background) and writes
//   texture_1_Enhanced.png, texture_1_MetallicSmoothness.png (there was none) - grime, rot under the
//   tattoos, fresh wet blood; no headlamp.
using SkiaSharp;

string mode = args[0];
bool yaku = mode == "yaku";
string inDir = args[1], outDir = args[2];
int seed = args.Length > 3 ? int.Parse(args[3]) : 1337;
Directory.CreateDirectory(outDir);
string Prefix = yaku ? "texture_1" : "Meshy_AI_Undead_Miner_Juggerna_biped_texture_0";
string AlbedoIn = Prefix + ".png", NormalIn = Prefix + "_normal.png";
string MetalIn = yaku ? null : Prefix + "_metallic.png", RoughIn = yaku ? null : Prefix + "_roughness.png";
string AlbedoOut = Prefix + "_Enhanced.png";
string MsOut = yaku ? Prefix + "_MetallicSmoothness.png" : Prefix + "_MetallicSmoothness_Enhanced.png";
float lift = yaku ? 0.9f : 0.78f;      // gamma on the albedo: the Yaku needs only a touch
int splatCount = yaku ? 70 : 120;       // the Yaku is already painted with some blood
float baseSmooth = 0.25f;               // used where there is no roughness map

float[] Load(string file, out int w, out int h)
{
    if (file == null) { w = h = 0; return null; }
    using var bmp = SKBitmap.Decode(Path.Combine(inDir, file));
    w = bmp.Width; h = bmp.Height;
    var px = bmp.Pixels;
    var f = new float[w * h * 3];
    for (int i = 0; i < px.Length; i++) { f[i * 3] = px[i].Red / 255f; f[i * 3 + 1] = px[i].Green / 255f; f[i * 3 + 2] = px[i].Blue / 255f; }
    return f;
}

var alb = Load(AlbedoIn, out int W, out int H);
var nrm = Load(NormalIn, out _, out _);
var met = Load(MetalIn, out _, out _);
var rgh = Load(RoughIn, out _, out _);
// The Yaku's UV background is flat light grey (171,172,154), the Miner's black.
float bgR = alb[0], bgG = alb[1], bgB = alb[2];
int N = W * H;
Console.WriteLine($"{W}x{H}");

// ---------------------------------------------------------------- helpers
static float Clamp01(float x) => x < 0 ? 0 : x > 1 ? 1 : x;
static float Smooth(float a, float b, float x) { float t = Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }
static float Lerp(float a, float b, float t) => a + (b - a) * t;

uint Hash(int x, int y, int s)
{
    unchecked
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + s * 2147483647);
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }
}
float Rnd(int x, int y, int s) => (Hash(x, y, s) & 0xFFFFFF) / 16777215f;
float ValueNoise(float x, float y, int s)
{
    int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
    float xf = x - xi, yf = y - yi;
    float u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
    return Lerp(Lerp(Rnd(xi, yi, s), Rnd(xi + 1, yi, s), u), Lerp(Rnd(xi, yi + 1, s), Rnd(xi + 1, yi + 1, s), u), v);
}
float Fbm(float x, float y, int s, int oct)
{
    float sum = 0, amp = 0.5f, f = 1, norm = 0;
    for (int o = 0; o < oct; o++) { sum += amp * ValueNoise(x * f, y * f, s + o * 101); norm += amp; amp *= 0.5f; f *= 2.03f; }
    return sum / norm;
}

// Luminance + its local average (box blur via integral image) -> crevices are darker than around them.
var lum = new float[N];
var valid = new bool[N];
for (int i = 0; i < N; i++)
{
    float r = alb[i * 3], g = alb[i * 3 + 1], b = alb[i * 3 + 2];
    lum[i] = 0.299f * r + 0.587f * g + 0.114f * b;
    // The background between UV islands stays untouched.
    valid[i] = yaku ? MathF.Abs(r - bgR) + MathF.Abs(g - bgG) + MathF.Abs(b - bgB) > 0.03f : r + g + b > 0.025f;
}
float[] BoxBlur(float[] src, int rad)
{
    var integ = new double[(W + 1) * (H + 1)];
    for (int y = 0; y < H; y++)
    {
        double row = 0;
        for (int x = 0; x < W; x++) { row += src[y * W + x]; integ[(y + 1) * (W + 1) + x + 1] = integ[y * (W + 1) + x + 1] + row; }
    }
    var dst = new float[N];
    for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            int x0 = Math.Max(0, x - rad), x1 = Math.Min(W, x + rad + 1), y0 = Math.Max(0, y - rad), y1 = Math.Min(H, y + rad + 1);
            double s = integ[y1 * (W + 1) + x1] - integ[y0 * (W + 1) + x1] - integ[y1 * (W + 1) + x0] + integ[y0 * (W + 1) + x0];
            dst[y * W + x] = (float)(s / ((x1 - x0) * (y1 - y0)));
        }
    return dst;
}
// Only average valid pixels so island borders don't pick up the black background.
var lumMasked = new float[N]; var cnt = new float[N];
for (int i = 0; i < N; i++) { lumMasked[i] = valid[i] ? lum[i] : 0; cnt[i] = valid[i] ? 1 : 0; }
var blurL = BoxBlur(lumMasked, 14); var blurC = BoxBlur(cnt, 14);
var cavity = new float[N]; var edge = new float[N];
for (int i = 0; i < N; i++)
{
    if (!valid[i]) continue;
    float local = blurL[i] / Math.Max(1e-4f, blurC[i]);
    float d = lum[i] - local;
    cavity[i] = Smooth(0.0f, 0.12f, -d);  // darker than surroundings: folds, seams, creases
    edge[i] = Smooth(0.03f, 0.14f, d);     // lighter: raised seams and edges
    // The normal map adds detail the albedo doesn't show.
    float nz = nrm[i * 3 + 2] * 2 - 1;
    cavity[i] = Clamp01(cavity[i] + Smooth(0.85f, 0.55f, nz) * 0.35f);
}

// ---------------------------------------------------------------- masks
var plaid = new float[N]; var skin = new float[N]; var leather = new float[N];
for (int i = 0; i < N; i++)
{
    if (!valid[i]) continue;
    float r = alb[i * 3], g = alb[i * 3 + 1], b = alb[i * 3 + 2];
    float mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
    float sat = mx > 0 ? (mx - mn) / mx : 0;
    plaid[i] = yaku ? 0f : Smooth(1.4f, 2.0f, r / Math.Max(0.02f, g)) * Smooth(0.12f, 0.25f, r);
    // Skin: pale, low-saturation warm grey.
    skin[i] = Smooth(0.30f, 0.42f, lum[i]) * Smooth(0.28f, 0.14f, sat) * (1 - plaid[i]);
    // Leather / brass / tan parts: warm and saturated but not red.
    leather[i] = Smooth(0.25f, 0.45f, sat) * Smooth(1.6f, 1.2f, r / Math.Max(0.02f, g)) * Smooth(0.15f, 0.3f, lum[i]);
}
// Soften masks a little so effects don't stop at hard pixel borders.
skin = BoxBlur(skin, 3); plaid = BoxBlur(plaid, 2);

// ---------------------------------------------------------------- blood splatter layer
var blood = new float[N];   // 0..1 coverage
var wet = new float[N];     // 0..1 fresh/wet amount
var rnd = new Random(seed);
int lampX = 314, lampY = 116, lampR = 60;
var rim = new float[N];     // dried darker edge of a stain
// One stain: stretched along `dirAng` by `stretch`, its outline warped by noise (no circles),
// patchy inside, with a darker dried rim.
void Splat(float cx, float cy, float rad, float strength, float wetness, float dirAng, float stretch)
{
    int r0 = (int)(rad * stretch * 1.7f) + 2;
    int s = rnd.Next(100000);
    float ca = MathF.Cos(dirAng), sa = MathF.Sin(dirAng);
    for (int y = (int)cy - r0; y <= cy + r0; y++)
        for (int x = (int)cx - r0; x <= cx + r0; x++)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) continue;
            int i = y * W + x;
            if (!valid[i]) continue;
            float dx = x - cx, dy = y - cy;
            float along = (dx * ca + dy * sa) / stretch, across = -dx * sa + dy * ca;
            // Domain warp: the outline wobbles and throws out lobes.
            float wx = (Fbm(x / rad * 1.3f, y / rad * 1.3f, s, 3) - 0.5f) * rad * 0.9f;
            float wy = (Fbm(x / rad * 1.3f + 7.3f, y / rad * 1.3f, s, 3) - 0.5f) * rad * 0.9f;
            float d = MathF.Sqrt((along + wx) * (along + wx) + (across + wy) * (across + wy)) / rad;
            float a = Smooth(1.0f, 0.86f, d);
            if (a <= 0) continue;
            float inner = 0.55f + 0.45f * Fbm(x * 0.06f, y * 0.06f, s + 5, 3);        // uneven soak
            a *= strength * inner;
            float edgeBand = Smooth(0.65f, 0.95f, d) * Smooth(1.0f, 0.9f, d);
            blood[i] = Math.Max(blood[i], a);
            rim[i] = Math.Max(rim[i], edgeBand * strength);
            wet[i] = Math.Max(wet[i], a * wetness);
        }
}
int splats = 0;
while (splats < splatCount)
{
    int x = rnd.Next(W), y = rnd.Next(H);
    int i = y * W + x;
    if (!valid[i]) continue;
    if (MathF.Abs(x - lampX) < lampR * 2 && MathF.Abs(y - lampY) < lampR * 2) continue; // keep the lamp clean
    float dir = (float)(rnd.NextDouble() * Math.PI * 2);
    float wetness = (float)rnd.NextDouble() < 0.3f ? 0.9f : 0.1f;          // some fresh, most dried
    double kind = rnd.NextDouble();
    if (kind < 0.25)
    {
        // Smear: a long, faint wipe.
        Splat(x, y, 14 + (float)rnd.NextDouble() * 22, 0.3f + 0.2f * (float)rnd.NextDouble(), wetness * 0.5f, dir, 3.5f + 2f * (float)rnd.NextDouble());
    }
    else
    {
        // Splatter: an irregular hit, then droplets thrown out in one direction, smaller further out.
        float size = 6 + MathF.Pow((float)rnd.NextDouble(), 2.4f) * 38;
        float strength = 0.6f + 0.35f * (float)rnd.NextDouble();
        Splat(x, y, size, strength, wetness, dir, 1.2f + 0.8f * (float)rnd.NextDouble());
        int drops = 4 + rnd.Next(14);
        for (int k = 0; k < drops; k++)
        {
            float ang = dir + (float)(rnd.NextDouble() - 0.5) * 1.2f, dist = size * (1.2f + 3.5f * (float)rnd.NextDouble());
            float dsz = size * (0.06f + 0.16f * (float)rnd.NextDouble()) * (1.4f - dist / (size * 4.7f));
            Splat(x + MathF.Cos(ang) * dist, y + MathF.Sin(ang) * dist, Math.Max(1.5f, dsz), strength, wetness, ang, 1.5f + 1.5f * (float)rnd.NextDouble());
        }
    }
    splats++;
}

// ---------------------------------------------------------------- compose albedo
var outA = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Opaque);
var outMS = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Unpremul);
var pxA = new SKColor[N]; var pxMS = new SKColor[N];
float sumBefore = 0, sumAfter = 0; int cntValid = 0;
for (int y = 0; y < H; y++)
    for (int x = 0; x < W; x++)
    {
        int i = y * W + x;
        float r = alb[i * 3], g = alb[i * 3 + 1], b = alb[i * 3 + 2];
        float metal = met != null ? met[i * 3] : 0f, smooth = rgh != null ? 1 - rgh[i * 3] : baseSmooth;
        if (!valid[i]) { pxA[i] = new SKColor((byte)(r * 255), (byte)(g * 255), (byte)(b * 255)); pxMS[i] = new SKColor((byte)(metal * 255), 0, 0, (byte)(smooth * 255)); continue; }
        sumBefore += lum[i];
        float u = x / (float)W * 24, v = y / (float)H * 24;

        // 1. Lift the mid-tones: the Meshy texture is about half as bright as the other zombies.
        r = MathF.Pow(r, lift); g = MathF.Pow(g, lift); b = MathF.Pow(b, lift);
        if (yaku)
        {
            // Painted colours (tattoo ink, denim) a little faded, as on a corpse.
            float l0 = 0.299f * r + 0.587f * g + 0.114f * b;
            r = Lerp(l0, r, 0.86f); g = Lerp(l0, g, 0.86f); b = Lerp(l0, b, 0.86f);
        }

        // 2. Faded, filthy plaid: dark muddy maroon rather than bright new red.
        float pl = plaid[i];
        float l = 0.299f * r + 0.587f * g + 0.114f * b;
        float fade = 0.75f + 0.25f * Fbm(u * 1.5f, v * 1.5f, seed + 3, 4);   // uneven fading
        r = Lerp(r, (0.66f * r + 0.34f * l) * 0.78f * fade, pl);
        g = Lerp(g, (0.66f * g + 0.34f * l) * 0.80f * fade + 0.012f, pl);
        b = Lerp(b, (0.66f * b + 0.34f * l) * 0.72f * fade, pl);

        // 3. Rotting skin: grey-yellow-green mottling, purple bruises, dark veins.
        float sk = skin[i];
        if (sk > 0.01f)
        {
            float mottle = Fbm(u * 3, v * 3, seed + 11, 5);
            float bruise = Smooth(0.58f, 0.78f, Fbm(u * 1.6f, v * 1.6f, seed + 23, 4));
            float vein = 1 - MathF.Abs(Fbm(u * 5, v * 5, seed + 31, 4) * 2 - 1);  // ridged noise -> thin lines
            vein = Smooth(0.86f, 0.97f, vein);
            float tr = Lerp(0.97f, 0.91f, mottle), tg = Lerp(0.97f, 0.93f, mottle), tb = Lerp(0.9f, 0.76f, mottle); // jaundiced grey, not green
            r *= Lerp(1, tr, sk); g *= Lerp(1, tg, sk); b *= Lerp(1, tb, sk);
            r = Lerp(r, r * 0.62f, bruise * sk * 0.8f); g = Lerp(g, g * 0.5f, bruise * sk * 0.8f); b = Lerp(b, b * 0.62f, bruise * sk * 0.8f);
            r = Lerp(r, r * 0.55f, vein * sk * 0.55f); g = Lerp(g, g * 0.45f, vein * sk * 0.55f); b = Lerp(b, b * 0.6f, vein * sk * 0.55f);
        }

        // 4. Coal dust and grime: blotchy, collecting in folds and seams.
        float dust = Smooth(0.45f, 0.72f, Fbm(u * 2.2f, v * 2.2f, seed + 47, 5));
        float speck = Smooth(0.62f, 0.7f, ValueNoise(x * 0.35f, y * 0.35f, seed + 53)) * 0.5f;  // fine coal grit
        float grime = Clamp01(dust * 0.7f + cavity[i] * 0.75f + speck * dust) * (1 - sk * 0.4f);
        r = Lerp(r, r * 0.38f + 0.015f, grime * 0.8f); g = Lerp(g, g * 0.38f + 0.014f, grime * 0.8f); b = Lerp(b, b * 0.37f + 0.013f, grime * 0.8f);

        // 5. Worn edges: raised seams and edges rubbed lighter (cloth and leather).
        float wear = edge[i] * (1 - sk) * 0.35f;
        r = Lerp(r, r * 1.25f + 0.04f, wear); g = Lerp(g, g * 1.25f + 0.04f, wear); b = Lerp(b, b * 1.2f + 0.035f, wear);

        // 6. Blood: dried is near-black red, fresh is darker crimson; soaks a little into the cloth.
        float bl = blood[i];
        if (bl > 0)
        {
            // Stained into the material (its folds still show), dried brown-black, fresh dark crimson.
            float w = wet[i] / Math.Max(1e-3f, bl);
            float shade = 0.55f + 0.9f * lum[i];
            float br = Lerp(0.15f, 0.30f, w) * shade, bg = Lerp(0.035f, 0.015f, w) * shade, bb = Lerp(0.025f, 0.018f, w) * shade;
            float k = bl * 0.9f;
            r = Lerp(r, br, k); g = Lerp(g, bg, k); b = Lerp(b, bb, k);
            float rm = rim[i] * 0.5f;                      // darker dried edge
            r *= 1 - rm; g *= 1 - rm; b *= 1 - rm;
        }

        r = Clamp01(r); g = Clamp01(g); b = Clamp01(b);
        sumAfter += 0.299f * r + 0.587f * g + 0.114f * b; cntValid++;
        pxA[i] = new SKColor((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));

        // Smoothness: wet blood shines, clammy skin a bit, dust is matte.
        float sm = smooth;
        sm = Lerp(sm, 0.82f, wet[i]);
        sm = Lerp(sm, Math.Max(sm, 0.38f), sk * 0.6f);
        sm *= 1 - grime * 0.45f;
        pxMS[i] = new SKColor((byte)(Clamp01(metal) * 255), 0, 0, (byte)(Clamp01(sm) * 255));
    }
outA.Pixels = pxA; outMS.Pixels = pxMS;
Console.WriteLine($"mean luminance before {sumBefore / cntValid:F3} after {sumAfter / cntValid:F3}");

void Save(SKBitmap bmp, string name)
{
    using var img = SKImage.FromBitmap(bmp);
    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
    using var fs = File.Create(Path.Combine(outDir, name));
    data.SaveTo(fs);
    Console.WriteLine("wrote " + name);
}
Save(outA, AlbedoOut);
Save(outMS, MsOut);

// ---------------------------------------------------------------- emission: the headlamp (Miner only)
if (yaku) return;
const int E = 1024; // UVs are resolution independent; the glow doesn't need 4K
var em = new SKBitmap(E, E, SKColorType.Rgba8888, SKAlphaType.Opaque);
var pxE = new SKColor[E * E];
float scale = W / (float)E;
for (int y = 0; y < E; y++)
    for (int x = 0; x < E; x++)
    {
        int sx = (int)(x * scale), sy = (int)(y * scale);
        float d = MathF.Sqrt((sx - lampX) * (sx - lampX) + (sy - lampY) * (sy - lampY));
        float a = 0;
        if (d < lampR * 1.4f)
        {
            int i = sy * W + sx;
            float lens = Smooth(0.55f, 0.85f, lum[i]);          // the bright lens itself
            float core = Smooth(lampR * 0.9f, lampR * 0.2f, d);
            a = Math.Max(lens * Smooth(lampR * 1.3f, lampR * 0.6f, d), core * 0.6f);
        }
        pxE[y * E + x] = new SKColor((byte)(255 * a), (byte)(220 * a), (byte)(150 * a));
    }
em.Pixels = pxE;
Save(em, Prefix + "_Emission.png");
