# Prepares the Meshy helicopter (helicopter.zip, "Ghost Rotor") for the intro cutscene:
#   - real size (about 4.4 m tall, a Huey-like utility helicopter),
#   - nose facing Unity's +Z, origin on the ground under the skids,
#   - the main rotor (4 blades + top hub) and the tail rotor (4 blades + hub) cut out as their own objects,
#     each with its pivot on its hub, so the game can spin them,
#   - exported with its texture to Assets/Resources/ZombieRush/Cutscene/ (Helicopter.fbx, Helicopter_Albedo.png).
# Run: blender -b --factory-startup -P Blender/heli_prepare.py -- <path to the Meshy .fbx> [--preview <dir>]
import bpy, sys, os, shutil, math
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
src = os.path.abspath(argv[0])
preview = argv[argv.index("--preview") + 1] if "--preview" in argv else None
here = os.path.dirname(os.path.abspath(__file__))
out_dir = os.path.abspath(os.path.join(here, "..", "Assets", "Resources", "ZombieRush", "Cutscene"))
os.makedirs(out_dir, exist_ok=True)

HEIGHT = 4.4                           # metres, skids to the top of the rotor hub
MAIN_HUB = Vector((-0.105, 0.0))       # x, y of the main rotor mast in the Meshy model (nose at -X, tail at +X)
TAIL_HUB = Vector((0.395, 0.049))      # x, z of the tail rotor hub; its blades lie in a thin x-z plane at y about -0.007

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
body = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
body.name = "Body"
mat = body.data.materials[0] if body.data.materials else None

# split into loose parts, sort each part into body / main rotor / tail rotor
bpy.context.view_layer.objects.active = body
body.select_set(True)
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.separate(type='LOOSE'); bpy.ops.object.mode_set(mode='OBJECT')
parts = [o for o in bpy.context.scene.objects if o.type == 'MESH']
groups = {"Body": [], "MainRotor": [], "TailRotor": []}
for p in parts:
    pts = [p.matrix_world @ v.co for v in p.data.vertices]
    lo = Vector((min(q.x for q in pts), min(q.y for q in pts), min(q.z for q in pts)))
    hi = Vector((max(q.x for q in pts), max(q.y for q in pts), max(q.z for q in pts)))
    c = (lo + hi) / 2; s = hi - lo
    if lo.z > 0.09:
        groups["MainRotor"].append(p)                       # the blades and hub sit above everything else
    elif (lo.x > 0.31 and hi.y < 0.003 and lo.y > -0.017 and s.x < 0.1 and s.z < 0.1
          and (Vector((c.x, c.z)) - TAIL_HUB).length < 0.075):
        groups["TailRotor"].append(p)                       # small thin pieces round the tail hub
    else:
        groups["Body"].append(p)
print({k: len(v) for k, v in groups.items()})

objs = {}
for name, items in groups.items():
    for o in bpy.context.selected_objects: o.select_set(False)
    for o in items: o.select_set(True)
    bpy.context.view_layer.objects.active = items[0]
    bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = name; o.data.name = name
    objs[name] = o

# scale, turn the nose from -X to Blender -Y (Unity +Z after export), put the skids on z = 0
all_pts = [o.matrix_world @ v.co for o in objs.values() for v in o.data.vertices]
lo_z = min(p.z for p in all_pts); hi_z = max(p.z for p in all_pts)
k = HEIGHT / (hi_z - lo_z)
cx = (min(p.x for p in all_pts) + max(p.x for p in all_pts)) / 2
cy = (min(p.y for p in all_pts) + max(p.y for p in all_pts)) / 2
fix = Matrix.Rotation(math.radians(90), 4, 'Z') @ Matrix.Scale(k, 4) @ Matrix.Translation(Vector((-cx, -cy, -lo_z)))
for o in objs.values():
    o.data.transform(fix)
    o.matrix_world = Matrix.Identity(4)

def to_new(x, y, z):
    return fix @ Vector((x, y, z))

# rotor pivots on their hubs
def set_origin(o, world):
    o.data.transform(Matrix.Translation(-world))
    o.matrix_world = Matrix.Translation(world)

main_top = max((objs["MainRotor"].matrix_world @ v.co).z for v in objs["MainRotor"].data.vertices)
set_origin(objs["MainRotor"], Vector((to_new(MAIN_HUB.x, MAIN_HUB.y, 0).x, to_new(MAIN_HUB.x, MAIN_HUB.y, 0).y, main_top - 0.15)))
tail_y = sum((objs["TailRotor"].matrix_world @ v.co).y for v in objs["TailRotor"].data.vertices) / len(objs["TailRotor"].data.vertices)
th = to_new(TAIL_HUB.x, -0.007, TAIL_HUB.y)
set_origin(objs["TailRotor"], Vector((th.x, th.y, th.z)))
for name in ("MainRotor", "TailRotor"):
    o = objs[name]; w = o.matrix_world.copy(); o.parent = objs["Body"]; o.matrix_world = w
print("size", round(k * (max(p.x for p in all_pts) - min(p.x for p in all_pts)), 2), "x", round(k * (max(p.y for p in all_pts) - min(p.y for p in all_pts)), 2), "x", round(HEIGHT, 2),
      "main hub", tuple(round(v, 2) for v in objs["MainRotor"].matrix_world.translation),
      "tail hub", tuple(round(v, 2) for v in objs["TailRotor"].matrix_world.translation))

if preview:
    # colour the rotors to check the cut, then render from the side and above
    scn = bpy.context.scene
    for name, col in (("MainRotor", (1, 0.2, 0.1, 1)), ("TailRotor", (0.1, 0.4, 1, 1))):
        m = bpy.data.materials.new(name + "Check"); m.diffuse_color = col; m.use_nodes = True
        m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = col
        objs[name].data.materials.clear(); objs[name].data.materials.append(m)
    scn.render.engine = "CYCLES"; scn.cycles.samples = 12
    scn.render.resolution_x, scn.render.resolution_y = 640, 400
    w = bpy.data.worlds.new("w"); scn.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs["Color"].default_value = (0.7, 0.72, 0.75, 1)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scn.collection.objects.link(cam); scn.camera = cam
    for vname, loc in (("side", (26, 0, 4)), ("top", (0, 0.01, 30)), ("rear3q", (14, 16, 9))):
        cam.location = Vector(loc); d = Vector((0, 0, 2)) - cam.location
        cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
        scn.render.filepath = os.path.join(preview, f"cut_{vname}.png")
        bpy.ops.render.render(write_still=True)
    print("preview written")
else:
    # export: Unity reads Y-up, -Z forward; bake the axes so no -90 degree rotation shows up in Unity
    for o in bpy.context.scene.objects: o.select_set(o.type == 'MESH')
    fbx_out = os.path.join(out_dir, "Helicopter.fbx")
    bpy.ops.export_scene.fbx(filepath=fbx_out, use_selection=True, object_types={'MESH'}, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                             path_mode='STRIP', embed_textures=False, mesh_smooth_type='FACE', add_leaf_bones=False)
    tex = os.path.join(os.path.dirname(src), os.path.splitext(os.path.basename(src))[0] + ".png")
    if os.path.exists(tex): shutil.copyfile(tex, os.path.join(out_dir, "Helicopter_Albedo.png"))
    print("exported", fbx_out, os.path.getsize(fbx_out))
