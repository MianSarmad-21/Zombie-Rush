"""Runner animations: a lean, frantic sprinter. exec()'d by zombie_anim.py after zr_anim_lib.py.

Character: always crouched and twitching, hunts in a low prowl, sprints leaning hard forward with
clawing arms, attacks with a leaping two-handed rake. Rig faces -Y (left = +X), t runs 0..1 per clip.
Timing marks the game uses (30 fps):
  Attack: the claws land at t = 0.54 of 36 frames -> 0.65 s (ZombieConfig attackHitDelay)
Ground speeds: Walk (prowl) ~0.9 m/s, Run ~4.5 m/s (the setup matches them to the game's speeds).
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


def ready(sd, dx=0.0, dy=0.0, dz=0.0):
    """Claws held low and forward, ready to grab."""
    return V(S_(sd) * (0.30 + dx), -0.30 + dy, 1.00 + dz)


def feet(P, dl=(0, 0, 0), dr=(0, 0, 0), pl=0, pr=0):
    P.leg_ik("Left", FOOT_L + V(*dl), pitch=pl)
    P.leg_ik("Right", FOOT_R + V(*dr), pitch=pr)


def zhead(P, tilt=0, turn=0, nod=0):
    P.rot("Neck", nod * 0.4, tilt * 0.4, turn * 0.4)
    P.rot("Head", nod * 0.6, tilt * 0.6, turn * 0.6)


CROUCH = 0.14     # how far the hips sit down in its normal stance


# ---------------- Idle: crouched, panting fast, sharp head jerks as it hears things, claws flexing.
def idle(t, P):
    pant = sin(tau(6 * t))
    sw = sin(tau(t))
    j1, j2, j3 = pulse(t, 0.18, 0.012), pulse(t, 0.47, 0.012), pulse(t, 0.80, 0.015)
    look = 26 * (j1 - j2) * 0 + 24 * ease(seg(t, 0.18, 0.22)) * (1 - ease(seg(t, 0.44, 0.50))) - 30 * ease(seg(t, 0.50, 0.54)) * (1 - ease(seg(t, 0.78, 0.84)))
    P.hips = V(0.03 * sw, 0.02, -CROUCH - 0.008 * pant)
    P.rot("Hips", 6, 3 * sw, 2 * sw)
    P.spine(20 + 3 * pant + 6 * j3, 0.3 * look, -2 * sw)
    zhead(P, tilt=10 * j1 - 12 * j2, turn=look, nod=-14 + 2 * pant - 8 * j3)
    for sd in ("Left", "Right"):
        s = S_(sd)
        P.arm_ik(sd, cp(P, ready(sd, 0.0, 0.03 * s * sw, 0.01 * pant)), roll=s * 16)
        P.claw(sd, 0.6 + 0.3 * sin(tau(3 * t) + s))
    feet(P, (0.05, -0.04, 0), (-0.06, 0.08, 0), pl=0, pr=0)


# ---------------- Gait (Walk = low stalking prowl, Run = full sprint)
def gait(P, t, S, H, stance, lean, drop, bob, sway, roll, twist, wide, arms, headnod):
    P.hips = V(sway * sin(tau(t)), 0.0, -drop - bob * cos(tau(2 * t)))
    P.rot("Hips", lean * 0.25, twist * sin(tau(t)), roll * sin(tau(t)))
    P.spine(lean + 3 * cos(tau(2 * t)), -twist * 0.5 * sin(tau(t)), -roll * 0.8 * sin(tau(t)))
    zhead(P, tilt=-4 * sin(tau(t)), turn=4 * sin(tau(t) + 1), nod=headnod + 4 * cos(tau(2 * t)))
    for side, off in (("Left", 0.0), ("Right", 0.5)):
        p = (t + off) % 1.0
        if p < stance:
            yy = lerp(-S, S, p / stance); zz = 0.0; pit = 10 * (p / stance - 0.5)
        else:
            u = (p - stance) / (1 - stance)
            yy = lerp(S, -S, ease(u)); zz = H * sin(pi * u); pit = -24 * sin(pi * u)
        rest = FOOT_L if side == "Left" else FOOT_R
        P.leg_ik(side, rest + V(S_(side) * wide, yy, zz), pitch=pit)
    arms(P, t)


def prowl_arms(P, t):
    c = cos(tau(t - 0.5))
    for sd, k in (("Left", 1), ("Right", -1)):
        P.arm_ik(sd, cp(P, ready(sd, 0.0, -0.06 * k * c, 0.03 * abs(c))), roll=S_(sd) * 16)
        P.claw(sd, 0.7)


def sprint_arms(P, t):
    c = cos(tau(t - 0.5))
    # arms pump hard, opposite to the legs, claws open
    for sd, k in (("Left", 1), ("Right", -1)):
        s = S_(sd)
        fwd = V(s * 0.25, -0.40, 1.14)
        back = V(s * 0.30, 0.10, 0.98)
        P.arm_ik(sd, cp(P, vlerp(back, fwd, 0.5 + 0.5 * k * c)), roll=s * 20)
        P.claw(sd, 0.5 + 0.3 * k * c)


def walk(t, P):
    gait(P, t, S=0.32, H=0.12, stance=0.60, lean=26, drop=CROUCH + 0.04, bob=0.02, sway=0.03, roll=5, twist=8, wide=0.03,
         arms=prowl_arms, headnod=-18)


def run(t, P):
    gait(P, t, S=0.62, H=0.26, stance=0.36, lean=30, drop=0.12, bob=0.06, sway=0.03, roll=5, twist=14, wide=0.01,
         arms=sprint_arms, headnod=-22)


# ---------------- Attack: drops into a crouch, leaps forward raking down with both claws. CLAWS land at t = 0.54.
def attack(t, P):
    coil = ease(seg(t, 0.0, 0.34)) * (1 - ease(seg(t, 0.38, 0.48)))
    raise_ = ease(seg(t, 0.22, 0.42)) * (1 - ease(seg(t, 0.46, 0.56)))
    rake = ease(seg(t, 0.44, 0.58))
    back = 1 - ease(seg(t, 0.66, 1.0))
    lunge = ease(seg(t, 0.38, 0.54)) * back
    P.hips = V(0.0, 0.06 * coil - 0.24 * lunge, -CROUCH - 0.12 * coil + 0.08 * lunge - 0.06 * rake * back)
    P.rot("Hips", 8 + 10 * coil + 12 * lunge, 0, 0)
    P.spine(20 + 10 * coil - 14 * raise_ + 12 * rake * back, 0, 0)
    zhead(P, tilt=0, turn=0, nod=-14 - 10 * raise_ + 6 * rake * back)
    for sd in ("Left", "Right"):
        s = S_(sd)
        high = V(s * 0.38, -0.20, 1.62)           # claws up over the head
        down = V(s * 0.22, -0.58, 1.08)           # raked down through the target, in reach (arm stays bent: no elbow flip)
        side = V(s * 0.52, -0.42, 1.36)           # the arc passes out to the side, not past the shoulder
        if rake > 0:
            # the arc starts relative to the chest (overhead, out to the side) and ends at a point in front of
            # the body at hip height in the world, so a pitched chest can't push it out of reach
            u = rake
            arc = cp(P, high * (1 - u) ** 2 + side * 2 * (1 - u) * u + down * u * u)
            end = V(s * 0.24, -0.62 - 0.24 * lunge, 0.92)
            w = ease(seg(u, 0.5, 1.0))
            tgt = vlerp(cp(P, ready(sd)), vlerp(arc, end, w), back)
        else:
            tgt = cp(P, vlerp(ready(sd), high, raise_))
        P.arm_ik(sd, tgt, roll=s * (18 + 10 * rake))
        P.claw(sd, 0.3 + 0.7 * rake * back)
    # the leap: left foot pushes off and lands forward, right follows
    lf = foot_path(t, [(0.0, (0.05, -0.04, 0)), (0.38, (0.05, -0.04, 0)), (0.52, (0.05, -0.34, 0)), (0.70, (0.05, -0.34, 0)), (0.92, (0.05, -0.04, 0))])
    rf = foot_path(t, [(0.0, (-0.06, 0.08, 0)), (0.54, (-0.06, 0.08, 0)), (0.66, (-0.06, -0.10, 0)), (0.80, (-0.06, -0.10, 0)), (0.98, (-0.06, 0.08, 0))])
    feet(P, tuple(lf), tuple(rf))


# ---------------- Hit: a violent jerk and twist, then snaps back into its crouch.
def hit(t, P):
    k = ease(t / 0.25) if t < 0.25 else (1 - (t - 0.25) / 0.75) ** 2
    P.hips = V(0.03 * k, 0.07 * k, -CROUCH + 0.02 * k)
    P.rot("Hips", 6 - 10 * k, -10 * k, 6 * k)
    P.spine(20 - 26 * k, -16 * k, 10 * k)
    zhead(P, tilt=-16 * k, turn=-24 * k, nod=-14 - 24 * k)
    for sd in ("Left", "Right"):
        s = S_(sd)
        P.arm_ik(sd, cp(P, vlerp(ready(sd), V(s * 0.44, -0.02, 1.20), k)), roll=s * 14)
        P.claw(sd, 0.6 + 0.3 * k)
    rf = foot_path(t, [(0.0, (-0.06, 0.08, 0)), (0.05, (-0.06, 0.08, 0)), (0.22, (-0.06, 0.20, 0)), (0.55, (-0.06, 0.20, 0)), (0.88, (-0.06, 0.08, 0))])
    feet(P, (0.05, -0.04, 0), tuple(rf))


# ---------------- Death: stumbles forward with its momentum and dives onto its face, sliding a little.
def death(t, P):
    stum = ease(seg(t, 0.0, 0.20))
    fall = ease(seg(t, 0.16, 0.56))
    slide = ease(seg(t, 0.52, 0.80))
    bounce = pulse(t, 0.58, 0.03)
    hy = lerp(-0.10 * stum, -0.85, fall) - 0.15 * slide
    hz = lerp(-CROUCH - 0.04 * stum, -0.80, fall) + 0.02 * bounce
    P.hips = V(0.02 * stum, hy, hz)
    P.rot("Hips", lerp(10 + 14 * stum, 84, fall), 0, lerp(0, -10, fall))
    P.spine(lerp(24 + 10 * stum, 0, fall), 0, -4 * fall)
    zhead(P, tilt=lerp(0, 36, fall), turn=lerp(0, 28, fall), nod=lerp(-14 - 10 * stum, -8, fall) + 8 * bounce)
    for sd in ("Left", "Right"):
        s = S_(sd)
        thrown = V(s * 0.40, -0.70, 1.10)             # flung forward as it trips
        flop = V(s * 0.55, -1.42 - 0.12 * slide, 0.05)
        k = ease(seg(fall, 0.3, 0.8))
        P.arm_ik(sd, vlerp(cp(P, vlerp(ready(sd), thrown, stum)), flop, k), roll=s * 10)
        P.claw(sd, 0.6 - 0.3 * fall)
    pole = vlerp(V(0, -1, 0), V(0, -0.2, -1), fall)
    lf = foot_path(t, [(0.0, (0.05, -0.04, 0)), (0.06, (0.05, -0.04, 0)), (0.16, (0.05, -0.30, 0))])
    # lying: legs stretched out behind the hips (they slide with the body)
    P.leg_ik("Left", FOOT_L + vlerp(lf, V(0.04, -0.05 - 0.15 * slide, 0.08), fall), pitch=60 * fall, pole=pole)
    P.leg_ik("Right", FOOT_R + vlerp(V(-0.06, 0.08, 0), V(-0.08, 0.02 - 0.15 * slide, 0.06), fall), pitch=55 * fall, pole=pole)


# ---------------- Rise (spawn): face down, a twitch, then scrambles up fast into its crouch.
def rise(t, P):
    twitch = pulse(t, 0.08, 0.015) + pulse(t, 0.16, 0.015)
    push = ease(seg(t, 0.20, 0.42))
    kneel = ease(seg(t, 0.36, 0.60))
    stand = ease(seg(t, 0.56, 0.84))
    up = max(push * 0.35, kneel * 0.7, stand)
    hz = lerp(-0.80, lerp(-0.50, -CROUCH, stand), max(push, kneel))
    hy = lerp(-0.70, lerp(-0.30, 0.02, stand), max(push * 0.5, kneel))
    P.hips = V(0.0, hy, hz + 0.01 * twitch)
    P.rot("Hips", lerp(82, lerp(46, 6, stand), max(push * 0.4, kneel)), 0, 3 * twitch)
    P.spine(lerp(4, lerp(34, 20, stand), max(push, kneel)), 0, 0)
    zhead(P, tilt=lerp(36, 0, up) + 10 * twitch, turn=lerp(28, 0, up), nod=lerp(-8, -14, up))
    for sd in ("Left", "Right"):
        s = S_(sd)
        ground = V(s * 0.44, -1.05 + 0.40 * kneel, 0.04)
        P.arm_ik(sd, ground if stand < 0.01 else vlerp(ground, cp(P, ready(sd)), stand))
        P.claw(sd, 0.7)
    pole = vlerp(V(0, -0.2, -1), V(0, -1, 0), max(kneel, stand))
    kn = kneel * (1 - stand)
    P.leg_ik("Left", FOOT_L + V(0.05, lerp(0.20, 0.05, max(push, kneel)) * (1 - stand) - 0.04 * stand, lerp(0.08, 0.0, kneel) * (1 - stand)),
             pitch=lerp(60, -20 * kn, max(kneel, stand)), pole=pole)
    P.leg_ik("Right", FOOT_R + V(-0.06, lerp(0.26, -0.10, max(push, kneel)) * (1 - stand) + 0.08 * stand, lerp(0.06, 0.0, kneel) * (1 - stand)),
             pitch=lerp(55, 0, max(kneel, stand)), pole=pole)


CLIPS = [("Idle", 80, idle, True, False), ("Walk", 36, walk, True, False), ("Run", 20, run, True, False),
         ("Attack", 36, attack, False, False), ("Hit", 22, hit, False, False),
         ("Death", 70, death, False, True), ("Rise", 60, rise, False, True)]
