"""Fortress room props (2026-11-17): steel crate stacks (replace the gun benches) and caged wall lamps (replace the
lantern posts).

    blender -b --factory-startup -P Blender/build_fortress_props.py -- [--preview DIR] [--write]

Output (with --write) in Assets/Models/2026-11-17_FortressHaven/:
  FortressProps.fbx   CrateStack (two crates below, one long rifle crate on top; its lid top sits at 0.92 m, the old
                      bench top, so BaseFurnishTools' guns and ammo boxes land on it), WallLamp (bulkhead lamp: back
                      plate, arm, glass dome, steel cage; origin on the wall, light side facing -Y = Unity +Z)
  Crate_Albedo/Normal/MetalSmooth.png   olive painted steel, worn edges, yellow stencil band, rivets (1 tile = 1 m)
Materials: Crate (new), Room_Steel and Room_LampGlow (shared with the room, made by fortress_base.py).
Placed by FortressBaseTools after BaseFurnishTools has furnished the room.
"""
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fortress_base as fb   # noqa: E402  (texture helpers; its main() only runs as a script)

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
WRITE = "--write" in argv
PREVIEW = argv[argv.index("--preview") + 1] if "--preview" in argv else None
OUT = fb.OUT
TOP = 0.92            # lid top of the stack = old bench top
DEPTH = 0.74          # front-to-back (Blender Y), matches the bench footprint
LENGTH = 1.88         # along the wall (Blender X)


def log(*a):
    print("[props]", *a, flush=True)


# ------------------------------------------------------------------ crate texture

def tex_crate(n=1024):
    yy, xx = np.mgrid[0:n, 0:n] / n
    big, mid, fine = fb.fbm(n, 2.2, 2, 91), fb.fbm(n, 1.6, 8, 92), fb.fbm(n, 1.0, 50, 93)
    col = np.stack([0.20, 0.24, 0.15]) * (0.8 + 0.3 * big[..., None]) * (0.9 + 0.2 * fine[..., None])
    # yellow stencil band across the middle, broken by wear
    band = (np.abs(yy - 0.5) < 0.035) & (fb.fbm(n, 1.2, 30, 94) > 0.35)
    col = np.where(band[..., None], np.stack([0.62, 0.50, 0.12]) * (0.85 + 0.2 * fine[..., None]), col)
    # worn paint: bare steel showing through, scratches
    worn = np.clip((fb.fbm(n, 1.3, 12, 95) - 0.7) * 4, 0, 1)
    scratch = np.clip((np.abs(np.sin((xx * 7 + yy * 31 + fine * 0.6) * math.tau * 3)) - 0.985) * 60, 0, 1) * (mid > 0.5)
    bare = np.clip(worn + scratch, 0, 1)
    col = col * (1 - bare[..., None]) + np.stack([0.45, 0.45, 0.43]) * bare[..., None]
    rust = np.clip((fb.fbm(n, 2.4, 3, 96) - 0.72) * 4, 0, 1)
    col = col * (1 - 0.7 * rust[..., None]) + np.stack([0.30, 0.15, 0.07]) * 0.7 * rust[..., None]
    # rivets on a 0.125 m grid near the tile edges
    rx, ry = (xx * 8) % 1.0 - 0.5, (yy * 8) % 1.0 - 0.5
    rivet = (rx * rx + ry * ry < 0.012) & ((xx < 0.07) | (xx > 0.93) | (yy < 0.07) | (yy > 0.93))
    col = np.where(rivet[..., None], col * 1.25, col)
    h = 0.3 * fine - 0.25 * bare + 1.0 * rivet - 0.2 * rust
    nrm = fb.normal_from_height(fb.wrap_blur(h.astype(float), 1), 3.0, wrap=True)
    metal = 0.1 + 0.75 * bare
    smooth = 0.32 + 0.25 * bare - 0.2 * rust
    return col, nrm, smooth, metal


# ------------------------------------------------------------------ mesh builder

