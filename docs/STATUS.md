# 10Lapse 3.0 status

Updated 2026-10-19. Roadmap issue: #59. Working agreement: `CLAUDE.md`.

## Now

2026-10-19: #100 (#35 motion) and #102 (#101 timing bounds) merged. In flight: #39 strings. Then #38 accessibility (alone).

## Waves

Each wave runs at most two issues at once, only when they touch disjoint
areas. A wave starts when everything it depends on is merged. Model: **O** is
Opus 5.5, **S** is Sonnet 5.5.

Waves 0 to 16 are done; every issue in them is under Merged below. #90
(start-up crash) stays open after its hardening in PR #91.

| Wave | Issue | Model | Area | Status |
|---|---|---|---|---|
| 17 | #39 Strings to resource files | O | every view (Opus: resw/resx split) | in progress |
| 17 | #101 Flaky timing lower bound (bug) | S | `tests/` timing asserts | merged #102 |
| 18 | #38 Accessibility pass | O | every view, alone | |

After wave 18, 3.0 is code-complete. Milestones 4 (private beta) and 5
(relaunch) are mostly owner work; milestone 6 (3.1 creative tools) gets its
own waves once 3.0 ships.

## Owner items (Sven)

Nothing blocks the build loop. Everything below waits until release is near
(Sven, 2026-10-19).

