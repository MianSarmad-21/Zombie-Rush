"""NOTE: Blender/survivor_bag.py runs after this and writes the final WastelandSurvivor.fbx (backpack cut
out as its own rigid mesh). If this script is ever rerun, rerun survivor_bag.py after it, after deleting
WastelandSurvivor_WithBag.fbx.bak so the bag script starts from the new fingered model.

Give the player body (WastelandSurvivor) finger joints.

Run:  blender -b -P Blender/survivor_fingers.py -- [preview_dir]   (or open it in Blender and run it)
The Meshy model's hands are mittens: a separate thumb and the four fingers as one block. This adds,
on each hand, a 3-joint thumb (<Side>HandThumb1-3) and a 3-joint chain for the finger block
(<Side>HandIndex1-3, mapped to the index finger in Unity), places them on the mesh, moves the
weights of those parts onto them, and writes the model back with all its clips:
  Assets/Models/2026-11-08_WastelandSurvivor_Final/WastelandSurvivor.fbx  (the old one is kept as
  WastelandSurvivor_NoFingers.fbx.bak next to it), and Blender/Survivor_Fingers.blend to look at.

How the hand is read (rest pose, arms hanging in an A-pose, thumbs forward):
  - hand vertices = those mostly weighted to the Hand bone;
  - along = from the wrist to the furthest fingertip; thumb side = the body's forward (-Y) across that;
  - thumb = vertices well out on the thumb side in the first 70 % of the hand;
  - finger block = the rest past the knuckle line (45 % of the hand length).
"""
import math
import os
import shutil
import sys

import bpy
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
FOLDER = os.path.join(PROJECT, "Assets", "Models", "2026-11-08_WastelandSurvivor_Final")
FBX = os.path.join(FOLDER, "WastelandSurvivor.fbx")
BACKUP = os.path.join(FOLDER, "WastelandSurvivor_NoFingers.fbx.bak")
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
PREVIEW = args[0] if args else None
KNUCKLE = 0.45          # share of the hand length where the fingers start
JOINTS = (0.45, 0.68, 0.85)
THUMB_OUT = 0.022       # metres out on the thumb side that counts as thumb


def log(*a):
    print("ZR_FING", *a)


if not os.path.exists(BACKUP):
    shutil.copyfile(FBX, BACKUP)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=BACKUP if os.path.exists(BACKUP) else FBX)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
mesh = next(o for o in bpy.data.objects if o.type == "MESH")
actions = list(bpy.data.actions)
log(f"{len(actions)} clips, {len(arm.data.bones)} bones, {len(mesh.data.vertices)} vertices")
arm.animation_data.action = None
for pb in arm.pose.bones:
    pb.rotation_quaternion = (1, 0, 0, 0); pb.rotation_euler = (0, 0, 0); pb.location = (0, 0, 0)
bpy.context.view_layer.update()
MW = arm.matrix_world
MWi = MW.inverted()
V = np.array([mesh.matrix_world @ v.co for v in mesh.data.vertices])


def weights_of(group):
    gi = mesh.vertex_groups[group].index
    w = np.zeros(len(mesh.data.vertices), np.float32)
    for v in mesh.data.vertices:
        for g in v.groups:
            if g.group == gi:
                w[v.index] = g.weight
    return w


plan = {}
for side in ("Left",):
    wh = weights_of(side + "Hand")
    hand = np.where(wh > 0.5)[0]
    W = np.array(MW @ arm.data.bones[side + "Hand"].head_local)
    rel = V[hand] - W
    far = rel[np.argmax(np.linalg.norm(rel, axis=1))]
    along = far / np.linalg.norm(far)
    s = rel @ along
    L = float(s.max())
    # past the palm the hand splits in two lumps with a gap: the thumb (smaller) and the finger block.
    # 2-means on the 3D positions of the outer part of the hand.
    dist = s > 0.35 * L
    pts = rel[dist]
    a0 = pts[np.argmax(s[dist])]                                  # finger block tip
    a1 = pts[np.argmax(np.linalg.norm(pts - a0, axis=1))]          # the point furthest from it
    for _ in range(30):
        lab = np.linalg.norm(pts - a1, axis=1) < np.linalg.norm(pts - a0, axis=1)
        a0, a1 = pts[~lab].mean(0), pts[lab].mean(0)
    thumb_c, finger_c = (a1, a0) if lab.sum() < (~lab).sum() else (a0, a1)
    # every hand vertex goes to the nearer lump, measured across the hand from the wrist line
    fwd = (thumb_c - finger_c) / np.linalg.norm(thumb_c - finger_c)
    t = rel @ fwd
    split = float((thumb_c @ fwd + finger_c @ fwd) / 2)
    thumb = (t > split) & (s > 0.15 * L)
    fingers = (~thumb) & (s > KNUCKLE * L - 0.01)
    log(f"{side}: lumps {lab.sum()} / {(~lab).sum()} verts, thumb centre {np.round(thumb_c, 3)}, finger centre {np.round(finger_c, 3)}")
    plan[side] = dict(W=W, along=along, fwd=fwd, L=L, hand=hand, s=s, t=t, thumb=hand[thumb], fingers=hand[fingers])
    log(f"{side}: hand {len(hand)} verts, length {L:.3f} m, thumb {thumb.sum()} verts, finger block {fingers.sum()} verts")

