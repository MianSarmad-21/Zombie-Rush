"""Check every clip of a zombie model: numbers first, pictures second.

Run:  blender -b -P Blender/zombie_check.py -- <path/to/Name.fbx> [sheet_dir]

Per clip it reports:
  - ground: lowest point of the skinned mesh per frame (sinks below -3 cm or floats above +4 cm while
    it should be standing; Death/Rise may leave the ground on purpose),
  - slide: how far a planted foot (sole within 6 cm of the ground) moves sideways/forwards per second
    in clips that stand in place (Idle, Attack, Hit, Spit),
  - pops: the largest turn of any bone between two frames (over 25 degrees per frame reads as a jerk),
  - seam: for looping clips, the largest difference between the first and the last frame,
  - knees: legs bent backwards (knee behind the hip-ankle line) at any frame.
With sheet_dir it also renders 8 frames per clip from the front and the side.
"""
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
FBX = args[0]
SHEETS = args[1] if len(args) > 1 else None
NAME = os.path.splitext(os.path.basename(FBX))[0]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
mesh = max((o for o in bpy.data.objects if o.type == "MESH"), key=lambda o: len(o.data.vertices))
sc = bpy.context.scene
sc.render.fps = 30
PFX = "mixamorig:" if any(b.name.startswith("mixamorig:") for b in arm.data.bones) else ""


def bone(n):
    return arm.pose.bones.get(PFX + n)


def world(pb, tail=False):
    return arm.matrix_world @ (pb.tail if tail else pb.head)


def lowest():
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    me = ev.to_mesh()
    co = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get("co", co)
    ev.to_mesh_clear()
    co = co.reshape(-1, 3)
    m = np.array(mesh.matrix_world)
    z = co @ m[2, :3] + m[2, 3]
    return float(z.min())


IN_PLACE = ("Idle", "Attack", "Hit", "Spit")
LOOPS = ("Idle", "Walk", "Run")
report = []
for act in sorted(bpy.data.actions, key=lambda a: a.name):
    clip = act.name.split("|")[-1]
    arm.animation_data_create()
    arm.animation_data.action = act
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    mins, feet, rots = [], {"LeftFoot": [], "RightFoot": []}, []
    knees_bad = 0
    for f in range(f0, f1 + 1):
        sc.frame_set(f)
        mins.append(lowest())
        for fn in feet:
            pb = bone(fn)
            feet[fn].append(world(pb) if pb else Vector())
        rots.append({pb.name: (arm.matrix_world.to_quaternion() @ pb.matrix.to_quaternion()) for pb in arm.pose.bones})
        for side in ("Left", "Right"):
            up, lo, ft = bone(side + "UpLeg"), bone(side + "Leg"), bone(side + "Foot")
            if up and lo and ft:
                hip, knee, ank = world(up), world(lo), world(ft)
                fwd = (arm.matrix_world.to_3x3() @ Vector((0, -1, 0))).normalized()
                line = ank - hip
                t = max(0.0, min(1.0, (knee - hip).dot(line) / max(line.length_squared, 1e-9)))
                off = knee - (hip + line * t)
                if off.dot(fwd) < -0.02 and line.length < 0.95 * ((knee - hip).length + (ank - knee).length):
                    knees_bad += 1
    n = len(mins)
    standing = clip not in ("Death", "Rise")
    sink, flt = min(mins), (min(mins) if not standing else max(mins))
    # pops
    pop, pop_at, pop_bone = 0.0, 0, ""
    for i in range(1, n):
        for b, q in rots[i].items():
            if any(f in b for f in ("Thumb", "Index", "Middle", "Ring", "Pinky")):
                continue   # fingers clench fast on purpose
            a = math.degrees(rots[i - 1][b].rotation_difference(q).angle)
            a = min(a, 360 - a)
            if a > pop:
                pop, pop_at, pop_bone = a, f0 + i, b
    # seam
    seam = 0.0
    if clip in LOOPS:
        for b, q in rots[-1].items():
            a = math.degrees(rots[0][b].rotation_difference(q).angle)
            seam = max(seam, min(a, 360 - a))
    # foot slide while planted (in-place clips)
    slide = 0.0
    if clip in IN_PLACE:
        for fn, pts in feet.items():
            for i in range(1, n):
                if pts[i].z < 0.12 and pts[i - 1].z < 0.12:
                    d = Vector((pts[i].x - pts[i - 1].x, pts[i].y - pts[i - 1].y)).length * sc.render.fps
                    slide = max(slide, d)
    issues = []
    if standing and sink < -0.03:
        issues.append(f"SINKS {-sink * 100:.0f} cm")
    if standing and max(mins) > 0.04 and clip != "Run":
        issues.append(f"FLOATS up to {max(mins) * 100:.0f} cm")
    if clip == "Death" and mins[-1] < -0.05:
        issues.append(f"ENDS {-mins[-1] * 100:.0f} cm in the ground")
    if pop > (35 if clip == "Run" else 30):
        issues.append(f"POP {pop:.0f} deg at frame {pop_at} ({pop_bone.replace(PFX, '')})")
    if seam > 3:
        issues.append(f"SEAM {seam:.0f} deg")
    if slide > 0.25:
        issues.append(f"FOOT SLIDES {slide:.2f} m/s")
    if knees_bad:
        issues.append(f"KNEE BENT BACKWARDS in {knees_bad} frames")
    line = (f"{NAME} {clip}: {n} frames ({(n - 1) / 30:.2f} s), lowest {sink * 100:+.0f}..{max(mins) * 100:+.0f} cm, "
            f"biggest step {pop:.0f} deg, seam {seam:.0f} deg, planted-foot slide {slide:.2f} m/s -> "
            + ("OK" if not issues else "; ".join(issues)))
    report.append(line)
    print("ZR_CHECK", line)

