# Builds the pickup sounds from the game's own CC0 gun recordings (no new sources), with Blender's audio module:
#   weapon_pickup.wav : a strap/cloth grab (weapon_draw_2) then the rifle's bolt rack (end of reload_rifle)
#   ammo_pickup.wav   : a short cloth touch then the rifle reload's magazine clack
#   gear_pickup.wav   : the belt/latch draw (weapon_draw_3), trimmed and levelled (health, armour)
#   blender -b --factory-startup -P Blender/sfx_pickups.py
# Writes into Assets/Audio/SFX/Game. Times below are click onsets measured in the source files.
import aud, numpy as np, os

here = os.path.dirname(os.path.abspath(__file__))
sfx = os.path.join(here, "..", "Assets", "Audio", "SFX")
RATE = 44100

def load(rel):
    s = aud.Sound(os.path.join(sfx, rel))
    d = s.data()
    d = d.mean(axis=1) if d.ndim > 1 else d
    assert int(s.specs[0]) == RATE, rel
    return d.astype(np.float32)

def cut(d, start, end, fade_in=0.004, fade_out=0.03):
    a, b = int(start * RATE), min(len(d), int(end * RATE))
    piece = d[a:b].copy()
    fi, fo = int(fade_in * RATE), int(fade_out * RATE)
    piece[:fi] *= np.linspace(0, 1, fi)
    piece[-fo:] *= np.linspace(1, 0, fo)
    return piece

def mix(length, parts):
    out = np.zeros(int(length * RATE), np.float32)
    for at, piece, gain in parts:
        i = int(at * RATE)
        n = min(len(piece), len(out) - i)
        out[i:i + n] += piece[:n] * gain
    return out

def write(name, d, peak=0.8):
    d = d / max(1e-6, np.abs(d).max()) * peak
    # trim trailing silence
    loud = np.nonzero(np.abs(d) > 0.004)[0]
    d = d[:loud[-1] + int(0.02 * RATE)] if len(loud) else d
    path = os.path.abspath(os.path.join(sfx, "Game", name))
    snd = aud.Sound.buffer(d.reshape(-1, 1), RATE)
    snd.write(path, RATE, aud.CHANNELS_MONO, aud.FORMAT_S16, aud.CONTAINER_WAV, aud.CODEC_PCM)
    print(f"[sfx_pickups] {name}: {len(d) / RATE:.2f}s, {os.path.getsize(path)} bytes")

rifle = load("Weapons/reload_rifle.wav")       # onsets 0.55, 1.62 (mag in), 1.85 + 1.92 (bolt pull, release)
draw2 = load("Weapons/weapon_draw_2.wav")      # cloth + strap, onsets 0.11, 0.22
draw3 = load("Weapons/weapon_draw_3.wav")      # belt + latch, onsets 0.07, 0.16

grab = cut(draw2, 0.06, 0.34)
rack = cut(rifle, 1.80, 2.26, fade_out=0.06)
write("weapon_pickup.wav", mix(0.8, [(0.0, grab, 0.75), (0.17, rack, 1.0)]))

touch = cut(draw3, 0.03, 0.14, fade_out=0.02)
clack = cut(rifle, 1.585, 1.80, fade_out=0.05)
write("ammo_pickup.wav", mix(0.45, [(0.0, touch, 0.5), (0.06, clack, 1.0)]))

write("gear_pickup.wav", cut(draw3, 0.02, 0.42, fade_out=0.08), peak=0.7)
