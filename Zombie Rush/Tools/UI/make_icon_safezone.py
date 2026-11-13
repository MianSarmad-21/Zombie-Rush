"""Draws Assets/UI/IconsPro/iconx_safezone.png: a green shield with a white house, in the
IconsPro style (dark outline, gradient fill, soft drop shadow, light top rim). Used by the
floating SAFE ZONE label over the base.
Run: python Tools/UI/make_icon_safezone.py   (from the project folder)"""
import math

from PIL import Image, ImageChops, ImageDraw, ImageFilter

S = 1024            # drawn at 4x, saved at 256
OUT = "Assets/UI/IconsPro/iconx_safezone.png"
OUTLINE = (3, 12, 6)
TOP, BOTTOM = (150, 238, 150), (24, 128, 48)
HOUSE_TOP, HOUSE_BOTTOM = (250, 255, 248), (196, 228, 200)


def shield():
    """Slightly arched top, straight sides, rounded bottom."""
    m = Image.new("L", (S, S), 0)
    d = ImageDraw.Draw(m)
    pts = [(170, 200), (512, 165), (854, 200), (854, 560)]
    # rounded belly from each side down to the tip (a quarter ellipse per side)
    for i in range(1, 41):
        a = (i / 40) * math.pi / 2
        pts.append((512 + 342 * math.cos(a), 560 + 340 * math.sin(a)))
    for i in range(1, 41):
        a = math.pi / 2 + (i / 40) * math.pi / 2
        pts.append((512 + 342 * math.cos(a), 560 + 340 * math.sin(a)))
    d.polygon(pts, fill=255)
    return m.filter(ImageFilter.GaussianBlur(3)).point(lambda v: 255 if v > 128 else 0)


def house():
    """A simple house: roof triangle over a square body with a door cut out."""
    m = Image.new("L", (S, S), 0)
    d = ImageDraw.Draw(m)
    d.polygon([(512, 330), (690, 490), (334, 490)], fill=255)        # roof
    d.rectangle((380, 480, 644, 680), fill=255)                        # body
    d.rectangle((478, 575, 546, 680), fill=0)                          # door
    return m


def grow(mask, px):
    return mask.filter(ImageFilter.GaussianBlur(px * 0.6)).point(lambda v: 255 if v > 20 else 0)


def gradient(mask, top, bottom, y0, y1):
    g = Image.new("RGBA", (S, S))
    d = ImageDraw.Draw(g)
    for y in range(S):
        t = min(1, max(0, (y - y0) / max(1, y1 - y0)))
        d.line([(0, y), (S, y)], fill=tuple(int(a + (b - a) * t) for a, b in zip(top, bottom)) + (255,))
    g.putalpha(mask)
    return g


def solid(mask, rgb, alpha=255):
    layer = Image.new("RGBA", (S, S), rgb + (alpha,))
    layer.putalpha(mask.point(lambda v: v * alpha // 255))
    return layer


body = shield()
inner = ImageChops.subtract(body, grow(ImageChops.invert(body), 52))  # inset band for the bevel
home = house()
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

shadow = grow(body, 28).filter(ImageFilter.GaussianBlur(18))
img.alpha_composite(solid(ImageChops.offset(shadow, 14, 22), (0, 0, 0), 120))
img.alpha_composite(solid(grow(body, 26), OUTLINE))
img.alpha_composite(gradient(body, TOP, BOTTOM, 170, 900))
img.alpha_composite(gradient(inner, (110, 214, 116), (18, 104, 40), 220, 860))
img.alpha_composite(solid(grow(home, 18), OUTLINE))
img.alpha_composite(gradient(home, HOUSE_TOP, HOUSE_BOTTOM, 300, 700))
# bevel: a thin light rim along the top edge of the shield
rim = ImageChops.subtract(body, ImageChops.offset(body, 0, 18))
img.alpha_composite(solid(rim, (220, 255, 220), 150))

img.resize((256, 256), Image.LANCZOS).save(OUT)
print("saved", OUT)
