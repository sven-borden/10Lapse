# Benchmarks

Two targets for 3.0 (issue #34): a 1,000-photo folder shows its filmstrip within 2 s, and 5,000 photos of 24 MP export
to 4K under 1 GB of memory. `scripts/bench.ps1` measures both, and the rest of what a release should not make worse.
It is run on demand, before a release and after any change to import, thumbnails, preview or export. It is not part of
the gate.

## Running it

```
pwsh -File scripts/bench.ps1 -Quick     # 500 photos of 12 MP: about 5 minutes with the builds, 2 GB of photos, 11 GB free needed
pwsh -File scripts/bench.ps1 -Full      # 5,000 photos of 24 MP: about 15 minutes with the builds, 43 GB of photos, 59 GB free needed
```

Close other GPU work first (a browser playing video, another export) and leave the PC alone while it runs: the app opens
its window and closes it itself. Every run prints a Markdown section for this file and a row for the table below. The
script says how much disk the run needs before it writes anything, deletes the photos at the end unless `-Keep` is
given, and writes only under `-Folder` (default `%LOCALAPPDATA%\Temp\10Lapse-bench`). `-Photos`, `-Size`, `-SkipSteps` (comma separated) and
`-Runs` change the profile; `Get-Help scripts/bench.ps1` lists them.

What it measures, and with which existing tool:

| Measure | Tool |
|---|---|
| First thumbnails of a 1,000-photo folder, and index time (import done, metadata read) for 1,000 and all photos | the app with `--import-bench=FOLDER` (Debug switch added by #34; imports through the Choose folder view model, reports from the moment the import starts) |
| Export frames per second, peak working set and private bytes | the Spike's `export-project --photos FOLDER` (the product's `ProjectExporter`), 4K H.264 High, 30 fps |
| Preview playback pacing, scrubbing and memory | the app with `--preview-bench` (#19) on generated sample photos |
| Filmstrip sweep and jumps | the app with `--filmstrip-bench` (#20) on generated sample photos |

The photos come from the Spike's `bench-photos` command: a few distinct JPEGs drawn with grain (0.36 byte a pixel, 8.6 MB
at 24 MP, like a camera's), each given an EXIF block with `DateTime`, `DateTimeOriginal` and a 160 pixel embedded
picture, then copied under as many names as needed with the capture time patched in place (5 s apart). The first 1,000
are hard links of the same files, so the 1,000-photo measures cost no disk. A second 1,000-photo folder has no embedded
picture, the worst case for the filmstrip (every thumbnail is decoded from the photo or its proxy).

Notes for reading the numbers:

- The app benches run the Debug configuration built with `Optimize=true`, because the switches are compiled out of
  Release. The compiler flag is the only difference from Release; the Spike runs in Release.
- The export is sensitive to other GPU work. The script prints the GPU load it saw before the export and puts a note in
  the section when it was over 15 %.
- The preview and filmstrip benches use the 2400 x 1600 sample photos the `--sample-frames` switch generates (1,000 for
  `-Full`, 300 for `-Quick`), not the generated 24 MP photos: those switches cannot import a folder.
- Memory is the process's own: working set and private bytes. Video memory on the GPU is not in either.

## Results

One row per run. The machine column is the GPU and RAM; the sections below name the whole machine.

| Date | Release | Commit | Machine | Profile | First view filled (photos) | Index 1,000 | Index all (photos) | Export | Export peak working set / private |
|---|---|---|---|---|---|---|---|---|---|
| 2026-10-19 | 3.0-dev | d2a5a68 | NVIDIA GeForce RTX 4060 Ti, 64 GB | Full | 455 ms (1,000) | 954 ms | 3,445 ms (5,000) | 25.5 fps | 606 / 1,336 MB |

### 3.0-dev, 2026-10-19, commit d2a5a68 (Full)

- Machine: 11th Gen Intel Core i7-11700F @ 2.50 GHz; NVIDIA GeForce RTX 4060 Ti (driver 32.0.16.1714); 64 GB RAM; Windows 11 26H2, build 26300.9711; photos on an NVMe SSD.
- Photos: 5,000 JPEGs of 6000 x 4000 (24 MP, 8.8 MB each, 43.8 GB), EXIF capture times 5 s apart and an embedded picture.
- The GPU was not quiet: another process kept it at 35 to 45 % (Edge, desktop) during the run, and an agent was building on the same PC. The export is decode-bound on the CPU (see below), so this moved it little: a second export, run alone afterwards, took 26.7 fps against 25.5.

| Measure | Target | Result | Verdict |
|---|---|---|---|
| First thumbnails, 1,000 photos | filmstrip shows within 2 s | first thumbnail 426 ms, first view filled 455 ms (median of 3, 347 to 535 ms) | met |
| Index, 1,000 photos (import done, metadata read) | none set | 954 ms (median of 3); capture time and size on all 1,000 | info |
| Index, 5,000 photos | none set | 3.4 s; capture time and size on all 5,000; the app's private bytes peaked at 503 MB, 335 MB settled | info |
| First thumbnails, 1,000 photos with no embedded picture | none set | first thumbnail 418 ms, first view filled 1,013 ms (15 thumbnails decoded from the photo at 158 ms each) | info |
| Export, 5,000 photos x 24 MP to 3840 x 2160 H.264 | none set | 25.5 frames/s (196 s), NVIDIA H.264 Encoder MFT (hardware), 727 MB file | info |
| Export peak memory | under 1 GB | working set 606 MB, **private bytes 1,336 MB** (14 MB before the export, managed heap 12 MB) | working set met, private bytes MISSED |
| Preview, cold playback (5 s, proxies being built), 1,000 sample photos | none set | 151 of 151 frames shown, 0 dropped; lateness median 10.0 ms, p95 10.5 ms | info |
| Preview, warm playback (10 s) | none set | 300 of 300 frames shown, 0 dropped; lateness median 9.5 ms, p95 9.9 ms | info |
| Preview, fast sweep over the project in 4 s | none set | 240 of 240 seeks shown while current; seek to picture median 1.1 ms, p95 1.7 ms | info |
| Preview, memory after the proxy pass | within the image budget | private bytes peak 493 MB, 479 MB after | met |
| Filmstrip, sweep over 1,000 frames in 20 s | none set | UI frame interval median 16.7 ms, p95 17.1 ms, max 24 ms; 0 over 33 ms | info |
| Filmstrip, jumps of four views | none set | view filled median 31 ms, p95 32 ms (14 jumps) | info |

Where the export's memory goes (a gap to decide, not tuned here). The 1 GB target holds for the working set (531 to 606
MB over two runs) and not for private bytes (commit): 1,215 and 1,336 MB. It is not a leak: 300, 1,200 and 5,000 frames
peak at about 1,160, 1,230 and 1,340 MB, and the managed heap stays at 12 MB, so the rest is native and driver memory
for the decoded frames and render targets. It follows the decode pipeline's size, which the exporter chooses by itself: on a
discrete GPU with plenty of RAM, 8 frames decoded ahead and up to 6 decode threads (commit 523020d). Spike `encode`
with explicit sizes, on the same 24 MP photos, 400 frames to 4K:

| Decode ahead / threads / targets | Frames/s | Peak working set | Peak private bytes |
|---|---|---|---|
| automatic (8 ahead, 6 threads), 4 targets | 26.5 | 498 MB | 1,161 MB |
| 4 / 3 / 4 | 14.6 | 376 MB | 870 MB |
| 2 / 2 / 2 | 6.9 | 311 MB | 620 MB |

The export is decode-bound on the CPU: halving the pipeline halves the speed. Getting private bytes under 1 GB at 24 MP
costs speed (between the first two rows), unless the decode itself gets cheaper or the budget is read as the working set.
The 12 MP run of `-Quick` (500 photos) peaks at 1,246 MB private, so the photo size is not what sets it; the pipeline
depth is.
