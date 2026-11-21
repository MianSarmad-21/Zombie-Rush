"""The player's (Shadow Operative) movement set, built from scratch (2026-11-14).

Run:  blender -b --factory-startup -P Blender/player_anim.py -- [--render DIR] [--export] [--only Clip,Clip]

Tactical-shooter locomotion on the Operative's own skeleton, every clip in place (the player controller moves
the player). Legs by two-bone IK on planted feet (heel strike, flat, toe-off pivoting on the ball, swing with
the heel kicking up), hips bobbing over the stance leg and turning toward the travel, chest and head held
square to the aim (the gun hold rides on top in the game), arms in a low ready swing, fingers relaxed.

Speeds are the game's (PlayerController walk 4, sprint 8, crouch 2.5 m/s) divided by the prefab's scale
(x1.052, from MeshyBodyTools' log), so the clips play at x1.
--export writes Assets/Models/2026-11-14_ShadowOperative_Final/ShadowOperative_Anims.fbx (armature only, one
take per clip); MeshyBodyTools.ClipSource points at it.
"""
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

PROJECT = "C:/Mian Sarmad/Unity/Zombie-Rush/Zombie Rush"
BODY = PROJECT + "/Assets/Models/2026-11-14_ShadowOperative_Final/ShadowOperative.fbx"
OUT = PROJECT + "/Assets/Models/2026-11-14_ShadowOperative_Final/ShadowOperative_Anims.fbx"
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
RENDER = argv[argv.index("--render") + 1] if "--render" in argv else None
ONLY = set(argv[argv.index("--only") + 1].split(",")) if "--only" in argv else None
FPS = 30
GAME_SCALE = 1.052                            # the prefab's scale (ZR_MeshyBody.log: "1.81 m as exported, scaled x1.052 to 1.9 m")


def log(*a):
    print("[player_anim]", *a, flush=True)


# ------------------------------------------------------------------ scene
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)
sc = bpy.context.scene
sc.render.fps = FPS
bpy.ops.import_scene.fbx(filepath=BODY)
sc.render.fps = FPS
rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
mesh = max((o for o in bpy.data.objects if o.type == "MESH"), key=lambda o: len(o.data.vertices))
RIG_NAME = rig.name                 # restored before export: Unity copies the body's avatar, the hierarchy must match
rig.name, mesh.name = "Operative_Rig", "Operative_Mesh"
for a in list(bpy.data.actions):
    bpy.data.actions.remove(a)
rig.animation_data_create()
rig.rotation_euler = (0, 0, 0)
for p in rig.pose.bones:
    p.rotation_mode = "QUATERNION"
B = rig.data.bones
NAMES = [b.name for b in B]
PAR = {b.name: (b.parent.name if b.parent else None) for b in B}
REST = {b.name: b.matrix_local.copy() for b in B}
REL = {n: (REST[PAR[n]].inverted() @ REST[n]) if PAR[n] else REST[n] for n in NAMES}
ORDER = []                                      # parents before children
_seen = set()
def _add(n):
    if n in _seen: return
    if PAR[n]: _add(PAR[n])
    _seen.add(n); ORDER.append(n)
for n in NAMES: _add(n)

# body axes (armature space): the model faces -Y, its left is +X, up +Z
FWD, LEFT, UP = Vector((0, -1, 0)), Vector((1, 0, 0)), Vector((0, 0, 1))
HEAD = {n: B[n].head_local.copy() for n in NAMES}


class Pose:
    """Local rotations (and hips location) with our own forward kinematics (no depsgraph per bone)."""
    def __init__(self):
        self.q = {n: Quaternion() for n in NAMES}
        self.hips_loc = Vector()           # in the Hips bone's rest frame
        self.gun = None                    # the pistol's frame when the hands hold one (previews only)
        self._w = None

    def world(self):
        if self._w is None:
            W = {}
            for n in ORDER:
                L = Matrix.LocRotScale(self.hips_loc if n == "Hips" else Vector(), self.q[n], None)
                W[n] = (W[PAR[n]] @ REL[n] if PAR[n] else REL[n]) @ L
            self._w = W
        return self._w

    def dirty(self):
        self._w = None

    def set_world_rot(self, n, R):
        """Give bone n the armature-space rotation R (3x3); parents must be final."""
        W = self.world()
        base = (W[PAR[n]] @ REL[n]).to_3x3() if PAR[n] else REL[n].to_3x3()
        self.q[n] = (base.inverted() @ R).to_quaternion()
        self.dirty()

    def set_hips_world(self, pos, R):
        self.hips_loc = REST["Hips"].to_3x3().inverted() @ (pos - REST["Hips"].translation)
        self.q["Hips"] = (REST["Hips"].to_3x3().inverted() @ R).to_quaternion()
        self.dirty()

    def aim(self, n, target, roll_ref=None):
        """Point bone n's Y axis at target (armature space) with the least turn from where its parent carries it;
        roll_ref: a direction the bone's X axis should face (made square to Y)."""
        W = self.world()
        base = (W[PAR[n]] @ REL[n]).to_3x3() if PAR[n] else REL[n].to_3x3()
        y0 = base.col[1].normalized()
        head = (W[PAR[n]] @ REL[n]).translation if PAR[n] else REL[n].translation
        y = (Vector(target) - head).normalized()
        if roll_ref is not None:
            x = Vector(roll_ref); x = (x - y * x.dot(y)).normalized(); z = x.cross(y)
            R = Matrix((x, y, z)).transposed()
        else:
            R = y0.rotation_difference(y).to_matrix() @ base
        self.set_world_rot(n, R)


def Rz(a): return Matrix.Rotation(math.radians(a), 3, "Z")
def Rx(a): return Matrix.Rotation(math.radians(a), 3, "X")
def Ry(a): return Matrix.Rotation(math.radians(a), 3, "Y")
def Rax(a, axis): return Matrix.Rotation(math.radians(a), 3, Vector(axis))


def smooth(x):
    x = max(0.0, min(1.0, x)); return x * x * (3 - 2 * x)


# ------------------------------------------------------------------ measurements
L_UP = (HEAD["RightLowerLeg"] - HEAD["RightUpperLeg"]).length
L_LO = (HEAD["RightFoot"] - HEAD["RightLowerLeg"]).length
HIP_Z = HEAD["Hips"].z
HIPJ = {s: HEAD[s + "UpperLeg"] - HEAD["Hips"] for s in ("Left", "Right")}       # hip joints from the hips bone
ANKLE_REST = {s: HEAD[s + "Foot"].copy() for s in ("Left", "Right")}
ANKLE_H = ANKLE_REST["Right"].z
# foot landmarks relative to the point on the ground under the ankle (rest, armature axes)
HEEL = Vector((0, 0.075, 0))
BALL = Vector((0, HEAD["RightToes"].y - ANKLE_REST["Right"].y, 0))
log(f"legs {L_UP:.3f} + {L_LO:.3f} m, hip joint {HEAD['RightUpperLeg'].z:.3f} m, ankle {ANKLE_H:.3f} m, ball {BALL.y:.3f} m ahead of the ankle")


