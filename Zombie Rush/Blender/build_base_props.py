"""Zombie Rush safe-base props (2026-11-15), built from code in Blender.

Run:  blender -b --factory-startup -P Blender/build_base_props.py -- [--preview DIR]
Exports to Assets/Models/2026-11-15_BaseProps/:
  - ZR_SteelBench.fbx   steel workbench for against a wall, 1.9 x 0.75 m, top at 0.92 m, front = Unity +Z
  - ZR_LanternPost.fbx  wooden post with an arm and a hanging open iron-cage oil lantern; the flame is a particle
                        system in Unity, at the child object "FlameSocket" (a tiny hidden mesh)
  - ZR_ArmorVest.fbx    plate-carrier vest lying flat (as on a table), ~0.12 m tall
  - ZR_Ammo_9mm / ZR_Ammo_Shotgun / ZR_Ammo_Rifle .fbx + .png: the user's Meshy ammo boxes, cut to ~1.5k triangles,
    real-ish sizes, own 512 px textures (sources: the zips in C:\\Mian Sarmad\\Unity)
Props stand on the ground at the origin, face +Y in Blender (forward in Unity), metres, cube-projected UVs
(1 tile = 2 m); material names ZR_* are mapped to the city props' Unity materials (CityPropsTools).
"""
import math
import os
import sys
import zipfile

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
PREVIEW = argv[argv.index("--preview") + 1] if "--preview" in argv else None
HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
OUT = os.path.join(PROJECT, "Assets", "Models", "2026-11-15_BaseProps")
SRC = os.path.normpath(os.path.join(PROJECT, "..", ".."))       # C:\Mian Sarmad\Unity (the user's zips)
TILE = 2.0
# Meshy ammo boxes: (output name, zip, longest side in metres, target triangles)
AMMO = [("ZR_Ammo_9mm", "9mm ammo.zip", 0.20, 1500),
        ("ZR_Ammo_Shotgun", "shotgun ammo.zip", 0.22, 1500),
        ("ZR_Ammo_Rifle", "rifle ammo.zip", 0.30, 1500)]

MATS = {  # same names (and look) as Blender/build_city_props.py, so Unity maps them to the city's materials
    "ZR_Wood": ((0.27, 0.16, 0.08), 0.0, 0.8),
    "ZR_WoodOld": ((0.19, 0.15, 0.11), 0.0, 0.9),
    "ZR_DarkMetal": ((0.06, 0.065, 0.07), 0.8, 0.55),
    "ZR_Rust": ((0.24, 0.11, 0.05), 0.6, 0.9),
    "ZR_Embers": ((0.3, 0.06, 0.01), 0.0, 0.9),
    "ZR_CanvasDark": ((0.08, 0.09, 0.06), 0.0, 0.95),
    "ZR_Canvas": ((0.17, 0.18, 0.11), 0.0, 0.95),
    "ZR_ArmyGreen": ((0.09, 0.13, 0.07), 0.4, 0.7),
    "ZR_Wire": ((0.12, 0.12, 0.12), 0.9, 0.6),
    "ZR_SteelPlate": ((0.21, 0.22, 0.23), 0.55, 0.55),
}


def log(*a):
    print("[base]", *a, flush=True)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"


def mat(name):
    m = bpy.data.materials.get(name)
    if m:
        return m
    col, metal, rough = MATS[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (*col, 1)
    bsdf.inputs["Metallic"].default_value = metal
    bsdf.inputs["Roughness"].default_value = rough
    m.diffuse_color = (*col, 1)
    return m


def box(name, size, loc=(0, 0, 0), material="ZR_Wood", bevel=0.0, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    bpy.ops.object.transform_apply(scale=True)
    if bevel > 0:
        b = o.modifiers.new("bevel", "BEVEL"); b.width = bevel; b.segments = 1; b.limit_method = "ANGLE"
        bpy.ops.object.modifier_apply(modifier=b.name)
    o.data.materials.append(mat(material))
    return o


def cyl(name, r, depth, loc=(0, 0, 0), rot=(0, 0, 0), verts=10, material="ZR_DarkMetal", cap=True, r2=None):
    if r2 is None:
        bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=loc, rotation=rot,
                                            end_fill_type="NGON" if cap else "NOTHING")
    else:
        bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r, radius2=r2, depth=depth, location=loc, rotation=rot,
                                        end_fill_type="NGON" if cap else "NOTHING")
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(mat(material))
    return o


