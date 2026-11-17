"""Double-barrel shotgun hold for the survivor body, posed in Blender (same method as grip_rifle.py).

Run (from the project folder; Gun_Shotgun.blend is only read, never saved):
  blender -b Blender/Gun_Shotgun.blend -P Blender/grip_shotgun.py -- <render_dir|-> Blender/Grip_Shotgun.blend [--export] [--textured] [--sheets]
Writes Blender/Grip_Shotgun.blend: actions Shotgun_Idle/Fire/Reload/Draw (72/12/72/14 frames, 30 fps, upper body
only) and Shotgun_Rest (2 frames, every bone at rest) on Player_Rig. With --export also
Assets/Models/2026-11-09_Guns/Shotgun/Survivor_Shotgun.fbx for the game: the survivor armature ("Armature.002")
with the Shotgun_* takes and the shotgun's armature riding the right hand.
The scene's body is swapped for the current fingered WastelandSurvivor.fbx first.

- Stance: the upper body turns a little to the right (left shoulder forward, like a real shooter),
  the head turns back to look down the gun, the shotgun's stock sits in the right shoulder pocket.
- Right hand: round the wrist of the stock (no pistol grip): palm on its right, the finger block bends
  joint by joint until it touches, curling down and under it; the thumb lies forward along the wrist.
- Left hand: the barrels/fore-end rest in the palm from below just ahead of the receiver (where the
  gun clip puts HandL), the finger block curls up their right side, the thumb runs along the left side.
- Every frame of each shotgun clip: the hands follow the clip's HandR / HandL bones (arm IK); the gun
  rides the right hand in the export.
"""
import bpy, math, os, sys
import numpy as np
from mathutils import Vector, Quaternion, Matrix

PROJECT = "C:/Mian Sarmad/Unity/Zombie-Rush/Zombie Rush"
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
RENDER = argv[0] if len(argv) > 0 and argv[0] != "-" else None
SAVE = argv[1] if len(argv) > 1 and argv[1] != "-" else None
ONLY_IDLE = "--idle-only" in argv

def log(*a): print("[grip]", *a, flush=True)

# swap in the current fingered body (Gun_Shotgun.blend may hold an older one)
_old = bpy.data.objects.get("Player_Rig")
if _old is None or "RightHandMiddle1" not in _old.data.bones:
    if _old is not None:
        for o in list(_old.children) + [_old]: bpy.data.objects.remove(o, do_unlink=True)
    for a in [a for a in bpy.data.actions if a.name.startswith("Body_")]: bpy.data.actions.remove(a)
    _before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=PROJECT + "/Assets/Models/2026-11-14_ShadowOperative_Final/ShadowOperative.fbx")
    _new = [o for o in bpy.data.objects if o not in _before]
    _b = next(o for o in _new if o.type == "ARMATURE"); _b.name = "Player_Rig"
    _meshes = [o for o in _new if o.type == "MESH"]
    max((o for o in _meshes if not o.name.startswith("Backpack")), key=lambda o: len(o.data.vertices)).name = "Survivor_Mesh"   # the body, not the separate backpack
    for a in [x for x in bpy.data.actions if x.name.startswith("Player_Rig") or "|" in x.name]: bpy.data.actions.remove(a)
    log("body swapped for the player body ShadowOperative.fbx")
body = bpy.data.objects["Player_Rig"]
gun = bpy.data.objects["Shotgun_Rig"]
GUN = "Shotgun"
assert all(abs(body.matrix_world[i][j] - (1.0 if i == j else 0.0)) < 1e-4 for i in range(4) for j in range(4)), "Player_Rig must sit at the origin"
sc = bpy.context.scene
sc.render.fps = 30
# start from the rest pose: the import can leave an old clip's hip offset on the bones
body.animation_data_create(); body.animation_data.action = None   # a clip-less body FBX imports without animation data
for pb in body.pose.bones:
    pb.location = (0, 0, 0); pb.rotation_mode = "QUATERNION"; pb.rotation_quaternion = (1, 0, 0, 0); pb.scale = (1, 1, 1)

g = {"ARM_NAME": "Player_Rig", "MESH_NAME": "Survivor_Mesh", "PFX_OVERRIDE": "",
     "AL_OVERRIDE": {k: k for k in ("Hips", "Spine", "Chest", "UpperChest", "Neck", "Head", "LeftShoulder", "RightShoulder",
                                    "LeftUpperArm", "RightUpperArm", "LeftLowerArm", "RightLowerArm", "LeftHand", "RightHand",
                                    "LeftUpperLeg", "RightUpperLeg", "LeftLowerLeg", "RightLowerLeg", "LeftFoot", "RightFoot",
                                    "LeftToes", "RightToes")},
     "__name__": "zr_anim"}
exec(open(os.path.join(PROJECT, "Blender", "zr_anim_lib.py"), encoding="utf-8").read(), g)
Pose, ML, HEAD, BONES = g["Pose"], g["ML"], g["HEAD"], g["BONES"]

RIGHT, UP, FWD = Vector((-1, 0, 0)), Vector((0, 0, 1)), Vector((0, -1, 0))
LEFT, DOWN, BACK = -RIGHT, -UP, -FWD

# ------------------------------------------------------------------ tuning (degrees / metres)
T = dict(
    spine_turn=-48.0,      # whole torso turns right (left shoulder forward): the arms are short, this is what lets the left hand reach
    spine_lean=6.0,        # leans into the gun
    head_turn=46.0,        # neck + head turn back to look straight ahead
    head_tilt=-3.0,         # head tips forward a little (upright: the first-person view sees the gun low and right)
    clav_left=-30.0,       # left collarbone swings forward
    clav_right=15.0,       # right collarbone forward into the stock (positive = forward on the right side)
    grip_at=(-0.17, -0.29, 0.06),   # wrist centre from the shoulder-joint midpoint (x + left, y - forward, z up); higher than the rifle: this stock drops, so its butt meets the shoulder
    gun_drop=0.0,          # lower the whole gun (m)
    finger_t=0.013,        # half thickness of a finger (m) when touching a surface (Shadow Operative's gloved fingers)
    palm_t=0.022,          # palm thickness from bone line to skin (m)
    r_fingers_fwd=10.0,    # fingers angle forward round the wrist; 10 lets the five separate fingers close round the stock (the Survivor's finger block used 50)
    caps=(70.0, 65.0, 55.0),
    forearm_twist=0.6,     # share of the hand turn the forearm takes (the rest stays at the wrist); with the upper-arm roll this spreads the twist so no joint pinches
    tw_up=1.0, tw_fore=1.0, tw_wrist=1.0,   # how much each of shoulder / forearm / wrist resists twist (squared cost)
    l_palm_side=0.0,     # left hand: degrees the hold turns from palm-up toward palm-on-the-left-side
    auto_elbow_l=1.0, auto_elbow_r=1.0,   # pick the elbow that keeps the wrist straight and the forearm twist small
    eye_up=0.03, eye_fwd=-0.06,   # first-person camera from the Head bone (up, forward): must match the game's eye offset for this gun     # share of the hand's turn the forearm takes   # most a finger-block joint may bend: the hand mesh tears beyond this
    r_palm_up=20.0,        # right palm turns this far from facing left toward facing up (the hand sits a little under the wrist)
    l_fingers_fwd=30.0,    # left hand: fingers angle forward from straight across (deg)
    l_hand_back=0.0,       # left hand slides back along the handguard (m)
    elbow_r=(-0.5, 0.0, -1.0), elbow_l=(0.5, 0.1, -1.0),
)

