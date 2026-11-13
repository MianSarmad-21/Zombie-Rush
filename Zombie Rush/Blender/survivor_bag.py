"""Take the backpack off the player body and put it back as a rigid piece on the upper chest.

Run:  blender -b -P Blender/survivor_bag.py -- [preview_dir|-] [out_fbx]   (out_fbx: a trial copy instead of the game's FBX)
The Meshy survivor's backpack (with the crowbar and machete strapped to it) is fused into the body
mesh and was skinned to the spine, shoulders and arms by the auto-rig, so it bent and wobbled with
every step. This script:
  1. reads the body with the bag (WastelandSurvivor_WithBag.fbx.bak, made from the current
     WastelandSurvivor.fbx the first time it runs, so it can be rerun safely);
  2. finds the bag by its texture islands: islands lying behind the back (more than 60 % of their
     area beyond BACK_Y) within the bag's width and height;
  3. cuts those faces out into their own mesh "Backpack", skinned 100 % to UpperChest (so it moves
     as one rigid piece with the upper back), and closes the holes they leave in the body;
  4. writes WastelandSurvivor.fbx back with both meshes and every clip (takes "Armature.002|<clip>";
     MeshyBodyTools finds clips by the part after the last "|" and re-applies loop settings itself).
This is the last step that writes WastelandSurvivor.fbx: survivor_fingers.py builds from the
no-finger backup and must not be run after it (or run this again after it).
"""
import os
import shutil
import sys

import bmesh
import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
FOLDER = os.path.join(PROJECT, "Assets", "Models", "2026-11-08_WastelandSurvivor_Final")
FBX = os.path.join(FOLDER, "WastelandSurvivor.fbx")
BACKUP = os.path.join(FOLDER, "WastelandSurvivor_WithBag.fbx.bak")
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
PREVIEW = args[0] if args and args[0] != "-" else None
OUT = args[1] if len(args) > 1 else FBX

BACK_Y = 0.10          # metres behind the body's centre line (+Y is the back) where the bag starts
BAG_HALF_WIDTH = 0.36  # the bag and its tools stay within this distance of the centre line
BAG_LOWEST = 0.55      # no part of the bag reaches below this height (m)
STICK_OUT_Y = 0.13     # any body face with a corner further behind than this goes with the bag
BAG_BONE = "UpperChest"
ARM_NODE = "Armature.002"   # the armature's node name in the game


def log(*a):
    print("ZR_BAG", *a, flush=True)


if not os.path.exists(BACKUP):
    shutil.copyfile(FBX, BACKUP)
    log("kept the body with its bag as", BACKUP)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=BACKUP)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
body = next(o for o in bpy.data.objects if o.type == "MESH")
# the importer names each clip "<object>|<take>": strip that so names do not grow a prefix every round trip
for a in bpy.data.actions:
    if a.name.startswith(arm.name + "|"):
        a.name = a.name[len(arm.name) + 1:]
log(f"{len(bpy.data.actions)} clips, {len(arm.data.bones)} bones, {len(body.data.vertices)} vertices")
arm.animation_data.action = None
for pb in arm.pose.bones:
    pb.rotation_quaternion = (1, 0, 0, 0); pb.rotation_euler = (0, 0, 0); pb.location = (0, 0, 0)
bpy.context.view_layer.update()

# ------------------------------------------------------------------ the bag: texture islands behind the back
bm = bmesh.new(); bm.from_mesh(body.data); bm.faces.ensure_lookup_table()
uv = bm.loops.layers.uv.active
def key(l): return (round(l[uv].uv.x, 5), round(l[uv].uv.y, 5))
island = [-1] * len(bm.faces); n = 0
for f in bm.faces:
    if island[f.index] >= 0: continue
    stack = [f]; island[f.index] = n
    while stack:
        x = stack.pop()
        for l in x.loops:
            for l2 in l.edge.link_loops:
                g = l2.face
                if g is x or island[g.index] >= 0: continue
                if {key(l), key(l.link_loop_next)} == {key(l2), key(l2.link_loop_next)}:
                    island[g.index] = n; stack.append(g)
    n += 1
MW = body.matrix_world
cent = np.array([tuple(MW @ f.calc_center_median()) for f in bm.faces])
area = np.array([f.calc_area() for f in bm.faces])
isl = np.array(island)
bag_isl = []
for i in range(n):
    m = isl == i; a = area[m]; c = cent[m]
    behind = a[c[:, 1] > BACK_Y].sum() / max(a.sum(), 1e-12)
    if behind > 0.6 and np.abs(c[:, 0]).max() < BAG_HALF_WIDTH and c[:, 2].min() > BAG_LOWEST:
        bag_isl.append(i)
bag = np.isin(isl, bag_isl)
bag = np.isin(isl, bag_isl)
# plus any single face with a corner sticking out past the jacket (a strap end of the machete lies
# close against the back, so its island fails the test above)
stray = [f.index for f in bm.faces if not bag[f.index] and max((MW @ v.co).y for v in f.verts) > STICK_OUT_Y
         and BAG_LOWEST < cent[f.index][2]]
