"""The 10Lapse motion reel's soundtrack, synthesized from scratch: 59 s, 120 BPM, 48 kHz stereo.

    python music.py music.wav [wave.js]

Every cue sits on reel.html's timeline: a one-second pickup, then a bar every 2 s from 1 s (the downbeats fall on the
odd seconds, like the cuts). wave.js gets the soundtrack's loudness, 100 values a second, for the waveform the reel
draws in its Music beat. The instruments are the Store trailer's (docs/store/showreel/source/music.py), with a few
new ones; the score is new.
"""
import sys
import wave

import numpy as np

SR = 48000
DUR = 59.0
N = int(SR * DUR)
TAU = 2 * np.pi
rng = np.random.default_rng(1440)


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


# ---------------------------------------------------------------- instruments (the Store trailer's)
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
    s = norm(spec(rng.standard_normal(len(t)), hp(3800, 2))) * np.exp(-t / 0.7)
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


# ---------------------------------------------------------------- instruments (new)
def pluck(f, dur=0.6, bright=0.5):
    """A soft mallet: FM with a fast-closing index, a little body."""
    s = fm(f, dur, ratio=3.0, index=1.4 + 2.2 * bright, idecay=0.03 + 0.03 * bright, adecay=0.22 + 0.2 * bright)
    s += 0.35 * fm(f * 2, dur, ratio=1.0, index=0.6, idecay=0.02, adecay=0.12)
    return norm(s)


def keys(f, dur=1.6):
    """Electric-piano-like tine for the calm beats."""
    t = T(dur)
    s = np.sin(TAU * f * t + 1.2 * np.exp(-t / 0.25) * np.sin(TAU * f * 14 * t) * 0.15 + 0.9 * np.exp(-t / 0.6) * np.sin(TAU * f * t))
    s *= np.exp(-t / 0.9) * np.minimum(1, t / 0.004)
    return norm(s)


def flip():
    """A photo turning over: a short airy fwip and a paper tick."""
    t = T(0.12)
    f = 900 * (3200 / 900) ** (t / 0.12)
    nz = shaped_noise(0.12, lambda tc: 900 * (3200 / 900) ** (tc / 0.12), lambda tc: 0.5)
    env = np.sin(np.pi * np.minimum(1, t / 0.12)) ** 1.5
    s = norm(nz) * env + 0.5 * np.where(t > 0.055, rng.standard_normal(len(t)) * np.exp(-(t - 0.055) / 0.002), 0)
    return norm(spec(s, hp(500)))


def ui_click(f=1800, dur=0.05):
    t = T(dur)
    s = np.sin(TAU * f * t) * np.exp(-t / 0.004) + 0.4 * spec(rng.standard_normal(len(t)), hp(2500)) * np.exp(-t / 0.0015)
    return norm(s)


def switch():
    """The toggle: two little clicks a knob-travel apart."""
    a = ui_click(2100, 0.06)
    b = ui_click(1500, 0.06)
    out = np.zeros(int(0.2 * SR))
    out[:len(a)] += a
    k = int(0.09 * SR)
    out[k:k + len(b)] += 0.8 * b
    return out


def thunk():
    t = T(0.35)
    f = 60 + 90 * np.exp(-t / 0.03)
    s = np.sin(TAU * np.cumsum(f) / SR) * np.exp(-t / 0.09)
    s += 0.3 * spec(rng.standard_normal(len(t)), bp(900, 0.8)) * np.exp(-t / 0.01)
    return norm(s)


def lock_click():
    t = T(0.18)
    s = spec(rng.standard_normal(len(t)), bp(3800, 2.0)) * np.exp(-t / 0.004)
    m = t > 0.035
    s[m] += 0.7 * spec(rng.standard_normal(m.sum()), bp(2400, 2.5)) * np.exp(-(t[m] - 0.035) / 0.006)
    s += 0.35 * np.sin(TAU * 1250 * t) * np.exp(-t / 0.02)
    return norm(s)


def glide(f0, f1, dur, att=0.05):
    t = T(dur)
    f = f0 * (f1 / f0) ** (t / dur)
    ph = TAU * np.cumsum(f) / SR
    s = np.sin(ph) + 0.25 * np.sin(2 * ph)
    env = np.minimum(1, t / att) * np.sin(np.pi * np.minimum(1, t / dur)) ** 0.6
    return norm(s * env)


