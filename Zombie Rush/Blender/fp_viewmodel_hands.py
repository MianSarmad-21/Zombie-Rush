"""First-person arms ("viewmodel") for one gun, posed against the camera.

Run (from the project folder; the grip blend is only read):
  blender -b Blender/Grip_<Gun>.blend -P Blender/fp_viewmodel_hands.py -- <Pistol|Rifle|Shotgun> [--render DIR] [--sheets] [--views] [--export] [--save] [--set key=value ...]

Shooters draw a separate arms rig fixed to the camera; this script builds one:
  1. reads the gun clips and the third-person grip clips (Grip_<Gun>.blend): the gun's motion, and the
     left hand's path relative to the gun (reloads, draws);
  2. imports the survivor, keeps only the arms (sleeves) as FP_Arms, lengthens them to normal human
     proportions, gives the hands full-length fingers (fp_hybrid_hands.py) and dresses them in tactical
     gloves and jacket sleeves (fp_gloves.py; --bare skips it);
  3. holds the gun the way shooters do (HOLDS below, in the gun's own space): each hand is turned onto
     its grip and slid in until the palm touches, the trigger finger and thumbs reach their spots, the
     other fingers close joint by joint until they touch the gun or the other hand;
  4. places the gun in front of the camera (GUNS[gun]["at"], yaw/pitch/roll), solves the arms onto the
     hands every frame, keys FP<Gun>_Idle/Fire/Reload/Draw on FP_Rig and exports
     Assets/Models/2026-11-10_FPArms/FP_<Gun>.fbx (FP_Rig, FP_Arms with sleeve/glove/skin materials,
     the gun's armature riding the right hand).
In the game the instance sits under the camera at identity, so this file's origin is the eye.
"""
import bpy, math, os, sys
import numpy as np
from mathutils import Vector, Quaternion, Matrix
from mathutils.bvhtree import BVHTree

PROJECT = "C:/Mian Sarmad/Unity/Zombie-Rush/Zombie Rush"
SURV = PROJECT + "/Assets/Models/2026-11-08_WastelandSurvivor_Final/WastelandSurvivor.fbx"
OUT_DIR = PROJECT + "/Assets/Models/2026-11-10_FPArms"
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
GUN = argv[0] if argv else "Rifle"
def opt(name):
    return argv[argv.index(name) + 1] if name in argv and argv.index(name) + 1 < len(argv) else None
RENDER = opt("--render")

import time
_T0 = time.time()
def log(*a): print(f"[fpvm {time.time() - _T0:6.1f}s]", *a, flush=True)

# ------------------------------------------------------------------ tuning
# Camera space: (right, up, forward) metres from the eye. Angles in degrees.
#   at       : where the gun's Gun bone sits (Idle frame 1)
#   yaw      : muzzle turned left (toward the screen centre) ; pitch: muzzle up ; roll: top turned right
#   sh_mid   : midpoint between the shoulder joints ; torso_yaw: + turns the chest left (right shoulder forward)
#   pole_r/l : which way each elbow points (right, up, forward)
COMMON = dict(k_upper=1.55, k_fore=1.12, fov=55.0, forearm_twist=0.6, reach_max=0.9, keep_fwd=0.30, keep_down=0.10,
              tw_up=1.0, tw_fore=1.0, tw_wrist=1.0)
GUNS = {
    "Pistol": dict(at=(0.12, -0.17, 0.52), yaw=12.0, pitch=1.5, roll=-6.0,
                   sh_mid=(0.0, -0.24, -0.02), torso_yaw=0.0,
                   pole_r=(0.6, -1.0, -0.2), pole_l=(-0.6, -1.0, -0.2)),
    "Rifle": dict(at=(0.125, -0.165, 0.31), yaw=2.5, pitch=0.5, roll=2.0,
                  sh_mid=(0.0, -0.22, 0.0), torso_yaw=-25.0,
                  pole_r=(0.7, -1.0, -0.3), pole_l=(-0.4, -1.0, 0.0)),
    "Shotgun": dict(at=(0.13, -0.19, 0.34), yaw=5.0, pitch=1.0, roll=2.0,
                    sh_mid=(0.0, -0.22, 0.0), torso_yaw=-25.0,
                    pole_r=(0.7, -1.0, -0.3), pole_l=(-0.4, -1.0, 0.0)),
}
T = dict(COMMON); T.update(GUNS[GUN])
for _i, _a in enumerate(argv):
    if _a == "--set" and _i + 1 < len(argv):
        _k, _v = argv[_i + 1].split("=", 1)
        T[_k] = tuple(float(x) for x in _v.split(",")) if "," in _v else float(_v)
        log(f"set {_k} = {T[_k]}")

