"""Draws Assets/UI/IconsPro/iconx_scope.png (a scope sight: ring, crosshair, centre dot) and
iconx_shoulder.png (a head and shoulders with a two-way arrow above), in the IconsPro style used by
the other touch buttons (dark outline, teal gradient fill, soft drop shadow, light top rim).
Run: python Tools/UI/make_icon_aim.py   (from the project folder)"""
from PIL import Image, ImageChops, ImageDraw, ImageFilter

S = 1024            # drawn at 4x, saved at 256
OUTLINE = (3, 10, 12)
TOP, BOTTOM = (140, 206, 212), (28, 120, 133)


def blank():
    return Image.new("L", (S, S), 0)


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


def finish(shape, path):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    shadow = grow(shape, 28).filter(ImageFilter.GaussianBlur(18))
    img.alpha_composite(solid(ImageChops.offset(shadow, 14, 22), (0, 0, 0), 120))
    img.alpha_composite(solid(grow(shape, 26), OUTLINE))
    img.alpha_composite(gradient(shape, TOP, BOTTOM, 150, 870))
    rim = ImageChops.subtract(shape, ImageChops.offset(shape, 0, 16))
    img.alpha_composite(solid(rim, (220, 250, 252), 150))
    img.resize((256, 256), Image.LANCZOS).save(path)
    print("saved", path)


# scope: a thick ring, four posts from the ring inwards, and a centre dot
m = blank(); d = ImageDraw.Draw(m)
c, R, w = S / 2, 330, 70
d.ellipse((c - R, c - R, c + R, c + R), fill=255)
d.ellipse((c - R + w, c - R + w, c + R - w, c + R - w), fill=0)
post = 46
for x0, y0, x1, y1 in ((c - post / 2, c - R + w - 10, c + post / 2, c - 95), (c - post / 2, c + 95, c + post / 2, c + R - w + 10),
                       (c - R + w - 10, c - post / 2, c - 95, c + post / 2), (c + 95, c - post / 2, c + R - w + 10, c + post / 2)):
    d.rectangle((x0, y0, x1, y1), fill=255)
d.ellipse((c - 40, c - 40, c + 40, c + 40), fill=255)
finish(m, "Assets/UI/IconsPro/iconx_scope.png")

# shoulder swap: head and shoulders, with a two-way arrow over the head
m = blank(); d = ImageDraw.Draw(m)
d.ellipse((c - 120, 400, c + 120, 640), fill=255)                         # head
d.pieslice((c - 300, 640, c + 300, 1180), 180, 360, fill=255)            # shoulders
d.rectangle((c - 230, 228, c + 230, 288), fill=255)                      # arrow shaft
for sx in (-1, 1):                                                        # arrow heads
    tip = c + sx * 330
    d.polygon([(tip, 258), (tip - sx * 130, 140), (tip - sx * 130, 376)], fill=255)
finish(m, "Assets/UI/IconsPro/iconx_shoulder.png")
