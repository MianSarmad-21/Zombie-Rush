"""Survivor's Kit UI art for Zombie Rush (Assets/UI/Kit). Run: python Tools/UI/make_kit_art.py

Every piece looks like gear a survivor carries: painted steel plates with rivets and worn edges, duct tape,
dog tags, a briefing clipboard with paper, rubber ink stamps, metal discs for the touch controls, a radar
screen, bullets for the magazine counter, and a stencil icon set. White or neutral art is tinted in Unity.
Drawn at 4x and scaled down for clean edges. Needs only Pillow.
"""
import math, os, random
from PIL import Image, ImageDraw, ImageFilter, ImageChops, ImageOps

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "UI", "Kit")
ICONS = os.path.join(OUT, "Icons")
os.makedirs(ICONS, exist_ok=True)
random.seed(21)
SS = 4   # supersampling

def noise(w, h, sigma=60, blur=0):
    n = Image.effect_noise((w, h), sigma)
    return n.filter(ImageFilter.GaussianBlur(blur)) if blur else n

def save(img, name, folder=OUT):
    img.save(os.path.join(folder, name + ".png"))
    print("wrote", name, img.size)

def down(img, w, h):
    return img.resize((w, h), Image.LANCZOS)

def rgba(c, a=255):
    return (c[0], c[1], c[2], a)

