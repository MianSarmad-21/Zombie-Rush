"""Zombie Rush loot chest (2026-11-16) from the user's Meshy "Locked Ironbound Chest" (C:\\Mian Sarmad\\Unity\\Chest.zip).

    blender -b --factory-startup -P Blender/build_chest.py -- [--preview DIR]

Exports Assets/Models/2026-11-16_Chest/ZR_Chest.fbx (+ ZR_Chest.png, 1024 px) with three parts for the opening animation:
  - ChestBody: the box; its top is cut open at the lid seam and given a dark, recessed inside (ZR_ChestInside);
  - ChestLid:  everything above the seam (with the hasp), pivot ON THE HINGE (back top edge) -> rotate about its X;
  - ChestLock: the padlock (body + shackle), pivot at the shackle top, double-sided (it falls and tumbles).
  Where the padlock hung, the front wall is flattened and textured with the plain painted wall next to it.
Also ZR_Medkit.fbx: a first-aid box for the chest's health reward (ZR_MedWhite / ZR_MedRed).
The chest faces +Y in Blender (forward in Unity), stands on the ground at the origin, metres.
"""
import math
import os
import sys
import zipfile

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
PREVIEW = argv[argv.index("--preview") + 1] if "--preview" in argv else None
HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
OUT = os.path.join(PROJECT, "Assets", "Models", "2026-11-16_Chest")
SRC_ZIP = os.path.normpath(os.path.join(PROJECT, "..", "..", "Chest.zip"))
TARGET_TRIS = 5000
SEAM_Z = 0.068          # lid / body split (top of the metal band), in the Meshy model's space (z up, centred)
LOCK_X, LOCK_Y = 0.08, -0.262   # padlock region: |x| < LOCK_X and in front of y < LOCK_Y (Meshy space: front = -Y)
CHEST_LENGTH = 0.85     # game size: long side in metres


def log(*a):
    print("[chest]", *a, flush=True)


def mat(name, color, rough=0.8, metal=0.0, emit=None):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    m.diffuse_color = (*color, 1)
    return m


def export(objs, path):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"},
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             mesh_smooth_type="FACE", use_mesh_modifiers=True, path_mode="STRIP",
                             embed_textures=False, add_leaf_bones=False, bake_anim=False)
    log("exported", path, [(o.name, len(o.data.polygons)) for o in objs])


def split_off(src, name, keep_face):
    """A copy of `src` with only the faces keep_face(face) is True for."""
    o = src.copy(); o.data = src.data.copy(); o.name = name; o.data.name = name
    bpy.context.scene.collection.objects.link(o)
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if not keep_face(f)], context="FACES")
    bm.to_mesh(o.data); bm.free()
    return o