- Before the first 3.0 submission, one Partner Center visit: the #7
  description line, and the privacy URL pointed at `PRIVACY.md` on GitHub.
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
- 2026-10-19 (Sven, #2): Windows 11 only (minimum 10.0.22000.0, #96), x64 and
  ARM64, no x86; Windows 10 devices keep 2.1.1. English only in 3.0;
  translations come later.
- 2026-10-18 (Sven): the orchestrator merges each PR itself after the review,
  the fix round and a green gate on the PR branch.
- 2026-10-18 (#3): solution `TenLapse.slnx`. Windows projects target
  `net10.0-windows10.0.26100.0`, minimum 10.0.22000.0 (#96); Core is `net10.0`. Each
  new project needs the same three platform mappings in the slnx (Any CPU maps
  Windows projects to x64), and the gate fails if a tracked csproj is missing
  from it. App root namespace is `TenLapse`; Core's is `TenLapse.Core`.
- 2026-10-18 (#3): Release MSIX is self-contained for .NET and depends on the
  Windows App Runtime 2 package. Pack builds allow warnings (`mspdbcmf.exe`);
  every other build has warnings as errors. NuGet audit fails on high and
  critical (NU1903, NU1904).
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
- 2026-10-18 (#16): `Project` is single-writer and publishes an immutable
  `ProjectSnapshot`; readers on any thread use the snapshot. A replaced project
  is closed (mutations throw `ObjectDisposedException`). `OutputResolution` is
  the short side. Defaults: 30 fps, 16:9 Fill, 1080p H.264 High, capture-time
  sort. User-visible text lives in `ProjectText` as whole format strings.
- 2026-10-18 (#28): `Shell/ShellView`, adaptive states on `ShellRoot`, custom
  title bar with one drag region. Mica tinted with the `Mica` token. InfoBars
  through `InfoBarQueue` (thread-safe, 5 waiting, coalesced, one action or a
  detail list). `AccessibilitySettings.HighContrastChanged` crashes
  unpackaged; use `UISettings.ColorValuesChanged`.
- 2026-10-18 (#9 review): HEVC is offered only when an HEVC encoder works and
  an HEVC decoder is present, so Windows can play the file.
- 2026-10-18 (#9): the encoder probe tests 30, 60 and 120 fps (cold about
  4 s in the background, cached about 0.25 s), once per adapter, driver, OS
  build, codec versions and Media build.
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
- 2026-10-19 (#34): the 1 GB export memory target applies on PCs with 8 GB RAM
  or less or an iGPU (decode 4 ahead, 3 threads: 932 MB peak private at 24 MP
  to 4K, thin margin). Larger PCs use 8 and 6 for speed (about 1.3 GB).
- 2026-10-19 (#31): the rating ask comes 4 s after the second successful
  export's finished state, once per package version, never after a failure or
  cancel; a failed ask is log-only (the one exception to never silent, since the
  user did not ask). Reset to defaults keeps the count. New settings fields need
  no schema bump. *Send feedback* opens GitHub issues, *Rate 10Lapse* the Store.
- 2026-10-19 (#2): same listing confirmed from the acquisitions and usage
  numbers Sven posted on #2 (#2 closed).
- 2026-10-19 (#35, PR #100): every timing and distance lives in
  `Presentation/Motion/MotionSpec.cs`, every transition in `MotionCatalogue`;
  animate only through `MotionTokens.Plan` (the one reduced-motion decision;
  `MotionRuleTests` bans literal times and curves elsewhere). The spec has no
  thumbnail-to-preview flight, so a thumbnail click stays an instant seek; the
  Settings cards' stagger is prototype-only and not built. InfoBars wait for a
  running transition (`MotionQuiet`). The shell pins its own `RequestedTheme`
  to Windows' mode (Default in high contrast) so the theme can crossfade; read
  `ActualTheme`, not `Application.RequestedTheme`, and a new swap chain or
  media element showing photos joins `ShellView.PhotosOnScreen`. One exception
  to "preview and export draw through the same code": during a 400 ms reframe
  the preview draws the photo by hand with geometry from `FrameRenderer.Layout`.
- 2026-10-19 (#90, PR #91): every framework callback (`AppWindow.Closing`,
  `Changed`, `Destroying`, backdrop, lambda handlers) runs through `RunLogged`;
  `FrameworkCallbackRuleTests` catches lambda and method-group handlers.
  `OnClosing` is one `RunLogged` call into `WindowCloseGate`: a throw logs,
  stops a running export and lets the window close. A Mica failure is log-only
  (the flat fallback is the documented look; it retries on the next connection).
  The encoder probe starts after the first frame (`FirstFrameRendered`, or
  10 s): cold, the first frame comes about 180 ms sooner; warm, the panel
  settles about 0.4 s later. `StartupTrace` logs start-up phases until the
  first frame and the applied probe answer. Launch-crash counts use
  `scripts/launch-test.ps1` (`-Plain` or under `tools/TenLapse.CrashCatcher`).
  Not reproduced in 980 launches; #90 stays open until it is seen or the beta
  is clean.
- 2026-10-19 (#37): the 16, 20, 24 and 32 px icons are full-bleed pixel-grid
  variants, not the canvas's padded tile, so they stay crisp. The splash is dark
  Mica `#1F1E1D` in both themes. The bundle keeps every scale in the main
  package (`AppxBundleAutoResourcePackageQualifiers` without Scale).
- 2026-10-19 (Sven): icon direction A Five frames (#37). Design spec approved
  (#11 closed): `DESIGN.md`, the canvas, `Main.dc.html` and the motion spec are
  the spec. License stays all rights reserved; no `LICENSE` file. #29 ships
  without *Try a sample*; #30 stays open for later.

## Merged

Details are in each PR's "What the next issues should know".

- #6 PR #60 `PRIVACY.md`, `README.md` (all rights reserved). #3 PR #61 new
  solution and gate. #4 PR #62 Store identity 3.0.0.0, `scripts/pack.ps1`.
- #10 PR #63 `Core.Timing` (`Fraction`, `FrameTiming`, `FrameRates`,
  `TimingFormat`). #27 PR #64 tokens and themes, `--gallery`. #16 PR #66
  project model and view models.
- #8 PR #65 and #22 PR #74: export through `IMFSinkWriter` and
  `ProjectExporter.ExportAsync`, `FrameRenderer.Draw`; per-export hidden
  `.partial`, journaled; a held target saves `Name (2).mp4`; below 1 fps a
  whole-fps nominal rate. NVIDIA leaks about 34 handles per export (not ours).
- #28 PR #67 shell. #9 PR #68 encoder probe (`EncoderCapabilities`). #26 PR #69
  error path, log, Settings, *Copy diagnostics*.
- #13 PR #70 WIC decode with EXIF orientation. #33 PR #72 photo fixtures. #15
  PR #71 frame order (`NaturalNameComparer`, `FrameGaps`). #12 PR #73 import
  (5,000 photos in about 1.9 s).
- #29 PR #75 first launch (Daybreak, `DragVerdict`; Debug `--drag-over`,
  `--reduce-motion`). #23 PR #76, #18 PR #80: output and frame panels;
  `Project.Output` is the preference, `OutputPlan.Resolve` the effective output
  (export with it, size included). #17 PR #78 timing panel and
  `SegmentedControl` (dot or comma decimal; snaps and clamps say so).
- #14 PR #79 missing-extension InfoBars with Store links (restart after
  installing; `--missing-codec=`).
- #19 PR #77 preview: render thread, `PlaybackClock`, `ProxyCache` (1280 px,
  2 GB, 30-day sweep, `LocalCache\Proxies` or `%LOCALAPPDATA%\10Lapse\Proxies`),
  `FrameImageCache` 192 MB. Debug `--sample-frames` writes real photos: pass
  `--sample-folder` under your branch folder and `--sample-clean`.
- #32 PR #81 golden render tests (exact frame times through `RecordingEncoder`,
  preview vs export geometry within 1 px; MP4 header durations one unit short at
  24, 29.97, 60 fps, accepted). `MediaSkips.CannotRunHere`.
- #20 PR #82 filmstrip (thumbnails proxy first, `FrameHistory` undo of removals
  and reorders, Delete and Ctrl+Z only with the strip or nothing focused, Space
  always plays; `--filmstrip-bench`).
- #21 PR #83 `settings.json` (versioned, never throws, 256 KB cap, atomic),
  *Reset to defaults*; `--settings-folder=`.
- #84 PR #85 bounded capture-time readers replace MetadataExtractor. #86 PR #87
  no process-wide memory deltas in tests.
- #34 PR #89 `scripts/bench.ps1`, `docs/benchmarks.md`; `--import-bench=`.
- #24 PR #88 export panel, film gate, premiere of the real MP4, Export button
  disabled at 0.6 outside Editing, `ShellViewModel.Edits` blocks edits during an
  export, close asks and waits up to 2 s, taskbar progress, notifications only
  packaged or with Debug `--notifications`, `Started`/`Ended` hooks; `--export-to=`.
- #25 PR #92: `IKeepAwake` power request (system required, not display) held
  by `ExportViewModel` from `Begin` to `End`; minimized exports run at full
  speed and the hidden preview is not seeked.
- #31 PR #93: `RatingPrompt`, `StoreRatingService` (no-op unpackaged), Settings
  *Feedback* card; Debug `--fake-rating`. Verify the real dialog in #40.
- #36 PR #94: below 1,000 DIPs the inspector is a 260 px panel under the stage
  with Timing/Frame/Output tabs (`ShellLayout`), selects in a row, floor
  152 px in short windows; export and finished use two columns and hide the
  tabs (deviations from the canvas, kept so the actions stay visible).
- #37 PR #95: `Assets/Source/TenLapse*.svg` and the Spike `assets` generator
  (deterministic) render 78 PNGs and `TenLapse.ico`; all tiles, target sizes and
  a 620x300 splash at scales 100 to 400; window icon via a guarded `SetIcon`.
- #90 PR #91 (refs only): start-up hardening, `StartupTrace`,
  `WindowCloseGate`, probe after the first frame, `scripts/launch-test.ps1`,
  `tools/TenLapse.CrashCatcher`; Debug `--log-folder=`, `--encoder-cache=`.
- #96 PR #97: minimum OS 10.0.22000.0 (`TenLapseWindowsMinVersion`, manifest,
  README). The Mica fallback stays; the stale "Windows 10" comment in
  `Shell/ShellView.xaml` is left for #35.
- #98 PR #99: cache tests wait for state, disposals and events, which the
  caches record under `_gate` and then free and raise after it; positive state
  is waited for, negative checks keep a short quiet window.
- #35 PR #100: motion system (26 transitions, `ScreenMotion`, `ThemeCrossfade`,
  arrival develop and cascade, retime and reframe, InfoBar hold);
  `scripts/motion-check.ps1`; Debug `--motion-log`, `--motion-import=`,
  `--theme-flip-after=`, `--motion-infobar-after=`.
- #101 PR #102: no elapsed-time floor within a timer tick (about 15.6 ms) of a
  real timeout; a timeout is proved by its effect (state, log line), ceilings
  are generous. The "cancel within 1 s" bounds stay (product promise). If a
  timing test flakes again, look first at `ImportPerformanceTests` (UI
  round-trip ceilings) and the `EncoderProbeTests` cached-load ratio.
