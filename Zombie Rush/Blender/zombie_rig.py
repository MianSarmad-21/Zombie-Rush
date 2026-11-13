"""Rig a Meshy zombie on the Mixamo-named skeleton the other zombies use.

Run:  blender -b -P Blender/zombie_rig.py -- <Name> <meshy.fbx> [target_tris] [preview.png]
Writes Blender/<Name>_Rig.blend with objects <Name>_Rig (armature) and <Name>_Mesh (skinned mesh),
ready for zombie_anim.py, and copies the textures to Assets/Models/2026-11-09_<Name>_Final/:
<Name>_Albedo.png, <Name>_Normal.png, <Name>_MetalSmooth.png (metallic in R, smoothness in A).

How it fits the skeleton:
  - the bone layout (52 bones, fingers included) comes from the Wanderer's rig;
  - the body is measured from the mesh: crotch height (where the legs split), leg centres, knee and
    ankle heights, shoulder height and width, and for each arm the line from the shoulder to the
    fingertip (Meshy models come in an A-pose, arms angled down);
  - legs and spine are placed on those landmarks, each arm (with its fingers) is turned, scaled and
    moved onto the measured arm line;
  - the mesh faces -Y like the others (turned round if it came facing +Y), is cut to target_tris
    triangles and skinned with automatic weights.
"""
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
TEMPLATE = os.path.join(PROJECT, "Assets", "Models", "2026-11-08_Wanderer_Final", "Wanderer.fbx")
PFX = "mixamorig:"

args = sys.argv[sys.argv.index("--") + 1:]
NAME, SRC = args[0], args[1]
TARGET_TRIS = int(args[2]) if len(args) > 2 else 36000
PREVIEW = args[3] if len(args) > 3 else None
OUT_DIR = os.path.join(PROJECT, "Assets", "Models", f"2026-11-09_{NAME}_Final")


def log(*a):
    print("ZR_RIG", *a)


# ---------------------------------------------------------------- template skeleton
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=TEMPLATE)
tarm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
T = {b.name: (tarm.matrix_world @ b.head_local, tarm.matrix_world @ b.tail_local, b.parent.name if b.parent else None)
     for b in tarm.data.bones}
T_ROLL = {}
bpy.context.view_layer.objects.active = tarm
bpy.ops.object.mode_set(mode="EDIT")
for eb in tarm.data.edit_bones:
    T_ROLL[eb.name] = eb.roll
bpy.ops.object.mode_set(mode="OBJECT")
ORDER = [b.name for b in tarm.data.bones]
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o)
for a in list(bpy.data.actions):
    bpy.data.actions.remove(a)


def th(n):
    return T[PFX + n][0].copy()


# ---------------------------------------------------------------- the Meshy mesh
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=SRC)
meshes = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
mesh = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.select_all(action="DESELECT")
    for m in meshes:
        m.select_set(True)
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.join()
for o in list(bpy.data.objects):
    if o.type != "MESH":
        bpy.data.objects.remove(o)
bpy.ops.object.select_all(action="DESELECT")
mesh.select_set(True)
bpy.context.view_layer.objects.active = mesh
mesh.parent = None
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
mesh.name = NAME + "_Mesh"
mesh.data.name = NAME + "_Mesh"


def verts():
    co = np.empty(len(mesh.data.vertices) * 3, np.float32)
    mesh.data.vertices.foreach_get("co", co)
    return co.reshape(-1, 3)


# stand on z = 0, centred on x and y
V = verts()
mn, mx = V.min(0), V.max(0)
shift = Vector((-(mn[0] + mx[0]) / 2, -(mn[1] + mx[1]) / 2, -mn[2]))
mesh.data.transform(Matrix.Translation(shift))
V = verts()
H = float(V[:, 2].max())
log(f"height {H:.2f} m, width {np.ptp(V[:, 0]):.2f}, depth {np.ptp(V[:, 1]):.2f}")