# --set key=value (repeatable) overrides a tuning value for a trial run
for _i, _a in enumerate(argv):
    if _a == "--set" and _i + 1 < len(argv):
        _k, _v = argv[_i + 1].split("=", 1)
        T[_k] = tuple(float(x) for x in _v.split(",")) if "," in _v else float(_v)
        log(f"set {_k} = {T[_k]}")

# ------------------------------------------------------------------ geometry of the shotgun (at Idle frame 1)
idle = bpy.data.actions["Idle"]
gun.animation_data.action = idle
sc.frame_set(1); bpy.context.view_layer.update()

def bone_world(arm, name):
    return arm.matrix_world @ arm.pose.bones[name].matrix

G0 = bone_world(gun, "Gun")
def gun_pts():
    pts = []
    dg = bpy.context.evaluated_depsgraph_get()
    for name in ("Shotgun", "Barrels"):
        o = bpy.data.objects[name]; e = o.evaluated_get(dg); m = e.to_mesh()
        pts += [o.matrix_world @ v.co for v in m.vertices]; e.to_mesh_clear()
    return pts
def to_gf(p, G=G0):
    d = p - G.translation
    return np.array([d.dot(RIGHT), d.dot(UP), d.dot(FWD)])
V = np.array([to_gf(p) for p in gun_pts()])

# the stock's wrist, behind the trigger guard (the double barrel has no pistol grip)
sel = V[(V[:, 1] > -0.06) & (V[:, 1] < 0.07) & (V[:, 2] > -0.11) & (V[:, 2] < 0.035) & (np.abs(V[:, 0]) < 0.045)]
c = sel.mean(axis=0)
u, s, vt = np.linalg.svd(sel - c)
ax = vt[0] if vt[0][1] > 0 else -vt[0]                # grip axis, bottom -> top, gun frame
grip_c = c
grip_ax = ax / np.linalg.norm(ax)
# half sizes across the grip: right/left and front/back (perpendicular to the axis)
# the way the fingers run across the wrist (palm on its right): LEFT x axis, worked out in world space
# because the gun frame (right, up, fwd) is left-handed; the wrist runs along the gun, so "forward
# perpendicular to the axis" (the rifle's choice) would be undefined here
_axw = RIGHT * float(grip_ax[0]) + UP * float(grip_ax[1]) + FWD * float(grip_ax[2])
_dw = LEFT.cross(_axw).normalized()
fwd_perp = np.array([_dw.dot(RIGHT), _dw.dot(UP), _dw.dot(FWD)]); fwd_perp /= np.linalg.norm(fwd_perp)
right_perp = np.cross(grip_ax, fwd_perp)  # completes the frame
rel = sel - grip_c
grip_hw = np.percentile(np.abs(rel @ np.array([1.0, 0, 0])), 95)
grip_hd = np.percentile(np.abs(rel @ fwd_perp), 95)
log(f"grip centre {grip_c.round(3)}, axis {grip_ax.round(2)}, half width {grip_hw:.3f}, half depth {grip_hd:.3f}, n {len(sel)}")

# handguard: slices 0.22..0.36 forward
# per 1 cm slice: bottom and top of the handguard (median over slices, so a pin or swivel under it is ignored)
bots, tops, wids = [], [], []
_hl = to_gf((gun.matrix_world @ gun.pose.bones["HandL"].matrix).translation)
for f in np.arange(_hl[2] - 0.05, _hl[2] + 0.055, 0.01):
    sl = V[(np.abs(V[:, 2] - f) < 0.005) & (V[:, 1] > 0.0) & (np.abs(V[:, 0]) < 0.06)]
    if len(sl) < 8: continue
    bots.append(np.percentile(sl[:, 1], 5)); tops.append(np.percentile(sl[:, 1], 95)); wids.append(np.percentile(np.abs(sl[:, 0]), 90))
hb_, ht_ = float(np.median(bots)), float(np.median(tops))
hg_c = np.array([0.0, (hb_ + ht_) / 2, float(_hl[2])])
hg_hh = (ht_ - hb_) / 2
hg_hw = float(np.median(wids))
log(f"handguard centre {hg_c.round(3)}, half height {hg_hh:.3f}, half width {hg_hw:.3f}")

def from_gf(v, G=G0, point=True):
    w = RIGHT * float(v[0]) + UP * float(v[1]) + FWD * float(v[2])
    return (G.translation + w) if point else w

# ------------------------------------------------------------------ hand frames at rest
FINGERS4 = ("Index", "Middle", "Ring", "Pinky")   # the player body has five 3-joint fingers
def chain(side, f):
    return [f"{side}Hand{f}{k}" for k in (1, 2, 3)]
def rest_pts(names):
    b = body.data.bones
    return [b[n].head_local.copy() for n in names] + [b[names[-1]].tail_local.copy()]
def curl_axis(pts):
    a = (pts[1] - pts[0]).cross(pts[3] - pts[1])
    return a.normalized()
# Palm side from the hand MESH: the hand, finger block and thumb lie flat in one plane and the palm
# faces out of it (the finger bones curve inside that plane, so the bones alone hide the palm side).
# Same method as Blender/grip_pistol.py hand_frame().
mesh = bpy.data.objects["Survivor_Mesh"]
VG = {vg.name: vg.index for vg in mesh.vertex_groups}
REST_CO = np.array([tuple(v.co) for v in mesh.data.vertices])
def owned(groups, wmin):
    idx = {VG[gn] for gn in groups if gn in VG}
    return [v.index for v in mesh.data.vertices if any(gg.group in idx and gg.weight >= wmin for gg in v.groups)]
