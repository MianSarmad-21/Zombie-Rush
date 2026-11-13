"""Toxic Ghoul animations: a bloated, heavy poison zombie. exec()'d by zombie_anim.py after zr_anim_lib.py.

Units are metres, the rig stands at the origin facing -Y, t runs 0..1 over each clip.
Timing marks the game uses (frames at 30 fps):
  Attack: left rake (feint), then the right rake hits at t = 0.48 of 40 frames -> 0.64 s
  Spit:   glob leaves the mouth at t = 0.56 of 54 frames -> 1.0 s
  Death:  belly bursts at t = 0.42 of 100 frames -> 1.4 s
"""
import math

FOOT_L = REST_HAND_POS["LeftFoot"]
FOOT_R = REST_HAND_POS["RightFoot"]
UC = N("UpperChest")


def claw_z(self, side, curl=1.0, spread=0.0, thumb=0.6):
    s = 1 if side == "Left" else -1
    for f, bones in FINGERS[side].items():
        ang = (35, 50, 35) if f != "Thumb" else (20, 25, 20)
        k = curl if f != "Thumb" else thumb * curl
        for b, a in zip(bones, ang):
            if b in self.q:
                Rq = ML[b].to_3x3().to_quaternion()
                d = Euler((0, 0, rad(-s * a * k)), 'XYZ').to_quaternion()
                self.q[b] = Rq.inverted() @ d @ Rq


Pose.claw = claw_z


def cp(P, pt):
    """A point given in rest space that follows the chest (hands move with the torso)."""
    P.fk()
    return P.M[UC] @ (ML[UC].inverted() @ Vector(pt))


def pulse(t, c, w):
    d = abs(((t - c + 0.5) % 1.0) - 0.5)
    return math.exp(-(d / w) ** 2)


def seg(t, a, b):
    return min(1.0, max(0.0, (t - a) / (b - a)))


def S_(s):
    return 1 if s == "Left" else -1


def hang(sd, dx=0.0, dy=0.0, dz=0.0):
    """Where a hand hangs at rest: long arms, hands by the thighs, a little in front."""
    return V(S_(sd) * (0.38 + dx), -0.12 + dy, 0.98 + dz)


def belly(sd, dx=0.0, dy=0.0, dz=0.0):
    """A hand pressed on the swollen belly."""
    return V(S_(sd) * (0.17 + dx), -0.34 + dy, 1.06 + dz)


def feet(P, dl=(0, 0, 0), dr=(0, 0, 0), pl=0, pr=0):
    P.leg_ik("Left", FOOT_L + V(*dl), pitch=pl)
    P.leg_ik("Right", FOOT_R + V(*dr), pitch=pr)


def zhead(P, tilt=0, turn=0, nod=0):
    P.rot("Neck", nod * 0.4, tilt * 0.4, turn * 0.4)
    P.rot("Head", nod * 0.6, tilt * 0.6, turn * 0.6)


# ---------------- Idle: heavy breathing that heaves the belly, the head lolls, a retch now and then.
def idle(t, P):
    br = sin(tau(2 * t))                 # two slow breaths
    sw = sin(tau(t))                     # weight shift
    gag = pulse(t, 0.70, 0.05)           # retch
    tw = pulse(t, 0.28, 0.02)            # finger twitch
    P.hips = V(0.025 * sw, 0.0, -0.03 - 0.012 * br - 0.04 * gag)
    P.rot("Hips", 0, 3 * sw, 3 * sw)
    P.spine(12 + 3 * br + 16 * gag, 2 * sw, -2 * sw)
    zhead(P, tilt=8 * sin(tau(t) + 1.0) + 6 * gag, turn=14 * sin(tau(t) + 0.4), nod=16 + 4 * br + 22 * gag)
    P.rot("LeftShoulder", 0, 0, 2 * br + 5 * gag)
    P.rot("RightShoulder", 0, 0, -2 * br - 5 * gag)
    # the left hand drifts to the belly when it retches, the right hangs and sways
    P.arm_ik("Left", cp(P, vlerp(hang("Left", 0.0, 0.02 * sw, 0.015 * br), belly("Left"), ease(gag * 1.4))), roll=12)
    P.arm_ik("Right", cp(P, hang("Right", 0.0, -0.02 * sw, 0.015 * br)), roll=-12)
    P.claw("Left", 0.55 + 0.35 * tw + 0.3 * gag)
    P.claw("Right", 0.5 + 0.1 * br)
    feet(P, (0.03, 0.0, 0), (-0.03, 0.03, 0))


