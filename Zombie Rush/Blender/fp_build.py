"""First-person arms for one gun from the hand-built grips (grips.json, grip_lab.py), 2026-11-14.

Run:  blender -b Blender/Grip_<Gun>.blend -P Blender/fp_build.py -- <Gun> [--render DIR] [--export] [--set key=value ...]

The same grips as the third-person holds (grip_build.py): each hand on the gun as built and checked by eye.
Here the gun sits in front of the eye (the file's origin is the eye, looking -Y), the Shadow Operative's arms
(sleeves, forearms, gauntlet gloves, five-finger hands) reach it from shoulders below and behind the eye, with
the elbow as a hinge placed where the wrist is straightest (grip_lab.solve_arm2).
--export writes Assets/Models/2026-11-10_FPArms/FP_<Gun>.fbx in the layout the game reads
(GunSetupTools.FirstPersonClips): FP_Rig + FP_Arms, the gun's armature riding the right hand, takes
FP<Gun>_Idle / _Fire / _Reload / _Draw.
"""
import json
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector

PROJECT = "C:/Mian Sarmad/Unity/Zombie-Rush/Zombie Rush"
BODY = PROJECT + "/Assets/Models/2026-11-14_ShadowOperative_Final/ShadowOperative.fbx"
OUT_DIR = PROJECT + "/Assets/Models/2026-11-10_FPArms"
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
GUN = argv[0]
RENDER = argv[argv.index("--render") + 1] if "--render" in argv else None


def log(*a):
    print("[fp_build]", *a, flush=True)


# ------------------------------------------------------------------ tuning (camera space: right, up, forward from the eye)
#   at: the Gun bone (grip) ; yaw: muzzle toward the screen centre ; pitch: muzzle up ; roll: top turned right
#   sh_mid: midpoint of the shoulder joints ; turn: chest turned (+ = left shoulder forward)
COMMON = dict(k_upper=1.0, k_fore=1.35, fov=55.0, reach_max=0.96, keep_fwd=0.30, keep_down=0.10)
GUNS = {
    "Pistol": dict(at=(0.09, -0.13, 0.44), yaw=10.0, pitch=1.5, roll=-4.0, sh_mid=(0.0, -0.25, -0.06), turn=8.0),
    # long guns: the support hand sits at the front of the handguard, far for these arms; longer upper arms and a
    # turned chest keep the shoulders behind the eye (out of view) instead of pushing them forward
    "Rifle": dict(at=(0.125, -0.165, 0.28), yaw=2.5, pitch=0.5, roll=2.0, sh_mid=(0.0, -0.24, -0.06), turn=40.0, k_upper=1.15),
    "Shotgun": dict(at=(0.13, -0.19, 0.31), yaw=5.0, pitch=1.0, roll=2.0, sh_mid=(0.0, -0.24, -0.06), turn=40.0, k_upper=1.15),
}
T = dict(COMMON)
T.update(GUNS[GUN])
for i, a in enumerate(argv):
    if a == "--set" and i + 1 < len(argv):
        k, v = argv[i + 1].split("=", 1)
        T[k] = tuple(float(x) for x in v.split(",")) if "," in v else float(v)
        log(f"set {k} = {T[k]}")

RIGHT, UP, FWD = Vector((-1, 0, 0)), Vector((0, 0, 1)), Vector((0, -1, 0))


def V3(t):
    return RIGHT * t[0] + UP * t[1] + FWD * t[2]


GR = json.load(open(os.path.join(PROJECT, "Blender", "grips.json")))[GUN]
META = GR["meta"]
sc = bpy.context.scene
sc.render.fps = 30


def upd():
    bpy.context.view_layer.update()


# ------------------------------------------------------------------ 1. the gun and its clips (Grip_<Gun>.blend)
gun = bpy.data.objects[GUN + "_Rig"]
gun.animation_data_create()
GUN_SCALE = META.get("scale", 1.0)
gun.scale = (GUN_SCALE,) * 3
for p in gun.pose.bones:
    p.rotation_mode = "QUATERNION"
    p.rotation_quaternion = Quaternion()
    p.location = (0, 0, 0)
upd()
GUN_MESHES = [o for o in bpy.data.objects if o.type == "MESH" and o.parent == gun]
for o in [o for o in bpy.data.objects if o.name in ("Player_Rig",) or (o.parent and o.parent.name == "Player_Rig")]:
    o.hide_render = True
    o.hide_viewport = True


