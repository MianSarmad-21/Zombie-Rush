"""Zombie Rush survival props, set 2: the fortified base, greenery, small covers and climbable structures.

Run:  blender -b -P Blender/build_survival_props.py
Uses the helpers and materials from build_city_props.py (same folder). Writes:
  - Blender/SurvivalProps.blend,
  - one FBX per prop to Assets/Models/2026-11-09_SurvivalProps/,
  - leaf and canvas textures to Assets/Models/2026-11-09_CityProps/Textures/.
Same rules as set 1: metres, standing on the origin, faces +Y in Blender, cube-projected UVs.
Climbable pieces (towers, nests, bridges, ramps) keep every walking surface at or under 35 degrees
and steps no taller than 0.3 m, so players and zombies can both use them.
"""
import math
import os
import random
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import importlib.util

_spec = importlib.util.spec_from_file_location("zr_city", os.path.join(os.path.dirname(os.path.abspath(__file__)), "build_city_props.py"))
city = importlib.util.module_from_spec(_spec)
city.__name__ = "zr_city"  # stops build_city_props.main() from running on import
_src = open(_spec.origin, encoding="utf-8").read().replace("\nmain()\n", "\n")
exec(compile(_src, _spec.origin, "exec"), city.__dict__)

box, cyl, join, finish, mat, dent, reset, collection, export = city.box, city.cyl, city.join, city.finish, city.mat, city.dent, city.reset, city.collection, city.export
PROJECT = city.PROJECT
OUT = os.path.join(PROJECT, "Assets", "Models", "2026-11-09_SurvivalProps")
city.OUT = OUT  # export() writes here
TEX = city.TEX

city.MATS.update({
    "ZR_Canvas": ((0.22, 0.24, 0.15), 0.0, 0.95, None, 0),
    "ZR_CanvasDark": ((0.14, 0.15, 0.10), 0.0, 0.95, None, 0),
    "ZR_Wood": ((0.28, 0.18, 0.10), 0.0, 0.85, None, 0),
    "ZR_WoodOld": ((0.20, 0.16, 0.12), 0.0, 0.9, None, 0),
    "ZR_Stone": ((0.25, 0.24, 0.23), 0.0, 0.95, None, 0),
    "ZR_Brick": ((0.30, 0.13, 0.08), 0.0, 0.9, None, 0),
    "ZR_Leaves": ((0.10, 0.18, 0.06), 0.0, 0.8, None, 0),
    "ZR_LeavesDry": ((0.20, 0.19, 0.08), 0.0, 0.85, None, 0),
    "ZR_Bark": ((0.12, 0.09, 0.07), 0.0, 0.95, None, 0),
    "ZR_Ivy": ((0.07, 0.15, 0.05), 0.0, 0.8, None, 0),
    "ZR_SearchGlow": ((1.0, 0.95, 0.85), 0.0, 0.3, (1.0, 0.93, 0.8), 8.0),
    "ZR_Wire": ((0.18, 0.18, 0.18), 0.9, 0.5, None, 0),
    "ZR_ArmyGreen": ((0.12, 0.16, 0.09), 0.4, 0.6, None, 0),
})


# ---------------------------------------------------------------- base


def rod(name, p0, p1, r, material, verts=6):
    """A cylinder running from point p0 to point p1."""
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    o = cyl(name, r, d.length, tuple((p0 + p1) / 2), verts=verts, material=material)
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d.normalized())
    return o


