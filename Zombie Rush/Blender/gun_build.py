"""Turn a Meshy gun into an animated game gun, with its animations made here in Blender.

Run:  blender -b -P Blender/gun_build.py -- <Pistol|Shotgun|Rifle> [sheet_dir]

What it does:
  1. imports the Meshy gun, cuts it to a game budget, turns it muzzle -Y / top +Z (the game's
     forward), scales it to real size and puts the origin on the right hand's grip;
  2. cuts the moving parts out of the one Meshy mesh (pistol slide and magazine, rifle magazine,
     shotgun barrels) and adds the small extra parts it needs (magazine body, shotgun shells);
  3. builds a small skeleton: Root, Gun (the whole gun's motion), the parts, HandR and HandL (where
     the hands go: the game's arm IK follows them) and Muzzle (the flash);
  4. writes the clips from Blender/clips_gun_<name>.py (Idle, Fire, Reload, Draw) as actions;
  5. exports Assets/Models/2026-11-09_Guns/<Name>/<Name>.fbx and copies the textures;
  6. saves Blender/Gun_<Name>.blend with the player body holding the gun (arms posed by IK onto
     HandR/HandL every frame), so every clip can be watched in Blender with the hands on the gun.
  With sheet_dir it also renders 8 frames per clip from the side and from the shooter's eye.
"""
import math
import os
import shutil
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
ROOT = os.path.dirname(os.path.dirname(PROJECT))           # C:/Mian Sarmad/Unity
args = sys.argv[sys.argv.index("--") + 1:]
NAME = args[0]
SHEETS = args[1] if len(args) > 1 else None
OUT = os.path.join(PROJECT, "Assets", "Models", "2026-11-09_Guns", NAME)
SURVIVOR = os.path.join(PROJECT, "Assets", "Models", "2026-11-08_WastelandSurvivor_Final", "WastelandSurvivor.fbx")
FPS = 30


def log(*a):
    print("ZR_GUN", *a)


# ------------------------------------------------------------------ per gun: source, size, landmarks (Meshy units)
# Meshy guns lie along X with the muzzle at -X, top +Z and the shooter's right at +Y.
SPECS = {
    "Pistol": dict(
        src="Meshy_AI_Rustborn_Sidearm_1108164531_texture_fbx", length=0.27, tris=9000,   # 1.3x real size: suits the survivor's big stylised hands
        grip=(0.64, 0.0, -0.08), muzzle=(-0.95, 0.0, 0.29), left=(0.66, -0.14, -0.22),
        parts={
            "Slide": lambda x, y, z: z > 0.20 and x < 0.92,
            "Magazine": lambda x, y, z: z < -0.30 and x > 0.38,
        },
        mag_body=dict(center=(0.66, 0.0, -0.08), size=(0.20, 0.13, 0.50), tilt=17.0),
    ),
    "Shotgun": dict(
        src="Meshy_AI_Rustbound_Double_Barr_1108165238_texture_fbx", length=1.05, tris=12000,
        grip=(0.42, 0.0, -0.03), muzzle=(-0.95, 0.0, 0.11), left=(-0.12, 0.0, 0.05),
        hinge=(0.12, 0.0, 0.03),
        parts={
            "Barrels": lambda x, y, z: (x < 0.0 and z > -0.02) or (0.0 <= x < 0.12 and z > 0.03),
        },
        chambers=((0.12, 0.05, 0.11), (0.12, -0.05, 0.11)),
    ),
    "Rifle": dict(
        src="Meshy_AI_Scrap_Assault_Rifle_1108171140_texture_fbx", length=0.88, tris=12000,
        grip=(0.32, 0.0, -0.10), muzzle=(-0.95, 0.0, 0.065), left=(-0.31, 0.0, 0.03),
        parts={
            "Magazine": lambda x, y, z: -0.11 < x < 0.15 and z < 0.0,
        },
        charge=(0.05, 0.08, 0.09),
    ),
}
spec = SPECS[NAME]
SRC = None
for dp, dn, fn in os.walk(os.path.join(ROOT, spec["src"])):
    for f in fn:
        if f.endswith(".fbx"):
            SRC = os.path.join(dp, f)
