"""Build a rigged zombie's animations and export it for Unity.

Run:  blender -b Blender/<Name>_Rig.blend -P Blender/zombie_anim.py -- <Name> [sheet_dir] [lod_ratio]
Uses Blender/zr_anim_lib.py (pose tools) and Blender/clips_<name>.py (that zombie's clips, a CLIPS list).
Writes Assets/Models/2026-11-09_<Name>_Final/<Name>.fbx (rig, skinned mesh, every clip) and
<Name>_LOD1.fbx (a cut-down mesh on the same skeleton, no clips). With sheet_dir it also renders,
for every clip, 8 frames from the front and 8 from the side into <sheet_dir>/<Name>_<Clip>.png.
"""
import math
import os
import sys

import bpy

args = sys.argv[sys.argv.index("--") + 1:]
NAME = args[0]
SHEETS = args[1] if len(args) > 1 else None
LOD_RATIO = float(args[2]) if len(args) > 2 else 0.25
HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
# the zombie's existing model folder (any date), so the export replaces the model the game already uses
_models = os.path.join(PROJECT, "Assets", "Models")
_found = sorted(d for d in os.listdir(_models) if d.endswith(f"_{NAME}_Final") and os.path.isdir(os.path.join(_models, d)))
OUT_DIR = os.path.join(_models, _found[-1] if _found else f"2026-11-09_{NAME}_Final")

g = {"ARM_NAME": f"{NAME}_Rig", "MESH_NAME": f"{NAME}_Mesh", "__name__": "zr_anim"}
exec(open(os.path.join(HERE, "zr_anim_lib.py"), encoding="utf-8").read(), g)
exec(open(os.path.join(HERE, f"clips_{NAME.lower()}.py"), encoding="utf-8").read(), g)
arm, mesh = g["arm"], g["LOD"]

bpy.context.scene.render.fps = 30
made = []
for name, frames, fn, loop, ground in g["CLIPS"]:
    a = g["make"](name, frames, fn, loop, ground)
    made.append((a, frames))
    print(f"ZR_ANIM {name}: {frames} frames ({frames / 30:.2f} s){' loop' if loop else ''}")

# rest pose for export, no action left active (every action is exported as its own take)
arm.animation_data.action = None
for pb in arm.pose.bones:
    pb.rotation_mode = "QUATERNION"
    pb.rotation_quaternion = (1, 0, 0, 0)
    pb.location = (0, 0, 0)

os.makedirs(OUT_DIR, exist_ok=True)


def export(path, objs, anim):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"},
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=False, add_leaf_bones=False, armature_nodetype="NULL",
                             mesh_smooth_type="FACE", use_mesh_modifiers=False, path_mode="STRIP",
                             bake_anim=anim, bake_anim_use_all_actions=anim, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)


export(os.path.join(OUT_DIR, f"{NAME}.fbx"), [arm, mesh], True)
# low detail copy on the same skeleton
lod = mesh.copy(); lod.data = mesh.data.copy(); lod.name = f"{NAME}_LOD1"
bpy.context.scene.collection.objects.link(lod)
dec = lod.modifiers.new("lod", "DECIMATE"); dec.ratio = LOD_RATIO
bpy.context.view_layer.objects.active = lod
bpy.ops.object.modifier_move_to_index(modifier=dec.name, index=0)
bpy.ops.object.modifier_apply(modifier=dec.name)
lod_tris = sum(len(p.vertices) - 2 for p in lod.data.polygons)
export(os.path.join(OUT_DIR, f"{NAME}_LOD1.fbx"), [arm, lod], False)
bpy.data.objects.remove(lod)
print(f"ZR_ANIM exported {NAME}.fbx and {NAME}_LOD1.fbx ({lod_tris} triangles) to {OUT_DIR}")
bpy.ops.wm.save_mainfile()

# ---------------- frame sheets for checking
if SHEETS:
    os.makedirs(SHEETS, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.render.resolution_x, sc.render.resolution_y = 260, 300
    cams = []
    for i, (loc, rot) in enumerate((((0, -7, 1.0), (math.radians(90), 0, 0)), ((7, 0, 1.0), (math.radians(90), 0, math.radians(90))))):
        cam = bpy.data.objects.new(f"sheetcam{i}", bpy.data.cameras.new(f"sheetcam{i}"))
        sc.collection.objects.link(cam)
        cam.data.type = "ORTHO"; cam.data.ortho_scale = 2.6
        cam.location = loc; cam.rotation_euler = rot
        cams.append(cam)
    import numpy as np
    for a, frames in made:
        arm.animation_data.action = a
        tiles = []
        for ci, cam in enumerate(cams):
            row = []
            for k in range(8):
                f = 1 + round(k * frames / 8)
                sc.frame_set(f)
                sc.camera = cam
                sc.render.filepath = os.path.join(SHEETS, "_tile.png")
                bpy.ops.render.render(write_still=True)
                im = bpy.data.images.load(sc.render.filepath)
                px = np.array(im.pixels[:], np.float32).reshape(300, 260, 4)
                bpy.data.images.remove(im)
                row.append(px)
            tiles.append(np.concatenate(row, axis=1))
        sheet = np.concatenate(tiles[::-1], axis=0)   # image rows are bottom-up: front view ends on top
        out = bpy.data.images.new("sheet", sheet.shape[1], sheet.shape[0], alpha=True)
        out.pixels.foreach_set(sheet.ravel())
        out.filepath_raw = os.path.join(SHEETS, f"{NAME}_{a.name}.png")
        out.file_format = "PNG"
        out.save()
        bpy.data.images.remove(out)
    print("ZR_ANIM sheets in", SHEETS)
