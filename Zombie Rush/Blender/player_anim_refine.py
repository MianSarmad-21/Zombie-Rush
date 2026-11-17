"""Natural-motion pass over the player's movement clips, run inside a live Blender (2026-11-14).

    exec(open(r"C:/Mian Sarmad/Unity/Zombie-Rush/Zombie Rush/Blender/player_anim_refine.py").read())
    refine_all()            # or bake("Run") for one clip; export_fbx() writes ShadowOperative_Anims.fbx

Works in the scene "PlayerAnim": PA_Rig (ShadowOperative.fbx) with the clips of ShadowOperative_Anims.fbx loaded as
<Clip>_PA actions (setup_scene() makes it). For each locomotion clip it writes <Clip>_N:
  - feet: the swing lifts and heel kicks calmed (k_lift, k_kick), the swing foot relaxed on the shin (a little
    toes-down) instead of flipping sole-up behind; stance feet untouched (planted, roll over the ball);
  - hips: one smooth bob per step (run: lowest at mid-stance; walk: highest), as high as the grounded and
    landing feet allow. The old clips sank the hips per frame, popping 12-20 cm at every footstrike: the
    in-game "vibration";
  - legs follow by IK (pole targets from the original knees, so the knee directions stay), then baked.
"""
import math
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

PROJECT = "C:/Mian Sarmad/Unity/Zombie-Rush/Zombie Rush"
MODELS = PROJECT + "/Assets/Models/2026-11-14_ShadowOperative_Final/"
GS = 1.052                                   # the prefab's scale in Unity
FWD, LEFT = Vector((0, -1, 0)), Vector((1, 0, 0))
SIDE = BACK = 0.75                           # PlayerController.strafeSpeedFactor / backSpeedFactor
_DF, _DB = 4.0 * math.sqrt(0.5 + 0.5 * SIDE ** 2), 4.0 * math.sqrt(0.5 * BACK ** 2 + 0.5 * SIDE ** 2)
CLIP = {  # travel (x = body left, y = forward), game speed m/s
    "Run": ((0, 1), 4.0), "Run_Back": ((0, -1), 4.0 * BACK), "Strafe_Left": ((1, 0), 4.0 * SIDE), "Strafe_Right": ((-1, 0), 4.0 * SIDE),
    "Run_FwdLeft": ((.7071, .7071), _DF), "Run_FwdRight": ((-.7071, .7071), _DF), "Run_BackLeft": ((.7071, -.7071), _DB),
    "Run_BackRight": ((-.7071, -.7071), _DB), "Sprint": ((0, 1), 6.5), "Walk": ((0, 1), 1.5), "Crouch_Walk": ((0, 1), 2.5)}
SETTINGS = {n: {} for n in CLIP}
SETTINGS["Sprint"] = dict(bob=0.009)
SETTINGS["Walk"] = dict(walk=True, bob=0.005, k_lift=1.0, k_kick=1.0, relax=0.3)
SETTINGS["Crouch_Walk"] = dict(walk=True, bob=0.004, k_lift=1.0, k_kick=1.0, relax=0.4)
POLE_ANGLE = -90.0                           # calibrated: knees land on the original knees


def sm(x):
    x = max(0.0, min(1.0, x)); return x * x * (3 - 2 * x)


def setup_scene():
    """Scene PlayerAnim with the body and the exported clips as <Clip>_PA actions."""
    if bpy.context.window is None:                       # background: work in the one scene
        sc = bpy.context.scene; sc.name = "PlayerAnim"
        for o in list(sc.objects): bpy.data.objects.remove(o, do_unlink=True)
    else:
        if "PlayerAnim" in bpy.data.scenes:
            bpy.data.scenes.remove(bpy.data.scenes["PlayerAnim"])
        sc = bpy.data.scenes.new("PlayerAnim"); bpy.context.window.scene = sc
    sc.render.fps = 30
    b0, a0 = set(bpy.data.objects), set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=MODELS + "ShadowOperative.fbx")
    body = [o for o in bpy.data.objects if o not in b0]
    b1 = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=MODELS + "ShadowOperative_Anims.fbx")
    sc.render.fps = 30
    for o in [o for o in bpy.data.objects if o not in b1]: bpy.data.objects.remove(o, do_unlink=True)
    rig = next(o for o in body if o.type == "ARMATURE"); rig.name = "PA_Rig"
    max((o for o in body if o.type == "MESH"), key=lambda o: len(o.data.vertices)).name = "PA_Mesh"
    for a in [a for a in bpy.data.actions if a not in a0]:
        a.name = a.name.split("|")[-1] + "_PA"; a.use_fake_user = True


def scene():
    return bpy.data.scenes["PlayerAnim"], bpy.data.objects["PA_Rig"]


