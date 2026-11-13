"""Zombie Rush city props, built from code in Blender.

Run:  blender -b -P Blender/build_city_props.py
Makes low-poly, game-ready props for the ruined night city, then:
  - saves Blender/CityProps.blend (every prop, for hand edits later),
  - exports one FBX per prop to Assets/Models/2026-11-09_CityProps/,
  - writes tileable grunge textures to Assets/Models/2026-11-09_CityProps/Textures/.
Every prop stands on the ground at the origin, faces +Y in Blender (forward in Unity),
uses metres, and has UVs from a cube projection (1 texture tile = 2 m).
Material names start with ZR_; Unity maps them to its own materials.
"""
import math
import os
import random

import bmesh
import bpy
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
OUT = os.path.join(PROJECT, "Assets", "Models", "2026-11-09_CityProps")
TEX = os.path.join(OUT, "Textures")
BLEND = os.path.join(HERE, "CityProps.blend")
TILE = 2.0  # metres per texture tile

random.seed(7)

# ---------------------------------------------------------------- scene


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"


def collection(name):
    col = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(col)
    return col


# ---------------------------------------------------------------- materials

MATS = {
    # name: (base colour, metallic, roughness, emission colour, emission strength)
    "ZR_BurntPaint": ((0.09, 0.08, 0.075), 0.3, 0.8, None, 0),
    "ZR_FadedPaint_Blue": ((0.12, 0.17, 0.22), 0.3, 0.75, None, 0),
    "ZR_FadedPaint_Red": ((0.28, 0.07, 0.05), 0.3, 0.75, None, 0),
    "ZR_FadedPaint_Cream": ((0.45, 0.42, 0.33), 0.2, 0.75, None, 0),
    "ZR_BusYellow": ((0.42, 0.29, 0.05), 0.2, 0.7, None, 0),
    "ZR_PoliceWhite": ((0.55, 0.55, 0.53), 0.3, 0.6, None, 0),
    "ZR_Rust": ((0.24, 0.11, 0.05), 0.6, 0.9, None, 0),
    "ZR_DarkMetal": ((0.06, 0.065, 0.07), 0.8, 0.55, None, 0),
    "ZR_Tire": ((0.025, 0.025, 0.025), 0.0, 0.95, None, 0),
    "ZR_GlassBroken": ((0.02, 0.03, 0.035), 0.1, 0.15, None, 0),
    "ZR_Concrete": ((0.38, 0.37, 0.35), 0.0, 0.95, None, 0),
    "ZR_HazardStripe": ((0.45, 0.33, 0.04), 0.0, 0.8, None, 0),
    "ZR_Sandbag": ((0.30, 0.26, 0.17), 0.0, 1.0, None, 0),
    "ZR_GreenMetal": ((0.07, 0.12, 0.07), 0.5, 0.7, None, 0),
    "ZR_LampGlow": ((1.0, 0.78, 0.45), 0.0, 0.4, (1.0, 0.72, 0.38), 6.0),
    "ZR_LampDead": ((0.15, 0.15, 0.14), 0.0, 0.3, None, 0),
    "ZR_SignalRed": ((0.6, 0.03, 0.02), 0.0, 0.3, (1.0, 0.05, 0.02), 4.0),
    "ZR_SignalDark": ((0.04, 0.04, 0.04), 0.0, 0.3, None, 0),
    "ZR_Embers": ((0.3, 0.06, 0.01), 0.0, 0.9, (1.0, 0.35, 0.05), 3.0),
    "ZR_Ash": ((0.05, 0.045, 0.04), 0.0, 1.0, None, 0),
    "ZR_Sign": ((0.04, 0.2, 0.1), 0.4, 0.6, None, 0),
    "ZR_PoliceBlue": ((0.03, 0.05, 0.18), 0.3, 0.6, None, 0),
    "ZR_PoliceLight": ((0.05, 0.08, 0.5), 0.0, 0.3, (0.1, 0.25, 1.0), 3.0),
}


