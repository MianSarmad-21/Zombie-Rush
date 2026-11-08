"""Zombie Rush character kit for Blender (run headless, see build_characters.py).

Builds stylized low-poly characters out of lofted tubes and shaped primitives,
paints them with procedural materials, bakes everything to one texture set
(albedo with ambient occlusion, plus emission), rigs them to a Mixamo-named
skeleton in a T-pose so Unity's Humanoid retargeting can drive them, and exports FBX.

Axes: the character faces -Y, +Z is up, its left side is +X. Units are metres.
"""
import math
import os
import random

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

V = Vector


# ---------------------------------------------------------------- scene

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    MAT_SPECS.clear()


def link(ob):
    bpy.context.scene.collection.objects.link(ob)
    return ob


def select_only(*obs):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in obs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]


# ---------------------------------------------------------------- colours

def hexc(h, a=1.0):
    """'#rrggbb' (sRGB) to a linear RGBA tuple for shader node defaults."""
    h = h.lstrip('#')
    out = []
    for i in (0, 2, 4):
        c = int(h[i:i + 2], 16) / 255.0
        out.append(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4)
    return (out[0], out[1], out[2], a)


# ---------------------------------------------------------------- materials

MAT_SPECS = {}


def define_materials(specs):
    MAT_SPECS.update(specs)


def get_mat(name):
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    spec = MAT_SPECS.get(name)
    if spec is None:
        raise KeyError('No material spec named ' + name)
    return build_material(name, spec)


def _node(nt, kind, x=0, y=0, **props):
    n = nt.nodes.new(kind)
    n.location = (x, y)
    for k, v in props.items():
        setattr(n, k, v)
    return n


def _mix(nt, a, b, fac):
    """Colour mix: a when fac=0, b when fac=1. a/b are sockets or RGBA tuples, fac socket or float."""
    m = _node(nt, 'ShaderNodeMix', data_type='RGBA')
    ins = [s for s in m.inputs if s.enabled]
    f, sa, sb = ins[0], ins[1], ins[2]
    for sock, val in ((sa, a), (sb, b)):
        if isinstance(val, tuple):
            sock.default_value = val
        else:
            nt.links.new(val, sock)
    if isinstance(fac, (int, float)):
        f.default_value = fac
    else:
        nt.links.new(fac, f)
    return [s for s in m.outputs if s.enabled][0]


def _range(nt, val, lo, hi, to_lo=0.0, to_hi=1.0):
    r = _node(nt, 'ShaderNodeMapRange')
    nt.links.new(val, r.inputs['Value'])
    r.inputs['From Min'].default_value = lo
    r.inputs['From Max'].default_value = hi
    r.inputs['To Min'].default_value = to_lo
    r.inputs['To Max'].default_value = to_hi
    r.clamp = True
    return r.outputs['Result']


def _math(nt, op, a, b=0.0):
    m = _node(nt, 'ShaderNodeMath', operation=op)
    for sock, val in ((m.inputs[0], a), (m.inputs[1], b)):
        if isinstance(val, (int, float)):
            sock.default_value = val
        else:
            nt.links.new(val, sock)
    return m.outputs[0]


def _noise(nt, coord, scale, detail=4.0, rough=0.55, distortion=0.0):
    n = _node(nt, 'ShaderNodeTexNoise')
    nt.links.new(coord, n.inputs['Vector'])
    n.inputs['Scale'].default_value = scale
    n.inputs['Detail'].default_value = detail
    n.inputs['Roughness'].default_value = rough
    if 'Distortion' in n.inputs:
        n.inputs['Distortion'].default_value = distortion
    return n.outputs['Fac']


def _wave(nt, coord, scale, direction='X', kind='BANDS', distortion=0.0, detail=0.0):
    w = _node(nt, 'ShaderNodeTexWave', wave_type=kind)
    if kind == 'BANDS':
        w.bands_direction = direction
    else:
        w.rings_direction = direction if direction in ('X', 'Y', 'Z', 'SPHERICAL') else 'SPHERICAL'
    nt.links.new(coord, w.inputs['Vector'])
    w.inputs['Scale'].default_value = scale
    w.inputs['Distortion'].default_value = distortion
    w.inputs['Detail'].default_value = detail
    return w.outputs['Fac']


