"""The survivor holding the pistol: arms, hands and fingers posed on the body's own rig.

Run:  blender -b Blender/Gun_Pistol.blend -P Blender/grip_pistol.py -- [sheet_dir]

Steps:
  1. swaps the old body in Gun_Pistol.blend for the finger-rigged survivor;
  2. puts the pistol out in front at aim height (the gun rig's Root, gun space: muzzle -Y, top +Z);
  3. right hand: turned so the palm presses the grip's right panel, the web high under the
     beavertail and the knuckles across the front strap; slid in until the palm touches the grip;
  4. left hand: palm on the left panel over the right hand's fingers, wrist angled down;
  5. fingers and thumbs close joint by joint until they touch the gun or the other hand
     (measured on the skinned mesh), so nothing sinks in or floats;
  6. arms reach the wrists by two-bone IK, shoulders forward a little.
The hand placements are kept in Gun-bone space, so the clips can carry them every frame.
"""
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(bpy.data.filepath)) if bpy.data.filepath else os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
SURV = os.path.join(PROJECT, "Assets", "Models", "2026-11-14_ShadowOperative_Final", "ShadowOperative.fbx")   # the player body
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SHEETS = args[0] if args else None
TUNE = {}
for a in args[1:]:
    k, v = a.split("=")
    TUNE[k] = float(v)


def log(*a):
    print("ZR_GRIP", *a)


def T(k, d):
    return TUNE.get(k, d)


# ------------------------------------------------------------------ body: the finger-rigged survivor
old = bpy.data.objects.get("Player_Rig")
if old:
    for o in [o for o in bpy.data.objects if o.parent == old] + [old]:
        bpy.data.objects.remove(o)
for a in [a for a in bpy.data.actions if a.name.startswith("Body_") or "|" in a.name]:
    bpy.data.actions.remove(a)
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=SURV)
new = [o for o in bpy.data.objects if o not in before]
body = next(o for o in new if o.type == "ARMATURE")
# the body: the biggest mesh that is not the backpack (the bag is a separate rigid mesh)
mesh = max((o for o in new if o.type == "MESH" and "Backpack" not in o.name), key=lambda o: len(o.data.vertices))
body.name = "Player_Rig"
mesh.name = "Player_Body"
for a in [a for a in bpy.data.actions if "|" in a.name]:
    bpy.data.actions.remove(a)
body.animation_data_create()
body.animation_data.action = None
for pb in body.pose.bones:
    pb.rotation_mode = "QUATERNION"
    pb.rotation_quaternion = Quaternion()
    pb.location = (0, 0, 0)

BONES = [b.name for b in body.data.bones]
ML = {b.name: b.matrix_local.copy() for b in body.data.bones}
PAR = {b.name: (b.parent.name if b.parent else None) for b in body.data.bones}
HEAD = {b.name: b.head_local.copy() for b in body.data.bones}
TAIL = {b.name: b.tail_local.copy() for b in body.data.bones}


class Pose:
    def __init__(self):
        self.q = {n: Quaternion() for n in BONES}

    def fk(self):
        M = {}
        for n in BONES:
            basis = self.q[n].to_matrix().to_4x4()
            p = PAR[n]
            M[n] = ML[n] @ basis if p is None else M[p] @ (ML[p].inverted() @ ML[n]) @ basis
        return M

    def world_rot(self, n, R):
        """Give bone n this armature-space rotation (3x3 of its bone matrix)."""
        M = self.fk()
        p = PAR[n]
        base = (M[p] @ (ML[p].inverted() @ ML[n])).to_3x3()
        self.q[n] = (base.inverted() @ R).to_quaternion()

    def turn_world(self, n, axis, deg):
        """Turn bone n about an armature-space axis through its head, on top of its pose."""
        M = self.fk()
        R = Quaternion(Vector(axis).normalized(), math.radians(deg)).to_matrix() @ M[n].to_3x3()
        self.world_rot(n, R)

    def aim(self, n, d):
        M = self.fk()
        p = PAR[n]
        base = (M[p] @ (ML[p].inverted() @ ML[n])).to_3x3()
        y0 = (base @ Vector((0, 1, 0))).normalized()
        S = y0.rotation_difference(Vector(d).normalized()).to_matrix()
        self.world_rot(n, S @ base)

    def two_bone(self, upper, lower, end, target, pole):
        M = self.fk()
        sh = M[upper].translation
        l1 = (HEAD[lower] - HEAD[upper]).length
        l2 = (HEAD[end] - HEAD[lower]).length
        v = Vector(target) - sh
        d = max(abs(l1 - l2) + 1e-3, min(l1 + l2 - 1e-3, v.length))
        u = v.normalized()
        a = (l1 * l1 - l2 * l2 + d * d) / (2 * d)
        h = math.sqrt(max(l1 * l1 - a * a, 0.0))
        pole = Vector(pole)
        perp = (pole - u * pole.dot(u)).normalized()
        elbow = sh + u * a + perp * h
        self.aim(upper, elbow - sh)
        self.aim(lower, sh + u * d - elbow)
        return v.length - d     # >0: the target is out of reach by this much


