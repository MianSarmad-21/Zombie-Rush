"""Draws Assets/UI/Art/scope_overlay.png: the AKM's 2x sight (2026-11-14), 2048x2048 RGBA.

    blender -b --factory-startup -P Tools/UI/make_scope_2x.py [-- OUT.png]

A low-power combat sight, not a sniper scope: a wide clear lens (the zoomed world shows through), a dark
machined housing ring with a lit inner bevel, a light vignette only at the very edge, a faint lens tint, and an
illuminated red circle-dot reticle with four fine black posts that stop short of the centre.
Black outside the lens (the HUD fills the rest of a wide screen with black).
"""
import sys

import bpy
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else "C:/Mian Sarmad/Unity/Zombie-Rush/Zombie Rush/Assets/UI/Art/scope_overlay.png"
S = 2048
c = (S - 1) / 2
y, x = np.mgrid[0:S, 0:S].astype(np.float32)
dx, dy = x - c, c - y
r = np.hypot(dx, dy)
aa = 1.2                                                        # anti-aliasing width (px)


def band(d, w):
    """1 inside a band of half-width w around d = 0, smooth edges"""
    return np.clip((w - np.abs(d)) / aa + 0.5, 0, 1)


def inside(d):
    """1 where d < 0"""
    return np.clip(-d / aa + 0.5, 0, 1)


def over(rgb, a, col, alpha):
    """paint col with coverage alpha over (rgb, a)"""
    alpha = alpha[..., None] if alpha.ndim == 2 else alpha
    rgb[:] = rgb * (1 - alpha) + np.asarray(col, np.float32) * alpha
    a[:] = a + (1 - a) * alpha[..., 0]


CENTRED = True     # sight in the middle of the screen, the world visible round it (no black outside)
R = S * (0.395 if CENTRED else 0.468)                           # clear lens radius (lens + housing fit the square)
rgb = np.zeros((S, S, 3), np.float32)
a = np.zeros((S, S), np.float32)

# lens: faint tint, and a light vignette only in the outer 9 % of the radius
t = np.clip((r - R * 0.91) / (R * 0.09), 0, 1)
over(rgb, a, (0.04, 0.07, 0.06), np.full((S, S), 0.05, np.float32) * inside(r - R))
over(rgb, a, (0.0, 0.0, 0.0), (t ** 2.0 * 0.75) * inside(r - R))

# housing: a machined tube ring (dark steel, lit inner bevel, darker outer edge); outside it either black to the
# edge (full-screen sight) or a soft shadow fading out (centred sight: the world shows round it)
ring_w = S * (0.055 if CENTRED else 0.022)
if CENTRED:
    shade = np.clip(1 - (r - R - ring_w) / (S * 0.045), 0, 1) ** 1.6 * 0.6
    over(rgb, a, (0.0, 0.0, 0.0), shade * (1 - inside(r - R - ring_w)))
else:
    over(rgb, a, (0.0, 0.0, 0.0), 1 - inside(r - R - ring_w))
u = np.clip((r - R) / ring_w, 0, 1)
steel = np.stack([0.10 + 0.16 * (1 - u) ** 3, 0.11 + 0.16 * (1 - u) ** 3, 0.12 + 0.17 * (1 - u) ** 3], -1)
ring = inside(r - R - ring_w) * (1 - inside(r - R))
rgb[:] = rgb * (1 - ring[..., None]) + steel * ring[..., None]
a[:] = a + (1 - a) * ring
over(rgb, a, (0.55, 0.58, 0.60), band(r - R - 1.5, 1.2) * 0.8)   # thin highlight on the lens edge

# fine black posts from the ring inwards, stopping well short of the centre
post_w, gap = S * 0.0022, R * 0.42
for ax, other in ((dx, dy), (dy, dx)):
    m = band(other, post_w) * (1 - inside(np.abs(ax) - gap)) * inside(np.abs(ax) - R)
    over(rgb, a, (0.02, 0.02, 0.025), m)
    # thicker outer third of each post
    m2 = band(other, post_w * 2.6) * (1 - inside(np.abs(ax) - R * 0.78)) * inside(np.abs(ax) - R)
    over(rgb, a, (0.02, 0.02, 0.025), m2)

# illuminated red reticle: a soft glow, then the circle and the dot
red = (1.0, 0.16, 0.10)
ring_r, ring_half, dot_r = S * 0.040, S * 0.0019, S * 0.0042
glow = np.exp(-((r - ring_r) / (S * 0.006)) ** 2) * 0.22 + np.exp(-(r / (S * 0.008)) ** 2) * 0.30
over(rgb, a, red, glow)
over(rgb, a, red, band(r - ring_r, ring_half))
over(rgb, a, (1.0, 0.35, 0.28), inside(r - dot_r))

# four tiny red ticks on the circle (holdover marks)
for ax, other in ((dx, dy), (dy, dx)):
    m = band(other, ring_half) * (1 - inside(np.abs(ax) - ring_r - S * 0.004)) * inside(np.abs(ax) - ring_r - S * 0.016)
    over(rgb, a, red, m)

img = np.concatenate([rgb / np.maximum(a[..., None], 1e-4) * (a[..., None] > 0), a[..., None]], -1)
img = np.clip(img, 0, 1)[::-1]                                  # Blender images are bottom-up
im = bpy.data.images.new("scope", S, S, alpha=True)
im.colorspace_settings.name = "sRGB"
im.alpha_mode = "STRAIGHT"
im.pixels.foreach_set(img.astype(np.float32).ravel())
im.filepath_raw = OUT
im.file_format = "PNG"
im.save()
print("[scope] saved", OUT)
