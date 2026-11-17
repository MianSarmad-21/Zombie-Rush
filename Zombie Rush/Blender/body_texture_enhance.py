"""Player body texture pass (2026-11-14): bakes an ambient-occlusion map and enhances the albedo.

    blender -b --factory-startup -P Blender/body_texture_enhance.py -- [--preview DIR] [--write]

  - ShadowOperative_Occlusion.png (2048, linear): Cycles AO baked from the body mesh onto its own UVs, so pouches,
    straps, folds, under the vest and between the fingers darken in Unity (URP Lit occlusion map).
  - ShadowOperative_Albedo.png: local contrast (detail lifted against a wide blur), a light unsharp mask, a little
    saturation back, and the AO's cavities multiplied in softly. The original goes to *_Albedo_orig.png once.
Without --write the results go only to --preview (default: the session scratchpad given on the command line).
"""
import os
import shutil
import sys

import bpy
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
WRITE = "--write" in argv
PREVIEW = argv[argv.index("--preview") + 1] if "--preview" in argv else None
FOLDER = "C:/Mian Sarmad/Unity/Zombie-Rush/Zombie Rush/Assets/Models/2026-11-14_ShadowOperative_Final/"
SIZE = 2048


def log(*a):
    print("[tex]", *a, flush=True)


for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.import_scene.fbx(filepath=FOLDER + "ShadowOperative.fbx")
mesh = max((o for o in bpy.data.objects if o.type == "MESH"), key=lambda o: len(o.data.vertices))
for o in bpy.data.objects: o.select_set(o is mesh)
bpy.context.view_layer.objects.active = mesh
log("mesh", mesh.name, len(mesh.data.polygons), "faces, uv", [u.name for u in mesh.data.uv_layers])

# ---------------------------------------------------------------- AO bake
sc = bpy.context.scene
try:
    sc.render.engine = "CYCLES"
except TypeError as e:
    log("engine:", e); raise
sc.cycles.samples = 128
sc.cycles.device = "CPU"
ao = bpy.data.images.new("AO", SIZE, SIZE, alpha=False, float_buffer=True)
ao.colorspace_settings.name = "Non-Color"
for m in mesh.data.materials:
    nt = m.node_tree
    node = nt.nodes.new("ShaderNodeTexImage"); node.image = ao
    for n in nt.nodes: n.select = False
    node.select = True; nt.nodes.active = node
sc.render.bake.margin = 16
sc.world = sc.world or bpy.data.worlds.new("W")
sc.world.light_settings.distance = 0.12          # contact-scale AO (the 10 m default darkens whole islands)
# Meshy meshes carry inward-facing faces: they bake black. Point every face outwards on this bake copy.
import bmesh
bm = bmesh.new(); bm.from_mesh(mesh.data)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
bm.to_mesh(mesh.data); bm.free()
bpy.ops.object.bake(type="AO", margin=16, use_clear=True)
A = np.array(ao.pixels[:], np.float32).reshape(SIZE, SIZE, 4)[..., 0]
log("AO mean", round(float(A.mean()), 3), "min", round(float(A.min()), 3))


def blur(img, r):
    """separable box blur x3 (~gaussian), radius r px; works on HxW or HxWxC"""
    out = img
    for _ in range(3):
        for ax in (0, 1):
            c = np.cumsum(np.pad(out, [(r + 1, r) if i == ax else (0, 0) for i in range(out.ndim)], mode="edge"), axis=ax)
            hi = np.take(c, np.arange(2 * r + 1, c.shape[ax]), axis=ax)
            lo = np.take(c, np.arange(0, c.shape[ax] - 2 * r - 1), axis=ax)
            out = (hi - lo) / (2 * r + 1)
    return out


# soften bake noise, keep contact shadows; a floor so faces hidden inside other shells (they bake black) can't
# show as black patches where they peek through (the wrist cuffs)
A = np.clip(0.6 * A + 0.4 * blur(A, 2), 0, 1)
A = 0.35 + 0.65 * A

# ---------------------------------------------------------------- albedo
src_path = FOLDER + "ShadowOperative_Albedo.png"
orig_path = FOLDER + "ShadowOperative_Albedo_orig.png"
base_path = orig_path if os.path.exists(orig_path) else src_path        # always enhance from the original
im = bpy.data.images.load(base_path)
W, H = im.size
C = np.array(im.pixels[:], np.float32).reshape(H, W, 4)
rgb, alpha = C[..., :3], C[..., 3:]
if (W, H) != (SIZE, SIZE): log("albedo size", W, H, "(AO is", SIZE, ")")

lin = np.where(rgb <= 0.04045, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)    # work in linear light
lum = (lin * np.array([0.2126, 0.7152, 0.0722])).sum(-1, keepdims=True)
# 1. local contrast: lift detail against a wide blur (clarity), gentle
wide = blur(lum, 24)
clar = np.clip(lum + (lum - wide) * 0.55, 0, 1)
lin = lin * (clar / np.maximum(lum, 1e-4))
# 2. unsharp mask (fine detail, stitching, edges)
fine = blur(lin, 1)
lin = np.clip(lin + (lin - fine) * 0.6, 0, 1)
# 3. a little saturation back
lum2 = (lin * np.array([0.2126, 0.7152, 0.0722])).sum(-1, keepdims=True)
lin = np.clip(lum2 + (lin - lum2) * 1.12, 0, 1)
# 4. cavities from the AO, soft (the occlusion map does the rest in the lit shader)
if A.shape == lin.shape[:2]:
    lin = lin * (0.72 + 0.28 * A[..., None])
out = np.where(lin <= 0.0031308, lin * 12.92, 1.055 * np.power(lin, 1 / 2.4) - 0.055)
OUT = np.concatenate([np.clip(out, 0, 1), alpha], -1)


def save(arr, path, colorspace, w, h):
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=arr.shape[-1] == 4)
    img.colorspace_settings.name = colorspace
    if arr.ndim == 2: arr = np.stack([arr, arr, arr, np.ones_like(arr)], -1)
    img.pixels.foreach_set(arr.astype(np.float32).ravel())
    img.filepath_raw = path; img.file_format = "PNG"; img.save()
    log("saved", path)


AO_RGBA = np.stack([A, A, A, np.ones_like(A)], -1)
if PREVIEW:
    os.makedirs(PREVIEW, exist_ok=True)
    save(AO_RGBA, os.path.join(PREVIEW, "ShadowOperative_Occlusion.png"), "Non-Color", SIZE, SIZE)
    save(OUT, os.path.join(PREVIEW, "ShadowOperative_Albedo.png"), "sRGB", W, H)
if WRITE:
    if not os.path.exists(orig_path): shutil.copy(src_path, orig_path); log("original kept as", orig_path)
    save(AO_RGBA, FOLDER + "ShadowOperative_Occlusion.png", "Non-Color", SIZE, SIZE)
    save(OUT, src_path, "sRGB", W, H)