REST = {}
for side in ("Right", "Left"):
    hb = body.data.bones[side + "Hand"]
    ids = owned([side + "Hand"] + sum((chain(side, f) for f in FINGERS4), []) + chain(side, "Thumb"), 0.3)
    Vh = REST_CO[ids]
    _, _, vt = np.linalg.svd(Vh - Vh.mean(0))
    nrm = Vector(vt[2].tolist())
    wrist = hb.head_local.copy()
    b = body.data.bones
    fmid = (b[side + "HandMiddle1"].head_local + b[side + "HandMiddle3"].tail_local) * 0.5
    along = fmid - wrist; along = (along - nrm * along.dot(nrm)).normalized()
    tv = b[side + "HandThumb3"].tail_local - wrist
    thumb = (tv - along * tv.dot(along) - nrm * tv.dot(nrm)).normalized()
    palm = thumb.cross(along) if side == "Right" else along.cross(thumb)
    palm.normalize()
    curl = along.cross(palm).normalized()              # fingers bend about this: along turns toward the palm
    tdir = (b[side + "HandThumb3"].tail_local - b[side + "HandThumb1"].head_local).normalized()
    tcurl = tdir.cross((palm * 0.8 - thumb * 0.5).normalized()).normalized()
    REST[side] = dict(ax=curl, d=along, n=palm, tcurl=tcurl, hand=hb.matrix_local.to_3x3(), head=wrist,
                      knuckle=(b[side + "HandMiddle1"].head_local + b[side + "HandRing1"].head_local) * 0.5)   # centre of the knuckle row
    log(f"{side} rest (mesh plane): along {tuple(round(x, 2) for x in along)}, palm {tuple(round(x, 2) for x in palm)}, thumb side {tuple(round(x, 2) for x in thumb)}")

def frame3(a, b, c):
    return Matrix((a, b, c)).transposed()

def hand_target(side, ax_t, n_t, knuckle_at):
    """Hand bone world rotation and head position so the curl axis lies along ax_t, the palm faces n_t,
    and the base knuckle of the finger block sits at knuckle_at."""
    r = REST[side]
    n_t = (n_t - ax_t * n_t.dot(ax_t)).normalized()
    d_t = n_t.cross(ax_t).normalized()
    Q = frame3(ax_t, n_t, d_t) @ frame3(r["ax"], r["n"], r["d"]).inverted()
    R = Q @ r["hand"]
    head = knuckle_at - Q @ (r["knuckle"] - r["head"])
    return R, head

# ------------------------------------------------------------------ pose helpers on the real armature
def torso(P):
    """The rig's spine is Chest + UpperChest: share the lean and the turn between them."""
    for n in ("Chest", "UpperChest"):
        P.rot(n, rx=T["spine_lean"] * 0.5, rz=T["spine_turn"] * 0.5)

def upd():
    bpy.context.view_layer.update()

def set_bone_world_rot(P, name, R):
    M = P.fk(); p = g["PAR"][name]
    base = (M[p] @ (ML[p].inverted() @ ML[name])).to_3x3()
    P.q[name] = (base.inverted() @ R).to_quaternion()

def write(P):
    for n in BONES:
        pb = body.pose.bones[n]; pb.rotation_mode = "QUATERNION"; pb.rotation_quaternion = P.q[n]
    upd()

def pb_pt(name, tail=False):
    pb = body.pose.bones[name]
    return (pb.tail if tail else pb.head).copy()

def rotate_bone_about(name, axis_world, ang):
    pb = body.pose.bones[name]
    M = pb.matrix.copy(); h = M.translation.copy()
    Rm = Quaternion(axis_world, ang).to_matrix().to_4x4()
    pb.matrix = Matrix.Translation(h) @ Rm @ Matrix.Translation(-h) @ M
    upd()

def aim_bone(name, target):
    pb = body.pose.bones[name]
    cur = (pb.tail - pb.head).normalized(); want = (target - pb.head).normalized()
    rotate_bone_about(name, cur.cross(want).normalized() if cur.cross(want).length > 1e-6 else Vector((1, 0, 0)),
                      cur.angle(want))

def ellipse_rho(p, centre, axis, e1, e2, h1, h2):
    """0 at the centre line, 1 on the surface of an elliptic cylinder (half sizes h1 along e1, h2 along e2)."""
    d = p - centre
    d = d - axis * d.dot(axis)
    return math.sqrt((d.dot(e1) / h1) ** 2 + (d.dot(e2) / h2) ** 2)

def curl_to_contact(names, axis_fn, rho_fn, lo=-35.0, hi=120.0, caps=None):
    """Bend each joint (about the chain's current curl axis) until the next joint / fingertip touches."""
    out = []
    for i, n in enumerate(names):
        hi_i = caps[i] if caps else hi
        nxt = (lambda: pb_pt(names[i + 1])) if i + 1 < len(names) else (lambda: pb_pt(n, tail=True))
        base = body.pose.bones[n].matrix.copy()
        axv = axis_fn()
        def at(deg):
            body.pose.bones[n].matrix = base; upd()
            rotate_bone_about(n, axv, math.radians(deg))
            return rho_fn(nxt())
        # Step from open to closed and stop at the first touch (the distance is not monotonic:
        # a joint that bends too far swings back out past the surface).
        step = 5.0
        prev = lo
        if at(lo) <= 1.0:          # already touching when opened right up: stay open
            out.append(lo); continue
        hit = None
        d = lo + step
        while d <= hi_i + 1e-6:
            if at(d) <= 1.0: hit = d; break
            prev = d; d += step
        if hit is None:            # never touches within the cap: stop at the cap (a relaxed, not clenched, hand)
            at(hi_i); out.append(hi_i); continue
        a, b = prev, hit
        for _ in range(14):
            m = (a + b) / 2
            if at(m) > 1.0: a = m
            else: b = m
        at(a); out.append(a)
    return out

def live_curl_axis(side, f="Index"):
    """The rest bend axis carried along by the hand's current turn."""
    Qh = body.pose.bones[side + "Hand"].matrix.to_3x3() @ REST[side]["hand"].inverted()
    return (Qh @ (REST[side]["ax"] if f == "Index" else REST[side]["tcurl"])).normalized()

# ------------------------------------------------------------------ the gun in the shoulder pocket
gun_rest_loc = gun.location.copy()
P0 = Pose()
torso(P0)
M0 = P0.fk()
uc = "UpperChest"
mid_sh = (HEAD["RightUpperArm"] + HEAD["LeftUpperArm"]) / 2
grip_target = mid_sh + Vector(T["grip_at"])
gun.location = gun.location + (grip_target - from_gf(grip_c)) + Vector((0, 0, -T["gun_drop"]))
upd()
log(f"gun moved by {tuple(round(x, 3) for x in (gun.location - gun_rest_loc))}")

# ------------------------------------------------------------------ hand targets, solved once at Idle frame 1
sc.frame_set(1); upd()
G1 = bone_world(gun, "Gun")
HR1, HL1 = bone_world(gun, "HandR"), bone_world(gun, "HandL")
gw = lambda v: from_gf(v, G1)
gv = lambda v: from_gf(v, G1, point=False)

