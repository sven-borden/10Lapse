# 10Lapse showreel

`10Lapse-showreel.mp4` is a 30-second motion piece about 10Lapse 3.0: 1920 × 1080 at 60 fps, H.264 with AAC stereo.
It is stored in Git LFS: clone with git-lfs installed to get the video, otherwise the file is a small pointer.

![The evening as a time slice, 27.7 s into the showreel](poster.jpg)

Nothing in it is edited by hand. Every frame comes from `source/reel.html` and the soundtrack from `source/music.py`,
both on one timeline at 120 BPM (a beat every 0.5 s, a bar every 2 s).

## What it shows

| Time | Beat |
|---|---|
| 0–2 s | The shoot: one photo every 5 seconds, faster and faster through an amber gate, IMG_0001.JPG to IMG_0742.JPG. |
| 2–5.5 s | 742 photos as a contact sheet, day at the top and night at the bottom. They fly into one stack. |
| 5.5–8 s | The stack rewinds and plays: "742 photos → 24.7 s at 30 frames per second". |
| 8–12 s | First launch, a folder dragged onto the window, the arrival choreography, the window taken apart into its layers. |
| 12–20 s | Speed, shape, exact timing (29.97 fps is 30000/1001), remove and undo in the filmstrip. Windows turns dark at sunset. |
| 20–26 s | Export: the frames pass the amber gate to 100 %, then "Harbor sunset.mp4 is ready". |
| 26–30 s | Night: "Your photos never leave this PC." The evening becomes a time slice, which folds into the Five frames icon. |

The photos are the app's sample look, the harbor sunset of the Debug `--sample-frames` switch in the brand's
day-to-night ramp, drawn by the page; there are no real photos. The window is rebuilt in HTML from the design canvas
(`docs/design/canvas/Main.dc.html`) with the app's own strings, and it shows only what 3.0 does. Inside the window the
motion uses `DESIGN.md`'s tokens (develop, cascade, segment pill, reframe, panel swap, film gate, check); the camera
around it, the type and the sound belong to the showreel.

It is the Store trailer of the 3.0 listing (#43, Sven's choice, 2026-10-21). #43 had asked for a real timelapse made in
10Lapse; that can come with a later listing update. The Store wants the trailer re-encoded to its MP4 profile on
upload, so this file stays the source.

## Trailer thumbnail

`thumbnail.png` is the 27.7 s frame above as a 1920 x 1080 PNG, the size the Store asks for. Its captions (255
characters at most) in the six listing languages are in [`thumbnail-captions.md`](thumbnail-captions.md).

[![The evening as a time slice](thumbnail.png)](10Lapse-showreel.mp4)

## Make it again

Needs Node 18 or later with `playwright-core` and a Chromium build, Python 3 with NumPy and Pillow, and ffmpeg.
From `source/`, with `PLAYWRIGHT_CORE` pointing at a `playwright-core` package folder and `CHROME` at a Chromium or
`chrome-headless-shell` executable when they are not installed where Node finds them:

```powershell
python music.py music.wav
node render.mjs video 60 | python accum.py frames.mp4 60
$x264 = '-c:v libx264 -preset slower -tune film -x264-params aq-mode=3:aq-strength=0.9 -b:v 7000k -maxrate 11000k -bufsize 22000k -pix_fmt yuv420p -colorspace bt709 -color_primaries bt709 -color_trc bt709 -color_range tv'.Split(' ')
ffmpeg -i frames.mp4 @x264 -pass 1 -an -f mp4 NUL
ffmpeg -i frames.mp4 -i music.wav -map 0:v -map 1:a @x264 -pass 2 -c:a aac -b:a 192k -movflags +faststart -shortest 10Lapse-showreel.mp4
```

`render.mjs` steps the page through `window.renderAt(t)` and screenshots it. Each frame averages 3 to 16 samples
across a 180° shutter (`window.samplesAt`), never across a cut (`window.CUTS`), which is the motion blur; `accum.py`
averages them, adds a fine grain that also dithers the gradients, and writes a near-lossless intermediate. The full
render takes about half an hour; `render.mjs video 60 <from> <to>` with `accum.py <out> 60 1.6 <from × 60>` renders a
range, so three ranges can run at once and be joined with ffmpeg's concat demuxer. `node render.mjs stills <folder>
2.5,9.9,22.4` writes single frames for checking.
