"""Mossy copies of the city pack's building colour textures.

Run:  blender -b -P Blender/moss_building_textures.py -- [preview.png]
Brown stains, dirt and gravel in the pack's colour textures become moss, and noisy moss patches
spread over the bare concrete, so every building looks overgrown. The pack's own textures are not touched; copies go to
Assets/Models/2026-11-09_MossyBuildings/Textures/<name>_color_moss.png (Unity swaps them in).
"""
import math
import os
import sys

import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
SRC = os.path.join(PROJECT, "Assets", "PostApocalypticBuildings", "Textures")
OUT = os.path.join(PROJECT, "Assets", "Models", "2026-11-09_MossyBuildings", "Textures")
NAMES = ["building1", "building2", "building3", "building4", "building5", "building6", "building7",
         "building8", "building9", "building10", "modular1"]

STAIN_TO_MOSS = 1.0     # how fully brown stains and gravel become moss
MOSS_COVER = 0.42       # share of bare concrete that gets moss patches
PATCH_STRENGTH = 0.85   # how green those patches get


def tile_noise(h, w, cells, octaves, seed):
    rng = np.random.default_rng(seed)
    out = np.zeros((h, w), np.float32)
    amp, total = 1.0, 0.0
    for o in range(octaves):
        n = cells * (2 ** o)
        grid = rng.random((n, n)).astype(np.float32)
        ty = np.arange(h, dtype=np.float32) * n / h
        tx = np.arange(w, dtype=np.float32) * n / w
        y0 = np.floor(ty).astype(int) % n; y1 = (y0 + 1) % n; fy = ty - np.floor(ty); fy = fy * fy * (3 - 2 * fy)
        x0 = np.floor(tx).astype(int) % n; x1 = (x0 + 1) % n; fx = tx - np.floor(tx); fx = fx * fx * (3 - 2 * fx)
        a = grid[y0][:, x0] * (1 - fx)[None, :] + grid[y0][:, x1] * fx[None, :]
        b = grid[y1][:, x0] * (1 - fx)[None, :] + grid[y1][:, x1] * fx[None, :]
        out += amp * (a * (1 - fy)[:, None] + b * fy[:, None])
        total += amp
        amp *= 0.5
    return out / total


def process(name, seed):
    path = os.path.join(SRC, name + "_color.png")
    if not os.path.exists(path):
        return None
    img = bpy.data.images.load(path)
    w, h = img.size
    px = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(px)
    px = px.reshape(h, w, 4)
    rgb = px[..., :3]
    mx, mn = rgb.max(-1), rgb.min(-1)
    sat = (mx - mn) / (mx + 1e-5)
    lum = rgb @ np.array([0.3, 0.55, 0.15], np.float32)
    big = tile_noise(h, w, 6, 4, seed)
    fine = tile_noise(h, w, 48, 3, seed + 1)
    # 1. brown stains, dirt and gravel turn to moss: warm (red > green > blue) and a little saturated
    warm = np.clip((rgb[..., 0] - rgb[..., 2]) / 0.12, 0, 1) * np.clip((sat - 0.12) / 0.12, 0, 1)
    stains = warm * np.clip((0.85 - mx) / 0.2, 0, 1)
    # 2. noisy moss patches over the bare concrete, so plain walls get green too
    patches = np.clip((big - (1.0 - MOSS_COVER)) / 0.18, 0, 1) * (0.6 + 0.4 * fine)
    m = np.clip(np.maximum(stains * STAIN_TO_MOSS, patches * PATCH_STRENGTH), 0, 1) * np.clip((lum - 0.1) / 0.1, 0, 1)  # never on black or dark window holes
    # moss colour: deep to fresh green with olive, keeping the original light and shade
    deep = np.array([0.08, 0.17, 0.05], np.float32)
    fresh = np.array([0.22, 0.36, 0.10], np.float32)
    olive = np.array([0.30, 0.32, 0.13], np.float32)
    k = tile_noise(h, w, 20, 3, seed + 2)[..., None]
    moss = deep * (1 - k) + fresh * k
    moss = moss * (1 - 0.3 * fine[..., None]) + olive * 0.3 * fine[..., None]
    moss = moss * (0.6 + 1.1 * lum[..., None])
    out = rgb * (1 - m[..., None]) + moss * m[..., None]
    snow = m  # share reported below
    px[..., :3] = np.clip(out, 0, 1)
    os.makedirs(OUT, exist_ok=True)
    dst = os.path.join(OUT, name + "_color_moss.png")
    new = bpy.data.images.new(name + "_moss", w, h, alpha=True)
    new.pixels.foreach_set(px.ravel())
    new.filepath_raw = dst
    new.file_format = "PNG"
    new.save()
    share = float(snow.mean())
    bpy.data.images.remove(img)
    return new, share


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    previews = []
    for i, n in enumerate(NAMES):
        r = process(n, 100 + i)
        if r is None:
            print(f"ZR_MOSS {n}: no texture")
            continue
        new, share = r
        print(f"ZR_MOSS {n}: {new.size[0]}x{new.size[1]}, moss on {share * 100:.0f}% of the texture (weighted)")
        if n in ("building9", "building8", "building10"):
            previews.append(new)
    if args and previews:
        # side by side preview, 512 px each
        tiles = []
        for im in previews:
            im.scale(512, 512)
            p = np.empty(512 * 512 * 4, np.float32)
            im.pixels.foreach_get(p)
            tiles.append(p.reshape(512, 512, 4))
        sheet = np.concatenate(tiles, axis=1)
        pv = bpy.data.images.new("preview", sheet.shape[1], 512, alpha=True)
        pv.pixels.foreach_set(sheet.ravel())
        pv.filepath_raw = args[0]
        pv.file_format = "PNG"
        pv.save()


main()
