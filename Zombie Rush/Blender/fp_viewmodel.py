"""First-person arms ("viewmodel") for one gun, posed against the camera.

Run (from the project folder; the grip blend is only read):
  blender -b Blender/Grip_<Gun>.blend -P Blender/fp_viewmodel.py -- <Pistol|Rifle|Shotgun> [--render DIR] [--sheets] [--export] [--save] [--set key=value ...]

Why: in first person the game used to show the full body's arms, posed for third person and seen from
inside the head. This body's arms are short (0.43 m shoulder to wrist), so every gun sat squashed against
the face and the arms reached in from odd angles. Shooters draw a separate arms rig fixed to the camera
instead; this script builds one:
  1. reads the gun clips and the third-person grip clips (Grip_<Gun>.blend): every frame's hand turn and
     position relative to the gun, and the finger shapes (contact-solved against the gun mesh);
  2. imports the survivor, keeps only the arms (upper arms, forearms, hands) as FP_Arms, and lengthens
     the arms to normal human proportions (the mesh is re-baked to the new rest pose);
  3. places the gun in a fixed spot in front of the camera (tuning: GUNS[gun]["at"], yaw/pitch/roll),
     the shoulders below and either side of the camera, and solves the arms onto the gun every frame,
     with the twist shared between upper arm, forearm and wrist so no joint wrings;
  4. keys FP<Gun>_Idle/Fire/Reload/Draw on FP_Rig (frames match the gun clips) and exports
     Assets/Models/2026-11-10_FPArms/FP_<Gun>.fbx: FP_Rig (camera at its origin, looking -Y, up +Z),
     the FP_Arms mesh, and the gun's armature riding the right hand (the game pins the real gun's Gun
     bone to it, as for the body clips).
In the game the instance sits under the camera at identity, so this file's origin is the eye.
"""
import bpy, math, os, sys
import numpy as np
from mathutils import Vector, Quaternion, Matrix

PROJECT = "C:/Mian Sarmad/Unity/Zombie-Rush/Zombie Rush"
SURV = PROJECT + "/Assets/Models/2026-11-08_WastelandSurvivor_Final/WastelandSurvivor.fbx"
OUT_DIR = PROJECT + "/Assets/Models/2026-11-10_FPArms"
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
GUN = argv[0] if argv else "Rifle"
def opt(name):
    return argv[argv.index(name) + 1] if name in argv and argv.index(name) + 1 < len(argv) else None
RENDER = opt("--render")

def log(*a): print("[fpvm]", *a, flush=True)

# ------------------------------------------------------------------ tuning
# Positions are in the camera's space in metres: (right, up, forward). Angles in degrees.
#   at       : where the gun's Gun bone sits (Idle frame 1)
#   yaw      : muzzle turned left (toward the screen centre) ; pitch: muzzle up ; roll: top turned right
#   sh_mid   : midpoint between the shoulder joints ; torso_yaw: + turns the chest left (right shoulder forward)
#   pole_r/l : which way each elbow points (right, up, forward)
COMMON = dict(k_upper=1.55, k_fore=1.12, fov=55.0, forearm_twist=0.6, reach_max=0.9,
              tw_up=1.0, tw_fore=1.0, tw_wrist=1.0)
GUNS = {
    "Pistol": dict(at=(0.11, -0.12, 0.44), yaw=14.0, pitch=1.5, roll=-6.0,
                   sh_mid=(0.0, -0.24, -0.02), torso_yaw=0.0,
                   pole_r=(0.6, -1.0, -0.2), pole_l=(-0.6, -1.0, -0.2)),
    "Rifle": dict(at=(0.125, -0.165, 0.31), yaw=2.5, pitch=0.5, roll=2.0,
                  sh_mid=(0.0, -0.22, 0.0), torso_yaw=-25.0,
                  pole_r=(0.7, -1.0, -0.3), pole_l=(-0.4, -1.0, 0.0)),
    "Shotgun": dict(at=(0.15, -0.22, 0.39), yaw=8.0, pitch=1.0, roll=2.0, r_roll=-55.0,
                    sh_mid=(0.0, -0.22, 0.0), torso_yaw=-25.0,
                    pole_r=(0.7, -1.0, -0.3), pole_l=(-0.4, -1.0, 0.0)),
}
T = dict(COMMON); T.update(GUNS[GUN])
for _i, _a in enumerate(argv):
    if _a == "--set" and _i + 1 < len(argv):
        _k, _v = argv[_i + 1].split("=", 1)
        T[_k] = tuple(float(x) for x in _v.split(",")) if "," in _v else float(_v)
        log(f"set {_k} = {T[_k]}")

