# 10Lapse 3.0 status

Updated 2026-10-19. Roadmap issue: #59. Working agreement: `CLAUDE.md`.

## Now

2026-10-19: #22 and #29 merged. Building: #19 preview (Opus) and #23 output panel (Sonnet). Next: wave 10, #20 filmstrip and #17 timing panel.

## Waves

Each wave runs at most two issues at once, only when they touch disjoint
areas. A wave starts when everything it depends on is merged. Model: **O** is
Opus 5.5, **S** is Sonnet 5.5.

| Wave | Issue | Model | Area | Status |
|---|---|---|---|---|
| 0 | #1 Toolchain | orchestrator | machine | done |
| 1 | #3 Tag legacy, new solution layout, gate script | O | repo root, all projects | merged #61 |
| 1 | #6 Privacy policy and README | S | `README.md`, `PRIVACY.md` | merged #60 |
| 2 | #4 Package identity and MSIX | S | `src/TenLapse.App` manifest, packaging props | merged #62 |
| 2 | #10 Exact frame timing | S | `TenLapse.Core/Timing` | merged #63 |
| 3 | #8 Spike: streaming encode | O | `TenLapse.Media`, a spike harness | merged #65 |
| 3 | #27 Design tokens and theme resources | S | `TenLapse.App/Themes` | merged #64 |
| 4 | #9 Spike: encoder capabilities | O | `TenLapse.Media/Encoding` | merged #68 |
| 4 | #16 Project model and view models | O | `TenLapse.Core/Projects`, new `TenLapse.Presentation` | merged #66 |
| 5 | #28 Title bar and app shell | O | `TenLapse.App` window and shell | merged #67 |
| 5 | #13 Photo formats through WIC | O | `TenLapse.Media/Decoding` | merged #70 |
| 6 | #26 One error path | O | App startup, `Core/Diagnostics` | merged #69 |
| 6 | #15 Frame order | S | `TenLapse.Core/Ordering` | merged #71 |
| 7 | #12 Import folder, files, drag and drop | O | `Core/Import`, App import commands | merged #73 |
| 7 | #33 Broken-photo fixtures | S | `tests/` fixtures | merged #72 |
| 8 | #22 Export pipeline | O | `TenLapse.Media/Export` | merged #74 |
| 8 | #29 First launch drop zone | O | App empty state view | merged #75 |
| 9 | #19 Preview with proxy cache | O | preview view and cache | building |
| 9 | #23 Output panel | S | output inspector section | building |
| 10 | #20 Filmstrip | O | filmstrip view | |
| 10 | #17 Timing panel | S | timing inspector section | |
| 11 | #24 Export panel and finished state | O | export panel | |
| 11 | #18 Frame panel | S | frame inspector section | |
| 12 | #32 Golden render test | O | `tests/` golden | |
| 12 | #25 Export while minimized, keep awake | S | export power request | |
| 13 | #21 Remember settings | S | settings persistence | |
| 13 | #14 HEIC and RAW extension prompts | S | import InfoBars | |
| 14 | #31 Rating prompt | S | Store rating | |
| 14 | #34 Benchmark | S | `tests/` bench, `docs/benchmarks.md` | |
| 15 | #36 Narrow window layout | S | shell visual states | |
| 15 | #37 Icon and tile assets | S | `Assets/` (direction A Five frames) | |
| 16 | #35 Motion system | O | every view, alone | |
| 17 | #39 Strings to resource files | S | every view, alone | |
| 18 | #38 Accessibility pass | O | every view, alone | |

After wave 18, 3.0 is code-complete. Milestones 4 (private beta) and 5
(relaunch) are mostly owner work; milestone 6 (3.1 creative tools) gets its
own waves once 3.0 ships.

## Owner items (Sven)

Nothing blocks the build loop. Everything below waits until release is near
(Sven, 2026-10-19).

- Before the first 3.0 submission, one Partner Center visit: #2 numbers
  (decides x86 and the first translations; #39 extracts English strings
  without them), #7 description line, and the privacy URL pointed at
  `PRIVACY.md` on GitHub.
- #30 Sample sequence: deferred. #29 ships without the *Try a sample* button;
  add it once a set exists.
- Release: #40 flight, #42 listing, #43 trailer, #44 rollout, #58 pricing.

## Decisions