def tent(col, name="ZR_Tent_Military", L=5.0, W=3.6, H=2.6):
    """Ridge tent: canvas roof on two slopes, walls of 1 m, an open front flap."""
    parts = []
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    wall = 1.0
    pts = [(-W / 2, 0), (-W / 2, wall), (0, H), (W / 2, wall), (W / 2, 0)]
    front = [bm.verts.new((x, L / 2, z)) for x, z in pts]
    back = [bm.verts.new((x, -L / 2, z)) for x, z in pts]
    for i in range(len(pts) - 1):
        bm.faces.new((front[i], front[i + 1], back[i + 1], back[i]))
    bm.faces.new(list(reversed(back)))  # closed back
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name + "_canvas", me)
    bpy.context.scene.collection.objects.link(o)
    o.data.materials.append(mat("ZR_Canvas"))
    sol = o.modifiers.new("thick", "SOLIDIFY")
    sol.thickness = 0.04
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=sol.name)
    dent(o, 0.02, 3)
    parts.append(o)
    # open front: two flaps tied back, a dark inside
    for sx in (-1, 1):
        f = box(name + "_flap", (0.9, 0.05, 1.7), (sx * W * 0.32, L / 2 + 0.3, 0.9), "ZR_CanvasDark")
        f.rotation_euler.z = math.radians(sx * 35)
        parts.append(f)
    parts.append(box(name + "_inside", (W * 0.9, 0.02, 0.9), (0, L / 2 - 0.05, 0.45), "ZR_CanvasDark"))
    # poles, guy ropes, pegs
    for y in (L / 2 + 0.02, -L / 2 - 0.02):
        parts.append(cyl(name + "_pole", 0.04, H, (0, y, H / 2), verts=6, material="ZR_Wood"))
    parts.append(cyl(name + "_ridge", 0.035, L + 0.2, (0, 0, H + 0.02), (math.pi / 2, 0, 0), 6, "ZR_Wood"))
    for sx in (-1, 1):  # guy ropes from the wall top down to a peg
        for y in (-L * 0.35, 0, L * 0.35):
            parts.append(rod(name + "_rope", (sx * W / 2, y, wall), (sx * (W / 2 + 0.9), y, 0.02), 0.012, "ZR_Wire", 4))
            parts.append(box(name + "_peg", (0.05, 0.05, 0.2), (sx * (W / 2 + 0.9), y, 0.08), "ZR_Wood"))
    # cots and crates inside the door
    parts.append(box(name + "_cot", (0.8, 2.0, 0.4), (-W * 0.25, -0.2, 0.2), "ZR_ArmyGreen"))
    parts.append(box(name + "_crate", (0.6, 0.5, 0.5), (W * 0.25, 0.6, 0.25), "ZR_Wood", bevel=0.02))
    o = join(parts, name)
    return finish(o, col, 40)


def searchlight_tower(col, name="ZR_SearchlightTower", H=6.0):
    """Four-leg lattice tower with a guarded platform, a ladder and a searchlight on top."""
    parts = []
    s = 1.2
    foot = s + 0.25  # legs splay a little: wider at the ground than at the deck
    corners = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
    for sx, sy in corners:
        parts.append(rod(name + "_leg", (sx * foot, sy * foot, 0), (sx * s, sy * s, H), 0.07, "ZR_DarkMetal"))

    def leg_at(sx, sy, z):
        k = z / H
        r = foot + (s - foot) * k
        return (sx * r, sy * r, z)

    levels = (0.3, 2.2, 4.1, H - 0.1)
    for i in range(4):  # each face: a horizontal tie at every level and an X between levels
        a, b = corners[i], corners[(i + 1) % 4]
        for z in levels:
            parts.append(rod(name + "_tie", leg_at(*a, z), leg_at(*b, z), 0.025, "ZR_DarkMetal", 4))
        for z0, z1 in zip(levels[:-1], levels[1:]):
            parts.append(rod(name + "_brace", leg_at(*a, z0), leg_at(*b, z1), 0.02, "ZR_DarkMetal", 4))
            parts.append(rod(name + "_brace", leg_at(*b, z0), leg_at(*a, z1), 0.02, "ZR_DarkMetal", 4))
    parts.append(box(name + "_deck", (2 * s + 0.6, 2 * s + 0.6, 0.12), (0, 0, H), "ZR_Wood"))
    for sx in (-1, 1):  # rails and sandbag guard
        parts.append(box(name + "_rail", (0.06, 2 * s + 0.6, 0.06), (sx * (s + 0.3), 0, H + 1.0), "ZR_DarkMetal"))
        parts.append(box(name + "_rail", (2 * s + 0.6, 0.06, 0.06), (0, sx * (s + 0.3), H + 1.0), "ZR_DarkMetal"))
        parts.append(box(name + "_guard", (2 * s + 0.6, 0.35, 0.55), (0, sx * (s + 0.12), H + 0.33), "ZR_Sandbag", bevel=0.08))
    for sx, sy in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
        parts.append(cyl(name + "_post", 0.04, 1.0, (sx * (s + 0.3), sy * (s + 0.3), H + 0.5), verts=6, material="ZR_DarkMetal"))
    # ladder on the front face
    for sx in (-1, 1):
        parts.append(box(name + "_ladder_side", (0.05, 0.05, H), (sx * 0.25, -s - 0.35, H / 2), "ZR_DarkMetal"))
    for i in range(int(H / 0.3)):
        parts.append(box(name + "_rung", (0.5, 0.04, 0.04), (0, -s - 0.35, 0.3 + i * 0.3), "ZR_DarkMetal"))
    # searchlight: drum on a yoke
    parts.append(cyl(name + "_yoke", 0.06, 0.6, (0, 0.2, H + 0.4), verts=6, material="ZR_DarkMetal"))
    drum = cyl(name + "_drum", 0.32, 0.55, (0, 0.35, H + 0.85), (math.radians(70), 0, 0), 14, "ZR_DarkMetal")
    parts.append(drum)
    lens = cyl(name + "_lens", 0.28, 0.04, (0, 0.62, H + 0.95), (math.radians(70), 0, 0), 14, "ZR_SearchGlow")
    parts.append(lens)
    o = join(parts, name)
    return finish(o, col, 30)