RIGHT, UP, FWD = Vector((-1, 0, 0)), Vector((0, 0, 1)), Vector((0, -1, 0))   # the body's axes (it faces -Y)
LEFT, DOWN, BACK = -RIGHT, -UP, -FWD
def V3(t):
    """(right, up, forward) -> Blender vector."""
    return RIGHT * t[0] + UP * t[1] + FWD * t[2]

sc = bpy.context.scene
sc.render.fps = 30
def upd(): bpy.context.view_layer.update()

# ------------------------------------------------------------------ 1. read the grip
grip = bpy.data.objects["Player_Rig"]
gun = bpy.data.objects[GUN + "_Rig"]
CLIPS = []
for c in ("Idle", "Fire", "Reload", "Draw"):
    a = bpy.data.actions[GUN + "_" + c]
    CLIPS.append((c, int(round(a.frame_range[1])) - 1))
FINGERS = [n + str(k) for s in ("Left", "Right") for n in (s + "HandIndex", s + "HandThumb") for k in (1, 2, 3)]
GOBJ = gun.matrix_world.copy()
SAMPLES = {}
for c, n in CLIPS:
    gun.animation_data.action = bpy.data.actions[c]
    grip.animation_data.action = bpy.data.actions[GUN + "_" + c]
    rows = []
    for f in range(1, n + 2):
        sc.frame_set(f); upd()
        rows.append(dict(
            hand={s: grip.matrix_world @ grip.pose.bones[s + "Hand"].matrix for s in ("Right", "Left")},
            fing={b: grip.pose.bones[b].matrix_basis.to_quaternion() for b in FINGERS},
            gunb=gun.matrix_world @ gun.pose.bones["Gun"].matrix))
    SAMPLES[c] = rows
G1 = SAMPLES["Idle"][0]["gunb"]
_mz = (gun.matrix_world @ gun.pose.bones["Muzzle"].matrix).translation if "Muzzle" in gun.pose.bones else None
log(f"clips {CLIPS}; Gun bone {tuple(round(x, 3) for x in G1.translation)}"
    + (f"; muzzle dir {tuple(round(x, 2) for x in (_mz - G1.translation).normalized())}" if _mz else ""))
GRIP_ML = {b.name: b.matrix_local.copy() for b in grip.data.bones}