# ------------------------------------------------------------------ skinning (numpy) for contact tests
gi = {g.index: g.name for g in mesh.vertex_groups}
NV = len(mesh.data.vertices)
REST_CO = np.array([v.co for v in mesh.data.vertices], np.float64)
W = {}                                   # bone -> (vertex ids, weights)
for v in mesh.data.vertices:
    for g in v.groups:
        n = gi.get(g.group)
        if n in ML and g.weight > 0.001:
            W.setdefault(n, ([], []))
            W[n][0].append(v.index)
            W[n][1].append(g.weight)
W = {n: (np.array(i), np.array(w)) for n, (i, w) in W.items()}


def owned(names, thr=0.5):
    s = set()
    for n in names:
        if n in W:
            i, w = W[n]
            s |= set(i[w > thr].tolist())
    return np.array(sorted(s), int)


WB = list(W.keys())
WD = np.zeros((NV, len(WB)))
for b, n in enumerate(WB):
    WD[W[n][0], b] = W[n][1]
WD /= np.maximum(WD.sum(1, keepdims=True), 1e-9)
MLI = {n: ML[n].inverted() for n in WB}


def skin(P, ids):
    """Posed positions of these vertices (linear blend skinning, as the armature does it)."""
    M = P.fk()
    A = np.array([np.array(M[n] @ MLI[n]) for n in WB])          # B x 4 x 4
    co = np.c_[REST_CO[ids], np.ones(len(ids))]                  # k x 4
    X = np.einsum("bij,kj->kbi", A[:, :3, :], co)                # k x B x 3
    return np.einsum("kb,kbi->ki", WD[ids], X)


# ------------------------------------------------------------------ the gun at aim height in front of the right eye
gun_rig = bpy.data.objects["Pistol_Rig"]
gun_rig.animation_data.action = None
for pb in gun_rig.pose.bones:
    pb.rotation_mode = "QUATERNION"
    pb.rotation_quaternion = Quaternion()
    pb.location = (0, 0, 0)
EYE = Vector((HEAD["Head"].x - 0.032, HEAD["Head"].y - 0.085, HEAD["Head"].z + 0.065))
# grip point: forward of the eye, below it, a little right of the nose (isosceles hold)
# shoulder height, centred: this body's arms are short, so a higher gun bends the wrists far past natural
# (first person lifts the arms into view in the game: WeaponData.fpLift)
GRIP = EYE + Vector((T("gx", -0.03), -T("gfwd", 0.33), -T("gdown", 0.28)))
gun_rig.location = GRIP
gun_rig.rotation_euler = (math.radians(T("gpitch", 0.0)), 0, 0)
# (the pistol itself is built 1.3x real size in gun_build.py: the survivor's hands are stylised big)
bpy.context.view_layer.update()
GM = gun_rig.matrix_world.copy()         # gun space -> world (Gun bone at rest = Root = rig object)
GR = GM.to_3x3().normalized()
gun_parts = [o for o in bpy.data.objects if o.type == "MESH" and o.parent == gun_rig]


def gun_bvh():
    dg = bpy.context.evaluated_depsgraph_get()
    verts, polys = [], []
    for o in gun_parts:
        ev = o.evaluated_get(dg)
        me = ev.to_mesh()
        base = len(verts)
        verts += [o.matrix_world @ v.co for v in me.vertices]
        polys += [[base + i for i in p.vertices] for p in me.polygons]
        ev.to_mesh_clear()
    return BVHTree.FromPolygons(verts, polys), np.array([tuple(v) for v in verts])


BVH, GUNV = gun_bvh()


def inside(p, bvh):
    """Odd number of surface crossings straight up = inside (the Meshy normals are not reliable)."""
    n, o, d = 0, Vector(p), Vector((0.0123, 0.0071, 1.0)).normalized()
    for _ in range(16):
        hit, nrm, idx, dist = bvh.ray_cast(o, d, 2.0)
        if hit is None:
            break
        n += 1
        o = hit + d * 1e-4
    return n % 2 == 1


def gap(points, bvh=BVH):
    """Smallest distance from these points to the surface (negative: inside)."""
    best = 1e9
    for p in points:
        loc, nrm, idx, d = bvh.find_nearest(Vector(p))
        if loc is None:
            continue
        if d < 0.02 and inside(p, bvh):
            d = -d
        best = min(best, d)
    return best


def G(right=0.0, up=0.0, fwd=0.0):
    """Gun-space direction (shooter's right, up, muzzle) in world."""
    return (GR @ Vector((-right, -fwd, up))).normalized()


def GP(right=0.0, up=0.0, fwd=0.0):
    return GM @ Vector((-right, -fwd, up))


