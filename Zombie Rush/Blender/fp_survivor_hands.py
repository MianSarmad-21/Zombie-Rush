"""The survivor's own hands, given five working fingers for first person.

exec() this from fp_viewmodel_hands.py after the arms are lengthened, with `rig` (FP_Rig), `arms` (FP_Arms),
`log`, `upd`, `ctx`, `PROJECT` in globals. The survivor's hand is a thumb plus one block for the four
fingers (the mesh is fused up to the fingertips; the rig has one Index chain bending the whole block).
This script:
  1. cuts the finger block into four fingers along three planes across it (the cuts run from the
     fingertips back to the knuckles, where the fingers stay joined to the palm), closes each cut side
     with a cap that borrows the texture next to it, and narrows each finger a little so gaps show;
  2. replaces the Index chain with Index, Middle, Ring and Pinky chains (3 bones each) laid along each
     finger's own centre line, re-weights each finger to its chain, and turns the thumb bones to the
     same convention (bone Z = the side the digit folds toward, +X turns fold it);
  3. straightens the fingers (the model's are half curled) and makes that the rest pose, so a fold of
     0 is a straight finger;
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
GAP_SHARE = 0.16          # each finger narrows by this share of its width (each side) away from the knuckles
SEG_SHARE = (0.46, 0.30, 0.24)   # phalanx lengths as shares of the finger

AW = arms.matrix_world
assert all(abs(a - b) < 1e-6 for ra, rb in zip(AW, Matrix.Identity(4)) for a, b in zip(ra, rb)), "FP_Arms must sit at identity"
B = rig.data.bones

def smooth(x):
    x = max(0.0, min(1.0, x)); return x * x * (3 - 2 * x)

def poly_param(pts, p):
    """Closest point on a polyline: (distance along it, distance to it)."""
    best, acc = None, 0.0
    for a, b in zip(pts, pts[1:]):
        ab = b - a; L = ab.length
        w = 0.0 if L < 1e-9 else max(0.0, min(1.0, (p - a).dot(ab) / (L * L)))
        d = (p - (a + ab * w)).length
        if best is None or d < best[1]: best = (acc + L * w, d)
        acc += L
    return best

# ---------------------------------------------------------------- 0. what the model has
gidx = {vg.name: vg.index for vg in arms.vertex_groups}
def wsum(v, names):
    ids = {gidx[n] for n in names if n in gidx}
    return sum(g.weight for g in v.groups if g.group in ids)

OLD = {}
for side in SIDES:
    idx = [B[f"{side}HandIndex{k}"].head_local.copy() for k in (1, 2, 3)] + [B[f"{side}HandIndex3"].tail_local.copy()]
    thb = [B[f"{side}HandThumb{k}"].head_local.copy() for k in (1, 2, 3)] + [B[f"{side}HandThumb3"].tail_local.copy()]
    W = B[side + "Hand"].head_local.copy()
    fin = [f"{side}HandIndex{k}" for k in (1, 2, 3)]; thn = [f"{side}HandThumb{k}" for k in (1, 2, 3)]
    block = [v.index for v in arms.data.vertices if wsum(v, fin) >= 0.5 and wsum(v, fin) >= wsum(v, thn)]
    thumb_c = sum((arms.data.vertices[i].co for i in range(len(arms.data.vertices)) if wsum(arms.data.vertices[i], thn) >= 0.5), Vector())
    nth = sum(1 for v in arms.data.vertices if wsum(v, thn) >= 0.5)
    thumb_c = thumb_c / max(1, nth)
    u = (idx[0] - W).normalized()
    t = thumb_c - W; t = (t - u * t.dot(u)).normalized()
    n = t.cross(u) if side == "Right" else u.cross(t)
    curl = (idx[3] - idx[0]).dot(n)
    OLD[side] = dict(idx=idx, thumb=thb, W=W, u=u, t=t, n=n, block=set(block))
    log(f"{side}: finger block {len(block)} vertices, thumb {nth}; fingertips sit {curl * 100:.1f} cm toward the palm (half curled)")

# ---------------------------------------------------------------- 1. cut the block into four fingers
bm = bmesh.new(); bm.from_mesh(arms.data)
bm.verts.ensure_lookup_table()
uvl = bm.loops.layers.uv.active
mark = bm.verts.layers.int.new("fp_finger")      # 1 right block, 2 left block
for k, side in enumerate(SIDES):
    for i in OLD[side]["block"]: bm.verts[i][mark] = k + 1

CUTS = {}
for k, side in enumerate(SIDES):
    o = OLD[side]; sid = k + 1
    S = np.array([(v.co - o["W"]).dot(o["t"]) for v in bm.verts if v[mark] == sid])
    lo, hi = np.percentile(S, 2), np.percentile(S, 98)
    edges = [lo + (hi - lo) * j / 4 for j in (1, 2, 3)]
    # keep each cut clear of vertices (a plane through a vertex makes slivers)
    fixed = []
    for e in edges:
        for _ in range(20):
            if np.min(np.abs(S - e)) > 0.0004: break
            e += 0.0003
        fixed.append(e)
    CUTS[side] = dict(edges=fixed, lo=lo, hi=hi)
    for e in fixed:
        faces = [f for f in bm.faces if all(v[mark] == sid for v in f.verts)]
        geom = faces + list({ed for f in faces for ed in f.edges}) + list({v for f in faces for v in f.verts})
        res = bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-6, plane_co=o["W"] + o["t"] * e, plane_no=o["t"])
        for g in res["geom_cut"]:
            if isinstance(g, bmesh.types.BMVert): g[mark] = sid
        cut_edges = [g for g in res["geom_cut"] if isinstance(g, bmesh.types.BMEdge)
                     and len(g.link_faces) == 2 and all(all(v[mark] == sid for v in f.verts) for f in g.link_faces)]
        bmesh.ops.split_edges(bm, edges=cut_edges)
    log(f"{side}: cut at {[round(x * 100, 2) for x in fixed]} cm across the block")

bm.verts.ensure_lookup_table(); bm.faces.ensure_lookup_table()

def face_s(f, o): return (f.calc_center_median() - o["W"]).dot(o["t"])
def strip_of(sv, side):
    """Which finger (0 Index .. 3 Pinky) a value across the block falls in."""
    e = CUTS[side]["edges"]
    j = sum(1 for x in e if sv > x)          # 0 = lowest (little finger side) .. 3 = thumb side
    return 3 - j

STRIP = {}       # vert -> (side, strip)
for k, side in enumerate(SIDES):
    o = OLD[side]; sid = k + 1
    for v in bm.verts:
        if v[mark] != sid: continue
        fs = [f for f in v.link_faces if all(w[mark] == sid for w in f.verts)]
        sv = sum(face_s(f, o) for f in fs) / len(fs) if fs else (v.co - o["W"]).dot(o["t"])
        STRIP[v] = (side, strip_of(sv, side))

# narrow each finger toward its own middle, more toward the tip (the base stays joined)
for side in SIDES:
    o = OLD[side]; e = [CUTS[side]["lo"]] + CUTS[side]["edges"] + [CUTS[side]["hi"]]
    mids = {3 - j: (e[j] + e[j + 1]) / 2 for j in range(4)}
    for v, (sd, j) in STRIP.items():
        if sd != side: continue
        d, _ = poly_param(o["idx"], v.co)
        f = smooth((d - 0.004) / 0.018)
        sv = (v.co - o["W"]).dot(o["t"])
        v.co += o["t"] * ((mids[j] - sv) * GAP_SHARE * 2 * f)

# cap the cut sides: each finger's open edge chain, closed across its two ends
def uv_of(v):
    for l in v.link_loops:
        if l.face.select is False and l.face.is_valid:
            return l[uvl].uv.copy()
    return None
for f in bm.faces: f.select = False
def face_strip(f):
    got = [STRIP[v] for v in f.verts if v in STRIP]
    return max(set(got), key=got.count) if got else None
caps = 0
for side in SIDES:
    for j in range(4):
        chain = [ed for ed in bm.edges if ed.is_boundary and ed.link_faces and face_strip(ed.link_faces[0]) == (side, j)]
        left = set(chain)
        while left:                                    # one run per cut side of this finger
            run = {left.pop()}; grow = True
            while grow:
                grow = False
                rv = {v for r in run for v in r.verts}
                for ed in list(left):
                    if ed.verts[0] in rv or ed.verts[1] in rv:
                        run.add(ed); left.discard(ed); grow = True
            vs = {v for ed in run for v in ed.verts}
            ends = [v for v in vs if sum(1 for ed in run if v in ed.verts) == 1]
            extra = []
            if len(ends) == 2 and bm.edges.get(ends) is None:
                extra.append(bm.edges.new(ends))
            if len(run) + len(extra) < 3: continue
            res = bmesh.ops.triangle_fill(bm, use_beauty=True, use_dissolve=False, edges=list(run) + extra)
            for g in res["geom"]:
                if isinstance(g, bmesh.types.BMFace):
                    g.select = True; g.material_index = 0; caps += 1
bm.normal_update()
capf = [f for f in bm.faces if f.select]
if capf: bmesh.ops.recalc_face_normals(bm, faces=list({f for v in {v for f in capf for v in f.verts} for f in v.link_faces}))
for f in capf:
    f.smooth = True
    for l in f.loops:
        u_ = uv_of(l.vert)
        if u_ is not None: l[uvl].uv = u_
for f in bm.faces: f.select = False
bm.verts.index_update(); bm.verts.ensure_lookup_table()
strip_by_index = {}
for v, s in STRIP.items(): strip_by_index[v.index] = s
bm.verts.layers.int.remove(mark)
bm.to_mesh(arms.data); bm.free(); arms.data.update()
log(f"cut sides capped with {caps} faces; FP_Arms now {len(arms.data.vertices)} vertices")

# ---------------------------------------------------------------- 2. finger chains along each finger
CENTRE = {}       # (side, digit) -> centre line points (finger base .. tip), measured on the mesh
RADII = {}
for side in SIDES:
    o = OLD[side]
    for j, digit in enumerate(FINGERS4):
        pts = [arms.data.vertices[i].co.copy() for i, s in strip_by_index.items() if s == (side, j)]
        ds = [poly_param(o["idx"], p)[0] for p in pts]
        L = max(ds)
        bins = 10
        line = []
        for b_ in range(bins):
            a0, a1 = L * b_ / bins, L * (b_ + 1) / bins
            sel = [p for p, d in zip(pts, ds) if a0 <= d <= a1]
            if sel: line.append(sum(sel, Vector()) / len(sel))
        # extend the line to the real tip (furthest vertex along the last direction) and back to the knuckle
        tipdir = (line[-1] - line[-2]).normalized()
        tip = max(pts, key=lambda p: (p - line[-1]).dot(tipdir))
        line[-1] = line[-1] + tipdir * max(0.0, (tip - line[-1]).dot(tipdir) - 0.004)
        CENTRE[(side, digit)] = line
        rr = [poly_param(line, p)[1] for p in pts]
        RADII[(side, digit)] = float(np.percentile(rr, 60))

def resample(line, shares):
    """Points at cumulative shares of the polyline's length."""
    seg = [(a - b).length for a, b in zip(line[1:], line)]
    total = sum(seg); out = [line[0].copy()]; acc = 0.0
    targets = np.cumsum(shares) * total
    for tg in targets:
        run = 0.0
        for a, b, L in zip(line, line[1:], seg):
            if run + L >= tg - 1e-9:
                out.append(a + (b - a) * ((tg - run) / max(L, 1e-9))); break
            run += L
        else:
            out.append(line[-1].copy())
    return out