def torus(name, R, r, loc, rot=(0, 0, 0), material="ZR_DarkMetal"):
    bpy.ops.mesh.primitive_torus_add(major_radius=R, minor_radius=r, major_segments=12, minor_segments=5, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(mat(material))
    return o


def join(objs, name):
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    bpy.context.scene.cursor.location = (0, 0, 0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    return o


def finish(o, smooth_angle=35):
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.0003)
    bpy.ops.uv.cube_project(cube_size=TILE, correct_aspect=True, clip_to_bounds=False, scale_to_bounds=False)
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(smooth_angle))
    return o


def tris(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def export(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    path = os.path.join(OUT, name + ".fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"},
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             mesh_smooth_type="FACE", use_mesh_modifiers=True, path_mode="STRIP",
                             embed_textures=False, add_leaf_bones=False, bake_anim=False)
    log(name, "tris", sum(tris(o) for o in objs), "size", tuple(round(x, 3) for x in size_of(objs)))


def size_of(objs):
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    return Vector([max(p[i] for p in pts) - min(p[i] for p in pts) for i in range(3)])


def jitter(o, amount, seed):
    import random
    rnd = random.Random(seed)
    for v in o.data.vertices:
        v.co += Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1))) * amount


# ---------------------------------------------------------------- steel workbench (user: "steel style", not wooden)

BENCH_TOP = 0.92


def steel_bench():
    """Military steel workbench for against a wall: grey steel top plate with a folded front lip and a back
    upstand, olive painted square-tube frame, lower shelf, a two-drawer unit on the right. Front = +Y."""
    L, W, H, T = 1.9, 0.75, BENCH_TOP, 0.03
    parts = []
    parts.append(box("top", (L, W, T), (0, 0, H - T / 2), "ZR_SteelPlate", bevel=0.004))
    parts.append(box("lip", (L, 0.012, 0.06), (0, W / 2 - 0.006, H - 0.03), "ZR_SteelPlate"))           # folded front edge
    parts.append(box("upstand", (L, 0.025, 0.14), (0, -W / 2 + 0.0125, H + 0.07), "ZR_SteelPlate", bevel=0.003))
    lx, ly, tw = L / 2 - 0.04, W / 2 - 0.04, 0.05
    for sx in (-1, 1):
        for sy in (-1, 1):
            parts.append(box(f"leg{sx}{sy}", (tw, tw, H - T), (sx * lx, sy * ly, (H - T) / 2), "ZR_ArmyGreen", bevel=0.004))
            parts.append(box(f"foot{sx}{sy}", (0.08, 0.08, 0.015), (sx * lx, sy * ly, 0.0075), "ZR_DarkMetal"))
    for z in (H - T - 0.03, 0.2):                    # frame rails under the top and round the shelf
        for sy in (-1, 1):
            parts.append(box(f"railL{z}{sy}", (2 * lx, 0.03, 0.05), (0, sy * ly, z), "ZR_ArmyGreen"))
        for sx in (-1, 1):
            parts.append(box(f"railS{z}{sx}", (0.03, 2 * ly, 0.05), (sx * lx, 0, z), "ZR_ArmyGreen"))
    parts.append(box("shelf", (2 * lx - 0.04, 2 * ly - 0.02, 0.012), (0, 0, 0.23), "ZR_SteelPlate"))
    # two-drawer unit hanging under the top, right side
    dx, dw = lx - 0.28, 0.5
    parts.append(box("drawerbox", (dw, 2 * ly - 0.04, 0.3), (dx, 0, H - T - 0.17), "ZR_ArmyGreen", bevel=0.004))
    for k, z in enumerate((H - T - 0.095, H - T - 0.245)):
        parts.append(box(f"drawer{k}", (dw - 0.03, 0.012, 0.13), (dx, ly - 0.01, z), "ZR_ArmyGreen", bevel=0.003))
        parts.append(box(f"handle{k}", (0.16, 0.02, 0.018), (dx, ly + 0.012, z + 0.02), "ZR_DarkMetal"))
    o = join(parts, "ZR_SteelBench")
    return [finish(o)]


