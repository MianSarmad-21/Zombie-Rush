"""Repairs the player's face mesh (2026-11-14). The dark marks on the face (a star-shaped crack by the right ear,
a pit at the nose tip, notches at the mouth corners) were small HOLES (the inside of the head showing through)
and inward-facing faces (culled in Unity, so see-through).

repair_face() (used): fills small boundary loops (<= 8 edges, < 9 cm) whose neighbours are skin-coloured - the eye
sockets and the hair are larger / not skin, so they stay - copying UVs from the neighbours, and flips skin faces
that point against their neighbours. fix_head() (smoothing) is kept for reference only: it melted the hair.

Usage inside any Blender with the body mesh:
    exec(open(".../face_mesh_fix.py").read()); repair_face(mesh_obj, report=True)
or background, on the shipped FBX (writes a preview render pair to --preview, and with --export re-exports
ShadowOperative.fbx, the original kept as ShadowOperative_orig.fbx):
    blender -b --factory-startup -P Blender/face_mesh_fix.py -- [--preview DIR] [--export]

Defects: head faces (Head/Neck weighted) whose normal turns against their neighbours' (folds), and vertices that
stick out of their 1-ring (spikes). Fix: Laplacian-smooth those vertices and their ring a few times; UVs and
weights stay, so the texture stays aligned and the rig is unchanged.
"""
import math
import os
import shutil
import sys

import bmesh
import bpy
from mathutils import Vector

FOLDER = "C:/Mian Sarmad/Unity/Zombie-Rush/Zombie Rush/Assets/Models/2026-11-14_ShadowOperative_Final/"


def head_verts(obj, groups=("Head", "Neck"), share=0.5):
    gi = {g.name: g.index for g in obj.vertex_groups}
    hg = {gi[n] for n in groups if n in gi}
    out = set()
    for v in obj.data.vertices:
        tot = sum(g.weight for g in v.groups) or 1.0
        if sum(g.weight for g in v.groups if g.group in hg) / tot > share: out.add(v.index)
    return out


def find_defects(bm, region, fold_dot=0.2, spike=0.35):
    bad = set()
    bm.normal_update()
    for f in bm.faces:
        if not all(v.index in region for v in f.verts): continue
        nb = [l.link_loop_radial_next.face for l in f.loops if l.link_loop_radial_next.face is not f]
        if not nb: continue
        avg = sum((g.normal for g in nb), Vector())
        if avg.length < 1e-6: continue
        if f.normal.dot(avg.normalized()) < fold_dot:
            bad.update(v.index for v in f.verts)
    for v in bm.verts:
        if v.index not in region or not v.link_edges: continue
        ring = [e.other_vert(v) for e in v.link_edges]
        c = sum((u.co for u in ring), Vector()) / len(ring)
        el = sum(e.calc_length() for e in v.link_edges) / len(v.link_edges)
        n = v.normal
        if el > 1e-6 and abs((v.co - c).dot(n)) / el > spike:
            bad.add(v.index)
    return bad


def fix_head(obj, iterations=6, factor=0.5, rounds=3, report=False):
    me = obj.data
    region = head_verts(obj)
    bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
    total = set()
    for r in range(rounds):
        bad = find_defects(bm, region)
        if report: print(f"[facefix] round {r}: {len(bad)} defect vertices")
        if not bad: break
        total |= bad
        grow = set(bad)
        for i in bad:
            grow.update(e.other_vert(bm.verts[i]).index for e in bm.verts[i].link_edges)
        grow &= region
        for _ in range(iterations):
            new = {}
            for i in grow:
                v = bm.verts[i]
                ring = [e.other_vert(v) for e in v.link_edges]
                if not ring or v.is_boundary: continue
                c = sum((u.co for u in ring), Vector()) / len(ring)
                new[i] = v.co.lerp(c, factor)
            for i, co in new.items(): bm.verts[i].co = co
        bm.normal_update()
    bm.to_mesh(me); bm.free(); me.update()
    if report: print(f"[facefix] moved {len(total)} vertices in total (head region {len(region)})")
    return total


def _skin_sampler(obj):
    """returns f(face) -> True when the albedo under the face centre is skin-coloured"""
    img = None
    for m in obj.data.materials:
        if m and m.use_nodes:
            for n in m.node_tree.nodes:
                if n.type == "TEX_IMAGE" and n.image and "Albedo" in n.image.name: img = n.image
    if img is None: return lambda f, uvl: True
    W, H = img.size; px = img.pixels[:]

    def is_skin(f, uvl):
        u = sum((l[uvl].uv for l in f.loops), Vector((0, 0))) / len(f.loops)
        x, y = min(W - 1, max(0, int(u.x * W))), min(H - 1, max(0, int(u.y * H)))
        i = (y * W + x) * 4; r, g, b = px[i], px[i + 1], px[i + 2]
        return r > g and g > b * 0.95 and r > 0.28 and r - b > 0.08
    return is_skin