class Mesh:
    def __init__(self, name):
        self.bm = bmesh.new(); self.uv = self.bm.loops.layers.uv.new("UVMap"); self.name = name

    def box(self, c, size, mat, yaw=0.0, bevel=0.0, tile=1.0):
        # built in its own bmesh (bevel replaces the cube's verts), then appended to this one
        m = Matrix.Translation(c) @ Matrix.Rotation(yaw, 4, "Z") @ Matrix.Diagonal((*size, 1))
        tmp = bmesh.new()
        bmesh.ops.create_cube(tmp, size=1.0, matrix=m)
        if bevel > 0:
            bmesh.ops.bevel(tmp, geom=tmp.edges[:] + tmp.verts[:], offset=bevel, segments=1, affect="EDGES", profile=0.5)
        uvl = tmp.loops.layers.uv.new("UVMap")
        for f in tmp.faces:
            f.material_index = mat
            n = f.normal; ax = int(np.argmax(np.abs([n.x, n.y, n.z])))
            for l in f.loops:
                co = l.vert.co
                u, v = ((co.x, co.y) if ax == 2 else (co.y, co.z) if ax == 0 else (co.x, co.z))
                l[uvl].uv = (u / tile, v / tile)
        me = bpy.data.meshes.new("_tmp"); tmp.to_mesh(me); tmp.free()
        self.bm.from_mesh(me); bpy.data.meshes.remove(me)
        return []

    def cyl(self, a, b, r, mat, seg=8, tile=1.0):
        a, b = Vector(a), Vector(b); d = b - a
        rot = d.to_track_quat("Z", "Y").to_matrix().to_4x4()
        m = Matrix.Translation((a + b) / 2) @ rot
        res = bmesh.ops.create_cone(self.bm, cap_ends=True, segments=seg, radius1=r, radius2=r, depth=d.length, matrix=m)
        faces = list({f for v in res["verts"] for f in v.link_faces})
        for f in faces: f.material_index = mat
        self._uv(faces, tile)
        return faces

    def _uv(self, faces, tile):
        for f in faces:
            if not f.is_valid: continue
            n = f.normal; ax = int(np.argmax(np.abs([n.x, n.y, n.z])))
            for l in f.loops:
                co = l.vert.co
                u, v = ((co.x, co.y) if ax == 2 else (co.y, co.z) if ax == 0 else (co.x, co.z))
                l[self.uv].uv = (u / tile, v / tile)

    def finish(self, mats):
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces[:])
        me = bpy.data.meshes.new(self.name); self.bm.to_mesh(me); self.bm.free()
        for m in mats: me.materials.append(m)
        o = bpy.data.objects.new(self.name, me); bpy.context.scene.collection.objects.link(o)
        return o


def crate(m, cx, cy, z0, L, D, H, yaw):
    """One military steel crate: body, lid lip, corner guards, skids, side handles, front latches."""
    CR, ST = 0, 1
    rot = Matrix.Rotation(yaw, 3, "Z")
    def P(x, y, z): return Vector((cx, cy, z0)) + rot @ Vector((x, y, z))
    m.box(P(0, 0, H * 0.5 - 0.01), (L - 0.04, D - 0.04, H - 0.06), CR, yaw, bevel=0.012)            # body
    m.box(P(0, 0, H - 0.035), (L, D, 0.07), CR, yaw, bevel=0.01)                                   # lid with lip
    for sx in (-1, 1):                                                                              # corner guards
        for sy in (-1, 1):
            m.box(P(sx * (L / 2 - 0.035), sy * (D / 2 - 0.035), H * 0.5), (0.075, 0.075, H - 0.02), ST, yaw, bevel=0.006)
    for sx in (-0.33, 0.33):                                                                        # skids
        m.box(P(sx * L, 0, 0.02), (0.08, D - 0.02, 0.04), ST, yaw)
    for sx in (-1, 1):                                                                              # end handles
        hx = sx * (L / 2 + 0.03)
        m.cyl(P(hx, -0.09, H * 0.62), P(hx, 0.09, H * 0.62), 0.012, ST)
        m.cyl(P(hx - sx * 0.03, -0.09, H * 0.62), P(hx, -0.09, H * 0.62), 0.011, ST)
        m.cyl(P(hx - sx * 0.03, 0.09, H * 0.62), P(hx, 0.09, H * 0.62), 0.011, ST)
    for sx in (-0.3, 0.3):                                                                          # front latches
        m.box(P(sx * L, -D / 2 - 0.012, H - 0.09), (0.06, 0.024, 0.09), ST, yaw, bevel=0.004)


def build_stack(mat_crate, mat_steel):
    m = Mesh("CrateStack")
    h0 = 0.48
    crate(m, -0.475, 0.0, 0.0, 0.93, DEPTH, h0, math.radians(1.2))
    crate(m, 0.475, 0.005, 0.0, 0.93, DEPTH - 0.02, h0, math.radians(-0.8))
    crate(m, 0.0, -0.01, h0, LENGTH, DEPTH - 0.04, TOP - h0, math.radians(0.4))     # long rifle crate on top
    return m.finish([mat_crate, mat_steel])