# Holds, in the gun's own space as (right, up, forward) from its Gun bone (gun at rest):
#   t      : the hand's thumb-side direction (across the knuckles, little finger -> index)
#   n      : the way the palm faces (made square to t)
#   palm   : the gun point the palm centre starts from; the hand then slides along n until it touches
#   trigger: where the trigger finger's pad goes (front face of the trigger)
#   thumb  : where the thumb's pad goes
#   close  : the fingers that close on the gun (and on the other hand)
HOLDS = {
    "Pistol": {
        "Right": dict(t=(0, 0.92, 0.40), n=(-0.97, 0, 0.25), palm=(0.045, 0.000, -0.030), palm_at=(0.0, 0.050), sink=0.004,
                      trigger=(0.0, 0.016, 0.073), thumb="close", close=("Middle", "Ring", "Pinky")),
        "Left": dict(t=(0, 0.85, 0.53), n=(0.96, 0, 0.26), palm=(-0.055, -0.016, -0.045), palm_at=(0.0, 0.050), sink=0.004,
                     thumb=(-0.027, 0.012, 0.075), close=("Index", "Middle", "Ring", "Pinky")),
    },
    "Rifle": {
        "Right": dict(t=(0, 0.96, 0.29), n=(-0.90, 0, 0.44), palm=(0.045, 0.035, -0.040), palm_at=(0.0, 0.050), sink=0.004,
                      trigger=(0.0, 0.062, 0.050), thumb=(0.030, 0.070, 0.000), close=("Middle", "Ring", "Pinky")),
        "Left": dict(t=(0, 0.15, 0.99), n=(-0.15, 1.0, 0), palm=(-0.016, 0.040, 0.290), palm_at=(0.0, 0.050), sink=0.003,
                     thumb=(-0.032, 0.076, 0.400), close=("Index", "Middle", "Ring", "Pinky")),
    },
    "Shotgun": {
        "Right": dict(t=(0, 0.45, 0.89), n=(-1.0, 0.10, 0), palm=(0.055, 0.030, 0.050), palm_at=(0.0, 0.050), sink=0.004,
                      trigger=(0.0, 0.012, 0.104), thumb=(-0.022, 0.066, 0.065), close=("Middle", "Ring", "Pinky")),
        "Left": dict(t=(0, 0.10, 0.99), n=(-0.15, 1.0, 0), palm=(-0.012, 0.000, 0.360), palm_at=(0.0, 0.050), sink=0.003,
                     thumb=(-0.052, 0.085, 0.420), close=("Index", "Middle", "Ring", "Pinky")),
    },
}
HOLD = HOLDS[GUN]
for _k, _v in list(T.items()):                    # --set R_palm=..., L_thumb=... reach into the holds
    if _k[:2] in ("R_", "L_"):
        HOLD["Right" if _k[0] == "R" else "Left"][_k[2:]] = _v

RIGHT, UP, FWD = Vector((-1, 0, 0)), Vector((0, 0, 1)), Vector((0, -1, 0))   # body/camera axes (faces -Y)
LEFT, DOWN, BACK = -RIGHT, -UP, -FWD
def V3(t):
    """(right, up, forward) -> Blender vector."""
    return RIGHT * t[0] + UP * t[1] + FWD * t[2]
GV = V3          # the gun's own space uses the same axes: its Gun bone faces -Y, up +Z, right -X

sc = bpy.context.scene
sc.render.fps = 30
def upd(): bpy.context.view_layer.update()

# ------------------------------------------------------------------ 1. read the clips
grip = bpy.data.objects["Player_Rig"]
gun = bpy.data.objects[GUN + "_Rig"]
CLIPS = []
for c in ("Idle", "Fire", "Reload", "Draw"):
    a = bpy.data.actions[GUN + "_" + c]
    CLIPS.append((c, int(round(a.frame_range[1])) - 1))
GOBJ = gun.matrix_world.copy()
GUN_MESHES = [o for o in bpy.data.objects if o.type == "MESH" and o.parent == gun]

def gun_local_bvh(gunb):
    """The gun's surface at the current frame in its Gun bone's space."""
    dg = bpy.context.evaluated_depsgraph_get()
    inv = gunb.inverted(); vs, ps = [], []
    for o in GUN_MESHES:
        e = o.evaluated_get(dg); me = e.to_mesh(); base = len(vs)
        M = inv @ o.matrix_world
        vs += [M @ v.co for v in me.vertices]
        ps += [[base + i for i in p.vertices] for p in me.polygons]
        e.to_mesh_clear()
    return BVHTree.FromPolygons(vs, ps, epsilon=0.0)

SAMPLES = {}
for c, n in CLIPS:
    gun.animation_data.action = bpy.data.actions[c]
    grip.animation_data.action = bpy.data.actions[GUN + "_" + c]
    rows = []
    for f in range(1, n + 2):
        sc.frame_set(f); upd()
        gunb = gun.matrix_world @ gun.pose.bones["Gun"].matrix
        rows.append(dict(frame=f, gunb=gunb,
                         hand={s: grip.matrix_world @ grip.pose.bones[s + "Hand"].matrix for s in ("Right", "Left")}))
    SAMPLES[c] = rows
G1 = SAMPLES["Idle"][0]["gunb"]
if "--quick" in argv:                              # hold check only: one Idle frame
    CLIPS = [("Idle", 0)]; SAMPLES = {"Idle": SAMPLES["Idle"][:1]}
log(f"clips {CLIPS}; Gun bone at Idle 1 vs the rig root: {tuple(round(x, 3) for x in (GOBJ.inverted() @ G1).translation)}")
GRIP_ML = {b.name: b.matrix_local.copy() for b in grip.data.bones}

# the bore: the gun armature's -Y (muzzle) and +Z (top) as the Idle hold carries them; first person
# starts from a level, upright gun.
_gml = gun.data.bones["Gun"].matrix_local.to_3x3().inverted()
BORE = (G1.to_3x3() @ _gml @ Vector((0, -1, 0))).normalized()
TOP = (G1.to_3x3() @ _gml @ Vector((0, 0, 1))).normalized()
_q1 = BORE.rotation_difference(FWD)
_top = _q1 @ TOP; _top = (_top - FWD * _top.dot(FWD)).normalized()
LEVEL = (Quaternion(FWD, UP.angle(_top) * (1 if UP.cross(_top).dot(FWD) < 0 else -1)) @ _q1).to_matrix().to_4x4()
Rg = (Quaternion(UP, math.radians(T["yaw"])) @ Quaternion(RIGHT, math.radians(T["pitch"]))
      @ Quaternion(FWD, math.radians(T["roll"]))).to_matrix().to_4x4()
