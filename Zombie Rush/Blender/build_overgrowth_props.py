"""Zombie Rush overgrowth set: realistic trees, bushes, grass and ivy made of leaf cards, plus heavy base walls.

Run:  blender -b -P Blender/build_overgrowth_props.py
Uses the helpers from build_city_props.py. Writes:
  - Blender/OvergrowthProps.blend,
  - one FBX per prop to Assets/Models/2026-11-09_OvergrowthProps/,
  - alpha textures (leaf clusters, ivy, grass) and a tileable moss texture to Assets/Models/2026-11-09_CityProps/Textures/.
Foliage is built from crossed cards (flat quads with a see-through leaf texture) with normals bent
outward from the crown, the usual way games make trees look full and soft at low cost.
Bark and wall pieces use cube-projected UVs like the other sets; cards keep their own 0-1 UVs.
"""
import math
import os
import random

import bmesh
import bpy
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
_src = open(os.path.join(HERE, "build_city_props.py"), encoding="utf-8").read().replace("\nmain()\n", "\n")
city = {"__name__": "zr_city", "__file__": os.path.join(HERE, "build_city_props.py")}
exec(compile(_src, city["__file__"], "exec"), city)

box, cyl, join, mat, reset, collection, export = city["box"], city["cyl"], city["join"], city["mat"], city["reset"], city["collection"], city["export"]
PROJECT = city["PROJECT"]
OUT = os.path.join(PROJECT, "Assets", "Models", "2026-11-09_OvergrowthProps")
city["OUT"] = OUT
TEX = city["TEX"]
TILE = city["TILE"]

city["MATS"].update({
    "ZR_Bark": ((0.12, 0.09, 0.07), 0.0, 0.95, None, 0),
    "ZR_Foliage": ((0.20, 0.26, 0.12), 0.0, 0.8, None, 0),
    "ZR_FoliageDry": ((0.30, 0.27, 0.14), 0.0, 0.85, None, 0),
    "ZR_IvyCard": ((0.18, 0.26, 0.11), 0.0, 0.8, None, 0),
    "ZR_GrassCard": ((0.26, 0.28, 0.13), 0.0, 0.9, None, 0),
    "ZR_Hesco": ((0.42, 0.37, 0.27), 0.0, 1.0, None, 0),
    "ZR_Sand": ((0.40, 0.35, 0.24), 0.0, 1.0, None, 0),
    "ZR_DryGrass": ((0.34, 0.30, 0.16), 0.0, 0.9, None, 0),
    "ZR_LeafA": ((0.10, 0.20, 0.06), 0.0, 0.7, None, 0),
    "ZR_LeafCore": ((0.04, 0.08, 0.025), 0.0, 0.9, None, 0),
    "ZR_LeafB": ((0.16, 0.27, 0.08), 0.0, 0.7, None, 0),
    "ZR_LeafC": ((0.24, 0.30, 0.10), 0.0, 0.7, None, 0),
    "ZR_DryGrassDark": ((0.22, 0.21, 0.11), 0.0, 0.9, None, 0),
    "ZR_Wire": ((0.18, 0.18, 0.18), 0.9, 0.5, None, 0),
    "ZR_Sandbag": ((0.30, 0.26, 0.17), 0.0, 1.0, None, 0),
})


def finish(o, col, smooth_angle=35, cube_uv=True):
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    if cube_uv:
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


def rod(name, p0, p1, r0, r1, material, verts=6):
    """Tapered cylinder from p0 (radius r0) to p1 (radius r1)."""
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r0, radius2=r1, depth=d.length, location=tuple((p0 + p1) / 2), end_fill_type="NOTHING")
    o = bpy.context.active_object
    o.name = name
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d.normalized())
    o.data.materials.append(mat(material))
    return o


# ---------------------------------------------------------------- cards