def build_material(name, s):
    """Procedural look, later baked. Keys:
    base, var (second tone), var_scale, rough, metal, grime (0-1), blood (0-1),
    plaid (colour), plaid_scale, tattoo (ink colour), tattoo_amount, stripes (colour),
    emit (colour), emit_str, spots (colour), spots_scale."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = _node(nt, 'ShaderNodeOutputMaterial', 900, 0)
    bsdf = _node(nt, 'ShaderNodeBsdfPrincipled', 600, 0)
    nt.links.new(bsdf.outputs['BSDF'], out.inputs['Surface'])
    coord = _node(nt, 'ShaderNodeTexCoord', -900, 0).outputs['Object']

    col = hexc(s['base'])
    if 'var' in s:
        f = _range(nt, _noise(nt, coord, s.get('var_scale', 9.0), 5.0), 0.38, 0.68)
        col = _mix(nt, col, hexc(s['var']), f)
    if 'plaid' in s:
        k = s.get('plaid_scale', 7.0)
        bx = _range(nt, _wave(nt, coord, k, 'X'), 0.72, 0.78)
        bz = _range(nt, _wave(nt, coord, k, 'Z'), 0.72, 0.78)
        thin_x = _range(nt, _wave(nt, coord, k * 3.0, 'X'), 0.93, 0.96)
        both = _math(nt, 'ADD', _math(nt, 'MULTIPLY', bx, 0.5), _math(nt, 'MULTIPLY', bz, 0.5))
        col = _mix(nt, col, hexc(s['plaid']), both)
        col = _mix(nt, col, hexc(s.get('plaid_line', '#e8d9b0')), _math(nt, 'MULTIPLY', thin_x, 0.35))
    if 'stripes' in s:
        st = _range(nt, _wave(nt, coord, s.get('stripes_scale', 6.0), s.get('stripes_dir', 'Z')), 0.8, 0.84)
        col = _mix(nt, col, hexc(s['stripes']), st)
    if 'spots' in s:
        v = _node(nt, 'ShaderNodeTexVoronoi')
        nt.links.new(coord, v.inputs['Vector'])
        v.inputs['Scale'].default_value = s.get('spots_scale', 14.0)
        sp = _range(nt, v.outputs['Distance'], 0.22, 0.12)
        col = _mix(nt, col, hexc(s['spots']), _math(nt, 'MULTIPLY', sp, 0.8))
    kind = s.get('kind') or KIND_BY_NAME.get(name, 'plain')
    if isinstance(col, tuple):
        col = _rgb(nt, col)

    # Fine colour breakup everywhere, so no surface reads as a flat slab.
    fine = _range(nt, _noise(nt, coord, 38.0, 6.0, 0.6), 0.3, 0.7)
    col = _mix(nt, _mix(nt, col, (0.0, 0.0, 0.0, 1.0), 0.14), _mix(nt, col, (1.0, 1.0, 1.0, 1.0), 0.06), fine)

    if kind == 'skin':
        # Bruised, mottled rot and a web of dark veins.
        bruise = _range(nt, _noise(nt, coord, 3.2, 4.0), 0.56, 0.66)
        col = _mix(nt, col, hexc(s.get('bruise', '#4e3a58')), _math(nt, 'MULTIPLY', bruise, 0.45))
        rot = _range(nt, _noise(nt, coord, 6.0, 5.0), 0.6, 0.7)
        col = _mix(nt, col, hexc(s.get('rot', '#7a7438')), _math(nt, 'MULTIPLY', rot, 0.3))
        veins = _veins(nt, coord, s.get('vein_scale', 9.0))
        vmask = _range(nt, _noise(nt, coord, 2.0, 2.0), 0.45, 0.6)
        col = _mix(nt, col, hexc(s.get('vein', '#2c2440')), _math(nt, 'MULTIPLY', _math(nt, 'MULTIPLY', veins, vmask),
                                                                   s.get('vein_amount', 0.55)))
    if 'tattoo' in s:
        w = _wave(nt, coord, s.get('tattoo_scale', 5.0), 'SPHERICAL', 'RINGS', distortion=14.0, detail=3.0)
        lines = _range(nt, _math(nt, 'ABSOLUTE', _math(nt, 'SUBTRACT', w, 0.5)), 0.16, 0.09)
        fill = _range(nt, _noise(nt, coord, 3.0, 3.0), 0.44, 0.48)
        cover = _range(nt, _noise(nt, coord, 1.2, 2.0), s.get('tattoo_cover', 0.36), s.get('tattoo_cover', 0.36) + 0.06)
        ink = _math(nt, 'MULTIPLY', _math(nt, 'MAXIMUM', lines, fill), cover)
        if s.get('munewari'):
            xyz = _node(nt, 'ShaderNodeSeparateXYZ')
            nt.links.new(coord, xyz.inputs[0])
            bare_x = _range(nt, _math(nt, 'ABSOLUTE', xyz.outputs['X']), 0.075, 0.055)
            front = _range(nt, xyz.outputs['Y'], -0.02, -0.06)
            ink = _math(nt, 'MULTIPLY', ink, _math(nt, 'SUBTRACT', 1.0, _math(nt, 'MULTIPLY', bare_x, front)))
        col = _mix(nt, col, hexc(s['tattoo']), _math(nt, 'MULTIPLY', ink, s.get('tattoo_amount', 1.0)))
        if 'tattoo2' in s:
            fill2 = _range(nt, _noise(nt, coord, 4.5, 2.0), 0.60, 0.64)
            col = _mix(nt, col, hexc(s['tattoo2']), _math(nt, 'MULTIPLY', _math(nt, 'MULTIPLY', fill2, cover), 0.7))
    if s.get('grime', 0) > 0:
        g = _range(nt, _noise(nt, coord, 9.0, 8.0, 0.65), 0.5, 0.78)
        col = _mix(nt, col, _mix(nt, col, hexc('#1a140e'), 0.45), _math(nt, 'MULTIPLY', g, s['grime'] * 0.6))

    for mk in s.get('marks', ()):  # painted spots at fixed points: eye rims, drool, wounds
        d = _node(nt, 'ShaderNodeVectorMath', operation='DISTANCE')
        nt.links.new(coord, d.inputs[0])
        d.inputs[1].default_value = mk['c']
        wob = _math(nt, 'MULTIPLY', _noise(nt, coord, 60.0, 2.0), mk['r'] * 0.6)
        dist = _math(nt, 'ADD', d.outputs['Value'], wob)
        k = _range(nt, dist, mk['r'], mk['r'] * 0.35)
        col = _mix(nt, col, hexc(mk['col']), _math(nt, 'MULTIPLY', k, mk.get('amt', 0.85)))

    wet = None
    if s.get('blood', 0) > 0:
        lo = 0.70 - 0.16 * s['blood']
        wet = _range(nt, _noise(nt, coord, 2.6, 7.0, 0.7), lo, lo + 0.04)
        drip = _range(nt, _noise(nt, coord, 22.0, 4.0), 0.75 - 0.05 * s['blood'], 0.77 - 0.05 * s['blood'])
        wet = _math(nt, 'MAXIMUM', wet, drip)
        col = _mix(nt, col, hexc(s.get('blood_col', '#4d0707')), wet)

    nt.links.new(col, bsdf.inputs['Base Color'])
    rough = _range(nt, _noise(nt, coord, 14.0, 3.0), 0.0, 1.0, s.get('rough', 0.75) - 0.08, s.get('rough', 0.75) + 0.08)
    if wet is not None:
        rough = _math(nt, 'SUBTRACT', rough, _math(nt, 'MULTIPLY', wet, s.get('rough', 0.75) - 0.12))
    nt.links.new(rough, bsdf.inputs['Roughness'])
    bsdf.inputs['Metallic'].default_value = s.get('metal', 0.0)
    if 'emit' in s:
        bsdf.inputs['Emission Color'].default_value = hexc(s['emit'])
        bsdf.inputs['Emission Strength'].default_value = s.get('emit_str', 1.0)
    else:
        bsdf.inputs['Emission Strength'].default_value = 0.0

    height, strength = _height(nt, coord, kind)
    if height is not None:
        bump = _node(nt, 'ShaderNodeBump')
        bump.inputs['Strength'].default_value = strength * s.get('bump', 1.0)
        bump.inputs['Distance'].default_value = 0.004
        nt.links.new(height, bump.inputs['Height'])
        nt.links.new(bump.outputs['Normal'], bsdf.inputs['Normal'])
    return m


# Surface type per material name, for the fine relief and colour detail.
KIND_BY_NAME = {
    'skin': 'skin', 'tat': 'skin',
    'jeans': 'cloth', 'flannel': 'cloth', 'overall': 'cloth', 'rags': 'cloth', 'pants': 'cloth', 'suit': 'cloth',
    'dress': 'cloth', 'jacket': 'cloth', 'bandana': 'cloth', 'pack': 'cloth', 'bedroll': 'cloth', 'carrier': 'cloth',
    'pouch': 'cloth',
    'vest': 'leather', 'boot': 'leather', 'belt': 'leather', 'sheath': 'leather', 'strap': 'leather', 'glove': 'leather',
    'mask': 'leather', 'pad': 'leather', 'tape': 'leather', 'wrap': 'cloth',
    'iron': 'metal', 'buckle': 'metal', 'metal': 'metal', 'guard': 'metal', 'lampring': 'metal', 'helmet': 'metal',
    'tank': 'metal', 'timer': 'metal', 'filter': 'metal',
    'hair': 'hair', 'beard': 'hair', 'brow': 'hair',
    'wound': 'flesh', 'sac': 'flesh', 'mouth': 'flesh', 'teeth': 'flesh', 'wood': 'wood', 'dyn': 'cloth',
}


def _rgb(nt, c):
    n = _node(nt, 'ShaderNodeRGB')
    n.outputs[0].default_value = c
    return n.outputs[0]


def _veins(nt, coord, scale):
    """Thin wandering lines (where a distorted noise crosses its middle): veins, cracks."""
    n = _noise(nt, coord, scale, 3.0, 0.5, distortion=0.6)
    return _range(nt, _math(nt, 'ABSOLUTE', _math(nt, 'SUBTRACT', n, 0.5)), 0.018, 0.0)


def _height(nt, coord, kind):
    """Relief for the normal map: (height socket, bump strength) or (None, 0)."""
    if kind == 'skin':
        pores = _noise(nt, coord, 260.0, 2.0)
        wrinkles = _noise(nt, coord, 30.0, 8.0, 0.65, distortion=0.4)
        veins = _veins(nt, coord, 9.0)
        h = _math(nt, 'ADD', _math(nt, 'ADD', _math(nt, 'MULTIPLY', pores, 0.25), _math(nt, 'MULTIPLY', wrinkles, 0.6)),
                  _math(nt, 'MULTIPLY', veins, 0.12))
        return h, 0.45
    if kind == 'flesh':
        return _noise(nt, coord, 70.0, 6.0, 0.7), 0.6
    if kind == 'cloth':
        wx = _wave(nt, coord, 260.0, 'X')
        wz = _wave(nt, coord, 260.0, 'Z')
        folds = _noise(nt, coord, 7.0, 4.0)
        h = _math(nt, 'ADD', _math(nt, 'MULTIPLY', _math(nt, 'ADD', wx, wz), 0.15), _math(nt, 'MULTIPLY', folds, 0.8))
        return h, 0.4
    if kind == 'leather':
        grain = _noise(nt, coord, 90.0, 6.0, 0.7)
        cracks = _veins(nt, coord, 30.0)
        return _math(nt, 'SUBTRACT', _math(nt, 'MULTIPLY', grain, 0.6), _math(nt, 'MULTIPLY', cracks, 0.4)), 0.45
    if kind == 'metal':
        dents = _noise(nt, coord, 12.0, 3.0)
        scratch = _range(nt, _noise(nt, coord, 400.0, 1.0), 0.62, 0.66)
        return _math(nt, 'SUBTRACT', _math(nt, 'MULTIPLY', dents, 0.5), _math(nt, 'MULTIPLY', scratch, 0.3)), 0.3
    if kind == 'hair':
        return _wave(nt, coord, 180.0, 'Z', distortion=6.0, detail=3.0), 0.55
    if kind == 'wood':
        return _wave(nt, coord, 90.0, 'Y', distortion=4.0, detail=2.0), 0.4
    return None, 0.0


# ---------------------------------------------------------------- geometry

class Part:
    """One mesh object under construction. bind = 'auto' (weights by distance to the
    bones), or a bone name (rigidly follows that bone)."""

    def __init__(self, name, bind='auto', subsurf=1, smooth=True, solidify=0.0):
        self.name = name
        self.bm = bmesh.new()
        self.mats = []
        self.bind = bind
        self.subsurf = subsurf
        self.smooth = smooth
        self.solidify = solidify

    def mi(self, m):
        if m not in self.mats:
            self.mats.append(m)
        return self.mats.index(m)

    def _paint(self, faces, mat):
        for f in faces:
            if callable(mat):
                c = f.calc_center_median()
                f.material_index = self.mi(mat(c))
            else:
                f.material_index = self.mi(mat)

    def finish(self):
        bm = self.bm
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        me = bpy.data.meshes.new(self.name)
        bm.to_mesh(me)
        bm.free()
        ob = link(bpy.data.objects.new(self.name, me))
        for m in self.mats:
            me.materials.append(get_mat(m))
        if self.solidify:
            mod = ob.modifiers.new('solid', 'SOLIDIFY')
            mod.thickness = self.solidify
            mod.offset = 1.0
        if self.subsurf:
            mod = ob.modifiers.new('sub', 'SUBSURF')
            mod.levels = self.subsurf
            mod.render_levels = self.subsurf
        if ob.modifiers:
            apply_modifiers(ob)
        me = ob.data
        me.polygons.foreach_set('use_smooth', [self.smooth] * len(me.polygons))
        ob['bind'] = self.bind or ''
        return ob



def apply_modifiers(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev)
    old = ob.data
    ob.modifiers.clear()
    ob.data = me
    bpy.data.meshes.remove(old)


def _frame(t, up=None):
    t = t.normalized()
    ref = V(up) if up is not None else (V((1, 0, 0)) if abs(t.z) > 0.7 else V((0, 0, 1)))
    u = ref - t * ref.dot(t)
    if u.length < 1e-6:
        u = V((0, 1, 0)) - t * t.y
    u.normalize()
    return t, u, t.cross(u)


def loft(P, pts, rx, ry=None, mat='skin', segs=12, cap0=True, cap1=True, up=None,
         off=None, prof=None, gap=None, jag0=0.0, jag1=0.0, seed=1):
    """Tube through pts with elliptical rings (rx along the frame's side axis, ry across).
    mat: name, or fn(t 0..1 along the tube, direction Vector) -> name.
    prof: fn(t, direction) -> radius multiplier. gap: fn(t, direction) -> True to leave a hole.
    jag0/jag1: ragged edge on the first/last ring (torn cloth)."""
    pts = [V(p) for p in pts]
    n = len(pts)
    ry = ry or rx
    rnd = random.Random(seed)
    rings, dirs = [], []
    for i, p in enumerate(pts):
        if i == 0:
            t = pts[1] - pts[0]
        elif i == n - 1:
            t = pts[-1] - pts[-2]
        else:
            t = (pts[i + 1] - pts[i]).normalized() + (pts[i] - pts[i - 1]).normalized()
        t, u, v = _frame(t, up)
        ring, rd = [], []
        tt = i / (n - 1)
        for k in range(segs):
            a = 2 * math.pi * k / segs
            d = u * math.cos(a) + v * math.sin(a)
            mlt = prof(tt, d) if prof else 1.0
            q = p + u * (rx[i] * math.cos(a) * mlt) + v * (ry[i] * math.sin(a) * mlt)
            if off is not None:
                q += V(off[i])
            if i == 0 and jag0:
                q -= t * (rnd.random() * jag0)
            if i == n - 1 and jag1:
                q += t * (rnd.random() * jag1)
            ring.append(P.bm.verts.new(q))
            rd.append(d)
        rings.append((ring, t))
        dirs.append(rd)

    faces = []
    for i in range(n - 1):
        r0, r1 = rings[i][0], rings[i + 1][0]
        for k in range(segs):
            k2 = (k + 1) % segs
            d = (dirs[i][k] + dirs[i][k2]).normalized()
            tm = (i + 0.5) / (n - 1)
            if gap and gap(tm, d):
                continue
            f = P.bm.faces.new((r0[k], r0[k2], r1[k2], r1[k]))
            f.material_index = P.mi(mat(tm, d) if callable(mat) else mat)
            faces.append(f)

    def cap(ring, t, tval, outward):
        c = sum((v.co for v in ring), V()) / len(ring)
        rad = sum(((v.co - c).length for v in ring)) / len(ring)
        cv = P.bm.verts.new(c + outward * t * rad * 0.45)
        for k in range(segs):
            k2 = (k + 1) % segs
            f = P.bm.faces.new((ring[k], ring[k2], cv))
            d = (ring[k].co - c).normalized()
            f.material_index = P.mi(mat(tval, d) if callable(mat) else mat)
            faces.append(f)

    if cap0:
        cap(rings[0][0], rings[0][1], 0.0, -1)
    if cap1:
        cap(rings[-1][0], rings[-1][1], 1.0, 1)
    return faces


def ellipsoid(P, c, r, mat='skin', segs=16, rings=10, rot=None, deform=None, cut=None):
    """Ellipsoid at c with radii r. deform(local unit-sphere co) -> new local co (before scaling).
    cut(local co) -> True deletes that face (open shells, caps)."""
    ret = bmesh.ops.create_uvsphere(P.bm, u_segments=segs, v_segments=rings, radius=1.0)
    verts = ret['verts']
    faces = list({f for v in verts for f in v.link_faces})
    if cut:
        dead = [f for f in faces if cut(f.calc_center_median())]
        bmesh.ops.delete(P.bm, geom=dead, context='FACES_ONLY')
        faces = [f for f in faces if f.is_valid]
        loose = [v for v in verts if v.is_valid and not v.link_faces]
        bmesh.ops.delete(P.bm, geom=loose, context='VERTS')
        verts = [v for v in verts if v.is_valid]
    R = rot.to_matrix() if rot is not None else Matrix.Identity(3)
    for v in verts:
        co = V(v.co)
        if deform:
            co = V(deform(co))
        co = V((co.x * r[0], co.y * r[1], co.z * r[2]))
        v.co = V(c) + R @ co
    P._paint(faces, mat)
    return faces


def box(P, c, size, mat='cloth', rot=None, bevel=0.0, bevel_segs=2):
    """Box of full size `size` centred on c, optionally with rounded (bevelled) edges."""
    before = set(P.bm.faces)
    ret = bmesh.ops.create_cube(P.bm, size=1.0)
    verts = ret['verts']
    R = rot.to_matrix() if rot is not None else Matrix.Identity(3)
    for v in verts:
        co = V((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
        v.co = V(c) + R @ co
    if bevel > 0:
        edges = list({e for v in verts for e in v.link_edges})
        bmesh.ops.bevel(P.bm, geom=edges + verts, offset=bevel, segments=bevel_segs,
                        affect='EDGES', profile=0.5)
    faces = [f for f in P.bm.faces if f not in before]
    P._paint(faces, mat)
    return faces


def cyl(P, p0, p1, r0, r1=None, mat='metal', segs=10, caps=True):
    p0, p1 = V(p0), V(p1)
    d = p1 - p0
    ret = bmesh.ops.create_cone(P.bm, cap_ends=caps, cap_tris=False, segments=segs,
                                radius1=r0, radius2=r0 if r1 is None else r1, depth=d.length)
    verts = ret['verts']
    q = V((0, 0, 1)).rotation_difference(d.normalized())
    mid = (p0 + p1) * 0.5
    for v in verts:
        v.co = mid + q @ v.co
    faces = list({f for v in verts for f in v.link_faces})
    P._paint(faces, mat)
    return faces


def tube(P, pts, r, mat='strap', segs=6):
    """Thin round strap / wire through pts."""
    return loft(P, pts, [r] * len(pts), [r] * len(pts), mat, segs=segs)


def band(P, pts, w, h, mat='strap', up=None):
    """Flat strap (w wide, h thick) through pts, e.g. a belt or harness."""
    return loft(P, pts, [h] * len(pts), [w] * len(pts), mat, segs=4, up=up,
                prof=None)


# ---------------------------------------------------------------- proportions

def make_joints(h=1.8, sw=0.17, hw=0.095, arm=1.0, leg=1.0, neck=1.0):
    """Bone head positions of a T-posed body, scaled from a 1.8 m reference.
    sw = shoulder half-width, hw = hip half-width, arm/leg/neck = length factors."""
    s = h / 1.8
    J = {}
    leg_z = 0.90 * leg
    J['Hips'] = V((0, 0, leg_z + 0.0))
    J['Spine'] = V((0, 0, leg_z + 0.10))
    J['Spine1'] = V((0, 0, leg_z + 0.22))
    J['Spine2'] = V((0, 0, leg_z + 0.34))
    top = leg_z + 0.58
    J['Neck'] = V((0, 0, top))
    J['Head'] = V((0, 0, top + 0.07 * neck))
    J['HeadTop'] = V((0, 0, top + 0.07 * neck + 0.27))
    J['HeadTopTip'] = V((0, 0, top + 0.07 * neck + 0.33))
    sz = top - 0.05
    for side, sx in (('Left', 1), ('Right', -1)):
        J[side + 'Shoulder'] = V((0.035 * sx, 0, sz))
        J[side + 'Arm'] = V((sw * sx, 0, sz))
        J[side + 'ForeArm'] = V(((sw + 0.28 * arm) * sx, 0, sz))
        J[side + 'Hand'] = V(((sw + 0.54 * arm) * sx, 0, sz))
        J[side + 'HandEnd'] = V(((sw + 0.72 * arm) * sx, 0, sz))
        J[side + 'UpLeg'] = V((hw * sx, 0, leg_z - 0.04))
        J[side + 'Leg'] = V((hw * sx, -0.005, 0.47 * leg))
        J[side + 'Foot'] = V((hw * sx, 0.02, 0.09))
        J[side + 'ToeBase'] = V((hw * sx, -0.10, 0.025))
        J[side + 'ToeEnd'] = V((hw * sx, -0.18, 0.025))
    for k in J:
        J[k] = J[k] * s
    J['_s'] = s
    return J


BONES = [
    # name, head key, tail key, parent
    ('Hips', 'Hips', 'Spine', None),
    ('Spine', 'Spine', 'Spine1', 'Hips'),
    ('Spine1', 'Spine1', 'Spine2', 'Spine'),
    ('Spine2', 'Spine2', 'Neck', 'Spine1'),
    ('Neck', 'Neck', 'Head', 'Spine2'),
    ('Head', 'Head', 'HeadTop', 'Neck'),
    ('HeadTop_End', 'HeadTop', 'HeadTopTip', 'Head'),  # leaf above the skull, like Mixamo rigs
]
for _s in ('Left', 'Right'):
    BONES += [
        (_s + 'Shoulder', _s + 'Shoulder', _s + 'Arm', 'Spine2'),
        (_s + 'Arm', _s + 'Arm', _s + 'ForeArm', _s + 'Shoulder'),
        (_s + 'ForeArm', _s + 'ForeArm', _s + 'Hand', _s + 'Arm'),
        (_s + 'Hand', _s + 'Hand', _s + 'HandEnd', _s + 'ForeArm'),
        (_s + 'UpLeg', _s + 'UpLeg', _s + 'Leg', 'Hips'),
        (_s + 'Leg', _s + 'Leg', _s + 'Foot', _s + 'UpLeg'),
        (_s + 'Foot', _s + 'Foot', _s + 'ToeBase', _s + 'Leg'),
        (_s + 'ToeBase', _s + 'ToeBase', _s + 'ToeEnd', _s + 'Foot'),
    ]


def build_armature(J, name='Armature'):
    arm = bpy.data.armatures.new(name)
    ob = link(bpy.data.objects.new(name, arm))
    select_only(ob)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = {}
    for bname, hk, tk, parent in BONES:
        b = arm.edit_bones.new(bname)
        b.head = J[hk]
        b.tail = J[tk]
        b.roll = 0.0
        if parent:
            b.parent = eb[parent]
            b.use_connect = False
        eb[bname] = b
    bpy.ops.object.mode_set(mode='OBJECT')
    return ob


# ---------------------------------------------------------------- skinning

def _seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
    return (a + ab * t - p).length


def skin_weights(ob, J, falloff=0.035, exclude=()):
    """Smooth automatic weights by distance to bone segments: each vertex follows its
    nearest bones, blended where two are almost equally close (the joints)."""
    segs = [(n, J[h], J[t]) for n, h, t, _ in BONES
            if n not in exclude and not n.endswith('ToeBase') and not n.endswith('_End')]
    segs.append(('LeftToeBase', J['LeftToeBase'], J['LeftToeEnd']))
    segs.append(('RightToeBase', J['RightToeBase'], J['RightToeEnd']))
    groups = {n: (ob.vertex_groups.get(n) or ob.vertex_groups.new(name=n)) for n, _, _ in segs}
    s = J['_s']
    for v in ob.data.vertices:
        p = v.co
        cands = []
        for n, a, b in segs:
            if p.x > 0.03 * s and n.startswith('Right'):
                continue
            if p.x < -0.03 * s and n.startswith('Left'):
                continue
            cands.append((_seg_dist(p, a, b), n))
        cands.sort()
        dmin = cands[0][0]
        ws = [(math.exp(-((d - dmin) / (falloff * s)) ** 2), n) for d, n in cands[:3]]
        tot = sum(w for w, _ in ws)
        for w, n in ws:
            if w / tot > 0.02:
                groups[n].add([v.index], w / tot, 'REPLACE')


def rigid_weights(ob, bone):
    g = ob.vertex_groups.get(bone) or ob.vertex_groups.new(name=bone)
    g.add([v.index for v in ob.data.vertices], 1.0, 'REPLACE')


# ---------------------------------------------------------------- assembly

def assemble(name, parts, J, extra_objects=()):
    """Weights every part, joins them into one mesh and parents it to a new armature.
    extra_objects: (object, bone) pairs kept as separate meshes parented to a bone."""
    arm = build_armature(J, name + '_Rig')
    obs = []
    for ob in parts:
        bind = ob.get('bind', 'auto')
        if bind == 'auto':
            skin_weights(ob, J)
        else:
            rigid_weights(ob, bind)
        obs.append(ob)
    select_only(*obs)
    bpy.ops.object.join()
    body = bpy.context.view_layer.objects.active
    body.name = name
    body.data.name = name
    body.parent = arm
    mod = body.modifiers.new('Armature', 'ARMATURE')
    mod.object = arm
    for ob, bone in extra_objects:
        mw = ob.matrix_world.copy()
        ob.parent = arm
        ob.parent_type = 'BONE'
        ob.parent_bone = bone
        ob.matrix_world = mw
    return arm, body


# ---------------------------------------------------------------- baking

def bake(body, out_dir, name, size=2048, ao_samples=16, ao_strength=0.6):
    """Unwraps the joined mesh and bakes the texture set Unity uses:
      _Albedo (colour with ambient occlusion multiplied in), _Normal (tangent-space relief),
      _MetalSmooth (R metallic, A smoothness, for URP Lit) and _Emission (glowing parts).
    Returns the list of saved paths."""
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = ao_samples
    scene.render.bake.margin = 8
    scene.render.bake.use_clear = True
    if scene.world is None:
        scene.world = bpy.data.worlds.new('BakeWorld')
    scene.world.light_settings.distance = 0.07  # AO reach in metres: creases, not whole limbs

    select_only(body)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.003, area_weight=0.0)
    bpy.ops.object.mode_set(mode='OBJECT')

    def target(img_name, non_color=False, alpha=False):
        img = bpy.data.images.new(img_name, size, size, alpha=alpha)
        if non_color:
            img.colorspace_settings.name = 'Non-Color'
        for slot in body.material_slots:
            nt = slot.material.node_tree
            node = nt.nodes.get('_bake') or nt.nodes.new('ShaderNodeTexImage')
            node.name = '_bake'
            node.image = img
            nt.nodes.active = node
        return img

    def bsdf_of(slot):
        return slot.material.node_tree.nodes.get('Principled BSDF')

    alb = target(name + '_Albedo')
    bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'})
    ao = target(name + '_AO', non_color=True)
    bpy.ops.object.bake(type='AO')
    nrm = target(name + '_Normal', non_color=True)
    bpy.ops.object.bake(type='NORMAL', normal_space='TANGENT')
    rough = target(name + '_Rough', non_color=True)
    bpy.ops.object.bake(type='ROUGHNESS')

    # Emission and metallic both go through the emission pass: save, swap, bake, restore.
    saved = {}
    for slot in body.material_slots:
        b = bsdf_of(slot)
        saved[slot.material.name] = (tuple(b.inputs['Emission Color'].default_value), b.inputs['Emission Strength'].default_value)
    emi = None
    if any(v[1] > 0 for v in saved.values()):
        for slot in body.material_slots:
            b = bsdf_of(slot)
            if saved[slot.material.name][1] > 0:
                b.inputs['Emission Strength'].default_value = 1.0  # unclipped colour; Unity scales it up
        emi = target(name + '_Emission')
        bpy.ops.object.bake(type='EMIT')
    for slot in body.material_slots:
        b = bsdf_of(slot)
        m = b.inputs['Metallic'].default_value
        b.inputs['Emission Color'].default_value = (m, m, m, 1.0)
        b.inputs['Emission Strength'].default_value = 1.0
    metal = target(name + '_Metal', non_color=True)
    bpy.ops.object.bake(type='EMIT')
    for slot in body.material_slots:
        b = bsdf_of(slot)
        c, st = saved[slot.material.name]
        b.inputs['Emission Color'].default_value = c
        b.inputs['Emission Strength'].default_value = st

    n = size * size * 4

    def px(img):
        a = np.empty(n, dtype=np.float32)
        img.pixels.foreach_get(a)
        return a.reshape(-1, 4)

    a, o = px(alb), px(ao)
    a[:, :3] *= 1.0 - ao_strength * (1.0 - o[:, :1])
    alb.pixels.foreach_set(a.ravel())

    ms = bpy.data.images.new(name + '_MetalSmooth', size, size, alpha=True)
    ms.colorspace_settings.name = 'Non-Color'
    mpx, rpx = px(metal), px(rough)
    out = np.zeros((size * size, 4), dtype=np.float32)
    out[:, 0] = mpx[:, 0]
    out[:, 1] = mpx[:, 0]
    out[:, 2] = mpx[:, 0]
    out[:, 3] = np.clip(1.0 - rpx[:, 0], 0.0, 1.0)
    ms.pixels.foreach_set(out.ravel())
    ms.alpha_mode = 'STRAIGHT'

    os.makedirs(out_dir, exist_ok=True)
    paths = []
    for img in (alb, nrm, ms, emi):
        if img is None:
            continue
        p = os.path.join(out_dir, img.name + '.png')
        img.filepath_raw = p
        img.file_format = 'PNG'
        img.save()
        paths.append(p)

    # One baked material on the whole mesh, so the export and the preview use the textures.
    final = bpy.data.materials.new(name + '_Baked')
    final.use_nodes = True
    nt = final.node_tree
    bsdf = nt.nodes['Principled BSDF']
    t = nt.nodes.new('ShaderNodeTexImage')
    t.image = alb
    nt.links.new(t.outputs['Color'], bsdf.inputs['Base Color'])
    nt_img = nt.nodes.new('ShaderNodeTexImage')
    nt_img.image = nrm
    nm = nt.nodes.new('ShaderNodeNormalMap')
    nt.links.new(nt_img.outputs['Color'], nm.inputs['Color'])
    nt.links.new(nm.outputs['Normal'], bsdf.inputs['Normal'])
    r_img = nt.nodes.new('ShaderNodeTexImage')
    r_img.image = rough
    nt.links.new(r_img.outputs['Color'], bsdf.inputs['Roughness'])
    if emi is not None:
        e = nt.nodes.new('ShaderNodeTexImage')
        e.image = emi
        nt.links.new(e.outputs['Color'], bsdf.inputs['Emission Color'])
        bsdf.inputs['Emission Strength'].default_value = 4.0
    body.data.materials.clear()
    body.data.materials.append(final)
    for p in body.data.polygons:
        p.material_index = 0
    return paths


# ---------------------------------------------------------------- export & preview

def export_fbx(path, arm, body, extras=()):
    """Exports facing Unity's forward (+Z). Characters are built facing -Y, which lands on
    Unity's -Z, so the rig is turned half a circle for the export only (the turn is stored on
    the rig node, under the Animator, so the Humanoid rest pose faces forward)."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    select_only(arm, body, *extras)
    arm.rotation_euler = (0.0, 0.0, math.pi)
    bpy.context.view_layer.update()
    try:
        bpy.ops.export_scene.fbx(
            filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'},
            apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
            axis_forward='-Z', axis_up='Y', use_mesh_modifiers=True, mesh_smooth_type='FACE',
            add_leaf_bones=False, use_armature_deform_only=False, bake_anim=False,
            path_mode='STRIP', embed_textures=False)
    finally:
        arm.rotation_euler = (0.0, 0.0, 0.0)
        bpy.context.view_layer.update()