# ------------------------------------------------------------------ the legs
def leg_ik(P, side, ankle, knee_dir, foot_R):
    """Hip -> knee -> ankle onto `ankle`, knee toward knee_dir; foot (and toes, flat on the ground when the heel
    is up) to foot_R. Returns how far the ankle was short (m)."""
    W = P.world()
    hip = W[side + "UpperLeg"].translation
    d = ankle - hip
    dist = d.length
    short = max(0.0, dist - (L_UP + L_LO) * 0.995)
    dd = min(dist, (L_UP + L_LO) * 0.995)
    x = d.normalized()
    cos_a = max(-1.0, min(1.0, (L_UP * L_UP + dd * dd - L_LO * L_LO) / (2 * L_UP * dd)))
    k = Vector(knee_dir); k = (k - x * k.dot(x))
    k = k.normalized() if k.length > 1e-6 else FWD.copy()
    knee = hip + x * (L_UP * cos_a) + k * (L_UP * math.sqrt(max(0.0, 1 - cos_a * cos_a)))
    lat = (knee - hip).cross(hip + x * dd - knee)          # knee hinge axis
    lat = lat.normalized() if lat.length > 1e-6 else LEFT.copy()
    if lat.dot(LEFT) < 0: lat = -lat
    P.aim(side + "UpperLeg", knee, roll_ref=lat)
    P.aim(side + "LowerLeg", hip + x * dd, roll_ref=lat)
    P.set_world_rot(side + "Foot", foot_R)
    return short


FOOT_REST_R = {s: REST[s + "Foot"].to_3x3() for s in ("Left", "Right")}
TOES_REST_R = {s: REST[s + "Toes"].to_3x3() for s in ("Left", "Right")}


def foot_pose(side, ground, yaw, pitch, pivot):
    """Foot on the ground: `ground` = point under the ankle (rest), yaw (deg, + = toe to the left), pitch (deg,
    + = toe up) about the heel ('heel') or the ball ('ball') or the ankle ('ankle'). Returns ankle position,
    foot rotation, toes rotation (toes flat when the heel lifts on the ball)."""
    Ry_ = Rz(yaw)
    lat = Ry_ @ LEFT
    Rp = Rax(-pitch, lat)            # + pitch = toe up
    R = Rp @ Ry_
    ank_off = Vector((0, 0, ANKLE_H))
    if pivot == "heel":
        pv = ground + Ry_ @ HEEL
    elif pivot == "ball":
        pv = ground + Ry_ @ BALL
    else:
        pv = ground + ank_off
    ankle = pv + Rp @ (ground + ank_off - pv)
    foot_R = R @ FOOT_REST_R[side]
    toes_R = (Ry_ if (pivot == "ball" and pitch < 0) else R) @ TOES_REST_R[side]
    return ankle, foot_R, toes_R


# ------------------------------------------------------------------ upper body
def upper_body(P, hips_R, chest_yaw, lean, roll=0.0, breathe=0.0, head_pitch=0.0):
    """Chest turned back toward the aim and leaned; neck and head level and facing forward."""
    face = Rz(chest_yaw)
    for n, share in (("Chest", 0.45), ("UpperChest", 0.55)):
        W = P.world()
        cur = (W[PAR[n]] @ REL[n]).to_3x3()
        # target world rotation: rest orientation, turned to face forward, leaned
        tgt = Rz(chest_yaw * 0.0) @ Rax(lean * (0.5 if n == "Chest" else 1.0) + breathe, face @ LEFT) @ Rax(roll, face @ FWD) @ face @ REST[n].to_3x3()
        P.set_world_rot(n, cur.slerp if False else tgt)
    P.set_world_rot("Neck", Rax(lean * 0.3, LEFT) @ REST["Neck"].to_3x3())
    P.set_world_rot("Head", Rax(head_pitch, LEFT) @ REST["Head"].to_3x3())


FINGERS = ("Index", "Middle", "Ring", "Pinky")


def hands_relaxed(P, grip=0.0):
    for s in ("Left", "Right"):
        for i, f in enumerate(FINGERS):
            base = 22 + 6 * i + 40 * grip
            for k, a in enumerate((base, base * 1.2, base * 0.8)):
                P.q[f"{s}Hand{f}{k + 1}"] = Quaternion((1, 0, 0), math.radians(a))
        for k, a in enumerate((12 + 20 * grip, 10 + 25 * grip, 8 + 20 * grip)):
            P.q[f"{s}HandThumb{k + 1}"] = Quaternion((1, 0, 0), math.radians(a))
    P.dirty()


def arms(P, swing_r, swing_l, elbow, out=12.0, fwd_bias=10.0, hand_turn=0.0):
    """Upper arms hang (out from the body by `out` deg) and swing forward/back by swing_* deg about the
    shoulder; elbows bent by `elbow` deg; the forearm points forward-in, hands neutral."""
    W = P.world()
    chest_R = W["UpperChest"].to_3x3() @ REST["UpperChest"].to_3x3().inverted()
    f, l, u = chest_R @ FWD, chest_R @ LEFT, chest_R @ UP
    for s, sw, sgn in (("Right", swing_r, -1.0), ("Left", swing_l, 1.0)):
        W = P.world()
        sh = W[s + "UpperArm"].translation
        a = math.radians(sw + fwd_bias); o = math.radians(out)
        d = (-u * math.cos(a) + f * math.sin(a)) * math.cos(o) + l * sgn * math.sin(o)
        a_len = (HEAD[s + "LowerArm"] - HEAD[s + "UpperArm"]).length
        elbow_p = sh + d.normalized() * a_len
        hinge = d.cross(l * sgn).normalized() if abs(d.normalized().dot(l)) < 0.95 else f
        P.aim(s + "UpperArm", elbow_p)
        W = P.world()
        # forearm: bent forward from the upper arm by `elbow`, turned a little inward
        e = math.radians(elbow)
        fa = (d.normalized() * math.cos(e) + (f * math.cos(math.radians(15)) - l * sgn * math.sin(math.radians(15))) * math.sin(e)).normalized()
        f_len = (HEAD[s + "Hand"] - HEAD[s + "LowerArm"]).length
        P.aim(s + "LowerArm", W[s + "LowerArm"].translation + fa * f_len)
        W = P.world()
        # hand continues the forearm, palm toward the body
        L = W[s + "LowerArm"].to_3x3()
        P.set_world_rot(s + "Hand", Rax(hand_turn * sgn, fa) @ L @ (REST[s + "LowerArm"].to_3x3().inverted() @ REST[s + "Hand"].to_3x3()))


# ------------------------------------------------------------------ pistol in both hands (the match intro)
# The game's own pistol hold (grip_hold.json, read off the baked Pistol_Idle by grip_hold_json.py, same skeleton): the
# aim pose's arm chain (its shape relative to the chest), every finger, and each hand's frame in the gun's frame
# (gun frame: origin on the grip, muzzle -Y, top +Z). Low ready is that aim pose with both arms turned down together
# at the shoulders, the way a shooter lowers the gun: elbows, forearms and wrists keep the aim's straight lines and
# both hands stay locked on the grip (both shoulder joints lie on the turning axis). The game puts the gun on the
# animated right hand the same way.
GRIP = json.load(open(PROJECT + "/Blender/grip_hold.json"))
HAND_IN_GUN = {s: Matrix(GRIP[s]["hand_in_gun"]) for s in ("Left", "Right")}
GRIP_ARM = {s: {n: Quaternion(q) for n, q in GRIP[s]["arm"].items()} for s in ("Left", "Right")}
GRIP_FINGERS = {s: {n: Quaternion(q) for n, q in GRIP[s]["fingers"].items()} for s in ("Left", "Right")}
LOW_READY_DIP = 40.0          # degrees the muzzle points below level at low ready