def cards_object(name, quads, material, normal_centre=None, up_normals=False):
    """quads: list of (centre, x_axis, y_axis) with full-size vectors. One mesh, 0-1 UVs per quad.
    Normals bend away from normal_centre (crowns, bushes) or straight up (grass), so cards shade softly."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    for c, ax, ay in quads:
        c, ax, ay = Vector(c), Vector(ax), Vector(ay)
        vs = [bm.verts.new(c - ax / 2 - ay / 2), bm.verts.new(c + ax / 2 - ay / 2), bm.verts.new(c + ax / 2 + ay / 2), bm.verts.new(c - ax / 2 + ay / 2)]
        f = bm.faces.new(vs)
        for loop, (u, v) in zip(f.loops, [(0, 0), (1, 0), (1, 1), (0, 1)]):
            loop[uv].uv = (u, v)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    o.data.materials.append(mat(material))
    if normal_centre is not None or up_normals:
        nc = Vector(normal_centre) if normal_centre is not None else None
        normals = []
        for v in me.vertices:
            n = Vector((0, 0, 1)) if up_normals else (v.co - nc)
            if n.length < 1e-4:
                n = Vector((0, 0, 1))
            normals.append(n.normalized())
        me.normals_split_custom_set_from_vertices(normals)
    return o


def crossed(centre, size, rnd, n=3, tilt=0.35):
    """n vertical cards crossing at centre, each turned and tipped a little."""
    out = []
    base = rnd.uniform(0, math.pi)
    for i in range(n):
        a = base + i * math.pi / n
        ax = Vector((math.cos(a), math.sin(a), 0)) * size
        ay = Vector((rnd.uniform(-tilt, tilt), rnd.uniform(-tilt, tilt), 1)).normalized() * size
        out.append((centre, ax, ay))
    # one roughly flat card on top hides the crossing seam from above
    a = rnd.uniform(0, math.pi)
    out.append((Vector(centre) + Vector((0, 0, size * 0.15)), Vector((math.cos(a), math.sin(a), 0)) * size * 0.9,
                Vector((-math.sin(a), math.cos(a), rnd.uniform(-0.3, 0.3))) * size * 0.9))
    return out


# ---------------------------------------------------------------- trees


def branch_tree(rnd, H, trunk_r, levels, spread, droop):
    """Trunk plus recursive branches as tapered rods. Returns (rod objects, twig end points with sizes)."""
    rods, ends = [], []
    # trunk in a few bent segments
    p = Vector((0, 0, 0))
    segs = 4
    lean = Vector((rnd.uniform(-0.25, 0.25), rnd.uniform(-0.25, 0.25), 0))
    trunk_pts = [p.copy()]
    for i in range(segs):
        q = p + Vector((0, 0, H * 0.62 / segs)) + lean * (H * 0.62 / segs) * 0.3 + Vector((rnd.uniform(-0.12, 0.12), rnd.uniform(-0.12, 0.12), 0))
        r0 = trunk_r * (1 - i / segs * 0.55)
        r1 = trunk_r * (1 - (i + 1) / segs * 0.55)
        rods.append(rod("trunk", p, q, r0, r1, "ZR_Bark", 8))
        trunk_pts.append(q.copy())
        p = q
    top = p

    def grow(start, direction, length, radius, level):
        d = direction.normalized()
        end = start + d * length
        end.z -= droop * length * level * 0.15
        rods.append(rod("branch", start, end, radius, radius * 0.55, "ZR_Bark", 6 if level == 1 else 5))
        if level >= levels:
            ends.append((end, length))
            return
        kids = rnd.randint(2, 3)
        for k in range(kids):
            t = rnd.uniform(0.45, 1.0)
            s = start + (end - start) * t
            a = rnd.uniform(0, math.pi * 2)
            nd = d * 0.6 + Vector((math.cos(a), math.sin(a), rnd.uniform(0.1, 0.7))) * spread
            grow(s, nd, length * rnd.uniform(0.5, 0.7), radius * 0.55, level + 1)
        ends.append((end, length * 0.8))

    n1 = rnd.randint(5, 7)
    for i in range(n1):
        t = rnd.uniform(0.45, 1.0)
        idx = min(int(t * segs), segs - 1)
        s = trunk_pts[idx] + (trunk_pts[idx + 1] - trunk_pts[idx]) * (t * segs - idx)
        a = i * 2 * math.pi / n1 + rnd.uniform(-0.4, 0.4)
        dvec = Vector((math.cos(a), math.sin(a), rnd.uniform(0.6, 1.2)))
        grow(s, dvec, H * rnd.uniform(0.28, 0.4), trunk_r * 0.45, 1)
    grow(top, Vector((rnd.uniform(-0.2, 0.2), rnd.uniform(-0.2, 0.2), 1)), H * 0.3, trunk_r * 0.4, 1)
    return rods, ends


def tree(col, name, seed, H=7.5, leafy=1.0, dry=False):
    """leafy: 1 = full crown, 0.4 = sparse half-dead crown, 0 = bare dead tree."""
    rnd = random.Random(seed)
    rods, ends = branch_tree(rnd, H, 0.22 if leafy > 0 else 0.18, 2 if leafy > 0 else 3, 0.8, 0.6 if leafy > 0 else 0.3)
    wood = join(rods, name)
    finish(wood, col, 60)
    if leafy <= 0:
        return wood
    crown = Vector((0, 0, H * 0.72))
    quads = []
    for end, length in ends:
        if rnd.random() > leafy:
            continue
        size = max(1.3, min(2.6, length * 1.1)) * rnd.uniform(0.85, 1.15)
        quads += crossed(end, size, rnd, n=2 if leafy < 0.8 else 3)
    # a few inner clumps fill the crown
    for i in range(int(6 * leafy)):
        c = crown + Vector((rnd.uniform(-1.4, 1.4), rnd.uniform(-1.4, 1.4), rnd.uniform(-0.8, 1.0)))
        quads += crossed(c, rnd.uniform(1.6, 2.4), rnd)
    leaves = cards_object(name + "_leaves", quads, "ZR_FoliageDry" if dry else "ZR_Foliage", normal_centre=crown)
    o = join([wood, leaves], name)
    return finish(o, col, cube_uv=False)


def bush(col, name, seed, dry=False):
    rnd = random.Random(seed)
    quads = []
    for i in range(5):
        c = Vector((rnd.uniform(-0.5, 0.5), rnd.uniform(-0.5, 0.5), rnd.uniform(0.35, 0.6)))
        quads += crossed(c, rnd.uniform(0.9, 1.3), rnd, n=2, tilt=0.5)
    o = cards_object(name, quads, "ZR_FoliageDry" if dry else "ZR_Foliage", normal_centre=(0, 0, -0.3))
    return finish(o, col, cube_uv=False)


def grass_clump(col, name, seed, tall=False):
    rnd = random.Random(seed)
    quads = []
    for i in range(3):
        a = i * math.pi / 3 + rnd.uniform(-0.2, 0.2)
        w = rnd.uniform(0.9, 1.3) * (1.3 if tall else 1.0)
        h = rnd.uniform(0.5, 0.7) * (1.6 if tall else 1.0)
        quads.append((Vector((rnd.uniform(-0.1, 0.1), rnd.uniform(-0.1, 0.1), h / 2)), Vector((math.cos(a), math.sin(a), 0)) * w, Vector((0, 0, h))))
    o = cards_object(name, quads, "ZR_GrassCard", up_normals=True)
    return finish(o, col, cube_uv=False)


def dry_weeds(col, name, seed, n=40, tall=1.0, green=0.5):
    """A clump of wild grass stalks made of real thin curved strips (no flat cards), roots sunk 3 cm.
    UV v runs root (0) to tip (1) so the material's gradient texture darkens roots and lightens tips.
    green = share of stalks using the greener gradient; the rest are straw."""
    rnd = random.Random(seed)
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    for i in range(n):
        a = rnd.uniform(0, math.pi * 2)
        r0 = rnd.uniform(0, 0.14) ** 1.3 / 0.14 ** 0.3  # most stalks near the middle
        root = Vector((math.cos(a) * r0, math.sin(a) * r0, -0.03))
        out = Vector((math.cos(a), math.sin(a), 0))
        h = rnd.uniform(0.25, 0.75) * tall * (1.0 - r0 * 1.5)  # shorter towards the edge
        lean = rnd.uniform(0.1, 0.45) + r0 * 1.5
        side = Vector((-out.y, out.x, 0)) * rnd.uniform(0.006, 0.012)
        segs = 3
        prev = None
        mi = 0 if rnd.random() < green else 1
        for k in range(segs + 1):
            t = k / segs
            p = root + out * lean * h * t * t + Vector((0, 0, h * t * (1 - 0.15 * t * t) + 0.03))
            w = side * (1 - t * 0.9)
            v0, v1 = bm.verts.new(p - w), bm.verts.new(p + w)
            if prev:
                f = bm.faces.new((prev[0], prev[1], v1, v0))
                f.material_index = mi
                t0 = (k - 1) / segs
                for loop, (u, v) in zip(f.loops, [(0, t0), (1, t0), (1, t), (0, t)]):
                    loop[uv].uv = (u, v)
            prev = (v0, v1)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    o.data.materials.append(mat("ZR_DryGrass"))
    o.data.materials.append(mat("ZR_DryGrassDark"))
    bpy.context.view_layer.objects.active = o
    return finish(o, col, cube_uv=False)  # keep the root-to-tip UVs; the Unity material draws both sides


def dead_shrub(col, name, seed, size=1.0):
    """A low tangle of bare twigs from a common root, like a dead bush."""
    rnd = random.Random(seed)
    rods = []

    def twig(start, d, length, radius, level):
        end = start + d.normalized() * length
        rods.append(rod("twig", start, end, radius, radius * 0.5, "ZR_Bark", 4))
        if level < 3:
            for k in range(rnd.randint(1, 3)):
                a = rnd.uniform(0, math.pi * 2)
                nd = d.normalized() * 0.7 + Vector((math.cos(a), math.sin(a), rnd.uniform(0.0, 0.6))) * 0.6
                twig(start + (end - start) * rnd.uniform(0.5, 1.0), nd, length * rnd.uniform(0.45, 0.65), radius * 0.6, level + 1)

    for i in range(rnd.randint(5, 7)):
        a = rnd.uniform(0, math.pi * 2)
        twig(Vector((0, 0, 0)), Vector((math.cos(a) * 0.6, math.sin(a) * 0.6, 1.0)), rnd.uniform(0.5, 0.9) * size, 0.025 * size, 1)
    o = join(rods, name)
    return finish(o, col, 60)


def leaf_mass(name, centres, rnd, count, leaf, spread, normal_centre):
    """Hundreds of small leaves (each a 2-triangle diamond) in rounded masses round the given centres.
    Small real leaves read as foliage from any angle, unlike a few big flat cards.
    centres: list of (point, radius). Normals bend away from normal_centre for soft shading."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    per = max(1, count // len(centres))
    from mathutils import Matrix
    for c, rad in centres:
        c = Vector(c)
        # a dense dark core so the crown never shows sky through it; the small leaves sit on its surface
        core = bmesh.ops.create_icosphere(bm, subdivisions=2, radius=rad * 0.72, matrix=Matrix.Translation(c) @ Matrix.Diagonal((1, 1, 0.8, 1)))
        for v in core["verts"]:
            v.co += Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1))) * rad * 0.16
        for f in {f for v in core["verts"] for f in v.link_faces}:
            f.material_index = 3  # dark shadowed inside
        for i in range(per):
            # a point in a squashed ball, denser towards the outside where you see it
            d = Vector((rnd.gauss(0, 1), rnd.gauss(0, 1), rnd.gauss(0, 0.75))).normalized()
            p = c + d * rad * (0.7 + 0.32 * rnd.random() ** 0.7)  # on and just outside the core
            out = (p - c).normalized() if (p - c).length > 1e-4 else Vector((0, 0, 1))
            t1 = out.cross(Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1)))).normalized()
            t2 = out.cross(t1).normalized()
            L = leaf * rnd.uniform(0.7, 1.3)
            W = L * rnd.uniform(0.35, 0.55)
            tip = p + t1 * L * 0.5 + out * L * spread
            base = p - t1 * L * 0.5
            vs = [bm.verts.new(base), bm.verts.new(p + t2 * W * 0.5), bm.verts.new(tip), bm.verts.new(p - t2 * W * 0.5)]
            f = bm.faces.new(vs)
            f.material_index = rnd.choice((0, 0, 1, 1, 1, 2))
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    for m in ("ZR_LeafA", "ZR_LeafB", "ZR_LeafC", "ZR_LeafCore"):
        o.data.materials.append(mat(m))
    nc = Vector(normal_centre)
    me.normals_split_custom_set_from_vertices([(v.co - nc).normalized() for v in me.vertices])
    return o