TO_CAM = Matrix.Translation(V3(T["at"])) @ Rg @ LEVEL @ Matrix.Translation(-G1.translation)

# ------------------------------------------------------------------ 2. the arms rig
for o in list(bpy.data.objects):
    if o.name.startswith("FP_"): bpy.data.objects.remove(o, do_unlink=True)
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=SURV)
new = [o for o in bpy.data.objects if o not in before]
rig = next(o for o in new if o.type == "ARMATURE"); rig.name = "FP_Rig"
meshes = [o for o in new if o.type == "MESH"]
arms = max((o for o in meshes if "Backpack" not in o.name), key=lambda o: len(o.data.vertices)); arms.name = "FP_Arms"
for o in meshes:
    if o is not arms: bpy.data.objects.remove(o, do_unlink=True)
for a in [x for x in bpy.data.actions if "|" in x.name]: bpy.data.actions.remove(a)
rig.animation_data_create(); rig.animation_data.action = None
for pb in rig.pose.bones:
    pb.rotation_mode = "QUATERNION"; pb.rotation_quaternion = Quaternion(); pb.location = (0, 0, 0); pb.scale = (1, 1, 1)
upd()
log(f"FP_Rig world is identity: {all(abs(a - b) < 1e-5 for ra, rb in zip(rig.matrix_world, Matrix.Identity(4)) for a, b in zip(ra, rb))}")

# keep only the arms: upper arms, forearms, hands
keep_groups = [vg.index for vg in arms.vertex_groups if any(k in vg.name for k in ("UpperArm", "LowerArm", "Hand"))]
drop = [v.index for v in arms.data.vertices if sum(g.weight for g in v.groups if g.group in keep_groups) < 0.5]
import bmesh
bm = bmesh.new(); bm.from_mesh(arms.data); bm.verts.ensure_lookup_table()
bmesh.ops.delete(bm, geom=[bm.verts[i] for i in drop], context="VERTS")
bm.to_mesh(arms.data); bm.free(); arms.data.update()
log(f"FP_Arms: {len(arms.data.vertices)} vertices kept, {len(drop)} dropped")

# lengthen the arms: scale the bones along their length, bake the mesh, make that the rest pose
def ctx(obj, **kw):
    return bpy.context.temp_override(object=obj, active_object=obj, selected_objects=[obj], selected_editable_objects=[obj], **kw)
L0 = {s: ((rig.data.bones[s + "LowerArm"].head_local - rig.data.bones[s + "UpperArm"].head_local).length,
          (rig.data.bones[s + "Hand"].head_local - rig.data.bones[s + "LowerArm"].head_local).length) for s in ("Right", "Left")}
for s in ("Right", "Left"):
    rig.data.bones[s + "LowerArm"].inherit_scale = "NONE"
    rig.data.bones[s + "Hand"].inherit_scale = "NONE"
    rig.pose.bones[s + "UpperArm"].scale = (1, T["k_upper"], 1)
    rig.pose.bones[s + "LowerArm"].scale = (1, T["k_fore"], 1)
upd()
if arms.data.shape_keys:
    with ctx(arms): bpy.ops.object.shape_key_remove(all=True, apply_mix=True)
mod = next(m for m in arms.modifiers if m.type == "ARMATURE")
with ctx(arms): bpy.ops.object.modifier_apply(modifier=mod.name)
bpy.context.view_layer.objects.active = rig
for o in bpy.context.view_layer.objects: o.select_set(o is rig)
with ctx(rig): bpy.ops.object.mode_set(mode="POSE")
bpy.ops.pose.select_all(action="SELECT")
bpy.ops.pose.armature_apply(selected=False)
bpy.ops.object.mode_set(mode="OBJECT")
for s in ("Right", "Left"):
    rig.data.bones[s + "LowerArm"].inherit_scale = "FULL"
    rig.data.bones[s + "Hand"].inherit_scale = "FULL"
for pb in rig.pose.bones: pb.scale = (1, 1, 1)
upd()
for s in ("Right", "Left"):
    u = (rig.data.bones[s + "LowerArm"].head_local - rig.data.bones[s + "UpperArm"].head_local).length
    f = (rig.data.bones[s + "Hand"].head_local - rig.data.bones[s + "LowerArm"].head_local).length
    log(f"{s} arm: upper {L0[s][0]:.3f} -> {u:.3f} m, forearm {L0[s][1]:.3f} -> {f:.3f} m")

# new hands (bones + mesh + grip solver)
HX = dict(globals())
# hybrid: survivor arms/palms/thumbs + full-length fingers; survivor: its own stub fingers cut apart; procedural: built gloves
HANDS = opt("--hands") or "procedural"
HX["FULL_GLOVES"] = "--bare" not in argv
exec(open(os.path.join(PROJECT, "Blender", {"hybrid": "fp_hybrid_hands.py", "survivor": "fp_survivor_hands.py",
                                            "procedural": "fp_hands.py"}[HANDS]), encoding="utf-8").read(), HX)
m2 = arms.modifiers.new("Armature", "ARMATURE"); m2.object = rig
upd()
# dress the hybrid arms: tactical gloves, glove strap and knuckle pad, jacket sleeves (fp_gloves.py); --bare keeps skin
GLOVES = HANDS in ("hybrid", "procedural") and "--bare" not in argv
if GLOVES:
    _G = dict(globals()); _G["HX"] = HX
    exec(open(os.path.join(PROJECT, "Blender", "fp_gloves.py"), encoding="utf-8").read(), _G)