# How the gun rides: held closer than the extended aim with relaxed elbows, floating on its own slight lag.
GUN_PULL, GUN_LIFT = 0.10, 0.03        # metres back toward the chest and up, from the arms-lowered aim
GUN_BOB_M, GUN_BOB_DEG = 0.006, 1.5    # per footfall: how far the gun settles (m) and dips (deg), a beat after the body
GUN_DRIFT_DEG = 1.2                    # per stride: a slow left-right drift of the muzzle
GUN_LAG = 1.9                          # radians the gun's settle trails the footfall


def _basis(y, n):
    y = y.normalized(); n = (n - y * n.dot(y)).normalized()
    return Matrix((y, n, y.cross(n))).transposed()


def arm_to(P, side, H):
    """Arm onto the hand frame H, solved from the aim pose already on the arm: the elbow keeps the aim's side of the
    shoulder-wrist line, both bones turn the least from the aim, and the hand's remaining twist is mostly taken by
    the forearm (as a real forearm turns), so the wrist keeps the aim's straight line."""
    W = P.world()
    S, E0, W0 = W[side + "UpperArm"].translation, W[side + "LowerArm"].translation, W[side + "Hand"].translation
    U0, L0, H0 = W[side + "UpperArm"].to_3x3(), W[side + "LowerArm"].to_3x3(), W[side + "Hand"].to_3x3()
    a, b = (E0 - S).length, (W0 - E0).length
    d = H.translation - S
    dist = max(0.05, min(d.length, (a + b) * 0.995))
    x, x0 = d.normalized(), (W0 - S).normalized()
    off0 = (E0 - S) - x0 * (E0 - S).dot(x0)
    k = x0.rotation_difference(x) @ off0
    k = k - x * k.dot(x)
    k = k.normalized() if k.length > 1e-6 else -UP
    cos_a = max(-1.0, min(1.0, (a * a + dist * dist - b * b) / (2 * a * dist)))
    E = S + x * (a * cos_a) + k * (a * math.sqrt(max(0.0, 1 - cos_a * cos_a)))
    Wr = S + x * dist
    n0 = (E0 - S).cross(W0 - E0)
    n0 = n0 if n0.length > 1e-5 else x0.cross(off0 if off0.length > 1e-6 else -UP)
    n = (E - S).cross(Wr - E)
    n = n if n.length > 1e-5 else x.cross(k)
    P.set_world_rot(side + "UpperArm", _basis(E - S, n) @ _basis(E0 - S, n0).inverted() @ U0)
    Lq = _basis(Wr - E, n) @ _basis(W0 - E0, n0).inverted() @ L0
    want = H.to_3x3().normalized()
    D = (want @ (Lq @ (L0.inverted() @ H0)).inverted()).to_quaternion()
    ax = (Wr - E).normalized()
    tw = Vector((D.x, D.y, D.z)).dot(ax)
    twist = 2.0 * math.atan2(tw, D.w)
    P.set_world_rot(side + "LowerArm", Matrix.Rotation(twist * 0.7, 3, ax) @ Lq)
    P.set_world_rot(side + "Hand", want)


def pistol_hold(P, step=0.0, stride=0.0, amp=1.0, dip=LOW_READY_DIP, pull=GUN_PULL, lift=GUN_LIFT):
    """Both hands on the pistol at low ready on the current chest. step / stride: the footfall (two per cycle) and
    stride (one per cycle) angles, radians, for the gun's float; amp scales the float (standing: small).
    dip / pull / lift: how far the muzzle points below level (deg) and how far the gun comes back and up (m).
    Returns the gun's frame (armature space)."""
    for side, sgn in (("Left", 1.0), ("Right", -1.0)):
        for n, q in GRIP_ARM[side].items():
            P.q[n] = q.copy()
        for n, q in GRIP_FINGERS[side].items():
            P.q[n] = q.copy()
    P.dirty()
    # shoulders lift a touch in turn with the stride
    for side, sgn in (("Left", 1.0), ("Right", -1.0)):
        W = P.world()
        P.set_world_rot(side + "Shoulder", Rax(1.2 * amp * sgn * math.sin(stride), FWD) @ W[side + "Shoulder"].to_3x3())
    W = P.world()
    G = W["RightHand"] @ HAND_IN_GUN["Right"].inverted()
    muzzle = (G.to_3x3() @ Vector((0, -1, 0))).normalized()
    pitch = math.degrees(math.asin(max(-1.0, min(1.0, -muzzle.z))))          # + = below level
    pR = W["RightUpperArm"].translation
    axis = (W["LeftUpperArm"].translation - pR).normalized()
    fwd = axis.cross(UP).normalized()
    G = Matrix.Translation(pR) @ Rax(dip - pitch, axis).to_4x4() @ Matrix.Translation(-pR) @ G
    G = Matrix.Translation(-fwd * pull + UP * lift) @ G
    # the float: a lagged settle per footfall and a slow drift per stride, about the grip
    g0 = G.translation.copy()
    rot = Rax(GUN_BOB_DEG * amp * math.cos(step - GUN_LAG), axis) @ Rz(GUN_DRIFT_DEG * amp * math.sin(stride - 0.7))
    G = Matrix.Translation(g0 + UP * (GUN_BOB_M * amp * math.cos(step - GUN_LAG))) @ rot.to_4x4() @ Matrix.Translation(-g0) @ G
    for side in ("Right", "Left"):
        arm_to(P, side, G @ HAND_IN_GUN[side])
    P.dirty()
    W = P.world()
    P.gun = W["RightHand"] @ HAND_IN_GUN["Right"].inverted()
    return P.gun


def torso_overlap(P, hips_yaw, chest_yaw, lean, roll):
    """Spine: the chest halfway between the hips' turn and its own, the upper chest fully on its own (the twist
    spreads over both joints), leaned and rolled."""
    for n, yaw, ln, rl in (("Chest", 0.5 * (hips_yaw + chest_yaw), lean * 0.5, roll * 0.5), ("UpperChest", chest_yaw, lean, roll)):
        face = Rz(yaw)
        P.set_world_rot(n, Rax(ln, face @ LEFT) @ Rax(rl, face @ FWD) @ face @ REST[n].to_3x3())


def head_follow(P, chest_yaw, yaw, pitch, roll):
    """Neck between the chest and the head; the head follows the body loosely (yaw), looks a little down the path
    (pitch, + = down) and rolls a touch with the step."""
    fn = Rz(0.5 * (chest_yaw + yaw))
    P.set_world_rot("Neck", Rax(pitch * 0.4, fn @ LEFT) @ fn @ REST["Neck"].to_3x3())
    fh = Rz(yaw)
    P.set_world_rot("Head", Rax(pitch, fh @ LEFT) @ Rax(roll, fh @ FWD) @ fh @ REST["Head"].to_3x3())


