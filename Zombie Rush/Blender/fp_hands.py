"""First-person gloved hands with five jointed fingers, and a grip solver that closes them on the gun.

exec() this from fp_viewmodel.py after the arms are lengthened, with `rig` (FP_Rig), `arms` (FP_Arms),
`log`, `upd` and `ctx` in globals. It:
  1. deletes the survivor's hand vertices and its two finger chains (a thumb and one finger block);
  2. adds Thumb, Index, Middle, Ring and Pinky chains (3 bones each) to each Hand bone, sized to an
     adult hand (wrist to middle fingertip 19.5 cm);
  3. builds the hand mesh from rounded parts (palm, cuff, knuckle pad, a capsule per finger segment),
     each part riding one bone, so bending a finger never stretches the mesh; half-finger gloves:
     palm and first finger segments are glove, the rest is skin;
  4. gives the grip solver: hand frames, finger forward kinematics, contact closing of fingers against
     a BVH (the gun) plus capsules (the other hand), and a reach solver for the trigger finger and thumbs.

Hand coordinates (metres) are (t, u, n): t toward the thumb side, u toward the fingertips, n out of the
palm. The wrist joint is the origin.
"""
import math
import bmesh
import numpy as np
from mathutils import Vector, Matrix, Quaternion
from mathutils.bvhtree import BVHTree

SIDES = ("Right", "Left")
DIGITS = ("Thumb", "Index", "Middle", "Ring", "Pinky")
FINGERS4 = ("Index", "Middle", "Ring", "Pinky")

# ---------------------------------------------------------------- hand measurements (t, u, n)
MCP = {"Index": (0.030, 0.096, 0.001), "Middle": (0.009, 0.100, 0.0),
       "Ring": (-0.011, 0.096, 0.001), "Pinky": (-0.028, 0.086, 0.004)}
SEG = {"Index": (0.042, 0.025, 0.022), "Middle": (0.046, 0.029, 0.023),
       "Ring": (0.043, 0.027, 0.022), "Pinky": (0.034, 0.021, 0.020)}
RAD = {"Index": (0.0097, 0.0089, 0.0082, 0.0074), "Middle": (0.0101, 0.0093, 0.0085, 0.0076),
       "Ring": (0.0095, 0.0087, 0.0080, 0.0072), "Pinky": (0.0085, 0.0078, 0.0072, 0.0065)}
SPLAY = {"Index": 5.0, "Middle": 0.0, "Ring": -5.0, "Pinky": -11.0}      # rest spread, + toward the thumb
CMC = (0.026, 0.022, 0.009)
T_SEG = (0.044, 0.032, 0.028)
T_RAD = (0.0145, 0.0118, 0.0102, 0.0094, 0.0082)
T_DIR = ((0.66, 0.62, 0.42), (0.40, 0.82, 0.41), (0.36, 0.86, 0.36))
PALM = dict(u0=0.004, u1=0.100, half_t=0.042, taper=0.78, n_palm=0.0155, n_back=-0.0115, e=0.5)
# segment numbers (1 = proximal) that are glove; the rest are skin. FULL_GLOVES (set by the caller) covers all three.
GLOVE_SEGS = {1, 2, 3} if globals().get("FULL_GLOVES") else {1}
CLEAR = 0.0012              # contact gap kept between a finger and the gun (m)

def tun_matrix(side):
    """3x3 whose columns are t, u, n in armature space, from the rest bones."""
    b = rig.data.bones
    W = b[side + "Hand"].head_local
    u = (W - b[side + "LowerArm"].head_local).normalized()
    th = b[side + "HandThumb1"].head_local if (side + "HandThumb1") in b else HAND_INFO[side]["thumb_hint"]
    t = th - W; t = (t - u * t.dot(u)).normalized()
    n = t.cross(u) if side == "Right" else u.cross(t)
    return Matrix((t, u, n)).transposed(), W.copy()