# Shotgun: the third-person grip wraps the right hand over the top of the stock's wrist (the body's short
# arm could not reach round it). With the longer first-person arm the whole hand, fingers included, rolls
# round the wrist (T["r_roll"] degrees, + turns the back of the hand down the right side). The wrist is
# near round, so the fingers keep their contact.
if T.get("r_roll", 0.0):
    sc.frame_set(1); gun.animation_data.action = bpy.data.actions["Idle"]; upd()
    dg = bpy.context.evaluated_depsgraph_get()
    pts = []
    for o in bpy.data.objects:
        if o.type == "MESH" and o.parent == gun:
            e = o.evaluated_get(dg); me = e.to_mesh()
            pts += [o.matrix_world @ v.co for v in me.vertices]; e.to_mesh_clear()
    gp = np.array([[ (p - G1.translation).dot(RIGHT), (p - G1.translation).dot(UP), (p - G1.translation).dot(FWD)] for p in pts])
    sel = gp[(gp[:, 1] > -0.06) & (gp[:, 1] < 0.07) & (gp[:, 2] > -0.11) & (gp[:, 2] < 0.035) & (np.abs(gp[:, 0]) < 0.045)]
    c = sel.mean(axis=0); _, _, vt = np.linalg.svd(sel - c)
    ax = vt[0] if vt[0][2] > 0 else -vt[0]                     # along the wrist, pointing forward
    W_C = G1.translation + V3(c); W_AX = V3(ax).normalized()
    in_gun = G1.inverted()
    C_G, AX_G = in_gun @ W_C, (in_gun.to_3x3() @ W_AX).normalized()
    for rows in SAMPLES.values():
        for row in rows:
            Gm = row["gunb"]
            cen, axw = Gm @ C_G, (Gm.to_3x3() @ AX_G).normalized()
            Rm = Matrix.Translation(cen) @ Quaternion(axw, math.radians(T["r_roll"])).to_matrix().to_4x4() @ Matrix.Translation(-cen)
            row["hand"]["Right"] = Rm @ row["hand"]["Right"]
    log(f"right hand rolled {T['r_roll']} deg round the stock wrist (centre {tuple(round(x, 3) for x in c)}, axis {tuple(round(x, 2) for x in ax)}, {len(sel)} points)")

# the bore: the gun armature's -Y (muzzle) and +Z (top) as the Idle hold carries them. The third-person
# grips tilt some guns (the pistol 13 deg up); first person starts from a level, upright gun.
_gml = gun.data.bones["Gun"].matrix_local.to_3x3().inverted()
BORE = (G1.to_3x3() @ _gml @ Vector((0, -1, 0))).normalized()
TOP = (G1.to_3x3() @ _gml @ Vector((0, 0, 1))).normalized()
_q1 = BORE.rotation_difference(FWD)
_top = _q1 @ TOP; _top = (_top - FWD * _top.dot(FWD)).normalized()
LEVEL = (Quaternion(FWD, UP.angle(_top) * (1 if UP.cross(_top).dot(FWD) < 0 else -1)) @ _q1).to_matrix().to_4x4()
log(f"bore {tuple(round(x, 2) for x in BORE)} top {tuple(round(x, 2) for x in TOP)}: levelled")
# grip world -> camera space: the gun's Gun bone goes to T["at"], levelled, then turned by yaw/pitch/roll
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
_diff = max(max(abs(a - b) for ra, rb in zip(rig.data.bones[n].matrix_local, GRIP_ML[n]) for a, b in zip(ra, rb))
            for n in GRIP_ML if n in rig.data.bones)
log(f"rest pose vs the grip's body: largest matrix difference {_diff:.4f}")

# keep only the arms: upper arms, forearms, hands, fingers
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
m2 = arms.modifiers.new("Armature", "ARMATURE"); m2.object = rig
upd()
for s in ("Right", "Left"):
    u = (rig.data.bones[s + "LowerArm"].head_local - rig.data.bones[s + "UpperArm"].head_local).length
    f = (rig.data.bones[s + "Hand"].head_local - rig.data.bones[s + "LowerArm"].head_local).length
    log(f"{s} arm: upper {L0[s][0]:.3f} -> {u:.3f} m, forearm {L0[s][1]:.3f} -> {f:.3f} m")

g = {"ARM_NAME": "FP_Rig", "MESH_NAME": "FP_Arms", "PFX_OVERRIDE": "",
     "AL_OVERRIDE": {k: k for k in ("Hips", "Spine", "Chest", "UpperChest", "Neck", "Head", "LeftShoulder", "RightShoulder",
                                    "LeftUpperArm", "RightUpperArm", "LeftLowerArm", "RightLowerArm", "LeftHand", "RightHand",
                                    "LeftUpperLeg", "RightUpperLeg", "LeftLowerLeg", "RightLowerLeg", "LeftFoot", "RightFoot",
                                    "LeftToes", "RightToes")},
     "__name__": "zr_anim"}
