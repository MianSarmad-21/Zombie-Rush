"""Shared humanoid body and face parts for the Zombie Rush characters."""
import math
import random

from mathutils import Euler, Vector

import zr_lib as L

V = Vector


def lerp(a, b, t):
    return a + (b - a) * t


def surf(c, r, d):
    """Point on the ellipsoid (centre c, radii r) in direction d."""
    d = V(d).normalized()
    k = 1.0 / math.sqrt((d.x / r[0]) ** 2 + (d.y / r[1]) ** 2 + (d.z / r[2]) ** 2)
    return V(c) + d * k


def const(name):
    return lambda *a: name


# Reference torso rings: (height fraction crotch->neck, half width, half depth, forward bulge)
TORSO = [
    (0.00, 0.105, 0.080, 0.0),
    (0.09, 0.150, 0.102, 0.0),
    (0.21, 0.165, 0.110, 0.0),
    (0.34, 0.150, 0.104, 0.0),
    (0.48, 0.150, 0.108, 0.0),
    (0.62, 0.163, 0.116, 0.0),
    (0.76, 0.176, 0.112, 0.0),
    (0.87, 0.168, 0.098, 0.0),
    (0.95, 0.138, 0.082, 0.0),
    (1.00, 0.078, 0.066, 0.0),
]