# right: curl axis along the grip (bottom -> top), palm facing left into the grip
r_ax = gv(grip_ax).normalized()
r_fwd = gv(fwd_perp).normalized()
r_tilt = Quaternion(LEFT, math.radians(-T["r_fingers_fwd"]))
# palm faces left and partly up into the wrist; the knuckles sit on the wrist's surface opposite the palm
_pa = math.radians(T["r_palm_up"])
r_palm = (LEFT * math.cos(_pa) + UP * math.sin(_pa)).normalized()
_u = -r_palm
_e1 = gv(np.array([1.0, 0, 0])); _e1 = (_e1 - r_ax * _e1.dot(r_ax)).normalized(); _e2 = r_ax.cross(_e1).normalized()
_r_surf = 1.0 / math.sqrt((_u.dot(_e1) / grip_hw) ** 2 + (_u.dot(_e2) / grip_hd) ** 2)
r_kn = gw(grip_c + grip_ax * 0.012) + _u * (_r_surf + T["palm_t"] * 0.6)
r_tilt = Quaternion(LEFT, math.radians(-T["r_fingers_fwd"]))
R_r, head_r = hand_target("Right", (r_tilt @ r_ax).normalized(), r_palm, r_kn)
# left: fingers across under the handguard, angled forward; palm facing up into it
l_d = (RIGHT * math.cos(math.radians(T["l_fingers_fwd"])) + FWD * math.sin(math.radians(T["l_fingers_fwd"]))).normalized()
l_ax = l_d.cross(UP).normalized()
hgc = gw(hg_c) + BACK * T["l_hand_back"]
l_kn = hgc + LEFT * (hg_hw * 0.7) + DOWN * (hg_hh + T["palm_t"] * 0.55)
# turn the whole left-hand hold round the handguard axis (degrees about the gun's forward axis; 0 = palm
# straight up under it). A palm-up hold asks this rig's arm for ~170 deg of twist and pinches the sleeve.
_qs = Quaternion(FWD, math.radians(T["l_palm_side"]))
R_l, head_l = hand_target("Left", (_qs @ l_ax).normalized(), (_qs @ UP).normalized(), hgc + _qs @ (l_kn - hgc))

# both hands relative to their clip bones, so they follow the clip on every frame
REL = {"Right": (HR1.inverted() @ Matrix.Translation(head_r) @ R_r.to_4x4()),
       "Left": (HL1.inverted() @ Matrix.Translation(head_l) @ R_l.to_4x4())}
HL1_gf = HL1.copy()

def hand_world(side, frame_bone):
    M = frame_bone @ REL[side]
    return M.to_3x3().normalized(), M.translation

def grip_rho_fn(G):
    cen = from_gf(grip_c, G); a = from_gf(grip_ax, G, False).normalized()
    e1 = from_gf(np.array([1.0, 0, 0]), G, False); e1 = (e1 - a * e1.dot(a)).normalized(); e2 = a.cross(e1).normalized()
    t = T["finger_t"]
    return lambda p: ellipse_rho(p, cen, a, e1, e2, grip_hw + t, grip_hd + t)

def guard_rho_fn(G):
    cen = from_gf(hg_c, G); a = from_gf(np.array([0, 0, 1.0]), G, False).normalized()
    e1 = from_gf(np.array([1.0, 0, 0]), G, False).normalized(); e2 = from_gf(np.array([0, 1.0, 0]), G, False).normalized()
    t = T["finger_t"]
    return lambda p: ellipse_rho(p, cen, a, e1, e2, hg_hw + t, hg_hh + t)

def best_elbow(P, side, wrist, R_hand):
    """Elbow direction round the shoulder->wrist line that keeps the wrist straightest and the forearm
    twist smallest (the hand's turn is fixed by the gun: a badly placed elbow bends the wrist, and a big
    forearm twist wrings the sleeve into a fin at the elbow). The elbow may not rise above the shoulder
    or swing in front of the chest."""
    sh = P.pos(side + "UpperArm")
    ax = (wrist - sh).normalized()
    u = (DOWN - ax * DOWN.dot(ax)).normalized(); v = ax.cross(u)
    along = (R_hand @ (REST[side]["hand"].inverted() @ REST[side]["d"])).normalized()
    lo, hn = side + "LowerArm", side + "Hand"
    rel = ML[lo].to_3x3().inverted() @ ML[hn].to_3x3()
    ideal = R_hand @ rel.inverted()
    toward_body = LEFT if side == "Right" else RIGHT
    best, best_cost = None, 1e9
    for k in range(36):
        a = 2 * math.pi * k / 36
        pole = u * math.cos(a) + v * math.sin(a)
        P.arm_ik(side, wrist, pole=tuple(pole))
        M = P.fk()
        elbow = M[lo].translation
        fore = (wrist - elbow).normalized()
        F = M[lo].to_3x3(); y = F.col[1].normalized()
        xc = F.col[0] - y * F.col[0].dot(y); xi = ideal.col[0] - y * ideal.col[0].dot(y)
        tw = abs(math.degrees(math.atan2(xc.cross(xi).dot(y), xc.dot(xi)))) if xc.length > 1e-6 and xi.length > 1e-6 else 0.0
        cost = math.degrees(fore.angle(along))   # twist is handled by solve_arm (upper arm + forearm), not here
        if elbow.z > sh.z + 0.03: cost += 200 * (elbow.z - sh.z)                       # elbow above the shoulder
        if (elbow - sh).dot(toward_body) > 0.02: cost += 400 * (elbow - sh).dot(toward_body)   # across the chest
        if cost < best_cost: best, best_cost = tuple(pole), cost
    return best

def bone_twist(P, name):
    """Twist (deg) of a bone's pose rotation about its own length (local Y)."""
    q = P.q[name]
    a = math.degrees(2 * math.atan2(q.y, q.w))
    return (a + 180) % 360 - 180

ROLL_REF, ROLL_LAST = {}, {}   # upper-arm roll found on Idle frame 1, and the last frame's roll
TW_REF, TW_LAST = {}, {}       # the same for the forearm twist, kept unwrapped (no +-180 flips)

