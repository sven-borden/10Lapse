# 10Lapse

10Lapse turns a folder of photos into a smooth timelapse video, up to 4K. It runs on Windows, and everything happens on your PC: your photos never leave it.

[Get 10Lapse from the Microsoft Store](https://apps.microsoft.com/detail/9NBN2XH6S4D8)

![A whole evening as one frame, from blue sky to night](docs/store/showreel/thumbnail.png)

*From daylight to night in one frame.*

![10Lapse with a sunset sequence imported: the preview, the filmstrip, and the speed and output panels](docs/store/screenshots/en-US/02-editing.png)

![The same photos framed 9:16 for a phone, in the light theme](docs/store/screenshots/en-US/03-vertical.png)

## Status

Version 3.0, a rewrite on WinUI 3 and .NET 10, is code-complete and was submitted to the Microsoft Store on 2026-10-21, straight to the public listing (no beta flight), with the listing, the screenshots and the trailer in six languages. The Store keeps offering 2.1.1, the old UWP app, until 3.0 has passed certification and rolled out. 3.0 needs Windows 11; the 2.1.1 packages were removed from the submission, so a Windows 10 PC that already has 2.1.1 keeps it, but no new Windows 10 customer can get the app from the Store. The old code lives at the tag `v2.1.1-legacy`.

10Lapse 3.0 speaks English, French, German, Spanish, Italian and Brazilian Portuguese, and follows the language Windows gives the app. The Store screenshots of every language are in [`docs/store/screenshots/`](docs/store/screenshots/).

## What it does

- Turns a folder of photos into a timelapse video, up to 4K. Import a folder, chosen photos, or drag them onto the window.
- Reads JPEG, PNG and TIFF; HEIC and camera RAW with Microsoft Store extensions. Puts the photos in the order they were taken; drag a thumbnail to move it.
- Sets the speed by frames per second, by video length, or by seconds per photo. Frame rates are exact, from 0.1 to 120 fps, 29.97 included. The photo count, speed and video length are always on screen.
- Frames the video to match the photos, or as 16:9, 9:16, 1:1, 4:5 or 4:3, with Fit or Fill.
- Shows a live preview drawn by the same code as the export, so the video looks like the preview.
- Has a filmstrip with thumbnails, gap markers for missing photos, and remove and undo. Removing a photo takes it out of the video, never off the disk.
- Exports an MP4 in H.264 (or HEVC where the PC supports it) at 720p, 1080p, 1440p or 4K, in three quality levels, with a file size estimate before you start.
- Exports on the graphics card's video encoder where the PC has one, keeps going when minimized, and keeps the PC awake until it is done.
- Says what went wrong and what to do next, and works offline: no account, no analytics.
- Has light, dark and high contrast themes, keyboard shortcuts and screen reader support.

The wording of the Store listing, with its limits and the other languages, is in [`docs/store/listing.md`](docs/store/listing.md).

## Build

You need Windows 11 (10.0.22000 or later), the .NET 10 SDK, the Windows SDK 10.0.26100 and the Windows App Runtime 2. The debug build of the app and the test host start on the installed runtime, and crash at startup without it. Visual Studio is not required; the .NET command line builds the app. The showreel and the screenshots are in Git LFS: clone with git-lfs installed to get the files, otherwise they are small pointers.

```
pwsh -File scripts/gate.ps1
```

The gate checks that every project is in `TenLapse.slnx`, restores packages, builds x64 Debug with warnings as errors, runs every test, and builds x64 Release.

The scripts in `scripts/` each have a help text (`Get-Help scripts/<name>.ps1 -Full`):

- `pack.ps1`: builds the Store upload, an unsigned `.msixupload` in `dist/store`, and checks the packages (`pwsh -File scripts/pack.ps1`)
- `store-screenshots.ps1`: builds the Debug app and makes the eight Store screenshots of a language from generated sample photos into `docs/store/screenshots/<language>/`; `-Language fr` picks the language and its number format
- `launch-test.ps1`: starts the Debug app many times, under CPU or GPU load if asked, and keeps the details of any launch that crashes
- `a11y-check.ps1`, `motion-check.ps1`, `pseudo-check.ps1`, `bench.ps1`: the accessibility scan, the motion log, the pseudo-localized screens and the benchmark, each run on demand

The Debug build has launch switches that the Release build does not contain, such as `--sample-frames=N` (generated photos), `--theme=light|dark`, `--lang=<tag>` (the app's language for the run, for example `--lang=fr`) and `--region=<tag>` (the regional format for the numbers, for example `--region=fr-FR`). They apply to the run only and are never saved.

The solution at the repo root has eight projects:

- `src/TenLapse.App`: the WinUI 3 app, packaged as a single-project MSIX
- `src/TenLapse.Core`: project model and timing, with no UI or Windows types
- `src/TenLapse.Presentation`: view models and the error path, with no UI types
- `src/TenLapse.Media`: photo decoding, frame rendering and video encoding
- `tests/TenLapse.Tests`: xUnit tests
- `tools/TenLapse.A11yScan`: the Accessibility Insights rules (Axe.Windows) run on a running window, used by `scripts/a11y-check.ps1` (not shipped)
- `tools/TenLapse.Spike`: the export pipeline's measuring harness (not shipped)
- `tools/TenLapse.CrashCatcher`: a debugger for start-up crashes, used by `scripts/launch-test.ps1` (not shipped)

## Privacy

10Lapse does not collect, store or transmit personal data. Read the [privacy policy](PRIVACY.md).

## License

No license has been chosen yet. All rights are reserved.