def mat(name):
    m = bpy.data.materials.get(name)
    if m:
        return m
    col, metal, rough, emit, strength = MATS[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*col, 1)
    bsdf.inputs["Metallic"].default_value = metal
    bsdf.inputs["Roughness"].default_value = rough
    if emit:
        bsdf.inputs["Emission Color"].default_value = (*emit, 1)
        bsdf.inputs["Emission Strength"].default_value = strength
    m.diffuse_color = (*col, 1)
    return m


# ---------------------------------------------------------------- mesh helpers


def box(name, size, loc=(0, 0, 0), material="ZR_DarkMetal", bevel=0.0, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    bpy.ops.object.transform_apply(scale=True)
    if bevel > 0:
        b = o.modifiers.new("bevel", "BEVEL")
        b.width = bevel
        b.segments = 2
        b.limit_method = "ANGLE"
        bpy.ops.object.modifier_apply(modifier=b.name)
    o.data.materials.append(mat(material))
    return o


def cyl(name, r, depth, loc=(0, 0, 0), rot=(0, 0, 0), verts=12, material="ZR_DarkMetal", cap=True):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=loc, rotation=rot,
                                        end_fill_type="NGON" if cap else "NOTHING")
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(mat(material))
    return o


def taper_top(o, fx, fy, shift_y=0.0):
    """Shrink the top face of a box (cabin / windscreen slope)."""
    me = o.data
    zs = [v.co.z for v in me.vertices]
    top = max(zs)
    for v in me.vertices:
        if abs(v.co.z - top) < 1e-4:
            v.co.x *= fx
            v.co.y = v.co.y * fy + shift_y


def dent(o, strength=0.03, seed=0):
    """Small random dents: wrecks are never straight."""
    rnd = random.Random(seed)
    for v in o.data.vertices:
        v.co += Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1))) * strength


def join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    # pivot on the ground at the world origin, so the prop stands where it is placed
    bpy.context.scene.cursor.location = (0, 0, 0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    return o


def finish(o, col, smooth_angle=35):
    """Cube-project UVs at a fixed texel scale, smooth by angle, put in its collection."""
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.0005)
    bpy.ops.uv.cube_project(cube_size=TILE, correct_aspect=True, clip_to_bounds=False, scale_to_bounds=False)
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(smooth_angle))
    for c in o.users_collection:
        c.objects.unlink(o)
    col.objects.link(o)
    return o


def wheel(name, x, y, r=0.34, w=0.24, flat=False, burnt=True):
    t = cyl(name + "_tire", r, w, (x, y, r * (0.8 if flat else 1.0)), (0, math.pi / 2, 0), 14, "ZR_Rust" if burnt else "ZR_Tire")
    if flat:
        t.scale = (1, 1, 1)
        for v in t.data.vertices:
            if v.co.x < -r * 0.55:  # local x is world -z after the rotation: squash the bottom
                v.co.x = -r * 0.55
    hub = cyl(name + "_hub", r * 0.55, w + 0.02, (x, y, r * (0.8 if flat else 1.0)), (0, math.pi / 2, 0), 10, "ZR_DarkMetal")
    return [t, hub]


# ---------------------------------------------------------------- props