def generator(col, name="ZR_Generator"):
    parts = [box(name + "_body", (1.0, 1.8, 0.95), (0, 0, 0.6), "ZR_ArmyGreen", bevel=0.04)]
    parts.append(box(name + "_skid", (1.1, 1.95, 0.12), (0, 0, 0.06), "ZR_DarkMetal"))
    parts.append(cyl(name + "_tank", 0.28, 0.9, (0, -0.3, 1.25), (0, math.pi / 2, 0), 12, "ZR_ArmyGreen"))
    parts.append(cyl(name + "_exhaust", 0.05, 0.6, (0.35, 0.75, 1.35), verts=8, material="ZR_Rust"))
    for i in range(6):  # vents
        parts.append(box(name + "_vent", (0.02, 0.9, 0.05), (0.51, -0.1, 0.35 + i * 0.1), "ZR_DarkMetal"))
    parts.append(box(name + "_panel", (0.02, 0.35, 0.3), (-0.51, 0.55, 0.75), "ZR_DarkMetal"))
    parts.append(box(name + "_lamp", (0.03, 0.06, 0.06), (-0.53, 0.45, 0.85), "ZR_SignalRed"))
    # a cable coil and a fuel can
    parts.append(cyl(name + "_coil", 0.25, 0.12, (-0.9, -0.5, 0.06), verts=12, material="ZR_Tire"))
    parts.append(box(name + "_can", (0.18, 0.3, 0.4), (-0.75, 0.5, 0.2), "ZR_FadedPaint_Red", bevel=0.02))
    o = join(parts, name)
    return finish(o, col)


def floodlight(col, name="ZR_Floodlight"):
    """Work light on a tripod: lights the base yard."""
    parts = []
    for a in range(3):
        ang = a * 2 * math.pi / 3 + math.pi / 2
        parts.append(rod(name + "_leg", (math.cos(ang) * 0.6, math.sin(ang) * 0.6, 0), (0, 0, 1.85), 0.03, "ZR_DarkMetal", 5))
    parts.append(cyl(name + "_mast", 0.035, 1.4, (0, 0, 2.5), verts=6, material="ZR_DarkMetal"))
    for sx in (-1, 1):
        parts.append(box(name + "_head", (0.5, 0.18, 0.35), (sx * 0.28, 0.05, 3.2), "ZR_DarkMetal", bevel=0.02))
        parts.append(box(name + "_glow", (0.42, 0.02, 0.27), (sx * 0.28, 0.15, 3.2), "ZR_SearchGlow"))
    parts.append(box(name + "_bar", (1.0, 0.06, 0.06), (0, 0, 3.2), "ZR_DarkMetal"))
    o = join(parts, name)
    return finish(o, col)


def campfire(col, name, big=False):
    """Stone ring, crossed logs and embers. big = bonfire (twice the size, a log pile)."""
    k = 1.8 if big else 1.0
    parts = []
    n = 12 if big else 9
    for i in range(n):
        a = i * 2 * math.pi / n
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=0.17 * k, location=(math.cos(a) * 0.55 * k, math.sin(a) * 0.55 * k, 0.08 * k))
        st = bpy.context.active_object
        st.scale = (1.0, 0.8, 0.6)
        st.data.materials.append(mat("ZR_Stone"))
        dent(st, 0.03 * k, i)
        parts.append(st)
    parts.append(cyl(name + "_ash", 0.45 * k, 0.04, (0, 0, 0.02), verts=12, material="ZR_Ash"))
    parts.append(cyl(name + "_embers", 0.3 * k, 0.05, (0, 0, 0.05), verts=10, material="ZR_Embers"))
    logs = 6 if big else 4
    for i in range(logs):  # teepee: from the ring to a point over the middle
        a = i * 2 * math.pi / logs
        parts.append(rod(name + "_log", (math.cos(a) * 0.42 * k, math.sin(a) * 0.42 * k, 0.04), (math.cos(a) * 0.05 * k, math.sin(a) * 0.05 * k, 0.6 * k), 0.06 * k, "ZR_Bark"))
    if big:
        for i in range(5):  # spare log pile beside it
            parts.append(cyl(name + "_spare", 0.09, 1.3, (1.9, -0.6 + i * 0.2, 0.09 + (i % 2) * 0.15), (math.pi / 2, 0, math.radians(90)), 6, "ZR_Bark"))
    o = join(parts, name)
    return finish(o, col, 50)