# ---------------- Walk: a wide, heavy waddle. Big side-to-side sway over each planted foot.
def gait(P, t, S, H, stance, lean, drop, bob, sway, roll, twist, wide, arms, headnod):
    P.hips = V(sway * sin(tau(t)), 0.0, -drop - bob * cos(tau(2 * t)))
    P.rot("Hips", 0, twist * sin(tau(t)), roll * sin(tau(t)))
    P.spine(lean + 2 * cos(tau(2 * t)), -twist * 0.4 * sin(tau(t)), -roll * 0.8 * sin(tau(t)))
    zhead(P, tilt=-6 * sin(tau(t)), turn=6 * sin(tau(t) + 1), nod=headnod + 5 * cos(tau(2 * t)))
    for side, off in (("Left", 0.0), ("Right", 0.5)):
        p = (t + off) % 1.0
        if p < stance:
            yy = lerp(-S, S, p / stance); zz = 0.0; pit = 8 * (p / stance - 0.5)
        else:
            u = (p - stance) / (1 - stance)
            yy = lerp(S, -S, ease(u)); zz = H * sin(pi * u); pit = -18 * sin(pi * u)
        rest = FOOT_L if side == "Left" else FOOT_R
        P.leg_ik(side, rest + V(S_(side) * wide, yy, zz), pitch=pit)
    arms(P, t)


def walk_arms(P, t):
    c = cos(tau(t - 0.5))
    P.arm_ik("Left", cp(P, hang("Left", 0.02, -0.12 * c, 0.03 * abs(c))), roll=10)
    P.arm_ik("Right", cp(P, hang("Right", 0.02, 0.12 * c, 0.03 * abs(c))), roll=-10)
    P.claw("Left", 0.6); P.claw("Right", 0.55)


def run_arms(P, t):
    c = cos(tau(t - 0.5)); s = sin(tau(t))
    # arms flail forward, grasping
    P.arm_ik("Left", cp(P, V(0.30, -0.40 - 0.18 * c, 1.20 + 0.08 * s)), roll=25)
    P.arm_ik("Right", cp(P, V(-0.30, -0.40 + 0.18 * c, 1.20 - 0.08 * s)), roll=-25)
    P.claw("Left", 0.85); P.claw("Right", 0.85)


def limp(t, P):
    """A lurching limp: the left leg takes a full heavy step, the right drags short and low with the toe
    scraping; the body drops hard onto the good leg. A spasm runs through the torso twice a cycle."""
    spasm = pulse(t, 0.22, 0.03) + 0.7 * pulse(t, 0.71, 0.025)
    dip = pulse(t, 0.08, 0.10)                            # dropping onto the left (good) leg
    P.hips = V(0.06 * sin(tau(t)) + 0.03 * spasm, 0.0, -0.06 - 0.06 * dip - 0.02 * spasm)
    P.rot("Hips", 2, 8 * sin(tau(t)) + 6 * dip, 12 * sin(tau(t)))
    P.spine(16 + 6 * dip + 14 * spasm, -6 * sin(tau(t)) - 8 * spasm, -10 * sin(tau(t)) + 10 * spasm)
    zhead(P, tilt=10 * sin(tau(t)) + 18 * spasm, turn=8 * sin(tau(t) + 1) - 20 * spasm, nod=18 + 6 * dip - 12 * spasm)
    P.rot("LeftShoulder", 0, 0, 3 * dip + 8 * spasm)
    P.rot("RightShoulder", 0, 0, -6 * spasm)
    # left leg: full step
    p = t % 1.0
    if p < 0.62:
        yy = lerp(-0.44, 0.44, p / 0.62); zz = 0.0; pit = 8 * (p / 0.62 - 0.5)
    else:
        u = (p - 0.62) / 0.38; yy = lerp(0.44, -0.44, ease(u)); zz = 0.13 * sin(pi * u); pit = -18 * sin(pi * u)
    P.leg_ik("Left", FOOT_L + V(0.05, yy, zz), pitch=pit)
    # right leg: short drag, toe down, barely leaves the ground
    p = (t + 0.5) % 1.0
    if p < 0.70:
        yy = lerp(-0.24, 0.30, p / 0.70); zz = 0.0; pit = 4
    else:
        u = (p - 0.70) / 0.30; yy = lerp(0.30, -0.24, ease(u)); zz = 0.025 * sin(pi * u); pit = 22 * sin(pi * u)
    P.leg_ik("Right", FOOT_R + V(-0.07, yy, zz), pitch=pit)
    # left hand clutches the belly, the right arm hangs and swings loose, jerking on each spasm
    P.arm_ik("Left", cp(P, belly("Left", 0.0, 0.0, 0.02 * sin(tau(2 * t)))), roll=12)
    c = cos(tau(t - 0.5))
    P.arm_ik("Right", cp(P, hang("Right", 0.04 + 0.08 * spasm, 0.16 * c, 0.03 * abs(c) + 0.10 * spasm)), roll=-12)
    P.claw("Left", 0.7); P.claw("Right", 0.4 + 0.5 * spasm)


