"""Weapon grips for the Shadow Operative, built from scratch (2026-11-14).

exec() in Blender with the Operative imported as Op_Rig / Op_Mesh and the gun rigs present (<Gun>_Rig,
Gun bone at the grip, muzzle -Y, top +Z). One grip per gun, kept in the gun's own space, so the
third-person body and the first-person arms hold the gun the same way; only the arms differ.

Hand frame (t, u, n): t across the knuckles (little finger -> index), u wrist -> knuckle row, n out of the
palm. Wrist at the origin. Finger bones curl toward the palm about their local +X.
"""
import itertools
import math
import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree

RIG = bpy.data.objects["Op_Rig"]
MESH = bpy.data.objects["Op_Mesh"]
B = RIG.data.bones
SIDES = ("Right", "Left")
DIGITS = ("Thumb", "Index", "Middle", "Ring", "Pinky")
FINGERS = ("Index", "Middle", "Ring", "Pinky")
CLEAR = 0.0015                      # gap kept between skin and gun (m)


def upd():
    bpy.context.view_layer.update()


def pb(n):
    p = RIG.pose.bones[n]
    p.rotation_mode = "QUATERNION"
    return p


def reset_pose():
    for p in RIG.pose.bones:
        p.rotation_mode = "QUATERNION"
        p.rotation_quaternion = Quaternion()
        p.location = (0, 0, 0)
        p.scale = (1, 1, 1)
    upd()


# ------------------------------------------------------------------ the hand at rest
def tun_rest(side):
    W = B[side + "Hand"].head_local
    kn = [B[f"{side}Hand{f}1"].head_local for f in FINGERS]
    u = (sum(kn, Vector()) / 4 - W).normalized()
    t = B[f"{side}HandIndex1"].head_local - B[f"{side}HandPinky1"].head_local
    t = (t - u * t.dot(u)).normalized()
    n = t.cross(u) if side == "Right" else u.cross(t)
    M = Matrix((t, u, n)).transposed().to_4x4()
    M.translation = W
    return M


TUN0 = {s: tun_rest(s) for s in SIDES}
HB2TUN = {s: B[s + "Hand"].matrix_local.inverted() @ TUN0[s] for s in SIDES}


def frame(side, t, u, origin):
    """A hand frame (armature space) from a thumb-side direction t and a knuckle direction u."""
    u = Vector(u).normalized()
    t = Vector(t)
    t = (t - u * t.dot(u)).normalized()
    n = t.cross(u) if side == "Right" else u.cross(t)
    M = Matrix((t, u, n)).transposed().to_4x4()
    M.translation = Vector(origin)
    return M


def hand_matrix(side, F):
    """Hand bone armature-space matrix that puts the hand frame at F."""
    return F @ HB2TUN[side].inverted()


def to_hand(side, p, F=None):
    F = F or TUN0[side]
    return (F.inverted() @ Vector(p))


# skin of the hand at rest (armature space) per bone, for palm contact and finger radii
_P = np.array([tuple(v.co) for v in MESH.data.vertices])
_GI = {vg.name: vg.index for vg in MESH.vertex_groups}
_W = {}


def weights(name):
    if name not in _W:
        w = np.zeros(len(_P))
        gi = _GI.get(name)
        if gi is not None:
            for v in MESH.data.vertices:
                for g in v.groups:
                    if g.group == gi:
                        w[v.index] = g.weight
        _W[name] = w
    return _W[name]


def palm_points(side):
    """Palm-side skin of the hand bone, in hand coords (rest)."""
    ids = np.where(weights(side + "Hand") > 0.55)[0]
    F = TUN0[side]
    Q = [to_hand(side, Vector(_P[i]), F) for i in ids]
    return [q for q in Q if q.z > 0.0]


def seg_radius(name):
    ids = np.where(weights(name) > 0.5)[0]
    b = B[name]
    a, c = np.array(b.head_local), np.array(b.tail_local)
    if len(ids) < 4:
        return 0.011
    ac = c - a
    s = np.clip(((_P[ids] - a) @ ac) / (ac @ ac), 0, 1)
    r = float(np.median(np.linalg.norm(_P[ids] - (a + s[:, None] * ac), axis=1)))
    return min(max(r, 0.007), 0.022)


RAD = {f"{s}Hand{d}{k}": seg_radius(f"{s}Hand{d}{k}") for s in SIDES for d in DIGITS for k in (1, 2, 3)}
PALM = {s: palm_points(s) for s in SIDES}


# ------------------------------------------------------------------ the gun
def gun_space(gun):
    """World matrix of the gun's Gun bone and helpers: gun space (right, up, fwd) <-> world."""
    rig = bpy.data.objects[gun + "_Rig"]
    return rig.matrix_world @ rig.pose.bones["Gun"].matrix


def gdir(G, r, u, f):
    """(right, up, forward) in gun space -> world direction. Gun bone: muzzle -Y, top +Z, right -X."""
    R = G.to_3x3().normalized()
    return (R @ Vector((-r, -f, u))).normalized()


def gpt(G, r, u, f):
    return G @ Vector((-r, -f, u))


def gun_bvh(gun):
    rig = bpy.data.objects[gun + "_Rig"]
    dg = bpy.context.evaluated_depsgraph_get()
    vs, ps = [], []
    for o in bpy.data.objects:
        if o.type != "MESH" or o.parent != rig or o.hide_get():
            continue
        e = o.evaluated_get(dg)
        me = e.to_mesh()
        base = len(vs)
        vs += [o.matrix_world @ v.co for v in me.vertices]
        ps += [[base + i for i in p.vertices] for p in me.polygons]
        e.to_mesh_clear()
    return BVHTree.FromPolygons(vs, ps, epsilon=0.0)


class Obstacles:
    """The gun (signed: inside is negative) plus capsules of the other hand."""
    def __init__(self, bvh, caps=()):
        self.bvh, self.caps = bvh, list(caps)

    def gap(self, p, r):
        best = 1.0
        # unsigned: the Meshy guns have flipped faces, so the face normal cannot tell inside from outside;
        # fingers always close in from free space, so the first touch is what counts
        hit = self.bvh.find_nearest(p)
        if hit[0] is not None:
            best = (p - hit[0]).length - r
        for a, b, ra, rb in self.caps:
            ab = b - a
            t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.dot(ab), 1e-12)))
            q = a + ab * t
            best = min(best, (p - q).length - r - (ra + (rb - ra) * t))
        return best


# ------------------------------------------------------------------ arms: two-bone IK onto the hand
def _aim(name, target, keep_twist_from=None):
    """Rotate a pose bone (parents already posed) so its tail points at target, with minimal twist."""
    p = pb(name)
    p.rotation_quaternion = Quaternion()
    upd()
    M = p.matrix.copy()
    y = M.col[1].to_3d().normalized()
    d = (Vector(target) - M.translation).normalized()
    q = y.rotation_difference(d)
    R = (q.to_matrix() @ M.to_3x3().normalized()).to_4x4()
    R.translation = M.translation
    p.matrix = R
    upd()