# ---------------------------------------------------------------- small covers


def crate(col, name, size=(1.0, 1.0, 1.0), material="ZR_Wood"):
    parts = [box(name + "_body", size, (0, 0, size[2] / 2), material, bevel=0.02)]
    w, d, h = size
    for z in (0.08, h - 0.08):  # slats
        for sy in (-1, 1):
            parts.append(box(name + "_band", (w + 0.03, 0.06, 0.1), (0, sy * (d / 2 + 0.01), z), "ZR_WoodOld"))
        for sx in (-1, 1):
            parts.append(box(name + "_band", (0.06, d + 0.03, 0.1), (sx * (w / 2 + 0.01), 0, z), "ZR_WoodOld"))
    o = join(parts, name)
    return finish(o, col)


def crate_stack(col, name="ZR_CrateStack"):
    rnd = random.Random(21)
    parts = []
    spots = [(0, 0, 0, 1.0), (1.05, 0.1, 0, 1.0), (0.5, 0.05, 1.0, 1.0), (0.2, 1.1, 0, 0.8)]
    for i, (x, y, z, s) in enumerate(spots):
        c = box(name + "_c", (s, s, s), (x, y, z + s / 2), "ZR_Wood", bevel=0.02)
        c.rotation_euler.z = math.radians(rnd.uniform(-8, 8))
        parts.append(c)
        for sy in (-1, 1):
            parts.append(box(name + "_band", (s + 0.03, 0.06, 0.1), (x, y + sy * (s / 2 + 0.01), z + 0.08), "ZR_WoodOld"))
    o = join(parts, name)
    return finish(o, col)


def ammo_boxes(col, name="ZR_AmmoBoxes"):
    parts = []
    for i, (x, y, z) in enumerate([(0, 0, 0), (0.62, 0, 0), (0.3, 0, 0.32), (0, 0.4, 0)]):
        parts.append(box(name + "_b", (0.58, 0.32, 0.3), (x, y, z + 0.15), "ZR_ArmyGreen", bevel=0.015))
        parts.append(box(name + "_h", (0.15, 0.05, 0.03), (x, y, z + 0.31), "ZR_DarkMetal"))
    o = join(parts, name)
    return finish(o, col)


def concrete_block(col, name="ZR_ConcreteBlock"):
    o = box(name, (1.2, 1.2, 0.9), (0, 0, 0.45), "ZR_Concrete", bevel=0.06)
    dent(o, 0.03, 31)
    return finish(o, col)


def low_wall(col, name="ZR_LowWall_Brick", L=3.0, H=1.1, broken=True, seed=9):
    """Waist-high brick wall: the classic cover. Broken top edge."""
    rnd = random.Random(seed)
    parts = []
    bw, bh = 0.25, 0.1
    rows = int(H / bh)
    for r in range(rows):
        n = int(L / bw)
        for i in range(n):
            x = -L / 2 + (i + (0.5 if r % 2 else 0)) * bw
            if x > L / 2 - bw * 0.5:
                continue
            top = H - (rnd.uniform(0, 0.45) if broken and abs(x) > L * 0.15 else 0)
            if r * bh > top:
                continue
            b = box(name + "_brick", (0.24, 0.24, 0.095), (x + bw / 2, 0, r * bh + bh / 2), "ZR_Brick")
            parts.append(b)
    o = join(parts, name)
    return finish(o, col, 30)


def tire_stack(col, name="ZR_TireStack"):
    parts = []
    for i, (x, y) in enumerate([(0, 0), (0, 0), (0, 0), (0.75, 0.1), (0.75, 0.1)]):
        z = 0.12 + (i if i < 3 else i - 3) * 0.24
        bpy.ops.mesh.primitive_torus_add(major_radius=0.32, minor_radius=0.12, major_segments=14, minor_segments=6, location=(x, y, z))
        t = bpy.context.active_object
        t.data.materials.append(mat("ZR_Tire"))
        parts.append(t)
    o = join(parts, name)
    return finish(o, col, 50)


