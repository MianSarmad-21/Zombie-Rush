"""Fortress Haven base (2026-11-17): the Meshy "Fortress Haven" model made ready to replace the HQ hall.

    blender -b --factory-startup -P Blender/fortress_base.py -- [--preview DIR] [--no-bake] [--write]

Source: Blender/Source/FortressHaven/Meshy_AI_Fortress_Haven_20261117_texture.{fbx,png} (kept untouched).
Output: Assets/Models/2026-11-17_FortressHaven/ (only with --write)
  FortressHaven.fbx      meshes: Fortress (Meshy atlas), Room (walk-in hall under the deck), Lamp_## (ceiling lamps)
  Fortress_Albedo.png    Meshy colour upscaled 2048 -> 4096 (Catmull-Rom), clarity, unsharp mask, a little
                         saturation, soft baked AO in the cavities
  Fortress_Normal.png    detail normals from the colour's high-pass height (UV-island edges masked out)
  Fortress_Occlusion.png Cycles AO bake (fortress + room together)
  Fortress_MetalSmooth.png  metallic 0 (R), smoothness from the colour (A)
  Room_<Wall|Floor|Ceiling|Steel>_<Albedo|Normal|MetalSmooth>.png  tiling textures made here (numpy), no downloads
  FortressHaven.json     Unity-local points (metres, Unity axes) for the Unity tool: room box, doors, gates,
                         lamps, outside flood-light spots, yard floor height (0)

What the script does to the model:
  - scales it to WIDTH metres across and sinks it so the yard floor (top of Meshy's ground slab) is at 0;
  - cuts the two gate leaves out of the outer wall (the gates face +X / -X in Blender);
  - clears everything inside the room box under the deck (stilts, slanted panels, stair feet) after bisecting
    the mesh on the box planes, then builds a clean room shell there: walls with a door facing each gate,
    floor, ceiling, steel beams, corner pillars, skirting, door frames and hanging lamps.
The Unity side is FortressBaseTools (marker Temp/ZR_RunFortress).
"""
import json
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
WRITE = "--write" in argv
BAKE = "--no-bake" not in argv
MESHY_PAINT = "--meshy-paint" in argv      # old look: Meshy's enhanced atlas instead of the class retexture
PREVIEW = argv[argv.index("--preview") + 1] if "--preview" in argv else None

PROJECT = "C:/Mian Sarmad/Unity/Zombie-Rush/Zombie Rush/"
SRC = PROJECT + "Blender/Source/FortressHaven/Meshy_AI_Fortress_Haven_20261117_texture"
OUT = PROJECT + "Assets/Models/2026-11-17_FortressHaven/"

# ---- settings (model measured at 32 m wide: slab top 0.99, deck underside ~5.0, gates y -2.6..1.4 at x +-14.9)
WIDTH = 36.0                      # metres across (old hall 30 x 20 m; compound ring wall about 26 m from centre)
REF = 32.0                        # the width the numbers below were measured at
ROOM = dict(x0=-10.0, x1=8.5, y0=-9.0, y1=8.0)   # room box at REF scale (Blender XY), under the deck
GATE = dict(y0=-2.7, y1=1.5, xin=13.6, z1=3.15)  # gate leaves at REF scale (z1 = top of the leaves, slab-relative 0.99)
DOOR_W, DOOR_H = 3.6, 3.0         # room doors (metres, final scale), centred on the gates' y
WALL_T = 0.3
ALBEDO_SIZE = 4096
AO_SIZE = 2048


def log(*a):
    print("[fort]", *a, flush=True)


# ------------------------------------------------------------------ image helpers (numpy)

def blur(img, r):
    if r < 1: return img
    out = img
    for _ in range(3):
        for ax in (0, 1):
            pad = [(r + 1, r) if i == ax else (0, 0) for i in range(out.ndim)]
            c = np.cumsum(np.pad(out, pad, mode="edge"), axis=ax)
            hi = np.take(c, np.arange(2 * r + 1, c.shape[ax]), axis=ax)
            lo = np.take(c, np.arange(0, c.shape[ax] - 2 * r - 1), axis=ax)
            out = (hi - lo) / (2 * r + 1)
    return out


def wrap_blur(img, r):
    """Box blur x3 that wraps round the edges (tiling textures stay tileable)."""
    if r < 1: return img
    out = img
    for _ in range(3):
        for ax in (0, 1):
            acc = np.zeros_like(out)
            for k in range(-r, r + 1): acc += np.roll(out, k, axis=ax)
            out = acc / (2 * r + 1)
    return out


def upscale2(img):
    """x2 Catmull-Rom upscale (separable). Output pixel j samples source position j/2 - 0.25."""
    def up_axis(a, ax):
        n = a.shape[ax]
        def at(i): return np.take(a, np.clip(i, 0, n - 1), axis=ax)
        def cr(i, t):          # value between source i and i+1 at fraction t
            p0, p1, p2, p3 = at(i - 1), at(i), at(i + 1), at(i + 2)
            return 0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3)
        i = np.arange(n)
        ev = cr(i - 1, 0.75)   # j = 2i   -> source i - 0.25
        od = cr(i, 0.25)       # j = 2i+1 -> source i + 0.25
        shape = list(a.shape); shape[ax] = 2 * n
        out = np.empty(shape, a.dtype)
        se = [slice(None)] * a.ndim; se[ax] = slice(0, None, 2)
        so = [slice(None)] * a.ndim; so[ax] = slice(1, None, 2)
        out[tuple(se)] = ev; out[tuple(so)] = od
        return out
    return np.clip(up_axis(up_axis(img, 0), 1), 0, 1)


def lum(rgb):
    return rgb[..., 0] * 0.2126 + rgb[..., 1] * 0.7152 + rgb[..., 2] * 0.0722


def normal_from_height(h, strength, wrap=False):
    if wrap:
        dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5
        dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5
    else:
        dx = np.gradient(h, axis=1); dy = np.gradient(h, axis=0)
    # rows run top-down (row + 1 = v - 1), so the green channel takes +d/drow to point up the slope in UV space
    nx, ny, nz = -dx * strength, dy * strength, np.ones_like(h)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.stack([nx / l * 0.5 + 0.5, ny / l * 0.5 + 0.5, nz / l * 0.5 + 0.5], -1)


def save(arr, path, srgb=True):
    """arr: H x W (grey) or H x W x 3/4, rows top-down (numpy image order)."""
    h, w = arr.shape[:2]
    if arr.ndim == 2: arr = np.stack([arr, arr, arr], -1)
    if arr.shape[2] == 3: arr = np.concatenate([arr, np.ones((h, w, 1), arr.dtype)], -1)
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=True)
    img.colorspace_settings.name = "sRGB" if srgb else "Non-Color"
    img.pixels.foreach_set(np.ascontiguousarray(np.clip(arr[::-1], 0, 1)).astype(np.float32).ravel())
    img.filepath_raw = path; img.file_format = "PNG"; img.save()
    bpy.data.images.remove(img)
    log("saved", path.replace(PROJECT, ""))


def load(path):
    img = bpy.data.images.load(path)
    w, h = img.size
    a = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)[::-1]
    bpy.data.images.remove(img)
    return a


# ------------------------------------------------------------------ tiling texture maker

RNG = np.random.default_rng(1117)


def fbm(n, beta=2.0, lo=2, seed=None):
    """Tileable fractal noise in 0..1 (FFT-filtered white noise)."""
    rng = np.random.default_rng(seed) if seed is not None else RNG
    f = np.fft.fftfreq(n) * n
    fx, fy = np.meshgrid(f, f)
    r = np.sqrt(fx * fx + fy * fy); r[0, 0] = 1
    amp = 1.0 / r ** (beta / 2); amp[r < lo] = 0
    spec = np.fft.fft2(rng.standard_normal((n, n))) * amp
    out = np.real(np.fft.ifft2(spec))
    out -= out.min(); out /= max(out.max(), 1e-6)
    return out


