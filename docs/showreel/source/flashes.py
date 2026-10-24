"""Flash check: does the reel stay under WCAG 2.3.1's general and red flash thresholds?

    python flashes.py ../10lapse-motion-reel-2026.mp4      # exits 1 if a second fails

The method is Popote's showreel check (sven-borden/popote, docs/showreel/flashes.py), made standalone and streaming.
It reads the film at one sample per 6 x 6 pixels (0.09 degree of visual field at full screen, where WCAG's 10
degrees are 640 px) and follows every spot through time, counting its transitions:

  general  a change of relative luminance of 0.10 or more, the darker side below 0.80
  red      a change of max(0, R - G - B) x 320 of more than 20 (linear RGB), with a saturated red
           (R / (R + G + B) >= 0.8) on one side

A slow ramp is one transition, not many. A flash is a pair of opposing transitions, so more than three flashes in a
second is seven transitions or more. A second fails when the spots that flash that often cover more than 25 % of a
10 degree field (a third of the frame's width by a third of its height, the worst case of full-screen playback).
"""
import collections
import subprocess
import sys

import numpy as np

SW, SH = 320, 180
FIELD = (SW // 3, SH // 3)
AREA = 0.25
TRANSITIONS = 7
FPS = 60
_S = np.arange(256) / 255.0
TO_LINEAR = np.where(_S <= 0.04045, _S / 12.92, ((_S + 0.055) / 1.055) ** 2.4).astype(np.float32)


class Transitions:
    """Per spot: a run in one direction extends to its extreme; a change the other way of at least `threshold`
    (more than, if `strict`), passing the gate on either side, is the next transition."""

    def __init__(self, threshold, strict=False):
        self.threshold, self.strict = threshold, strict
        self.ref = None

    def step(self, v, ok_now):
        if self.ref is None:
            self.ref, self.ref_ok = v.copy(), ok_now.copy()
            self.direction = np.zeros(v.shape, np.int8)
            return np.zeros(v.shape, bool)
        d0 = self.direction
        extend = ((d0 == 1) & (v > self.ref)) | ((d0 == -1) & (v < self.ref))
        self.ref = np.where(extend, v, self.ref)
        self.ref_ok = np.where(extend, ok_now, self.ref_ok)
        d = v - self.ref
        big_up = d > self.threshold if self.strict else d >= self.threshold
        big_down = -d > self.threshold if self.strict else -d >= self.threshold
        allowed = ok_now | self.ref_ok
        up = big_up & (d0 != 1) & allowed
        down = big_down & (d0 != -1) & allowed
        hit = up | down
        self.direction = np.where(up, 1, np.where(down, -1, d0)).astype(np.int8)
        self.ref = np.where(hit, v, self.ref)
        self.ref_ok = np.where(hit, ok_now, self.ref_ok)
        return hit


class Window:
    """Transitions per spot over the last second, and the worst share of a 10 degree field that reaches the count."""

    def __init__(self):
        self.q = collections.deque()
        self.count = np.zeros(SH * SW, np.int16)
        self.runs, self.top, self.top_t, self.peak, self.peak_t = [], 0, 0.0, 0.0, 0.0

    def push(self, hit, f):
        self.q.append(hit)
        self.count += hit
        if len(self.q) > FPS:
            self.count -= self.q.popleft()
        if len(self.q) < FPS:
            return
        t = (f - FPS + 1) / FPS
        c = self.count.reshape(SH, SW)
        if c.max() > self.top:
            self.top, self.top_t = int(c.max()), t
        mask = (c >= TRANSITIONS).astype(np.int32)
        if not mask.any():
            return
        fw, fh = FIELD
        sat = np.pad(mask.cumsum(0).cumsum(1), ((1, 0), (1, 0)))
        box = sat[fh:, fw:] - sat[:-fh, fw:] - sat[fh:, :-fw] + sat[:-fh, :-fw]
        share = box.max() / (fw * fh)
        if share > self.peak:
            self.peak, self.peak_t = share, t
        if share > AREA:
            if self.runs and t - self.runs[-1][1] <= 1.5 / FPS:
                self.runs[-1][1] = t
                self.runs[-1][2] = max(self.runs[-1][2], share)
            else:
                self.runs.append([t, t, share])


def main():
    cmd = ['ffmpeg', '-v', 'error', '-i', sys.argv[1], '-vf',
           f'scale={SW}:{SH}:flags=area:in_range=tv:in_color_matrix=bt709,format=rgb24', '-r', str(FPS), '-f', 'rawvideo', '-']
    p = subprocess.Popen(cmd, stdout=subprocess.PIPE)
    gen, red = Transitions(0.10), Transitions(20.0, strict=True)
    wg, wr = Window(), Window()
    size, f = SW * SH * 3, 0
    while True:
        buf = p.stdout.read(size)
        if not buf or len(buf) < size:
            break
        lin = TO_LINEAR[np.frombuffer(buf, np.uint8).reshape(-1, 3)]
        r, g, b = lin[:, 0], lin[:, 1], lin[:, 2]
        lum = 0.2126 * r + 0.7152 * g + 0.0722 * b
        wg.push(gen.step(lum, lum < 0.80), f)
        wr.push(red.step(np.maximum(0.0, r - g - b) * 320.0, r / (r + g + b + 1e-9) >= 0.8), f)
        f += 1
    p.wait()
    print(f'{f} frames at {FPS} fps, sampled at {SW} x {SH}')
    failed = False
    for name, w in (('general flashes', wg), ('red flashes', wr)):
        print(f'{name}: at most {w.top} transitions of one spot in a second (from {w.top_t:.2f} s); the most of a 10 degree '
              f'field flashing that often is {100 * w.peak:.0f} % (the second from {w.peak_t:.2f} s), limit 25 %')
        for a, z, share in w.runs:
            print(f'  over: seconds starting {a:.2f} to {z:.2f} s, up to {100 * share:.0f} % of a 10 degree field')
        failed |= bool(w.runs)
    print('Some seconds flash over the threshold.' if failed else 'Every second is under the flash thresholds.')
    return 1 if failed else 0


if __name__ == '__main__':
    sys.exit(main())
