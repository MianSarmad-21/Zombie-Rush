"""Scrap assault rifle clips. exec()'d by gun_build.py; G(right, up, forward) in metres.
Bones: Gun, Mag (curved magazine), HandL (on the handguard), HandR, Muzzle.
Reload (2.1 s in game): rock the old magazine out, toss it, fresh one from the chest rig, rock it in,
slap, reach over and work the charging handle on the right side.
"""
HANDL = REST["HandL"].copy()
MAG_UP = L["MagTop"] - L["MagBottom"]
POUCH = G(-0.20, -0.22, -0.12)


def idle(t, gp):
    a = 2 * math.pi * t
    gp.set("Gun", G(0.002 * math.sin(a), 0.003 * math.sin(2 * a), 0), turn(0.55 * math.sin(2 * a + 0.5), 0.4 * math.sin(a), 0.3 * math.sin(a + 1.1)))
    gp.set("HandL", gp.at("Gun", HANDL), gp.gun_rot())


def fire(t, gp):
    k = ease(seg(t, 0, 0.25)) * (1 - ease(seg(t, 0.25, 1.0)))
    gp.set("Gun", G(0.0015 * k, 0.003 * k, -0.018 * k), turn(2.4 * k, 0.4 * k, 0.8 * k))
    gp.set("HandL", gp.at("Gun", HANDL), gp.gun_rot())


def reload(t, gp):
    # 0.00-0.12 gun rolls right and tips up a little
    # 0.08-0.22 left hand back from the handguard onto the magazine
    # 0.22-0.34 rocks it forward and out; 0.34-0.42 tosses it (falls, gone by 0.55)
    # 0.40-0.55 hand to the chest pouch, fresh magazine (shown from 0.52)
    # 0.55-0.70 up under the magazine well; 0.70-0.77 hooked in and rocked back; slap at 0.79
    # 0.80-0.88 over the top to the charging handle; 0.88 pulled back, 0.92 released
    # 0.92-1.00 back to the handguard
    tilt = ease(seg(t, 0.0, 0.12)) * (1 - ease(seg(t, 0.90, 1.0)))
    slap = bell(t, 0.79, 0.015)
    pull = bell(t, 0.90, 0.025)
    gp.set("Gun", G(-0.02 * tilt, 0.03 * tilt - 0.01 * slap, -0.03 * tilt + 0.01 * pull),
           turn(10 * tilt + 3 * slap - 2 * pull, 8 * tilt, 24 * tilt - 6 * pull))
    gr = gp.gun_rot()
    rest = gp.at("Gun", HANDL)
    grab = gp.at("Gun", L["MagBottom"]) + gr @ (MAG_UP * 0.45)        # palm wrapped round the magazine
    well = gp.at("Gun", L["MagTop"])
    charge = gp.at("Gun", L["Charge"]) + gr @ G(0.01, 0.03, 0.0)
    hand = path(t, [(0.00, rest), (0.08, rest + G(0, -0.03, -0.03)), (0.20, grab), (0.24, grab),
                    (0.34, grab + G(0.0, -0.10, 0.06)), (0.40, grab + G(-0.06, -0.16, 0.02)), (0.50, POUCH), (0.54, POUCH),
                    (0.62, well + gr @ G(-0.02, -0.22, 0.05)), (0.70, well + gr @ G(0, -0.13, 0.03)), (0.77, grab),
                    (0.80, grab + G(0, -0.02, 0)), (0.86, charge), (0.90, charge + G(0, 0, -0.07)), (0.93, charge + G(0, 0.02, -0.05)),
                    (1.00, rest)])
    hold_mag = ease(seg(t, 0.18, 0.24)) * (1 - ease(seg(t, 0.76, 0.80)))
    over = ease(seg(t, 0.80, 0.86)) * (1 - ease(seg(t, 0.93, 0.98)))
    gp.set("HandL", hand, gr @ turn(0, 0, 50 * hold_mag - 120 * over))
    # magazine
    if t < 0.22:
        gp.rel("Mag")
    elif t < 0.34:                         # rocked forward and down out of the well, in the hand
        u = ease(seg(t, 0.22, 0.34))
        gp.rel("Mag", (0, -0.12 * u, 0.04 * u), turn(-18 * u, 0, 0))
    elif t < 0.55:                         # tossed: falls away tumbling
        u = seg(t, 0.34, 0.55)
        start = gp.at("Gun", L["MagTop"]) + gr @ G(0, -0.12, 0.04)
        gp.set("Mag", start + G(-0.10 * u, 0.15 * u - 1.6 * u * u, 0.05 * u), gr @ turn(-18 - 120 * u, 0, 60 * u),
               scale=1.0 if t < 0.53 else 0.001)
    elif t < 0.77:                         # fresh one rides in the hand, then is hooked in and rocked back
        u = ease(seg(t, 0.70, 0.77))
        inhand = hand + gr @ (MAG_UP * 0.55)
        hooked = gp.at("Gun", L["MagTop"]) + gr @ G(0, -0.015, 0.02)
        pos = inhand.lerp(hooked, ease(seg(t, 0.64, 0.70))).lerp(well, u)
        gp.set("Mag", pos, gr @ turn(-14 * (1 - u), 0, 0), scale=1.0 if t > 0.52 else 0.001)
    else:
        gp.rel("Mag")


def draw(t, gp):
    u = 1 - ease(t)
    settle = math.sin(math.pi * seg(t, 0.6, 1.0)) * 0.12
    gp.set("Gun", G(0.06 * u, -0.24 * u + 0.004 * settle, -0.12 * u), turn(-40 * u + 2 * settle, 30 * u, 25 * u))
    reach = ease(seg(t, 0.3, 0.85))
    gp.set("HandL", gp.at("Gun", HANDL) + G(-0.10, -0.12, -0.18) * (1 - reach), gp.gun_rot())


CLIPS = [("Idle", 72, idle, True), ("Fire", 4, fire, False), ("Reload", 63, reload, False), ("Draw", 14, draw, False)]