# ---------------------------------------------------------------- 1. new bones
HAND_INFO = {}
for side in SIDES:
    R, W = tun_matrix(side)
    HAND_INFO[side] = dict(R=R, W=W, thumb_hint=rig.data.bones[side + "HandThumb1"].head_local.copy())

def hpt(side, p):
    """hand coords -> armature space (rest)."""
    h = HAND_INFO[side]
    return h["W"] + h["R"] @ Vector(p)

def hdir(side, d):
    return (HAND_INFO[side]["R"] @ Vector(d)).normalized()

def chain_points(side, digit):
    """Rest joint positions (4 points: base, two knuckles, tip) and segment radii."""
    if digit == "Thumb":
        p = [hpt(side, CMC)]
        for d, L in zip(T_DIR, T_SEG):
            p.append(p[-1] + hdir(side, d) * L)
        return p, T_RAD
    a = math.radians(SPLAY[digit])
    d = hdir(side, (math.sin(a), math.cos(a), 0.0))
    p = [hpt(side, MCP[digit])]
    for L in SEG[digit]:
        p.append(p[-1] + d * L)
    return p, RAD[digit]

old_fingers = [b.name for b in rig.data.bones if any(b.name.startswith(s + "Hand") and b.name != s + "Hand" for s in SIDES)]
# drop the survivor's hand vertices (hand and finger weights) before those bones go
_hg = {vg.index for vg in arms.vertex_groups if vg.name in old_fingers or vg.name in ("RightHand", "LeftHand")}
gone = [v.index for v in arms.data.vertices if sum(g.weight for g in v.groups if g.group in _hg) >= 0.3]
bm = bmesh.new(); bm.from_mesh(arms.data); bm.verts.ensure_lookup_table()
bmesh.ops.delete(bm, geom=[bm.verts[i] for i in gone], context="VERTS")
bm.to_mesh(arms.data); bm.free(); arms.data.update()
bpy.context.view_layer.objects.active = rig
for o in bpy.context.view_layer.objects: o.select_set(o is rig)
with ctx(rig): bpy.ops.object.mode_set(mode="EDIT")
eb = rig.data.edit_bones
for nme in old_fingers:
    if nme in eb: eb.remove(eb[nme])
for side in SIDES:
    hand = eb[side + "Hand"]
    nrm = hdir(side, (0, 0, 1)); tdir = hdir(side, (1, 0, 0))
    for digit in DIGITS:
        pts, _ = chain_points(side, digit)
        parent = hand
        for k in range(3):
            bn = eb.new(f"{side}Hand{digit}{k + 1}")
            bn.head, bn.tail = pts[k], pts[k + 1]
            y = (pts[k + 1] - pts[k]).normalized()
            if digit == "Thumb":
                flex = nrm * 0.80 - tdir * 0.60          # a thumb folds into the palm, toward the little finger
                z = (flex - y * flex.dot(y)).normalized()
            else:
                z = (nrm - y * nrm.dot(y)).normalized()
            bn.align_roll(z)                              # Z = fold side, so +X turns curl the digit
            bn.parent = parent; bn.use_connect = k > 0
            bn.use_deform = True
            parent = bn
bpy.ops.object.mode_set(mode="OBJECT")
upd()
for vg in list(arms.vertex_groups):
    if vg.name in old_fingers: arms.vertex_groups.remove(vg)
for pb in rig.pose.bones:
    pb.rotation_mode = "QUATERNION"
log(f"hand bones: removed {len(old_fingers)}, added {len(SIDES) * len(DIGITS) * 3}")

# ---------------------------------------------------------------- 2. sleeves rolled up to mid forearm
# The survivor's sleeves are thick at the wrist and hide the hands from the eye. First person shows them
# rolled up: the sleeve is cut ROLL_AT of the way down the forearm, a cloth roll sits on the cut, and a
# slim bare forearm runs from there to the glove.
ROLL_AT = 0.45                      # share of the forearm (from the elbow) the sleeve still covers
AW = arms.matrix_world
SLEEVE = {}
side_of = {}
for vg in arms.vertex_groups:
    side_of[vg.index] = "Right" if vg.name.startswith("Right") else ("Left" if vg.name.startswith("Left") else None)
