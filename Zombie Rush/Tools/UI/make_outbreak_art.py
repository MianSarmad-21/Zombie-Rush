"""Outbreak UI art for Zombie Rush (Assets/UI/Outbreak). Run: python Tools/UI/make_outbreak_art.py

The look: paint and blood on a dark night. Brush strokes (with and without drips) carry the numbers and
buttons, blood splats and claw scratches decorate, dark grunge panels hold the menus, glass discs make the
touch controls, and a night skyline with a moon and a horde backs the lobby.
White masks are tinted in Unity; pieces named in BAKED keep their own colours.
Needs only Pillow. Run make_outbreak_icons.py first: the rating skulls are made from its skull.
"""
import math, os, random
from PIL import Image, ImageDraw, ImageFilter, ImageChops

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "UI", "Outbreak")
os.makedirs(OUT, exist_ok=True)
SS = 2


def noise(w, h, sigma=60, blur=0):
    n = Image.effect_noise((max(1, w), max(1, h)), sigma)
    return n.filter(ImageFilter.GaussianBlur(blur)) if blur else n


def save(img, name):
    img.save(os.path.join(OUT, name + ".png"))
    print("wrote", name, img.size)


def down(img, w, h):
    return img.resize((w, h), Image.LANCZOS)


def white_with(mask, lum_lo=215):
    """A white RGBA image whose alpha is the mask, with a little paint texture in the brightness."""
    W, H = mask.size
    lum = noise(W // 3, H // 3, 70, 0.8).resize((W, H), Image.BICUBIC).point(lambda v: lum_lo + v * (255 - lum_lo) // 255)
    img = Image.merge("RGBA", (lum, lum, lum, mask))
    return img


def hgrad(W, H, fn):
    """L image whose value depends on x only: fn(t in 0..1) -> 0..255."""
    row = Image.new("L", (W, 1))
    row.putdata([max(0, min(255, int(fn(x / (W - 1))))) for x in range(W)])
    return row.resize((W, H))


def vgrad(W, H, fn):
    col = Image.new("L", (1, H))
    col.putdata([max(0, min(255, int(fn(y / (H - 1))))) for y in range(H)])
    return col.resize((W, H))


# ------------------------------------------------------------------ brush stroke (9-slice left/right)
def stroke(name, w, h, band=(0.16, 0.84), drips=0, seed=1, bristles=90):
    random.seed(seed)
    W, H = w * SS, h * SS
    top, bot = band[0] * H, band[1] * H
    mid = (top + bot) / 2
    mask = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(mask)

    # body: rough-edged band, ragged ends
    n = 70
    xl, xr = W * 0.045, W * 0.955
    up, dn = [], []
    yt, yb = top, bot
    for i in range(n + 1):
        x = xl + (xr - xl) * i / n
        yt = min(mid - H * 0.18, max(top - H * 0.03, yt + random.uniform(-1, 1) * H * 0.018))
        yb = max(mid + H * 0.18, min(bot + H * 0.03, yb + random.uniform(-1, 1) * H * 0.018))
        up.append((x, yt)); dn.append((x, yb))
    right = [(xr + random.uniform(-0.02, 0.035) * W, top + (bot - top) * k / 8) for k in range(1, 8)]
    left = [(xl + random.uniform(-0.035, 0.02) * W, bot - (bot - top) * k / 8) for k in range(1, 8)]
    d.polygon(up + right + dn[::-1] + left, fill=255)

    # bristles on their own layer (so they never carve the body), mostly along the edges
    hair = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(hair)
    for _ in range(bristles):
        edge_y = top if random.random() < 0.5 else bot
        y = random.gauss(edge_y, (bot - top) * 0.07)
        y = min(bot + H * 0.04, max(top - H * 0.04, y))
        th = random.uniform(1.0, 5.5) * SS
        xs = random.uniform(0.0, 0.10) * W
        xe = W - random.uniform(0.0, 0.12) * W
        a = random.randint(120, 255)
        amp = random.uniform(0.0, 0.012) * H
        ph = random.uniform(0, 6.28)
        pts = [(xs + (xe - xs) * t / 24, y + math.sin(ph + t * 0.6) * amp) for t in range(25)]
        d.line(pts, fill=a, width=max(1, int(th)))
    mask = ImageChops.lighter(mask, hair)
    d = ImageDraw.Draw(mask)

    # drips hanging off the bottom edge
    for _ in range(drips):
        x = random.uniform(0.12, 0.88) * W
        # find the bottom edge near x
        k = int((x - xl) / (xr - xl) * n); k = max(0, min(n, k))
        y0 = dn[k][1] - 3 * SS
        L = random.uniform(0.08, 0.42) * H * (1.6 if random.random() < 0.25 else 1.0)
        L = min(L, H - y0 - 14 * SS)
        wd = random.uniform(5, 13) * SS
        d.line([(x, y0), (x, y0 + L)], fill=245, width=int(wd))
        r = wd * random.uniform(0.6, 0.85)
        d.ellipse([x - r, y0 + L - r * 0.6, x + r, y0 + L + r * 1.2], fill=245)
        # a fillet where the drip leaves the stroke
        d.polygon([(x - wd * 1.6, y0 - 2 * SS), (x + wd * 1.6, y0 - 2 * SS), (x, y0 + wd * 2.2)], fill=245)

    # dry-brush gaps: strong at the ends, light in the middle
    streak = noise(max(2, W // 90), H // SS, 90).resize((W, H), Image.BICUBIC)
    gaps = ImageChops.invert(streak.point(lambda v: 255 if v > 95 else int(v * 2.6)))
    weight = hgrad(W, H, lambda t: 12 + 230 * max(0.0, (abs(t - 0.5) - 0.28) / 0.22) ** 1.4)
    keep = ImageChops.invert(ImageChops.multiply(gaps, weight))
    mask = ImageChops.multiply(mask, keep)
    if drips:   # drips stay solid where they hang below the band
        solid = vgrad(W, H, lambda t: 255 if t > band[1] + 0.03 else 0)
        mask = ImageChops.lighter(mask, ImageChops.multiply(mask.point(lambda v: 255 if v > 200 else 0), solid))
    mask = mask.filter(ImageFilter.GaussianBlur(0.5 * SS))
    save(down(white_with(mask), w, h), name)


# ------------------------------------------------------------------ thin scratched line (underlines, dividers)
def scratch_line(name, w=1024, h=48, seed=3):
    random.seed(seed)
    W, H = w * SS, h * SS
    m = Image.new("L", (W, H), 0); d = ImageDraw.Draw(m)
    for k in range(5):
        y = H * 0.5 + random.uniform(-0.18, 0.18) * H
        xs, xe = random.uniform(0, 0.08) * W, W - random.uniform(0, 0.1) * W
        steps = 40
        for i in range(steps):
            t0, t1 = i / steps, (i + 1) / steps
            th = (math.sin(math.pi * (t0 + t1) / 2) ** 0.6) * H * random.uniform(0.10, 0.22)
            yy = y + math.sin(t0 * 9 + k) * H * 0.03
            d.line([(xs + (xe - xs) * t0, yy), (xs + (xe - xs) * t1, yy)], fill=random.randint(170, 255), width=max(1, int(th)))
    m = m.filter(ImageFilter.GaussianBlur(0.4 * SS))
    save(down(white_with(m), w, h), name)


# ------------------------------------------------------------------ blood splat
def splat(name, size=512, seed=7, spikes=26, drops=60):
    random.seed(seed)
    W = size * SS; c = W / 2
    m = Image.new("L", (W, W), 0); d = ImageDraw.Draw(m)
    R = W * 0.2
    for _ in range(26):
        a = random.uniform(0, 6.28); r = random.uniform(0, R * 0.55)
        rr = random.uniform(0.35, 0.75) * R
        x, y = c + math.cos(a) * r, c + math.sin(a) * r
        d.ellipse([x - rr, y - rr, x + rr, y + rr], fill=255)
    for _ in range(spikes):
        a = random.uniform(0, 6.28); L = random.uniform(0.25, 0.47) * W
        wd = random.uniform(0.012, 0.035) * W
        ex, ey = c + math.cos(a) * L, c + math.sin(a) * L
        px, py = -math.sin(a) * wd, math.cos(a) * wd
        bx, by = c + math.cos(a) * R * 0.6, c + math.sin(a) * R * 0.6
        d.polygon([(bx + px * 2, by + py * 2), (ex, ey), (bx - px * 2, by - py * 2)], fill=255)
        r = wd * random.uniform(0.6, 1.3)
        d.ellipse([ex - r, ey - r, ex + r, ey + r], fill=255)
    for _ in range(drops):
        a = random.uniform(0, 6.28); L = random.uniform(0.28, 0.49) * W
        r = random.uniform(0.003, 0.016) * W
        x, y = c + math.cos(a) * L, c + math.sin(a) * L
        d.ellipse([x - r, y - r, x + r, y + r], fill=255)
    # wobble the edge, then make it crisp again
    m = m.filter(ImageFilter.GaussianBlur(3 * SS)).point(lambda v: 255 if v > 120 else 0)
    m = m.filter(ImageFilter.GaussianBlur(0.6 * SS))
    # wet look: brighter inside, darker rim (tinted red it reads as blood)
    inner = m.filter(ImageFilter.MinFilter(9)).filter(ImageFilter.GaussianBlur(6 * SS))
    lum = ImageChops.lighter(inner.point(lambda v: 150 + v * 105 // 255), Image.new("L", (W, W), 150))
    img = Image.merge("RGBA", (lum, lum, lum, m))
    save(down(img, size, size), name)


# ------------------------------------------------------------------ claw scratches
def claw(name, size=512, seed=11):
    random.seed(seed)
    W = size * SS
    m = Image.new("L", (W, W), 0); d = ImageDraw.Draw(m)
    for i in (-1, 0, 1, 2):
        if i == 2 and random.random() < 0.5: continue
        off = i * W * 0.13
        p0 = (W * 0.18 + off, W * 0.08); p1 = (W * 0.42 + off * 1.1, W * 0.45); p2 = (W * 0.62 + off * 1.2, W * 0.92)
        steps = 120
        for s in range(steps + 1):
            t = s / steps
            x = (1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t * t * p2[0]
            y = (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t * t * p2[1]
            r = W * 0.028 * math.sin(math.pi * t) ** 0.7 * (1 - abs(i) * 0.12) + random.uniform(-0.002, 0.002) * W
            if r > 0: d.ellipse([x - r, y - r, x + r, y + r], fill=255)
    m = m.filter(ImageFilter.GaussianBlur(0.8 * SS))
    # a lighter core inside each gash
    core = m.filter(ImageFilter.MinFilter(7))
    lum = ImageChops.lighter(core, Image.new("L", (W, W), 170))
    save(down(Image.merge("RGBA", (lum, lum, lum, m)), size, size), name)


# ------------------------------------------------------------------ dark grunge panel (baked, 9-slice)
def panel(name, size=512, seed=5, alpha=255):
    random.seed(seed)
    W = size * SS; e = 18 * SS
    shape = Image.new("L", (W, W), 0); d = ImageDraw.Draw(shape)
    pts = []
    n = 40
    for side in range(4):
        for i in range(n):
            t = i / n
            j = random.uniform(-1, 1) * 5 * SS
            if side == 0: pts.append((e + (W - 2 * e) * t, e + j))
            elif side == 1: pts.append((W - e + j, e + (W - 2 * e) * t))
            elif side == 2: pts.append((W - e - (W - 2 * e) * t, W - e + j))
            else: pts.append((e + j, W - e - (W - 2 * e) * t))
    d.polygon(pts, fill=alpha)
    shape = shape.filter(ImageFilter.GaussianBlur(0.6 * SS))
    grime = noise(W // 6, W // 6, 60, 1.5).resize((W, W), Image.BICUBIC)
    base = grime.point(lambda v: 12 + v * 16 // 255)
    rgb = Image.merge("RGB", (base.point(lambda v: v + 3), base, base))
    sd = ImageDraw.Draw(rgb)
    for _ in range(40):
        x, y = random.uniform(0, W), random.uniform(0, W)
        a = random.uniform(0, math.pi); L = random.uniform(20, 120) * SS
        g = random.randint(36, 52)
        sd.line([(x, y), (x + math.cos(a) * L, y + math.sin(a) * L)], fill=(g, g - 2, g - 3), width=SS)
    # edge: thin pale rim, worn
    rim = ImageChops.subtract(shape, shape.filter(ImageFilter.MinFilter(5)))
    wear = noise(W // 2, W // 2, 90).resize((W, W)).point(lambda v: 255 if v > 110 else 0)
    rim = ImageChops.multiply(rim, wear).point(lambda v: v * 3 // 5)
    rgb = Image.composite(Image.new("RGB", (W, W), (150, 140, 130)), rgb, rim)
    img = rgb.convert("RGBA"); img.putalpha(shape)
    save(down(img, size, size), name)


# ------------------------------------------------------------------ touch discs
def disc(name, size=256):
    W = size * SS; c = W / 2
    a = Image.new("L", (W, W), 0); d = ImageDraw.Draw(a)
    r = W * 0.48
    d.ellipse([c - r, c - r, c + r, c + r], fill=150)
    glow = vgrad(W, W, lambda t: 40 - 40 * t)       # slightly lighter at the top, like glass
    rgb = Image.merge("RGB", (glow.point(lambda v: 10 + v), glow.point(lambda v: 10 + v), glow.point(lambda v: 12 + v)))
    ring = Image.new("L", (W, W), 0); rd = ImageDraw.Draw(ring)
    rd.ellipse([c - r, c - r, c + r, c + r], outline=230, width=int(3.5 * SS))
    rgb = Image.composite(Image.new("RGB", (W, W), (225, 218, 205)), rgb, ring)
    a = ImageChops.lighter(a, ring)
    a = a.filter(ImageFilter.GaussianBlur(0.5 * SS))
    img = rgb.convert("RGBA"); img.putalpha(a)
    save(down(img, size, size), name)


def ring(name, size=256, width=0.07, ticks=0, seed=9):
    """Painted ring mask (tinted in Unity). Optional tick marks around it."""
    random.seed(seed)
    W = size * SS; c = W / 2
    m = Image.new("L", (W, W), 0); d = ImageDraw.Draw(m)
    r = W * 0.47
    steps = 180
    for s in range(steps):
        a0 = s / steps * 6.283
        wv = width * W * (0.75 + 0.25 * math.sin(a0 * 3 + 1) + random.uniform(-0.08, 0.08))
        x, y = c + math.cos(a0) * (r - wv / 2), c + math.sin(a0) * (r - wv / 2)
        d.ellipse([x - wv / 2, y - wv / 2, x + wv / 2, y + wv / 2], fill=255)
    for k in range(ticks):
        a0 = k / ticks * 6.283
        r0, r1 = r * 0.78, r * 0.86
        d.line([(c + math.cos(a0) * r0, c + math.sin(a0) * r0), (c + math.cos(a0) * r1, c + math.sin(a0) * r1)], fill=200, width=int(2.5 * SS))
    m = m.filter(ImageFilter.GaussianBlur(0.5 * SS))
    save(down(white_with(m, 230), size, size), name)


# ------------------------------------------------------------------ bar + keycap + bullet
def bar(name, w=256, h=48, seed=4):
    random.seed(seed)
    W, H = w * SS, h * SS; s = H * 0.35
    m = Image.new("L", (W, H), 0); d = ImageDraw.Draw(m)
    top = [(s + (W - s - 4 * SS) * i / 30, 3 * SS + random.uniform(0, 2.5) * SS) for i in range(31)]
    botm = [(W - s - 4 * SS - (W - s - 4 * SS) * i / 30 + 0, H - 3 * SS - random.uniform(0, 2.5) * SS) for i in range(31)]
    d.polygon(top + [(W - 3 * SS, 3 * SS)] + botm + [(3 * SS, H - 3 * SS)], fill=255)
    m = m.filter(ImageFilter.GaussianBlur(0.5 * SS))
    save(down(white_with(m, 225), w, h), name)


def keycap(name, size=128, seed=6):
    random.seed(seed)
    W = size * SS; e = 8 * SS
    a = Image.new("L", (W, W), 0); d = ImageDraw.Draw(a)
    d.rounded_rectangle([e, e, W - e, W - e], radius=14 * SS, fill=215)
    ring_ = Image.new("L", (W, W), 0); rd = ImageDraw.Draw(ring_)
    rd.rounded_rectangle([e, e, W - e, W - e], radius=14 * SS, outline=255, width=3 * SS)
    rgb = Image.composite(Image.new("RGB", (W, W), (220, 212, 198)), Image.new("RGB", (W, W), (14, 13, 13)), ring_)
    a = ImageChops.lighter(a, ring_)
    img = rgb.convert("RGBA"); img.putalpha(a)
    save(down(img, size, size), name)


def bullet(name, w=48, h=128):
    W, H = w * SS, h * SS
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
    cx = W / 2; bw = W * 0.36
    d.rectangle([cx - bw, H * 0.38, cx + bw, H * 0.97], fill=(190, 140, 60, 255))           # brass case
    d.rectangle([cx - bw, H * 0.38, cx - bw * 0.35, H * 0.97], fill=(235, 190, 105, 255))   # highlight
    d.rectangle([cx - bw * 1.05, H * 0.90, cx + bw * 1.05, H * 0.97], fill=(150, 105, 40, 255))
    d.polygon([(cx - bw, H * 0.40), (cx - bw, H * 0.22), (cx, H * 0.03), (cx + bw, H * 0.22), (cx + bw, H * 0.40)], fill=(170, 95, 60, 255))
    d.polygon([(cx - bw, H * 0.40), (cx - bw, H * 0.22), (cx - bw * 0.2, H * 0.08), (cx - bw * 0.35, H * 0.40)], fill=(215, 140, 95, 255))
    save(down(img, w, h), name)


# ------------------------------------------------------------------ vignette mask
def vignette(name, size=512, inner=0.42, power=1.6):
    W = size
    m = Image.new("L", (W, W))
    px = []
    for y in range(W):
        for x in range(W):
            dx, dy = (x - W / 2) / (W / 2), (y - W / 2) / (W / 2)
            r = math.sqrt(dx * dx + dy * dy) / math.sqrt(2)
            v = max(0.0, (r - inner * 0.7) / (1 - inner * 0.7))
            px.append(int(255 * min(1.0, v ** power * 1.25)))
    m.putdata(px)
    img = Image.merge("RGBA", (Image.new("L", (W, W), 255),) * 3 + (m,))
    save(img, name)


# ------------------------------------------------------------------ lobby backdrop: night skyline, moon, horde
def zombie(d, x, base, h, color, flip, seed):
    """A shambling silhouette: hunched torso with shoulders, head hung forward, thick limbs with round joints,
    arms reaching ahead, a torn shirt hem."""
    rnd = random.Random(seed)
    f = -1 if flip else 1
    lean = rnd.uniform(0.10, 0.24)
    lw = max(3, h * 0.07)

    def limb(pts, wd):
        d.line(pts, fill=color, width=int(wd))
        for (px, py) in pts:
            r = wd / 2
            d.ellipse([px - r, py - r, px + r, py + r], fill=color)

    hip = (x, base - h * 0.47)
    sh_c = (x + f * lean * h * 0.85, base - h * 0.80)          # shoulder centre, pushed forward
    sw, hw = h * 0.13, h * 0.09
    # legs: a dragging step
    step = rnd.uniform(0.06, 0.15) * h
    limb([(hip[0] - f * hw * 0.4, hip[1]), (x - f * step * 0.2, base - h * 0.24), (x - f * step, base - lw * 0.4)], lw * 1.15)
    limb([(hip[0] + f * hw * 0.4, hip[1]), (x + f * step * 0.7, base - h * 0.23), (x + f * step * 0.45, base - lw * 0.4)], lw * 1.15)
    # torso with a ragged hem
    hem = [(hip[0] + f * hw * (1.1 - 2.2 * k / 6), hip[1] + rnd.uniform(0.0, 0.06) * h) for k in range(7)]
    d.polygon([(sh_c[0] - f * sw, sh_c[1] + h * 0.02), (sh_c[0] + f * sw * 0.9, sh_c[1] - h * 0.01)] + hem, fill=color)
    # head hung low and forward
    hr = h * 0.082
    hx, hy = sh_c[0] + f * h * 0.07, sh_c[1] - hr * 0.55 + rnd.uniform(0.0, 0.03) * h
    limb([(sh_c[0], sh_c[1]), (hx - f * hr * 0.3, hy + hr * 0.3)], lw)
    d.ellipse([hx - hr, hy - hr * 1.12, hx + hr, hy + hr * 1.0], fill=color)
    # arms reaching forward, one higher
    for k in range(2):
        s0 = (sh_c[0] + f * sw * (0.55 if k else -0.1), sh_c[1] + h * 0.03)
        reach = rnd.uniform(0.26, 0.38) * h
        drop = (rnd.uniform(-0.05, 0.03) if k else rnd.uniform(0.02, 0.12)) * h
        elbow = (s0[0] + f * reach * 0.5, s0[1] + drop * 0.6 + h * 0.025)
        hand = (s0[0] + f * reach, s0[1] + drop)
        limb([s0, elbow, hand], lw * 0.9)
        for c in range(3):   # fingers
            a = (-0.35 + c * 0.35)
            d.line([hand, (hand[0] + f * math.cos(a) * h * 0.045, hand[1] + math.sin(a) * h * 0.045)], fill=color, width=max(1, int(lw * 0.3)))


def backdrop(name, w=1920, h=1080, seed=13):
    random.seed(seed)
    W, H = w * SS, h * SS
    sky = Image.new("RGB", (W, H))
    top, mid, hor = (5, 7, 13), (14, 14, 24), (58, 26, 30)
    col = []
    for y in range(H):
        t = y / (H - 1)
        if t < 0.55:
            k = t / 0.55; c = tuple(int(top[i] + (mid[i] - top[i]) * k) for i in range(3))
        else:
            k = min(1.0, (t - 0.55) / 0.25); c = tuple(int(mid[i] + (hor[i] - mid[i]) * k) for i in range(3))
        col.append(c)
    colimg = Image.new("RGB", (1, H)); colimg.putdata(col)
    sky = colimg.resize((W, H))

    # moon with halo
    mx, my, mr = W * 0.70, H * 0.30, H * 0.15
    halo = Image.new("L", (W, H), 0); hd = ImageDraw.Draw(halo)
    hd.ellipse([mx - mr * 2.6, my - mr * 2.6, mx + mr * 2.6, my + mr * 2.6], fill=70)
    halo = halo.filter(ImageFilter.GaussianBlur(mr * 0.9))
    sky = Image.composite(Image.new("RGB", (W, H), (150, 110, 105)), sky, halo)
    moon = Image.new("L", (W, H), 0); md = ImageDraw.Draw(moon)
    md.ellipse([mx - mr, my - mr, mx + mr, my + mr], fill=255)
    moon = moon.filter(ImageFilter.GaussianBlur(1.2 * SS))
    craters = noise(int(mr // 6), int(mr // 6), 70, 1.0).resize((W, H), Image.BICUBIC).point(lambda v: 175 + v * 80 // 255)
    moonrgb = Image.merge("RGB", (craters.point(lambda v: min(255, v + 8)), craters.point(lambda v: v - 5), craters.point(lambda v: v - 22)))
    sky = Image.composite(moonrgb, sky, moon)

    # drifting cloud bands across the moon
    clouds = noise(W // 48, H // 96, 90, 1.2).resize((W, H), Image.BICUBIC)
    clouds = clouds.point(lambda v: max(0, (v - 120) * 2))
    band = vgrad(W, H, lambda t: 255 * max(0.0, 1 - abs(t - 0.33) / 0.2))
    clouds = ImageChops.multiply(clouds, band)
    sky = Image.composite(Image.new("RGB", (W, H), (16, 14, 20)), sky, clouds.point(lambda v: v * 3 // 4))

    d = ImageDraw.Draw(sky)
    # three layers of broken city
    def skyline(base, hmin, hmax, color, windows=0.0, broken=0.4):
        x = -random.uniform(0, 80) * SS
        while x < W:
            bw = random.uniform(70, 190) * SS
            bh = random.uniform(hmin, hmax) * H
            y0 = base - bh
            pts = [(x, base), (x, y0)]
            if random.random() < broken:   # a jagged, collapsed top
                k = random.randint(3, 6)
                for i in range(1, k):
                    pts.append((x + bw * i / k, y0 + random.uniform(0, 0.07) * bh))
            pts += [(x + bw, y0 + (random.uniform(0, 0.12) * bh if random.random() < broken else 0)), (x + bw, base)]
            d.polygon(pts, fill=color)
            if random.random() < 0.25:
                ax = x + random.uniform(0.2, 0.8) * bw
                d.line([(ax, y0), (ax, y0 - random.uniform(20, 60) * SS)], fill=color, width=2 * SS)
            if windows > 0:
                for wy in range(int(y0 + 14 * SS), int(base - 10 * SS), int(18 * SS)):
                    for wx in range(int(x + 10 * SS), int(x + bw - 10 * SS), int(16 * SS)):
                        if random.random() < windows:
                            c = random.choice([(150, 100, 45), (120, 85, 40), (90, 140, 70)])
                            d.rectangle([wx, wy, wx + 5 * SS, wy + 7 * SS], fill=c)
            x += bw + random.uniform(-10, 30) * SS
    skyline(H * 0.80, 0.22, 0.48, (24, 21, 30), 0.0, 0.5)
    skyline(H * 0.84, 0.16, 0.36, (14, 13, 18), 0.012, 0.6)

    # ground fog over the horizon
    fog = vgrad(W, H, lambda t: 150 * max(0.0, 1 - abs(t - 0.80) / 0.09))
    fog = ImageChops.multiply(fog, noise(W // 64, H // 64, 60, 1).resize((W, H), Image.BICUBIC).point(lambda v: 120 + v // 2))
    sky = Image.composite(Image.new("RGB", (W, H), (70, 40, 44)), sky, fog)
    d = ImageDraw.Draw(sky)
    d.rectangle([0, H * 0.90, W, H], fill=(6, 6, 7))

    # the horde walking in from the right
    for k in range(20):
        x = W * (0.36 + 0.66 * k / 19 + random.uniform(-0.02, 0.02))
        depth = random.uniform(0, 1)
        hh = (110 + depth * 170) * SS
        base = H * (0.905 + depth * 0.08)
        zombie(d, x, base, hh, (3, 3, 4), flip=True, seed=k)
    for k in range(6):
        x = W * random.uniform(0.02, 0.3)
        zombie(d, x, H * random.uniform(0.92, 0.99), random.uniform(120, 220) * SS, (5, 5, 6), flip=False, seed=100 + k)

    # red edge light + grain
    v = Image.new("L", (W, H), 0)
    vd = ImageDraw.Draw(v); vd.rectangle([0, 0, W, H], fill=0)
    edge = hgrad(W, H, lambda t: 110 * max(0.0, 1 - t / 0.35) ** 2)
    sky = Image.composite(Image.new("RGB", (W, H), (60, 6, 8)), sky, edge)
    grain = noise(W // 2, H // 2, 22).resize((W, H))
    sky = ImageChops.add(sky, Image.merge("RGB", (grain,) * 3).point(lambda p: max(0, p - 128) // 6))
    save(down(sky, w, h), name)


# ------------------------------------------------------------------ left-to-right fade (lobby text side)
def fade_left(name, w=512, h=16):
    m = hgrad(w, h, lambda t: 255 * (1 - t) ** 1.3)
    save(Image.merge("RGBA", (Image.new("L", (w, h), 255),) * 3 + (m,)), name)


# ------------------------------------------------------------------ rating skulls for the game over screen (baked)
def skulls(icon_path):
    src = Image.open(icon_path).convert("RGBA")
    a = src.split()[3]
    glow = a.filter(ImageFilter.GaussianBlur(src.size[0] * 0.06)).point(lambda v: v * 3 // 4)
    on = Image.new("RGBA", src.size, (0, 0, 0, 0))
    on.paste(Image.new("RGBA", src.size, (150, 10, 10, 255)), (0, 0), glow)
    on.paste(Image.new("RGBA", src.size, (220, 32, 26, 255)), (0, 0), a)
    save(on, "skull_on")
    off = Image.new("RGBA", src.size, (0, 0, 0, 0))
    off.paste(Image.new("RGBA", src.size, (78, 74, 72, 255)), (0, 0), a.point(lambda v: v * 7 // 10))
    save(off, "skull_off")


if __name__ == "__main__":
    stroke("stroke", 1024, 192, seed=1)
    stroke("stroke_b", 1024, 192, seed=2, bristles=130)
    stroke("stroke_drip", 1024, 320, band=(0.08, 0.48), drips=16, seed=3)
    scratch_line("line")
    splat("splat", seed=7)
    splat("splat_b", seed=17, spikes=18, drops=90)
    claw("claw")
    panel("panel")
    disc("disc")
    ring("ring", width=0.07)
    ring("ring_ticks", width=0.025, ticks=24, seed=10)
    bar("bar")
    keycap("key")
    bullet("bullet")
    vignette("vignette")
    backdrop("backdrop")
    fade_left("fade_left")
    skulls(os.path.join(OUT, "Icons", "ic_skull.png"))
