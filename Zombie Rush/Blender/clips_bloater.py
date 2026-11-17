"""Bloater animations: the fat plague zombie ("Zombie 6", Meshy Plaguebelly Ghoul) that waddles at the
player and bursts. exec()'d by zombie_anim.py after zr_anim_lib.py. Rig faces -Y (left = +X), t runs 0..1.
Rest: 1.75 m, arms in a T-pose, palms down; the belly hangs far in front, so the spine sits near the back.

Character: heavy and wobbly. Breathes through the mouth, rolls from foot to foot when it walks, jogs with
its arms reaching for you, and when it gets close it rears back, arches and convulses until the belly bursts.
Timing the game uses (30 fps):
  Attack = the swell before the burst: 36 frames (1.2 s). BellyBurst bursts at the end of it (fuse 1.2 s).
Sign conventions (P.rot / P.spine, degrees): rx > 0 leans forward, ry rolls to the side, rz twists.
"""
import math

FL = V(0.17, 0.07, 0.10)      # ankle targets at rest (feet under the hips)
FR = V(-0.17, 0.07, 0.10)
UC = N("UpperChest")

# Fingers: each bends about its own curl axis, relative to its sculpted shape (palms face down at rest).
CLAW_REST, CLAW_GAIN = 0.45, 0.7


def _curl_axes():
    axes = {}
    for side in ("Left", "Right"):
        fallback = Vector((0, 1 if side == "Left" else -1, 0))
        for f, bones in FINGERS[side].items():
            d1 = ML[bones[0]].to_3x3() @ Vector((0, 1, 0))
            d3 = ML[bones[2]].to_3x3() @ Vector((0, 1, 0))
            n = d1.cross(d3)
            n = n.normalized() if n.length > 0.15 else fallback
            if n.dot(fallback) < 0: n = -n
            axes[(side, f)] = n
    return axes


CURL_AXIS = _curl_axes()


def claw_n(self, side, curl=1.0, spread=0.0, thumb=0.6):
    k = (curl - CLAW_REST) / (1 - CLAW_REST) * CLAW_GAIN
    for f, bones in FINGERS[side].items():
        ang = (30, 40, 30) if f != "Thumb" else (15, 20, 15)
        kk = k if f != "Thumb" else thumb * k
        n = CURL_AXIS[(side, f)]
        for b, a in zip(bones, ang):
            if b in self.q:
                Rq = ML[b].to_3x3().to_quaternion()
                self.q[b] = Rq.inverted() @ Quaternion(n, rad(a * kk)) @ Rq


Pose.claw = claw_n


def cp(P, pt):
    """A point given in the rest pose, carried along by the upper chest as it moves now."""
    P.fk()
    return P.M[UC] @ (ML[UC].inverted() @ Vector(pt))


def pulse(t, c, w):
    d = abs(((t - c + 0.5) % 1.0) - 0.5)
    return math.exp(-(d / w) ** 2)


def seg(t, a, b):
    return min(1.0, max(0.0, (t - a) / (b - a)))


def S_(s):
    return 1 if s == "Left" else -1


def zhead(P, tilt=0, turn=0, nod=0):
    P.rot("Neck", nod * 0.4, tilt * 0.4, turn * 0.4)
    P.rot("Head", nod * 0.6, tilt * 0.6, turn * 0.6)


def arms_out(sd, dx=0.0, dy=0.0, dz=0.0):
    """Its natural reach: hands out to the sides and in front, over the belly."""
    return V(S_(sd) * (0.60 + dx), -0.20 + dy, 1.06 + dz)


def reach(P, sd, tgt):
    P.arm_ik(sd, cp(P, tgt), pole=(S_(sd) * 0.6, 0.8, -0.3))


def feet(P, dl=(0, 0, 0), dr=(0, 0, 0), pl=0, pr=0):
    P.leg_ik("Left", FL + V(*dl), pitch=pl)
    P.leg_ik("Right", FR + V(*dr), pitch=pr)


