# Refits the intro helicopter (made by heli_prepare.py from the Meshy model, kept in Blender/HeliSource/):
#   - takes out the Meshy cabin seats (small, low, too crude to sit four operatives on),
#   - builds troop seats: two Huey-style benches facing the cockpit, two seats each: round aluminium tube frames with
#     red nylon webbing slung for the seat and back (object "Seats", its own texture Seats_Albedo.png),
#   - smooths the shading (the Meshy mesh came out faceted),
#   - repaints it: the Meshy texture is an atlas of small blotchy scraps; this bakes a military scheme into the same UVs:
#     olive drab, a greyer underside, a matte black anti-glare panel on the nose, stencilled black markings (HAVEN and
#     the tail number, rendered in the Stencil font), exhaust soot, bare metal skids, dark rotor blades with yellow tips,
#     a red and white tail rotor, a dark grey cabin; then dirt in the creases, grime low down, worn edges, the Meshy
#     panel lines faintly, dark glass where the Meshy texture had windows. Plus smoothness, metallic and a normal map.
# Writes Assets/Resources/ZombieRush/Cutscene/Helicopter.fbx, Helicopter_Albedo.png, Helicopter_Mask.png (R metallic,
# A smoothness), Helicopter_Normal.png; IntroHeliTools makes the materials from them. Seat numbers are printed for IntroConfig (Unity: x right, y up, z toward the nose).
# Run: blender -b --factory-startup -P Blender/heli_refit.py -- [--preview DIR]
import bpy, bmesh, sys, os, math
import numpy as np
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
PREVIEW = argv[argv.index("--preview") + 1] if "--preview" in argv else None
HERE = os.path.dirname(os.path.abspath(__file__))
SRC_FBX = os.path.join(HERE, "HeliSource", "Helicopter_Meshy.fbx")
SRC_TEX = os.path.join(HERE, "HeliSource", "Helicopter_Meshy_Albedo.png")
OUT = os.path.abspath(os.path.join(HERE, "..", "Assets", "Resources", "ZombieRush", "Cutscene"))
TEX = 2048

# the cabin (Blender: nose -Y, so Unity z = -y): floor about 0.86, roof underside 2.15-2.23, door opening z 1.1-3.0
SEAT_H = 0.34                 # seat top above the floor (low troop seats: the roof is only 1.3 m over the floor)
SEAT_DEPTH, SEAT_W, BACK_H = 0.45, 0.52, 0.6
BENCH_HALF = 0.86              # each bench spans the cabin, two seats wide
SEAT_ROWS = (2.55, 1.55)      # each row's front edge along the cabin (Unity z): legroom ahead of both, in the door opening
SEAT_X = (-0.45, 0.45)        # seat centres across the cabin
FLOOR = 0.86

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC_FBX)
objs = {o.name.split(".")[0]: o for o in bpy.context.scene.objects if o.type == "MESH"}
body, rotors = objs["Body"], [objs["MainRotor"], objs["TailRotor"]]
for n, o in objs.items():
    o.name = n; o.data.name = n
# the import leaves a turn on each object (the FBX axes); bake it into the meshes so every object is unrotated, each
# keeping its origin (the rotors' pivots on their hubs), as heli_prepare.py made them: Unity reads a turned object
# differently and the body came out on its side
worlds = {o: o.matrix_world.copy() for o in objs.values()}   # all first: un-parenting moves the children
for o, W in worlds.items():
    o.parent = None
    o.data.transform(Matrix.Translation(-W.translation) @ W)
    o.matrix_world = Matrix.Translation(W.translation)
for o in rotors:
    o.parent = body
    o.matrix_parent_inverse = body.matrix_world.inverted()

# where the paint scheme's parts go, measured on the mesh (Blender: nose toward -Y, up +Z)
_bv = [v.co for v in body.data.vertices]
NOSE_Y = min(v.y for v in _bv if v.z > 0.5)                                       # the tip of the nose
MAIN_R = max(math.hypot(v.co.x, v.co.y) for v in objs["MainRotor"].data.vertices)   # blade tip radius
TAIL_R = max(math.hypot(v.co.y, v.co.z) for v in objs["TailRotor"].data.vertices)
EXH_Y = 0.4                       # the engine exhaust's back edge (side view of the Meshy model)
TAIL_BAND = 4.6                   # where the white band rings the tail boom
body["part"], objs["MainRotor"]["part"], objs["TailRotor"]["part"] = 0.0, 1.0, 2.0   # read by the paint's Attribute node


