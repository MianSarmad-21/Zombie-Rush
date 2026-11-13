"""Pose library for code-written character animation (from the 2026-11-08 zombie work).

exec() this with ARM_NAME and MESH_NAME set. Pose(): per-bone rotations in rest-relative degrees (rot, spine),
two-bone IK for arms and legs (arm_ik, leg_ik), finger curl (claw), hips offset (P.hips, metres).
make(name, frames, fn, loop, ground) keys one action from fn(t, P) with t running 0..1.
"""
import bpy, math
from mathutils import Euler, Vector, Quaternion, Matrix
arm = bpy.data.objects[ARM_NAME]
LOD = bpy.data.objects[MESH_NAME]
PFX = globals().get("PFX_OVERRIDE", "mixamorig:")
AL = globals().get("AL_OVERRIDE") or {"Hips": "Hips", "Spine": "Spine", "Chest": "Spine1", "UpperChest": "Spine2", "Neck": "Neck", "Head": "Head",
      "LeftShoulder": "LeftShoulder", "RightShoulder": "RightShoulder",
      "LeftUpperArm": "LeftArm", "RightUpperArm": "RightArm", "LeftLowerArm": "LeftForeArm", "RightLowerArm": "RightForeArm",
      "LeftHand": "LeftHand", "RightHand": "RightHand",
      "LeftUpperLeg": "LeftUpLeg", "RightUpperLeg": "RightUpLeg", "LeftLowerLeg": "LeftLeg", "RightLowerLeg": "RightLeg",
      "LeftFoot": "LeftFoot", "RightFoot": "RightFoot", "LeftToes": "LeftToeBase", "RightToes": "RightToeBase"}
def N(n):
    n = AL.get(n, n)
    return n if n.startswith(PFX) else PFX + n
BONES = [b.name for b in arm.data.bones]
ML = {b.name: b.matrix_local.copy() for b in arm.data.bones}
PAR = {b.name: (b.parent.name if b.parent else None) for b in arm.data.bones}
HEAD = {b.name: b.head_local.copy() for b in arm.data.bones}
HIPS = N("Hips")
sin = math.sin; cos = math.cos; pi = math.pi
def tau(t): return 2 * pi * t
def rad(x): return math.radians(x)
def L(a, b): return (HEAD[N(b)] - HEAD[N(a)]).length
LEN = {"L_arm": (L("LeftUpperArm", "LeftLowerArm"), L("LeftLowerArm", "LeftHand")),
       "R_arm": (L("RightUpperArm", "RightLowerArm"), L("RightLowerArm", "RightHand")),
       "L_leg": (L("LeftUpperLeg", "LeftLowerLeg"), L("LeftLowerLeg", "LeftFoot")),
       "R_leg": (L("RightUpperLeg", "RightLowerLeg"), L("RightLowerLeg", "RightFoot"))}
REST_FOOT_DIR = {n: (ML[N(n)].to_3x3() @ Vector((0, 1, 0))).normalized() for n in ("LeftFoot", "RightFoot")}
REST_HAND_POS = {n: HEAD[N(n)].copy() for n in ("LeftHand", "RightHand", "LeftFoot", "RightFoot")}
FINGERS = {s: {f: [PFX + s + "Hand" + f + str(i) for i in (1, 2, 3)] for f in ("Thumb", "Index", "Middle", "Ring", "Pinky")} for s in ("Left", "Right")}

