"""Zombie Rush cast: Miner, Spitter, Bomber, Screamer and the Survivor (player)."""
import math
import random

from mathutils import Euler, Vector

import zr_lib as L
from zr_body import Body, lerp, surf

V = Vector

COMMON = {
    'socket': dict(base='#120808', rough=0.9),
    'mouth': dict(base='#2a0507', rough=0.4),
    'teeth': dict(base='#c9bb90', var='#8a7b52', rough=0.4),
    'nail': dict(base='#2e281d', rough=0.5),
    'sole': dict(base='#141110', rough=0.9),
    'strap': dict(base='#171615', var='#262420', rough=0.6),
    'buckle': dict(base='#9a9a92', rough=0.3, metal=0.8),
}


def strap_over_shoulder(P, B, sx, front_frac, back_frac, xoff, mat, w=0.022, h=0.006, out=0.012):
    """Strap from the chest, over one shoulder, down the back."""
    s = B.s
    f = B.torso_point(front_frac, sx * xoff, -1, out)
    top = B.torso_point(0.97, sx * 0.55, 0.02, out)
    top.z += 0.02 * s
    b = B.torso_point(back_frac, sx * xoff, 1, out)
    mid_f = B.torso_point(lerp(front_frac, 0.97, 0.7), sx * xoff * 1.2, -1, out)
    mid_b = B.torso_point(lerp(back_frac, 0.97, 0.7), sx * xoff * 1.2, 1, out)
    L.loft(P, [f, mid_f, top, mid_b, b], [w] * 5, [h] * 5, mat, segs=4, up=(1, 0, 0))


def ring_at(P, B, frac, mat, out=0.012, height=0.05, segs=24):
    z, rx, ry, yo = B.torso_ring(frac)
    L.loft(P, [(0, yo, z - height / 2), (0, yo, z + height / 2)], [rx + out] * 2, [ry + out] * 2, mat,
           segs=segs, cap0=False, cap1=False)