def snap(f):
    """A shape change: an aperture-like snap with a short pitch."""
    t = T(0.16)
    s = spec(rng.standard_normal(len(t)), bp(5200, 1.2)) * np.exp(-t / 0.003)
    s += 0.6 * np.sin(TAU * f * t) * np.exp(-t / 0.035)
    return norm(s)


def shimmer(dur, f0=2000, f1=8000):
    sig = shaped_noise(dur, lambda tc: f0 * (f1 / f0) ** (tc / dur), lambda tc: 0.35)
    u = T(dur) / dur
    return pan_sweep(norm(sig) * np.sin(np.pi * u) ** 1.2, -0.5, 0.5)


# ---------------------------------------------------------------- the score
drums, bass, pads, keysb, sfx, bells = Bus(), Bus(), Bus(), Bus(), Bus(), Bus()
kicks = []


def bar(n):
    """Downbeat of bar n (1-based): bars start on the odd seconds."""
    return 2 * n - 1.0


# chords: (start, end, voicing, root, brightness)
CHORDS = [
    (0.0, 3.0, ['D3', 'A3', 'C#4', 'E4', 'F#4'], 'D2', 1600),
    (3.0, 5.0, ['B2', 'F#3', 'A3', 'D4', 'E4'], 'B1', 1800),
    (5.0, 6.0, ['G2', 'D3', 'F#3', 'A3', 'B3'], 'G1', 2100),
    (6.0, 7.0, ['A2', 'D3', 'E3', 'A3', 'D4'], 'A1', 2500),
    (7.0, 9.0, ['D3', 'A3', 'C#4', 'E4', 'F#4'], 'D2', 2600),
    (9.0, 11.0, ['B2', 'F#3', 'A3', 'C#4', 'D4'], 'B1', 2400),
    (11.0, 12.0, ['G2', 'D3', 'F#3', 'A3', 'B3'], 'G1', 2300),
    (12.0, 13.0, ['A2', 'E3', 'F#3', 'B3', 'C#4'], 'A1', 2300),
    (13.0, 15.0, ['F#2', 'D3', 'A3', 'C#4', 'E4'], 'F#1', 2200),
    (15.0, 16.0, ['G2', 'D3', 'F#3', 'A3', 'B3'], 'G1', 2200),
    (16.0, 17.0, ['A2', 'D3', 'E3', 'A3', 'C#4'], 'A1', 2400),
    (17.0, 19.0, ['B2', 'F#3', 'A3', 'C#4', 'D4'], 'B1', 2300),
    (19.0, 21.0, ['G2', 'D3', 'F#3', 'A3', 'B3'], 'G1', 2100),
    (21.0, 22.0, ['A2', 'D3', 'F#3', 'A3', 'C#4'], 'D2', 2000),
    (22.0, 23.0, ['A2', 'D3', 'E3', 'G3', 'C#4'], 'A1', 1900),
    (23.0, 25.0, ['E2', 'B2', 'D3', 'F#3', 'G3'], 'E1', 1800),
    (25.0, 27.0, ['G2', 'D3', 'F#3', 'A3', 'B3'], 'G1', 1700),
    (27.0, 28.0, ['B2', 'F#3', 'A3', 'C#4', 'D4'], 'B1', 1600),
    (28.0, 29.0, ['A2', 'E3', 'A3', 'C#4', 'E4'], 'A1', 1600),
    (29.0, 31.0, ['B2', 'F#3', 'A3', 'D4', 'E4'], 'B1', 1300),
    (31.0, 33.0, ['G2', 'D3', 'F#3', 'A3', 'C#4'], 'G1', 1500),
    (33.0, 34.0, ['E2', 'B2', 'D3', 'F#3', 'G3'], 'E1', 1400),
    (34.0, 35.0, ['F#2', 'C#3', 'E3', 'A3', 'C#4'], 'F#1', 1400),
    (35.0, 37.0, ['G2', 'D3', 'F#3', 'A3', 'B3'], 'G1', 1500),
    (37.0, 39.0, ['F#2', 'D3', 'A3', 'C#4', 'E4'], 'F#1', 1600),
    (39.0, 40.0, ['B2', 'F#3', 'A3', 'C#4', 'D4'], 'B1', 1900),
    (40.0, 41.0, ['A2', 'E3', 'A3', 'C#4', 'E4'], 'A1', 2000),
    (41.0, 43.0, ['G2', 'D3', 'F#3', 'A3', 'B3'], 'G1', 1900),
    (43.0, 44.0, ['A2', 'D3', 'E3', 'A3', 'D4'], 'A1', 2300),
    (44.0, 45.0, ['A2', 'E3', 'G3', 'C#4', 'E4'], 'A1', 2700),
    (45.0, 47.0, ['D3', 'A3', 'C#4', 'E4', 'F#4'], 'D2', 2800),
    (47.0, 48.0, ['B2', 'F#3', 'A3', 'C#4', 'D4'], 'B1', 2500),
    (48.0, 49.0, ['G2', 'D3', 'F#3', 'A3', 'B3'], 'G1', 2300),
    (49.0, 51.0, ['E2', 'B2', 'D3', 'F#3', 'G3'], 'E1', 1200),
    (51.0, 52.0, ['G2', 'D3', 'F#3', 'B3', 'D4'], 'G1', 1200),
    (52.0, 53.0, ['A2', 'D3', 'E3', 'A3', 'C#4'], 'A1', 1300),
    (53.0, 59.0, ['D3', 'A3', 'C#4', 'E4', 'F#4', 'A4'], 'D2', 1700),
]