def pistol_body(P, t, g, duty):
    """Upper body of a pistol gait with overlapping action: the chest trails the hips, the head trails the chest,
    the gun floats a beat behind the footfall."""
    tw, rl, counter = g.get("pelvis_twist", 6.0), g.get("pelvis_roll", 4.0), g.get("counter", 0.7)
    def hyaw(tt): return tw * math.sin(2 * math.pi * (tt - duty / 2) + math.pi / 2)
    def hroll(tt): return rl * math.sin(2 * math.pi * (tt - duty / 2))
    step = 4 * math.pi * (t - duty / 2)
    chest_yaw = -counter * hyaw(t - 0.06)
    torso_overlap(P, hyaw(t), chest_yaw, g["lean"] + 0.8 * math.sin(step - 1.0), -0.6 * hroll(t - 0.06))
    head_follow(P, chest_yaw, -0.35 * counter * hyaw(t - 0.12), g.get("look_down", 8.0) + 1.2 * math.sin(step - 2.0), -0.3 * hroll(t - 0.12))
    pistol_hold(P, step=step, stride=2 * math.pi * (t - duty / 2), amp=1.0)


# ------------------------------------------------------------------ locomotion
def gait_pose(t, g):
    """One frame of a gait. t in [0, 1): left foot touches down at t = 0, right at t = 0.5."""
    P = Pose()
    T = g["frames"] / FPS
    v = g["speed"]
    D = Vector(g["dir"]).normalized()                 # travel, body space (x left, y forward) -> armature
    Dw = (LEFT * D.x + FWD * D.y).normalized()
    duty = g["duty"]
    S = v * duty * T                                  # stance sweep (ground under the planted foot)
    # hips turn toward the travel (knees follow the legs), chest turns back to the aim
    psi = g.get("hip_yaw", 0.0)
    bob = g["bob"]; drop = g["drop"]
    # pelvis height: lowest at each foot's mid-stance
    zc = HIP_Z - drop - bob * math.cos(4 * math.pi * (t - duty / 2))
    sway = g.get("sway", 0.01) * math.sin(2 * math.pi * (t - duty / 2))       # toward the stance foot (left at t ~ duty/2)
    hip_yaw = psi + g.get("pelvis_twist", 6.0) * math.sin(2 * math.pi * (t - duty / 2) + math.pi / 2) * (1 if D.y >= -0.3 else -1)
    hip_roll = g.get("pelvis_roll", 4.0) * math.sin(2 * math.pi * (t - duty / 2))
    hip_pitch = g.get("pelvis_pitch", 5.0)
    hips_R = Rz(hip_yaw) @ Rax(hip_roll, Rz(hip_yaw) @ FWD) @ Rax(hip_pitch, Rz(hip_yaw) @ LEFT) @ REST["Hips"].to_3x3()
    hips_pos = Vector((0, HEAD["Hips"].y, zc)) + LEFT * sway
    P.set_hips_world(hips_pos, hips_R)
    feet = gait_feet(t, g)
    # the hips sink as far as a planted foot needs (a knee never locks straight on the ground)
    reach = (L_UP + L_LO) * 0.975
    for _ in range(3):
        dz = 0.0
        W = P.world()
        for side, (ankle, foot_R, toes_R, knee_dir, planted) in feet.items():
            if not planted: continue
            hip = W[side + "UpperLeg"].translation
            h = Vector((ankle.x - hip.x, ankle.y - hip.y, 0)).length
            vmax = math.sqrt(max(0.0, reach * reach - h * h))
            dz = max(dz, (hip.z - ankle.z) - vmax)
        if dz <= 1e-4: break
        hips_pos = hips_pos - UP * dz
        P.set_hips_world(hips_pos, hips_R)
    shorts = []
    for side, (ankle, foot_R, toes_R, knee_dir, planted) in feet.items():
        s_ = leg_ik(P, side, ankle, knee_dir, foot_R)
        shorts.append(s_ if planted else 0.0)
        P.set_world_rot(side + "Toes", toes_R)
    # upper body: chest square to the aim
    a = g.get("arm_swing", 18.0) * math.sin(2 * math.pi * (t - duty / 2) - math.pi / 2 + math.pi)
    if g.get("pistol"):
        pistol_body(P, t, g, duty)
    else:
        upper_body(P, hips_R, 0.0, g["lean"], roll=-hip_roll * 0.6, breathe=0.0, head_pitch=-g["lean"] * 0.8)
        arms(P, a, -a, g.get("elbow", 70.0), out=g.get("arm_out", 12.0))
        hands_relaxed(P, g.get("grip", 0.3))
    return P, max(shorts)


def gait_feet(t, g):
    """Foot targets for one frame: side -> (ankle, foot rotation, toes rotation, knee direction, planted)."""
    T = g["frames"] / FPS
    v = g["speed"]
    D = Vector(g["dir"]).normalized()
    Dw = (LEFT * D.x + FWD * D.y).normalized()
    duty = g["duty"]
    S = v * duty * T
    psi = g.get("hip_yaw", 0.0)
    land = g.get("land", 0.5)      # runners land close under the body and push off far behind (0.5 = symmetric)
    out = {}
    for side, ph in (("Left", 0.0), ("Right", 0.5)):
        u = (t - ph) % 1.0
        sgn = 1.0 if side == "Left" else -1.0
        width = g.get("width", 0.11)
        # the left foot runs on the body's left of the travel line (backpedalling, "left of the travel" would be
        # the body's right: crossed legs); pure strafes have no body-left across the travel, so there the trailing
        # foot's line is in front
        perp = LEFT - Dw * LEFT.dot(Dw)
        perp = Vector((-Dw.y, Dw.x, 0)).normalized() if perp.length < 0.2 else perp.normalized()
        # the two feet run on parallel lines either side of the travel line (never on the same line: no scissoring)
        base = Vector((0, HEAD["Hips"].y, 0)) + perp * sgn * width
        if perp.dot(LEFT) ** 2 < 0.1:                           # pure strafe: keep some hip width sideways too
            base = base + LEFT * sgn * g.get("side_w", 0.0)
        yaw = psi * 0.85 + g.get("toe_out", 7.0) * sgn
        if u < duty:                                            # stance: the ground point slides back under the body
            s = u / duty
            ground = base + Dw * (S * land - S * s)                # lands `land` of the sweep ahead, pushes off the rest behind
            if s < 0.14:                                        # heel strike -> flat
                pitch, pivot = g["strike"] * (1 - smooth(s / 0.14)), "heel" if g["strike"] > 0 else "ball"
            elif s > g.get("heel_off", 0.55):                   # heel rises, the foot rolls onto the ball
                pitch, pivot = -g["toe_off"] * smooth((s - g.get("heel_off", 0.55)) / (1 - g.get("heel_off", 0.55))), "ball"
            else:
                pitch, pivot = 0.0, "heel"
            lift = 0.0
        else:                                                   # swing: back up, through, forward and down
            s = (u - duty) / (1 - duty)
            e = s * s * (3 - 2 * s)
            e = 0.6 * e + 0.4 * (1 - (1 - s) ** 2.2) if g.get("kick", 0.0) == 0 else e ** 1.25
            ground = base + Dw * (-S * (1 - land) + S * e)
            lift = g["lift"] * math.sin(math.pi * min(1.0, s * 1.04) ** g.get("lift_skew", 0.7))   # peaks early: the heel comes up behind
            pitch = -g["toe_off"] * (1 - smooth(s / 0.35)) + g["strike"] * smooth((s - 0.7) / 0.3)
            pivot = "ankle"
        if g.get("slope"):
            # stairs going down: the ground under a foot is lower the further ahead it is, on the line through the
            # tread centres (each planted foot sits flat on a tread centre; the swing arcs over the nosing)
            ground = ground + UP * (-g["slope"] * (ground - Vector((0, HEAD["Hips"].y, 0))).dot(Dw))
        ankle, foot_R, toes_R = foot_pose(side, ground, yaw, pitch, pivot)
        leg_fwd = Rz(yaw - g.get("toe_out", 7.0) * sgn) @ FWD        # where the knee points (the hips' facing)
        if u >= duty:
            # the heel kicks up behind the body on the way through (the knee folds backward whatever the travel:
            # behind the runner, behind a strafing leg, toward the travel when backpedalling)
            ankle = ankle + UP * lift + (-leg_fwd) * g.get("kick", 0.0) * math.sin(math.pi * min(1.0, s * 1.6)) ** 2 * (1 if s < 0.6 else 0.0)
        # strafes: the knees open toward the travel (a sideways leg leads with the knee; keeps the knees apart
        # when the trailing leg crosses in front)
        knee_dir = leg_fwd + UP * 0.1 + Dw * g.get("knee_travel", 0.0)
        out[side] = (ankle, foot_R, toes_R, knee_dir, u < duty)
    return out