def solve_arm(side, hand_M, pole, twist=0.5):
    """Shoulder->elbow->wrist onto hand_M (armature-space Hand matrix); pole: direction the elbow points."""
    up, lo, hn = side + "UpperArm", side + "LowerArm", side + "Hand"
    for n in (up, lo, hn):
        pb(n).rotation_quaternion = Quaternion()
    upd()
    S = RIG.pose.bones[up].head.copy()
    Wt = hand_M.translation.copy()
    a = (B[lo].head_local - B[up].head_local).length
    b = (B[hn].head_local - B[lo].head_local).length
    d_vec = Wt - S
    d = min(d_vec.length, (a + b) * 0.9999)
    x = d_vec.normalized()
    pv = Vector(pole)
    pv = (pv - x * pv.dot(x))
    pv = pv.normalized() if pv.length > 1e-6 else Vector((0, 0, -1))
    cos_a = (a * a + d * d - b * b) / (2 * a * d)
    cos_a = max(-1.0, min(1.0, cos_a))
    E = S + x * (a * cos_a) + pv * (a * math.sqrt(max(0.0, 1 - cos_a * cos_a)))
    _aim(up, E)
    _aim(lo, S + x * d)
    # forearm twist: share the hand's roll with the forearm so the wrist does not wring
    p = pb(hn)
    p.matrix = hand_M
    upd()
    if twist:
        L = RIG.pose.bones[lo].matrix
        fa = L.col[1].to_3d().normalized()
        hx = hand_M.col[0].to_3d()
        hx = (hx - fa * hx.dot(fa)).normalized()
        lx = L.col[0].to_3d()
        lx = (lx - fa * lx.dot(fa)).normalized()
        ang = math.atan2(lx.cross(hx).dot(fa), lx.dot(hx))
        Lm = (Matrix.Rotation(ang * twist, 4, fa) @ Matrix(L).to_3x3().to_4x4())
        Lm.translation = L.translation
        RIG.pose.bones[lo].matrix = Lm
        upd()
        pb(hn).matrix = hand_M
        upd()
    reach = d_vec.length / (a + b)
    return reach


# ------------------------------------------------------------------ fingers
def digit_caps(side, digit):
    """Posed capsules of one digit (armature space): (head, tail, r_head, r_tail) x3."""
    out = []
    for k in (1, 2, 3):
        n = f"{side}Hand{digit}{k}"
        p = RIG.pose.bones[n]
        r = RAD[n]
        r2 = RAD[f"{side}Hand{digit}{k + 1}"] if k < 3 else r * 0.85
        out.append((p.head.copy(), p.tail.copy(), r, r2))
    return out


def seg_gap(ob, cap, n=6):
    a, b, ra, rb = cap
    return min(ob.gap(a + (b - a) * (i / (n - 1)), ra + (rb - ra) * (i / (n - 1))) for i in range(n))


def set_digit(side, digit, angles, spread=0.0, twist=0.0):
    """angles: curl (deg) at the 3 joints; spread: sideways at the base (deg, + toward the thumb side)."""
    for k, a in enumerate(angles):
        q = Quaternion((1, 0, 0), math.radians(a))
        if k == 0 and (spread or twist):
            q = Quaternion((0, 0, 1), math.radians(spread)) @ Quaternion((0, 1, 0), math.radians(twist)) @ q
        pb(f"{side}Hand{digit}{k + 1}").rotation_quaternion = q
    upd()


def close_digit(ob, side, digit, start=(0, 0, 0), ratio=(1.0, 1.15, 0.8), limit=(95, 105, 80), spread=0.0, twist=0.0, step=2.0):
    """Curl a digit joint by joint until each segment touches (gap <= CLEAR) or reaches its limit."""
    ang = list(start)
    set_digit(side, digit, ang, spread, twist)
    # already inside the gun (the rest pose is pre-curled): open the finger until it is free
    while min(seg_gap(ob, c) for c in digit_caps(side, digit)) <= CLEAR and ang[0] > -40:
        ang = [ang[0] - 2, max(-10, ang[1] - 2), max(-10, ang[2] - 2)]
        set_digit(side, digit, ang, spread, twist)
    for k in range(3):
        while ang[k] < limit[k]:
            trial = list(ang)
            trial[k] = min(limit[k], trial[k] + step)
            # carry the distal joints along proportionally so the finger wraps instead of poking
            for j in range(k + 1, 3):
                trial[j] = max(trial[j], min(limit[j], trial[k] * ratio[j] / ratio[k] * 0.6))
            set_digit(side, digit, trial, spread, twist)
            caps = digit_caps(side, digit)
            if min(seg_gap(ob, c) for c in caps[k:]) <= CLEAR:
                set_digit(side, digit, ang, spread, twist)
                break
            ang = trial
    set_digit(side, digit, ang, spread, twist)
    caps = digit_caps(side, digit)
    return ang, [round(seg_gap(ob, c) * 1000, 1) for c in caps]


def pad_point(side, digit, at=0.55):
    """The finger pad: along the last segment, on its palm side (+Z of the bone)."""
    n = f"{side}Hand{digit}3"
    p = RIG.pose.bones[n]
    z = p.matrix.col[2].to_3d().normalized()
    return p.head + (p.tail - p.head) * at + z * RAD[n] * 0.9


def reach_pad(ob, side, digit, target, start=(20, 20, 10, 0), lo=(-20, -5, -5, -25), hi=(95, 105, 80, 25), at=0.55, iters=60, starts=None):
    """Coordinate search of (curl1, curl2, curl3, spread) bringing the pad onto target without sinking in.
    starts: several start poses; the best result wins (the gun can block the path from a single start)."""
    if starts:
        best = None
        for s0 in starts:
            r = reach_pad(ob, side, digit, target, s0, lo, hi, at, iters)
            set_digit(side, digit, r[0][:3], r[0][3])
            pen = min(seg_gap(ob, c) for c in digit_caps(side, digit))
            score = r[1] + max(0.0, CLEAR - pen) * 8.0
            if best is None or score < best[0]:
                best = (score, r)
        set_digit(side, digit, best[1][0][:3], best[1][0][3])
        return best[1]
    target = Vector(target)
    x = list(start)

    def cost(v):
        set_digit(side, digit, v[:3], v[3])
        c = (pad_point(side, digit, at) - target).length
        caps = digit_caps(side, digit)
        pen = min(seg_gap(ob, cp) for cp in caps)
        return c + (max(0.0, CLEAR - pen)) * 8.0
    best = cost(x)
    step = 8.0
    for _ in range(iters):
        improved = False
        for i in range(4):
            for sgn in (1, -1):
                y = list(x)
                y[i] = max(lo[i], min(hi[i], y[i] + sgn * step))
                c = cost(y)
                if c < best - 1e-6:
                    best, x, improved = c, y, True
        if not improved:
            step *= 0.5
            if step < 0.5:
                break
    set_digit(side, digit, x[:3], x[3])
    return x, (pad_point(side, digit, at) - target).length