class Pose:
    def __init__(self):
        self.q = {n: Quaternion() for n in BONES}
        self.hips = Vector((0, 0, 0))
        self.M = None
    def fk(self):
        M = {}
        for n in BONES:
            basis = self.q[n].to_matrix().to_4x4()
            if n == HIPS:
                loc = ML[n].to_3x3().inverted() @ self.hips
                M[n] = ML[n] @ (Matrix.Translation(loc) @ basis)
            else:
                p = PAR[n]
                M[n] = M[p] @ (ML[p].inverted() @ ML[n]) @ basis
        self.M = M
        return M
    def rot(self, name, rx=0, ry=0, rz=0):
        name = N(name)
        Rq = ML[name].to_3x3().to_quaternion()
        d = Euler((rad(rx), rad(ry), rad(rz)), 'XYZ').to_quaternion()
        self.q[name] = Rq.inverted() @ d @ Rq
    def spine(self, rx=0, ry=0, rz=0, w=(0.3, 0.35, 0.35)):
        for n, k in zip(("Spine", "Chest", "UpperChest"), w): self.rot(n, rx * k, ry * k, rz * k)
    def pos(self, name):
        return self.fk()[N(name)].translation.copy()
    def facing(self, ref):
        """The body's forward (-Y at rest) as bone `ref` carries it now."""
        ref = N(ref)
        M = self.fk()
        return (M[ref].to_3x3() @ (ML[ref].to_3x3().inverted() @ Vector((0, -1, 0)))).normalized()

    def aim(self, name, d, roll=0, pivot=None):
        """Point bone `name` along `d`. With `pivot` (a direction the bone never points away from, e.g.
        the body's forward for an arm), the turn goes rest -> pivot -> d, so it never flips when `d`
        swings across the body (the shortest turn from rest flips there)."""
        name = N(name)
        d = Vector(d).normalized()
        M = self.fk(); p = PAR[name]
        base = (M[p] @ (ML[p].inverted() @ ML[name])).to_3x3()
        Y0 = (base @ Vector((0, 1, 0))).normalized()
        if pivot is not None:
            F = Vector(pivot).normalized()
            S = F.rotation_difference(d) @ Y0.rotation_difference(F)
        else:
            S = Y0.rotation_difference(d)
        if roll: S = Quaternion(d, rad(roll)) @ S
        Q = base.to_quaternion()
        self.q[name] = Q.inverted() @ S @ Q
    def ik(self, upper, lower, end, target, pole, key, roll=0):
        L1, L2 = LEN[key]
        sh = self.pos(upper)
        target = Vector(target); pole = Vector(pole)
        v = target - sh
        dist = v.length
        dist = max(abs(L1 - L2) + 1e-3, min(L1 + L2 - 1e-3, dist))
        dirn = v.normalized()
        a = (L1 * L1 - L2 * L2 + dist * dist) / (2 * dist)
        h = math.sqrt(max(L1 * L1 - a * a, 0))
        perp = pole - dirn * pole.dot(dirn)
        perp = perp.normalized() if perp.length > 1e-6 else Vector((0, -1, 0))
        elbow = sh + dirn * a + perp * h
        # The upper bone's twist comes from the bend plane, like a real hinge: the elbow (knee) always
        # bends towards its pole. Turning it by its pointing direction alone spins it on fast swings.
        self.hinge(upper, elbow - sh, -perp, arm=key.endswith("arm"))
        self.aim(lower, sh + dirn * dist - elbow, roll=roll)

    def hinge(self, name, d, fold, arm):
        """Point bone `name` along `d` with its bend side facing `fold` (where the next bone folds to).
        At rest, forearms fold forward and shins backward."""
        name = N(name)
        M = self.fk(); p = PAR[name]
        base = (M[p] @ (ML[p].inverted() @ ML[name])).to_3x3()
        rest_w = ML[name].to_3x3()
        y_r = (rest_w @ Vector((0, 1, 0))).normalized()
        f_r = Vector((0, -1, 0)) if arm else Vector((0, 1, 0))
        f_r = (f_r - y_r * f_r.dot(y_r)).normalized()
        y_n = Vector(d).normalized()
        f_n = Vector(fold)
        f_n = (f_n - y_n * f_n.dot(y_n))
        if f_n.length < 1e-6:
            return self.aim(name, d)
        f_n.normalize()
        def B(y, f):
            return Matrix((y, f, y.cross(f))).transposed()
        R = B(y_n, f_n) @ B(y_r, f_r).transposed()
        self.q[name] = (base.inverted() @ (R @ rest_w)).to_quaternion()
    def arm_ik(self, side, target, pole=None, roll=0):
        s = 1 if side == "Left" else -1
        pole = pole or (s * 0.7, 0.6, -1)
        self.ik(side + "UpperArm", side + "LowerArm", side + "Hand", target, pole, "L_arm" if side == "Left" else "R_arm", roll)
    def leg_ik(self, side, foot_target, pitch=0, pole=(0, -1, 0)):
        self.ik(side + "UpperLeg", side + "LowerLeg", side + "Foot", foot_target, pole, "L_leg" if side == "Left" else "R_leg")
        d = REST_FOOT_DIR[side + "Foot"]
        if pitch: d = Quaternion(Vector((1, 0, 0)), rad(pitch)) @ d
        self.aim(side + "Foot", d)
    def foot(self, side, dx=0, dy=0, dz=0):
        return REST_HAND_POS[side + "Foot"] + Vector((dx, dy, dz))
    def claw(self, side, curl=1.0, spread=0.0, thumb=0.6):
        """curl fingers toward the palm (palm faces down at rest). curl 0..1"""
        s = 1 if side == "Left" else -1
        for f, bones in FINGERS[side].items():
            ang = (35, 50, 35) if f != "Thumb" else (20, 25, 20)
            k = curl if f != "Thumb" else thumb * curl
            for b, a in zip(bones, ang):
                if b in self.q:
                    Rq = ML[b].to_3x3().to_quaternion()
                    d = Euler((0, rad(s * a * k), 0), 'XYZ').to_quaternion()
                    self.q[b] = Rq.inverted() @ d @ Rq

