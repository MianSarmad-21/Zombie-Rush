"""AK-47 texture, IGI style, third pass (2026-11-15). Replaces ak_wood_smooth.py.

    blender -b --factory-startup -P Blender/ak_texture_igi.py -- [--preview DIR] [--write]

User feedback so far: first IGI bake "too sharp", flat smooth pass "too rough, not realistic", grain pass "still bad".
What was wrong besides the wood: the metal was nearly black (no highlights: a black blob), orange smears sat on
the receiver and magazine (the UV wood mask marked a few metal triangles as wood) and the orange was toy-bright.
This pass:
  1. wood mask cleaned ON THE MODEL: each face is wood or metal (old UV mask sampled per face), wood patches that are
     tiny next to the real wooden parts become metal, the magazine is all metal; baked back to a UV mask;
  2. wood: IGI orange-brown varnish, soft straight grain along the gun's long axis (tree rings), no sharpening;
  3. metal: dark blued gunmetal grey (not black) keeping the model's own shading detail, cavities darker;
  4. normal map flat on the wood and on the old smears; MetalSmooth rebuilt: wood 0 metallic / 0.74 smooth,
     metal 0.55 metallic / 0.6 smooth.
Sources (never overwritten): Rifle_{Albedo,Normal,MetalSmooth}_sharpwood.png + Rifle_Occlusion.png + ak_wood_mask.png.
"""
import os
import sys
from collections import defaultdict

import bpy
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
WRITE = "--write" in argv
PREVIEW = argv[argv.index("--preview") + 1] if "--preview" in argv else None
HERE = os.path.dirname(os.path.abspath(__file__))
D = os.path.join(HERE, "..", "Assets", "Models", "2026-11-09_Guns", "Rifle")
FBX = os.path.join(D, "Rifle.fbx")
ALB, NRM, MSP = (os.path.join(D, f"Rifle_{n}.png") for n in ("Albedo", "Normal", "MetalSmooth"))
AO = os.path.join(D, "Rifle_Occlusion.png")
MASK = os.path.join(HERE, "ak_wood_mask.png")
RES = 2048

# IGI orange-brown varnish (sRGB): early wood / mid / thin late-wood line / back
WOOD_COLS = ((0.0, (0.76, 0.43, 0.20)), (0.74, (0.72, 0.39, 0.17)), (0.93, (0.56, 0.28, 0.11)), (1.0, (0.67, 0.36, 0.15)))
METAL = np.array([0.27, 0.28, 0.31], np.float32)   # blued gunmetal, sRGB (lighter: the game is at night)
WOOD_SMOOTH, METAL_SMOOTH, METAL_METALLIC = 0.74, 0.58, 0.40   # less metallic: at night a metal surface only mirrors the black sky
SMALL_WOOD = 0.002  # wood patches under this share of the biggest wooden part become metal


def log(*a):
    print("[ak]", *a, flush=True)


