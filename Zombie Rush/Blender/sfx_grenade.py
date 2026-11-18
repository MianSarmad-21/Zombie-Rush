"""Grenade sounds (2026-11-18), made from noise and tones, no downloads:

    python Blender/sfx_grenade.py            (plain Python + numpy; also runs inside Blender)

Writes Assets/Audio/SFX/Grenade/ (44.1 kHz mono 16-bit):
  grenade_pin.wav        pin pulled: a metal click and a short ring
  grenade_spoon.wav      lever flies off: a springy metal ping
  grenade_throw.wav      arm swing: a soft whoosh
  grenade_bounce_1..3    the grenade hitting the ground: dull metal clinks
  grenade_explode_1..3   the blast: a hard crack, a deep boom with a sub thump, rumbling debris tail
"""
import os
import wave

import numpy as np

SR = 44100
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Audio", "SFX", "Grenade")
rng = np.random.default_rng(1118)


def t(sec): return np.arange(int(SR * sec)) / SR


def lowpass(x, cutoff):
    """One-pole low-pass, cutoff in Hz (array allowed for a sweep)."""
    cutoff = np.broadcast_to(np.asarray(cutoff, float), x.shape)
    a = np.exp(-2 * np.pi * cutoff / SR)
    y = np.empty_like(x); acc = 0.0
    for i in range(len(x)):
        acc = (1 - a[i]) * x[i] + a[i] * acc
        y[i] = acc
    return y


def highpass(x, cutoff): return x - lowpass(x, cutoff)


def env(n, attack, decay_sec):
    e = np.exp(-np.arange(n) / (SR * decay_sec))
    a = int(SR * attack)
    if a > 0: e[:a] *= np.linspace(0, 1, a)
    return e


def norm(x, peak=0.9):
    m = np.max(np.abs(x))
    return x * (peak / m) if m > 0 else x


def save(name, x):
    os.makedirs(OUT, exist_ok=True)
    x = np.clip(x, -1, 1)
    fade = min(len(x), int(SR * 0.01))
    x[-fade:] *= np.linspace(1, 0, fade)
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((x * 32767).astype(np.int16).tobytes())
    print("[sfx] wrote", name, f"{len(x) / SR:.2f}s")


def metal_ring(sec, freqs, decay, amp):
    tt = t(sec); out = np.zeros_like(tt)
    for f, d, a in zip(freqs, decay, amp):
        out += a * np.sin(2 * np.pi * f * tt + rng.random() * 6) * np.exp(-tt / d)
    return out


def pin():
    n = int(SR * 0.35)
    click = highpass(rng.standard_normal(n) * env(n, 0.0005, 0.004), 2500) * 0.9
    ring = metal_ring(0.35, [3150, 4720, 6900, 8800], [0.05, 0.035, 0.02, 0.012], [0.35, 0.25, 0.15, 0.1])
    slide = np.zeros(n); k = int(SR * 0.03)
    scr = highpass(rng.standard_normal(int(SR * 0.06)), 3000) * np.hanning(int(SR * 0.06)) * 0.25
    slide[k:k + len(scr)] += scr
    save("grenade_pin.wav", norm(click + ring + slide, 0.8))


def spoon():
    n = int(SR * 0.5)
    ping = metal_ring(0.5, [1850, 2610, 4100, 5900], [0.12, 0.08, 0.05, 0.03], [0.5, 0.35, 0.2, 0.12])
    twang = np.sin(2 * np.pi * (900 + 400 * np.exp(-t(0.5) / 0.05)) * t(0.5)) * np.exp(-t(0.5) / 0.08) * 0.3
    tick = highpass(rng.standard_normal(n) * env(n, 0.0003, 0.003), 3000) * 0.8
    save("grenade_spoon.wav", norm(ping + twang + tick, 0.7))


def throw():
    sec = 0.42; n = int(SR * sec); tt = t(sec)
    noise = rng.standard_normal(n)
    shape = np.sin(np.pi * np.clip(tt / sec, 0, 1)) ** 2
    sweep = 400 + 2200 * np.sin(np.pi * np.clip(tt / sec, 0, 1))
    x = lowpass(highpass(noise, 250), sweep) * shape
    save("grenade_throw.wav", norm(x, 0.55))


def bounce(i):
    sec = 0.3; n = int(SR * sec)
    base = [620, 540, 700][i]
    thud = lowpass(rng.standard_normal(n), 900) * env(n, 0.0005, 0.012) * 1.2
    clink = metal_ring(sec, [base * 3.1, base * 4.7, base * 6.6], [0.03, 0.02, 0.012], [0.4, 0.3, 0.2])
    grit = highpass(rng.standard_normal(n), 2000) * env(n, 0.0005, 0.02) * 0.25
    save(f"grenade_bounce_{i + 1}.wav", norm(thud + clink + grit, 0.6 - 0.05 * i))


def explode(i):
    sec = 3.2; n = int(SR * sec); tt = t(sec)
    noise = rng.standard_normal(n)
    # 1. the crack: a very short bright burst
    crack = highpass(noise * env(n, 0.0002, 0.012), 1200) * 1.4
    # 2. the boom: noise through a low-pass that closes fast, plus a falling sine thump
    cut = 150 + 4500 * np.exp(-tt / 0.08)
    boom = lowpass(noise, cut) * env(n, 0.002, 0.42 + 0.06 * i) * 2.6
    f0 = [48, 42, 55][i]
    thump = np.sin(2 * np.pi * np.cumsum(f0 + 90 * np.exp(-tt / 0.06)) / SR) * env(n, 0.003, 0.35) * 1.3
    # 3. the tail: low rumble and falling debris (sparse ticks)
    rumble = lowpass(rng.standard_normal(n), 120) * np.exp(-tt / 1.1) * np.clip(tt / 0.15, 0, 1) * 2.0
    debris = np.zeros(n)
    for _ in range(26 + 6 * i):
        at = int(SR * (0.25 + rng.random() * 1.6)); ln = int(SR * 0.02)
        if at + ln < n:
            debris[at:at + ln] += highpass(rng.standard_normal(ln), 1800) * np.exp(-np.arange(ln) / (SR * 0.004)) * rng.uniform(0.05, 0.22)
    x = crack + boom + thump + rumble + debris
    x = np.tanh(x * 1.4) / np.tanh(1.4)          # a little saturation: it hits harder
    save(f"grenade_explode_{i + 1}.wav", norm(x, 0.95))


if __name__ == "__main__":
    pin(); spoon(); throw()
    for i in range(3): bounce(i)
    for i in range(3): explode(i)