def hand_caps(side):
    caps = []
    for d in DIGITS:
        caps += digit_caps(side, d)
    # palm as three capsules wrist -> knuckles
    F = RIG.pose.bones[side + "Hand"].matrix @ HB2TUN[side]
    for t in (-0.035, 0.0, 0.035):
        caps.append((F @ Vector((t, 0.02, 0.005)), F @ Vector((t, 0.11, 0.005)), 0.03, 0.025))
    return caps


def palm_gap(ob, side, F):
    return min(ob.gap(F @ q, 0.0) for q in PALM[side][::3])


def slide_until_touch(ob, side, F, direction, max_d=0.08, step=0.001):
    """Move frame F along direction until the palm skin touches (gap CLEAR)."""
    d = Vector(direction).normalized()
    g0 = palm_gap(ob, side, F)
    s = 0.0
    if g0 > CLEAR:
        while s < max_d and palm_gap(ob, side, Matrix.Translation(d * (s + step)) @ F) > CLEAR:
            s += step
    else:
        while s > -max_d and palm_gap(ob, side, Matrix.Translation(d * s) @ F) <= CLEAR:
            s -= step
    return Matrix.Translation(d * s) @ F, s


def setL(gun, ang, yaw, roll, k_at, curls, thumb=None, pole=(-0.6,-1,-0.2)):
    G = gun_space(gun)
    fw, upv, rt = gdir(G,0,0,1), gdir(G,0,1,0), gdir(G,1,0,0)
    KN = sum((to_hand("Left", B[f"LeftHand{f}1"].head_local) for f in FINGERS), Vector()) / 4
    a = math.radians(ang)
    u = fw*math.cos(a) + upv*math.sin(a); t = upv*math.cos(a) - fw*math.sin(a)
    F = frame("Left", t, u, Vector((0,0,0)))
    F = Matrix.Rotation(math.radians(-yaw), 4, upv) @ F
    F = Matrix.Rotation(math.radians(roll), 4, F.col[1].to_3d()) @ F
    F.translation = gpt(G, *k_at) - F.to_3x3() @ KN
    for n in ("LeftUpperArm","LeftLowerArm","LeftHand"): pb(n).rotation_quaternion = (1,0,0,0)
    upd()
    reach = solve_arm("Left", hand_matrix("Left", F), pole=gdir(G,*pole))
    for d, c in curls.items(): set_digit("Left", d, c[:3], c[3] if len(c) > 3 else 0)
    return F, reach


def right_grip_search(gun, thetas, rs, ups, fwds, trig, pip_off=(0.024, 0.0, 0.008), quiet=False):
    G = gun_space(gun); bvh = gun_bvh(gun); ob = Obstacles(bvh)
    fw, upv = gdir(G,0,0,1), gdir(G,0,1,0)
    MID = to_hand("Right", B["RightHandMiddle1"].head_local)
    PIP_T = gpt(G, trig[0] + pip_off[0], trig[1] + pip_off[1], trig[2] + pip_off[2]); DIP_T = gpt(G, *trig)
    rows = []
    for theta, r_, u_, f_ in itertools.product(thetas, rs, ups, fwds):
        th = math.radians(theta); u = fw*math.cos(th) - upv*math.sin(th); t = upv*math.cos(th) + fw*math.sin(th)
        F = frame("Right", t, u, Vector((0,0,0))); F.translation = gpt(G, r_, u_, f_) - F.to_3x3() @ MID
        pg = palm_gap(ob, "Right", F)
        if pg < -0.005: continue
        reset_pose(); pb("RightHand").matrix = hand_matrix("Right", F); upd()
        globals()["CLEAR"] = -0.002
        res = {d: close_digit(ob, "Right", d, spread=sp, step=4.0) for d, sp in (("Middle",0),("Ring",-2),("Pinky",-4))}
        best = None
        for a1, a2, sp in itertools.product(range(-10, 61, 10), range(20, 106, 10), range(-30, 41, 10)):
            set_digit("Right", "Index", (a1, a2, 25), sp)
            c = (RIG.pose.bones["RightHandIndex2"].head - PIP_T).length + (RIG.pose.bones["RightHandIndex3"].head - DIP_T).length
            if best is None or c < best[0]: best = (c, a1, a2, sp)
        set_digit("Right", "Index", (best[1], best[2], 25), best[3])
        gi = [seg_gap(ob,cp)*1000 for cp in digit_caps("Right","Index")]
        globals()["CLEAR"] = 0.0015
        sc = best[0]*1000 + sum(max(0,-x-2)*2 for x in gi) + max(0,pg*1000)*0.5
        for d,(a,g) in res.items():
            w = 0.3 if d == "Pinky" else 1.0
            sc += w * (sum(max(0,-x-2)*3 for x in g) + sum(min(abs(x),25) for x in g) + max(0,-a[0])*0.6 + max(0,-a[1]))
        rows.append((round(sc,1), (theta, r_, u_, f_), round(pg*1000,1), {d: [round(x) for x in a] for d,(a,g) in res.items()}, round(best[0]*1000,1)))
    rows.sort(key=lambda r: r[0])
    if not quiet:
        for r in rows[:5]: print(r)
    return rows

def right_grip_apply(gun, p, trig, pip_off=(0.024, 0.0, 0.008), thumb_targets=(), pole=(0.6,-1,-0.2)):
    G = gun_space(gun); bvh = gun_bvh(gun); ob = Obstacles(bvh)
    fw, upv = gdir(G,0,0,1), gdir(G,0,1,0)
    MID = to_hand("Right", B["RightHandMiddle1"].head_local)
    theta, r_, u_, f_ = p
    th = math.radians(theta); u = fw*math.cos(th) - upv*math.sin(th); t = upv*math.cos(th) + fw*math.sin(th)
    F = frame("Right", t, u, Vector((0,0,0))); F.translation = gpt(G, r_, u_, f_) - F.to_3x3() @ MID
    for n in ("RightUpperArm","RightLowerArm","RightHand"): pb(n).rotation_quaternion = (1,0,0,0)
    upd()
    reach = solve_arm("Right", hand_matrix("Right", F), pole=gdir(G, *pole))
    globals()["CLEAR"] = -0.002
    for d, sp in (("Middle",0),("Ring",-2)): close_digit(ob, "Right", d, spread=sp, step=2.0)
    ob2 = Obstacles(bvh, digit_caps("Right","Ring")); close_digit(ob2, "Right", "Pinky", spread=-4, step=2.0)
    PIP_T = gpt(G, trig[0] + pip_off[0], trig[1] + pip_off[1], trig[2] + pip_off[2]); DIP_T = gpt(G, *trig)
    best = None
    for a1, a2, a3, sp in itertools.product(range(-10, 61, 5), range(20, 106, 5), (10, 30, 50), range(-30, 41, 5)):
        set_digit("Right", "Index", (a1, a2, a3), sp)
        gi = min(seg_gap(ob,cp) for cp in digit_caps("Right","Index"))
        c = (RIG.pose.bones["RightHandIndex2"].head - PIP_T).length + (RIG.pose.bones["RightHandIndex3"].head - DIP_T).length + max(0, -0.003 - gi) * 3
        if best is None or c < best[0]: best = (c, a1, a2, a3, sp)
    set_digit("Right", "Index", best[1:4], best[4])
    if thumb_targets:
        obT = Obstacles(bvh, digit_caps("Right","Middle") + digit_caps("Right","Index"))
        M_T, I_T, TIP_T = [gpt(G, *x) for x in thumb_targets]
        def cost(v):
            set_digit("Right", "Thumb", (v[0], v[3], v[4]), v[1], v[2])
            g = min(seg_gap(obT, cp) for cp in digit_caps("Right","Thumb")[1:])
            return ((RIG.pose.bones["RightHandThumb2"].head - M_T).length + (RIG.pose.bones["RightHandThumb3"].head - I_T).length
                    + (RIG.pose.bones["RightHandThumb3"].tail - TIP_T).length + max(0, -0.002 - g) * 4)
        bt = None
        for start in ([10, 20, -8, -20, 10], [10, 20, 0, 0, 20], [20, 30, 20, 10, 30], [0, 10, -20, 20, 40], [30, 0, 0, 0, 0]):
            x = list(start); bc = cost(x); step = 8
            while step >= 1:
                imp = False
                for i in range(5):
                    for s in (1, -1):
                        y = list(x); y[i] += s*step; cc = cost(y)
                        if cc < bc: bc, x, imp = cc, y, True
                if not imp: step /= 2
            if bt is None or bc < bt[0]: bt = (bc, x)
        cost(bt[1])
        print("thumb fit mm", round(bt[0]*1000,1))
    globals()["CLEAR"] = 0.0015
    print("index fit mm", round(best[0]*1000,1), "reach", round(reach,2))
    return F