# ------------------------------------------------------------------ hand frames from the rig
def hand_frame(side):
    """Rest axes of a hand: along (wrist -> finger root), palm (the way the fingers curl), and the
    curl axes of the finger block and the thumb."""
    # The hand mesh is flat: its plane (PCA of the hand, finger block and thumb) holds the fingers and
    # the thumb side by side, and the palm faces out of that plane. (The finger bones curve inside the
    # plane, so the bones alone do not tell which side the palm is.)
    h = side + "Hand"
    ids = owned([h] + [side + "Hand%s%d" % (f, i) for f in ("Index", "Middle", "Ring", "Pinky") for i in (1, 2, 3)] + [side + "HandThumb%d" % i for i in (1, 2, 3)], 0.3)
    V = REST_CO[ids]
    _, _, vt = np.linalg.svd(V - V.mean(0))
    nrm = Vector(vt[2].tolist())
    wrist = HEAD[h]
    fmid = (HEAD[side + "HandMiddle1"] + TAIL[side + "HandMiddle3"]) * 0.5
    along = fmid - wrist
    along = (along - nrm * along.dot(nrm)).normalized()
    tv = TAIL[side + "HandThumb3"] - wrist
    thumb = (tv - along * tv.dot(along) - nrm * tv.dot(nrm)).normalized()
    # a right hand has thumb = along x palm, a left hand thumb = palm x along
    palm = thumb.cross(along) if side == "Right" else along.cross(thumb)
    curl = along.cross(palm).normalized()            # fingers bend about this: along turns towards the palm
    tdir = (TAIL[side + "HandThumb3"] - HEAD[side + "HandThumb1"]).normalized()
    tcurl = tdir.cross((palm * 0.8 - thumb * 0.5).normalized()).normalized()   # thumb folds across the palm
    return dict(along=along, palm=palm.normalized(), curl=curl, tcurl=tcurl, thumb=thumb,
                rest=ML[h].to_3x3(), wrist_to_palm=(HEAD[side + "HandIndex1"] - wrist).length)


HF = {s: hand_frame(s) for s in ("Left", "Right")}
for s, f in HF.items():
    log(s, "thumb = along x palm" if f["thumb"].dot(f["along"].cross(f["palm"])) > 0 else "thumb = palm x along", "along", tuple(round(x, 2) for x in f["along"]), "palm", tuple(round(x, 2) for x in f["palm"]),
        "thumb side", tuple(round(x, 2) for x in f["thumb"]), "hand length", round(f["wrist_to_palm"], 3))


def basis(a, n):
    a = a.normalized()
    n = (n - a * n.dot(a)).normalized()
    return Matrix((a, n, a.cross(n))).transposed()


def hand_rot(side, along, palm):
    """World rotation of the hand bone whose along/palm axes point this way."""
    f = HF[side]
    rest_palm = Quaternion(f["along"], math.radians(T(side[0].lower() + "_roll", 0.0))) @ f["palm"]
    R = basis(along, palm) @ basis(f["along"], rest_palm).transposed()
    return R @ f["rest"]


# ------------------------------------------------------------------ the pose
# the middle finger is solved against the gun (the centre of the hand, where the old one-piece finger block was);
# curl() gives the index, ring and little fingers the same bends (same bone axes, so the same local turn)
FING = {s: [s + "HandMiddle1", s + "HandMiddle2", s + "HandMiddle3"] for s in ("Left", "Right")}
OTHER_FINGERS = {s: [[s + "Hand" + f + str(k) for k in (1, 2, 3)] for f in ("Index", "Ring", "Pinky")] for s in ("Left", "Right")}
ALL_FINGER_BONES = {s: FING[s] + sum(OTHER_FINGERS[s], []) for s in ("Left", "Right")}
THUMB = {s: [s + "HandThumb1", s + "HandThumb2", s + "HandThumb3"] for s in ("Left", "Right")}


def curl(P, side, bones, axis, degs):
    chains = [bones] + (OTHER_FINGERS[side] if bones == FING[side] else [])
    for ch in chains:
        for b, d in zip(ch, degs):
            local = (ML[b].to_3x3().inverted() @ axis).normalized()
            P.q[b] = Quaternion(local, math.radians(d))


def wrap(P, side, bones, axis, targets, ratio, limit, step=2.0, touch=0.003):
    """Close a chain the way a hand closes: all joints together in a fixed ratio, until any part of
    it touches a surface (then each joint past the touching one keeps closing on its own)."""
    ids = [owned(bones[j:]) for j in range(len(bones))]
    k = 0.0
    while k < limit:
        k += step
        degs = [k * r for r in ratio]
        curl(P, side, bones, axis, degs)
        if min(gap(skin(P, ids[0]), b) for b in targets) < touch:
            k -= step
            break
    degs = [k * r for r in ratio]
    curl(P, side, bones, axis, degs)
    # the tip segments close on further until they touch too (fingertips round the far side)
    for j in range(1, len(bones)):
        while degs[j] < k * ratio[j] + 45:
            degs[j] += step
            curl(P, side, bones, axis, degs)
            if min(gap(skin(P, ids[j]), b) for b in targets) < touch:
                degs[j] -= step
                curl(P, side, bones, axis, degs)
                break
    return degs


