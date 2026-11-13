"""Tactical gloves and jacket sleeves for the first-person arms.

exec() this from fp_viewmodel_hands.py after the hands are built, with `rig`, `arms`, `HANDS`, `HX` (the hand
builder's globals), `log`, `upd`, `ctx` in globals. Built hands (procedural, the default) are remapped by
material name; hybrid hands are split by bone weights and the wrist position.

The survivor's bandaged forearms and the grown fingers' flat skin read badly up close, so the arms get
dressed instead of re-modelled:
  slot 0  survivor material : a few faces at the shoulder cut (never seen; keeps the slot in the file)
  slot 1  FP_Glove          : the whole hand and fingers, a little past the wrist
  slot 2  FP_Cloth          : the jacket sleeve (forearm and upper arm)
  slot 3  FP_Trim           : the glove's wrist strap and the hard knuckle pad on the back of the hand
Unity fills slots 1..3 from GunSetupTools.FirstPersonMaterials (same order). Glove, sleeve and trim faces
get their own UVs (smart project) so the tiling leather and canvas textures sit evenly, and smooth shading.
"""
import math
import bmesh
import bpy
from mathutils import Vector

GLOVE_PAST_WRIST = 0.012     # the glove runs this far up the forearm (m)
STRAP = 0.030                # wrist strap width (m), past the glove's edge
PAD = (0.55, 1.08, 0.040)    # knuckle pad: from/to share of the wrist->knuckle length, half width (m)

me = arms.data
OLD_MATS = list(me.materials)
OLD_IDX = [p.material_index for p in me.polygons]   # read before the slots are reset (that renumbers faces)
_sh = [rig.data.bones[n].head_local for n in ("RightUpperArm", "LeftUpperArm") if n in rig.data.bones]
SHOULDER_C = sum(_sh, Vector()) / max(1, len(_sh))
while len(me.materials) > 1:
    me.materials.pop(index=len(me.materials) - 1)
if len(me.materials) == 0:
    me.materials.append(None)
COLORS = {"FP_Glove": (0.07, 0.07, 0.065, 1), "FP_Cloth": (0.22, 0.24, 0.17, 1), "FP_Trim": (0.03, 0.03, 0.03, 1)}
for n, c in COLORS.items():
    m = bpy.data.materials.get(n) or bpy.data.materials.new(n)
    m.diffuse_color = c
    me.materials.append(m)
GLOVE, CLOTH, TRIM = 1, 2, 3

gname = {vg.index: vg.name for vg in arms.vertex_groups}
if HANDS == "procedural":
    # built hands (fp_hands.py) already carry Glove/Skin/Cloth/Trim slots: map them by name, the survivor's
    # own sleeve becomes canvas too, and only a few faces at the shoulder cut keep the survivor slot
    bm = bmesh.new(); bm.from_mesh(me)
    want = {"FP_Glove": GLOVE, "FP_Skin": GLOVE, "FP_Cloth": CLOTH, "FP_Trim": TRIM}
    old = list(OLD_MATS)
    counts = {0: 0, GLOVE: 0, CLOTH: 0, TRIM: 0}
    far = []
    for f in bm.faces:
        oi = OLD_IDX[f.index]
        m = old[oi] if oi < len(old) else None
        mat = want.get(m.name if m else "", CLOTH)
        if mat == CLOTH and (m is None or m.name not in want):
            far.append((min((v.co - SHOULDER_C).length for v in f.verts), f))   # nearest the shoulder first
        f.material_index = mat; f.smooth = True; counts[mat] += 1
    far.sort(key=lambda t: t[0])
    for _, f in far[:12]:
        f.material_index = 0; counts[CLOTH] -= 1; counts[0] += 1
    bm.to_mesh(me); bm.free(); me.update()
    log(f"gloves (built hands): faces survivor {counts[0]}, glove {counts[GLOVE]}, sleeve {counts[CLOTH]}, trim {counts[TRIM]}")
else:
    HAND_INFO, FING = HX["HAND_INFO"], HX["FING"]
    KN = {}
    for s in ("Right", "Left"):
        R, W = HAND_INFO[s]["R"], HAND_INFO[s]["W"]
        ys = [(R.transposed() @ (FING[(s, d)]["pts"][0] - W)).y for d in ("Index", "Middle", "Ring", "Pinky")]
        KN[s] = sum(ys) / len(ys)
        log(f"gloves: {s} wrist->knuckle {KN[s] * 100:.1f} cm")

    bm = bmesh.new(); bm.from_mesh(me)
    deform = bm.verts.layers.deform.active
    counts = {0: 0, GLOVE: 0, CLOTH: 0, TRIM: 0}
    far = []
    for f in bm.faces:
        side_w = {"Right": 0.0, "Left": 0.0}
        hand_w = hand_only = 0.0
        for v in f.verts:
            for gi, w in v[deform].items():
                n = gname.get(gi, "")
                for s in side_w:
                    if n.startswith(s): side_w[s] += w
                if "Hand" in n:
                    hand_w += w
                    if n.endswith("Hand"): hand_only += w
        nv = len(f.verts)
        s = max(side_w, key=side_w.get)
        R, W = HAND_INFO[s]["R"], HAND_INFO[s]["W"]
        loc = R.transposed() @ (f.calc_center_median() - W)
        y = loc.y
        if hand_w / nv > 0.5 or y > -GLOVE_PAST_WRIST and loc.length < 0.2:
            mat = GLOVE
            nrm = R.transposed() @ f.normal
            if (hand_only / nv > 0.6 and nrm.z < -0.35 and PAD[0] * KN[s] < y < PAD[1] * KN[s] and abs(loc.x) < PAD[2]):
                mat = TRIM
        elif y > -GLOVE_PAST_WRIST - STRAP and loc.length < 0.25:
            mat = TRIM
        else:
            mat = CLOTH
            far.append((loc.length, f))
        f.material_index = mat
        f.smooth = True
        counts[mat] += 1
    far.sort(key=lambda t: -t[0])
    for _, f in far[:12]:            # the shoulder cut keeps the survivor slot alive
        f.material_index = 0; counts[CLOTH] -= 1; counts[0] += 1
    bm.to_mesh(me); bm.free(); me.update()
    log(f"gloves: faces survivor {counts[0]}, glove {counts[GLOVE]}, sleeve {counts[CLOTH]}, trim {counts[TRIM]}")

# even UVs for the dressed faces (the survivor faces keep theirs)
for ob in bpy.context.view_layer.objects: ob.select_set(ob is arms)
bpy.context.view_layer.objects.active = arms
with ctx(arms): bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="DESELECT")
bm = bmesh.from_edit_mesh(me)
for f in bm.faces: f.select = f.material_index != 0
bmesh.update_edit_mesh(me)
bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.01, area_weight=0.0, scale_to_bounds=False)
# faces in slot order: Unity numbers the sub-meshes by first use, so this keeps sub-mesh i = slot i
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.mesh.sort_elements(type="MATERIAL", elements={"FACE"})
bpy.ops.object.mode_set(mode="OBJECT")
upd()
log("gloves: dressed")