# facing: toes stick out in front of the ankles. The other zombies face -Y.
feet = V[V[:, 2] < 0.05 * H]
legs = V[(V[:, 2] > 0.12 * H) & (V[:, 2] < 0.2 * H)]
toe_front = -(feet[:, 1].min() - legs[:, 1].mean())
toe_back = feet[:, 1].max() - legs[:, 1].mean()
if toe_back > toe_front * 1.15:
    mesh.data.transform(Matrix.Rotation(math.pi, 4, "Z"))
    V = verts()
    log("turned to face -Y")
else:
    log(f"faces -Y (toes {toe_front:.2f} m in front, {toe_back:.2f} m behind)")


# ---------------------------------------------------------------- landmarks
def band(z0, z1):
    return V[(V[:, 2] >= z0) & (V[:, 2] < z1)]


# crotch: going up from the knees, the first height where the middle (|x| < 2 cm) has mesh
crotch = 0.45 * H
for z in np.arange(0.30 * H, 0.62 * H, 0.005):
    sl = band(z, z + 0.01)
    if (np.abs(sl[:, 0]) < 0.02).sum() >= 3:
        crotch = float(z)
        break
hips_z = crotch + 0.05 * H
log(f"crotch {crotch:.2f} m, hips {hips_z:.2f} m")

def leg_centre(z, side):
    sl = band(z - 0.01, z + 0.01)
    sl = sl[sl[:, 0] * side > 0.01]
    sl = sl[np.abs(sl[:, 0]) < 0.3 * H]  # arms hanging low are further out
    return Vector((float(np.median(sl[:, 0])), float(np.median(sl[:, 1])), z)) if len(sl) else None

# torso width: measured at the belly, where A-pose arms hang well clear of the body
belly = band(0.60 * H, 0.66 * H)
belly = belly[np.abs(belly[:, 0]) < 0.2 * H]
half = float(np.percentile(np.abs(belly[:, 0]), 95))
chest = band(0.74 * H, 0.78 * H)
chest = chest[np.abs(chest[:, 0]) < half]
sh_z = 0.81 * H
log(f"chest half width {half:.2f} m, shoulders at {sh_z:.2f} m")

# arms: everything well outside the chest and above the knees; the fingertip is the vertex furthest out
def arm_line(side):
    pts = V[(V[:, 0] * side > half + 0.04) & (V[:, 2] > 0.32 * H) & (V[:, 2] < 0.86 * H)]
    tip = pts[np.argmax(pts[:, 0] * side)]
    # the arm's centre line, from the clear part of the arm (below the armpit, above the hand),
    # followed up to shoulder height: that is the shoulder joint
    mid = pts[(pts[:, 2] > tip[2] + 0.12 * H) & (pts[:, 2] < sh_z - 0.08 * H)]
    c = mid.mean(0)
    _, _, vt = np.linalg.svd(mid - c)
    d = vt[0] if vt[0][2] > 0 else -vt[0]
    t = (sh_z - 0.02 * H - c[2]) / d[2]
    j = c + d * t
    root = Vector((float(j[0]), float(np.median(chest[:, 1])) if len(chest) else float(j[1]), sh_z - 0.02 * H))
    return root, Vector(tip.tolist()), len(pts)


# ---------------------------------------------------------------- fit the skeleton
P = {}
hip_c = Vector((0.0, float(np.median(band(hips_z - 0.02, hips_z + 0.02)[:, 1])), hips_z))
tz = lambda n: th(n).z
scale_body = (sh_z - hips_z) / (tz("LeftArm") - tz("Hips"))
for n in ("Hips", "Spine", "Spine1", "Spine2", "Neck", "Head", "HeadTop_End"):
    if PFX + n not in T:
        continue
    t = th(n)
    if n in ("Neck", "Head", "HeadTop_End"):
        # head proportions from the real top of the head
        k = {"Neck": 0.835, "Head": 0.88, "HeadTop_End": 1.0}[n]
        P[n] = Vector((0.0, hip_c.y + (t.y - th("Hips").y) * scale_body, k * H))
    else:
        P[n] = hip_c + (t - th("Hips")) * scale_body