exec(open(os.path.join(PROJECT, "Blender", "zr_anim_lib.py"), encoding="utf-8").read(), g)
Pose, ML, BONES, PAR = g["Pose"], g["ML"], g["BONES"], g["PAR"]
ARM_LEN = {s: g["LEN"]["R_arm" if s == "Right" else "L_arm"] for s in ("Right", "Left")}

# ------------------------------------------------------------------ 3. pose helpers
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
    """Arm IK with the twist shared out like a real arm (same method as grip_rifle.py): the upper arm
    rolls in the shoulder, the forearm takes a share of the hand's turn, the wrist the rest."""
    up, lo, hn = side + "UpperArm", side + "LowerArm", side + "Hand"
    P.arm_ik(side, wrist, pole=pole)
    M = P.fk(); sh = M[up].translation.copy(); elbow = M[lo].translation.copy()
    rel = ML[lo].to_3x3().inverted() @ ML[hn].to_3x3()
    ideal = R_hand @ rel.inverted()
    rolls = [ROLL_LAST[side] + d for d in range(-20, 21, 4)] if side in ROLL_LAST else range(-150, 151, 10)
    best = None
    for ru in rolls:
        P.aim(up, elbow - sh, roll=ru)
        P.aim(lo, wrist - elbow)
        F = P.fk()[lo].to_3x3(); y = F.col[1].normalized()
        xc = F.col[0] - y * F.col[0].dot(y); xi = ideal.col[0] - y * ideal.col[0].dot(y)
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

def hand_target(row, side):
    M = TO_CAM @ row["hand"][side]
    return M.to_3x3().normalized(), M.translation.copy()

# shoulders: pushed forward until both hands are comfortably in reach on the Idle hold
sh_mid = V3(T["sh_mid"])
row0 = SAMPLES["Idle"][0]
for _ in range(40):
    P = Pose(); torso(P, sh_mid); M = P.fk()
    ratio = {s: (hand_target(row0, s)[1] - M[s + "UpperArm"].translation).length / sum(ARM_LEN[s]) for s in ("Right", "Left")}
    if max(ratio.values()) <= T["reach_max"]: break
    sh_mid = sh_mid + FWD * 0.01
log(f"shoulders at (right, up, fwd) {tuple(round(x, 3) for x in (sh_mid.dot(RIGHT), sh_mid.dot(UP), sh_mid.dot(FWD)))}; "
    f"reach R {ratio['Right']:.2f} L {ratio['Left']:.2f} of arm length")

# ------------------------------------------------------------------ 4. key the clips
def pose_row(row):
    P = Pose(); torso(P, sh_mid)
    miss = {}
    for side in ("Right", "Left"):
        R, w = hand_target(row, side)
        solve_arm(P, side, w, tuple(V3(T["pole_r" if side == "Right" else "pole_l"])), R)
        miss[side] = (P.fk()[side + "Hand"].translation - w).length
    for b, q in row["fing"].items(): P.q[b] = q.copy()
    return P, miss

report = []
for c, n in CLIPS:
    name = f"FP{GUN}_{c}"
    old = bpy.data.actions.get(name)
    if old: bpy.data.actions.remove(old)
    act = bpy.data.actions.new(name); act.use_fake_user = True
    rig.animation_data.action = act
    ROLL_LAST.clear(); TW_LAST.clear()
    worst = {"Right": 0.0, "Left": 0.0}
    for i, row in enumerate(SAMPLES[c]):
        P, miss = pose_row(row)
        g["write_pose"](P, i + 1)
        for s in worst: worst[s] = max(worst[s], miss[s])
    report.append(f"{name}: {n + 1} keys, worst hand miss R {worst['Right'] * 100:.1f} cm, L {worst['Left'] * 100:.1f} cm")
for r in report: log(r)

# the gun in camera space (static object; its bones keep playing the gun clips)
gun.matrix_world = TO_CAM @ GOBJ
upd()
for o in (grip,) + tuple(o for o in bpy.data.objects if o.parent == grip):
    o.hide_render = True; o.hide_viewport = True

