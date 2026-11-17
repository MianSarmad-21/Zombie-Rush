"""Suit designs for the player (2026-11-14): five looks baked onto the existing mesh and UVs.

    blender -b --factory-startup -P Blender/suit_designs.py -- [--only Urban,Desert] [--apply NAME]

Writes Blender/suit_designs/Suit_<Name>_Albedo.png and _MetalSmooth.png (outside Assets: nothing in the game
changes). --apply NAME copies that design over Assets/.../ShadowOperative_Albedo.png and _MetalSmooth.png (the
previous ones kept once as *_v1.png).

Regions per face (bone that drives it, height, darkness in the current albedo):
  keep    : skin (skin-coloured) and the head (Head-driven: face, hair, ears) -> current albedo untouched
  gloves  : hand / finger bones (not skin)              -> leather
  boots   : foot / toes, and the lower leg below 24 cm   -> leather
  gear    : texels darker than the fabric (vest, pouches, straps, holster, belt; Otsu split)
  fabric  : the rest of the jacket and pants            -> the design's camo
  accent  : tabs on the vest and a shoulder patch, or an armband (per design)
The camo is laid out in 3D (object space; digital designs snap it to a cell grid) and baked onto the UVs, so it
runs on across UV seams. The current albedo's light/shade detail (folds, seams, stitching) is carried into the
new colours, so the suit keeps its sculpted look.
"""
import os
import shutil
import sys

import bpy
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ONLY = set(argv[argv.index("--only") + 1].split(",")) if "--only" in argv else None
APPLY = argv[argv.index("--apply") + 1] if "--apply" in argv else None
PROJECT = "C:/Mian Sarmad/Unity/Zombie-Rush/Zombie Rush"
FOLDER = PROJECT + "/Assets/Models/2026-11-14_ShadowOperative_Final/"
OUTDIR = PROJECT + "/Blender/suit_designs/"
S = 2048
KEEP, GLOVES, BOOTS, GEAR, FABRIC, ACCENT = range(6)

DESIGNS = {
    # camo: 4 tones dark -> light with ramp break points; snap: digital cell size (0 = organic shapes)
    "Urban": dict(camo=["2b2e31", "5d6166", "8b8f93", "b4b7ba"], breaks=[0.50, 0.585, 0.69], snap=0.0, scale=(2.8, 7.5),
                  gear="1e2022", gloves="17181a", boots="1b1b1c", accent="e8641c", accent_mode="tabs", grime=0.0),
    "Desert": dict(camo=["6e5a3f", "7c7a55", "a08660", "c2a77d"], breaks=[0.40, 0.50, 0.62], snap=0.0, scale=(2.6, 7.0),
                   gear="7a6246", gloves="4d4e36", boots="a88c63", accent="3f4a35", accent_mode="tabs", grime=0.0),
    "BlackOps": dict(camo=["13161c", "181c24", "1d222c", "232936"], breaks=[0.45, 0.55, 0.65], snap=0.0, scale=(3.0, 8.0),
                     gear="0f1012", gloves="0e0e0f", boots="101011", accent="b3201e", accent_mode="armband", grime=0.0,
                     cover_forearms=True),      # long black gloves up the forearms (user: "make the hands black too")
    "Woodland": dict(camo=["2a2a22", "4b5a36", "6b5a3d", "8a8a62"], breaks=[0.40, 0.52, 0.64], snap=0.0, scale=(2.6, 7.0),
                     gear="4a4d33", gloves="4a3322", boots="3e2c1e", accent="a39a6e", accent_mode="tabs", grime=0.35),
    "Arctic": dict(camo=["5f676d", "8f979d", "c3c8cc", "e6e8ea"], breaks=[0.36, 0.48, 0.60], snap=0.0, scale=(2.8, 7.5),
                   gear="4a5056", gloves="2a2d30", boots="3a3d40", accent="1f9fd6", accent_mode="tabs", grime=0.0),
}


def log(*a):
    print("[suit]", *a, flush=True)


def srgb(h):
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


def save(arr, path, cs):
    h, w = arr.shape[:2]
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=True); img.colorspace_settings.name = cs
    img.pixels.foreach_set(arr.astype(np.float32).ravel()); img.filepath_raw = path; img.file_format = "PNG"; img.save()
    log("saved", path)