g = {"ARM_NAME": "FP_Rig", "MESH_NAME": "FP_Arms", "PFX_OVERRIDE": "",
     "AL_OVERRIDE": {k: k for k in ("Hips", "Spine", "Chest", "UpperChest", "Neck", "Head", "LeftShoulder", "RightShoulder",
                                    "LeftUpperArm", "RightUpperArm", "LeftLowerArm", "RightLowerArm", "LeftHand", "RightHand",
                                    "LeftUpperLeg", "RightUpperLeg", "LeftLowerLeg", "RightLowerLeg", "LeftFoot", "RightFoot",
                                    "LeftToes", "RightToes")},
     "__name__": "zr_anim"}
exec(open(os.path.join(PROJECT, "Blender", "zr_anim_lib.py"), encoding="utf-8").read(), g)
Pose, ML, BONES, PAR = g["Pose"], g["ML"], g["BONES"], g["PAR"]
if "--handtest" in argv and RENDER:
    # the bare right hand at rest with the fingers half folded, from the palm side and the thumb side
    os.makedirs(RENDER, exist_ok=True)
    sc.render.engine = "BLENDER_WORKBENCH"; sc.display.shading.light = "STUDIO"; sc.display.shading.color_type = "MATERIAL"
    sc.render.resolution_x, sc.render.resolution_y = 900, 900
    for o in bpy.data.objects:
        if o.type == "MESH" and o is not arms: o.hide_render = True
    for b in rig.pose.bones:
        if "Hand" in b.name and b.name[-1] in "123":
            k = int(b.name[-1]) - 1
            b.rotation_quaternion = HX["basis_q"](*((25, 15, 15)[k], 0, 0) if "Thumb" in b.name else ((35, 50, 30)[k], 0, 0))
    upd()
    R, W = HX["HAND_INFO"]["Right"]["R"], HX["HAND_INFO"]["Right"]["W"]
    c = W + R @ Vector((0, 0.08, 0))
    cam = bpy.data.objects.new("HT", bpy.data.cameras.new("HT")); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.type = "ORTHO"; cam.data.ortho_scale = 0.30
    for nm, d, upv in (("palm", R @ Vector((0, 0, 1)), R @ Vector((0, 1, 0))), ("thumbside", R @ Vector((1, 0, 0)), R @ Vector((0, 0, 1))),
                       ("back", R @ Vector((0, 0, -1)), R @ Vector((0, 1, 0)))):
        cam.location = c + d * 0.5
        zc = d.normalized(); yc = (upv - zc * upv.dot(zc)).normalized(); xc = yc.cross(zc)
        cam.matrix_world = Matrix((xc, yc, zc)).transposed().to_4x4() @ Matrix.Identity(4)
        cam.location = c + d * 0.5
        sc.render.filepath = os.path.join(RENDER, f"HandTest_{nm}.png"); bpy.ops.render.render(write_still=True)
    for b in rig.pose.bones: b.rotation_quaternion = Quaternion()
    for o in bpy.data.objects:
        if o.type == "MESH": o.hide_render = False
    log("hand test renders done"); raise SystemExit
ARM_LEN = {s: g["LEN"]["R_arm" if s == "Right" else "L_arm"] for s in ("Right", "Left")}

# ------------------------------------------------------------------ 3. the holds (gun space)
def frame_from(side, h):
    t = GV(h["t"]).normalized()
    n = GV(h["n"]); n = (n - t * n.dot(t)).normalized()
    u = n.cross(t) if side == "Right" else t.cross(n)
    F = Matrix((t, u, n)).transposed().to_4x4()
    # the palm centre (t 0, u palm_at[1], palm surface) starts at the gun point h["palm"]
    pc = Vector((h["palm_at"][0], h["palm_at"][1], HX["PALM"]["n_palm"]))
    F.translation = GV(h["palm"]) - F.to_3x3() @ pc
    return F

gun.animation_data.action = bpy.data.actions["Idle"]; sc.frame_set(1); upd()
BVH0 = gun_local_bvh(G1)
Obstacles, slide_to_contact, close_digit, reach_digit = HX["Obstacles"], HX["slide_to_contact"], HX["close_digit"], HX["reach_digit"]
relaxed, hand_capsules, hand_bone_from_tun, HB2TUN = HX["relaxed"], HX["hand_capsules"], HX["hand_bone_from_tun"], HX["HB2TUN"]