def razor_fence(col, name="ZR_RazorFence", L=4.0, H=2.0):
    """Chain-link panel on two posts with a barbed-wire coil on top."""
    parts = []
    for sx in (-1, 1):
        parts.append(cyl(name + "_post", 0.05, H + 0.3, (sx * L / 2, 0, (H + 0.3) / 2), verts=6, material="ZR_DarkMetal"))
        parts.append(box(name + "_foot", (0.5, 0.5, 0.15), (sx * L / 2, 0, 0.075), "ZR_Concrete"))
    # mesh: a grid of thin wires (diamond look from a distance)
    for i in range(int(L / 0.2) + 1):
        x = -L / 2 + i * 0.2
        w = box(name + "_wire", (0.012, 0.012, H), (x, 0, H / 2 + 0.1), "ZR_Wire")
        w.rotation_euler.y = math.radians(12)
        parts.append(w)
    for z in (0.12, H / 2 + 0.1, H + 0.08):
        parts.append(cyl(name + "_rail", 0.025, L, (0, 0, z), (0, math.pi / 2, 0), 5, "ZR_DarkMetal"))
    # barbed coil
    turns = int(L / 0.25)
    me = bpy.data.meshes.new(name + "_coil")
    bm = bmesh.new()
    prev = None
    steps = turns * 10
    for k in range(steps + 1):
        t = k / steps
        a = t * turns * 2 * math.pi
        v = bm.verts.new((-L / 2 + t * L, math.cos(a) * 0.22, H + 0.32 + math.sin(a) * 0.22))
        if prev:
            bm.edges.new((prev, v))
        prev = v
    bm.to_mesh(me)
    bm.free()
    coil = bpy.data.objects.new(name + "_coil", me)
    bpy.context.scene.collection.objects.link(coil)
    bpy.context.view_layer.objects.active = coil
    coil.select_set(True)
    bpy.ops.object.convert(target="CURVE")
    coil.data.bevel_depth = 0.012
    coil.data.bevel_resolution = 0
    bpy.ops.object.convert(target="MESH")
    coil.data.materials.append(mat("ZR_Wire"))
    parts.append(coil)
    o = join(parts, name)
    return finish(o, col)


# ---------------------------------------------------------------- greenery


def blob_cluster(parts, centre, n, radius, size, material, rnd, flat=0.8):
    for i in range(n):
        a = rnd.uniform(0, math.pi * 2)
        r = rnd.uniform(0, radius)
        loc = (centre[0] + math.cos(a) * r, centre[1] + math.sin(a) * r, centre[2] + rnd.uniform(-0.15, 0.15) * size)
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=size * rnd.uniform(0.7, 1.1), location=loc)
        b = bpy.context.active_object
        b.scale = (1, 1, flat)
        b.data.materials.append(mat(material))
        dent(b, size * 0.12, i + int(centre[0] * 10))
        parts.append(b)


def bush(col, name, dry=False, seed=40):
    rnd = random.Random(seed)
    parts = []
    blob_cluster(parts, (0, 0, 0.45), 7, 0.55, 0.42, "ZR_LeavesDry" if dry else "ZR_Leaves", rnd)
    o = join(parts, name)
    return finish(o, col, 80)


def grass_tuft(col, name="ZR_GrassTuft", seed=41):
    """Crossed blades: a cheap patch of wild grass for cracks and edges."""
    rnd = random.Random(seed)
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    for i in range(14):
        a = rnd.uniform(0, math.pi)
        r = rnd.uniform(0, 0.35)
        cx, cy = math.cos(rnd.uniform(0, 6.28)) * r, math.sin(rnd.uniform(0, 6.28)) * r
        h = rnd.uniform(0.25, 0.6)
        w = 0.05
        dx, dy = math.cos(a) * w, math.sin(a) * w
        lean = Vector((rnd.uniform(-0.12, 0.12), rnd.uniform(-0.12, 0.12), 0))
        v = [bm.verts.new((cx - dx, cy - dy, 0)), bm.verts.new((cx + dx, cy + dy, 0)), bm.verts.new(Vector((cx, cy, h)) + lean)]
        bm.faces.new(v)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    o.data.materials.append(mat("ZR_LeavesDry"))
    o.data.materials.append(mat("ZR_Leaves"))
    for p in o.data.polygons:
        p.material_index = p.index % 2
    sol = o.modifiers.new("thick", "SOLIDIFY")
    sol.thickness = 0.01
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=sol.name)
    return finish(o, col, 80)