bag[stray] = True
log(f"bag: {len(bag_isl)} of {n} texture islands + {len(stray)} stray faces, {int(bag.sum())} of {len(bag)} faces")
bm.free()

# ------------------------------------------------------------------ cut it out
def boundary_keys(mesh_obj):
    b = bmesh.new(); b.from_mesh(mesh_obj.data)
    ks = {tuple(sorted((tuple(round(c, 5) for c in e.verts[0].co), tuple(round(c, 5) for c in e.verts[1].co))))
          for e in b.edges if e.is_boundary}
    b.free(); return ks

before = boundary_keys(body)
bpy.ops.object.select_all(action="DESELECT")
body.select_set(True); bpy.context.view_layer.objects.active = body
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_mode(type="FACE")
bpy.ops.mesh.select_all(action="DESELECT")
bpy.ops.object.mode_set(mode="OBJECT")
for p in body.data.polygons:
    p.select = bool(bag[p.index])
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.separate(type="SELECTED")
bpy.ops.object.mode_set(mode="OBJECT")
bagobj = next(o for o in bpy.context.selected_objects if o is not body)
bagobj.name = "Backpack"; bagobj.data.name = "Backpack"
log(f"Backpack: {len(bagobj.data.vertices)} vertices; body now {len(body.data.vertices)}")

# close the holes the bag leaves in the body (under the bag, so a plain patch is enough); give the
# patch the texture of the jacket right next to it
b = bmesh.new(); b.from_mesh(body.data)
uvl = b.loops.layers.uv.active
new_edges = [e for e in b.edges if e.is_boundary and tuple(sorted((tuple(round(c, 5) for c in e.verts[0].co), tuple(round(c, 5) for c in e.verts[1].co)))) not in before]
res = bmesh.ops.holes_fill(b, edges=new_edges, sides=0)
filled = res.get("faces", [])
for f in filled:
    near = None
    for v in f.verts:
        for l in v.link_loops:
            if l.face not in filled: near = l[uvl].uv.copy(); break
        if near is not None: break
    for l in f.loops:
        if near is not None: l[uvl].uv = near
bmesh.ops.triangulate(b, faces=filled)
b.to_mesh(body.data); b.free()
log(f"body: closed {len(filled)} hole patches from {len(new_edges)} open edges")

# the bag rides the upper chest as one rigid piece
for vg in list(bagobj.vertex_groups):
    bagobj.vertex_groups.remove(vg)
g = bagobj.vertex_groups.new(name=BAG_BONE)
g.add(list(range(len(bagobj.data.vertices))), 1.0, "REPLACE")
if not any(m.type == "ARMATURE" for m in bagobj.modifiers):
    mod = bagobj.modifiers.new("Armature", "ARMATURE"); mod.object = arm
log(f"Backpack skinned 100 % to {BAG_BONE}")

# ------------------------------------------------------------------ write the model back with every clip
bpy.ops.object.select_all(action="DESELECT")
arm.select_set(True); body.select_set(True); bagobj.select_set(True)
bpy.context.view_layer.objects.active = arm
arm.name = ARM_NODE
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={"ARMATURE", "MESH"}, apply_unit_scale=True,
                         apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y", bake_space_transform=False,
                         add_leaf_bones=False, armature_nodetype="NULL", mesh_smooth_type="FACE", use_mesh_modifiers=False,
                         path_mode="STRIP", bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                         bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)
log("exported", OUT)
if OUT == FBX:
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "Survivor_Bag.blend"))
log("saved", os.path.join(HERE, "Survivor_Bag.blend"))

if PREVIEW:
    import math
    from mathutils import Vector
    os.makedirs(PREVIEW, exist_ok=True)
    sc = bpy.context.scene
    for im in bpy.data.images:
        if im.name.startswith("texture_0.png"):
            im.filepath = os.path.join(FOLDER, "WastelandSurvivor_Albedo.png"); im.reload()
    cam = bpy.data.objects.new("C", bpy.data.cameras.new("C")); sc.collection.objects.link(cam)
    cam.data.type = "ORTHO"; cam.data.ortho_scale = 1.3
    sc.render.engine = "BLENDER_WORKBENCH"; sc.display.shading.light = "STUDIO"; sc.display.shading.color_type = "TEXTURE"
    sc.world = sc.world or bpy.data.worlds.new("W"); sc.world.color = (0.45, 0.47, 0.5)
    sc.render.resolution_x, sc.render.resolution_y = 500, 650
    for tag, hide in (("with", False), ("bodyonly", True)):
        bagobj.hide_render = hide
        for name, d in (("back", (0, 1, 0)), ("side", (-1, 0, 0))):
            dv = Vector(d); tgt = Vector((0, 0.05, 1.15))
            cam.location = tgt + dv * 3; cam.rotation_euler = (-dv).to_track_quat("-Z", "Y").to_euler(); sc.camera = cam
            sc.render.filepath = os.path.join(PREVIEW, f"{tag}_{name}.png"); bpy.ops.render.render(write_still=True)
    bagobj.hide_render = False
    log("pictures in", PREVIEW)