cut_ids = []
for side in SIDES:
    R, W = HAND_INFO[side]["R"], HAND_INFO[side]["W"]
    L = (W - rig.data.bones[side + "LowerArm"].head_local).length
    u_cut = -L * (1 - ROLL_AT)
    ring = []
    for v in arms.data.vertices:
        sw = {}
        for g_ in v.groups:
            sd = side_of.get(g_.group)
            if sd: sw[sd] = sw.get(sd, 0) + g_.weight
        if not sw or max(sw, key=sw.get) != side: continue
        p = R.transposed() @ (AW @ v.co - W)
        if p.y > u_cut: cut_ids.append(v.index)
        elif p.y > u_cut - 0.03: ring.append(p)
    xs = [p.x for p in ring] or [-0.045, 0.045]; zs = [p.z for p in ring] or [-0.04, 0.04]
    SLEEVE[side] = dict(L=L, cut=u_cut, t=(max(xs) - min(xs)) / 2, n=(max(zs) - min(zs)) / 2,
                        ct=(max(xs) + min(xs)) / 2, cn=(max(zs) + min(zs)) / 2)
    log(f"{side} sleeve cut {abs(u_cut) * 100:.1f} cm before the wrist; there it is {SLEEVE[side]['t'] * 200:.1f} x {SLEEVE[side]['n'] * 200:.1f} cm")
cut_set = set(cut_ids)
# the kept sleeve narrows over its last TAPER metres to a rolled opening ROLL_T x ROLL_N half sizes
TAPER, ROLL_T, ROLL_N = 0.08, 0.047, 0.041
for side in SIDES:
    R, W = HAND_INFO[side]["R"], HAND_INFO[side]["W"]; sl = SLEEVE[side]
    kt, kn = min(1.0, ROLL_T / max(sl["t"], 1e-3)), min(1.0, ROLL_N / max(sl["n"], 1e-3))
    for v in arms.data.vertices:
        if v.index in cut_set: continue
        sw = {}
        for g_ in v.groups:
            sd = side_of.get(g_.group)
            if sd: sw[sd] = sw.get(sd, 0) + g_.weight
        if not sw or max(sw, key=sw.get) != side: continue
        p = R.transposed() @ (AW @ v.co - W)
        w = (p.y - (sl["cut"] - TAPER)) / TAPER
        if w <= 0: continue
        w = min(1.0, w); w = w * w * (3 - 2 * w)
        p.x = sl["ct"] + (p.x - sl["ct"]) * (1 + (kt - 1) * w)
        p.z = sl["cn"] + (p.z - sl["cn"]) * (1 + (kn - 1) * w)
        v.co = AW.inverted() @ (W + R @ p)
    sl["t"], sl["n"] = sl["t"] * kt, sl["n"] * kn
bm = bmesh.new(); bm.from_mesh(arms.data); bm.verts.ensure_lookup_table()
bmesh.ops.delete(bm, geom=[bm.verts[i] for i in cut_ids], context="VERTS")
bm.to_mesh(arms.data); bm.free(); arms.data.update()
log(f"FP_Arms: {len(gone)} hand and {len(cut_ids)} lower-sleeve vertices removed, {len(arms.data.vertices)} left")

# ---------------------------------------------------------------- 3. the hand mesh
VERTS, FACES, FMAT, VW = [], [], [], []      # positions (armature space), faces, material per face, [(group, w)]

def add_part(vs, fs, weights, mat):
    base = len(VERTS)
    VERTS.extend(vs); VW.extend(weights)
    for f in fs:
        FACES.append(tuple(i + base for i in f)); FMAT.append(mat)

