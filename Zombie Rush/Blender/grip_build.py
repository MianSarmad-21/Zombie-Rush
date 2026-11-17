"""Third-person gun holds for the Shadow Operative from the hand-built grips (grips.json, grip_lab.py).

Run:  blender -b Blender/Grip_<Gun>.blend -P Blender/grip_build.py -- <Gun> [--render DIR] [--export]

The grips (hand on gun, every finger) were built and checked by eye in Blender (grip_lab.py), once per
gun, in the gun's own space. This script carries them through the gun's clips (Idle / Fire / Reload /
Draw, which move the gun, its parts and the HandL path the support hand follows on reloads):
  - right hand: locked to the gun every frame (fingers as built);
  - left hand: locked to the gun in Idle / Fire; in Reload / Draw it follows HandL's motion away from its
    rest spot, and its fingers go from the grip to a holding curl while it is away;
  - torso turned / leaned per gun (rifle and shotgun: bladed stance), head turned back to the front;
  - arms by two-bone IK.
--export writes Assets/Models/2026-11-09_Guns/<Gun>/Survivor_<Gun>.fbx in the layout the game reads
(GunSetupTools.BodyClips): armature "Armature.002" with the gun's skeleton riding the right hand, takes
<Gun>_Idle / _Fire / _Reload / _Draw / _Rest.
"""
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

PROJECT = "C:/Mian Sarmad/Unity/Zombie-Rush/Zombie Rush"
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
GUN = argv[0]
RENDER = argv[argv.index("--render") + 1] if "--render" in argv else None
EXPORT = "--export" in argv


def log(*a):
    print("[grip_build]", *a, flush=True)


GR = json.load(open(os.path.join(PROJECT, "Blender", "grips.json")))[GUN]
META = GR["meta"]

body = bpy.data.objects["Player_Rig"]
mesh = max((o for o in bpy.data.objects if o.type == "MESH" and o.parent == body), key=lambda o: len(o.data.vertices))
MESH_NAME = mesh.name
gun_rig = bpy.data.objects[GUN + "_Rig"]
log(f"body {len(body.data.bones)} bones, RightHand rest {tuple(round(x, 3) for x in body.data.bones['RightHand'].head_local)}, mesh {mesh.name} {len(mesh.data.vertices)} verts")
# grip_lab works on Op_Rig / Op_Mesh
body.name, mesh.name = "Op_Rig", "Op_Mesh"
ns = {}
exec(open(os.path.join(PROJECT, "Blender", "grip_lab.py"), encoding="utf-8").read(), ns)
RIG, pb, upd, solve_arm, torso, reset_pose = ns["RIG"], ns["pb"], ns["upd"], ns["solve_arm"], ns["torso"], ns["reset_pose"]
DIGITS = ns["DIGITS"]

sc = bpy.context.scene
sc.render.fps = 30
body.animation_data_create()
body.animation_data.action = None
gun_rig.animation_data_create()
gun_rig.animation_data.action = None
for p in gun_rig.pose.bones:
    p.rotation_mode = "QUATERNION"
    p.rotation_quaternion = Quaternion()
    p.location = (0, 0, 0)
gun_rig.location = Vector(META["rig_loc"])
GUN_SCALE = META.get("scale", 1.0)          # the game scales the gun model on import (Pistol.fbx: 1.25)
gun_rig.scale = (GUN_SCALE,) * 3
reset_pose()
upd()


def bw(name):
    return gun_rig.matrix_world @ gun_rig.pose.bones[name].matrix


G_REST = bw("Gun")
HL_REST_G = G_REST.inverted() @ bw("HandL")        # HandL in gun space at rest
HAND_G = {s: Matrix(GR[s]["hand_in_gun"]) for s in ("Right", "Left")}
FING = {s: {n: Quaternion(q) for n, q in GR[s]["fingers"].items()} for s in ("Right", "Left")}


def curl_q(a):
    return Quaternion((1, 0, 0), math.radians(a))


HOLDING = {}       # the left hand's curl while it is away from the grip (a magazine / shells in hand)
for d in DIGITS:
    a = (20, 15, 10) if d == "Thumb" else (40, 50, 35)
    for k in (1, 2, 3):
        HOLDING[f"LeftHand{d}{k}"] = curl_q(a[k - 1])

TURN, LEAN = META["turn"], META["lean"]
POLE_R, POLE_L = Vector(META["pole_r"]), Vector(META["pole_l"])


def gdir(G, v):
    R = G.to_3x3().normalized()
    return (R @ Vector((-v[0], -v[2], v[1]))).normalized()