def car(col, name, paint, length=4.4, width=1.8, cabin_len=2.3, cabin_h=0.55, hatch=False, police=False,
        seed=1, burnt_amount=0.0):
    rnd = random.Random(seed)
    parts = []
    body = box(name + "_body", (width, length, 0.62), (0, 0, 0.62), paint, bevel=0.06)
    dent(body, 0.025, seed)
    parts.append(body)
    # cabin: window band then roof
    cab_y = -0.25 if not hatch else -0.55
    cab = box(name + "_cabin", (width * 0.88, cabin_len, cabin_h), (0, cab_y, 0.93 + cabin_h / 2), "ZR_GlassBroken", bevel=0.03)
    taper_top(cab, 0.82, 0.62 if not hatch else 0.8, 0.0 if not hatch else -0.12)
    # roof and the top bevel in body paint, the sloped sides stay glass
    cab.data.materials.append(mat(paint))
    for poly in cab.data.polygons:
        if poly.normal.z > 0.6:
            poly.material_index = 1
    parts.append(cab)
    roof = box(name + "_roof", (width * 0.8, cabin_len * (0.68 if not hatch else 0.82), 0.07),
               (0, cab_y + (0 if not hatch else -0.12), 0.93 + cabin_h + 0.03), paint, bevel=0.02)
    dent(roof, 0.02, seed + 1)
    parts.append(roof)
    # window pillars
    for sx in (-1, 1):
        for py in (cabin_len * 0.47, -cabin_len * 0.47):
            p = box(name + "_pillar", (0.06, 0.07, cabin_h), (sx * width * 0.42, cab_y + py * (0.85 if py > 0 else 0.85), 0.93 + cabin_h / 2), paint)
            p.rotation_euler.x = math.radians(-22 if py > 0 else 18)
            parts.append(p)
    # bumpers, lights, grille
    for sy in (1, -1):
        parts.append(box(name + "_bumper", (width * 1.02, 0.14, 0.18), (0, sy * length / 2, 0.42), "ZR_DarkMetal", bevel=0.03))
        for sx in (-1, 1):
            parts.append(box(name + "_lamp", (0.32, 0.04, 0.12), (sx * width * 0.36, sy * (length / 2 + 0.01), 0.7),
                             "ZR_LampDead" if sy > 0 else "ZR_FadedPaint_Red"))
    # wheels (some flat), arches
    for sx in (-1, 1):
        for sy in (1, -1):
            parts += wheel(name + "_w", sx * width * 0.47, sy * length * 0.31, flat=rnd.random() < 0.5, burnt=burnt_amount > 0.5)
    if police:
        bar = box(name + "_lightbar", (width * 0.6, 0.25, 0.1), (0, cab_y, 0.93 + cabin_h + 0.12), "ZR_DarkMetal")
        parts.append(bar)
        parts.append(box(name + "_blue", (0.28, 0.22, 0.09), (-0.22, cab_y, 0.93 + cabin_h + 0.18), "ZR_PoliceLight"))
        parts.append(box(name + "_red", (0.28, 0.22, 0.09), (0.22, cab_y, 0.93 + cabin_h + 0.18), "ZR_SignalRed"))
        for sx in (-1, 1):
            parts.append(box(name + "_door", (0.02, 1.3, 0.38), (sx * (width / 2 + 0.005), -0.1, 0.66), "ZR_PoliceBlue"))
    # rust patches
    for i in range(3 if burnt_amount < 0.5 else 6):
        sx = rnd.choice((-1, 1))
        parts.append(box(name + "_rust", (0.015, rnd.uniform(0.3, 0.9), rnd.uniform(0.15, 0.35)),
                         (sx * (width / 2 + 0.008), rnd.uniform(-length * 0.4, length * 0.4), rnd.uniform(0.45, 0.8)), "ZR_Rust"))
    o = join(parts, name)
    return finish(o, col)


def bus(col, name="ZR_Bus_Wreck", seed=3):
    rnd = random.Random(seed)
    L, W, H = 11.0, 2.5, 2.9
    parts = []
    body = box(name + "_lower", (W, L, 1.1), (0, 0, 0.35 + 0.55), "ZR_BusYellow", bevel=0.05)
    dent(body, 0.03, seed)
    parts.append(body)
    band = box(name + "_windows", (W * 0.98, L * 0.96, 0.95), (0, 0, 1.45 + 0.475), "ZR_GlassBroken")
    parts.append(band)
    roof = box(name + "_roof", (W, L, 0.35), (0, 0, 2.4 + 0.17), "ZR_BusYellow", bevel=0.1)
    dent(roof, 0.03, seed + 2)
    parts.append(roof)
    # window posts
    n = 9
    for i in range(n + 1):
        y = -L * 0.48 + i * (L * 0.96 / n)
        for sx in (-1, 1):
            parts.append(box(name + "_post", (0.06, 0.1, 0.95), (sx * W * 0.495, y, 1.92), "ZR_BusYellow"))
    # black stripe, bumpers
    for sx in (-1, 1):
        parts.append(box(name + "_stripe", (0.02, L * 0.98, 0.12), (sx * (W / 2 + 0.005), 0, 1.3), "ZR_DarkMetal"))
    for sy in (1, -1):
        parts.append(box(name + "_bumper", (W * 1.02, 0.18, 0.3), (0, sy * L / 2, 0.45), "ZR_DarkMetal", bevel=0.04))
    # wheels
    for sx in (-1, 1):
        for sy in (L * 0.33, -L * 0.3):
            parts += wheel(name + "_w", sx * W * 0.45, sy, r=0.5, w=0.32, flat=rnd.random() < 0.6)
    # rust and burns
    for i in range(10):
        sx = rnd.choice((-1, 1))
        parts.append(box(name + "_rust", (0.015, rnd.uniform(0.5, 1.8), rnd.uniform(0.2, 0.6)),
                         (sx * (W / 2 + 0.01), rnd.uniform(-L * 0.45, L * 0.45), rnd.uniform(0.6, 1.3)), "ZR_Rust"))
    o = join(parts, name)
    return finish(o, col)


