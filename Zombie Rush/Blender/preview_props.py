"""Render a preview sheet of every prop in a .blend, laid out in a grid.
blender -b Blender/CityProps.blend -P Blender/preview_props.py -- out.png [columns]"""
import math
import sys

import bpy

args = sys.argv[sys.argv.index("--") + 1:]
out = args[0]
cols = int(args[1]) if len(args) > 1 else 6
sc = bpy.context.scene
objs = [o for o in sc.objects if o.type == "MESH"]
cell = max(max(o.dimensions.x, o.dimensions.y) for o in objs) + 1.5
for i, o in enumerate(objs):
    o.location = ((i % cols) * cell, -(i // cols) * cell, 0)
rows = (len(objs) + cols - 1) // cols
cx, cy = (cols - 1) * cell / 2, -(rows - 1) * cell / 2
bpy.ops.mesh.primitive_plane_add(size=cell * (cols + rows) * 2, location=(cx, cy, 0))
cam_data = bpy.data.cameras.new("cam")
cam = bpy.data.objects.new("cam", cam_data)
sc.collection.objects.link(cam)
span = max(cols, rows) * cell
cam.location = (cx, cy - span * 0.75, span * 0.75)
cam.rotation_euler = (math.radians(45), 0, 0)
cam_data.lens = 28
sc.camera = cam
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
sc.collection.objects.link(sun)
sun.rotation_euler = (math.radians(50), 0, math.radians(30))
sun.data.energy = 3
sc.render.engine = "BLENDER_EEVEE"
sc.render.resolution_x, sc.render.resolution_y = 1800, 1200
w = bpy.data.worlds.new("w")
w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.25, 0.27, 0.32, 1)
sc.world = w
sc.render.filepath = out
bpy.ops.render.render(write_still=True)
print("ZR_PREVIEW", out)