def head_front():
    """Turn the neck and head back so the face looks along the gun (the torso is turned)."""
    if not TURN:
        return
    for n, share in (("Neck", 0.45), ("Head", 0.55)):
        p = pb(n)
        p.rotation_quaternion = Quaternion()
        upd()
        M = p.matrix.copy()
        h = M.translation.copy()
        R = Matrix.Rotation(math.radians(TURN * share), 4, "Z") @ Matrix.Rotation(math.radians(-4 * share), 4, "X")
        p.matrix = Matrix.Translation(h) @ R @ Matrix.Translation(-h) @ M
        upd()


ARM_BONES = [b.name for b in RIG.data.bones if any(k in b.name for k in ("Shoulder", "Arm", "Hand"))]
KEY_BONES = ["Chest", "UpperChest", "Neck", "Head"] + ARM_BONES


def pose_frame(clip):
    G = bw("Gun")
    gl_, gr_, gs_ = G.decompose()
    G = Matrix.LocRotScale(gl_, gr_, None)
    right = G @ HAND_G["Right"]
    if clip in ("Reload", "Draw"):
        H = G.inverted() @ bw("HandL")
        loc, rot, sca = H.decompose()
        H = HL_REST_G if min(abs(s) for s in sca) < 1e-3 else Matrix.LocRotScale(loc, rot, None)   # scale-free path
        left_g = H @ HL_REST_G.inverted() @ HAND_G["Left"]
    else:
        left_g = HAND_G["Left"]
    left = G @ left_g
    away = (left_g.translation - HAND_G["Left"].translation).length
    w = max(0.0, min(1.0, (0.05 - away) / 0.035))      # 1 on the grip, 0 when 5 cm or more away
    torso(TURN, LEAN)
    head_front()
    # elbow as a hinge, placed where the wrist is straightest; forearm roll shared elbow / wrist
    reach_r = ns["solve_arm2"]("Right", right)
    try:
        reach_l = ns["solve_arm2"]("Left", left, relax=0.8 * (1.0 - w))   # away from the gun: natural wrist
    except TypeError:
        S = RIG.pose.bones["LeftUpperArm"].head
        log(f"  {clip} frame {sc.frame_current}: left target {tuple(round(x, 3) for x in left.translation)} "
            f"is {((left.translation - S).length):.3f} m from the shoulder; holding the last good hand")
        reach_l = ns["solve_arm2"]("Left", LAST_LEFT[0] if LAST_LEFT else G @ HAND_G["Left"])
    else:
        LAST_LEFT[:] = [left]
    for n, q in FING["Right"].items():
        pb(n).rotation_quaternion = q
    for n, q in FING["Left"].items():
        pb(n).rotation_quaternion = HOLDING[n].slerp(q, w)
    upd()
    WRIST.append((ns["wrist_cost"]("Right")[1], ns["wrist_cost"]("Left")[1]))
    return reach_r, reach_l, w


WRIST = []
LAST_LEFT = []


def POLE_R_W(G):
    return gdir(G, POLE_R)


def POLE_L_W(G):
    return gdir(G, POLE_L)


CLIPS = []
for c in ("Idle", "Fire", "Reload", "Draw"):
    a = bpy.data.actions[c]
    CLIPS.append((c, int(round(a.frame_range[1]))))

made = []
for clip, last in CLIPS:
    name = f"{GUN}_{clip}"
    old = bpy.data.actions.get(name)
    if old:
        bpy.data.actions.remove(old)
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    gun_rig.animation_data.action = bpy.data.actions[clip]
    worst = [0, 0]
    WRIST.clear()
    for f in range(1, last + 1):
        body.animation_data.action = None
        sc.frame_set(f)
        upd()
        rr, rl, w = pose_frame(clip)
        worst = [max(worst[0], rr), max(worst[1], rl)]
        body.animation_data.action = act
        for n in KEY_BONES:
            pb(n).keyframe_insert("rotation_quaternion", frame=f, group=n)
        body.animation_data.action = None
    act.use_frame_range = True
    act.frame_start, act.frame_end = 1, last
    act.use_cyclic = clip == "Idle"
    made.append((act, bpy.data.actions[clip], last))
    mx = lambda i, k: max(abs(w[i][k]) for w in WRIST)
    log(f"{name}: {last} frames, max reach R {worst[0]:.2f} L {worst[1]:.2f}; worst wrist (back, sideways, twist) "
        f"R {mx(0, 0)}/{mx(0, 1)}/{mx(0, 2)} L {mx(1, 0)}/{mx(1, 1)}/{mx(1, 2)}")

old = bpy.data.actions.get(f"{GUN}_Rest")
if old:
    bpy.data.actions.remove(old)