# =================================================================== Miner
def miner():
    """Miner juggernaut: hulking, plaid flannel with rolled sleeves, overalls, hard hat with a
    working headlamp, gas mask, sledgehammer hanging from the right hand."""
    L.define_materials(dict(COMMON, **{
        'skin': dict(base='#73828a', var='#525f67', grime=0.55, blood=0.3, spots='#4c4740', spots_scale=16, rough=0.6),
        'flannel': dict(base='#8e1d1d', var='#6a1313', plaid='#250808', plaid_scale=6.5, grime=0.5, blood=0.2, rough=0.9),
        'overall': dict(base='#2d3b52', var='#1d2738', grime=0.65, blood=0.2, rough=0.85),
        'boot': dict(base='#2c241e', var='#1a1511', grime=0.6, rough=0.6),
        'eye': dict(base='#ff8a4a', emit='#ff4a14', emit_str=7.0, rough=0.2),
        'helmet': dict(base='#e2aa1c', var='#a87812', grime=0.55, rough=0.4),
        'lampring': dict(base='#353535', rough=0.4, metal=0.7),
        'lens': dict(base='#fff4cc', emit='#ffe7a0', emit_str=12.0, rough=0.1),
        'mask': dict(base='#2a2a2a', var='#1a1a1a', grime=0.4, rough=0.5),
        'filter': dict(base='#5a5f4e', var='#3c4034', stripes='#262626', stripes_scale=70, stripes_dir='X', rough=0.5),
        'wood': dict(base='#6f4c2b', var='#4a311b', stripes='#3a2614', stripes_scale=40, stripes_dir='Y', rough=0.7),
        'iron': dict(base='#454a4f', var='#2c3034', grime=0.5, rough=0.45, metal=0.8),
    }))
    J = L.make_joints(1.98, sw=0.23, hw=0.115)
    B = Body(J, w=1.32, d=1.28, chest=1.18, belly=0.03, arm=1.5, leg=1.3, hand=1.45, head=1.18, neck=1.3,
             hunch_back=0.05)
    parts = []
    body = L.Part('body')
    waist = 0.36
    B.torso(body, lambda t, d: 'overall' if t < waist else 'flannel')
    B.neck(body, 'skin')
    for side in ('Left', 'Right'):
        B.arm(body, side, lambda t, d: 'flannel' if t < 0.5 else 'skin')
        B.cuff(body, side, 0.49, 'flannel', scale=1.28, width=0.06)
        B.leg(body, side, 'overall', scale=1.06, flare=0.3)
        B.boot(body, side, 'boot', shaft=0.22, sole='sole', scale=1.1)
    parts.append(body)

    bib = L.Part('bib', solidify=0.012)
    B.torso(bib, 'overall', f0=0.30, f1=0.72, scale=1.05, steps=8, cap0=False, cap1=False,
            gap=lambda t, d: d.y > -0.5)
    p = B.torso_point(0.6, 0, -1, 0.02)
    L.box(bib, p, (0.12 * B.s, 0.02, 0.09 * B.s), 'overall', bevel=0.008)
    for sx in (1, -1):
        strap_over_shoulder(bib, B, sx, 0.70, 0.36, 0.42, 'overall', w=0.03, h=0.007)
        L.ellipsoid(bib, B.torso_point(0.70, sx * 0.42, -1, 0.02), (0.018, 0.01, 0.018), 'buckle', segs=10, rings=6)
    parts.append(bib)

    for side in ('Left', 'Right'):
        hp = L.Part('hand' + side, bind=side + 'Hand')
        B.hand(hp, side, 'skin', nail='nail', curl=0.5, fist=(side == 'Right'))
        parts.append(hp)

    head = L.Part('head', bind='Head')
    B.head(head, 'skin', jaw=0.25, chin=0.04)
    B.zombie_eyes(head, 'socket', 'eye', brow='skin')
    B.ears(head, 'skin')
    B.face_marks('skin', mouth_height=-0.55)
    parts.append(head)

    gear = L.Part('helmet', bind='Head', subsurf=1)
    hc, hr = B.hc, B.hr
    dc = hc + V((0, 0.004, 0.035)) * B.s
    dr = V((hr.x * 1.13, hr.y * 1.13, hr.z * 1.0))
    L.ellipsoid(gear, dc, dr, 'helmet', segs=22, rings=12, cut=lambda c: c.z < 0.1)
    L.ellipsoid(gear, dc + V((0, 0, dr.z * 0.1)), V((dr.x * 1.32, dr.y * 1.42, 0.012)), 'helmet', segs=22, rings=6)
    L.ellipsoid(gear, dc + V((0, 0, dr.z * 0.95)), V((0.018, dr.y * 0.95, 0.02)), 'helmet', segs=10, rings=6)
    lb = surf(dc, dr, (0, -1, 0.5))
    ld = V((0, -1, 0.15)).normalized()
    L.cyl(gear, lb - ld * 0.02, lb + ld * 0.04, 0.034, 0.03, 'lampring', segs=14)
    L.cyl(gear, lb + ld * 0.035, lb + ld * 0.045, 0.026, None, 'lens', segs=14)
    # Gas mask over mouth and nose, filters either side, strap round the head.
    mc = B.at((0, -1, -0.55), -0.01)
    L.ellipsoid(gear, mc, V((0.068, 0.05, 0.05)) * B.s * 1.18, 'mask', segs=16, rings=10)
    for sx in (1, -1):
        fb = mc + V((sx * 0.045, -0.02, -0.02)) * B.s
        fd = V((sx * 0.8, -0.6, -0.3)).normalized()
        L.cyl(gear, fb, fb + fd * 0.06 * B.s, 0.034 * B.s, 0.036 * B.s, 'filter', segs=14)
    z = mc.z + 0.01
    L.loft(gear, [(hc.x, hc.y, z - 0.012), (hc.x, hc.y, z + 0.012)], [hr.x * 1.01] * 2, [hr.y * 1.0] * 2, 'strap',
           segs=20, cap0=False, cap1=False)
    parts.append(gear)

    hammer = L.Part('hammer', bind='RightHand', subsurf=0)
    h = B.o['hand'] * B.s
    hp = J['RightHand'] + V((-0.07 * h, 0, -0.02 * h))
    L.cyl(hammer, hp + V((0.06, 0, 0)), hp + V((-0.58 * B.s, 0, 0)), 0.021, 0.019, 'wood', segs=10)
    L.box(hammer, hp + V((-0.60 * B.s, 0, 0)), (0.12, 0.28, 0.12), 'iron', bevel=0.012)
    parts.append(hammer)
    return J, parts, []