assert SRC, "no fbx for " + NAME

# ------------------------------------------------------------------ import, orient, scale
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.render.fps = FPS
bpy.ops.import_scene.fbx(filepath=SRC)
for o in list(bpy.data.objects):
    if o.type != "MESH":
        bpy.data.objects.remove(o)
gun = next(o for o in bpy.data.objects if o.type == "MESH")
bpy.context.view_layer.objects.active = gun
gun.select_set(True)
gun.parent = None
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
gun.name = NAME
co = np.empty(len(gun.data.vertices) * 3, np.float32)
gun.data.vertices.foreach_get("co", co)
co = co.reshape(-1, 3)
S = spec["length"] / float(co[:, 0].max() - co[:, 0].min())
GRIP = Vector(spec["grip"])


def W(p):
    """Meshy coordinates -> game coordinates: muzzle -Y, top +Z, origin on the right-hand grip, metres."""
    v = Vector(p) - GRIP
    return Vector((-v.y, v.x, v.z)) * S


T = Matrix.Scale(S, 4) @ Matrix.Rotation(math.radians(90), 4, "Z") @ Matrix.Translation(-GRIP)
# (rotation +90 about Z sends -X to -Y and +Y (shooter's right) to -X)
tris = sum(len(p.vertices) - 2 for p in gun.data.polygons)
if tris > spec["tris"]:
    d = gun.modifiers.new("cut", "DECIMATE"); d.ratio = spec["tris"] / tris
    bpy.ops.object.modifier_apply(modifier=d.name)
log(f"{tris} -> {sum(len(p.vertices) - 2 for p in gun.data.polygons)} triangles, scale {S:.4f}")


# ------------------------------------------------------------------ cut the parts out (decided in Meshy units)
def separate(obj, name, pred):
    bm = bmesh.new(); bm.from_mesh(obj.data)
    sel = 0
    for f in bm.faces:
        c = f.calc_center_median()
        f.select = bool(pred(c.x, c.y, c.z)); sel += f.select
    bm.to_mesh(obj.data); bm.free()
    if sel == 0:
        log(f"part {name}: nothing in its box"); return None
    bpy.ops.object.select_all(action="DESELECT"); obj.select_set(True); bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.separate(type="SELECTED"); bpy.ops.object.mode_set(mode="OBJECT")
    part = next(o for o in bpy.context.selected_objects if o != obj)
    part.name = name; part.data.name = name
    log(f"part {name}: {sel} faces")
    return part


parts = {n: separate(gun, n, p) for n, p in spec["parts"].items()}
parts = {k: v for k, v in parts.items() if v is not None}
for o in [gun] + list(parts.values()):
    o.data.transform(T)


def dark_mat(name, rgb, metal=0.6, rough=0.5):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*rgb, 1); b.inputs["Metallic"].default_value = metal; b.inputs["Roughness"].default_value = rough
    return m


def box_obj(name, center, size, mat, rot=None):
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 0))
    o = bpy.context.active_object; o.name = name
    o.data.transform(Matrix.Diagonal((*size, 1)))
    if rot is not None:
        o.data.transform(rot.to_4x4())
    o.data.transform(Matrix.Translation(center))
    o.data.materials.append(mat)
    return o


def shell_obj(name, at, axis, length, radius):
    """A shotgun shell: red hull with a brass head; base at `at`, pointing along `axis`."""
    red, brass = dark_mat("Gun_ShellHull", (0.45, 0.05, 0.04), 0.0, 0.6), dark_mat("Gun_ShellBrass", (0.75, 0.55, 0.2), 0.9, 0.3)
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=radius, depth=length * 0.75)
    hull = bpy.context.active_object; hull.data.transform(Matrix.Translation((0, 0, length * 0.25 + length * 0.375))); hull.data.materials.append(red)
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=radius * 1.08, depth=length * 0.25)
    head = bpy.context.active_object; head.data.transform(Matrix.Translation((0, 0, length * 0.125))); head.data.materials.append(brass)
    bpy.ops.object.select_all(action="DESELECT"); hull.select_set(True); head.select_set(True); bpy.context.view_layer.objects.active = hull
    bpy.ops.object.join()
    o = bpy.context.active_object; o.name = name
    rot = Vector((0, 0, 1)).rotation_difference(Vector(axis).normalized()).to_matrix().to_4x4()
    o.data.transform(Matrix.Translation(at) @ rot)
    return o


