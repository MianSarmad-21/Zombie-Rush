"""Grenade HUD icon + fuse ring for Zombie Rush, in the Outbreak icon style (white mask, worn edges; tinted in Unity).

    python Tools/UI/make_icon_grenade.py      (needs Pillow; run without -I so the user site-packages load)

Writes Assets/UI/Outbreak/Icons/ic_grenade.png (a frag grenade: egg body with grid grooves, fuse head, lever
and pull ring) and ic_fusering.png (a thick ring, used as a radial fill for the cooking fuse).
"""
import math
import os
import random

from PIL import Image, ImageChops, ImageDraw, ImageFilter

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "UI", "Outbreak", "Icons")
W, SIZE = 512, 256


def P(x, y): return (x * W, y * W)


def rough(mask, seed, amount=1.0, scratches=2):
    """Same wear as make_outbreak_icons.rough."""
    random.seed(seed)
    b = mask.filter(ImageFilter.GaussianBlur(W * 0.007))
    n = Image.effect_noise((W // 5, W // 5), 55 * amount).resize((W, W), Image.BICUBIC)
    fine = Image.effect_noise((W // 2, W // 2), 40 * amount).resize((W, W), Image.BICUBIC)
    m = ImageChops.add(b, n, 1.0, -128)
    m = ImageChops.add(m, fine, 1.0, -128)
    m = m.point(lambda v: 255 if v > 128 else 0)
    m = ImageChops.multiply(m, mask.filter(ImageFilter.MaxFilter(9)))
    d = ImageDraw.Draw(m)
    for _ in range(scratches):
        x, y = random.uniform(0.15, 0.85) * W, random.uniform(0.15, 0.85) * W
        a = random.uniform(0, math.pi); L = random.uniform(0.15, 0.3) * W
        d.line([(x, y), (x + math.cos(a) * L, y + math.sin(a) * L)], fill=0, width=max(2, int(W * 0.009)))
    return m.filter(ImageFilter.GaussianBlur(W * 0.0025))


def save(m, name):
    box = m.getbbox()
    if box:
        crop = m.crop(box); side = max(crop.size); k = int(W * 0.88) / side
        crop = crop.resize((max(1, int(crop.size[0] * k)), max(1, int(crop.size[1] * k))), Image.LANCZOS)
        m = Image.new("L", (W, W), 0); m.paste(crop, ((W - crop.size[0]) // 2, (W - crop.size[1]) // 2))
    img = Image.merge("RGBA", (Image.new("L", (W, W), 255),) * 3 + (m,))
    os.makedirs(OUT, exist_ok=True)
    img.resize((SIZE, SIZE), Image.LANCZOS).save(os.path.join(OUT, name))
    print("wrote", name)


def grenade():
    m = Image.new("L", (W, W), 0); d = ImageDraw.Draw(m)
    # body: an egg, a little wider low down
    d.ellipse([P(0.22, 0.30), P(0.74, 0.92)], fill=255)
    # grid grooves cut into the body (the frag pattern)
    for k in range(1, 4):
        y = 0.30 + k * 0.155
        d.line([P(0.20, y), P(0.76, y)], fill=0, width=int(W * 0.022))
    for x in (0.39, 0.57):
        d.line([P(x, 0.30), P(x, 0.92)], fill=0, width=int(W * 0.022))
    # fuse head
    d.rectangle([P(0.39, 0.19), P(0.57, 0.33)], fill=255)
    d.rectangle([P(0.36, 0.15), P(0.60, 0.21)], fill=255)
    # lever down the right side
    d.line([P(0.58, 0.17), P(0.72, 0.20), P(0.80, 0.34), P(0.80, 0.58)], fill=255, width=int(W * 0.05))
    # pull ring on the left
    d.ellipse([P(0.10, 0.08), P(0.34, 0.32)], outline=255, width=int(W * 0.035))
    d.line([P(0.31, 0.24), P(0.40, 0.24)], fill=255, width=int(W * 0.03))
    save(rough(m, 811), "ic_grenade.png")


def fuse_ring():
    m = Image.new("L", (W, W), 0); d = ImageDraw.Draw(m)
    d.ellipse([P(0.04, 0.04), P(0.96, 0.96)], outline=255, width=int(W * 0.09))
    save(m.filter(ImageFilter.GaussianBlur(1.2)), "ic_fusering.png")


if __name__ == "__main__":
    grenade(); fuse_ring()