def build_chest():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    tmp = os.path.join(OUT, "_tmp")
    os.makedirs(tmp, exist_ok=True)
    with zipfile.ZipFile(SRC_ZIP) as z: z.extractall(tmp)
    fbx = next(os.path.join(r, f) for r, _, fs in os.walk(tmp) for f in fs if f.lower().endswith(".fbx"))
    png = next(os.path.join(r, f) for r, _, fs in os.walk(tmp) for f in fs if f.lower().endswith(".png"))
    bpy.ops.import_scene.fbx(filepath=fbx)
    src = [o for o in bpy.data.objects if o.type == "MESH"][0]
    bpy.ops.object.select_all(action="DESELECT"); src.select_set(True); bpy.context.view_layer.objects.active = src
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for o in [o for o in bpy.data.objects if o is not src]: bpy.data.objects.remove(o, do_unlink=True)

    # texture: 1024, own material
    img = bpy.data.images.load(png); img.scale(1024, 1024)
    out_png = os.path.join(OUT, "ZR_Chest.png"); img.filepath_raw = out_png; img.file_format = "PNG"; img.save()
    skin = bpy.data.materials.new("ZR_Chest"); skin.use_nodes = True
    bs = next(n for n in skin.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    t = skin.node_tree.nodes.new("ShaderNodeTexImage"); t.image = img
    skin.node_tree.links.new(t.outputs[0], bs.inputs["Base Color"])
    src.data.materials.clear(); src.data.materials.append(skin)
    inside = mat("ZR_ChestInside", (0.035, 0.03, 0.025), 0.95)
    src.data.materials.append(inside)

    # fewer triangles
    before = len(src.data.polygons)
    dec = src.modifiers.new("dec", "DECIMATE"); dec.ratio = min(1.0, TARGET_TRIS / max(1, before)); dec.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=dec.name)
    log("tris", before, "->", len(src.data.polygons))
    # a real cut along the seam: triangles crossing it are split (UVs interpolated), so every face is wholly lid or
    # body (sorting big decimated triangles by their centre tore holes in the body wall)
    bm = bmesh.new(); bm.from_mesh(src.data)
    bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0, 0, SEAM_Z), plane_no=(0, 0, 1), dist=0.0005)
    bm.to_mesh(src.data); bm.free()

    # plain painted wall UV beside the lock (to texture the patch the padlock leaves)
    me = src.data; uvl = me.uv_layers.active.data
    best, wall_uv = 1e9, None
    want = Vector((0.12, -0.21, -0.1))
    for p in me.polygons:
        d = (p.center - want).length
        if d < best and p.normal.y < -0.7: best, wall_uv = d, Vector(uvl[p.loop_indices[0]].uv)
    log("wall uv", tuple(round(x, 3) for x in wall_uv), "from", round(best, 3))

    def is_lock(f):
        c = f.calc_center_median()
        return abs(c.x) < LOCK_X and c.y < LOCK_Y and c.z < SEAM_Z - 0.005

    lock = split_off(src, "ChestLock", is_lock)
    lid = split_off(src, "ChestLid", lambda f: not is_lock(f) and f.calc_center_median().z >= SEAM_Z)
    body = split_off(src, "ChestBody", lambda f: not is_lock(f) and f.calc_center_median().z < SEAM_Z)
    bpy.data.objects.remove(src, do_unlink=True)

    bpy.context.view_layer.update()
    bpts = np.array([v.co[:] for v in body.data.vertices if abs(v.co.z - SEAM_Z) < 0.02])
    bx0, by0 = bpts[:, 0].min(), bpts[:, 1].min()
    bx1, by1 = bpts[:, 0].max(), bpts[:, 1].max()
    log("seam footprint x", round(bx0, 3), round(bx1, 3), "y", round(by0, 3), round(by1, 3))

    def add_quads(o, quads, mat_index, uv_point=None):
        """Append quads (4 corners each, counter-clockwise seen from where they should face) to o."""
        bm = bmesh.new(); bm.from_mesh(o.data); uvl_ = bm.loops.layers.uv.active
        for q in quads:
            vs = [bm.verts.new(c) for c in q]
            f = bm.faces.new(vs); f.material_index = mat_index
            if uv_point is not None:
                for l in f.loops: l[uvl_].uv = uv_point
        bm.to_mesh(o.data); bm.free()

    # inside of the body: a dark box sunk 0.17 m below the rim, plus a rim ring over the ragged cut
    # the inside sits within the real walls (measured half way up, between the straps), not the band round the rim
    allv = np.array([v.co[:] for v in body.data.vertices])
    mid = allv[(allv[:, 2] > -0.14) & (allv[:, 2] < -0.06)]
    mid = allv[(allv[:, 2] > -0.2) & (allv[:, 2] < 0.0)]
    wx = mid[np.abs(mid[:, 1]) < 0.25][:, 0]; wyy = mid[np.abs(mid[:, 0]) < 0.38][:, 1]
    inset, depth, z = 0.03, 0.17, SEAM_Z + 0.002
    # the wall is the layer closest to the centre on each side (straps, band and brackets only stick out of it)
    ix0, ix1 = np.percentile(wx[wx < -0.3], 85) + inset, np.percentile(wx[wx > 0.3], 15) - inset
    iy0, iy1 = np.percentile(wyy[wyy < -0.15], 85) + inset, np.percentile(wyy[wyy > 0.15], 15) - inset
    sx0, sx1, sy0, sy1 = bx0, bx1, by0, by1   # the seam outline (for the lid underside)
    bx0, bx1, by0, by1 = ix0 - inset - 0.012, ix1 + inset + 0.012, iy0 - inset - 0.012, iy1 + inset + 0.012   # rim: just over the wall top
    log("walls x", round(ix0 - inset, 3), round(ix1 + inset, 3), "y", round(iy0 - inset, 3), round(iy1 + inset, 3))
    zb = z - depth
    P = lambda x, y, zz: Vector((x, y, zz))
    quads = [
        # rim ring (faces up)
        [P(bx0, by0, z), P(bx1, by0, z), P(ix1, iy0, z), P(ix0, iy0, z)],
        [P(bx1, by0, z), P(bx1, by1, z), P(ix1, iy1, z), P(ix1, iy0, z)],
        [P(bx1, by1, z), P(bx0, by1, z), P(ix0, iy1, z), P(ix1, iy1, z)],
        [P(bx0, by1, z), P(bx0, by0, z), P(ix0, iy0, z), P(ix0, iy1, z)],
        # inner walls (face inward) and floor (faces up)
        [P(ix0, iy0, z), P(ix1, iy0, z), P(ix1, iy0, zb), P(ix0, iy0, zb)],
        [P(ix1, iy0, z), P(ix1, iy1, z), P(ix1, iy1, zb), P(ix1, iy0, zb)],
        [P(ix1, iy1, z), P(ix0, iy1, z), P(ix0, iy1, zb), P(ix1, iy1, zb)],
        [P(ix0, iy1, z), P(ix0, iy0, z), P(ix0, iy0, zb), P(ix0, iy1, zb)],
        [P(ix0, iy0, zb), P(ix1, iy0, zb), P(ix1, iy1, zb), P(ix0, iy1, zb)],
    ]
    add_quads(body, quads, 1)
    # where the padlock hung: a flat patch of plain painted wall (the Meshy wall behind it isn't there)
    wy = -0.236   # just in front of the wall, behind the band
    add_quads(body, [[P(-LOCK_X - 0.01, wy, -0.19), P(LOCK_X + 0.01, wy, -0.19), P(LOCK_X + 0.01, wy, SEAM_Z - 0.004), P(-LOCK_X - 0.01, wy, SEAM_Z - 0.004)]],
              0, wall_uv)
    # lid underside: one dark panel just above the seam, facing down
    zl = SEAM_Z + 0.004
    add_quads(lid, [[P(bx0, by0, zl), P(bx0, by1, zl), P(bx1, by1, zl), P(bx1, by0, zl)]], 1)   # inside the walls: never shows when shut
    for o in (body, lid):
        bm = bmesh.new(); bm.from_mesh(o.data)
        bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=0.0005)
        bm.to_mesh(o.data); bm.free()
    log("body: rim + inside box", round(ix1 - ix0, 2), "x", round(iy1 - iy0, 2), "x", depth, "; lock patch; lid underside")

    # lock: both sides (it tumbles when it falls)
    bm = bmesh.new(); bm.from_mesh(lock.data)
    dup = bmesh.ops.duplicate(bm, geom=bm.faces[:])
    bmesh.ops.reverse_faces(bm, faces=[g for g in dup["geom"] if isinstance(g, bmesh.types.BMFace)])
    bm.to_mesh(lock.data); bm.free()

    # face +Y (forward in Unity): turn 180 about Z (the padlock is on -Y in the Meshy model); stand on the ground; game size
    pts = np.array([v.co[:] for o in (body, lid, lock) for v in o.data.vertices])
    lo, hi = pts.min(0), pts.max(0)
    k = CHEST_LENGTH / (hi[0] - lo[0])
    T = Matrix.Scale(k, 4) @ Matrix.Rotation(math.pi, 4, "Z") @ Matrix.Translation((-(lo[0] + hi[0]) / 2, -(lo[1] + hi[1]) / 2, -lo[2]))
    for o in (body, lid, lock): o.data.transform(T)
    # pivots: lid on the hinge (back top edge of the body, now -Y), lock at the shackle top
    hinge = T @ Vector((0, hi[1] - 0.02, SEAM_Z))
    shackle = T @ Vector((0, -0.29, SEAM_Z - 0.01))
    for o, p in ((lid, hinge), (lock, shackle), (body, Vector((0, 0, 0)))):
        o.data.transform(Matrix.Translation(-p)); o.location = p
    for o in (body, lid, lock):
        bpy.context.view_layer.objects.active = o
        o.select_set(True)
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))
        o.select_set(False)
    log("scale", round(k, 3), "hinge", tuple(round(x, 3) for x in hinge), "lock pivot", tuple(round(x, 3) for x in shackle))
    import shutil
    shutil.rmtree(tmp, ignore_errors=True)
    return body, lid, lock


