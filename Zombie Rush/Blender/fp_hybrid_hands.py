"""First-person hands: the survivor's own arms, palms and thumbs, with full-length fingers.

exec() this from fp_viewmodel_hands.py (--hands hybrid) after the arms are lengthened, with `rig` (FP_Rig),
`arms` (FP_Arms), `log`, `upd`, `ctx`, `PROJECT` in globals.

The survivor's four fingers are one short block folded into the palm (1.5-2 cm of finger), so they can
never wrap a grip. This script:
  1. deletes that block and closes the palm where it was (the cap borrows the palm's texture);
  2. grows four fingers from the palm's knuckle edge: three rounded segments each, sized to the palm,
     textured from the old block (each new finger vertex takes the texture spot of the old finger's
     vertex at the same place along it, palm side or back), all in the survivor's own material;
  3. gives each finger three bones (bone Z = the side it folds toward, +X turns fold it), each segment
     riding its bone; turns the thumb's bones to the same convention (its mesh stays the survivor's);
  4. gives the grip solver (fp_grip_solver.py) the hand frames, joint points and finger radii.
"""
import math
import os
import bmesh
import numpy as np
from mathutils import Vector, Matrix, Quaternion

SIDES = ("Right", "Left")
DIGITS = ("Thumb", "Index", "Middle", "Ring", "Pinky")
FINGERS4 = ("Index", "Middle", "Ring", "Pinky")
CLEAR = 0.0012
SEG = {"Index": (0.042, 0.025, 0.022), "Middle": (0.046, 0.029, 0.023),
       "Ring": (0.043, 0.027, 0.022), "Pinky": (0.034, 0.021, 0.020)}
THICK = {"Index": 1.0, "Middle": 1.04, "Ring": 0.97, "Pinky": 0.86}
TAPER = (1.0, 0.92, 0.85, 0.77)
SPLAY = {"Index": 5.0, "Middle": 0.0, "Ring": -5.0, "Pinky": -11.0}
LEN_BACK = {"Index": 0.0, "Middle": 0.004, "Ring": 0.0, "Pinky": -0.008}   # knuckle line arches: middle furthest out

AW = arms.matrix_world
assert all(abs(a - b) < 1e-6 for ra, rb in zip(AW, Matrix.Identity(4)) for a, b in zip(ra, rb)), "FP_Arms must sit at identity"
B = rig.data.bones

def poly_param(pts, p):
    best, acc = None, 0.0
    for a, b in zip(pts, pts[1:]):
        ab = b - a; L = ab.length
        w = 0.0 if L < 1e-9 else max(0.0, min(1.0, (p - a).dot(ab) / (L * L)))
        q = a + ab * w; d = (p - q).length
        if best is None or d < best[1]: best = (acc + L * w, d, q)
        acc += L
    return best

gidx = {vg.name: vg.index for vg in arms.vertex_groups}
def wsum(v, names):
    ids = {gidx[n] for n in names if n in gidx}
    return sum(g.weight for g in v.groups if g.group in ids)

# ---------------------------------------------------------------- 1. remove the stub fingers, close the palm
bm = bmesh.new(); bm.from_mesh(arms.data)
bm.verts.ensure_lookup_table()
uvl = bm.loops.layers.uv.active
VUV = {}
for v in bm.verts:
    for l in v.link_loops:
        VUV[v.index] = l[uvl].uv.copy(); break

OLD = {}
for side in SIDES:
    fin = [f"{side}HandIndex{k}" for k in (1, 2, 3)]; thn = [f"{side}HandThumb{k}" for k in (1, 2, 3)]
    W = B[side + "Hand"].head_local.copy()
    idx = [B[f"{side}HandIndex{k}"].head_local.copy() for k in (1, 2, 3)] + [B[f"{side}HandIndex3"].tail_local.copy()]
    block = [v.index for v in arms.data.vertices if wsum(v, fin) >= 0.5 and wsum(v, fin) >= wsum(v, thn)]
    th = [arms.data.vertices[i].co for i in range(len(arms.data.vertices)) if wsum(arms.data.vertices[i], thn) >= 0.5]
    thumb_c = sum(th, Vector()) / max(1, len(th))
    u0 = (idx[0] - W).normalized()
    t0 = thumb_c - W; t0 = (t0 - u0 * t0.dot(u0)).normalized()
    n0 = t0.cross(u0) if side == "Right" else u0.cross(t0)
    # the old block's texture spots: per vertex, which quarter across, how far along, palm side or back
    S = np.array([(arms.data.vertices[i].co - W).dot(t0) for i in block])
    lo, hi = np.percentile(S, 2), np.percentile(S, 98)
    Lmax = max(poly_param(idx, arms.data.vertices[i].co)[0] for i in block) or 1.0
    spots = []
    for i, sv in zip(block, S):
        p = arms.data.vertices[i].co
        d, _, q = poly_param(idx, p)
        j = 3 - min(3, max(0, int((sv - lo) / max(hi - lo, 1e-6) * 4)))     # 0 = index (thumb side) .. 3 = little finger
        spots.append((j, d / Lmax, 1 if (p - q).dot(n0) > 0 else -1, VUV.get(i)))
    OLD[side] = dict(W=W, idx=idx, block=set(block), thumb_c=thumb_c, t=t0, u=u0, n=n0, spots=spots)