def chord_at(t):
    for c in CHORDS:
        if c[0] <= t < c[1]:
            return c
    return CHORDS[-1]


# pads: one sustained voicing per chord, with an intro swell
for (a, b, voice, root, br) in CHORDS:
    att = 0.9 if a == 0.0 else (0.25 if a in (7.0, 45.0, 53.0) else 0.12)
    rel = 2.6 if b >= 59 else 0.5
    for nm in voice:
        pads.add(pad_note(hz(nm), b - a, att=att, rel=rel, bright=br, voices=3, cents=8), a, gain=0.09)
# the night pad: a high, airy layer over the blended night and the end card
for (a, b) in ((31.0, 35.2), (53.4, 59.0)):
    for nm in ('F#5', 'A5', 'C#6') if a < 40 else ('A5', 'C#6', 'E6'):
        pads.add(pad_note(hz(nm), b - a, att=1.2, rel=1.8, bright=5000, voices=2, cents=14, spread=1.0), a, gain=0.02)

# drums
GROOVE = {  # section start, end, pattern
    'build': (3.0, 7.0), 'title': (7.0, 13.0), 'import': (13.0, 17.0), 'defl': (17.0, 23.0), 'crop': (23.0, 29.0),
    'blend': (29.0, 35.0), 'music': (39.0, 41.0), 'export': (41.0, 45.0), 'prem': (45.0, 49.0),
}
K, CL, HC, HO, SN = kick(f_hi=170, f_lo=54, decay=0.17, click=0.5), clap(), hat(), hat(True), snare()


def k_at(t, g=1.0):
    drums.add(K, t, gain=0.68 * g)
    kicks.append(t)


for name, (a, b) in GROOVE.items():
    t = a
    while t < b - 1e-6:
        beat = round((t - 1.0) / 0.5) % 4      # 0..3 within the bar
        if name == 'build':
            u = (t - a) / (b - a)
            k_at(t, 0.55 + 0.45 * u)
            if t >= 4.0:
                drums.add(HC, t + 0.25, gain=0.12 + 0.12 * u, pan=0.25)
            if t >= 5.0:
                drums.add(HO, t + 0.25, gain=0.06 + 0.06 * u, pan=-0.2)
        elif name in ('title', 'defl', 'crop', 'prem', 'export', 'music'):
            if beat in (0, 2) or name in ('export',):
                k_at(t, 1.0 if beat in (0, 2) else 0.8)
            if beat in (1, 3):
                drums.add(CL, t, gain=0.32, pan=0.05)
            drums.add(HC, t + 0.25, gain=0.16, pan=0.3)
            if name in ('prem', 'export'):
                drums.add(HC, t, gain=0.07, pan=-0.3)
                drums.add(HC, t + 0.125, gain=0.05, pan=0.4)
                drums.add(HC, t + 0.375, gain=0.05, pan=-0.4)
        elif name == 'import':
            if beat == 0:
                k_at(t, 0.85)
            if beat == 2:
                drums.add(CL, t, gain=0.24)
            drums.add(HC, t + 0.25, gain=0.12, pan=0.3)
        elif name == 'blend':
            if beat == 0:
                k_at(t, 0.9)
            if beat == 2:
                drums.add(CL, t, gain=0.3)
            for q in (0.125, 0.375):
                drums.add(HC, t + q, gain=0.05, pan=0.35 if q < 0.3 else -0.35)
        t += 0.5