extras = {}
if "mag_body" in spec:   # pistol: the magazine's hidden body inside the grip, so a dropped magazine looks whole
    mb = spec["mag_body"]
    rot = Matrix.Rotation(math.radians(-mb["tilt"]), 3, "X")   # grip leans back
    sz = Vector(mb["size"]) * S
    extras["MagBody"] = box_obj("MagBody", W(mb["center"]), (sz.y, sz.x, sz.z), dark_mat("Gun_DarkMetal", (0.05, 0.05, 0.055)), rot)
if "chambers" in spec:   # shotgun: two fired shells in the chambers and two fresh ones for the hand
    back = Vector((0, 1, 0))   # out of the breech, towards the shooter
    for i, ch in enumerate(spec["chambers"]):
        base = W(ch) + back * 0.005
        extras[f"ShellOut{i + 1}"] = shell_obj(f"ShellOut{i + 1}", base, -back, 0.068, 0.0105)
        extras[f"ShellIn{i + 1}"] = shell_obj(f"ShellIn{i + 1}", base, -back, 0.068, 0.0105)

# ------------------------------------------------------------------ landmarks in game space
L = {"HandR": Vector((0, 0, 0)), "Muzzle": W(spec["muzzle"]), "HandL": W(spec["left"])}
if "hinge" in spec:
    L["Hinge"] = W(spec["hinge"])
if "charge" in spec:
    L["Charge"] = W(spec["charge"])
if "chambers" in spec:
    L["Chamber1"], L["Chamber2"] = W(spec["chambers"][0]), W(spec["chambers"][1])
if "Magazine" in parts:
    v = np.array([p.co for p in parts["Magazine"].data.vertices])
    L["MagTop"] = Vector(((v[:, 0].min() + v[:, 0].max()) / 2, (v[:, 1].min() + v[:, 1].max()) / 2, v[:, 2].max()))
    L["MagBottom"] = Vector(((v[:, 0].min() + v[:, 0].max()) / 2, (v[:, 1].min() + v[:, 1].max()) / 2, v[:, 2].min()))
gv = np.array([p.co for p in gun.data.vertices])
L["Back"] = Vector((0, float(gv[:, 1].max()), 0))
log("landmarks", {k: tuple(round(c, 3) for c in v) for k, v in L.items()})

# ------------------------------------------------------------------ skeleton: every bone points +Y with no roll (rest = no rotation)
arm_data = bpy.data.armatures.new(NAME + "_Rig")
arm = bpy.data.objects.new(NAME + "_Rig", arm_data)
bpy.context.scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
BONES = [("Root", None, Vector((0, 0, 0))), ("Gun", "Root", Vector((0, 0, 0))),
         ("HandR", "Gun", L["HandR"]), ("HandL", "Root", L["HandL"]), ("Muzzle", "Gun", L["Muzzle"])]
if NAME == "Pistol":
    BONES += [("Slide", "Gun", Vector((0, 0, L["Muzzle"].z))), ("Mag", "Gun", L["MagTop"])]
if NAME == "Rifle":
    BONES += [("Mag", "Gun", L["MagTop"])]
if NAME == "Shotgun":
    BONES += [("Barrels", "Gun", L["Hinge"])]
    BONES = [(n, "Barrels" if n == "Muzzle" else p, h) for n, p, h in BONES]
    BONES += [("ShellOut1", "Barrels", L["Chamber1"]), ("ShellOut2", "Barrels", L["Chamber2"]),
              ("ShellIn1", "Root", L["Chamber1"]), ("ShellIn2", "Root", L["Chamber2"])]
# parents first, so key_pose can walk the list in order
done, ordered = set(), []
while len(ordered) < len(BONES):
    for n, p, h in BONES:
        if n not in done and (p is None or p in done):
            ordered.append((n, p, h)); done.add(n)