def leafy_bush(col, name, seed, size=1.0, leaves=650):
    rnd = random.Random(seed)
    centres = [((rnd.uniform(-0.35, 0.35) * size, rnd.uniform(-0.35, 0.35) * size, rnd.uniform(0.35, 0.55) * size), rnd.uniform(0.38, 0.55) * size) for _ in range(3)]
    o = leaf_mass(name, centres, rnd, leaves, 0.13 * size, 0.12, (0, 0, -0.4 * size))
    return finish(o, col, cube_uv=True)


def leafy_tree(col, name, seed, H=7.0, leaves=3400):
    rnd = random.Random(seed)
    rods, ends = branch_tree(rnd, H, 0.2, 2, 0.8, 0.5)
    wood = join(rods, name)
    finish(wood, col, 60)
    crown = Vector((0, 0, H * 0.72))
    centres = [(e, max(0.6, min(1.4, l * 0.7))) for e, l in ends]
    centres += [(crown + Vector((rnd.uniform(-1.2, 1.2), rnd.uniform(-1.2, 1.2), rnd.uniform(-0.6, 0.9))), rnd.uniform(1.0, 1.5)) for _ in range(4)]
    leaves_o = leaf_mass(name + "_leaves", centres, rnd, leaves, 0.27, 0.1, crown - Vector((0, 0, 1.5)))
    bpy.ops.object.select_all(action="DESELECT")
    leaves_o.select_set(True)
    bpy.context.view_layer.objects.active = leaves_o
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.cube_project(cube_size=TILE)
    bpy.ops.object.mode_set(mode="OBJECT")
    o = join([wood, leaves_o], name)
    return finish(o, col, cube_uv=False)