# the deflicker beat keeps its groove; the hold beat stops the drums; the boomerang has only its motif
# snare rolls into the drops at 7 and 45
for (a, b) in ((6.0, 7.0), (44.0, 45.0)):
    t = a
    while t < b - 1e-6:
        u = (t - a) / (b - a)
        drums.add(SN, t, gain=0.08 + 0.22 * u ** 1.5, pan=0.0)
        t += 0.125 if u < 0.5 else 0.0625
# crashes
for t, g in ((7.0, 0.24), (17.0, 0.15), (39.0, 0.14), (45.0, 0.26), (53.0, 0.2)):
    drums.add(crash(3.2), t, gain=g, pan=0.15)

# bass: eighths on the root in the grooves, long notes elsewhere
for (a, b, voice, root, br) in CHORDS:
    f = hz(root)
    if f < 54:
        f *= 2
    if any(ga <= a < gb for (ga, gb) in GROOVE.values()) and not (35.0 <= a < 39.0):
        t = a
        while t < b - 1e-6:
            on = round((t - a) / 0.25) % 2 == 0
            if on:
                bass.add(bass_note(f, 0.2, dec=0.12, sus=0.5, drive=2.2), t, gain=0.14)
            t += 0.25
    else:
        bass.add(bass_note(f, b - a - 0.05, att=0.03, dec=0.6, sus=0.75, rel=0.3, drive=1.8), a, gain=0.13)

# keys: the arpeggio, eighths, sixteenths where it builds; chord tones an octave up
ARP_SECTIONS = [(3.0, 7.0, 0.125), (7.0, 13.0, 0.25), (13.0, 17.0, 0.25), (17.0, 23.0, 0.25), (23.0, 29.0, 0.25),
                (39.0, 41.0, 0.25), (41.0, 45.0, 0.125), (45.0, 49.0, 0.125)]
for (a, b, step) in ARP_SECTIONS:
    t = a
    i = 0
    while t < b - 1e-6:
        c = chord_at(t)
        notes = c[2][1:]
        nm = notes[[0, 1, 2, 3, 2, 1][i % 6] % len(notes)]
        f = hz(nm) * (2 if step == 0.25 else 4 if a in (41.0, 45.0) else 2)
        u = (t - a) / (b - a)
        g = 0.05 if step == 0.25 else 0.032 + (0.03 * u if a in (3.0, 41.0) else 0.0)
        keysb.add(pluck(f, 0.5, bright=0.35 + (0.4 * u if a in (3.0, 41.0) else 0.2)), t, gain=g, pan=0.35 * np.sin(i * 0.9))
        t += step
        i += 1

# the calm beats get slow keys
for t, nm in ((49.0, 'F#5'), (49.5, 'B4'), (50.5, 'D5'), (51.0, 'A4'), (51.5, 'F#5'), (52.0, 'E5'), (52.5, 'C#5')):
    keysb.add(keys(hz(nm), 1.8), t, gain=0.09, pan=-0.2 + 0.1 * (t - 49))

# ---------------------------------------------------------------- sound design, on the picture
SH = shutter()
# the shoot: the shutter, on the frames the reel shows
for t in (0.5, 1.0, 1.5, 2.0, 2.25, 2.5, 2.75):
    sfx.add(SH, t, gain=0.32, pan=0.1)
sfx.add(SH, 58.3, gain=0.14, pan=0.1)     # the last photo of the evening
# photo 1 shrinks into the contact sheet, then the sheet fills
sfx.add(whoosh(0.75, 2600, 700, peak=0.35, p0=0.2, p1=-0.2), 3.0, gain=0.18)
sfx.add(riser(2.5, 300, 7000, tone=98), 3.75, gain=0.13)


