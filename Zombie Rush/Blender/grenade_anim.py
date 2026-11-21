"""Grenade throw animations, shooter style (2026-11-18): first-person arms and third-person body, on the player's skeleton.

    blender -b --factory-startup -P Blender/grenade_anim.py -- [--render DIR] [--export] [--only FP|TP]

The throw works like COD: holding the button pulls the pin and cocks the arm; letting go throws at once.
  Cook   (COOK_N frames): the grenade comes up in front of the chest (frames 0-6), the left hand pinches the pin and
         pulls it out (6-10), the right arm cocks back to the ready pose (8-16, COOK_READY), then holds it with a slow
         sway (16-COOK_N, looped by Unity while the button is held). Third person: the left arm points ahead to aim.
  Throw  (THROW_N frames): from the ready pose a short wind (t 0.18), release forward and high (t RELEASE_T, the
         same as GrenadeConfig.releaseAt), follow-through down across the body (t 1). Unity stretches it over
         GrenadeConfig.throwTime.
Hands: the right hand is a fist round the grenade, the left pinches the pin or points; each hand's turn comes from a
(thumb, fingers, palm) frame (the left one mirrored from right-hand values), the forearm rolled to share the wrist's
twist (no corkscrew wrists).

Output in Assets/Models/2026-11-18_Grenade/:
  FP_Grenade.fbx         first person: the body's two arms (forearms x1.35 like the gun arms), FPGrenade on the
                         RightHand bone, FPPin_Grenade (the pin still in, on the right hand) and FPPin_Hand (the pulled
                         pin, on the left hand); takes FPGrenade_Cook / FPGrenade_Throw. Origin = the eye (camera at
                         identity, looking along -Y; right = -X), like Assets/Models/2026-11-10_FPArms/FP_<Gun>.fbx.
  Operative_Grenade.fbx  third person: the whole skeleton (armature only) + TPGrenade on RightHand; takes
                         Grenade_Rest / Grenade_Cook / Grenade_Throw (both arms, fingers and a chest twist move).
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Quaternion, Vector

PROJECT = "C:/Mian Sarmad/Unity/Zombie-Rush/Zombie Rush"
BODY = PROJECT + "/Assets/Models/2026-11-14_ShadowOperative_Final/ShadowOperative.fbx"
GRENADE = PROJECT + "/Assets/Models/2026-11-18_Grenade/Grenade.fbx"
OUT = PROJECT + "/Assets/Models/2026-11-18_Grenade/"
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
RENDER = argv[argv.index("--render") + 1] if "--render" in argv else None
EXPORT = "--export" in argv
ONLY = argv[argv.index("--only") + 1] if "--only" in argv else None
FPS = 30
COOK_N, THROW_N = 46, 14
# cook frames: up, pin pinched, pin out, ready (GrenadeConfig.cookRaise = COOK_READY / FPS, pinPullAt = PIN_F / FPS)
UP_F, PIN_F, OUT_F, COOK_READY = 6, 8, 11, 16
RELEASE_T = 0.36
VFOV = 55.0   # the first-person arms' camera (WeaponHoldConfig.viewmodelFov)

# camera / body axes in Blender (the body faces -Y): (right, up, forward)
RIGHT, UP, FWD = Vector((-1, 0, 0)), Vector((0, 0, 1)), Vector((0, -1, 0))
def V3(t): return RIGHT * t[0] + UP * t[1] + FWD * t[2]
MIRROR = Matrix.Diagonal((-1, 1, 1))   # left <-> right of the body

# right fist: fold per segment (deg) about the bone's own X; thumb folds less and swings toward the fingers
FIST = {"Index": (52, 62, 40), "Middle": (56, 66, 42), "Ring": (58, 66, 42), "Pinky": (60, 64, 40)}
THUMB = ((18, -18, 0), (22, 0, 0), (20, 0, 0))
# left hand: pinch (index and thumb meet, the rest tucked) and point (fingers nearly straight)
PINCH = {"Index": (40, 55, 30), "Middle": (60, 70, 45), "Ring": (64, 72, 45), "Pinky": (66, 70, 42)}
PINCH_THUMB = ((22, -24, 0), (26, 0, 0), (24, 0, 0))
POINT = {"Index": (6, 6, 4), "Middle": (10, 10, 6), "Ring": (14, 12, 8), "Pinky": (18, 14, 8)}
POINT_THUMB = ((10, -8, 0), (8, 0, 0), (6, 0, 0))
# the grenade in the right hand's (t, u, n) frame: across the palm, toward the knuckles, out of the palm (m);
# the left hand's pinch point (between thumb and index tip)
GRIP = (0.004, 0.072, 0.048)
PINCH_AT = (0.02, 0.085, 0.03)

# hand keys: (place (right, up, fwd) from the eye (FP) or the feet (TP), hand turn (pitch, yaw, roll deg),
#             elbow pole (right, up, fwd)). Left-hand values are written as for a right hand on the mirrored side;
#             positions and poles of the left hand are plain (right is +).
FP = {
    "R_low":     ((0.20, -0.62, 0.22), (-25, 25, 35), (1.0, -1.0, -0.2)),
    "R_up":      ((0.05, -0.10, 0.37), (-5, 10, 50), (1.0, -1.0, -0.2)),
    "R_ready":   ((0.16, -0.065, 0.30), (35, 15, 10), (1.0, -0.5, -0.4)),
    "R_wind":    ((0.27, 0.00, 0.10), (60, 15, -5), (1.0, 0.2, -0.6)),
    "R_release": ((0.07, 0.05, 0.52), (-20, -5, -10), (0.8, -0.6, -0.1)),
    "R_follow":  ((0.05, -0.62, 0.34), (-60, 0, 0), (1.0, -1.0, 0.0)),
    "L_low":     ((-0.22, -0.68, 0.18), (-30, 0, 0), (-1.0, -1.0, -0.2)),
    "L_pull":    ((-0.13, -0.17, 0.36), (-10, 20, 20), (-1.0, -1.0, -0.2)),
}
TP = {
    "R_low":     ((0.22, 0.98, 0.16), (-30, 15, -25), (1.0, -1.0, -0.2)),
    "R_up":      ((0.06, 1.24, 0.30), (-5, 10, 40), (1.0, -1.0, -0.2)),
    "R_ready":   ((0.30, 1.60, -0.10), (60, 20, -30), (1.0, 0.1, -0.6)),
    "R_wind":    ((0.32, 1.66, -0.22), (70, 20, -40), (1.0, 0.4, -0.5)),
    "R_release": ((0.17, 1.64, 0.46), (-15, -5, -10), (0.8, -0.3, 0.0)),
    "R_follow":  ((-0.10, 1.04, 0.38), (-70, -20, 10), (0.5, -1.0, 0.2)),
    "L_low":     ((-0.22, 1.00, 0.18), (-30, 0, 0), (-1.0, -1.0, -0.2)),
    "L_pull":    ((-0.26, 1.20, 0.34), (-10, 20, 20), (-1.0, -1.0, -0.2)),
    "L_point":   ((-0.10, 1.40, 0.52), (-75, 0, -10), (-1.0, -0.8, -0.2)),
    "L_tuck":    ((-0.22, 1.12, 0.12), (-20, 10, 20), (-1.0, -1.0, 0.0)),
}


def log(*a): print("[grenade_anim]", *a, flush=True)
def upd(): bpy.context.view_layer.update()
def ctx(obj, **kw):
    return bpy.context.temp_override(object=obj, active_object=obj, selected_objects=[obj], selected_editable_objects=[obj], **kw)
def smooth(x): x = max(0.0, min(1.0, x)); return x * x * (3 - 2 * x)


def load_body(fp):
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    for a in list(bpy.data.actions): bpy.data.actions.remove(a)
    bpy.ops.import_scene.fbx(filepath=BODY)
    bpy.context.scene.render.fps = FPS
    rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    mesh = max((o for o in bpy.data.objects if o.type == "MESH"), key=lambda o: len(o.data.vertices))
    for o in [o for o in bpy.data.objects if o.type == "MESH" and o is not mesh]: bpy.data.objects.remove(o, do_unlink=True)
    for a in list(bpy.data.actions): bpy.data.actions.remove(a)
    rig.animation_data_create(); rig.animation_data.action = None
    for pb in rig.pose.bones:
        pb.rotation_mode = "QUATERNION"; pb.rotation_quaternion = Quaternion(); pb.location = (0, 0, 0); pb.scale = (1, 1, 1)
    upd()
    if fp:
        rig.name = "FP_GrenadeRig"; mesh.name = "FP_GrenadeArms"
        # keep the two arms only
        keep = [vg.index for vg in mesh.vertex_groups
                if vg.name.startswith(("Right", "Left")) and any(k in vg.name for k in ("UpperArm", "LowerArm", "Hand"))]
        drop = [v.index for v in mesh.data.vertices if sum(g.weight for g in v.groups if g.group in keep) < 0.5]
        bm = bmesh.new(); bm.from_mesh(mesh.data); bm.verts.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[bm.verts[i] for i in drop], context="VERTS")
        bm.to_mesh(mesh.data); bm.free(); mesh.data.update()
        # longer forearms, as the first-person gun arms have them (the body's own look stubby from the eye)
        b = rig.data.bones
        for s in ("Right", "Left"):
            b[s + "Hand"].inherit_scale = "NONE"
            rig.pose.bones[s + "LowerArm"].scale = (1, 1.35, 1)
        upd()
        if mesh.data.shape_keys:
            with ctx(mesh): bpy.ops.object.shape_key_remove(all=True, apply_mix=True)
        mod = next(m for m in mesh.modifiers if m.type == "ARMATURE")
        with ctx(mesh): bpy.ops.object.modifier_apply(modifier=mod.name)
        bpy.context.view_layer.objects.active = rig
        for o in bpy.context.view_layer.objects: o.select_set(o is rig)
        with ctx(rig): bpy.ops.object.mode_set(mode="POSE")
        bpy.ops.pose.select_all(action="SELECT"); bpy.ops.pose.armature_apply(selected=False)
        bpy.ops.object.mode_set(mode="OBJECT")
        for s in ("Right", "Left"): b[s + "Hand"].inherit_scale = "FULL"
        for pb in rig.pose.bones: pb.scale = (1, 1, 1)
        m2 = mesh.modifiers.new("Armature", "ARMATURE"); m2.object = rig
        upd()
        log(f"FP arms: {len(mesh.data.vertices)} vertices, forearms x1.35")
    g = {"ARM_NAME": rig.name, "MESH_NAME": mesh.name, "PFX_OVERRIDE": "",
         "AL_OVERRIDE": {k: k for k in ("Hips", "Spine", "Chest", "UpperChest", "Neck", "Head", "LeftShoulder", "RightShoulder",
                                        "LeftUpperArm", "RightUpperArm", "LeftLowerArm", "RightLowerArm", "LeftHand", "RightHand",
                                        "LeftUpperLeg", "RightUpperLeg", "LeftLowerLeg", "RightLowerLeg", "LeftFoot", "RightFoot",
                                        "LeftToes", "RightToes")},
         "__name__": "zr_anim"}
    exec(open(os.path.join(PROJECT, "Blender", "zr_anim_lib.py"), encoding="utf-8").read(), g)
    return rig, mesh, g


def hand_frame_rest(rig, side):
    """(t, u, n) of a hand at rest, armature space: t toward the thumb, u to the knuckles, n = t x u
    (out of the palm on the right hand, out of the back on the left)."""
    B = rig.data.bones
    W = B[side + "Hand"].head_local
    kn = sum((B[f"{side}Hand{f}1"].head_local for f in ("Index", "Middle", "Ring", "Pinky")), Vector()) / 4
    u = (kn - W).normalized()
    t = B[side + "HandThumb1"].head_local - W; t = (t - u * t.dot(u)).normalized()
    n = t.cross(u)
    return Matrix((t, u, n)).transposed()


def tun_for(turn, side):
    """Wanted (t, u, n) of a hand: fingers forward-up, palm toward the body's middle, then turned by (pitch, yaw,
    roll). The left hand is the right-hand frame mirrored (its n stays t x u)."""
    u = (FWD * 0.55 + UP * 0.8).normalized()
    n = (-RIGHT * 0.9 + FWD * 0.2); n = (n - u * n.dot(u)).normalized()
    t = u.cross(n)
    base = Matrix((t, u, n)).transposed()
    p, y, r = (math.radians(a) for a in turn)
    F = Matrix.Rotation(y, 3, UP) @ Matrix.Rotation(p, 3, RIGHT) @ Matrix.Rotation(r, 3, FWD) @ base
    if side == "Right": return F
    t2, u2 = MIRROR @ F.col[0], MIRROR @ F.col[1]
    return Matrix((t2, u2, t2.cross(u2))).transposed()


def blend(A, B, w):
    pa, ta, la = A; pb, tb, lb = B
    return (Vector(pa).lerp(Vector(pb), w), tuple(x + (y - x) * w for x, y in zip(ta, tb)), Vector(la).lerp(Vector(lb), w))


def path(seq, t):
    """seq = [(t, key), ...] -> the blended key at t (smoothstep between neighbours)."""
    if t <= seq[0][0]: return blend(seq[0][1], seq[0][1], 0)
    for (t0, a), (t1, b) in zip(seq, seq[1:]):
        if t <= t1: return blend(a, b, smooth((t - t0) / max(1e-6, t1 - t0)))
    return blend(seq[-1][1], seq[-1][1], 0)


def run(fp):
    rig, mesh, g = load_body(fp)
    ML, PAR, make = g["ML"], g["PAR"], g["make"]
    R0 = {s: hand_frame_rest(rig, s) for s in ("Right", "Left")}
    H0 = {s: ML[s + "Hand"].to_3x3() for s in ("Right", "Left")}
    K = FP if fp else TP
    TW = {"Right": None, "Left": None}

    def place(P, side, pos, turn, pole, offset, fingers, thumb):
        """The hand's `offset` point (hand frame) at camera/body point pos, the hand turned to `turn`; arm by IK."""
        F = tun_for(turn, side)
        Rhand = F @ R0[side].transposed() @ H0[side]
        wrist = V3(pos) - F @ Vector(offset)
        P.arm_ik(side, wrist, pole=tuple(V3(pole)))
        M = P.fk()
        elbow = M[side + "LowerArm"].translation.copy()
        # forearm roll: share the wrist's twist (search, keep it close to last frame's)
        lower, hand = side + "LowerArm", side + "Hand"
        want = Rhand @ (ML[lower].to_3x3().inverted() @ ML[hand].to_3x3()).inverted()
        best = None
        for roll in range(-150, 151, 6):
            P.aim(lower, wrist - elbow, roll=roll)
            Mf = P.fk()[lower].to_3x3()
            y = Mf.col[1].normalized()
            xc = Mf.col[0] - y * Mf.col[0].dot(y); xi = want.col[0] - y * want.col[0].dot(y)
            tw = math.degrees(math.atan2(xc.cross(xi).dot(y), xc.dot(xi))) if xc.length > 1e-6 and xi.length > 1e-6 else 0.0
            cost = (tw * 0.4) ** 2 + (0 if TW[side] is None else 4 * abs(roll - TW[side]))
            if best is None or cost < best[0]: best = (cost, roll)
        TW[side] = best[1]
        P.aim(lower, wrist - elbow, roll=best[1])
        Mm = P.fk(); par = PAR[hand]
        base = (Mm[par] @ (ML[par].inverted() @ ML[hand])).to_3x3()
        P.q[hand] = (base.inverted() @ Rhand).to_quaternion()
        for f, angs in fingers.items():
            for k, a in enumerate(angs):
                P.q[f"{side}Hand{f}{k + 1}"] = Quaternion((1, 0, 0), math.radians(a))
        sgn = 1 if side == "Right" else -1
        for k, (fl, sw, _) in enumerate(thumb):
            q = Quaternion((1, 0, 0), math.radians(fl))
            if sw: q = Quaternion((0, 0, 1), math.radians(sw * sgn)) @ q
            P.q[f"{side}HandThumb{k + 1}"] = q

    def fingers_mix(a, b, w):
        return {f: tuple(x + (y - x) * w for x, y in zip(a[f], b[f])) for f in a}

    def thumb_mix(a, b, w):
        return tuple(tuple(x + (y - x) * w for x, y in zip(p, q)) for p, q in zip(a, b))

    def place_body(P):
        if fp:
            # the shoulders a little under and behind the eye (as the gun arms): the shoulder midpoint at
            # (0, -0.24, -0.02) in camera space; the eye is the origin
            M = P.fk()
            cur = (M["RightUpperArm"].translation + M["LeftUpperArm"].translation) / 2
            P.hips = V3((0.0, -0.24, -0.02)) - cur
        else:
            P.hips = Vector((0, 0, 0))

    def twist(P, ang):
        if fp: return
        # the chest turns the right shoulder back (-) on the cock and through (+) on the throw
        P.rot("Chest", rz=ang * 0.45); P.rot("UpperChest", rz=ang * 0.55)
        P.rot("RightShoulder", rz=ang * 0.3)

    def grenade_at(P):
        """The grenade centre and the right hand's frame now (after the right hand is posed)."""
        M = P.fk()
        F = M["RightHand"].to_3x3() @ H0["Right"].inverted() @ R0["Right"]
        return M["RightHand"].translation + F @ Vector(GRIP), F

    def pin_spot(P):
        """Where the pull ring sits on the grenade in the right hand (top of the fuse, off to the left side)."""
        c, F = grenade_at(P)
        return c + F.col[1].normalized() * 0.062 - RIGHT * 0.028

    def to_key(spot):
        """A body/camera point (Blender space) back to (right, up, fwd)."""
        return (spot.dot(RIGHT), spot.dot(UP), spot.dot(FWD))

    def cook_fn(t, P):
        f = t * COOK_N
        if f == 0: TW["Right"] = TW["Left"] = None
        place_body(P)
        # right hand: up, then cocked to the ready pose, then the sway
        if f <= COOK_READY:
            r = path([(0, K["R_low"]), (UP_F, K["R_up"]), (PIN_F + 1, K["R_up"]), (COOK_READY, K["R_ready"])], f)
        else:
            s = (f - COOK_READY) / (COOK_N - COOK_READY)
            pos, turn, pole = K["R_ready"]
            pos = Vector(pos) + Vector((math.sin(s * 2 * math.pi) * 0.004, math.sin(s * 4 * math.pi) * 0.003, 0))
            r = (pos, turn, pole)
        ang = 0.0 if fp else -22.0 * smooth((f - PIN_F) / (COOK_READY - PIN_F)) + (0 if f <= COOK_READY else 1.5 * math.sin((f - COOK_READY) / (COOK_N - COOK_READY) * 2 * math.pi))
        twist(P, ang)
        place(P, "Right", *r, GRIP, FIST, THUMB)
        # left hand: to the pin, pinch, pull it out, then down (FP) or point ahead (TP)
        pin = to_key(pin_spot(P))
        at_pin = (pin, K["L_pull"][1], K["L_pull"][2])
        if f <= PIN_F:
            l = path([(0, K["L_low"]), (UP_F, at_pin), (PIN_F, at_pin)], f)
            fw = 0.0
        elif f <= OUT_F:
            l = blend(at_pin, K["L_pull"], smooth((f - PIN_F) / (OUT_F - PIN_F)))
            fw = 0.0
        else:
            end = K["L_low"] if fp else K["L_point"]
            l = blend(K["L_pull"], end, smooth((f - OUT_F) / (COOK_READY + 3 - OUT_F)))
            fw = 0.0 if fp else smooth((f - OUT_F) / (COOK_READY + 3 - OUT_F))
        place(P, "Left", *l, PINCH_AT, fingers_mix(PINCH, POINT, fw), thumb_mix(PINCH_THUMB, POINT_THUMB, fw))

    def throw_fn(t, P):
        if t == 0: TW["Right"] = TW["Left"] = None
        place_body(P)
        twist(P, throw_twist(t))
        r = path([(0, K["R_ready"]), (0.18, K["R_wind"]), (RELEASE_T, K["R_release"]), (1.0, K["R_follow"])], t)
        place(P, "Right", *r, GRIP, FIST, THUMB)
        if fp:
            l = K["L_low"]; fw = 0.0
        else:
            l = path([(0, K["L_point"]), (0.2, K["L_point"]), (0.6, K["L_tuck"]), (1.0, K["L_tuck"])], t)
            fw = 1.0 - smooth((t - 0.2) / 0.4)
        place(P, "Left", *l, PINCH_AT, fingers_mix(PINCH, POINT, fw), thumb_mix(PINCH_THUMB, POINT_THUMB, fw))

    def throw_twist(t):
        seq = [(0.0, -22.0), (0.18, -28.0), (RELEASE_T, 4.0), (0.7, 18.0), (1.0, 10.0)]
        for (t0, a), (t1, b) in zip(seq, seq[1:]):
            if t <= t1: return a + (b - a) * smooth((t - t0) / max(1e-6, t1 - t0))
        return seq[-1][1]

    pre = "FPGrenade_" if fp else "Grenade_"
    make(pre + "Cook", COOK_N, cook_fn, loop=False)
    make(pre + "Throw", THROW_N, throw_fn, loop=False)
    if not fp:
        make("Grenade_Rest", 1, lambda t, P: None, loop=False)
    log(f"{'FP' if fp else 'TP'} clips: {[a.name for a in bpy.data.actions]}")

    # the grenade (pin out) on the right hand, at the ready pose; first person also gets the pin twice: still in the
    # grenade (right hand, until the pull) and pulled (left hand, from the pull)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=GRENADE)
    new = [o for o in bpy.data.objects if o not in before]
    for o in [o for o in new if o.type != "MESH"]: bpy.data.objects.remove(o, do_unlink=True)
    parts = [o for o in bpy.data.objects if o in new and o.type == "MESH"]
    pins = [o for o in parts if o.name.startswith("Grenade_Pin")]
    parts = [o for o in parts if o not in pins]
    for o in bpy.data.objects: o.select_set(False)
    for o in parts: o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    with bpy.context.temp_override(object=parts[0], active_object=parts[0], selected_objects=parts, selected_editable_objects=parts):
        bpy.ops.object.join()
    gren = bpy.context.view_layer.objects.active
    gren.name = "FPGrenade" if fp else "TPGrenade"
    with ctx(gren): bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    def grenade_world(action, frame):
        rig.animation_data.action = bpy.data.actions[action]
        bpy.context.scene.frame_set(frame); upd()
        Hb = rig.matrix_world @ rig.pose.bones["RightHand"].matrix
        F = Hb.to_3x3() @ H0["Right"].inverted() @ R0["Right"]
        centre = Hb.translation + F @ Vector(GRIP)
        return Matrix.Translation(centre) @ (F @ Matrix.Rotation(math.radians(-90), 3, "X")).to_4x4()   # fuse along the fingers

    def attach(obj, bone, world):
        obj.parent = rig; obj.parent_type = "BONE"; obj.parent_bone = bone
        upd(); obj.matrix_world = world; upd()

    attach(gren, "RightHand", grenade_world(pre + "Cook", COOK_N + 1))
    extra = []
    if fp and pins:
        pin = pins[0]
        with ctx(pin): bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        pin.name = "FPPin_Grenade"
        pin2 = pin.copy(); pin2.data = pin.data.copy(); pin2.name = "FPPin_Hand"
        bpy.context.scene.collection.objects.link(pin2)
        attach(pin, "RightHand", grenade_world(pre + "Cook", COOK_N + 1))
        attach(pin2, "LeftHand", grenade_world(pre + "Cook", PIN_F + 1))   # where the pin is when the fingers close on it
        extra = [pin, pin2]
    else:
        for o in pins: bpy.data.objects.remove(o, do_unlink=True)
    rig.animation_data.action = None
    log(f"{gren.name} on RightHand, {len(gren.data.polygons)} faces; pins {[o.name for o in extra]}")
    return rig, mesh, gren, extra


