"""Calms the player's butt / jacket hem (2026-11-14): seen from behind they bulged and flared with every stride
because the back of the hips and the jacket hem were weighted to the thigh bones.

    blender -b --factory-startup -P Blender/butt_weights_fix.py -- [--preview DIR] [--export]
    or inside Blender: exec(...); fix_butt(mesh_obj, rig_obj)

Behind the hip joints (and round the sides), from 14 cm below the hip joint up to 3 cm above it, the
LeftUpperLeg / RightUpperLeg weight fades out (fully gone at the top) and goes to Hips. The front (crotch, thigh
fronts) is untouched so the legs still bend cleanly. --export re-exports ShadowOperative.fbx (the face-fix
export settings); the source .blend is patched with the same function by the caller.
"""
import os
import sys

import bpy
from mathutils import Vector

FOLDER = "C:/Mian Sarmad/Unity/Zombie-Rush/Zombie Rush/Assets/Models/2026-11-14_ShadowOperative_Final/"


def fix_butt(mesh, rig, below=0.14, above=0.03, report=False):
    vg = {g.name: g for g in mesh.vertex_groups}
    hips = vg.get("Hips")
    if hips is None: raise RuntimeError("no Hips group")
    moved, total = 0, 0.0
    for side in ("Left", "Right"):
        g = vg.get(side + "UpperLeg")
        if g is None: continue
        joint = rig.matrix_world @ rig.data.bones[side + "UpperLeg"].head_local
        for v in mesh.data.vertices:
            p = mesh.matrix_world @ v.co
            if p.z < joint.z - below or p.z > joint.z + above + 0.10: continue
            # behind the hip joint (+Y is the back: the model faces -Y), with a soft edge round the sides
            back = (p.y - (joint.y - 0.02)) / 0.05
            if back <= 0: continue
            back = min(1.0, back)
            try: w = g.weight(v.index)
            except RuntimeError: continue
            if w <= 0: continue
            t = min(1.0, max(0.0, (p.z - (joint.z - below)) / (below + above)))   # 0 at the bottom, 1 at the top
            k = (t * t * (3 - 2 * t)) * back                                        # share of the leg weight to move
            dw = w * k
            if dw < 1e-4: continue
            g.add([v.index], w - dw, "REPLACE")
            try: hw = hips.weight(v.index)
            except RuntimeError: hw = 0.0
            hips.add([v.index], hw + dw, "REPLACE")
            moved += 1; total += dw
    if report: print(f"[butt] moved thigh weight to Hips on {moved} vertex entries (total {total:.1f})")
    return moved


if __name__ == "__main__" and bpy.app.background and "--" in sys.argv:
    argv = sys.argv[sys.argv.index("--") + 1:]
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    bpy.ops.import_scene.fbx(filepath=FOLDER + "ShadowOperative.fbx")
    rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    mesh = max((o for o in bpy.data.objects if o.type == "MESH"), key=lambda o: len(o.data.vertices))
    fix_butt(mesh, rig, report=True)
    if "--export" in argv:
        for o in bpy.data.objects: o.select_set(o in (rig, mesh))
        bpy.context.view_layer.objects.active = rig
        bpy.ops.export_scene.fbx(filepath=FOLDER + "ShadowOperative.fbx", use_selection=True, object_types={"ARMATURE", "MESH"},
                                 add_leaf_bones=False, apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                                 bake_space_transform=False, bake_anim=False, path_mode="RELATIVE")
        print("[butt] exported", FOLDER + "ShadowOperative.fbx")
