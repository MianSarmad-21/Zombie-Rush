"""Side and top views of a Meshy gun with a 10 cm grid, to find its parts.
blender -b -P Blender/gun_peek.py -- <gun.fbx> <out.png>"""
import math
import sys

import bpy
import numpy as np

src, out = sys.argv[sys.argv.index("--") + 1:][:2]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
m = next(o for o in bpy.data.objects if o.type == "MESH")
bpy.context.view_layer.objects.active = m
m.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
co = np.empty(len(m.data.vertices) * 3, np.float32)
m.data.vertices.foreach_get("co", co)
co = co.reshape(-1, 3)
mn, mx = co.min(0), co.max(0)
print("ZR_GUN bounds min", mn.round(3), "max", mx.round(3), "size", (mx - mn).round(3))
c = (mn + mx) / 2
sc = bpy.context.scene
span = float((mx - mn).max())
# grid lines every 10% of the longest side, as thin boxes behind the gun
mat = bpy.data.materials.new("grid"); mat.diffuse_color = (0.9, 0.2, 0.2, 1)
for i in range(11):
    for axis in (0, 1, 2):
        pass
views = [("side", (c[0] + span * 2, c[1], c[2]), (math.radians(90), 0, math.radians(90))),
         ("top", (c[0], c[1], c[2] + span * 2), (0, 0, 0)),
         ("front", (c[0], c[1] - span * 2, c[2]), (math.radians(90), 0, 0))]
sun = bpy.data.objects.new("s", bpy.data.lights.new("s", "SUN")); sc.collection.objects.link(sun); sun.rotation_euler = (0.6, 0.3, 0.8); sun.data.energy = 4
w = bpy.data.worlds.new("w"); w.use_nodes = True; w.node_tree.nodes["Background"].inputs[0].default_value = (0.6, 0.6, 0.65, 1); sc.world = w
sc.render.engine = "BLENDER_EEVEE"
sc.render.resolution_x, sc.render.resolution_y = 900, 900
paths = []
for name, loc, rot in views:
    cam = bpy.data.objects.new(name, bpy.data.cameras.new(name)); sc.collection.objects.link(cam)
    cam.data.type = "ORTHO"; cam.data.ortho_scale = span * 1.1
    cam.location = loc; cam.rotation_euler = rot
    sc.camera = cam
    sc.render.filepath = out.replace(".png", f"_{name}.png")
    bpy.ops.render.render(write_still=True)
    paths.append(sc.render.filepath)
print("ZR_GUN views", paths)