def play(action_name):
    sc, rig = scene()
    a = bpy.data.actions[action_name]
    rig.animation_data_create(); rig.animation_data.action = a
    if a.slots and rig.animation_data.action_slot not in list(a.slots):
        rig.animation_data.action_slot = a.slots[0]
    sc.frame_start, sc.frame_end = int(a.frame_range[0]), int(a.frame_range[1]) - (1 if a.use_cyclic else 0)
    sc.frame_set(sc.frame_start)


def legs():
    _, rig = scene(); B = rig.data.bones
    up = (B["RightLowerLeg"].head_local - B["RightUpperLeg"].head_local).length
    lo = (B["RightFoot"].head_local - B["RightLowerLeg"].head_local).length
    return up, lo, B["RightFoot"].head_local.z


def sample(name):
    """Per cycle frame of <name>_PA: hips height; per side hip joint, knee, ankle, foot and toes world rotation, ball."""
    sc, rig = scene(); play(name + "_PA"); mw = rig.matrix_world; S = []
    for f in range(sc.frame_start, sc.frame_end + 1):
        sc.frame_set(f); pb = rig.pose.bones; d = {"hz": (mw @ pb["Hips"].head).z}
        for s in ("Left", "Right"):
            fm = mw @ pb[s + "Foot"].matrix; tm = mw @ pb[s + "Toes"].matrix
            d[s] = dict(hip=mw @ pb[s + "UpperLeg"].head, knee=mw @ pb[s + "LowerLeg"].head, ank=fm.translation.copy(),
                        R=fm.to_3x3(), TR=tm.to_3x3())
        S.append(d)
    # the exported loops end on a copy of their first frame: drop it, or the cycle repeats a pose (a hitch per loop)
    if len(S) > 2 and all((S[-1][s]["ank"] - S[0][s]["ank"]).length < 1e-3 for s in ("Left", "Right")) \
            and abs(S[-1]["hz"] - S[0]["hz"]) < 1e-3:
        S.pop()
        sc.frame_end = sc.frame_start + len(S) - 1
    return S


def refine(name, k_lift=0.82, k_kick=0.7, k_side=0.7, bob=0.006, reach_k=0.965, walk=False, cap=True, **_):
    """New foot targets and hip heights for one clip."""
    sc, rig = scene(); L_UP, L_LO, ANK = legs()
    S = sample(name); n = len(S)
    trav, gspeed = CLIP[name]; D = (LEFT * trav[0] + FWD * trav[1]).normalized(); v = gspeed / GS
    dt = 1 / sc.render.fps; T = {}
    for s in ("Left", "Right"):
        A = [d[s]["ank"] for d in S]
        vel = [((A[(i + 1) % n] - A[i - 1]) / (2 * dt)) for i in range(n)]
        st = [(-vel[i].dot(D) > 0.55 * v and A[i].z < ANK + 0.09) for i in range(n)]   # planted: moving back at ground speed
        out = []
        for i in range(n):
            d = S[i][s]
            if st[i]:
                out.append(dict(ank=d["ank"].copy(), R=d["R"], TR=d["TR"], st=True, u=0.0, knee=d["knee"], hip=d["hip"]))
                continue
            j = i
            while not st[j % n]: j -= 1
            k = i
            while not st[k % n]: k += 1
            a0, a1 = A[j % n], A[k % n]; u = (i - j) / max(1, k - j); e = u * u * (3 - 2 * u)
            line = a0.lerp(a1, e); dev = d["ank"] - line                     # swing: calm the path round the straight line
            hor = Vector((dev.x, dev.y, 0)); along = D * hor.dot(D); side = hor - along
            ank = line + along * k_kick + side * k_side
            ground = a0.z + (a1.z - a0.z) * e
            ank.z = ground + max(0.0, d["ank"].z - ground) * k_lift
            out.append(dict(ank=ank, R=d["R"], TR=d["TR"], st=False, u=u, knee=d["knee"], hip=d["hip"]))
        T[s] = out
    mids = []
    for s in ("Left", "Right"):
        st = [o["st"] for o in T[s]]
        start = next(i for i in range(n) if st[i] and not st[i - 1])
        mids.append((start + (sum(st) - 1) / 2) % n)
    sign = 1.0 if walk else -1.0
    curve = [sign * bob * math.cos(4 * math.pi * (i - mids[0]) / n) for i in range(n)]
    lim = []
    for i in range(n):
        m = 9.0
        for s in ("Left", "Right"):
            o = T[s][i]
            if not o["st"] and o["ank"].z > ANK + 0.04: continue               # planted or about to land
            rk = reach_k if o["st"] else 0.995                                  # a landing leg is nearly straight
            h, a = o["hip"], o["ank"]; hor = Vector((a.x - h.x, a.y - h.y)).length
            jmax = a.z + math.sqrt(max(0.0, ((L_UP + L_LO) * rk) ** 2 - hor * hor))
            m = min(m, S[i]["hz"] + (jmax - h.z))
        lim.append(m)
    zc = min(lim[i] - curve[i] for i in range(n) if lim[i] < 9)
    if cap: zc = min(zc, sum(d["hz"] for d in S) / n)                           # never taller than before (crouch stays low)
    return S, T, [zc + c for c in curve]