def jersey_barrier(col, name="ZR_Barrier_Concrete", length=3.0, seed=4):
    """Concrete road barrier, the classic sloped profile."""
    prof = [(-0.3, 0.0), (0.3, 0.0), (0.3, 0.08), (0.18, 0.3), (0.08, 0.81), (-0.08, 0.81), (-0.18, 0.3), (-0.3, 0.08)]
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    front = [bm.verts.new((x, length / 2, z)) for x, z in prof]
    back = [bm.verts.new((x, -length / 2, z)) for x, z in prof]
    bm.faces.new(front)
    bm.faces.new(list(reversed(back)))
    k = len(prof)
    for i in range(k):
        bm.faces.new((front[i], front[(i + 1) % k], back[(i + 1) % k], back[i]))
    bmesh.ops.subdivide_edges(bm, edges=[e for e in bm.edges if abs(e.verts[0].co.y - e.verts[1].co.y) > 1], cuts=3)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    o.data.materials.append(mat("ZR_Concrete"))
    bpy.context.view_layer.objects.active = o
    dent(o, 0.012, seed)
    # hazard stripes and chipped corner
    parts = [o]
    for sy in (-0.9, 0.0, 0.9):
        for sx in (-1, 1):
            s = box(name + "_stripe", (0.01, 0.35, 0.18), (sx * 0.16, sy, 0.5), "ZR_HazardStripe")
            s.rotation_euler.y = math.radians(-sx * 11)
            parts.append(s)
    o = join(parts, name)
    return finish(o, col, 30)


def sandbag(loc, L, W, H, rnd):
    """One filled sandbag: a rounded box, a little squashed and uneven, tied ends tucked under."""
    b = box("bag", (W * rnd.uniform(0.95, 1.05), L * rnd.uniform(0.94, 1.02), H), loc, "ZR_Sandbag")
    bev = b.modifiers.new("round", "BEVEL")
    bev.width = min(H, W) * 0.38
    bev.segments = 2
    bpy.context.view_layer.objects.active = b
    bpy.ops.object.modifier_apply(modifier=bev.name)
    c = Vector(loc)  # box() bakes the position into the mesh: shape round the bag's own centre
    for v in b.data.vertices:  # fuller in the middle, flatter at the tied ends
        d = v.co - c
        k = 1.0 - min(1.0, abs(d.y) / (L * 0.5)) ** 2 * 0.25
        v.co.x = c.x + d.x * k
        v.co.z = c.z + d.z * (0.85 + 0.15 * k)
    dent(b, 0.012, int(loc[1] * 100 + loc[2] * 10))
    b.rotation_euler.z = math.radians(rnd.uniform(-5, 5))
    return b