# the contact sheet fills: a click for each photo while they can be counted, then a tick for each finished row
# (the same fill as scenes.js: 1,440 photos from 3.75 s to 6.25 s)
def travel(acc, dec, n=4000):
    u = (np.arange(n + 1) + 0.5) / n
    v = np.minimum(np.clip(u / acc, 0, 1), np.clip((1 - u) / dec, 0, 1))
    v = v * v * (3 - 2 * v)
    P = np.concatenate([[0], np.cumsum(v[:-1])])
    return P / P[-1]


P = travel(0.1, 0.25)
tt = np.linspace(3.75, 6.25, len(P))
land = np.interp(np.arange(1440) + 1, 1 + 1439 * P, tt)
tk = norm(spec(rng.standard_normal(int(0.012 * SR)), bp(2200, 0.9)) * np.exp(-T(0.012) / 0.0018))
prev = -1.0
for i in range(1, 1440):
    if land[i] - prev < 0.045:
        break
    sfx.add(tk, land[i], gain=0.07, pan=-0.5 + (i % 48) / 48)
    prev = land[i]
for r in range(30):
    tr = land[min(1439, 48 * (r + 1) - 1)]
    sfx.add(tk, tr, gain=0.06 + 0.05 * r / 29, pan=0.6 * np.sin(r * 2.39))
    keysb.add(pluck(hz(('D6', 'E6', 'F#6', 'A6', 'B6')[r % 5]), 0.25, bright=0.2), tr, gain=0.012, pan=0.6 * np.sin(r * 2.39))
sfx.add(whoosh(1.0, 400, 1600, peak=0.2, p0=-0.1, p1=0.1), 6.0, gain=0.1)
sfx.add(reverse_swell(1.0), 6.0, gain=0.16)
# the drop: five frames
sfx.add(impact(3.5), 7.0, gain=0.42)
for k, t in enumerate((7.0, 7.125, 7.25, 7.375)):
    sfx.add(flip(), t, gain=0.22, pan=-0.45 + 0.3 * k)
sfx.add(shimmer(0.8, 1500, 7000), 7.95, gain=0.07)
for k, nm in enumerate(('D6', 'F#6', 'A6', 'E7')):
    bells.add(bell(hz(nm), 2.4, decay=0.7), 8.6 + 0.05 * k, gain=0.045, pan=-0.3 + 0.2 * k)
# the wordmark, letter by letter (and again on the end card)
WORD = ('D5', 'E5', 'F#5', 'A5', 'B5', 'D6', 'E6')
for base in (9.2, 54.1):
    for k, nm in enumerate(WORD):
        keysb.add(pluck(hz(nm), 0.7, bright=0.7), base + 0.04 * k + 0.12, gain=0.05, pan=-0.3 + 0.1 * k)
# the icon flies into the title bar; the window opens
sfx.add(whoosh(0.65, 3000, 900, peak=0.4, p0=0.3, p1=-0.6), 13.0, gain=0.12)
sfx.add(shimmer(0.6, 900, 3000), 13.25, gain=0.05)
# the drag, the drop, the photos landing
sfx.add(whoosh(0.65, 500, 1400, peak=0.7, p0=0.6, p1=-0.2), 13.75, gain=0.08)
sfx.add(ui_click(1400), 14.15, gain=0.08)
sfx.add(thunk(), 14.5, gain=0.3)
sfx.add(shimmer(0.7, 1200, 6000), 14.55, gain=0.06)
LAND = ['D5', 'E5', 'F#5', 'A5', 'B5', 'D6', 'E6', 'F#6', 'A6']
for i in range(18):
    keysb.add(pluck(hz(LAND[i % 9]) * (1 if i < 9 else 2), 0.35, bright=0.3), 14.6 + 0.05 * i + 0.05, gain=0.022, pan=-0.6 + 1.2 * i / 17)