def left_under(gun, beta, tilt, at, pole=(-0.6,-1,0.0), slide=True):
    G = gun_space(gun); bvh = gun_bvh(gun); ob = Obstacles(bvh)
    fw, upv, rt = gdir(G,0,0,1), gdir(G,0,1,0), gdir(G,1,0,0)
    PALMC = Vector((0.0, 0.06, 0.04))
    b = math.radians(beta); tl = math.radians(tilt)
    u = rt*math.cos(b) + fw*math.sin(b)
    nrm = upv*math.cos(tl) - rt*math.sin(tl)
    t = nrm.cross(u)
    F = frame("Left", t, u, Vector((0,0,0)))
    if F.col[2].to_3d().dot(upv) < 0: F = frame("Left", -t, u, Vector((0,0,0)))
    F.translation = gpt(G, *at) - F.to_3x3() @ PALMC
    if slide: F, s = slide_until_touch(ob, "Left", F, F.col[2].to_3d(), max_d=0.15)
    for n in ("LeftUpperArm","LeftLowerArm","LeftHand"): pb(n).rotation_quaternion = (1,0,0,0)
    upd()
    reach = solve_arm("Left", hand_matrix("Left", F), pole=gdir(G,*pole))
    return F, reach, ob


def torso(turn=0.0, lean=0.0, bones=("Chest", "UpperChest")):
    """Turn (deg, + = left shoulder forward / chest faces right) and lean (deg, + = forward) shared by the spine bones."""
    for n in bones:
        p = pb(n); p.rotation_quaternion = Quaternion(); upd()
    for n in bones:
        p = pb(n); M = p.matrix.copy(); h = M.translation.copy()
        R = Matrix.Rotation(math.radians(-turn / len(bones)), 4, "Z") @ Matrix.Rotation(math.radians(lean / len(bones)), 4, "X")
        p.matrix = Matrix.Translation(h) @ R @ Matrix.Translation(-h) @ M
        upd()


def wrap_digit(ob, side, digit, max_iter=40, target=0.004):
    """Close the knuckle further while opening the end joints, until the base segment lies on the gun too."""
    qs = [pb(f"{side}Hand{digit}{k}").rotation_quaternion.copy() for k in (1, 2, 3)]
    ang = [math.degrees(2 * math.atan2(q.x, q.w)) for q in qs]
    sp = 0.0
    for _ in range(max_iter):
        caps = digit_caps(side, digit)
        g = [seg_gap(ob, c) for c in caps]
        if g[0] <= target and g[1] <= target + 0.004:
            break
        trial = [ang[0] + 2, ang[1], ang[2]]
        set_digit(side, digit, trial, sp)
        caps = digit_caps(side, digit)
        g2 = [seg_gap(ob, c) for c in caps]
        k = 2
        while min(g2) < CLEAR and k >= 1:
            while min(g2) < CLEAR and trial[k] > -5:
                trial[k] -= 3
                set_digit(side, digit, trial, sp)
                g2 = [seg_gap(ob, c) for c in digit_caps(side, digit)]
            k -= 1
        if min(g2) < CLEAR:
            set_digit(side, digit, ang, sp); break
        ang = trial
        if g2[1] > target + 0.004 and ang[1] < 100:
            t2 = [ang[0], ang[1] + 3, ang[2]]; set_digit(side, digit, t2, sp)
            if min(seg_gap(ob, c) for c in digit_caps(side, digit)) >= CLEAR: ang = t2
            else: set_digit(side, digit, ang, sp)
    set_digit(side, digit, ang, sp)
    return [round(a) for a in ang], [round(seg_gap(ob, c) * 1000, 1) for c in digit_caps(side, digit)]


# neutral wrist: the forearm's direction expressed in the hand bone's frame at rest
NEUTRAL = {}
for _s in SIDES:
    _L = B[_s + "LowerArm"].matrix_local; _H = B[_s + "Hand"].matrix_local
    NEUTRAL[_s] = (_H.to_3x3().inverted() @ _L.col[1].to_3d()).normalized()
    NEUTRAL[_s + "_x"] = (_H.to_3x3().inverted() @ _L.col[0].to_3d()).normalized()

def wrist_state(side):
    """(bend deg, twist deg) of the wrist: forearm direction vs the hand's neutral forearm direction."""
    L = RIG.pose.bones[side + "LowerArm"].matrix; H = RIG.pose.bones[side + "Hand"].matrix
    fa = L.col[1].to_3d().normalized()
    want = (H.to_3x3() @ NEUTRAL[side]).normalized()
    bend = math.degrees(fa.angle(want))
    lx = L.col[0].to_3d(); lx = (lx - fa * lx.dot(fa)).normalized()
    hx = (H.to_3x3() @ NEUTRAL[side + "_x"]); hx = (hx - fa * hx.dot(fa)).normalized()
    tw = math.degrees(math.atan2(lx.cross(hx).dot(fa), lx.dot(hx)))
    return bend, tw


NEUTRAL = {}
for _s in SIDES:
    _L = B[_s + "LowerArm"].matrix_local; _H = B[_s + "Hand"].matrix_local
    NEUTRAL[_s] = (_H.to_3x3().inverted() @ _L.col[1].to_3d()).normalized()
    NEUTRAL[_s + "_x"] = (_H.to_3x3().inverted() @ _L.col[0].to_3d()).normalized()