# ---------------------------------------------------------------- lantern post (open iron cage: the flame shows)

LANTERN_ARM = 0.55     # metres out along +Y
LANTERN_TOP = 2.05     # arm height
LANTERN_SCALE = 1.4


def lantern_post():
    parts = []
    parts.append(box("post", (0.12, 0.12, LANTERN_TOP + 0.1), (0, 0, (LANTERN_TOP + 0.1) / 2), "ZR_WoodOld", bevel=0.01))
    parts.append(box("footA", (0.7, 0.1, 0.08), (0, 0, 0.04), "ZR_WoodOld", bevel=0.01))
    parts.append(box("footB", (0.1, 0.7, 0.08), (0, 0, 0.04), "ZR_WoodOld", bevel=0.01))
    parts.append(box("arm", (0.07, LANTERN_ARM + 0.12, 0.08), (0, (LANTERN_ARM + 0.12) / 2 - 0.06, LANTERN_TOP), "ZR_WoodOld", bevel=0.008))
    # diagonal brace from the post up to the arm (rises toward +Y)
    brace = box("brace", (0.05, 0.40, 0.05), (0, 0, 0), "ZR_WoodOld")
    from mathutils import Matrix
    R = Matrix.Rotation(math.radians(41), 3, "X")   # baked into the vertices (an object rotation got applied about the origin)
    for v in brace.data.vertices:
        v.co = R @ v.co + Vector((0, 0.19, LANTERN_TOP - 0.15))
    parts.append(brace)
    # hook and a short chain
    hy = LANTERN_ARM
    parts.append(cyl("hook", 0.008, 0.08, (0, hy, LANTERN_TOP - 0.08), material="ZR_DarkMetal", verts=6))
    for k in range(3):
        parts.append(torus(f"link{k}", 0.016, 0.004, (0, hy, LANTERN_TOP - 0.13 - k * 0.028),
                           rot=(0, math.radians(90 * (k % 2)), 0), material="ZR_DarkMetal"))
    # lantern: handle ring, cap, top plate, 6 cage bars, oil bowl, wick glow
    first_lantern = len(parts)
    top = LANTERN_TOP - 0.2
    parts.append(torus("handle", 0.045, 0.006, (0, hy, top + 0.02), rot=(math.radians(90), 0, 0), material="ZR_DarkMetal"))
    parts.append(cyl("cap", 0.105, 0.07, (0, hy, top - 0.035), verts=8, material="ZR_DarkMetal", r2=0.03))
    parts.append(cyl("plateT", 0.1, 0.012, (0, hy, top - 0.075), verts=8, material="ZR_DarkMetal"))
    cage_h = 0.22
    for k in range(6):
        a = k * math.pi / 3
        parts.append(cyl(f"bar{k}", 0.007, cage_h, (math.cos(a) * 0.09, hy + math.sin(a) * 0.09, top - 0.08 - cage_h / 2),
                         verts=5, material="ZR_DarkMetal"))
    parts.append(torus("ringMid", 0.09, 0.005, (0, hy, top - 0.08 - cage_h * 0.5), material="ZR_DarkMetal"))
    bottom = top - 0.08 - cage_h
    parts.append(cyl("bowl", 0.1, 0.05, (0, hy, bottom - 0.02), verts=8, material="ZR_Rust", r2=0.075))
    parts.append(cyl("oil", 0.06, 0.03, (0, hy, bottom + 0.012), verts=8, material="ZR_DarkMetal"))
    parts.append(cyl("wick", 0.012, 0.03, (0, hy, bottom + 0.04), verts=6, material="ZR_Embers"))
    # the lantern a third bigger (it read as a toy next to the 2 m post), hanging from the chain
    pivot = Vector((0, hy, LANTERN_TOP - 0.2))
    for p in parts[first_lantern:]:
        p.location = pivot + (p.location - pivot) * LANTERN_SCALE
        p.scale = p.scale * LANTERN_SCALE
    bottom = pivot.z + (bottom - pivot.z) * LANTERN_SCALE
    o = finish(join(parts, "ZR_LanternPost"))
    # the flame's spot: a tiny mesh, hidden in Unity
    bpy.ops.mesh.primitive_plane_add(size=0.01, location=(0, hy, bottom + 0.06 * LANTERN_SCALE))
    s = bpy.context.active_object; s.name = "FlameSocket"
    s.data.materials.append(mat("ZR_Embers"))
    log("flame socket (Blender x,y,z)", tuple(round(x, 3) for x in s.location))
    return [o, s]