# ------------------------------------------------------------------ previews from the game camera
def camera():
    c = bpy.data.objects.get("FP_Cam") or bpy.data.objects.new("FP_Cam", bpy.data.cameras.new("FP_Cam"))
    if c.name not in sc.collection.objects: sc.collection.objects.link(c)
    c.matrix_world = Matrix.Identity(4)
    c.rotation_euler = FWD.to_track_quat("-Z", "Y").to_euler()
    c.data.sensor_fit = "VERTICAL"; c.data.angle_y = math.radians(T["fov"])
    c.data.clip_start = 0.03; c.data.clip_end = 50
    return c

if RENDER:
    os.makedirs(RENDER, exist_ok=True)
    sc.render.engine = "BLENDER_WORKBENCH"
    sh = sc.display.shading
    sh.light = "STUDIO"; sh.color_type = "TEXTURE"; sh.show_cavity = True
    sc.world = sc.world or bpy.data.worlds.new("W"); sc.world.color = (0.35, 0.38, 0.36)
    albedo = PROJECT + "/Assets/Models/2026-11-08_WastelandSurvivor_Final/WastelandSurvivor_Albedo.png"
    for im in bpy.data.images:
        if not os.path.exists(bpy.path.abspath(im.filepath)) and os.path.exists(albedo) and "texture" in im.name.lower():
            im.filepath = albedo; im.reload()
    sc.render.resolution_x, sc.render.resolution_y = 1280, 720
    # a crosshair far ahead
    xh = bpy.data.objects.get("FP_Cross")
    if xh is None:
        me = bpy.data.meshes.new("FP_Cross"); bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=8, v_segments=6, radius=0.06); bm.to_mesh(me); bm.free()
        xh = bpy.data.objects.new("FP_Cross", me); sc.collection.objects.link(xh)
    xh.location = FWD * 20; xh.color = (1, 0.1, 0.1, 1)
    sc.camera = camera()
    gun.animation_data.action = bpy.data.actions["Idle"]; rig.animation_data.action = bpy.data.actions[f"FP{GUN}_Idle"]
    sc.frame_set(1); upd()
    sc.render.filepath = os.path.join(RENDER, f"FP_{GUN}_Idle.png")
    bpy.ops.render.render(write_still=True)
    # a side view to check the arms behind the camera
    side = bpy.data.objects.get("FP_Side") or bpy.data.objects.new("FP_Side", bpy.data.cameras.new("FP_Side"))
    if side.name not in sc.collection.objects: sc.collection.objects.link(side)
    side.location = LEFT * 1.6 + FWD * 0.15 + DOWN * 0.15
    side.rotation_euler = (RIGHT).to_track_quat("-Z", "Y").to_euler()
    side.data.type = "ORTHO"; side.data.ortho_scale = 1.2
    sc.camera = side
    sc.render.filepath = os.path.join(RENDER, f"FP_{GUN}_Side.png")
    bpy.ops.render.render(write_still=True)
    # close-ups of each hand from the eye (narrow view aimed at the wrist)
    for s in ("Right", "Left"):
        cu = bpy.data.objects.get("FP_Close") or bpy.data.objects.new("FP_Close", bpy.data.cameras.new("FP_Close"))
        if cu.name not in sc.collection.objects: sc.collection.objects.link(cu)
        pb = rig.pose.bones[s + "Hand"]
        tgt = rig.matrix_world @ ((pb.head + pb.tail) / 2)
        cu.location = (0, 0, 0)
        cu.rotation_euler = tgt.normalized().to_track_quat("-Z", "Y").to_euler()
        cu.data.sensor_fit = "VERTICAL"; cu.data.angle_y = math.radians(18); cu.data.clip_start = 0.03
        sc.camera = cu
        sc.render.filepath = os.path.join(RENDER, f"FP_{GUN}_Close{s}.png")
        bpy.ops.render.render(write_still=True)
    sc.camera = camera()
    if "--sheets" in argv:
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

# ------------------------------------------------------------------ 5. export
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
    # only this rig's takes go into the file
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