def ivy_card(col, name="ZR_IvyCard"):
    """One 1 x 1 m ivy card: pivot at the bottom middle, faces +Y (away from the wall). Placement scales it."""
    o = cards_object(name, [(Vector((0, 0.0, 0.5)), Vector((1, 0, 0)), Vector((0, 0, 1)))], "ZR_IvyCard")
    me = o.data
    me.normals_split_custom_set_from_vertices([Vector((0, 1, 0.25)).normalized()] * len(me.vertices))
    return finish(o, col, cube_uv=False)


# ---------------------------------------------------------------- base walls


def hesco(col, name="ZR_Hesco", cells=3, cell=1.1, H=1.6, D=1.1):
    """Wire-mesh sand bastion: a row of filled cells, the standard military base wall."""
    parts = []
    L = cells * cell
    body = box(name + "_fill", (D, L, H), (0, 0, H / 2), "ZR_Hesco", bevel=0.04)
    for v in body.data.vertices:  # bulge the fabric between frames a little
        if abs(v.co.x) > D * 0.45 and 0.1 < v.co.z < H - 0.1:
            v.co.x *= 1.03
    parts.append(body)
    # sand heaped on top
    top = box(name + "_top", (D * 0.9, L * 0.96, 0.12), (0, 0, H + 0.04), "ZR_Sand", bevel=0.05)
    city["dent"](top, 0.03, 3)
    parts.append(top)
    # wire frame: posts at the cell joints and a grid on the long faces
    for i in range(cells + 1):
        y = -L / 2 + i * cell
        for sx in (-1, 1):
            parts.append(box(name + "_post", (0.04, 0.04, H + 0.02), (sx * (D / 2 + 0.02), y, H / 2), "ZR_Wire"))
    for z in np.linspace(0.15, H - 0.1, 6):
        for sx in (-1, 1):
            parts.append(box(name + "_wire", (0.015, L, 0.015), (sx * (D / 2 + 0.025), 0, float(z)), "ZR_Wire"))
    for i in range(cells * 4 + 1):
        y = -L / 2 + i * cell / 4
        for sx in (-1, 1):
            parts.append(box(name + "_wirev", (0.015, 0.015, H), (sx * (D / 2 + 0.025), y, H / 2), "ZR_Wire"))
    o = join(parts, name)
    return city["finish"](o, col)