# =================================================================== Spitter
def spitter():
    """Spitter: emaciated, bloated toxic belly and a glowing throat sac, jaw unhinged and
    dribbling green bile, rags of a hospital gown."""
    L.define_materials(dict(COMMON, **{
        'skin': dict(base='#7e8c4c', var='#56622e', grime=0.5, spots='#b3ab45', spots_scale=22, blood=0.15, rough=0.5),
        'sac': dict(base='#6fbf2e', var='#2f7412', spots='#d8ff70', spots_scale=26, emit='#3fd010', emit_str=1.5,
                    rough=0.2),
        'goo': dict(base='#9cff4a', emit='#78ff26', emit_str=5.0, rough=0.1),
        'rags': dict(base='#8d8e80', var='#5f6053', grime=0.85, blood=0.35, rough=0.9),
        'pants': dict(base='#4b4537', var='#2f2b22', grime=0.7, blood=0.2, rough=0.9),
        'eye': dict(base='#b8ff8a', emit='#78ff26', emit_str=7.0, rough=0.2),
        'mouth': dict(base='#1b3608', emit='#3caa10', emit_str=1.5, rough=0.3),
        'teeth': dict(base='#b7ad74', var='#7d7240', rough=0.4),
    }))
    J = L.make_joints(1.78, sw=0.155, hw=0.085, arm=1.06, neck=1.4)
    B = Body(J, w=0.86, d=0.86, chest=0.95, belly=0.075, arm=0.78, leg=0.8, hand=1.05, head=1.04, neck=0.9,
             hunch_back=0.03)
    parts = []
    body = L.Part('body')
    B.torso(body, lambda t, d: 'pants' if t < 0.27 else 'skin')
    B.neck(body, 'skin')
    for side in ('Left', 'Right'):
        B.arm(body, side, 'skin')
        B.leg(body, side, lambda t, d: 'pants' if t < 0.58 else 'skin')
        B.bare_foot(body, side, 'skin', claw='nail')
    parts.append(body)

    rags = L.Part('rags', solidify=0.01)
    B.torso(rags, 'rags', f0=0.18, f1=0.94, scale=1.07, steps=12, cap0=False, cap1=False, jag0=0.1,
            gap=lambda t, d: 0.22 < t < 0.62 and d.y < -0.25 and abs(d.x) < 0.75)
    parts.append(rags)

    sacs = L.Part('sacs')
    rnd = random.Random(7)
    L.ellipsoid(sacs, (0, -0.05 * B.s, J['Neck'].z - 0.005), V((0.065, 0.052, 0.055)) * B.s, 'sac', segs=16, rings=10)
    for i in range(9):
        f = rnd.uniform(0.28, 0.55)
        a = rnd.uniform(-2.3, -0.85)
        p = B.torso_point(f, math.cos(a), math.sin(a), -0.004)
        r = rnd.uniform(0.022, 0.04) * B.s
        L.ellipsoid(sacs, p, (r, r, r), 'sac', segs=10, rings=6)
    for i in range(5):
        f = rnd.uniform(0.7, 0.9)
        a = rnd.uniform(0.6, 2.5)
        p = B.torso_point(f, math.cos(a), math.sin(a), -0.004)
        r = rnd.uniform(0.025, 0.045) * B.s
        L.ellipsoid(sacs, p, (r, r, r * 0.9), 'sac', segs=10, rings=6)
    parts.append(sacs)

    for side in ('Left', 'Right'):
        hp = L.Part('hand' + side, bind=side + 'Hand')
        B.hand(hp, side, 'skin', nail='nail', curl=0.65, finger_len=1.2)
        parts.append(hp)

    head = L.Part('head', bind='Head')
    B.head(head, 'skin', gaunt=0.35, chin=0.02, elong=1.12)
    B.zombie_eyes(head, 'socket', 'eye', spread=0.38, brow='skin')
    B.cheeks(head, 'skin')
    B.nose(head, 'skin', rotten='socket')
    B.ears(head, 'skin', 0.8)
    mc = B.zombie_mouth(head, 'mouth', 'teeth', open_=0.95, width=1.18, height=-0.45, seed=11)
    B.face_marks('skin', spread=0.38, rim='#2a2a10', blood='#3c6a10', mouth_height=-0.45)
    for x, ln in ((-0.025, 0.07), (0.0, 0.11), (0.022, 0.06), (0.035, 0.04)):
        top = mc + V((x, -0.012, -0.03)) * B.s
        L.loft(head, [top, top + V((0, -0.004, -ln * 0.5)) * B.s, top + V((0, -0.002, -ln)) * B.s],
               [0.008 * B.s, 0.005 * B.s, 0.0035 * B.s], None, 'goo', segs=6)
        L.ellipsoid(head, top + V((0, -0.002, -ln - 0.004)) * B.s, V((0.007, 0.007, 0.009)) * B.s, 'goo', segs=8,
                    rings=6)
    parts.append(head)
    return J, parts, []