bpy.context.view_layer.objects.active = rig
for ob in bpy.context.view_layer.objects: ob.select_set(ob is rig)
with ctx(rig): bpy.ops.object.mode_set(mode="EDIT")
eb = rig.data.edit_bones
for side in SIDES:
    for k in (1, 2, 3):
        eb.remove(eb[f"{side}HandIndex{k}"])
    n = OLD[side]["n"]; t = OLD[side]["t"]
    for digit in FINGERS4:
        J = resample(CENTRE[(side, digit)], SEG_SHARE)
        parent = eb[side + "Hand"]
        for k in range(3):
            bn = eb.new(f"{side}Hand{digit}{k + 1}")
            bn.head, bn.tail = J[k], J[k + 1]
            y = (J[k + 1] - J[k]).normalized()
            z = n - y * n.dot(y)
            if z.length < 1e-4: z = (J[k + 1] - J[k]).cross(t)
            bn.align_roll(z.normalized()); bn.parent = parent; bn.use_connect = k > 0; bn.use_deform = True
            parent = bn
    for k in (1, 2, 3):
        bn = eb[f"{side}HandThumb{k}"]
        y = (bn.tail - bn.head).normalized()
        flex = n * 0.80 - t * 0.60
        bn.align_roll((flex - y * flex.dot(y)).normalized())
