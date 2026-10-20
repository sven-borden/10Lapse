"""The 10Lapse showreel soundtrack, synthesized from scratch: 30 s, 120 BPM, 48 kHz stereo.
Every cue sits on the same timeline as reel.html.   python music.py out.wav
"""
import sys
import wave

import numpy as np

SR = 48000
DUR = 30.0
N = int(SR * DUR)
TAU = 2 * np.pi
rng = np.random.default_rng(10)


def T(d):
    return np.arange(int(round(d * SR))) / SR


NAMES = {'C': 0, 'C#': 1, 'D': 2, 'D#': 3, 'E': 4, 'F': 5, 'F#': 6, 'G': 7, 'G#': 8, 'A': 9, 'A#': 10, 'B': 11}


def hz(s):
    return 440.0 * 2 ** ((12 * (int(s[-1]) + 1) + NAMES[s[:-1]] - 69) / 12)


def norm(x):
    m = np.abs(x).max()
    return x / m if m > 0 else x


class Bus:
    def __init__(self):
        self.x = np.zeros((2, N))

    def add(self, sig, t0, gain=1.0, pan=0.0):
        i = int(round(t0 * SR))
        if sig.ndim == 1:
            a = (np.clip(pan, -1, 1) + 1) * np.pi / 4
            st = np.vstack([sig * np.cos(a), sig * np.sin(a)]) * np.sqrt(2)
        else:
            st = sig
        st = st * gain
        if i < 0:
            st, i = st[:, -i:], 0
        j = min(N, i + st.shape[1])
        if j > i:
            self.x[:, i:j] += st[:, :j - i]


def spec(x, resp):
    n = len(x)
    f = np.fft.rfftfreq(n, 1 / SR)
    return np.fft.irfft(np.fft.rfft(x) * resp(f), n)


def hp(fc, o=2):
    return lambda f: 1 / np.sqrt(1 + (fc / np.maximum(f, 1e-3)) ** (2 * o))


def lp(fc, o=2):
    return lambda f: 1 / np.sqrt(1 + (f / fc) ** (2 * o))


def bp(fc, q):
    return lambda f: 1 / np.sqrt(1 + (q * (f / fc - fc / np.maximum(f, 1e-3))) ** 2)


def pan_sweep(sig, p0, p1):
    p = np.linspace(p0, p1, len(sig))
    a = (np.clip(p, -1, 1) + 1) * np.pi / 4
    return np.vstack([sig * np.cos(a), sig * np.sin(a)]) * np.sqrt(2)


def shaped_noise(dur, fc, bw, nfft=2048, hop=512):
    n = int(dur * SR)
    x = rng.standard_normal(n + nfft)
    win = np.hanning(nfft)
    f = np.fft.rfftfreq(nfft, 1 / SR)
    out = np.zeros(n + nfft)
    for s in range(0, n, hop):
        tc = s / SR
        g = np.exp(-0.5 * (np.log2(np.maximum(f, 1) / fc(tc)) / bw(tc)) ** 2)
        out[s:s + nfft] += np.fft.irfft(np.fft.rfft(x[s:s + nfft] * win) * g, nfft) * win
    return out[:n]


# ---------------------------------------------------------------- instruments
def kick(f_hi=150, f_lo=46, punch=0.042, decay=0.32, click=0.3, dur=0.7):
    t = T(dur)
    f = f_lo + (f_hi - f_lo) * np.exp(-t / punch)
    body = np.sin(TAU * np.cumsum(f) / SR) * np.exp(-t / decay)
    cl = spec(rng.standard_normal(len(t)) * np.exp(-t / 0.0022), hp(1800)) * click
    return np.tanh(1.8 * (body + cl)) / np.tanh(1.8)


def clap(dur=0.6):
    t = T(dur)
    env = np.zeros_like(t)
    for off in (0, 0.0105, 0.021):
        env += np.where(t >= off, np.exp(-(t - off) / 0.0065), 0) * 0.7
    env += np.where(t >= 0.03, np.exp(-(np.maximum(t - 0.03, 0)) / 0.13), 0)
    return norm(spec(rng.standard_normal(len(t)) * env, bp(1350, 1.1)))