GAITS = {}
JOG = 4.0 / GAME_SCALE
SPRINT = 6.5 / GAME_SCALE                   # PlayerController.sprintSpeed (8 made the short legs lunge)
CROUCH = 2.5 / GAME_SCALE
# 2026-11-16 calm pass (user: "legs moving too much"): 200 steps/min looked frantic -> 180 (20 frames), knee lift
# and heel flick cut (lift 0.16 -> 0.10, kick 0.07 -> 0.025), softer toe-off; stance sweep 3.8 x 0.23 x 0.667 = 0.58 m
base_jog = dict(frames=20, speed=JOG, duty=0.23, land=0.42, bob=0.03, drop=0.03, lift=0.10, kick=0.025, strike=8.0, toe_off=24.0,
                lean=8.0, arm_swing=30.0, elbow=88.0, pelvis_twist=2.5, pelvis_roll=1.2, pelvis_pitch=4.0, sway=0.004, lift_skew=1.0)
GAITS["Run"] = dict(base_jog, dir=(0, 1))
SIDE, BACK = 0.75, 0.75                     # PlayerController.strafeSpeedFactor / backSpeedFactor
GAITS["Run_Back"] = dict(base_jog, dir=(0, -1), speed=JOG * BACK, land=0.45, duty=0.30, strike=-8.0, toe_off=16.0, lean=-2.0, kick=0.015, lift=0.07, arm_swing=12.0, pelvis_pitch=2.0)
# A/D, shooter style (COD / IGI): chest, head and gun stay on the crosshair, only the legs travel sideways. Hips
# turn a little toward the travel (20 deg), the lead foot's toes open toward it, the trailing leg crosses in
# front of the lead leg on its own line (no scissoring), feet land on the ball (no heel strike sideways), knees
# bent low and athletic, quicker steps (200/min) so the sideways stride stays short.
# 2026-11-16 calm pass: less crouch (drop 0.07 -> 0.035), knees no longer flung open toward the travel
# (knee_travel 0.5 -> 0.15), low feet (lift 0.12 -> 0.07, kick 0.07 -> 0.015), the feet keep a little of the hip
# width sideways (side_w) so the legs pass close instead of scissoring wide across each other.
strafe = dict(base_jog, frames=16, speed=JOG * SIDE, duty=0.30, land=0.5, hip_yaw=22.0, toe_out=6.0, width=0.06, side_w=0.10, knee_travel=0.15, lean=4.0, drop=0.035, bob=0.012,
              lift=0.07, kick=0.015, strike=-4.0, toe_off=14.0, heel_off=0.5, arm_swing=5.0, elbow=80.0,
              pelvis_twist=1.5, pelvis_roll=1.0, pelvis_pitch=3.0, sway=0.0, lift_skew=1.0)
GAITS["Strafe_Left"] = dict(strafe, dir=(1, 0))
GAITS["Strafe_Right"] = dict(strafe, dir=(-1, 0), hip_yaw=-22.0)
GAITS["Run_FwdLeft"] = dict(base_jog, speed=JOG * math.sqrt(0.5 + 0.5 * SIDE * SIDE), dir=(0.7071, 0.7071), hip_yaw=30.0, arm_swing=14.0)
GAITS["Run_FwdRight"] = dict(base_jog, speed=JOG * math.sqrt(0.5 + 0.5 * SIDE * SIDE), dir=(-0.7071, 0.7071), hip_yaw=-30.0, arm_swing=14.0)
GAITS["Run_BackLeft"] = dict(GAITS["Run_Back"], speed=JOG * math.sqrt(0.5 * BACK * BACK + 0.5 * SIDE * SIDE), dir=(0.7071, -0.7071), hip_yaw=-30.0)
GAITS["Run_BackRight"] = dict(GAITS["Run_Back"], speed=JOG * math.sqrt(0.5 * BACK * BACK + 0.5 * SIDE * SIDE), dir=(-0.7071, -0.7071), hip_yaw=30.0)
# calm pass: 257 -> 225 steps/min (16 frames), knee lift 0.24 -> 0.15, heel kick 0.13 -> 0.05
GAITS["Sprint"] = dict(frames=16, speed=SPRINT, dir=(0, 1), duty=0.20, land=0.40, bob=0.04, drop=0.04, lift=0.15, kick=0.05, strike=8.0, toe_off=30.0,
                       lean=11.0, arm_swing=36.0, elbow=88.0, pelvis_twist=4.0, pelvis_roll=1.5, pelvis_pitch=6.0, sway=0.004, lift_skew=1.0)
GAITS["Walk"] = dict(frames=32, speed=1.5 / GAME_SCALE, dir=(0, 1), duty=0.62, bob=0.015, drop=0.04, lift=0.06, kick=0.0, strike=16.0, toe_off=26.0,
                     lean=3.0, arm_swing=14.0, elbow=25.0, pelvis_twist=3.0, pelvis_roll=2.0, pelvis_pitch=2.0, sway=0.012, heel_off=0.6)
# the match intro: a shooter's walk with the pistol at low ready. Knees kept bent (the hips ride low and level), short
# heel-to-toe steps at 120/min, no arm swing (both hands on the gun), chest quiet over the hips.
GAITS["Walk_Pistol"] = dict(frames=30, speed=1.4 / GAME_SCALE, dir=(0, 1), duty=0.60, bob=0.016, drop=0.055, lift=0.07, kick=0.0, strike=14.0,
                            toe_off=24.0, lean=6.0, pelvis_twist=6.0, pelvis_roll=3.0, pelvis_pitch=5.0, sway=0.014, heel_off=0.58, width=0.11,
                            toe_out=8.0, counter=0.7, gun_bob=1.5, pistol=True)
