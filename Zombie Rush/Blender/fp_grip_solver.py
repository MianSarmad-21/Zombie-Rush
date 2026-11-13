"""Grip solver for first-person hands: finger forward kinematics, contact closing against the gun
(a BVH) and the other hand (capsules), and a reach solver for trigger fingers and thumbs.

exec() this after a hand builder (fp_hands.py or fp_survivor_hands.py) that defines, in the same globals:
rig, SIDES, DIGITS, FINGERS4, HAND_INFO {side: {R, W}}, chain_points(side, digit) -> (4 joint points,
4 radii), PALM["n_palm"], CMC (thumb base in hand coords), CLEAR.
"""
import math
from mathutils import Vector, Matrix, Quaternion

# ---------------------------------------------------------------- 4. grip solver
BML = {b.name: b.matrix_local.copy() for b in rig.data.bones}
BLEN = {b.name: b.length for b in rig.data.bones}
# hand bone frame -> (t, u, n) frame, fixed: tun_world = hand_bone_world @ HB2TUN
HB2TUN = {}
for side in SIDES:
    Hm = BML[side + "Hand"]
    T4 = HAND_INFO[side]["R"].to_4x4(); T4.translation = HAND_INFO[side]["W"]
    HB2TUN[side] = Hm.inverted() @ T4
RELS = {}
for side in SIDES:
    for digit in DIGITS:
        par = side + "Hand"
        for k in range(3):
            nm = f"{side}Hand{digit}{k + 1}"
            RELS[nm] = BML[par].inverted() @ BML[nm]
            par = nm
CHAIN_RAD = {side: {d: chain_points(side, d)[1] for d in DIGITS} for side in SIDES}

def hand_bone_from_tun(side, F):
    """The Hand bone's world matrix that puts the (t, u, n) frame at F."""
    return F @ HB2TUN[side].inverted()

def basis_q(fl, sw=0.0, tw=0.0):
    """Bone-local turn: fold fl about X, swing sw about Z, twist tw about Y (degrees)."""
    q = Quaternion((1, 0, 0), math.radians(fl))
    if sw: q = Quaternion((0, 0, 1), math.radians(sw)) @ q
    if tw: q = Quaternion((0, 1, 0), math.radians(tw)) @ q
    return q

def digit_fk(side, digit, Mh, ang):
    """World matrices of a digit's 3 bones for angles [(fl, sw, tw), ...] under hand bone matrix Mh."""
    out, M = [], Mh
    for k in range(3):
        nm = f"{side}Hand{digit}{k + 1}"
        a = ang[k]
        M = M @ RELS[nm] @ basis_q(*a).to_matrix().to_4x4()
        out.append(M)
    return out

def seg_samples(side, digit, Ms, k, n=4):
    """Sample points (centre, radius) along segment k of a posed digit."""
    nm = f"{side}Hand{digit}{k + 1}"
    rad = CHAIN_RAD[side][digit]
    A = Ms[k].translation; y = Ms[k].col[1].to_3d().normalized(); L = BLEN[nm]
    pts = []
    for i in range(n):
        w = i / (n - 1)
        pts.append((A + y * (L * w), rad[k] + (rad[k + 1] - rad[k]) * w))
    if k == 2:
        pts.append((A + y * (L + rad[3] * 0.6), rad[3] * 0.7))
    return pts

class Obstacles:
    """The gun (a BVH of its surface) plus capsules (the other hand)."""
    REACH = 0.02          # only contacts closer than this matter
    def __init__(self, bvh, caps=()):
        self.bvh = bvh
        self.caps = [(A, B, ra, rb, (A + B) / 2, (B - A).length / 2 + max(ra, rb)) for A, B, ra, rb in caps]
    def gap(self, p, r):
        """Clearance of a ball (p, r): negative = touching or inside."""
        best = self.REACH
        if self.bvh is not None:
            hit = self.bvh.find_nearest(p, r + self.REACH)
            if hit[0] is not None:
                d = hit[3]
                inside = (p - hit[0]).dot(hit[1]) < 0
                best = min(best, (-d if inside else d) - r)
        for A, B, ra, rb, mid, brad in self.caps:
            if (p - mid).length - brad - r > best: continue
            ab = B - A; L2 = ab.length_squared
            w = 0.0 if L2 < 1e-12 else max(0.0, min(1.0, (p - A).dot(ab) / L2))
            best = min(best, (p - (A + ab * w)).length - (ra + (rb - ra) * w) - r)
        return best

def seg_gap(obs, side, digit, Ms, k):
    return min(obs.gap(p, r) for p, r in seg_samples(side, digit, Ms, k))

def close_digit(obs, side, digit, Mh, base, lim, step0=6.0, weights=(1.0, 1.15, 0.85)):
    """Close a digit like a grasp: every free joint folds a step at a time (weighted); on contact the step
    halves until it is under a degree, then the touching segment and the joints before it stop and the
    rest carry on at the full step. base: start angles [(fl, sw, tw)]; lim: max fold each."""
    ang = [list(a) for a in base]
    free = [ang[k][0] < lim[k] for k in range(3)]
    step = step0
    for _ in range(200):
        if not any(free): break
        trial = [list(a) for a in ang]
        for k in range(3):
            if free[k]: trial[k][0] = min(lim[k], trial[k][0] + step * weights[k])
        Ms = digit_fk(side, digit, Mh, trial)
        hit = None
        for k in range(3):
            if seg_gap(obs, side, digit, Ms, k) < CLEAR:
                hit = k; break
        if hit is None:
            ang = trial
            for k in range(3):
                if free[k] and ang[k][0] >= lim[k] - 1e-6: free[k] = False
            continue
        if step > 0.8:
            step *= 0.5; continue
        for k in range(hit + 1): free[k] = False
        step = step0
    return [tuple(a) for a in ang]

