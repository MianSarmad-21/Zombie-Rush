"""First-person hands from the player body's own arms (Shadow Operative): rolled sleeves, bare forearms,
gauntlet gloves and five rigged 3-joint fingers, all kept as modelled and textured.

exec() this from fp_viewmodel_hands.py (--hands body, the default) after the arms are lengthened, with
`rig` (FP_Rig), `arms` (FP_Arms), `log`, `upd` and `ctx` in globals. Nothing is rebuilt or re-dressed: the
body's hand mesh, finger bones and weights stay. This only measures the hand for the grip solver:
  - the (t, u, n) hand frame (t toward the thumb, u along the forearm, n out of the palm), wrist at the origin;
  - each digit's joint chain from its bones, and segment radii from the skin around each bone;
  - the palm surface depth and extent (the palm contact balls and the palm capsules are sized to this hand);
then loads fp_grip_solver.py. The arms keep the body's material as their only slot.
"""
import math
import numpy as np
from mathutils import Vector, Matrix

SIDES = ("Right", "Left")
DIGITS = ("Thumb", "Index", "Middle", "Ring", "Pinky")
FINGERS4 = ("Index", "Middle", "Ring", "Pinky")
CLEAR = 0.0012              # contact gap kept between a finger and the gun (m)

B = rig.data.bones

