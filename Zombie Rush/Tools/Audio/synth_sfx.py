"""Synthesizes the special-zombie sound effects into Assets/Audio/SFX/Specials.

  python Tools/Audio/synth_sfx.py

Pure Python (no numpy). Each sound is built from noise, filtered sweeps and envelopes.
"""
import math
import os
import random
import struct
import wave

RATE = 44100
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, '..', '..', 'Assets', 'Audio', 'SFX', 'Specials'))


def save(name, samples):
    os.makedirs(OUT, exist_ok=True)
    peak = max(1e-6, max(abs(s) for s in samples))
    k = 0.89 / peak
    path = os.path.join(OUT, name + '.wav')
    with wave.open(path, 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b''.join(struct.pack('<h', int(max(-1, min(1, s * k)) * 32767)) for s in samples))
    print('wrote', path)


def lowpass(xs, cutoff_fn):
    """One-pole low-pass; cutoff_fn(t) gives the cutoff in Hz over time."""
    out, y = [], 0.0
    for i, x in enumerate(xs):
        c = cutoff_fn(i / RATE)
        a = 1.0 - math.exp(-2.0 * math.pi * c / RATE)
        y += a * (x - y)
        out.append(y)
    return out


def noise(n, seed):
    r = random.Random(seed)
    return [r.uniform(-1, 1) for _ in range(n)]


def beep():
    n = int(0.09 * RATE)
    return [math.sin(2 * math.pi * 2100 * i / RATE) * (1 if i < n - 400 else (n - i) / 400) *
            min(1, i / 60) * (0.6 + 0.4 * math.sin(2 * math.pi * 4200 * i / RATE)) for i in range(n)]


def explosion(seed, length=1.8):
    n = int(length * RATE)
    ns = noise(n, seed)
    body = lowpass(ns, lambda t: 2600 * math.exp(-t * 3.2) + 120)
    rumble = lowpass(noise(n, seed + 1), lambda t: 90)
    out = []
    for i in range(n):
        t = i / RATE
        env = min(1, t / 0.004) * math.exp(-t * 2.4)
        thump = math.sin(2 * math.pi * (55 * math.exp(-t * 2.0) + 30) * t) * math.exp(-t * 5.0)
        crackle = (ns[i] if random.Random(i // 300 + seed).random() < 0.12 else 0) * math.exp(-t * 6)
        out.append(body[i] * env * 1.4 + rumble[i] * 6 * env + thump * 0.9 + crackle * 0.25)
    return out


def spit():
    n = int(0.45 * RATE)
    ns = noise(n, 3)
    hiss = lowpass(ns, lambda t: 5000 * math.exp(-t * 6) + 400)
    out = []
    for i in range(n):
        t = i / RATE
        gurgle = math.sin(2 * math.pi * (180 + 60 * math.sin(2 * math.pi * 23 * t)) * t) * math.exp(-t * 7)
        env = min(1, t / 0.01) * math.exp(-t * 7)
        out.append(hiss[i] * env * 1.2 + gurgle * 0.5 * min(1, t / 0.05))
    return out


def splat():
    n = int(0.5 * RATE)
    ns = noise(n, 9)
    wet = lowpass(ns, lambda t: 1600 * math.exp(-t * 9) + 150)
    out = []
    for i in range(n):
        t = i / RATE
        bubbles = sum(math.sin(2 * math.pi * f * t) * math.exp(-max(0, t - d) * 30) * (t > d)
                      for f, d in ((420, 0.05), (610, 0.11), (350, 0.18), (520, 0.26)))
        out.append(wet[i] * min(1, t / 0.003) * math.exp(-t * 8) * 1.6 + bubbles * 0.15)
    return out


def shriek():
    n = int(1.6 * RATE)
    ns = noise(n, 21)
    breath = lowpass(ns, lambda t: 3500)
    out, ph = [], [0.0, 0.0, 0.0]
    for i in range(n):
        t = i / RATE
        f0 = 620 + 380 * min(1, t / 0.25) - 160 * max(0, t - 0.9) + 25 * math.sin(2 * math.pi * 7 * t)
        s = 0
        for j, (mul, amp) in enumerate(((1.0, 1.0), (2.01, 0.55), (3.03, 0.3))):
            ph[j] += 2 * math.pi * f0 * mul / RATE
            s += math.sin(ph[j] + 0.8 * math.sin(ph[j] * 0.5)) * amp
        env = min(1, t / 0.08) * (1 if t < 1.1 else max(0, 1 - (t - 1.1) / 0.5))
        out.append((s * 0.55 + breath[i] * 0.6) * env)
    return out


if __name__ == '__main__':
    save('bomb_beep', beep())
    save('explosion_1', explosion(1))
    save('explosion_2', explosion(7, 2.1))
    save('goo_spit', spit())
    save('goo_splat', splat())
    save('shriek', shriek())