def streaks(n, seed):
    """Vertical run-down stains, tileable."""
    rng = np.random.default_rng(seed)
    base = rng.random((n // 16, n))
    base = np.repeat(base, 16, axis=0)
    col = wrap_blur(base, 3)
    v = fbm(n, 1.6, 2, seed + 1)
    s = np.clip((col - 0.62) * 4, 0, 1) * (0.4 + 0.6 * v)
    return wrap_blur(s, 2)


def tex_wall(n=1024):
    """Bunker wall: poured concrete with form-tie holes and board seams up top, olive paint band below with
    chips, grime at the floor and run-down stains. v=0 is the floor, v=1 the ceiling (UVs: 1 tile = room height)."""
    yy, xx = np.mgrid[0:n, 0:n] / n
    v = 1.0 - yy                                    # image row 0 = top = ceiling
    big, mid, fine = fbm(n, 2.4, 2, 1), fbm(n, 1.8, 6, 2), fbm(n, 1.0, 40, 3)
    conc = np.stack([0.38, 0.36, 0.33]) * (0.7 + 0.45 * big[..., None]) * (0.9 + 0.2 * fine[..., None])
    paint = np.stack([0.20, 0.24, 0.18]) * (0.85 + 0.25 * mid[..., None])
    band = 0.33 + 0.004 * (fbm(n, 1.2, 30, 9) - 0.5)          # paint up to a third of the wall height
    chips = (fbm(n, 1.4, 8, 4) > 0.72) & (np.abs(v - band) < 0.12 + 0.1 * fine)
    in_paint = (v < band) & ~chips
    col = np.where(in_paint[..., None], paint, conc)
    stripe = np.abs(v - band) < 0.008                           # dark line on the paint edge
    col = np.where(stripe[..., None], col * 0.45, col)
    # formwork: board seams every 1/6 of the height above the paint, form-tie holes on a grid
    seam = (np.abs(((v * 6) % 1.0) - 0.5) > 0.485) & (v > band)
    col = np.where(seam[..., None], col * 0.7, col)
    tx, ty = (xx * 4) % 1.0 - 0.5, (v * 6) % 1.0 - 0.5
    hole = (tx * tx + ty * ty < 0.0016) & (v > band + 0.03)
    col = np.where(hole[..., None], col * 0.35, col)
    # stains and grime
    st = streaks(n, 5)
    col = col * (1 - 0.35 * st[..., None])
    grime = np.clip(1 - v / 0.12, 0, 1) ** 1.5 * (0.6 + 0.4 * mid)
    col = col * (1 - 0.45 * grime[..., None]) + np.stack([0.08, 0.07, 0.05]) * 0.4 * grime[..., None]
    h = 0.5 * big + 0.25 * fine - 0.6 * seam - 1.2 * hole - 0.3 * chips + 0.15 * in_paint
    nrm = normal_from_height(wrap_blur(h, 1), 3.0, wrap=True)
    smooth = np.where(in_paint, 0.32, 0.14) - 0.1 * st - 0.05 * grime
    return col, nrm, smooth


def tex_floor(n=1024):
    """Sealed concrete floor: slab joints (1 tile = 4 m, joint every 2 m), oil stains, scuffs, a hazard stripe-free
    plain floor (markings come from props)."""
    yy, xx = np.mgrid[0:n, 0:n] / n
    big, mid, fine = fbm(n, 2.4, 2, 11), fbm(n, 1.8, 6, 12), fbm(n, 0.9, 60, 13)
    col = np.stack([0.27, 0.265, 0.25]) * (0.75 + 0.4 * big[..., None]) * (0.92 + 0.16 * fine[..., None])
    joint = (np.abs((xx * 2) % 1.0 - 0.5) > 0.494) | (np.abs((yy * 2) % 1.0 - 0.5) > 0.494)
    col = np.where(joint[..., None], col * 0.45, col)
    oil = np.clip((fbm(n, 2.8, 2, 14) - 0.68) * 5, 0, 1)
    col = col * (1 - 0.55 * oil[..., None])
    scuff = np.clip((fbm(n, 1.2, 20, 15) - 0.6) * 3, 0, 1) * 0.15
    col = col + scuff[..., None]
    h = 0.4 * big + 0.2 * fine - 0.8 * joint
    nrm = normal_from_height(wrap_blur(h, 1), 2.5, wrap=True)
    smooth = 0.22 + 0.35 * oil - 0.1 * scuff
    return col, nrm, smooth


def tex_ceiling(n=1024):
    """Corrugated steel deck (1 tile = 2 m, ribs along u), dark paint with rust bleeding along the ribs."""
    yy, xx = np.mgrid[0:n, 0:n] / n
    rib = 0.5 + 0.5 * np.sin(xx * math.tau * 10)
    mid, fine = fbm(n, 1.8, 6, 21), fbm(n, 1.0, 40, 22)
    paint = np.stack([0.17, 0.18, 0.17]) * (0.85 + 0.3 * mid[..., None])
    rust = np.clip((fbm(n, 2.0, 4, 23) - 0.55) * 3 + (1 - rib) * 0.4 - 0.3, 0, 1)
    col = paint * (1 - rust[..., None]) + np.stack([0.30, 0.16, 0.08]) * (0.7 + 0.5 * fine[..., None]) * rust[..., None]
    col *= (0.75 + 0.25 * rib)[..., None]
    nrm = normal_from_height(rib * 6 + fine * 0.5, 2.0, wrap=True)
    smooth = 0.35 - 0.25 * rust
    return col, nrm, smooth


def tex_steel(n=512):
    """Painted structural steel (beams, pillars, frames): worn dark olive with bare-metal edges and rust spots."""
    mid, fine = fbm(n, 1.8, 4, 31), fbm(n, 1.0, 30, 32)
    col = np.stack([0.16, 0.18, 0.14]) * (0.8 + 0.4 * mid[..., None])
    worn = np.clip((fbm(n, 1.4, 10, 33) - 0.66) * 4, 0, 1)
    rust = np.clip((fbm(n, 2.2, 3, 34) - 0.7) * 4, 0, 1)
    col = col * (1 - worn[..., None]) + np.stack([0.42, 0.42, 0.40])[None, None] * worn[..., None]
    col = col * (1 - rust[..., None]) + np.stack([0.32, 0.17, 0.08]) * rust[..., None]
    col *= (0.9 + 0.2 * fine[..., None])
    nrm = normal_from_height(fine * 0.6 - rust * 0.5, 2.0, wrap=True)
    metal = 0.15 + 0.7 * worn
    smooth = 0.3 + 0.25 * worn - 0.2 * rust
    return col, nrm, smooth, metal


# ------------------------------------------------------------------ scene helpers

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def material(name, base_tex=None, color=(0.5, 0.5, 0.5), emit=None, rough=0.8, metal=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Roughness"].default_value = rough; b.inputs["Metallic"].default_value = metal
    if base_tex is not None:
        t = m.node_tree.nodes.new("ShaderNodeTexImage"); t.image = base_tex
        m.node_tree.links.new(t.outputs["Color"], b.inputs["Base Color"])
    else:
        b.inputs["Base Color"].default_value = (*color, 1)
    if emit:
        b.inputs["Emission Color"].default_value = (*emit[:3], 1); b.inputs["Emission Strength"].default_value = emit[3]
    return m


def image_from(arr, name):
    h, w = arr.shape[:2]
    if arr.shape[2] == 3: arr = np.concatenate([arr, np.ones((h, w, 1), arr.dtype)], -1)
    img = bpy.data.images.new(name, w, h, alpha=True)
    img.pixels.foreach_set(np.ascontiguousarray(np.clip(arr[::-1], 0, 1)).astype(np.float32).ravel())
    img.pack()
    return img


class Builder:
    """Collects quads with per-face material and planar UVs (metres / tile)."""
    def __init__(self, name):
        self.bm = bmesh.new(); self.uv = self.bm.loops.layers.uv.new("UVMap"); self.name = name

    def quad(self, pts, mat, tile, uv_axes):
        vs = [self.bm.verts.new(p) for p in pts]
        f = self.bm.faces.new(vs); f.material_index = mat
        ua, va = uv_axes
        for l in f.loops:
            co = l.vert.co
            l[self.uv].uv = (co.dot(ua) / tile[0], co.dot(va) / tile[1])
        return f

    def box(self, lo, hi, mat, tile, skip=()):
        """Closed box (outward faces), world-space UVs per face direction."""
        x0, y0, z0 = lo; x1, y1, z1 = hi
        X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
        faces = {
            "+x": ([(x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1)], (Y, Z)),
            "-x": ([(x0, y1, z0), (x0, y0, z0), (x0, y0, z1), (x0, y1, z1)], (-Y, Z)),
            "+y": ([(x1, y1, z0), (x0, y1, z0), (x0, y1, z1), (x1, y1, z1)], (-X, Z)),
            "-y": ([(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)], (X, Z)),
            "+z": ([(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)], (X, Y)),
            "-z": ([(x0, y1, z0), (x1, y1, z0), (x1, y0, z0), (x0, y0, z0)], (X, -Y)),
        }
        for k, (pts, axes) in faces.items():
            if k not in skip: self.quad(pts, mat, tile, axes)

    def finish(self, mats):
        me = bpy.data.meshes.new(self.name)
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts[:], dist=0.0005)
        self.bm.to_mesh(me); self.bm.free()
        for m in mats: me.materials.append(m)
        o = bpy.data.objects.new(self.name, me); bpy.context.scene.collection.objects.link(o)
        return o


# ------------------------------------------------------------------ the fortress

def import_fortress():
    bpy.ops.import_scene.fbx(filepath=SRC + ".fbx")
    o = [o for o in bpy.context.scene.objects if o.type == "MESH"][0]
    for x in list(bpy.context.scene.objects):
        if x != o: bpy.data.objects.remove(x, do_unlink=True)
    bpy.context.view_layer.objects.active = o; o.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    k = WIDTH / max(o.dimensions.x, o.dimensions.y)
    me = o.data
    zmin = min(v.co.z for v in me.vertices)
    for v in me.vertices: v.co *= k
    zmin *= k
    # yard floor = top of the ground slab: highest surface below 1.3 m (REF) on vertical rays over the yard
    bvh = BVHTree.FromObject(o, bpy.context.evaluated_depsgraph_get())
    tops = []
    for x in np.linspace(-0.4, 0.4, 9) * WIDTH:
        for y in np.linspace(-0.4, 0.4, 9) * WIDTH:
            z = zmin - 1; best = None
            for _ in range(6):
                loc, n, i, d = bvh.ray_cast(Vector((x, y, z)), Vector((0, 0, 1)), 50)
                if loc is None or loc.z - zmin > 1.3 * WIDTH / REF: break
                best = loc.z; z = loc.z + 0.01
            if best is not None: tops.append(best)
    slab = float(np.median(tops))
    for v in me.vertices: v.co.z -= slab
    o.name = "Fortress"; me.name = "Fortress"
    xs = np.array([v.co.x for v in me.vertices]); ys = np.array([v.co.y for v in me.vertices])
    log(f"scale x{k:.2f} (model -> {WIDTH} m), slab top {slab - zmin:.2f} m above the bottom, sunk; "
        f"x {xs.min():.1f}..{xs.max():.1f} y {ys.min():.1f}..{ys.max():.1f}, max radius {np.sqrt(xs**2 + ys**2).max():.1f} m, "
        f"height {max(v.co.z for v in me.vertices):.1f} m")
    return o, WIDTH / REF


def bisect(bm, co, no):
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, dist=0.0001, plane_co=co, plane_no=no)


def cut_box(o, lo, hi, label):
    """Bisects the mesh on the box's planes and deletes every face whose centre is inside the box."""
    bm = bmesh.new(); bm.from_mesh(o.data)
    for ax in range(3):
        for val in (lo[ax], hi[ax]):
            co = Vector((0, 0, 0)); co[ax] = val
            no = Vector((0, 0, 0)); no[ax] = 1
            bisect(bm, co, no)
    bm.faces.ensure_lookup_table()
    gone = [f for f in bm.faces if all(lo[i] < f.calc_center_median()[i] < hi[i] for i in range(3))]
    bmesh.ops.delete(bm, geom=gone, context="FACES")
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.to_mesh(o.data); bm.free()
    log(f"{label}: cut {len(gone)} faces inside {tuple(round(v, 1) for v in lo)} .. {tuple(round(v, 1) for v in hi)}")


def deck_underside(o, x0, x1, y0, y1):
    """Lowest surface above 2 m over the room area (the deck's underside) - ignores the thin bits we cut anyway
    by taking the 20th percentile of the per-ray lowest hits."""
    bvh = BVHTree.FromObject(o, bpy.context.evaluated_depsgraph_get())
    hits = []
    for x in np.linspace(x0 + 1, x1 - 1, 12):
        for y in np.linspace(y0 + 1, y1 - 1, 12):
            loc, n, i, d = bvh.ray_cast(Vector((x, y, 2.0)), Vector((0, 0, 1)), 30)
            if loc is not None: hits.append(loc.z)
    hits = np.array(hits)
    return float(np.percentile(hits, 50)), hits


def build_room(k, ceil, gate_y):
    """Room shell under the deck: walls (outer + inner face) with a door to each gate, floor, ceiling, beams,
    pillars, skirting, door frames, lamps. Returns (room object, lamp objects, info)."""
    x0, x1 = ROOM["x0"] * k, ROOM["x1"] * k
    y0, y1 = ROOM["y0"] * k, ROOM["y1"] * k
    H = ceil
    T = WALL_T
    MW, MF, MC, MS = 0, 1, 2, 3                   # wall, floor, ceiling, steel
    b = Builder("Room")
    X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
    wall_tile = (H, H)                           # wall texture: one tile = room height (paint band at the bottom)
    dy0, dy1 = gate_y - DOOR_W / 2, gate_y + DOOR_W / 2

    def face(p0, p1, z0, z1, facing):
        """Vertical quad from XY point p0 to p1, z0..z1, wound so its normal points along `facing`."""
        pts = [(p0[0], p0[1], z0), (p1[0], p1[1], z0), (p1[0], p1[1], z1), (p0[0], p0[1], z1)]
        e1 = Vector(pts[1]) - Vector(pts[0]); e2 = Vector(pts[3]) - Vector(pts[0])
        if e1.cross(e2).dot(facing) < 0: pts = pts[::-1]
        along = (Vector((p1[0], p1[1], 0)) - Vector((p0[0], p0[1], 0))).normalized()
        b.quad(pts, MW, wall_tile, (along, Z))

    def wall_x(xw, outward):
        """Wall on the x = xw plane (room side) with a door at the gate's y; inner + outer face, steel reveal."""
        xo = xw + outward * T
        for xf, facing in ((xw, Vector((-outward, 0, 0))), (xo, Vector((outward, 0, 0)))):
            ya, yb = (y0, y1) if xf == xw else (y0 - T, y1 + T)
            face((xf, ya), (xf, dy0), 0, H, facing)
            face((xf, dy1), (xf, yb), 0, H, facing)
            face((xf, dy0), (xf, dy1), DOOR_H, H, facing)
        lo, hi = min(xw, xo), max(xw, xo)
        b.box((lo - 0.04, dy0 - 0.14, 0), (hi + 0.04, dy0, DOOR_H + 0.14), MS, (1, 1))
        b.box((lo - 0.04, dy1, 0), (hi + 0.04, dy1 + 0.14, DOOR_H + 0.14), MS, (1, 1))
        b.box((lo - 0.06, dy0 - 0.14, DOOR_H), (hi + 0.06, dy1 + 0.14, DOOR_H + 0.28), MS, (1, 1))

    def wall_y(yw, outward):
        yo = yw + outward * T
        face((x0, yw), (x1, yw), 0, H, Vector((0, -outward, 0)))
        face((x0 - T, yo), (x1 + T, yo), 0, H, Vector((0, outward, 0)))
        face((x0 - T, yw), (x0 - T, yo), 0, H, Vector((-1, 0, 0)))     # wall ends (where the x walls meet)
        face((x1 + T, yw), (x1 + T, yo), 0, H, Vector((1, 0, 0)))

    wall_x(x1, +1); wall_x(x0, -1); wall_y(y1, +1); wall_y(y0, -1)
    # floor (slightly above the yard so it never z-fights the slab) and ceiling (faces down)
    b.quad([(x0, y0, 0.02), (x1, y0, 0.02), (x1, y1, 0.02), (x0, y1, 0.02)], MF, (4.0, 4.0), (X, Y))
    b.quad([(x0 - T, y1 + T, H), (x1 + T, y1 + T, H), (x1 + T, y0 - T, H), (x0 - T, y0 - T, H)], MC, (2.0, 2.0), (X, -Y))
    # top of the walls closed (seen from the yard when the deck is higher): a cap the wall thickness wide
    for (lo, hi) in (((x0 - T, y0 - T, H - 0.01), (x1 + T, y0, H + 0.02)), ((x0 - T, y1, H - 0.01), (x1 + T, y1 + T, H + 0.02)),
                     ((x0 - T, y0, H - 0.01), (x0, y1, H + 0.02)), ((x1, y0, H - 0.01), (x1 + T, y1, H + 0.02))):
        b.box(lo, hi, MS, (1, 1), skip=("-z",))
    # ceiling beams across the short way every ~3.2 m, and a spine beam along the long way
    nb = max(3, int(round((x1 - x0) / 3.2)))
    for i in range(1, nb):
        x = x0 + (x1 - x0) * i / nb
        b.box((x - 0.15, y0, H - 0.45), (x + 0.15, y1, H), MS, (1, 1), skip=("+z",))
    # pillars: corners + two along each long wall
    pil = [(x0 + 0.25, y0 + 0.25), (x1 - 0.25, y0 + 0.25), (x0 + 0.25, y1 - 0.25), (x1 - 0.25, y1 - 0.25)]
    for x in (x0 + (x1 - x0) / 3, x0 + 2 * (x1 - x0) / 3):
        pil += [(x, y0 + 0.25), (x, y1 - 0.25)]
    for (px, py) in pil:
        b.box((px - 0.22, py - 0.22, 0), (px + 0.22, py + 0.22, H - 0.01), MS, (1, 1), skip=("-z", "+z"))
    # (no skirting: a kick plate under the benches' backs makes BaseFurnishTools' space check fail)

    room_mats = []
    # lamps: hanging shades on a 3 x 2 grid between the beams (separate objects so Unity finds each one)
    lamps = []
    lamp_pts = []
    for i in range(3):
        for j in range(2):
            lx = x0 + (x1 - x0) * (i + 0.5) / 3
            ly = y0 + (y1 - y0) * (j + 0.5) / 2
            lamp_pts.append((lx, ly, H - 1.1))
    info = dict(x0=x0, x1=x1, y0=y0, y1=y1, H=H, doors=[(x1 + T, gate_y), (x0 - T, gate_y)], lamps=lamp_pts,
                pillars=pil, beams=nb)
    return b, lamp_pts, info


def lamp_mesh(name, p, steel, glow):
    """Hanging industrial lamp: rod to the ceiling, closed conical shade, glowing disc facing down."""
    bm = bmesh.new(); uv = bm.loops.layers.uv.new("UVMap")
    bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=0.02, radius2=0.02, depth=1.1, matrix=Matrix.Translation((0, 0, 0.69)))
    bmesh.ops.create_cone(bm, cap_ends=True, segments=16, radius1=0.42, radius2=0.12, depth=0.28, matrix=Matrix.Translation((0, 0, 0)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    for f in bm.faces: f.material_index = 0
    disc = bmesh.ops.create_circle(bm, cap_ends=True, segments=16, radius=0.36, matrix=Matrix.Translation((0, 0, -0.145)))
    for f in {f for v in disc["verts"] for f in v.link_faces}:
        f.material_index = 1
        if f.normal.z > 0: f.normal_flip()
    for f in bm.faces:
        for l in f.loops: l[uv].uv = (l.vert.co.x + 0.5, l.vert.co.z + 0.5)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    me.materials.append(steel); me.materials.append(glow)
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    o.location = p
    return o


# ------------------------------------------------------------------ fortress texture pass

def raster_uv_mask(o, size):
    mask = np.zeros((size, size), bool)
    me = o.data; uvl = me.uv_layers.active.data
    for p in me.polygons:
        pts = np.array([uvl[li].uv[:] for li in p.loop_indices]) * size
        xs, ys = pts[:, 0], pts[:, 1]
        bx0, bx1 = int(max(0, xs.min())), int(min(size - 1, xs.max() + 1))
        by0, by1 = int(max(0, ys.min())), int(min(size - 1, ys.max() + 1))
        if bx1 < bx0 or by1 < by0: continue
        gx, gy = np.meshgrid(np.arange(bx0, bx1 + 1) + 0.5, np.arange(by0, by1 + 1) + 0.5)
        inside = np.ones_like(gx, bool)
        n = len(pts); sgn = None
        for i in range(n):
            ax, ay = pts[i]; cx, cy = pts[(i + 1) % n]
            cr = (cx - ax) * (gy - ay) - (cy - ay) * (gx - ax)
            s = np.sign((cx - ax) * (pts[(i + 2) % n][1] - ay) - (cy - ay) * (pts[(i + 2) % n][0] - ax))
            if s == 0: continue
            inside &= cr * s >= -1e-6
        mask[by0:by1 + 1, bx0:bx1 + 1] |= inside
    return mask[::-1]          # numpy rows top-down


def bake_ao(objs, size):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"; sc.cycles.samples = 64; sc.cycles.device = "CPU"
    sc.world = sc.world or bpy.data.worlds.new("W"); sc.world.light_settings.distance = 1.6
    ao = bpy.data.images.new("AO", size, size, alpha=False, float_buffer=True); ao.colorspace_settings.name = "Non-Color"
    target = objs[0]
    for m in target.data.materials:
        n = m.node_tree.nodes.new("ShaderNodeTexImage"); n.image = ao
        for x in m.node_tree.nodes: x.select = False
        n.select = True; m.node_tree.nodes.active = n
    # other objects: give them a dummy active image so the bake ignores them (only the target is selected)
    bpy.ops.object.select_all(action="DESELECT")
    target.select_set(True); bpy.context.view_layer.objects.active = target
    sc.render.bake.margin = 8
    log("baking AO", size, "(Cycles CPU, 64 samples)...")
    bpy.ops.object.bake(type="AO")
    a = np.array(ao.pixels[:], dtype=np.float32).reshape(size, size, 4)[::-1, :, 0]
    return a


def enhance_fortress(o, ao):
    src = load(SRC + ".png")[..., :3]
    log("albedo source", src.shape)
    size = src.shape[0]
    mask = raster_uv_mask(o, size)
    grown = blur(mask.astype(np.float32), 2) > 0.01
    # fill the atlas background with the nearby island colour (push-pull), so blurs, the clarity pass and the
    # normal map see no step at island borders (no dark seams, no embossed outlines) and mips don't bleed black
    m = grown.astype(np.float32)
    filled = src * m[..., None]
    for r in (2, 4, 8, 16, 32, 64):
        w = blur(m, r)
        avg = blur(filled * m[..., None], r) / np.maximum(w, 1e-4)[..., None]
        new = (m < 0.5) & (w > 1e-3)
        filled = np.where(new[..., None], avg, filled)
        m = np.where(new, 1.0, m)
    src = filled
    log(f"atlas islands cover {grown.mean() * 100:.0f}% of the texture; background filled")
    # --- albedo at source res: clarity (local contrast), unsharp mask, saturation, AO in cavities
    L = lum(src)
    base = blur(L, 24)
    clarity = (L - base) * 0.35
    det = src - blur(src, 2)
    out = src + clarity[..., None] + det * 0.9
    g = lum(out)[..., None]
    out = g + (out - g) * 1.12
    if ao is not None:
        a = np.clip(ao, 0, 1)
        out = out * (0.55 + 0.45 * a[..., None]) ** 0.8
    out = np.clip(out, 0, 1)
    # --- up to 4096 and a final light sharpen at the new size
    big = upscale2(out) if ALBEDO_SIZE > size else out
    big = np.clip(big + (big - blur(big, 2)) * 0.45, 0, 1)
    # --- normal map from the high-passed luminance (detail only: no big shapes), off on the background
    Lb = lum(big)
    hp = Lb - blur(Lb, 10)
    hp = blur(hp, 1)
    gm = np.repeat(np.repeat(grown, 2, 0), 2, 1) if big.shape[0] == 2 * size else grown
    hp = hp * gm
    nrm = normal_from_height(hp, 9.0)
    # --- metallic 0 / smoothness: darker, bluish-grey (steel, glass) a bit glossier, light dusty concrete matte
    sat = big.max(-1) - big.min(-1)
    blue = np.clip(big[..., 2] - big[..., 0], 0, 1)
    smooth = np.clip(0.12 + 0.25 * (1 - Lb) * (1 - sat * 2) + 0.4 * blue, 0.05, 0.55)
    ms = np.stack([np.zeros_like(smooth), np.zeros_like(smooth), np.zeros_like(smooth), smooth], -1)
    occ = None
    if ao is not None:
        occ = np.clip(0.35 + 0.65 * ao, 0, 1)
    return big, nrm, ms, occ




# ------------------------------------------------------------------ Meshy leftovers + helipad (2026-11-18)
# Meshy added flags and windsocks on poles, signboards hanging in the air, cables and pointer-like sticks poking out,
# and rows of bumps on the top deck. They go; the top deck gets a real helipad (the intro's helicopter lands there).
PAD_R = 7.0            # helipad half width (octagon), metres
PAD_CLEAR = 7.5        # radius kept clear of anything higher than the pad (rotor disc 13 m)
TAIL_LEN = 9.5         # strip behind the pad centre kept clear for the tail boom
ROTOR_H = 3.5          # nothing taller than this above the pad inside the rotor disc


def loose_parts(o):
    """Connected pieces of the mesh: list of (face indices, lo, hi, area)."""
    bm = bmesh.new(); bm.from_mesh(o.data); bm.faces.ensure_lookup_table()
    seen = set(); parts = []
    for f0 in bm.faces:
        if f0.index in seen: continue
        stack = [f0]; comp = []; seen.add(f0.index)
        while stack:
            f = stack.pop(); comp.append(f.index)
            for v in f.verts:
                for g in v.link_faces:
                    if g.index not in seen: seen.add(g.index); stack.append(g)
        co = np.array([v.co[:] for i in comp for v in bm.faces[i].verts])
        parts.append(dict(faces=comp, lo=co.min(0), hi=co.max(0), area=sum(bm.faces[i].calc_area() for i in comp)))
    bm.free()
    return parts


def delete_faces(o, faces):
    bm = bmesh.new(); bm.from_mesh(o.data); bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in set(faces)], context="FACES")
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.to_mesh(o.data); bm.free()


