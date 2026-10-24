"""Reads render.mjs video frames from stdin, averages each frame's shutter samples in linear light (motion blur, so
bright things that move stay bright), adds a fine luma grain that also dithers the gradients, and pipes the result to
ffmpeg as a near-lossless intermediate.

    node render.mjs video 60 [from] [to] | python accum.py out.mp4 60 [grain_sigma] [first_frame]
"""
import struct
import subprocess
import sys
import time
from io import BytesIO

import numpy as np
from PIL import Image

W, H = 1920, 1080
out_path, fps = sys.argv[1], int(sys.argv[2])
sigma = float(sys.argv[3]) if len(sys.argv) > 3 else 1.3
first = int(sys.argv[4]) if len(sys.argv) > 4 else 0

_s = np.arange(256) / 255.0
TO_LIN = np.where(_s <= 0.04045, _s / 12.92, ((_s + 0.055) / 1.055) ** 2.4).astype(np.float32)
_l = np.linspace(0.0, 1.0, 16385)
TO_SRGB = (np.where(_l <= 0.0031308, _l * 12.92, 1.055 * _l ** (1 / 2.4) - 0.055) * 255.0).astype(np.float32)

ff = subprocess.Popen(
    ['ffmpeg', '-v', 'error', '-y', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-s', f'{W}x{H}', '-r', str(fps), '-i', '-',
     '-vf', 'scale=out_color_matrix=bt709:out_range=tv,format=yuv444p',
     '-c:v', 'libx264', '-preset', 'veryfast', '-crf', '6',
     '-color_primaries', 'bt709', '-color_trc', 'bt709', '-colorspace', 'bt709', out_path],
    stdin=subprocess.PIPE)

src = sys.stdin.buffer
lum = np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)


def read_exact(n):
    buf = bytearray()
    while len(buf) < n:
        chunk = src.read(n - len(buf))
        if not chunk:
            return None
        buf.extend(chunk)
    return bytes(buf)


frame = 0
t0 = time.time()
while True:
    head = read_exact(4)
    if head is None:
        break
    (n,) = struct.unpack('<I', head)
    acc = np.zeros((H, W, 3), dtype=np.float32)
    for _ in range(n):
        (length,) = struct.unpack('<I', read_exact(4))
        img = np.asarray(Image.open(BytesIO(read_exact(length))).convert('RGB'))
        acc += TO_LIN[img]
    acc *= 1.0 / n
    out = TO_SRGB[np.clip(acc * 16384.0 + 0.5, 0, 16384).astype(np.int32)]
    if sigma > 0:
        rng = np.random.default_rng(1000 + first + frame)
        noise = rng.standard_normal((H, W), dtype=np.float32) * sigma
        shade = 1.15 - 0.5 * (out @ lum) / 255.0  # a little more grain in the shadows, as film has
        out += (noise * shade)[:, :, None]
    ff.stdin.write(np.clip(out + 0.5, 0, 255).astype(np.uint8).tobytes())
    frame += 1
    if frame % 60 == 0:
        print(f'accum {frame} frames, {time.time() - t0:.0f}s', file=sys.stderr, flush=True)

ff.stdin.close()
ff.wait()
print(f'done {frame} frames in {time.time() - t0:.0f}s', file=sys.stderr, flush=True)