def _empty(name):
    sc, _ = scene()
    o = bpy.data.objects.get(name)
    if o is None:
        o = bpy.data.objects.new(name, None); sc.collection.objects.link(o); o.empty_display_size = 0.04
    o.animation_data_clear(); o.rotation_mode = "QUATERNION"; o.hide_viewport = True; return o


def _key(o, f, loc, R=None):
    o.location = loc; o.keyframe_insert("location", frame=f)
    if R is not None:
        o.rotation_quaternion = R.to_quaternion(); o.keyframe_insert("rotation_quaternion", frame=f)


def _constraints(on):
    _, rig = scene(); pb = rig.pose.bones
    for p in pb:
        for c in list(p.constraints):
            if c.name.startswith("PA_"): p.constraints.remove(c)
    if not on: return
    for s in ("Left", "Right"):
        ik = pb[s + "LowerLeg"].constraints.new("IK"); ik.name = "PA_IK"
        ik.target = bpy.data.objects["PA_ank_" + s]; ik.pole_target = bpy.data.objects["PA_pole_" + s]
        ik.pole_angle = math.radians(POLE_ANGLE); ik.chain_count = 2; ik.use_stretch = False
        for b, e in ((s + "Foot", "PA_foot_"), (s + "Toes", "PA_toes_")):
            c = pb[b].constraints.new("COPY_ROTATION"); c.name = "PA_rot"; c.target = bpy.data.objects[e + s]
    c = pb["Hips"].constraints.new("COPY_LOCATION"); c.name = "PA_z"; c.target = bpy.data.objects["PA_hipz"]
    c.use_x = c.use_y = False


def bake(name, relax=0.85, plantar=-15.0, **kw):
    """<name>_PA -> <name>_N. Returns (worst foot miss in m, action)."""
    kw = {**SETTINGS.get(name, {}), **kw}
    relax = kw.pop("relax", relax)
    sc, rig = scene(); play(name + "_PA")
    S, T, hz = refine(name, **kw)
    n = len(S); f0 = sc.frame_start; f1 = f0 + n
    for s in ("Left", "Right"):
        ea, ep, ef, et = (_empty(p + s) for p in ("PA_ank_", "PA_pole_", "PA_foot_", "PA_toes_"))
        for i in range(n + 1):
            o = T[s][i % n]; f = f0 + i
            _key(ea, f, o["ank"]); _key(ef, f, o["ank"], o["R"]); _key(et, f, o["ank"], o["TR"])
            mid = (o["hip"] + o["ank"]) * 0.5
            _key(ep, f, o["knee"] + (o["knee"] - mid).normalized() * 0.5)
    eh = _empty("PA_hipz")
    for i in range(n + 1): _key(eh, f0 + i, Vector((0, 0, hz[i % n])))
    _constraints(True)
    worst = 0.0
    for i in range(n):
        sc.frame_set(f0 + i)
        for s in ("Left", "Right"):
            worst = max(worst, ((rig.matrix_world @ rig.pose.bones[s + "Foot"].head) - T[s][i]["ank"]).length)
    for o in sc.objects: o.select_set(False)
    rig.select_set(True); bpy.context.view_layer.objects.active = rig

    def _do_bake():
        bpy.ops.object.mode_set(mode="POSE"); bpy.ops.pose.select_all(action="SELECT")
        bpy.ops.nla.bake(frame_start=f0, frame_end=f1, only_selected=False, visual_keying=True, clear_constraints=False,
                         use_current_action=False, bake_types={"POSE"})
        bpy.ops.object.mode_set(mode="OBJECT")
    wins = bpy.context.window_manager.windows if bpy.context.window_manager else []
    if wins:                                              # live Blender: run the operators in a 3D view
        win = wins[0]
        area = next((a for a in win.screen.areas if a.type == "VIEW_3D"), win.screen.areas[0])
        with bpy.context.temp_override(window=win, area=area, active_object=rig, selected_objects=[rig], object=rig):
            _do_bake()
    else:                                                 # background Blender
        _do_bake()
    _constraints(False)
    new = rig.animation_data.action
    old = bpy.data.actions.get(name + "_N")
    if old and old != new: bpy.data.actions.remove(old)
    new.name = name + "_N"; new.use_fake_user = True; new.use_frame_range = True
    new.frame_start, new.frame_end = f0, f1; new.use_cyclic = True
    # swing feet relaxed on the shin, eased in after toe-off and out before touch-down
    q_relax = Quaternion((1, 0, 0), math.radians(plantar))      # -X = toes down on this rig
    for s in ("Left", "Right"):
        for i in range(n):
            o = T[s][i]
            if o["st"]: continue
            w = relax * (1 - max(1 - sm(o["u"] / 0.25), sm((o["u"] - 0.8) / 0.2)))
            for f in ([f0, f1] if i == 0 else [f0 + i]):
                sc.frame_set(f)
                for b, qr in ((s + "Foot", q_relax), (s + "Toes", Quaternion())):
                    p = rig.pose.bones[b]
                    p.rotation_quaternion = p.rotation_quaternion.slerp(qr, w)
                    p.keyframe_insert("rotation_quaternion", frame=f, group=b)
    return worst, new