BONES = ordered
for n, p, h in BONES:
    e = arm_data.edit_bones.new(n)
    e.head = h; e.tail = h + Vector((0, 0.03, 0)); e.roll = 0.0
    if p:
        e.parent = arm_data.edit_bones[p]
bpy.ops.object.mode_set(mode="OBJECT")
REST = {n: h.copy() for n, p, h in BONES}
PARENT = {n: p for n, p, h in BONES}
ORDER = [n for n, p, h in BONES]


def parent_to(obj, bone):
    mw = obj.matrix_world.copy()
    obj.parent = arm; obj.parent_type = "BONE"; obj.parent_bone = bone
    bpy.context.view_layer.update()
    obj.matrix_world = mw


parent_to(gun, "Gun")
OWNER = {"Slide": "Slide", "Magazine": "Mag", "Barrels": "Barrels", "MagBody": "Mag",
         "ShellOut1": "ShellOut1", "ShellOut2": "ShellOut2", "ShellIn1": "ShellIn1", "ShellIn2": "ShellIn2"}
for n, o in list(parts.items()) + list(extras.items()):
    parent_to(o, OWNER[n])

# ------------------------------------------------------------------ clips
class GunPose:
    """Bone transforms for one frame, each given in the gun root's space (metres, game axes:
    forward -Y, up +Z, right -X). Unset bones stay at rest relative to their parent."""

    def __init__(self):
        self.M = {}

    def set(self, bone, pos=None, rot=None, scale=1.0):
        rest = REST[bone]
        self.M[bone] = Matrix.LocRotScale(pos if pos is not None else rest, rot or Quaternion(), Vector((scale,) * 3))

    def world(self, bone):
        """Root-space matrix of a bone (following its parents if not set)."""
        if bone in self.M:
            return self.M[bone]
        p = PARENT[bone]
        if p is None:
            return Matrix.Translation(REST[bone])
        return self.world(p) @ Matrix.Translation(REST[bone] - REST[p])

    def at(self, bone, point):
        """A rest-pose point (root space) carried along by a bone as it is now."""
        return self.world(bone) @ (Matrix.Translation(-REST[bone]) @ Vector(point))

    def rel(self, bone, dpos=(0, 0, 0), drot=None, scale=1.0):
        """A bone moved from its rest place on its parent: dpos (right, up, forward, metres) and
        drot in the parent's frame."""
        p = PARENT[bone]
        off = REST[bone] - REST[p]
        d = G(*dpos)
        self.M[bone] = self.world(p) @ Matrix.Translation(off + d) @ (drot or Quaternion()).to_matrix().to_4x4() @ Matrix.Scale(scale, 4)

    def gun_rot(self):
        return self.world("Gun").to_quaternion()


def G(right=0.0, up=0.0, fwd=0.0):
    """Game-space vector from right / up / forward (game forward is -Y, right -X, up +Z)."""
    return Vector((-right, -fwd, up))


def turn(pitch=0.0, yaw=0.0, roll=0.0):
    """Degrees: pitch = muzzle up, yaw = muzzle left, roll = top to the right."""
    return (Quaternion((0, 0, 1), math.radians(yaw)) @ Quaternion((1, 0, 0), math.radians(-pitch))
            @ Quaternion((0, 1, 0), math.radians(-roll)))


def ease(x):
    x = max(0.0, min(1.0, x)); return x * x * (3 - 2 * x)


def seg(t, a, b):
    return max(0.0, min(1.0, (t - a) / (b - a)))


def bell(t, c, w):
    return math.exp(-((t - c) / w) ** 2)


def path(t, pts):
    """Catmull-Rom through (time, Vector) waypoints, so a hand keeps moving instead of stopping."""
    if t <= pts[0][0]:
        return pts[0][1].copy()
    for i in range(1, len(pts)):
        if t > pts[i][0]:
            continue
        u = (t - pts[i - 1][0]) / max(1e-6, pts[i][0] - pts[i - 1][0])
        a, b, c, d = pts[max(i - 2, 0)][1], pts[i - 1][1], pts[i][1], pts[min(i + 1, len(pts) - 1)][1]
        u2, u3 = u * u, u * u * u
        return 0.5 * (2 * b + (-a + c) * u + (2 * a - 5 * b + 4 * c - d) * u2 + (-a + 3 * b - 3 * c + d) * u3)
    return pts[-1][1].copy()


