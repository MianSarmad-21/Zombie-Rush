"""Draws Assets/UI/Art/scope_overlay.png: a rifle scope sight for the aim zoom, at 2048x2048.
Black outside a round lens with a soft dark rim, a thin dark ring, fine crosshair lines with thicker
outer posts, small range ticks and a red centre dot. The middle is transparent, so the zoomed world
shows through. Run: python Tools/UI/make_scope_overlay.py   (from the project folder)"""
import math, os, sys
from PIL import Image, ImageDraw, ImageFilter

S = 2048
OUT = sys.argv[1] if len(sys.argv) > 1 else "Assets/UI/Art/scope_overlay.png"
c = S / 2
R = S * 0.46                     # lens radius

# black everywhere, a clear lens in the middle with a soft dark rim (vignette)
alpha = Image.new("L", (S, S), 255)
lens = Image.new("L", (S, S), 0)
ImageDraw.Draw(lens).ellipse((c - R, c - R, c + R, c + R), fill=255)
vign = Image.new("L", (S, S), 0)
d = ImageDraw.Draw(vign)
for i in range(60):              # rim darkening: 0 in the middle, rising near the edge
    t = i / 59
    r = R * (0.80 + 0.20 * t)
    a = int(230 * t ** 2.2)
    d.ellipse((c - r, c - r, c + r, c + r), outline=a, width=int(R * 0.004) + 2)
vign = vign.filter(ImageFilter.GaussianBlur(S * 0.01))
inside = Image.eval(lens, lambda v: 255 - v)                     # 255 outside the lens
alpha = Image.composite(alpha, vign, inside)                      # outside: black, inside: vignette
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
img.putalpha(alpha)

g = ImageDraw.Draw(img)
dark = (8, 10, 12, 255)
# lens ring
g.ellipse((c - R, c - R, c + R, c + R), outline=dark, width=int(S * 0.006))
# thin crosshair lines with a gap around the centre
thin = max(2, int(S * 0.0016)); thick = int(S * 0.008); gap = S * 0.012
for sgn in (-1, 1):
    g.line((c + sgn * gap, c, c + sgn * R * 0.55, c), fill=dark, width=thin)
    g.line((c, c + sgn * gap, c, c + sgn * R * 0.55), fill=dark, width=thin)
    # thick posts from the ring inwards
    g.line((c + sgn * R * 0.55, c, c + sgn * R, c), fill=dark, width=thick)
    g.line((c, c + sgn * R * 0.55, c, c + sgn * R), fill=dark, width=thick)
    # range ticks on the lower and side lines
    for k in range(1, 5):
        o = R * 0.1 * k
        h = S * (0.006 if k % 2 else 0.011)
        g.line((c + sgn * o, c - h, c + sgn * o, c + h), fill=dark, width=thin)
        if sgn > 0:
            g.line((c - h, c + o, c + h, c + o), fill=dark, width=thin)
# red centre dot
rd = S * 0.0035
g.ellipse((c - rd, c - rd, c + rd, c + rd), fill=(225, 40, 32, 255))
os.makedirs(os.path.dirname(OUT), exist_ok=True)
img.save(OUT)
print("saved", OUT)
