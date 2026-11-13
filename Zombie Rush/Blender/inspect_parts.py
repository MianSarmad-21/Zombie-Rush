"""Split an FBX building into loose parts and report them; render a picture. -- in.fbx out.png"""
import sys, math, bpy
args = sys.argv[sys.argv.index("--") + 1:]
f, out = args[0], args[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=f)
o = [x for x in bpy.context.scene.objects if x.type == "MESH"][0]
bpy.context.view_layer.objects.active = o; o.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
# render first, on a ground plane at height 0
bpy.ops.mesh.primitive_plane_add(size=400, location=(o.location.x, o.location.y, 0)); bpy.context.active_object.name = "ground"
bpy.context.view_layer.objects.active = o
sc = bpy.context.scene
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam)
d = max(o.dimensions)
cam.location = (d * 1.1, -d * 1.3, d * 0.9); cam.rotation_euler = (math.radians(60), 0, math.radians(40)); sc.camera = cam
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sc.collection.objects.link(sun); sun.rotation_euler = (0.8, 0.2, 0.5); sun.data.energy = 4
sc.render.engine = "BLENDER_EEVEE"; sc.render.resolution_x, sc.render.resolution_y = 1200, 800; sc.render.filepath = out
w = bpy.data.worlds.new("w"); w.use_nodes = True; w.node_tree.nodes["Background"].inputs[0].default_value = (0.3, 0.3, 0.35, 1); sc.world = w
bpy.ops.render.render(write_still=True)
# loose parts
bpy.ops.object.select_all(action="DESELECT"); o.select_set(True); bpy.context.view_layer.objects.active = o
bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT"); bpy.ops.mesh.separate(type="LOOSE"); bpy.ops.object.mode_set(mode="OBJECT")
parts = [x for x in sc.objects if x.type == "MESH" and x.name != "ground"]
from mathutils import Vector
rows = []
for p in parts:
    bb = [p.matrix_world @ Vector(c) for c in p.bound_box]
    zmax = max(v.z for v in bb); zmin = min(v.z for v in bb)
    rows.append((len(p.data.polygons), round(max(p.dimensions), 1), round(zmin, 1), round(zmax, 1)))
rows.sort(reverse=True)
print("ZR_PARTS", len(rows), "total polys", sum(r[0] for r in rows))
print("ZR_LOW", "parts with top below 3m:", sum(1 for r in rows if r[3] < 3), "polys", sum(r[0] for r in rows if r[3] < 3))
for r in rows[:15]: print("ZR_PART polys/size/zmin/zmax", r)