def track(action_name, bones=("Hips", "LeftFoot", "RightFoot", "Head")):
    sc, rig = scene(); play(action_name); out = []
    for f in range(sc.frame_start, sc.frame_end + 1):
        sc.frame_set(f); out.append({b: rig.matrix_world @ rig.pose.bones[b].head for b in bones})
    return out


def report(name):
    T = track(name + "_N"); hz = [t["Hips"].z for t in T]; dz = [t["Head"].z for t in T]
    step = max(abs(hz[i + 1] - hz[i]) for i in range(len(hz) - 1))
    return f"hips {min(hz):.3f}-{max(hz):.3f}, biggest frame step {step * 1000:.0f} mm, head bob {1000 * (max(dz) - min(dz)):.0f} mm"


def refine_all(names=None):
    for name in names or CLIP:
        w, _ = bake(name)
        print(f"[refine] {name:13s} feet miss {w * 1000:.0f} mm | {report(name)}")


def export_fbx(path=MODELS + "ShadowOperative_Anims.fbx"):
    """All clips: the refined <Clip>_N where there is one, else <Clip>_PA, exported as <Clip> takes."""
    sc, rig = scene()
    clips = {}
    for a in bpy.data.actions:
        if a.name.endswith("_PA"): clips.setdefault(a.name[:-3], a)
    for a in bpy.data.actions:
        if a.name.endswith("_N"): clips[a.name[:-2]] = a
    rig.animation_data.action = None
    ad = rig.animation_data
    for t in list(ad.nla_tracks): ad.nla_tracks.remove(t)
    for clip, a in clips.items():                       # one track and strip per clip; the take is named after it
        tr = ad.nla_tracks.new(); tr.name = clip
        st = tr.strips.new(clip, int(a.frame_range[0]), a); st.name = clip; st.extrapolation = "NOTHING"
        if a.slots and getattr(st, "action_slot", None) is None:
            st.action_slot = a.slots[0]
    for p in rig.pose.bones:
        p.rotation_quaternion = Quaternion(); p.location = (0, 0, 0)
    sc.frame_set(0)
    for o in sc.objects: o.select_set(o is rig)
    bpy.context.view_layer.objects.active = rig
    # Unity copies the body's avatar: the rig node must be named exactly ShadowOperative_Rig (free the name first)
    holder = bpy.data.objects.get("ShadowOperative_Rig")
    if holder is not None and holder is not rig: holder.name = "ShadowOperative_Rig_hold"
    rig.name = "ShadowOperative_Rig"
    assert rig.name == "ShadowOperative_Rig", rig.name
    try:
        bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"ARMATURE"}, add_leaf_bones=False,
                                 apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                                 bake_space_transform=False, bake_anim=True, bake_anim_use_all_actions=False,
                                 bake_anim_use_nla_strips=True, bake_anim_force_startend_keying=True,
                                 bake_anim_simplify_factor=0.0)
    finally:
        rig.name = "PA_Rig"
        if holder is not None and holder is not rig: holder.name = "ShadowOperative_Rig"
        for t in list(ad.nla_tracks): ad.nla_tracks.remove(t)
    print("[refine] exported", path, len(clips), "clips")


if bpy.app.background and "--" in sys.argv and "--all" in sys.argv[sys.argv.index("--") + 1:]:
    # blender -b --factory-startup -P Blender/player_anim_refine.py -- --all
    setup_scene(); refine_all(); export_fbx()
    bpy.ops.wm.save_as_mainfile(filepath=PROJECT + "/Blender/PlayerAnim.blend")
    print("[refine] saved", PROJECT + "/Blender/PlayerAnim.blend")