def solve_fingers(side, Mh, obs, h, quiet=False):
    """Finger angles for a hand bone matrix Mh (gun space) on obstacles obs."""
    ang = {}
    rep = []
    if h.get("trigger") is not None:
        a, miss, gap = reach_digit(obs, side, "Index", Mh, GV(h["trigger"]),
                                   grid=[range(-10, 81, 6), range(-40, 41, 8), range(0, 101, 10), [None]])
        ang["Index"] = a; rep.append(f"trigger pad off {miss * 1000:.1f} mm (gaps {gap}, angles {[tuple(round(x) for x in q) for q in a]})")
    if h.get("thumb") == "close":
        best = None
        for sw in (-40.0, -20.0, 0.0, 20.0, 40.0):           # the swing that lets it fold furthest round the grip
            a = close_digit(obs, side, "Thumb", Mh, [(-35.0, sw, 0.0), (-10.0, 0.0, 0.0), (0.0, 0.0, 0.0)], (60.0, 70.0, 60.0))
            tot = sum(x[0] for x in a)
            if best is None or tot > best[0]: best = (tot, a)
        ang["Thumb"] = best[1]; rep.append(f"thumb closed {[tuple(round(x) for x in q[:2]) for q in best[1]]}")
    elif h.get("thumb") is not None:
        a, miss, gap = reach_digit(obs, side, "Thumb", Mh, GV(h["thumb"]),
                                   grid=[range(-30, 61, 10), range(-50, 51, 10), range(-10, 61, 10), [None]])
        ang["Thumb"] = a; rep.append(f"thumb pad off {miss * 1000:.1f} mm (gaps {gap})")
    for d in h["close"]:
        ang[d] = close_digit(obs, side, d, Mh, [(0.0, 0.0, 0.0)] * 3, (90.0, 100.0, 75.0))
    for d in HX["DIGITS"]:
        ang.setdefault(d, relaxed(d))
    if not quiet:
        log(f"{GUN} {side}: " + "; ".join(rep) + "; folds " +
            ", ".join(f"{d} {[round(a[0]) for a in ang[d]]}" for d in HX["FINGERS4"]))
    return ang

HOLD_F, HOLD_ANG = {}, {}
obs = Obstacles(BVH0)
F = frame_from("Right", HOLD["Right"])
F = slide_to_contact(obs, "Right", F, F.col[2].to_3d().normalized(), sink=HOLD["Right"].get("sink", 0.0))
HOLD_F["Right"] = F
MhR = hand_bone_from_tun("Right", F)
HOLD_ANG["Right"] = solve_fingers("Right", MhR, obs, HOLD["Right"])
CAPS_R = hand_capsules("Right", MhR, HOLD_ANG["Right"])
obsL = Obstacles(BVH0, CAPS_R)
F = frame_from("Left", HOLD["Left"])
F = slide_to_contact(obsL, "Left", F, F.col[2].to_3d().normalized(), sink=HOLD["Left"].get("sink", 0.0))
HOLD_F["Left"] = F
HOLD_ANG["Left"] = solve_fingers("Left", hand_bone_from_tun("Left", F), obsL, HOLD["Left"])
for s in ("Right", "Left"):
    p = HOLD_F[s].translation
    log(f"{s} wrist in gun space (right, up, fwd) {tuple(round(x, 3) for x in (p.dot(RIGHT), p.dot(UP), p.dot(FWD)))}")

# the left hand's own path in the clips (relative to the gun), so reloads keep their moves: near its
# Idle spot it takes the new hold; as it leaves, the clip's path takes over.
def rel(row, s): return row["gunb"].inverted() @ row["hand"][s]
L_IDLE = rel(SAMPLES["Idle"][0], "Left")
L_HOLD_BONE = hand_bone_from_tun("Left", HOLD_F["Left"])
D_L = L_HOLD_BONE @ L_IDLE.inverted()
def left_rel(row):
    r = rel(row, "Left")
    dp = (r.translation - L_IDLE.translation).length
    da = r.to_quaternion().rotation_difference(L_IDLE.to_quaternion()).angle
    x = (dp + da * 0.05 - 0.015) / 0.06
    w = 1.0 - max(0.0, min(1.0, x)); w = w * w * (3 - 2 * w)
    if w >= 0.999: return D_L @ r, 1.0
    if w <= 0.001: return r, 0.0
    q = Quaternion().slerp(D_L.to_quaternion(), w)
    Dw = q.to_matrix().to_4x4(); Dw.translation = D_L.translation * w
    return Dw @ r, w

# ------------------------------------------------------------------ 4. pose helpers
def set_bone_world_rot(P, name, R):
    M = P.fk(); p = PAR[name]
    base = (M[p] @ (ML[p].inverted() @ ML[name])).to_3x3()
    P.q[name] = (base.inverted() @ R).to_quaternion()

def bone_twist(P, name):
    q = P.q[name]
    a = math.degrees(2 * math.atan2(q.y, q.w))
    return (a + 180) % 360 - 180

ROLL_LAST, TW_LAST, ROLL_REF, TW_REF = {}, {}, {}, {}
def solve_arm(P, side, wrist, pole, R_hand):
    """Arm IK with the twist shared out like a real arm: the upper arm rolls in the shoulder, the
    forearm takes a share of the hand's turn, the wrist the rest."""
    up, lo, hn = side + "UpperArm", side + "LowerArm", side + "Hand"
    P.arm_ik(side, wrist, pole=pole)
    M = P.fk(); sh = M[up].translation.copy(); elbow = M[lo].translation.copy()
    rel_ = ML[lo].to_3x3().inverted() @ ML[hn].to_3x3()
    ideal = R_hand @ rel_.inverted()
    rolls = [ROLL_LAST[side] + d for d in range(-20, 21, 4)] if side in ROLL_LAST else range(-150, 151, 10)
    best = None
    for ru in rolls:
        P.aim(up, elbow - sh, roll=ru)
        P.aim(lo, wrist - elbow)
        Fm = P.fk()[lo].to_3x3(); y = Fm.col[1].normalized()
        xc = Fm.col[0] - y * Fm.col[0].dot(y); xi = ideal.col[0] - y * ideal.col[0].dot(y)
        tw = math.degrees(math.atan2(xc.cross(xi).dot(y), xc.dot(xi))) if xc.length > 1e-6 and xi.length > 1e-6 else 0.0
        prev = TW_LAST.get(side, TW_REF.get(side))
        if prev is not None:
            while tw - prev > 180: tw -= 360
            while tw - prev < -180: tw += 360
        P.aim(lo, wrist - elbow, roll=tw * T["forearm_twist"])
        set_bone_world_rot(P, hn, R_hand)
        t_up, t_hn = bone_twist(P, up), bone_twist(P, hn)
        cost = T["tw_up"] * t_up ** 2 + T["tw_fore"] * (tw * T["forearm_twist"]) ** 2 + T["tw_wrist"] * t_hn ** 2
        if prev is not None: cost += 40 * abs(tw - prev)
        if side not in ROLL_LAST and side in ROLL_REF: cost += 20 * abs(ru - ROLL_REF[side])
        if best is None or cost < best[0]: best = (cost, ru, tw)
    _, ru, tw = best
    ROLL_REF.setdefault(side, ru); ROLL_LAST[side] = ru
    TW_REF.setdefault(side, tw); TW_LAST[side] = tw
    P.aim(up, elbow - sh, roll=ru)
    P.aim(lo, wrist - elbow, roll=tw * T["forearm_twist"])
    set_bone_world_rot(P, hn, R_hand)
    return tw * T["forearm_twist"]

