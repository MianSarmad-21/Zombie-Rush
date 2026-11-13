"""Effect textures for blood and poison (Assets/Resources/ZombieRush/Fx). White albedo with the shape in
alpha, so the game tints each one (red blood, green poison). Run: python Tools/Fx/make_fx_textures.py
  Fx_Splat   : blood splatter for the ground: a ragged blob, spikes flung out, loose droplets
  Fx_Puddle  : poison goo puddle: a lumpy blob with a darker rim and glossy highlights
  Fx_Drop    : one droplet (stretched along its flight by the particle renderer)
  Fx_Smoke   : a soft billowing puff (gas cloud, blood mist)
  Fx_Chunk   : a small ragged gore/goo chunk
"""
import math, os, random
from PIL import Image, ImageDraw, ImageFilter, ImageChops

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Resources", "ZombieRush", "Fx")
random.seed(7)

def noise(size, scale, octaves=4, seed=0):
    """Fractal value noise as an L image (0..255)."""
    rnd = random.Random(seed)
    acc = Image.new("F", (size, size), 0.0)
    amp, total = 1.0, 0.0
    for o in range(octaves):
        n = max(2, int(size / scale * (2 ** o)))
        small = Image.new("L", (n, n))
        small.putdata([rnd.randint(0, 255) for _ in range(n * n)])
        big = small.resize((size, size), Image.BICUBIC).convert("F")
        accd = list(acc.getdata()); bd = list(big.getdata())
        acc.putdata([x + y * amp for x, y in zip(accd, bd)])
        total += amp; amp *= 0.5
    d = list(acc.getdata())
    lo, hi = min(d), max(d)
    out = Image.new("L", (size, size)); out.putdata([int((v - lo) / (hi - lo + 1e-6) * 255) for v in d])
    return out

def radial(size, f):
    img = Image.new("L", (size, size)); h = (size - 1) / 2
    img.putdata([int(255 * max(0.0, min(1.0, f(math.hypot(x - h, y - h) / h)))) for y in range(size) for x in range(size)])
    return img

def threshold(img, t, soft=10):
    return img.point(lambda v: 0 if v < t - soft else 255 if v > t + soft else int((v - (t - soft)) / (2 * soft) * 255))

def save(name, lum, alpha):
    img = Image.merge("RGBA", (lum, lum, lum, alpha))
    img.save(os.path.join(OUT, name + ".png"))
    print("wrote", name, img.size)

def splat(size=256, spikes=14, drops=26, seed=1, puddle=False):
    rnd = random.Random(seed)
    m = Image.new("L", (size, size), 0); d = ImageDraw.Draw(m); c = size / 2
    # main body: overlapping circles
    for _ in range(9 if puddle else 7):
        r = size * rnd.uniform(0.12, 0.22); a = rnd.uniform(0, 2 * math.pi); off = size * rnd.uniform(0.0, 0.10)
        x, y = c + math.cos(a) * off, c + math.sin(a) * off
        d.ellipse([x - r, y - r, x + r, y + r], fill=255)
    if not puddle:
        # spikes flung outwards, each ending in a droplet
        for _ in range(spikes):
            a = rnd.uniform(0, 2 * math.pi); L = size * rnd.uniform(0.22, 0.46); w = size * rnd.uniform(0.012, 0.03)
            x0, y0 = c + math.cos(a) * size * 0.12, c + math.sin(a) * size * 0.12
            x1, y1 = c + math.cos(a) * L, c + math.sin(a) * L
            d.line([x0, y0, x1, y1], fill=255, width=int(w * 2))
            r = w * rnd.uniform(1.2, 2.2); d.ellipse([x1 - r, y1 - r, x1 + r, y1 + r], fill=255)
        for _ in range(drops):
            a = rnd.uniform(0, 2 * math.pi); L = size * rnd.uniform(0.25, 0.48); r = size * rnd.uniform(0.006, 0.02)
            x, y = c + math.cos(a) * L, c + math.sin(a) * L
            d.ellipse([x - r, y - r, x + r, y + r], fill=255)
    else:
        for _ in range(10):
            a = rnd.uniform(0, 2 * math.pi); L = size * rnd.uniform(0.26, 0.40); r = size * rnd.uniform(0.04, 0.09)
            x, y = c + math.cos(a) * L, c + math.sin(a) * L
            d.ellipse([x - r, y - r, x + r, y + r], fill=255)
    # ragged edge: blur, perturb with noise, re-threshold
    n = noise(size, 28, 3, seed + 10)
    mixed = ImageChops.add(m.filter(ImageFilter.GaussianBlur(size * 0.02)).point(lambda v: v * 0.8), n.point(lambda v: v * 0.25))
    alpha = threshold(mixed, 150, 6).filter(ImageFilter.GaussianBlur(0.8))
    # inside: darker towards the rim (liquid collects at the edge), mottled
    inner = alpha.filter(ImageFilter.GaussianBlur(size * 0.04))
    mott = noise(size, 18, 4, seed + 20)
    lum = Image.new("L", (size, size))
    ia, ii, im = list(alpha.getdata()), list(inner.getdata()), list(mott.getdata())
    out = []
    for a, i, mo in zip(ia, ii, im):
        rim = 1.0 - (i / 255.0)                        # 1 at the edge, 0 deep inside
        v = 0.72 + 0.22 * (mo / 255.0) - 0.35 * rim
        out.append(int(255 * max(0.0, min(1.0, v))))
    lum.putdata(out)
    if puddle:
        # glossy highlights: a few soft bright spots
        hl = Image.new("L", (size, size), 0); hd = ImageDraw.Draw(hl)
        for _ in range(6):
            x, y = c + rnd.uniform(-0.2, 0.2) * size, c + rnd.uniform(-0.2, 0.2) * size; r = size * rnd.uniform(0.015, 0.04)
            hd.ellipse([x - r * 1.6, y - r, x + r * 1.6, y + r], fill=255)
        hl = hl.filter(ImageFilter.GaussianBlur(size * 0.01))
        lum = ImageChops.lighter(lum, ImageChops.multiply(hl, alpha))
    return lum, alpha

os.makedirs(OUT, exist_ok=True)
save("Fx_Splat", *splat(256, seed=3))
save("Fx_Puddle", *splat(256, seed=5, puddle=True))
# droplet: round, hard edge, small highlight
s = 64
a = radial(s, lambda r: 1.0 if r < 0.78 else max(0.0, (0.92 - r) / 0.14)).filter(ImageFilter.GaussianBlur(0.6))
lum = radial(s, lambda r: 0.78 + 0.22 * (1 - r))
hl = Image.new("L", (s, s), 0); ImageDraw.Draw(hl).ellipse([20, 16, 30, 24], fill=255); hl = hl.filter(ImageFilter.GaussianBlur(2))
save("Fx_Drop", ImageChops.lighter(lum, hl), a)
# smoke puff: noise inside a soft round falloff
s = 128
n = noise(s, 20, 4, 40)
fall = radial(s, lambda r: min(1.0, 1.6 * (1 - r)) if r < 1 else 0)
alpha = ImageChops.multiply(fall, n.point(lambda v: min(255, int(60 + v * 1.3))))
alpha = alpha.filter(ImageFilter.GaussianBlur(1.5))
lum = n.point(lambda v: int(180 + v * 0.29))
save("Fx_Smoke", lum, alpha)
# chunk: small ragged lump
lum, alpha = splat(64, spikes=0, drops=0, seed=9)
save("Fx_Chunk", lum, alpha)
