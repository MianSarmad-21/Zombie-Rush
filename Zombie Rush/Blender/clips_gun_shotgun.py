"""Double-barrel shotgun clips (Rustbound Double Barrel). exec()'d by gun_build.py; G(right, up, forward) in metres.
Bones: Gun, Barrels (hinged at the front of the action), ShellOut1/2 (the fired shells, ride the barrels),
ShellIn1/2 (fresh shells, ride the left hand then the chambers), HandL (on the fore-end), HandR, Muzzle.
Reload (2.4 s in game): break open, spent shells kick out, two fresh ones from the belt, snap shut.
"""
HANDL = REST["HandL"].copy()
POUCH = G(-0.26, -0.30, -0.08)
CH1, CH2 = L["Chamber1"], L["Chamber2"]
SPREAD = (CH1 - CH2)                        # chamber 2 -> chamber 1


def on_forend(gp):
    gp.set("HandL", gp.at("Barrels", HANDL), gp.world("Barrels").to_quaternion())


def no_shells(gp):
    """Closed gun: every shell hidden (they would poke out of the breech)."""
    for n in ("ShellOut1", "ShellOut2"):
        gp.rel(n, scale=0.001)
    for n in ("ShellIn1", "ShellIn2"):
        gp.set(n, REST[n], scale=0.001)


def idle(t, gp):
    a = 2 * math.pi * t
    gp.set("Gun", G(0.002 * math.sin(a), 0.003 * math.sin(2 * a), 0), turn(0.6 * math.sin(2 * a + 0.4), 0.45 * math.sin(a), 0.35 * math.sin(a + 1.2)))
    gp.rel("Barrels")
    on_forend(gp)
    no_shells(gp)


def fire(t, gp):
    # a heavy shove into the shoulder and a big muzzle climb, slow recovery
    k = ease(seg(t, 0, 0.12)) * (1 - ease(seg(t, 0.12, 1.0)))
    over = math.sin(math.pi * seg(t, 0.5, 1.0)) * 0.1
    gp.set("Gun", G(0.004 * k, 0.012 * k, -0.06 * k), turn(15 * k - 1.5 * over, 1.5 * k, 4 * k))
    gp.rel("Barrels")
    on_forend(gp)
    no_shells(gp)


def reload(t, gp):
    # 0.00-0.12 muzzle dips and the gun rolls so the breech faces the eyes
    # 0.10-0.20 the left hand pushes the barrels down: the action breaks open (40 degrees)
    # 0.20-0.32 the two spent shells kick out backwards over the shoulder
    # 0.26-0.42 left hand to the belt, two fresh shells (shown from 0.42)
    # 0.42-0.58 up to the open breech; 0.58-0.68 thumbed into the chambers
    # 0.68-0.80 hand back to the fore-end; 0.80-0.86 swings the barrels up: snaps shut
    # 0.86-1.00 back up to aim
    tilt = ease(seg(t, 0.0, 0.14)) * (1 - ease(seg(t, 0.86, 1.0)))
    snap = bell(t, 0.855, 0.015)
    # the breech comes up and in towards the face, so the shells are seen going in (first person)
    gp.set("Gun", G(-0.02 * tilt, 0.04 * tilt + 0.01 * snap, 0.02 * tilt), turn(3 * tilt + 6 * snap, 10 * tilt, -22 * tilt))
    opened = ease(seg(t, 0.10, 0.20)) * (1 - ease(seg(t, 0.80, 0.85)))
    gp.rel("Barrels", (0, 0, 0), turn(-32 * opened, 0, 0))
    br = gp.world("Barrels").to_quaternion()
    fore = gp.at("Barrels", HANDL)
    breech = (gp.at("Barrels", CH1) + gp.at("Barrels", CH2)) * 0.5 + G(0, 0.03, -0.06)
    hand = path(t, [(0.00, fore), (0.20, fore), (0.28, fore + G(-0.06, -0.08, -0.10)), (0.38, POUCH + G(0, 0.05, 0.03)),
                    (0.43, POUCH), (0.50, POUCH + G(0.05, 0.12, 0.08)), (0.58, breech), (0.66, breech + G(0, -0.01, 0.03)),
                    (0.72, fore + G(-0.03, -0.06, -0.05)), (0.80, fore), (1.00, fore)])
    # the fore-end hand follows the barrels while it holds them
    if t < 0.20 or t > 0.80:
        hand = fore
    cup = ease(seg(t, 0.40, 0.50)) * (1 - ease(seg(t, 0.66, 0.72)))
    gp.set("HandL", hand, br @ turn(15 * cup, 0, 70 * cup))
    # spent shells: ride the barrels, then fly out back and up, tumbling, gone by 0.50
    for i, ch in ((1, CH1), (2, CH2)):
        if t < 0.10:
            gp.rel(f"ShellOut{i}", scale=0.001)
        elif t < 0.20:
            gp.rel(f"ShellOut{i}")
        elif t < 0.50:
            u = seg(t, 0.20, 0.50)
            start = gp.at("Barrels", ch)
            fly = start + G(0.08 * (1 if i == 1 else -0.6), 0.45 * u - 1.4 * u * u, -0.55 * u)
            gp.set(f"ShellOut{i}", fly, br @ turn(260 * u * (1 if i == 1 else 1.3), 40 * u, 0), scale=1.0 if t < 0.48 else 0.001)
        else:
            gp.rel(f"ShellOut{i}", scale=0.001)
    # fresh shells: hidden until the hand comes back from the belt, carried, pushed home
    for i, ch in ((1, CH1), (2, CH2)):
        home = gp.at("Barrels", ch)
        if t < 0.42:
            gp.set(f"ShellIn{i}", home, br, scale=0.001)
        elif t < 0.68:
            push = ease(seg(t, 0.58, 0.67))
            held = hand + G(0, 0.02, 0.0) + (SPREAD * (0.5 if i == 1 else -0.5)) + G(0, 0, 0.03)
            gp.set(f"ShellIn{i}", held.lerp(home, push), br)
        else:
            gp.set(f"ShellIn{i}", home, br, scale=1.0 if t < 0.84 else 0.001)


def draw(t, gp):
    u = 1 - ease(t)
    settle = math.sin(math.pi * seg(t, 0.6, 1.0)) * 0.12
    gp.set("Gun", G(0.08 * u, -0.25 * u + 0.005 * settle, -0.15 * u), turn(-45 * u + 2 * settle, 25 * u, 20 * u))
    gp.rel("Barrels")
    reach = ease(seg(t, 0.3, 0.85))
    gp.set("HandL", gp.at("Barrels", HANDL) + G(-0.12, -0.10, -0.15) * (1 - reach), gp.world("Barrels").to_quaternion())
    no_shells(gp)


CLIPS = [("Idle", 72, idle, True), ("Fire", 12, fire, False), ("Reload", 72, reload, False), ("Draw", 14, draw, False)]