def tint(gray, color):
    """grey L image -> RGB tinted (multiply)."""
    r, g, b = color
    return Image.merge("RGB", [gray.point(lambda v, k=k: v * k // 255) for k in (r, g, b)])

# ------------------------------------------------------------------ steel plate (9-slice)
def plate(name, w=256, h=256, base=(58, 62, 44), corner=34, rivets=True, wear=1.0, seed=1):
    random.seed(seed)
    W, H = w * SS, h * SS
    shape = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(shape)
    c = corner * SS // 2
    # cut corners (chamfered, like a stamped plate)
    d.polygon([(c, 0), (W - c, 0), (W, c), (W, H - c), (W - c, H), (c, H), (0, H - c), (0, c)], fill=255)
    # paint: base colour with mottled grime
    grime = noise(W // 8, H // 8, 70, 1.2).resize((W, H), Image.BICUBIC)
    paint = tint(grime.point(lambda v: 150 + v * 105 // 255), base)
    # worn edge: bare metal shows near the border
    edge = shape.filter(ImageFilter.GaussianBlur(10 * SS))
    edge = ImageChops.subtract(shape, edge.point(lambda v: min(255, v * 2)))
    scratchy = noise(W // 2, H // 2, 90).resize((W, H)).point(lambda v: 255 if v > 150 else 0)
    wearmask = ImageChops.multiply(edge.point(lambda v: min(255, int(v * 2.5 * wear))), scratchy)
    metal = tint(noise(W, H, 30).point(lambda v: 160 + v // 4), (150, 150, 140))
    paint = Image.composite(metal, paint, wearmask.filter(ImageFilter.GaussianBlur(SS * 0.6)))
    # scratches across the face
    sd = ImageDraw.Draw(paint)
    for _ in range(int(26 * wear)):
        x, y = random.randint(0, W), random.randint(0, H)
        a = random.uniform(0, math.pi); L = random.randint(20, 90) * SS
        col = tuple(min(255, v + 40) for v in base)
        sd.line([(x, y), (x + math.cos(a) * L, y + math.sin(a) * L)], fill=col, width=max(1, SS // 2))
    # bevel: light top-left, dark bottom-right inside the border
    inner = shape.filter(ImageFilter.MinFilter(9))
    hl = ImageChops.subtract(shape, ImageChops.offset(inner, 3 * SS, 3 * SS))
    sh = ImageChops.subtract(shape, ImageChops.offset(inner, -3 * SS, -3 * SS))
    paint = Image.composite(Image.new("RGB", (W, H), tuple(min(255, v + 70) for v in base)), paint, hl.point(lambda v: v // 2))
    paint = Image.composite(Image.new("RGB", (W, H), (12, 12, 10)), paint, sh)
    # rivets in the corners
    if rivets:
        rd = ImageDraw.Draw(paint)
        r = 7 * SS; m = (corner * 0.55 + 12) * SS
        for (x, y) in [(m, m), (W - m, m), (m, H - m), (W - m, H - m)]:
            rd.ellipse([x - r - SS, y - r - SS, x + r + SS, y + r + SS], fill=(10, 10, 8))
            rd.ellipse([x - r, y - r, x + r, y + r], fill=(118, 118, 106))
            rd.ellipse([x - r * 0.55, y - r * 0.75, x + r * 0.2, y - r * 0.1], fill=(190, 188, 170))
    # dark outline
    outline = ImageChops.subtract(shape, shape.filter(ImageFilter.MinFilter(5)))
    paint = Image.composite(Image.new("RGB", (W, H), (8, 8, 6)), paint, outline)
    img = paint.convert("RGBA"); img.putalpha(shape)
    save(down(img, w, h), name)

# ------------------------------------------------------------------ duct tape strip (9-slice horizontally)
def tape(name="tape", w=512, h=110):
    W, H = w * SS, h * SS
    shape = Image.new("L", (W, H), 0); d = ImageDraw.Draw(shape)
    top, bot = 10 * SS, H - 10 * SS
    pts = []
    # torn left edge
    for i in range(14):
        y = top + (bot - top) * i / 13; pts.append((random.randint(0, 14) * SS, y))
    pts += [(W - random.randint(0, 14) * SS, bot)]
    for i in range(14):
        y = bot - (bot - top) * i / 13; pts.append((W - random.randint(0, 14) * SS, y))
    d.polygon(pts, fill=255)
    fabric = Image.new("L", (W, H), 150); fd = ImageDraw.Draw(fabric)
    for x in range(0, W, 3 * SS): fd.line([(x, 0), (x, H)], fill=165, width=SS)
    for y in range(0, H, 3 * SS): fd.line([(0, y), (W, y)], fill=140, width=SS)
    fabric = ImageChops.add(fabric, noise(W, H, 40).point(lambda v: v // 5), 1, -25)
    sheen = Image.new("L", (W, H), 0); ImageDraw.Draw(sheen).rectangle([0, top, W, top + (bot - top) * 0.35], fill=40)
    fabric = ImageChops.add(fabric, sheen.filter(ImageFilter.GaussianBlur(8 * SS)))
    col = tint(fabric, (205, 205, 200))
    edge = ImageChops.subtract(shape, shape.filter(ImageFilter.MinFilter(5)))
    col = Image.composite(Image.new("RGB", (W, H), (70, 70, 66)), col, edge)
    img = col.convert("RGBA"); img.putalpha(shape.point(lambda v: v * 245 // 255))
    save(down(img, w, h), name)

# ------------------------------------------------------------------ dog tag (9-slice)
def dogtag(name="dogtag", w=420, h=200):
    W, H = w * SS, h * SS
    shape = Image.new("L", (W, H), 0); d = ImageDraw.Draw(shape)
    r = H // 2 - 6 * SS
    d.rounded_rectangle([6 * SS, 6 * SS, W - 6 * SS, H - 6 * SS], radius=r, fill=255)
    hole = 15 * SS; cx, cy = 44 * SS, H // 2
    d.ellipse([cx - hole, cy - hole, cx + hole, cy + hole], fill=0)
    brushed = Image.new("L", (W, H), 0); bd = ImageDraw.Draw(brushed)
    for y in range(0, H, SS):
        bd.line([(0, y), (W, y)], fill=150 + random.randint(-25, 25), width=SS)
    brushed = brushed.filter(ImageFilter.GaussianBlur(SS * 0.7))
    col = tint(brushed, (200, 202, 196))
    inner = shape.filter(ImageFilter.MinFilter(13))
    rim = ImageChops.subtract(shape, inner)
    col = Image.composite(Image.new("RGB", (W, H), (235, 235, 228)), col, ImageChops.multiply(rim, Image.linear_gradient("L").resize((W, H)).point(lambda v: 255 - v)))
    col = Image.composite(Image.new("RGB", (W, H), (90, 92, 88)), col, ImageChops.multiply(rim, Image.linear_gradient("L").resize((W, H))))
    # rolled edge line
    ImageDraw.Draw(col).rounded_rectangle([18 * SS, 18 * SS, W - 18 * SS, H - 18 * SS], radius=r - 12 * SS, outline=(120, 122, 116), width=2 * SS)
    edge = ImageChops.subtract(shape, shape.filter(ImageFilter.MinFilter(5)))
    col = Image.composite(Image.new("RGB", (W, H), (40, 40, 38)), col, edge)
    img = col.convert("RGBA"); img.putalpha(shape)
    save(down(img, w, h), name)

# ------------------------------------------------------------------ paper + clipboard
def paper(name="paper", w=512, h=512):
    W, H = w * SS, h * SS
    shape = Image.new("L", (W, H), 0); d = ImageDraw.Draw(shape)
    pts = []
    n = 40
    for i in range(n):   # slightly uneven edges
        pts.append((W * i / n, random.randint(0, 4) * SS))
    for i in range(n):
        pts.append((W - random.randint(0, 4) * SS, H * i / n))
    for i in range(n):
        pts.append((W - W * i / n, H - random.randint(0, 4) * SS))
    for i in range(n):
        pts.append((random.randint(0, 4) * SS, H - H * i / n))
    d.polygon(pts, fill=255)
    grain = noise(W // 2, H // 2, 35).resize((W, H)).point(lambda v: 220 + v // 9)
    col = tint(grain, (232, 222, 192))
    # coffee ring + smudges
    st = Image.new("L", (W, H), 0); sd = ImageDraw.Draw(st)
    cx, cy, rr = W * 0.78, H * 0.80, W * 0.11
    sd.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], outline=120, width=5 * SS)
    for _ in range(5):
        x, y, r2 = random.uniform(0, W), random.uniform(0, H), random.uniform(20, 60) * SS
        sd.ellipse([x - r2, y - r2, x + r2, y + r2], fill=30)
    st = st.filter(ImageFilter.GaussianBlur(4 * SS))
    col = Image.composite(Image.new("RGB", (W, H), (150, 115, 70)), col, st)
    # darker toward the edges
    vig = shape.filter(ImageFilter.GaussianBlur(25 * SS))
    col = Image.composite(col, Image.new("RGB", (W, H), (190, 172, 135)), vig)
    img = col.convert("RGBA"); img.putalpha(shape)
    save(down(img, w, h), name)

def board(name="board", w=512, h=640):
    W, H = w * SS, h * SS
    shape = Image.new("L", (W, H), 0)
    ImageDraw.Draw(shape).rounded_rectangle([0, 0, W - 1, H - 1], radius=26 * SS, fill=255)
    fib = noise(W // 3, H // 3, 50).resize((W, H)).point(lambda v: 120 + v // 3)
    col = tint(fib, (120, 84, 50))
    inner = shape.filter(ImageFilter.MinFilter(15))
    edge = ImageChops.subtract(shape, inner)
    col = Image.composite(Image.new("RGB", (W, H), (70, 46, 26)), col, edge)
    img = col.convert("RGBA"); img.putalpha(shape)
    save(down(img, w, h), name)

def clip(name="clip", w=300, h=120):
    W, H = w * SS, h * SS
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
    d.rounded_rectangle([20 * SS, 30 * SS, W - 20 * SS, H - 8 * SS], radius=14 * SS, fill=(150, 152, 150, 255), outline=(40, 40, 40, 255), width=3 * SS)
    d.rounded_rectangle([W * 0.3, 4 * SS, W * 0.7, 50 * SS], radius=20 * SS, fill=(120, 122, 120, 255), outline=(40, 40, 40, 255), width=3 * SS)
    d.ellipse([W / 2 - 14 * SS, 14 * SS, W / 2 + 14 * SS, 42 * SS], fill=(40, 40, 40, 255))
    d.rectangle([24 * SS, 34 * SS, W - 24 * SS, 46 * SS], fill=(205, 206, 204, 255))
    save(down(img, w, h), name)

# ------------------------------------------------------------------ rubber stamp ring (9-slice, white, tinted)
def stamp(name="stamp", w=360, h=150, inset=10):
    W, H = w * SS, h * SS
    m = Image.new("L", (W, H), 0); d = ImageDraw.Draw(m)
    d.rounded_rectangle([inset * SS, inset * SS, W - inset * SS, H - inset * SS], radius=16 * SS, outline=255, width=9 * SS)
    d.rounded_rectangle([(inset + 16) * SS, (inset + 16) * SS, W - (inset + 16) * SS, H - (inset + 16) * SS], radius=10 * SS, outline=255, width=3 * SS)
    ink = noise(W // 3, H // 3, 90).resize((W, H)).point(lambda v: 255 if v > 70 else 0)
    m = ImageChops.multiply(m, ink).filter(ImageFilter.GaussianBlur(SS * 0.5))
    img = Image.new("RGBA", (W, H), (255, 255, 255, 0)); img.putalpha(m)
    save(down(img, w, h), name)

def stamp_fill(name="stamp_fill", w=360, h=150, inset=10):
    """Solid ink block (a pressed stamp / button face), rough edges."""
    W, H = w * SS, h * SS
    m = Image.new("L", (W, H), 0)
    ImageDraw.Draw(m).rounded_rectangle([inset * SS, inset * SS, W - inset * SS, H - inset * SS], radius=16 * SS, fill=255)
    ink = noise(W // 3, H // 3, 90).resize((W, H)).point(lambda v: 255 if v > 45 else 120)
    m = ImageChops.multiply(m, ink).filter(ImageFilter.GaussianBlur(SS * 0.5))
    img = Image.new("RGBA", (W, H), (255, 255, 255, 0)); img.putalpha(m)
    save(down(img, w, h), name)

# ------------------------------------------------------------------ hazard stripes (tile)
def hazard(name="hazard", s=128):
    W = s * SS
    img = Image.new("RGB", (W, W), (24, 22, 18)); d = ImageDraw.Draw(img)
    step = W // 2
    for k in range(-2, 4):
        x = k * step
        d.polygon([(x, W), (x + step / 2, W), (x + step / 2 + W, 0), (x + W, 0)], fill=(232, 166, 46))
    wear = noise(W, W, 70).point(lambda v: 255 if v > 175 else 0).filter(ImageFilter.GaussianBlur(SS))
    img = Image.composite(Image.new("RGB", (W, W), (60, 54, 40)), img, wear)
    save(down(img.convert("RGBA"), s, s), name)

# ------------------------------------------------------------------ monitor glass (ECG / radar screen, 9-slice)
def screen(name="screen", w=256, h=128):
    W, H = w * SS, h * SS
    shape = Image.new("L", (W, H), 0)
    ImageDraw.Draw(shape).rounded_rectangle([0, 0, W - 1, H - 1], radius=14 * SS, fill=255)
    col = Image.new("RGB", (W, H), (10, 18, 12)); d = ImageDraw.Draw(col)
    for y in range(0, H, 4 * SS): d.line([(0, y), (W, y)], fill=(16, 28, 18), width=SS)
    for x in range(0, W, 24 * SS): d.line([(x, 0), (x, H)], fill=(18, 36, 22), width=SS)
    glow = shape.filter(ImageFilter.GaussianBlur(20 * SS))
    col = Image.composite(col, Image.new("RGB", (W, H), (4, 8, 5)), glow)
    rim = ImageChops.subtract(shape, shape.filter(ImageFilter.MinFilter(9)))
    col = Image.composite(Image.new("RGB", (W, H), (30, 32, 28)), col, rim)
    img = col.convert("RGBA"); img.putalpha(shape)
    save(down(img, w, h), name)

# ------------------------------------------------------------------ bars
def bar(name, w=256, h=40, fill=False):
    W, H = w * SS, h * SS
    shape = Image.new("L", (W, H), 0); d = ImageDraw.Draw(shape)
    sl = H * 0.45
    d.polygon([(sl, 0), (W, 0), (W - sl, H), (0, H)], fill=255)
    if fill:
        col = Image.new("RGB", (W, H), (255, 255, 255)); cd = ImageDraw.Draw(col)
        for x in range(-H, W, 14 * SS):
            cd.polygon([(x, H), (x + 5 * SS, H), (x + 5 * SS + H, 0), (x + H, 0)], fill=(225, 225, 225))
        cd.rectangle([0, 0, W, H * 0.3], fill=(255, 255, 255))
    else:
        col = Image.new("RGB", (W, H), (14, 14, 12))
        rim = ImageChops.subtract(shape, shape.filter(ImageFilter.MinFilter(7)))
        col = Image.composite(Image.new("RGB", (W, H), (110, 108, 96)), col, rim)
    img = col.convert("RGBA"); img.putalpha(shape)
    save(down(img, w, h), name)

# ------------------------------------------------------------------ bullet (magazine tick)
def bullet(name="bullet", w=28, h=72):
    W, H = w * SS, h * SS
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
    d.rectangle([2 * SS, H * 0.42, W - 2 * SS, H - 2 * SS], fill=(205, 160, 70, 255))   # brass case
    d.rectangle([2 * SS, H - 8 * SS, W - 2 * SS, H - 2 * SS], fill=(160, 120, 50, 255))
    d.pieslice([2 * SS, 2 * SS, W - 2 * SS, H * 0.62], 180, 360, fill=(190, 120, 80, 255))  # copper tip
    d.rectangle([2 * SS, H * 0.32, W - 2 * SS, H * 0.44], fill=(190, 120, 80, 255))
    d.line([(W * 0.32, H * 0.45), (W * 0.32, H - 10 * SS)], fill=(245, 215, 140, 255), width=2 * SS)
    save(down(img, w, h), name)

# ------------------------------------------------------------------ touch discs
def disc(name, s=256, ring_hazard=False):
    W = s * SS; c = W / 2
    img = Image.new("RGBA", (W, W), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
    R = W / 2 - 3 * SS
    if ring_hazard:
        haz = Image.open(os.path.join(OUT, "hazard.png")).convert("RGBA").resize((W, W))
        ringm = Image.new("L", (W, W), 0); rd = ImageDraw.Draw(ringm)
        rd.ellipse([c - R, c - R, c + R, c + R], fill=255); rd.ellipse([c - R * 0.8, c - R * 0.8, c + R * 0.8, c + R * 0.8], fill=0)
        img.paste(haz, (0, 0), ringm)
        R2 = R * 0.8
    else:
        d.ellipse([c - R, c - R, c + R, c + R], fill=(30, 32, 26, 235))
        R2 = R * 0.9
    face = noise(W, W, 40, 1).point(lambda v: 90 + v // 5)
    facec = tint(face, (130, 136, 104)).convert("RGBA")
    fm = Image.new("L", (W, W), 0); ImageDraw.Draw(fm).ellipse([c - R2, c - R2, c + R2, c + R2], fill=230)
    img.paste(facec, (0, 0), fm)
    d = ImageDraw.Draw(img)
    d.arc([c - R2, c - R2, c + R2, c + R2], 200, 340, fill=(190, 196, 160, 255), width=3 * SS)
    d.arc([c - R2, c - R2, c + R2, c + R2], 20, 160, fill=(14, 14, 12, 255), width=4 * SS)
    d.ellipse([c - R, c - R, c + R, c + R], outline=(8, 8, 6, 255), width=3 * SS)
    for a in (45, 135, 225, 315):   # screws
        x, y = c + math.cos(math.radians(a)) * R2 * 0.82, c + math.sin(math.radians(a)) * R2 * 0.82
        r = 6 * SS
        d.ellipse([x - r, y - r, x + r, y + r], fill=(70, 72, 62, 255), outline=(10, 10, 8, 255), width=SS)
        d.line([(x - r * 0.7, y), (x + r * 0.7, y)], fill=(20, 20, 18, 255), width=SS)
    save(down(img, s, s), name)

def stick_base(name="stick_base", s=320):
    W = s * SS; c = W / 2; R = W / 2 - 4 * SS
    img = Image.new("RGBA", (W, W), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
    d.ellipse([c - R, c - R, c + R, c + R], fill=(18, 20, 16, 120), outline=(200, 190, 150, 200), width=4 * SS)
    for k in range(48):
        a = math.radians(k * 7.5); L = 14 * SS if k % 6 == 0 else 7 * SS
        d.line([(c + math.cos(a) * (R - 6 * SS), c + math.sin(a) * (R - 6 * SS)), (c + math.cos(a) * (R - 6 * SS - L), c + math.sin(a) * (R - 6 * SS - L))],
               fill=(200, 190, 150, 200 if k % 6 == 0 else 110), width=2 * SS)
    for a in (0, 90, 180, 270):   # chevrons
        x, y = c + math.cos(math.radians(a)) * R * 0.66, c + math.sin(math.radians(a)) * R * 0.66
        ux, uy = math.cos(math.radians(a)), math.sin(math.radians(a)); px, py = -uy, ux
        p = [(x + ux * 12 * SS + px * 12 * SS, y + uy * 12 * SS + py * 12 * SS), (x + ux * 24 * SS, y + uy * 24 * SS), (x + ux * 12 * SS - px * 12 * SS, y + uy * 12 * SS - py * 12 * SS)]
        d.line(p, fill=(232, 166, 46, 200), width=4 * SS)
    save(down(img, s, s), name)

# ------------------------------------------------------------------ icons (white, stencil style)
def icon(name, draw, s=128):
    W = s * SS
    m = Image.new("L", (W, W), 0)
    draw(ImageDraw.Draw(m), W)
    img = Image.new("RGBA", (W, W), (255, 255, 255, 0)); img.putalpha(m)
    save(down(img, s, s), "ic_" + name, ICONS)

def U(W, *p): return [v * W for v in p]

def ic_health(d, W):
    t = 0.14
    d.rectangle(U(W, 0.5 - t, 0.14, 0.5 + t, 0.86), fill=255); d.rectangle(U(W, 0.14, 0.5 - t, 0.86, 0.5 + t), fill=255)
    d.rectangle(U(W, 0.5 - t, 0.46, 0.5 + t, 0.54), fill=0)   # stencil bridge
def ic_armor(d, W):
    d.polygon([(0.5 * W, 0.08 * W), (0.86 * W, 0.2 * W), (0.8 * W, 0.62 * W), (0.5 * W, 0.92 * W), (0.2 * W, 0.62 * W), (0.14 * W, 0.2 * W)], fill=255)
    d.line([(0.5 * W, 0.08 * W), (0.5 * W, 0.92 * W)], fill=0, width=int(0.05 * W))
def ic_scrap(d, W):
    c = W / 2
    for k in range(8):
        a = math.radians(k * 45)
        d.rectangle([c + math.cos(a) * 0.36 * W - 0.07 * W, c + math.sin(a) * 0.36 * W - 0.07 * W, c + math.cos(a) * 0.36 * W + 0.07 * W, c + math.sin(a) * 0.36 * W + 0.07 * W], fill=255)
    d.ellipse(U(W, 0.18, 0.18, 0.82, 0.82), fill=255); d.ellipse(U(W, 0.38, 0.38, 0.62, 0.62), fill=0)
def ic_skull(d, W):
    d.ellipse(U(W, 0.18, 0.1, 0.82, 0.68), fill=255); d.rectangle(U(W, 0.3, 0.55, 0.7, 0.86), fill=255)
    d.ellipse(U(W, 0.28, 0.32, 0.45, 0.5), fill=0); d.ellipse(U(W, 0.55, 0.32, 0.72, 0.5), fill=0)
    d.polygon([(0.5 * W, 0.52 * W), (0.45 * W, 0.62 * W), (0.55 * W, 0.62 * W)], fill=0)
    for x in (0.4, 0.5, 0.6): d.line([(x * W, 0.72 * W), (x * W, 0.86 * W)], fill=0, width=int(0.04 * W))
def ic_hand(d, W):   # zombie hand reaching up
    d.rectangle(U(W, 0.36, 0.55, 0.66, 0.95), fill=255)
    for i, (x, top) in enumerate([(0.3, 0.2), (0.42, 0.08), (0.54, 0.12), (0.66, 0.24)]):
        d.rounded_rectangle([(x - 0.045) * W, top * W, (x + 0.045) * W, 0.62 * W], radius=0.04 * W, fill=255)
    d.rounded_rectangle(U(W, 0.16, 0.45, 0.4, 0.58), radius=0.05 * W, fill=255)
    d.line([(0.36 * W, 0.78 * W), (0.66 * W, 0.72 * W)], fill=0, width=int(0.04 * W))
def ic_radar(d, W):
    for r in (0.42, 0.28, 0.14): d.ellipse(U(W, 0.5 - r, 0.5 - r, 0.5 + r, 0.5 + r), outline=255, width=int(0.05 * W))
    d.pieslice(U(W, 0.08, 0.08, 0.92, 0.92), 300, 345, fill=255)
    d.ellipse(U(W, 0.66, 0.26, 0.76, 0.36), fill=255)
def ic_turret(d, W):
    d.polygon([(0.2 * W, 0.92 * W), (0.5 * W, 0.6 * W), (0.8 * W, 0.92 * W)], fill=255)
    d.rounded_rectangle(U(W, 0.26, 0.38, 0.66, 0.64), radius=0.06 * W, fill=255)
    d.rectangle(U(W, 0.6, 0.44, 0.94, 0.52), fill=255); d.rectangle(U(W, 0.6, 0.54, 0.9, 0.6), fill=255)
def ic_expand(d, W):
    for (sx, sy) in [(-1, -1), (1, -1), (-1, 1), (1, 1)]:
        cx, cy = 0.5 + sx * 0.3, 0.5 + sy * 0.3
        d.polygon([(cx * W, cy * W), ((cx - sx * 0.22) * W, cy * W), (cx * W, (cy - sy * 0.22) * W)], fill=255)
        d.line([((cx - sx * 0.06) * W, (cy - sy * 0.06) * W), ((0.5 + sx * 0.06) * W, (0.5 + sy * 0.06) * W)], fill=255, width=int(0.07 * W))
def ic_pause(d, W):
    d.rectangle(U(W, 0.26, 0.18, 0.42, 0.82), fill=255); d.rectangle(U(W, 0.58, 0.18, 0.74, 0.82), fill=255)
def ic_gear(d, W):
    c = W / 2
    for k in range(8):
        a = math.radians(k * 45); x, y = c + math.cos(a) * 0.36 * W, c + math.sin(a) * 0.36 * W
        d.ellipse([x - 0.09 * W, y - 0.09 * W, x + 0.09 * W, y + 0.09 * W], fill=255)
    d.ellipse(U(W, 0.2, 0.2, 0.8, 0.8), fill=255); d.ellipse(U(W, 0.37, 0.37, 0.63, 0.63), fill=0)
def ic_eye(d, W):
    d.ellipse(U(W, 0.08, 0.26, 0.92, 0.74), fill=255); d.ellipse(U(W, 0.33, 0.33, 0.67, 0.67), fill=0); d.ellipse(U(W, 0.43, 0.43, 0.57, 0.57), fill=255)
def ic_reload(d, W):
    d.arc(U(W, 0.16, 0.16, 0.84, 0.84), 20, 300, fill=255, width=int(0.12 * W))
    d.polygon([(0.86 * W, 0.18 * W), (0.86 * W, 0.48 * W), (0.58 * W, 0.4 * W)], fill=255)
def ic_jump(d, W):
    for k, y in enumerate((0.3, 0.56)):
        d.line([(0.2 * W, (y + 0.18) * W), (0.5 * W, y * W), (0.8 * W, (y + 0.18) * W)], fill=255, width=int(0.11 * W), joint="curve")
def ic_crouch(d, W):
    for y in (0.26, 0.52):
        d.line([(0.2 * W, y * W), (0.5 * W, (y + 0.18) * W), (0.8 * W, y * W)], fill=255, width=int(0.11 * W), joint="curve")
def ic_swap(d, W):
    d.line([(0.18 * W, 0.34 * W), (0.78 * W, 0.34 * W)], fill=255, width=int(0.09 * W)); d.polygon([(0.86 * W, 0.34 * W), (0.66 * W, 0.2 * W), (0.66 * W, 0.48 * W)], fill=255)
    d.line([(0.22 * W, 0.66 * W), (0.82 * W, 0.66 * W)], fill=255, width=int(0.09 * W)); d.polygon([(0.14 * W, 0.66 * W), (0.34 * W, 0.52 * W), (0.34 * W, 0.8 * W)], fill=255)
def ic_aim(d, W):
    d.ellipse(U(W, 0.14, 0.14, 0.86, 0.86), outline=255, width=int(0.07 * W))
    for (a, b, c2, e) in [(0.5, 0.04, 0.5, 0.36), (0.5, 0.64, 0.5, 0.96), (0.04, 0.5, 0.36, 0.5), (0.64, 0.5, 0.96, 0.5)]:
        d.line([(a * W, b * W), (c2 * W, e * W)], fill=255, width=int(0.06 * W))
    d.ellipse(U(W, 0.46, 0.46, 0.54, 0.54), fill=255)
def ic_fire(d, W):   # a cartridge
    d.rectangle(U(W, 0.36, 0.42, 0.64, 0.9), fill=255)
    d.pieslice(U(W, 0.36, 0.1, 0.64, 0.58), 180, 360, fill=255); d.rectangle(U(W, 0.36, 0.33, 0.64, 0.42), fill=255)
    d.line([(0.36 * W, 0.42 * W), (0.64 * W, 0.42 * W)], fill=0, width=int(0.04 * W))
    d.rectangle(U(W, 0.32, 0.84, 0.68, 0.92), fill=255)
def ic_home(d, W):
    d.polygon([(0.5 * W, 0.12 * W), (0.9 * W, 0.5 * W), (0.1 * W, 0.5 * W)], fill=255); d.rectangle(U(W, 0.22, 0.5, 0.78, 0.88), fill=255); d.rectangle(U(W, 0.42, 0.62, 0.58, 0.88), fill=0)
def ic_play(d, W):
    d.polygon([(0.28 * W, 0.16 * W), (0.84 * W, 0.5 * W), (0.28 * W, 0.84 * W)], fill=255)
def ic_squad(d, W):
    for (cx, s) in [(0.32, 0.8), (0.68, 0.8), (0.5, 1.0)]:
        r = 0.11 * s
        d.ellipse([(cx - r) * W, (0.36 - r - 0.04 * s) * W, (cx + r) * W, (0.36 + r - 0.04 * s) * W], fill=255)
        d.pieslice([(cx - 0.2 * s) * W, (0.5) * W, (cx + 0.2 * s) * W, (0.5 + 0.44 * s) * W], 180, 360, fill=255)
def ic_link(d, W):
    d.rounded_rectangle(U(W, 0.08, 0.36, 0.56, 0.64), radius=0.14 * W, outline=255, width=int(0.09 * W))
    d.rounded_rectangle(U(W, 0.44, 0.36, 0.92, 0.64), radius=0.14 * W, outline=255, width=int(0.09 * W))
def ic_solo(d, W):
    d.ellipse(U(W, 0.36, 0.12, 0.64, 0.4), fill=255); d.pieslice(U(W, 0.22, 0.44, 0.78, 1.2), 180, 360, fill=255)
def ic_close(d, W):
    d.line([(0.22 * W, 0.22 * W), (0.78 * W, 0.78 * W)], fill=255, width=int(0.13 * W)); d.line([(0.78 * W, 0.22 * W), (0.22 * W, 0.78 * W)], fill=255, width=int(0.13 * W))
def ic_star(d, W):
    pts = []
    for k in range(10):
        r = 0.42 if k % 2 == 0 else 0.18; a = math.radians(-90 + k * 36)
        pts.append((W / 2 + math.cos(a) * r * W, W / 2 + math.sin(a) * r * W))
    d.polygon(pts, fill=255)
def ic_star_empty(d, W):
    pts = []
    for k in range(10):
        r = 0.42 if k % 2 == 0 else 0.18; a = math.radians(-90 + k * 36)
        pts.append((W / 2 + math.cos(a) * r * W, W / 2 + math.sin(a) * r * W))
    d.line(pts + [pts[0]], fill=255, width=int(0.06 * W), joint="curve")
def ic_lock(d, W):
    d.rounded_rectangle(U(W, 0.2, 0.44, 0.8, 0.9), radius=0.06 * W, fill=255)
    d.arc(U(W, 0.3, 0.14, 0.7, 0.62), 180, 360, fill=255, width=int(0.1 * W))
    d.ellipse(U(W, 0.44, 0.56, 0.56, 0.68), fill=0); d.rectangle(U(W, 0.47, 0.64, 0.53, 0.78), fill=0)
def ic_shoulder(d, W):
    d.ellipse(U(W, 0.08, 0.3, 0.42, 0.64), outline=255, width=int(0.07 * W)); d.ellipse(U(W, 0.58, 0.3, 0.92, 0.64), fill=255)
    d.line([(0.32 * W, 0.84 * W), (0.68 * W, 0.84 * W)], fill=255, width=int(0.07 * W)); d.polygon([(0.76 * W, 0.84 * W), (0.62 * W, 0.74 * W), (0.62 * W, 0.94 * W)], fill=255)
def ic_sound(d, W):
    d.polygon([(0.12 * W, 0.38 * W), (0.3 * W, 0.38 * W), (0.52 * W, 0.18 * W), (0.52 * W, 0.82 * W), (0.3 * W, 0.62 * W), (0.12 * W, 0.62 * W)], fill=255)
    d.arc(U(W, 0.42, 0.26, 0.78, 0.74), -50, 50, fill=255, width=int(0.07 * W)); d.arc(U(W, 0.4, 0.1, 0.94, 0.9), -50, 50, fill=255, width=int(0.07 * W))

# ------------------------------------------------------------------ run
plate("plate", base=(58, 62, 44), seed=1)
plate("plate_dark", base=(32, 34, 28), seed=2, wear=0.7)
plate("plate_wide", w=512, h=160, base=(52, 56, 40), corner=30, seed=3)
plate("plate_small", w=160, h=160, base=(56, 60, 42), corner=26, rivets=False, seed=4, wear=0.8)
tape()
dogtag()
paper()
board()
clip()
stamp(); stamp_fill()
hazard()
screen()
bar("bar_frame"); bar("bar_fill", fill=True)
bullet()
disc("disc"); disc("disc_fire", ring_hazard=True)
stick_base()
for n, f in [("health", ic_health), ("armor", ic_armor), ("scrap", ic_scrap), ("skull", ic_skull), ("hand", ic_hand), ("radar", ic_radar),
             ("turret", ic_turret), ("expand", ic_expand), ("pause", ic_pause), ("gear", ic_gear), ("eye", ic_eye), ("reload", ic_reload),
             ("jump", ic_jump), ("crouch", ic_crouch), ("swap", ic_swap), ("aim", ic_aim), ("fire", ic_fire), ("home", ic_home), ("play", ic_play),
             ("squad", ic_squad), ("link", ic_link), ("solo", ic_solo), ("close", ic_close), ("star", ic_star), ("sound", ic_sound), ("lock", ic_lock), ("shoulder", ic_shoulder), ("star_empty", ic_star_empty)]:
    icon(n, f)