def sandbag_wall_tall(col, name="ZR_SandbagWall_Tall", length=3.0, rows=7, seed=15):
    """Two bags deep, seven rows high (about 1.3 m), with the top row stepped in."""
    rnd = random.Random(seed)
    parts = []
    bag_l, bag_w, bag_h = 0.62, 0.36, 0.19
    for r in range(rows):
        n = int(length / bag_l)
        off = (bag_l / 2) if r % 2 else 0
        depths = (-0.2, 0.2) if r < rows - 1 else (0.0,)
        for i in range(n - (r % 2)):
            for depth in depths:
                parts.append(city["sandbag"]((depth + rnd.uniform(-0.02, 0.02), -length / 2 + bag_l / 2 + off + i * bag_l, bag_h / 2 + r * bag_h * 0.95),
                                             bag_l, bag_w, bag_h, rnd))
    o = join(parts, name)
    return city["finish"](o, col, 60)


# ---------------------------------------------------------------- textures


def draw_leaf(img, cx, cy, ang, L, W, col):
    """Paint one pointed leaf (RGBA float image, y down) with a darker midrib."""
    h, w, _ = img.shape
    r = int(max(L, W)) + 2
    x0, x1 = max(0, int(cx - r)), min(w, int(cx + r))
    y0, y1 = max(0, int(cy - r)), min(h, int(cy + r))
    if x0 >= x1 or y0 >= y1:
        return
    ys, xs = np.mgrid[y0:y1, x0:x1].astype(np.float32)
    dx, dy = xs - cx, ys - cy
    ca, sa = math.cos(ang), math.sin(ang)
    u = dx * ca + dy * sa          # along the leaf, 0 at the stem
    v = -dx * sa + dy * ca
    t = u / L
    width = W * np.clip(np.sin(np.clip(t, 0, 1) * math.pi) ** 0.8, 0, 1)
    inside = (t >= 0) & (t <= 1) & (np.abs(v) <= width)
    if not inside.any():
        return
    shade = 1.0 - 0.25 * (np.abs(v) / np.maximum(width, 1e-3)) - 0.35 * (np.abs(v) < 0.6)
    sub = img[y0:y1, x0:x1]
    for k in range(3):
        sub[..., k] = np.where(inside, col[k] * shade, sub[..., k])
    sub[..., 3] = np.where(inside, 1.0, sub[..., 3])