# ---------------------------------------------------------------- armour vest, lying flat on its back

def armor_vest():
    parts = []
    W, Lg, T = 0.34, 0.42, 0.06       # width (x), length (y: neck at +y), plate thickness (z)
    body = box("plate", (W, Lg, T), (0, 0, T / 2 + 0.01), "ZR_ArmyGreen", bevel=0.02)
    for v in body.data.vertices:       # plate curve: the sides drop a little
        v.co.z -= (abs(v.co.x) / (W / 2)) ** 2 * 0.025
    parts.append(body)
    back = box("backpanel", (W * 1.02, Lg * 0.98, 0.025), (0, -0.005, 0.0125), "ZR_CanvasDark", bevel=0.008)
    parts.append(back)
    # neck cut-out look: a darker collar band
    parts.append(box("collar", (W * 0.55, 0.05, 0.02), (0, Lg / 2 - 0.03, T + 0.01), "ZR_CanvasDark", bevel=0.006))
    # shoulder straps running off the top edge
    for sx in (-1, 1):
        parts.append(box(f"strap{sx}", (0.06, 0.16, 0.025), (sx * 0.11, Lg / 2 + 0.06, 0.02), "ZR_CanvasDark", bevel=0.006))
        parts.append(box(f"buckle{sx}", (0.045, 0.02, 0.012), (sx * 0.11, Lg / 2 + 0.12, 0.035), "ZR_DarkMetal"))
    # side cummerbund flaps
    for sx in (-1, 1):
        f = box(f"cumm{sx}", (0.13, 0.2, 0.02), (sx * (W / 2 + 0.055), -0.07, 0.012), "ZR_Canvas", bevel=0.006)
        parts.append(f)
    # three rifle-mag pouches across the lower front, plus a small admin pouch
    for k in range(3):
        x = (k - 1) * 0.095
        parts.append(box(f"pouch{k}", (0.085, 0.13, 0.05), (x, -Lg / 2 + 0.1, T + 0.035), "ZR_Canvas", bevel=0.01))
        parts.append(box(f"flap{k}", (0.088, 0.045, 0.012), (x, -Lg / 2 + 0.145, T + 0.064), "ZR_CanvasDark", bevel=0.004))
    parts.append(box("admin", (0.16, 0.09, 0.03), (0, 0.05, T + 0.02), "ZR_Canvas", bevel=0.008))
    # MOLLE webbing rows across the chest
    for r in range(3):
        parts.append(box(f"molle{r}", (W * 0.8, 0.012, 0.006), (0, 0.11 + r * 0.03, T + 0.012), "ZR_CanvasDark"))
    o = finish(join(parts, "ZR_ArmorVest"))
    return [o]