rest = bpy.data.actions.new(f"{GUN}_Rest")
rest.use_fake_user = True
reset_pose()
body.animation_data.action = rest
for f in (1, 2):
    for b in RIG.data.bones:
        pb(b.name).keyframe_insert("rotation_quaternion", frame=f, group=b.name)
body.animation_data.action = None

# ------------------------------------------------------------------ pictures
if RENDER:
    os.makedirs(RENDER, exist_ok=True)
    import numpy as np
    for o in [o for o in bpy.data.objects if o.type == "CAMERA"]:
        bpy.data.objects.remove(o)

    def camera(name, loc, look, lens=40):
        c = bpy.data.objects.new(name, bpy.data.cameras.new(name))
        sc.collection.objects.link(c)
        c.location = loc
        c.rotation_euler = (Vector(look) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        c.data.lens = lens
        c.data.clip_start = 0.01
        return c
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "OBJECT"
    sc.world = sc.world or bpy.data.worlds.new("W")
    sc.world.color = (0.32, 0.36, 0.42)
    mesh.color = (0.62, 0.57, 0.5, 1)
    for o in bpy.data.objects:
        if o.type == "MESH" and o.parent == gun_rig:
            o.color = (0.12, 0.12, 0.13, 1)
    g0 = G_REST.translation
    cams = [camera("Side", g0 + Vector((-0.9, 0.15, 0.05)), g0 + Vector((0, 0.15, 0.0)), 50),
            camera("Front34", g0 + Vector((-0.5, -0.8, 0.15)), g0 + Vector((0.05, 0.1, -0.05)), 45),
            camera("Behind", g0 + Vector((-0.35, 1.0, 0.35)), g0 + Vector((0.0, -0.2, 0.0)), 45),
            camera("Full", Vector((-2.6, -1.6, 1.2)), Vector((0, -0.2, 1.0)), 50)]
    RX, RY = 480, 360
    sc.render.resolution_x, sc.render.resolution_y = RX, RY
    for act, ga, last in made:
        body.animation_data.action = act
        gun_rig.animation_data.action = ga
        frames = [1 + round(i * (last - 1) / 4) for i in range(5)] if act.name.endswith(("Reload", "Draw")) else [1]
        rows = []
        for c in cams:
            row = []
            for f in frames:
                sc.frame_set(f)
                sc.camera = c
                sc.render.filepath = os.path.join(RENDER, "_t.png")
                bpy.ops.render.render(write_still=True)
                im = bpy.data.images.load(sc.render.filepath)
                row.append(np.array(im.pixels[:], np.float32).reshape(RY, RX, 4))
                bpy.data.images.remove(im)
            rows.append(np.concatenate(row, axis=1))
        sheet = np.concatenate(rows[::-1], axis=0)
        im = bpy.data.images.new("s", sheet.shape[1], sheet.shape[0], alpha=True)
        im.pixels.foreach_set(sheet.ravel())
        im.filepath_raw = os.path.join(RENDER, f"{act.name}.png")
        im.file_format = "PNG"
        im.save()
        bpy.data.images.remove(im)
    body.animation_data.action = None
    gun_rig.animation_data.action = None
    log("sheets in", RENDER)

# ------------------------------------------------------------------ export
if EXPORT:
    gun_rig.animation_data.action = None
    sc.frame_set(1)
    for p in gun_rig.pose.bones:
        p.rotation_quaternion = Quaternion()
        p.location = (0, 0, 0)
    body.animation_data.action = None
    pose_frame("Idle")
    upd()
    gun_rig.scale = (1, 1, 1)                # exported unscaled: the game's gun model carries the scale itself
    upd()
    mw = gun_rig.matrix_world.copy()
    gun_rig.parent = body
    gun_rig.parent_type = "BONE"
    gun_rig.parent_bone = "RightHand"
    upd()
    gun_rig.matrix_world = mw
    upd()
    out = os.path.join(PROJECT, "Assets", "Models", "2026-11-09_Guns", GUN, f"Survivor_{GUN}.fbx")
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    body.select_set(True)
    gun_rig.select_set(True)
    bpy.context.view_layer.objects.active = body
    body.name = "Armature.002"          # the survivor's armature node in the game
    bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={"ARMATURE"}, add_leaf_bones=False,
                             apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y", bake_space_transform=False,
                             bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)
    body.name = "Op_Rig"
    gun_rig.parent = None
    gun_rig.matrix_world = mw
    log("exported", out)

body.name, mesh.name = "Player_Rig", MESH_NAME
if "--save" in argv:
    bpy.ops.wm.save_mainfile()
    log("saved", bpy.data.filepath)
