"""Draws Assets/UI/Art/scope_overlay.png: the AKM's 2x scope seen from behind (2026-11-16), 2048x2048 RGBA.

    python Tools/UI/make_scope_real.py [--preview OUT.png]

Replaces the flat ring of make_scope_2x.py with a real eyepiece: a thick rubber eye-cup (ribbed, lit from
above, rounded), a machined metal bevel round the glass, the dark fall-off at the lens edge every real scope
has, a faint glass tint with a soft glare, and a soft shadow round the outside. The world shows round it
(centred sight, ScopeUI) and the gun is drawn below it by the first-person camera.
Reticle: fine black posts (thick outer thirds) and an illuminated red circle-dot, as before.
"""
import os
import sys

import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "UI", "Art", "scope_overlay.png")
preview = sys.argv[sys.argv.index("--preview") + 1] if "--preview" in sys.argv else None

S = 2048
c = (S - 1) / 2
y, x = np.mgrid[0:S, 0:S].astype(np.float32)
dx, dy = x - c, c - y                 # dy: up is positive
r = np.hypot(dx, dy)
ang = np.arctan2(dy, dx)
aa = 1.2
up = np.clip(dy / np.maximum(r, 1), -1, 1)    # +1 at the top of a ring, -1 at the bottom


def band(d, w):
    return np.clip((w - np.abs(d)) / aa + 0.5, 0, 1)


def inside(d):
    return np.clip(-d / aa + 0.5, 0, 1)


rgb = np.zeros((S, S, 3), np.float32)
a = np.zeros((S, S), np.float32)


def over(col, alpha):
    alpha = np.clip(alpha, 0, 1)
    col = np.asarray(col, np.float32)
    if col.ndim == 1:
        col = col[None, None, :]
    rgb[:] = rgb * (1 - alpha[..., None]) + col * alpha[..., None]
    a[:] = a + (1 - a) * alpha


R = S * 0.355            # clear glass radius
BEVEL = S * 0.022        # machined metal ring round the glass
CUP = S * 0.082          # rubber eye-cup
SHADOW = S * 0.038       # soft shadow round the outside
R1, R2 = R + BEVEL, R + BEVEL + CUP
lens = inside(r - R)

# ---- glass: faint cool tint, the exit-pupil fall-off at the edge (dark, a touch blue), a soft glare
over((0.03, 0.06, 0.07), 0.07 * lens)
t = np.clip((r - R * 0.72) / (R * 0.28), 0, 1)
over((0.0, 0.0, 0.01), (t ** 2.2) * 0.93 * lens)
over((0.10, 0.25, 0.45), band(r - R * 0.965, R * 0.018) * 0.10 * lens)          # thin blue fringe (coating)
# glare: a soft crescent across the upper left of the glass
gx, gy = dx + R * 0.30, dy - R * 0.30
crescent = np.clip(1 - np.abs(np.hypot(gx, gy) - R * 0.95) / (R * 0.10), 0, 1) ** 2
crescent *= np.clip((-(dx * 0.7) + dy * 0.7) / R, 0, 1)
over((0.85, 0.92, 1.0), crescent * 0.07 * lens)


# ---- reticle: fine black posts from the edge inwards (thick outer third), red circle-dot
post_w, gap = S * 0.0021, R * 0.40
for ax, other in ((dx, dy), (dy, dx)):
    over((0.015, 0.015, 0.02), band(other, post_w) * (1 - inside(np.abs(ax) - gap)) * inside(np.abs(ax) - R))
    over((0.015, 0.015, 0.02), band(other, post_w * 2.8) * (1 - inside(np.abs(ax) - R * 0.74)) * inside(np.abs(ax) - R))
# small range ticks on the lower post
for k in (0.50, 0.58, 0.66):
    over((0.015, 0.015, 0.02), band(dy + R * k, S * 0.0018) * band(dx, S * 0.012) * lens)
red = (1.0, 0.16, 0.10)
ring_r, ring_half, dot_r = S * 0.038, S * 0.0019, S * 0.0040
over(red, np.exp(-((r - ring_r) / (S * 0.006)) ** 2) * 0.22 + np.exp(-(r / (S * 0.008)) ** 2) * 0.30)
over(red, band(r - ring_r, ring_half))
over((1.0, 0.35, 0.28), inside(r - dot_r))

# ---- soft shadow outside the eye-cup (heavier underneath, where the cup hangs over the gun)
sh = np.clip(1 - (r - R2) / (SHADOW * (1.0 - 0.35 * up)), 0, 1) ** 1.8 * 0.62
over((0.0, 0.0, 0.0), sh * (1 - inside(r - R2)))

# ---- rubber eye-cup: rounded profile (light along its crest), lit from above, fine ribs, a lip at each edge
u = np.clip((r - R1) / CUP, 0, 1)
crest = np.clip(np.sin(np.pi * u), 0, 1) ** 0.8                                 # rounded: brightest along the middle
light = 0.55 + 0.45 * up                                          # top lit, bottom in shade
ribs = 0.5 + 0.5 * np.sin(ang * 140)                              # moulded grip ribs
ribs = 1 - 0.18 * ribs * np.clip((u - 0.25) / 0.5, 0, 1) * np.clip((0.95 - u) / 0.3, 0, 1)
v = (0.035 + 0.075 * crest * light) * ribs
cup = np.stack([v * 1.0, v * 1.02, v * 1.06], -1)
mask_cup = inside(r - R2) * (1 - inside(r - R1))
over(cup, mask_cup)
over((0.30, 0.31, 0.33), band(r - R2 + S * 0.004, S * 0.0022) * np.clip(0.25 + 0.75 * up, 0, 1) * 0.55)   # outer lip catch-light

# ---- machined metal bevel round the glass: a cone lit on its upper inner face, knurled band, dark seat
ub = np.clip((r - R) / BEVEL, 0, 1)
facet = np.where(ub < 0.45, 0.20 + 0.32 * np.clip(-up * 0.2 + 0.8 * np.clip(up, 0, 1) + 0.2, 0, 1) * (1 - ub / 0.45), 0.09)
knurl = np.where(ub >= 0.45, 1 - 0.35 * (0.5 + 0.5 * np.sign(np.sin(ang * 360))), 1)
m = facet * knurl
metal = np.stack([m * 0.95, m * 0.98, m * 1.05], -1)
over(metal, inside(r - R1) * (1 - inside(r - R)))
over((0.75, 0.78, 0.82), band(r - R - 1.6, 1.1) * np.clip(0.2 + 0.8 * up, 0, 1) * 0.85)  # bright edge of the glass seat
over((0.0, 0.0, 0.0), band(r - R1, 1.4) * 0.8)                                        # seam between metal and rubber

img = np.concatenate([rgb / np.maximum(a[..., None], 1e-4) * (a[..., None] > 0), a[..., None]], -1)
img8 = (np.clip(img, 0, 1) * 255 + 0.5).astype(np.uint8)
Image.fromarray(img8, "RGBA").save(OUT)
print("[scope] saved", OUT)
if preview:
    # over a mid-grey/green test scene so the glass and the shadow can be judged
    bg = np.zeros((S, S, 3), np.float32)
    bg[:] = np.where((y < S * 0.55)[..., None], [0.25, 0.32, 0.45], [0.30, 0.38, 0.22])
    al = img[..., 3:4]
    comp = bg * (1 - al) + img[..., :3] * al
    Image.fromarray((np.clip(comp, 0, 1) * 255).astype(np.uint8)).resize((768, 768), Image.LANCZOS).save(preview)
    print("[scope] preview", preview)