ctx = {"GunPose": GunPose, "G": G, "turn": turn, "ease": ease, "seg": seg, "bell": bell, "path": path, "REST": REST, "L": L,
       "math": math, "Vector": Vector, "Quaternion": Quaternion, "Matrix": Matrix, "NAME": NAME}
exec(open(os.path.join(HERE, f"clips_gun_{NAME.lower()}.py"), encoding="utf-8").read(), ctx)
CLIPS = ctx["CLIPS"]


def key_pose(gp, frame):
    """Key every bone from root-space matrices: basis = (parent_now @ rest offset)^-1 @ wanted."""
    W_ = {}
    for n in ORDER:
        wanted = gp.world(n)
        p = PARENT[n]
        parent_now = W_[p] if p else Matrix()
        parent_rest = Matrix.Translation(REST[p]) if p else Matrix()
        rest_local = parent_rest.inverted() @ Matrix.Translation(REST[n])
        basis = (parent_now @ rest_local).inverted() @ wanted
        W_[n] = wanted
        pb = arm.pose.bones[n]
        pb.rotation_mode = "QUATERNION"
        loc, rot, sc = basis.decompose()
        pb.location = loc; pb.rotation_quaternion = rot; pb.scale = sc
        for path in ("location", "rotation_quaternion", "scale"):
            pb.keyframe_insert(path, frame=frame, group=n)


actions = []
bpy.context.scene.render.fps = FPS          # importing the Meshy file resets the frame rate
bpy.context.scene.render.fps_base = 1.0
arm.animation_data_create()
for cname, frames, fn, loop in CLIPS:
    a = bpy.data.actions.new(cname); a.use_fake_user = True
    arm.animation_data.action = a
    for f in range(frames + 1):
        gp = GunPose(); fn(f / frames, gp)
        key_pose(gp, f + 1)
    a.use_frame_range = True; a.frame_start = 1; a.frame_end = frames + 1; a.use_cyclic = loop
    actions.append((a, frames))
    log(f"clip {cname}: {frames} frames ({frames / FPS:.2f} s){' loop' if loop else ''}")
arm.animation_data.action = None
for pb in arm.pose.bones:
    pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.scale = (1, 1, 1)

# ------------------------------------------------------------------ export + textures
os.makedirs(OUT, exist_ok=True)
objs = [arm, gun] + list(parts.values()) + list(extras.values())
bpy.ops.object.select_all(action="DESELECT")
for o in objs:
    o.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, f"{NAME}.fbx"), use_selection=True, object_types={"ARMATURE", "MESH"},
                         apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                         bake_space_transform=False, add_leaf_bones=False, armature_nodetype="NULL", mesh_smooth_type="FACE",
                         use_mesh_modifiers=False, path_mode="STRIP", bake_anim=True, bake_anim_use_all_actions=True,
                         bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)
src_dir, base = os.path.dirname(SRC), os.path.splitext(os.path.basename(SRC))[0]
for suffix, dst in (("", "Albedo"), ("_normal", "Normal")):
    p = os.path.join(src_dir, base + suffix + ".png")
    if os.path.exists(p):
        shutil.copyfile(p, os.path.join(OUT, f"{NAME}_{dst}.png"))
mi, ro = os.path.join(src_dir, base + "_metallic.png"), os.path.join(src_dir, base + "_roughness.png")
if os.path.exists(mi) and os.path.exists(ro):
    a_, b_ = bpy.data.images.load(mi), bpy.data.images.load(ro)
    w, h = a_.size
    A = np.empty(w * h * 4, np.float32); a_.pixels.foreach_get(A); A = A.reshape(h, w, 4)
    B = np.empty(w * h * 4, np.float32); b_.pixels.foreach_get(B); B = B.reshape(h, w, 4)
    O = np.zeros((h, w, 4), np.float32); O[..., 0] = A[..., 0]; O[..., 3] = 1 - B[..., 0]
    ms = bpy.data.images.new("ms", w, h, alpha=True); ms.pixels.foreach_set(O.ravel())
    ms.filepath_raw = os.path.join(OUT, f"{NAME}_MetalSmooth.png"); ms.file_format = "PNG"; ms.save()