def log(*a):
    print("[heli_refit]", *a, flush=True)


# ------------------------------------------------------------------ out with the Meshy seats
# Whole pieces only: every loose piece of the mesh that lies mostly inside the cabin's seat space goes; pieces that
# only reach into it (the cabin's rear wall, the floor) stay whole. Cutting faces out of the space instead left holes
# in the rear wall and jagged scraps of the old seats.
def in_seat_space(c):
    return abs(c.x) <= 1.3 and 0.7 <= -c.y <= 2.8 and 0.84 <= c.z <= 2.08


bm = bmesh.new(); bm.from_mesh(body.data); bm.verts.ensure_lookup_table()
seen, gone = set(), []
for v in bm.verts:
    if v.index in seen: continue
    stack, comp = [v], []
    seen.add(v.index)
    while stack:
        a = stack.pop(); comp.append(a)
        for e in a.link_edges:
            b = e.other_vert(a)
            if b.index not in seen: seen.add(b.index); stack.append(b)
    faces = {f for x in comp for f in x.link_faces}
    if faces and sum(in_seat_space(body.matrix_world @ f.calc_center_median()) for f in faces) >= 0.5 * len(faces):
        gone += comp
bmesh.ops.delete(bm, geom=gone, context="VERTS")
bm.to_mesh(body.data); bm.free()
log(f"removed {len(gone)} vertices of Meshy seats (whole pieces)")

# ------------------------------------------------------------------ troop seats: two Huey-style benches facing the cockpit
# Each bench seats two: a round aluminium tube frame (front and rear seat tubes, legs to the floor, back posts and a top
# tube) with red nylon webbing slung between the tubes for the seat and the back, sagging a little as real slings do.
# Its own small texture (Seats_Albedo.png, made below): the left half brushed aluminium, the right half the webbing.
sm = bpy.data.meshes.new("Seats"); bm = bmesh.new()
uvl = bm.loops.layers.uv.new("UVMap")
METAL_UV = (0.25, 0.5)


def cyl(a, b, r=0.016, seg=10):
    """A round tube from a to b, its UVs on the aluminium half."""
    a, b = Vector(a), Vector(b)
    d = b - a
    rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix().to_4x4()
    m = Matrix.Translation((a + b) / 2) @ rot
    r_ = bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r, radius2=r, depth=d.length, matrix=m)
    for f in {f for v in r_["verts"] for f in v.link_faces}:
        for l in f.loops:
            l[uvl].uv = METAL_UV


def sling(corners, nu, nv, sag, sag_dir):
    """A webbing sheet over the quad corners (a, b, c, d: a-b along u, a-d along v), bowed by `sag` metres along
    `sag_dir` in the middle; one-sided (the seat material draws both faces), UVs across the webbing half."""
    a, b, c, d = (Vector(x) for x in corners)
    grid = []
    for j in range(nv + 1):
        row = []
        for i in range(nu + 1):
            u, v = i / nu, j / nv
            p = a.lerp(b, u).lerp(d.lerp(c, u), v)
            row.append(bm.verts.new(p + Vector(sag_dir) * sag * math.sin(math.pi * u) * math.sin(math.pi * v)))
        grid.append(row)
    for j in range(nv):
        for i in range(nu):
            f = bm.faces.new([grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]])
            for l, (ii, jj) in zip(f.loops, [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]):
                l[uvl].uv = (0.5 + 0.4995 * ii / nu, jj / nv)


