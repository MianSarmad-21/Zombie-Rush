"""Tileable textures for the first-person gloves and jacket sleeves (Assets/Models/2026-11-10_FPArms/Textures).
Run: python Tools/Fx/make_glove_textures.py

  Glove_Albedo / Glove_Normal : dark synthetic-leather tactical glove, fine pebbled grain, worn lighter spots
  Cloth_Albedo / Cloth_Normal : olive canvas weave for the jacket sleeve, faded and a little dirty
Normals are OpenGL style (green up), as Unity expects. Every map tiles. Needs only Pillow.
"""
import os, random
from PIL import Image, ImageFilter, ImageChops

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Models", "2026-11-10_FPArms", "Textures")
os.makedirs(OUT, exist_ok=True)
N = 1024
random.seed(4)


def tile_noise(cells, sigma=80, size=N):
    """Smooth noise that tiles: noise on a small grid, repeated 3x3, scaled up, the middle cut out."""
    small = Image.effect_noise((cells, cells), sigma)
    big = Image.new("L", (cells * 3, cells * 3))
    for i in range(3):
        for j in range(3):
            big.paste(small, (i * cells, j * cells))
    up = big.resize((size * 3, size * 3), Image.BICUBIC)
    return up.crop((size, size, size * 2, size * 2))


def normal_from(height, strength=1.0):
    k = 1.0 / max(0.05, strength)
    dx = height.filter(ImageFilter.Kernel((3, 3), [1, 0, -1, 2, 0, -2, 1, 0, -1], scale=k, offset=128))
    dy = height.filter(ImageFilter.Kernel((3, 3), [-1, -2, -1, 0, 0, 0, 1, 2, 1], scale=k, offset=128))
    return Image.merge("RGB", (dx, dy, Image.new("L", height.size, 255)))


def blend(a, b, mask):
    return Image.composite(b, a, mask)


# ------------------------------------------------------------------ glove
pebble = tile_noise(128, 70)                                   # pebbled grain, soft
pebble = ImageChops.add(pebble, tile_noise(64, 50), 2.0, 0)
wear = tile_noise(8, 90).point(lambda v: max(0, (v - 135) * 3))   # broad worn patches
height = ImageChops.add(pebble, wear.point(lambda v: v // 3), 1.0, -20)
lum = pebble.point(lambda v: 36 + v * 9 // 255)                # 36..45: near black, barely mottled
alb = Image.merge("RGB", (lum, lum.point(lambda v: v - 1), lum.point(lambda v: v - 3)))
alb = blend(alb, Image.merge("RGB", (lum.point(lambda v: v + 14),) * 3), wear.point(lambda v: v * 2 // 5))
alb.save(os.path.join(OUT, "Glove_Albedo.png"))
normal_from(height.filter(ImageFilter.GaussianBlur(2.0)), 0.3).save(os.path.join(OUT, "Glove_Normal.png"))
print("wrote glove maps")

# ------------------------------------------------------------------ canvas sleeve
weave = Image.new("L", (N, N))
px = []
period = 16                                                    # one thread every 16 px
for y in range(N):
    for x in range(N):
        a = (x // period + y // period) % 2                    # over / under
        u = (x % period) / period; v = (y % period) / period
        t = (1 - abs(u - 0.5) * 2) if a else (1 - abs(v - 0.5) * 2)
        px.append(int(110 + 50 * t))
weave.putdata(px)
fibre = tile_noise(256, 30)
weave = ImageChops.add(weave, fibre, 1.0, -110)
dirt = tile_noise(10, 90).point(lambda v: max(0, (v - 120) * 2))
base = (64, 68, 50)
lum = weave.point(lambda v: 205 + v * 50 // 255)
alb = Image.merge("RGB", tuple(lum.point(lambda v, c=c: v * c // 255) for c in base))
alb = blend(alb, Image.new("RGB", (N, N), (40, 38, 30)), dirt.point(lambda v: v // 2))
alb.save(os.path.join(OUT, "Cloth_Albedo.png"))
normal_from(weave.filter(ImageFilter.GaussianBlur(1.6)), 0.5).save(os.path.join(OUT, "Cloth_Normal.png"))
print("wrote cloth maps")
