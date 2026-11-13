"""Strip the rubble skirts off the city pack's ruined buildings.

Run:  blender -b -P Blender/clean_buildings.py -- [names...]
For each building FBX in Assets/PostApocalypticBuildings/Models (and its -lod1/-lod2), writes
Assets/Models/2026-11-09_CleanBuildings/<name>_clean.fbx:
  - the standing structure is kept as it is (walls, frames, floors),
  - loose debris lying on the ground outside the walls is removed,
  - the rubble mound under the building is pulled down into the ground outside the walls,
    so it fades out over FADE metres instead of spreading 8-15 m onto the street.
The mesh is edited in its own local space and exported with the same axis settings as the pack,
so Unity can swap it in place of the original mesh (same position, rotation, materials).
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
SRC = os.path.join(PROJECT, "Assets", "PostApocalypticBuildings", "Models")
OUT = os.path.join(PROJECT, "Assets", "Models", "2026-11-09_CleanBuildings")

DEFAULT = ["building9", "building9-variation1", "building10", "building10-variant1",
           "building8", "building8-variation1", "building8-variation2"]

CORE_SHARE = 0.45       # an island reaching above this share of the building height is standing structure
WALL_MARGIN = 1.0       # m outside the walls where debris may stay
FADE = 3.0              # m over which the mound sinks into the ground
BURY = -1.0             # height the mound sinks to
SKIRT_SPAN = 0.75       # an island wider than this share of the building is a skirt / mound


def islands(bm):
    seen, out = set(), []
    for f in bm.faces:
        if f.index in seen:
            continue
        stack, isl = [f], []
        seen.add(f.index)
        while stack:
            cur = stack.pop()
            isl.append(cur)
            for e in cur.edges:
                for g in e.link_faces:
                    if g.index not in seen:
                        seen.add(g.index)
                        stack.append(g)
        out.append(isl)
    return out


def clean(name, rect=None):
    path = os.path.join(SRC, name + ".fbx")
    if not os.path.exists(path):
        return None, rect
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    objs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if len(objs) != 1:
        return f"{name}: skipped, {len(objs)} meshes", rect
    o = objs[0]
    mw = o.matrix_world
    inv = mw.inverted()
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bm.faces.ensure_lookup_table()
    world = {v.index: mw @ v.co for v in bm.verts}

    isl = islands(bm)
    info = []
    allx = [p.x for p in world.values()]
    ally = [p.y for p in world.values()]
    span = max(max(allx) - min(allx), max(ally) - min(ally))
    for faces in isl:
        vs = {v.index for f in faces for v in f.verts}
        xs = [world[i].x for i in vs]
        ys = [world[i].y for i in vs]
        zs = [world[i].z for i in vs]
        info.append(dict(faces=faces, vs=vs, x0=min(xs), x1=max(xs), y0=min(ys), y1=max(ys), z0=min(zs), z1=max(zs),
                         w=max(max(xs) - min(xs), max(ys) - min(ys))))
    height = max(i["z1"] for i in info)
    for i in info:
        i["skirt"] = i["w"] > span * SKIRT_SPAN and i["z1"] < height * 0.6
        i["core"] = (not i["skirt"]) and i["z1"] > height * CORE_SHARE
    if rect is None:  # LOD copies reuse the walls found on the full-detail model
        core = [i for i in info if i["core"]]
        if not core:
            return f"{name}: skipped, no standing structure found", rect
        rect = (min(i["x0"] for i in core), max(i["x1"] for i in core), min(i["y0"] for i in core), max(i["y1"] for i in core))
    x0, x1, y0, y1 = rect

    def outside(p):
        dx = max(x0 - p.x, p.x - x1, 0.0)
        dy = max(y0 - p.y, p.y - y1, 0.0)
        return math.hypot(dx, dy)

    kill = set()
    moved = 0
    for i in info:
        if i["core"]:
            continue
        cx, cy = (i["x0"] + i["x1"]) / 2, (i["y0"] + i["y1"]) / 2
        if not i["skirt"] and outside(Vector((cx, cy, 0))) > WALL_MARGIN:
            kill.update(f.index for f in i["faces"])
        elif i["skirt"]:
            for vi in i["vs"]:
                p = world[vi]
                d = outside(p)
                if d > 0:
                    t = min(d / FADE, 1.0)
                    p2 = Vector((p.x, p.y, p.z + (min(p.z, BURY) - p.z) * t))
                    world[vi] = p2
                    moved += 1
    bm.verts.ensure_lookup_table()
    for vi, p in world.items():
        bm.verts[vi].co = inv @ p
    # faces of the mound now fully underground: gone
    for i in info:
        if i["skirt"]:
            for f in i["faces"]:
                if all(world[v.index].z <= BURY + 0.01 for v in f.verts):
                    kill.add(f.index)
    before = len(bm.faces)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.index in kill], context="FACES")
    bm.to_mesh(o.data)
    bm.free()
    o.data.update()
    after = len(o.data.polygons)

    os.makedirs(OUT, exist_ok=True)
    o.name = name + "_clean"
    o.data.name = name + "_clean"
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + "_clean.fbx"), use_selection=True, object_types={"MESH"},
                             axis_forward="-Z", axis_up="Y", apply_unit_scale=True, apply_scale_options="FBX_SCALE_NONE",
                             bake_space_transform=False, mesh_smooth_type="OFF", use_mesh_modifiers=False,
                             path_mode="STRIP", embed_textures=False, add_leaf_bones=False, bake_anim=False)
    return (f"{name}: {before} -> {after} faces ({100 * (before - after) / max(before, 1):.0f}% removed), "
            f"walls {x1 - x0:.1f} x {y1 - y0:.1f} m, footprint was {span:.1f} m, {moved} mound points sunk"), rect


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    names = argv or DEFAULT
    for n in names:
        msg, rect = clean(n)
        print("ZR_CLEAN " + str(msg))
        if rect is None:
            continue
        for lod in ("-lod1", "-lod2"):
            msg2, _ = clean(n + lod, rect)
            if msg2:
                print("ZR_CLEAN " + msg2)


main()
