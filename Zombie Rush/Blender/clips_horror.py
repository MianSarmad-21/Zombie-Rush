"""Horror animations: the giant brute (2.6 m in game; the rig is ~1.85 m and the game scales it).
exec()'d by zombie_anim.py after zr_anim_lib.py. Rig faces -Y (left = +X), t runs 0..1 per clip.

Character: hunched, huge long arms, heavy. Breathes deep, lumbers with the knuckles low and the ground
shaking on every step, charges like a gorilla, and attacks with a giant wind-up slash.
Timing marks the game uses (30 fps):
  Attack: the claw rips through in front at t = 0.52 of 48 frames -> 0.83 s (ZombieSetupTools Horror.HitDelay = 25/30)
Ground speeds: Walk ~1.0 m/s, Run (charge) ~2.8 m/s, sped up or slowed by the setup to the game's.
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
    """Long arms hanging, fists low by the knees and a little forward."""
    return V(S_(sd) * (0.40 + dx), -0.06 + dy, 0.84 + dz)


def feet(P, dl=(0, 0, 0), dr=(0, 0, 0), pl=0, pr=0):
    P.leg_ik("Left", FOOT_L + V(*dl), pitch=pl)
    P.leg_ik("Right", FOOT_R + V(*dr), pitch=pr)


def zhead(P, tilt=0, turn=0, nod=0):
    P.rot("Neck", nod * 0.4, tilt * 0.4, turn * 0.4)
    P.rot("Head", nod * 0.6, tilt * 0.6, turn * 0.6)


HUNCH = 16   # forward hunch of the spine at rest


# ---------------- Idle: deep slow breaths, shoulders roll, head sways and turns, fists open and close.
def idle(t, P):
    br = sin(tau(2 * t))
    sw = sin(tau(t))
    snort = pulse(t, 0.55, 0.03)
    P.hips = V(0.02 * sw, 0.0, -0.05 - 0.015 * br)
    P.rot("Hips", 4, 3 * sw, 2 * sw)
    P.spine(HUNCH + 4 * br + 6 * snort, 3 * sw, -2 * sw)
    zhead(P, tilt=5 * sin(tau(t) + 1.4), turn=16 * sin(tau(t) + 0.2), nod=-6 + 3 * br - 10 * snort)
    P.rot("LeftShoulder", 0, 0, 3 * br + 4 * snort)
    P.rot("RightShoulder", 0, 0, -3 * br - 4 * snort)
    for sd in ("Left", "Right"):
        s = S_(sd)
        P.arm_ik(sd, cp(P, hang(sd, 0.0, -0.03 * s * sw, 0.02 * br)), roll=s * 14)
        P.claw(sd, 0.55 + 0.25 * sin(tau(2 * t) + s))
    feet(P, (0.06, -0.02, 0), (-0.06, 0.04, 0))


# ---------------- Gait
def gait(P, t, S, H, stance, lean, drop, bob, sway, roll, twist, wide, arms, headnod, stomp):
    land = pulse(t, 0.0, 0.035) + pulse(t, 0.5, 0.035)     # each foot landing shakes the body
    P.hips = V(sway * sin(tau(t)), 0.0, -drop - bob * cos(tau(2 * t)) - stomp * land)
    P.rot("Hips", 4, twist * sin(tau(t)), roll * sin(tau(t)))
    P.spine(lean + 2 * cos(tau(2 * t)) + 4 * stomp * land / 0.03, -twist * 0.5 * sin(tau(t)), -roll * 0.9 * sin(tau(t)))
    zhead(P, tilt=-5 * sin(tau(t)), turn=5 * sin(tau(t) + 1), nod=headnod + 4 * cos(tau(2 * t)))
    for side, off in (("Left", 0.0), ("Right", 0.5)):
        p = (t + off) % 1.0
        if p < stance:
            yy = lerp(-S, S, p / stance); zz = 0.0; pit = 6 * (p / stance - 0.5)
        else:
            u = (p - stance) / (1 - stance)
            yy = lerp(S, -S, ease(u)); zz = H * sin(pi * u); pit = -16 * sin(pi * u)
        rest = FOOT_L if side == "Left" else FOOT_R
        P.leg_ik(side, rest + V(S_(side) * wide, yy, zz), pitch=pit)
    arms(P, t)


def walk_arms(P, t):
    """Knuckles hang low by the knees; each arm swings heavy and late with the opposite leg, the
    shoulder rolling forward with it."""
    c = cos(tau(t - 0.5))
    for sd, k in (("Left", 1), ("Right", -1)):
        s = S_(sd)
        P.rot(sd + "Shoulder", 0, 0, -s * 6 * k * c)
        P.arm_ik(sd, cp(P, hang(sd, 0.08, -0.26 * k * c, -0.20 + 0.06 * abs(c))), roll=s * 16)
        P.claw(sd, 0.75)


def charge_arms(P, t):
    """Gorilla charge: each arm reaches far forward and high, then rakes down and back past the hip,
    pulling the body on (opposite to its leg)."""
    for sd, off in (("Left", 0.0), ("Right", 0.5)):
        s = S_(sd)
        u = (t + off) % 1.0
        reach = 0.5 - 0.5 * cos(tau(u))                      # 0 back, 1 forward
        y = lerp(0.22, -0.78, reach)
        z = lerp(0.55, 1.20, reach) + 0.10 * sin(tau(u))
        P.rot(sd + "Shoulder", 0, 0, -s * 10 * (reach - 0.5))
        P.arm_ik(sd, cp(P, V(s * 0.50, y, z)), roll=s * 20)
        P.claw(sd, 0.6 + 0.4 * (1 - reach))


def walk(t, P):
    # slow lumber: wide planted stance, the whole body rolls over each foot and shudders on every stomp
    gait(P, t, S=0.38, H=0.10, stance=0.66, lean=HUNCH + 10, drop=0.11, bob=0.05, sway=0.14, roll=13, twist=11, wide=0.11,
         arms=walk_arms, headnod=-14, stomp=0.05)


def run(t, P):
    gait(P, t, S=0.60, H=0.24, stance=0.42, lean=HUNCH + 30, drop=0.16, bob=0.07, sway=0.07, roll=12, twist=9, wide=0.08,
         arms=charge_arms, headnod=-22, stomp=0.05)


# ---------------- Attack: GIANT SLASH. Coils right (claw high over the right shoulder, left arm out for
# balance), steps in with the left foot and rips the right claw diagonally across and down to the left
# hip, the torso unwinding with it. HIT at t = 0.52 (25 of 48 frames = 0.83 s).
SLASH_HIT = 0.52


def attack(t, P):
    coil = ease(seg(t, 0.0, 0.40))                      # wind-up
    rip = ease(seg(t, 0.42, 0.62))                      # the slash itself (fast)
    back = 1 - ease(seg(t, 0.72, 1.0))                  # recover
    wind = coil * (1 - rip)
    follow = rip * back
    roar = pulse(t, 0.34, 0.06)
    shake = pulse(t, SLASH_HIT + 0.03, 0.03)
    # torso: twists right and leans back on the coil, unwinds hard left and folds forward on the rip
    P.hips = V(0.04 * wind - 0.05 * follow, 0.08 * wind - 0.12 * follow, -0.08 + 0.03 * wind - 0.06 * follow - 0.02 * shake)
    P.rot("Hips", 4 - 6 * wind + 8 * follow, 4 * wind - 6 * follow, -16 * wind + 26 * follow)
    P.spine(HUNCH - 18 * wind + 12 * follow, 6 * wind - 10 * follow, -34 * wind + 52 * follow)
    zhead(P, tilt=-6 * wind + 8 * follow, turn=18 * wind - 22 * follow, nod=-8 - 18 * wind - 10 * roar + 14 * follow)
    # right claw: hang -> up and back over the right shoulder -> diagonal rip across to low left
    rest = hang("Right")
    high = V(-0.46, 0.18, 2.02)
    over = V(-0.40, -0.70, 1.86)                         # Bezier pull: the rip passes high in front first
    low = V(0.48, -0.50, 0.84)                           # finishes by the left hip, the giant stays upright
    if rip <= 0:
        tgt = rest * (1 - coil) ** 2 + V(-0.70, 0.02, 1.50) * 2 * (1 - coil) * coil + high * coil * coil
    else:
        u = rip
        arc = high * (1 - u) ** 2 + over * 2 * (1 - u) * u + low * u * u
        tgt = vlerp(rest, arc, back)
    P.arm_ik("Right", cp(P, tgt), pole=(-0.8, 0.5, -0.6), roll=-18 - 24 * follow)
    P.claw("Right", 0.15 + 0.85 * follow)               # fingers splayed for the rip, closing through it
    # left arm: thrown out front-left for balance on the coil, swept back on the rip
    lft = vlerp(vlerp(hang("Left"), V(0.58, -0.52, 1.30), wind), V(0.52, 0.20, 0.92), follow)
    P.arm_ik("Left", cp(P, lft), roll=16)
    P.claw("Left", 0.8)
    # big step in with the left foot as it rips, right heel lifts and pivots
    lf = foot_path(t, [(0.0, (0.06, -0.02, 0)), (0.30, (0.06, -0.02, 0)), (0.50, (0.10, -0.42, 0)), (0.80, (0.10, -0.42, 0)), (0.98, (0.06, -0.02, 0))])
    rf = foot_path(t, [(0.0, (-0.06, 0.04, 0)), (0.20, (-0.06, 0.04, 0)), (0.38, (-0.10, 0.14, 0)), (0.86, (-0.10, 0.14, 0)), (0.99, (-0.06, 0.04, 0))])
    feet(P, tuple(lf), tuple(rf), pl=0, pr=-14 * follow)


# ---------------- Hit: a heavy stagger, one step back, an angry head shake.
def hit(t, P):
    k = ease(t / 0.22) if t < 0.22 else (1 - (t - 0.22) / 0.78) ** 2
    shake = sin(tau(t) * 3) * pulse(t, 0.55, 0.15)
    P.hips = V(0.02 * k, 0.07 * k, -0.05 - 0.03 * k)
    P.rot("Hips", 4 - 6 * k, -6 * k, 4 * k)
    P.spine(HUNCH - 14 * k, -8 * k, 6 * k)
    zhead(P, tilt=-10 * k + 10 * shake, turn=-14 * k + 12 * shake, nod=-6 - 18 * k)
    for sd in ("Left", "Right"):
        s = S_(sd)
        P.arm_ik(sd, cp(P, vlerp(hang(sd), V(s * 0.56, 0.02, 1.10), k)), roll=s * 14)
        P.claw(sd, 0.7 + 0.2 * k)
    rf = foot_path(t, [(0.0, (-0.06, 0.04, 0)), (0.06, (-0.06, 0.04, 0)), (0.26, (-0.06, 0.18, 0)), (0.58, (-0.06, 0.18, 0)), (0.90, (-0.06, 0.04, 0))])
    feet(P, (0.06, -0.02, 0), tuple(rf))


# ---------------- Death: staggers back, drops to its knees, topples onto its back with a heavy thud.
def death(t, P):
    stag = ease(seg(t, 0.0, 0.22))
    kneel = ease(seg(t, 0.18, 0.44))
    fall = ease(seg(t, 0.42, 0.78))
    thud = pulse(t, 0.80, 0.03)
    hz = lerp(-0.05 - 0.04 * stag - 0.42 * kneel, -0.80, fall) + 0.03 * thud
    hy = lerp(0.06 * stag + 0.04 * kneel, 0.70, fall)
    P.hips = V(0.02 * stag, hy, hz)
    P.rot("Hips", lerp(-6 * stag + 10 * kneel, -82, fall), 4 * stag * (1 - fall), 4 * kneel * (1 - fall))
    P.spine(lerp(HUNCH - 10 * stag + 8 * kneel, -6, fall), 0, 0)
    zhead(P, tilt=lerp(-6 * stag, 14, fall), turn=lerp(-10 * stag, 24, fall), nod=lerp(-20 * stag + 16 * kneel, -14, fall) + 6 * thud)
    for sd in ("Left", "Right"):
        s = S_(sd)
        lying = V(s * 0.85, 0.70, 0.08)
        k = ease(seg(fall, 0.3, 0.8))
        P.arm_ik(sd, vlerp(cp(P, vlerp(hang(sd), hang(sd, 0.08, 0.02, -0.06), kneel)), lying, k), roll=s * 10)
        P.claw(sd, 0.8 - 0.4 * fall)
    kn = kneel * (1 - fall)
    pole = V(0, -1, 0)
    P.leg_ik("Left", FOOT_L + V(0.06, -0.02 + 0.30 * kn - 0.20 * fall, 0.0), pitch=-30 * kn - 6 * fall, pole=pole)
    P.leg_ik("Right", FOOT_R + V(-0.06, 0.04 + 0.30 * kn - 0.10 * fall, 0.0), pitch=-30 * kn - 6 * fall, pole=pole)


# ---------------- Rise (spawn): lying on its back -> rolls up to sit -> heaves itself to its feet.
def rise(t, P):
    twitch = pulse(t, 0.06, 0.012) + pulse(t, 0.13, 0.012)
    u = ease(seg(t, 0.16, 0.84))
    hy = lerp(0.70, 0.0, u)
    hz = lerp(-0.80, -0.05, ease(u ** 0.8))
    P.hips = V(0.0, hy, hz)
    P.rot("Hips", lerp(-82, 4, u) + 2 * twitch, 0, 3 * twitch)
    sit = sin(pi * min(u * 1.2, 1.0))
    P.spine(lerp(-6, HUNCH, u) + 28 * sit * (1 - u), 0, 0)
    zhead(P, tilt=8 * twitch, turn=lerp(24, 0, u) + 10 * twitch, nod=lerp(-14, -6, u))
    for sd in ("Left", "Right"):
        s = S_(sd)
        ground = V(s * 0.50, hy + 0.40 * (1 - u) + 0.12, 0.10)    # hands push on the ground
        k = ease(seg(u, 0.55, 1.0))
        P.arm_ik(sd, vlerp(ground, cp(P, hang(sd)), k))
        P.claw(sd, 0.9 - 0.2 * u)
    feet(P, (0.06, -0.02, 0), (-0.06, 0.04, 0))


CLIPS = [("Idle", 70, idle, True, False), ("Walk", 36, walk, True, False), ("Run", 24, run, True, False),
         ("Attack", 48, attack, False, False), ("Hit", 24, hit, False, False),
         ("Death", 84, death, False, True), ("Rise", 80, rise, False, True)]
