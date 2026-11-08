"""Builds the Zombie Rush characters in Blender and exports them for Unity.

Run headless (Blender 5.2):
  blender -b --factory-startup --python Tools/Blender/build_characters.py -- <names|all> [--out <Assets/Models dir>] [--preview <dir>]

Each character goes to <out>/ZR_<Name>/ZR_<Name>.fbx with _Albedo.png and _Emission.png,
and a preview render to <preview>/ZR_<Name>.png.
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402
from mathutils import Euler, Vector  # noqa: E402

import zr_lib as L  # noqa: E402
from zr_body import Body, lerp  # noqa: E402
import zr_cast  # noqa: E402

V = Vector
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..'))


# =================================================================== Yaku
def yaku():
    """Tattooed yakuza enforcer: open leather vest over a full-body tattoo, jeans,
    slicked pompadour, a katana slung across the back, burning amber eyes."""
    L.define_materials({
        'skin': dict(base='#869276', var='#64705a', grime=0.45, blood=0.25, spots='#6d6a55', spots_scale=18, rough=0.6),
        'tat': dict(base='#869276', var='#64705a', tattoo='#15203a', tattoo2='#9a2a1e', tattoo_scale=4.5, munewari=True, tattoo_cover=0.28,
                    grime=0.4, blood=0.3, rough=0.55),
        'vest': dict(base='#2a1c14', var='#171009', grime=0.6, blood=0.35, rough=0.55),
        'jeans': dict(base='#33476a', var='#202d47', grime=0.55, blood=0.3, rough=0.85),
        'belt': dict(base='#1d1713', var='#2b221b', rough=0.5),
        'buckle': dict(base='#b8a774', rough=0.3, metal=0.8),
        'boot': dict(base='#3a2a1e', var='#22180f', grime=0.6, rough=0.6),
        'sole': dict(base='#151210', rough=0.9),
        'hair': dict(base='#0f0e12', var='#24222c', var_scale=40, rough=0.3),
        'socket': dict(base='#1a0b0b', rough=0.9),
        'eye': dict(base='#ffc95a', emit='#ffae1f', emit_str=6.0, rough=0.2),
        'mouth': dict(base='#2e0608', rough=0.4),
        'teeth': dict(base='#cbbd93', var='#8f8055', rough=0.4),
        'nail': dict(base='#3b3426', rough=0.5),
        'wound': dict(base='#6a0b0b', var='#2b0303', spots='#c0a080', spots_scale=30, rough=0.25),
        'sheath': dict(base='#141013', var='#2a2228', rough=0.3),
        'wrap': dict(base='#6a1b1b', stripes='#141414', stripes_scale=40, stripes_dir='Y', rough=0.7),
        'guard': dict(base='#8e8158', rough=0.3, metal=0.8),
    })
    J = L.make_joints(1.82, sw=0.175)
    B = Body(J, w=1.0, d=0.98, chest=1.08, arm=1.08)
    parts = []

    body = L.Part('body')
    waist = 0.31
    B.torso(body, lambda t, d: 'jeans' if t < waist else 'tat')
    B.neck(body, 'tat')
    for side in ('Left', 'Right'):
        B.arm(body, side, 'tat')
        B.leg(body, side, 'jeans', scale=1.06, flare=0.5)
        B.boot(body, side, 'boot', shaft=0.17, sole='sole')
    parts.append(body)

    # Open leather vest with a ragged hem, and a wound across the ribs.
    vest = L.Part('vest', solidify=0.012)
    B.torso(vest, 'vest', f0=0.28, f1=0.93, scale=1.06, steps=12, cap0=False, cap1=False,
            gap=lambda t, d: d.y < -0.8 and t < 0.92, jag0=0.06)
    parts.append(vest)
    wound = L.Part('wound', subsurf=1)
    z, rx, ry, yo = B.torso_ring(0.52)
    L.ellipsoid(wound, (0.05, yo - ry * 0.93, z), (0.06, 0.012, 0.035), 'wound', segs=12, rings=6,
                rot=Euler((0, 0.5, 0)))
    parts.append(wound)

    belt = L.Part('belt', subsurf=0, smooth=True)
    z, rx, ry, yo = B.torso_ring(waist)
    L.loft(belt, [(0, yo, z - 0.025), (0, yo, z + 0.025)], [rx * 1.05] * 2, [ry * 1.07] * 2, 'belt', segs=24,
           cap0=False, cap1=False)
    L.box(belt, (0, yo - ry * 1.09, z), (0.07, 0.014, 0.055), 'buckle', bevel=0.006)
    belt.solidify = 0.01
    parts.append(belt)

    for side in ('Left', 'Right'):
        hp = L.Part('hand' + side, bind=side + 'Hand')
        B.hand(hp, side, 'skin', nail='nail', curl=0.55)
        parts.append(hp)

    head = L.Part('head', bind='Head')
    B.head(head, 'skin', gaunt=0.25, chin=0.08)
    B.zombie_eyes(head, 'socket', 'eye', brow='skin')
    B.cheeks(head, 'skin')
    B.nose(head, 'skin')
    B.ears(head, 'skin')
    B.zombie_mouth(head, 'mouth', 'teeth', open_=0.55)
    B.face_marks('skin')
    # Scar slash over the left eye.
    L.ellipsoid(head, B.at((0.3, -1, 0.3), 0.004), V((0.006, 0.006, 0.04)) * B.s, 'wound', segs=8, rings=6,
                rot=Euler((0, -0.5, 0)))
    parts.append(head)

    hair = L.Part('hair', bind='Head')
    hc, hr = B.hc, B.hr
    L.ellipsoid(hair, hc + V((0, 0.012, 0.012)), hr * 1.07, 'hair', segs=20, rings=12,
                cut=lambda c: (c.z < 0.05 and c.y < 0.55) or c.z < -0.45)
    L.ellipsoid(hair, hc + V((0, -0.065, 0.10)) * B.s, V((0.075, 0.07, 0.045)) * B.s, 'hair', segs=14, rings=8,
                rot=Euler((-0.45, 0, 0)))
    for sx in (1, -1):
        L.ellipsoid(hair, hc + V((sx * 0.085, 0.02, 0.0)) * B.s, V((0.02, 0.07, 0.05)) * B.s, 'hair', segs=10,
                    rings=6)
    parts.append(hair)

    # Katana across the back, handle over the right shoulder.
    sword = L.Part('katana', bind='Spine2', subsurf=0)
    z0, z1 = J['Spine'].z - 0.02, J['Neck'].z + 0.06
    p0, p1 = V((0.20, 0.16, z0)), V((-0.17, 0.165, z1))
    L.cyl(sword, p0, p1, 0.02, 0.019, 'sheath', segs=10)
    g = p1 + (p1 - p0).normalized() * 0.01
    L.cyl(sword, p1, g, 0.036, None, 'guard', segs=12)
    L.cyl(sword, g, g + (p1 - p0).normalized() * 0.25, 0.017, 0.016, 'wrap', segs=10)
    L.band(sword, [V((0.16, 0.13, z0 + 0.05)), V((0.05, -0.13, J['Spine1'].z)), V((-0.17, -0.12, J['Spine2'].z + 0.12)),
                   V((-0.12, 0.14, z1 - 0.03))], 0.018, 0.004, 'belt', up=(0, 1, 0))
    parts.append(sword)

    return J, parts, []


CHARACTERS = {
    'Yaku': yaku,
    'Miner': zr_cast.miner,
    'Spitter': zr_cast.spitter,
    'Bomber': zr_cast.bomber,
    'Screamer': zr_cast.screamer,
    'Survivor': zr_cast.survivor,
}


def build(name, out_root, preview_dir):
    L.reset()
    J, parts, extras = CHARACTERS[name]()
    obs = [p.finish() for p in parts]
    extra_obs = [(p.finish(), bone) for p, bone in extras]
    full = 'ZR_' + name
    arm, body = L.assemble(full, obs, J, extra_obs)
    out_dir = os.path.join(out_root, full)
    L.bake(body, out_dir, full)
    L.export_fbx(os.path.join(out_dir, full + '.fbx'), arm, body, [o for o, _ in extra_obs])
    print(f'[ZR] {full}: {sum(len(p.polygons) for p in [body.data])} faces')
    if preview_dir:
        L.pose_arms_down(arm, J)
        L.render_preview(os.path.join(preview_dir, full + '.png'), J['HeadTop'].z)
        if '--face' in sys.argv:
            L.render_preview(os.path.join(preview_dir, full + '_face.png'), J['HeadTop'].z, res=(700, 700), focus=J['Head'].z + 0.02, dist=1.2)


def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    out_root = os.path.join(ROOT, 'Assets', 'Models', '2026-11-05_Characters')
    preview = os.path.join(ROOT, 'Docs', 'Characters')
    names = []
    i = 0
    while i < len(argv):
        if argv[i] == '--out':
            out_root = argv[i + 1]
            i += 2
        elif argv[i] == '--preview':
            preview = argv[i + 1]
            i += 2
        elif argv[i].startswith('--'):
            i += 1  # a switch such as --face
        else:
            names.append(argv[i])
            i += 1
    if not names or names == ['all']:
        names = list(CHARACTERS)
    os.makedirs(preview, exist_ok=True)
    for n in names:
        build(n, out_root, preview)


main()