top = FLOOR + SEAT_H
seats = []
for zf in SEAT_ROWS:
    yf, yb = -zf, -(zf - SEAT_DEPTH)          # front and back of the seat (Blender y; the nose is -y)
    xa, xb = -BENCH_HALF, BENCH_HALF
    yp = yb + 0.05                            # back posts, a little behind the seat
    # tubes: seat front and rear, legs, back posts, top tube, a foot rail low at the back
    cyl((xa, yf, top - 0.01), (xb, yf, top - 0.01))
    cyl((xa, yb, top + 0.01), (xb, yb, top + 0.01))
    for x in (xa, 0.0, xb):
        cyl((x, yf + 0.03, FLOOR), (x, yf, top - 0.01))
        cyl((x, yp, FLOOR), (x, yp, top + BACK_H))
        cyl((x, yf, top - 0.01), (x, yp, top + 0.01))
    cyl((xa, yp, top + BACK_H), (xb, yp, top + BACK_H))
    cyl((xa, yp, FLOOR + 0.08), (xb, yp, FLOOR + 0.08), r=0.012)
    # webbing: the seat slung between the seat tubes, the back between the posts
    sling(((xa + 0.02, yf, top), (xb - 0.02, yf, top), (xb - 0.02, yb, top + 0.01), (xa + 0.02, yb, top + 0.01)), 16, 5, 0.035, (0, 0, -1))
    sling(((xa + 0.02, yp - 0.012, top + 0.08), (xb - 0.02, yp - 0.012, top + 0.08), (xb - 0.02, yp - 0.012, top + BACK_H - 0.04), (xa + 0.02, yp - 0.012, top + BACK_H - 0.04)), 16, 6, 0.03, (0, 1, 0))
    for cx in SEAT_X:
        seats.append((cx, zf))
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0001)
bm.to_mesh(sm); bm.free()
seat_obj = bpy.data.objects.new("Seats", sm)
bpy.context.scene.collection.objects.link(seat_obj)
for p in sm.polygons: p.use_smooth = True
seat_obj.parent = body
seat_obj.matrix_parent_inverse = body.matrix_world.inverted()