class Body:
    """A T-posed humanoid built from lofted tubes. Options (all scale factors default 1):
    w, d (torso width/depth), chest, belly (metres of extra belly), arm (arm thickness),
    leg (leg thickness), hand, head, neck (thickness), hunch_back (extra upper-back depth)."""

    def __init__(self, J, **o):
        self.J = J
        self.s = J['_s']
        self.o = dict(w=1.0, d=1.0, chest=1.0, belly=0.0, arm=1.0, leg=1.0, hand=1.15,
                      head=1.12, neck=1.0, hunch_back=0.0)
        self.o.update(o)
        s = self.s
        self.zc = J['LeftUpLeg'].z - 0.085 * s
        self.zn = J['Neck'].z + 0.02 * s
        hr = self.o['head']
        self.hc = J['Head'] + V((0, -0.008, 0.12)) * s * hr
        self.hr = V((0.108, 0.122, 0.135)) * s * hr

    # ------------------------------------------------------------ torso
    def torso_ring(self, frac):
        """(z, rx, ry, y offset) of the torso at a height fraction, interpolated."""
        o, s = self.o, self.s
        for i in range(len(TORSO) - 1):
            a, b = TORSO[i], TORSO[i + 1]
            if a[0] <= frac <= b[0]:
                t = (frac - a[0]) / (b[0] - a[0])
                rx = lerp(a[1], b[1], t)
                ry = lerp(a[2], b[2], t)
                break
        else:
            rx, ry = TORSO[-1][1], TORSO[-1][2]
        chest = 1.0 + (o['chest'] - 1.0) * max(0.0, min(1.0, (frac - 0.45) / 0.3)) * (1.0 if frac < 0.93 else 0.4)
        rx *= o['w'] * chest
        ry *= o['d'] * (1.0 + (chest - 1.0) * 0.6)
        bump = math.exp(-((frac - 0.38) / 0.16) ** 2) * o['belly']
        hb = math.exp(-((frac - 0.78) / 0.12) ** 2) * o['hunch_back']
        yoff = -bump * 0.8 * s + hb * 0.5 * s
        ry += (bump + hb) * s
        z = lerp(self.zc, self.zn, frac)
        return z, rx * s, ry * s, yoff

    def torso(self, P, mat, f0=0.0, f1=1.0, scale=1.0, steps=14, gap=None, jag0=0.0, jag1=0.0,
              cap0=True, cap1=True, segs=14, add=0.0):
        pts, rx, ry = [], [], []
        for i in range(steps):
            f = lerp(f0, f1, i / (steps - 1))
            z, a, b, yo = self.torso_ring(f)
            pts.append((0, yo, z))
            rx.append(a * scale + add)
            ry.append(b * scale + add)
        back = lambda t, d: 0.94 if d.y > 0.5 else 1.0
        return L.loft(P, pts, rx, ry, mat, segs=segs, cap0=cap0, cap1=cap1, gap=gap,
                      jag0=jag0, jag1=jag1, prof=back, up=(1, 0, 0))

    def neck(self, P, mat, r=0.056):
        J, s = self.J, self.s
        r = r * s * self.o['neck']
        z0, z1 = J['Neck'].z - 0.06 * s, self.hc.z - 0.02 * s
        return L.loft(P, [(0, 0.005, z0), (0, 0.01, lerp(z0, z1, 0.5)), (0, 0.012, z1)],
                      [r * 1.15, r, r * 0.95], [r * 1.05, r * 0.95, r * 0.95], mat, segs=10)

    # ------------------------------------------------------------ limbs
    ARM = [(0.00, 0.060, 0.062), (0.10, 0.064, 0.060), (0.30, 0.052, 0.050), (0.50, 0.043, 0.042),
           (0.56, 0.042, 0.043), (0.68, 0.047, 0.046), (0.88, 0.036, 0.040), (1.00, 0.031, 0.036)]

    def _arm_span(self, side):
        J, s = self.J, self.s
        x0 = abs(J[side + 'Arm'].x) - 0.075 * s
        return x0, abs(J[side + 'Hand'].x) - x0

    def arm(self, P, side, mat, scale=1.0):
        """Upper arm + forearm along X. mat(t, d): t 0 shoulder .. 1 wrist."""
        J, s = self.J, self.s
        sx = 1 if side == 'Left' else -1
        z = J[side + 'Arm'].z
        x0, span = self._arm_span(side)
        k = self.o['arm'] * scale * s
        pts = [(sx * (x0 + t * span), 0, z) for t, _, _ in self.ARM]
        return L.loft(P, pts, [a * k for _, a, _ in self.ARM], [b * k for _, _, b in self.ARM], mat, segs=10,
                      up=(0, 0, 1))

    def arm_point(self, side, t):
        """(centre, radius) of the arm at t (0 shoulder .. 1 wrist)."""
        sx = 1 if side == 'Left' else -1
        x0, span = self._arm_span(side)
        for i in range(len(self.ARM) - 1):
            a, b = self.ARM[i], self.ARM[i + 1]
            if a[0] <= t <= b[0]:
                r = lerp(a[1], b[1], (t - a[0]) / (b[0] - a[0]))
                break
        else:
            r = self.ARM[-1][1]
        return V((sx * (x0 + t * span), 0, self.J[side + 'Arm'].z)), r * self.o['arm'] * self.s

    def cuff(self, P, side, t, mat, scale=1.3, width=0.05):
        c, r = self.arm_point(side, t)
        sx = 1 if side == 'Left' else -1
        L.loft(P, [c - V((sx * width * 0.5, 0, 0)), c, c + V((sx * width * 0.5, 0, 0))], [r * scale * 0.95, r * scale, r * scale * 0.92],
               None, mat, segs=10, up=(0, 0, 1))

    def torso_point(self, frac, dx, dy, out=0.0):
        """Point on the torso surface at a height fraction, in the horizontal direction (dx, dy)."""
        z, rx, ry, yo = self.torso_ring(frac)
        d = V((dx, dy, 0)).normalized()
        k = 1.0 / math.sqrt((d.x / rx) ** 2 + (d.y / ry) ** 2)
        return V((0, yo, z)) + d * (k + out)

    def leg(self, P, side, mat, scale=1.0, flare=0.0):
        J, s = self.J, self.s
        x = J[side + 'UpLeg'].x
        zt, zk, za = J[side + 'UpLeg'].z, J[side + 'Leg'].z, J[side + 'Foot'].z + 0.035 * s
        pts = [(x * 0.92, 0, zt + 0.07 * s), (x, 0, zt - 0.05 * s), (x, 0, lerp(zt, zk, 0.55)),
               (x, -0.004 * s, zk + 0.03 * s), (x, -0.004 * s, zk - 0.025 * s), (x, 0.012 * s, lerp(zk, za, 0.33)),
               (x, 0.006 * s, lerp(zk, za, 0.75)), (x, 0.004 * s, za)]
        r = [0.112, 0.106, 0.088, 0.066, 0.064, 0.071, 0.052, 0.046]
        k = self.o['leg'] * scale * s
        r = [a + flare * max(0.0, i - 3) / 4.0 * 0.05 for i, a in enumerate(r)]
        return L.loft(P, pts, [a * k for a in r], [a * k * 0.97 for a in r], mat, segs=10, up=(1, 0, 0))

    def boot(self, P, side, mat, shaft=0.20, sole=None, scale=1.0):
        J, s = self.J, self.s
        x = J[side + 'Foot'].x
        k = s * scale * max(1.0, self.o['leg'] * 0.85)
        pts = [(x, 0.065 * k, 0.058 * k), (x, 0.0, 0.064 * k), (x, -0.085 * k, 0.048 * k),
               (x, -0.15 * k, 0.038 * k), (x, -0.19 * k, 0.032 * k)]
        rx = [0.058, 0.066, 0.048, 0.036, 0.022]
        ry = [0.050, 0.054, 0.058, 0.050, 0.034]
        flat = lambda t, d: 0.82 if d.z < -0.4 else 1.0
        L.loft(P, pts, [a * k for a in rx], [b * k for b in ry], mat, segs=10, up=(0, 0, 1), prof=flat)
        if sole:
            L.loft(P, [(x, 0.07 * k, 0.012 * k), (x, -0.06 * k, 0.012 * k), (x, -0.19 * k, 0.014 * k)],
                   [0.013 * k] * 3, [0.056 * k, 0.062 * k, 0.040 * k], sole, segs=8, up=(0, 0, 1))
        if shaft:
            z0 = 0.06 * k
            r = 0.050 * k * max(1.0, self.o['leg'])
            L.loft(P, [(x, 0.012 * k, z0), (x, 0.010 * k, z0 + shaft * 0.5 * s), (x, 0.008 * k, z0 + shaft * s)],
                   [r, r * 1.02, r * 1.08], [r, r, r * 1.04], mat, segs=10, cap1=False)

    def bare_foot(self, P, side, mat, claw=None):
        J, s = self.J, self.s
        x = J[side + 'Foot'].x
        k = s
        pts = [(x, 0.055 * k, 0.05 * k), (x, 0.0, 0.055 * k), (x, -0.09 * k, 0.034 * k), (x, -0.15 * k, 0.024 * k)]
        L.loft(P, pts, [0.042 * k, 0.05 * k, 0.03 * k, 0.02 * k], [0.04 * k, 0.045 * k, 0.05 * k, 0.045 * k], mat,
               segs=10, up=(0, 0, 1), prof=lambda t, d: 0.8 if d.z < -0.4 else 1.0)
        r = 0.04 * k * max(0.8, self.o['leg'])
        L.loft(P, [(x, 0.012 * k, 0.05 * k), (x, 0.008 * k, 0.12 * k), (x, 0.006 * k, 0.17 * k)], [r * 1.05, r, r * 0.95],
               None, mat, segs=10)
        sx = 1 if side == 'Left' else -1
        for i in range(5):
            tx = x + sx * (-0.03 + i * 0.015) * k
            L.loft(P, [(tx, -0.15 * k, 0.02 * k), (tx, -0.18 * k, 0.016 * k)], [0.009 * k, 0.007 * k],
                   [0.009 * k, 0.007 * k], mat, segs=6)
            if claw:
                L.cyl(P, (tx, -0.178 * k, 0.017 * k), (tx, -0.195 * k, 0.012 * k), 0.006 * k, 0.001 * k, claw, segs=5)

    def hand(self, P, side, mat, nail=None, curl=0.35, finger_len=1.0, fist=False):
        """Palm-down T-pose hand with four fingers and a thumb."""
        J, s = self.J, self.s
        sx = 1 if side == 'Left' else -1
        h = self.o['hand'] * s
        w = J[side + 'Hand']
        L.box(P, w + V((sx * 0.048 * h, 0, -0.004 * h)), (0.095 * h, 0.082 * h, 0.034 * h), mat, bevel=0.012 * h)
        L.loft(P, [w + V((sx * -0.01 * h, 0, 0)), w + V((sx * 0.02 * h, 0, 0))], [0.026 * h] * 2,
               [0.036 * h] * 2, mat, segs=8, up=(0, 0, 1))
        base_x = w.x + sx * 0.092 * h
        for i, yo in enumerate((-0.03, -0.01, 0.01, 0.03)):
            ln = (0.082 if i in (1, 2) else 0.07) * h * finger_len
            if fist:
                pts = [V((base_x, yo * h, w.z)), V((base_x + sx * 0.03 * h, yo * h, w.z - 0.015 * h)),
                       V((base_x + sx * 0.02 * h, yo * h, w.z - 0.045 * h)), V((base_x - sx * 0.01 * h, yo * h, w.z - 0.04 * h))]
            else:
                c = curl
                pts = [V((base_x, yo * h, w.z)),
                       V((base_x + sx * ln * 0.45, yo * h, w.z - ln * 0.12 * c)),
                       V((base_x + sx * ln * 0.80, yo * h, w.z - ln * 0.42 * c)),
                       V((base_x + sx * ln * 0.98, yo * h, w.z - ln * 0.85 * c))]
            L.loft(P, pts, [0.0135 * h] * 3 + [0.011 * h], [0.0135 * h] * 3 + [0.011 * h], mat, segs=6)
            if nail:
                tip, prev = pts[-1], pts[-2]
                dvec = (tip - prev).normalized()
                L.cyl(P, tip - dvec * 0.004 * h, tip + dvec * 0.02 * h, 0.007 * h, 0.001 * h, nail, segs=5)
        tb = w + V((sx * 0.03 * h, -0.042 * h, -0.006 * h))
        tm = w + V((sx * 0.06 * h, -0.07 * h, -0.014 * h))
        tt = w + V((sx * 0.085 * h, -0.082 * h, -0.024 * h))
        L.loft(P, [tb, tm, tt], [0.014 * h, 0.012 * h, 0.01 * h], [0.014 * h, 0.012 * h, 0.01 * h], mat, segs=6)
        if nail:
            d = (tt - tm).normalized()
            L.cyl(P, tt - d * 0.004 * h, tt + d * 0.018 * h, 0.007 * h, 0.001 * h, nail, segs=5)

    # ------------------------------------------------------------ head
    def head(self, P, mat, gaunt=0.0, jaw=0.0, chin=0.06, elong=1.0, brow=True):
        """Skull shape. gaunt hollows the cheeks, jaw widens the lower face, elong stretches it."""
        def deform(co):
            x, y, z = co.x, co.y, co.z
            if z < 0.1:
                k = (0.1 - z)
                x *= 1.0 - 0.18 * k + jaw * k
                if y < 0:
                    y -= chin * k * k * 2.0
            if gaunt and -0.4 < z < 0.15 and y < 0.2:
                x *= 1.0 - gaunt * math.exp(-((z + 0.15) / 0.2) ** 2) * 0.5
            if y > 0.3:
                y *= 1.04
            z *= elong if z < 0 else 1.0
            return (x, y, z)
        L.ellipsoid(P, self.hc, self.hr, mat, segs=24, rings=16, deform=deform)
        if brow:
            c = surf(self.hc, self.hr, (0, -1, 0.42)) + V((0, 0.012, 0)) * self.s
            L.ellipsoid(P, c, V((0.075, 0.025, 0.022)) * self.s * self.o['head'], mat, segs=12, rings=6)

    def at(self, direction, inset=0.0):
        """Point on the head surface in a direction, pushed in by `inset` metres."""
        d = V(direction).normalized()
        return surf(self.hc, self.hr, d) - d * inset * self.s

    def zombie_eyes(self, P, socket, glow, spread=0.36, height=0.16, size=1.0, brow=None):
        s = self.s * self.o['head'] * size
        for sx in (1, -1):
            d = V((spread * sx, -1, height))
            L.ellipsoid(P, self.at(d, 0.014), V((0.034, 0.02, 0.027)) * s, socket, segs=12, rings=8)
            L.ellipsoid(P, self.at(d, 0.011), V((0.019, 0.015, 0.017)) * s, glow, segs=12, rings=8)
            if brow:
                L.ellipsoid(P, self.at(V((spread * sx * 1.05, -1, height + 0.22)), 0.006), V((0.04, 0.016, 0.014)) * s,
                            brow, segs=10, rings=6, rot=Euler((0, -0.25 * sx, 0)))

    def face_marks(self, mat, spread=0.36, height=0.16, rim='#2a1018', blood='#4a0505', mouth_height=-0.42):
        """Paints a zombie face into the skin material: sunken bruised rings round the eyes,
        blood smeared round the mouth and dribbled down the chin."""
        spec = L.MAT_SPECS[mat]
        marks = list(spec.get('marks', ()))
        s = self.s * self.o['head']
        for sx in (1, -1):
            c = self.at(V((spread * sx, -1, height)), 0.0)
            marks.append(dict(c=tuple(c), r=0.05 * s, col=rim, amt=0.8))
        m = self.at((0, -1, mouth_height), 0.0)
        marks.append(dict(c=tuple(m), r=0.045 * s, col=blood, amt=0.85))
        marks.append(dict(c=tuple(m + V((0.01, -0.01, -0.05)) * s), r=0.03 * s, col=blood, amt=0.75))
        marks.append(dict(c=tuple(m + V((-0.005, -0.012, -0.09)) * s), r=0.022 * s, col=blood, amt=0.6))
        spec['marks'] = marks

    def cheeks(self, P, mat, size=1.0):
        s = self.s * self.o['head'] * size
        for sx in (1, -1):
            L.ellipsoid(P, self.at((0.62 * sx, -1, -0.12), 0.012), V((0.03, 0.02, 0.022)) * s, mat, segs=10, rings=6)

    def nose(self, P, mat, size=1.0, rotten=None):
        s = self.s * self.o['head'] * size
        if rotten:
            L.ellipsoid(P, self.at((0, -1, 0.0), 0.006), V((0.022, 0.012, 0.022)) * s, rotten, segs=8, rings=6)
        else:
            L.ellipsoid(P, self.at((0, -1, 0.02), -0.012), V((0.017, 0.022, 0.03)) * s, mat, segs=10, rings=8)

    def ears(self, P, mat, size=1.0):
        s = self.s * self.o['head'] * size
        for sx in (1, -1):
            L.ellipsoid(P, self.at((sx, 0.05, 0.02), 0.01), V((0.012, 0.022, 0.034)) * s, mat, segs=10, rings=6)

    def zombie_mouth(self, P, inside, teeth, open_=0.5, width=1.0, height=-0.42, n_teeth=7, seed=3):
        s = self.s * self.o['head']
        c = self.at((0, -1, height), 0.004)
        hh = 0.012 + 0.028 * open_
        L.ellipsoid(P, c, V((0.047 * width, 0.016, hh)) * s, inside, segs=14, rings=8)
        rnd = random.Random(seed)
        for row in (1, -1):
            for i in range(n_teeth):
                if rnd.random() < 0.18:
                    continue  # missing tooth
                x = (-0.036 + i * 0.072 / (n_teeth - 1)) * width
                hgt = rnd.uniform(0.011, 0.019)
                curve = (x / (0.05 * width)) ** 2 * 0.01
                tc = c + V((x, -0.010 + curve, row * (hh - hgt * 0.45))) * s
                L.box(P, tc, V((0.0095, 0.008, hgt)) * s, teeth, rot=Euler((0, rnd.uniform(-0.25, 0.25), 0)))
        return c