if APPLY:
    for n in ("Albedo", "MetalSmooth"):
        src, v1 = FOLDER + f"ShadowOperative_{n}.png", FOLDER + f"ShadowOperative_{n}_v1.png"
        if not os.path.exists(v1): shutil.copy(src, v1); log("previous kept as", v1)
        shutil.copy(OUTDIR + f"Suit_{APPLY}_{n}.png", src); log("applied", APPLY, n)
    raise SystemExit(0)

for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.import_scene.fbx(filepath=FOLDER + "ShadowOperative.fbx")
mesh = max((o for o in bpy.data.objects if o.type == "MESH"), key=lambda o: len(o.data.vertices))
me = mesh.data
# a few Meshy slivers carry giant UV triangles across the atlas (the thin diagonal lines): baked last they paint over
# ~14 % of the texels (gloves, knees came out unchanged). Leave them out of this bake copy.
import bmesh
_bm = bmesh.new(); _bm.from_mesh(me); _uv = _bm.loops.layers.uv.active
_big = []
for f in _bm.faces:
    pts = [l[_uv].uv for l in f.loops]
    a = 0.5 * abs(sum(pts[i].x * pts[i - 1].y - pts[i - 1].x * pts[i].y for i in range(len(pts))))
    if a > 1e-3: _big.append(f)
bmesh.ops.delete(_bm, geom=_big, context="FACES_ONLY")
_nq = sum(1 for f in _bm.faces if len(f.verts) > 3)
bmesh.ops.triangulate(_bm, faces=_bm.faces[:])          # quads/n-gons lost their region in the bake
_bm.to_mesh(me); _bm.free(); me.update()
print("[suit] triangulated", _nq, "quads/n-gons for the bake")
print("[suit] left out", len(_big), "giant-UV sliver faces")
src_alb = FOLDER + ("ShadowOperative_Albedo_v1.png" if os.path.exists(FOLDER + "ShadowOperative_Albedo_v1.png") else "ShadowOperative_Albedo.png")
src_ms = FOLDER + ("ShadowOperative_MetalSmooth_v1.png" if os.path.exists(FOLDER + "ShadowOperative_MetalSmooth_v1.png") else "ShadowOperative_MetalSmooth.png")
alb_img = bpy.data.images.load(src_alb)
AW, AH = alb_img.size
if (AW, AH) != (S, S): raise SystemExit("albedo is not 2048")
ALB = np.array(alb_img.pixels[:], np.float32).reshape(AH, AW, 4)
uvl = me.uv_layers.active.data

# ---------------------------------------------------------------- classify faces
names = [g.name for g in mesh.vertex_groups]
dom = []
for v in me.vertices:
    best = max(v.groups, key=lambda g: g.weight, default=None)
    dom.append(names[best.group] if best else "")


def sample_alb(p):
    u = sum((uvl[li].uv for li in p.loop_indices), Vector((0, 0))) / p.loop_total
    x, y = min(AW - 1, max(0, int(u.x * AW))), min(AH - 1, max(0, int(u.y * AH)))
    return ALB[y, x, :3]


def is_skin(c):
    r, g, b = c
    return r > g and g > b * 0.95 and r > 0.28 and r - b > 0.08


def lum(c): return float(0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2])


FEET = ("LeftFoot", "RightFoot", "LeftToes", "RightToes")
pre = []
for p in me.polygons:
    bones = [dom[i] for i in p.vertices]
    pre.append((p.index, max(set(bones), key=bones.count), sample_alb(p), p.center.copy()))
L = np.array([lum(c) for _, b, c, _ in pre if not is_skin(c) and b != "Head" and "Hand" not in b and b not in FEET])
hist, edges = np.histogram(L, bins=64, range=(0, max(0.05, float(L.max()))))
cen = (edges[:-1] + edges[1:]) / 2
best_t, best_v = edges[1], -1
for k in range(1, 63):
    w0, w1 = hist[:k].sum(), hist[k:].sum()
    if w0 == 0 or w1 == 0: continue
    m0, m1 = (hist[:k] * cen[:k]).sum() / w0, (hist[k:] * cen[k:]).sum() / w1
    v = w0 * w1 * (m0 - m1) ** 2
    if v > best_v: best_v, best_t = v, edges[k]
GEAR_T = float(best_t)
log(f"gear/fabric luminance split {GEAR_T:.3f}")


SKIN_BONES = ("Head", "Neck", "LeftLowerArm", "RightLowerArm", "UpperChest", "Chest")   # bare skin only here