def close(P, side, bones, axis, targets, limit, step=4.0, touch=0.003, start=None):
    """Close a chain joint by joint until the part past that joint touches a surface."""
    degs = list(start or [0.0] * len(bones))
    for j in range(len(bones)):
        ids = owned(bones[j:])
        while degs[j] < limit[j]:
            degs[j] += step
            curl(P, side, bones, axis, degs)
            pts = skin(P, ids)
            if min(gap(pts, b) for b in targets) < touch:
                degs[j] -= step * 0.5
                curl(P, side, bones, axis, degs)
                break
    return degs


def place_hand(P, side, along, palm, anchor, pole, targets, approach=0.05, touch=0.003, palm_ids=None):
    """Turn the hand, start it `approach` back from `anchor` along -palm, IK the arm, then slide it
    in along the palm until the palm touches."""
    R = hand_rot(side, along, palm)
    ids = palm_ids if palm_ids is not None else owned([side + "Hand"], 0.6)
    pdir = palm.normalized()
    best = None
    for k in range(0, 41):
        s = approach - k * 0.0025
        wrist = anchor - pdir * s
        P.two_bone(side + "UpperArm", side + "LowerArm", side + "Hand", wrist, pole)
        P.world_rot(side + "Hand", R)
        g = min(gap(skin(P, ids), b) for b in targets)
        if g < touch:
            break
        best = s
    s = best if best is not None else approach
    wrist = anchor - pdir * s
    miss = P.two_bone(side + "UpperArm", side + "LowerArm", side + "Hand", wrist, pole)
    P.world_rot(side + "Hand", R)
    return wrist, miss


P = Pose()
# shoulders forward and slightly down: a pistol shooter's arms reach out
for side, sgn in (("Right", 1), ("Left", -1)):
    P.turn_world(side + "Shoulder", (0, 0, 1), sgn * T("protract", 14.0))
    P.turn_world(side + "Shoulder", (0, 1, 0), sgn * T("drop", -4.0))

# right hand: palm on the right panel (palm faces the gun's left), along = forward-down across the grip,
# web high under the beavertail. anchor = where the palm centre sits, the slide-in finds the panel.
r_along = G(0, -T("r_down", 0.22), T("r_fwd", 0.91))
r_palm = G(-1.0, 0, 0)
r_anchor = GP(0.0, T("r_up", 0.0), T("r_fwd0", 0.0156))
r_wrist, r_miss = place_hand(P, "Right", r_along, r_palm, r_anchor - r_along * HF["Right"]["wrist_to_palm"] * 0.55,
                             pole=(-0.8, 0.2, -1.0), targets=[BVH])
log("right wrist", tuple(round(x, 3) for x in r_wrist), "reach short by", round(r_miss, 3))
# right fingers wrap the front strap, thumb along the left side
# fingers wrap the front strap together (base, middle, tip in the ratio a closing hand uses)
r_f = wrap(P, "Right", FING["Right"], HF["Right"]["curl"], [BVH], ratio=(1.0, 0.85, 0.6), limit=95)
# thumb: relaxed along the left side of the frame, a light bend at each joint until it rests on the gun
r_t = wrap(P, "Right", THUMB["Right"], HF["Right"]["tcurl"], [BVH], ratio=(0.5, 1.0, 0.8), limit=T("r_thumb", 25))
log("right fingers", [round(d) for d in r_f], "thumb", [round(d) for d in r_t])


def gun_dir(v):
    g = GR.inverted() @ v
    return f"right {-g.x:+.2f} up {g.z:+.2f} fwd {-g.y:+.2f}"


for s_ in ("Right", "Left"):
    pass
M_ = P.fk()
th_ = M_["RightHandThumb3"].translation - M_["RightHandThumb1"].translation
log("right thumb runs", gun_dir(th_.normalized()))

# left hand: closes over the right fingers; needs the right hand's skin as a surface
rh_ids = owned(["RightHand"] + ALL_FINGER_BONES["Right"] + THUMB["Right"], 0.3)


def right_hand_bvh():
    pts = skin(P, rh_ids)
    return BVHTree.FromPoints if False else BVHTree.FromPolygons(*hand_mesh(pts))


# faces of the right hand region for its BVH
_poly = [list(p.vertices) for p in mesh.data.polygons]
_idmap = {v: k for k, v in enumerate(rh_ids)}
RH_POLYS = [[_idmap[i] for i in p] for p in _poly if all(i in _idmap for i in p)]


def hand_mesh(pts):
    return [Vector(p) for p in pts], RH_POLYS