# =================================================================== Bomber
def bomber():
    """Bomber: heavy-set demolition worker in a torn hi-vis jumpsuit with a dynamite belt,
    a ticking timer box (its light blinks faster as the fuse burns) and a gas tank on the back."""
    L.define_materials(dict(COMMON, **{
        'skin': dict(base='#998874', var='#746350', grime=0.5, spots='#5a2f22', spots_scale=20, blood=0.2, rough=0.55),
        'suit': dict(base='#c95a16', var='#983e10', grime=0.7, blood=0.25, stripes='#dcd9b2', stripes_scale=1.5,
                     stripes_dir='Z', rough=0.85),
        'dyn': dict(base='#b3261e', var='#861912', stripes='#e2caa0', stripes_scale=14, stripes_dir='Z', rough=0.5),
        'tape': dict(base='#3d3d3d', var='#5a5a5a', rough=0.6),
        'wire_r': dict(base='#cf1717', rough=0.4),
        'wire_y': dict(base='#e0c020', rough=0.4),
        'timer': dict(base='#1c1c1c', var='#2a2a2a', rough=0.4),
        'display': dict(base='#ff3a2a', emit='#ff2a1a', emit_str=6.0, rough=0.2),
        'tank': dict(base='#d6aa1e', var='#a8841a', grime=0.6, stripes='#161616', stripes_scale=9,
                     stripes_dir='DIAGONAL', rough=0.5),
        'metal': dict(base='#7a7a7a', grime=0.3, rough=0.35, metal=0.8),
        'glove': dict(base='#2a2a2a', var='#1a1a1a', rough=0.7),
        'boot': dict(base='#231d18', var='#15110e', grime=0.5, rough=0.6),
        'eye': dict(base='#ff7a6a', emit='#ff2414', emit_str=7.0, rough=0.2),
        'stitch': dict(base='#1a0e0e', rough=0.6),
        'lens': dict(base='#2c5a5c', rough=0.08, metal=0.3),
    }))
    J = L.make_joints(1.74, sw=0.18, hw=0.105)
    B = Body(J, w=1.22, d=1.2, chest=1.0, belly=0.11, arm=1.22, leg=1.2, hand=1.2, head=1.08, neck=1.45)
    parts = []
    body = L.Part('body')
    B.torso(body, 'suit')
    B.neck(body, 'skin')
    for side in ('Left', 'Right'):
        B.arm(body, side, 'suit')
        B.cuff(body, side, 0.97, 'glove', scale=1.25, width=0.05)
        B.leg(body, side, 'suit', scale=1.07, flare=0.3)
        B.boot(body, side, 'boot', shaft=0.2, sole='sole', scale=1.08)
    ring_at(body, B, 0.995, 'suit', out=0.008, height=0.04)
    parts.append(body)

    for side in ('Left', 'Right'):
        hp = L.Part('hand' + side, bind=side + 'Hand')
        B.hand(hp, side, 'glove', curl=0.45)
        parts.append(hp)

    # Dynamite belt round the belly.
    belt = L.Part('dynamite', bind='Spine', subsurf=0)
    z, rx, ry, yo = B.torso_ring(0.36)
    n = 15
    for i in range(n):
        a = 2 * math.pi * i / n
        if abs(math.sin(a) - 1) < 0.25:
            continue  # leave the spine side free for the tank
        p = V((0, yo, z)) + V(((rx + 0.024) * math.cos(a), (ry + 0.024) * math.sin(a), 0))
        L.cyl(belt, p - V((0, 0, 0.1)), p + V((0, 0, 0.1)), 0.024, None, 'dyn', segs=10)
    for dz in (-0.055, 0.055):
        L.loft(belt, [(0, yo, z + dz - 0.016), (0, yo, z + dz + 0.016)], [rx + 0.05] * 2, [ry + 0.05] * 2, 'tape',
               segs=28, cap0=False, cap1=False)
    parts.append(belt)

    timer = L.Part('timer', bind='Spine2', subsurf=0)
    tp = B.torso_point(0.66, 0, -1, 0.035)
    L.box(timer, tp, (0.15 * B.s, 0.06, 0.11 * B.s), 'timer', bevel=0.008)
    L.box(timer, tp + V((0, -0.031, 0.012)), (0.085 * B.s, 0.006, 0.035 * B.s), 'display')
    for sx in (1, -1):
        L.cyl(timer, tp + V((sx * 0.05, -0.031, -0.03)), tp + V((sx * 0.05, -0.036, -0.03)), 0.008, None, 'metal')
    parts.append(timer)

    wires = L.Part('wires', subsurf=0)
    belt_front = B.torso_point(0.36, 0, -1, 0.05)
    for sx, m in ((1, 'wire_r'), (-1, 'wire_y'), (0.5, 'wire_r')):
        a = tp + V((sx * 0.06, -0.01, -0.055))
        b = belt_front + V((sx * 0.09, -0.01, 0.1))
        mid = (a + b) * 0.5 + V((sx * 0.03, -0.03, 0))
        L.tube(wires, [a, mid, b], 0.006, m)
    parts.append(wires)

    tank = L.Part('tank', bind='Spine2', subsurf=1)
    tb = B.torso_point(0.48, 0, 1, 0.09)
    tt = B.torso_point(0.86, 0, 1, 0.09)
    L.loft(tank, [tb, (tb + tt) * 0.5, tt], [0.085] * 3, None, 'tank', segs=16)
    L.cyl(tank, tt + V((0, 0, 0.03)), tt + V((0, 0, 0.09)), 0.02, None, 'metal')
    for t in (0.15, 0.85):
        p = lerp(tb, tt, t)
        L.loft(tank, [p - V((0, 0, 0.012)), p + V((0, 0, 0.012))], [0.089] * 2, None, 'tape', segs=16,
               cap0=False, cap1=False)
    hose = [tt + V((0, 0, 0.09)), tt + V((-0.1, -0.02, 0.14)), B.torso_point(0.97, -0.5, -0.3, 0.05),
            tp + V((-0.08, -0.01, 0.05))]
    L.loft(tank, hose, [0.012] * 4, None, 'tape', segs=6)
    for sx in (1, -1):
        strap_over_shoulder(tank, B, sx, 0.62, 0.45, 0.45, 'strap', w=0.03, h=0.006, out=0.014)
    parts.append(tank)

    head = L.Part('head', bind='Head')
    B.head(head, 'skin', jaw=0.18, chin=0.05)
    B.zombie_eyes(head, 'socket', 'eye', brow='skin')
    B.nose(head, 'skin')
    B.ears(head, 'skin')
    B.cheeks(head, 'skin')
    B.zombie_mouth(head, 'mouth', 'teeth', open_=0.3, seed=5)
    B.face_marks('skin')
    for i in range(9):  # stitched scalp wound
        t = i / 8
        d = V((lerp(-0.55, 0.45, t), lerp(-0.2, 0.3, t), 0.85))
        L.box(head, B.at(d, 0.0), V((0.004, 0.022, 0.004)) * B.s, 'stitch', rot=Euler((0, 0, 0.6)))
    # Goggles pushed up on the forehead.
    z = B.at((0, -1, 0.55)).z
    hc, hr = B.hc, B.hr
    L.loft(head, [(hc.x, hc.y, z - 0.014), (hc.x, hc.y, z + 0.014)], [hr.x * 1.03] * 2, [hr.y * 1.03] * 2, 'strap',
           segs=20, cap0=False, cap1=False)
    for sx in (1, -1):
        gp = B.at((sx * 0.38, -1, 0.55), -0.012)
        L.cyl(head, gp + V((0, 0.012, 0)), gp + V((0, -0.016, 0)), 0.026 * B.s, None, 'timer', segs=14)
        L.cyl(head, gp + V((0, -0.016, 0)), gp + V((0, -0.019, 0)), 0.02 * B.s, None, 'lens', segs=14)
    parts.append(head)

    light = L.Part('BombLight', bind=None, subsurf=1)
    L.ellipsoid(light, tp + V((0.045, -0.01, 0.07)), (0.018, 0.018, 0.016), 'display', segs=12, rings=8)
    return J, parts, [(light, 'Spine2')]