def wrist_state(side):
    """(bend deg, twist deg) of the wrist: forearm direction vs the hand's neutral forearm direction."""
    L = RIG.pose.bones[side + "LowerArm"].matrix; H = RIG.pose.bones[side + "Hand"].matrix
    fa = L.col[1].to_3d().normalized()
    want = (H.to_3x3() @ NEUTRAL[side]).normalized()
    bend = math.degrees(fa.angle(want))
    lx = L.col[0].to_3d(); lx = (lx - fa * lx.dot(fa)).normalized()
    hx = (H.to_3x3() @ NEUTRAL[side + "_x"]); hx = (hx - fa * hx.dot(fa)).normalized()
    tw = math.degrees(math.atan2(lx.cross(hx).dot(fa), lx.dot(hx)))
    return bend, tw


def _hinge_sign(side):
    up, lo, hn = side + "UpperArm", side + "LowerArm", side + "Hand"
    S, E, W = B[up].head_local, B[lo].head_local, B[hn].head_local
    h = (E - S).cross(W - E).normalized()
    return 1.0 if B[lo].matrix_local.col[0].to_3d().dot(h) >= 0 else -1.0
HINGE = {s: _hinge_sign(s) for s in SIDES}

def _bone_mat(head, ydir, xdir):
    y = ydir.normalized(); x = (xdir - y * xdir.dot(y)).normalized(); z = x.cross(y)
    M = Matrix((x, y, z)).transposed().to_4x4(); M.translation = head
    return M

def solve_arm2(side, hand_M, share=0.5, step=6, report=False):
    """Arm onto hand_M with the elbow as a hinge, the elbow placed (around the shoulder-wrist line) where the
    wrist is straightest and the forearm roll smallest; the roll is shared between elbow and wrist."""
    up, lo, hn = side + "UpperArm", side + "LowerArm", side + "Hand"
    for n in (up, lo, hn): pb(n).rotation_quaternion = Quaternion()
    upd()
    S = RIG.pose.bones[up].head.copy()
    W = hand_M.translation.copy()
    a = (B[lo].head_local - B[up].head_local).length
    b = (B[hn].head_local - B[lo].head_local).length
    dv = W - S; d = min(dv.length, (a + b) * 0.999); x = dv.normalized()
    cos_a = max(-1.0, min(1.0, (a * a + d * d - b * b) / (2 * a * d)))
    rad = a * math.sqrt(max(0.0, 1 - cos_a * cos_a)); C = S + x * (a * cos_a)
    ref = Vector((0, 0, -1)); ref = (ref - x * ref.dot(x))
    if ref.length < 1e-4: ref = Vector((1, 0, 0)) - x * x.x
    ref.normalize(); ref2 = x.cross(ref)
    Hn = (hand_M.to_3x3() @ NEUTRAL[side]).normalized()
    Hx = hand_M.to_3x3() @ NEUTRAL[side + "_x"]
    mid = Vector((0, S.y, S.z))
    best = None
    for k in range(0, 360, step):
        t = math.radians(k)
        E = C + (ref * math.cos(t) + ref2 * math.sin(t)) * rad
        Wd = S + x * d
        fa = (Wd - E).normalized()
        hinge = (E - S).cross(Wd - E)
        if hinge.length < 1e-6: continue
        hinge = hinge.normalized() * HINGE[side]
        bend = math.degrees(fa.angle(Hn))
        hx = (Hx - fa * Hx.dot(fa)).normalized(); lx = (hinge - fa * hinge.dot(fa)).normalized()
        pron = math.degrees(math.atan2(lx.cross(hx).dot(fa), lx.dot(hx)))
        cost = bend ** 2 + 0.25 * pron ** 2 + 4.0 * max(0.0, abs(pron) - 90) ** 2
        if E.z > S.z + 0.03: cost += (E.z - S.z - 0.03) * 1e6               # elbow above the shoulder
        inward = (E.x - S.x) * (1 if side == "Right" else -1)                   # toward the body's middle
        if inward > 0.06: cost += (inward - 0.06) * 3e5
        if (E - Vector((0, E.y, E.z))).length < 0.12 and E.y > -0.05: cost += 1e4   # inside the chest
        if best is None or cost < best[0]: best = (cost, E, hinge, pron, bend)
    cost, E, hinge, pron, bend = best
    Wd = S + x * d
    RIG.pose.bones[up].matrix = _bone_mat(S, E - S, hinge); upd()
    fa = (Wd - E).normalized()
    xr = Matrix.Rotation(math.radians(pron * share), 3, fa) @ hinge
    RIG.pose.bones[lo].matrix = _bone_mat(RIG.pose.bones[lo].head.copy(), fa, xr); upd()
    pb(hn).matrix = hand_M; upd()
    if report: print(side, "elbow", tuple(round(v, 3) for v in E), "bend", round(bend), "roll", round(pron))
    return dv.length / (a + b)


def right_grip_search2(gun, thetas, rs, ups, fwds, trig, pip_off=(0.024, 0.0, 0.008), turn=0, lean=0, wcost=0.03):
    G = gun_space(gun); bvh = gun_bvh(gun); ob = Obstacles(bvh)
    fw, upv = gdir(G,0,0,1), gdir(G,0,1,0)
    MID = to_hand("Right", B["RightHandMiddle1"].head_local)
    PIP_T = gpt(G, trig[0] + pip_off[0], trig[1] + pip_off[1], trig[2] + pip_off[2]); DIP_T = gpt(G, *trig)
    rows = []
    for theta, r_, u_, f_ in itertools.product(thetas, rs, ups, fwds):
        th = math.radians(theta); u = fw*math.cos(th) - upv*math.sin(th); t = upv*math.cos(th) + fw*math.sin(th)
        F = frame("Right", t, u, Vector((0,0,0))); F.translation = gpt(G, r_, u_, f_) - F.to_3x3() @ MID
        pg = palm_gap(ob, "Right", F)
        if pg < -0.005: continue
        reset_pose(); torso(turn, lean)
        reach = solve_arm2("Right", hand_matrix("Right", F))
        bend, roll = wrist_state("Right")
        globals()["CLEAR"] = -0.002
        res = {d: close_digit(ob, "Right", d, spread=sp, step=4.0) for d, sp in (("Middle",0),("Ring",-2),("Pinky",-4))}
        best = None
        for a1, a2, sp in itertools.product(range(-10, 61, 10), range(20, 106, 10), range(-30, 41, 10)):
            set_digit("Right", "Index", (a1, a2, 25), sp)
            c = (RIG.pose.bones["RightHandIndex2"].head - PIP_T).length + (RIG.pose.bones["RightHandIndex3"].head - DIP_T).length
            if best is None or c < best[0]: best = (c, a1, a2, sp)
        globals()["CLEAR"] = 0.0015
        sc = best[0]*1000 + max(0,pg*1000)*0.5 + wcost * bend ** 2 + 0.01 * max(0, abs(roll) - 45) ** 2 + max(0, reach - 0.97) * 500
        for d,(a,g) in res.items():
            w = 0.3 if d == "Pinky" else 1.0
            sc += w * (sum(max(0,-x-2)*3 for x in g) + sum(min(abs(x),25) for x in g) + max(0,-a[0])*0.6 + max(0,-a[1]))
        rows.append((round(sc,1), (theta, r_, u_, f_), round(bend), round(roll), round(reach,2), round(best[0]*1000,1), {d: [round(x) for x in a] for d,(a,g) in res.items()}))
    rows.sort(key=lambda r: r[0])
    return rows