RBVH = BVHTree.FromPolygons(*hand_mesh(skin(P, rh_ids)))
l_along = G(T("l_in", 0.25), -T("l_down", 0.70), T("l_fwd", 0.66))
l_palm = G(T("l_palm_r", 0.92), T("l_palm_up", 0.25), 0)
l_anchor = GP(T("l_x", -0.039), T("l_up", -0.035), T("l_back", 0.0))   # l_up -0.035: the Shadow Operative's bigger hand wraps over the right fingers (-0.065 for the Survivor)
l_wrist, l_miss = place_hand(P, "Left", l_along, l_palm, l_anchor - l_along * HF["Left"]["wrist_to_palm"] * 0.55,
                             pole=(0.8, 0.2, -1.0), targets=[BVH, RBVH], approach=0.07)
log("left wrist", tuple(round(x, 3) for x in l_wrist), "reach short by", round(l_miss, 3))
l_f = wrap(P, "Left", FING["Left"], HF["Left"]["curl"], [BVH, RBVH], ratio=(1.0, 0.85, 0.6), limit=95)
l_t = wrap(P, "Left", THUMB["Left"], HF["Left"]["tcurl"], [BVH, RBVH], ratio=(0.5, 1.0, 0.8), limit=T("l_thumb", 25))
log("left fingers", [round(d) for d in l_f], "thumb", [round(d) for d in l_t])


def wrist_bend(side):
    """Angle between the forearm and the hand (0 = straight wrist)."""
    M = P.fk()
    fore = (M[side + "Hand"].translation - M[side + "LowerArm"].translation).normalized()
    hand = (M[side + "Hand"].to_3x3() @ (ML[side + "Hand"].to_3x3().inverted() @ HF[side]["along"])).normalized()
    return math.degrees(fore.angle(hand))


log("wrist bend: right", round(wrist_bend("Right")), "deg, left", round(wrist_bend("Left")), "deg")

# how deep anything sinks into the gun
hand_ids = owned([s + "Hand" for s in ("Left", "Right")] + ALL_FINGER_BONES["Left"] + ALL_FINGER_BONES["Right"] + THUMB["Left"] + THUMB["Right"], 0.3)
log("deepest point inside the gun (m)", round(-min(0.0, gap(skin(P, hand_ids))), 4))

for n in BONES:
    pb = body.pose.bones[n]
    pb.rotation_quaternion = P.q[n]
bpy.context.view_layer.update()

# ------------------------------------------------------------------ body clips: the hold carried through the gun's clips
# Right hand rides the Gun bone (kick, reload tilt, draw); left hand follows the HandL bone's motion
# from its rest (pouch, magazine, slap, rack) with finger shapes for each moment. Only the arms,
# hands and fingers are keyed: the game's spine aim keeps working underneath.
HOLD = P
ARM_BONES = [s + n for s in ("Left", "Right") for n in ("Shoulder", "UpperArm", "LowerArm", "Hand", "HandThumb1", "HandThumb2", "HandThumb3")] \
    + ALL_FINGER_BONES["Left"] + ALL_FINGER_BONES["Right"]
LF_HOLD, LT_HOLD = list(l_f), list(l_t)
RF_HOLD, RT_HOLD = list(r_f), list(r_t)
OPEN, GRAB, FLAT, PINCH = [8, 6, 4], [55, 65, 45], [2, 2, 2], [35, 70, 50]


def blend_curls(t, keys):
    """Finger degrees at t from (time, degrees) keys, eased between them."""
    for (t0, a), (t1, b) in zip(keys, keys[1:]):
        if t0 <= t <= t1:
            w = 0.0 if t1 == t0 else (t - t0) / (t1 - t0)
            w = w * w * (3 - 2 * w)
            return [x + (y - x) * w for x, y in zip(a, b)]
    return list(keys[-1][1])


LEFT_FINGERS = {
    "Idle": [(0, LF_HOLD), (1, LF_HOLD)],
    "Fire": [(0, LF_HOLD), (1, LF_HOLD)],
    # lets go (0.10-0.20), open to the pouch, grabs the magazine (0.36-0.42), carries it, flat slap (0.75),
    # claw over the slide for the rack (0.84-0.90), back on the grip
    "Reload": [(0, LF_HOLD), (0.10, LF_HOLD), (0.20, OPEN), (0.34, OPEN), (0.41, GRAB), (0.70, GRAB), (0.735, FLAT), (0.78, FLAT),
               (0.83, OPEN), (0.86, PINCH), (0.90, PINCH), (0.97, LF_HOLD), (1, LF_HOLD)],
    "Draw": [(0, OPEN), (0.4, OPEN), (0.85, LF_HOLD), (1, LF_HOLD)],
}
LEFT_THUMB = {
    "Reload": [(0, LT_HOLD), (0.12, LT_HOLD), (0.22, [10, 10, 5]), (0.36, [10, 10, 5]), (0.42, [30, 30, 20]), (0.70, [30, 30, 20]),
               (0.74, [2, 2, 2]), (0.80, [2, 2, 2]), (0.86, [30, 40, 30]), (0.90, [30, 40, 30]), (0.97, LT_HOLD), (1, LT_HOLD)],
}