def sandbag_wall(col, name="ZR_Sandbags", length=2.6, rows=4, seed=5):
    rnd = random.Random(seed)
    parts = []
    bag_l, bag_w, bag_h = 0.62, 0.36, 0.17
    for r in range(rows):
        n = int(length / bag_l)
        off = (bag_l / 2) if r % 2 else 0
        for i in range(n - (r % 2)):
            for depth in ((-0.2, 0.2) if r < rows - 1 else (0.0,)):
                parts.append(sandbag((depth + rnd.uniform(-0.02, 0.02), -length / 2 + bag_l / 2 + off + i * bag_l, bag_h / 2 + r * bag_h * 0.95),
                                     bag_l, bag_w, bag_h, rnd))
    o = join(parts, name)
    return finish(o, col, 60)


def street_lamp(col, name, working=True, bent=False):
    parts = []
    pole = cyl(name + "_pole", 0.09, 6.0, (0, 0, 3.0), verts=10, material="ZR_DarkMetal")
    parts.append(pole)
    parts.append(cyl(name + "_base", 0.18, 0.6, (0, 0, 0.3), verts=10, material="ZR_DarkMetal"))
    arm = cyl(name + "_arm", 0.05, 1.6, (0, 0.75, 5.95), (math.pi / 2, 0, 0), 8, "ZR_DarkMetal")
    parts.append(arm)
    head = box(name + "_head", (0.32, 0.6, 0.14), (0, 1.5, 5.9), "ZR_DarkMetal", bevel=0.03)
    parts.append(head)
    parts.append(box(name + "_bulb", (0.24, 0.48, 0.03), (0, 1.5, 5.82), "ZR_LampGlow" if working else "ZR_LampDead"))
    o = join(parts, name)
    if bent:
        # bend everything above 2.6 m over to one side
        for v in o.data.vertices:
            if v.co.z > 2.6:
                h = v.co.z - 2.6
                ang = min(h / 3.4, 1.0) * math.radians(38)
                y, z = v.co.y, h
                v.co.y = y * math.cos(ang) + z * math.sin(ang) * 0.0
                v.co.x = v.co.x + z * math.sin(ang)
                v.co.z = 2.6 + z * math.cos(ang)
    return finish(o, col)


def traffic_light(col, name="ZR_TrafficLight"):
    parts = [cyl(name + "_pole", 0.1, 4.6, (0, 0, 2.3), verts=10, material="ZR_GreenMetal"),
             cyl(name + "_base", 0.2, 0.4, (0, 0, 0.2), verts=10, material="ZR_GreenMetal"),
             cyl(name + "_arm", 0.06, 3.2, (0, 1.6, 4.5), (math.pi / 2, 0, 0), 8, "ZR_GreenMetal")]
    for y in (1.6, 3.0):
        parts.append(box(name + "_housing", (0.36, 0.3, 1.0), (0, y, 3.95), "ZR_DarkMetal", bevel=0.03))
        for i, m in enumerate(("ZR_SignalRed", "ZR_SignalDark", "ZR_SignalDark")):
            parts.append(cyl(name + "_lens", 0.11, 0.05, (0, y - 0.16, 4.27 - i * 0.31), (math.pi / 2, 0, 0), 12, m))
            parts.append(box(name + "_visor", (0.28, 0.16, 0.03), (0, y - 0.24, 4.4 - i * 0.31), "ZR_DarkMetal"))
    # hanging street sign
    parts.append(box(name + "_sign", (0.03, 1.4, 0.3), (0, 0.6, 4.5 - 0.3), "ZR_Sign"))
    o = join(parts, name)
    return finish(o, col)