def solve_arm(P, side, wrist, pole, R_hand):
    """Arm IK with the twist shared out like a real arm: the upper arm rolls in the shoulder and the
    forearm turns, so neither the elbow nor the wrist wrings (a forearm asked for ~170 deg of twist
    pinches its sleeve into a fin at the elbow). Tries upper-arm rolls; the elbow and wrist stay put."""
    up, lo, hn = side + "UpperArm", side + "LowerArm", side + "Hand"
    P.arm_ik(side, wrist, pole=pole)
    M = P.fk(); sh = M[up].translation.copy(); elbow = M[lo].translation.copy()
    rel = ML[lo].to_3x3().inverted() @ ML[hn].to_3x3()
    ideal = R_hand @ rel.inverted()
    best = None
    # the first solve searches every roll; after that each frame stays within 20 deg of the last one, and
    # every clip starts from the Idle roll, so the arm never flips between frames or clips (the game blends clips)
    if side in ROLL_LAST:
        c = ROLL_LAST[side]
        rolls = [c + d for d in range(-20, 21, 4)]       # at most 20 deg of roll change per frame
    else:                                                # first frame of a clip: any roll, preferring the Idle one
        rolls = range(-150, 151, 10)
    for ru in rolls:
        P.aim(up, elbow - sh, roll=ru)
        P.aim(lo, wrist - elbow)
        F = P.fk()[lo].to_3x3(); y = F.col[1].normalized()
        xc = F.col[0] - y * F.col[0].dot(y); xi = ideal.col[0] - y * ideal.col[0].dot(y)
        tw = math.degrees(math.atan2(xc.cross(xi).dot(y), xc.dot(xi))) if xc.length > 1e-6 and xi.length > 1e-6 else 0.0
        prev = TW_LAST.get(side, TW_REF.get(side))
        if prev is not None:                       # the same turn, the way round nearest the last frame
            while tw - prev > 180: tw -= 360
            while tw - prev < -180: tw += 360
        P.aim(lo, wrist - elbow, roll=tw * T["forearm_twist"])
        set_bone_world_rot(P, hn, R_hand)
        t_up, t_lo, t_hn = bone_twist(P, up), bone_twist(P, lo), bone_twist(P, hn)
        # spread the twist: the cost of each place grows with the square, so no one joint takes it all
        cost = T["tw_up"] * t_up ** 2 + T["tw_fore"] * (tw * T["forearm_twist"]) ** 2 + T["tw_wrist"] * t_hn ** 2
        if prev is not None: cost += 40 * abs(tw - prev)    # no sudden spin of the forearm
        if side not in ROLL_LAST and side in ROLL_REF: cost += 20 * abs(ru - ROLL_REF[side])
        if best is None or cost < best[0]:
            best = (cost, ru, tw, t_up, t_lo, t_hn)
    _, ru, tw, t_up, t_lo, t_hn = best
    ROLL_REF.setdefault(side, ru); ROLL_LAST[side] = ru
    TW_REF.setdefault(side, tw); TW_LAST[side] = tw
    P.aim(up, elbow - sh, roll=ru)
    P.aim(lo, wrist - elbow, roll=tw * T["forearm_twist"])
    set_bone_world_rot(P, hn, R_hand)
    return t_up, t_lo, t_hn

# ------------------------------------------------------------------ one frame
def pose_frame(frame, solve_fingers=True, cache={}):
    sc.frame_set(frame); upd()
    G = bone_world(gun, "Gun")
    P = Pose()
    torso(P)
    P.rot("Neck", rx=T["head_tilt"] * 0.4, rz=T["head_turn"] * 0.5)
    P.rot("Head", rx=T["head_tilt"] * 0.6, rz=T["head_turn"] * 0.5)
    P.rot("LeftShoulder", rz=T["clav_left"])
    P.rot("RightShoulder", rz=T["clav_right"])
    # rot() takes rest-relative world angles; the shoulders sit under the turned chest, so re-express
    # how far the left hand is from its handguard spot: 0 on it, 1 away (fetching a magazine or shells)
    G_now = bone_world(gun, "Gun")
    _near = (bone_world(gun, "HandL").translation - (G_now @ (G1.inverted() @ HL1_gf)).translation).length
    w_away = min(1.0, max(0.0, (_near - 0.02) / 0.12)); w_away = w_away * w_away * (3 - 2 * w_away)   # the hand turns while it travels, not at the last moment
    for side in ("Right", "Left"):
        R, head = hand_world(side, bone_world(gun, "HandR" if side == "Right" else "HandL"))
        if side == "Left" and w_away > 0:
            # away from the handguard the clip's hand turns are not meant for a real wrist: relax toward the
            # hand simply following the forearm (no wrist turn), so the arm never needs an impossible twist
            pole_l = T["elbow_l"] if T.get("auto_elbow_l", 0.0) <= 0 else best_elbow(P, side, head, R)
            P.arm_ik(side, head, pole=pole_l)
            Mx = P.fk()
            R_nat = (Mx["LeftLowerArm"].to_3x3() @ (ML["LeftLowerArm"].to_3x3().inverted() @ ML["LeftHand"].to_3x3()))
            R = R.to_quaternion().slerp(R_nat.to_quaternion(), w_away).to_matrix()
        pole = T["elbow_r"] if side == "Right" else T["elbow_l"]
        if T.get("auto_elbow_" + side[0].lower(), 0.0) > 0:
            pole = best_elbow(P, side, head, R)
        t_up, t_lo, t_hn = solve_arm(P, side, head, pole, R)
        if "--twist-log" in argv and (frame == 1 or frame % 4 == 0):
            log(f"TWIST {side} f{frame}: upper arm {t_up:.0f}, forearm {t_lo:.0f}, wrist {t_hn:.0f} deg")
    write(P)
    if frame == 1 and "--twist-log" in argv:
        for sd in ("Right", "Left"):
            fa = body.pose.bones[sd + "LowerArm"]; hd_ = body.pose.bones[sd + "Hand"]
            fy = (fa.tail - fa.head).normalized(); hy = (hd_.tail - hd_.head).normalized()
            ry = (body.data.bones[sd + "Hand"].matrix_local.to_3x3() @ Vector((0, 1, 0))); rf = (body.data.bones[sd + "LowerArm"].matrix_local.to_3x3() @ Vector((0, 1, 0)))
            log(f"WRIST {sd}: bend {math.degrees(fy.angle(hy)):.0f} deg (rest {math.degrees(rf.angle(ry)):.0f})")
    reach = {s: (pb_pt(s + "Hand") - hand_world(s, bone_world(gun, "HandR" if s == "Right" else "HandL"))[1]).length for s in ("Right", "Left")}
    # Fingers: solved once against the grip and the handguard (Idle frame 1), then the same shapes on
    # every frame, so nothing jitters; the left hand loosens while it is away from the handguard.
    if solve_fingers:
        if not FINGERS:
            solve_reference(G, frame)
        set_locals(FINGERS["Right"])
        HL = bone_world(gun, "HandL")
        near = (HL.translation - (G @ (G1.inverted() @ HL1_gf)).translation).length
        w = min(1.0, max(0.0, (near - 0.015) / 0.045)); w = w * w * (3 - 2 * w)
        set_locals({n: FINGERS["Left"][n].slerp(LOOSE[n], w) for n in FINGERS["Left"]})
        return reach, round(w, 2), None
    return reach, None, None