# Down the helipad's steel stairs (Blender/fortress_base.py: tread ST_RUN 0.27 m, 16 risers of 0.192 m), pistol at
# low ready: one tread per step at 120 steps/min, toes land first, the rear knee folds deep. Each cycle covers two
# treads; with duty x land = 0.25 every foot lands on a tread centre when the game plays the clip by position
# (IntroCutscene: phase = metres down the flight / (2 x tread)). The game's stair collider runs through the tread
# centres, so the body's origin is always on that line.
STAIR_TREAD, STAIR_RISE = 0.27, 0.192
GAITS["Stairs_Down_Pistol"] = dict(frames=30, speed=2 * STAIR_TREAD / 1.0 / GAME_SCALE, dir=(0, 1), duty=0.62, land=0.25 / 0.62,
                                   slope=STAIR_RISE / STAIR_TREAD, bob=0.012, drop=0.04, lift=0.10, kick=0.0, strike=-14.0, toe_off=14.0,
                                   lean=3.0, pelvis_twist=4.0, pelvis_roll=3.5, pelvis_pitch=2.0, sway=0.016, heel_off=0.65, width=0.11,
                                   toe_out=8.0, counter=0.6, gun_bob=1.2, pistol=True)
GAITS["Crouch_Walk"] = dict(frames=24, speed=CROUCH, dir=(0, 1), duty=0.55, bob=0.012, drop=0.25, lift=0.07, kick=0.0, strike=10.0, toe_off=24.0,
                            lean=20.0, arm_swing=8.0, elbow=60.0, pelvis_twist=2.0, pelvis_roll=1.5, pelvis_pitch=8.0, sway=0.008, width=0.13)


# ------------------------------------------------------------------ stances
def stance_pose(t, s):
    """Idle / crouch idle: feet planted apart (left a little ahead), weight shifting, breathing."""
    P = Pose()
    shift = s["shift"] * math.sin(2 * math.pi * t)
    zc = HIP_Z - s["drop"] - 0.006 * (0.5 - 0.5 * math.cos(4 * math.pi * t))
    hip_yaw = s.get("yaw", 12.0)
    hips_R = Rz(hip_yaw) @ Rax(s.get("pelvis_pitch", 4.0), LEFT) @ Rax(shift * 60, FWD) @ REST["Hips"].to_3x3()
    P.set_hips_world(Vector((0, HEAD["Hips"].y + s.get("back", 0.0), zc)) + LEFT * shift, hips_R)
    for side, sgn in (("Left", 1.0), ("Right", -1.0)):
        ground = Vector((sgn * s["width"], HEAD["Hips"].y, 0)) + FWD * s["stagger"] * sgn
        ankle, foot_R, toes_R = foot_pose(side, ground, hip_yaw * 0.7 + 10 * sgn, s.get("heel", {}).get(side, 0.0), "ball")
        leg_ik(P, side, ankle, Rz(hip_yaw * 0.7 + 4 * sgn) @ FWD + UP * 0.1 + LEFT * sgn * s.get("knee_out", 0.1), foot_R)
        P.set_world_rot(side + "Toes", toes_R)
    breathe = 1.2 * math.sin(2 * math.pi * t * s.get("breaths", 3))
    if s.get("pistol"):
        # standing ready: breathing in the chest, the head drifting a little as it watches, the gun barely moving
        br = 2 * math.pi * t * s.get("breaths", 3)
        look = 6.0 * math.sin(2 * math.pi * t) + 2.0 * math.sin(6 * math.pi * t + 1.0)
        torso_overlap(P, s.get("yaw", 12.0) * 0.5, 0.0, s["lean"] + 0.8 * math.sin(br), 0.0)
        head_follow(P, 0.0, look, s.get("look_down", 6.0) + 1.0 * math.sin(br - 0.8), 0.6 * math.sin(2 * math.pi * t + 2.0))
        pistol_hold(P, step=br, stride=2 * math.pi * t, amp=0.35)
    else:
        upper_body(P, hips_R, 0.0, s["lean"], breathe=breathe, head_pitch=-s["lean"] * 0.7)
        arms(P, 4 * math.sin(2 * math.pi * t), 4 * math.sin(2 * math.pi * t + 1), s.get("elbow", 30.0), out=s.get("arm_out", 10.0), fwd_bias=s.get("arm_fwd", 8.0))
        hands_relaxed(P, s.get("grip", 0.25))
    return P, 0.0


STANCES = {
    "Idle_Aim": dict(frames=120, shift=0.008, drop=0.018, width=0.12, stagger=0.06, lean=5.0, yaw=8.0, elbow=45.0, grip=0.45, arm_fwd=15.0),   # calm: shoulder-width, slight knee bend
    "Idle_Pistol": dict(frames=120, shift=0.006, drop=0.05, width=0.13, stagger=0.09, lean=6.0, yaw=10.0, pistol=True),   # intro: stands at low ready
    "Idle": dict(frames=150, shift=0.018, drop=0.025, width=0.14, stagger=0.05, lean=2.0, yaw=8.0, elbow=20.0, grip=0.2, arm_fwd=4.0),
    "Crouch_Idle": dict(frames=120, shift=0.008, drop=0.30, width=0.17, stagger=0.16, lean=18.0, yaw=16.0, elbow=60.0, grip=0.45, arm_fwd=20.0,
                        pelvis_pitch=14.0, knee_out=0.3, heel={"Right": -18.0}),
}


# ------------------------------------------------------------------ seated (the match intro: on a troop seat in the helicopter)
# Sits on a low troop seat (the cabin is only 1.3 m tall), feet flat on the floor, knees a little apart, leaned in
# over them, pistol held low between the knees, muzzle down, watching out of the open door. The seat: its top SEAT_Z
# above the clip's origin (the floor), its front edge SEAT_EDGE ahead of the origin (model metres; the game places
# the body by these, x GAME_SCALE, and builds the seats to match: Blender/heli_refit.py SEAT_H 0.34 m).
SEAT_Z = 0.323
SEAT_EDGE = 0.20
SEATS = {
    "Sit_Pistol": dict(frames=150, tilt=-6.0, lean=20.0, feet_fwd=0.43, feet_apart=0.16, toe_out=12.0, heel_lift=4.0,
                       look_down=8.0, scan=10.0, dip=55.0, pull=0.04, lift=0.07),
}


def sit_pose(t, s):
    P = Pose()
    w = 2 * math.pi * t
    br = w * 2                                                    # two breaths per loop
    # pelvis: the hip joints over the back half of the seat, the pelvis rolled back as a seated pelvis is, a slow sway
    sway = 1.0 * math.sin(w + 0.4)
    hips_R = Rax(sway, FWD) @ Rax(s["tilt"], LEFT) @ REST["Hips"].to_3x3()
    hip_joint_z = SEAT_Z + 0.09
    hip_joint_y = -SEAT_EDGE + 0.31                               # 0.31 behind the front edge
    P.set_hips_world(Vector((0, hip_joint_y, hip_joint_z)) - hips_R @ (REST["Hips"].to_3x3().inverted() @ Vector((0, HIPJ["Right"].y, HIPJ["Right"].z))), hips_R)
    # legs: feet flat on the floor ahead of the seat, knees up and a little apart; one heel lifts and settles now and then
    for side, sgn in (("Left", 1.0), ("Right", -1.0)):
        ground = Vector((sgn * s["feet_apart"], hip_joint_y, 0)) + FWD * s["feet_fwd"]
        heel = s["heel_lift"] * max(0.0, math.sin(w + (0.0 if sgn > 0 else 2.6))) ** 3
        ankle, foot_R, toes_R = foot_pose(side, ground, s["toe_out"] * sgn, -heel, "ball")
        leg_ik(P, side, ankle, FWD + UP * 0.6 + LEFT * sgn * 0.25, foot_R)
        P.set_world_rot(side + "Toes", toes_R)
    # upper body: leaned in over the knees, breathing; the head looks out and a little down, slowly scanning
    torso_overlap(P, 0.0, 0.0, s["lean"] + 0.8 * math.sin(br), sway * 0.5)
    look = s["scan"] * math.sin(w) + 3.0 * math.sin(2 * w + 1.0)
    head_follow(P, 0.0, look, s["look_down"] + 1.5 * math.sin(w + 1.3) + 0.8 * math.sin(br - 0.8), 0.6 * math.sin(w + 2.0))
    pistol_hold(P, step=br, stride=w, amp=0.3, dip=s["dip"], pull=s["pull"], lift=s["lift"])
    return P, 0.0