def bone_world(rig, name):
    return rig.matrix_world @ rig.pose.bones[name].matrix


M0 = HOLD.fk()
R_REST, L_REST = M0["RightHand"].copy(), M0["LeftHand"].copy()
gun_rig.animation_data_create()
gun_rig.animation_data.action = None
bpy.context.view_layer.update()
GUN_REST_W = bone_world(gun_rig, "Gun")
HANDL_REST_W = bone_world(gun_rig, "HandL")


def body_pose_at(clip, t):
    Q = Pose()
    Q.q = dict(HOLD.q)
    gun_now = bone_world(gun_rig, "Gun")
    hl_now = bone_world(gun_rig, "HandL")
    rt = (gun_now @ GUN_REST_W.inverted()) @ R_REST
    lt = (hl_now @ HANDL_REST_W.inverted()) @ L_REST
    Q.two_bone("RightUpperArm", "RightLowerArm", "RightHand", rt.translation, (-0.8, 0.2, -1.0))
    Q.world_rot("RightHand", rt.to_3x3().normalized())
    Q.two_bone("LeftUpperArm", "LeftLowerArm", "LeftHand", lt.translation, (0.8, 0.2, -1.0))
    Q.world_rot("LeftHand", lt.to_3x3().normalized())
    curl(Q, "Right", FING["Right"], HF["Right"]["curl"], RF_HOLD)
    curl(Q, "Right", THUMB["Right"], HF["Right"]["tcurl"], RT_HOLD)
    curl(Q, "Left", FING["Left"], HF["Left"]["curl"], blend_curls(t, LEFT_FINGERS[clip]))
    curl(Q, "Left", THUMB["Left"], HF["Left"]["tcurl"], blend_curls(t, LEFT_THUMB.get(clip, [(0, LT_HOLD), (1, LT_HOLD)])))
    return Q


GUN_CLIPS = [("Idle", 60, True), ("Fire", 8, False), ("Reload", 42, False), ("Draw", 12, False)]
sc0 = bpy.context.scene
sc0.render.fps = 30
body_actions = []
if T("clips", 0):
    for clip, frames, loop in GUN_CLIPS:
        ga = bpy.data.actions[clip]
        gun_rig.animation_data.action = ga
        name = "Pistol_" + clip
        old_a = bpy.data.actions.get(name)
        if old_a:
            bpy.data.actions.remove(old_a)
        act = bpy.data.actions.new(name)
        act.use_fake_user = True
        body.animation_data.action = act
        for f in range(1, frames + 2):
            sc0.frame_set(f)
            Q = body_pose_at(clip, (f - 1) / frames)
            for n in ARM_BONES:
                pb = body.pose.bones[n]
                pb.rotation_quaternion = Q.q[n]
                pb.keyframe_insert("rotation_quaternion", frame=f, group=n)
        act.use_frame_range = True
        act.frame_start, act.frame_end = 1, frames + 1
        act.use_cyclic = loop
        body_actions.append((act, ga, frames))
        log("clip", name, frames + 1, "frames")
    # the body at rest: the game measures chest/neck/head turns from it
    old_a = bpy.data.actions.get("Pistol_Rest")
    if old_a:
        bpy.data.actions.remove(old_a)
    act = bpy.data.actions.new("Pistol_Rest")
    act.use_fake_user = True
    body.animation_data.action = act
    for f in (1, 2):
        for n in BONES:
            pb = body.pose.bones[n]
            pb.rotation_quaternion = Quaternion()
            pb.keyframe_insert("rotation_quaternion", frame=f, group=n)
    # back to the hold, no action, for the socket and the pictures
    body.animation_data.action = None
    gun_rig.animation_data.action = None
    for n in BONES:
        body.pose.bones[n].rotation_quaternion = HOLD.q[n]
    sc0.frame_set(1)
    bpy.context.view_layer.update()

# ------------------------------------------------------------------ cameras and pictures
sc = bpy.context.scene
for o in [o for o in bpy.data.objects if o.type == "CAMERA"]:
    bpy.data.objects.remove(o)


def camera(name, loc, look, ortho=None, lens=35):
    c = bpy.data.objects.new(name, bpy.data.cameras.new(name))
    sc.collection.objects.link(c)
    c.location = loc
    d = (Vector(look) - Vector(loc)).normalized()
    c.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    if ortho:
        c.data.type = "ORTHO"
        c.data.ortho_scale = ortho
    else:
        c.data.lens = lens
    c.data.clip_start = 0.01
    return c