bpy.ops.object.mode_set(mode="OBJECT")
for pb in rig.pose.bones: pb.rotation_mode = "QUATERNION"
upd()

# weights: each finger vertex to its own chain (blended over a few mm at the joints and at the base)
for side in SIDES:
    old_ids = {arms.vertex_groups[f"{side}HandIndex{k}"].index for k in (1, 2, 3) if f"{side}HandIndex{k}" in arms.vertex_groups}
    hand_vg = arms.vertex_groups[side + "Hand"]
    chains = {}
    for digit in FINGERS4:
        names = [f"{side}Hand{digit}{k}" for k in (1, 2, 3)]
        for nm in names:
            if nm not in arms.vertex_groups: arms.vertex_groups.new(name=nm)
        J = [B[names[0]].head_local.copy()] + [B[nm].tail_local.copy() for nm in names]
        chains[digit] = (names, J)
    o = OLD[side]
    e = CUTS[side]["edges"]
    for v in arms.data.vertices:
        w_old = sum(g.weight for g in v.groups if g.group in old_ids)
        if w_old <= 0: continue
        s = strip_by_index.get(v.index)
        if s is not None and s[0] == side:
            digit = FINGERS4[s[1]]
        else:
            digit = FINGERS4[strip_of((v.co - o["W"]).dot(o["t"]), side)]
        names, J = chains[digit]
        d, _ = poly_param(J, v.co)
        bounds = [0.0] + list(np.cumsum([(J[i + 1] - J[i]).length for i in range(3)]))
        ws = [0.0, 0.0, 0.0]; blend = 0.004
        for i in range(3):
            a, b = bounds[i], bounds[i + 1]
            lo_ = smooth((d - (a - blend)) / (2 * blend)) if i > 0 else 1.0
            hi_ = 1.0 - smooth((d - (b - blend)) / (2 * blend)) if i < 2 else 1.0
            ws[i] = max(0.0, lo_ * hi_)
        tot = sum(ws) or 1.0
        base = smooth((d + 0.002) / 0.008)                 # right at the knuckle, part of it stays with the hand
        for i, nm in enumerate(names):
            arms.vertex_groups[nm].add([v.index], w_old * base * ws[i] / tot, "ADD")
        if base < 1:
            hand_vg.add([v.index], w_old * (1 - base), "ADD")
