"""Zombie spawn "ground breach" decal: poisoned earth torn open by glowing toxic-green cracks (2026-11-16).

    python Tools/Fx/make_breach_texture.py [--preview out.png]

Writes Assets/Resources/ZombieRush/Fx/Fx_Breach.png (512, PREMULTIPLIED alpha: drawn with Blend One OneMinusSrcAlpha,
so the dark scorch darkens the ground and the crack glow adds light on top in one draw).
"""
import math, os, random, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

N = 512
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "Assets", "Resources", "ZombieRush", "Fx", "Fx_Breach.png")
rng = random.Random(7)
np.random.seed(7)


def fbm(n, scale, octaves=5):
    """Smooth value noise, 0..1."""
    out = np.zeros((n, n), np.float32); amp = 1.0; tot = 0.0
    for o in range(octaves):
        s = max(2, int(scale * 2 ** o))
        g = np.random.rand(s + 1, s + 1).astype(np.float32)
        im = Image.fromarray((g * 255).astype(np.uint8)).resize((n, n), Image.BICUBIC)
        out += np.asarray(im, np.float32) / 255 * amp; tot += amp; amp *= 0.5
    return out / tot


yy, xx = np.mgrid[0:N, 0:N].astype(np.float32)
cx = cy = (N - 1) / 2
dx, dy = (xx - cx) / (N / 2), (yy - cy) / (N / 2)
r = np.sqrt(dx * dx + dy * dy)
ang = np.arctan2(dy, dx)

# ---- scorched patch: irregular edge, darker towards the hole
edge = 0.78 + 0.16 * (fbm(N, 3) - 0.5) * 2 + 0.05 * np.sin(ang * 5 + 1.3)
scorch = np.clip((edge - r) / 0.22, 0, 1) ** 1.4
grain = fbm(N, 24, 3)
soil = np.stack([0.055, 0.065, 0.040]) [:, None, None] * (0.6 + 0.8 * grain)[None]
scorch_a = scorch * (0.80 + 0.15 * np.clip(1 - r / 0.5, 0, 1))

# ---- cracks: main fissures from the hole outward, each branching; drawn at 2x then blurred into glow
S = 2
mask = Image.new("L", (N * S, N * S), 0)
d = ImageDraw.Draw(mask)


def crack(x, y, a, length, width, depth):
    steps = int(length / 9) + 2
    pts = [(x, y)]
    for i in range(steps):
        a += rng.uniform(-0.22, 0.22) + (rng.choice([-1, 1]) * 0.5 if rng.random() < 0.12 else 0.0)   # jagged kinks
        step = length / steps
        x += math.cos(a) * step; y += math.sin(a) * step
        pts.append((x, y))
        w = max(1.0, width * (1 - i / steps) ** 0.8)
        (ax, ay), (bx, by) = pts[-2], pts[-1]
        d.line([(ax * S, ay * S), (bx * S, by * S)], fill=255, width=int(round(w * S)))
        d.ellipse([bx * S - w * S / 2, by * S - w * S / 2, bx * S + w * S / 2, by * S + w * S / 2], fill=255)   # round joints
        if depth < 1 and i > 2 and rng.random() < 0.09:
            crack(x, y, a + rng.choice([-1, 1]) * rng.uniform(0.5, 1.0), length * rng.uniform(0.3, 0.5), w * 0.65, depth + 1)


hole_r = 0.11 * N / 2
mains = 8
for k in range(mains):
    a = 2 * math.pi * (k + rng.uniform(-0.3, 0.3)) / mains
    x0 = cx + math.cos(a) * hole_r * 0.8; y0 = cy + math.sin(a) * hole_r * 0.8
    crack(x0, y0, a, N / 2 * rng.uniform(0.42, 0.7), rng.uniform(7.0, 11.0), 0)
# the torn hole in the middle
d2 = ImageDraw.Draw(mask)
pts = []
for i in range(40):
    a = 2 * math.pi * i / 40
    rr = hole_r * (1 + 0.28 * math.sin(a * 3 + 0.7) + rng.uniform(-0.12, 0.12))
    pts.append(((cx + math.cos(a) * rr) * S, (cy + math.sin(a) * rr) * S))
d2.polygon(pts, fill=255)
mask = mask.resize((N, N), Image.LANCZOS)
core = np.asarray(mask, np.float32) / 255
mid = np.asarray(mask.filter(ImageFilter.GaussianBlur(4)), np.float32) / 255
halo = np.asarray(mask.filter(ImageFilter.GaussianBlur(14)), np.float32) / 255

# cracks cool down towards their tips: hottest by the hole
heat = np.clip(1.15 - r * 1.05, 0.25, 1.0)
flicker = 0.75 + 0.5 * fbm(N, 10, 3)
c_core = np.array([0.80, 1.0, 0.38])[:, None, None]   # toxic: lime-white core
c_mid = np.array([0.28, 0.90, 0.08])[:, None, None]
c_halo = np.array([0.04, 0.32, 0.03])[:, None, None]
glow = (c_core * (core * heat)[None] + c_mid * (np.clip(mid * 1.3, 0, 1) * heat)[None] * 0.55
        + c_halo * (np.clip(halo * 1.6, 0, 1) * heat)[None] * 0.5) * flicker[None]
glow *= np.clip((edge + 0.05 - r) / 0.12, 0, 1)[None]            # nothing glows outside the patch
# the hole: a deep red throat
throat = np.clip(1 - r / 0.13, 0, 1) ** 0.7
pit = np.clip(1 - r / 0.075, 0, 1) ** 0.6                       # a dark pit with a red-hot rim
glow = glow * (1 - pit[None] * 0.92) + np.array([0.20, 0.75, 0.06])[:, None, None] * (throat * (1 - pit))[None] * 0.5

# ---- premultiplied: soil darkens (rgb = soil * a), glow adds; cracks are open (less soil there)
shade = np.clip(mid * 1.5, 0, 1) * (1 - core)            # the broken plates darken along the crack edges
a = np.clip(np.maximum(scorch_a * (1 - 0.6 * core), pit * 0.95), 0, 1)
soil = soil * (1 - 0.6 * shade[None]) * (1 - pit[None])
rgb = soil * a[None] + glow
img = np.concatenate([np.clip(rgb, 0, 1), a[None]], 0).transpose(1, 2, 0)
Image.fromarray((img * 255 + 0.5).astype(np.uint8), "RGBA").save(OUT)
print("wrote", OUT)

if "--preview" in sys.argv:
    # composite over a grass/dirt-ish ground, as premultiplied
    ground = np.ones((N, N, 3), np.float32) * np.array([0.22, 0.27, 0.16]) * (0.7 + 0.6 * fbm(N, 16, 3))[..., None]
    comp = img[..., :3] * 1.3 + ground * (1 - img[..., 3:4])
    Image.fromarray((np.clip(comp, 0, 1) * 255).astype(np.uint8)).save(sys.argv[sys.argv.index("--preview") + 1])