for side, s in (("Left", 1), ("Right", -1)):
    knee_z, ankle_z = 0.28 * H, 0.055 * H
    up = leg_centre(crotch - 0.03 * H, s) or Vector((s * 0.1, hip_c.y, crotch))
    kn = leg_centre(knee_z, s) or Vector((up.x, up.y, knee_z))
    an = leg_centre(ankle_z + 0.02, s) or Vector((kn.x, kn.y, ankle_z))
    P[side + "UpLeg"] = Vector((up.x * 0.9, hip_c.y, hips_z - 0.04 * H))
    P[side + "Leg"] = Vector((kn.x, kn.y - 0.01, knee_z))
    P[side + "Foot"] = Vector((an.x, an.y + 0.01, ankle_z))
    fv = V[(V[:, 2] < 0.06 * H) & (V[:, 0] * s > 0)]
    toe_y = float(fv[:, 1].min())
    P[side + "ToeBase"] = Vector((an.x, an.y + (toe_y - an.y) * 0.62, 0.02 * H))
    P[side + "Toe_End"] = Vector((an.x, toe_y, 0.02 * H))
    # arm chain: template arm (T-pose, along +-X) turned onto the measured shoulder -> fingertip line
    root, tip, count = arm_line(s)
    t_root, t_tip = th(side + "Arm"), th(side + "HandMiddle3") + (th(side + "HandMiddle3") - th(side + "HandMiddle2"))
    d_t, d_n = (t_tip - t_root), (tip - root)
    R = d_t.normalized().rotation_difference(d_n.normalized()).to_matrix()
    k = d_n.length / d_t.length
    for b in ORDER:
        short = b[len(PFX):]
        if short.startswith(side) and short not in (side + "Shoulder", side + "UpLeg", side + "Leg", side + "Foot", side + "ToeBase", side + "Toe_End"):
            P[short] = root + R @ ((T[b][0] - t_root) * k)
    P[side + "Shoulder"] = Vector((s * 0.03 * H, P["Spine2"].y, sh_z - 0.01 * H))
    log(f"{side} arm: {count} verts, shoulder ({root.x:.2f}, {root.z:.2f}) -> fingertip ({tip.x:.2f}, {tip.z:.2f}), {math.degrees(d_n.angle(Vector((s, 0, 0)))):.0f} deg below level")

# build the armature with the template's names, parents and rolls; tails point at the first child
arm_data = bpy.data.armatures.new(NAME + "_Rig")
arm = bpy.data.objects.new(NAME + "_Rig", arm_data)
bpy.context.scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
children = {}
for b in ORDER:
    p = T[b][2]
    if p:
        children.setdefault(p, []).append(b)
eb = {}
for b in ORDER:
    e = arm_data.edit_bones.new(b)
    short = b[len(PFX):]
    head = P.get(short)
    if head is None:  # any bone not placed: keep the template offset from its parent
        par = T[b][2]
        head = P[par[len(PFX):]] + (T[b][0] - T[par][0]) * scale_body
        P[short] = head
    e.head = head
    eb[b] = e
for b in ORDER:
    e = eb[b]
    kids = children.get(b, [])
    main = [k for k in kids if not any(f in k for f in ("Thumb", "Index", "Ring", "Pinky", "Shoulder", "UpLeg"))]
    if b.endswith("Hand"):
        main = [k for k in kids if "Middle1" in k]
    if b.endswith("Hips"):
        main = [k for k in kids if k.endswith("Spine")]
    if b.endswith("Spine2"):
        main = [k for k in kids if k.endswith("Neck")]
    if main:
        e.tail = eb[main[0]].head
    else:  # end bones: continue the parent's direction
        par = T[b][2]
        d = (e.head - eb[par].head) if par else Vector((0, 0, 0.1))
        e.tail = e.head + d.normalized() * max(0.02, d.length * 0.5)
    if (e.tail - e.head).length < 0.005:
        e.tail = e.head + Vector((0, 0, 0.02))