def repair_face(obj, max_hole_edges=8, max_perimeter=0.09, report=False):
    """Fill the small holes on the face skin and turn inward-facing skin faces outwards. Eye sockets (larger
    openings), hair and everything off the head are left alone. Returns (holes filled, faces flipped)."""
    region = head_verts(obj)
    is_skin = _skin_sampler(obj)
    bm = bmesh.new(); bm.from_mesh(obj.data); bm.verts.ensure_lookup_table(); bm.faces.ensure_lookup_table()
    uvl = bm.loops.layers.uv.active
    # --- holes
    seen, filled = set(), 0
    for e in [e for e in bm.edges if e.is_boundary and all(v.index in region for v in e.verts)]:
        if e in seen: continue
        loop, stack = [], [e]
        while stack:
            x = stack.pop()
            if x in seen: continue
            seen.add(x); loop.append(x)
            for v in x.verts:
                stack += [y for y in v.link_edges if y.is_boundary and y not in seen]
        per = sum(x.calc_length() for x in loop)
        if len(loop) > max_hole_edges or per > max_perimeter: continue
        nbf = [f for x in loop for f in x.link_faces]
        if not any(is_skin(f, uvl) for f in nbf): continue              # hair / clothing holes stay
        res = bmesh.ops.holes_fill(bm, edges=loop, sides=max_hole_edges + 1)
        for f in res.get("faces", []):
            f.material_index = nbf[0].material_index
            for l in f.loops:                                            # UV from the vertex's existing loops
                src = next((m for m in l.vert.link_loops if m.face is not f and m.face not in res["faces"]), None)
                if src is not None: l[uvl].uv = src[uvl].uv.copy()
            filled += 1
    # --- inward skin faces: flip the ones that point against their neighbours
    bm.normal_update(); flipped = 0
    for f in list(bm.faces):
        if not all(v.index in region for v in f.verts) or not is_skin(f, uvl): continue
        nb = [l.link_loop_radial_next.face for l in f.loops if l.link_loop_radial_next.face is not f]
        if len(nb) < 2: continue
        avg = sum((g.normal for g in nb), Vector())
        if avg.length > 1e-6 and f.normal.dot(avg.normalized()) < -0.5:
            f.normal_flip(); flipped += 1
    bm.normal_update()
    bm.to_mesh(obj.data); bm.free(); obj.data.update()
    if report: print(f"[facefix] filled {filled} hole faces, flipped {flipped} inward skin faces")
    return filled, flipped


if __name__ == "__main__" and bpy.app.background and "--" in sys.argv:
    argv = sys.argv[sys.argv.index("--") + 1:]
    PREVIEW = argv[argv.index("--preview") + 1] if "--preview" in argv else None
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    bpy.ops.import_scene.fbx(filepath=FOLDER + "ShadowOperative.fbx")
    rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    mesh = max((o for o in bpy.data.objects if o.type == "MESH"), key=lambda o: len(o.data.vertices))
    sc = bpy.context.scene

    def render(tag):
        for eng in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
            try: sc.render.engine = eng; break
            except TypeError: pass
        sc.render.resolution_x = sc.render.resolution_y = 600
        if sc.world is None: sc.world = bpy.data.worlds.new("W")
        sc.world.color = (0.45, 0.45, 0.45)
        if "L" not in bpy.data.objects:
            L = bpy.data.objects.new("L", bpy.data.lights.new("L", "SUN")); sc.collection.objects.link(L)
            L.rotation_euler = (math.radians(50), 0, math.radians(-20)); L.data.energy = 3
        cam = bpy.data.objects.get("C") or bpy.data.objects.new("C", bpy.data.cameras.new("C"))
        if cam.name not in sc.collection.objects: sc.collection.objects.link(cam)
        hb = rig.pose.bones["Head"]; hp = rig.matrix_world @ ((hb.head + hb.tail) * 0.5)
        for view, d in (("34", Vector((-0.6, -1, 0.05))), ("front", Vector((0, -1, 0.05)))):
            cam.location = hp + d.normalized() * 0.7; cam.rotation_euler = (hp - cam.location).to_track_quat("-Z", "Y").to_euler()
            cam.data.lens = 70; sc.camera = cam
            sc.render.filepath = os.path.join(PREVIEW, f"{tag}_{view}.png"); bpy.ops.render.render(write_still=True)

    if PREVIEW: os.makedirs(PREVIEW, exist_ok=True); render("before")
    repair_face(mesh, report=True)
    if PREVIEW: render("after")
    if "--export" in argv:
        orig = FOLDER + "ShadowOperative_orig.fbx"
        if not os.path.exists(orig): shutil.copy(FOLDER + "ShadowOperative.fbx", orig)
        for o in bpy.data.objects: o.select_set(o in (rig, mesh))
        bpy.context.view_layer.objects.active = rig
        bpy.ops.export_scene.fbx(filepath=FOLDER + "ShadowOperative.fbx", use_selection=True, object_types={"ARMATURE", "MESH"},
                                 add_leaf_bones=False, apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                                 bake_space_transform=False, bake_anim=False, path_mode="RELATIVE")
        print("[facefix] exported", FOLDER + "ShadowOperative.fbx")