def build_medkit():
    """Red-and-white first-aid box with a carry handle."""
    white = mat("ZR_MedWhite", (0.8, 0.78, 0.74), 0.5)
    red = mat("ZR_MedRed", (0.62, 0.04, 0.03), 0.45)
    dark = mat("ZR_DarkMetal", (0.06, 0.065, 0.07), 0.55, 0.8)
    parts = []

    def cube(size, loc, m, bevel=0.0):
        bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
        o = bpy.context.active_object; o.scale = size
        bpy.ops.object.transform_apply(scale=True)
        if bevel:
            b = o.modifiers.new("b", "BEVEL"); b.width = bevel; b.segments = 2
            bpy.ops.object.modifier_apply(modifier=b.name)
        o.data.materials.append(m); parts.append(o)
        return o
    L, W, H = 0.34, 0.12, 0.24     # stands on its long thin side like a case
    cube((L, W, H), (0, 0, H / 2), white, 0.02)
    cube((L + 0.004, W + 0.004, 0.025), (0, 0, H * 0.62), red)                 # red band
    for s in (-1, 1):                                                           # crosses on both faces
        cube((0.1, 0.006, 0.032), (0, s * (W / 2 + 0.002), H * 0.38), red)
        cube((0.032, 0.006, 0.1), (0, s * (W / 2 + 0.0035), H * 0.38), red)   # a hair in front: no flicker where the bars cross
    cube((0.15, 0.025, 0.02), (0, 0, H + 0.045), dark, 0.006)                   # handle bar
    for s in (-1, 1): cube((0.02, 0.025, 0.05), (s * 0.065, 0, H + 0.02), dark)
    bpy.ops.object.select_all(action="DESELECT")
    for o in parts: o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    o = bpy.context.active_object; o.name = "ZR_Medkit"
    bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.cube_project(cube_size=2.0); bpy.ops.object.mode_set(mode="OBJECT")
    return o