def capsule(A, B, ra, rb, group, mat, seg=12, hr=4, tip=1.0):
    """A tapered capsule from A to B (radius ra -> rb), rounded at both ends; tip > 1 lengthens the
    B end's cap (a fingertip)."""
    y = (B - A); L = y.length; y.normalize()
    x = y.orthogonal().normalized(); z = y.cross(x)
    rings = []
    for i in range(1, hr + 1):                     # A cap, from the pole out to the equator
        ph = -math.pi / 2 + (math.pi / 2) * i / hr
        rings.append((A + y * (ra * math.sin(ph)), ra * math.cos(ph)))
    for i in range(1, 3):
        w = i / 3
        rings.append((A + y * (L * w), ra + (rb - ra) * w))
    for i in range(0, hr):                         # B cap, equator to just short of the pole
        ph = (math.pi / 2) * i / hr
        rings.append((B + y * (rb * math.sin(ph) * tip), rb * math.cos(ph)))
    vs = [A - y * ra]
    for c, r in rings:
        for k in range(seg):
            a = 2 * math.pi * k / seg
            vs.append(c + (x * math.cos(a) + z * math.sin(a)) * r)
    vs.append(B + y * rb * tip)
    fs = [(0, 1 + (k + 1) % seg, 1 + k) for k in range(seg)]
    for ri in range(len(rings) - 1):
        a0, a1 = 1 + ri * seg, 1 + (ri + 1) * seg
        for k in range(seg):
            k2 = (k + 1) % seg
            fs.append((a0 + k, a0 + k2, a1 + k2, a1 + k))
    last = 1 + (len(rings) - 1) * seg; top = len(vs) - 1
    fs += [(last + k, last + (k + 1) % seg, top) for k in range(seg)]
    add_part(vs, fs, [[(group, 1.0)]] * len(vs), mat)