# ---------------- Idle: heavy mouth breathing, the belly heaves, it sways from foot to foot, head lolls.
def idle(t, P):
    br = sin(tau(2 * t))
    sw = sin(tau(t))
    gulp = pulse(t, 0.62, 0.04)
    P.hips = V(0.03 * sw, 0.0, -0.03 - 0.012 * br)
    P.rot("Hips", 2, 3 * sw, 3 * sw)
    P.spine(-4 + 3 * br + 4 * gulp, -2 * sw, -3 * sw)
    zhead(P, tilt=7 * sin(tau(t) + 1.0), turn=14 * sin(tau(t) + 0.3), nod=4 + 3 * br - 8 * gulp)
    for sd in ("Left", "Right"):
        s = S_(sd)
        reach(P, sd, arms_out(sd, 0.0, 0.02 * s * sw, 0.03 * br))
        P.claw(sd, 0.5 + 0.2 * sin(tau(2 * t) + s))
    feet(P)


# ---------------- Gait: a waddle. The whole body rolls over each planted foot, short steps, arms out wide.
def gait(P, t, S, H, stance, lean, sway, roll, twist, wide, bob, curl0, arm_amp, arm_fwd, arm_up):
    land = pulse(t, 0.0, 0.04) + pulse(t, 0.5, 0.04)
    P.hips = V(sway * sin(tau(t)), 0.0, -0.04 - bob * cos(tau(2 * t)) - 0.02 * land)
    P.rot("Hips", 2, roll * sin(tau(t)), twist * sin(tau(t)))
    P.spine(lean + 2 * cos(tau(2 * t)) + 4 * land, -roll * 0.7 * sin(tau(t)), -twist * 0.5 * sin(tau(t)))
    zhead(P, tilt=-5 * sin(tau(t)), turn=5 * sin(tau(t) + 1.0), nod=-lean * 0.5 + 3 * cos(tau(2 * t)))
    for side, off in (("Left", 0.0), ("Right", 0.5)):
        p = (t + off) % 1.0
        if p < stance:
            yy = lerp(-S, S, p / stance); zz = 0.0; pit = 0.0
        else:
            u = (p - stance) / (1 - stance)
            yy = lerp(S, -S, ease(u)); zz = H * sin(pi * u); pit = -12 * sin(pi * u)
        base = FL if side == "Left" else FR
        P.leg_ik(side, base + V(S_(side) * wide, yy, zz), pitch=pit)
    for sd, k in (("Left", 1), ("Right", -1)):
        c = cos(tau(t - 0.5))
        reach(P, sd, arms_out(sd, 0.0, -arm_fwd - arm_amp * k * c, arm_up + 0.04 * abs(c) + 0.03 * land))
        P.claw(sd, curl0 + 0.15 * sin(tau(t) + k))


def walk(t, P):
    gait(P, t, S=0.27, H=0.08, stance=0.62, lean=4, sway=0.07, roll=10, twist=6, wide=0.02, bob=0.025,
         curl0=0.5, arm_amp=0.10, arm_fwd=0.0, arm_up=0.0)


def run(t, P):
    # a heavy jog, leaning in, arms reaching out for the player
    gait(P, t, S=0.36, H=0.13, stance=0.45, lean=14, sway=0.05, roll=8, twist=8, wide=0.0, bob=0.05,
         curl0=0.3, arm_amp=0.20, arm_fwd=0.16, arm_up=0.08)


# ---------------- Attack = the swell: rears back with its arms flung wide, arches to thrust the belly out,
# throws its head back screaming and convulses harder and harder until it bursts (in game) at the end.
def attack(t, P):
    sw = ease(seg(t, 0.0, 0.35))
    conv = seg(t, 0.25, 1.0)
    shake = sin(tau(t) * 9) * conv * conv
    P.hips = V(0.015 * shake, 0.05 * sw, -0.04 - 0.04 * sw + 0.01 * shake)
    P.rot("Hips", -4 * sw, 3 * shake, 0)
    P.spine(-26 * sw + 6 * shake, 4 * shake, 3 * shake)
    zhead(P, tilt=8 * shake, turn=6 * shake, nod=-30 * sw - 8 * conv)
    for sd in ("Left", "Right"):
        s = S_(sd)
        # arms flung up and out, elbows bent (a target within reach, so the arm never locks straight)
        tgt = vlerp(arms_out(sd), V(s * 0.56, 0.04, 1.60), sw) + V(0.02 * shake * s, 0, 0.04 * shake * s)
        reach(P, sd, tgt)
        P.claw(sd, lerp(0.5, 0.0, sw) + 0.3 * abs(shake))
    feet(P, (0.04, 0.0, 0), (-0.04, 0.0, 0))