def pose_arms_down(arm, J, angle=68.0, fore=14.0):
    """Preview-only relaxed pose (does not touch the exported T-pose)."""
    select_only(arm)
    bpy.ops.object.mode_set(mode='POSE')

    def rot(bone, deg, axis):
        pb = arm.pose.bones[bone]
        h = pb.head.copy()
        R = Matrix.Rotation(math.radians(deg), 4, axis)
        pb.matrix = Matrix.Translation(h) @ R @ Matrix.Translation(-h) @ pb.matrix
        bpy.context.view_layer.update()

    for side, sx in (('Left', 1), ('Right', -1)):
        rot(side + 'Arm', angle * sx, 'Y')
        rot(side + 'ForeArm', -fore, 'X')
    bpy.ops.object.mode_set(mode='OBJECT')


def render_preview(path, height, res=(640, 900), yaw=-28.0, focus=None, dist=None):
    scene = bpy.context.scene
    world = bpy.data.worlds.new('W')
    world.use_nodes = True
    world.node_tree.nodes['Background'].inputs['Color'].default_value = hexc('#1d2430')
    world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.6
    scene.world = world

    def light(kind, energy, loc, rot, size=1.0, color='#ffffff'):
        l = bpy.data.lights.new('L', kind)
        l.energy = energy
        l.color = hexc(color)[:3]
        if kind == 'AREA':
            l.size = size
        o = link(bpy.data.objects.new('L', l))
        o.location = loc
        o.rotation_euler = [math.radians(a) for a in rot]
        return o

    light('SUN', 5.5, (0, 0, 5), (50, 0, -30), color='#fff2dd')
    light('AREA', 250, (2.2, -2.0, 1.6), (65, 0, 48), 2.0, '#9fb8ff')
    light('AREA', 400, (-1.5, 2.5, 2.4), (-60, 0, -150), 1.5, '#ffd0a0')

    cam_data = bpy.data.cameras.new('C')
    cam_data.lens = 50
    cam = link(bpy.data.objects.new('C', cam_data))
    dist = dist or height * 1.75
    a = math.radians(yaw)
    target = V((0, 0, focus if focus is not None else height * 0.5))
    cam.location = target + V((math.sin(a) * dist, -math.cos(a) * dist, height * 0.05))
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scene.camera = cam

    floor_me = bpy.data.meshes.new('floor')
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=4)
    bm.to_mesh(floor_me)
    bm.free()
    floor = link(bpy.data.objects.new('floor', floor_me))
    fm = bpy.data.materials.new('floor')
    fm.use_nodes = True
    fm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = hexc('#2a2d33')
    floor_me.materials.append(fm)

    scene.view_settings.view_transform = 'Standard'
    scene.view_settings.look = 'None'
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.film_transparent = False
    scene.render.image_settings.file_format = 'PNG'
    scene.render.filepath = path
    try:
        scene.render.engine = 'BLENDER_EEVEE'
        bpy.ops.render.render(write_still=True)
    except Exception as e:  # no GPU context: fall back to Cycles
        print('EEVEE preview failed, using Cycles:', e)
        scene.render.engine = 'CYCLES'
        scene.cycles.samples = 48
        bpy.ops.render.render(write_still=True)