log("finger chains built; " + ", ".join(f"{s[0][0]}{s[1][:2]} r{RADII[s] * 1000:.0f}mm" for s in RADII))

# ---------------------------------------------------------------- 3. straighten the fingers (new rest)
m_ = next((m for m in arms.modifiers if m.type == "ARMATURE"), None) or arms.modifiers.new("Armature", "ARMATURE")
m_.object = rig
upd()
for side in SIDES:
    o = OLD[side]
    for digit in FINGERS4:
        names = [f"{side}Hand{digit}{k}" for k in (1, 2, 3)]
        base_dir = (B[names[0]].tail_local - B[names[0]].head_local).normalized()
        sp = base_dir - o["n"] * base_dir.dot(o["n"])     # keep its spread, lose its fold
        target = (sp.normalized() * 0.6 + o["u"] * 0.4).normalized()
        target = (target - o["n"] * target.dot(o["n"])).normalized()
        for nm in names:
            pb = rig.pose.bones[nm]
            upd()
            M = rig.matrix_world.inverted() @ (rig.matrix_world @ pb.matrix)
            y = M.to_3x3().col[1].normalized()
            q = y.rotation_difference(target)
            R = q.to_matrix() @ M.to_3x3()
            Mn = R.to_4x4(); Mn.translation = M.translation
            pb.matrix = Mn
    upd()