# =================================================================== Screamer
def screamer():
    """Screamer: tall, gaunt banshee with a torn gown and lank hair; her unhinged jaw glows
    from the throat when she shrieks to frenzy the horde."""
    L.define_materials(dict(COMMON, **{
        'skin': dict(base='#a2adb7', var='#78838f', grime=0.45, blood=0.25, spots='#565e69', spots_scale=18, rough=0.5),
        'dress': dict(base='#3a3047', var='#211b2a', grime=0.6, blood=0.35, rough=0.9),
        'hair': dict(base='#0b0b0e', var='#22202a', var_scale=40, rough=0.4),
        'eye': dict(base='#e8fbff', emit='#a8f0ff', emit_str=8.0, rough=0.2),
        'mouth': dict(base='#1d4a5a', emit='#6fe8ff', emit_str=4.0, rough=0.3),
        'teeth': dict(base='#d8d2b8', var='#a39c80', rough=0.4),
        'nail': dict(base='#1d1d24', rough=0.4),
    }))
    J = L.make_joints(1.92, sw=0.15, hw=0.088, arm=1.14, leg=1.04, neck=1.3)
    B = Body(J, w=0.84, d=0.8, chest=0.95, arm=0.72, leg=0.74, hand=1.2, head=1.0, neck=0.85)
    parts = []
    body = L.Part('body')
    B.torso(body, 'skin')
    B.neck(body, 'skin')
    for side in ('Left', 'Right'):
        B.arm(body, side, 'skin')
        B.leg(body, side, 'skin')
        B.bare_foot(body, side, 'skin', claw='nail')
    parts.append(body)

    dress = L.Part('dress', solidify=0.01)
    pts, rx, ry = [], [], []
    for i in range(8):
        f = lerp(0.9, 0.05, i / 7)
        z, a, b, yo = B.torso_ring(f)
        pts.append((0, yo, z))
        rx.append(a * 1.07)
        ry.append(b * 1.08)
    zk = J['LeftLeg'].z - 0.12 * B.s
    for i in range(1, 6):
        t = i / 5
        pts.append((0, 0.005, lerp(B.zc - 0.02, zk, t)))
        rx.append(lerp(0.2, 0.27, t) * B.s)
        ry.append(lerp(0.14, 0.2, t) * B.s)
    L.loft(dress, pts, rx, ry, 'dress', segs=16, cap0=False, cap1=False, jag1=0.16, up=(1, 0, 0))
    for sx in (1, -1):
        strap_over_shoulder(dress, B, sx, 0.88, 0.85, 0.4, 'dress', w=0.022, h=0.004, out=0.01)
    parts.append(dress)

    for side in ('Left', 'Right'):
        hp = L.Part('hand' + side, bind=side + 'Hand')
        B.hand(hp, side, 'skin', nail='nail', curl=0.6, finger_len=1.4)
        parts.append(hp)

    head = L.Part('head', bind='Head')
    B.head(head, 'skin', gaunt=0.45, chin=0.0, elong=1.3)
    B.zombie_eyes(head, 'socket', 'eye', spread=0.36, height=0.18, size=1.1)
    B.cheeks(head, 'skin', 0.9)
    B.nose(head, 'skin', rotten='socket', size=0.9)
    B.zombie_mouth(head, 'mouth', 'teeth', open_=1.7, width=1.05, height=-0.52, n_teeth=8, seed=9)
    B.face_marks('skin', height=0.18, rim='#14182a', mouth_height=-0.52)
    parts.append(head)

    hair = L.Part('hair', bind='Head')
    hc, hr = B.hc, B.hr
    L.ellipsoid(hair, hc + V((0, 0.01, 0.01)), hr * 1.06, 'hair', segs=20, rings=12,
                cut=lambda c: (c.z < 0.3 and c.y < -0.45) or c.z < -0.55)
    rnd = random.Random(5)
    for i in range(18):
        az = math.radians(rnd.uniform(-125, 125))
        d = V((math.sin(az), math.cos(az), 0))
        top = hc + V((d.x * hr.x * 0.9, d.y * hr.y * 0.9, hr.z * 0.45))
        side = hc + V((d.x * hr.x * 1.12, d.y * hr.y * 1.12, -hr.z * 0.3))
        end = hc + V((d.x * hr.x * 1.25, d.y * hr.y * 1.3, -hr.z * 0.3 - (0.17 + rnd.random() * 0.1) * B.s))
        r = rnd.uniform(0.014, 0.02) * B.s
        L.loft(hair, [top, side, end], [r, r * 0.9, r * 0.35], None, 'hair', segs=6)
    parts.append(hair)
    return J, parts, []


