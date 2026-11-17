"""AK-47 colour texture, IGI style (2026-11-15): bright varnished orange wood with real grain + dark blued gunmetal.

    blender -b --factory-startup -P Blender/ak_texture_bake.py -- <wood_mask.png> <meshy_albedo.png> <out.png> [--preview DIR]

The Meshy AK texture has little painted detail and is cut into many small UV islands, so up close (first person)
it looked blurry. This bakes a new colour texture on the game model (Assets/Models/2026-11-09_Guns/Rifle/Rifle.fbx):
  - wood (where wood_mask is white, found from the Meshy texture's orange): grain bands running along the gun, in the
    model's own space so the grain flows across the UV seams; lighter and darker boards; varnish orange;
  - metal: dark blued steel, faint brushing along the gun, a little mottling;
  - the Meshy texture's own shading (edges, carvings, worn spots) multiplied on top, softly.
Write it as Rifle_Albedo_orig.png; gun_texture_enhance.py --only Rifle --write then adds the AO and sharpening.
"""
import math
import os
import sys

import bpy

argv = sys.argv[sys.argv.index("--") + 1:]
MASK, MESHY, OUT = argv[:3]
PREVIEW = argv[argv.index("--preview") + 1] if "--preview" in argv else None
FBX = "C:/Mian Sarmad/Unity/Zombie-Rush/Zombie Rush/Assets/Models/2026-11-09_Guns/Rifle/Rifle.fbx"
RES = 2048