def rigid(M):
    l, r, s = M.decompose()
    return Matrix.LocRotScale(l, r, None)


def gun_bone():
    return gun.matrix_world @ gun.pose.bones["Gun"].matrix


CLIPS = [(c, int(round(bpy.data.actions[c].frame_range[1]))) for c in ("Idle", "Fire", "Reload", "Draw")]
gun.animation_data.action = None
upd()
G_REST = rigid(gun_bone())
HL_REST_G = G_REST.inverted() @ rigid(gun.matrix_world @ gun.pose.bones["HandL"].matrix)
gun.animation_data.action = bpy.data.actions["Idle"]
sc.frame_set(1)
upd()
G1 = rigid(gun_bone())
# first person starts from a level, upright gun: the Gun bone's -Y (muzzle) forward, +Z up
_gml = gun.data.bones["Gun"].matrix_local.to_3x3().inverted()
BORE = (G1.to_3x3() @ _gml @ Vector((0, -1, 0))).normalized()
TOP = (G1.to_3x3() @ _gml @ Vector((0, 0, 1))).normalized()
_q1 = BORE.rotation_difference(FWD)
_top = _q1 @ TOP
_top = (_top - FWD * _top.dot(FWD)).normalized()
LEVEL = (Quaternion(FWD, UP.angle(_top) * (1 if UP.cross(_top).dot(FWD) < 0 else -1)) @ _q1).to_matrix().to_4x4()
Rg = (Quaternion(UP, math.radians(T["yaw"])) @ Quaternion(RIGHT, math.radians(T["pitch"]))
      @ Quaternion(FWD, math.radians(T["roll"]))).to_matrix().to_4x4()
TO_CAM = Matrix.Translation(V3(T["at"])) @ Rg @ LEVEL @ Matrix.Translation(-G1.translation)

# ------------------------------------------------------------------ 2. the arms rig (FP_Rig / FP_Arms)
for o in list(bpy.data.objects):
    if o.name.startswith("FP_"):
        bpy.data.objects.remove(o, do_unlink=True)
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=BODY)
sc.render.fps = 30
new = [o for o in bpy.data.objects if o not in before]
rig = next(o for o in new if o.type == "ARMATURE")
rig.name = "FP_Rig"
arms = max((o for o in new if o.type == "MESH"), key=lambda o: len(o.data.vertices))
arms.name = "FP_Arms"
for o in new:
    if o.type == "MESH" and o is not arms:
        bpy.data.objects.remove(o, do_unlink=True)
for a in [x for x in bpy.data.actions if "|" in x.name]:
    bpy.data.actions.remove(a)
rig.animation_data_create()
rig.animation_data.action = None
rig.rotation_euler = (0, 0, 0)
for p in rig.pose.bones:
    p.rotation_mode = "QUATERNION"
    p.rotation_quaternion = Quaternion()
    p.location = (0, 0, 0)
upd()

# keep the arms: upper arms, forearms, hands
keep = [vg.index for vg in arms.vertex_groups if any(k in vg.name for k in ("UpperArm", "LowerArm", "Hand"))]
drop = [v.index for v in arms.data.vertices if sum(g.weight for g in v.groups if g.group in keep) < 0.5]
bm = bmesh.new()
bm.from_mesh(arms.data)
bm.verts.ensure_lookup_table()
bmesh.ops.delete(bm, geom=[bm.verts[i] for i in drop], context="VERTS")
bm.to_mesh(arms.data)
bm.free()
arms.data.update()
log(f"FP_Arms: {len(arms.data.vertices)} vertices")


def ctx(obj):
    return bpy.context.temp_override(object=obj, active_object=obj, selected_objects=[obj], selected_editable_objects=[obj])


# lengthen the forearms (the Operative's are short for a view model), make it the rest pose
for s in ("Right", "Left"):
    rig.data.bones[s + "LowerArm"].inherit_scale = "NONE"
    rig.data.bones[s + "Hand"].inherit_scale = "NONE"
    rig.pose.bones[s + "UpperArm"].scale = (1, T["k_upper"], 1)
    rig.pose.bones[s + "LowerArm"].scale = (1, T["k_fore"], 1)
