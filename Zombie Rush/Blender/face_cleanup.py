"""Cleans the player's face paint (2026-11-14): Meshy left thin dark cracks and blotches on the skin (cheek
scar, near the ear, a black blob in the nostril, a notch at the right mouth corner).

    blender -b --factory-startup -P Blender/face_cleanup.py -- [--preview DIR] [--write]

Texture space, head skin only: texels of faces driven by the Head/Neck bones whose colour is skin. A mark is a
thin dark feature (black-hat of the luminance, under ~9 px wide) that is almost entirely surrounded by skin, so
eyes, brows, lashes and lips (bordered by non-skin) stay. Marks are filled from the surrounding skin.
Works on ShadowOperative_Albedo.png in place (run body_texture_enhance.py first; it rebuilds the albedo from
*_Albedo_orig.png, so run this again after it).
"""
import os
import sys

import bpy
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
WRITE = "--write" in argv
PREVIEW = argv[argv.index("--preview") + 1] if "--preview" in argv else None
FOLDER = "C:/Mian Sarmad/Unity/Zombie-Rush/Zombie Rush/Assets/Models/2026-11-14_ShadowOperative_Final/"


def log(*a):
    print("[face]", *a, flush=True)


for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.import_scene.fbx(filepath=FOLDER + "ShadowOperative.fbx")
mesh = max((o for o in bpy.data.objects if o.type == "MESH"), key=lambda o: len(o.data.vertices))
me = mesh.data
im = bpy.data.images.load(FOLDER + "ShadowOperative_Albedo.png")
W, H = im.size
C = np.array(im.pixels[:], np.float32).reshape(H, W, 4)
rgb = C[..., :3]

# ---- head faces -> UV mask
gi = {g.name: g.index for g in mesh.vertex_groups}
head_groups = {gi[n] for n in ("Head", "Neck") if n in gi}
headv = np.zeros(len(me.vertices), bool)
for v in me.vertices:
    w_head = sum(g.weight for g in v.groups if g.group in head_groups)
    w_all = sum(g.weight for g in v.groups) or 1.0
    headv[v.index] = w_head / w_all > 0.5
uv = me.uv_layers.active.data
mask = np.zeros((H, W), bool)
yy, xx = np.mgrid[0:H, 0:W]
n_faces = 0
for p in me.polygons:
    if not all(headv[i] for i in p.vertices): continue
    n_faces += 1
    pts = [uv[li].uv for li in p.loop_indices]
    for k in range(1, len(pts) - 1):                       # fan triangles
        tri = np.array([pts[0], pts[k], pts[k + 1]]) * [W, H]
        x0, y0 = np.floor(tri.min(0)).astype(int) - 1
        x1, y1 = np.ceil(tri.max(0)).astype(int) + 1
        x0, y0, x1, y1 = max(x0, 0), max(y0, 0), min(x1, W), min(y1, H)
        if x1 <= x0 or y1 <= y0: continue
        X, Y = xx[y0:y1, x0:x1] + 0.5, yy[y0:y1, x0:x1] + 0.5
        (ax, ay), (bx, by), (cx, cy) = tri
        d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(d) < 1e-9: continue
        l1 = ((by - cy) * (X - cx) + (cx - bx) * (Y - cy)) / d
        l2 = ((cy - ay) * (X - cx) + (ax - cx) * (Y - cy)) / d
        inside = (l1 >= -0.02) & (l2 >= -0.02) & (1 - l1 - l2 >= -0.02)
        mask[y0:y1, x0:x1] |= inside
log("head faces", n_faces, "texels", int(mask.sum()))


def box(a, r):
    """box mean, radius r (a: HxW float)"""
    c = np.cumsum(np.cumsum(np.pad(a, ((r + 1, r), (r + 1, r)), mode="edge"), 0), 1)
    k = 2 * r + 1
    return (c[k:, k:] - c[:-k, k:] - c[k:, :-k] + c[:-k, :-k]) / (k * k)


def maxf(a, r):
    out = a.copy()
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            out = np.maximum(out, np.roll(np.roll(a, dy, 0), dx, 1))
    return out


def minf(a, r):
    return -maxf(-a, r)


lin = np.where(rgb <= 0.04045, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)
L = (lin * [0.2126, 0.7152, 0.0722]).sum(-1)
r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
skin = mask & (r > g) & (g > b * 0.95) & (r > 0.28) & (r - b > 0.08)
log("skin texels", int(skin.sum()))
# thin dark features: black-hat (closing minus the image) with a 4 px structuring radius
close = minf(maxf(L, 4), 4)
blackhat = close - L
skin_frac = box(skin.astype(np.float32), 7)
marks = mask & (blackhat > 0.035) & (blackhat / np.maximum(close, 1e-3) > 0.28) & (skin_frac > 0.72)
marks = maxf(marks.astype(np.float32), 1) > 0                # grow 1 px over the soft edge
log("mark texels", int(marks.sum()))

# fill: normalised blur of the good skin around each mark
good = (skin & ~marks).astype(np.float32)
fill = np.zeros_like(rgb)
den = box(good, 6)
for ch in range(3):
    fill[..., ch] = box(rgb[..., ch] * good, 6) / np.maximum(den, 1e-4)
soft = np.clip(box(marks.astype(np.float32), 1) * 1.5, 0, 1)[..., None]   # feathered edge
ok = (den > 0.05)[..., None]
out = np.where(ok, rgb * (1 - soft) + fill * soft, rgb)
C2 = np.concatenate([out, C[..., 3:]], -1)


def save(arr, path):
    img = bpy.data.images.new(os.path.basename(path), W, H, alpha=True)
    img.colorspace_settings.name = "sRGB"
    img.pixels.foreach_set(arr.astype(np.float32).ravel())
    img.filepath_raw = path; img.file_format = "PNG"; img.save(); log("saved", path)


if PREVIEW:
    os.makedirs(PREVIEW, exist_ok=True)
    save(C2, os.path.join(PREVIEW, "face_albedo.png"))
    dbg = C.copy(); dbg[marks] = [1, 0, 1, 1]; dbg[mask & ~skin & ~marks, :3] *= 0.5
    save(dbg, os.path.join(PREVIEW, "face_marks.png"))
if WRITE:
    save(C2, FOLDER + "ShadowOperative_Albedo.png")