gone = [bm.verts[i] for s in SIDES for i in OLD[s]["block"]]
bmesh.ops.delete(bm, geom=gone, context="VERTS")
bm.verts.ensure_lookup_table(); bm.verts.index_update()
KNUCKLE = {}
capf = 0
for side in SIDES:
    o = OLD[side]
    allb = [e for e in bm.edges if e.is_boundary]
    log(f"{side}: boundary edges near the hand: " + str(sorted(round((e.verts[0].co - o["W"]).length * 100) for e in allb if (e.verts[0].co - o["W"]).length < 0.25)))
    edges = [e for e in allb if all((v.co - o["W"]).length < 0.16 for v in e.verts)]
    pts = list({v for e in edges for v in e.verts})
    KNUCKLE[side] = [v.co.copy() for v in pts]
    res = bmesh.ops.triangle_fill(bm, use_beauty=True, use_dissolve=False, edges=edges)
    res["faces"] = [g for g in res["geom"] if isinstance(g, bmesh.types.BMFace)]
    for f in res["faces"]:
        f.smooth = True; capf += 1
        for l in f.loops:
            for l2 in l.vert.link_loops:
                if l2.face is not f and l2.face not in res["faces"]:
                    l[uvl].uv = l2[uvl].uv.copy(); break
    log(f"{side}: removed {len(o['block'])} stub-finger vertices; palm edge {len(pts)} vertices")
bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
bm.to_mesh(arms.data); bm.free(); arms.data.update()
log(f"palm openings closed with {capf} faces")

# ---------------------------------------------------------------- 2. hand frames and the new fingers
HAND_INFO, FING = {}, {}
for side in SIDES:
    o = OLD[side]; W = o["W"]
    kc = sum(KNUCKLE[side], Vector()) / len(KNUCKLE[side])
    u = (kc - W).normalized()
    t = o["thumb_c"] - W; t = (t - u * t.dot(u)).normalized()
    n = t.cross(u) if side == "Right" else u.cross(t)
    R = Matrix((t, u, n)).transposed()
    HAND_INFO[side] = dict(R=R, W=W.copy())
    loc = [R.transposed() @ (p - W) for p in KNUCKLE[side]]
    tmin, tmax = min(p.x for p in loc), max(p.x for p in loc)
    kc_l = R.transposed() @ (kc - W)
    width = tmax - tmin
    palm_len = kc_l.y
    k = max(0.9, min(1.15, palm_len / 0.098))
    r_base = max(0.0078, min(0.0105, 0.47 * width / 4))
    for j, digit in enumerate(FINGERS4):
        tj = tmax - (j + 0.5) * width / 4
        base = Vector((tj, kc_l.y - 0.010 + LEN_BACK[digit], kc_l.z))
        a = math.radians(SPLAY[digit])
        d = Vector((math.sin(a), math.cos(a), 0.0))
        pts = [base]
        for L in SEG[digit]: pts.append(pts[-1] + d * (L * k))
        r0 = r_base * THICK[digit]
        FING[(side, digit)] = dict(pts=[W + R @ p for p in pts], rad=[r0 * x for x in TAPER])
    log(f"{side}: palm {palm_len * 100:.1f} cm long, knuckles {width * 100:.1f} cm wide -> fingers x{k:.2f}, radius {r_base * 1000:.1f} mm")