# the right hand is the mirror image of the left: each right-hand vertex takes the class of the
# nearest left-hand vertex mirrored across the body's middle
P = plan["Left"]
whR = weights_of("RightHand")
handR = np.where(whR > 0.5)[0]
mir = V[handR] * np.array([-1.0, 1.0, 1.0])
D = np.linalg.norm(mir[:, None, :] - V[P["hand"]][None, :, :], axis=2)
near = P["hand"][np.argmin(D, axis=1)]
tset, fset = set(P["thumb"].tolist()), set(P["fingers"].tolist())
WR = np.array(MW @ arm.data.bones["RightHand"].head_local)
relR = V[handR] - WR
alongR = P["along"] * np.array([-1.0, 1.0, 1.0])
plan["Right"] = dict(W=WR, along=alongR, L=P["L"], hand=handR,
                     thumb=handR[[n in tset for n in near]], fingers=handR[[n in fset for n in near]])
log(f"Right (mirrored, worst match {D.min(axis=1).max() * 100:.1f} cm): thumb {len(plan['Right']['thumb'])} verts, finger block {len(plan['Right']['fingers'])} verts")

# ------------------------------------------------------------------ joints
def slice_mean(idx, u, lo, hi):
    m = (u >= lo) & (u <= hi)
    while m.sum() < 4 and hi - lo < 1.0:
        lo -= 0.02; hi += 0.02; m = (u >= lo) & (u <= hi)
    return V[idx[m]].mean(0)


JOINT = {}
for side, P in plan.items():
    W = P["W"]
    fu = ((V[P["fingers"]] - W) @ P["along"]) / P["L"]
    chain = [slice_mean(P["fingers"], fu, j - 0.04, j + 0.04) for j in JOINTS] + [slice_mean(P["fingers"], fu, 0.93, 1.0)]
    tr = np.linalg.norm(V[P["thumb"]] - W, axis=1)
    r0, r1 = float(tr.min()), float(tr.max())
    tu = (tr - r0) / max(1e-6, r1 - r0)
    tip = V[P["thumb"][np.argmax(tr)]]
    tchain = [slice_mean(P["thumb"], tu, j - 0.06, j + 0.06) for j in (0.10, 0.50, 0.78)] + [tip]
    JOINT[side] = dict(index=chain, thumb=tchain, fu=fu, tu=tu)
    log(f"{side}: finger joints {[tuple(np.round(c, 3)) for c in chain]}")
    log(f"{side}: thumb joints {[tuple(np.round(c, 3)) for c in tchain]}")

# ------------------------------------------------------------------ bones
bpy.ops.object.select_all(action="DESELECT")
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
eb = arm.data.edit_bones
NEW = []
for side in plan:
    for finger, pts in (("Index", JOINT[side]["index"]), ("Thumb", JOINT[side]["thumb"])):
        parent = eb[side + "Hand"]
        for k in range(3):
            n = f"{side}Hand{finger}{k + 1}"
            e = eb.get(n) or eb.new(n)
            e.head = MWi @ Vector(pts[k]); e.tail = MWi @ Vector(pts[k + 1])
            if (e.tail - e.head).length < 0.004:
                e.tail = e.head + (MWi.to_3x3() @ Vector(plan[side]["along"])) * 0.01
            e.parent = parent; e.use_connect = False
            e.align_roll(MWi.to_3x3() @ Vector((0, 0, 1)))
            parent = e
            NEW.append(n)
bpy.ops.object.mode_set(mode="OBJECT")
log(f"added {len(NEW)} bones")

# ------------------------------------------------------------------ weights: smooth hand-offs between the joints
def bands(u, cuts, h):
    """Weights for len(cuts)+1 segments split at `cuts`, each hand-off blended over +-h."""
    def step(x):
        x = np.clip((x + h) / (2 * h), 0, 1); return x * x * (3 - 2 * x)
    out = []
    for i in range(len(cuts) + 1):
        lo = step(u - cuts[i - 1]) if i > 0 else np.ones_like(u)
        hi = 1 - step(u - cuts[i]) if i < len(cuts) else np.ones_like(u)
        out.append(lo * hi)
    w = np.array(out)
    return w / np.maximum(w.sum(0), 1e-6)


for side, P in plan.items():
    J = JOINT[side]
    groups = {}
    for n in [side + "Hand"] + [f"{side}Hand{f}{k}" for f in ("Index", "Thumb") for k in (1, 2, 3)]:
        groups[n] = mesh.vertex_groups.get(n) or mesh.vertex_groups.new(name=n)
    fu = ((V[P["fingers"]] - P["W"]) @ P["along"]) / P["L"]
    for idx, u, finger, cuts in ((P["fingers"], fu, "Index", (JOINTS[0], JOINTS[1], JOINTS[2])),
                                (P["thumb"], None, "Thumb", (0.10, 0.50, 0.78))):
        if u is None:
            tr = np.linalg.norm(V[idx] - P["W"], axis=1)
            u = (tr - tr.min()) / max(1e-6, tr.max() - tr.min())
        w = bands(u, cuts, 0.05)
        names = [side + "Hand"] + [f"{side}Hand{finger}{k}" for k in (1, 2, 3)]
        for j, vi in enumerate(idx):
            for g in list(mesh.data.vertices[vi].groups):
                mesh.vertex_groups[g.group].remove([int(vi)])
            for n, wv in zip(names, w[:, j]):
                if wv > 0.01:
                    groups[n].add([int(vi)], float(wv), "REPLACE")
