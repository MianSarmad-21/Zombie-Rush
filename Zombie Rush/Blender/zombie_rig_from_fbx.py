"""Make Blender/<Name>_Rig.blend from a zombie's exported model, for zombie_anim.py.

Run:  blender -b -P Blender/zombie_rig_from_fbx.py -- <Name>
Imports Assets/Models/*_<Name>_Final/<Name>.fbx (Mixamo skeleton, mesh <Name>_Mesh on <Name>_Rig),
drops its old clips, puts the skeleton in its rest pose and saves the .blend next to this script.
For zombies whose rig .blend is gone (zombie_rig.py makes it for new zombies).
"""
import os
import sys

import bpy

NAME = sys.argv[sys.argv.index("--") + 1]
HERE = os.path.dirname(os.path.abspath(__file__))
MODELS = os.path.join(os.path.dirname(HERE), "Assets", "Models")
folder = sorted(d for d in os.listdir(MODELS) if d.endswith(f"_{NAME}_Final"))[-1]
fbx = os.path.join(MODELS, folder, f"{NAME}.fbx")

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
mesh = max((o for o in bpy.data.objects if o.type == "MESH" and "LOD" not in o.name), key=lambda o: len(o.data.vertices))
arm.name, mesh.name = f"{NAME}_Rig", f"{NAME}_Mesh"
arm.data.name = f"{NAME}_Rig"
for a in list(bpy.data.actions):
    bpy.data.actions.remove(a)
arm.animation_data_create()
arm.animation_data.action = None
for pb in arm.pose.bones:
    pb.rotation_mode = "QUATERNION"
    pb.rotation_quaternion = (1, 0, 0, 0)
    pb.location = (0, 0, 0)
    pb.scale = (1, 1, 1)
bpy.context.scene.render.fps = 30
out = os.path.join(HERE, f"{NAME}_Rig.blend")
bpy.ops.wm.save_as_mainfile(filepath=out)
print("ZR_RIG saved", out, "from", fbx, len(arm.data.bones), "bones,", len(mesh.data.vertices), "verts")