def torso(P, sh_mid):
    """Turn the chest (Hips) and move the body so the shoulder midpoint sits at sh_mid (camera space)."""
    P.rot("Hips", rz=T["torso_yaw"])
    P.hips = Vector((0, 0, 0))
    M = P.fk()
    cur = (M["RightUpperArm"].translation + M["LeftUpperArm"].translation) / 2
    P.hips = sh_mid - cur

def hand_targets(row):
    """Both hand bones in gun space, and in camera space, for one frame."""
    Gc = TO_CAM @ row["gunb"]
    loc = {"Right": hand_bone_from_tun("Right", HOLD_F["Right"])}
    loc["Left"], w = left_rel(row)
    cam = {s: Gc @ loc[s] for s in loc}
    # keep-out zone in front of the eye: a hand whose clip path swings up at the camera (racking a slide,
    # loading shells) is held at least KEEP_FWD ahead and KEEP_DOWN below the eye
    for s in ("Left",):
        p = cam[s].translation
        f, u = p.dot(FWD), p.dot(UP)
        df = max(0.0, T["keep_fwd"] - f); du = max(0.0, u - (-T["keep_down"]))
        if df > 0 or du > 0:
            M = cam[s].copy(); M.translation = p + FWD * df - UP * du; cam[s] = M
            loc[s] = Gc.inverted() @ M
    return loc, cam, w

# shoulders: pushed forward until both hands are comfortably in reach on the Idle hold
sh_mid = V3(T["sh_mid"])
_, cam0, _ = hand_targets(SAMPLES["Idle"][0])
for _ in range(40):
    P = Pose(); torso(P, sh_mid); M = P.fk()
    ratio = {s: (cam0[s].translation - M[s + "UpperArm"].translation).length / sum(ARM_LEN[s]) for s in ("Right", "Left")}
    if max(ratio.values()) <= T["reach_max"]: break
    sh_mid = sh_mid + FWD * 0.01
log(f"shoulders at (right, up, fwd) {tuple(round(x, 3) for x in (sh_mid.dot(RIGHT), sh_mid.dot(UP), sh_mid.dot(FWD)))}; "
    f"reach R {ratio['Right']:.2f} L {ratio['Left']:.2f} of arm length")

# ------------------------------------------------------------------ 5. key the clips
def left_fingers(row, loc, w, cache):
    """Left fingers for one frame: the Idle hold's shapes while the hand sits there, a fresh grip solve
    when it holds something else (a magazine, the slide), relaxed when it is away from the gun."""
    if w >= 0.85:                                 # at (or swaying round) its hold spot: the hold's fingers
        return HOLD_ANG["Left"]
    key = cache.get("key")
    M = loc["Left"]
    if key is not None and (M.translation - key.translation).length < 0.003 and \
            M.to_quaternion().rotation_difference(key.to_quaternion()).angle < 0.035:
        return cache["ang"]
    bvh = gun_local_bvh(row["gunb"])
    ob = Obstacles(bvh, CAPS_R)
    F = M @ HB2TUN["Left"]
    gap = min(ob.gap(p, r) for p, r in HX["palm_points"]("Left", F))
    if gap >= Obstacles.REACH - 1e-6:             # palm more than 2 cm from anything: relaxed hand
        ang = {d: relaxed(d) for d in HX["DIGITS"]}
    else:
        h = dict(HOLD["Left"]); h["thumb"] = None
        ang = solve_fingers("Left", M, ob, h, quiet=True)
        ang["Thumb"] = HX["close_digit"](ob, "Left", "Thumb", M, [(0.0, 0.0, 0.0)] * 3, (40.0, 60.0, 50.0))
    cache["key"] = M.copy(); cache["ang"] = ang
    return ang

def blend_ang(a, b, w):
    return {d: [tuple(x + (y - x) * w for x, y in zip(a[d][k], b[d][k])) for k in range(3)] for d in a}

def pose_row(row, cache):
    loc, cam, w = hand_targets(row)
    gun.animation_data.action = bpy.data.actions[row["clip"]]; sc.frame_set(row["frame"]); upd()
    P = Pose(); torso(P, sh_mid)
    miss = {}
    for side in ("Right", "Left"):
        Mh = cam[side]
        solve_arm(P, side, Mh.translation.copy(), tuple(V3(T["pole_r" if side == "Right" else "pole_l"])), Mh.to_3x3().normalized())
        miss[side] = (P.fk()[side + "Hand"].translation - Mh.translation).length
    angs = {"Right": HOLD_ANG["Right"], "Left": left_fingers(row, loc, w, cache)}
    for side, ang in angs.items():
        for d, a in ang.items():
            for k in range(3):
                P.q[f"{side}Hand{d}{k + 1}"] = HX["basis_q"](*a[k])
    return P, miss, angs