def snare(dur=0.45):
    t = T(dur)
    body = np.sin(TAU * np.cumsum(180 + 70 * np.exp(-t / 0.018)) / SR) * np.exp(-t / 0.085)
    nz = norm(spec(rng.standard_normal(len(t)) * np.exp(-t / 0.12), bp(3200, 0.6)))
    return norm(0.6 * body + 0.85 * nz)


def hat(open_=False):
    t = T(0.45 if open_ else 0.09)
    s = spec(rng.standard_normal(len(t)), hp(7200, 3)) * np.exp(-t / (0.13 if open_ else 0.02))
    return norm(s)


def crash(dur=3.0):
    t = T(dur)
    s = norm(spec(rng.standard_normal(len(t)), hp(3800, 2))) * np.exp(-t / 1.0)
    for f in (3150, 4470, 5320, 6810, 7930, 9120):
        s += 0.06 * np.sin(TAU * f * t + rng.uniform(0, TAU)) * np.exp(-t / 0.8)
    return norm(s * np.minimum(1, t / 0.002))


def fm(f, dur=0.5, ratio=2.0, index=2.0, idecay=0.08, adecay=0.35):
    t = T(dur)
    s = np.sin(TAU * f * t + index * np.exp(-t / idecay) * np.sin(TAU * f * ratio * t)) * np.exp(-t / adecay)
    return s * np.minimum(1, t / 0.003)


def bell(f, dur=3.0, decay=1.1):
    t = T(dur)
    s = np.sin(TAU * f * t + 2.6 * np.exp(-t / 0.5) * np.sin(TAU * f * 3.5 * t)) * np.exp(-t / decay)
    s += 0.35 * np.sin(TAU * f * 2.0 * t) * np.exp(-t / (decay * 0.5))
    return norm(s * np.minimum(1, t / 0.002))


def tick(f=2600, decay=0.006, noise=0.5, dur=0.06):
    t = T(dur)
    s = np.sin(TAU * f * t) * np.exp(-t / decay)
    s += noise * norm(spec(rng.standard_normal(len(t)), hp(3000))) * np.exp(-t / 0.0025)
    return norm(s)


def shutter():
    t = T(0.14)
    s = np.zeros_like(t)
    for off, g in ((0, 1.0), (0.04, 0.65)):
        m = t >= off
        tt = t[m] - off
        s[m] += g * rng.standard_normal(m.sum()) * np.exp(-tt / 0.0045)
        s[m] += g * 0.6 * np.sin(TAU * 150 * tt) * np.exp(-tt / 0.014)
    return norm(spec(s, lambda f: bp(2600, 0.7)(f) + 0.5 * lp(260)(f)))


def impact(dur=3.5, f0=78, f1=30, decay=1.0):
    t = T(dur)
    f = f1 + (f0 - f1) * np.exp(-t / 0.22)
    sub = np.sin(TAU * np.cumsum(f) / SR) * np.exp(-t / decay)
    nz = norm(spec(rng.standard_normal(len(t)), lp(700))) * np.exp(-t / 0.2)
    return norm(np.tanh(1.4 * (sub + 0.45 * nz)))


def whoosh(dur, f0, f1, peak=0.6, bw=0.85, p0=-0.6, p1=0.6):
    sig = shaped_noise(dur, lambda tc: f0 * (f1 / f0) ** (tc / dur), lambda tc: bw)
    u = T(dur) / dur
    env = np.where(u < peak, (u / peak) ** 2, ((1 - u) / (1 - peak)) ** 1.5)
    return pan_sweep(norm(sig) * env, p0, p1)