FINGERS, LOOSE = {}, {}
def finger_names(side):
    return sum((chain(side, f) for f in FINGERS4), []) + chain(side, "Thumb")
def get_locals(side):
    return {n: body.pose.bones[n].rotation_quaternion.copy() for n in finger_names(side)}
def set_locals(d):
    for n, q in d.items():
        body.pose.bones[n].rotation_quaternion = q
    upd()

def solve_reference(G, frame):
    rr = grip_rho_fn(G)
    a1 = curl_to_contact(chain("Right", "Index"), lambda: live_curl_axis("Right"), rr, caps=T["caps"])
    for _f in FINGERS4[1:]:
        curl_to_contact(chain("Right", _f), lambda: live_curl_axis("Right"), rr, caps=T["caps"])
    thumb_right(G)
    gr = guard_rho_fn(G)
    a2 = curl_to_contact(chain("Left", "Index"), lambda: live_curl_axis("Left"), gr, caps=T["caps"])
    for _f in FINGERS4[1:]:
        curl_to_contact(chain("Left", _f), lambda: live_curl_axis("Left"), gr, caps=T["caps"])
    thumb_left(G)
    names = chain("Right", "Index"); ln = chain("Left", "Index")
    log("CONTACT right block rho " + str([round(rr(p), 2) for p in [pb_pt(n) for n in names] + [pb_pt(names[-1], tail=True)]])
        + "; thumb rho " + str([round(rr(pb_pt(n, tail=True)), 2) for n in chain("Right", "Thumb")]) + f"; bends {[round(x) for x in a1]}")
    log("CONTACT left block rho " + str([round(gr(p), 2) for p in [pb_pt(n) for n in ln] + [pb_pt(ln[-1], tail=True)]]) + f"; bends {[round(x) for x in a2]}")
    FINGERS["Right"] = get_locals("Right"); FINGERS["Left"] = get_locals("Left")
    # the loose hold: rest fingers with a moderate curl, for carrying a magazine
    set_locals({n: Quaternion() for n in finger_names("Left")})
    loose_grab("Left")
    LOOSE.update(get_locals("Left"))
    set_locals(FINGERS["Left"])

def thumb_right(G):
    """Thumb over the top of the wrist toward its left side, pointing forward (the classic stock-wrist grip)."""
    t = T["finger_t"]
    gl = lambda v: from_gf(v, G)
    up_n = -fwd_perp                                          # away from the finger side: the top of the wrist
    names = chain("Right", "Thumb")
    aim_bone(names[0], gl(grip_c + up_n * (grip_hd + t) + np.array([grip_hw * 0.5, 0, 0]) + grip_ax * 0.01))
    aim_bone(names[1], gl(grip_c + up_n * (grip_hd + t) + np.array([-grip_hw * 0.2, 0, 0]) + grip_ax * 0.04))
    aim_bone(names[2], gl(grip_c + up_n * (grip_hd * 0.8 + t) + np.array([-(grip_hw + t) * 0.8, 0, 0]) + grip_ax * 0.065))

def thumb_left(G):
    """Thumb along the left side of the handguard, pointing forward."""
    t = T["finger_t"]
    gl = lambda v: from_gf(v, G)
    base = hg_c + np.array([-(hg_hw + t), -hg_hh * 0.35, 0.0])
    names = chain("Left", "Thumb")
    aim_bone(names[0], gl(base + np.array([0, 0, -0.005])))
    aim_bone(names[1], gl(base + np.array([0.0, 0.004, 0.035])))
    aim_bone(names[2], gl(base + np.array([0.003, 0.008, 0.07])))

def loose_grab(side):
    out = []
    for _f in FINGERS4:
        for n, deg in zip(chain(side, _f), (35, 40, 30)):
            rotate_bone_about(n, live_curl_axis(side), math.radians(deg)); out.append(deg)
    for n, deg in zip(chain(side, "Thumb"), (10, 20, 15)):
        rotate_bone_about(n, live_curl_axis(side, "Thumb"), math.radians(deg))
    return out

# ------------------------------------------------------------------ socket on the right hand: where the gun's Root sits
sock = bpy.data.objects.get("GunSocket") or bpy.data.objects.new("GunSocket", None)
if sock.name not in sc.collection.objects: sc.collection.objects.link(sock)
sock.empty_display_type = "ARROWS"; sock.empty_display_size = 0.05
sock.parent = body; sock.parent_type = "BONE"; sock.parent_bone = "RightHand"

def key_frame(action_name, frame):
    for n in BONES:
        if n in UPPER:
            body.pose.bones[n].keyframe_insert("rotation_quaternion", frame=frame, group=n)
    # socket: gun Root relative to the right hand bone's tail frame (Blender bone parenting is at the tail)
    hb = body.pose.bones["RightHand"]
    hand_tail = body.matrix_world @ hb.matrix @ Matrix.Translation(Vector((0, hb.length, 0)))
    # the gun's Gun bone in the hand (what the game pins), as a rest-pose rig would place it
    gun_in_hand = bone_world(gun, "Gun") @ GUN_REST_REL.inverted()
    sock.matrix_parent_inverse = Matrix.Identity(4)
    sock.matrix_basis = hand_tail.inverted() @ gun_in_hand
    sock.keyframe_insert("location", frame=frame); sock.keyframe_insert("rotation_quaternion" if sock.rotation_mode == "QUATERNION" else "rotation_euler", frame=frame)

_gb = gun.data.bones
GUN_REST_REL = _gb["Root"].matrix_local.inverted() @ _gb["Gun"].matrix_local   # Root -> Gun at rest
UPPER = set()
for n in BONES:
    b = body.data.bones[n]
    while b is not None and b.name != "Chest":
        b = b.parent
    if b is not None:
        UPPER.add(n)
log(f"upper-body bones keyed: {len(UPPER)}")

clips = [("Idle", 72), ("Fire", 12), ("Reload", 72), ("Draw", 14)]
if ONLY_IDLE: clips = clips[:1]
report = []
for gname, frames in clips:
    ga = bpy.data.actions[gname]
    gun.animation_data.action = ga
    name = GUN + "_" + gname
    old = bpy.data.actions.get(name)
    if old: bpy.data.actions.remove(old)
    act = bpy.data.actions.new(name); act.use_fake_user = True
    body.animation_data.action = act
    sock.animation_data_create()
    sa = bpy.data.actions.get("GunSocket_" + gname)
    if sa: bpy.data.actions.remove(sa)
    sa = bpy.data.actions.new("GunSocket_" + gname); sa.use_fake_user = True
    sock.animation_data.action = sa
    ROLL_LAST.clear(); TW_LAST.clear()
    for f in range(1, frames + 2):   # the gun clips run frames + 1 keys (gun_build.py): match them exactly
        reach, loose, _ = pose_frame(f)
        key_frame(name, f)
        if f == 1 or f % 8 == 0:
            report.append(f"{name} f{f}: hand miss R {reach['Right']*100:.1f} cm L {reach['Left']*100:.1f} cm; left hand loose {loose}")