log("exported", os.path.join(OUT, f"{NAME}.fbx"))

# ------------------------------------------------------------------ preview: the player body holding the gun
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=SURVIVOR)
new = [o for o in bpy.data.objects if o not in before]
body = next(o for o in new if o.type == "ARMATURE")
body.name = "Player_Rig"
for a in [x for x in bpy.data.actions if x.name.startswith(body.name) or "|" in x.name]:
    bpy.data.actions.remove(a)
body.animation_data_create(); body.animation_data.action = None
body_mesh = max((o for o in new if o.type == "MESH" and "Backpack" not in o.name), key=lambda o: len(o.data.vertices))   # not the backpack
g = {"ARM_NAME": "Player_Rig", "MESH_NAME": body_mesh.name, "PFX_OVERRIDE": "",
     "AL_OVERRIDE": {k: k for k in ("Hips", "Chest", "UpperChest", "Neck", "Head", "LeftShoulder", "RightShoulder", "LeftUpperArm",
                                    "RightUpperArm", "LeftLowerArm", "RightLowerArm", "LeftHand", "RightHand", "LeftUpperLeg",
                                    "RightUpperLeg", "LeftLowerLeg", "RightLowerLeg", "LeftFoot", "RightFoot", "LeftToes", "RightToes")},
     "__name__": "zr_anim"}
src_lib = open(os.path.join(HERE, "zr_anim_lib.py"), encoding="utf-8").read().replace('"Chest": "Spine1", "UpperChest": "Spine2"', '"Chest": "Chest", "UpperChest": "UpperChest"')
exec(src_lib, g)
Pose = g["Pose"]; ML = g["ML"]
chest = g["HEAD"]["UpperChest"]
H = max((body.matrix_world @ Vector(c)).z for c in body_mesh.bound_box)
k = H / 1.8
# where the game puts the gun (WeaponHoldConfig): long guns: stock in the shoulder pocket; pistol: grip out in front
if NAME == "Pistol":
    root_at = chest + Vector((-0.06, -0.46, 0.08)) * k
else:
    pocket = chest + Vector((-0.16, -0.06, 0.12)) * k
    root_at = pocket - L["Back"]
arm.location = root_at
RIGHT, UP, FWD = Vector((-1, 0, 0)), Vector((0, 0, 1)), Vector((0, -1, 0))


def place_arms(frame):
    """IK the body's arms onto HandR / HandL for one frame of the active gun clip."""
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    P = Pose()
    for side, bone in (("Right", "HandR"), ("Left", "HandL")):
        m = arm.matrix_world @ arm.pose.bones[bone].matrix
        grip = m.translation
        rot = m.to_quaternion()
        if side == "Right":
            fingers, palm = (FWD + UP * 0.15).normalized(), -RIGHT
        elif NAME == "Pistol":
            fingers, palm = (FWD * 0.8 - UP * 0.3 + RIGHT * 0.4).normalized(), RIGHT
        else:
            fingers, palm = (FWD * 0.5 + RIGHT * 0.85).normalized(), UP
        fingers, palm = rot @ fingers, rot @ palm
        wrist = grip - fingers * 0.07 * k - palm * 0.025 * k
        P.arm_ik(side, wrist, pole=((-0.7 if side == "Right" else 0.7), 0.3, -1))
        P.aim(side + "Hand", fingers)
    for n in g["BONES"]:
        pb = body.pose.bones[n]; pb.rotation_mode = "QUATERNION"; pb.rotation_quaternion = P.q[n]
        pb.keyframe_insert("rotation_quaternion", frame=frame, group=n)