# The body's hands are relaxed, bent ~30 deg toward the palm at rest. The holds (fp_viewmodel_hands.HOLDS) are made
# for a hand that continues the forearm in a straight line, so straighten the wrists first and make that the rest
# pose: otherwise every hold bends the wrist that much further and the gauntlet's cuff opens toward the eye.
def _straighten_wrists():
    mod = arms.modifiers.new("_wrist", "ARMATURE"); mod.object = rig
    for side in SIDES:
        hb = B[side + "Hand"]; W = hb.head_local.copy()
        fa = (W - B[side + "LowerArm"].head_local).normalized()
        kn = sum((B[f"{side}Hand{f}1"].head_local for f in ("Index", "Middle", "Ring", "Pinky")), Vector()) / 4
        q = (kn - W).normalized().rotation_difference(fa)
        log(f"{side} wrist straightened by {math.degrees(q.angle):.0f} deg")
        pb = rig.pose.bones[side + "Hand"]; pb.rotation_mode = "QUATERNION"
        pb.matrix = Matrix.Translation(W) @ q.to_matrix().to_4x4() @ Matrix.Translation(-W) @ hb.matrix_local
        upd()
    with ctx(arms): bpy.ops.object.modifier_apply(modifier="_wrist")
    for o in bpy.context.view_layer.objects: o.select_set(o is rig)
    bpy.context.view_layer.objects.active = rig
    with ctx(rig): bpy.ops.object.mode_set(mode="POSE")
    bpy.ops.pose.select_all(action="SELECT")
    bpy.ops.pose.armature_apply(selected=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    for pb in rig.pose.bones:
        pb.rotation_mode = "QUATERNION"; pb.rotation_quaternion = (1, 0, 0, 0); pb.location = (0, 0, 0); pb.scale = (1, 1, 1)
    upd()
_straighten_wrists()
B = rig.data.bones

# First-person wrists bend far (the holds ask ~60-100 deg). The body's gauntlet rides the hand bone (right for third
# person), which here swings the cuff's open end toward the eye and shows the forearm inside it. In first person the
# cuff stays on the forearm (like the built gloves' cuff) and the hand turns just past the wrist joint, inside it.
def _soft_cuffs():
    names = {vg.index: vg.name for vg in arms.vertex_groups}
    def sm(x):
        x = max(0.0, min(1.0, x)); return x * x * (3 - 2 * x)
    n = 0
    for side in SIDES:
        W = B[side + "Hand"].head_local; fa = (W - B[side + "LowerArm"].head_local).normalized()
        gh, gl = arms.vertex_groups[side + "Hand"], arms.vertex_groups[side + "LowerArm"]
        for v in arms.data.vertices:
            p = v.co
            if (p - W).length > 0.16: continue
            al = (p - W).dot(fa)
            if al < -0.09 or al > 0.03: continue
            gs = {names[g.group]: g.weight for g in v.groups}
            if not gs or max(gs, key=gs.get).startswith("Right" if side == "Left" else "Left"): continue
            if any(k.startswith(side + "Hand") and k != side + "Hand" for k in gs): continue
            if not set(gs) <= {side + "Hand", side + "LowerArm", side + "UpperArm"}: continue
            w = sm((al + 0.012) / 0.03)          # hand weight: 0 at 1.2 cm before the wrist joint, 1 at 1.8 cm past it
            for g in list(v.groups): arms.vertex_groups[g.group].remove([v.index])
            if w < 0.999: gl.add([v.index], 1 - w, "REPLACE")
            if w > 0.001: gh.add([v.index], w, "REPLACE")
            n += 1
    log(f"first-person cuffs: {n} wrist vertices follow the forearm")
_soft_cuffs()

def tun_matrix(side):
    """3x3 whose columns are t, u, n in armature space, from the rest bones. u runs along the hand (wrist to
    the knuckle row), not the forearm: this hand is bent toward the palm at rest."""
    W = B[side + "Hand"].head_local
    knuckles = sum((B[f"{side}Hand{f}1"].head_local for f in ("Index", "Middle", "Ring", "Pinky")), Vector()) / 4
    u = (knuckles - W).normalized()
    t = B[side + "HandThumb1"].head_local - W; t = (t - u * t.dot(u)).normalized()
    n = t.cross(u) if side == "Right" else u.cross(t)
    return Matrix((t, u, n)).transposed(), W.copy()

HAND_INFO = {}
for side in SIDES:
    R, W = tun_matrix(side)
    HAND_INFO[side] = dict(R=R, W=W, thumb_hint=B[side + "HandThumb1"].head_local.copy())

# skinned rest positions (armature space) and per-group weights
_AW = rig.matrix_world.inverted() @ arms.matrix_world
_P = np.array([tuple(_AW @ v.co) for v in arms.data.vertices])
_GI = {vg.name: vg.index for vg in arms.vertex_groups}
def _weights(name):
    w = np.zeros(len(_P))
    gi = _GI.get(name)
    if gi is None: return w
    for v in arms.data.vertices:
        for g in v.groups:
            if g.group == gi: w[v.index] = g.weight
    return w

def _seg_radius(a, b, ids):
    """Median distance of these vertices from the bone line a -> b (the finger's skin radius)."""
    if len(ids) < 4: return None
    a, b = np.array(a), np.array(b); ab = b - a
    t = np.clip(((_P[ids] - a) @ ab) / max(ab @ ab, 1e-12), 0, 1)
    return float(np.median(np.linalg.norm(_P[ids] - (a + t[:, None] * ab), axis=1)))

CHAIN = {}
for side in SIDES:
    for d in DIGITS:
        names = [f"{side}Hand{d}{k}" for k in (1, 2, 3)]
        pts = [B[n].head_local.copy() for n in names] + [B[names[-1]].tail_local.copy()]
        r = []
        for k, n in enumerate(names):
            ids = np.where(_weights(n) > 0.5)[0]
            r.append(_seg_radius(pts[k], pts[k + 1], ids))
        fallback = 0.011 if d != "Thumb" else 0.013
        r = [x if x is not None and 0.004 < x < 0.03 else fallback for x in r]
        CHAIN[(side, d)] = (pts, (r[0], (r[0] + r[1]) / 2, (r[1] + r[2]) / 2, r[2] * 0.85))

def chain_points(side, digit):
    """Rest joint positions (4 points: base, two knuckles, tip) and radii at those points."""
    return CHAIN[(side, digit)]

def to_hand(side, p):
    h = HAND_INFO[side]
    return h["R"].transposed() @ (Vector(p) - h["W"])

# palm: the hand bone's own skin (not fingers, not the gauntlet cuff), in hand coords
HAND_SHAPE = {}
for side in SIDES:
    ids = np.where(_weights(side + "Hand") > 0.6)[0]
    Q = np.array([tuple(to_hand(side, _P[i])) for i in ids]) if len(ids) else np.zeros((1, 3))
    mcp_u = float(np.mean([to_hand(side, B[f"{side}Hand{f}1"].head_local).y for f in FINGERS4]))
    core = Q[(Q[:, 1] > 0.02) & (Q[:, 1] < mcp_u)]
    if len(core) < 10: core = Q
    HAND_SHAPE[side] = dict(n_palm=float(np.percentile(core[:, 2], 92)), n_back=float(np.percentile(core[:, 2], 8)),
                            t_min=float(np.percentile(core[:, 0], 5)), t_max=float(np.percentile(core[:, 0], 95)), mcp_u=mcp_u)
    log(f"{side} body hand: palm surface n {HAND_SHAPE[side]['n_palm'] * 100:.1f} cm, back {HAND_SHAPE[side]['n_back'] * 100:.1f} cm, "
        f"width {(HAND_SHAPE[side]['t_max'] - HAND_SHAPE[side]['t_min']) * 100:.1f} cm, knuckles {mcp_u * 100:.1f} cm from the wrist; "
        f"radii " + ", ".join(f"{d} {chain_points(side, d)[1][0] * 1000:.0f} mm" for d in DIGITS))

_hs = HAND_SHAPE["Right"]
PALM = dict(n_palm=_hs["n_palm"], n_back=_hs["n_back"], half_t=(_hs["t_max"] - _hs["t_min"]) / 2, u0=0.004, u1=_hs["mcp_u"])
CMC = tuple(to_hand("Right", B["RightHandThumb1"].head_local))

# ---------------------------------------------------------------- grip solver
exec(open(os.path.join(PROJECT, "Blender", "fp_grip_solver.py"), encoding="utf-8").read(), globals())

# the solver's palm balls and palm capsules are sized for an adult hand of 10 cm to the knuckles: fit them
# to this (gloved, larger) hand
def palm_points(side, F):
    """Balls covering the palm surface, for contact sliding (world)."""
    hs = HAND_SHAPE[side]
    pts = []
    for i in range(5):
        t = hs["t_min"] + 0.006 + (hs["t_max"] - hs["t_min"] - 0.012) * i / 4
        for j in range(4):
            u = 0.015 + (hs["mcp_u"] * 0.92 - 0.015) * j / 3
            pts.append((F @ Vector((t, u, hs["n_palm"] - 0.008)), 0.008))
    c = to_hand(side, B[side + "HandThumb1"].head_local)
    pts.append((F @ Vector((c.x + 0.010, c.y + 0.022, hs["n_palm"] - 0.010)), 0.012))   # thenar
    return pts

def hand_capsules(side, Mh, digit_angles):
    """Capsules of a posed hand (digits, and three along the palm) as obstacles for the other hand."""
    caps = []
    for digit, ang in digit_angles.items():
        Ms = digit_fk(side, digit, Mh, ang)
        rad = CHAIN_RAD[side][digit]
        for k in range(3):
            nm = f"{side}Hand{digit}{k + 1}"
            A = Ms[k].translation; y = Ms[k].col[1].to_3d().normalized()
            caps.append((A.copy(), A + y * BLEN[nm], rad[k], rad[k + 1]))
    hs = HAND_SHAPE[side]
    F = Mh @ HB2TUN[side]
    r = max(0.010, (hs["n_palm"] - hs["n_back"]) / 2)
    cn = (hs["n_palm"] + hs["n_back"]) / 2
    for t in (hs["t_min"] + r, (hs["t_min"] + hs["t_max"]) / 2, hs["t_max"] - r):
        caps.append((F @ Vector((t, 0.01, cn)), F @ Vector((t, hs["mcp_u"] * 0.95, cn)), r, r))
    return caps