# =================================================================== Survivor (player)
def survivor():
    """The player: a capable survivor in an olive field jacket, plate carrier with mag pouches,
    cargo pants with knee pads, gloves, a red bandana and a pack with a bedroll."""
    L.define_materials(dict(COMMON, **{
        'skin': dict(base='#d29b74', var='#bd8462', var_scale=6, rough=0.55),
        'lips': dict(base='#a0594a', rough=0.4),
        'jacket': dict(base='#545f3a', var='#3e472b', grime=0.25, rough=0.85),
        'carrier': dict(base='#2a2e30', var='#1d2022', grime=0.2, rough=0.7),
        'pouch': dict(base='#3e4536', var='#2e3428', grime=0.25, rough=0.8),
        'pants': dict(base='#8a7b5c', var='#72654a', grime=0.3, rough=0.9),
        'pad': dict(base='#232323', var='#333333', rough=0.5),
        'boot': dict(base='#3e2c1e', var='#2a1e15', grime=0.3, rough=0.5),
        'glove': dict(base='#1f1f1f', var='#2c2c2c', rough=0.7),
        'hair': dict(base='#2a1b11', var='#3d2919', var_scale=45, rough=0.5),
        'beard': dict(base='#7a5844', var='#5a3e2c', var_scale=80, rough=0.8),
        'sclera': dict(base='#efebe2', rough=0.2),
        'iris': dict(base='#2c5f80', rough=0.1),
        'pupil': dict(base='#050505', rough=0.1),
        'brow': dict(base='#2a1b11', rough=0.6),
        'bandana': dict(base='#a12020', var='#741414', rough=0.8),
        'pack': dict(base='#48523a', var='#353d2b', grime=0.3, rough=0.85),
        'bedroll': dict(base='#5c4b36', stripes='#3a2f22', stripes_scale=30, stripes_dir='X', rough=0.9),
    }))
    J = L.make_joints(1.80, sw=0.185, hw=0.098)
    B = Body(J, w=1.06, d=1.0, chest=1.12, arm=1.14, leg=1.06, hand=1.1, head=1.04, neck=1.3)
    parts = []
    body = L.Part('body')
    waist = 0.3
    B.torso(body, lambda t, d: 'pants' if t < waist else 'jacket')
    B.neck(body, 'skin')
    for side in ('Left', 'Right'):
        B.arm(body, side, 'jacket', scale=1.06)
        B.cuff(body, side, 0.95, 'jacket', scale=1.3, width=0.05)
        B.leg(body, side, 'pants', scale=1.08, flare=0.25)
        B.boot(body, side, 'boot', shaft=0.2, sole='sole', scale=1.04)
    parts.append(body)

    gear = L.Part('carrier', solidify=0.026)
    B.torso(gear, 'carrier', f0=0.44, f1=0.86, scale=1.06, steps=8, cap0=False, cap1=False,
            gap=lambda t, d: abs(d.x) > 0.8)
    parts.append(gear)

    kit = L.Part('kit', subsurf=1)
    for x in (-0.075, 0.0, 0.075):
        p = B.torso_point(0.53, x * 2.2, -1, 0.045)
        L.box(kit, p + V((0, -0.01, 0)), (0.06 * B.s, 0.04, 0.085 * B.s), 'pouch', bevel=0.008)
    L.box(kit, B.torso_point(0.74, 0.45, -1, 0.04), (0.05, 0.035, 0.07), 'pouch', bevel=0.007)
    for sx in (1, -1):
        strap_over_shoulder(kit, B, sx, 0.84, 0.84, 0.48, 'carrier', w=0.04, h=0.012, out=0.012)
        strap_over_shoulder(kit, B, sx, 0.62, 0.7, 0.62, 'strap', w=0.022, h=0.006, out=0.035)
    ring_at(kit, B, waist, 'strap', out=0.016, height=0.05, segs=24)
    L.box(kit, B.torso_point(waist, 0, -1, 0.02), (0.055, 0.016, 0.045), 'buckle', bevel=0.004)
    for sx in (1, -1):
        L.box(kit, B.torso_point(waist - 0.04, sx, 0.2, 0.04), (0.04, 0.08, 0.08), 'pouch', bevel=0.008)
    # Thigh pouch on the right leg, knee pads on both.
    tz = lerp(J['RightUpLeg'].z, J['RightLeg'].z, 0.45)
    L.box(kit, (J['RightUpLeg'].x - 0.1 * B.s, 0.0, tz), (0.04, 0.1, 0.12), 'pouch', bevel=0.01)
    for side in ('Left', 'Right'):
        k = J[side + 'Leg']
        L.ellipsoid(kit, k + V((0, -0.06, 0.0)), (0.055, 0.03, 0.06), 'pad', segs=12, rings=8)
    parts.append(kit)

    for side in ('Left', 'Right'):
        hp = L.Part('hand' + side, bind=side + 'Hand')
        B.hand(hp, side, 'glove', curl=0.3)
        parts.append(hp)

    pack = L.Part('pack', bind='Spine2', subsurf=1)
    pc = B.torso_point(0.62, 0, 1, 0.09)
    L.box(pack, pc, (0.3 * B.s, 0.14, 0.34 * B.s), 'pack', bevel=0.03)
    L.box(pack, pc + V((0, 0.072, -0.05)), (0.2 * B.s, 0.03, 0.14 * B.s), 'pack', bevel=0.015)
    L.cyl(pack, pc + V((0.2, 0.0, 0.22)), pc + V((-0.2, 0.0, 0.22)), 0.06, None, 'bedroll', segs=14)
    parts.append(pack)

    head = L.Part('head', bind='Head')
    B.head(head, 'skin', jaw=0.14, chin=0.07, brow=False)
    s = B.s * B.o['head']
    for sx in (1, -1):
        e = B.at(V((0.35 * sx, -1, 0.12)), 0.014)
        L.ellipsoid(head, e, V((0.02, 0.013, 0.015)) * s, 'sclera', segs=12, rings=8)
        L.ellipsoid(head, e + V((0, -0.011, 0)) * s, V((0.0095, 0.004, 0.0095)) * s, 'iris', segs=10, rings=6)
        L.ellipsoid(head, e + V((0, -0.0145, 0)) * s, V((0.0045, 0.002, 0.0045)) * s, 'pupil', segs=8, rings=6)
        L.ellipsoid(head, e + V((0, -0.002, 0.009)) * s, V((0.023, 0.014, 0.008)) * s, 'skin', segs=12, rings=6)
        L.ellipsoid(head, B.at(V((0.36 * sx, -1, 0.34)), 0.004), V((0.03, 0.01, 0.008)) * s, 'brow', segs=10,
                    rings=6, rot=Euler((0, -0.18 * sx, 0)))
    B.nose(head, 'skin', 1.15)
    L.ellipsoid(head, B.at((0, -1, -0.42), 0.006), V((0.03, 0.01, 0.009)) * s, 'lips', segs=12, rings=6)
    B.ears(head, 'skin')
    parts.append(head)

    hair = L.Part('hair', bind='Head')
    hc, hr = B.hc, B.hr
    L.ellipsoid(hair, hc + V((0, 0.006, 0.018)), hr * 1.06, 'hair', segs=22, rings=14,
                cut=lambda c: not (c.z > 0.3 or (c.y > 0.3 and c.z > -0.3) or (c.y > -0.1 and c.z > 0.0)) or (c.y < -0.55 and c.z < 0.5))
    L.ellipsoid(hair, hc + V((0.02, -0.07, 0.1)) * B.s, V((0.075, 0.05, 0.04)) * B.s, 'hair', segs=14, rings=8,
                rot=Euler((-0.5, 0.1, 0.2)))
    L.ellipsoid(hair, hc + V((0, 0.002, 0.0)), hr * 1.018, 'beard', segs=22, rings=14,
                cut=lambda c: not (c.z < -0.32 and c.y < -0.25) or (abs(c.x) < 0.3 and -0.55 < c.z < -0.3 and c.y < -0.6))
    parts.append(hair)

    band = L.Part('bandana', bind='Neck', solidify=0.008)
    z0 = J['Neck'].z + 0.01
    L.loft(band, [(0, 0.0, z0 - 0.02), (0, 0.0, z0 + 0.03)], [0.09 * B.s, 0.08 * B.s], [0.084 * B.s, 0.077 * B.s],
           'bandana', segs=14, cap0=False, cap1=False)
    L.ellipsoid(band, (0, -0.085 * B.s, z0 - 0.03), V((0.05, 0.02, 0.05)) * B.s, 'bandana', segs=10, rings=6)
    parts.append(band)
    return J, parts, []