mid = GP(0, -0.02, 0.02)
cams = [
    camera("Eye", EYE + Vector((0, -0.05, 0)), EYE + Vector((0, -1, -0.25)), lens=T("eyelens", 18)),
    camera("Left", mid + G(1, 0, 0) * 0.6, mid, ortho=0.32),
    camera("Right", mid + G(-1, 0, 0) * 0.6, mid, ortho=0.32),
    camera("Below", mid + G(0.3, -1, 0.5) * 0.6, mid, ortho=0.34),
    camera("Front", mid + G(0.25, 0.15, 1) * 0.6, mid, ortho=0.34),
    camera("Side", Vector((-2.2, GRIP.y + 0.22, GRIP.z - 0.12)), Vector((0, GRIP.y + 0.22, GRIP.z - 0.12)), ortho=1.05),
]
sc.camera = cams[0]
if not any(o.type == "LIGHT" for o in bpy.data.objects):
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sc.collection.objects.link(sun)
    sun.rotation_euler = (0.7, 0.3, 0.5)

# the first-person camera: Head + (up, forward) as in the game (WeaponData.eyeOffset for this gun)
FP_EYE = HEAD["Head"] + Vector((-T("fp_x", 0.0), -T("fp_fwd", 0.09), T("fp_up", 0.09)))
fp = camera("FP", FP_EYE, FP_EYE + Vector((0, -1, 0)))
fp.data.sensor_fit = "VERTICAL"
fp.data.angle = math.radians(60)                           # the game's field of view
fp.data.clip_start = 0.12 + max(0.0, -T("fp_fwd", 0.09))  # the game hides the head in first person
tp = camera("TP", Vector((-0.65, 1.9, 1.9)), Vector((-0.65, -1.0, 1.9)))   # PlayerViewConfig.shoulderOffset from the pivot
tp.data.sensor_fit = "VERTICAL"
tp.data.angle = math.radians(60)

if SHEETS and T("look", 0):
    # plain colours, as the player would see it: first person and third person
    os.makedirs(SHEETS, exist_ok=True)
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "OBJECT"
    sc.world = sc.world or bpy.data.worlds.new("W")
    sc.world.color = (0.32, 0.36, 0.42)
    mesh.color = (0.55, 0.5, 0.44, 1)
    for o in gun_parts:
        o.color = (0.13, 0.13, 0.14, 1) if o.name != "Slide" else (0.22, 0.2, 0.19, 1)
    sc.render.resolution_x, sc.render.resolution_y = 1280, 720
    tpc = camera("TPclose", Vector((-0.5, 0.95, 1.62)), Vector((-0.2, -1.0, 1.35)))
    tpc.data.sensor_fit = "VERTICAL"
    tpc.data.angle = math.radians(50)
    front = camera("Front34", GRIP + Vector((-0.55, -0.75, 0.10)), GRIP + Vector((0.0, 0.25, -0.05)), lens=50)
    side = camera("SideClose", GRIP + Vector((-0.95, 0.25, 0.0)), GRIP + Vector((0.0, 0.25, 0.0)), lens=50)
    for c in (fp, tpc, front, side):
        sc.camera = c
        sc.render.filepath = os.path.join(SHEETS, f"look_{c.name}.png")
        bpy.ops.render.render(write_still=True)
    log("look renders in", SHEETS)
elif SHEETS:
    os.makedirs(SHEETS, exist_ok=True)
    # colours: skin grey, fingers orange, thumbs red, gun dark, magazine yellow
    me = mesh.data
    mats = {}
    for name, c in (("skin", (0.62, 0.58, 0.52)), ("thumb", (0.85, 0.2, 0.12)), ("fing", (0.95, 0.6, 0.15))):
        m = bpy.data.materials.new("zr_" + name)
        m.diffuse_color = (*c, 1)
        me.materials.append(m)
        mats[name] = len(me.materials) - 1
    th = set(owned(THUMB["Left"] + THUMB["Right"]).tolist())
    fi = set(owned(FING["Left"] + FING["Right"]).tolist())
    for p in me.polygons:
        vs = set(p.vertices)
        p.material_index = mats["thumb"] if vs <= th else mats["fing"] if vs <= fi else mats["skin"]
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "MATERIAL"
    for o in gun_parts:
        for s in o.material_slots:
            pass
        gm = bpy.data.materials.new("zr_gun_" + o.name)
        gm.diffuse_color = (0.9, 0.55, 0.1, 1) if o.name in ("Magazine", "MagBody") else (0.08, 0.09, 0.1, 1) if o.name == "Pistol" else (0.35, 0.1, 0.08, 1)
        o.data.materials.clear()
        o.data.materials.append(gm)
    sc.world = sc.world or bpy.data.worlds.new("W")
    sc.world.color = (0.42, 0.44, 0.48)
    RX, RY = 420, 340
    sc.render.resolution_x, sc.render.resolution_y = RX, RY
    tiles = []
    for c in cams:
        sc.camera = c
        sc.render.filepath = os.path.join(SHEETS, "_t.png")
        bpy.ops.render.render(write_still=True)
        im = bpy.data.images.load(sc.render.filepath)
        tiles.append(np.array(im.pixels[:], np.float32).reshape(RY, RX, 4))
        bpy.data.images.remove(im)
    rows = [np.concatenate(tiles[i:i + 3], axis=1) for i in (0, 3)]
    sheet = np.concatenate(rows[::-1], axis=0)       # Blender images are bottom-up: top row = Eye, Left, Right
    im = bpy.data.images.new("s", sheet.shape[1], sheet.shape[0], alpha=True)
    im.pixels.foreach_set(sheet.ravel())
    im.filepath_raw = os.path.join(SHEETS, "grip_pistol.png")
    im.file_format = "PNG"
    im.save()
    log("sheet", im.filepath_raw)