def riser(dur, f0=250, f1=5000, tone=110):
    sig = norm(shaped_noise(dur, lambda tc: f0 * (f1 / f0) ** (tc / dur), lambda tc: 0.55))
    t = T(dur)
    env = (t / dur) ** 2.4
    saw = sum(np.sin(TAU * k * np.cumsum(tone * 2 ** (2 * t / dur)) / SR) / k for k in range(1, 8))
    return (sig + 0.18 * norm(saw)) * env


def reverse_swell(dur=1.2):
    c = crash(dur + 0.3)[:int(dur * SR)][::-1]
    return c * (T(dur) / dur) ** 1.5


def rewind(dur=0.48):
    t = T(dur)
    f = 1100 * (130 / 1100) ** (t / dur)
    ph = TAU * np.cumsum(f) / SR
    tone = sum(np.sin(k * ph) / k for k in range(1, 6))
    gate = 0.55 + 0.45 * np.sign(np.sin(TAU * 34 * t))
    nz = norm(spec(rng.standard_normal(len(t)), bp(1800, 0.5)))
    env = np.sin(np.pi * t / dur) ** 0.7
    return norm((0.7 * norm(tone) + 0.35 * nz) * gate * env)


def pad_note(f, dur, att=0.3, rel=1.0, bright=2000, voices=3, cents=9, spread=0.7):
    t = T(dur + rel)
    out = np.zeros((2, len(t)))
    for v in range(voices):
        fv = f * 2 ** ((v - (voices - 1) / 2) * cents / 1200)
        K = int(min(26, 9000 // fv))
        k = np.arange(1, K + 1)[:, None]
        g = (1 / k) / np.sqrt(1 + (k * fv / bright) ** 4)
        s = (g * np.sin(TAU * k * fv * t[None, :] + rng.uniform(0, TAU, (K, 1)))).sum(0)
        a = ((v - (voices - 1) / 2) * spread + 1) * np.pi / 4
        out += np.vstack([s * np.cos(a), s * np.sin(a)])
    env = np.minimum(1, t / att) * np.where(t > dur, np.cos(np.minimum(1, (t - dur) / rel) * np.pi / 2), 1)
    env = np.sin(env * np.pi / 2) ** 2
    return out * env / voices


def bass_note(f, dur, att=0.006, dec=0.2, sus=0.55, rel=0.06, drive=1.6):
    t = T(dur + rel)
    s = np.sin(TAU * f * t) + 0.22 * np.sin(TAU * 2 * f * t) + 0.08 * np.sin(TAU * 3 * f * t)
    env = np.minimum(1, t / att) * (sus + (1 - sus) * np.exp(-t / dec))
    env *= np.where(t > dur, np.maximum(0, 1 - (t - dur) / rel), 1)
    return np.tanh(drive * s * env) / np.tanh(drive)


# ---------------------------------------------------------------- the score
drums, music, pads, sfx, bells = Bus(), Bus(), Bus(), Bus(), Bus()

CH = [  # start, length, voicing, brightness
    (2.0, 2.0, ['B2', 'F#3', 'D4', 'C#5'], 1300), (4.0, 2.0, ['G2', 'D3', 'B3', 'F#4'], 1450),
    (6.0, 2.0, ['D3', 'A3', 'F#4', 'E5'], 1900), (8.0, 2.0, ['A2', 'E3', 'A3', 'C#4', 'E4'], 2300),
    (10.0, 2.0, ['B2', 'F#3', 'A3', 'D4', 'F#4'], 2300), (12.0, 2.0, ['G2', 'D3', 'A3', 'B3', 'F#4'], 2400),
    (14.0, 2.0, ['D3', 'A3', 'D4', 'F#4', 'A4'], 2500), (16.0, 2.0, ['F#2', 'C#3', 'A3', 'E4'], 1700),
    (18.0, 1.0, ['G2', 'D3', 'B3', 'F#4'], 2000), (19.0, 1.0, ['A2', 'E3', 'C#4', 'E4'], 1500),
    (20.0, 2.0, ['B2', 'F#3', 'D4', 'F#4'], 2400), (22.0, 1.0, ['G2', 'D3', 'B3', 'G4'], 3000),
    (23.0, 1.0, ['A2', 'E3', 'C#4', 'A4'], 3600), (24.0, 2.0, ['D3', 'A3', 'F#4', 'A4', 'D5'], 2600),
    (26.0, 2.0, ['G2', 'D3', 'B3', 'F#4', 'A4'], 950), (28.0, 2.0, ['D2', 'A2', 'D3', 'F#3', 'A3', 'E4'], 2800),
]
ROOT = {2.0: 'B1', 4.0: 'G1', 6.0: 'D2', 8.0: 'A1', 10.0: 'B1', 12.0: 'G1', 14.0: 'D2', 16.0: 'F#1', 18.0: 'G1', 19.0: 'A1',
        20.0: 'B1', 22.0: 'G1', 23.0: 'A1', 24.0: 'D2', 26.0: 'G1', 28.0: 'D1'}

# intro drone: an open fifth that swells into the first downbeat
for nm, g in (('D2', 0.5), ('A2', 0.35), ('D3', 0.2)):
    pads.add(pad_note(hz(nm), 1.75, att=1.4, rel=0.4, bright=700), 0.0, gain=g * 0.9)
for t0, d, notes, br in CH:
    last = t0 >= 28
    for k, nm in enumerate(notes):
        pads.add(pad_note(hz(nm), d + (0.6 if last else 0.05), att=0.12 if t0 in (8.0, 21.0, 28.0) else 0.3, rel=2.2 if last else 0.9, bright=br),
                  t0, gain=(0.15 if last else 0.12) * (0.75 if k == 0 else 1.0))

# bass: held under the opening, pulsing eighths once the groove starts
for t0, d, notes, br in CH:
    f = hz(ROOT[t0])
    if t0 < 8 or 16 <= t0 < 18 or t0 >= 26:
        if t0 >= 28:
            music.add(bass_note(f, 1.4, dec=0.6, sus=0.5, rel=0.6), t0, gain=0.55)
        else:
            music.add(bass_note(f, d - 0.05, att=0.04, dec=0.8, sus=0.7, rel=0.08, drive=1.2), t0, gain=0.42 if t0 >= 26 else 0.5)
    else:
        step = 0.5 if t0 >= 24 else 0.25
        k = 0
        while k * step < d - 1e-6:
            fo = f * (2 if (k % 4 == 3 and step == 0.25) else 1)
            music.add(bass_note(fo, step * 0.8, dec=0.12, sus=0.45), t0 + k * step, gain=0.5)
            k += 1

# drums
kicks = [2.0, 2.75, 4.0, 4.75, 6.0, 6.75]
kicks += [8.0 + 0.5 * i for i in range(16)]
kicks += [16.0, 17.0, 17.5]
kicks += [18.0 + 0.5 * i for i in range(4)] + [20.0]
kicks += [21.0 + 0.5 * i for i in range(6)]
kicks += [24.0, 25.0, 25.5]
for k in kicks:
    drums.add(kick(), k, gain=0.95 if k in (2.0, 8.0, 21.0, 24.0) else 0.8)
snares = [3.0, 5.0, 7.0] + [8.5 + i for i in range(8)] + [18.5, 19.5] + [21.5, 22.5, 23.5] + [24.5, 25.5]
for s_ in snares:
    drums.add(clap(), s_, gain=0.42, pan=0.05)
    drums.add(snare(), s_, gain=0.28)
hats = [2.0 + 0.25 * i for i in range(24) if i % 2 == 1]
hats += [8.0 + 0.25 * i for i in range(32) if i % 2 == 1]
hats += [18.0 + 0.25 * i for i in range(6) if i % 2 == 1]
hats += [21.0 + 0.125 * i for i in range(22)]
hats += [24.0 + 0.25 * i for i in range(8) if i % 2 == 1]
for i, h in enumerate(hats):
    drums.add(hat(), h, gain=0.16 + (0.05 if i % 2 else 0), pan=0.35 if i % 2 else -0.25)
for h in (9.75, 11.75, 13.75, 15.75, 19.75):
    drums.add(hat(True), h, gain=0.16, pan=0.3)
for c, g in ((8.0, 0.32), (21.0, 0.3), (23.75, 0.34), (28.0, 0.45)):
    drums.add(pan_sweep(crash(), -0.3, 0.3), c, gain=g)
# snare rolls into the drops
for a, b, g in ((7.5, 8.0, 0.32), (19.5, 20.0, 0.28)):
    tt = a
    while tt < b - 1e-6:
        u = (tt - a) / (b - a)
        drums.add(snare(), tt, gain=g * (0.35 + 0.65 * u))
        tt += 0.125 if u < 0.5 else 0.0625
# the clock in the timing beat: every sixteenth, tick and tock
for i in range(24):
    tt = 16.25 + 0.0625 * i
    drums.add(tick(3400 if i % 2 == 0 else 2700, decay=0.004, noise=0.3), tt, gain=0.12 + (0.06 if i % 4 == 0 else 0), pan=0.3 if i % 2 else -0.3)

# arps: eighths in the editing beats, sixteenths that climb with the export
def chord_at(t):
    cur = CH[0]
    for c in CH:
        if t >= c[0] - 1e-9:
            cur = c
    return cur


i = 0
for t0 in np.arange(9.0, 16.0, 0.25):
    notes = chord_at(t0)[2]
    pat = [1, 2, 3, 2, 4 % len(notes), 2, 3, 1]
    f = hz(notes[pat[i % 8] % len(notes)]) * 2
    music.add(fm(f, 0.45, ratio=2.0, index=1.4, adecay=0.22), t0, gain=0.085, pan=0.45 if i % 2 else -0.45)
    i += 1
for t0 in np.arange(18.0, 20.0, 0.25):
    notes = chord_at(t0)[2]
    f = hz(notes[(i % 3) + 1]) * 2
    music.add(fm(f, 0.4, ratio=2.0, index=1.2, adecay=0.2), t0, gain=0.07, pan=0.4 if i % 2 else -0.4)
    i += 1
for k, t0 in enumerate(np.arange(21.0, 23.75, 0.125)):
    notes = chord_at(t0)[2]
    u = k / 22
    f = hz(notes[1 + k % (len(notes) - 1)]) * 2 * (2 if k % 8 >= 4 and u > 0.5 else 1)
    music.add(fm(f, 0.3, ratio=2.0, index=1.0 + 2.5 * u, idecay=0.05, adecay=0.12 + 0.06 * u), t0, gain=0.06 + 0.05 * u, pan=0.5 if k % 2 else -0.5)

# night: three slow notes
for tt, nm in ((26.1, 'F#5'), (26.6, 'A5'), (27.1, 'D5')):
    bells.add(fm(hz(nm), 1.8, ratio=1.0, index=0.8, idecay=0.3, adecay=0.9), tt, gain=0.16, pan=0.2)

# ---------------------------------------------------------------- sound design
# the shoot: the gate draws, the shutter fires faster and faster
sfx.add(norm(np.sin(TAU * np.cumsum(420 * 2 ** (T(0.5) / 0.5)) / SR) * np.sin(np.pi * T(0.5) / 0.5) ** 2), 0.12, gain=0.05, pan=-0.2)
for c in (0.75, 1.0, 1.125, 1.25, 1.3125, 1.375, 1.4375, 1.5):
    sfx.add(shutter(), c, gain=0.95, pan=0.1)
for f in range(1, 24):
    tt = 1.5 + f / 60
    sfx.add(tick(5200 + 60 * f, decay=0.002, noise=0.8, dur=0.02), tt, gain=0.22 + 0.012 * f, pan=rng.uniform(-0.3, 0.3))
    sfx.add(shutter(), tt, gain=0.25 + 0.01 * f, pan=rng.uniform(-0.2, 0.2))
sfx.add(reverse_swell(0.9), 1.1, gain=0.4)
# 742 photos: the pull-back and the ripple
sfx.add(whoosh(1.1, 3000, 300, peak=0.12, p0=0.0, p1=-0.4), 1.98, gain=0.35)
sfx.add(impact(2.5, f0=70, f1=34, decay=0.7), 2.0, gain=0.55)
for k in range(70):
    tt = 2.03 + 0.57 * (k / 70) ** 1.3 + rng.uniform(0, 0.01)
    sfx.add(tick(rng.uniform(3800, 8200), decay=0.003, noise=0.2, dur=0.02), tt, gain=0.045 * (1 - k / 90), pan=rng.uniform(-0.8, 0.8))
sfx.add(whoosh(0.7, 500, 2500, peak=0.55), 2.35, gain=0.1)
# the swarm, the rewind, the hero
sfx.add(riser(0.95, 300, 5200, tone=98), 4.5, gain=0.24)
sfx.add(whoosh(1.0, 400, 3500, peak=0.7, p0=-0.7, p1=0.7), 4.5, gain=0.25)
sfx.add(rewind(0.47), 5.5, gain=0.26)
sfx.add(impact(2.4, f0=90, f1=38, decay=0.6), 6.0, gain=0.42)
sfx.add(whoosh(0.7, 4000, 600, peak=0.1, p0=0.2, p1=0), 5.98, gain=0.25)
sfx.add(whoosh(0.4, 900, 4000, peak=0.6, p0=-0.4, p1=0.4), 6.95, gain=0.09)
for k in range(4):
    sfx.add(tick(2200 + 300 * k, decay=0.005, noise=0.3), 7.12 + 0.06 * k + 0.35, gain=0.08)
sfx.add(reverse_swell(0.6), 7.4, gain=0.2)
# the window: swing in, drag, drop, the strip files in
sfx.add(whoosh(0.9, 3500, 250, peak=0.15, p0=0.6, p1=-0.2), 7.97, gain=0.38)
sfx.add(impact(2.2, f0=80, f1=36, decay=0.5), 8.0, gain=0.35)
sfx.add(whoosh(0.65, 300, 1500, peak=0.6, p0=-0.8, p1=0.2), 8.28, gain=0.12)
sfx.add(norm(np.sin(TAU * 1180 * T(0.12)) * np.exp(-T(0.12) / 0.03)), 8.62, gain=0.06, pan=0.2)
sfx.add(norm(np.sin(TAU * np.cumsum(120 * np.exp(-T(0.4) / 0.08) + 50) / SR) * np.exp(-T(0.4) / 0.12)), 9.0, gain=0.3)
for k in range(18):
    sfx.add(tick(2000 + 120 * k, decay=0.004, noise=0.25, dur=0.03), 9.27 + 0.018 * k + 0.1, gain=0.05, pan=-0.6 + 1.2 * k / 17)
sfx.add(whoosh(0.8, 300, 2200, peak=0.6, p0=0.3, p1=-0.3), 10.0, gain=0.14)
sfx.add(whoosh(0.7, 2200, 400, peak=0.4, p0=-0.3, p1=0.3), 11.3, gain=0.14)
# speed: the pill clicks into each value
for tt, f in ((12.25, 1500), (12.75, 2400), (13.25, 2100), (13.75, 1800)):
    sfx.add(tick(f, decay=0.008, noise=0.35, dur=0.08), tt, gain=0.12, pan=0.35)
    sfx.add(tick(f / 2, decay=0.012, noise=0.0, dur=0.08), tt + 0.012, gain=0.06, pan=0.35)
sfx.add(whoosh(0.4, 800, 5000, peak=0.5, p0=0.5, p1=-0.5), 13.86, gain=0.18)
for tt in (14.25, 14.75, 15.25, 15.75):
    sfx.add(whoosh(0.4, 1800, 700, peak=0.35, bw=0.6, p0=-0.15, p1=0.15), tt - 0.02, gain=0.09)
    sfx.add(tick(1300, decay=0.01, noise=0.2, dur=0.08), tt + 0.24, gain=0.06)
sfx.add(riser(0.36, 600, 7000, tone=180), 15.86, gain=0.16)
sfx.add(whoosh(0.5, 4000, 900, peak=0.1), 16.18, gain=0.12)
sfx.add(whoosh(0.35, 1200, 3500, peak=0.5), 16.6, gain=0.08)
sfx.add(norm(np.sin(TAU * 1760 * T(0.25)) * np.exp(-T(0.25) / 0.06)), 16.97, gain=0.07, pan=0.0)
sfx.add(whoosh(0.5, 5000, 400, peak=0.25, p0=0.8, p1=-0.4), 17.72, gain=0.3)
# filmstrip: remove, undo, sunset
sfx.add(fm(900 * 2 ** (-T(0.18) / 0.25), 0.18, ratio=1.5, index=1.0, adecay=0.06), 18.5, gain=0.18, pan=-0.1)
sfx.add(fm(520 * 2 ** (T(0.18) / 0.22), 0.18, ratio=1.5, index=1.0, adecay=0.06), 19.0, gain=0.18, pan=-0.1)
sfx.add(whoosh(1.0, 1500, 300, peak=0.5, bw=1.2, p0=0.3, p1=-0.3), 19.2, gain=0.08)
# export: press, dive, the gate steps, one hundred percent
sfx.add(whoosh(0.45, 600, 4500, peak=0.5, p0=-0.4, p1=0.4), 19.86, gain=0.2)
sfx.add(shutter(), 20.5, gain=0.3)
sfx.add(impact(1.6, f0=95, f1=40, decay=0.35), 20.5, gain=0.3)
sfx.add(riser(0.45, 400, 8000, tone=140), 20.57, gain=0.28)
sfx.add(impact(2.4, f0=85, f1=36, decay=0.6), 21.0, gain=0.4)
STEP = [21.0 + 0.125 * k for k in range(16)] + [23.0 + 0.0625 * k for k in range(12)]
for k, st in enumerate(STEP):
    land = st + (0.1 if k < 16 else 0.055)
    sfx.add(shutter(), land, gain=0.1 + 0.004 * k, pan=0.15)
    sfx.add(tick(1800 + 70 * k, decay=0.005, noise=0.2, dur=0.04), land, gain=0.05, pan=-0.2)
sfx.add(impact(2.8, f0=90, f1=34, decay=0.8), 23.75, gain=0.5)
sfx.add(reverse_swell(0.5), 23.25, gain=0.16)
bells.add(bell(hz('D6'), 2.5), 24.2, gain=0.2, pan=0.15)
bells.add(bell(hz('A6'), 2.5), 24.38, gain=0.16, pan=0.3)
sfx.add(whoosh(0.6, 400, 3000, peak=0.85, p0=0.3, p1=0.0), 25.42, gain=0.2)
# night: the lock, then the time slice
sfx.add(norm(np.sin(TAU * 95 * T(0.3)) * np.exp(-T(0.3) / 0.07)), 26.6, gain=0.18)
sfx.add(tick(1500, decay=0.006, noise=0.5, dur=0.06), 26.6, gain=0.12)
for k in range(40):
    tt = 27.2 + k * 0.009 + 0.15
    sfx.add(tick(1500 + 45 * k, decay=0.003, noise=0.6, dur=0.025), tt, gain=0.05 + 0.002 * k, pan=-0.9 + 1.8 * k / 39)
sfx.add(riser(0.8, 300, 9000, tone=110), 27.2, gain=0.26)
sfx.add(reverse_swell(1.0), 27.0, gain=0.3)
# five frames: the final hit, the sheen, the wordmark
sfx.add(impact(4.0, f0=70, f1=28, decay=1.4), 28.0, gain=0.72)
sfx.add(whoosh(0.9, 3000, 200, peak=0.08, p0=0.0, p1=0.0), 27.99, gain=0.3)
for k, nm in enumerate(('D6', 'F#6', 'A6', 'D7', 'E7')):
    bells.add(bell(hz(nm), 2.6, decay=0.8), 28.55 + 0.075 * k, gain=0.09, pan=-0.5 + 0.25 * k)
sfx.add(whoosh(0.6, 800, 3500, peak=0.5, p0=-0.3, p1=0.3), 28.4, gain=0.08)


# ---------------------------------------------------------------- mix
def reverb_ir(rt=2.3, dur=2.8):
    t = T(dur)
    ir = np.zeros((2, len(t)))
    for c in range(2):
        nz = spec(rng.standard_normal(len(t)), lp(5200, 1))
        ir[c] = nz * np.exp(-t * 6.9 / rt)
    pre = int(0.022 * SR)
    ir = np.concatenate([np.zeros((2, pre)), ir[:, :-pre]], axis=1)
    return ir / np.sqrt((ir ** 2).sum(axis=1, keepdims=True))


def convolve(x, ir):
    n = x.shape[1] + ir.shape[1]
    m = 1 << (n - 1).bit_length()
    return np.vstack([np.fft.irfft(np.fft.rfft(x[c], m) * np.fft.rfft(ir[c], m), m)[:x.shape[1]] for c in range(2)])


tt = np.arange(N) / SR
duck = np.ones(N)
for k in kicks:
    m = tt >= k
    duck[m] -= 0.5 * np.exp(-(tt[m] - k) / 0.15)
duck = np.clip(duck, 0.4, 1)
pads.x = np.vstack([spec(pads.x[c], hp(150, 2)) for c in range(2)])
music.x += pads.x
music.x *= duck
# the sunset darkens the bed; the night thins it
send = 0.22 * music.x + 0.12 * drums.x + 0.55 * bells.x + 0.16 * sfx.x
wet = convolve(send, reverb_ir())
mix = 0.9 * drums.x + 0.85 * music.x + 0.9 * bells.x + 0.85 * sfx.x + 0.42 * wet
mix = np.vstack([spec(mix[c], hp(28, 2)) for c in range(2)])
fade = np.ones(N)
fade[tt > 29.25] = np.cos(np.minimum(1, (tt[tt > 29.25] - 29.25) / 0.72) * np.pi / 2) ** 2
fade[tt < 0.01] = tt[tt < 0.01] / 0.01
mix *= fade
body = mix[:, int(2 * SR):int(26 * SR)]
mix *= 10 ** (-12.5 / 20) / np.sqrt((body ** 2).mean())
k = 0.62
mag = np.abs(mix)
over = mag > k
mix[over] = np.sign(mix[over]) * (k + (1 - k) * np.tanh((mag[over] - k) / (1 - k)))
mix *= 0.94 / max(0.94, np.abs(mix).max())
print(f'limited samples: {over.mean() * 100:.3f}%')

for name, b in (('drums', drums), ('music', music), ('bells', bells), ('sfx', sfx)):
    print(f'{name:6s} rms {20 * np.log10(np.sqrt((b.x ** 2).mean()) + 1e-12):6.1f} dB  peak {20 * np.log10(np.abs(b.x).max() + 1e-12):6.1f} dB')
print('master per 2 s:', ' '.join(f'{20 * np.log10(np.sqrt((mix[:, int(a * SR):int((a + 2) * SR)] ** 2).mean()) + 1e-12):.1f}' for a in range(0, 30, 2)))
print(f'master peak {20 * np.log10(np.abs(mix).max()):.2f} dBFS, nan={np.isnan(mix).any()}')

pcm = np.clip(mix + rng.uniform(-0.5, 0.5, mix.shape) / 32768 + rng.uniform(-0.5, 0.5, mix.shape) / 32768, -1, 1)
data = (pcm.T * 32767).astype('<i2').tobytes()
with wave.open(sys.argv[1], 'wb') as w:
    w.setnchannels(2)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes(data)