for a, frames in actions:
    arm.animation_data.action = a
    ba = bpy.data.actions.new("Body_" + a.name); ba.use_fake_user = True
    body.animation_data.action = ba
    for f in range(1, frames + 2):
        place_arms(f)
# a scene to watch in Blender: the reload playing, camera at the shooter's eye
arm.animation_data.action = next(a for a, _ in actions if a.name == "Reload")
body.animation_data.action = bpy.data.actions["Body_Reload"]
sc = bpy.context.scene
sc.frame_start, sc.frame_end = 1, next(fr for a, fr in actions if a.name == "Reload") + 1
cam = bpy.data.objects.new("Eye", bpy.data.cameras.new("Eye")); sc.collection.objects.link(cam)
cam.location = g["HEAD"]["Head"] + Vector((-0.02, -0.24, 0.08)) * k   # just in front of the face, at eye height
cam.rotation_euler = (math.radians(78), 0, math.radians(180)); cam.data.lens = 22; cam.data.clip_start = 0.02; sc.camera = cam
sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN")); sc.collection.objects.link(sun); sun.rotation_euler = (0.7, 0.3, 0.5); sun.data.energy = 3
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, f"Gun_{NAME}.blend"))
log("saved", os.path.join(HERE, f"Gun_{NAME}.blend"))

if SHEETS:
    os.makedirs(SHEETS, exist_ok=True)
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.color_type = "OBJECT"
    sc.display.shading.light = "STUDIO"
    sc.world = sc.world or bpy.data.worlds.new("W")
    sc.world.color = (0.42, 0.44, 0.48)
    for o in new:
        o.color = (0.62, 0.58, 0.52, 1)
    for o in [gun] + list(parts.values()) + list(extras.values()):
        o.color = (0.12, 0.12, 0.13, 1)
    for n, o in list(parts.items()) + list(extras.items()):
        o.color = {"Slide": (0.55, 0.12, 0.08, 1), "Magazine": (0.9, 0.55, 0.1, 1), "MagBody": (0.9, 0.55, 0.1, 1),
                   "Barrels": (0.55, 0.12, 0.08, 1)}.get(n, (0.9, 0.55, 0.1, 1))
    sc.render.resolution_x, sc.render.resolution_y = 320, 260
    side = bpy.data.objects.new("SideCam", bpy.data.cameras.new("SideCam")); sc.collection.objects.link(side)
    side.data.type = "ORTHO"; side.data.ortho_scale = 0.75 if NAME == "Pistol" else 1.25
    side.location = Vector((-3.0, root_at.y + (0.10 if NAME == "Pistol" else -0.2), root_at.z - 0.12)); side.rotation_euler = (math.radians(90), 0, math.radians(-90))
    close = bpy.data.objects.new("GunCam", bpy.data.cameras.new("GunCam")); sc.collection.objects.link(close)
    close.data.type = "ORTHO"; close.data.ortho_scale = spec["length"] * 1.25
    close.parent = arm; close.location = Vector((-1.0, -spec["length"] * 0.35, 0.0)); close.rotation_euler = (math.radians(90), 0, math.radians(-90))
    for a, frames in actions:
        arm.animation_data.action = a; body.animation_data.action = bpy.data.actions["Body_" + a.name]
        rows = []
        for camo in (side, cam, close):
            row = []
            for i in range(8):
                f = 1 + round(i * frames / 8)
                sc.frame_set(f); sc.camera = camo
                sc.render.filepath = os.path.join(SHEETS, "_t.png"); bpy.ops.render.render(write_still=True)
                im = bpy.data.images.load(sc.render.filepath)
                row.append(np.array(im.pixels[:], np.float32).reshape(260, 320, 4)); bpy.data.images.remove(im)
            rows.append(np.concatenate(row, axis=1))
        sheet = np.concatenate(rows[::-1], axis=0)
        im = bpy.data.images.new("s", sheet.shape[1], sheet.shape[0], alpha=True); im.pixels.foreach_set(sheet.ravel())
        im.filepath_raw = os.path.join(SHEETS, f"{NAME}_{a.name}.png"); im.file_format = "PNG"; im.save(); bpy.data.images.remove(im)
    log("sheets in", SHEETS)