# the seats' texture: brushed aluminium (left half) and red nylon webbing (right half), straps and stitching
SW, SH = 1024, 512
yy, xx = np.mgrid[0:SH, 0:SW].astype(np.float32)
img = np.zeros((SH, SW, 4), np.float32); img[..., 3] = 1.0
rng = np.random.default_rng(7)
brush = rng.normal(0, 1, (SH, 1)).astype(np.float32) * 0.025 + rng.normal(0, 1, (SH, SW)).astype(np.float32) * 0.01
metal = 0.42 + brush
img[:, : SW // 2, 0] = metal[:, : SW // 2]; img[:, : SW // 2, 1] = metal[:, : SW // 2] * 1.01; img[:, : SW // 2, 2] = metal[:, : SW // 2] * 1.03
u = (xx[:, SW // 2:] - SW // 2) / (SW // 2); v = yy[:, SW // 2:] / SH
strap_u = (np.mod(u * 14, 1.0) < 0.82).astype(np.float32)            # vertical straps with gaps
strap_v = (np.mod(v * 7, 1.0) < 0.78).astype(np.float32)            # horizontal straps
weave = np.maximum(strap_u, strap_v) * (0.9 + 0.1 * np.sin(u * 900) * np.sin(v * 700))
over = np.where(np.mod(np.floor(u * 14) + np.floor(v * 7), 2) == 0, 1.0, 0.88)   # over-under shading where straps cross
red = np.array([0.36, 0.07, 0.055])                                  # faded red nylon (linear-ish)
gap = np.array([0.03, 0.025, 0.02])
for k in range(3):
    img[:, SW // 2:, k] = gap[k] + (red[k] - gap[k]) * weave * over
grime = rng.normal(0, 1, (SH // 16, SW // 32)).astype(np.float32)
grime = np.kron(grime, np.ones((16, 16), np.float32))[:SH, : SW // 2]
img[:, SW // 2:, :3] *= (0.85 + 0.08 * grime)[..., None]
img = np.clip(img, 0, 1)[::-1]
simg = bpy.data.images.new("Seats_Albedo", SW, SH, alpha=True)
simg.pixels.foreach_set(img.ravel())
simg.filepath_raw = os.path.join(OUT, "Seats_Albedo.png"); simg.file_format = "PNG"; simg.save()
smat = bpy.data.materials.new("SeatPaint"); smat.use_nodes = True
_bs = next(n for n in smat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
_t = smat.node_tree.nodes.new("ShaderNodeTexImage"); _t.image = simg
smat.node_tree.links.new(_t.outputs[0], _bs.inputs["Base Color"])
sm.materials.append(smat)
log("seats for IntroConfig.seats (Unity local: the middle of each seat's front edge at the seat top; all face +z, the nose):")
for cx, zf in seats:
    log(f"  new Vector3({cx:.2f}f, {top:.2f}f, {zf:.2f}f)")

# ------------------------------------------------------------------ stencilled markings (rendered here, then painted on)
STENCIL_FONT = "C:/Windows/Fonts/STENCIL.TTF"


def render_decal(text, name, px_h=256):
    """The text in the Stencil font, rendered white on transparent (the paint uses its alpha), cropped to the text."""
    sc_ = bpy.context.scene
    hidden = [o for o in sc_.objects if not o.hide_render]
    for o in hidden: o.hide_render = True
    cu = bpy.data.curves.new(name, type="FONT"); cu.body = text
    cu.font = bpy.data.fonts.load(STENCIL_FONT, check_existing=True)
    cu.align_x, cu.align_y = "CENTER", "CENTER"
    to = bpy.data.objects.new(name, cu); sc_.collection.objects.link(to)
    bpy.context.view_layer.update()
    w, h = max(to.dimensions.x, 0.01), max(to.dimensions.y, 0.01)
    cam = bpy.data.objects.new(name + "Cam", bpy.data.cameras.new(name + "Cam")); sc_.collection.objects.link(cam)
    cam.data.type = "ORTHO"; cam.data.ortho_scale = w * 1.04
    cam.location = (0, 0, 5); cam.rotation_euler = (0, 0, 0)
    old = (sc_.camera, sc_.render.engine, sc_.render.resolution_x, sc_.render.resolution_y, sc_.render.film_transparent, sc_.render.filepath)
    sc_.camera = cam
    sc_.render.engine = "BLENDER_WORKBENCH"
    sc_.display.shading.light = "FLAT"; sc_.display.shading.color_type = "SINGLE"; sc_.display.shading.single_color = (1, 1, 1)
    sc_.render.resolution_x, sc_.render.resolution_y = int(px_h * w * 1.04 / (h * 1.04)), px_h
    cam.data.sensor_fit = "HORIZONTAL"
    sc_.render.film_transparent = True
    path = os.path.join(bpy.app.tempdir, name + ".png")
    sc_.render.filepath = path
    bpy.ops.render.render(write_still=True)
    sc_.camera, sc_.render.engine, sc_.render.resolution_x, sc_.render.resolution_y, sc_.render.film_transparent, sc_.render.filepath = old
    bpy.data.objects.remove(to); bpy.data.objects.remove(cam)
    for o in hidden: o.hide_render = False
    im = bpy.data.images.load(path); im.colorspace_settings.name = "Non-Color"
    return im, w / h


def side_span(y, zlo=1.0, zhi=4.6):
    """Height range of the body's side at y (from the +x side), metres."""
    dg_ = bpy.context.evaluated_depsgraph_get()
    hits = []
    z = zlo
    while z <= zhi:
        ok, loc, n, i = body.ray_cast(Vector((4.0, y, z)), Vector((-1.0, 0.0, 0.0)), depsgraph=dg_)
        if ok and loc.x > 0.05: hits.append(z)
        z += 0.02
    return (min(hits), max(hits)) if hits else (2.3, 3.0)


DECALS = []
for text, yc, fill in (("HAVEN", 4.3, 0.55), ("HV-47", 6.55, 0.42)):
    im, aspect = render_decal(text, "Decal_" + text.replace("-", ""))
    lo_, hi_ = side_span(yc)
    hgt = (hi_ - lo_) * fill
    wid = hgt * aspect
    zc = (lo_ + hi_) / 2
    DECALS.append((im, yc - wid / 2, yc + wid / 2, zc - hgt / 2, zc + hgt / 2))
    log(f"marking {text}: boom side z {lo_:.2f}-{hi_:.2f} at y {yc}, letters {hgt:.2f} m tall, {wid:.2f} m long")

# ------------------------------------------------------------------ smooth shading
for o in [body] + rotors:
    bpy.context.view_layer.objects.active = o
    for x in bpy.context.selected_objects: x.select_set(False)
    o.select_set(True)
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))

# ------------------------------------------------------------------ repaint (bake into the Meshy UVs)
sc = bpy.context.scene
try:
    sc.render.engine = "CYCLES"
except TypeError as e:
    log("no Cycles", e); raise
sc.cycles.samples = 24
sc.cycles.device = "CPU"
src = bpy.data.images.load(SRC_TEX)
src.colorspace_settings.name = "sRGB"
img_col = bpy.data.images.new("Paint", TEX, TEX, alpha=False, float_buffer=False)
img_smo = bpy.data.images.new("Smooth", TEX, TEX, alpha=False, float_buffer=False)
img_nrm = bpy.data.images.new("Normal", TEX, TEX, alpha=False, float_buffer=False)
img_smo.colorspace_settings.name = "Non-Color"; img_nrm.colorspace_settings.name = "Non-Color"

mat = bpy.data.materials.new("HeliPaint")
mat.use_nodes = True
nt = mat.node_tree; N = nt.nodes; Lk = nt.links
for n in list(N): N.remove(n)
out = N.new("ShaderNodeOutputMaterial")


def node(t, **kw):
    n = N.new(t)
    for k, v in kw.items():
        if k.startswith("in_"):
            n.inputs[int(k[3:])].default_value = v
        else:
            setattr(n, k, v)
    return n


def math_(op, a, b=None, clamp=False):
    n = node("ShaderNodeMath", operation=op, use_clamp=clamp)
    for i, x in enumerate((a, b)):
        if x is None: continue
        if isinstance(x, (int, float)): n.inputs[i].default_value = x
        else: Lk.new(x, n.inputs[i])
    return n.outputs[0]


def mix(fac, a, b):
    n = node("ShaderNodeMix", data_type="RGBA", blend_type="MIX")
    for sock, x in ((0, fac), (6, a), (7, b)):
        if isinstance(x, (int, float)): n.inputs[sock].default_value = x
        elif isinstance(x, tuple): n.inputs[sock].default_value = x
        else: Lk.new(x, n.inputs[sock])
    return n.outputs[2]


def mul(a, b):
    n = node("ShaderNodeMix", data_type="RGBA", blend_type="MULTIPLY")
    n.inputs[0].default_value = 1.0
    for sock, x in ((6, a), (7, b)):
        if isinstance(x, tuple): n.inputs[sock].default_value = x
        else: Lk.new(x, n.inputs[sock])
    return n.outputs[2]


tex = node("ShaderNodeTexImage", image=src)
uv = node("ShaderNodeUVMap")
Lk.new(uv.outputs[0], tex.inputs[0])
lum = node("ShaderNodeRGBToBW"); Lk.new(tex.outputs[0], lum.inputs[0]); lum = lum.outputs[0]
sep = node("ShaderNodeSeparateColor"); Lk.new(tex.outputs[0], sep.inputs[0])
coord = node("ShaderNodeTexCoord")
geo = node("ShaderNodeNewGeometry")
pos = node("ShaderNodeSeparateXYZ"); Lk.new(coord.outputs["Object"], pos.inputs[0])
nrm = node("ShaderNodeSeparateXYZ"); Lk.new(geo.outputs["Normal"], nrm.inputs[0])
X, Y, Z = pos.outputs[0], pos.outputs[1], pos.outputs[2]
part = node("ShaderNodeAttribute", attribute_type="OBJECT", attribute_name="part").outputs["Fac"]   # 0 body, 1 main rotor, 2 tail rotor
is_main = math_("COMPARE", part, 1.0); is_main.node.inputs[2].default_value = 0.1
is_tail = math_("COMPARE", part, 2.0); is_tail.node.inputs[2].default_value = 0.1
fine = node("ShaderNodeTexNoise"); fine.inputs["Scale"].default_value = 18.0; fine.inputs["Detail"].default_value = 4.0; Lk.new(coord.outputs["Object"], fine.inputs[0])


def band(v, lo, hi, soft=0.04):
    """1 between lo and hi (soft edges), 0 outside."""
    a = math_("MULTIPLY", math_("SUBTRACT", v, lo - soft), 1.0 / (2 * soft), clamp=True)
    b = math_("MULTIPLY", math_("SUBTRACT", hi + soft, v), 1.0 / (2 * soft), clamp=True)
    return math_("MULTIPLY", a, b)


def AND(*xs):
    r = xs[0]
    for x in xs[1:]: r = math_("MULTIPLY", r, x)
    return r


absx = math_("ABSOLUTE", X)

# --- body: olive drab, an even military paint with only a slow, faint variation (sun-faded on top, a touch darker low)
OD = (0.095, 0.112, 0.062, 1)
big = node("ShaderNodeTexNoise"); big.inputs["Scale"].default_value = 0.35; big.inputs["Detail"].default_value = 2.0
Lk.new(coord.outputs["Object"], big.inputs[0])
vary = math_("ADD", 0.94, math_("MULTIPLY", math_("SUBTRACT", big.outputs[0], 0.5), 0.12))
faded = math_("ADD", vary, math_("MULTIPLY", math_("MAXIMUM", nrm.outputs[2], 0.0), 0.06))   # tops sun-faded a little
paint = mul(OD, mix(faded, (0, 0, 0, 1), (1, 1, 1, 1)))
# slightly greyer underside
under = AND(math_("MULTIPLY", math_("SUBTRACT", -0.2, nrm.outputs[2]), 2.0, clamp=True), math_("LESS_THAN", Z, 1.3))
paint = mix(math_("MULTIPLY", under, 0.6), paint, (0.085, 0.092, 0.07, 1))
# matte black anti-glare panel on top of the nose, ahead of the windscreen
glare = AND(math_("LESS_THAN", Y, NOSE_Y + 1.25), band(Z, 1.15, 2.1), math_("GREATER_THAN", nrm.outputs[2], 0.25))
paint = mix(glare, paint, (0.012, 0.012, 0.012, 1))
# cabin interior: plain dark grey-green
inside = AND(math_("LESS_THAN", absx, 1.12), band(Y, -3.1, -0.75, 0.02), band(Z, 0.84, 2.26, 0.02))
paint = mix(inside, paint, (0.05, 0.055, 0.048, 1))
# stencilled black markings (rendered in Blender with the Stencil font): HAVEN along the tail boom, the tail number
# further aft, both sides, reading nose to tail on each
STENCIL = (0.014, 0.014, 0.013, 1)


def decal(image, y0, y1, z0, z1):
    total = None
    for sgn in (1.0, -1.0):
        u = math_("DIVIDE", math_("SUBTRACT", Y, y0) if sgn > 0 else math_("SUBTRACT", y1, Y), y1 - y0)
        v = math_("DIVIDE", math_("SUBTRACT", Z, z0), z1 - z0)
        uvw = node("ShaderNodeCombineXYZ"); Lk.new(u, uvw.inputs[0]); Lk.new(v, uvw.inputs[1])
        t = node("ShaderNodeTexImage", image=image, extension="CLIP"); Lk.new(uvw.outputs[0], t.inputs[0])
        facing = math_("GREATER_THAN", math_("MULTIPLY", nrm.outputs[0], sgn), 0.35)
        m = AND(t.outputs["Alpha"], facing)
        total = m if total is None else math_("MAXIMUM", total, m)
    return total


for (img_, y0, y1, z0, z1) in DECALS:
    paint = mix(decal(img_, y0, y1, z0, z1), paint, STENCIL)
# soot from the engine exhaust, streaking back along the top
soot = AND(band(Y, EXH_Y, EXH_Y + 2.6, 0.3), math_("GREATER_THAN", Z, 2.55), math_("LESS_THAN", absx, 0.9))
soot = math_("MULTIPLY", soot, math_("SUBTRACT", 1.0, math_("MULTIPLY", math_("SUBTRACT", Y, EXH_Y), 0.33, clamp=True)))
paint = mix(math_("MULTIPLY", soot, 0.7), paint, (0.01, 0.01, 0.01, 1))
# bare metal skids and struts
skid = AND(math_("LESS_THAN", Z, 0.42), math_("GREATER_THAN", absx, 0.75))
paint = mix(skid, paint, (0.11, 0.11, 0.105, 1))

# --- main rotor: dark grey blades with yellow tips, a grey metal hub
rr = node("ShaderNodeVectorMath", operation="LENGTH")
flat_xy = node("ShaderNodeCombineXYZ"); Lk.new(X, flat_xy.inputs[0]); Lk.new(Y, flat_xy.inputs[1])
Lk.new(flat_xy.outputs[0], rr.inputs[0]); r_main = rr.outputs["Value"]
blade = mix(math_("GREATER_THAN", r_main, MAIN_R - 0.45), (0.03, 0.032, 0.032, 1), (0.6, 0.45, 0.04, 1))
blade = mix(math_("LESS_THAN", r_main, 0.75), blade, (0.09, 0.09, 0.09, 1))
paint = mix(is_main, paint, blade)

# --- tail rotor: red and white warning bands toward the tips
rt = node("ShaderNodeVectorMath", operation="LENGTH")
flat_yz = node("ShaderNodeCombineXYZ"); Lk.new(Y, flat_yz.inputs[1]); Lk.new(Z, flat_yz.inputs[2])
Lk.new(flat_yz.outputs[0], rt.inputs[0]); r_t = math_("DIVIDE", rt.outputs["Value"], TAIL_R)
stripes = mix(band(r_t, 0.72, 0.86, 0.005), (0.55, 0.55, 0.52, 1), (0.42, 0.03, 0.02, 1))
trot = mix(math_("LESS_THAN", r_t, 0.55), stripes, (0.03, 0.032, 0.032, 1))
paint = mix(is_tail, paint, trot)

# --- weathering over everything: the Meshy panel lines faintly, dirt in creases, grime low down, worn edges
det = math_("MAXIMUM", math_("MINIMUM", math_("MULTIPLY", lum, 2.6), 1.35), 0.45)
paint = mul(paint, mix(0.28, (1, 1, 1, 1), det))
ao = node("ShaderNodeAmbientOcclusion", samples=12, inside=False, only_local=True); ao.inputs["Distance"].default_value = 0.5
paint = mul(paint, mix(math_("MULTIPLY", math_("SUBTRACT", 1.0, ao.outputs[1]), 0.8), (1, 1, 1, 1), (0.5, 0.48, 0.4, 1)))
grime = math_("MULTIPLY", math_("SUBTRACT", 1.0, math_("MINIMUM", math_("DIVIDE", Z, 1.4), 1.0)), 0.3)
paint = mix(math_("MULTIPLY", grime, math_("SUBTRACT", 1.0, math_("ADD", is_main, is_tail))), paint, mul(paint, (0.6, 0.55, 0.45, 1)))
wear = math_("MULTIPLY", math_("SUBTRACT", geo.outputs["Pointiness"], 0.56), 18.0, clamp=True)
wear = math_("MULTIPLY", wear, math_("GREATER_THAN", fine.outputs[0], 0.45))
paint = mix(math_("MULTIPLY", wear, 0.3), paint, (0.24, 0.23, 0.2, 1))
# glass: where the Meshy texture is dark and bluish (on the body only)
blue = math_("GREATER_THAN", math_("SUBTRACT", sep.outputs[2], sep.outputs[0]), 0.035)
glass = AND(blue, math_("LESS_THAN", lum, 0.33), math_("LESS_THAN", part, 0.5))
paint = mix(glass, paint, (0.018, 0.024, 0.03, 1))

# smoothness: matte camo, dead-flat anti-glare, satin metal and rotors, glossy glass; dirt dulls it
smooth = math_("ADD", 0.24, math_("MULTIPLY", wear, 0.12))
smooth = math_("SUBTRACT", smooth, math_("MULTIPLY", glare, 0.16))
smooth = math_("ADD", smooth, math_("MULTIPLY", math_("MAXIMUM", skid, math_("ADD", is_main, is_tail)), 0.14))
smooth = math_("SUBTRACT", smooth, math_("MULTIPLY", math_("SUBTRACT", 1.0, ao.outputs[1]), 0.12))
smooth = mix(glass, mix(0.0, smooth, smooth), (0.88, 0.88, 0.88, 1))
# metallic: bare metal skids and rotor hubs and worn edges, painted skin a little
metal = math_("ADD", 0.08, math_("MULTIPLY", math_("MAXIMUM", skid, wear), 0.5))
# normal: the panel lines and a faint hammered skin
height = math_("ADD", math_("MULTIPLY", lum, 0.6), math_("MULTIPLY", fine.outputs[0], 0.25))
bump = node("ShaderNodeBump"); bump.inputs["Strength"].default_value = 0.35; bump.inputs["Distance"].default_value = 0.004
Lk.new(height, bump.inputs["Height"])
bsdf = node("ShaderNodeBsdfPrincipled"); Lk.new(bump.outputs[0], bsdf.inputs["Normal"])
emit = node("ShaderNodeEmission")
target = node("ShaderNodeTexImage")
N.active = target

for o in [body] + rotors:
    o.data.materials.clear(); o.data.materials.append(mat)
for x in bpy.context.selected_objects: x.select_set(False)
for o in [body] + rotors: o.select_set(True)
bpy.context.view_layer.objects.active = body
sc.render.bake.margin = 6
sc.render.bake.use_clear = True


def bake(kind, image, color_socket=None):
    target.image = image
    N.active = target
    for l in list(out.inputs[0].links): Lk.remove(l)
    if kind == "EMIT":
        Lk.new(color_socket, emit.inputs[0]); Lk.new(emit.outputs[0], out.inputs[0])
        bpy.ops.object.bake(type="EMIT")
    else:
        Lk.new(bsdf.outputs[0], out.inputs[0])
        sc.render.bake.normal_space = "TANGENT"
        bpy.ops.object.bake(type="NORMAL")
    log("baked", image.name)


bake("EMIT", img_col, paint)
bake("EMIT", img_smo, smooth)
img_met = bpy.data.images.new("Metal", TEX, TEX, alpha=False, float_buffer=False)
img_met.colorspace_settings.name = "Non-Color"
bake("EMIT", img_met, metal)
bake("NORMAL", img_nrm)

# albedo (opaque: the rotor blur reads its alpha) and the URP mask: R metallic, A smoothness
col = np.array(img_col.pixels[:], np.float32).reshape(TEX, TEX, 4)
smo = np.array(img_smo.pixels[:], np.float32).reshape(TEX, TEX, 4)
col[..., 3] = 1.0
final = bpy.data.images.new("Helicopter_Albedo", TEX, TEX, alpha=True)
final.pixels.foreach_set(col.ravel())
final.filepath_raw = os.path.join(OUT, "Helicopter_Albedo.png"); final.file_format = "PNG"; final.save()
mask = np.zeros((TEX, TEX, 4), np.float32)
met = np.array(img_met.pixels[:], np.float32).reshape(TEX, TEX, 4)
mask[..., 0] = met[..., 0]; mask[..., 3] = smo[..., 0]
mimg = bpy.data.images.new("Helicopter_Mask", TEX, TEX, alpha=True)
mimg.colorspace_settings.name = "Non-Color"
mimg.pixels.foreach_set(mask.ravel())
mimg.filepath_raw = os.path.join(OUT, "Helicopter_Mask.png"); mimg.file_format = "PNG"; mimg.save()
img_nrm.filepath_raw = os.path.join(OUT, "Helicopter_Normal.png"); img_nrm.file_format = "PNG"; img_nrm.save()
log("textures written")

if PREVIEW:
    os.makedirs(PREVIEW, exist_ok=True)
    look = bpy.data.materials.new("Look"); look.use_nodes = True
    ln = look.node_tree.nodes; bs = next(n for n in ln if n.type == "BSDF_PRINCIPLED")
    t = ln.new("ShaderNodeTexImage"); t.image = final
    look.node_tree.links.new(t.outputs[0], bs.inputs["Base Color"])
    bs.inputs["Roughness"].default_value = 0.6
    for o in [body] + rotors:
        o.data.materials.clear(); o.data.materials.append(look)
    sc.cycles.samples = 16
    sc.render.resolution_x, sc.render.resolution_y = 960, 600
    w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs["Color"].default_value = (0.55, 0.6, 0.68, 1); bg.inputs["Strength"].default_value = 1.2
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sc.collection.objects.link(sun)
    sun.rotation_euler = (0.7, 0.2, 0.8); sun.data.energy = 5.0
    sc.view_settings.exposure = 0.3
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
    for name, loc, at, lens in (("refit_side", (9, -3, 2.6), (0, -0.5, 1.6), 35), ("refit_cabin", (3.4, -1.9, 1.7), (0, -1.9, 1.25), 18), ("refit_cabin2", (2.6, -3.3, 1.75), (0, -1.6, 1.25), 20),
                                 ("refit_top", (-7, -9, 9), (0, 0, 1.5), 30), ("refit_low", (-6, 5, 0.4), (0, 0, 1.8), 30)):
        cam.location = Vector(loc); cam.rotation_euler = (Vector(at) - Vector(loc)).to_track_quat("-Z", "Y").to_euler(); cam.data.lens = lens
        sc.render.filepath = os.path.join(PREVIEW, name + ".png"); bpy.ops.render.render(write_still=True)
    log("previews in", PREVIEW)
    for o in [body] + rotors:
        o.data.materials.clear(); o.data.materials.append(mat)

# ------------------------------------------------------------------ export (same axes as heli_prepare.py)
for x in bpy.context.scene.objects: x.select_set(x.type == "MESH")
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "Helicopter.fbx"), use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
                         apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                         path_mode="STRIP", embed_textures=False, mesh_smooth_type="FACE", add_leaf_bones=False)
log("exported", os.path.join(OUT, "Helicopter.fbx"))
