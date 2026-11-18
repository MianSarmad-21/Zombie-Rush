# Renders the bullet hole decal for ImpactEffect: a dark pit with a chipped, slightly raised rim and a few
# hairline cracks, lit from the top left, on a transparent background.
#   blender -b --factory-startup -P Blender/fx_bullet_hole.py -- <out.png> [size]
# Default out: Assets/Resources/ZombieRush/Fx/Fx_BulletHole.png at 128 px (a few KB: keeps the build small).
import bpy, bmesh, math, random, sys, os

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
here = os.path.dirname(os.path.abspath(__file__))
out = os.path.abspath(argv[0]) if argv else os.path.join(here, "..", "Assets", "Resources", "ZombieRush", "Fx", "Fx_BulletHole.png")
size = int(argv[1]) if len(argv) > 1 else 128
random.seed(7)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

# ---------------------------------------------------------------- the surface: a fine grid shaped in Python
N = 220
bm = bmesh.new()
bmesh.ops.create_grid(bm, x_segments=N, y_segments=N, size=0.5)   # 1 x 1 m, centred
cracks = [(random.uniform(0, 2 * math.pi), random.uniform(0.28, 0.46), random.uniform(0.006, 0.011)) for _ in range(6)]
chips = [(random.uniform(0, 2 * math.pi), random.uniform(0.15, 0.2), random.uniform(0.03, 0.06)) for _ in range(9)]

def shape(x, y):
    """Height, darkness (0 light .. 1 black) and opacity at a point."""
    r = math.hypot(x, y)
    a = math.atan2(y, x)
    # irregular edge: several unrelated ripples, so it never reads as a flower
    wobble = 1 + 0.06 * math.sin(a * 3 + 1.3) + 0.045 * math.sin(a * 7 + 0.4) + 0.03 * math.sin(a * 13 + 2.2)
    pit = 0.075 * wobble
    rim = 0.145 * (1 + 0.1 * math.sin(a * 5 + 0.9) + 0.06 * math.sin(a * 9 + 1.7))
    z, dark, alpha = 0.0, 0.0, 0.0
    if r < pit:                                   # the hole itself: deep and black
        t = r / pit
        z = -0.06 * (1 - t * t)
        dark, alpha = 1.0, 1.0
    elif r < rim:                                 # torn surface round it, sloping into the pit, a lip at the edge
        t = (r - pit) / (rim - pit)
        z = -0.02 * (1 - t) ** 2 + 0.008 * math.exp(-((t - 0.75) / 0.15) ** 2)
        dark = 0.9 * (1 - t) ** 2 - 0.35 * t   # dark down the slope, lighter freshly broken concrete at the lip
        alpha = 1.0 if t < 0.7 else 1 - (t - 0.7) / 0.3 * 0.65
        for ca, cr, cw in chips:                  # flakes knocked out of the rim
            d = math.hypot(x - math.cos(ca) * cr, y - math.sin(ca) * cr)
            if d < cw:
                z -= 0.01 * (1 - d / cw)
                dark = max(dark, 0.6)
    else:                                         # scorch fading out
        t = min(1.0, (r - rim) / 0.08)
        dark = 0.25 * (1 - t)
        alpha = 0.22 * (1 - t) ** 2
    for ca, length, width in cracks:              # hairline cracks running out from the hole
        along = x * math.cos(ca) + y * math.sin(ca)
        side = abs(-x * math.sin(ca) + y * math.cos(ca))
        if pit < along < length:
            k = 1 - (along - pit) / (length - pit)
            w = width * (0.4 + 0.6 * k) * (1 + 0.3 * math.sin(along * 90))
            if side < w:
                s = 1 - side / w
                z -= 0.006 * s * k
                dark = max(dark, 0.85 * s)
                alpha = max(alpha, 0.9 * s * min(1.0, k * 2))
    return z, dark, alpha

col = bm.loops.layers.color.new("Look")
for v in bm.verts:
    v.co.z = shape(v.co.x, v.co.y)[0]
for f in bm.faces:
    for loop in f.loops:
        _, dark, alpha = shape(loop.vert.co.x, loop.vert.co.y)
        shade = max(0.004, 0.2 * (1 - dark))            # concrete grey-brown down to black (dark < 0 = lighter chips)
        loop[col] = (shade * 1.05, shade, shade * 0.9, alpha)
mesh = bpy.data.meshes.new("Hole")
bm.to_mesh(mesh)
bm.free()
for p in mesh.polygons: p.use_smooth = True
obj = bpy.data.objects.new("Hole", mesh)
scene.collection.objects.link(obj)

# material: colour and see-through from the painted vertex colours, a little rough sheen
mat = bpy.data.materials.new("HoleMat")
mat.use_nodes = True
nt = mat.node_tree
bsdf = nt.nodes["Principled BSDF"]
attr = nt.nodes.new("ShaderNodeVertexColor"); attr.layer_name = "Look"
nt.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])
nt.links.new(attr.outputs["Alpha"], bsdf.inputs["Alpha"])
bsdf.inputs["Roughness"].default_value = 1.0
for name in ("Specular IOR Level", "Specular"):   # no shine: a shiny pit reads grey, not black
    if name in bsdf.inputs: bsdf.inputs[name].default_value = 0.0
obj.data.materials.append(mat)

# ---------------------------------------------------------------- light and camera
sun = bpy.data.lights.new("Sun", "SUN"); sun.energy = 3.2; sun.angle = math.radians(8)
sun_obj = bpy.data.objects.new("Sun", sun); scene.collection.objects.link(sun_obj)
sun_obj.rotation_euler = (math.radians(38), math.radians(-28), math.radians(20))   # from the top left
world = bpy.data.worlds.new("World"); scene.world = world
world.use_nodes = True
world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.35, 0.35, 0.37, 1)
world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.6

cam = bpy.data.cameras.new("Cam"); cam.type = "ORTHO"; cam.ortho_scale = 0.62
cam_obj = bpy.data.objects.new("Cam", cam); scene.collection.objects.link(cam_obj)
cam_obj.location = (0, 0, 2); cam_obj.rotation_euler = (0, 0, 0)
scene.camera = cam_obj

# ---------------------------------------------------------------- render: small, transparent, plain colours
scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.samples = 48
scene.cycles.use_denoising = False
scene.render.resolution_x = scene.render.resolution_y = size
scene.render.resolution_percentage = 100
scene.render.film_transparent = True
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.render.image_settings.compression = 100
scene.view_settings.view_transform = "Standard"
scene.render.filepath = out
os.makedirs(os.path.dirname(out), exist_ok=True)
bpy.ops.render.render(write_still=True)
print(f"[fx_bullet_hole] wrote {out} ({os.path.getsize(out)} bytes, {size}px)")