def renders(rig, mesh, gren, extra, fp, outdir):
    os.makedirs(outdir, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"; sc.render.resolution_x = 960; sc.render.resolution_y = 540
    w = sc.world or bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.5, 0.55, 0.6, 1); w.node_tree.nodes["Background"].inputs[1].default_value = 0.8
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sc.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(50), 0, math.radians(200)); sun.data.energy = 3
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
    mat = bpy.data.materials.new("GrenadeLook"); mat.diffuse_color = (0.25, 0.3, 0.15, 1)
    pm = bpy.data.materials.new("PinLook"); pm.diffuse_color = (0.85, 0.75, 0.2, 1)
    gren.data.materials.clear(); gren.data.materials.append(mat)
    for o in extra: o.data.materials.clear(); o.data.materials.append(pm)
    pre = "FPGrenade_" if fp else "Grenade_"
    shots = [("Cook", UP_F + 1, "1up"), ("Cook", PIN_F + 1, "2pinch"), ("Cook", OUT_F + 1, "3pinout"),
             ("Cook", COOK_READY + 1, "4ready"), ("Throw", int(0.18 * THROW_N) + 1, "5wind"),
             ("Throw", int(RELEASE_T * THROW_N) + 1, "6release"), ("Throw", int(0.7 * THROW_N) + 1, "7follow")]
    for clip, f, nm in shots:
        rig.animation_data.action = bpy.data.actions[pre + clip]; sc.frame_set(f); upd()
        if fp:
            cam.data.sensor_fit = "VERTICAL"; cam.data.lens_unit = "FOV"; cam.data.angle = math.radians(VFOV)
            cam.data.clip_start = 0.03
            cam.location = (0, 0, 0); cam.rotation_euler = (math.radians(90), 0, math.radians(180))   # looking along -Y
            sc.render.filepath = os.path.join(outdir, f"FP_{nm}.png"); bpy.ops.render.render(write_still=True)
        else:
            views = (("side", Vector((-2.6, -0.6, 1.4)), 1.25, 35), ("front", Vector((-1.2, -2.6, 1.5)), 1.25, 35),
                     ("back", Vector((0.9, 2.4, 1.9)), 1.35, 35))
            for view, pos, look_z, lens in views:
                cam.data.lens = lens
                cam.location = pos; cam.rotation_euler = (Vector((0, 0, look_z)) - pos).to_track_quat("-Z", "Y").to_euler()
                sc.render.filepath = os.path.join(outdir, f"TP_{nm}_{view}.png"); bpy.ops.render.render(write_still=True)
    log("renders in", outdir)


def export(rig, mesh, gren, extra, fp):
    for a in bpy.data.actions: a.use_fake_user = True
    bpy.context.scene.render.fps = FPS   # importing the grenade model resets it to the file's 24
    path_out =OUT + ("FP_Grenade.fbx" if fp else "Operative_Grenade.fbx")
    bpy.ops.object.select_all(action="DESELECT")
    objs = [rig, gren] + extra + ([mesh] if fp else [])
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = rig
    rig.animation_data.action = None
    bpy.ops.export_scene.fbx(filepath=path_out, use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=False, bake_anim=True, bake_anim_use_all_actions=True,
                             bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
                             path_mode="STRIP", embed_textures=False, mesh_smooth_type="FACE")
    log("exported", path_out)


for fp in (True, False):
    if ONLY and ONLY != ("FP" if fp else "TP"): continue
    rig, mesh, gren, extra = run(fp)
    if RENDER: renders(rig, mesh, gren, extra, fp, RENDER)
    if EXPORT: export(rig, mesh, gren, extra, fp)