for b in ORDER:
    if T[b][2]:
        eb[b].parent = eb[T[b][2]]
        eb[b].use_connect = False
bpy.ops.object.mode_set(mode="EDIT")
for e in arm_data.edit_bones:
    e.roll = T_ROLL.get(e.name, 0.0)
bpy.ops.object.mode_set(mode="OBJECT")

# ---------------------------------------------------------------- trim and skin
# weld the texture-seam splits first: automatic weights fail on a mesh in many loose pieces
bm = bmesh.new(); bm.from_mesh(mesh.data)
n0 = len(bm.verts)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0005)
bm.to_mesh(mesh.data); bm.free()
log(f"welded seams: {n0} -> {len(mesh.data.vertices)} vertices")
tris = sum(len(p.vertices) - 2 for p in mesh.data.polygons)
if tris > TARGET_TRIS:
    dec = mesh.modifiers.new("trim", "DECIMATE")
    dec.ratio = TARGET_TRIS / tris
    dec.use_collapse_triangulate = True
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.modifier_apply(modifier=dec.name)
tris = sum(len(p.vertices) - 2 for p in mesh.data.polygons)
# skin by distance to the bones (Blender's automatic weights fail on these meshes):
# each vertex takes its nearest bones, legs only their own side, then the weights are smoothed
segs, names = [], []
for b in arm_data.bones:
    segs.append((np.array(b.head_local), np.array(b.tail_local)))
    names.append(b.name)
A = np.array([s_[0] for s_ in segs]); Bt = np.array([s_[1] for s_ in segs])
V = verts()
AB = Bt - A
t = np.clip(np.einsum("vbk,bk->vb", V[:, None, :] - A[None], AB) / np.maximum((AB * AB).sum(1), 1e-9)[None], 0, 1)
closest = A[None] + t[..., None] * AB[None]
D = np.linalg.norm(V[:, None, :] - closest, axis=2)
side = np.sign(V[:, 0])
for i, n in enumerate(names):
    short = n[len(PFX):]
    if short.startswith("Left"):
    # a leg never pulls the other leg; an arm never pulls the other side
    if short.startswith("Left"):
        D[V[:, 0] < -0.02, i] = 1e3
    if short.startswith("Right"):
        D[V[:, 0] > 0.02, i] = 1e3
    # legs only below the hips, arms only above the crotch
    if any(k in short for k in ("UpLeg", "Leg", "Foot", "Toe")):
        D[V[:, 2] > hips_z + 0.04 * H, i] = 1e3
    if any(k in short for k in ("Arm", "Hand", "Shoulder")):
        D[V[:, 2] < crotch, i] = 1e3
K = 3
idx = np.argsort(D, 1)[:, :K]
dk = np.take_along_axis(D, idx, 1)
w = 1.0 / np.maximum(dk, 0.01) ** 4
w[dk > dk[:, :1] * 1.8 + 0.02] = 0.0
w /= w.sum(1, keepdims=True)
W = np.zeros((len(V), len(names)), np.float32)
np.put_along_axis(W, idx, w, 1)
# smooth along mesh edges so joints bend softly
edges = np.array([e.vertices[:] for e in mesh.data.edges])
for it in range(4):
    acc = np.zeros_like(W); cnt = np.zeros(len(V), np.float32)
    np.add.at(acc, edges[:, 0], W[edges[:, 1]]); np.add.at(acc, edges[:, 1], W[edges[:, 0]])
    np.add.at(cnt, edges[:, 0], 1); np.add.at(cnt, edges[:, 1], 1)
    W = 0.5 * W + 0.5 * acc / np.maximum(cnt, 1)[:, None]