def capsule(A, Bp, ra, rb, seg=12, hr=4, tip=1.0):
    y = Bp - A; L = y.length; y.normalize()
    x = y.orthogonal().normalized(); z = y.cross(x)
    rings = []
    for i in range(1, hr + 1):
        ph = -math.pi / 2 + (math.pi / 2) * i / hr
        rings.append((A + y * (ra * math.sin(ph)), ra * math.cos(ph)))
    for i in range(1, 3):
        w = i / 3; rings.append((A + y * (L * w), ra + (rb - ra) * w))
    for i in range(0, hr):
        ph = (math.pi / 2) * i / hr; rings.append((Bp + y * (rb * math.sin(ph) * tip), rb * math.cos(ph)))
    vs = [A - y * ra]
    for c, r in rings:
        for kk in range(seg):
            a = 2 * math.pi * kk / seg; vs.append(c + (x * math.cos(a) + z * math.sin(a)) * r)
    vs.append(Bp + y * rb * tip)
    fs = [(0, 1 + (kk + 1) % seg, 1 + kk) for kk in range(seg)]
    for ri in range(len(rings) - 1):
        a0, a1 = 1 + ri * seg, 1 + (ri + 1) * seg
        for kk in range(seg):
            k2 = (kk + 1) % seg; fs.append((a0 + kk, a0 + k2, a1 + k2, a1 + kk))
    last = 1 + (len(rings) - 1) * seg; top = len(vs) - 1
    fs += [(last + kk, last + (kk + 1) % seg, top) for kk in range(seg)]
    return vs, fs

# one skin spot of the texture for every finger: the old block's texel nearest its median colour
SKIN_UV = None
alb = os.path.join(PROJECT, "Assets", "Models", "2026-11-08_WastelandSurvivor_Final", "WastelandSurvivor_Albedo.png")
img = bpy.data.images.load(alb, check_existing=True) if os.path.exists(alb) else None
if img is not None and img.size[0] > 0:
    w_, h_ = img.size; px = np.array(img.pixels[:], np.float32).reshape(h_, w_, img.channels)
    cols = []
    for side in SIDES:
        for s_ in OLD[side]["spots"]:
            if s_[3] is None: continue
            x = min(w_ - 1, max(0, int(s_[3].x % 1.0 * w_))); y = min(h_ - 1, max(0, int(s_[3].y % 1.0 * h_)))
            cols.append((px[y, x, :3], s_[3]))
    med = np.median(np.array([c for c, _ in cols]), axis=0)
    # skin is the bright warm part: take the median of the brighter half
    bright = [c for c in cols if c[0].sum() >= np.median([c_[0].sum() for c_ in cols])]
    med = np.median(np.array([c for c, _ in bright]), axis=0)
    SKIN_UV = min(cols, key=lambda c: float(((c[0] - med) ** 2).sum()))[1].copy()
    log(f"finger skin colour {tuple(round(float(x), 2) for x in med)} at uv {tuple(round(x, 3) for x in SKIN_UV)}")

bm = bmesh.new()
uvn = bm.loops.layers.uv.new("UVMap")
VGROUP = []
for side in SIDES:
    o = OLD[side]; n = HAND_INFO[side]["R"].col[2].to_3d()
    for j, digit in enumerate(FINGERS4):
        F = FING[(side, digit)]; P = F["pts"]; Ltot = sum((P[i + 1] - P[i]).length for i in range(3))
        cands = [s for s in o["spots"] if s[0] == j and s[3] is not None] or [s for s in o["spots"] if s[3] is not None]
        acc = 0.0
        for kk in range(3):
            vs, fs = capsule(P[kk], P[kk + 1], F["rad"][kk], F["rad"][kk + 1], seg=12 if kk < 2 else 10, tip=1.25 if kk == 2 else 1.0)
            Lk = (P[kk + 1] - P[kk]).length
            bv = []
            for p in vs:
                share = (acc + max(0.0, min(Lk, (p - P[kk]).dot((P[kk + 1] - P[kk]).normalized())))) / Ltot
                sgn = 1 if (p - P[kk]).dot(n) > 0 else -1
                best = min(cands, key=lambda s: abs(s[1] - share) + (0 if s[2] == sgn else 0.5))
                v = bm.verts.new(p); bv.append((v, SKIN_UV if SKIN_UV is not None else best[3]))
                VGROUP.append(f"{side}Hand{digit}{kk + 1}")
            for f in fs:
                try:
                    face = bm.faces.new([bv[i][0] for i in f])
                except ValueError:
                    continue
                face.smooth = True
                for l in face.loops:
                    l[uvn].uv = next(b for a, b in bv if a is l.vert)
            acc += Lk