# Spots cleared by hand (Blender metres, after scaling): every loose part wholly inside a box goes. The yard corner
# under the -x/+y scaffold holds scaffold net sheets (solid once retextured), scrap and a bar on the floor.
HAND_CLEAR = [((-13.8, 10.6, -1.1), (-10.4, 15.25, 1.9))]


def part_shape(o, faces):
    """(thinness, tilt in degrees) of a part from its vertex spread: thinness = smallest / largest spread,
    tilt 0 = lying flat, 90 = standing upright."""
    me = o.data
    co = np.array([me.vertices[vi].co[:] for fi in faces for vi in me.polygons[fi].vertices])
    if len(co) < 4: return 1.0, 0.0
    ev, evec = np.linalg.eigh(np.cov((co - co.mean(0)).T))
    thin = math.sqrt(max(ev[0], 0)) / max(math.sqrt(max(ev[2], 1e-12)), 1e-6)
    return thin, math.degrees(math.acos(min(1.0, abs(evec[2, 0]))))


def ground_junk(o, c):
    """Meshy clutter at yard/ground level: boulders, sunk concrete lumps outside the wall, sheets and poles leaning
    or lying about, small rubble on the floor. Returns the reason or None. Upright wall panels, gate hardware and
    flat floor plates stay."""
    lo, hi = c["lo"], c["hi"]; d = hi - lo; big = float(d.max())
    nf = len(c["faces"]); roundish = d.min() > 0.5 * big
    r = float(np.hypot(*((lo[:2] + hi[:2]) / 2)))
    if lo[2] < 0.6 and hi[2] < 1.5 and 0.5 < big < 2.8 and d.min() > 0.4 * big and nf >= 70: return "rock"
    if hi[2] < 0.5 and big < 1.8 and nf >= 60: return "rubble"
    if lo[2] < -0.5 and hi[2] < 0.5 and r > 13 and big < 3.5 and c["area"] < 15: return "ground scrap"
    for b0, b1 in HAND_CLEAR:
        if all(lo[i] >= b0[i] for i in range(3)) and all(hi[i] <= b1[i] for i in range(3)): return "hand-picked"
    if lo[2] < -0.5 and 1.0 < big < 3.2 and roundish and r > 15 and c["area"] < 20: return "lump"
    if hi[2] < 0.6 and big > 1.5 and sorted(d)[1] < 0.3: return "lying pole"
    if hi[2] < 0.75 and lo[2] > -0.3 and big < 1.2 and d[2] > 0.1 and c["area"] < 1.5: return "rubble"
    if lo[2] < 0.8 and 1.0 < big < 7.0 and c["area"] < 25:
        thin, tilt = part_shape(o, c["faces"])
        if thin < 0.08 and 20 < tilt < 72: return "leaning sheet"
    if lo[2] < -0.3 and r > 11 and c["area"] < 6 and big < 3.0:
        thin, tilt = part_shape(o, c["faces"])
        if thin < 0.08: return "loose sheet"           # broken panels and slabs on the ground outside the wall
    return None


