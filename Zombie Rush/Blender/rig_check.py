"""Bend a rigged zombie into test poses and render it, to check the skeleton fit and the skin weights.

Run:  blender -b Blender/<Name>_Rig.blend -P Blender/rig_check.py -- <Name> out.png
Red dots mark the joints. Pose: left arm raised forward, right elbow bent, left leg striding
forward, right knee bent, spine hunched and the head turned.
"""
import math
import sys

import bpy
from mathutils import Euler, Vector

args = sys.argv[sys.argv.index("--") + 1:]
NAME, OUT = args[0], args[1]
arm = bpy.data.objects[NAME + "_Rig"]
mesh = bpy.data.objects[NAME + "_Mesh"]
P = "mixamorig:"


def rot(bone, x=0, y=0, z=0):
    pb = arm.pose.bones[P + bone]
    pb.rotation_mode = "XYZ"
    pb.rotation_euler = Euler((math.radians(x), math.radians(y), math.radians(z)))


rot("LeftArm", 0, 0, 70)
rot("LeftForeArm", 0, 0, 20)
rot("RightForeArm", 70, 0, 0)
rot("LeftUpLeg", -40, 0, 0)
rot("LeftLeg", 30, 0, 0)
rot("RightUpLeg", 15, 0, 0)
rot("RightLeg", 60, 0, 0)
rot("Spine", 15, 0, 0)
rot("Spine1", 10, 0, 0)
rot("Head", 0, 30, 0)
bpy.context.view_layer.update()

sc = bpy.context.scene
mat = bpy.data.materials.new("joint")
mat.diffuse_color = (1, 0.1, 0.1, 1)
for b in arm.pose.bones:
    if any(f in b.name for f in ("Thumb", "Index", "Middle", "Ring", "Pinky")):
        continue
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.022, location=arm.matrix_world @ b.head)
    s = bpy.context.active_object
    s.data.materials.append(mat)
    s.show_in_front = True
H = max((mesh.matrix_world @ Vector(c)).z for c in mesh.bound_box)
for i, (loc, rot_) in enumerate((((0, -6, H / 2), (math.radians(90), 0, 0)), ((6, 0, H / 2), (math.radians(90), 0, math.radians(90))))):
    cam = bpy.data.objects.new(f"cam{i}", bpy.data.cameras.new(f"cam{i}"))
    sc.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = H * 1.2
    cam.location = loc
    cam.rotation_euler = rot_
sc.render.engine = "BLENDER_WORKBENCH"
sc.display.shading.color_type = "MATERIAL"
sc.render.resolution_x, sc.render.resolution_y = 700, 800
paths = []
for i in range(2):
    sc.camera = bpy.data.objects[f"cam{i}"]
    sc.render.filepath = OUT.replace(".png", f"_{i}.png")
    bpy.ops.render.render(write_still=True)
    paths.append(sc.render.filepath)
print("ZR_CHECK", paths)