def tree(col, name, dead=False, seed=42, H=6.0):
    rnd = random.Random(seed)
    parts = []
    trunk = cyl(name + "_trunk", 0.2, H * 0.6, (0, 0, H * 0.3), verts=8, material="ZR_Bark")
    for v in trunk.data.vertices:  # taper
        k = 1 - (v.co.z + H * 0.3) / (H * 0.6) * 0.5
        v.co.x *= k
        v.co.y *= k
    parts.append(trunk)
    for i in range(5 if dead else 4):
        a = i * 2 * math.pi / (5 if dead else 4) + rnd.uniform(-0.3, 0.3)
        L = rnd.uniform(1.4, 2.2)
        z = H * rnd.uniform(0.45, 0.6)
        parts.append(rod(name + "_branch", (0, 0, z), (math.cos(a) * L * 0.8, math.sin(a) * L * 0.8, z + L * 0.6), 0.07, "ZR_Bark", 5))
    if not dead:
        blob_cluster(parts, (0, 0, H * 0.75), 9, 1.4, 1.1, "ZR_Leaves", rnd, flat=0.75)
    o = join(parts, name)
    return finish(o, col, 70)


def ivy(col, name="ZR_Ivy_Wall", W=3.0, H=4.0, seed=43):
    """Vines hanging down a wall: flat leaf clumps on a thin sheet, back side at y=0 (against the wall)."""
    rnd = random.Random(seed)
    parts = []
    strands = 7
    for s in range(strands):
        x = -W / 2 + (s + 0.5) * W / strands + rnd.uniform(-0.15, 0.15)
        length = H * rnd.uniform(0.45, 1.0)
        n = int(length / 0.22)
        for i in range(n):
            z = H - i * 0.22
            bpy.ops.mesh.primitive_plane_add(size=1, location=(x + rnd.uniform(-0.12, 0.12), 0.03 + rnd.uniform(0, 0.04), z))
            lf = bpy.context.active_object
            sz = rnd.uniform(0.18, 0.32) * (1.0 - i / (n + 4) * 0.5)
            lf.scale = (sz, sz, sz)
            lf.rotation_euler = (math.radians(90 + rnd.uniform(-25, 25)), rnd.uniform(-0.6, 0.6), rnd.uniform(-0.5, 0.5))
            lf.data.materials.append(mat("ZR_Ivy"))
            parts.append(lf)
    o = join(parts, name)
    sol = o.modifiers.new("thick", "SOLIDIFY")
    sol.thickness = 0.008
    bpy.ops.object.modifier_apply(modifier=sol.name)
    return finish(o, col, 80)


# ---------------------------------------------------------------- climbable


def stairs(parts, name, x, y0, z0, z1, width=1.0, material="ZR_Wood", along=1):
    """Straight stair from height z0 to z1 going +Y (along=1) or -Y. Rise <= 0.25 m, about 33 degrees."""
    rise = z1 - z0
    n = max(2, math.ceil(rise / 0.25))
    step_h = rise / n
    run = step_h / math.tan(math.radians(33))
    for i in range(n):
        parts.append(box(name + "_step", (width, run + 0.02, 0.06), (x, y0 + along * (i + 0.5) * run, z0 + (i + 1) * step_h - 0.03), material))
    for sx in (-1, 1):  # stringers and a hand rail
        L = math.hypot(n * run, rise)
        ang = math.atan2(rise, n * run)
        st = box(name + "_stringer", (0.06, L, 0.2), (x + sx * width / 2, y0 + along * n * run / 2, z0 + rise / 2), "ZR_WoodOld")
        st.rotation_euler.x = ang * along
        parts.append(st)
        rl = box(name + "_handrail", (0.05, L, 0.05), (x + sx * width / 2, y0 + along * n * run / 2, z0 + rise / 2 + 0.95), "ZR_DarkMetal")
        rl.rotation_euler.x = ang * along
        parts.append(rl)
    return n * run