me = bpy.data.meshes.new("FP_Fingers"); bm.to_mesh(me); bm.free()
fingers = bpy.data.objects.new("FP_Fingers", me); bpy.context.scene.collection.objects.link(fingers)
for name in sorted(set(VGROUP)):
    fingers.vertex_groups.new(name=name)
for i, name in enumerate(VGROUP):
    fingers.vertex_groups[name].add([i], 1.0, "REPLACE")
if arms.data.materials: fingers.data.materials.append(arms.data.materials[0])
# the arms' UV layer name, so the join merges them
if arms.data.uv_layers: me.uv_layers[0].name = arms.data.uv_layers.active.name
for ob in bpy.context.view_layer.objects: ob.select_set(ob in (arms, fingers))
bpy.context.view_layer.objects.active = arms
with bpy.context.temp_override(object=arms, active_object=arms, selected_objects=[arms, fingers], selected_editable_objects=[arms, fingers]):
    bpy.ops.object.join()
log(f"fingers: {len(VGROUP)} vertices; FP_Arms now {len(arms.data.vertices)} vertices")

# ---------------------------------------------------------------- 3. bones
bpy.context.view_layer.objects.active = rig
for ob in bpy.context.view_layer.objects: ob.select_set(ob is rig)
with ctx(rig): bpy.ops.object.mode_set(mode="EDIT")
eb = rig.data.edit_bones
for side in SIDES:
    for kk in (1, 2, 3):
        eb.remove(eb[f"{side}HandIndex{kk}"])
    R = HAND_INFO[side]["R"]; n = R.col[2].to_3d(); t = R.col[0].to_3d()
    for digit in FINGERS4:
        P = FING[(side, digit)]["pts"]; parent = eb[side + "Hand"]
        for kk in range(3):
            bn = eb.new(f"{side}Hand{digit}{kk + 1}")
            bn.head, bn.tail = P[kk], P[kk + 1]
            y = (P[kk + 1] - P[kk]).normalized()
            bn.align_roll((n - y * n.dot(y)).normalized())
            bn.parent = parent; bn.use_connect = kk > 0; bn.use_deform = True
            parent = bn
    for kk in (1, 2, 3):
        bn = eb[f"{side}HandThumb{kk}"]
        y = (bn.tail - bn.head).normalized()
        flex = n * 0.80 - t * 0.60
        bn.align_roll((flex - y * flex.dot(y)).normalized())
bpy.ops.object.mode_set(mode="OBJECT")
for pb in rig.pose.bones: pb.rotation_mode = "QUATERNION"
for vg in list(arms.vertex_groups):
    if vg.name not in rig.data.bones and "Hand" in vg.name: arms.vertex_groups.remove(vg)
upd()
B = rig.data.bones

# ---------------------------------------------------------------- 4. what the grip solver needs
def chain_points(side, digit):
    if digit == "Thumb":
        names = [f"{side}HandThumb{kk}" for kk in (1, 2, 3)]
        pts = [B[names[0]].head_local.copy()] + [B[nm].tail_local.copy() for nm in names]
        return pts, (0.0125, 0.0110, 0.0100, 0.0090)
    F = FING[(side, digit)]
    return [p.copy() for p in F["pts"]], tuple(F["rad"])

pn = []
for side in SIDES:
    R, W = HAND_INFO[side]["R"], HAND_INFO[side]["W"]
    hid = arms.vertex_groups[side + "Hand"].index
    for v in arms.data.vertices:
        if any(g.group == hid and g.weight > 0.7 for g in v.groups):
            p = R.transposed() @ (v.co - W)
            if 0.03 < p.y < 0.08 and abs(p.x) < 0.03 and p.z > 0: pn.append(p.z)
PALM = dict(n_palm=float(np.percentile(pn, 85)) if pn else 0.015)
CMC = tuple(HAND_INFO["Right"]["R"].transposed() @ (B["RightHandThumb1"].head_local - HAND_INFO["Right"]["W"]))
log(f"palm surface {PALM['n_palm'] * 1000:.1f} mm out; thumb base (t,u,n) {tuple(round(x, 3) for x in CMC)}")

exec(open(os.path.join(PROJECT, "Blender", "fp_grip_solver.py"), encoding="utf-8").read(), globals())