def wrist_parts(side):
    """Wrist angles in the hand frame: flex (+ palm-ward), deviation (+ toward the little finger), roll."""
    L = RIG.pose.bones[side + "LowerArm"].matrix; H = RIG.pose.bones[side + "Hand"].matrix
    F = (H @ HB2TUN[side]).to_3x3()
    back = F.inverted() @ (-L.col[1].to_3d().normalized())          # wrist -> elbow, in (t, u, n)
    rest = (TUN0[side].to_3x3().inverted() @ (-B[side + "LowerArm"].matrix_local.col[1].to_3d().normalized()))
    def parts(v): return math.degrees(math.atan2(v.z, -v.y)), math.degrees(math.atan2(-v.x, -v.y))
    f, d = parts(back); f0, d0 = parts(rest)
    return round(f - f0), round(d - d0), round(wrist_state(side)[1])


def wrist_cost(side):
    f, d, r = wrist_parts(side)          # f: + toward the back of the hand side forearm (see wrist_parts), d: + radial, r: roll
    flex = -f                             # + = hand bent toward the palm
    c = max(0, abs(flex) - 25) ** 2 + max(0, d - 12) ** 2 * 1.5 + max(0, -d - 30) ** 2 + 0.3 * max(0, abs(r) - 40) ** 2
    return c, (flex, d, r)

def right_grip_search3(gun, thetas, yaws, rs, ups, fwds, trig, pip_off=(0.024, 0.0, 0.008), turn=0, lean=0, wk=0.05):
    G = gun_space(gun); bvh = gun_bvh(gun); ob = Obstacles(bvh)
    fw, upv = gdir(G,0,0,1), gdir(G,0,1,0)
    MID = to_hand("Right", B["RightHandMiddle1"].head_local)
    PIP_T = gpt(G, trig[0] + pip_off[0], trig[1] + pip_off[1], trig[2] + pip_off[2]); DIP_T = gpt(G, *trig)
    rows = []
    for theta, yaw, r_, u_, f_ in itertools.product(thetas, yaws, rs, ups, fwds):
        th = math.radians(theta); u = fw*math.cos(th) - upv*math.sin(th); t = upv*math.cos(th) + fw*math.sin(th)
        F = frame("Right", t, u, Vector((0,0,0)))
        F = Matrix.Rotation(math.radians(yaw), 4, upv) @ F
        F.translation = gpt(G, r_, u_, f_) - F.to_3x3() @ MID
        pg = palm_gap(ob, "Right", F)
        if pg < -0.005 or pg > 0.012: continue
        reset_pose(); torso(turn, lean)
        reach = solve_arm2("Right", hand_matrix("Right", F))
        wc, parts = wrist_cost("Right")
        if wc * wk > 150: continue
        globals()["CLEAR"] = -0.002
        res = {d: close_digit(ob, "Right", d, spread=sp, step=4.0) for d, sp in (("Middle",0),("Ring",-2),("Pinky",-4))}
        best = None
        for a1, a2, sp in itertools.product(range(-10, 61, 10), range(20, 106, 10), range(-30, 41, 10)):
            set_digit("Right", "Index", (a1, a2, 25), sp)
            c = (RIG.pose.bones["RightHandIndex2"].head - PIP_T).length + (RIG.pose.bones["RightHandIndex3"].head - DIP_T).length
            if best is None or c < best[0]: best = (c, a1, a2, sp)
        globals()["CLEAR"] = 0.0015
        sc = best[0]*1000 + max(0,pg*1000)*0.5 + wk * wc + max(0, reach - 0.97) * 500
        for d,(a,g) in res.items():
            w = 0.3 if d == "Pinky" else 1.0
            sc += w * (sum(max(0,-x-2)*3 for x in g) + sum(min(abs(x),25) for x in g) + max(0,-a[0])*0.6 + max(0,-a[1]))
        rows.append((round(sc,1), (theta, yaw, r_, u_, f_), parts, round(reach,2), round(best[0]*1000,1), {d: [round(x) for x in a] for d,(a,g) in res.items()}))
    rows.sort(key=lambda r: r[0])
    return rows


def wrist_cost(side):
    f, d, r = wrist_parts(side)
    ext, ulnar = -f, d                    # ext: + bent back; ulnar: + toward the little finger, - toward the thumb
    c = max(0, ext - 25) ** 2 + max(0, -ext - 20) ** 2 * 1.5 + max(0, ulnar - 30) ** 2 + max(0, -ulnar - 12) ** 2 * 1.5 + 0.3 * max(0, abs(r) - 40) ** 2
    return c, (ext, ulnar, r)


def fit_right_fingers(gun, PIP, DIP, thumb_targets, thumb_lo=(-25, -50, -40, -20, -45), thumb_hi=(70, 50, 50, 60, 70)):
    G = gun_space(gun); bvh = gun_bvh(gun); ob = Obstacles(bvh)
    globals()["CLEAR"] = -0.002
    for d, sp in (("Middle",0),("Ring",-2)): set_digit("Right", d, (0,0,0), sp); close_digit(ob, "Right", d, spread=sp, step=2.0)
    set_digit("Right", "Pinky", (0,0,0), -4); close_digit(Obstacles(bvh, digit_caps("Right","Ring")), "Right", "Pinky", spread=-4, step=2.0)
    globals()["CLEAR"] = -0.003
    PIP_T = gpt(G, *PIP); DIP_T = gpt(G, *DIP)
    best = None
    for a1, a2, a3, sp in itertools.product(range(-10, 71, 5), range(10, 106, 5), (10, 30, 50), range(-20, 31, 5)):
        set_digit("Right", "Index", (a1, a2, a3), sp)
        gi = min(seg_gap(ob,cp) for cp in digit_caps("Right","Index"))
        c = (RIG.pose.bones["RightHandIndex2"].head - PIP_T).length + (RIG.pose.bones["RightHandIndex3"].head - DIP_T).length + max(0, -0.003 - gi) * 3
        if best is None or c < best[0]: best = (c, a1, a2, a3, sp)
    set_digit("Right", "Index", best[1:4], best[4])
    obT = Obstacles(bvh, digit_caps("Right","Middle") + digit_caps("Right","Index"))
    M_T, I_T, TIP_T = [gpt(G, *x) for x in thumb_targets]
    def cost(v):
        set_digit("Right", "Thumb", (v[0], v[3], v[4]), v[1], v[2])
        g = min(seg_gap(obT, cp) for cp in digit_caps("Right","Thumb")[1:])
        return ((RIG.pose.bones["RightHandThumb2"].head - M_T).length + (RIG.pose.bones["RightHandThumb3"].head - I_T).length
                + (RIG.pose.bones["RightHandThumb3"].tail - TIP_T).length + max(0, -0.003 - g) * 4)
    bt = None
    for start in ([10, 20, 0, 0, 10], [20, 30, 20, 10, 20], [0, 10, -20, 0, -20], [30, 0, 0, 0, 0], [40, 40, 0, 0, -30], [20, 40, 40, -15, -35], [10, 40, 30, 5, 5]):
        x = list(start); bc = cost(x); step = 8
        while step >= 1:
            imp = False
            for i in range(5):
                for s in (1, -1):
                    y = list(x); y[i] = max(thumb_lo[i], min(thumb_hi[i], y[i] + s*step)); cc = cost(y)
                    if cc < bc: bc, x, imp = cc, y, True
            if not imp: step /= 2
        if bt is None or bc < bt[0]: bt = (bc, x)
    cost(bt[1])
    globals()["CLEAR"] = 0.0015
    return round(best[0]*1000, 1), round(bt[0]*1000, 1)

