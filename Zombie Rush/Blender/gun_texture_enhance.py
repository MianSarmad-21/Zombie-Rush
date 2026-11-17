"""Gun texture pass (2026-11-15): baked ambient occlusion + an enhanced albedo for Pistol, Rifle, Shotgun.

    blender -b --factory-startup -P Blender/gun_texture_enhance.py -- [--only Rifle] [--preview DIR] [--write]

For each gun in Assets/Models/2026-11-09_Guns/<Gun>/<Gun>.fbx (all its mesh parts together):
  - <Gun>_Occlusion.png (2048, linear): Cycles AO over 2.5 cm (gun-scale contact shading: grooves, rails,
    under the sights, between parts); normals recalculated on the bake copy; giant-UV slivers left out; floor 0.4.
    GunSetupTools assigns it as the URP occlusion map.
  - <Gun>_Albedo.png: clarity (detail lifted against a blur), unsharp mask, a little saturation, soft AO cavities.
    The original is kept once as <Gun>_Albedo_orig.png and always used as the source.
"""
import os
import shutil
import sys

import bmesh
import bpy
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
WRITE = "--write" in argv
PREVIEW = argv[argv.index("--preview") + 1] if "--preview" in argv else None
ONLY = set(argv[argv.index("--only") + 1].split(",")) if "--only" in argv else None
ROOT = "C:/Mian Sarmad/Unity/Zombie-Rush/Zombie Rush/Assets/Models/2026-11-09_Guns/"
GUNS = ["Pistol", "Rifle", "Shotgun"]


def log(*a):
    print("[gun]", *a, flush=True)


def blur(img, r):
    out = img
    for _ in range(3):
        for ax in (0, 1):
            c = np.cumsum(np.pad(out, [(r + 1, r) if i == ax else (0, 0) for i in range(out.ndim)], mode="edge"), axis=ax)
            hi = np.take(c, np.arange(2 * r + 1, c.shape[ax]), axis=ax)
            lo = np.take(c, np.arange(0, c.shape[ax] - 2 * r - 1), axis=ax)
            out = (hi - lo) / (2 * r + 1)
    return out


def save(arr, path, cs):
    h, w = arr.shape[:2]
    if arr.ndim == 2: arr = np.stack([arr, arr, arr, np.ones_like(arr)], -1)
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=True); img.colorspace_settings.name = cs
    img.pixels.foreach_set(arr.astype(np.float32).ravel()); img.filepath_raw = path; img.file_format = "PNG"; img.save()
    log("saved", path)


def do_gun(gun):
    d = ROOT + gun + "/"
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    bpy.ops.import_scene.fbx(filepath=d + gun + ".fbx")
    meshes = [o for o in bpy.data.objects if o.type == "MESH" and o.data.uv_layers]
    src = d + gun + "_Albedo_orig.png"
    if not os.path.exists(src): src = d + gun + "_Albedo.png"
    alb = bpy.data.images.load(src)
    W, H = alb.size
    log(gun, "parts", [(m.name, len(m.data.polygons)) for m in meshes], "albedo", (W, H))
    # bake copy clean-up: outward normals, no giant-UV slivers, triangles
    for m in meshes:
        bm = bmesh.new(); bm.from_mesh(m.data); uv = bm.loops.layers.uv.active
        big = []
        for f in bm.faces:
            pts = [l[uv].uv for l in f.loops]
            if 0.5 * abs(sum(pts[i].x * pts[i - 1].y - pts[i - 1].x * pts[i].y for i in range(len(pts)))) > 0.02: big.append(f)
        bmesh.ops.delete(bm, geom=big, context="FACES_ONLY")
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        bm.to_mesh(m.data); bm.free()
    meshes = [m for m in meshes if len(m.data.polygons) > 0]    # parts with nothing to bake (e.g. the pistol's MagBody)
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"; sc.cycles.samples = 96; sc.cycles.device = "CPU"
    sc.world = sc.world or bpy.data.worlds.new("W"); sc.world.light_settings.distance = 0.025
    ao = bpy.data.images.new("AO", W, H, alpha=False, float_buffer=True); ao.colorspace_settings.name = "Non-Color"
    mats = {s.material for m in meshes for s in m.material_slots if s.material}
    for mat in mats:
        mat.use_nodes = True
        n = mat.node_tree.nodes.new("ShaderNodeTexImage"); n.image = ao
        for x in mat.node_tree.nodes: x.select = False
        n.select = True; mat.node_tree.nodes.active = n
    for o in bpy.data.objects: o.select_set(o in meshes)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.bake(type="AO", margin=8, use_clear=True)
    A = np.array(ao.pixels[:], np.float32).reshape(H, W, 4)[..., 0]
    A = 0.4 + 0.6 * np.clip(0.6 * A + 0.4 * blur(A, 1), 0, 1)
    log(gun, "AO mean", round(float(A.mean()), 3))

    C = np.array(alb.pixels[:], np.float32).reshape(H, W, 4)
    rgb, alpha = C[..., :3], C[..., 3:]
    lin = np.where(rgb <= 0.04045, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)
    lum = (lin * np.array([0.2126, 0.7152, 0.0722])).sum(-1, keepdims=True)
    clar = np.clip(lum + (lum - blur(lum, 16)) * 0.6, 0, 1)
    lin = lin * (clar / np.maximum(lum, 1e-4))
    lin = np.clip(lin + (lin - blur(lin, 1)) * 0.7, 0, 1)
    l2 = (lin * np.array([0.2126, 0.7152, 0.0722])).sum(-1, keepdims=True)
    lin = np.clip(l2 + (lin - l2) * 1.08, 0, 1)
    lin = lin * (0.75 + 0.25 * A[..., None])
    out = np.where(lin <= 0.0031308, lin * 12.92, 1.055 * np.power(lin, 1 / 2.4) - 0.055)
    OUT = np.concatenate([np.clip(out, 0, 1), alpha], -1)
    if PREVIEW:
        os.makedirs(PREVIEW, exist_ok=True)
        save(A, os.path.join(PREVIEW, gun + "_Occlusion.png"), "Non-Color")
        save(OUT, os.path.join(PREVIEW, gun + "_Albedo.png"), "sRGB")
    if WRITE:
        orig = d + gun + "_Albedo_orig.png"
        if not os.path.exists(orig): shutil.copy(d + gun + "_Albedo.png", orig); log("original kept as", orig)
        save(A, d + gun + "_Occlusion.png", "Non-Color")
        save(OUT, d + gun + "_Albedo.png", "sRGB")


for g in GUNS:
    if ONLY and g not in ONLY: continue
    do_gun(g)