def clean_meshy(o, deck_top=11.6):
    """Removes Meshy junk in passes (a pole removed leaves its dish floating, so repeat until nothing changes):
    floating bits, flat flags/signs up high, long thin sticks/cables, and small things standing above the top deck
    (dishes, windsocks, masts). Returns the counts."""
    total = {}
    for _ in range(4):
        why = _clean_pass(o, deck_top)
        for k, v in why.items(): total[k] = total.get(k, 0) + v
        if not why: break
    log(f"Meshy leftovers removed (all passes): {total}")
    return total


def _clean_pass(o, deck_top):
    parts = loose_parts(o)
    parts.sort(key=lambda c: -c["area"])
    hull = parts[0]
    # everything else as one BVH per part is slow; test "floating" against the whole mesh minus the part itself
    bvh_all = BVHTree.FromObject(o, bpy.context.evaluated_depsgraph_get())
    me = o.data
    gone, why = [], {}
    for c in parts[1:]:
        dims = c["hi"] - c["lo"]; big = float(dims.max()); small = float(dims.min())
        top = float(c["hi"][2])
        # floating: no other surface within 6 cm of any of its corners' neighbourhood (sampled vertices)
        fset = set(c["faces"]); touching = False
        for fi in c["faces"][:: max(1, len(c["faces"]) // 24)]:
            for vi in me.polygons[fi].vertices:
                co = me.vertices[vi].co
                for d in ((0, 0, -1), (0, 0, 1), (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0)):
                    loc, n, idx, dist = bvh_all.ray_cast(co + Vector(d) * 0.002, Vector(d), 0.06)
                    if loc is not None and idx not in fset: touching = True; break
                if touching: break
            if touching: break
        flat_high = small < 0.08 * big and 0.5 < big < 6.0 and c["lo"][2] > 9.5 and c["area"] < 12
        mid_r = float(np.hypot(*((c["lo"][:2] + c["hi"][:2]) / 2)))
        stick = big > 2.0 and sorted(dims)[1] < 0.35 and (top > 10.0 or mid_r > WIDTH * 0.42)
        above_deck = c["lo"][2] > deck_top + 0.6 and big < 3.5 and c["area"] < 8
        ground = ground_junk(o, c)
        if not touching or flat_high or stick or above_deck or ground:
            gone += c["faces"]
            k = "floating" if not touching else "flag/sign" if flat_high else "stick" if stick else "above deck" if above_deck else ground
            why[k] = why.get(k, 0) + 1
    if gone: delete_faces(o, gone)
    return why


def helipad_spot(o):
    """The top deck: the biggest up-facing area above 9 m. Returns (centre xy, top z, deck radius)."""
    me = o.data
    pts, ws, zs = [], [], []
    for poly in me.polygons:
        if poly.normal.z > 0.9 and poly.center.z > 9.0 and poly.area > 0.05:
            pts.append(poly.center[:2]); ws.append(poly.area); zs.append(poly.center.z)
    pts, ws, zs = np.array(pts), np.array(ws), np.array(zs)
    # the biggest height band (0.6 m) by area
    best = None
    for z0 in np.arange(9.0, 18.0, 0.2):
        sel = (zs >= z0) & (zs < z0 + 0.6)
        a = ws[sel].sum()
        if best is None or a > best[0]: best = (a, z0)
    sel = (zs >= best[1]) & (zs < best[1] + 0.6)
    c = (pts[sel] * ws[sel, None]).sum(0) / ws[sel].sum()
    top = float(np.percentile(zs[sel], 90))
    reach = float(np.percentile(np.linalg.norm(pts[sel] - c, axis=1), 95))
    log(f"top deck: {best[0]:.0f} m2 at z {top:.2f}, centre ({c[0]:.1f}, {c[1]:.1f}), reach {reach:.1f} m")
    return c, top, reach


def clear_pad(o, c, top):
    """Clears the rotor disc: pieces that stand above the deck inside PAD_CLEAR go (stones, barrels, dishes, poles)."""
    parts = loose_parts(o)
    parts.sort(key=lambda p: -p["area"])
    gone, n = [], 0
    for p in parts[1:]:
        mid = (p["lo"][:2] + p["hi"][:2]) / 2
        r = float(np.linalg.norm(mid - c))
        if p["hi"][2] > top + 0.05 and p["lo"][2] > top - 1.0 and r < PAD_CLEAR + 0.5 * float(max(p["hi"][:2] - p["lo"][:2])):
            gone += p["faces"]; n += 1
    delete_faces(o, gone)
    log(f"helipad: {n} pieces cleared off the deck")


def tex_helipad(n=1024):
    """Dark painted concrete pad, yellow ring, white H, a thin white edge band; UV 0..1 across the pad."""
    yy, xx = np.mgrid[0:n, 0:n] / n
    x, y = xx - 0.5, yy - 0.5
    big, fine = fbm(n, 2.2, 2, 301), fbm(n, 1.0, 50, 302)
    col = np.stack([0.17, 0.17, 0.17]) * (0.8 + 0.35 * big[..., None]) * (0.9 + 0.2 * fine[..., None])
    r = np.sqrt(x * x + y * y)
    wear = fbm(n, 1.2, 20, 303) > 0.3
    ring = (np.abs(r - 0.36) < 0.018) & wear
    col = np.where(ring[..., None], np.stack([0.85, 0.66, 0.10]), col)
    hx = (np.abs(x) < 0.14) & (np.abs(x) > 0.09) & (np.abs(y) < 0.2)
    hb = (np.abs(x) < 0.12) & (np.abs(y) < 0.025)
    H = (hx | hb) & wear
    col = np.where(H[..., None], np.stack([0.86, 0.86, 0.84]), col)
    edge = (np.maximum(np.abs(x), np.abs(y)) > 0.46) & wear
    col = np.where(edge[..., None], np.stack([0.8, 0.8, 0.78]), col)
    col = col * (0.85 + 0.25 * fine[..., None])
    h = 0.3 * big + 0.3 * fine - 0.3 * (ring | H | edge)
    return col, normal_from_height(wrap_blur(h, 1), 2.5, wrap=False), 0.2 + 0.1 * (ring | H), np.zeros((n, n))


def build_helipad(c, top, yaw):
    """Pad slab (octagon) on the deck, edge light studs; returns (pad object, light points)."""
    bm = bmesh.new(); uv = bm.loops.layers.uv.new("UVMap")
    R, T = PAD_R, 0.14
    ring = []
    for i in range(8):
        a = math.radians(22.5 + 45 * i)
        ring.append((math.cos(a) * R / math.cos(math.radians(22.5)), math.sin(a) * R / math.cos(math.radians(22.5))))
    rot = Matrix.Rotation(yaw, 3, "Z")
    def P(x, y, z): return Vector((c[0], c[1], 0)) + rot @ Vector((x, y, 0)) + Vector((0, 0, z))
    topv = [bm.verts.new(P(x, y, top + T)) for x, y in ring]
    botv = [bm.verts.new(P(x, y, top - 0.10)) for x, y in ring]
    ft = bm.faces.new(topv); ft.material_index = 0
    for l in ft.loops:
        q = rot.transposed() @ (l.vert.co - Vector((c[0], c[1], top + T)))
        l[uv].uv = (q.x / (2 * R) + 0.5, q.y / (2 * R) + 0.5)
    for i in range(8):
        j = (i + 1) % 8
        f = bm.faces.new((botv[i], botv[j], topv[j], topv[i])); f.material_index = 1
        for l in f.loops: l[uv].uv = (l.vert.co.x * 0.5, l.vert.co.z * 0.5)
    fb_ = bm.faces.new(botv[::-1]); fb_.material_index = 1
    for l in fb_.loops: l[uv].uv = (0.5, 0.5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    # light studs round the edge (16)
    lights = []
    for i in range(16):
        a = 2 * math.pi * i / 16
        x, y = math.cos(a) * (R - 0.25), math.sin(a) * (R - 0.25)
        p = P(x, y, top + T)
        res = bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=0.09, radius2=0.07, depth=0.08,
                                    matrix=Matrix.Translation(p + Vector((0, 0, 0.04))))
        for f in {f for v in res["verts"] for f in v.link_faces}: f.material_index = 2
        lights.append(tuple(p + Vector((0, 0, 0.12))))
    me = bpy.data.meshes.new("HelipadDeck"); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new("HelipadDeck", me); bpy.context.scene.collection.objects.link(o)
    return o, lights


def pad_clearance(o, c, top, yaw):
    """Highest thing (above the pad) inside the rotor disc and in the tail strip."""
    bvh = BVHTree.FromObject(o, bpy.context.evaluated_depsgraph_get())
    worst_r, worst_t = 0.0, 0.0
    rot = Matrix.Rotation(yaw, 3, "Z")
    for rr in np.arange(0.5, PAD_CLEAR, 0.5):
        for a in np.arange(0, 2 * math.pi, 0.2):
            p = Vector((c[0] + math.cos(a) * rr, c[1] + math.sin(a) * rr, top + 30))
            loc, n, i, d = bvh.ray_cast(p, Vector((0, 0, -1)), 60)
            if loc is not None and loc.z > top + 0.3: worst_r = max(worst_r, loc.z - top)
    for t in np.arange(PAD_R, TAIL_LEN + 0.01, 0.5):
        for w in (-0.6, 0, 0.6):
            q = Vector((c[0], c[1], 0)) + rot @ Vector((w, -t, 0))
            loc, n, i, d = bvh.ray_cast(Vector((q.x, q.y, top + 30)), Vector((0, 0, -1)), 60)
            if loc is not None and loc.z > top + 0.3: worst_t = max(worst_t, loc.z - top)
    return worst_r, worst_t


def best_tail_yaw(o, c, top):
    """The tail goes where the deck has least in the way (the nose points the other way)."""
    best = None
    for deg in range(0, 360, 15):
        r, t = pad_clearance(o, c, top, math.radians(deg))
        if best is None or t < best[0]: best = (t, deg)
    return math.radians(best[1])


def stairs_spot(o, c, top, reach):
    """The way down from the deck: the ladder/stair piece whose top is nearest the deck edge (tall, narrow parts
    that end just under the deck rim)."""
    best = None
    for p in loose_parts(o):
        dims = p["hi"] - p["lo"]
        if dims[2] < 2.0 or max(dims[0], dims[1]) > 4.5: continue
        if abs(p["hi"][2] - top) > 1.2: continue
        mid = (p["lo"][:2] + p["hi"][:2]) / 2
        r = float(np.linalg.norm(mid - c))
        if r < PAD_R or r > reach + 3: continue
        if best is None or r < best[0]: best = (r, mid, float(p["hi"][2]))
    if best is None: return None
    return (float(best[1][0]), float(best[1][1]), best[2])


# ------------------------------------------------------------------ outside retexture (2026-11-17, round 2)
# Every fortress face gets a surface class from Meshy's colour under it (plus its facing and height), a material per
# class, and world-scale box-projected UVs, so the outside uses sharp tiling textures like the room instead of
# Meshy's soft painted atlas. Each class's texture is tinted towards Meshy's own colour for that class.

STONE_TOP = 1.0     # metres: upright faces below this are the stone base course
EXT_TILE = dict(Concrete=3.0, ConcreteDark=3.0, Stone=1.5, Steel=1.6, Rust=2.0, Sandbag=1.8, Ground=3.2, Paint=1.6,
                Olive=1.6, Roof=2.4)
# how far each class's texture is tinted toward Meshy's colour under it (0 = keeps its own colour). Meshy's atlas is
# mostly pale grey, so a strong tint washes every surface out to the same grey.
EXT_TINT = dict(Concrete=0.2, ConcreteDark=0.2, Stone=0.3, Rust=0.3, Sandbag=0.4, Ground=0.3, Paint=0.9,
                Steel=0.0, Olive=0.0, Roof=0.0)


def cells(n, count, seed):
    """Tileable Worley noise: (F1, F2, cell id) for `count` random points wrapped round the tile."""
    rng = np.random.default_rng(seed)
    pts = rng.random((count, 2))
    yy, xx = np.mgrid[0:n, 0:n] / n
    f1 = np.full((n, n), 9.0); f2 = np.full((n, n), 9.0); cid = np.zeros((n, n), int)
    for i, (px, py) in enumerate(pts):
        dx = np.abs(xx - px); dx = np.minimum(dx, 1 - dx)
        dy = np.abs(yy - py); dy = np.minimum(dy, 1 - dy)
        d = np.sqrt(dx * dx + dy * dy)
        closer = d < f1
        f2 = np.where(closer, f1, np.minimum(f2, d))
        cid = np.where(closer, i, cid)
        f1 = np.where(closer, d, f1)
    return f1, f2, cid


def tex_concrete_ext(n=1024, seed=40):
    """Weathered outside concrete: form boards, tie holes, run-down stains, patchy lichen."""
    yy, xx = np.mgrid[0:n, 0:n] / n
    v = 1.0 - yy
    big, mid, fine = fbm(n, 2.4, 2, seed), fbm(n, 1.8, 6, seed + 1), fbm(n, 1.0, 40, seed + 2)
    col = np.stack([0.40, 0.385, 0.36]) * (0.7 + 0.45 * big[..., None]) * (0.9 + 0.2 * fine[..., None])
    seam = np.abs(((v * 5) % 1.0) - 0.5) > 0.488
    col = np.where(seam[..., None], col * 0.68, col)
    tx, ty = (xx * 3) % 1.0 - 0.5, (v * 5) % 1.0 - 0.5
    hole = tx * tx + ty * ty < 0.0012
    col = np.where(hole[..., None], col * 0.35, col)
    st = streaks(n, seed + 3)
    col = col * (1 - 0.42 * st[..., None])
    lich = np.clip((fbm(n, 2.2, 4, seed + 4) - 0.62) * 4, 0, 1) * (0.5 + 0.5 * fine)
    col = col * (1 - 0.5 * lich[..., None]) + np.stack([0.17, 0.2, 0.11]) * 0.5 * lich[..., None]
    h = 0.5 * big + 0.3 * fine - 0.6 * seam - 1.2 * hole
    return col, normal_from_height(wrap_blur(h, 1), 3.2, wrap=True), 0.12 - 0.06 * st + 0.08 * lich, np.zeros((n, n))


def tex_stone(n=1024, seed=50):
    """Mossy stone blocks (irregular cells), dark mortar with moss in the joints."""
    f1, f2, cid = cells(n, 48, seed)
    edge = f2 - f1
    mortar = np.clip(1 - edge / 0.012, 0, 1)
    rng = np.random.default_rng(seed + 1)
    tone = rng.random(48)[cid]
    fine, mid = fbm(n, 1.0, 40, seed + 2), fbm(n, 1.8, 8, seed + 3)
    stone = np.stack([0.36, 0.35, 0.31]) * (0.65 + 0.5 * tone[..., None]) * (0.85 + 0.3 * fine[..., None])
    moss = np.clip((mid - 0.45) * 2.5, 0, 1) * 0.6 + mortar * 0.5
    mossc = np.stack([0.16, 0.22, 0.09]) * (0.7 + 0.5 * fine[..., None])
    col = stone * (1 - moss[..., None] * 0.7) + mossc * moss[..., None] * 0.7
    col = col * (1 - 0.55 * mortar[..., None])
    col = col * (0.55 + 0.45 * np.clip(edge / 0.035, 0, 1) ** 0.6)[..., None]     # rounded stones: darker toward the joints
    col = col * (0.85 + 0.3 * fbm(n, 1.4, 16, seed + 4))[..., None]
    h = np.sqrt(np.clip(edge / 0.06, 0, 1)) * 1.2 + 0.25 * fine
    return col, normal_from_height(wrap_blur(h, 1), 4.0, wrap=True), 0.1 + 0.12 * moss, np.zeros((n, n))


def tex_sandbag(n=1024, seed=60):
    """Rows of filled sandbags: rounded bags, woven cloth, seams, dust."""
    yy, xx = np.mgrid[0:n, 0:n] / n
    rows = 5
    r = yy * rows; ri = np.floor(r).astype(int); fy = r - ri - 0.5
    shift = (ri % 2) * 0.5
    bx = (xx * 3 + shift) % 1.0 - 0.5
    bag = np.clip(1 - (bx / 0.5) ** 6 - (fy / 0.5) ** 4, 0, 1)
    weave = 0.5 + 0.25 * np.sin(xx * math.tau * 260) * np.sin(yy * math.tau * 260)
    fine, mid = fbm(n, 1.0, 40, seed), fbm(n, 2.0, 4, seed + 1)
    rng = np.random.default_rng(seed + 2)
    tone = rng.random((rows, 3))[ri % rows, ((xx * 3 + shift) // 1).astype(int) % 3]
    col = np.stack([0.50, 0.45, 0.34]) * (0.75 + 0.3 * tone[..., None]) * (0.85 + 0.2 * weave[..., None]) * (0.8 + 0.3 * mid[..., None])
    col = col * (0.45 + 0.55 * np.sqrt(bag)[..., None])
    h = np.sqrt(bag) * 2 + 0.08 * weave + 0.1 * fine
    return col, normal_from_height(wrap_blur(h, 1), 3.0, wrap=True), 0.06 + 0.04 * fine, np.zeros((n, n))


def tex_rust(n=1024, seed=70):
    """Rusty corrugated sheet metal: ribs along u, flaking paint, streaks."""
    yy, xx = np.mgrid[0:n, 0:n] / n
    rib = 0.5 + 0.5 * np.sin(xx * math.tau * 8)
    fine, mid = fbm(n, 1.0, 40, seed), fbm(n, 1.8, 6, seed + 1)
    rust = np.clip((fbm(n, 2.0, 3, seed + 2) - 0.35) * 2.2, 0, 1)
    paint = np.stack([0.22, 0.24, 0.20]) * (0.8 + 0.3 * mid[..., None])
    rc = np.stack([0.36, 0.18, 0.08]) * (0.6 + 0.6 * fine[..., None])
    col = paint * (1 - rust[..., None]) + rc * rust[..., None]
    col = col * (1 - 0.3 * streaks(n, seed + 3)[..., None]) * (0.75 + 0.25 * rib[..., None])
    return col, normal_from_height(rib * 6 + fine * 0.6, 2.2, wrap=True), 0.3 - 0.22 * rust, 0.5 * (1 - rust)


def tex_ground(n=1024, seed=80):
    """Yard paving: big worn concrete slabs, dirt and moss in the joints."""
    f1, f2, cid = cells(n, 14, seed)
    joint = np.clip(1 - (f2 - f1) / 0.008, 0, 1)
    rng = np.random.default_rng(seed + 1)
    tone = rng.random(14)[cid]
    big, fine = fbm(n, 2.4, 2, seed + 2), fbm(n, 0.9, 60, seed + 3)
    col = np.stack([0.31, 0.30, 0.28]) * (0.75 + 0.3 * tone[..., None]) * (0.8 + 0.35 * big[..., None]) * (0.9 + 0.2 * fine[..., None])
    dirt = np.clip((fbm(n, 2.6, 2, seed + 4) - 0.55) * 3, 0, 1)
    col = col * (1 - 0.4 * dirt[..., None]) + np.stack([0.14, 0.12, 0.08]) * 0.4 * dirt[..., None]
    mj = joint * (0.6 + 0.4 * fine)
    col = col * (1 - mj[..., None] * 0.6) + np.stack([0.12, 0.16, 0.07]) * mj[..., None] * 0.5
    h = 0.3 * big + 0.2 * fine - 1.0 * joint
    return col, normal_from_height(wrap_blur(h, 1), 3.0, wrap=True), 0.14 + 0.1 * dirt, np.zeros((n, n))


def tex_gunmetal(n=1024, seed=90):
    """Railings, ladders, poles, pipes: dark gunmetal with worn bright edges and a few rust spots."""
    mid, fine = fbm(n, 1.8, 4, seed), fbm(n, 1.0, 30, seed + 1)
    col = np.stack([0.13, 0.135, 0.14]) * (0.8 + 0.4 * mid[..., None])
    worn = np.clip((fbm(n, 1.4, 10, seed + 2) - 0.66) * 4, 0, 1)
    rust = np.clip((fbm(n, 2.2, 3, seed + 3) - 0.72) * 4, 0, 1)
    col = col * (1 - worn[..., None]) + np.stack([0.38, 0.38, 0.37]) * worn[..., None]
    col = col * (1 - rust[..., None]) + np.stack([0.30, 0.15, 0.07]) * rust[..., None]
    col *= (0.9 + 0.2 * fine[..., None])
    return col, normal_from_height(fine * 0.6 - rust * 0.5, 2.0, wrap=True), 0.35 + 0.25 * worn - 0.2 * rust, 0.4 + 0.5 * worn


def tex_olive(n=1024, seed=100):
    """Army olive drab paint on metal (boxes, tanks, doors, sheds): panel seams, rivets, chipped paint, dust."""
    yy, xx = np.mgrid[0:n, 0:n] / n
    mid, fine, big = fbm(n, 1.8, 6, seed), fbm(n, 1.0, 40, seed + 1), fbm(n, 2.4, 2, seed + 2)
    col = np.stack([0.21, 0.24, 0.13]) * (0.8 + 0.3 * big[..., None]) * (0.9 + 0.2 * fine[..., None])
    seam = (np.abs(((xx * 2) % 1.0) - 0.5) > 0.492) | (np.abs(((yy * 2) % 1.0) - 0.5) > 0.492)
    rx, ry = (xx * 2) % 1.0, (yy * 16) % 1.0
    rivet = ((np.abs(rx - 0.03) < 0.006) | (np.abs(rx - 0.97) < 0.006)) & (np.abs(ry - 0.5) < 0.05)
    chip = np.clip((fbm(n, 1.2, 14, seed + 3) - 0.68) * 5, 0, 1)
    col = col * (1 - 0.45 * seam[..., None]) * (1 - 0.3 * rivet[..., None])
    col = col * (1 - chip[..., None]) + np.stack([0.34, 0.33, 0.31]) * chip[..., None]
    col = col * (1 - 0.35 * streaks(n, seed + 4)[..., None])
    dust = np.clip((mid - 0.55) * 2, 0, 1)
    col = col * (1 - 0.3 * dust[..., None]) + np.stack([0.33, 0.30, 0.24]) * 0.3 * dust[..., None]
    h = 0.3 * big + 0.2 * fine - 0.8 * seam + 0.6 * rivet - 0.3 * chip
    return col, normal_from_height(wrap_blur(h, 1), 2.5, wrap=True), 0.22 + 0.1 * chip - 0.08 * dust, 0.6 * chip


def tex_roof(n=1024, seed=110):
    """Roof and canopy sheets: dark grey-green corrugated metal, rust along the ribs, rain streaks."""
    col, nrm, sm, met = tex_rust(n, seed)
    g = col.mean(-1, keepdims=True)
    col = g + (col - g) * 0.8
    return col * 0.85, nrm, sm, met


def ext_textures():
    st_c, st_n, st_s, st_m = tex_steel(1024)
    return {
        "Concrete": tex_concrete_ext(1024, 40), "ConcreteDark": tex_concrete_ext(1024, 45),
        "Stone": tex_stone(), "Sandbag": tex_sandbag(), "Rust": tex_rust(), "Ground": tex_ground(),
        "Steel": tex_gunmetal(), "Paint": (st_c, st_n, st_s, st_m), "Olive": tex_olive(), "Roof": tex_roof(),
    }


def classify_faces(o, atlas):
    """Class per face from the (blurred) Meshy colour at its UV centre, its facing and height; then two passes of
    area-weighted majority vote over edge neighbours to remove speckle."""
    size = atlas.shape[0]
    soft = blur(atlas, 3)
    bm = bmesh.new(); bm.from_mesh(o.data); uv = bm.loops.layers.uv.active
    bm.faces.ensure_lookup_table()
    names = ["Concrete", "ConcreteDark", "Stone", "Steel", "Rust", "Sandbag", "Ground", "Paint", "Olive", "Roof"]
    cls = np.zeros(len(bm.faces), int); colours = np.zeros((len(bm.faces), 3))
    for f in bm.faces:
        u = sum(l[uv].uv.x for l in f.loops) / len(f.loops); v = sum(l[uv].uv.y for l in f.loops) / len(f.loops)
        px = int(np.clip(u * size, 0, size - 1)); py = int(np.clip((1 - v) * size, 0, size - 1))
        c = soft[py, px]; colours[f.index] = c
        r, g, b_ = c; L = 0.2126 * r + 0.7152 * g + 0.0722 * b_; sat = max(c) - min(c)
        z = f.calc_center_median().z
        if f.normal.z > 0.85 and z < 0.6: k = "Ground"
        elif f.normal.z > 0.85 and L > 0.14: k = "ConcreteDark"          # deck and walkway floors, darker than walls
        elif sat > 0.38 and r > 0.45 and r > g * 1.3: k = "Paint"
        elif g > r * 1.02 and g > b_ * 1.08 and sat > 0.05: k = "Stone"
        elif r > g > b_ and sat > 0.14 and L > 0.1: k = "Sandbag" if L > 0.42 else "Rust"
        elif L < 0.14: k = "Steel"
        elif L > 0.34: k = "Concrete"
        else: k = "ConcreteDark"
        cls[f.index] = names.index(k)
    area = np.array([f.calc_area() for f in bm.faces])
    # shape rules, per loose part, so each part reads as what it is: thin parts (railings, ladders, poles, antennas,
    # stair stringers) are gunmetal; thin roof/canopy sheets up high are corrugated metal; boxes, tanks, sheds and
    # door/hatch panels are olive drab paint; the bottom metre of every upright wall is a stone base course
    seen = set(); thin = 0; kinds = {"Roof": 0, "Olive": 0}
    for f0 in bm.faces:
        if f0.index in seen: continue
        stack = [f0]; comp = []
        seen.add(f0.index)
        while stack:
            f = stack.pop(); comp.append(f)
            for v in f.verts:
                for g2 in v.link_faces:
                    if g2.index not in seen: seen.add(g2.index); stack.append(g2)
        co = np.array([v.co[:] for f in comp for v in f.verts])
        lo, hi = co.min(0), co.max(0)
        dims = np.sort(hi - lo)
        a = sum(area[f.index] for f in comp)
        if dims[1] < 0.35 or a < 0.5:
            for f in comp: cls[f.index] = names.index("Steel")
            thin += 1
            continue
        if len(comp) > 2000: continue                      # the main hull keeps its per-face classes
        flat_a = sum(area[f.index] for f in comp if abs(f.normal.z) > 0.75)
        flat = flat_a / max(a, 1e-6)
        # roof sheets slope; deck slabs (the helipad deck too) lie level and stay concrete
        slope = sum(area[f.index] * abs(f.normal.z) for f in comp if abs(f.normal.z) > 0.75) / max(flat_a, 1e-6)
        ev = np.linalg.eigvalsh(np.cov((co - co.mean(0)).T))
        sheet = math.sqrt(max(ev[0], 0)) / max(math.sqrt(max(ev[2], 1e-12)), 1e-6) < 0.08
        mid_z = (lo[2] + hi[2]) / 2
        if flat > 0.7 and slope < 0.99 and dims[0] < 0.5 and a > 4 and mid_z > 5.0 and dims[2] > 3.0:
            k = "Roof"
        elif dims[2] < 3.2 and a < 30 and hi[2] > 0.8 and not (sheet and flat > 0.7):
            k = "Olive"                                    # boxes, tanks, generators, sheds, doors, hatches
        else:
            continue
        for f in comp: cls[f.index] = names.index(k)
        kinds[k] += 1
    log(f"shape classes: {kinds['Roof']} roof sheets, {kinds['Olive']} olive boxes/tanks/doors")
    for f in bm.faces:
        if cls[f.index] in (names.index(n) for n in ("Steel", "Ground", "Olive", "Roof")): continue
        if abs(f.normal.z) < 0.6 and f.calc_center_median().z < STONE_TOP:
            cls[f.index] = names.index("Stone")
    log(f"{thin} thin/small parts set to steel")
    for _ in range(2):
        new = cls.copy()
        for f in bm.faces:
            votes = {cls[f.index]: area[f.index] * 1.5}
            for e in f.edges:
                for g2 in e.link_faces:
                    if g2 is f: continue
                    votes[cls[g2.index]] = votes.get(cls[g2.index], 0) + area[g2.index]
            new[f.index] = max(votes, key=votes.get)
        cls = new
    bm.free()
    return names, cls, colours, area


def retexture_exterior(o, atlas):
    # a clean cut at the stone base course height, so the band has a straight top edge
    bm = bmesh.new(); bm.from_mesh(o.data)
    bisect(bm, Vector((0, 0, STONE_TOP)), Vector((0, 0, 1)))
    bm.to_mesh(o.data); bm.free()
    names, cls, colours, area = classify_faces(o, atlas)
    texs = ext_textures()
    used = sorted(set(cls.tolist()))
    final = {}
    for ci in used:
        name = names[ci]
        sel = cls == ci
        target = (colours[sel] * area[sel, None]).sum(0) / max(area[sel].sum(), 1e-6)
        col, nrm, sm, met = texs[name]
        mean = col.reshape(-1, 3).mean(0)
        k = EXT_TINT[name]
        ratio = np.clip((target / np.maximum(mean, 1e-3)) ** k, 0.6, 1.6)
        final[name] = (np.clip(col * ratio, 0, 1), nrm, sm, met)
        log(f"outside class {name:12s}: {area[sel].sum():7.0f} m2, {sel.sum():6d} faces, Meshy colour {np.round(target, 2)}, tint {np.round(ratio, 2)}")
    me = o.data
    me.materials.clear()
    slot = {}
    for ci in used:
        name = names[ci]
        slot[ci] = len(me.materials)
        me.materials.append(material(f"Ext_{name}", image_from(final[name][0], f"Ext_{name}"), rough=0.85))
    bm = bmesh.new(); bm.from_mesh(me); uv = bm.loops.layers.uv.active
    bm.faces.ensure_lookup_table()
    for f in bm.faces:
        ci = cls[f.index]; f.material_index = slot[ci]
        t = EXT_TILE[names[ci]]
        n = f.normal; ax = int(np.argmax(np.abs([n.x, n.y, n.z])))
        for l in f.loops:
            co = l.vert.co
            if ax == 2: uvv = (co.x, co.y * (1 if n.z > 0 else -1))
            elif ax == 0: uvv = (co.y * (1 if n.x > 0 else -1), co.z)
            else: uvv = (co.x * (-1 if n.y > 0 else 1), co.z)
            l[uv].uv = (uvv[0] / t, uvv[1] / t)
    bm.to_mesh(me); bm.free()
    return {names[ci]: final[names[ci]] for ci in used}


# ------------------------------------------------------------------ main

def unity_point(p):
    """Blender (x, y, z) -> Unity local (FBX export -Z forward, Y up, then Unity's x flip)."""
    return [round(-p[0], 3), round(p[2], 3), round(-p[1], 3)]


def flood_spots(o, info, k):
    """Outside light spots: over each gate (outside and in the yard), and the four deck corners."""
    bvh = BVHTree.FromObject(o, bpy.context.evaluated_depsgraph_get())
    gy = info["gate_y"]; spots = []
    for side in (1, -1):
        xg = side * 14.9 * k
        spots.append(dict(kind="gate_out", pos=(xg + side * 0.6, gy, GATE["z1"] * k - 0.99 * k + 0.6), aim=(side, 0, -0.6)))
        spots.append(dict(kind="gate_in", pos=(xg - side * 0.8, gy, GATE["z1"] * k - 0.99 * k + 0.4), aim=(-side, 0, -0.7)))
    # deck corners: walk in from the bounding corner along the diagonal until the deck is under us
    for sx in (1, -1):
        for sy in (1, -1):
            for t in np.linspace(0.95, 0.5, 20):
                p = Vector((sx * 0.5 * WIDTH * t, sy * 0.5 * WIDTH * t, 30))
                loc, n, i, d = bvh.ray_cast(p, Vector((0, 0, -1)), 40)
                if loc is not None and loc.z > 4.0:
                    spots.append(dict(kind="corner", pos=(loc.x, loc.y, loc.z + 2.2), aim=(sx, sy, -1.2)))
                    break
    return spots


def preview(objs, lamps, info, outdir):
    os.makedirs(outdir, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"; sc.render.resolution_x = 1280; sc.render.resolution_y = 720
    try: sc.eevee.use_raytracing = True
    except Exception: pass
    w = sc.world or bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
    day = "--day" in argv          # daylight preview, for judging surface colours
    bg = w.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.45, 0.5, 0.58, 1) if day else (0.05, 0.06, 0.08, 1); bg.inputs[1].default_value = 1.0
    moon = bpy.data.objects.new("moon", bpy.data.lights.new("moon", "SUN")); sc.collection.objects.link(moon)
    moon.rotation_euler = (math.radians(50), 0, math.radians(35)); moon.data.energy = 4.0 if day else 1.2
    moon.data.color = (1.0, 0.97, 0.92) if day else (0.75, 0.82, 1.0)
    for p in info["lamps"]:
        l = bpy.data.objects.new("pl", bpy.data.lights.new("pl", "POINT")); sc.collection.objects.link(l)
        l.location = (p[0], p[1], p[2] - 0.25); l.data.energy = 900; l.data.color = (1.0, 0.78, 0.5); l.data.shadow_soft_size = 0.3
    for s in info["spots"]:
        l = bpy.data.objects.new("sp", bpy.data.lights.new("sp", "SPOT")); sc.collection.objects.link(l)
        l.location = s["pos"]; l.data.energy = 6000; l.data.spot_size = math.radians(80); l.data.color = (1.0, 0.85, 0.65)
        l.rotation_euler = Vector(s["aim"]).to_track_quat("-Z", "Y").to_euler()
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.lens = 16
    x0, x1, y0, y1, H = info["x0"], info["x1"], info["y0"], info["y1"], info["H"]
    gy = info["gate_y"]
    views = {
        "out_gate": ((WIDTH * 1.0, gy - 6, 2.2), (0, gy, 3)),
        "out_high": ((WIDTH * 0.9, -WIDTH * 0.9, WIDTH * 0.55), (0, 0, 4)),
        "out_back": ((-WIDTH * 0.9, WIDTH * 0.7, WIDTH * 0.35), (0, 0, 4)),
        "yard": ((x1 + 2.5, y1 + 2.0, 1.7), (x1 + 6, gy, 2.0)),
        "room_door": ((x1 + 4.0, gy, 1.7), (x0, gy, 1.5)),
        "room_in": ((x1 - 1.0, y0 + 1.0, 1.7), (x0 + 2, y1 - 2, 1.6)),
        "room_in2": ((x0 + 1.0, y1 - 1.0, 1.7), (x1 - 2, y0 + 2, 1.4)),
    }
    for name, (pos, tgt) in views.items():
        cam.location = pos; cam.rotation_euler = (Vector(tgt) - Vector(pos)).to_track_quat("-Z", "Y").to_euler()
        sc.render.filepath = os.path.join(outdir, f"Fort_{name}.png"); bpy.ops.render.render(write_still=True)
        log("preview", name)


def main():
    reset()
    fort, k = import_fortress()
    meshy_mat = fort.data.materials[0]
    meshy_mat.name = "Fortress"
    # ---- gates: the two door leaves in the outer wall
    gate_y = (GATE["y0"] + GATE["y1"]) / 2 * k
    for side in (1, -1):
        xs = sorted((side * GATE["xin"] * k, side * (WIDTH / 2 + 1)))
        cut_box(fort, (xs[0], GATE["y0"] * k, 0.05), (xs[1], GATE["y1"] * k, (GATE["z1"] - 0.99) * k), f"gate {'+x' if side > 0 else '-x'}")
    # ---- room: measure the deck, clear the box, build the shell
    x0, x1, y0, y1 = (ROOM[n] * k for n in ("x0", "x1", "y0", "y1"))
    deck, hits = deck_underside(fort, x0, x1, y0, y1)
    ceil = round(deck - 0.08, 2)
    log(f"deck underside over the room: median {deck:.2f} m (min {hits.min():.2f}, max {hits.max():.2f}); room height {ceil:.2f}")
    cut_box(fort, (x0 - WALL_T - 0.02, y0 - WALL_T - 0.02, -0.6), (x1 + WALL_T + 0.02, y1 + WALL_T + 0.02, ceil + 0.02), "room")
    b, lamp_pts, info = build_room(k, ceil, gate_y)
    info["gate_y"] = gate_y
    # ---- room textures
    tex = {}
    wc, wn, wsm = tex_wall(); fc, fn, fsm = tex_floor(); cc, cn, csm = tex_ceiling(); sc_, sn, ssm, smet = tex_steel()
    tex["Wall"] = (wc, wn, np.zeros_like(wsm), wsm); tex["Floor"] = (fc, fn, np.zeros_like(fsm), fsm)
    tex["Ceiling"] = (cc, cn, np.full_like(csm, 0.35), csm); tex["Steel"] = (sc_, sn, smet, ssm)
    mats = []
    for name in ("Wall", "Floor", "Ceiling", "Steel"):
        col = tex[name][0]
        mats.append(material(f"Room_{name}", image_from(col, f"Room_{name}"), rough=0.8))
    room = b.finish(mats)
    steel_m = mats[3]
    glow = material("Room_LampGlow", color=(1.0, 0.85, 0.6), emit=(1.0, 0.8, 0.55, 12.0))
    lamps = [lamp_mesh(f"Lamp_{i + 1:02d}", p, steel_m, glow) for i, p in enumerate(lamp_pts)]
    info["spots"] = flood_spots(fort, info, k)
    # ---- Meshy leftovers and the helipad on the top deck
    pad = None
    if not MESHY_PAINT:
        pc, ptop, preach = helipad_spot(fort)
        clean_meshy(fort, ptop)
        pc, ptop, preach = helipad_spot(fort)
        clear_pad(fort, pc, ptop)
        yaw = best_tail_yaw(fort, pc, ptop)
        rotor, tail = pad_clearance(fort, pc, ptop, yaw)
        stairs = stairs_spot(fort, pc, ptop, preach)
        pad_obj, pad_lights = build_helipad(pc, ptop, yaw)
        pad = dict(obj=pad_obj, centre=(float(pc[0]), float(pc[1]), ptop + 0.14), yaw=yaw, lights=pad_lights, stairs=stairs,
                   rotor=rotor, tail=tail)
        log(f"helipad: {2 * PAD_R:.0f} m at ({pc[0]:.1f}, {pc[1]:.1f}, {ptop + 0.14:.2f}), tail toward {math.degrees(yaw):.0f} deg; "
            f"highest thing in the rotor disc {rotor:.2f} m, in the tail strip {tail:.2f} m (limits {ROTOR_H} / 3.0); stairs {stairs}")
    ext = None
    if MESHY_PAINT:
        # ---- old path: Meshy's own atlas, enhanced, with an AO bake (room + lamps as occluders)
        ao = bake_ao([fort, room], AO_SIZE) if BAKE else None
        big, nrm, ms, occ = enhance_fortress(fort, ao)
        img = image_from(big, "Fortress_Albedo")
        for n in meshy_mat.node_tree.nodes:
            if n.type == "TEX_IMAGE" and n.image is not None and n.image.name != "AO": n.image = img
    else:
        ext = retexture_exterior(fort, load(SRC + ".png")[..., :3])
    if pad is not None:
        hc, hn, hs, hm = tex_helipad()
        pad_mats = [material("Ext_Helipad", image_from(hc, "Ext_Helipad"), rough=0.8), fort.data.materials[0],
                    material("Helipad_Light", color=(0.4, 1.0, 0.5), emit=(0.4, 1.0, 0.5, 8.0))]
        for m in pad_mats: pad["obj"].data.materials.append(m)
        ext["Helipad"] = (hc, hn, hs, hm)
    log(f"room {x1 - x0:.1f} x {y1 - y0:.1f} m, {ceil:.2f} m high, {info['beams'] - 1} beams, {len(info['pillars'])} pillars, "
        f"{len(lamps)} lamps, doors at y {gate_y:.2f}; flood spots {len(info['spots'])}")
    if PREVIEW:
        preview([fort, room], lamps, info, PREVIEW)
    if WRITE:
        os.makedirs(OUT, exist_ok=True)
        if MESHY_PAINT:
            save(big, OUT + "Fortress_Albedo.png")
            save(nrm, OUT + "Fortress_Normal.png", srgb=False)
            save(ms, OUT + "Fortress_MetalSmooth.png", srgb=False)
            if occ is not None:
                save(np.stack([occ] * 3, -1), OUT + "Fortress_Occlusion.png", srgb=False)
        else:
            for name, (col, n_, sm, met) in ext.items():
                save(col, OUT + f"Ext_{name}_Albedo.png")
                save(n_, OUT + f"Ext_{name}_Normal.png", srgb=False)
                save(np.stack([met, met, met, np.clip(sm, 0, 1)], -1), OUT + f"Ext_{name}_MetalSmooth.png", srgb=False)
        for name, (col, n_, met, sm) in tex.items():
            save(col, OUT + f"Room_{name}_Albedo.png")
            save(n_, OUT + f"Room_{name}_Normal.png", srgb=False)
            save(np.stack([met, met, met, sm], -1), OUT + f"Room_{name}_MetalSmooth.png", srgb=False)
        # export: fortress, room, lamps (the bake image nodes go first so the FBX keeps the albedo link)
        if MESHY_PAINT:
            for m in meshy_mat.node_tree.nodes:
                if m.type == "TEX_IMAGE" and m.image is not None and m.image.name == "AO": meshy_mat.node_tree.nodes.remove(m)
        bpy.ops.object.select_all(action="DESELECT")
        for o in [fort, room] + lamps + ([pad["obj"]] if pad is not None else []): o.select_set(True)
        bpy.context.view_layer.objects.active = fort
        bpy.ops.export_scene.fbx(filepath=OUT + "FortressHaven.fbx", use_selection=True, object_types={"MESH"},
                                 apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                                 axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                                 path_mode="STRIP", embed_textures=False, mesh_smooth_type="FACE")
        log("exported", OUT + "FortressHaven.fbx")
        data = dict(
            note="Unity-local points (metres) of FortressHaven.fbx; written by Blender/fortress_base.py",
            width=WIDTH, yardFloor=0.0,
            room=dict(min=unity_point((info["x1"], info["y1"], 0)), max=unity_point((info["x0"], info["y0"], info["H"])),
                      height=info["H"]),
            doors=[unity_point((d[0], d[1], 0)) for d in info["doors"]],
            doorWidth=DOOR_W, doorHeight=DOOR_H,
            gates=[unity_point((s * 14.9 * k, gate_y, 0)) for s in (1, -1)],
            lamps=[unity_point((p[0], p[1], p[2] - 0.15)) for p in info["lamps"]],
            pillars=[unity_point((p[0], p[1], 0)) for p in info["pillars"]],
            spots=[dict(kind=s["kind"], pos=unity_point(s["pos"]), aim=unity_point(s["aim"])) for s in info["spots"]],
            exterior=sorted(k for k in ext.keys() if k != "Helipad") if ext else [],
            helipad=(dict(centre=unity_point(pad["centre"]), nose=unity_point(tuple(Matrix.Rotation(pad["yaw"], 3, "Z") @ Vector((0, 1, 0)))), radius=PAD_R,
                          lights=[unity_point(p) for p in pad["lights"]],
                          stairs=unity_point(pad["stairs"]) if pad["stairs"] else None,
                          rotorClear=pad["rotor"], tailClear=pad["tail"]) if pad is not None else None),
        )
        with open(OUT + "FortressHaven.json", "w") as f: json.dump(data, f, indent=1)
        log("wrote", OUT + "FortressHaven.json")


if __name__ == "__main__":
    main()