upd()
if arms.data.shape_keys:
    with ctx(arms):
        bpy.ops.object.shape_key_remove(all=True, apply_mix=True)
mod = next(m for m in arms.modifiers if m.type == "ARMATURE")
with ctx(arms):
    bpy.ops.object.modifier_apply(modifier=mod.name)
for o in bpy.context.view_layer.objects:
    o.select_set(o is rig)
bpy.context.view_layer.objects.active = rig
with ctx(rig):
    bpy.ops.object.mode_set(mode="POSE")
bpy.ops.pose.select_all(action="SELECT")
bpy.ops.pose.armature_apply(selected=False)
bpy.ops.object.mode_set(mode="OBJECT")
for s in ("Right", "Left"):
    rig.data.bones[s + "LowerArm"].inherit_scale = "FULL"
    rig.data.bones[s + "Hand"].inherit_scale = "FULL"
for p in rig.pose.bones:
    p.scale = (1, 1, 1)
m2 = arms.modifiers.new("Armature", "ARMATURE")
m2.object = rig
upd()
B = rig.data.bones
log("forearm", round((B["RightHand"].head_local - B["RightLowerArm"].head_local).length, 3), "m")

# the gauntlet's cuff stays on the forearm while the hand turns inside it (first person sees the wrist up close)
names = {vg.index: vg.name for vg in arms.vertex_groups}
nc = 0
for side in ("Right", "Left"):
    W = B[side + "Hand"].head_local
    fa = (W - B[side + "LowerArm"].head_local).normalized()
    gh, gl_ = arms.vertex_groups[side + "Hand"], arms.vertex_groups[side + "LowerArm"]
    for v in arms.data.vertices:
        p = v.co
        if (p - W).length > 0.16:
            continue
        al = (p - W).dot(fa)
        if al < -0.09 or al > 0.03:
            continue
        gs = {names[g.group]: g.weight for g in v.groups}
        if not gs or not set(gs) <= {side + "Hand", side + "LowerArm", side + "UpperArm"}:
            continue
        x = max(0.0, min(1.0, (al + 0.012) / 0.03))
        w = x * x * (3 - 2 * x)
        for g in list(v.groups):
            arms.vertex_groups[g.group].remove([v.index])
        if w < 0.999:
            gl_.add([v.index], 1 - w, "REPLACE")
        if w > 0.001:
            gh.add([v.index], w, "REPLACE")
        nc += 1
log(f"soft cuffs: {nc} wrist vertices")

# grip_lab on this rig
rig.name, arms.name = "Op_Rig", "Op_Mesh"
ns = {}
exec(open(os.path.join(PROJECT, "Blender", "grip_lab.py"), encoding="utf-8").read(), ns)
rig.name, arms.name = "FP_Rig", "FP_Arms"
pb, solve_arm2, torso, wrist_cost, DIGITS = ns["pb"], ns["solve_arm2"], ns["torso"], ns["wrist_cost"], ns["DIGITS"]

HAND_G = {s: Matrix(GR[s]["hand_in_gun"]) for s in ("Right", "Left")}
FING = {s: {n: Quaternion(q) for n, q in GR[s]["fingers"].items()} for s in ("Right", "Left")}
HOLDING = {}
for d in DIGITS:
    a = (20, 15, 10) if d == "Thumb" else (40, 50, 35)
    for k in (1, 2, 3):
        HOLDING[f"LeftHand{d}{k}"] = Quaternion((1, 0, 0), math.radians(a[k - 1]))

HIPS_REST = rig.data.bones["Hips"].matrix_local.copy()


def place_body(sh_mid):
    """Chest turned, the body moved so the shoulder midpoint sits at sh_mid (camera space)."""
    for p in rig.pose.bones:
        p.rotation_quaternion = Quaternion()
        p.location = (0, 0, 0)
    upd()
    torso(T["turn"], 0.0)
    mid = (rig.pose.bones["RightUpperArm"].head + rig.pose.bones["LeftUpperArm"].head) / 2
    off = sh_mid - mid
    hp = rig.pose.bones["Hips"]
    hp.location = HIPS_REST.to_3x3().inverted() @ off
    upd()


