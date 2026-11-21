"""The pistol grip as the game shows it, saved for clips made outside the gun pipeline (player_anim.py pistol clips).

Run:  blender -b Blender/Grip_Pistol.blend -P Blender/grip_hold_json.py

Reads the baked third-person hold (actions Idle on Pistol_Rig and Pistol_Idle on Player_Rig, frame 1, the gun rig
placed and scaled as grip_build.py does) and writes Blender/grip_hold.json:
  hand_in_gun: each Hand bone's armature-space matrix in the Gun bone's rigid frame (origin on the grip, muzzle -Y,
               top +Z); the gun model itself is x1.25 (grips.json meta scale), the frame is not;
  fingers:     every finger and thumb bone's local rotation (w, x, y, z);
  arm:         Shoulder, UpperArm, LowerArm, Hand local rotations (the aim pose's arm shape, relative to the chest).
"""
import json
import os

import bpy
from mathutils import Matrix

PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(bpy.data.filepath)))
OUT = os.path.join(PROJECT, "Blender", "grip_hold.json")
META = json.load(open(os.path.join(PROJECT, "Blender", "grips.json")))["Pistol"]["meta"]

sc = bpy.context.scene
body = bpy.data.objects["Player_Rig"]
gun = bpy.data.objects["Pistol_Rig"]
gun.animation_data.action = bpy.data.actions["Idle"]
body.animation_data.action = bpy.data.actions["Pistol_Idle"]
gun.location = META["rig_loc"]
gun.scale = (META.get("scale", 1.0),) * 3
sc.frame_set(1)
bpy.context.view_layer.update()

l, r, _ = (gun.matrix_world @ gun.pose.bones["Gun"].matrix).decompose()
G = Matrix.LocRotScale(l, r, None)
out = {"note": __doc__.strip().splitlines()[0], "gun_scale": META.get("scale", 1.0)}
for side in ("Right", "Left"):
    H = G.inverted() @ (body.matrix_world @ body.pose.bones[side + "Hand"].matrix)
    fingers = {}
    for pb in body.pose.bones:
        if pb.name.startswith(side + "Hand") and pb.name != side + "Hand":
            q = pb.matrix_basis.to_quaternion()
            fingers[pb.name] = [q.w, q.x, q.y, q.z]
    # the aim pose's arm chain (local rotations: the arm's shape relative to the chest), for clips that lower the
    # gun by turning the arms at the shoulders so the wrists keep the aim's straight line
    arm = {}
    for n in ("Shoulder", "UpperArm", "LowerArm", "Hand"):
        q = body.pose.bones[side + n].matrix_basis.to_quaternion()
        arm[side + n] = [q.w, q.x, q.y, q.z]
    out[side] = {"hand_in_gun": [list(row) for row in H], "fingers": fingers, "arm": arm}
    print(f"ZR_GRIPHOLD {side}: wrist {tuple(round(x, 3) for x in H.translation)} m from the grip, {len(fingers)} finger bones")
json.dump(out, open(OUT, "w"), indent=1)
print("ZR_GRIPHOLD wrote", OUT)