if T("clips", 0):
    if SHEETS:
        # strips per clip: first person (top) and the side (bottom), 8 frames each
        sc.render.engine = "BLENDER_WORKBENCH"
        sc.display.shading.light = "STUDIO"
        sc.display.shading.color_type = "OBJECT"
        sc.world = sc.world or bpy.data.worlds.new("W")
        sc.world.color = (0.32, 0.36, 0.42)
        mesh.color = (0.55, 0.5, 0.44, 1)
        for o in gun_parts:
            o.color = (0.9, 0.55, 0.1, 1) if o.name in ("Magazine", "MagBody") else (0.13, 0.13, 0.14, 1)
        RX, RY = 320, 240
        sc.render.resolution_x, sc.render.resolution_y = RX, RY
        side = camera("SideClip", Vector((-1.6, GRIP.y + 0.25, GRIP.z - 0.2)), Vector((0, GRIP.y + 0.25, GRIP.z - 0.2)), ortho=0.95)
        for act, ga, frames in body_actions:
            body.animation_data.action = act
            gun_rig.animation_data.action = ga
            rows = []
            for c in (fp, side):
                row = []
                for i in range(8):
                    sc.frame_set(1 + round(i * frames / 8))
                    sc.camera = c
                    sc.render.filepath = os.path.join(SHEETS, "_c.png")
                    bpy.ops.render.render(write_still=True)
                    im = bpy.data.images.load(sc.render.filepath)
                    row.append(np.array(im.pixels[:], np.float32).reshape(RY, RX, 4))
                    bpy.data.images.remove(im)
                rows.append(np.concatenate(row, axis=1))
            sheet = np.concatenate(rows[::-1], axis=0)
            im = bpy.data.images.new("s", sheet.shape[1], sheet.shape[0], alpha=True)
            im.pixels.foreach_set(sheet.ravel())
            im.filepath_raw = os.path.join(SHEETS, f"clip_{act.name}.png")
            im.file_format = "PNG"
            im.save()
            bpy.data.images.remove(im)
        log("clip sheets in", SHEETS)
        body.animation_data.action = None
        gun_rig.animation_data.action = None
    # The gun's own skeleton rides the right hand in the export: in the game, the gun's Gun bone goes
    # where this copy's Gun bone is (same exporter, same node layout, so no axis guessing).
    for n in BONES:
        body.pose.bones[n].rotation_quaternion = HOLD.q[n]
    for pb in gun_rig.pose.bones:
        pb.rotation_quaternion = Quaternion()
        pb.location = (0, 0, 0)
    bpy.context.view_layer.update()
    mw = gun_rig.matrix_world.copy()
    gun_rig.parent = body
    gun_rig.parent_type = "BONE"
    gun_rig.parent_bone = "RightHand"
    bpy.context.view_layer.update()
    gun_rig.matrix_world = mw
    bpy.context.view_layer.update()
    OUTF = os.path.join(PROJECT, "Assets", "Models", "2026-11-09_Guns", "Pistol", "Survivor_Pistol.fbx")
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    body.select_set(True)
    gun_rig.select_set(True)
    bpy.context.view_layer.objects.active = body
    body.name = "Armature.002"          # the survivor's armature node in the game
    gun_rig.animation_data.action = None
    # every action goes out as a take; the game uses the Pistol_* ones (the gun's own takes are ignored)
    bpy.ops.export_scene.fbx(filepath=OUTF, use_selection=True, object_types={"ARMATURE"}, add_leaf_bones=False,
                             apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y", bake_space_transform=False,
                             bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)
    body.name = "Player_Rig"
    log("exported", OUTF)
    # back to a gun standing in the world, so the clips play right in Blender (the hand follows the gun)
    gun_rig.parent = None
    gun_rig.matrix_world = mw
    body.animation_data.action = bpy.data.actions["Pistol_Reload"]
    gun_rig.animation_data.action = bpy.data.actions["Reload"]
    sc.frame_start, sc.frame_end = 1, 43
    sc.render.fps = 30
    sc.frame_set(1)

if T("save", 0):
    # a scene to look at in Blender: first-person camera active, gun and body in the hold
    sc.camera = fp
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "Grip_Pistol.blend"))
    log("saved", os.path.join(HERE, "Grip_Pistol.blend"))
