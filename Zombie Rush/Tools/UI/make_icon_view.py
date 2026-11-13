"""Draws Assets/UI/IconsPro/iconx_view.png: an eye in the IconsPro style
(dark outline, teal gradient fill, soft drop shadow). Used by the first/third-person button.
Run: python Tools/UI/make_icon_view.py   (from the project folder)"""
from PIL import Image, ImageChops, ImageDraw, ImageFilter

S = 1024            # drawn at 4x, saved at 256
OUT = "Assets/UI/IconsPro/iconx_view.png"
OUTLINE = (3, 10, 12)
TOP, BOTTOM = (140, 206, 212), (28, 120, 133)


def circle(r, cx=S / 2, cy=S / 2):
    m = Image.new("L", (S, S), 0)
    ImageDraw.Draw(m).ellipse((cx - r, cy - r, cx + r, cy + r), fill=255)
    return m


def lens(half_w, half_h, cy=S / 2):
    """Almond: the overlap of two big circles."""
    rb = (half_w ** 2 + half_h ** 2) / (2 * half_h)
    d = rb - half_h
    return ImageChops.multiply(circle(rb, cy=cy + d), circle(rb, cy=cy - d))


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


eye = lens(430, 250)
iris_r, ring_r, pupil_r = 170, 205, 78
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

shadow = grow(eye, 28).filter(ImageFilter.GaussianBlur(18))
img.alpha_composite(solid(ImageChops.offset(shadow, 14, 22), (0, 0, 0), 120))
img.alpha_composite(solid(grow(eye, 26), OUTLINE))
img.alpha_composite(gradient(eye, TOP, BOTTOM, 262, 762))
img.alpha_composite(solid(ImageChops.multiply(circle(ring_r), eye), OUTLINE))
img.alpha_composite(gradient(circle(iris_r), (170, 228, 232), (24, 104, 116), 342, 682))
img.alpha_composite(solid(circle(pupil_r), OUTLINE))
img.alpha_composite(solid(circle(30, S / 2 - 52, S / 2 - 58), (240, 255, 255), 235))
# bevel: a thin light rim along the top edge of the almond
rim = ImageChops.subtract(eye, ImageChops.offset(eye, 0, 18))
img.alpha_composite(solid(rim, (220, 250, 252), 150))

img.resize((256, 256), Image.LANCZOS).save(OUT)
print("wrote", OUT)
