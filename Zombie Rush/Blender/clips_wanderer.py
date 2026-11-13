"""Wanderer animations: the classic shambling dead. exec()'d by zombie_anim.py after zr_anim_lib.py.

Character: head lolls to the right, the right arm hangs limp, the left reaches; it limps, dragging the
right foot. Units are metres, the rig stands at the origin facing -Y (left = +X), t runs 0..1 per clip.
Timing marks the game uses (30 fps):
  Attack: the bite lands at t = 0.49 of 40 frames -> 0.65 s (ZombieConfig attackHitDelay)
Ground speeds (the setup plays Walk/Run at the game's speeds): Walk ~0.75 m/s, Run ~3.3 m/s.
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
    """A point in rest space that follows the chest (hands move with the torso)."""
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
    """A hand hanging by the thigh, a little in front."""
    return V(S_(sd) * (0.27 + dx), -0.06 + dy, 0.86 + dz)


def reach(sd, dx=0.0, dy=0.0, dz=0.0):
    """A hand held out in front at chest height, reaching for prey."""
    return V(S_(sd) * (0.22 + dx), -0.48 + dy, 1.22 + dz)


def feet(P, dl=(0, 0, 0), dr=(0, 0, 0), pl=0, pr=0):
    P.leg_ik("Left", FOOT_L + V(*dl), pitch=pl)
    P.leg_ik("Right", FOOT_R + V(*dr), pitch=pr)


def zhead(P, tilt=0, turn=0, nod=0):
    P.rot("Neck", nod * 0.4, tilt * 0.4, turn * 0.4)
    P.rot("Head", nod * 0.6, tilt * 0.6, turn * 0.6)


LOLL = -16     # the head hangs to its right


# ---------------- Idle: sways on the spot, head lolling, jaw working; the left hand twitches up now and then.
def idle(t, P):
    sw = sin(tau(t))
    br = sin(tau(2 * t))
    twitch = pulse(t, 0.62, 0.04)
    jerk = pulse(t, 0.30, 0.02)
    P.hips = V(0.03 * sw, 0.0, -0.02 - 0.01 * br)
    P.rot("Hips", 0, 4 * sw, 3 * sw)
    P.spine(10 + 2 * br + 4 * jerk, 3 * sw, -4 * sw - 3)
    zhead(P, tilt=LOLL + 6 * sin(tau(t) + 1.2) - 10 * jerk, turn=10 * sin(tau(t) + 0.3) + 8 * jerk, nod=12 + 4 * br - 14 * jerk)
    P.rot("RightShoulder", 0, 0, -4)          # the limp side droops
    P.arm_ik("Left", cp(P, vlerp(hang("Left", 0.0, -0.04 * sw, 0.01 * br), reach("Left", 0.02, 0.10, -0.12), ease(twitch * 1.3))), roll=10)
    P.arm_ik("Right", cp(P, hang("Right", -0.02, 0.03 * sw, -0.02)), roll=-14)
    P.claw("Left", 0.45 + 0.4 * twitch)
    P.claw("Right", 0.25)
    feet(P, (0.02, -0.02, 0), (-0.04, 0.06, 0), pr=0)


# ---------------- Gait: shared by Walk and Run. The right foot drags (low, toe scraping) when limp > 0.
def gait(P, t, S, H, stance, lean, drop, bob, sway, roll, twist, wide, limp, arms, headnod):
    P.hips = V(sway * sin(tau(t)), 0.0, -drop - bob * cos(tau(2 * t)) - 0.025 * limp * max(0.0, sin(tau(t))))
    P.rot("Hips", 0, twist * sin(tau(t)), roll * sin(tau(t)) + 3 * limp)
    P.spine(lean + 2 * cos(tau(2 * t)), -twist * 0.4 * sin(tau(t)), -roll * 0.8 * sin(tau(t)) - 4 * limp)
    zhead(P, tilt=LOLL * (0.5 + 0.5 * limp) - 5 * sin(tau(t)), turn=5 * sin(tau(t) + 1), nod=headnod + 5 * cos(tau(2 * t)))
    for side, off in (("Left", 0.0), ("Right", 0.5)):
        p = (t + off) % 1.0
        drag = limp if side == "Right" else 0.0
        if p < stance:
            yy = lerp(-S, S, p / stance); zz = 0.0; pit = 8 * (p / stance - 0.5)
        else:
            u = (p - stance) / (1 - stance)
            yy = lerp(S, -S, ease(u))
            zz = H * (1 - 0.5 * drag) * sin(pi * u)
            pit = -18 * sin(pi * u) * (1 - drag) + 10 * drag * sin(pi * u)   # a dragged foot trails toe-down, just clearing the ground
        rest = FOOT_L if side == "Left" else FOOT_R
        P.leg_ik(side, rest + V(S_(side) * wide, yy, zz), pitch=pit)
    arms(P, t)


def walk_arms(P, t):
    c = cos(tau(t - 0.5))
    # left arm half raised, groping forward; right arm limp, swinging a little
    P.arm_ik("Left", cp(P, vlerp(hang("Left"), reach("Left", 0.03, 0.12, -0.10), 0.55) + V(0, -0.05 * c, 0.02 * c)), roll=12)
    P.arm_ik("Right", cp(P, hang("Right", -0.02, 0.10 * c, 0.02 * abs(c))), roll=-14)
    P.claw("Left", 0.5); P.claw("Right", 0.25)


def run_arms(P, t):
    c = cos(tau(t - 0.5)); s = sin(tau(t))
    # both arms thrown forward, grabbing at the air
    P.arm_ik("Left", cp(P, reach("Left", 0.04, -0.06 - 0.10 * c, 0.06 * s)), roll=20)
    P.arm_ik("Right", cp(P, reach("Right", 0.04, -0.06 + 0.10 * c, -0.06 * s)), roll=-20)
    P.claw("Left", 0.8); P.claw("Right", 0.8)


def walk(t, P):
    gait(P, t, S=0.30, H=0.11, stance=0.62, lean=12, drop=0.04, bob=0.025, sway=0.05, roll=7, twist=7, wide=0.02, limp=1.0,
         arms=walk_arms, headnod=14)


def run(t, P):
    gait(P, t, S=0.50, H=0.20, stance=0.40, lean=24, drop=0.09, bob=0.05, sway=0.04, roll=6, twist=10, wide=0.01, limp=0.3,
         arms=run_arms, headnod=2)


# ---------------- Attack: lunges, grabs with both hands, pulls the prey in and bites. BITE lands at t = 0.49.
def attack(t, P):
    reach_k = ease(seg(t, 0.05, 0.32))                 # arms come up and out
    grab = ease(seg(t, 0.32, 0.42))                    # hands close round the target
    pull = ease(seg(t, 0.40, 0.49))                    # pulls in, lunges with the head
    back = 1 - ease(seg(t, 0.62, 1.0))
    lunge = pull * back
    P.hips = V(0.0, -0.05 * reach_k - 0.06 * lunge, -0.03 - 0.05 * lunge)
    P.rot("Hips", 4 * reach_k * back, 0, 0)
    P.spine(10 + 8 * reach_k * back + 18 * lunge, 0, -3)
    zhead(P, tilt=LOLL * (1 - reach_k * back) + 6 * lunge, turn=0, nod=10 - 18 * reach_k * back + 34 * lunge)
    for sd in ("Left", "Right"):
        s = S_(sd)
        out = reach(sd, 0.06, -0.12, 0.04)            # arms reaching wide
        clutch = V(s * 0.12, -0.42, 1.30)              # hands together, gripping, pulled toward the mouth
        tgt = vlerp(vlerp(hang(sd), out, reach_k * back), clutch, grab * back)
        P.arm_ik(sd, cp(P, tgt), roll=s * (12 + 18 * grab))
        P.claw(sd, 0.4 + 0.6 * grab * back)
    lf = foot_path(t, [(0.0, (0.02, 0, 0)), (0.18, (0.02, 0, 0)), (0.34, (0.02, -0.18, 0)), (0.66, (0.02, -0.18, 0)), (0.90, (0.02, 0, 0))])
    feet(P, tuple(lf), (-0.04, 0.06, 0), pl=0, pr=0)


# ---------------- Hit: jolted backwards, head snaps back, steps back to catch itself.
def hit(t, P):
    k = ease(t / 0.2) if t < 0.2 else (1 - (t - 0.2) / 0.8) ** 2
    P.hips = V(0.0, 0.08 * k, -0.04 * k)
    P.rot("Hips", -8 * k, 0, 4 * k)
    P.spine(8 - 22 * k, 6 * k, 6 * k)
    zhead(P, tilt=LOLL + 10 * k, turn=-14 * k, nod=10 - 36 * k)
    for sd in ("Left", "Right"):
        s = S_(sd)
        P.arm_ik(sd, cp(P, vlerp(hang(sd), V(s * 0.42, 0.06, 1.18), k)), roll=s * 12)
        P.claw(sd, 0.3 + 0.5 * k)
    rf = foot_path(t, [(0.0, (-0.04, 0.06, 0)), (0.05, (-0.04, 0.06, 0)), (0.22, (-0.04, 0.18, 0)), (0.55, (-0.04, 0.18, 0)), (0.88, (-0.04, 0.06, 0))])
    feet(P, (0.02, -0.02, 0), tuple(rf))


# ---------------- Death: knees buckle, sags, topples forward onto its face, arms flop out.
def death(t, P):
    sag = ease(seg(t, 0.0, 0.30))
    fall = ease(seg(t, 0.28, 0.72))
    bounce = pulse(t, 0.76, 0.03)
    settle = ease(seg(t, 0.72, 1.0))
    hz = lerp(-0.20 * sag, -0.80, fall) + 0.02 * bounce
    hy = lerp(-0.04 * sag, -0.70, fall)
    P.hips = V(0.02 * sag, hy, hz)
    P.rot("Hips", lerp(10 * sag, 82, fall), 0, lerp(4 * sag, 8, fall))
    P.spine(lerp(14 * sag, 4, fall), 0, 4 * fall)
    zhead(P, tilt=lerp(LOLL - 10 * sag, -40, fall), turn=lerp(0, -30, fall), nod=lerp(24 * sag, -10, fall) + 8 * bounce)
    for sd in ("Left", "Right"):
        s = S_(sd)
        flop = V(s * 0.50, -1.30, 0.05)                 # on the ground beside the head
        tgt_body = vlerp(hang(sd), hang(sd, 0.02, -0.06, 0.04), sag)
        k = ease(seg(fall, 0.3, 0.8))
        P.arm_ik(sd, vlerp(cp(P, tgt_body), flop + V(0, 0.06 * settle * s, 0), k), roll=s * 10)
        P.claw(sd, 0.4 - 0.2 * fall)
    # knees give way, then the legs lie stretched out behind
    kneel = sag * (1 - fall)
    pole = vlerp(V(0, -1, 0), V(0, -0.2, -1), fall)
    P.leg_ik("Left", FOOT_L + V(0.04, -0.06 * kneel + 0.20 * fall, 0.08 * fall), pitch=-10 * kneel + 60 * fall, pole=pole)
    P.leg_ik("Right", FOOT_R + V(-0.06, 0.04 * kneel + 0.26 * fall, 0.06 * fall), pitch=-6 * kneel + 55 * fall, pole=pole)


# ---------------- Rise (spawn): lying face down -> pushes up on its hands -> kneels -> stands, head last.
def rise(t, P):
    twitch = pulse(t, 0.05, 0.012) + pulse(t, 0.12, 0.012)
    push = ease(seg(t, 0.14, 0.42))
    kneel = ease(seg(t, 0.36, 0.66))
    stand = ease(seg(t, 0.62, 0.92))
    up = max(push * 0.35, kneel * 0.7, stand)
    hz = lerp(-0.80, lerp(-0.45, -0.02, stand), max(push, kneel)) if stand < 1 else -0.02
    hy = lerp(-0.70, lerp(-0.30, 0.0, stand), max(push * 0.5, kneel))
    P.hips = V(0.0, hy, hz + 0.01 * twitch)
    P.rot("Hips", lerp(82, lerp(40, 0, stand), max(push * 0.4, kneel)), 0, 3 * twitch)
    P.spine(lerp(4, lerp(30, 12, stand), max(push, kneel)), 0, 0)
    zhead(P, tilt=lerp(-40, LOLL, up) + 10 * twitch, turn=lerp(-30, 0, up), nod=lerp(-10, 14, up))
    for sd in ("Left", "Right"):
        s = S_(sd)
        ground = V(s * 0.42, -1.05 + 0.40 * kneel, 0.04)     # hands pressed on the ground
        if stand < 0.01:
            P.arm_ik(sd, ground)
        else:
            P.arm_ik(sd, vlerp(ground, cp(P, hang(sd)), stand))
        P.claw(sd, 0.6 - 0.3 * stand)
    pole = vlerp(V(0, -0.2, -1), V(0, -1, 0), max(kneel, stand))
    kn = kneel * (1 - stand)
    P.leg_ik("Left", FOOT_L + V(0.04, lerp(0.20, 0.05, max(push, kneel)) * (1 - stand), lerp(0.08, 0.0, kneel) * (1 - stand)),
             pitch=lerp(60, -20 * kn, max(kneel, stand)), pole=pole)
    P.leg_ik("Right", FOOT_R + V(-0.06, lerp(0.26, -0.10, max(push, kneel)) * (1 - stand) + 0.06 * stand, lerp(0.06, 0.0, kneel) * (1 - stand)),
             pitch=lerp(55, 0, max(kneel, stand)), pole=pole)


CLIPS = [("Idle", 90, idle, True, False), ("Walk", 40, walk, True, False), ("Run", 22, run, True, False),
         ("Attack", 40, attack, False, False), ("Hit", 24, hit, False, False),
         ("Death", 84, death, False, True), ("Rise", 84, rise, False, True)]
