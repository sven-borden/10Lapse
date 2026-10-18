"""Recreates tests/TenLapse.Tests/Fixtures/Photos/ byte for byte.

    python -P tests/TenLapse.Tests/Fixtures/make_fixtures.py

Needs Pillow in the user or system site (-P keeps the script folder off the import path without hiding it; -I would; written with 12.0.0; another version may write other bytes, same pixels). Everything is generated
here: no third-party photos. Fixtures/README.md says what is awkward about each file and what the decoder does with
it; Fixtures/PhotoFixtureCatalogue.cs holds the same expectations for the tests.

The scene every picture shows has four flat quadrants, so a test can read one pixel in the middle of each:
red top-left (the orientation marker), blue top-right, green bottom-left, yellow bottom-right.
"""

import io
import math
import random
import struct
import zlib
from pathlib import Path

from PIL import Image

OUT = Path(__file__).resolve().parent / "Photos"

# top-left, top-right, bottom-left, bottom-right
SCENE = [(240, 20, 20), (30, 60, 220), (30, 200, 60), (240, 220, 30)]
GRAY_SCENE = [230, 40, 120, 190]
W, H = 96, 64


def quadrants(w, h, colours):
    image = Image.new("RGB" if isinstance(colours[0], tuple) else "L", (w, h))
    for i, colour in enumerate(colours):
        x, y = (i % 2) * (w // 2), (i // 2) * (h // 2)
        image.paste(colour, (x, y, x + w // 2, y + h // 2))
    return image


def noisy(w, h, seed):
    """The scene plus noise, so a JPEG of it is big enough for a cut to lose real image data."""
    rng = random.Random(seed)
    image = quadrants(w, h, SCENE)
    pixels = image.load()
    for y in range(h):
        for x in range(w):
            n = rng.randint(-40, 40)
            r, g, b = pixels[x, y]
            pixels[x, y] = (max(0, min(255, r + n)), max(0, min(255, g + n)), max(0, min(255, b + n)))
    return image


def jpeg_bytes(image, **options):
    buffer = io.BytesIO()
    image.save(buffer, "JPEG", quality=90, **options)
    return buffer.getvalue()


# ---- ICC profiles: matrix/TRC, version 2, built by hand ----

D50 = (0.9642, 1.0, 0.8249)
# Colorants adapted to D50 (the profile connection space), as the real profiles carry them.
PRIMARIES = {
    "srgb": [(0.4360747, 0.2225045, 0.0139322), (0.3850649, 0.7168786, 0.0971045), (0.1430804, 0.0606169, 0.7141733)],
    "adobergb": [(0.6097559, 0.3111242, 0.0194811), (0.2052401, 0.6256560, 0.0608902), (0.1492240, 0.0632197, 0.7448387)],
    "displayp3": [(0.5151, 0.2412, -0.0011), (0.2920, 0.6922, 0.0419), (0.1571, 0.0666, 0.7841)],
}
TRC = {"srgb": "srgb", "adobergb": 2.19921875, "displayp3": "srgb"}
NAMES = {"srgb": "sRGB IEC61966-2.1 (generated)", "adobergb": "Adobe RGB (1998) (generated)", "displayp3": "Display P3 (generated)"}


def srgb_decode(v):
    return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4


def srgb_encode(v):
    v = max(0.0, min(1.0, v))
    return v * 12.92 if v <= 0.0031308 else 1.055 * v ** (1 / 2.4) - 0.055


def trc_decode(kind, v):
    return srgb_decode(v) if kind == "srgb" else v ** kind


def s15(v):
    return struct.pack(">i", round(v * 65536))


def xyz_tag(x, y, z):
    return b"XYZ \0\0\0\0" + s15(x) + s15(y) + s15(z)


def curve_tag(kind):
    if kind == "srgb":
        table = [round(srgb_decode(i / 1023) * 65535) for i in range(1024)]
        return b"curv\0\0\0\0" + struct.pack(">I", 1024) + b"".join(struct.pack(">H", v) for v in table)
    return b"curv\0\0\0\0" + struct.pack(">I", 1) + struct.pack(">H", round(kind * 256))


def icc_profile(name):
    description = NAMES[name].encode("ascii") + b"\0"
    tags = {
        b"desc": b"desc\0\0\0\0" + struct.pack(">I", len(description)) + description + b"\0" * (4 + 4 + 2 + 1 + 67),
        b"cprt": b"text\0\0\0\0Generated for the 10Lapse fixtures. No copyright.\0",
        b"wtpt": xyz_tag(*D50),
        b"rXYZ": xyz_tag(*PRIMARIES[name][0]),
        b"gXYZ": xyz_tag(*PRIMARIES[name][1]),
        b"bXYZ": xyz_tag(*PRIMARIES[name][2]),
    }
    curve = curve_tag(TRC[name])
    tags[b"rTRC"] = tags[b"gTRC"] = tags[b"bTRC"] = curve
    order = [b"desc", b"cprt", b"wtpt", b"rXYZ", b"gXYZ", b"bXYZ", b"rTRC", b"gTRC", b"bTRC"]
    offset = 128 + 4 + 12 * len(order)
    table, body, placed = b"", b"", {}
    for tag in order:
        data = tags[tag]
        if data is curve and "curve" in placed:  # the three curves share one block
            at = placed["curve"]
        else:
            at = offset + len(body)
            body += data + b"\0" * (-len(data) % 4)
            if data is curve:
                placed["curve"] = at
        table += tag + struct.pack(">II", at, len(data))
    size = 128 + 4 + len(table) + len(body)
    header = struct.pack(">I4sI4s4s4s", size, b"\0\0\0\0", 0x02400000, b"mntr", b"RGB ", b"XYZ ")
    header += struct.pack(">6H", 2026, 1, 1, 0, 0, 0) + b"acsp" + b"MSFT" + b"\0" * 4 + b"\0" * 4 + b"\0" * 4 + b"\0" * 8
    header += struct.pack(">I", 0) + s15(D50[0]) + s15(D50[1]) + s15(D50[2]) + b"\0" * 4 + b"\0" * 44
    assert len(header) == 128, len(header)
    return header + struct.pack(">I", len(order)) + table + body


def inverse(m):
    (a, b, c), (d, e, f), (g, h, i) = m
    det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g)
    return [
        [(e * i - f * h) / det, (c * h - b * i) / det, (b * f - c * e) / det],
        [(f * g - d * i) / det, (a * i - c * g) / det, (c * d - a * f) / det],
        [(d * h - e * g) / det, (b * g - a * h) / det, (a * e - b * d) / det],
    ]


def to_srgb(name, rgb):
    """What a colour managed viewer shows for stored `rgb` in the profile `name` (relative colorimetric, clipped)."""
    lin = [trc_decode(TRC[name], v / 255) for v in rgb]
    cols = PRIMARIES[name]
    xyz = [sum(cols[c][row] * lin[c] for c in range(3)) for row in range(3)]
    srgb = PRIMARIES["srgb"]
    matrix = inverse([[srgb[c][row] for c in range(3)] for row in range(3)])
    return tuple(round(255 * srgb_encode(sum(matrix[row][c] * xyz[c] for c in range(3)))) for row in range(3))


# ---- hand-written files ----

def png_chunk(kind, data):
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))