def fit_left_thumb(gun, tip, ip, obstacles_caps=(), lo=(-30,-30,-45,-60,-40), hi=(70,60,70,60,40)):
    G = gun_space(gun); bvh = gun_bvh(gun); ob = Obstacles(bvh, list(obstacles_caps))
    TIP = gpt(G, *tip); IP = gpt(G, *ip)
    globals()["CLEAR"] = -0.003
    best = None
    for a1, a2, a3, sp, tw in itertools.product(range(lo[0], hi[0]+1, 10), range(lo[1], hi[1]+1, 15), range(lo[2], hi[2]+1, 20), range(lo[3], hi[3]+1, 15), range(lo[4], hi[4]+1, 20)):
        set_digit("Left", "Thumb", (a1, a2, a3), sp, tw)
        gg = min(seg_gap(ob, c) for c in digit_caps("Left","Thumb")[1:])
        c = (RIG.pose.bones["LeftHandThumb3"].tail - TIP).length + (RIG.pose.bones["LeftHandThumb3"].head - IP).length + max(0, -0.003 - gg)*3
        if best is None or c < best[0]: best = (c, a1, a2, a3, sp, tw)
    set_digit("Left", "Thumb", best[1:4], best[4], best[5])
    globals()["CLEAR"] = 0.0015
    return round(best[0]*1000, 1), best[1:]


def left_under_search(gun, betas, tilts, pfs, pus, prs=(-0.008,), wk=0.1):
    rows = []
    for beta, tilt, pf, pu, prr in itertools.product(betas, tilts, pfs, pus, prs):
        FL, reach, ob = left_under(gun, beta, tilt, (prr, pu, pf))
        if reach > 0.98: continue
        solve_arm2("Left", hand_matrix("Left", FL))
        wc, parts = wrist_cost("Left")
        pg = palm_gap(ob, "Left", FL)
        globals()["CLEAR"] = -0.002
        sc = abs(pg) * 1000 + wk * wc; det = []
        for dg in ("Index", "Middle", "Ring", "Pinky"):
            set_digit("Left", dg, (0, 0, 0))
            an, g = close_digit(ob, "Left", dg, step=4.0)
            sc += sum(max(0, -x-2)*3 for x in g) + sum(min(abs(x), 25) for x in g) * 0.6 + max(0, -an[0])*0.6
            det.append([round(x) for x in an])
        globals()["CLEAR"] = 0.0015
        rows.append((round(sc, 1), (beta, tilt, pf, pu, prr), parts, round(reach, 2), round(pg*1000, 1), det))
    rows.sort(key=lambda r: r[0])
    return rows


def _elbow_penalty(side, E, S):
    p = 0.0
    if E.z > S.z + 0.03: p += (E.z - S.z - 0.03) * 1e6                       # elbow above the shoulder
    c = RIG.pose.bones["UpperChest"].head
    d = math.hypot(E.x - c.x, E.y - c.y)
    if 0.85 < E.z < 1.5 and d < 0.17: p += (0.17 - d) * 3e5                  # inside the torso
    return p

def solve_arm2(side, hand_M, share=0.5, step=6, report=False):
    up, lo, hn = side + "UpperArm", side + "LowerArm", side + "Hand"
    for n in (up, lo, hn): pb(n).rotation_quaternion = Quaternion()
    upd()
    S = RIG.pose.bones[up].head.copy()
    W = hand_M.translation.copy()
    a = (B[lo].head_local - B[up].head_local).length
    b = (B[hn].head_local - B[lo].head_local).length
    dv = W - S; d = min(dv.length, (a + b) * 0.999); x = dv.normalized()
    cos_a = max(-1.0, min(1.0, (a * a + d * d - b * b) / (2 * a * d)))
    rad = a * math.sqrt(max(0.0, 1 - cos_a * cos_a)); C = S + x * (a * cos_a)
    ref = Vector((0, 0, -1)); ref = (ref - x * ref.dot(x))
    if ref.length < 1e-4: ref = Vector((1, 0, 0)) - x * x.x
    ref.normalize(); ref2 = x.cross(ref)
    Hn = (hand_M.to_3x3() @ NEUTRAL[side]).normalized()
    Hx = hand_M.to_3x3() @ NEUTRAL[side + "_x"]
    best = None
    for k in range(0, 360, step):
        t = math.radians(k)
        E = C + (ref * math.cos(t) + ref2 * math.sin(t)) * rad
        Wd = S + x * d
        fa = (Wd - E).normalized()
        hinge = (E - S).cross(Wd - E)
        if hinge.length < 1e-6: continue
        hinge = hinge.normalized() * HINGE[side]
        bend = math.degrees(fa.angle(Hn))
        hx = (Hx - fa * Hx.dot(fa)).normalized(); lx = (hinge - fa * hinge.dot(fa)).normalized()
        pron = math.degrees(math.atan2(lx.cross(hx).dot(fa), lx.dot(hx)))
        cost = bend ** 2 + 0.25 * pron ** 2 + 4.0 * max(0.0, abs(pron) - 90) ** 2 + _elbow_penalty(side, E, S)
        if best is None or cost < best[0]: best = (cost, E, hinge, pron, bend)
    cost, E, hinge, pron, bend = best
    Wd = S + x * d
    RIG.pose.bones[up].matrix = _bone_mat(S, E - S, hinge); upd()
    fa = (Wd - E).normalized()
    xr = Matrix.Rotation(math.radians(pron * share), 3, fa) @ hinge
    RIG.pose.bones[lo].matrix = _bone_mat(RIG.pose.bones[lo].head.copy(), fa, xr); upd()
    pb(hn).matrix = hand_M; upd()
    if report: print(side, "elbow", tuple(round(v, 3) for v in E), "bend", round(bend), "roll", round(pron))
    return dv.length / (a + b)