def walk(t, P):
    limp(t, P)


def run(t, P):
    gait(P, t, S=0.46, H=0.22, stance=0.44, lean=24, drop=0.10, bob=0.07, sway=0.07, roll=10, twist=12, wide=0.05, arms=run_arms, headnod=4)
    sp = pulse(t, 0.30, 0.03)                             # a convulsion mid-stride (on top of the gait's head swing)
    zhead(P, tilt=-6 * sin(tau(t)) + 16 * sp, turn=6 * sin(tau(t) + 1) - 12 * sp, nod=4 + 5 * cos(tau(2 * t)) + 10 * sp)


# ---------------- Attack: a frenzied double rake. The left claw slashes across first (a feint), the right
# follows straight away, harder, down across the front. HIT on the right rake at t = 0.48 (0.64 s).
def rake_arm(P, sd, wind, swipe, back, rest):
    s = S_(sd)
    up = V(s * 0.44, 0.16, 1.76)
    side = V(s * 0.64, -0.42, 1.46)
    across = V(-s * 0.14, -0.70, 1.00)
    if swipe > 0:
        u = swipe
        tgt = up * (1 - u) ** 2 + side * 2 * (1 - u) * u + across * u * u
        tgt = vlerp(rest, tgt, back)
    else:
        u = wind
        tgt = rest * (1 - u) ** 2 + V(s * 0.66, -0.05, 1.36) * 2 * (1 - u) * u + up * u * u
    P.arm_ik(sd, cp(P, tgt), pole=(s * 0.7, 0.6, -1.0), roll=s * (20 + 30 * swipe * back))
    P.claw(sd, 0.3 + 0.7 * swipe * back)


def attack(t, P):
    lw = ease(seg(t, 0.0, 0.14)) * (1 - ease(seg(t, 0.16, 0.30)))      # left: quick wind-up ...
    ls = ease(seg(t, 0.16, 0.30)); lb = 1 - ease(seg(t, 0.34, 0.60))   # ... and rake
    rw = ease(seg(t, 0.12, 0.34)) * (1 - ease(seg(t, 0.36, 0.54)))     # right winds up while the left rakes
    rs = ease(seg(t, 0.36, 0.54)); rb = 1 - ease(seg(t, 0.64, 1.0))
    l, r = ls * lb, rs * rb
    P.hips = V(-0.03 * l + 0.03 * r, -0.05 * l - 0.10 * r, -0.06 - 0.03 * l - 0.06 * r)
    P.rot("Hips", 4 + 6 * l + 8 * r, 10 * rw - 14 * l + 16 * r, 0)
    P.spine(8 + 14 * l + 24 * r - 8 * rw, 16 * l - 22 * rw + 30 * r, -4 * l + 6 * r)
    zhead(P, tilt=6 * l - 6 * rw + 8 * r, turn=12 * l - 10 * rw + 14 * r, nod=10 + 10 * l - 10 * rw + 18 * r)
    rake_arm(P, "Left", lw, ls, lb, hang("Left"))
    rake_arm(P, "Right", rw, rs, rb, hang("Right"))
    lf = foot_path(t, [(0.0, (0.02, 0, 0)), (0.30, (0.02, 0, 0)), (0.48, (0.02, -0.20, 0)), (0.70, (0.02, -0.20, 0)), (0.94, (0.02, 0, 0))])
    rf = foot_path(t, [(0.0, (-0.03, 0, 0)), (0.10, (-0.03, 0, 0)), (0.28, (-0.03, 0.08, 0)), (0.60, (-0.03, 0.08, 0)), (0.80, (-0.03, 0, 0))])
    feet(P, tuple(lf), tuple(rf), pl=0, pr=-8 * rw)


