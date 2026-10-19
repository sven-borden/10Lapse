# Benchmarks

Two targets for 3.0 (issue #34): a 1,000-photo folder shows its filmstrip within 2 s, and 5,000 photos of 24 MP export
to 4K under 1 GB of memory. `scripts/bench.ps1` measures both, and the rest of what a release should not make worse.
It is run on demand, before a release and after any change to import, thumbnails, preview or export. It is not part of
the gate.

## Running it

```
pwsh -File scripts/bench.ps1 -Quick     # 500 photos of 12 MP: about 10 minutes, 2 GB of photos, 11 GB free needed
pwsh -File scripts/bench.ps1 -Full      # 5,000 photos of 24 MP: about 40 minutes, 43 GB of photos, 60 GB free needed
```

Close other GPU work first (a browser playing video, another export) and leave the PC alone while it runs: the app opens
its window and closes it itself. Every run prints a Markdown section for this file and a row for the table below. The
script says how much disk the run needs before it writes anything, deletes the photos at the end unless `-Keep` is
given, and writes only under `-Folder` (default `%LOCALAPPDATA%\Temp\10Lapse-bench`). `-Photos`, `-Size`, `-Skip` and
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
