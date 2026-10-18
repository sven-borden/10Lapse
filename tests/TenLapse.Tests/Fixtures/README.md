# Photo fixtures (#33)

A folder of broken and awkward photos for the decoder, the import (#12) and the export. Every file is generated:
no third-party photos, nothing here is a real person's picture.

```
python -P tests/TenLapse.Tests/Fixtures/make_fixtures.py
```

recreates `Photos/` byte for byte (Pillow 12.0.0 writes the same bytes each run). The expectations below are also
in code, `PhotoFixtureCatalogue.cs`, and `PhotoFixtureTests.TheFolderHoldsExactlyTheCataloguedFilesAndTheReadmeNamesEachOne`
fails when a file is missing from either, or from this table. `Photos/` is under 300 KB and is copied next to the
test host (`Fixtures/Photos` in the output folder).

## The scene

Every picture shows four flat quadrants: **red top-left** (the orientation marker), blue top-right, green
bottom-left, yellow bottom-right. 96x64 unless the table says otherwise. A test reads one pixel in the middle of
each quadrant of the decoded bitmap. Decoded means `PhotoDecoder.DecodeAsync` with `PhotoDecodeOptions.Cover(1920x1080)`,
which never enlarges, so the decoded size is the file's oriented size.

## Committed files (`Photos/`)

| File | What is awkward | Expected decoder outcome |
|---|---|---|
| `empty.jpg` | 0 bytes | `EmptyFile` (header read and decode) |
| `truncated.jpg` | a noisy 256x160 JPEG cut at 60 % | `Truncated` |
| `no-eoi.jpg` | the same JPEG missing only its 2-byte end-of-image marker | `Truncated`: rejected, a documented choice (telling it from a cut would need decoding the entropy-coded data; such files are rare) |
| `text.heic` | a text file named `.heic` | `UnsupportedFormat` (the content is not HEIF); `NeedsExtension` on a PC without the HEIF decoder |
| `png-named.jpg` | a PNG named `.jpg` | decodes by content, colours exact |
| `trailing-data.jpg` | a 256x160 JPEG followed by 200 KB of other data after its end (a motion photo, a tool trailer) | decodes the image, colours kept |
| `progressive.jpg` | a progressive 256x160 JPEG | decodes |
| `two-frames.gif` | a GIF with 2 frames (the second solid green) | decodes the first frame, `FrameCount` 2 |
| `rgb16.png` | 16 bits per channel | decodes to BGRA8, colours within 2 levels |
| `palette-alpha.png` | palette PNG with a transparency table: bottom-left 50 % opaque green, bottom-right fully transparent | BGRA8 premultiplied: bottom-left (15,100,30,128), bottom-right (0,0,0,0) |
| `gray.jpg` | one-channel JPEG, quadrants 230, 40, 120, 190 | R = G = B = those levels |
| `cmyk.jpg` | four-channel JPEG with an Adobe APP14 marker | decodes through Windows' default CMYK profile, so the colours are ink-like, not the scene's: (218,64,51) (77,75,124) (36,133,91) (240,203,65) within 16 (the stored C = 255 - R, no K) |
| `profile-srgb.jpg` | embedded sRGB ICC profile (hand-built in the script) | decodes unconverted, scene colours |
| `profile-adobergb.jpg` | embedded Adobe RGB (1998) profile; the stored scene values are Adobe RGB | converted to sRGB: (255,12,12) (0,57,225) (0,201,37) (248,221,0) within 16 |
| `profile-displayp3.jpg` | embedded Display P3 profile; the stored values are P3 | converted to sRGB: (255,0,0) (17,61,229) (0,204,13) (244,219,0) within 16 |
| `orientation-1.jpg` | EXIF orientation 1, 96x64 | red top-left, 96x64 |
| `orientation-3.jpg` | EXIF orientation 3 (turned 180) | red **bottom-right**, 96x64 |
| `orientation-6.jpg` | EXIF orientation 6 (turned 90 clockwise) | red **top-right**, 64x96 |
| `orientation-8.jpg` | EXIF orientation 8 (turned 90 counterclockwise) | red **bottom-left**, 64x96 |
| `exif-date.jpg` | EXIF `DateTimeOriginal` 2024:06:15 14:30:45 and `SubSecTimeOriginal` 123 | decodes; Windows reads `System.Photo.DateTaken` 2024-06-15 14:30:45 and the sub-second field "123" (Windows folds it into the date as 45.12 s) |

The expected values of the wide-gamut files come from the script (`to_srgb`, relative colorimetric, clipped to
the sRGB gamut); it prints them when it runs.

## Made at run time (`RuntimeFixtures.cs`)

Too big or too stateful to commit. Each test makes its own temp folder and cleans it up.

| Case | How | Expected decoder outcome |
|---|---|---|
| 100 MP JPEG | 10000x10000 through WIC's encoder, once per test run, deleted at exit | header reads 10000x10000; a 1080p cover decode returns 1920x1920 with the red marker top-left |
| Locked file | another `FileStream` with `FileShare.None` | `FileInUse` |
| Deleted after listing | listed by `Directory.EnumerateFiles`, then deleted | `FileNotFound` |
| Long Unicode path | over 300 characters, accents, CJK, an emoji | decodes |
| Read-only file | `FileAttributes.ReadOnly` | decodes (nothing writes to a photo) |

## For the import (#12)

`PhotoFixtureCatalogue.Directory` is the folder; every file in it passes `PhotoCodecs.IsReadable`, so an import
sees all of them and must skip exactly `empty.jpg` (`EmptyFile`), `truncated.jpg` and `no-eoi.jpg` (`Truncated`)
and `text.heic` (`UnsupportedFormat`), and import the rest. `RuntimeFixtures` adds the locked, deleted, long-path
and read-only cases. `PhotoFixtureTests.ImportingTheWholeFolderReportsEveryProblemFile...` is the shape of such a
test with the decoder alone.

## Adding a fixture

1. Add it to `make_fixtures.py` and run the script (it rewrites the whole folder).
2. Add its row to `PhotoFixtureCatalogue.All` and to the table above.
3. A file that is bigger than a few KB, or that needs live state (a lock, a deletion), goes in `RuntimeFixtures`.
4. If it exposes a decoder bug, give the catalogue row a `Skip` that names the bug.