# ---------------- Spit: the belly convulses twice, it rears back, then lurches forward and spits. RELEASE at t = 0.56
def spit(t, P):
    heave = pulse(t, 0.10, 0.035) + pulse(t, 0.24, 0.035)
    clutch = ease(seg(t, 0.0, 0.12)) * (1 - ease(seg(t, 0.36, 0.46)))
    rear = ease(seg(t, 0.30, 0.47)) * (1 - ease(seg(t, 0.49, 0.60)))
    lurch = ease(seg(t, 0.47, 0.61)) * (1 - ease(seg(t, 0.68, 1.0)))
    P.hips = V(0.0, 0.06 * rear - 0.09 * lurch, -0.05 - 0.05 * heave - 0.05 * lurch)
    P.rot("Hips", -4 * rear + 6 * lurch, 0, 0)
    P.spine(18 * clutch + 14 * heave - 20 * rear + 34 * lurch, 0, 0)
    zhead(P, tilt=4 * heave, turn=0, nod=20 * clutch + 16 * heave - 32 * rear + 22 * lurch)
    P.rot("LeftShoulder", 0, 0, 6 * rear); P.rot("RightShoulder", 0, 0, -6 * rear)
    for sd in ("Left", "Right"):
        s = S_(sd)
        out = V(s * 0.50, 0.10, 1.20)       # flung out and back when it rears
        brace = V(s * 0.40, -0.30, 1.00)    # thrown forward and down with the spit
        tgt = vlerp(vlerp(vlerp(hang(sd), belly(sd), clutch), out, rear), brace, lurch)
        P.arm_ik(sd, cp(P, tgt), roll=s * 15)
        P.claw(sd, 0.8 * clutch + 0.4 + 0.3 * lurch)
    lf = foot_path(t, [(0.0, (0.03, 0, 0)), (0.50, (0.03, 0, 0)), (0.58, (0.03, -0.20, 0)), (0.70, (0.03, -0.20, 0)), (0.95, (0.03, 0, 0))])
    rf = foot_path(t, [(0.0, (-0.03, 0, 0)), (0.30, (-0.03, 0, 0)), (0.46, (-0.03, 0.08, 0)), (0.60, (-0.03, 0.08, 0)), (0.72, (-0.03, 0.0, 0))])
    feet(P, tuple(lf), tuple(rf), pr=-8 * rear)


# ---------------- Hit: a heavy flinch, the belly wobbles, it groans and settles.
def hit(t, P):
    k = ease(t / 0.18) if t < 0.18 else (1 - (t - 0.18) / 0.82) ** 2
    wob = sin(tau(t) * 4) * (1 - t) * 0.6
    P.hips = V(0.04 * k, 0.10 * k, -0.06 * k)
    P.rot("Hips", -6 * k, 0, 8 * k)
    P.spine(4 - 18 * k + 4 * wob, 4 * wob, 12 * k)
    zhead(P, tilt=-14 * k, turn=-18 * k, nod=10 - 26 * k)
    for sd in ("Left", "Right"):
        s = S_(sd)
        P.arm_ik(sd, cp(P, vlerp(hang(sd), V(s * 0.52, 0.04, 1.30), k)), roll=s * 10)
        P.claw(sd, 0.4 + 0.5 * k)
    rf = foot_path(t, [(0.0, (0, 0, 0)), (0.04, (0, 0, 0)), (0.22, (0, 0.12, 0)), (0.55, (0, 0.12, 0)), (0.90, (0, 0, 0))])
    feet(P, (0.0, 0.0, 0), tuple(rf))