def classify(accent_mode, cover_forearms=False):
    """Per face. Gear vs fabric is NOT decided here (per face it made a triangle mosaic): both are FABRIC and the
    composite splits them per texel from the old albedo's darkness."""
    region = np.zeros(len(me.polygons), np.int32)
    for idx, bone, col, c in pre:
        if cover_forearms and bone.endswith("LowerArm") and is_skin(col): r = GLOVES
        elif bone == "Head" or (bone in SKIN_BONES and is_skin(col)): r = KEEP
        elif "Hand" in bone: r = GLOVES
        elif bone in FEET or (bone.endswith("LowerLeg") and c.z < 0.24): r = BOOTS
        else: r = FABRIC
        if r == FABRIC:
            if accent_mode == "tabs":
                if lum(col) < GEAR_T and c.y < -0.06 and 0.045 < c.x < 0.11 and 1.30 < c.z < 1.36: r = ACCENT
                if bone == "LeftUpperArm" and c.x > 0.20 and 1.30 < c.z < 1.40 and abs(c.y) < 0.04: r = ACCENT
            elif accent_mode == "armband":
                if bone == "LeftUpperArm" and lum(col) >= GEAR_T and 0.17 < c.x < 0.27: r = ACCENT
        region[idx] = r
    return region


# ---------------------------------------------------------------- bake set-up
sc = bpy.context.scene
sc.render.engine = "CYCLES"; sc.cycles.samples = 1; sc.cycles.device = "CPU"
for o in bpy.data.objects: o.select_set(o is mesh)
bpy.context.view_layer.objects.active = mesh
attr = me.attributes.new("suit_region", "FLOAT", "FACE")


def bake(material, name):
    img = bpy.data.images.new(name, S, S, alpha=False, float_buffer=True); img.colorspace_settings.name = "Non-Color"
    me.materials.clear(); me.materials.append(material)
    me.polygons.foreach_set("material_index", [0] * len(me.polygons))   # every face bakes (the body has several slots)
    for n in material.node_tree.nodes: n.select = False
    tex = material.node_tree.nodes.new("ShaderNodeTexImage"); tex.image = img; tex.select = True
    material.node_tree.nodes.active = tex
    bpy.ops.object.bake(type="EMIT", margin=16, use_clear=True)
    material.node_tree.nodes.remove(tex)
    return np.array(img.pixels[:], np.float32).reshape(S, S, 4)[..., :3]