def targets(clip):
    Gw = rigid(gun_bone())
    Gc = rigid(TO_CAM @ gun_bone())
    right = Gc @ HAND_G["Right"]
    if clip in ("Reload", "Draw"):
        H = Gw.inverted() @ (gun.matrix_world @ gun.pose.bones["HandL"].matrix)
        l, r, s = H.decompose()
        H = HL_REST_G if min(abs(x) for x in s) < 1e-3 else Matrix.LocRotScale(l, r, None)
        left_g = H @ HL_REST_G.inverted() @ HAND_G["Left"]
    else:
        left_g = HAND_G["Left"]
    left = Gc @ left_g
    # keep-out zone in front of the eye (a hand swinging up at the camera on a reload)
    p = left.translation
    df = max(0.0, T["keep_fwd"] - p.dot(FWD))
    du = max(0.0, p.dot(UP) + T["keep_down"])
    if df > 0 or du > 0:
        left = left.copy()
        left.translation = p + FWD * df - UP * du
    away = (left_g.translation - HAND_G["Left"].translation).length
    w = max(0.0, min(1.0, (0.05 - away) / 0.035))
    return right, left, w


# shoulders: pushed forward until both hands are within reach on the Idle hold
sh_mid = V3(T["sh_mid"])
gun.animation_data.action = bpy.data.actions["Idle"]
sc.frame_set(1)
upd()
r0, l0, _ = targets("Idle")
a_len = {s: (B[s + "LowerArm"].head_local - B[s + "UpperArm"].head_local).length + (B[s + "Hand"].head_local - B[s + "LowerArm"].head_local).length
         for s in ("Right", "Left")}
for _ in range(40):
    place_body(sh_mid)
    ratio = max((r0.translation - rig.pose.bones["RightUpperArm"].head).length / a_len["Right"],
                (l0.translation - rig.pose.bones["LeftUpperArm"].head).length / a_len["Left"])
    if ratio <= T["reach_max"]:
        break
    sh_mid = sh_mid + FWD * 0.01
log(f"shoulders at (right, up, fwd) {tuple(round(x, 3) for x in (sh_mid.dot(RIGHT), sh_mid.dot(UP), sh_mid.dot(FWD)))}, reach {ratio:.2f}")

# ------------------------------------------------------------------ 3. clips
KEYS = [b.name for b in rig.data.bones]
made = []
for clip, last in CLIPS:
    name = f"FP{GUN}_{clip}"
    old = bpy.data.actions.get(name)
    if old:
        bpy.data.actions.remove(old)
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    gun.animation_data.action = bpy.data.actions[clip]
    worst = [(0, 0, 0), (0, 0, 0)]
    wr = []
    for f in range(1, last + 1):
        rig.animation_data.action = None
        sc.frame_set(f)
        upd()
        right, left, w = targets(clip)
        place_body(sh_mid)
        solve_arm2("Right", right)
        solve_arm2("Left", left, relax=0.8 * (1.0 - w))
        for n, q in FING["Right"].items():
            pb(n).rotation_quaternion = q
        for n, q in FING["Left"].items():
            pb(n).rotation_quaternion = HOLDING[n].slerp(q, w)
        upd()
        wr.append((wrist_cost("Right")[1], wrist_cost("Left")[1]))
        rig.animation_data.action = act
        for n in KEYS:
            pb(n).keyframe_insert("rotation_quaternion", frame=f, group=n)
        rig.pose.bones["Hips"].keyframe_insert("location", frame=f, group="Hips")
        rig.animation_data.action = None
    act.use_frame_range = True
    act.frame_start, act.frame_end = 1, last
    act.use_cyclic = clip == "Idle"
    made.append((act, bpy.data.actions[clip], last))
    mx = lambda i, k: max(abs(x[i][k]) for x in wr)
    log(f"{name}: {last} frames; worst wrist (back, sideways, twist) R {mx(0, 0)}/{mx(0, 1)}/{mx(0, 2)} L {mx(1, 0)}/{mx(1, 1)}/{mx(1, 2)}")

# the gun shown in camera space (its bones keep playing the gun clips)
gun_obj_rest = gun.matrix_world.copy()
gun.matrix_world = TO_CAM @ gun_obj_rest
upd()