def barrel(col, name, burning=False, rusty=True, seed=6):
    parts = []
    r, h = 0.3, 0.9
    shell = cyl(name + "_shell", r, h, (0, 0, h / 2), verts=16, material="ZR_Rust" if rusty else "ZR_FadedPaint_Blue",
                cap=not burning)
    parts.append(shell)
    for z in (0.3, 0.6):
        parts.append(cyl(name + "_rib", r + 0.015, 0.04, (0, 0, z), verts=16, material="ZR_DarkMetal"))
    parts.append(cyl(name + "_bottom", r, 0.02, (0, 0, 0.01), verts=16, material="ZR_DarkMetal"))
    if burning:
        # inner wall, a bed of embers and charred wood sticking out
        inner = cyl(name + "_inner", r * 0.95, h * 0.98, (0, 0, h / 2), verts=16, material="ZR_Ash", cap=False)
        bpy.ops.object.select_all(action="DESELECT")
        inner.select_set(True)
        bpy.context.view_layer.objects.active = inner
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.mesh.flip_normals()
        bpy.ops.object.mode_set(mode="OBJECT")
        parts.append(inner)
        parts.append(cyl(name + "_embers", r * 0.93, 0.05, (0, 0, h * 0.78), verts=16, material="ZR_Embers"))
        rnd = random.Random(seed)
        for i in range(5):
            a = rnd.uniform(0, math.pi * 2)
            parts.append(cyl(name + "_wood", 0.035, 0.7, (math.cos(a) * 0.1, math.sin(a) * 0.1, h * 0.85),
                             (rnd.uniform(-0.6, 0.6), rnd.uniform(-0.6, 0.6), 0), 6, "ZR_Ash"))
    o = join(parts, name)
    return finish(o, col, 50)


def dumpster(col, name="ZR_Dumpster", seed=8):
    parts = []
    L, W, H = 1.9, 1.15, 1.15
    body = box(name + "_body", (L, W, H), (0, 0, 0.2 + H / 2), "ZR_GreenMetal", bevel=0.03)
    # slanted front: pull the top-front edge back
    for v in body.data.vertices:
        if v.co.z > 0.2 + H * 0.9 and v.co.y > 0:
            v.co.y -= 0.12
    dent(body, 0.015, seed)
    parts.append(body)
    parts.append(box(name + "_rim", (L + 0.08, W + 0.06, 0.08), (0, -0.06, 0.2 + H), "ZR_DarkMetal"))
    lid = box(name + "_lid", (L * 0.49, W + 0.05, 0.04), (-L * 0.25, -0.05, 0.2 + H + 0.05), "ZR_DarkMetal")
    parts.append(lid)
    lid2 = box(name + "_lid_open", (L * 0.49, W + 0.05, 0.04), (L * 0.25, -W * 0.55, 0.2 + H + 0.45), "ZR_DarkMetal")
    lid2.rotation_euler.x = math.radians(70)
    parts.append(lid2)
    for sx in (-1, 1):
        parts.append(box(name + "_side_bar", (0.08, 0.08, 0.5), (sx * (L / 2 + 0.04), 0, 0.2 + H * 0.5), "ZR_DarkMetal"))
        for sy in (-1, 1):
            parts.append(cyl(name + "_caster", 0.09, 0.07, (sx * L * 0.4, sy * W * 0.38, 0.1), (0, math.pi / 2, 0), 8, "ZR_Tire"))
    for i in range(3):
        parts.append(box(name + "_rust", (L * 0.3, 0.015, 0.25), (-0.5 + i * 0.5, W / 2 + 0.006, 0.45 + (i % 2) * 0.3), "ZR_Rust"))
    o = join(parts, name)
    return finish(o, col)


# ---------------------------------------------------------------- textures


def tile_noise(size, cells, octaves, seed):
    """Tileable value noise in 0..1."""
    rng = np.random.default_rng(seed)
    out = np.zeros((size, size), np.float32)
    amp, total = 1.0, 0.0
    for o in range(octaves):
        n = cells * (2 ** o)
        grid = rng.random((n, n)).astype(np.float32)
        t = np.arange(size, dtype=np.float32) * n / size
        i0 = np.floor(t).astype(int) % n
        i1 = (i0 + 1) % n
        f = t - np.floor(t)
        f = f * f * (3 - 2 * f)
        a = grid[i0][:, i0] * (1 - f)[None, :] + grid[i0][:, i1] * f[None, :]
        b = grid[i1][:, i0] * (1 - f)[None, :] + grid[i1][:, i1] * f[None, :]
        out += amp * (a * (1 - f)[:, None] + b * f[:, None])
        total += amp
        amp *= 0.5
    return out / total