def design_material(d):
    mat = bpy.data.materials.new("SuitBake"); mat.use_nodes = True
    nt = mat.node_tree; N, Lk = nt.nodes, nt.links
    for n in list(N): N.remove(n)
    out = N.new("ShaderNodeOutputMaterial"); emis = N.new("ShaderNodeEmission"); Lk.new(emis.outputs[0], out.inputs[0])
    att = N.new("ShaderNodeAttribute"); att.attribute_name = "suit_region"; att.attribute_type = "GEOMETRY"
    coord = N.new("ShaderNodeTexCoord")
    pos = coord.outputs["Object"]
    if d["snap"] > 0:
        snap = N.new("ShaderNodeVectorMath"); snap.operation = "SNAP"; snap.inputs[1].default_value = (d["snap"],) * 3
        Lk.new(pos, snap.inputs[0]); pos = snap.outputs[0]

    def noise(scale, detail, off):
        add = N.new("ShaderNodeVectorMath"); add.operation = "ADD"; add.inputs[1].default_value = off
        Lk.new(pos, add.inputs[0])
        nz = N.new("ShaderNodeTexNoise"); nz.inputs["Scale"].default_value = scale; nz.inputs["Detail"].default_value = detail
        nz.inputs["Roughness"].default_value = 0.55; Lk.new(add.outputs[0], nz.inputs["Vector"]); return nz

    n1, n2 = noise(d["scale"][0], 2.0, (3.1, 7.7, 1.3)), noise(d["scale"][1], 1.0, (9.2, 2.4, 5.5))
    mixv = N.new("ShaderNodeMath"); mixv.operation = "MULTIPLY_ADD"; mixv.inputs[1].default_value = 0.35
    Lk.new(n2.outputs["Fac"], mixv.inputs[0]); Lk.new(n1.outputs["Fac"], mixv.inputs[2])
    sub = N.new("ShaderNodeMath"); sub.operation = "SUBTRACT"; sub.inputs[1].default_value = 0.175
    Lk.new(mixv.outputs[0], sub.inputs[0])
    cr = N.new("ShaderNodeValToRGB"); cr.color_ramp.interpolation = "CONSTANT"
    els = cr.color_ramp.elements
    while len(els) < 4: els.new(0.5)
    for el, p, col in zip(els, [0.0] + d["breaks"], d["camo"]):
        el.position = p; el.color = (*srgb(col), 1.0)
    Lk.new(sub.outputs[0], cr.inputs[0])

    grain = N.new("ShaderNodeTexNoise"); grain.inputs["Scale"].default_value = 420.0; grain.inputs["Detail"].default_value = 0.0
    Lk.new(coord.outputs["Object"], grain.inputs["Vector"])
    mott = N.new("ShaderNodeTexNoise"); mott.inputs["Scale"].default_value = 60.0; mott.inputs["Detail"].default_value = 4.0
    Lk.new(coord.outputs["Object"], mott.inputs["Vector"])

    def const(rgb):
        n = N.new("ShaderNodeRGB"); n.outputs[0].default_value = (*rgb, 1.0); return n.outputs[0]

    def keep_tex():
        """the current albedo where the design keeps it (no marker colour to bleed over island edges)"""
        tx = N.new("ShaderNodeTexImage"); tx.image = alb_img; return tx.outputs["Color"]

    def leather(rgb):
        m = N.new("ShaderNodeMixRGB")
        f = N.new("ShaderNodeMath"); f.operation = "MULTIPLY"; f.inputs[1].default_value = 0.35
        Lk.new(mott.outputs["Fac"], f.inputs[0]); Lk.new(f.outputs[0], m.inputs[0])
        Lk.new(const(rgb), m.inputs[1]); Lk.new(const(tuple(min(1, x * 2.4) for x in rgb)), m.inputs[2])
        return m.outputs[0]

    prev = cr.outputs["Color"]
    for k, sock in ((GEAR, const(srgb(d["gear"]))), (GLOVES, leather(srgb(d["gloves"]))), (BOOTS, leather(srgb(d["boots"]))),
                    (ACCENT, const(srgb(d["accent"]))), (KEEP, keep_tex())):
        cmp = N.new("ShaderNodeMath"); cmp.operation = "COMPARE"; cmp.inputs[1].default_value = float(k); cmp.inputs[2].default_value = 0.1
        Lk.new(att.outputs["Fac"], cmp.inputs[0])
        mx = N.new("ShaderNodeMixRGB"); Lk.new(cmp.outputs[0], mx.inputs[0]); Lk.new(prev, mx.inputs[1]); Lk.new(sock, mx.inputs[2])
        prev = mx.outputs[0]
    g2 = N.new("ShaderNodeMath"); g2.operation = "MULTIPLY_ADD"; g2.inputs[1].default_value = 0.12; g2.inputs[2].default_value = 0.94
    Lk.new(grain.outputs["Fac"], g2.inputs[0])
    gm = N.new("ShaderNodeMixRGB"); gm.blend_type = "MULTIPLY"; gm.inputs[0].default_value = 1.0
    Lk.new(prev, gm.inputs[1]); Lk.new(g2.outputs[0], gm.inputs[2])
    Lk.new(gm.outputs[0], emis.inputs["Color"])
    return mat


def region_material():
    m = bpy.data.materials.new("RegionBake"); m.use_nodes = True; nr = m.node_tree
    for n in list(nr.nodes): nr.nodes.remove(n)
    o2 = nr.nodes.new("ShaderNodeOutputMaterial"); e2 = nr.nodes.new("ShaderNodeEmission")
    a2 = nr.nodes.new("ShaderNodeAttribute"); a2.attribute_name = "suit_region"; a2.attribute_type = "GEOMETRY"
    dv = nr.nodes.new("ShaderNodeMath"); dv.operation = "DIVIDE"; dv.inputs[1].default_value = 10.0
    nr.links.new(a2.outputs["Fac"], dv.inputs[0]); nr.links.new(dv.outputs[0], e2.inputs["Color"]); nr.links.new(e2.outputs[0], o2.inputs[0])
    return m


def box(a, r):
    c = np.cumsum(np.cumsum(np.pad(a, ((r + 1, r), (r + 1, r)), mode="edge"), 0), 1)
    k = 2 * r + 1
    return (c[k:, k:] - c[:-k, k:] - c[k:, :-k] + c[:-k, :-k]) / (k * k)