# ------------------------------------------------------------------ 4. pictures
if RENDER:
    os.makedirs(RENDER, exist_ok=True)
    sc.render.engine = "BLENDER_WORKBENCH"
    sh = sc.display.shading
    sh.light = "STUDIO"
    sh.color_type = "TEXTURE"
    sh.show_cavity = True
    albedo = PROJECT + "/Assets/Models/2026-11-14_ShadowOperative_Final/ShadowOperative_Albedo.png"
    for im in bpy.data.images:
        if not os.path.exists(bpy.path.abspath(im.filepath)) and os.path.exists(albedo) and "texture" in im.name.lower():
            im.filepath = albedo
            im.reload()
    sc.world = sc.world or bpy.data.worlds.new("W")
    sc.world.color = (0.35, 0.38, 0.36)
    for o in GUN_MESHES:
        for m in o.data.materials:
            if m is not None:
                m.diffuse_color = (0.25, 0.26, 0.28, 1)

    def cam(name, loc, look=None, fov=None, ortho=None, rot_dir=None):
        c = bpy.data.objects.get(name) or bpy.data.objects.new(name, bpy.data.cameras.new(name))
        if c.name not in sc.collection.objects:
            sc.collection.objects.link(c)
        c.location = loc
        d = (Vector(look) - Vector(loc)) if look is not None else rot_dir
        c.rotation_euler = d.normalized().to_track_quat("-Z", "Y").to_euler()
        c.data.clip_start = 0.02
        c.data.sensor_fit = "VERTICAL"
        if ortho:
            c.data.type = "ORTHO"
            c.data.ortho_scale = ortho
        else:
            c.data.angle_y = math.radians(fov or T["fov"])
        return c
    eye = cam("FP_Eye", Vector((0, 0, 0)), rot_dir=FWD)
    side = cam("FP_SideV", -RIGHT * 1.4 + FWD * 0.2 - UP * 0.12, FWD * 0.2 - UP * 0.12, ortho=1.0)
    close = cam("FP_CloseV", V3((0.35, 0.05, 0.05)), V3(T["at"]), fov=35)
    under = cam("FP_UnderV", V3((-0.15, -0.45, 0.25)), V3(T["at"]), fov=40)
    RX, RY = 640, 360
    sc.render.resolution_x, sc.render.resolution_y = RX, RY
    for act, ga, last in made:
        rig.animation_data.action = act
        gun.animation_data.action = ga
        frames = [1 + round(i * (last - 1) / 4) for i in range(5)] if act.name.endswith(("Reload", "Draw")) else [1]
        rows = []
        for c in (eye, close, side, under):
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
    rig.animation_data.action = None
    log("sheets in", RENDER)

# ------------------------------------------------------------------ 5. export
if "--export" in argv:
    gun.animation_data.action = bpy.data.actions["Idle"]
    rig.animation_data.action = bpy.data.actions[f"FP{GUN}_Idle"]
    sc.frame_set(1)
    upd()
    gun_hold = rigid(gun.matrix_world @ gun.pose.bones["Gun"].matrix)
    gun.animation_data.action = None
    for p in gun.pose.bones:
        p.rotation_quaternion = Quaternion()
        p.location = (0, 0, 0)
        p.scale = (1, 1, 1)
    gun.scale = (1, 1, 1)                     # the game's gun model carries its own scale
    upd()
    rest_rel = gun.data.bones["Root"].matrix_local.inverted() @ gun.data.bones["Gun"].matrix_local
    gun.matrix_world = gun_hold @ rest_rel.inverted() @ gun.data.bones["Root"].matrix_local.inverted()
    upd()
    mw = gun.matrix_world.copy()
    gun.parent = rig
    gun.parent_type = "BONE"
    gun.parent_bone = "RightHand"
    upd()
    gun.matrix_world = mw
    upd()
    log(f"gun Gun bone in hand: off by {((gun.matrix_world @ gun.pose.bones['Gun'].matrix).translation - gun_hold.translation).length * 1000:.2f} mm")
    for a in list(bpy.data.actions):
        if not a.name.startswith(f"FP{GUN}_"):
            bpy.data.actions.remove(a)
    os.makedirs(OUT_DIR, exist_ok=True)
    outf = os.path.join(OUT_DIR, f"FP_{GUN}.fbx")
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in (rig, arms, gun):
        o.hide_viewport = False
        o.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(filepath=outf, use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
                             apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y", bake_space_transform=False,
                             bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, path_mode="STRIP")
    log("exported", outf)
