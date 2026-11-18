"""Frag grenade (2026-11-18): an M67-style hand grenade for the throwable grenade.

    blender -b --factory-startup -P Blender/build_grenade.py -- [--preview DIR] [--write]

Output (with --write) in Assets/Models/2026-11-18_Grenade/:
  Grenade.fbx   Grenade_Body (egg-shaped body + fuse head), Grenade_Spoon (the lever that flies off on the throw),
                Grenade_Pin (pull ring + pin, pulled when cooking starts). Origin = body centre. Real size: 6.4 cm wide,
                9 cm tall with the fuse. Up = +Z in Blender (Unity +Y).
  Grenade_Albedo/Normal/MetalSmooth.png   olive drab paint, yellow band, worn edges, cast seam (512, body UVs)
Materials: Grenade (body), GrenadeMetal (fuse head, spoon, pin; plain grey steel in Unity).
"""
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fortress_base as fb   # noqa: E402  (texture helpers)

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
WRITE = "--write" in argv
PREVIEW = argv[argv.index("--preview") + 1] if "--preview" in argv else None
OUT = "C:/Mian Sarmad/Unity/Zombie-Rush/Zombie Rush/Assets/Models/2026-11-18_Grenade/"
R = 0.032          # body radius
H = 0.040          # body half height (egg)


def log(*a):
    print("[grenade]", *a, flush=True)


def tex_body(n=512):
    """UV: u around the body, v from bottom (0) to top (1)."""
    yy, xx = np.mgrid[0:n, 0:n] / n
    v = 1.0 - yy
    big, fine = fb.fbm(n, 2.0, 2, 201), fb.fbm(n, 1.0, 40, 202)
    col = np.stack([0.24, 0.27, 0.16]) * (0.85 + 0.25 * big[..., None]) * (0.9 + 0.2 * fine[..., None])
    band = np.abs(v - 0.78) < 0.025                                  # yellow identification band near the top
    col = np.where(band[..., None], np.stack([0.75, 0.62, 0.12]) * (0.85 + 0.2 * fine[..., None]), col)
    seam = np.abs(v - 0.5) < 0.006                                   # cast seam round the middle
    col = np.where(seam[..., None], col * 0.7, col)
    worn = np.clip((fb.fbm(n, 1.4, 14, 203) - 0.72) * 4, 0, 1) * (0.4 + 0.6 * np.abs(v - 0.5) * 2)
    col = col * (1 - worn[..., None]) + np.stack([0.40, 0.40, 0.38]) * worn[..., None]
    h = 0.3 * fine - 0.8 * seam - 0.2 * worn
    nrm = fb.normal_from_height(fb.wrap_blur(h.astype(float), 1), 2.0, wrap=True)
    metal = 0.1 + 0.8 * worn
    smooth = 0.3 + 0.3 * worn
    return col, nrm, smooth, metal


def body():
    """Egg body (UV sphere squashed toward the bottom) with a fuse well on top; cylindrical UVs."""
    bm = bmesh.new(); uv = bm.loops.layers.uv.new("UVMap")
    segs, rings = 32, 18
    verts = []
    for j in range(rings + 1):
        a = math.pi * j / rings                  # 0 at the top
        z = math.cos(a); r = math.sin(a)
        z = z * H * (1.0 if z > 0 else 0.92)
        rr = r * R * (1.0 - 0.06 * max(0.0, math.cos(a)))  # a little narrower toward the top
        row = []
        for i in range(segs):
            t = 2 * math.pi * i / segs
            row.append(bm.verts.new((rr * math.cos(t), rr * math.sin(t), z)))
        verts.append(row)
    for j in range(rings):
        for i in range(segs):
            i2 = (i + 1) % segs
            f = bm.faces.new((verts[j][i], verts[j][i2], verts[j + 1][i2], verts[j + 1][i]))
            for l, (jj, ii) in zip(f.loops, ((j, i), (j, i + 1), (j + 1, i + 1), (j + 1, i))):
                l[uv].uv = (ii / segs, 1.0 - jj / rings)
            f.material_index = 0
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    for f in bm.faces: f.smooth = True
    # fuse head: a short steel cylinder and a cap on top
    def cyl(z0, z1, r, seg=16):
        res = bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r, radius2=r, depth=z1 - z0,
                                    matrix=Matrix.Translation((0, 0, (z0 + z1) / 2)))
        for f in {f for v in res["verts"] for f in v.link_faces}:
            f.material_index = 1; f.smooth = False
            for l in f.loops: l[uv].uv = (0.5 + l.vert.co.x * 4, 0.5 + l.vert.co.z * 4)
    cyl(H - 0.004, H + 0.010, 0.0115)
    cyl(H + 0.010, H + 0.019, 0.0135)
    cyl(H + 0.019, H + 0.024, 0.0095)
    me = bpy.data.meshes.new("Grenade_Body"); bm.to_mesh(me); bm.free()
    return me