sfx.add(shimmer(0.4, 1500, 5000), 14.97, gain=0.05)
bells.add(bell(hz('A6'), 1.8, decay=0.5), 15.05, gain=0.06, pan=0.3)
# the whip into the sunset
sfx.add(whoosh(0.55, 700, 4200, peak=0.45, bw=1.0, p0=-0.9, p1=0.9), 16.92, gain=0.3)
# deflicker: the flicker you see is a pumping pad you hear, until the switch
sfx.add(switch(), 18.92, gain=0.16)
sfx.add(glide(hz('A5'), hz('D6'), 0.7), 19.05, gain=0.03)
# crop and pan
sfx.add(whoosh(0.8, 2400, 800, peak=0.3, p0=0.0, p1=0.3), 23.0, gain=0.12)
sfx.add(ui_click(1900), 23.6, gain=0.07)
sfx.add(glide(hz('E5'), hz('B5'), 0.45), 24.4, gain=0.03)
for t in (24.45, 25.05):
    sfx.add(ui_click(2300), t, gain=0.07)
sfx.add(whoosh(0.5, 900, 3000, peak=0.6, p0=-0.2, p1=0.2), 25.0, gain=0.12)
sfx.add(whoosh(1.55, 300, 2400, peak=0.85, bw=0.7, p0=-0.3, p1=0.4), 25.45, gain=0.1)
sfx.add(whoosh(0.4, 2400, 900, peak=0.3, p0=0.3, p1=0.0), 27.0, gain=0.08)
for k, t in enumerate((27.5, 27.875, 28.25, 28.625, 29.0)):
    sfx.add(snap(hz(('A5', 'B5', 'D6', 'E6', 'F#6')[k])), t - 0.02, gain=0.12, pan=-0.2 + 0.1 * k)
# frame blending: night falls; sixteen photos fan out and fold into one
sfx.add(whoosh(0.8, 2200, 300, peak=0.4, p0=0.2, p1=-0.2), 29.4, gain=0.14)
for k in range(16):
    keysb.add(pluck(hz(('D5', 'E5', 'F#5', 'A5', 'B5', 'D6', 'E6', 'F#6')[k % 8]) * (1 if k < 8 else 2), 0.4, bright=0.4), 30.15 + 0.024 * k, gain=0.022, pan=-0.7 + 1.4 * k / 15)
sfx.add(reverse_swell(0.42), 30.58, gain=0.14)
sfx.add(impact(2.5, f0=70, f1=34, decay=0.8), 31.0, gain=0.2)
for k, nm in enumerate(('B5', 'D6', 'F#6', 'A6', 'C#7')):
    bells.add(bell(hz(nm), 3.6, decay=1.5), 31.0 + 0.06 * k, gain=0.04, pan=-0.4 + 0.2 * k)
sfx.add(switch(), 31.22, gain=0.12)
# hold, boomerang, music
sfx.add(whoosh(0.7, 2600, 600, peak=0.3, p0=0.0, p1=-0.4), 35.0, gain=0.12)
for i in range(12):
    keysb.add(pluck(hz(LAND[8 - i % 9]), 0.3, bright=0.3), 35.05 + 0.04 * i, gain=0.018, pan=-0.7 + 0.12 * i)
sfx.add(ui_click(2000), 35.75, gain=0.06)
# hold: one long note that holds on
pads.add(pad_note(hz('D5'), 1.1, att=0.08, rel=0.4, bright=3500, voices=2, cents=6), 35.8, gain=0.06)
keysb.add(keys(hz('D6'), 1.4), 35.8, gain=0.08)
# boomerang: a figure out, then the same figure played backwards
fig = np.zeros(int(0.5 * SR))
for k, nm in enumerate(('D5', 'F#5', 'A5', 'D6')):
    p_ = pluck(hz(nm), 0.4, bright=0.6)
    i0 = int(k * 0.125 * SR)
    j0 = min(len(fig), i0 + len(p_))
    fig[i0:j0] += p_[:j0 - i0]
fig = norm(fig)
for t in (37.0, 38.0):
    keysb.add(fig, t, gain=0.08, pan=-0.25)
    keysb.add(fig[::-1], t + 0.5, gain=0.08, pan=0.25)