def save_png(path, rgb):
    h, w, _ = rgb.shape
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=True)
    px = np.ones((h, w, 4), np.float32)
    px[:, :, :3] = np.clip(rgb, 0, 1)
    img.pixels.foreach_set(px.ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def textures():
    """Grayscale grunge masks the Unity materials tint: dirt, scratches, rust spots, fabric."""
    os.makedirs(TEX, exist_ok=True)
    S = 512
    dirt = tile_noise(S, 4, 6, 1)
    spots = np.clip((tile_noise(S, 8, 4, 2) - 0.55) * 4, 0, 1)
    fine = tile_noise(S, 32, 3, 3)
    grunge = np.clip(0.75 + (dirt - 0.5) * 0.6 - spots * 0.35 + (fine - 0.5) * 0.25, 0, 1)
    save_png(os.path.join(TEX, "ZR_Grunge.png"), np.dstack([grunge] * 3))
    # concrete: speckle and pits
    pits = np.clip((tile_noise(S, 48, 2, 4) - 0.7) * 5, 0, 1)
    concrete = np.clip(0.8 + (tile_noise(S, 6, 5, 5) - 0.5) * 0.4 - pits * 0.3 + (fine - 0.5) * 0.2, 0, 1)
    save_png(os.path.join(TEX, "ZR_ConcreteGrunge.png"), np.dstack([concrete] * 3))
    # burlap: woven lines
    x = np.arange(S)
    weave = 0.85 + 0.15 * (np.sin(x[None, :] * math.pi * 2 * 64 / S) * np.sin(x[:, None] * math.pi * 2 * 64 / S))
    burlap = np.clip(weave * (0.8 + (dirt - 0.5) * 0.5), 0, 1)
    save_png(os.path.join(TEX, "ZR_Burlap.png"), np.dstack([burlap] * 3))


# ---------------------------------------------------------------- export


def export(o):
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    path = os.path.join(OUT, o.name + ".fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"},
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             mesh_smooth_type="FACE", use_mesh_modifiers=True, path_mode="STRIP",
                             embed_textures=False, add_leaf_bones=False, bake_anim=False)
    tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
    return tris


def main():
    reset()
    os.makedirs(OUT, exist_ok=True)
    cars = collection("Vehicles")
    road = collection("Barricades")
    street = collection("Street")
    junk = collection("Survivor")
    props = [
        car(cars, "ZR_Car_Sedan_Burnt", "ZR_BurntPaint", seed=11, burnt_amount=1.0),
        car(cars, "ZR_Car_Sedan_Blue", "ZR_FadedPaint_Blue", seed=12),
        car(cars, "ZR_Car_Hatch_Red", "ZR_FadedPaint_Red", length=3.9, cabin_len=2.2, cabin_h=0.62, hatch=True, seed=13),
        car(cars, "ZR_Car_Hatch_Cream", "ZR_FadedPaint_Cream", length=3.9, cabin_len=2.2, cabin_h=0.62, hatch=True, seed=14),
        car(cars, "ZR_Car_Police", "ZR_PoliceWhite", seed=15, police=True),
        bus(cars),
        jersey_barrier(road),
        sandbag_wall(road),
        street_lamp(street, "ZR_StreetLamp_On", working=True),
        street_lamp(street, "ZR_StreetLamp_Broken", working=False, bent=True),
        traffic_light(street),
        barrel(junk, "ZR_Barrel_Fire", burning=True),
        barrel(junk, "ZR_Barrel_Rust"),
        barrel(junk, "ZR_Barrel_Blue", rusty=False),
        dumpster(junk),
    ]
    # spread them out so the .blend is easy to look at
    x = 0.0
    for o in props:
        dims = o.dimensions
        o.location.x = x + dims.x / 2
        x += dims.x + 2.0
    report = []
    for o in props:
        loc = o.location.copy()
        o.location = (0, 0, 0)
        report.append(f"{o.name}: {export(o)} tris, {o.dimensions.x:.1f} x {o.dimensions.y:.1f} x {o.dimensions.z:.1f} m")
        o.location = loc
    textures()
    bpy.ops.wm.save_as_mainfile(filepath=BLEND)
    print("ZR_PROPS_DONE")
    for line in report:
        print("ZR_PROP " + line)


main()