report = []
for c, n in CLIPS:
    name = f"FP{GUN}_{c}"
    old = bpy.data.actions.get(name)
    if old: bpy.data.actions.remove(old)
    act = bpy.data.actions.new(name); act.use_fake_user = True
    rig.animation_data.action = act
    ROLL_LAST.clear(); TW_LAST.clear()
    worst = {"Right": 0.0, "Left": 0.0}
    cache = {}
    poses = []
    for i, row in enumerate(SAMPLES[c]):
        row["clip"] = c
        P, miss, angs = pose_row(row, cache)
        poses.append((P, angs))
        for s in worst: worst[s] = max(worst[s], miss[s])
    # smooth the left fingers over time (solves can step between frames)
    for i, (P, angs) in enumerate(poses):
        nb = [poses[j][1]["Left"] for j in (i - 1, i, i + 1) if 0 <= j < len(poses)]
        for d in HX["DIGITS"]:
            for k in range(3):
                a = tuple(sum(x[d][k][m] for x in nb) / len(nb) for m in range(3))
                P.q[f"LeftHand{d}{k + 1}"] = HX["basis_q"](*a)
        g["write_pose"](P, i + 1)
    log(f"{name} keyed")
    report.append(f"{name}: {n + 1} keys, worst hand miss R {worst['Right'] * 100:.1f} cm, L {worst['Left'] * 100:.1f} cm")
for r in report: log(r)

# the gun in camera space (static object; its bones keep playing the gun clips)
gun.matrix_world = TO_CAM @ GOBJ
upd()
for o in (grip,) + tuple(o for o in bpy.data.objects if o.parent == grip):
    o.hide_render = True; o.hide_viewport = True

# ------------------------------------------------------------------ previews
def camera():
    c = bpy.data.objects.get("FP_Cam") or bpy.data.objects.new("FP_Cam", bpy.data.cameras.new("FP_Cam"))
    if c.name not in sc.collection.objects: sc.collection.objects.link(c)
    c.matrix_world = Matrix.Identity(4)
    c.rotation_euler = FWD.to_track_quat("-Z", "Y").to_euler()
    c.data.sensor_fit = "VERTICAL"; c.data.angle_y = math.radians(T["fov"])
    c.data.clip_start = 0.03; c.data.clip_end = 50
    return c

def shot(cam_obj, path):
    sc.camera = cam_obj; sc.render.filepath = path; bpy.ops.render.render(write_still=True)

