# Benchmarks

Two targets for 3.0 (issue #34): a 1,000-photo folder shows its filmstrip within 2 s, and 5,000 photos of 24 MP export
to 4K under 1 GB of memory on PCs with 8 GB of RAM or less or an integrated GPU (see "The memory target" below).
`scripts/bench.ps1` measures both, and the rest of what a release should not make worse. It is run on demand, before a
release and after any change to import, thumbnails, preview or export. It is not part of the gate.

## Running it

```
pwsh -File scripts/bench.ps1 -Quick                 # 500 photos of 12 MP: about 5 minutes with the builds, writes about 4 GB, needs 11 GB free
pwsh -File scripts/bench.ps1 -Full                  # 5,000 photos of 24 MP: about 15 minutes with the builds, 20 with -TightMemory, writes about 50 GB, needs 56 GB free
pwsh -File scripts/bench.ps1 -Full -TightMemory     # the same, the export with 4 frames decoded ahead and 3 threads
```

The times include the two builds. The disk figures are what the script prints before it writes anything (it refuses when
the space is not free); the photos are deleted at the end unless `-Keep` is given.

Close other GPU work first (a browser playing video, another export) and leave the PC alone while it runs: the app opens
its window and closes it itself. Every run prints a Markdown section for this file and a row for the table below.
Photos, the exported video, caches and the bench logs go only under `-Folder` (default
`%LOCALAPPDATA%\Temp\10Lapse-bench`). Two things are written elsewhere: the app writes its normal log to
`%LOCALAPPDATA%\10Lapse\Logs`, and the builds write `bin` and `obj` in the repository. `-Photos`, `-Size`, `-SkipSteps`
(comma separated), `-Runs` and `-TightMemory` change the profile; `Get-Help scripts/bench.ps1` lists them.

If a run stops with "The app wrote no report", the Debug app died at start-up (exit code -1073741189, 0xC000027B, with
the app log ending after "Remembered settings loaded"). That is the known start-up crash,
[#90](https://github.com/sven-borden/10Lapse/issues/90), not the benchmark. The script prints the exit code and the last
line of the app's log and tries that run once more; if it fails twice, rerun the script.

What it measures, and with which existing tool:

| Measure | Tool |
|---|---|
| First thumbnails of a 1,000-photo folder, and index time (import done, metadata read) for 1,000 and all photos | the app with `--import-bench=FOLDER` (Debug switch added by #34; imports through the Choose folder view model, reports from the moment the import starts) |
| Export frames per second, peak working set and private bytes | the Spike's `export-project --photos FOLDER` (the product's `ProjectExporter`), 4K H.264 High, 30 fps |
| Preview playback pacing, scrubbing and memory | the app with `--preview-bench` (#19) on generated sample photos |
| Filmstrip sweep and jumps | the app with `--filmstrip-bench` (#20) on generated sample photos |

The photos come from the Spike's `bench-photos` command: a few distinct JPEGs drawn with grain (0.36 byte a pixel, 8.6 MB
at 24 MP, like a camera's), each given an EXIF block with `DateTime`, `DateTimeOriginal` and a 160 pixel embedded
picture, then copied under as many names as needed with the capture time patched in place (5 s apart). Only a few
images are distinct, so decode work is realistic, but file compression and the file cache behave a little more kindly
than on a library of thousands of different photos. The first 1,000 are hard links of the same files, so the 1,000-photo
measures cost no disk. A second 1,000-photo folder has no embedded picture, the worst case for the filmstrip (every
thumbnail is decoded from the photo or its proxy).

Notes for reading the numbers:

- The app benches (import, preview, filmstrip) run the Debug configuration built with `Optimize=true`, because the
  switches are compiled out of Release. Debug still has asserts and unoptimised dependencies, so the UI-path numbers are
  an upper bound: a Release build is as fast or faster. The export runs in the Spike, in Release.
- The export is sensitive to other GPU work. The script prints the GPU load it saw before the export and puts a note in
  the section when it was over 15 %. That check uses `nvidia-smi`, so it only exists for NVIDIA GPUs; with an AMD or
  Intel GPU the section says the load was not measured, and closing other GPU work is up to the person running it.
- The preview and filmstrip benches use the 2400 x 1600 sample photos the `--sample-frames` switch generates (1,000 for
  `-Full`, 300 for `-Quick`), not the generated 24 MP photos: those switches cannot import a folder.
- Memory is the process's own: working set and private bytes. Video memory on the GPU is not in either.

## Results

One row per run. The machine column is the GPU and RAM; the sections below name the whole machine.

| Date | Release | Commit | Machine | Profile | First view filled (photos) | Index, first 1,000 (photos) | Index, all (photos) | Export | Export peak working set / private |
|---|---|---|---|---|---|---|---|---|---|
| 2026-10-19 | 3.0-dev | d2a5a68 | NVIDIA GeForce RTX 4060 Ti, 64 GB | Full | 455 ms (1,000) | 954 ms (1,000) | 3,445 ms (5,000) | 25.5 fps | 606 / 1,336 MB |
| 2026-10-19 | 3.0-dev | 2272c9a | NVIDIA GeForce RTX 4060 Ti, 64 GB | Full, tight-memory profile (export only) | - | - | - | 13.0 fps | 387 / 938 MB |
| 2026-10-19 | 3.0-dev | 2272c9a | NVIDIA GeForce RTX 4060 Ti, 64 GB | Quick, tight-memory profile | 491 ms (500) | 520 ms (500) | - | 25.6 fps | 377 / 939 MB |

The Full rows are 5,000 photos of 24 MP, the Quick row 500 photos of 12 MP. The tight-memory rows force 4 frames decoded
ahead and 3 decode threads (`-TightMemory`) on a PC that would pick 8 and 6; the "export only" row skipped the import,
preview and filmstrip steps. The first row is the default profile of this PC. Both runs on 2272c9a had other work on the
GPU (38 to 53 % busy: Edge, the desktop and another agent's app launches), which the export, being CPU-bound, felt
little; the Spike's `encode` measured the same profile at 14.6 fps on 400 frames.

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
| Export peak memory, default profile (8 ahead, 6 threads on this PC) | under 1 GB on 8 GB RAM or less or an integrated GPU only | working set 606 MB, private bytes 1,336 MB (14 MB before the export, managed heap 12 MB) | info (bigger PCs may use more memory for speed) |
| Preview, cold playback (5 s, proxies being built), 1,000 sample photos | none set | 151 of 151 frames shown, 0 dropped; lateness median 10.0 ms, p95 10.5 ms | info |
| Preview, warm playback (10 s) | none set | 300 of 300 frames shown, 0 dropped; lateness median 9.5 ms, p95 9.9 ms | info |
| Preview, fast sweep over the project in 4 s | none set | 240 of 240 seeks shown while current; seek to picture median 1.1 ms, p95 1.7 ms | info |
| Preview, memory after the proxy pass | within the image budget | private bytes peak 493 MB, 479 MB after | met |
| Filmstrip, sweep over 1,000 frames in 20 s | none set | UI frame interval median 16.7 ms, p95 17.1 ms, max 24 ms; 0 over 33 ms | info |
| Filmstrip, jumps of four views | none set | view filled median 31 ms, p95 32 ms (14 jumps) | info |

### The memory target

The 1 GB target applies on PCs with 8 GB of RAM or less, or an integrated GPU. The exporter gives those PCs the tight
profile, 4 frames decoded ahead and at most 3 decode threads (`VideoExportSettings.DecodeFor`, commit 523020d). Larger
PCs decode 8 ahead on up to 6 threads and may use more memory for speed by design, so the default profile's private
bytes are reported as information, not as a missed target.

The target is met on the tight profile, with a thin margin. Five thousand photos of 24 MP, forced to 4 ahead and 3
threads with `-Full -TightMemory` (the Spike's `export-project --decode-ahead 4 --decode-threads 3`), peaked at 938 MB
private bytes and 387 MB working set: 86 MB, about 8 %, under 1,024 MB (about 6 % under 1,000 MB). A reviewer's 400-frame
`encode` run of the same profile peaked at 932 MB, so about 7 % is the margin to expect. A change to the decode, the
render targets or the Windows App SDK that adds about 90 MB of private bytes to the export puts the target at risk;
the tight row of the table above is the one to compare. The detection of such a PC (RAM, integrated GPU) is not
simulated by the script: `-TightMemory` forces the depth on any PC, and #41's test matrix runs it on a real tight PC.

Where the default profile's memory goes. It is not a leak: 300, 1,200 and 5,000 frames peak at about 1,160, 1,230 and
1,340 MB, and the managed heap stays at 12 MB, so the rest is native and driver memory for the decoded frames and
render targets, sized by the decode pipeline, not by the photo size (the 12 MP run of `-Quick` peaks at 1,246 MB
private). Spike `encode` with explicit sizes, on the same 24 MP photos, 400 frames to 4K:

| Decode ahead / threads / targets | Frames/s | Peak working set | Peak private bytes |
|---|---|---|---|
| automatic (8 ahead, 6 threads), 4 targets | 26.5 | 498 MB | 1,161 MB |
| 4 / 3 / 4 (the tight profile) | 14.6 | 376 MB | 870 MB |
| 2 / 2 / 2 | 6.9 | 311 MB | 620 MB |

The export is decode-bound on the CPU: halving the pipeline halves the speed. Only the 2 / 2 / 2 row has real headroom
under 1 GB, at about a quarter of the default speed. (The 4 / 3 / 4 row measured 870 MB in that run, 932 MB when the
reviewer repeated it on another set of 24 JPEGs, and 938 MB in the 5,000-photo run: use the 5,000-photo number.)
