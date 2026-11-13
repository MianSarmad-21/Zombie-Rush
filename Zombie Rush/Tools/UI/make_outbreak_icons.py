"""Outbreak icon set for Zombie Rush (Assets/UI/Outbreak/Icons). Run: python Tools/UI/make_outbreak_icons.py

Every icon is a white mask (tinted in Unity), drawn big, then roughened so the edges look worn and painted
like the rest of the Outbreak UI, with a couple of fine scratches. Themed shapes: a heart cut by a heartbeat,
a cracked skull, a zombie hand rising from the dirt, nut and bolt scrap, a sentry turret on a tripod, a
boarded-up house, a slit-pupil eye, a plate-carrier vest. Needs only Pillow.
"""
import math, os, random
from PIL import Image, ImageDraw, ImageFilter, ImageChops

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "UI", "Outbreak", "Icons")
os.makedirs(OUT, exist_ok=True)
W = 512          # drawing size
SIZE = 256       # saved size
ON, OFF = 255, 0


def P(x, y):
    return (x * W, y * W)


def pts(*xy):
    return [P(xy[i], xy[i + 1]) for i in range(0, len(xy), 2)]


def line(d, points, w, fill=ON):
    """Thick polyline with round caps and joints (w in 0..1 units)."""
    pw = w * W
    d.line(points, fill=fill, width=int(pw))
    for (x, y) in points:
        d.ellipse([x - pw / 2, y - pw / 2, x + pw / 2, y + pw / 2], fill=fill)


def circle(d, cx, cy, r, fill=ON):
    d.ellipse([(cx - r) * W, (cy - r) * W, (cx + r) * W, (cy + r) * W], fill=fill)


def ring(d, cx, cy, r, w, fill=ON):
    d.ellipse([(cx - r) * W, (cy - r) * W, (cx + r) * W, (cy + r) * W], outline=fill, width=int(w * W))


def arrowhead(d, tip, direction, size, fill=ON):
    a = math.atan2(direction[1], direction[0])
    tx, ty = tip
    L = size * W
    left = (tx - math.cos(a - 0.6) * L, ty - math.sin(a - 0.6) * L)
    right = (tx - math.cos(a + 0.6) * L, ty - math.sin(a + 0.6) * L)
    d.polygon([(tx, ty), left, right], fill=fill)