def reach_digit(obs, side, digit, Mh, target, grid, fixed=None, pad=0.6, w_pen=60.0, w_rest=0.0, rest=None):
    """Put the digit's fingertip pad on `target`: search fold/swing angles (grid: lists per variable,
    vars = [fl1, sw1, fl2, fl3]; fl3 may be None to follow fl2 * 0.75), then refine. Penalises
    sinking into the obstacles."""
    def angles(v):
        f1, s1, f2, f3 = v
        if f3 is None: f3 = f2 * 0.75
        return [(f1, s1, 0.0), (f2, 0.0, 0.0), (f3, 0.0, 0.0)]
    def pad_point(Ms):
        nm = f"{side}Hand{digit}3"
        r = CHAIN_RAD[side][digit][3]
        y = Ms[2].col[1].to_3d().normalized(); z = Ms[2].col[2].to_3d().normalized()
        return Ms[2].translation + y * (BLEN[nm] * pad) + z * r
    def cost(v):
        Ms = digit_fk(side, digit, Mh, angles(v))
        c = (pad_point(Ms) - target).length ** 2 * 1e4
        for k in range(3):
            g = seg_gap(obs, side, digit, Ms, k) + (0.003 if k == 2 else 0.0)   # the pad may press in a little
            if g < 0: c += w_pen * (g * 1e3) ** 2 * 0.01
        if rest is not None: c += w_rest * sum((a - b) ** 2 for a, b in zip(v, rest) if a is not None and b is not None) * 1e-4
        return c
    best = None
    import itertools
    for v in itertools.product(*grid):
        v = list(v)
        if v[3] is None and len(grid[3]) == 1: pass
        c = cost(v)
        if best is None or c < best[0]: best = (c, v)
    c0, v = best
    step = 4.0
    while step > 0.4:
        improved = False
        for i in range(4):
            if v[i] is None: continue
            for s in (-step, step):
                w = list(v); w[i] = w[i] + s
                c = cost(w)
                if c < c0: c0, v, improved = c, w, True
        if not improved: step *= 0.5
    Ms = digit_fk(side, digit, Mh, angles(v))
    return angles(v), (pad_point(Ms) - target).length, [round(seg_gap(obs, side, digit, Ms, k) * 1000, 1) for k in range(3)]

def palm_points(side, F):
    """Balls covering the palm surface and back, for contact sliding (world)."""
    pts = []
    for t in (-0.028, -0.012, 0.004, 0.020, 0.032):
        for u in (0.015, 0.040, 0.065, 0.088):
            pts.append((F @ Vector((t * (0.82 + 0.18 * u / 0.1), u, PALM["n_palm"] - 0.008)), 0.008))
    pts.append((F @ Vector((CMC[0] + 0.012, CMC[1] + 0.022, CMC[2] + 0.004)), 0.012))   # thenar
    return pts

def slide_to_contact(obs, side, F, d, max_go=0.08, step=0.001, sink=0.0):
    """Move frame F along world direction d until its palm touches (or pull it back out if it starts
    inside). Returns the moved frame."""
    def g(Fm): return min(obs.gap(p, r) for p, r in palm_points(side, Fm))
    def moved(s):
        M = F.copy(); M.translation = F.translation + d * s; return M
    s = 0.0
    if g(F) < -sink:
        while g(moved(s)) < -sink and s > -max_go: s -= step
        return moved(s)
    while s < max_go and g(moved(s + step)) > CLEAR - sink: s += step
    return moved(s)

def hand_capsules(side, Mh, digit_angles):
    """Capsules of a posed hand (fingers, thumb, and a few for the palm) as obstacles for the other hand."""
    caps = []
    for digit, ang in digit_angles.items():
        Ms = digit_fk(side, digit, Mh, ang)
        rad = CHAIN_RAD[side][digit]
        for k in range(3):
            nm = f"{side}Hand{digit}{k + 1}"
            A = Ms[k].translation; y = Ms[k].col[1].to_3d().normalized()
            caps.append((A.copy(), A + y * BLEN[nm], rad[k], rad[k + 1]))
    F = Mh @ HB2TUN[side]
    for t in (-0.022, 0.0, 0.022):
        caps.append((F @ Vector((t, 0.01, 0.002)), F @ Vector((t, 0.09, 0.002)), 0.013, 0.013))
    return caps

def relaxed(digit):
    if digit == "Thumb": return [(10.0, 0.0, 0.0), (15.0, 0.0, 0.0), (12.0, 0.0, 0.0)]
    return [(22.0, 0.0, 0.0), (30.0, 0.0, 0.0), (18.0, 0.0, 0.0)]