def scaffold_tower(col, name="ZR_ScaffoldTower", H=6.0, W=3.0, D=3.0):
    """Two-level scaffold with switch-back stairs and a top deck. Footprint W x (D + stairs)."""
    parts = []
    levels = [H / 2, H]
    for sx in (-1, 1):
        for sy in (-1, 1):
            parts.append(cyl(name + "_pipe", 0.045, H + 1.1, (sx * W / 2, sy * D / 2, (H + 1.1) / 2), verts=6, material="ZR_DarkMetal"))
    for z in levels:
        parts.append(box(name + "_deck", (W + 0.1, D + 0.1, 0.1), (0, 0, z), "ZR_WoodOld"))
        for sx in (-1, 1):
            parts.append(cyl(name + "_tube", 0.035, D, (sx * W / 2, 0, z + 1.0), (math.pi / 2, 0, 0), 5, "ZR_DarkMetal"))
        parts.append(cyl(name + "_tube", 0.035, W, (0, D / 2, z + 1.0), (0, math.pi / 2, 0), 5, "ZR_DarkMetal"))
        for sx in (-1, 1):  # diagonal brace on each side, from the level below up to this one
            parts.append(rod(name + "_brace", (sx * W / 2, -D / 2, z - H / 2), (sx * W / 2, D / 2, z), 0.03, "ZR_DarkMetal", 5))
    # stairs in front (-Y side): ground -> level 1 going -Y, landing, level 1 -> level 2 back towards the tower
    run1 = stairs(parts, name + "_s1", -W / 4, -D / 2, 0.0, levels[0], width=W / 2 - 0.1, along=-1)
    parts.append(box(name + "_landing", (W + 0.1, 1.2, 0.1), (0, -D / 2 - run1 - 0.6, levels[0]), "ZR_WoodOld"))
    for sx in (-1, 1):
        parts.append(cyl(name + "_lpost", 0.04, levels[0], (sx * W / 2, -D / 2 - run1 - 1.15, levels[0] / 2), verts=5, material="ZR_DarkMetal"))
    stairs(parts, name + "_s2", W / 4, -D / 2 - run1 - 1.2, levels[0], levels[1], width=W / 2 - 0.1, along=1)
    # sandbag nest on top deck
    for sx in (-1, 1):
        parts.append(box(name + "_bags", (0.4, D * 0.7, 0.5), (sx * (W / 2 - 0.2), 0.3, H + 0.3), "ZR_Sandbag", bevel=0.08))
    parts.append(box(name + "_bags", (W * 0.8, 0.4, 0.5), (0, D / 2 - 0.2, H + 0.3), "ZR_Sandbag", bevel=0.08))
    o = join(parts, name)
    return finish(o, col, 30)


def sniper_nest(col, name="ZR_SniperNest", H=4.0, open_x=False):
    """Wooden platform with a roof and a single straight stair: a lookout for one or two players."""
    parts = []
    W = 2.6
    for sx in (-1, 1):
        for sy in (-1, 1):
            parts.append(box(name + "_leg", (0.18, 0.18, H + 2.3), (sx * W / 2, sy * W / 2, (H + 2.3) / 2), "ZR_Wood"))
    parts.append(box(name + "_floor", (W + 0.3, W + 0.3, 0.12), (0, 0, H), "ZR_WoodOld"))
    for sx in ((-1,) if open_x else (-1, 1)):  # open_x: no wall on +X, a plank bridge lands there
        parts.append(box(name + "_side", (0.06, W + 0.3, 0.9), (sx * (W / 2 + 0.12), 0, H + 0.5), "ZR_WoodOld"))
    parts.append(box(name + "_back", (W + 0.3, 0.06, 0.9), (0, W / 2 + 0.12, H + 0.5), "ZR_WoodOld"))
    for sx in (-1, 1):  # front half wall with a gap for the stairs
        parts.append(box(name + "_front", (W * 0.35, 0.06, 0.9), (sx * W * 0.33, -W / 2 - 0.12, H + 0.5), "ZR_WoodOld"))
    roof = box(name + "_roof", (W + 0.8, W + 0.8, 0.08), (0, 0.2, H + 2.35), "ZR_Rust")
    roof.rotation_euler.x = math.radians(-8)
    parts.append(roof)
    stairs(parts, name + "_st", 0, -W / 2 - 0.15, 0.0, H, width=0.9, along=-1)
    parts.append(box(name + "_bags", (W * 0.8, 0.35, 0.4), (0, W / 2 - 0.1, H + 0.26), "ZR_Sandbag", bevel=0.06))
    o = join(parts, name)
    return finish(o, col, 30)


def plank_bridge(col, name="ZR_PlankBridge", L=10.0, W=1.6):
    """Plank walkway with rope rails, spans L metres between two decks of the same height (top at z=0)."""
    parts = []
    rnd = random.Random(50)
    n = int(L / 0.32)
    for i in range(n):
        p = box(name + "_plank", (W * rnd.uniform(0.92, 1.0), 0.28, 0.06), (rnd.uniform(-0.04, 0.04), -L / 2 + (i + 0.5) * L / n, -0.03 - 0.05 * math.sin(math.pi * (i + 0.5) / n)), "ZR_WoodOld")
        p.rotation_euler.z = math.radians(rnd.uniform(-3, 3))
        parts.append(p)
    for sx in (-1, 1):
        parts.append(box(name + "_beam", (0.1, L, 0.14), (sx * W * 0.42, 0, -0.13), "ZR_Wood"))
        parts.append(cyl(name + "_rope", 0.02, L, (sx * W / 2, 0, 0.95), (math.pi / 2, 0, 0), 5, "ZR_Wire"))
        for i in range(5):
            y = -L / 2 + i * L / 4
            parts.append(box(name + "_rpost", (0.06, 0.06, 1.0), (sx * W / 2, y, 0.45), "ZR_Wood"))
    o = join(parts, name)
    return finish(o, col, 30)