def rounded_box(side, centre, half, e, cuts, weight_fn, mat, shape=None):
    """A superellipsoid (rounded box) in hand coords: centre and half sizes (t, u, n); shape(p) may bend
    the local point before it goes to armature space."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=2.0)
    bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=cuts, use_grid_fill=True)
    bm.verts.ensure_lookup_table(); bm.faces.ensure_lookup_table()
    vs, ws = [], []
    for v in bm.verts:
        d = v.co.normalized()
        s = sum((abs(d[i]) / half[i]) ** (2.0 / e) for i in range(3)) ** (-e / 2.0)
        p = Vector(centre) + d * s
        if shape: p = shape(p)
        vs.append(hpt(side, p)); ws.append(weight_fn(p))
    fs = [tuple(v.index for v in f.verts) for f in bm.faces]
    bm.free()
    add_part(vs, fs, ws, mat)

GLOVE, SKIN, CLOTH, TRIM = 1, 2, 3, 4

def tube(side, rings, weight_fn, mat, seg=14):
    """A closed tube in hand coords: rings = [(u, half_t, half_n, centre_t, centre_n), ...]."""
    vs, ws = [], []
    for (uu, ht, hn, ct, cn) in rings:
        for k in range(seg):
            a = 2 * math.pi * k / seg
            p = Vector((ct + ht * math.cos(a), uu, cn + hn * math.sin(a)))
            vs.append(hpt(side, p)); ws.append(weight_fn(p))
    for end in (0, len(rings) - 1):
        uu, ht, hn, ct, cn = rings[end]
        p = Vector((ct, uu, cn)); vs.append(hpt(side, p)); ws.append(weight_fn(p))
    fs = []
    for r in range(len(rings) - 1):
        for k in range(seg):
            k2 = (k + 1) % seg
            fs.append((r * seg + k, r * seg + k2, (r + 1) * seg + k2, (r + 1) * seg + k))
    c0, c1 = len(rings) * seg, len(rings) * seg + 1
    last = (len(rings) - 1) * seg
    fs += [(c0, (k + 1) % seg, k) for k in range(seg)] + [(c1, last + k, last + (k + 1) % seg) for k in range(seg)]
    add_part(vs, fs, ws, mat)

def torus(side, uu, ht, hn, ct, cn, minor, weight_fn, mat, seg=16, mseg=8):
    vs, ws = [], []
    for k in range(seg):
        a = 2 * math.pi * k / seg
        c = Vector((ct + ht * math.cos(a), uu, cn + hn * math.sin(a)))
        out = Vector((math.cos(a) * hn, 0, math.sin(a) * ht)).normalized()
        for j in range(mseg):
            b = 2 * math.pi * j / mseg
            p = c + out * (minor * math.cos(b)) + Vector((0, minor * 1.3 * math.sin(b), 0))
            vs.append(hpt(side, p)); ws.append(weight_fn(p))
    fs = []
    for k in range(seg):
        for j in range(mseg):
            k2, j2 = (k + 1) % seg, (j + 1) % mseg
            fs.append((k * mseg + j, k2 * mseg + j, k2 * mseg + j2, k * mseg + j2))
    add_part(vs, fs, ws, mat)
for side in SIDES:
    H = side + "Hand"
    P = PALM
    cu = (P["u0"] + P["u1"]) / 2; hu = (P["u1"] - P["u0"]) / 2
    cn = (P["n_palm"] + P["n_back"]) / 2; hn = (P["n_palm"] - P["n_back"]) / 2
    def palm_shape(p, P=P, cu=cu, hu=hu):
        w = (p.y - P["u0"]) / (P["u1"] - P["u0"])
        p = p.copy(); p.x *= P["taper"] + (1 - P["taper"]) * max(0.0, min(1.0, w))
        p.z += 0.004 * (p.x / P["half_t"]) ** 2        # cupped palm: the edges sit toward the palm side
        return p
    rounded_box(side, (0.0, cu, cn), (P["half_t"], hu, hn), P["e"], 6, lambda p: [(H, 1.0)], GLOVE, palm_shape)
    # knuckle pad over the back of the knuckles: narrower than the glove and bent with its back, so its
    # ends stay inside the glove (a flat, full-width pad stood out as fins seen edge-on from the eye)
    PAD_T = 0.032
    def pad_shape(p, PAD_T=PAD_T):
        p = p.copy(); p.z += 0.0065 * (p.x / PAD_T) ** 2
        return p
    rounded_box(side, (0.002, 0.084, P["n_back"] - 0.0005), (PAD_T, 0.012, 0.0045), 0.45, 3, lambda p: [(H, 1.0)], TRIM, pad_shape)
    # cuff over the sleeve's end, blending from the forearm to the hand
    sl = SLEEVE[side]
    c0, c1 = -0.035, 0.014                 # starts 1.7 cm inside the sleeve: longer, it poked through on bent wrists
    def cuff_w(p, H=H, side=side, c0=c0):
        w = max(0.0, min(1.0, (p.y - (c0 + 0.012)) / (-0.004 - (c0 + 0.012))))
        w = w * w * (3 - 2 * w)
        return [(H, w), (side + "LowerArm", 1 - w)] if w < 1 else [(H, 1.0)]
    rounded_box(side, (0.0, (c0 + c1) / 2, 0.0015), (0.0315, (c1 - c0) / 2, 0.0235), 0.55, 5, cuff_w, GLOVE)
    # bare forearm from inside the rolled sleeve to inside the glove cuff, and the roll on the sleeve's cut
    sl = SLEEVE[side]; LA = side + "LowerArm"
    def arm_w(p, H=H, LA=LA):
        w = max(0.0, min(1.0, (p.y + 0.030) / 0.028)); w = w * w * (3 - 2 * w)
        return [(LA, 1.0)] if w <= 0 else [(H, w), (LA, 1 - w)]
    # the jacket sleeve carries on from the survivor's cut at full width, tapering to just over the glove cuff
    u0 = sl["cut"] - 0.02
    u1 = -0.018
    fr = []
    for i in range(9):
        w = i / 8
        uu = u0 + (u1 - u0) * w
        e = w ** 1.3
        fr.append((uu, sl["t"] * 0.98 + (0.037 - sl["t"] * 0.98) * e, sl["n"] * 0.98 + (0.030 - sl["n"] * 0.98) * e,
                   sl["ct"] * (1 - e), sl["cn"] + (0.0015 - sl["cn"]) * e))
    tube(side, fr, arm_w, CLOTH, seg=20)
    # a thin seam where it meets the survivor's sleeve, and a hem at the wrist
    torus(side, sl["cut"] + 0.002, sl["t"] * 0.99, sl["n"] * 0.99, sl["ct"], sl["cn"], 0.004, lambda p, LA=LA: [(LA, 1.0)], CLOTH)
    torus(side, u1 + 0.002, 0.037, 0.030, 0.0, 0.0015, 0.0035, arm_w, CLOTH)
    for digit in DIGITS:
        pts, rad = chain_points(side, digit)
        for k in range(3):
            capsule(pts[k], pts[k + 1], rad[k], rad[k + 1], f"{side}Hand{digit}{k + 1}",
                    GLOVE if (k + 1) in GLOVE_SEGS or (digit == "Thumb" and k == 0) else SKIN,
                    seg=12 if k < 2 else 10, tip=1.25 if k == 2 else 1.0)

me = bpy.data.meshes.new("FP_Hands")
inv = AW.inverted()
me.from_pydata([inv @ p for p in VERTS], [], FACES)
me.update()
# every part is closed: point all faces outward. The part builders wind faces for one hand's frame, and the
# other hand's frame is mirrored, so its sleeve tube and hems faced inward; Unity culls those faces and the
# black glove cuff showed through the sleeve. Face order is kept, so FMAT below still lines up.
_bm = bmesh.new(); _bm.from_mesh(me)
bmesh.ops.recalc_face_normals(_bm, faces=_bm.faces[:])
_bm.to_mesh(me); _bm.free(); me.update()
hands = bpy.data.objects.new("FP_Hands", me)
bpy.context.scene.collection.objects.link(hands)
hands.matrix_world = AW.copy()
for nm in ("Sleeve_Placeholder", "Glove", "Skin", "Cloth", "Trim"):
    m = bpy.data.materials.get("FP_" + nm) or bpy.data.materials.new("FP_" + nm)
    hands.data.materials.append(m)
bpy.data.materials["FP_Glove"].diffuse_color = (0.34, 0.27, 0.18, 1)
bpy.data.materials["FP_Cloth"].diffuse_color = (0.24, 0.25, 0.20, 1)
bpy.data.materials["FP_Trim"].diffuse_color = (0.10, 0.095, 0.085, 1)
bpy.data.materials["FP_Skin"].diffuse_color = (0.66, 0.47, 0.37, 1)
for poly, mi in zip(me.polygons, FMAT):
    poly.material_index = mi; poly.use_smooth = True
groups = {}
for i, ws in enumerate(VW):
    for gname, w in ws:
        if w <= 0: continue
        vg = groups.get(gname) or hands.vertex_groups.new(name=gname); groups[gname] = vg
        vg.add([i], w, "REPLACE")
# join into FP_Arms: its own material stays slot 0 (the sleeve), then glove and skin
arm_mat = arms.data.materials[0] if len(arms.data.materials) else None
hands.data.materials[0] = arm_mat
for o in bpy.context.view_layer.objects: o.select_set(o in (arms, hands))
bpy.context.view_layer.objects.active = arms
with bpy.context.temp_override(object=arms, active_object=arms, selected_objects=[arms, hands], selected_editable_objects=[arms, hands]):
    bpy.ops.object.join()
log(f"hands: {len(VERTS)} vertices; FP_Arms now {len(arms.data.vertices)} vertices, materials {[m.name if m else None for m in arms.data.materials]}")

# ---------------------------------------------------------------- 4. grip solver
exec(open(os.path.join(PROJECT, "Blender", "fp_grip_solver.py"), encoding="utf-8").read(), globals())