if RENDER:
    os.makedirs(RENDER, exist_ok=True)
    sc.render.engine = "BLENDER_WORKBENCH"
    sh = sc.display.shading
    sh.light = "STUDIO"; sh.color_type = "MATERIAL" if HANDS == "procedural" or GLOVES else "TEXTURE"; sh.show_cavity = True
    albedo = PROJECT + "/Assets/Models/2026-11-08_WastelandSurvivor_Final/WastelandSurvivor_Albedo.png"
    for im in bpy.data.images:
        if not os.path.exists(bpy.path.abspath(im.filepath)) and os.path.exists(albedo) and "texture" in im.name.lower():
            im.filepath = albedo; im.reload()
    sc.world = sc.world or bpy.data.worlds.new("W"); sc.world.color = (0.35, 0.38, 0.36)
    sl = arms.data.materials[0]
    if sl is not None and HANDS == "procedural": sl.diffuse_color = (0.33, 0.30, 0.25, 1)
    for o in GUN_MESHES:
        for m in o.data.materials:
            if m is not None: m.diffuse_color = (0.42, 0.44, 0.47, 1)
    sc.render.resolution_x, sc.render.resolution_y = 1280, 720
    xh = bpy.data.objects.get("FP_Cross")
    if xh is None:
        me = bpy.data.meshes.new("FP_Cross"); bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=8, v_segments=6, radius=0.06); bm.to_mesh(me); bm.free()
        xh = bpy.data.objects.new("FP_Cross", me); sc.collection.objects.link(xh)
    xh.location = FWD * 20
    gun.animation_data.action = bpy.data.actions["Idle"]; rig.animation_data.action = bpy.data.actions[f"FP{GUN}_Idle"]
    sc.frame_set(1); upd()
    shot(camera(), os.path.join(RENDER, f"FP_{GUN}_Idle.png"))
    side = bpy.data.objects.get("FP_Side") or bpy.data.objects.new("FP_Side", bpy.data.cameras.new("FP_Side"))
    if side.name not in sc.collection.objects: sc.collection.objects.link(side)
    side.location = LEFT * 1.6 + FWD * 0.15 + DOWN * 0.15
    side.rotation_euler = (RIGHT).to_track_quat("-Z", "Y").to_euler()
    side.data.type = "ORTHO"; side.data.ortho_scale = 1.2
    shot(side, os.path.join(RENDER, f"FP_{GUN}_Side.png"))
    cu = bpy.data.objects.get("FP_Close") or bpy.data.objects.new("FP_Close", bpy.data.cameras.new("FP_Close"))
    if cu.name not in sc.collection.objects: sc.collection.objects.link(cu)
    cu.data.sensor_fit = "VERTICAL"; cu.data.clip_start = 0.01
    for s in ("Right", "Left"):
        pb = rig.pose.bones[s + "Hand"]
        tgt = rig.matrix_world @ ((pb.matrix @ HB2TUN[s]) @ Vector((0.0, 0.07, 0.0)))
        cu.location = (0, 0, 0)
        cu.rotation_euler = tgt.normalized().to_track_quat("-Z", "Y").to_euler()
        cu.data.angle_y = math.radians(22)
        shot(cu, os.path.join(RENDER, f"FP_{GUN}_Close{s}.png"))
    if "--views" in argv:
        # outside views of each hand on the gun (the other hand hidden), to check contact
        for s in ("Right", "Left"):
            vg = arms.vertex_groups.get("VM_" + s) or arms.vertex_groups.new(name="VM_" + s)
            ids = {vg2.index for vg2 in arms.vertex_groups if vg2.name.startswith(s + "Hand")}
            vg.add([v.index for v in arms.data.vertices if any(g.group in ids and g.weight > 0.5 for g in v.groups)], 1.0, "REPLACE")
        for s in ("Right", "Left"):
            mk = arms.modifiers.get("VM_Mask") or arms.modifiers.new("VM_Mask", "MASK")
            mk.vertex_group = "VM_" + ("Left" if s == "Right" else "Right"); mk.invert_vertex_group = True
            upd()
            pb = rig.pose.bones[s + "Hand"]
            tgt = rig.matrix_world @ ((pb.matrix @ HB2TUN[s]) @ Vector((0.0, 0.07, 0.0)))
            for vname, d in (("Front", FWD * 1 + UP * 0.3), ("Under", DOWN + FWD * 0.3), ("OutR", RIGHT + FWD * 0.4),
                             ("OutL", LEFT + FWD * 0.4), ("Top", UP + BACK * 0.3)):
                d = d.normalized()
                cu.location = tgt + d * 0.32
                cu.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
                cu.data.angle_y = math.radians(40)
                shot(cu, os.path.join(RENDER, f"FP_{GUN}_{s}_{vname}.png"))
        arms.modifiers.remove(arms.modifiers["VM_Mask"])
        for s in ("Right", "Left"): arms.vertex_groups.remove(arms.vertex_groups["VM_" + s])
        upd()
    if "--sheets" in argv:
        sc.camera = camera()
        sc.render.resolution_x, sc.render.resolution_y = 640, 360
        for c, n in CLIPS:
            gun.animation_data.action = bpy.data.actions[c]; rig.animation_data.action = bpy.data.actions[f"FP{GUN}_{c}"]
            tiles = []
            for i in range(8):
                f = 1 + round(i * n / 7)
                sc.frame_set(f); upd()
                sc.render.filepath = os.path.join(RENDER, f"_t_{GUN}.png"); bpy.ops.render.render(write_still=True)
                im = bpy.data.images.load(sc.render.filepath)
                tiles.append(np.array(im.pixels[:], np.float32).reshape(360, 640, 4)); bpy.data.images.remove(im)
            sheet = np.concatenate([np.concatenate(tiles[:4], axis=1), np.concatenate(tiles[4:], axis=1)][::-1], axis=0)
            im = bpy.data.images.new("s", sheet.shape[1], sheet.shape[0], alpha=True); im.pixels.foreach_set(sheet.ravel())
            im.filepath_raw = os.path.join(RENDER, f"FP_{GUN}_Sheet_{c}.png"); im.file_format = "PNG"; im.save(); bpy.data.images.remove(im)
    log("renders in", RENDER)

# ------------------------------------------------------------------ 6. export
if "--save" in argv:
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(PROJECT, "Blender", f"FP_{GUN}.blend"), copy=True)
    log("saved", f"Blender/FP_{GUN}.blend")

if "--export" in argv:
    # the gun's armature at rest, riding the right hand where the Idle hold puts its Gun bone
    gun.animation_data.action = bpy.data.actions["Idle"]; rig.animation_data.action = bpy.data.actions[f"FP{GUN}_Idle"]
    sc.frame_set(1); upd()
    gun_hold = gun.matrix_world @ gun.pose.bones["Gun"].matrix
    gun.animation_data.action = None
    for pb in gun.pose.bones:
        pb.rotation_mode = "QUATERNION"; pb.rotation_quaternion = Quaternion(); pb.location = (0, 0, 0); pb.scale = (1, 1, 1)
    upd()
    rest_rel = gun.data.bones["Root"].matrix_local.inverted() @ gun.data.bones["Gun"].matrix_local
    gun.matrix_world = gun_hold @ rest_rel.inverted() @ gun.data.bones["Root"].matrix_local.inverted()
    upd()
    mw = gun.matrix_world.copy()
    gun.parent = rig; gun.parent_type = "BONE"; gun.parent_bone = "RightHand"
    upd(); gun.matrix_world = mw; upd()
    log(f"gun Gun bone in hand: off by {((gun.matrix_world @ gun.pose.bones['Gun'].matrix).translation - gun_hold.translation).length * 1000:.2f} mm")
    for a in list(bpy.data.actions):
        if not a.name.startswith(f"FP{GUN}_"): bpy.data.actions.remove(a)
    os.makedirs(OUT_DIR, exist_ok=True)
    outf = os.path.join(OUT_DIR, f"FP_{GUN}.fbx")
    for o in bpy.context.view_layer.objects: o.select_set(False)
    for o in (rig, arms, gun): o.hide_viewport = False; o.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(filepath=outf, use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
                             apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y", bake_space_transform=False,
                             bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, path_mode="STRIP")
    log("exported", outf)