# ------------------------------------------------------------------ one-shots
def fall_pose(t, _):
    P = Pose()
    k = math.sin(2 * math.pi * t)
    hips_R = Rax(-6, LEFT) @ REST["Hips"].to_3x3()
    P.set_hips_world(Vector((0, HEAD["Hips"].y, HIP_Z)), hips_R)
    for side, sgn, ph in (("Left", 1.0, 0.0), ("Right", -1.0, math.pi)):
        kk = math.sin(2 * math.pi * t + ph)
        ground = Vector((sgn * 0.13, HEAD["Hips"].y, 0)) + FWD * (0.10 + 0.06 * kk)
        ankle, foot_R, toes_R = foot_pose(side, ground, 8 * sgn, -25.0, "ankle")
        ankle = ankle + UP * (0.25 + 0.05 * kk)
        leg_ik(P, side, ankle, FWD + UP * 0.2, foot_R)
        P.set_world_rot(side + "Toes", toes_R)
    upper_body(P, hips_R, 0.0, 6.0 + 2 * k, head_pitch=-4)
    arms(P, 10 + 6 * k, 10 - 6 * k, 40.0, out=30.0)
    hands_relaxed(P, 0.15)
    return P, 0.0


def death_pose(t, _):
    """Hit, knees give, falls forward onto the side and lies still (root stays: the body falls in place)."""
    P = Pose()
    a = smooth(t / 0.25)            # buckle
    b = smooth((t - 0.2) / 0.45)    # topple
    c = smooth((t - 0.6) / 0.25)    # settle
    zc = HIP_Z - 0.03 - 0.35 * a - 0.42 * b + 0.02 * math.sin(math.pi * c) * (1 - c)
    pitch = -8 * smooth(t / 0.1) + 25 * a + 55 * b
    roll = 70 * b
    hips_R = Rax(roll, FWD) @ Rax(pitch, LEFT) @ REST["Hips"].to_3x3()
    P.set_hips_world(Vector((0, HEAD["Hips"].y - 0.25 * b, zc)), hips_R)
    for side, sgn in (("Left", 1.0), ("Right", -1.0)):
        ground = Vector((sgn * 0.14, HEAD["Hips"].y, 0)) + FWD * (0.12 * a * sgn)
        ankle, foot_R, toes_R = foot_pose(side, ground, 10 * sgn, -20 * a, "ball")
        ankle = ankle.lerp(Vector((sgn * 0.2, HEAD["Hips"].y + 0.35, 0.12)), b)
        leg_ik(P, side, ankle, FWD + UP * 0.1 + LEFT * sgn * 0.3 * b, foot_R)
        P.set_world_rot(side + "Toes", toes_R)
    # the spine follows the hips down (local bends, not held upright in the world)
    for n, bend, twist in (("Chest", 12 * a + 10 * b, 6 * b), ("UpperChest", 8 * a + 8 * b, 8 * b), ("Neck", -5 * a + 10 * b, 0), ("Head", -8 * a + 15 * b, 20 * b)):
        P.q[n] = Quaternion((0, 1, 0), math.radians(twist)) @ Quaternion((1, 0, 0), math.radians(bend))
    P.dirty()
    arms(P, -20 * a + 25 * b, -10 * a + 15 * b, 30 + 40 * a - 25 * b, out=20 - 5 * b)   # arms end lying along the body
    hands_relaxed(P, 0.1)
    return P, 0.0


def upper_shot(t, kind):
    """Fire / reload / hit: an Idle_Aim body with the torso doing the move (the game masks these to the upper body)."""
    P, _ = stance_pose(0.0, STANCES["Idle_Aim"])
    if kind == "Fire":
        k = math.exp(-((t - 0.15) / 0.12) ** 2) if t > 0.05 else t / 0.05 * math.exp(-1.5)
        lean, roll, yaw = -5 * k, 0.0, 2 * k
    elif kind == "Reload":
        k = smooth(t / 0.2) * (1 - smooth((t - 0.75) / 0.25))
        lean, roll, yaw = 8 * k, -6 * k * math.sin(math.pi * t), -10 * k
    else:  # Hit_Reaction
        k = math.exp(-((t - 0.18) / 0.15) ** 2)
        lean, roll, yaw = -14 * k, 8 * k, 12 * k
    hips_R = P.world()["Hips"].to_3x3()
    upper_body(P, hips_R, yaw, STANCES["Idle_Aim"]["lean"] + lean, roll=roll, head_pitch=-lean * 0.5)
    arms(P, 0, 0, 45.0, out=10.0, fwd_bias=15.0)
    hands_relaxed(P, 0.45)
    return P, 0.0


CLIPS = []
for n, g in GAITS.items():
    CLIPS.append((n, g["frames"], True, lambda t, g=g: gait_pose(t, g)))
for n, s in STANCES.items():
    CLIPS.append((n, s["frames"], True, lambda t, s=s: stance_pose(t, s)))
for n, s in SEATS.items():
    CLIPS.append((n, s["frames"], True, lambda t, s=s: sit_pose(t, s)))
CLIPS.append(("Fall", 30, True, lambda t: fall_pose(t, None)))
CLIPS.append(("Death", 75, False, lambda t: death_pose(t, None)))
CLIPS.append(("Fire", 10, False, lambda t: upper_shot(t, "Fire")))
CLIPS.append(("Reload", 60, False, lambda t: upper_shot(t, "Reload")))
CLIPS.append(("Hit_Reaction", 18, False, lambda t: upper_shot(t, "Hit")))
if ONLY:
    CLIPS = [c for c in CLIPS if c[0] in ONLY]

# ------------------------------------------------------------------ bake
made = []
GUN_AT = {}                       # clip -> frame -> pistol frame (previews draw a stand-in gun there)
for name, frames, loop, fn in CLIPS:
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    rig.animation_data.action = act
    worst = 0.0
    last = frames + 1 if loop else frames
    for f in range(1, last + 1):
        t = ((f - 1) / frames) if loop else (f - 1) / max(1, frames - 1)
        P, short = fn(t % 1.0 if loop else t)
        worst = max(worst, short)
        if P.gun is not None:
            GUN_AT.setdefault(name, {})[f] = P.gun.copy()
        for n in NAMES:
            pbn = rig.pose.bones[n]
            pbn.rotation_quaternion = P.q[n]
            pbn.keyframe_insert("rotation_quaternion", frame=f, group=n)
        hp = rig.pose.bones["Hips"]
        hp.location = P.hips_loc
        hp.keyframe_insert("location", frame=f, group="Hips")
    act.use_frame_range = True
    act.frame_start, act.frame_end = 1, last
    act.use_cyclic = loop
    made.append((act, last))
    extra = ""
    if name in GAITS:
        g = GAITS[name]
        extra = f", {g['speed']:.2f} m/s model = {g['speed'] * GAME_SCALE:.2f} m/s in game, cadence {120 * FPS / g['frames']:.0f} steps/min"
    log(f"{name}: {last} frames{extra}; feet short of their targets by up to {worst * 1000:.0f} mm")