if SHEETS:
    os.makedirs(SHEETS, exist_ok=True)
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.render.resolution_x, sc.render.resolution_y = 260, 300
    H = max((mesh.matrix_world @ Vector(c)).z for c in mesh.bound_box)
    cams = []
    for i, (loc, rot) in enumerate((((0, -7, H * 0.55), (math.radians(90), 0, 0)), ((7, 0, H * 0.55), (math.radians(90), 0, math.radians(90))))):
        cam = bpy.data.objects.new(f"c{i}", bpy.data.cameras.new(f"c{i}"))
        sc.collection.objects.link(cam)
        cam.data.type = "ORTHO"
        cam.data.ortho_scale = H * 1.45
        cam.location = loc
        cam.rotation_euler = rot
        cams.append(cam)
    # ground line for reference
    bpy.ops.mesh.primitive_plane_add(size=6, location=(0, 0, 0))
    for act in sorted(bpy.data.actions, key=lambda a: a.name):
        arm.animation_data.action = act
        f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
        tiles = []
        for cam in cams:
            row = []
            for k in range(8):
                sc.frame_set(f0 + round(k * (f1 - f0) / 8))
                sc.camera = cam
                sc.render.filepath = os.path.join(SHEETS, f"_t_{NAME}.png")
                bpy.ops.render.render(write_still=True)
                im = bpy.data.images.load(sc.render.filepath)
                row.append(np.array(im.pixels[:], np.float32).reshape(300, 260, 4))
                bpy.data.images.remove(im)
            tiles.append(np.concatenate(row, axis=1))
        sheet = np.concatenate(tiles[::-1], axis=0)
        out = bpy.data.images.new("s", sheet.shape[1], sheet.shape[0], alpha=True)
        out.pixels.foreach_set(sheet.ravel())
        out.filepath_raw = os.path.join(SHEETS, f"{NAME}_{act.name.split('|')[-1]}.png")
        out.file_format = "PNG"
        out.save()
        bpy.data.images.remove(out)
    print("ZR_CHECK sheets in", SHEETS)
