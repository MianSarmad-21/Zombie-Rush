"""Pistol clips (Rustborn Sidearm). exec()'d by gun_build.py; positions in metres, G(right, up, forward).
Bones: Gun (whole gun, turns about the grip), Slide, Mag (magazine + its body), HandL (support hand),
HandR (fixed on the grip), Muzzle. Every clip starts and ends on the rest pose so they cut cleanly.
The game stretches Reload to the gun's reload time (WeaponData.reloadTime, 1.4 s).
"""
HANDL = REST["HandL"].copy()
MAG_UP = L["MagTop"] - L["MagBottom"]          # magazine bottom -> top
POUCH = G(-0.20, -0.42, -0.36)                 # magazine pouch on the left hip, seen from the gun


def idle(t, gp):
    # breathing: a slow figure-eight drift of the muzzle
    a = 2 * math.pi * t
    gp.set("Gun", G(0.0015 * math.sin(a), 0.002 * math.sin(2 * a), 0), turn(0.5 * math.sin(2 * a + 0.6), 0.35 * math.sin(a), 0.3 * math.sin(a + 1.0)))
    gp.set("HandL", gp.at("Gun", HANDL), gp.gun_rot())


def fire(t, gp):
    # snap up and back, slide cycles, settles with a small overshoot
    k = ease(seg(t, 0, 0.18)) * (1 - ease(seg(t, 0.18, 1.0)))
    over = math.sin(math.pi * seg(t, 0.55, 1.0)) * 0.12
    gp.set("Gun", G(0, 0.006 * k, -0.028 * k), turn(10 * k - 1.2 * over, 0.6 * k, 1.5 * k))
    slide = ease(seg(t, 0.0, 0.12)) * (1 - ease(seg(t, 0.22, 0.42)))
    gp.rel("Slide", (0, 0, -0.024 * slide))
    gp.set("HandL", gp.at("Gun", HANDL), gp.gun_rot())


def reload(t, gp):
    # 0.00-0.14 gun turns in and tips up (magazine side towards the left hand)
    # 0.14-0.22 magazine released: slides out of the grip and drops away (gone by 0.36)
    # 0.10-0.40 left hand down to the hip pouch, takes a fresh magazine (shown from 0.40)
    # 0.40-0.66 brings it up under the grip; 0.66-0.74 pushes it home, a palm slap at 0.75
    # 0.80-0.92 hand over the top, racks the slide back (0.86) and lets it slam forward (0.89)
    # 0.92-1.00 back to the two-hand grip
    tilt = ease(seg(t, 0.0, 0.14)) * (1 - ease(seg(t, 0.90, 1.0)))
    slap = bell(t, 0.75, 0.02)
    rack = bell(t, 0.86, 0.03)
    gp.set("Gun", G(0.03 * tilt, 0.05 * tilt - 0.004 * slap, -0.03 * tilt), turn(18 * tilt + 4 * slap - 3 * rack, 16 * tilt, -28 * tilt + 6 * rack))
    gr = gp.gun_rot()
    well = gp.at("Gun", L["MagBottom"])          # where the magazine's bottom sits in the grip
    rest = gp.at("Gun", HANDL)
    over = gp.at("Gun", REST["Slide"] + G(0.0, 0.035, -0.06))   # hand on top, behind the slide
    hand = path(t, [(0.00, rest), (0.12, rest + G(-0.04, -0.06, -0.02)), (0.26, POUCH + G(0.02, 0.06, 0.04)), (0.36, POUCH),
                    (0.42, POUCH + G(0, 0.02, 0)), (0.56, well + G(-0.03, -0.10, 0)), (0.66, well + G(0, -0.025, 0)),
                    (0.74, well + G(0, 0.004, 0)), (0.78, well + G(-0.02, -0.04, 0)), (0.84, over),
                    (0.88, over + G(0, 0, -0.03)), (0.92, over + G(0.0, 0.0, -0.01)), (1.00, rest)])
    # hand turns palm-up under the grip while it carries the magazine, palm-down over the slide
    carry = ease(seg(t, 0.36, 0.46)) * (1 - ease(seg(t, 0.76, 0.82)))
    top = ease(seg(t, 0.78, 0.84)) * (1 - ease(seg(t, 0.92, 0.98)))
    hrot = gr @ turn(0, 10 * carry, 60 * carry - 150 * top)
    gp.set("HandL", hand, hrot)
    # magazine: in the grip, slides out, falls; a fresh one rides in the hand and goes home
    if t < 0.14:
        gp.rel("Mag")
    elif t < 0.36:
        out = ease(seg(t, 0.14, 0.22)) * 0.07
        fall = seg(t, 0.20, 0.36)
        gp.rel("Mag", (0.0, -out - 0.9 * fall * fall, 0.0), turn(0, 0, 40 * fall), scale=1.0 if t < 0.34 else 0.001)
    elif t < 0.40:
        gp.rel("Mag", (0, -2.0, 0), scale=0.001)
    elif t < 0.74:
        ins = ease(seg(t, 0.66, 0.74))
        inhand = hand + gr @ MAG_UP          # magazine standing in the palm, top up
        home = gp.at("Gun", L["MagTop"])
        gp.set("Mag", inhand.lerp(home, ins), gr)
    else:
        gp.rel("Mag")
    gp.rel("Slide", (0, 0, -0.026 * ease(seg(t, 0.84, 0.88)) * (1 - ease(seg(t, 0.88, 0.90)))))


def draw(t, gp):
    # comes up from low on the right, muzzle down, rolls level and settles
    u = 1 - ease(t)
    settle = math.sin(math.pi * seg(t, 0.6, 1.0)) * 0.15
    gp.set("Gun", G(0.06 * u, -0.22 * u + 0.004 * settle, -0.10 * u), turn(-55 * u + 2 * settle, -10 * u, 35 * u))
    reach = ease(seg(t, 0.35, 0.85))
    gp.set("HandL", gp.at("Gun", HANDL) + G(-0.10, -0.08, -0.05) * (1 - reach), gp.gun_rot())


CLIPS = [("Idle", 60, idle, True), ("Fire", 8, fire, False), ("Reload", 42, reload, False), ("Draw", 12, draw, False)]