- 2026-10-18: No CI/CD. `scripts/gate.ps1` run locally replaces it; #5 closed
  as not planned.
- 2026-10-18: No Visual Studio. The .NET CLI builds, runs and packs the app
  (see `CLAUDE.md`, Machine facts). #1 closed.
- 2026-10-18: The Store package depends on the Windows App Runtime 2 framework
  package, which the Store installs with the app; it is not self-contained.
  Debug builds run unpackaged for local checks.
- 2026-10-18: Architectures x64 and ARM64. x86 only if #2 shows real x86
  installs.
- 2026-10-18 (Sven): the orchestrator merges each PR itself after the review,
  the fix round and a green gate on the PR branch.
- 2026-10-18 (#3): solution `TenLapse.slnx`. Windows projects target
  `net10.0-windows10.0.26100.0`, minimum 10.0.17763.0; Core is `net10.0`. Each
  new project needs the same three platform mappings in the slnx (Any CPU maps
  Windows projects to x64), and the gate fails if a tracked csproj is missing
  from it. App root namespace is `TenLapse`; Core's is `TenLapse.Core`.
- 2026-10-18 (#3): Release is an MSIX that carries the .NET runtime
  (`SelfContained`, there is no Store framework package for .NET) and depends on
  the Windows App Runtime 2 framework package. Pack builds
  (`GenerateAppxPackageOnBuild=true`) do not treat MSBuild warnings as errors,
  because the uncoded `mspdbcmf.exe` warning cannot be exempted; every other
  build does.
- 2026-10-18 (#3): NuGet audit: NU1900 to NU1902 stay warnings, NU1903 (high)
  and NU1904 (critical) fail the build.
- 2026-10-18 (#3): tests never reference the App exe (NETSDK1151 against the
  self-contained Release App). View models live in a separate `net10.0`
  library, `src/TenLapse.Presentation`, created in #16.
- 2026-10-18 (#3): Debug is unpackaged, so it has no package identity:
  `ApplicationData.Current`, `Package.Current` and `StoreContext` throw there.
  Code that needs identity guards for it or has a path that works without it
  (#21 settings, #31 rating).
- 2026-10-18 (#27, widened 2026-10-19 by #29): one amber accent button per
  screen, as `DESIGN.md` says (Export; *Choose folder* on first launch via
  `AccentActionButtonStyle`, based on `ExportButtonStyle`; a rule test
  allow-lists the views). `ExportButtonStyle` carries a
  copy of Fluent's accent button template (WinUI 3 styles cannot hold
  resources), so a Fluent template change will not reach it. Fluent's own
  `AccentButtonStyle` and the `AccentButton*` keys stay untouched, so dialog
  primary buttons keep the system accent.
- 2026-10-18 (#27): `ThemeShadow.Receivers.Add` throws on Windows App SDK 2.5.1.
  Elevation is built with a Composition `DropShadow` from the `Shadow*` color
  tokens and the blur and offset numbers in `Layout.xaml` (#28, #24).
- 2026-10-18 (#27): a test fails on `DeepAmberIndicator*` outside the indicator
  override keys. A bespoke indicator (scrubber, playhead) must add its file to
  `_bespokeIndicatorFiles` in `tests/TenLapse.Tests/Design/AmberRuleTests.cs`.
- 2026-10-18 (#27): High contrast was never seen on screen here; it is covered
  by key parity, resolution and tests. #41 (test matrix) checks it for real.
- 2026-10-18 (#8, PR #65 review): encoding uses `IMFSinkWriter` through CsWin32
  on a D3D11 device created per export, not `MediaTranscoder` (drops frames on
  D3D surfaces, 8 GB on CPU buffers). Four pipeline requirements for #22 are
  on #22: cancel under 1 s, decode at the output size, frame-level failure
  context, encode-stage stall guard.
- 2026-10-18 (#16): `Project` is single-writer (the creating thread) and
  publishes an immutable `ProjectSnapshot` with a `Version`; readers on any
  thread take the snapshot. A replaced project is closed: mutations throw
  `ObjectDisposedException`, which an import treats as cancelled. Errors that
  must not crash go to `IErrorSink` (Presentation; #26 replaces the Trace
  implementation). Timing keeps each mode's own input; `EffectiveRate(count)`
  clamps and says so. `OutputResolution` is the short side. Defaults: 30 fps,
  16:9 Fill, 1080p H.264 High, capture-time sort. User-visible text in
  `ProjectText` as whole format strings.
- 2026-10-18 (#28): the shell is `Shell/ShellView` (a UserControl; adaptive
  states go on `ShellRoot`). Title bar is custom elements with `SetTitleBar`
  (caption buttons 32 px in the 40 px strip); title and command bar are one
  drag region with passthrough for controls. Mica is tinted with the `Mica`
  token (0.9 light, 0.95 dark) so it reads neutral, not blue. InfoBars go
  through `IInfoBarService` (`InfoBarQueue`: thread-safe, at most 5 waiting,
  identical requests coalesce, optional detail list behind View list), with
  UIA announcements. `FlyoutShadow` is disposable; overlays must dispose it.
  `AccessibilitySettings.HighContrastChanged` crashes unpackaged; use
  `UISettings.ColorValuesChanged`. Ctrl+N goes on `ShellRoot`.
- 2026-10-18 (#9 review): HEVC is offered only when an HEVC encoder works and
  an HEVC decoder is present, so Windows can play the file.
- 2026-10-18 (#9): the probe tests 30, 60 and 120 fps; a cold probe takes
  about 3.5 s in the background (H.264 4K120 refusals cost about 550 ms each),
  a cached load about 0.25 s. It runs once per adapter, driver, OS build,
  codec versions and Media build. #23 must never block on it; if the cold time
  matters, #23 can move 120 fps to an on-demand probe.
- 2026-10-18 (#26): commands are built only with `errors.AsyncCommand(...)`,
  `errors.Command(...)` or `GuardCancellableAsync` (a rule test bans raw
  `[RelayCommand]`/`RelayCommand` and unguarded `async void`). Every
  `DispatcherQueue` callback goes through `TryEnqueueGuarded` (a throwing raw
  callback kills the process). `OperationCanceledException` is silent only
  when the command's own token was cancelled. Log: one file per process per
  day in `LocalState\Logs` (packaged) or `%LOCALAPPDATA%\10Lapse\Logs`, 7 days,
  repeats collapsed, a reserve for Critical lines. *Copy diagnostics* redacts
  the profile path and user name. PRIVACY.md says the log may include file
  and folder names. Settings is a full page (Back, Escape, Alt+Left, mouse
  back); new cards go in its sections. The encoder cache file is
  `AppEnvironment.EncoderCacheFile()`.
- 2026-10-18 (#13 review): RAW+JPEG pairs with the same base name in one
  folder import once, keeping the JPEG (#12).
- 2026-10-19 (#12): `IPhotoImportSource` lives in Core; Media's
  `PhotoImporter` implements it; Presentation's `ImportViewModel` drives it.
  Import fills in two phases (list, then read metadata with 8 readers). OneDrive
  online-only files are not read silently: one question per import ("N photos
  are only in OneDrive. Download them now? About X GB."), default *Skip them*
  (`SkipReason.CloudOnly`). Junctions and symlinks are not followed (cloud
  reparse folders are). A RAW is dropped only when a readable non-RAW twin
  exists. Folders that will not open get their own InfoBar, not a skipped
  photo. `ApplyDefaultSort` only on a project that was empty and unsorted by
  the user during the import. Drops are refused while Settings is open.
- 2026-10-19 (Sven): icon direction A Five frames (#37). Design spec approved
  (#11 closed): `DESIGN.md`, the canvas, `Main.dc.html` and the motion spec are
  the spec. License stays all rights reserved; no `LICENSE` file. #29 ships
  without *Try a sample*; #30 stays open for later.

## Merged

- #6 PR #60: `PRIVACY.md` and `README.md`. Owner: point the Partner Center
  privacy URL at `PRIVACY.md`. License: all rights reserved (Sven,
  2026-10-19).
- #3 PR #61: tag `v2.1.1-legacy`, legacy UWP removed, new solution, gate
  (`solution`, `restore`, Debug `build`, `test`, Release `build`, about 19 s).
- #4 PR #62: Store identity `62394glaglasven.10Lapse` 3.0.0.0, Application Id
  `App` kept, `Windows.Desktop` 17763 to 26100, `runFullTrust` only.
  `pwsh -File scripts/pack.ps1` writes the unsigned x64 + ARM64 `.msixupload`
  (about 124 MB, 65 MB per architecture) to `dist/store` and checks the built
  identity and version. Bump the manifest `Version` for every submission. Owner
  notes for the first submission are on #40.
- #10 PR #63: `TenLapse.Core.Timing`. `Fraction` (reduced rational),
  `FrameTiming.FrameStart/FrameDuration/Duration` in 100 ns ticks with floor
  rounding, `FrameRates` (presets, NTSC snapping within 0.01, the three speed
  modes), `TimingFormat` ("24.7 s", "1 min 12 s", "1 h 02 min", "29.97 fps",
  "2 s per photo"), every user-visible word in `TimingWords`. A single comma is
  a decimal point.
- #27 PR #64: tokens in `src/TenLapse.App/Themes/` (`<Token>Color`/`<Token>Brush`
  for Light, Dark, HighContrast; `<Step>TextStyle` and `<Step>TabularTextStyle`;
  `Spacing*`, `Thickness*`, `Radius*`, `ControlHeight`; `Motion*Duration` and
  `Motion*KeySpline`; C# `TenLapse.Themes.MotionTokens`, `AnimationsEnabled`
  polled at each animation start). Per-control overrides put the indicator
  color on progress, slider, toggle, check box, radio and ProgressRing, with
  hover and pressed tints. Debug gallery: `TenLapse.App.exe --gallery
  [--theme=light|dark] [--scroll=PX]`.
- #16 PR #66: `TenLapse.Core.Projects` (`Project`, `ProjectSnapshot`, `Frame`,
  `SkippedFile`, settings records, `ProjectChangedEventArgs` with a frame delta,
  `AddFrames`/`InsertFrames`/`RemoveFrames`/`ReorderFrames`/`UpdateFrames`,
  `MarkExported(version)`), `src/TenLapse.Presentation` (`ShellViewModel`,
  `ProjectViewModel`, `ProjectText`, `IDialogService`, `DialogQueue`,
  `IErrorSink`), App `ContentDialogService` and `TraceErrorSink`, manual
  composition root in `App.xaml.cs`.
- #8 PR #65: `TenLapse.Media.Export` (`VideoExporter.ExportAsync`, callable
  from any thread; `VideoDevice` per export; `SinkWriterEncoder` behind the
  internal `IVideoEncoder` seam; `IFrameSource`, `PhotoFileSource`;
  `VideoExportFailure` categories classified by HRESULT; output deleted on
  failure or cancel), `TenLapse.Media.Inspection.VideoFileReader` (exact sample
  times, first-sample check), colour tags (BT.709 above 576 lines, BT.601
  below, studio range), no B-frames. `tools/TenLapse.Spike` harness. 4K from
  memory: 155 fps, 151 MB peak on NVENC. MP4 timestamps: exact when rate × 1000
  is whole, else 0 to one timescale unit late (#32). About 34 handles per
  export leak inside the NVIDIA encoder MFT, not fixable on our side.
- #28 PR #67: app shell (title bar on tinted Mica, command bar with *New
  project* once there is work, Empty/Editing states with stage 920×555 at
  1280×800, inspector 320, filmstrip row), InfoBar host, `FlyoutShadow`,
  `TintedMicaBackdrop`, minimum window 640×480. Debug switches
  `--sample-frames=N`, `--infobar`, `--theme=light|dark`.
- #9 PR #68: `TenLapse.Media.Encoding` (`EncoderProbe.LoadOrProbeAsync(cachePath)`,
  `EncoderCapabilities` with `Supports`/`MaxShortSide`/summary text,
  `EncoderCatalog`, JSON cache, 8 s per test encode, transient results not
  cached). HEVC export (Main profile; below 1 fps the nominal rate is a whole
  multiple ≥ 1 fps with frame-rate conversion off, after a software-encoder
  crash). This PC: H.264 4K at 30/60, 1440p at 120; HEVC 4K at all three.
- #26 PR #69: `TenLapse.Core.Diagnostics.RollingFileLog`, Presentation
  `ErrorPath`, `ErrorPresenter`, `ErrorMessages`, `Diagnostics`,
  `DiagnosticsRedaction`; App global handlers, `TryEnqueueGuarded`,
  `VideoExportErrorMapper`, `EncoderDiagnostics`, Settings page with *Copy
  diagnostics*. Debug `--crash=command|task|ui|unhandled`.
- #13 PR #70: `TenLapse.Media.Decoding` (`PhotoDecoder.ReadInfoAsync` header
  plus end check, `DecodeAsync` to cover size with EXIF orientation, files read
  through a shared handle and closed before decoding so nothing stays mapped;
  sRGB profiles skip colour conversion, others convert after scaling;
  truncated JPEG/PNG/GIF are `Truncated` at import; `PhotoCodecs` lists
  installed ∩ known photo extensions; `KnownPhotoFormats` with container and
  codec Store IDs for #14). `PhotoFileSource` decodes at the output's cover
  size: 4K export from 24 MP JPEGs about 25–28 fps, 540–640 MB peak.
  Presentation `SkippedFileText` and `SkippedFilesInfoBar` for #12. A JPEG
  missing only its EOI marker is rejected as truncated (documented).
- #33 PR #72: `tests/TenLapse.Tests/Fixtures/Photos/` (20 generated files,
  288 KB; `make_fixtures.py`, run with `python -P`; README with expected
  outcomes), `PhotoFixtureCatalogue`, `RuntimeFixtures` (100 MP, locked,
  deleted, long Unicode path, read-only). #12's import test must skip exactly
  `empty.jpg`, `truncated.jpg`, `no-eoi.jpg`, `text.heic` and import the other
  16.
- #15 PR #71: `TenLapse.Core.Ordering` (`NaturalNameComparer` exact against
  `StrCmpLogicalW` for ASCII, Latin-1 and Latin Extended-A, names normalised to
  Form C; `CaptureTimeReader` with sub-seconds and offsets, never throws;
  `FrameSorter` sorting by UTC only when every timed frame has an offset;
  `FrameGaps.Detect(frames, mode)` with timestamp names excluded from
  numbering; `Project` extensions `ApplySort`, `ApplyDefaultSort` (empty
  project only), `Resort` (keeps Manual order), `ReorderManually`). New
  `Frame.CaptureUtcOffset`. RAW and CR3 capture times are unverified until #30.
- #12 PR #73: import from *Add photos* (*Choose folder…*, *Choose photos…*,
  *Include subfolders*), and drops on the window (asks to add or start new on
  a non-empty project). 5,000 photos: first frames about 0.1 s, done about
  1.9 s, UI steps under 30 ms. `Project.RemoveFramesAndSkip` (one commit),
  `ProjectViewModel.RemoveResource`, `IDialogService.ChooseAsync`,
  `IUiDispatcher`, `IPhotoPicker`, `IImportFolderMemory` (session only; #21
  persists it). The import progress sits in the command bar with Cancel.
- #22 PR #74: `ProjectExporter.ExportAsync(snapshot, path, progress, ct)`
  through `FrameRenderer.Draw` (the call preview must use). Each export writes
  its own hidden `<name>.<8 hex>.partial`, journaled in
  `unfinished-exports.txt` and deleted at next launch if left. A target held
  by another app past 5 s: the video is saved as `Name (2).mp4` and
  `ProjectExportResult.TargetInUse` says so. H.264 and HEVC below 1 fps use a
  whole-fps nominal rate with scaled bitrate. Decode defaults 4 ahead and 3
  threads on an iGPU or 8 GB RAM or less, else 8 and 6. New failures
  `FrameUnreadable`, `DrawingFailed`, `EncoderStuck` (restart 10Lapse).
  Real-encoder tests run in one serial collection. #23 uses `ExportBitrate`;
  #25 takes the power request and awaits `ExportCleanup.WhenAllDone()` on close.
- #29 PR #75: `Shell/FirstLaunchView` in the shell's `EmptyRegion`: the
  canvas copy (length from `FrameTiming`), *Choose folder* (amber accent) and
  *Choose photos* on #12's commands, the privacy line, no sample button (#30
  deferred). Daybreak, a Win2D 20 s loop (600 frames at 30 fps) driven by
  `DaybreakPlayback` (Run, Still, Hidden: stops when hidden, inactive,
  minimized, narrow, animations off; hidden in high contrast); colours from
  `Daybreak*` tokens via `DaybreakPalette`. `DragVerdict` shows the drag look
  only once the count is known. `TimingFormat.Timecode` and `TextBalance` in
  Core/Presentation. Launch focus on *Choose folder* uses `FocusState.Pointer`
  so no ring shows until the keyboard is used (#38 to confirm). Debug
  switches `--drag-over`, `--reduce-motion`.