with ctx(arms):
    bpy.ops.object.modifier_apply(modifier=m_.name)
bpy.context.view_layer.objects.active = rig
for ob in bpy.context.view_layer.objects: ob.select_set(ob is rig)
with ctx(rig): bpy.ops.object.mode_set(mode="POSE")
bpy.ops.pose.select_all(action="SELECT")
bpy.ops.pose.armature_apply(selected=False)
bpy.ops.object.mode_set(mode="OBJECT")
for pb in rig.pose.bones:
    pb.rotation_quaternion = Quaternion(); pb.location = (0, 0, 0); pb.scale = (1, 1, 1)
# old Index groups are now empty or unused; drop the ones with no bone
for vg in list(arms.vertex_groups):
    if vg.name not in rig.data.bones and "Hand" in vg.name:
        arms.vertex_groups.remove(vg)
upd()
log("fingers straightened and made the rest pose")

# ---------------------------------------------------------------- 4. what the grip solver needs
B = rig.data.bones
HAND_INFO = {}
for side in SIDES:
    W = B[side + "Hand"].head_local.copy()
    u = sum(((B[f"{side}Hand{d}1"].tail_local - B[f"{side}Hand{d}1"].head_local).normalized() for d in FINGERS4), Vector()).normalized()
    t = OLD[side]["t"]; t = (t - u * t.dot(u)).normalized()
    n = t.cross(u) if side == "Right" else u.cross(t)
    HAND_INFO[side] = dict(R=Matrix((t, u, n)).transposed(), W=W)
_RT = {s: HAND_INFO[s]["R"].transposed() for s in SIDES}

def chain_points(side, digit):
    names = [f"{side}Hand{digit}{k}" for k in (1, 2, 3)]
    pts = [B[names[0]].head_local.copy()] + [B[nm].tail_local.copy() for nm in names]
    if digit == "Thumb":
        r = (0.0125, 0.0110, 0.0100, 0.0090)
    else:
        r0 = RADII[(side, digit)]
        r = (r0 * 1.05, r0, r0 * 0.95, r0 * 0.88)
    return pts, r

# palm surface: how far the palm's skin sits from the wrist-knuckle plane, on the palm side
pn = []
for side in SIDES:
    R, W = HAND_INFO[side]["R"], HAND_INFO[side]["W"]
    hid = arms.vertex_groups[side + "Hand"].index
    for v in arms.data.vertices:
        if any(g.group == hid and g.weight > 0.7 for g in v.groups):
            p = _RT[side] @ (v.co - W)
            if 0.03 < p.y < 0.08 and abs(p.x) < 0.03: pn.append(p.z)
PALM = dict(n_palm=float(np.percentile(pn, 92)) if pn else 0.015)
CMC = tuple(_RT["Right"] @ (B["RightHandThumb1"].head_local - HAND_INFO["Right"]["W"]))
log(f"palm surface {PALM['n_palm'] * 1000:.1f} mm out; thumb base (t,u,n) {tuple(round(x, 3) for x in CMC)}")

exec(open(os.path.join(PROJECT, "Blender", "fp_grip_solver.py"), encoding="utf-8").read(), globals())