def build_lamp(mat_steel, mat_glow):
    """Bulkhead cage lamp; origin on the wall, lamp out along -Y."""
    m = Mesh("WallLamp")
    ST, GL = 0, 1
    m.box((0, -0.015, 0), (0.16, 0.03, 0.22), ST, bevel=0.006)                    # back plate
    m.box((0, -0.075, 0.0), (0.05, 0.09, 0.05), ST)                               # arm
    m.cyl((0, -0.115, 0), (0, -0.135, 0), 0.085, ST, seg=16)                      # collar
    # glass dome: a squashed sphere facing out
    res = bmesh.ops.create_uvsphere(m.bm, u_segments=16, v_segments=8, radius=0.075,
                                    matrix=Matrix.Translation((0, -0.17, 0)) @ Matrix.Rotation(math.radians(90), 4, "X") @ Matrix.Diagonal((1, 1, 0.9, 1)))
    faces = list({f for v in res["verts"] for f in v.link_faces})
    for f in faces: f.material_index = GL
    m._uv(faces, 0.2)
    # cage: four ribs from the collar over the dome, and a ring
    for a in range(4):
        ang = a * math.pi / 2 + math.pi / 4
        x, z = math.cos(ang) * 0.088, math.sin(ang) * 0.088
        m.cyl((x, -0.13, z), (x * 0.6, -0.23, z * 0.6), 0.006, ST, seg=6)
        m.cyl((x * 0.6, -0.23, z * 0.6), (0, -0.255, 0), 0.006, ST, seg=6)
    for i in range(12):
        a0, a1 = i * math.tau / 12, (i + 1) * math.tau / 12
        m.cyl((math.cos(a0) * 0.084, -0.19, math.sin(a0) * 0.084), (math.cos(a1) * 0.084, -0.19, math.sin(a1) * 0.084), 0.005, ST, seg=5)
    return m.finish([mat_steel, mat_glow])


def preview(objs, outdir):
    os.makedirs(outdir, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"; sc.render.resolution_x = 1100; sc.render.resolution_y = 700
    w = sc.world or bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.5, 0.55, 0.6, 1); w.node_tree.nodes["Background"].inputs[1].default_value = 0.7
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sc.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(50), 0, math.radians(30)); sun.data.energy = 3
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
    for name, (pos, tgt, lens) in {"stack": ((1.9, -2.4, 1.5), (0, 0, 0.5), 35), "lamp": ((2.55, -0.75, 0.15), (2.2, -0.14, 0), 50)}.items():
        cam.data.lens = lens
        cam.location = pos; cam.rotation_euler = (Vector(tgt) - Vector(pos)).to_track_quat("-Z", "Y").to_euler()
        sc.render.filepath = os.path.join(outdir, f"Props_{name}.png"); bpy.ops.render.render(write_still=True)


def main():
    fb.reset()
    cc, cn, csm, cmet = tex_crate()
    sc_, sn, ssm, smet = fb.tex_steel()
    mat_crate = fb.material("Crate", fb.image_from(cc, "Crate"), rough=0.6)
    mat_steel = fb.material("Room_Steel", fb.image_from(sc_, "Room_Steel"), rough=0.6)
    mat_glow = fb.material("Room_LampGlow", color=(1.0, 0.85, 0.6), emit=(1.0, 0.8, 0.55, 12.0))
    stack = build_stack(mat_crate, mat_steel)
    lamp = build_lamp(mat_steel, mat_glow)
    lamp.location.x = 2.2
    log(f"CrateStack {len(stack.data.polygons)} faces, dims {tuple(round(d, 2) for d in stack.dimensions)}; "
        f"WallLamp {len(lamp.data.polygons)} faces, dims {tuple(round(d, 3) for d in lamp.dimensions)}")
    if PREVIEW:
        preview([stack, lamp], PREVIEW)
    if WRITE:
        lamp.location.x = 0
        os.makedirs(OUT, exist_ok=True)
        fb.save(cc, OUT + "Crate_Albedo.png")
        fb.save(cn, OUT + "Crate_Normal.png", srgb=False)
        fb.save(np.stack([cmet, cmet, cmet, np.clip(csm, 0, 1)], -1), OUT + "Crate_MetalSmooth.png", srgb=False)
        bpy.ops.object.select_all(action="DESELECT")
        for o in (stack, lamp): o.select_set(True)
        bpy.ops.export_scene.fbx(filepath=OUT + "FortressProps.fbx", use_selection=True, object_types={"MESH"},
                                 apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                                 axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                                 path_mode="STRIP", embed_textures=False, mesh_smooth_type="FACE")
        log("exported", OUT + "FortressProps.fbx")


main()