def lin(c):
    return tuple(((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c) + (1.0,)


def to_srgb(x):
    x = np.clip(x, 0, 1)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(x, 1 / 2.4) - 0.055)


def pixels(img):
    w, h = img.size
    return np.array(img.pixels[:], np.float32).reshape(h, w, 4)


def load(path):
    img = bpy.data.images.load(path); img.colorspace_settings.name = "Non-Color"   # raw stored values
    return img


def src_of(p):
    s = p.replace(".png", "_sharpwood.png")
    return s if os.path.exists(s) else p


def blur(img, r):
    out = img
    for _ in range(3):
        for ax in (0, 1):
            c = np.cumsum(np.pad(out, [(r + 1, r) if i == ax else (0, 0) for i in range(out.ndim)], mode="edge"), axis=ax)
            hi = np.take(c, np.arange(2 * r + 1, c.shape[ax]), axis=ax)
            lo = np.take(c, np.arange(0, c.shape[ax] - 2 * r - 1), axis=ax)
            out = (hi - lo) / (2 * r + 1)
    return out


def save(arr, path):
    img = bpy.data.images.new(os.path.basename(path), arr.shape[1], arr.shape[0], alpha=True)
    img.colorspace_settings.name = "Non-Color"
    img.pixels.foreach_set(arr.astype(np.float32).ravel()); img.filepath_raw = path; img.file_format = "PNG"; img.save()
    log("saved", path)


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
body = max(meshes, key=lambda o: len(o.data.vertices))
log("meshes", [(o.name, len(o.data.polygons)) for o in meshes])
sc = bpy.context.scene
sc.render.engine = "CYCLES"; sc.cycles.device = "CPU"
sc.render.bake.margin_type = "EXTEND"


def bake(material, image, margin, samples):
    nt = material.node_tree
    t = nt.nodes.new("ShaderNodeTexImage"); t.image = image
    for n in nt.nodes: n.select = False
    t.select = True; nt.nodes.active = t
    for o in meshes:
        o.data.materials.clear(); o.data.materials.append(material)
    sc.cycles.samples = samples; sc.render.bake.margin = margin
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes: o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.bake(type="EMIT", use_clear=True)
    return pixels(image)


def new_mat(name):
    m = bpy.data.materials.new(name); m.use_nodes = True
    for n in list(m.node_tree.nodes): m.node_tree.nodes.remove(n)
    out = m.node_tree.nodes.new("ShaderNodeOutputMaterial")
    em = m.node_tree.nodes.new("ShaderNodeEmission"); m.node_tree.links.new(em.outputs[0], out.inputs[0])
    return m, em


# ---------------------------------------------------------------- 1. wood mask, cleaned on the model
M0 = pixels(load(MASK))[..., 0]


def sample(uv):
    x = min(RES - 1, max(0, int(uv[0] % 1.0 * RES))); y = min(RES - 1, max(0, int(uv[1] % 1.0 * RES)))
    return M0[y, x]


for o in meshes:
    me = o.data; uvl = me.uv_layers.active.data
    wood = np.zeros(len(me.polygons), bool); area = np.zeros(len(me.polygons))
    for p in me.polygons:
        uvs = [uvl[li].uv for li in p.loop_indices]
        c = sum((Vector(u) for u in uvs), Vector((0, 0))) / len(uvs)
        s = [sample(u * 0.7 + c * 0.3) for u in uvs] + [sample(c)] * 2   # corners pulled inward + centre twice
        wood[p.index] = np.mean(s) > 0.5
        area[p.index] = p.area
    if o is not body:
        log(o.name, "all metal (was", int(wood.sum()), "wood faces)"); wood[:] = False
    else:
        def patches(sel):
            """Connected patches (faces sharing an EDGE - a corner is not enough: a dark sliver in the handguard touched
            the receiver at one vertex) of the selected faces: face -> patch id, patch -> area."""
            parent = list(range(len(me.polygons)))

            def find(i):
                while parent[i] != i:
                    parent[i] = parent[parent[i]]; i = parent[i]
                return i
            by_vert = defaultdict(list)
            for p in me.polygons:
                if sel[p.index]:
                    for ek in p.edge_keys: by_vert[ek].append(p.index)
            for fs in by_vert.values():
                for f in fs[1:]:
                    a, b = find(fs[0]), find(f)
                    if a != b: parent[a] = b
            pid = {i: find(i) for i in np.nonzero(sel)[0]}
            parea = defaultdict(float)
            for i, c in pid.items(): parea[c] += area[i]
            return pid, parea

        # tiny wood patches out on the metal (the old smears) -> metal
        pid, parea = patches(wood)
        big = max(parea.values())
        small = {c for c, a in parea.items() if a < big * SMALL_WOOD}
        log("wood patches", len(parea), "areas", sorted((round(a / big, 3) for a in parea.values()), reverse=True)[:12], "-> to metal", len(small))
        for i, c in pid.items():
            if c in small: wood[i] = False
        # tiny metal patches touching the wood (holes, dark slashes in the stock) -> wood
        wood_verts = {v for p in me.polygons if wood[p.index] for v in p.vertices}
        pid, parea = patches(~wood)
        bigm = max(parea.values())
        touch = defaultdict(bool)
        for i, c in pid.items():
            if any(v in wood_verts for v in me.polygons[i].vertices): touch[c] = True
        holes = {c for c, a in parea.items() if a < bigm * 0.004 and touch[c]}
        log("metal patches", len(parea), "-> to wood", len(holes), "areas", sorted((round(parea[c] / bigm, 4) for c in holes), reverse=True)[:10])
        for i, c in pid.items():
            if c in holes: wood[i] = True
    attr = me.color_attributes.new("WoodMask", "FLOAT_COLOR", "CORNER")
    vals = np.zeros((len(me.loops), 4), np.float32); vals[:, 3] = 1
    for p in me.polygons:
        if wood[p.index]: vals[list(p.loop_indices), :3] = 1
    attr.data.foreach_set("color", vals.ravel())
    log(o.name, "wood faces", int(wood.sum()), "of", len(wood))

mm, em = new_mat("MaskBake")
an = mm.node_tree.nodes.new("ShaderNodeAttribute"); an.attribute_name = "WoodMask"
mm.node_tree.links.new(an.outputs["Color"], em.inputs["Color"])
Mc = bake(mm, bpy.data.images.new("MaskC", RES, RES, alpha=False, float_buffer=True), 6, 1)[..., 0]
Mc = np.clip(blur(Mc, 1), 0, 1)                                    # soft 1-2 px edge
# The Meshy UVs OVERLAP in places: a metal face baked over part of the handguard's pixels (a dark slash on top of
# it, right in the first-person view). Wood wins: every wood triangle is painted back into the mask (1 px wider),
# and faces with collapsed UVs get a small dot. The few hidden metal faces sharing those pixels show wood instead.
painted = 0
for o in meshes:
    me = o.data; uvl = me.uv_layers.active.data; at = me.color_attributes["WoodMask"].data
    me.calc_loop_triangles()
    for t in me.loop_triangles:
        if at[t.loops[0]].color[0] < 0.5: continue
        P = np.array([[uvl[li].uv[0] * RES, uvl[li].uv[1] * RES] for li in t.loops])
        x0, y0 = np.floor(P.min(0)).astype(int) - 1; x1, y1 = np.ceil(P.max(0)).astype(int) + 1
        x0, y0 = max(x0, 0), max(y0, 0); x1, y1 = min(x1, RES - 1), min(y1, RES - 1)
        if x1 < x0 or y1 < y0: continue
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        (ax, ay), (bx, by), (cx, cy) = P
        den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(den) < 1e-6:
            Mc[y0:y1 + 1, x0:x1 + 1] = 1.0; painted += 1; continue
        l1 = ((by - cy) * (xs - cx) + (cx - bx) * (ys - cy)) / den
        l2 = ((cy - ay) * (xs - cx) + (ax - cx) * (ys - cy)) / den
        l3 = 1 - l1 - l2
        pad = 1.0 / max(1.0, np.sqrt(abs(den)))                     # ~1 px outside the triangle
        inside = (l1 > -pad) & (l2 > -pad) & (l3 > -pad)
        Mc[y0:y1 + 1, x0:x1 + 1][inside] = 1.0
        painted += 1
log("wood triangles painted into the mask", painted)
log("mask: wood share old", round(float(M0.mean()), 4), "clean", round(float(Mc.mean()), 4))

# ---------------------------------------------------------------- 2. wood colour (grain along the long axis)
wm, em = new_mat("WoodBake")
N, Lk = wm.node_tree.nodes, wm.node_tree.links


def node(t, **kw):
    n = N.new(t)
    for k, v in kw.items(): setattr(n, k, v)
    return n


tc = node("ShaderNodeTexCoord")
V = np.array([v.co for v in body.data.vertices], np.float64)       # LOCAL: Object tex coords are mesh-local
ctr = V.mean(0)
axis = Vector(np.linalg.eigh(np.cov((V - ctr).T))[1][:, -1])
rot = axis.normalized().rotation_difference(Vector((0, 1, 0)))
loc = -(rot.to_matrix() @ Vector(ctr)) + Vector((0.11, 0.0, 0.06))
m1 = node("ShaderNodeMapping"); m1.inputs["Rotation"].default_value = rot.to_euler("XYZ"); m1.inputs["Location"].default_value = loc
Lk.new(tc.outputs["Object"], m1.inputs[0])
m2 = node("ShaderNodeMapping"); m2.inputs["Scale"].default_value = (1.0, 0.3, 1.0)
Lk.new(m1.outputs[0], m2.inputs[0])
rings = node("ShaderNodeTexWave", wave_type="RINGS", rings_direction="Y", wave_profile="SAW")
for k_, v_ in (("Scale", 95.0), ("Distortion", 13.0), ("Detail", 3.0), ("Detail Scale", 0.14), ("Detail Roughness", 0.45)):
    rings.inputs[k_].default_value = v_
Lk.new(m2.outputs[0], rings.inputs[0])
ramp = node("ShaderNodeValToRGB"); cr = ramp.color_ramp
cr.elements[0].position, cr.elements[0].color = WOOD_COLS[0][0], lin(WOOD_COLS[0][1])
cr.elements[1].position, cr.elements[1].color = WOOD_COLS[1][0], lin(WOOD_COLS[1][1])
for pos, c in WOOD_COLS[2:]:
    e = cr.elements.new(pos); e.color = lin(c)
Lk.new(rings.outputs["Fac"], ramp.inputs[0])
m3 = node("ShaderNodeMapping"); m3.inputs["Scale"].default_value = (1.0, 0.03, 1.0)
Lk.new(m1.outputs[0], m3.inputs[0])
streak = node("ShaderNodeTexNoise"); streak.inputs["Scale"].default_value = 160.0; streak.inputs["Detail"].default_value = 2.0
Lk.new(m3.outputs[0], streak.inputs[0])
sr = node("ShaderNodeMapRange"); sr.inputs["To Min"].default_value = 0.92; sr.inputs["To Max"].default_value = 1.05
Lk.new(streak.outputs["Fac"], sr.inputs[0])
broad = node("ShaderNodeTexNoise"); broad.inputs["Scale"].default_value = 4.0; broad.inputs["Detail"].default_value = 1.0
Lk.new(m1.outputs[0], broad.inputs[0])
br = node("ShaderNodeMapRange"); br.inputs["To Min"].default_value = 0.88; br.inputs["To Max"].default_value = 1.06
Lk.new(broad.outputs["Fac"], br.inputs[0])
k = node("ShaderNodeMath", operation="MULTIPLY"); Lk.new(sr.outputs[0], k.inputs[0]); Lk.new(br.outputs[0], k.inputs[1])
col = node("ShaderNodeMixRGB", blend_type="MULTIPLY"); col.inputs[0].default_value = 1.0
Lk.new(ramp.outputs[0], col.inputs[1]); Lk.new(k.outputs[0], col.inputs[2])
Lk.new(col.outputs[0], em.inputs["Color"])
W = bake(wm, bpy.data.images.new("WoodC", RES, RES, alpha=False, float_buffer=True), 24, 16)[..., :3]   # linear

# ---------------------------------------------------------------- 3. compose albedo
A = pixels(load(src_of(ALB)))                                     # sRGB values of the old (sharp) texture
O = blur(pixels(load(AO))[..., 0], 3)                             # 0.4..1
ao = np.clip((O - 0.4) / 0.6, 0, 1)
r, g, b = A[..., 0], A[..., 1], A[..., 2]
L = 0.2126 * r + 0.7152 * g + 0.0722 * b
orange = ((r > 0.12) & (r > g * 1.35) & (r > b * 2.0)).astype(np.float32)
# metal: the model's own shading detail (brightness against its surroundings), but not where it was orange
detail = np.clip((L + 0.01) / (blur(L, 24) + 0.01), 0.55, 1.7) ** 0.55
detail = detail * (1 - blur(orange, 2)) + blur(orange, 2) * 1.0
metal = METAL[None, None, :] * detail[..., None] * (0.72 + 0.28 * ao[..., None])
wood = to_srgb(W * (0.85 + 0.15 * ao[..., None]))
Mk = Mc[..., None]
rgb = metal * (1 - Mk) + wood * Mk
OUT = np.concatenate([np.clip(rgb, 0, 1), A[..., 3:]], -1)

# normal: flat on the wood and on the old orange smears (their grain ridges were baked into it)
NA = pixels(load(src_of(NRM)))
flatten = np.clip(np.maximum(blur(np.clip(blur(Mc, 3) * 3.0, 0, 1), 2), blur(orange, 3)), 0, 1)[..., None]
NOUT = np.concatenate([NA[..., :3] * (1 - flatten) + np.array([0.5, 0.5, 1.0], np.float32) * flatten, NA[..., 3:]], -1)

# metal / smoothness (1024): R metallic, A smoothness
MS = pixels(load(src_of(MSP))); h2 = MS.shape[0]; f2 = RES // h2
Ms = Mc.reshape(h2, f2, h2, f2).mean(axis=(1, 3))
MS[..., 0] = METAL_METALLIC * (1 - Ms)
MS[..., 1] = 0; MS[..., 2] = 0
MS[..., 3] = METAL_SMOOTH * (1 - Ms) + WOOD_SMOOTH * Ms

outs = ((OUT, ALB), (NOUT, NRM), (MS, MSP))
if PREVIEW:
    os.makedirs(PREVIEW, exist_ok=True)
    for arr, path in outs: save(arr, os.path.join(PREVIEW, os.path.basename(path)))
if WRITE:
    import shutil
    for arr, path in outs:
        keep = path.replace(".png", "_sharpwood.png")
        if not os.path.exists(keep): shutil.copy(path, keep); log("old kept as", keep)
        save(arr, path)

# ---------------------------------------------------------------- preview renders: full PBR, like the game
if PREVIEW:
    pm = bpy.data.materials.new("AK_prev"); pm.use_nodes = True
    nt = pm.node_tree
    bs = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    ta = nt.nodes.new("ShaderNodeTexImage"); ta.image = bpy.data.images.load(os.path.join(PREVIEW, os.path.basename(ALB)))
    tm = nt.nodes.new("ShaderNodeTexImage"); tm.image = load(os.path.join(PREVIEW, os.path.basename(MSP)))
    tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = load(os.path.join(PREVIEW, os.path.basename(NRM)))
    nm = nt.nodes.new("ShaderNodeNormalMap")
    sp = nt.nodes.new("ShaderNodeSeparateColor")
    inv = nt.nodes.new("ShaderNodeMath"); inv.operation = "SUBTRACT"; inv.inputs[0].default_value = 1.0
    nt.links.new(ta.outputs[0], bs.inputs["Base Color"])
    nt.links.new(tm.outputs[0], sp.inputs[0]); nt.links.new(sp.outputs[0], bs.inputs["Metallic"])
    nt.links.new(tm.outputs["Alpha"], inv.inputs[1]); nt.links.new(inv.outputs[0], bs.inputs["Roughness"])
    nt.links.new(tn.outputs[0], nm.inputs["Color"]); nt.links.new(nm.outputs[0], bs.inputs["Normal"])
    for o in meshes:
        o.data.materials.clear(); o.data.materials.append(pm)
    pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
    lo = Vector([min(p[i] for p in pts) for i in range(3)]); hi = Vector([max(p[i] for p in pts) for i in range(3)])
    cen = (lo + hi) / 2; size = hi - lo; Lmax = max(size)
    w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.35, 0.37, 0.42, 1)
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sc.collection.objects.link(sun)
    sun.data.energy = 3.0; sun.rotation_euler = (0.9, 0.25, 0.6)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
    sc.cycles.samples = 48
    sc.render.resolution_x, sc.render.resolution_y = 1400, 700
    axes = sorted(range(3), key=lambda i: size[i])
    side = Vector([1.0 if i == axes[0] else 0.0 for i in range(3)])
    shots = (("side", cen, cen + side * Lmax * 1.25, 50),
             ("fore", cen + Vector((0, -0.12, -0.0)) * Lmax, cen + Vector((0, -0.12, 0.1)) * Lmax + side * Lmax * 0.32, 50),
             ("rear", cen + Vector((0, 0.15, -0.1)) * Lmax, cen + Vector((0, 0.15, 0.0)) * Lmax + side * Lmax * 0.4, 50))
    for name, look, at, lens in shots:
        cam.location = at
        cam.rotation_euler = (look - at).to_track_quat("-Z", "Y").to_euler()
        cam.data.lens = lens
        sc.render.filepath = os.path.join(PREVIEW, f"ak_{name}.png"); bpy.ops.render.render(write_still=True)
    log("previews in", PREVIEW)