# ------------------------------------------------------------------ pictures
if RENDER:
    import numpy as np
    os.makedirs(RENDER, exist_ok=True)
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "OBJECT"
    mesh.color = (0.6, 0.56, 0.5, 1)
    sc.world = bpy.data.worlds.new("W")
    sc.world.color = (0.33, 0.36, 0.4)
    # a floor grid so sliding feet show
    bpy.ops.mesh.primitive_plane_add(size=6, location=(0, 0, 0))
    floor = bpy.context.active_object
    floor.color = (0.25, 0.27, 0.3, 1)

    def cam(name, loc, look, lens=40):
        c = bpy.data.objects.new(name, bpy.data.cameras.new(name)); sc.collection.objects.link(c)
        c.location = loc; c.rotation_euler = (Vector(look) - Vector(loc)).to_track_quat("-Z", "Y").to_euler(); c.data.lens = lens
        return c
    # a stand-in pistol (slide + grip, gun frame: origin on the grip, muzzle -Y, top +Z; the game's gun is x1.25)
    import bmesh
    gm = bpy.data.meshes.new("GunProxy"); bm = bmesh.new()
    for lo, hi in (((-0.014, -0.21, 0.02), (0.014, 0.035, 0.06)), ((-0.013, -0.005, -0.10), (0.013, 0.045, 0.025))):
        r = bmesh.ops.create_cube(bm, size=1.0)
        for v in r["verts"]:
            v.co = Vector(tuple(lo[i] + (hi[i] - lo[i]) * (v.co[i] + 0.5) for i in range(3))) * 1.25
    bm.to_mesh(gm); bm.free()
    gun_proxy = bpy.data.objects.new("GunProxy", gm); sc.collection.objects.link(gun_proxy)
    gun_proxy.color = (0.08, 0.08, 0.09, 1)
    side = cam("Side", Vector((-4.2, -0.0, 0.95)), Vector((0, 0, 0.85)), 45)
    front = cam("Front", Vector((-1.6, -3.6, 1.3)), Vector((0, 0, 0.8)), 45)
    close = cam("Close", Vector((-0.75, -1.05, 1.45)), Vector((0.0, -0.2, 1.0)), 45)   # hands and gun, front right
    RX, RY = 300, 400
    sc.render.resolution_x, sc.render.resolution_y = RX, RY
    for act, last in made:
        rig.animation_data.action = act
        frames = [1 + round(i * (last - 1) / 6) for i in range(6)]
        for o in [o for o in bpy.data.objects if o.name.startswith("Flight")]:
            bpy.data.objects.remove(o)
        g = GAITS.get(act.name, {})
        floor.hide_render = bool(g.get("slope"))
        if act.name in SEATS:
            # the ledge it sits on: top at SEAT_Z, front edge SEAT_EDGE ahead of the origin
            lm = bpy.data.meshes.new("FlightLedge"); bm = bmesh.new()
            r = bmesh.ops.create_cube(bm, size=1.0)
            lo, hi = Vector((-0.6, -SEAT_EDGE, SEAT_Z - 0.08)), Vector((0.6, 1.2, SEAT_Z))
            for v in r["verts"]:
                v.co = Vector(tuple(lo[i] + (hi[i] - lo[i]) * (v.co[i] + 0.5) for i in range(3)))
            bm.to_mesh(lm); bm.free()
            ledge = bpy.data.objects.new("FlightLedge", lm); sc.collection.objects.link(ledge)
            ledge.color = (0.3, 0.33, 0.3, 1)
        if g.get("slope"):
            # the flight under the body: treads on the line through their centres, moving back and up as it walks
            gt, ht = STAIR_TREAD / GAME_SCALE, STAIR_RISE / GAME_SCALE
            sm = bpy.data.meshes.new("Flight"); bm = bmesh.new()
            for k in range(-4, 10):
                lo = Vector((-0.5, -(k * gt), -(k - 0.5) * ht - 0.05)); hi = Vector((0.5, -((k - 1) * gt), -(k - 0.5) * ht))
                r = bmesh.ops.create_cube(bm, size=1.0)
                for v in r["verts"]:
                    v.co = Vector(tuple(lo[i] + (hi[i] - lo[i]) * (v.co[i] + 0.5) for i in range(3)))
            bm.to_mesh(sm); bm.free()
            flight = bpy.data.objects.new("Flight", sm); sc.collection.objects.link(flight)
            flight.color = (0.25, 0.27, 0.3, 1)
        rows = []
        # seated: the close-up looks at the lap from the side, lower down
        close.location = Vector((-1.25, -0.55, 0.95)) if act.name in SEATS else Vector((-0.75, -1.05, 1.45))
        close.rotation_euler = (Vector((0.0, -0.25, 0.75) if act.name in SEATS else (0.0, -0.2, 1.0)) - close.location).to_track_quat("-Z", "Y").to_euler()
        for c in ((side, front, close) if act.name in GUN_AT else (side, front)):
            row = []
            for f in frames:
                sc.frame_set(f); sc.camera = c
                gp = GUN_AT.get(act.name, {}).get(f)
                gun_proxy.hide_render = gp is None
                if gp is not None: gun_proxy.matrix_world = gp
                if g.get("slope"):
                    vb = g["speed"] * (g["frames"] / FPS) * ((f - 1) / g["frames"])          # metres walked down
                    flight.location = Vector((0, vb, g["slope"] * vb))                      # FWD is -Y
                sc.render.filepath = os.path.join(RENDER, "_t.png")
                bpy.ops.render.render(write_still=True)
                im = bpy.data.images.load(sc.render.filepath)
                row.append(np.array(im.pixels[:], np.float32).reshape(RY, RX, 4)); bpy.data.images.remove(im)
            rows.append(np.concatenate(row, axis=1))
        sheet = np.concatenate(rows[::-1], axis=0)
        im = bpy.data.images.new("s", sheet.shape[1], sheet.shape[0], alpha=True); im.pixels.foreach_set(sheet.ravel())
        im.filepath_raw = os.path.join(RENDER, f"{act.name}.png"); im.file_format = "PNG"; im.save(); bpy.data.images.remove(im)
    bpy.data.objects.remove(floor)
    for o in [o for o in bpy.data.objects if o.name.startswith("Flight")]:
        bpy.data.objects.remove(o)
    log("sheets in", RENDER)

if "--save" in argv:
    bpy.ops.wm.save_as_mainfile(filepath=argv[argv.index("--save") + 1], copy=True)
    log("saved", argv[argv.index("--save") + 1])

# ------------------------------------------------------------------ export
if "--export" in argv:
    rig.name = RIG_NAME
    rig.animation_data.action = None
    for p in rig.pose.bones:
        p.rotation_quaternion = Quaternion(); p.location = (0, 0, 0)
    sc.frame_set(0)
    for o in bpy.context.view_layer.objects:
        o.select_set(o is rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={"ARMATURE"}, add_leaf_bones=False,
                             apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y", bake_space_transform=False,
                             bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)
    log("exported", OUT, len(made), "clips")
