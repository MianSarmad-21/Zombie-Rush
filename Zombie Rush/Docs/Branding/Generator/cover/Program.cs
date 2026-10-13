using SkiaSharp;
// args: oldCover thumb out
var old = SKBitmap.Decode(args[0]);
var thumb = SKBitmap.Decode(args[1]);
int W = old.Width, H = old.Height; // 1240x1754
using var surf = SKSurface.Create(new SKImageInfo(W, H));
var c = surf.Canvas;
var bg = old.GetPixel(20, 20); Console.WriteLine(bg); c.Clear(bg);
var hi = new SKSamplingOptions(SKCubicResampler.Mitchell);

// Hero: thumbnail full width, faded into black top/bottom
int th = (int)Math.Round(W * thumb.Height / (double)thumb.Width);
int ty = 330;
c.DrawImage(SKImage.FromBitmap(thumb), new SKRect(0, ty, W, ty + th), hi);
float f = 70;
using (var p = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, ty), new SKPoint(0, ty + f), new[] { bg, bg.WithAlpha(0) }, SKShaderTileMode.Clamp) })
    c.DrawRect(0, ty, W, f, p);
using (var p = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, ty + th - f), new SKPoint(0, ty + th), new[] { bg.WithAlpha(0), bg }, SKShaderTileMode.Clamp) })
    c.DrawRect(0, ty + th - f, W, f, p);

// Red accent rules
using (var p = new SKPaint { Color = new SKColor(0xB0, 0x14, 0x14), IsAntialias = true })
{
    c.DrawRect(W / 2f - 260, ty - 40, 520, 4, p);
    c.DrawRect(W / 2f - 260, ty + th + 36, 520, 4, p);
}

// Reuse the original "Design Document" lettering and the studio footer
var ddSrc = new SKRectI(320, 800, 920, 910);
int ddY = ty + th + 90;
c.DrawBitmap(old, ddSrc, new SKRect(ddSrc.Left, ddY, ddSrc.Right, ddY + ddSrc.Height));
var ftSrc = new SKRectI(0, 1530, W, 1690);
c.DrawBitmap(old, ftSrc, new SKRect(0, ftSrc.Top, W, ftSrc.Bottom));

// Team members, between the title and the studio footer
var oswald = SKTypeface.FromFile(@"D:UnityGamesZombie-RushZombie RushAssetsUIFontsOswald-Medium.ttf");
using (var font = new SKFont(oswald, 30))
using (var paint = new SKPaint { Color = new SKColor(0xC9, 0xC0, 0xB0), IsAntialias = true })
{
    string[] team = { "MIAN SARMAD UR REHMAN", "SYED ZAIN UL ABIDEEN", "MUHAMMAD SHAYAN" };
    float spacing = 5, y = ddY + ddSrc.Height + 150;
    foreach (var name in team)
    {
        float w = 0; foreach (var ch in name) w += font.MeasureText(ch.ToString()) + spacing; w -= spacing;
        float x = (W - w) / 2;
        foreach (var ch in name) { c.DrawText(ch.ToString(), x, y, font, paint); x += font.MeasureText(ch.ToString()) + spacing; }
        y += 52;
    }
}

using var img = surf.Snapshot();
using var data = img.Encode(SKEncodedImageFormat.Png, 100);
File.WriteAllBytes(args[2], data.ToArray());
Console.WriteLine($"thumb h={th}, dd at {ddY}");