log("weights moved onto the finger and thumb joints")

# ------------------------------------------------------------------ export with every clip
arm.animation_data.action = None
for pb in arm.pose.bones:
    pb.rotation_quaternion = (1, 0, 0, 0); pb.rotation_euler = (0, 0, 0); pb.location = (0, 0, 0)
bpy.ops.object.select_all(action="DESELECT")
arm.select_set(True); mesh.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(filepath=FBX, use_selection=True, object_types={"ARMATURE", "MESH"}, apply_unit_scale=True,
                         apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y", bake_space_transform=False,
                         add_leaf_bones=False, armature_nodetype="NULL", mesh_smooth_type="FACE", use_mesh_modifiers=False,
                         path_mode="STRIP", bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                         bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)
log("exported", FBX)

# a test clip to see the hands close in Blender: open, fist round a grip, open.
# Each chain bends about the axis across its knuckles: the normal of the plane its joints curl in.
from mathutils import Quaternion
CURL = {}
for side in plan:
    c = [Vector(p) for p in JOINT[side]["index"]]
    ax = (c[1] - c[0]).cross(c[3] - c[1])
    if ax.length < 1e-6:
        ax = Vector(plan[side]["along"]).cross(Vector((0, 0, 1)))
    CURL[side] = ax.normalized()
    log(f"{side}: knuckle axis {tuple(round(v, 2) for v in CURL[side])}")
test = bpy.data.actions.new("Finger_Test"); test.use_fake_user = True
arm.animation_data.action = test
for f, c in ((1, 0.0), (20, 1.0), (40, 1.0), (60, 0.0)):
    for side in plan:
        for finger, deg in (("Index", (55, 60, 40)), ("Thumb", (15, 25, 25))):
            for k in (1, 2, 3):
                pb = arm.pose.bones[f"{side}Hand{finger}{k}"]
                local = (pb.bone.matrix_local.to_3x3().inverted() @ (MWi.to_3x3() @ CURL[side])).normalized()
                pb.rotation_mode = "QUATERNION"
                pb.rotation_quaternion = Quaternion(local, math.radians(deg[k - 1] * c))
                pb.keyframe_insert("rotation_quaternion", frame=f)
bpy.context.scene.frame_start, bpy.context.scene.frame_end = 1, 60
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "Survivor_Fingers.blend"))
log("saved", os.path.join(HERE, "Survivor_Fingers.blend"))

if PREVIEW:
    bpy.context.scene.frame_set(int(os.environ.get("ZR_FRAME", "20")))   # 20 = fist round a grip
    # colour the faces: hand grey, thumb red, fingers orange
    mats = []
    for name, c in (("hand", (0.6, 0.6, 0.6)), ("thumb", (0.9, 0.15, 0.1)), ("fingers", (0.95, 0.6, 0.1))):
        m = bpy.data.materials.new(name); m.diffuse_color = (*c, 1); mats.append(m)
    me = mesh.data
    base = len(me.materials)
    for m in mats:
        me.materials.append(m)
    th = set(np.concatenate([plan[s]["thumb"] for s in plan]).tolist())
    fi = set(np.concatenate([plan[s]["fingers"] for s in plan]).tolist())
    ha = set(np.concatenate([plan[s]["hand"] for s in plan]).tolist())
    for p in me.polygons:
        vs = set(p.vertices)
        if vs <= th:
            p.material_index = base + 1
        elif vs <= fi:
            p.material_index = base + 2
        elif vs & ha:
            p.material_index = base
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"; sc.display.shading.color_type = "MATERIAL"
    sc.render.resolution_x, sc.render.resolution_y = 600, 600
    os.makedirs(PREVIEW, exist_ok=True)
    i = 0
    for side in ("Left", "Right"):
        P = plan[side]
        c = Vector(P["W"] + P["along"] * P["L"] * 0.5)
        for off, rot in ((Vector((0, -0.5, 0)), (math.radians(90), 0, 0)), (Vector((0.5 if side == "Left" else -0.5, 0, 0)), (math.radians(90), 0, math.radians(90 if side == "Left" else -90)))):
            cam = bpy.data.objects.new(f"c{i}", bpy.data.cameras.new(f"c{i}")); sc.collection.objects.link(cam)
            cam.data.type = "ORTHO"; cam.data.ortho_scale = 0.28
            cam.location = c + off; cam.rotation_euler = rot; sc.camera = cam
            sc.render.filepath = os.path.join(PREVIEW, f"parts_{i}.png"); bpy.ops.render.render(write_still=True)
            i += 1
    log("preview in", PREVIEW)