def plank_ramp(col, name="ZR_PlankRamp", H=2.4, W=1.8):
    """Rubble-and-plank ramp up onto a low ledge, 30 degrees; top edge at y=0, z=H."""
    parts = []
    run = H / math.tan(math.radians(30))
    L = math.hypot(run, H)
    deck = box(name + "_deck", (W, L, 0.08), (0, -run / 2, H / 2), "ZR_WoodOld")
    deck.rotation_euler.x = math.radians(30)
    parts.append(deck)
    for i in range(int(L / 0.45)):  # cleats
        t = (i + 0.5) / int(L / 0.45)
        c = box(name + "_cleat", (W * 0.9, 0.05, 0.04), (0, -run * (1 - t), H * t + 0.06), "ZR_Wood")
        c.rotation_euler.x = math.radians(30)
        parts.append(c)
    rnd = random.Random(51)
    for i in range(8):  # rubble under it
        y = -run * rnd.uniform(0.1, 0.9)
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=rnd.uniform(0.25, 0.5), location=(rnd.uniform(-W / 2, W / 2), y, 0.15))
        r = bpy.context.active_object
        r.scale = (1, 1, 0.6)
        r.data.materials.append(mat("ZR_Concrete"))
        parts.append(r)
    o = join(parts, name)
    return finish(o, col, 30)


# ---------------------------------------------------------------- textures


def textures():
    S = 256
    rng = np.random.default_rng(5)
    leaf = city.tile_noise(S, 8, 4, 6)
    city.save_png(os.path.join(TEX, "ZR_LeafNoise.png"), np.dstack([np.clip(0.6 + (leaf - 0.5) * 0.9, 0, 1)] * 3))
    canvas = city.tile_noise(S, 6, 5, 7)
    x = np.arange(S)
    weave = 0.9 + 0.1 * np.sin(x[None, :] * math.pi * 2 * 48 / S)
    city.save_png(os.path.join(TEX, "ZR_CanvasGrunge.png"), np.dstack([np.clip(weave * (0.75 + (canvas - 0.5) * 0.6), 0, 1)] * 3))
    grain = city.tile_noise(S, 4, 3, 8)
    stripes = 0.85 + 0.15 * np.sin(x[:, None] * math.pi * 2 * 24 / S + grain * 6)
    city.save_png(os.path.join(TEX, "ZR_WoodGrain.png"), np.dstack([np.clip(stripes * (0.8 + (grain - 0.5) * 0.4), 0, 1)] * 3))


def main():
    reset()
    os.makedirs(OUT, exist_ok=True)
    base = collection("Base")
    covers = collection("Covers")
    green = collection("Greenery")
    climb = collection("Climbable")
    props = [
        tent(base), searchlight_tower(base), generator(base), floodlight(base),
        campfire(base, "ZR_Campfire"), campfire(base, "ZR_Bonfire", big=True),
        crate(covers, "ZR_Crate"), crate_stack(covers), ammo_boxes(covers), concrete_block(covers),
        low_wall(covers), low_wall(covers, "ZR_LowWall_Brick_Short", L=1.8, H=0.9, seed=12), tire_stack(covers), razor_fence(covers),
        bush(green, "ZR_Bush"), bush(green, "ZR_Bush_Dry", dry=True, seed=44), grass_tuft(green),
        tree(green, "ZR_Tree"), tree(green, "ZR_Tree_Dead", dead=True, seed=45), ivy(green),
        scaffold_tower(climb), sniper_nest(climb), sniper_nest(climb, "ZR_SniperNest_Open", open_x=True), plank_bridge(climb), plank_ramp(climb),
    ]
    x = 0.0
    for o in props:
        o.location.x = x + o.dimensions.x / 2
        x += o.dimensions.x + 2.0
    report = []
    for o in props:
        loc = o.location.copy()
        o.location = (0, 0, 0)
        report.append(f"{o.name}: {export(o)} tris, {o.dimensions.x:.1f} x {o.dimensions.y:.1f} x {o.dimensions.z:.1f} m")
        o.location = loc
    textures()
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(os.path.dirname(os.path.abspath(__file__)), "SurvivalProps.blend"))
    print("ZR_PROPS_DONE")
    for line in report:
        print("ZR_PROP " + line)


main()