def lin(c):
    return tuple(((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c) + (1.0,)


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
print("[ak] meshes", [o.name for o in meshes])
target = bpy.data.images.new("AK_bake", RES, RES, alpha=False)
mask_img = bpy.data.images.load(MASK); mask_img.colorspace_settings.name = "Non-Color"
meshy_img = bpy.data.images.load(MESHY)

mat = bpy.data.materials.new("AK_bake"); mat.use_nodes = True
nt = mat.node_tree; N = nt.nodes; Lk = nt.links
for n in list(N): N.remove(n)


def node(t, **kw):
    n = N.new(t)
    for k, v in kw.items():
        setattr(n, k, v)
    return n


out = node("ShaderNodeOutputMaterial")
emit = node("ShaderNodeEmission")
Lk.new(emit.outputs[0], out.inputs[0])
uv = node("ShaderNodeTexCoord")
tmask = node("ShaderNodeTexImage", image=mask_img, interpolation="Cubic"); Lk.new(uv.outputs["UV"], tmask.inputs[0])
tmeshy = node("ShaderNodeTexImage", image=meshy_img, interpolation="Cubic"); Lk.new(uv.outputs["UV"], tmeshy.inputs[0])

# model space, the gun's length along Y (game forward is -Y); grain stretched along the length
mp = node("ShaderNodeMapping"); mp.inputs["Scale"].default_value = (1.0, 0.07, 1.0)
Lk.new(uv.outputs["Object"], mp.inputs[0])
wave = node("ShaderNodeTexWave", wave_type="BANDS", bands_direction="Z", wave_profile="SIN")
wave.inputs["Scale"].default_value = 95.0; wave.inputs["Distortion"].default_value = 16.0
wave.inputs["Detail"].default_value = 3.0; wave.inputs["Detail Scale"].default_value = 1.6
Lk.new(mp.outputs[0], wave.inputs[0])
fine = node("ShaderNodeTexNoise"); fine.inputs["Scale"].default_value = 180.0; fine.inputs["Detail"].default_value = 6.0
mpf = node("ShaderNodeMapping"); mpf.inputs["Scale"].default_value = (1.0, 0.05, 1.0)
Lk.new(uv.outputs["Object"], mpf.inputs[0]); Lk.new(mpf.outputs[0], fine.inputs[0])
board = node("ShaderNodeTexNoise"); board.inputs["Scale"].default_value = 6.0; board.inputs["Detail"].default_value = 1.0
Lk.new(uv.outputs["Object"], board.inputs[0])
# grain value: the bands, roughened by fine streaks
g = node("ShaderNodeMath", operation="MULTIPLY_ADD")
Lk.new(wave.outputs["Fac"], g.inputs[0]); g.inputs[1].default_value = 0.75
gf = node("ShaderNodeMath", operation="MULTIPLY"); Lk.new(fine.outputs["Fac"], gf.inputs[0]); gf.inputs[1].default_value = 0.35
Lk.new(gf.outputs[0], g.inputs[2])
ramp = node("ShaderNodeValToRGB")
cr = ramp.color_ramp
cr.elements[0].position = 0.10; cr.elements[0].color = lin((0.60, 0.27, 0.06))
cr.elements[1].position = 0.62; cr.elements[1].color = lin((0.86, 0.46, 0.11))
e = cr.elements.new(0.9); e.color = lin((0.96, 0.60, 0.20))
Lk.new(g.outputs[0], ramp.inputs[0])
# boards: some lighter, some darker
bmix = node("ShaderNodeMixRGB", blend_type="MULTIPLY"); bmix.inputs[0].default_value = 0.2
Lk.new(ramp.outputs[0], bmix.inputs[1])
bramp = node("ShaderNodeValToRGB"); bramp.color_ramp.elements[0].color = (0.75, 0.75, 0.75, 1); bramp.color_ramp.elements[1].color = (1.25, 1.25, 1.25, 1)
Lk.new(board.outputs["Fac"], bramp.inputs[0]); Lk.new(bramp.outputs[0], bmix.inputs[2])

# metal: blued steel, brushed along the gun, mottled
brush = node("ShaderNodeTexNoise"); brush.inputs["Scale"].default_value = 400.0; brush.inputs["Detail"].default_value = 2.0
mpb = node("ShaderNodeMapping"); mpb.inputs["Scale"].default_value = (1.0, 0.02, 1.0)
Lk.new(uv.outputs["Object"], mpb.inputs[0]); Lk.new(mpb.outputs[0], brush.inputs[0])
mott = node("ShaderNodeTexNoise"); mott.inputs["Scale"].default_value = 30.0; mott.inputs["Detail"].default_value = 4.0
Lk.new(uv.outputs["Object"], mott.inputs[0])
mv = node("ShaderNodeMath", operation="MULTIPLY_ADD"); Lk.new(brush.outputs["Fac"], mv.inputs[0]); mv.inputs[1].default_value = 0.5
mv2 = node("ShaderNodeMath", operation="MULTIPLY"); Lk.new(mott.outputs["Fac"], mv2.inputs[0]); mv2.inputs[1].default_value = 0.5
Lk.new(mv2.outputs[0], mv.inputs[2])
mramp = node("ShaderNodeValToRGB")
mramp.color_ramp.elements[0].position = 0.35; mramp.color_ramp.elements[0].color = lin((0.055, 0.06, 0.07))
mramp.color_ramp.elements[1].position = 0.75; mramp.color_ramp.elements[1].color = lin((0.13, 0.135, 0.15))
Lk.new(mv.outputs[0], mramp.inputs[0])

# wood / metal by the mask
mix = node("ShaderNodeMixRGB"); Lk.new(tmask.outputs["Color"], mix.inputs[0])
Lk.new(mramp.outputs[0], mix.inputs[1]); Lk.new(bmix.outputs[0], mix.inputs[2])
# the Meshy shading on top: its brightness over its mid grey, softened (edges, carvings, worn spots)
bw = node("ShaderNodeRGBToBW"); Lk.new(tmeshy.outputs["Color"], bw.inputs[0])
div = node("ShaderNodeMath", operation="DIVIDE"); Lk.new(bw.outputs[0], div.inputs[0]); div.inputs[1].default_value = 0.075
cl = node("ShaderNodeClamp"); cl.inputs["Min"].default_value = 0.35; cl.inputs["Max"].default_value = 2.2
Lk.new(div.outputs[0], cl.inputs[0])
pw = node("ShaderNodeMath", operation="POWER"); Lk.new(cl.outputs[0], pw.inputs[0]); pw.inputs[1].default_value = 0.45
shade = node("ShaderNodeMixRGB", blend_type="MULTIPLY"); shade.inputs[0].default_value = 1.0
Lk.new(mix.outputs[0], shade.inputs[1]); Lk.new(pw.outputs[0], shade.inputs[2])
Lk.new(shade.outputs[0], emit.inputs["Color"])

# bake target node, selected and active
tgt = node("ShaderNodeTexImage", image=target)
N.active = tgt; tgt.select = True
for o in meshes:
    o.data.materials.clear(); o.data.materials.append(mat)
sc = bpy.context.scene
sc.render.engine = "CYCLES"; sc.cycles.samples = 4; sc.cycles.device = "CPU"
sc.render.bake.margin = 16; sc.render.bake.margin_type = "EXTEND"
bpy.ops.object.select_all(action="DESELECT")
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.object.bake(type="EMIT", use_clear=True)
target.filepath_raw = OUT; target.file_format = "PNG"; target.save()
print("[ak] baked", OUT)

if PREVIEW:
    os.makedirs(PREVIEW, exist_ok=True)
    pm = bpy.data.materials.new("AK_prev"); pm.use_nodes = True
    bs = next(n for n in pm.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    ti = pm.node_tree.nodes.new("ShaderNodeTexImage"); ti.image = bpy.data.images.load(OUT)
    pm.node_tree.links.new(ti.outputs[0], bs.inputs["Base Color"])
    bs.inputs["Roughness"].default_value = 0.38; bs.inputs["Metallic"].default_value = 0.3
    for o in meshes:
        o.data.materials.clear(); o.data.materials.append(pm)
    w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.58, 0.62, 1)
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sc.collection.objects.link(sun)
    sun.data.energy = 3.5; sun.rotation_euler = (0.9, 0.25, 0.6)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
    sc.cycles.samples = 48
    sc.render.resolution_x, sc.render.resolution_y = 1400, 700
    for name, loc, look in (("side", (-1.3, -0.15, 0.05), (0, -0.15, 0.0)), ("close", (-0.32, 0.18, 0.12), (0, -0.05, 0.02))):
        cam.location = loc
        d = (__import__("mathutils").Vector(look) - cam.location)
        cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
        cam.data.lens = 50 if name == "side" else 35
        sc.render.filepath = os.path.join(PREVIEW, f"ak_{name}.png"); bpy.ops.render.render(write_still=True)
    print("[ak] previews in", PREVIEW)