def save_rgba(path, img):
    h, w, _ = img.shape
    im = bpy.data.images.new(os.path.basename(path), w, h, alpha=True)
    im.pixels.foreach_set(np.flipud(img).ravel())  # Blender stores rows bottom-up
    im.filepath_raw = path
    im.file_format = "PNG"
    im.save()
    bpy.data.images.remove(im)


def leaf_palette(rnd, dry):
    if dry:
        base = [(0.36, 0.33, 0.14), (0.42, 0.34, 0.16), (0.30, 0.30, 0.13), (0.38, 0.24, 0.10)]
    else:
        base = [(0.14, 0.22, 0.08), (0.20, 0.28, 0.10), (0.11, 0.17, 0.07), (0.25, 0.30, 0.12), (0.30, 0.28, 0.12)]
    c = rnd.choice(base)
    k = rnd.uniform(0.8, 1.15)
    return tuple(min(1, x * k) for x in c)


def textures():
    rnd = random.Random(77)
    S = 512
    # leaf cluster: dense round clump with a ragged edge, darker inside, twigs
    for dry, fname in ((False, "ZR_LeafCluster.png"), (True, "ZR_LeafClusterDry.png")):
        img = np.zeros((S, S, 4), np.float32)
        lobes = [(rnd.uniform(0.05, 0.10), rnd.randint(4, 7), rnd.uniform(0, 6.28)) for _ in range(2)]
        for i in range(2200):
            a = rnd.uniform(0, math.pi * 2)
            edge = 1.0 + sum(amp * math.sin(k * a + ph) for amp, k, ph in lobes)  # ragged, lobed outline
            r = S * 0.36 * edge * rnd.random() ** 0.6
            cx, cy = S / 2 + math.cos(a) * r, S / 2 + math.sin(a) * r
            dark = 0.6 + 0.4 * min(1.0, r / (S * 0.42))   # inner leaves in shadow
            col = tuple(x * dark for x in leaf_palette(rnd, dry))
            draw_leaf(img, cx, cy, rnd.uniform(0, math.pi * 2), rnd.uniform(14, 26), rnd.uniform(5, 9), col)
        save_rgba(os.path.join(TEX, fname), img)
    # ivy: vines hanging from the top, heart-ish leaves along them, thicker at the top
    img = np.zeros((S, S, 4), np.float32)
    for s in range(16):
        x = rnd.uniform(0.04, 0.96) * S
        length = rnd.uniform(0.45, 1.0) * S
        y = 0.0
        while y < length:
            x += rnd.uniform(-2.5, 2.5)
            yy = int(y)
            img[yy:yy + 3, max(0, int(x) - 1):int(x) + 2, :3] = (0.12, 0.10, 0.06)
            img[yy:yy + 3, max(0, int(x) - 1):int(x) + 2, 3] = 1.0
            if rnd.random() < 0.55:
                side = rnd.choice((-1, 1))
                draw_leaf(img, x, y, math.pi / 2 + side * rnd.uniform(0.4, 1.2), rnd.uniform(13, 22) * (1.2 - y / S * 0.5), rnd.uniform(7, 11), leaf_palette(rnd, rnd.random() < 0.12))
            y += rnd.uniform(4, 8)
    # a dense mat along the top edge
    for i in range(500):
        draw_leaf(img, rnd.uniform(0, S), rnd.uniform(0, S * 0.25) ** 1.0, rnd.uniform(0, math.pi * 2), rnd.uniform(14, 22), rnd.uniform(7, 10), leaf_palette(rnd, rnd.random() < 0.1))
    save_rgba(os.path.join(TEX, "ZR_IvyCard.png"), img)
    # grass tuft: blades rise from a narrow root in the middle and fan out; the card's sides stay clear
    img = np.zeros((S, S, 4), np.float32)
    for i in range(170):
        off = rnd.gauss(0, 0.11)                      # root spread round the middle
        x0 = S * (0.5 + off)
        h = S * rnd.uniform(0.35, 0.95) * (1.0 - abs(off) * 1.6)
        lean = off * 2.2 + rnd.uniform(-0.18, 0.18)   # outer blades lean outward
        w = rnd.uniform(1.6, 3.4)
        col = rnd.choice([(0.24, 0.30, 0.10), (0.30, 0.34, 0.13), (0.40, 0.37, 0.17), (0.18, 0.24, 0.08), (0.34, 0.29, 0.13)])
        for k in range(int(h)):
            t = k / h
            x = x0 + lean * h * t * (0.6 + 0.4 * t)
            ww = w * (1 - t) + 0.5
            yy = S - 1 - k
            lo, hi = max(0, int(x - ww)), min(S, max(0, int(x + ww) + 1))
            if hi <= lo:
                continue
            img[yy, lo:hi, :3] = [c * (0.6 + 0.4 * t) for c in col]
            img[yy, lo:hi, 3] = 1.0
    for i in range(10):  # a few weed leaves low down
        draw_leaf(img, S * (0.5 + rnd.gauss(0, 0.12)), S - rnd.uniform(5, S * 0.3), -math.pi / 2 + rnd.uniform(-0.9, 0.9), rnd.uniform(20, 34), rnd.uniform(6, 10), leaf_palette(rnd, False))
    save_rgba(os.path.join(TEX, "ZR_GrassCard.png"), img)
    # stalk gradients (root at the bottom row of the image = v 0): green grass and dry straw
    for fname, stops in (("ZR_StalkGreen.png", [(0.0, (0.07, 0.10, 0.03)), (0.3, (0.18, 0.30, 0.08)), (0.75, (0.30, 0.45, 0.13)), (1.0, (0.45, 0.55, 0.22))]),
                         ("ZR_StalkStraw.png", [(0.0, (0.08, 0.07, 0.04)), (0.35, (0.24, 0.21, 0.10)), (0.75, (0.45, 0.40, 0.22)), (1.0, (0.62, 0.56, 0.36))])):
        H = 64
        g = np.zeros((H, 8, 4), np.float32)
        for y in range(H):
            t = 1.0 - y / (H - 1)  # image row 0 is the top (tip)
            for (t0, c0), (t1, c1) in zip(stops[:-1], stops[1:]):
                if t0 <= t <= t1:
                    f = (t - t0) / (t1 - t0)
                    g[y, :, :3] = [c0[i] + (c1[i] - c0[i]) * f for i in range(3)]
            g[y, :, 3] = 1.0
        save_rgba(os.path.join(TEX, fname), g)
    # moss ground (tileable, no alpha): patchy greens over damp brown
    n1 = city["tile_noise"](S, 6, 5, 21)
    n2 = city["tile_noise"](S, 24, 3, 22)
    n3 = city["tile_noise"](S, 64, 2, 23)
    green = np.clip((n1 - 0.24) * 2.4, 0, 1)  # patchy: overgrowth with bare earth between
    base = np.stack([0.20 + 0.08 * n2, 0.18 + 0.06 * n2, 0.12 + 0.04 * n2], -1)
    blades = city["tile_noise"](S, 128, 2, 24)
    moss = np.stack([0.11 + 0.07 * n3 + 0.04 * blades, 0.23 + 0.12 * n3 + 0.07 * blades, 0.07 + 0.03 * n3], -1)  # weedy green, not lawn
    rgb = base * (1 - green[..., None]) + moss * green[..., None]
    city["save_png"](os.path.join(TEX, "ZR_MossGround.png"), rgb)