def preview(objs, name, lid=None, lock=None):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"; sc.cycles.samples = 24
    w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.4, 0.42, 0.46, 1)
    if not any(o.type == "LIGHT" for o in sc.objects):
        sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sc.collection.objects.link(sun)
        sun.data.energy = 3.0; sun.rotation_euler = (0.8, 0.2, 0.7)
    cam = sc.camera
    if cam is None:
        cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    lo = Vector([min(p[i] for p in pts) for i in range(3)]); hi = Vector([max(p[i] for p in pts) for i in range(3)])
    ctr = (lo + hi) / 2; s = max(hi - lo)
    cam.location = ctr + Vector((0.9, 1.6, 1.2)).normalized() * s * 2.0
    cam.rotation_euler = (ctr - cam.location).to_track_quat("-Z", "Y").to_euler()
    sc.render.resolution_x, sc.render.resolution_y = 640, 480
    os.makedirs(PREVIEW, exist_ok=True)
    sc.render.filepath = os.path.join(PREVIEW, name + ".png"); bpy.ops.render.render(write_still=True)
    if lid is not None:
        lid.rotation_euler = (math.radians(105), 0, 0)   # open about the hinge (X): the front rises, the lid swings back (-Y)
        lock.location = lock.location + Vector((0.0, 0.25, -0.0)); lock.rotation_euler = (0.4, 0.8, 0.3)
        lock.location.z = 0.03
        sc.render.filepath = os.path.join(PREVIEW, name + "_open.png"); bpy.ops.render.render(write_still=True)
        lid.rotation_euler = (0, 0, 0)


def main():
    os.makedirs(OUT, exist_ok=True)
    body, lid, lock = build_chest()
    export([body, lid, lock], os.path.join(OUT, "ZR_Chest.fbx"))
    if PREVIEW: preview([body, lid, lock], "chest", lid, lock)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    kit = build_medkit()
    export([kit], os.path.join(OUT, "ZR_Medkit.fbx"))
    if PREVIEW: preview([kit], "medkit")


main()