sfx.add(ui_click(2300), 37.05, gain=0.07)
sfx.add(ui_click(2300), 38.9, gain=0.06)
# the waveform appears as the music comes in
sfx.add(shimmer(0.75, 800, 5000), 39.15, gain=0.05)
# export: the gate, the percent, the four lines, then the file grows into the premiere
sfx.add(whoosh(0.45, 900, 3400, peak=0.7, p0=0.5, p1=-0.8), 40.95, gain=0.16)
sfx.add(riser(2.9, 250, 6000, tone=110), 41.35, gain=0.11)
for k in range(4):
    sfx.add(ui_click(1700 + 150 * k), 41.55 + 0.5 * k, gain=0.06, pan=0.4)
for k, nm in enumerate(('A5', 'D6')):
    bells.add(bell(hz(nm), 1.6, decay=0.5), 44.2 + 0.08 * k, gain=0.06, pan=0.2)
sfx.add(whoosh(0.7, 800, 5000, peak=0.3, p0=0.3, p1=0.0), 44.95, gain=0.18)
sfx.add(reverse_swell(0.55), 44.45, gain=0.16)
sfx.add(impact(3.5), 45.0, gain=0.4)
for k, nm in enumerate(('A5', 'D6', 'F#6')):
    bells.add(bell(hz(nm), 2.2, decay=0.7), 46.05 + 0.07 * k, gain=0.06, pan=-0.2 + 0.2 * k)
# privacy: a low swell, the lock drawn, the shackle closes
sfx.add(impact(4.0, f0=55, f1=28, decay=1.6), 49.0, gain=0.22)
sfx.add(glide(hz('D4'), hz('A4'), 0.6), 49.35, gain=0.025)
sfx.add(lock_click(), 50.15, gain=0.2, pan=0.0)
# the end card: five bars rise, one note each; they gather into the icon
for k, nm in enumerate(('D5', 'F#5', 'A5', 'B5', 'D6')):
    bells.add(bell(hz(nm), 3.0, decay=1.0), 52.98 + 0.05 * k, gain=0.07, pan=-0.6 + 0.3 * k)
sfx.add(impact(4.5, f0=62, f1=28, decay=1.8), 53.0, gain=0.3)
sfx.add(whoosh(0.6, 5000, 900, peak=0.85, p0=-0.3, p1=0.0), 53.45, gain=0.12)
sfx.add(thunk(), 54.05, gain=0.12)
sfx.add(shimmer(1.0, 1500, 6000), 54.4, gain=0.04)


# ---------------------------------------------------------------- mix
def reverb_ir(rt=2.4, dur=3.0):
    t = T(dur)
    ir = np.zeros((2, len(t)))
    for c in range(2):
        nz = spec(rng.standard_normal(len(t)), lp(5200, 1))
        ir[c] = nz * np.exp(-t * 6.9 / rt)
    pre = int(0.024 * SR)
    ir = np.concatenate([np.zeros((2, pre)), ir[:, :-pre]], axis=1)
    return ir / np.sqrt((ir ** 2).sum(axis=1, keepdims=True))


def convolve(x, ir):
    n = x.shape[1] + ir.shape[1]
    m = 1 << (n - 1).bit_length()
    return np.vstack([np.fft.irfft(np.fft.rfft(x[c], m) * np.fft.rfft(ir[c], m), m)[:x.shape[1]] for c in range(2)])


tt = np.arange(N) / SR
# sidechain: everything melodic ducks under the kick
duck = np.ones(N)
for k in kicks:
    m = (tt >= k) & (tt < k + 0.6)
    duck[m] -= 0.42 * np.exp(-(tt[m] - k) / 0.13)