for r in report: log(r)

# where the gun sits for the game's first-person camera (Head + eye offset, 60 deg view)
gun.animation_data.action = bpy.data.actions["Idle"]; body.animation_data.action = bpy.data.actions[GUN + "_Idle"] if "GUN" in globals() else bpy.data.actions["Rifle_Idle"]
sc.frame_set(1); upd()
_eye = pb_pt("Head") + UP * T["eye_up"] + FWD * T["eye_fwd"]
_gp = from_gf(grip_c, bone_world(gun, "Gun")) - _eye
_r, _u, _f = _gp.dot(RIGHT), _gp.dot(UP), _gp.dot(FWD)
log(f"EYE grip right {_r:.3f} down {-_u:.3f} fwd {_f:.3f}  -> {math.degrees(math.atan2(-_u, _f)):.0f} deg below the view centre, {math.degrees(math.atan2(_r, _f)):.0f} deg right")

# the body at rest (every bone at identity): the game measures the chest/neck/head turns from it,
# not from the FBX's default pose (which is a clip frame)
_old = bpy.data.actions.get(GUN + "_Rest")
if _old: bpy.data.actions.remove(_old)
_rest = bpy.data.actions.new(GUN + "_Rest"); _rest.use_fake_user = True
body.animation_data.action = _rest
for f in (1, 2):
    for n in BONES:
        pb = body.pose.bones[n]; pb.rotation_mode = "QUATERNION"; pb.rotation_quaternion = Quaternion()
        pb.keyframe_insert("rotation_quaternion", frame=f, group=n)
body.animation_data.action = None

# ------------------------------------------------------------------ previews
if RENDER:
    os.makedirs(RENDER, exist_ok=True)
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"; sc.display.shading.color_type = "OBJECT"
    sc.display.shading.show_cavity = True
    sc.world = sc.world or bpy.data.worlds.new("W"); sc.world.color = (0.45, 0.47, 0.5)
    bpy.data.objects["Survivor_Mesh"].color = (0.78, 0.62, 0.52, 1)
    for n in ("Shotgun", "Barrels"): bpy.data.objects[n].color = (0.16, 0.16, 0.17, 1)
    sc.render.resolution_x, sc.render.resolution_y = 640, 360
    gun.animation_data.action = bpy.data.actions["Idle"]; body.animation_data.action = bpy.data.actions[GUN + "_Idle"]
    sock.animation_data.action = bpy.data.actions["GunSocket_Idle"]
    sc.frame_set(1); upd()
    G = bone_world(gun, "Gun")
    cams = {}
    def cam(name, loc, look, lens=35, ortho=None):
        c = bpy.data.objects.get(name) or bpy.data.objects.new(name, bpy.data.cameras.new(name))
        if c.name not in sc.collection.objects: sc.collection.objects.link(c)
        c.location = loc
        c.rotation_euler = (look - loc).to_track_quat("-Z", "Y").to_euler()
        c.data.lens = lens
        c.data.clip_start = 0.02 if name.startswith("C_fpp") else (max(0.01, (look - loc).length - 0.15) if name in ("C_rr", "C_rl", "C_lb", "C_lr") else 0.01)
        if ortho: c.data.type = "ORTHO"; c.data.ortho_scale = ortho
        else: c.data.type = "PERSP"
        if name.startswith("C_fpp"):   # the game camera: 60 degrees vertical
            c.data.sensor_fit = "VERTICAL"; c.data.lens_unit = "FOV"; c.data.angle_y = math.radians(60)
        return c
    head = pb_pt("Head")
    hb = body.pose.bones["Head"]
    eye = head + UP * T["eye_up"] + FWD * T["eye_fwd"]   # the game's FP camera: Head + WeaponHoldConfig.eyeOffset
    log("EYE vs gun (right, up, fwd from the eye): grip " + str(tuple(round(x, 3) for x in to_gf(from_gf(grip_c, G), Matrix.Translation(eye)))) +
        " muzzle " + str(tuple(round(x, 3) for x in to_gf(bone_world(gun, "Muzzle").translation, Matrix.Translation(eye)))) +
        f"; head bone head {tuple(round(x, 3) for x in head)}, shoulder mid {tuple(round(x, 3) for x in (pb_pt('RightUpperArm') + pb_pt('LeftUpperArm')) / 2)}")
    gp = lambda v: from_gf(np.array(v), G)
    shots = [
        ("fpp", cam("C_fpp", eye, eye + FWD * 1.0)),
        ("fpp_down", cam("C_fpp_down", eye, eye + FWD * math.cos(math.radians(40)) + DOWN * math.sin(math.radians(40)))),
        ("tpp", cam("C_tpp", head + BACK * 1.6 + RIGHT * 0.55 + UP * 0.25, head + FWD * 0.6 + DOWN * 0.15, lens=30)),
        ("side", cam("C_side", gp([1.4, 0.05, 0.15]), gp([0, 0.05, 0.15]), ortho=1.1)),
        ("front", cam("C_front", gp([0.15, 0.25, 1.6]), gp([0, 0.0, 0.0]), lens=50)),
        ("rhand_fr", cam("C_rr", gp([0.20, -0.06, 0.22]), gp([0, 0.0, -0.03]), lens=45)),
        ("rhand_fl", cam("C_rl", gp([-0.20, -0.04, 0.22]), gp([0, 0.0, -0.03]), lens=45)),
        ("lhand_fb", cam("C_lb", gp([0.06, -0.20, 0.55]), gp([0, 0.09, 0.30]), lens=45)),
        ("lhand_fr", cam("C_lr", gp([0.26, 0.02, 0.48]), gp([0, 0.09, 0.30]), lens=45)),
    ]
    def zoom(name, centre_gf, from_dir_gf, scale, depth=0.09):
        """Orthographic close-up that only sees a slab +-depth around the gun's centre plane along the view."""
        c = bpy.data.objects.get(name) or bpy.data.objects.new(name, bpy.data.cameras.new(name))
        if c.name not in sc.collection.objects: sc.collection.objects.link(c)
        tgt = gp(centre_gf); d = from_gf(np.array(from_dir_gf, dtype=float), G, False).normalized()
        c.location = tgt + d * 1.0
        c.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
        c.data.type = "ORTHO"; c.data.ortho_scale = scale
        c.data.clip_start = 1.0 - depth; c.data.clip_end = 1.0 + depth
        return c
    gc = list(grip_c); hc = list(hg_c)
    shots += [
        ("zr_right", zoom("Z_rr", gc, [1, 0, 0], 0.22)),
        ("zr_left", zoom("Z_rl", gc, [-1, 0, 0], 0.22)),
        ("zr_back", zoom("Z_rb", gc, [0.0, 0.25, -1], 0.22, depth=0.12)),
        ("zl_right", zoom("Z_lr", hc, [1, 0, 0], 0.26)),
        ("zl_left", zoom("Z_ll", hc, [-1, 0, 0], 0.26)),
        ("zl_below", zoom("Z_lb", hc, [0, -1, 0.0001], 0.26, depth=0.14)),
    ]
    if "--textured" in argv:
        sc.display.shading.color_type = "TEXTURE"
        albedo = PROJECT + "/Assets/Models/2026-11-14_ShadowOperative_Final/ShadowOperative_Albedo.png"
        for im in bpy.data.images:
            if im.name.startswith("texture_0.png.") and not os.path.exists(bpy.path.abspath(im.filepath)):
                im.filepath = albedo; im.reload()
        sc.render.resolution_x, sc.render.resolution_y = 1280, 720
        shots = [x for x in shots if x[0] in ("fpp", "fpp_down", "tpp", "side", "front")]
        shots.append(("ots", cam("C_ots", head + BACK * 1.25 + RIGHT * 0.45 + UP * 0.12, head + FWD * 2.0 + RIGHT * 0.1, lens=28)))
    def fp_arms_only(on):
        """First person in the game shows only FirstPersonArms: collarbones, upper arms, forearms, hands."""
        m = bpy.data.objects["Survivor_Mesh"]
        mod = m.modifiers.get("FP_Mask")
        if on and mod is None:
            vg = m.vertex_groups.get("FP_Arms") or m.vertex_groups.new(name="FP_Arms")
            keep = {VG[n] for n in VG if any(k in n for k in ("Shoulder", "UpperArm", "LowerArm", "Hand"))}   # as FirstPersonArms: collarbones, arms, hands
            ids = [v.index for v in m.data.vertices if sum(gg.weight for gg in v.groups if gg.group in keep) >= 0.5]
            vg.add(ids, 1.0, "REPLACE")
            mod = m.modifiers.new("FP_Mask", "MASK"); mod.vertex_group = "FP_Arms"
        if mod is not None: mod.show_render = on
    for tag, c in shots:
        fp_arms_only(tag.startswith("fpp"))
        sc.camera = c
        sc.render.filepath = os.path.join(RENDER, f"{tag}.png")
        bpy.ops.render.render(write_still=True)
    log("renders in", RENDER)

