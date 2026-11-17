# blender -b Blender/Grip_<Gun>.blend -P swap_gun.py -- <Gun> <Gun_<Gun>.blend>
# Replaces the gun rig, its meshes and its clips (Idle/Fire/Reload/Draw) in the grip scene with the newly built gun.
import bpy, sys
gun, src = sys.argv[sys.argv.index("--") + 1:][:2]
old = bpy.data.objects[f"{gun}_Rig"]
loc = old.location.copy()
for o in [c for c in bpy.data.objects if c.parent == old] + [old]:
    bpy.data.objects.remove(o)
for n in ("Idle", "Fire", "Reload", "Draw"):
    a = bpy.data.actions.get(n)
    if a: bpy.data.actions.remove(a)
bpy.data.orphans_purge(do_recursive=True)
with bpy.data.libraries.load(src, link=False) as (data_from, data_to):
    data_to.objects = [n for n in data_from.objects if n == f"{gun}_Rig"]
    data_to.actions = [n for n in data_from.actions if n in ("Idle", "Fire", "Reload", "Draw")]
for a in data_to.actions: a.use_fake_user = True
rig = data_to.objects[0]
bpy.context.scene.collection.objects.link(rig)
# its meshes: load the children of the rig in the source file
with bpy.data.libraries.load(src, link=False) as (data_from, data_to2):
    data_to2.objects = list(data_from.objects)
kids = []
for o in data_to2.objects:
    if o is None: continue
    if o.parent is not None and o.parent.name.startswith(f"{gun}_Rig") and o.type == "MESH":
        o.parent = rig
        bpy.context.scene.collection.objects.link(o); kids.append(o.name)
for o in list(data_to2.objects):          # drop everything else the second load brought in
    if o is not None and o.name not in kids and o.users == 0:
        bpy.data.objects.remove(o)
rig.location = loc
if rig.animation_data: rig.animation_data.action = None
bpy.data.orphans_purge(do_recursive=True)
print("SWAP", gun, "rig", rig.name, "meshes", kids, "actions", sorted(a.name for a in bpy.data.actions))
bpy.ops.wm.save_mainfile()