def spoon():
    """The lever: a curved strip from the fuse head down the side, hugging the body."""
    bm = bmesh.new(); uv = bm.loops.layers.uv.new("UVMap")
    w = 0.0085; pts = []
    def body_r(z):                                # the egg's radius at height z (top half: H, bottom: 0.92 H)
        hz = H if z > 0 else H * 0.92
        return R * math.sqrt(max(0.0, 1 - (z / hz) ** 2))
    top = H + 0.021
    pts = [(0.004, top), (0.010, top), (0.0145, top - 0.002), (0.0150, top - 0.012), (0.0155, H - 0.002)]
    for k in range(1, 11):                        # then down the side, 2 mm off the body
        z = H - 0.002 - k * (H + 0.004) / 10
        pts.append((max(0.0155, body_r(z) + 0.0022), z))
    rows = []
    for (x, z) in pts:
        rows.append([bm.verts.new((x, -w / 2, z)), bm.verts.new((x, w / 2, z)),
                     bm.verts.new((x + 0.0012, w / 2, z)), bm.verts.new((x + 0.0012, -w / 2, z))])
    for k in range(len(rows) - 1):
        for e in range(4):
            e2 = (e + 1) % 4
            bm.faces.new((rows[k][e], rows[k][e2], rows[k + 1][e2], rows[k + 1][e]))
    bm.faces.new(rows[0][::-1]); bm.faces.new(rows[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    for f in bm.faces:
        f.material_index = 1
        for l in f.loops: l[uv].uv = (l.vert.co.x * 10, l.vert.co.z * 10)
    me = bpy.data.meshes.new("Grenade_Spoon"); bm.to_mesh(me); bm.free()
    return me


def pin():
    """Pull ring on the side of the fuse head, and the pin through it."""
    bm = bmesh.new(); uv = bm.loops.layers.uv.new("UVMap")
    ring_c = Vector((-0.004, -0.022, H + 0.012))
    seg, mseg, Rr, rr = 20, 6, 0.0115, 0.0012
    grid = []
    for i in range(seg):
        a = 2 * math.pi * i / seg
        c = ring_c + Vector((0, math.cos(a) * Rr, math.sin(a) * Rr))
        rowv = []
        for j in range(mseg):
            b = 2 * math.pi * j / mseg
            n = Vector((math.cos(b), math.cos(a) * math.sin(b), math.sin(a) * math.sin(b)))
            rowv.append(bm.verts.new(c + n * rr))
        grid.append(rowv)
    for i in range(seg):
        for j in range(mseg):
            bm.faces.new((grid[i][j], grid[(i + 1) % seg][j], grid[(i + 1) % seg][(j + 1) % mseg], grid[i][(j + 1) % mseg]))
    res = bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=0.0012, radius2=0.0012, depth=0.03,
                                matrix=Matrix.Translation((-0.004, -0.004, H + 0.012)) @ Matrix.Rotation(math.radians(90), 4, "X"))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    for f in bm.faces:
        f.material_index = 1
        for l in f.loops: l[uv].uv = (l.vert.co.y * 10, l.vert.co.z * 10)
    me = bpy.data.meshes.new("Grenade_Pin"); bm.to_mesh(me); bm.free()
    return me


def main():
    fb.reset()
    col, nrm, sm, met = tex_body()
    m_body = fb.material("Grenade", fb.image_from(col, "Grenade"), rough=0.5)
    m_metal = fb.material("GrenadeMetal", color=(0.30, 0.31, 0.29), rough=0.4, metal=0.85)
    objs = []
    for me in (body(), spoon(), pin()):
        me.materials.append(m_body); me.materials.append(m_metal)
        o = bpy.data.objects.new(me.name, me); bpy.context.scene.collection.objects.link(o); objs.append(o)
    log("parts", [(o.name, len(o.data.polygons)) for o in objs], "size", tuple(round(d, 3) for d in objs[0].dimensions))
    if PREVIEW:
        os.makedirs(PREVIEW, exist_ok=True)
        sc = bpy.context.scene
        sc.render.engine = "BLENDER_EEVEE"; sc.render.resolution_x = 900; sc.render.resolution_y = 900
        w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
        w.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.58, 0.62, 1)
        sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sc.collection.objects.link(sun)
        sun.rotation_euler = (math.radians(45), 0, math.radians(30)); sun.data.energy = 3
        cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
        cam.data.lens = 85
        for name, pos in {"front": (0.0, -0.32, 0.06), "side": (0.3, -0.12, 0.08)}.items():
            cam.location = pos; cam.rotation_euler = (Vector((0, 0, 0.005)) - Vector(pos)).to_track_quat("-Z", "Y").to_euler()
            sc.render.filepath = os.path.join(PREVIEW, f"Grenade_{name}.png"); bpy.ops.render.render(write_still=True)
    if WRITE:
        os.makedirs(OUT, exist_ok=True)
        fb.save(col, OUT + "Grenade_Albedo.png")
        fb.save(nrm, OUT + "Grenade_Normal.png", srgb=False)
        fb.save(np.stack([met, met, met, np.clip(sm, 0, 1)], -1), OUT + "Grenade_MetalSmooth.png", srgb=False)
        bpy.ops.object.select_all(action="DESELECT")
        for o in objs: o.select_set(True)
        bpy.ops.export_scene.fbx(filepath=OUT + "Grenade.fbx", use_selection=True, object_types={"MESH"},
                                 apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                                 axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                                 path_mode="STRIP", embed_textures=False, mesh_smooth_type="FACE")
        log("exported", OUT + "Grenade.fbx")


main()
