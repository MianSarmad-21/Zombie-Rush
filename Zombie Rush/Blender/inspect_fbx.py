import sys, bpy
from collections import Counter
files = sys.argv[sys.argv.index("--") + 1:]
for f in files:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=f)
    print("ZR_FILE", f.split("/")[-1])
    for o in bpy.context.scene.objects:
        if o.type != "MESH": continue
        me = o.data
        c = Counter(p.material_index for p in me.polygons)
        mats = {i: (me.materials[i].name if i < len(me.materials) and me.materials[i] else "?") for i in c}
        print("ZR_OBJ", o.name, "polys", len(me.polygons), "dims", tuple(round(d,1) for d in o.dimensions), "mats", {mats[i]: n for i, n in c.items()})
