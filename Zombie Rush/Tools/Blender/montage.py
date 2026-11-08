"""Joins preview renders into one sheet: blender -b --python montage.py -- out.png cols img1 img2 ..."""
import sys
import bpy
import numpy as np
a = sys.argv[sys.argv.index('--') + 1:]
out, cols, files = a[0], int(a[1]), a[2:]
imgs = [bpy.data.images.load(f) for f in files]
w, h = imgs[0].size
rows = (len(imgs) + cols - 1) // cols
sheet = np.zeros((rows * h, cols * w, 4), dtype=np.float32)
for i, im in enumerate(imgs):
    px = np.empty(w * h * 4, dtype=np.float32)
    im.pixels.foreach_get(px)
    r, c = divmod(i, cols)
    y0 = (rows - 1 - r) * h
    sheet[y0:y0 + h, c * w:(c + 1) * w] = px.reshape(h, w, 4)
res = bpy.data.images.new('sheet', cols * w, rows * h, alpha=True)
res.pixels.foreach_set(sheet.ravel())
res.filepath_raw = out
res.file_format = 'PNG'
res.save()