def rough(mask, seed, amount=1.0, scratches=2):
    """Worn edges: blur, push the edge around with grainy noise, threshold; then a few fine scratches."""
    random.seed(seed)
    b = mask.filter(ImageFilter.GaussianBlur(W * 0.007))
    n = Image.effect_noise((W // 5, W // 5), 55 * amount).resize((W, W), Image.BICUBIC)
    fine = Image.effect_noise((W // 2, W // 2), 40 * amount).resize((W, W), Image.BICUBIC)
    m = ImageChops.add(b, n, 1.0, -128)
    m = ImageChops.add(m, fine, 1.0, -128)
    m = m.point(lambda v: 255 if v > 128 else 0)
    near = mask.filter(ImageFilter.MaxFilter(9))          # no stray specks away from the shape
    m = ImageChops.multiply(m, near)
    d = ImageDraw.Draw(m)
    for _ in range(scratches):
        x, y = random.uniform(0.15, 0.85) * W, random.uniform(0.15, 0.85) * W
        a = random.uniform(0, math.pi); L = random.uniform(0.15, 0.3) * W
        d.line([(x, y), (x + math.cos(a) * L, y + math.sin(a) * L)], fill=0, width=max(2, int(W * 0.009)))
    return m.filter(ImageFilter.GaussianBlur(W * 0.0025))


def icon(name, fn, seed=0, amount=1.0, scratches=2):
    m = Image.new("L", (W, W), 0)
    d = ImageDraw.Draw(m)
    fn(d)
    m = rough(m, seed or hash(name) % 1000, amount, scratches)
    # trim to a 6% margin so every icon fills its box the same way
    box = m.getbbox()
    if box:
        crop = m.crop(box)
        side = max(crop.size)
        target = int(W * 0.88)
        k = target / side
        crop = crop.resize((max(1, int(crop.size[0] * k)), max(1, int(crop.size[1] * k))), Image.LANCZOS)
        m = Image.new("L", (W, W), 0)
        m.paste(crop, ((W - crop.size[0]) // 2, (W - crop.size[1]) // 2))
    img = Image.merge("RGBA", (Image.new("L", (W, W), 255),) * 3 + (m,))
    img.resize((SIZE, SIZE), Image.LANCZOS).save(os.path.join(OUT, "ic_" + name + ".png"))
    print("wrote ic_" + name)


# ------------------------------------------------------------------ the set
def health(d):      # a heart cut by a heartbeat
    circle(d, 0.32, 0.36, 0.2); circle(d, 0.68, 0.36, 0.2)
    d.polygon(pts(0.125, 0.42, 0.5, 0.9, 0.875, 0.42, 0.5, 0.3), fill=ON)
    line(d, pts(0.02, 0.53, 0.3, 0.53, 0.38, 0.38, 0.47, 0.72, 0.56, 0.26, 0.64, 0.53, 0.98, 0.53), 0.055, OFF)


def armor(d):       # plate carrier
    d.polygon(pts(0.22, 0.08, 0.36, 0.08, 0.41, 0.22, 0.59, 0.22, 0.64, 0.08, 0.78, 0.08,
                  0.83, 0.32, 0.84, 0.86, 0.5, 0.94, 0.16, 0.86, 0.17, 0.32), fill=ON)
    for y in (0.5, 0.64, 0.78):
        d.line(pts(0.24, y, 0.76, y), fill=OFF, width=int(0.025 * W))
    d.line(pts(0.5, 0.36, 0.5, 0.92), fill=OFF, width=int(0.025 * W))
    d.line(pts(0.21, 0.32, 0.79, 0.32), fill=OFF, width=int(0.03 * W))


def scrap(d):       # a bolt behind a hex nut
    line(d, pts(0.2, 0.84, 0.74, 0.3), 0.15)
    hx, hy = 0.76, 0.27
    d.polygon([P(hx + 0.15 * math.cos(a), hy + 0.15 * math.sin(a)) for a in [k * math.pi / 3 + 0.3 for k in range(6)]], fill=ON)
    for t in (0.35, 0.45, 0.55):        # thread cuts
        x, y = 0.2 + (0.74 - 0.2) * t, 0.84 + (0.3 - 0.84) * t
        d.line(pts(x - 0.06, y - 0.06, x + 0.06, y + 0.06), fill=OFF, width=int(0.02 * W))
    nx, ny, r = 0.4, 0.6, 0.27
    hexa = [P(nx + (r + 0.04) * math.cos(a), ny + (r + 0.04) * math.sin(a)) for a in [k * math.pi / 3 for k in range(6)]]
    d.polygon(hexa, fill=OFF)
    d.polygon([P(nx + r * math.cos(a), ny + r * math.sin(a)) for a in [k * math.pi / 3 for k in range(6)]], fill=ON)
    circle(d, nx, ny, 0.11, OFF)


def skull(d):       # cracked skull
    d.ellipse([P(0.16, 0.06), P(0.84, 0.66)], fill=ON)
    d.rounded_rectangle([P(0.3, 0.5), P(0.7, 0.9)], radius=0.08 * W, fill=ON)
    d.polygon(pts(0.2, 0.5, 0.8, 0.5, 0.7, 0.72, 0.3, 0.72), fill=ON)
    d.polygon(pts(0.25, 0.36, 0.36, 0.31, 0.46, 0.37, 0.44, 0.52, 0.3, 0.53), fill=OFF)
    d.polygon(pts(0.54, 0.37, 0.64, 0.31, 0.75, 0.36, 0.7, 0.53, 0.56, 0.52), fill=OFF)
    d.polygon(pts(0.5, 0.55, 0.45, 0.66, 0.55, 0.66), fill=OFF)
    for x in (0.4, 0.5, 0.6):
        d.line(pts(x, 0.74, x, 0.9), fill=OFF, width=int(0.03 * W))
    d.line(pts(0.31, 0.76, 0.69, 0.76), fill=OFF, width=int(0.025 * W))
    line(d, pts(0.56, 0.05, 0.6, 0.15, 0.53, 0.22, 0.57, 0.29), 0.022, OFF)


def hand(d):        # a zombie hand rising from the dirt
    d.polygon(pts(0.4, 0.95, 0.42, 0.62, 0.6, 0.6, 0.63, 0.95), fill=ON)                 # forearm
    d.polygon(pts(0.36, 0.66, 0.4, 0.42, 0.64, 0.4, 0.68, 0.64, 0.6, 0.7, 0.44, 0.7), fill=ON)  # palm
    line(d, pts(0.42, 0.44, 0.35, 0.28, 0.3, 0.12), 0.07)
    line(d, pts(0.49, 0.42, 0.48, 0.24, 0.46, 0.05), 0.07)
    line(d, pts(0.56, 0.42, 0.6, 0.25, 0.63, 0.1), 0.07)
    line(d, pts(0.63, 0.46, 0.72, 0.32, 0.78, 0.22), 0.065)
    line(d, pts(0.38, 0.6, 0.26, 0.5, 0.2, 0.38), 0.07)                                   # thumb
    d.polygon(pts(0.37, 0.72, 0.42, 0.66, 0.47, 0.73, 0.52, 0.66, 0.57, 0.74, 0.62, 0.66, 0.67, 0.73, 0.66, 0.8, 0.38, 0.8), fill=ON)  # torn sleeve
    bumps = [P(0.02, 1.0)] + [P(0.02 + 0.96 * k / 10, 0.86 + (0.04 if k % 2 else 0.0) - (0.03 if k in (4, 5, 6) else 0)) for k in range(11)] + [P(0.98, 1.0)]
    d.polygon(bumps, fill=ON)


def radar(d):
    ring(d, 0.5, 0.5, 0.45, 0.07); ring(d, 0.5, 0.5, 0.27, 0.055)
    d.pieslice([P(0.08, 0.08), P(0.92, 0.92)], -90, -30, fill=ON)
    circle(d, 0.5, 0.5, 0.06)
    circle(d, 0.3, 0.68, 0.045); circle(d, 0.7, 0.62, 0.035)


def turret(d):      # sentry gun on a tripod
    line(d, pts(0.42, 0.56, 0.18, 0.92), 0.055); line(d, pts(0.5, 0.56, 0.5, 0.94), 0.055); line(d, pts(0.58, 0.56, 0.82, 0.92), 0.055)
    d.rounded_rectangle([P(0.26, 0.3), P(0.66, 0.58)], radius=0.05 * W, fill=ON)
    d.rectangle([P(0.6, 0.36), P(0.96, 0.44)], fill=ON)                      # barrel
    d.rectangle([P(0.88, 0.33), P(0.96, 0.47)], fill=ON)                     # muzzle brake
    d.rectangle([P(0.12, 0.36), P(0.26, 0.56)], fill=ON)                     # ammo box
    d.line(pts(0.3, 0.44, 0.6, 0.44), fill=OFF, width=int(0.025 * W))
    line(d, pts(0.36, 0.3, 0.4, 0.2, 0.52, 0.2), 0.04)                        # sight


def expand(d):      # zone ring pushed outward
    ring(d, 0.5, 0.5, 0.2, 0.07)
    for sx, sy in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
        a = (0.5 + sx * 0.25, 0.5 + sy * 0.25); b = (0.5 + sx * 0.44, 0.5 + sy * 0.44)
        line(d, [P(*a), P(*b)], 0.065)
        arrowhead(d, P(0.5 + sx * 0.5, 0.5 + sy * 0.5), (sx, sy), 0.2)


def pause(d):
    d.rounded_rectangle([P(0.2, 0.12), P(0.4, 0.88)], radius=0.04 * W, fill=ON)
    d.rounded_rectangle([P(0.6, 0.12), P(0.8, 0.88)], radius=0.04 * W, fill=ON)


def gear(d):
    for k in range(8):
        a = k * math.pi / 4
        c, s = math.cos(a), math.sin(a)
        w2, r0, r1 = 0.085, 0.25, 0.46
        d.polygon([P(0.5 + c * r0 - s * w2, 0.5 + s * r0 + c * w2), P(0.5 + c * r1 - s * w2 * 0.8, 0.5 + s * r1 + c * w2 * 0.8),
                   P(0.5 + c * r1 + s * w2 * 0.8, 0.5 + s * r1 - c * w2 * 0.8), P(0.5 + c * r0 + s * w2, 0.5 + s * r0 - c * w2)], fill=ON)
    circle(d, 0.5, 0.5, 0.35)
    circle(d, 0.5, 0.5, 0.14, OFF)


def eye(d):         # slit-pupil eye
    top = [P(0.04 + 0.92 * t / 30, 0.5 - 0.3 * math.sin(math.pi * t / 30)) for t in range(31)]
    bot = [P(0.96 - 0.92 * t / 30, 0.5 + 0.3 * math.sin(math.pi * t / 30)) for t in range(31)]
    d.polygon(top + bot, fill=ON)
    circle(d, 0.5, 0.5, 0.2, OFF)
    d.ellipse([P(0.455, 0.33), P(0.545, 0.67)], fill=ON)


def reload(d):
    d.arc([P(0.12, 0.12), P(0.88, 0.88)], 40, 330, fill=ON, width=int(0.13 * W))
    arrowhead(d, P(0.94, 0.3), (0.35, -1.0), 0.3)


def chevrons(d, up):
    for y in (0.3, 0.62):
        yy = y if up else 1 - y
        dy = -0.2 if up else 0.2
        line(d, pts(0.14, yy + 0.12 * (1 if up else -1), 0.5, yy + 0.12 * (1 if up else -1) + dy, 0.86, yy + 0.12 * (1 if up else -1)), 0.11)


def jump(d): chevrons(d, True)
def crouch(d): chevrons(d, False)


def swap(d):
    line(d, pts(0.1, 0.32, 0.72, 0.32), 0.09); arrowhead(d, P(0.94, 0.32), (1, 0), 0.26)
    line(d, pts(0.28, 0.68, 0.9, 0.68), 0.09); arrowhead(d, P(0.06, 0.68), (-1, 0), 0.26)


def aim(d):
    ring(d, 0.5, 0.5, 0.36, 0.065)
    for (a, b) in (((0.5, 0.02), (0.5, 0.3)), ((0.5, 0.7), (0.5, 0.98)), ((0.02, 0.5), (0.3, 0.5)), ((0.7, 0.5), (0.98, 0.5))):
        line(d, [P(*a), P(*b)], 0.06)
    circle(d, 0.5, 0.5, 0.05)


def fire(d):        # a round with sparks off its tip
    d.rectangle([P(0.38, 0.42), P(0.62, 0.95)], fill=ON)
    d.rectangle([P(0.35, 0.86), P(0.65, 0.95)], fill=ON)
    d.polygon(pts(0.38, 0.42, 0.38, 0.3, 0.5, 0.12, 0.62, 0.3, 0.62, 0.42), fill=ON)
    d.line(pts(0.36, 0.44, 0.64, 0.44), fill=OFF, width=int(0.025 * W))
    for (a, b) in (((0.5, 0.0), (0.5, 0.05)), ((0.24, 0.12), (0.3, 0.18)), ((0.76, 0.12), (0.7, 0.18)), ((0.18, 0.3), (0.26, 0.31)), ((0.82, 0.3), (0.74, 0.31))):
        line(d, [P(*a), P(*b)], 0.035)


def home(d):        # boarded-up house
    d.polygon(pts(0.5, 0.06, 0.96, 0.46, 0.84, 0.46, 0.84, 0.94, 0.16, 0.94, 0.16, 0.46, 0.04, 0.46), fill=ON)
    d.rectangle([P(0.42, 0.62), P(0.6, 0.94)], fill=OFF)
    d.rectangle([P(0.24, 0.5), P(0.38, 0.64)], fill=OFF)
    line(d, pts(0.21, 0.5, 0.41, 0.63), 0.035); line(d, pts(0.21, 0.63, 0.41, 0.52), 0.035)
    d.rectangle([P(0.66, 0.12), P(0.76, 0.3)], fill=ON)          # chimney


def play(d):
    d.polygon(pts(0.2, 0.08, 0.9, 0.5, 0.2, 0.92), fill=ON)


def bust(d, cx, cy, s, fill=ON):
    circle(d, cx, cy - 0.17 * s, 0.13 * s, fill)
    d.pieslice([P(cx - 0.3 * s, cy + 0.0 * s), P(cx + 0.3 * s, cy + 0.6 * s)], 180, 360, fill=fill)


def squad(d):
    bust(d, 0.24, 0.5, 0.8); bust(d, 0.76, 0.5, 0.8)
    bust(d, 0.5, 0.48, 1.12, OFF)
    # carve around the front one, then draw it
    circle(d, 0.5, 0.48 - 0.19, 0.17, OFF)
    bust(d, 0.5, 0.48, 1.0)


def solo(d):
    bust(d, 0.5, 0.45, 1.45)
    d.line(pts(0.36, 0.5, 0.42, 0.92), fill=OFF, width=int(0.03 * W))     # backpack strap


def link(d):
    for (cx, cy) in ((0.36, 0.64), (0.64, 0.36)):
        layer = Image.new("L", (W, W), 0)
        ld = ImageDraw.Draw(layer)
        ld.rounded_rectangle([P(0.5 - 0.25, 0.5 - 0.13), P(0.5 + 0.25, 0.5 + 0.13)], radius=0.13 * W, outline=ON, width=int(0.075 * W))
        layer = layer.rotate(45, resample=Image.BICUBIC, center=P(0.5, 0.5), translate=((cx - 0.5) * W, (cy - 0.5) * W))
        d._image.paste(255, (0, 0), layer.point(lambda v: 255 if v > 128 else 0))


def close(d):
    line(d, pts(0.16, 0.16, 0.84, 0.84), 0.13); line(d, pts(0.84, 0.16, 0.16, 0.84), 0.13)


def star_pts(cx, cy, r0, r1):
    return [P(cx + (r1 if k % 2 == 0 else r0) * math.sin(k * math.pi / 5), cy - (r1 if k % 2 == 0 else r0) * math.cos(k * math.pi / 5)) for k in range(10)]


def star(d):
    d.polygon(star_pts(0.5, 0.53, 0.2, 0.47), fill=ON)


def star_empty(d):
    d.polygon(star_pts(0.5, 0.53, 0.2, 0.47), fill=ON)
    d.polygon(star_pts(0.5, 0.53, 0.11, 0.3), fill=OFF)


def lock(d):
    d.arc([P(0.26, 0.06), P(0.74, 0.62)], 180, 360, fill=ON, width=int(0.09 * W))
    d.line(pts(0.3, 0.34, 0.3, 0.46), fill=ON, width=int(0.09 * W)); d.line(pts(0.7, 0.34, 0.7, 0.46), fill=ON, width=int(0.09 * W))
    d.rounded_rectangle([P(0.16, 0.44), P(0.84, 0.94)], radius=0.06 * W, fill=ON)
    circle(d, 0.5, 0.64, 0.07, OFF); d.rectangle([P(0.47, 0.64), P(0.53, 0.8)], fill=OFF)


def shoulder(d):    # swap the camera to the other shoulder
    bust(d, 0.5, 0.46, 1.05)
    line(d, pts(0.2, 0.86, 0.8, 0.86), 0.06)
    arrowhead(d, P(0.04, 0.86), (-1, 0), 0.18); arrowhead(d, P(0.96, 0.86), (1, 0), 0.18)


def sound(d):
    d.polygon(pts(0.06, 0.36, 0.26, 0.36, 0.5, 0.14, 0.5, 0.86, 0.26, 0.64, 0.06, 0.64), fill=ON)
    d.arc([P(0.42, 0.28), P(0.72, 0.72)], -55, 55, fill=ON, width=int(0.07 * W))
    d.arc([P(0.44, 0.1), P(0.94, 0.9)], -55, 55, fill=ON, width=int(0.07 * W))


SET = dict(health=health, armor=armor, scrap=scrap, skull=skull, hand=hand, radar=radar, turret=turret, expand=expand,
           pause=pause, gear=gear, eye=eye, reload=reload, jump=jump, crouch=crouch, swap=swap, aim=aim, fire=fire,
           home=home, play=play, squad=squad, solo=solo, link=link, close=close, star=star, star_empty=star_empty,
           lock=lock, shoulder=shoulder, sound=sound)

if __name__ == "__main__":
    for i, (n, fn) in enumerate(SET.items()):
        icon(n, fn, seed=i + 1)