def write_pose(P, f):
    for n in BONES:
        pb = arm.pose.bones[n]
        pb.rotation_mode = 'QUATERNION'
        pb.rotation_quaternion = P.q[n]
        pb.keyframe_insert("rotation_quaternion", frame=f, group=n)
    hb = arm.pose.bones[HIPS]
    hb.location = ML[HIPS].to_3x3().inverted() @ P.hips
    hb.keyframe_insert("location", frame=f, group="Hips")

def mesh_min_z():
    bpy.context.view_layer.update()
    ev = LOD.evaluated_get(bpy.context.evaluated_depsgraph_get()); me = ev.to_mesh()
    z = min(v.co.z for v in me.vertices); ev.to_mesh_clear(); return z
def set_pose_raw(P):
    for n in BONES:
        pb = arm.pose.bones[n]; pb.rotation_mode = 'QUATERNION'; pb.rotation_quaternion = P.q[n]
    arm.pose.bones[HIPS].location = ML[HIPS].to_3x3().inverted() @ P.hips

def make(name, nframes, fn, loop=True, ground=False):
    old = bpy.data.actions.get(name)
    if old: bpy.data.actions.remove(old)
    a = bpy.data.actions.new(name); a.use_fake_user = True
    arm.animation_data_create(); arm.animation_data.action = a
    for f in range(1, nframes + 2):
        t = (f - 1) / nframes
        P = Pose(); fn(t, P)
        if ground:
            set_pose_raw(P); z = mesh_min_z()
            if z < 0: P.hips.z -= z
        write_pose(P, f)
    a.use_frame_range = True; a.frame_start = 1; a.frame_end = nframes + 1
    a.use_cyclic = loop
    return a

def ease(x):
    x = max(0.0, min(1.0, x)); return x * x * (3 - 2 * x)
def lerp(a, b, w): return a + (b - a) * w
def vlerp(a, b, w): return Vector(a) * (1 - w) + Vector(b) * w
def V(*a): return Vector(a) if len(a) == 3 else Vector(a[0])


def seg_(t, a, b):
    return min(1.0, max(0.0, (t - a) / (b - a))) if b > a else (1.0 if t >= b else 0.0)


def foot_path(t, keys, lift=0.6, max_lift=0.10):
    """Where a foot is at t, from (time, offset) keys: it stays put at a key and steps between keys,
    lifted in an arc (height ~ lift x distance, capped), so it never slides along the ground."""
    keys = [(k, V(o)) for k, o in keys]
    if t <= keys[0][0]:
        return keys[0][1].copy()
    for (t0, a), (t1, b) in zip(keys, keys[1:]):
        if t <= t1:
            u = seg_(t, t0, t1)
            p = a.lerp(b, ease(u))
            d = (b - a).length
            p.z += min(max_lift, lift * d) * math.sin(math.pi * u)
            return p
    return keys[-1][1].copy()