def png_rgb16(w, h, colours):
    """A 16-bit RGB PNG (Pillow cannot write one): every 8-bit level v is stored as v * 257."""
    rows = b""
    for y in range(h):
        row = b"\0"
        for x in range(w):
            r, g, b = colours[(y // (h // 2)) * 2 + x // (w // 2)]
            row += struct.pack(">3H", r * 257, g * 257, b * 257)
        rows += row
    header = struct.pack(">IIBBBBB", w, h, 16, 2, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + png_chunk(b"IHDR", header) + png_chunk(b"IDAT", zlib.compress(rows, 9)) + png_chunk(b"IEND", b"")


def indexed(w, h, colours):
    image = Image.new("P", (w, h))
    image.putpalette([c for colour in colours for c in colour])
    for i in range(4):
        x, y = (i % 2) * (w // 2), (i // 2) * (h // 2)
        image.paste(i, (x, y, x + w // 2, y + h // 2))
    return image


def write(name, data):
    (OUT / name).write_bytes(data)


def main():
    OUT.mkdir(exist_ok=True)
    for old in OUT.iterdir():
        if old.is_file():
            old.unlink()
    scene = quadrants(W, H, SCENE)

    # ---- files the decoder rejects ----
    write("empty.jpg", b"")
    big = jpeg_bytes(noisy(256, 160, 1))
    write("truncated.jpg", big[: len(big) * 6 // 10])
    assert big.endswith(b"\xff\xd9")
    write("no-eoi.jpg", big[:-2])
    write("text.heic", b"This is a text file that somebody named .heic. It is not a photo.\n")

    # ---- files that are not what their name says ----
    buffer = io.BytesIO()
    scene.save(buffer, "PNG")
    write("png-named.jpg", buffer.getvalue())
    rng = random.Random(7)
    trailer = b"\0\0\0\x18ftypmp42" + bytes(rng.randrange(256) for _ in range(200 * 1024 - 12))
    while trailer.endswith(b"\xff\xd9"):
        trailer = trailer[:-1] + b"\0"
    write("trailing-data.jpg", big + trailer)

    # ---- formats and colour ----
    write("progressive.jpg", jpeg_bytes(noisy(256, 160, 2), progressive=True))
    write("cmyk.jpg", jpeg_bytes(scene.convert("CMYK")))
    write("gray.jpg", jpeg_bytes(quadrants(W, H, GRAY_SCENE)))
    write("rgb16.png", png_rgb16(W, H, SCENE))
    palette = indexed(W, H, SCENE)
    buffer = io.BytesIO()
    palette.save(buffer, "PNG", transparency=bytes([255, 255, 128, 0]))
    write("palette-alpha.png", buffer.getvalue())
    frames = [indexed(W, H, SCENE), indexed(W, H, [(0, 255, 0)] * 4)]
    buffer = io.BytesIO()
    frames[0].save(buffer, "GIF", save_all=True, append_images=frames[1:], duration=500, loop=0)
    write("two-frames.gif", buffer.getvalue())

    # ---- colour profiles ----
    for name, file in (("srgb", "profile-srgb.jpg"), ("adobergb", "profile-adobergb.jpg"), ("displayp3", "profile-displayp3.jpg")):
        write(file, jpeg_bytes(scene, icc_profile=icc_profile(name)))

    # ---- EXIF ----
    for orientation in (1, 3, 6, 8):
        exif = Image.Exif()
        exif[0x0112] = orientation
        write(f"orientation-{orientation}.jpg", jpeg_bytes(scene, exif=exif))
    exif = Image.Exif()
    exif[0x0132] = "2024:06:15 14:30:45"
    exif_ifd = exif.get_ifd(0x8769)
    exif_ifd[0x9003] = "2024:06:15 14:30:45"  # DateTimeOriginal
    exif_ifd[0x9291] = "123"  # SubSecTimeOriginal
    write("exif-date.jpg", jpeg_bytes(scene, exif=exif))

    for name, rgb in (("adobergb", SCENE), ("displayp3", SCENE)):
        print(name, "->", [to_srgb(name, c) for c in rgb])
    total = sum(f.stat().st_size for f in OUT.iterdir())
    print(f"{len(list(OUT.iterdir()))} files, {total / 1024:.0f} KB")


if __name__ == "__main__":
    main()
