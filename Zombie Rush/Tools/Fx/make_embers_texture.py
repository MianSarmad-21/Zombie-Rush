"""Gate fire line: a long bed of burning coals and charred wood across a base gate (2026-11-16).

    python Tools/Fx/make_embers_texture.py [--preview out.png]

Writes Assets/Resources/ZombieRush/Fx/Fx_Embers.png (1024 x 256, PREMULTIPLIED alpha: drawn with Blend One
OneMinusSrcAlpha like Fx_Breach, so the black char darkens the ground and the glowing coals add light, one draw).
Layout: u runs along the line (tiles), v across it; soft ragged edges top and bottom, hottest along the middle.
"""
import os, sys
import numpy as np
from PIL import Image, ImageFilter

W, H = 1024, 256
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "Assets", "Resources", "ZombieRush", "Fx", "Fx_Embers.png")
rng = np.random.default_rng(11)


def noise(scale_x, scale_y, octaves=5, seed=0):
    """Tileable-in-u value noise 0..1."""
    r = np.random.default_rng(seed)
    out = np.zeros((H, W), np.float32); amp, tot = 1.0, 0.0
    for o in range(octaves):
        sx, sy = max(2, int(scale_x * 2 ** o)), max(2, int(scale_y * 2 ** o))
        g = r.random((sy + 1, sx)).astype(np.float32)
        g = np.concatenate([g, g[:, :1]], 1)            # wrap in u so the strip tiles
        im = Image.fromarray((g * 255).astype(np.uint8)).resize((W, H), Image.BICUBIC)
        out += np.asarray(im, np.float32) / 255 * amp; tot += amp; amp *= 0.5
    return out / tot


yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
v = (yy + 0.5) / H                       # 0..1 across
across = np.abs(v - 0.5) * 2             # 0 middle .. 1 edge

# ragged outline of the bed
edge_n = noise(10, 2, 4, 1)
bed = np.clip((0.92 - across - (edge_n - 0.5) * 0.45) / 0.18, 0, 1)

# coals: lumpy cells; cracks between them glow
cells = noise(40, 10, 3, 2)
lumps = noise(90, 22, 2, 3)
hot = np.clip(1 - across * 1.15, 0, 1) ** 1.3                     # hottest along the middle
crack = np.clip(1 - np.abs(cells - 0.5) / 0.06, 0, 1) ** 1.5      # thin lines where the noise crosses 0.5
glow_spots = np.clip((lumps - 0.62) / 0.2, 0, 1) ** 1.2
heat = np.clip((crack * 0.9 + glow_spots * 0.8) * (0.25 + 0.75 * hot) + hot * 0.12 * noise(30, 8, 3, 4), 0, 1)

# char: near-black with a little grey ash on top of the coals
ash = np.clip((noise(60, 15, 3, 5) - 0.55) / 0.25, 0, 1) * (1 - heat) * 0.5
char = np.stack([0.035 + 0.10 * ash, 0.032 + 0.10 * ash, 0.03 + 0.10 * ash], -1)

# glow colour: deep red at the cool end, orange, yellow-white in the hottest cracks
h = heat[..., None]
glow = np.where(h < 0.5, np.array([0.9, 0.18, 0.02]) * (h / 0.5),
                np.array([0.9, 0.18, 0.02]) + (np.array([1.0, 0.75, 0.3]) - np.array([0.9, 0.18, 0.02])) * ((h - 0.5) / 0.5))
glow = glow * (0.35 + 1.4 * h)            # bright: added light

alpha = bed * (0.88 - 0.25 * across)     # the char darkens the ground, softer at the edges
rgb = char * alpha[..., None] + glow * bed[..., None]   # premultiplied: char * a + emitted light
rgb = np.clip(rgb, 0, 1)
img = np.concatenate([rgb, alpha[..., None]], -1)
im = Image.fromarray((img * 255 + 0.5).astype(np.uint8), "RGBA").filter(ImageFilter.GaussianBlur(0.6))
im.save(OUT)
print("[embers] saved", OUT)
if "--preview" in sys.argv:
    p = sys.argv[sys.argv.index("--preview") + 1]
    a = np.asarray(im, np.float32) / 255
    ground = np.array([0.22, 0.25, 0.14])
    comp = a[..., :3] + ground * (1 - a[..., 3:4])
    Image.fromarray((np.clip(comp, 0, 1) * 255).astype(np.uint8)).save(p)
    print("[embers] preview", p)
