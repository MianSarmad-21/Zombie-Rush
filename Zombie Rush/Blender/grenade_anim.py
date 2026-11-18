"""Grenade throw animations (2026-11-18): first-person arm and third-person body, on the player's own skeleton.

    blender -b --factory-startup -P Blender/grenade_anim.py -- [--render DIR] [--export] [--only FP|TP]

Both use zr_anim_lib (two-bone arm IK with the elbow toward a pole, aim/roll per bone) and the same throw:
  Cook   (36 frames): the grenade comes up from below (frames 1-8) and is held, pin out, with a slow sway (9-36 loop);
  Throw  (18 frames): wind-up behind the shoulder (t 0.25), release forward and high (t 0.38, GrenadeConfig.releaseAt),
         follow-through down across the body (t 1). Unity stretches it over GrenadeConfig.throwTime.
The hand: a fist round the grenade (fingers folded about their own X like the grip solver's basis_q), its turn set
from a (thumb, fingers, palm) frame, the forearm rolled to share the wrist's twist (no corkscrew wrists).

Output in Assets/Models/2026-11-18_Grenade/:
  FP_Grenade.fbx         first person: the body's right arm only (forearm x1.35 like the gun arms), FPGrenade mesh
                         on the RightHand bone; takes FPGrenade_Cook / FPGrenade_Throw. Origin = the eye (camera at
                         identity, looking along -Y; right = -X), like Assets/Models/2026-11-10_FPArms/FP_<Gun>.fbx.
  Operative_Grenade.fbx  third person: the whole skeleton (armature only) + TPGrenade on RightHand; takes
                         Grenade_Rest / Grenade_Cook / Grenade_Throw (right arm, fingers and a chest twist move).
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Euler, Matrix, Quaternion, Vector

PROJECT = "C:/Mian Sarmad/Unity/Zombie-Rush/Zombie Rush"
BODY = PROJECT + "/Assets/Models/2026-11-14_ShadowOperative_Final/ShadowOperative.fbx"
GRENADE = PROJECT + "/Assets/Models/2026-11-18_Grenade/Grenade.fbx"
OUT = PROJECT + "/Assets/Models/2026-11-18_Grenade/"
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
RENDER = argv[argv.index("--render") + 1] if "--render" in argv else None
EXPORT = "--export" in argv
ONLY = argv[argv.index("--only") + 1] if "--only" in argv else None
FPS = 30
COOK_N, RAISE_N, THROW_N = 35, 8, 18
RELEASE_T = 0.38

# camera / body axes in Blender (the body faces -Y): (right, up, forward)
RIGHT, UP, FWD = Vector((-1, 0, 0)), Vector((0, 0, 1)), Vector((0, -1, 0))
def V3(t): return RIGHT * t[0] + UP * t[1] + FWD * t[2]

# fist: fold per segment (deg) about the bone's own X; thumb folds less and swings toward the fingers
FIST = {"Index": (52, 62, 40), "Middle": (56, 66, 42), "Ring": (58, 66, 42), "Pinky": (60, 64, 40)}
THUMB = ((18, -18, 0), (22, 0, 0), (20, 0, 0))
# the grenade in the hand's (t, u, n) frame: across the palm, toward the knuckles, out of the palm (m)
GRIP = (0.004, 0.072, 0.048)

# throw keys: (t, grenade centre (right, up, fwd), hand frame turn (pitch, yaw, roll deg), elbow pole (right, up, fwd))
FP_KEYS = {
    "cook":    ((0.125, -0.095, 0.38), (-5, 25, 35), (1.0, -1.0, -0.2)),
    "low":     ((0.22, -0.45, 0.30), (-25, 25, 35), (1.0, -1.0, -0.2)),
    "wind":    ((0.27, 0.01, 0.18), (50, 20, -10), (1.0, 0.2, -0.6)),
    "release": ((0.08, 0.04, 0.52), (-20, -5, -10), (0.8, -0.6, -0.1)),
    "follow":  ((0.14, -0.55, 0.36), (-60, 0, 0), (1.0, -1.0, 0.0)),
}
TP_KEYS = {
    "cook":    ((0.24, 1.22, 0.30), (-10, 15, -25), (1.0, -1.0, -0.2)),
    "low":     ((0.30, 0.98, 0.20), (-30, 15, -25), (1.0, -1.0, -0.2)),
    "wind":    ((0.34, 1.66, -0.22), (65, 20, -40), (1.0, 0.6, -0.4)),
    "release": ((0.20, 1.62, 0.48), (-15, -5, -10), (0.8, -0.3, 0.0)),
    "follow":  ((-0.05, 1.02, 0.42), (-70, -20, 10), (0.5, -1.0, 0.2)),
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
        rig.name = "FP_GrenadeRig"; mesh.name = "FP_GrenadeArm"
        # keep the right arm only
        keep = [vg.index for vg in mesh.vertex_groups if vg.name.startswith("Right") and any(k in vg.name for k in ("UpperArm", "LowerArm", "Hand"))]
        drop = [v.index for v in mesh.data.vertices if sum(g.weight for g in v.groups if g.group in keep) < 0.5]
        bm = bmesh.new(); bm.from_mesh(mesh.data); bm.verts.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[bm.verts[i] for i in drop], context="VERTS")
        bm.to_mesh(mesh.data); bm.free(); mesh.data.update()
        # longer forearm, as the first-person gun arms have it (the body's own looks stubby from the eye)
        b = rig.data.bones
        b["RightHand"].inherit_scale = "NONE"
        rig.pose.bones["RightLowerArm"].scale = (1, 1.35, 1)
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
        b["RightHand"].inherit_scale = "FULL"
        for pb in rig.pose.bones: pb.scale = (1, 1, 1)
        m2 = mesh.modifiers.new("Armature", "ARMATURE"); m2.object = rig
        upd()
        log(f"FP arm: {len(mesh.data.vertices)} vertices, forearm x1.35")
    g = {"ARM_NAME": rig.name, "MESH_NAME": mesh.name, "PFX_OVERRIDE": "",
         "AL_OVERRIDE": {k: k for k in ("Hips", "Spine", "Chest", "UpperChest", "Neck", "Head", "LeftShoulder", "RightShoulder",
                                        "LeftUpperArm", "RightUpperArm", "LeftLowerArm", "RightLowerArm", "LeftHand", "RightHand",
                                        "LeftUpperLeg", "RightUpperLeg", "LeftLowerLeg", "RightLowerLeg", "LeftFoot", "RightFoot",
                                        "LeftToes", "RightToes")},
         "__name__": "zr_anim"}
    exec(open(os.path.join(PROJECT, "Blender", "zr_anim_lib.py"), encoding="utf-8").read(), g)
    return rig, mesh, g


def hand_frame_rest(rig):
    """(t, u, n) of the right hand at rest, armature space: t toward the thumb, u to the knuckles, n out of the palm."""
    B = rig.data.bones
    W = B["RightHand"].head_local
    kn = sum((B[f"RightHand{f}1"].head_local for f in ("Index", "Middle", "Ring", "Pinky")), Vector()) / 4
    u = (kn - W).normalized()
    t = B["RightHandThumb1"].head_local - W; t = (t - u * t.dot(u)).normalized()
    n = t.cross(u)
    return Matrix((t, u, n)).transposed()


def run(fp):
    rig, mesh, g = load_body(fp)
    Pose, ML, PAR, write_pose, make = g["Pose"], g["ML"], g["PAR"], g["write_pose"], g["make"]
    R0 = hand_frame_rest(rig)
    H0 = ML["RightHand"].to_3x3()
    # hand bone rotation for a wanted (t, u, n) frame: Rd @ R0^T @ H0
    KEYS = FP_KEYS if fp else TP_KEYS

    # base frame of the hold: fingers forward-up, palm facing left-forward (the grenade sits in the palm, fuse up)
    def tun_for(turn):
        u = (FWD * 0.55 + UP * 0.8).normalized()
        n = (-RIGHT * 0.9 + FWD * 0.2); n = (n - u * n.dot(u)).normalized()
        t = u.cross(n)
        base = Matrix((t, u, n)).transposed()
        p, y, r = (math.radians(a) for a in turn)
        rot = (Matrix.Rotation(y, 3, UP) @ Matrix.Rotation(p, 3, RIGHT) @ Matrix.Rotation(r, 3, FWD))
        return rot @ base

    def key_at(t):
        """Grenade centre, hand frame and pole at throw time t (0..1), between the keys."""
        seq = [(0.0, "cook"), (0.25, "wind"), (RELEASE_T, "release"), (1.0, "follow")]
        for (t0, a), (t1, b) in zip(seq, seq[1:]):
            if t <= t1:
                w = smooth((t - t0) / max(1e-6, t1 - t0))
                return blend(KEYS[a], KEYS[b], w)
        return blend(KEYS["follow"], KEYS["follow"], 0)

    def blend(A, B, w):
        pa, ta, la = A; pb, tb, lb = B
        pos = Vector(pa).lerp(Vector(pb), w)
        turn = tuple(x + (y - x) * w for x, y in zip(ta, tb))
        pole = Vector(la).lerp(Vector(lb), w)
        return pos, turn, pole

    TW = {"last": None}

    def pose_hand(P, gpos, turn, pole):
        """Fist round the grenade at camera/body point gpos with hand frame `turn`; arm by IK."""
        F = tun_for(turn)
        Rhand = F @ R0.transposed() @ H0
        wrist = gpos - F @ Vector(GRIP)
        # the elbow toward its pole (camera axes -> Blender)
        P.arm_ik("Right", wrist, pole=tuple(V3(pole)))
        M = P.fk()
        sh, elbow = M["RightUpperArm"].translation.copy(), M["RightLowerArm"].translation.copy()
        # forearm roll: share 60% of the wrist's twist (search, keep it close to last frame's)
        best = None
        for roll in range(-150, 151, 6):
            P.aim("RightLowerArm", wrist - elbow, roll=roll)
            Mf = P.fk()["RightLowerArm"].to_3x3()
            y = Mf.col[1].normalized()
            want = Rhand @ (ML["RightLowerArm"].to_3x3().inverted() @ ML["RightHand"].to_3x3()).inverted()
            xc = Mf.col[0] - y * Mf.col[0].dot(y); xi = want.col[0] - y * want.col[0].dot(y)
            tw = math.degrees(math.atan2(xc.cross(xi).dot(y), xc.dot(xi))) if xc.length > 1e-6 and xi.length > 1e-6 else 0.0
            cost = (tw * 0.4) ** 2 + (0 if TW["last"] is None else 4 * abs(roll - TW["last"]))
            if best is None or cost < best[0]: best = (cost, roll)
        TW["last"] = best[1]
        P.aim("RightLowerArm", wrist - elbow, roll=best[1])
        # the hand's own turn
        Mm = P.fk(); par = PAR["RightHand"]
        base = (Mm[par] @ (ML[par].inverted() @ ML["RightHand"])).to_3x3()
        P.q["RightHand"] = (base.inverted() @ Rhand).to_quaternion()
        # fist
        for f, angs in FIST.items():
            for k, a in enumerate(angs):
                P.q[f"RightHand{f}{k + 1}"] = Quaternion((1, 0, 0), math.radians(a))
        for k, (fl, sw, tw) in enumerate(THUMB):
            q = Quaternion((1, 0, 0), math.radians(fl))
            if sw: q = Quaternion((0, 0, 1), math.radians(sw)) @ q
            P.q[f"RightHandThumb{k + 1}"] = q

    def place_body(P):
        if fp:
            # the shoulders a little under and behind the eye (as the gun arms): body moved so the shoulder
            # midpoint sits at (0, -0.24, -0.02) in camera space; the eye is the origin
            M = P.fk()
            cur = (M["RightUpperArm"].translation + M["LeftUpperArm"].translation) / 2
            P.hips = V3((0.0, -0.24, -0.02)) - cur
        else:
            P.hips = Vector((0, 0, 0))

    def twist(P, t_throw):
        if fp: return
        # the chest turns the right shoulder back on the wind-up and through on the throw
        seq = [(0.0, 0.0), (0.25, -22.0), (RELEASE_T, 6.0), (0.7, 18.0), (1.0, 10.0)]
        for (t0, a), (t1, b) in zip(seq, seq[1:]):
            if t_throw <= t1:
                w = smooth((t_throw - t0) / max(1e-6, t1 - t0)); ang = a + (b - a) * w; break
        P.rot("Chest", rz=ang * 0.45); P.rot("UpperChest", rz=ang * 0.55)
        P.rot("RightShoulder", rz=ang * 0.3)

    def to_space(pos):
        """Key positions are camera space (FP: from the eye) or body space (TP: from the feet, forward)."""
        return V3(pos)

    def cook_fn(t, P):
        TW["last"] = None if t == 0 else TW["last"]
        place_body(P)
        f = t * COOK_N
        if f <= RAISE_N:
            w = smooth(f / RAISE_N)
            pos, turn, pole = blend(KEYS["low"], KEYS["cook"], w)
        else:
            s = (f - RAISE_N) / (COOK_N - RAISE_N)
            pos, turn, pole = KEYS["cook"]
            pos = Vector(pos) + Vector((math.sin(s * 2 * math.pi) * 0.004, math.sin(s * 4 * math.pi) * 0.003, 0))
        pose_hand(P, to_space(pos), turn, pole)

    def throw_fn(t, P):
        place_body(P)
        twist(P, t)
        pos, turn, pole = key_at(t)
        pose_hand(P, to_space(pos), turn, pole)

    pre = "FPGrenade_" if fp else "Grenade_"
    TW["last"] = None
    make(pre + "Cook", COOK_N, cook_fn, loop=False)
    TW["last"] = None
    make(pre + "Throw", THROW_N, throw_fn, loop=False)
    if not fp:
        make("Grenade_Rest", 1, lambda t, P: None, loop=False)
    log(f"{'FP' if fp else 'TP'} clips: {[a.name for a in bpy.data.actions]}")

    # the grenade in the hand (pin out), parented to the RightHand bone at the Cook pose
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=GRENADE)
    parts = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    for o in [o for o in bpy.data.objects if o not in before and o.type != "MESH"]: bpy.data.objects.remove(o, do_unlink=True)
    pins = [o for o in parts if o.name.startswith("Grenade_Pin")]
    parts = [o for o in parts if o not in pins]
    for o in pins: bpy.data.objects.remove(o, do_unlink=True)
    for o in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    with bpy.context.temp_override(object=parts[0], active_object=parts[0], selected_objects=parts, selected_editable_objects=parts):
        bpy.ops.object.join()
    gren = bpy.context.view_layer.objects.active
    gren.name = "FPGrenade" if fp else "TPGrenade"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    rig.animation_data.action = bpy.data.actions[pre + "Cook"]
    bpy.context.scene.frame_set(COOK_N + 1); upd()
    Hb = rig.matrix_world @ rig.pose.bones["RightHand"].matrix
    F = (Hb.to_3x3() @ H0.inverted() @ R0)            # the hand's (t, u, n) now
    centre = Hb.translation + F @ Vector(GRIP)
    want = Matrix.Translation(centre) @ (F @ Matrix.Rotation(math.radians(-90), 3, "X")).to_4x4()   # fuse along the fingers
    gren.parent = rig; gren.parent_type = "BONE"; gren.parent_bone = "RightHand"
    upd()
    gren.matrix_world = want
    upd()
    log(f"{gren.name} on RightHand, {len(gren.data.polygons)} faces")
    return rig, mesh, gren


def renders(rig, mesh, gren, fp, outdir):
    os.makedirs(outdir, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"; sc.render.resolution_x = 960; sc.render.resolution_y = 540
    w = sc.world or bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.5, 0.55, 0.6, 1); w.node_tree.nodes["Background"].inputs[1].default_value = 0.8
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sc.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(50), 0, math.radians(200)); sun.data.energy = 3
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
    shots = [("Cook", COOK_N + 1, "cook"), ("Cook", 3, "raise"), ("Throw", int(0.25 * THROW_N) + 1, "wind"),
             ("Throw", int(RELEASE_T * THROW_N) + 1, "release"), ("Throw", THROW_N + 1, "follow")]
    pre = "FPGrenade_" if fp else "Grenade_"
    for clip, f, nm in shots:
        rig.animation_data.action = bpy.data.actions[pre + clip]; sc.frame_set(f); upd()
        if fp:
            cam.data.lens_unit = "FOV"; cam.data.angle = math.radians(70)   # ~55 deg vertical at 16:9
            cam.location = (0, 0, 0); cam.rotation_euler = (math.radians(90), 0, math.radians(180))   # looking along -Y
            sc.render.filepath = os.path.join(outdir, f"FP_{nm}.png"); bpy.ops.render.render(write_still=True)
        else:
            for view, pos in (("side", Vector((-2.6, -0.6, 1.4))), ("front", Vector((-1.2, -2.6, 1.5)))):
                cam.data.lens = 35
                cam.location = pos; cam.rotation_euler = (Vector((0, 0, 1.25)) - pos).to_track_quat("-Z", "Y").to_euler()
                sc.render.filepath = os.path.join(outdir, f"TP_{nm}_{view}.png"); bpy.ops.render.render(write_still=True)
    log("renders in", outdir)


def export(rig, mesh, gren, fp):
    for a in bpy.data.actions: a.use_fake_user = True
    path = OUT + ("FP_Grenade.fbx" if fp else "Operative_Grenade.fbx")
    bpy.ops.object.select_all(action="DESELECT")
    objs = [rig, gren] + ([mesh] if fp else [])
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = rig
    rig.animation_data.action = None
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=False, bake_anim=True, bake_anim_use_all_actions=True,
                             bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
                             path_mode="STRIP", embed_textures=False, mesh_smooth_type="FACE")
    log("exported", path)


for fp in (True, False):
    if ONLY and ONLY != ("FP" if fp else "TP"): continue
    rig, mesh, gren = run(fp)
    if RENDER: renders(rig, mesh, gren, fp, RENDER)
    if EXPORT: export(rig, mesh, gren, fp)