srgb_old = ALB[..., :3]
lin_old = np.where(srgb_old <= 0.04045, srgb_old / 12.92, ((srgb_old + 0.055) / 1.055) ** 2.4)
Lold = (lin_old * [0.2126, 0.7152, 0.0722]).sum(-1)
detail = np.clip(Lold / np.maximum(box(box(Lold, 10), 10), 1e-3), 0.45, 1.8) ** 0.85
ms_img = bpy.data.images.load(src_ms)
MW, MH = ms_img.size
MS = np.array(ms_img.pixels[:], np.float32).reshape(MH, MW, 4)
if (MW, MH) != (S, S): MS = MS[np.arange(S) * MH // S][:, np.arange(S) * MW // S]
# height of each texel (for knee grime): bake object Z once
zmat = bpy.data.materials.new("ZBake"); zmat.use_nodes = True; zn = zmat.node_tree
for n in list(zn.nodes): zn.nodes.remove(n)
zo = zn.nodes.new("ShaderNodeOutputMaterial"); ze = zn.nodes.new("ShaderNodeEmission"); zc = zn.nodes.new("ShaderNodeTexCoord")
zs = zn.nodes.new("ShaderNodeSeparateXYZ"); zn.links.new(zc.outputs["Object"], zs.inputs[0])
zn.links.new(zs.outputs["Z"], ze.inputs["Color"]); zn.links.new(ze.outputs[0], zo.inputs[0])
ZT = bake(zmat, "z")[..., 0]
os.makedirs(OUTDIR, exist_ok=True)
# gear (vest, pouches, straps, holster) vs camo fabric, per texel: darker than the split in the old albedo = gear
Ls = box((srgb_old * [0.2126, 0.7152, 0.0722]).sum(-1), 2)
GW = np.clip((GEAR_T + 0.02 - Ls) / 0.04, 0, 1)

for name, d in DESIGNS.items():
    if ONLY and name not in ONLY: continue
    region = classify(d["accent_mode"], d.get("cover_forearms", False))
    attr.data.foreach_set("value", region.astype(np.float32))
    me.update()
    COLB = bake(design_material(d), "col_" + name)
    REG = np.rint(bake(region_material(), "reg_" + name)[..., 0] * 10).astype(np.int32)
    if "--debug" in argv:
        np.save(OUTDIR + f"_reg_{name}.npy", REG); np.save(OUTDIR + "_region_faces.npy", region)
    fab = (REG == FABRIC)
    gw = np.where(fab, GW, 0.0)[..., None]
    COLB = COLB * (1 - gw) + np.array(srgb(d["gear"])) * gw
    REG = np.where(fab & (GW > 0.5), GEAR, REG)              # for the smoothness map
    lin_new = COLB * detail[..., None]
    lea = np.isin(REG, (GLOVES, BOOTS))[..., None]
    lin_new = np.where(lea, lin_new * (1 + 0.6 * np.clip(detail[..., None] - 1, 0, 1)), lin_new)
    if d["grime"] > 0:                                   # dirt rising from the boots, heaviest round the knees
        g = np.clip(1 - np.abs(ZT - 0.45) / 0.35, 0, 1) * d["grime"] + np.clip((0.35 - ZT) / 0.35, 0, 1) * d["grime"] * 0.6
        dirt = np.array(srgb("3a3226"))
        cloth = np.isin(REG, (FABRIC, GEAR, BOOTS))[..., None]
        lin_new = np.where(cloth, lin_new * (1 - g[..., None] * 0.5) + dirt * g[..., None] * 0.5 * detail[..., None], lin_new)
    keep = (REG == KEEP)[..., None]
    lin_out = np.where(keep, lin_old, np.clip(lin_new, 0, 1))
    out_rgb = np.where(lin_out <= 0.0031308, lin_out * 12.92, 1.055 * np.power(lin_out, 1 / 2.4) - 0.055)
    save(np.concatenate([np.clip(out_rgb, 0, 1), ALB[..., 3:]], -1), OUTDIR + f"Suit_{name}_Albedo.png", "sRGB")
    smooth = {GLOVES: 0.48, BOOTS: 0.40, GEAR: 0.22, FABRIC: 0.12, ACCENT: 0.30}
    MSN = MS.copy()
    for k, sv in smooth.items():
        m = REG == k
        MSN[m, 3] = sv * np.clip(detail[m], 0.8, 1.2); MSN[m, :3] = 0.0
    save(MSN, OUTDIR + f"Suit_{name}_MetalSmooth.png", "Non-Color")
    log(name, "texels per region (keep, gloves, boots, gear, fabric, accent):", np.bincount(REG.ravel().clip(0, 5), minlength=6).tolist())