duck = np.clip(duck, 0.45, 1)
# the flicker: the pad pumps on every beat until deflicker is switched on at 19
pump = np.ones(N)
m = (tt >= 17.5) & (tt < 19.0)
pump[m] = 1 - 0.62 * ((tt[m] - 17.5) * 2 % 1)
m = (tt >= 19.0) & (tt < 19.25)
pump[m] = 0.69 + 0.31 * (tt[m] - 19.0) / 0.25
# the hold beat: the drums stop while the hold holds, and nothing plays in the gap before Music
gate = np.ones(N)
gate[(tt >= 35.75) & (tt < 39.0)] = 0.0
drums.x *= gate
bass.x *= np.where((tt >= 38.85) & (tt < 39.0), 0.0, 1.0)
pads.x = np.vstack([spec(pads.x[c], hp(140, 2)) for c in range(2)])
pads.x *= pump
music = pads.x + keysb.x + bass.x
music *= duck
gap = np.where((tt >= 38.87) & (tt < 39.0), 0.15, 1.0)
music *= gap
send = 0.24 * pads.x + 0.3 * keysb.x + 0.1 * drums.x + 0.6 * bells.x + 0.22 * sfx.x
wet = convolve(send, reverb_ir())
# longer air on the blended night (a long exposure, heard)
night = np.where((tt >= 31.0) & (tt < 35.0), 1.0, 0.0)
wet2 = convolve(send * night, reverb_ir(rt=4.5, dur=5.0))
mix = 0.9 * drums.x + 0.85 * music + 0.9 * bells.x + 0.85 * sfx.x + 0.42 * wet + 0.3 * wet2
mix = np.vstack([spec(mix[c], lambda f: hp(28, 2)(f) * (1 - 0.42 / (1 + (f / 85) ** 4))) for c in range(2)])
fade = np.ones(N)
fade[tt > 57.3] = np.cos(np.minimum(1, (tt[tt > 57.3] - 57.3) / 1.7) * np.pi / 2) ** 2
fade[tt < 0.01] = tt[tt < 0.01] / 0.01
mix *= fade
body = mix[:, int(3 * SR):int(49 * SR)]
mix *= 10 ** (-13.0 / 20) / np.sqrt((body ** 2).mean())
k = 0.62
mag = np.abs(mix)
over = mag > k
mix[over] = np.sign(mix[over]) * (k + (1 - k) * np.tanh((mag[over] - k) / (1 - k)))
mix *= 0.89 / max(0.89, np.abs(mix).max())


# true peak: look between the samples at 4x and keep it at -1 dBTP, so the AAC encode does not clip
def true_peak(x):
    n = x.shape[1]
    X = np.fft.rfft(x, axis=1)
    Y = np.zeros((x.shape[0], 2 * n + 1), complex)
    Y[:, :X.shape[1]] = X
    return np.abs(np.fft.irfft(Y, 4 * n, axis=1) * 4).max()


tp = true_peak(mix)
mix *= min(1.0, 10 ** (-1.0 / 20) / tp)
print(f'limited samples: {over.mean() * 100:.3f}%, true peak before {20 * np.log10(tp):.2f} dBTP, after {20 * np.log10(true_peak(mix)):.2f} dBTP')
for name, b in (('drums', drums), ('bass', bass), ('pads', pads), ('keys', keysb), ('bells', bells), ('sfx', sfx)):
    print(f'{name:6s} rms {20 * np.log10(np.sqrt((b.x ** 2).mean()) + 1e-12):6.1f} dB  peak {20 * np.log10(np.abs(b.x).max() + 1e-12):6.1f} dB')
print('master per 2 s:', ' '.join(f'{20 * np.log10(np.sqrt((mix[:, int(a * SR):int((a + 2) * SR)] ** 2).mean()) + 1e-12):.1f}' for a in range(0, 58, 2)))
print(f'master peak {20 * np.log10(np.abs(mix).max()):.2f} dBFS, nan={np.isnan(mix).any()}')

pcm = np.clip(mix + rng.uniform(-0.5, 0.5, mix.shape) / 32768 + rng.uniform(-0.5, 0.5, mix.shape) / 32768, -1, 1)
data = (pcm.T * 32767).astype('<i2').tobytes()
with wave.open(sys.argv[1], 'wb') as w:
    w.setnchannels(2)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes(data)

# the waveform for the reel: loudness every 10 ms, 0 to 1
if len(sys.argv) > 2:
    mono = np.abs(mix).mean(axis=0)
    hop = SR // 100
    n = len(mono) // hop
    rms = np.sqrt((mono[:n * hop].reshape(n, hop) ** 2).mean(axis=1))
    v = np.sqrt(rms / rms.max())
    with open(sys.argv[2], 'w', encoding='utf-8') as f:
        f.write('// Written by music.py: the soundtrack\'s loudness, 100 values a second, for the Music beat.\n')
        f.write('window.WAVE = { rate: 100, peaks: [' + ','.join(f'{x:.2f}'.rstrip('0').rstrip('.') if x > 0 else '0' for x in v) + '] };\n')