# ---------------- Death: clutches the belly, staggers, convulses, the belly BURSTS (t = 0.42), it falls on its back.
def death(t, P):
    clutch = ease(seg(t, 0.0, 0.12))
    stag = ease(seg(t, 0.05, 0.30))
    conv = pulse(t, 0.20, 0.03) + pulse(t, 0.30, 0.03)
    burst = ease(seg(t, 0.38, 0.44)) * (1 - ease(seg(t, 0.46, 0.60)))
    fall = ease(seg(t, 0.46, 0.86))
    settle = pulse(t, 0.92, 0.03)
    hy = lerp(0.10 * stag, 0.80, fall)
    hz = lerp(-0.10 * stag - 0.05 * conv, -0.78, fall)
    P.hips = V(0.04 * stag - 0.02 * fall, hy, hz)
    P.rot("Hips", lerp(10 * stag - 12 * burst, -84, fall), 6 * stag * (1 - fall), 6 * conv)
    P.spine(lerp(22 * stag + 12 * conv - 30 * burst, -8, fall), 0, 4 * conv)
    zhead(P, tilt=6 * conv, turn=14 * stag * (1 - fall), nod=lerp(24 * stag - 40 * burst, -10 + 6 * settle, fall))
    for sd in ("Left", "Right"):
        s = S_(sd)
        thrown = V(s * 0.70, 0.15, 1.35)                       # arms flung out by the burst
        lying = V(s * 0.75, 0.75, 0.10)                        # spread on the ground behind
        tgt = vlerp(vlerp(vlerp(hang(sd), belly(sd), clutch), thrown, burst), lying, fall)
        # from chest-relative (standing) to ground-relative (lying), blended so the arms don't jump
        k = ease(seg(fall, 0.3, 0.7))
        P.arm_ik(sd, vlerp(cp(P, tgt), tgt, k))
        P.claw(sd, 0.9 * clutch * (1 - burst) + 0.3 + 0.2 * settle)
    # legs buckle, then lie stretched out
    kneel = stag * (1 - fall)
    P.leg_ik("Left", FOOT_L + V(0.04, -0.10 * kneel - 0.25 * fall, 0.0), pitch=-10 * fall)
    P.leg_ik("Right", FOOT_R + V(-0.04, 0.08 * kneel - 0.10 * fall, 0.0), pitch=-6 * fall)


# ---------------- Rise from the ground (spawn): lying on its back -> sits up -> stands.
def rise(t, P):
    twitch = pulse(t, 0.06, 0.012) + pulse(t, 0.14, 0.012)
    u = ease(seg(t, 0.16, 0.82))
    hy = lerp(0.80, 0.0, u)
    hz = lerp(-0.78, -0.03, ease(u ** 0.8))
    P.hips = V(0.0, hy, hz)
    P.rot("Hips", lerp(-84, 0, u) + 2 * twitch, 0, 3 * twitch)
    sit = sin(pi * min(u * 1.2, 1.0))
    P.spine(lerp(4, 12, u) + 26 * sit * (1 - u), 0, 0)
    zhead(P, tilt=lerp(6, 8, u) + 8 * twitch, turn=12 * twitch, nod=lerp(-30, 16, u))
    for sd in ("Left", "Right"):
        s = S_(sd)
        if u < 0.55:
            P.arm_ik(sd, V(s * 0.42, hy + 0.40 * (1 - u) + 0.1, 0.10))   # hands push on the ground
        else:
            P.arm_ik(sd, cp(P, vlerp(V(s * 0.42, 0.30, 0.10), hang(sd), ease((u - 0.55) / 0.45))))
        P.claw(sd, 0.9 - 0.3 * u)
    feet(P, (0.03, 0.0, 0), (-0.03, 0.03, 0))


CLIPS = [("Idle", 100, idle, True, False), ("Walk", 36, walk, True, False), ("Run", 24, run, True, False),
         ("Attack", 40, attack, False, False), ("Spit", 54, spit, False, False), ("Hit", 24, hit, False, False),
         ("Death", 100, death, False, True), ("Rise", 84, rise, False, True)]