if RENDER and "--sheets" in argv:
    # frame strips per clip: side and eye view, 8 frames each
    sc.render.resolution_x, sc.render.resolution_y = 480, 270
    for gname, frames in clips:
        gun.animation_data.action = bpy.data.actions[gname]
        body.animation_data.action = bpy.data.actions[GUN + "_" + gname]
        rows = []
        for camname in ("C_side", "C_fpp"):
            row = []
            for i in range(8):
                f = 1 + round(i * (frames - 1) / 7)
                sc.frame_set(f); upd()
                if camname == "C_fpp":   # the eye camera rides the head
                    hp = pb_pt("Head"); e = hp + UP * T["eye_up"] + FWD * T["eye_fwd"]
                    c = bpy.data.objects["C_fpp"]; c.location = e
                fp_arms_only(camname == "C_fpp")
                sc.camera = bpy.data.objects[camname]
                sc.render.filepath = os.path.join(RENDER, "_t.png"); bpy.ops.render.render(write_still=True)
                im = bpy.data.images.load(sc.render.filepath)
                row.append(np.array(im.pixels[:], np.float32).reshape(270, 480, 4)); bpy.data.images.remove(im)
            rows.append(np.concatenate(row, axis=1))
        sheet = np.concatenate(rows[::-1], axis=0)
        im = bpy.data.images.new("s", sheet.shape[1], sheet.shape[0], alpha=True); im.pixels.foreach_set(sheet.ravel())
        im.filepath_raw = os.path.join(RENDER, f"Sheet_{gname}.png"); im.file_format = "PNG"; im.save(); bpy.data.images.remove(im)
    log("sheets in", RENDER)

# the socket was only a check while posing: the export carries the hand -> gun offset itself
for a in [a for a in bpy.data.actions if a.name.startswith("GunSocket_")]: bpy.data.actions.remove(a)
if bpy.data.objects.get("GunSocket"): bpy.data.objects.remove(bpy.data.objects["GunSocket"], do_unlink=True)

if "--export" in argv:
    # One FBX for the game (same layout as Survivor_Pistol.fbx): the survivor armature as "Armature.002"
    # with the Shotgun_* takes, and the shotgun's own armature at rest riding the right hand, placed so its
    # Gun bone sits where the hold puts it. The game pins the real gun's Gun bone to that one.
    gun.animation_data.action = bpy.data.actions["Idle"]; body.animation_data.action = bpy.data.actions[GUN + "_Idle"]
    sc.frame_set(1); upd()
    gun_world_hold = bone_world(gun, "Gun")
    hold_q = {n: body.pose.bones[n].rotation_quaternion.copy() for n in BONES}
    body.animation_data.action = None
    for n in BONES: body.pose.bones[n].rotation_quaternion = hold_q[n]
    gun.animation_data.action = None
    for pb in gun.pose.bones:
        pb.rotation_mode = "QUATERNION"; pb.rotation_quaternion = Quaternion(); pb.location = (0, 0, 0); pb.scale = (1, 1, 1)
    upd()
    gun.matrix_world = gun_world_hold @ GUN_REST_REL.inverted() @ gun.data.bones["Root"].matrix_local.inverted()
    upd()
    mw = gun.matrix_world.copy()
    gun.parent = body; gun.parent_type = "BONE"; gun.parent_bone = "RightHand"
    upd(); gun.matrix_world = mw; upd()
    log(f"gun Gun bone in hand: off by {(bone_world(gun, 'Gun').translation - gun_world_hold.translation).length * 1000:.2f} mm")
    OUTF = os.path.join(PROJECT, "Assets", "Models", "2026-11-09_Guns", "Shotgun", "Survivor_Shotgun.fbx")
    for o in bpy.context.view_layer.objects: o.select_set(False)
    body.select_set(True); gun.select_set(True); bpy.context.view_layer.objects.active = body
    body.name = "Armature.002"          # the survivor's armature node in the game
    bpy.ops.export_scene.fbx(filepath=OUTF, use_selection=True, object_types={"ARMATURE"}, add_leaf_bones=False,
                             apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y", bake_space_transform=False,
                             bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)
    body.name = "Player_Rig"
    log("exported", OUTF)
    gun.parent = None; gun.matrix_world = mw

if SAVE:
    bpy.ops.wm.save_as_mainfile(filepath=SAVE, copy=True)
    log("saved", SAVE)