# keep the 4 strongest (Unity's default skin weights), renormalise
top = np.argsort(-W, 1)[:, :4]
Wt = np.take_along_axis(W, top, 1)
Wt /= np.maximum(Wt.sum(1, keepdims=True), 1e-6)
mesh.vertex_groups.clear()
groups = {n: mesh.vertex_groups.new(name=n) for n in names}
for vi in range(len(V)):
    for k in range(4):
        if Wt[vi, k] > 0.01:
            groups[names[top[vi, k]]].add([vi], float(Wt[vi, k]), "REPLACE")
mesh.parent = arm
mod = mesh.modifiers.new("Armature", "ARMATURE")
mod.object = arm
weighted = sum(1 for v in mesh.data.vertices if any(g.weight > 0.01 for g in v.groups))
log(f"mesh {tris} triangles, {weighted} of {len(mesh.data.vertices)} vertices weighted, {len(mesh.vertex_groups)} bone groups")

# ---------------------------------------------------------------- textures
os.makedirs(OUT_DIR, exist_ok=True)
src_dir = os.path.dirname(SRC)
base = os.path.splitext(os.path.basename(SRC))[0]


def load(suffix):
    p = os.path.join(src_dir, base + suffix + ".png")
    return bpy.data.images.load(p) if os.path.exists(p) else None


def save_copy(img, dst):
    img.filepath_raw = dst
    img.file_format = "PNG"
    img.save()


import shutil
for suffix, dst in (("", "Albedo"), ("_normal", "Normal")):
    srcp = os.path.join(src_dir, base + suffix + ".png")
    if os.path.exists(srcp):
        shutil.copyfile(srcp, os.path.join(OUT_DIR, f"{NAME}_{dst}.png"))
met, rough = load("_metallic"), load("_roughness")
if met and rough:
    w, h = met.size
    a = np.empty(w * h * 4, np.float32); met.pixels.foreach_get(a); a = a.reshape(h, w, 4)
    b = np.empty(w * h * 4, np.float32); rough.pixels.foreach_get(b); b = b.reshape(h, w, 4)
    out = np.zeros((h, w, 4), np.float32)
    out[..., 0] = a[..., 0]
    out[..., 3] = 1.0 - b[..., 0]
    ms = bpy.data.images.new(NAME + "_MetalSmooth", w, h, alpha=True)
    ms.pixels.foreach_set(out.ravel())
    save_copy(ms, os.path.join(OUT_DIR, f"{NAME}_MetalSmooth.png"))
log("textures copied to", OUT_DIR)

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, f"{NAME}_Rig.blend"))
log("saved", os.path.join(HERE, f"{NAME}_Rig.blend"))

# ---------------------------------------------------------------- preview: front and side, bones over the mesh
if PREVIEW:
    sc = bpy.context.scene
    arm.show_in_front = True
    arm.data.display_type = "STICK"
    for i, (loc, rot) in enumerate((((0, -6, H / 2), (math.radians(90), 0, 0)), ((6, 0, H / 2), (math.radians(90), 0, math.radians(90))))):
        cam = bpy.data.objects.new(f"cam{i}", bpy.data.cameras.new(f"cam{i}"))
        sc.collection.objects.link(cam)
        cam.data.type = "ORTHO"; cam.data.ortho_scale = H * 1.15
        cam.location = loc; cam.rotation_euler = rot
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sc.collection.objects.link(sun)
    sun.rotation_euler = (0.7, 0.2, 0.4); sun.data.energy = 3
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.show_xray = True
    sc.display.shading.xray_alpha = 0.35
    sc.render.resolution_x, sc.render.resolution_y = 700, 800
    for i in range(2):
        sc.camera = bpy.data.objects[f"cam{i}"]
        sc.render.filepath = PREVIEW.replace(".png", f"_{i}.png")
        bpy.ops.render.render(write_still=True)
    log("preview", PREVIEW)