def _elbow_dir_penalty(side, pd):
    """pd: unit direction the elbow points (from the shoulder-wrist line). Elbows point down or outward, never
    inward across the chest or up."""
    sr, sl = RIG.pose.bones["RightUpperArm"].head, RIG.pose.bones["LeftUpperArm"].head
    out = (sr - sl) if side == "Right" else (sl - sr)
    out = Vector((out.x, out.y, 0)).normalized()
    inward = -pd.dot(out); up = pd.z
    return max(0.0, inward - 0.25) * 4e4 + max(0.0, up - 0.2) * 4e4

_solve_arm2_core = solve_arm2
def solve_arm2(side, hand_M, share=0.5, step=6, report=False):
    up, lo, hn = side + "UpperArm", side + "LowerArm", side + "Hand"
    for n in (up, lo, hn): pb(n).rotation_quaternion = Quaternion()
    upd()
    S = RIG.pose.bones[up].head.copy()
    W = hand_M.translation.copy()
    a = (B[lo].head_local - B[up].head_local).length
    b = (B[hn].head_local - B[lo].head_local).length
    dv = W - S; d = min(dv.length, (a + b) * 0.999); x = dv.normalized()
    cos_a = max(-1.0, min(1.0, (a * a + d * d - b * b) / (2 * a * d)))
    rad = a * math.sqrt(max(0.0, 1 - cos_a * cos_a)); C = S + x * (a * cos_a)
    ref = Vector((0, 0, -1)); ref = (ref - x * ref.dot(x))
    if ref.length < 1e-4: ref = Vector((1, 0, 0)) - x * x.x
    ref.normalize(); ref2 = x.cross(ref)
    Hn = (hand_M.to_3x3() @ NEUTRAL[side]).normalized()
    Hx = hand_M.to_3x3() @ NEUTRAL[side + "_x"]
    best = None
    for k in range(0, 360, step):
        t = math.radians(k)
        pd = (ref * math.cos(t) + ref2 * math.sin(t))
        E = C + pd * rad
        Wd = S + x * d
        fa = (Wd - E).normalized()
        hinge = (E - S).cross(Wd - E)
        if hinge.length < 1e-6: continue
        hinge = hinge.normalized() * HINGE[side]
        bend = math.degrees(fa.angle(Hn))
        hx = (Hx - fa * Hx.dot(fa)).normalized(); lx = (hinge - fa * hinge.dot(fa)).normalized()
        pron = math.degrees(math.atan2(lx.cross(hx).dot(fa), lx.dot(hx)))
        cost = (bend ** 2 + 0.25 * pron ** 2 + 4.0 * max(0.0, abs(pron) - 90) ** 2
                + _elbow_penalty(side, E, S) + (_elbow_dir_penalty(side, pd) if rad > 0.03 else 0.0))
        if best is None or cost < best[0]: best = (cost, E, hinge, pron, bend)
    cost, E, hinge, pron, bend = best
    Wd = S + x * d
    RIG.pose.bones[up].matrix = _bone_mat(S, E - S, hinge); upd()
    fa = (Wd - E).normalized()
    xr = Matrix.Rotation(math.radians(pron * share), 3, fa) @ hinge
    RIG.pose.bones[lo].matrix = _bone_mat(RIG.pose.bones[lo].head.copy(), fa, xr); upd()
    pb(hn).matrix = hand_M; upd()
    if report: print(side, "elbow", tuple(round(v, 3) for v in E), "bend", round(bend), "roll", round(pron))
    return dv.length / (a + b)


def _elbow_dir_penalty(side, pd):
    sr, sl = RIG.pose.bones["RightUpperArm"].head, RIG.pose.bones["LeftUpperArm"].head
    out = (sr - sl) if side == "Right" else (sl - sr)
    out = Vector((out.x, out.y, 0)).normalized()
    inward = -pd.dot(out); up = pd.z
    allow = 0.25 if side == "Right" else 0.6          # the support elbow tucks under the gun
    return max(0.0, inward - allow) * 4e4 + max(0.0, up - 0.2) * 4e4


def _elbow_dir_penalty(side, pd):
    sr, sl = RIG.pose.bones["RightUpperArm"].head, RIG.pose.bones["LeftUpperArm"].head
    out = (sr - sl) if side == "Right" else (sl - sr)
    out = Vector((out.x, out.y, 0)).normalized()
    inward = -pd.dot(out); up = pd.z
    p = max(0.0, up - 0.2) * 4e4
    if side == "Right": p += max(0.0, inward - 0.25) * 4e4      # the trigger-hand elbow never folds across the chest
    return p


_solve_arm2_inner = solve_arm2
def solve_arm2(side, hand_M, share=0.5, step=6, report=False, relax=0.0):
    """relax 0..1: how far the hand turns from its target toward a natural line with the forearm (a hand away from
    the gun, carrying a magazine, should not keep the grip's wrist angle)."""
    W = hand_M.translation
    S = RIG.pose.bones[side + "UpperArm"].head
    a = (B[side + "LowerArm"].head_local - B[side + "UpperArm"].head_local).length
    b = (B[side + "Hand"].head_local - B[side + "LowerArm"].head_local).length
    if (W - S).length >= (a + b) * 0.995:                 # straight arm: pull the target in a hair so the elbow can bend
        M = hand_M.copy(); M.translation = S + (W - S).normalized() * (a + b) * 0.99; hand_M = M
    r = _solve_arm2_inner(side, hand_M, share, step, report)
    if relax > 1e-3:
        L = RIG.pose.bones[side + "LowerArm"].matrix
        nat = (L.to_3x3() @ (B[side + "LowerArm"].matrix_local.to_3x3().inverted() @ B[side + "Hand"].matrix_local.to_3x3())).to_quaternion()
        q = hand_M.to_quaternion().slerp(nat, min(1.0, relax))
        M = q.to_matrix().to_4x4(); M.translation = RIG.pose.bones[side + "Hand"].matrix.translation
        pb(side + "Hand").matrix = M; upd()
    return r


_solve_arm2_outer = solve_arm2
def solve_arm2(side, hand_M, share=0.5, step=6, report=False, relax=0.0):
    try:
        return _solve_arm2_outer(side, hand_M, share, step, report, relax)
    except TypeError:
        S = RIG.pose.bones[side + "UpperArm"].head
        M = hand_M.copy(); M.translation = S + (hand_M.translation - S) * 0.93     # degenerate: bend the arm a little
        return _solve_arm2_outer(side, M, share, step, report, relax)


# scale-aware gun space: matrices are rigid (no scale); gun-space points are scaled by the rig's scale
CUR_S = [1.0]
def gun_space(gun):
    rig = bpy.data.objects[gun + "_Rig"]
    M = rig.matrix_world @ rig.pose.bones["Gun"].matrix
    l, r, s = M.decompose()
    CUR_S[0] = s.x
    return Matrix.LocRotScale(l, r, None)

def gpt(G, r, u, f):
    s = CUR_S[0]
    return G @ Vector((-r * s, -f * s, u * s))