# ---------------------------------------------------------------- ammo boxes from the Meshy zips

def ammo_box(name, zip_name, length, target):
    tmp = os.path.join(OUT, "_tmp_" + name)
    os.makedirs(tmp, exist_ok=True)
    with zipfile.ZipFile(os.path.join(SRC, zip_name)) as z:
        z.extractall(tmp)
    fbx = next(os.path.join(r, f) for r, _, fs in os.walk(tmp) for f in fs if f.lower().endswith(".fbx"))
    png = next(os.path.join(r, f) for r, _, fs in os.walk(tmp) for f in fs if f.lower().endswith(".png"))
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=fbx)
    objs = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    o = objs[0] if len(objs) == 1 else join(objs, name)
    for x in [x for x in bpy.data.objects if x not in before and x is not o]:
        bpy.data.objects.remove(x, do_unlink=True)
    o.name = name
    bpy.ops.object.select_all(action="DESELECT"); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    # sit on the ground at the origin, longest side = length
    pts = [Vector(v.co) for v in o.data.vertices]
    lo = Vector([min(p[i] for p in pts) for i in range(3)]); hi = Vector([max(p[i] for p in pts) for i in range(3)])
    k = length / max(hi - lo)
    off = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
    for v in o.data.vertices:
        v.co = (v.co - off) * k
    before_t = tris(o)
    dec = o.modifiers.new("dec", "DECIMATE"); dec.ratio = min(1.0, target / max(1, before_t)); dec.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=dec.name)
    # own material + 512 px texture
    img = bpy.data.images.load(png)
    img.scale(512, 512)
    out_png = os.path.join(OUT, name + ".png")
    img.filepath_raw = out_png; img.file_format = "PNG"; img.save()
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    t = m.node_tree.nodes.new("ShaderNodeTexImage"); t.image = img
    m.node_tree.links.new(t.outputs[0], bsdf.inputs["Base Color"])
    o.data.materials.clear(); o.data.materials.append(m)
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))
    log(name, "tris", before_t, "->", tris(o))
    import shutil
    shutil.rmtree(tmp, ignore_errors=True)
    return [o]


# ---------------------------------------------------------------- main

def main():
    os.makedirs(OUT, exist_ok=True)
    built = {}
    for maker, name in ((steel_bench, "ZR_SteelBench"), (lantern_post, "ZR_LanternPost"), (armor_vest, "ZR_ArmorVest")):
        reset()
        objs = maker()
        export(objs, name)
        built[name] = objs
        if PREVIEW: preview(objs, name)
    for name, z, length, target in AMMO:
        reset()
        objs = ammo_box(name, z, length, target)
        export(objs, name)
        if PREVIEW: preview(objs, name)


def preview(objs, name):
    os.makedirs(PREVIEW, exist_ok=True)
    sc = bpy.context.scene
    try:
        sc.render.engine = "BLENDER_EEVEE_NEXT"
    except TypeError:
        sc.render.engine = "CYCLES"; sc.cycles.samples = 24
    w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.3, 0.32, 0.36, 1)
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sc.collection.objects.link(sun)
    sun.data.energy = 3.0; sun.rotation_euler = (0.8, 0.2, 0.7)
    s = size_of(objs); h = max(s)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
    cam.location = Vector((h * 1.1, -h * 1.5, h * 0.9 + s.z * 0.3))
    look = Vector((0, 0, s.z * 0.45))
    cam.rotation_euler = (look - cam.location).to_track_quat("-Z", "Y").to_euler()
    sc.render.resolution_x, sc.render.resolution_y = 640, 480
    sc.render.filepath = os.path.join(PREVIEW, name + ".png")
    bpy.ops.render.render(write_still=True)


main()