def main():
    reset()
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(TEX, exist_ok=True)
    textures()
    trees = collection("Trees")
    plants = collection("Plants")
    walls = collection("BaseWalls")
    props = [
        tree(trees, "ZR_TreeOak", 101, H=8.0, leafy=1.0),
        tree(trees, "ZR_TreeOak_B", 102, H=7.0, leafy=1.0),
        tree(trees, "ZR_TreeSparse", 103, H=7.5, leafy=0.45, dry=True),
        tree(trees, "ZR_TreeBare", 104, H=7.0, leafy=0.0),
        tree(trees, "ZR_TreeBare_B", 105, H=5.5, leafy=0.0),
        bush(plants, "ZR_BushWild", 201), bush(plants, "ZR_BushWild_Dry", 202, dry=True),
        dry_weeds(plants, "ZR_DryWeeds", 401, green=0.65), dry_weeds(plants, "ZR_DryWeeds_Tall", 402, n=30, tall=1.5, green=0.35),
        dead_shrub(plants, "ZR_DeadShrub", 501), dead_shrub(plants, "ZR_DeadShrub_B", 502, size=1.4),
        dry_weeds(plants, "ZR_WildGrass", 403, n=48, tall=1.15, green=0.9), dry_weeds(plants, "ZR_WildGrass_Tall", 404, n=40, tall=1.7, green=0.75),
        leafy_bush(plants, "ZR_LeafyBush", 601), leafy_bush(plants, "ZR_LeafyBush_B", 602, size=1.35, leaves=850),
        leafy_tree(trees, "ZR_LeafyTree", 701, H=7.5), leafy_tree(trees, "ZR_LeafyTree_B", 702, H=6.0, leaves=2600),
        grass_clump(plants, "ZR_GrassClump", 301), grass_clump(plants, "ZR_GrassClump_Tall", 302, tall=True),
        ivy_card(plants),
        hesco(walls), sandbag_wall_tall(walls),
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
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "OvergrowthProps.blend"))
    print("ZR_PROPS_DONE")
    for line in report:
        print("ZR_PROP " + line)


main()