# ---------------- Hit: flinches back, the belly wobbles, one shuffle step.
def hit(t, P):
    k = ease(t / 0.2) if t < 0.2 else (1 - (t - 0.2) / 0.8) ** 2
    wob = sin(tau(t) * 3) * pulse(t, 0.45, 0.2)
    P.hips = V(0.02 * k, 0.06 * k, -0.03 - 0.03 * k)
    P.rot("Hips", -6 * k, 4 * wob, 4 * k)
    P.spine(-10 * k + 4 * wob, -6 * k, 5 * k)
    zhead(P, tilt=-10 * k + 8 * wob, turn=-12 * k, nod=-14 * k)
    for sd in ("Left", "Right"):
        s = S_(sd)
        reach(P, sd, vlerp(arms_out(sd), V(s * 0.66, 0.0, 1.24), k))
        P.claw(sd, 0.6 + 0.3 * k)
    rf = foot_path(t, [(0.0, (0, 0, 0)), (0.06, (0, 0, 0)), (0.24, (0, 0.14, 0)), (0.60, (0, 0.14, 0)), (0.90, (0, 0, 0))])
    feet(P, (0, 0, 0), tuple(rf))


# ---------------- Death: clutches the belly, staggers, drops to its knees and slumps forward.
def death(t, P):
    clutch = ease(seg(t, 0.0, 0.25))
    kneel = ease(seg(t, 0.20, 0.55))
    slump = ease(seg(t, 0.50, 0.85))
    P.hips = V(0.0, -0.06 * slump, -0.04 - 0.36 * kneel - 0.12 * slump)
    P.rot("Hips", 6 * kneel + 26 * slump, 0, 0)
    P.spine(12 * clutch + 22 * slump, 0, 0)
    zhead(P, nod=10 * clutch + 20 * slump, turn=10 * clutch * (1 - slump))
    for sd in ("Left", "Right"):
        s = S_(sd)
        belly = V(s * 0.20, -0.42, 0.95)
        down = V(s * 0.40, -0.55, 0.15)
        reach(P, sd, vlerp(vlerp(arms_out(sd), belly, clutch), down, slump))
        P.claw(sd, 0.8)
    kn = kneel
    P.leg_ik("Left", FL + V(0.02, 0.32 * kn, 0.0), pitch=-30 * kn, pole=V(0, -1, 0))
    P.leg_ik("Right", FR + V(-0.02, 0.32 * kn, 0.0), pitch=-30 * kn, pole=V(0, -1, 0))


# ---------------- Rise (spawn): lying on its back -> rolls up to sit -> heaves itself to its feet.
def rise(t, P):
    twitch = pulse(t, 0.06, 0.012) + pulse(t, 0.13, 0.012)
    u = ease(seg(t, 0.16, 0.86))
    hy = lerp(0.62, 0.0, u)
    hz = lerp(-0.72, -0.03, ease(u ** 0.8))
    P.hips = V(0.0, hy, hz)
    P.rot("Hips", lerp(-82, 2, u) + 2 * twitch, 0, 3 * twitch)
    sit = sin(pi * min(u * 1.2, 1.0))
    P.spine(lerp(-6, -4, u) + 30 * sit * (1 - u), 0, 0)
    zhead(P, tilt=8 * twitch, turn=lerp(20, 0, u) + 10 * twitch, nod=lerp(-14, 4, u))
    for sd in ("Left", "Right"):
        s = S_(sd)
        ground = V(s * 0.55, hy + 0.35 * (1 - u) + 0.10, 0.10)
        k = ease(seg(u, 0.55, 1.0))
        P.arm_ik(sd, vlerp(ground, cp(P, arms_out(sd)), k), pole=(s * 0.6, 0.8, -0.3))
        P.claw(sd, 0.8 - 0.3 * u)
    feet(P)


CLIPS = [("Idle", 60, idle, True, False), ("Walk", 32, walk, True, False), ("Run", 18, run, True, False),
         ("Attack", 36, attack, False, False), ("Hit", 20, hit, False, False),
         ("Death", 60, death, False, True), ("Rise", 70, rise, False, True)]
