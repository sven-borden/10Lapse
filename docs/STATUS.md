# 10Lapse 3.0 status

Updated 2026-10-18. Roadmap issue: #59. Working agreement: `CLAUDE.md`.

## Now

Running: PR #70 fix round (#13) and #15 (wave 6).

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
| 5 | #13 Photo formats through WIC | O | `TenLapse.Media/Decoding` | PR #70, fix round |
| 6 | #26 One error path | O | App startup, `Core/Diagnostics` | merged #69 |
| 6 | #15 Frame order | S | `TenLapse.Core/Ordering` | running |
| 7 | #12 Import folder, files, drag and drop | O | `Core/Import`, App import commands | |
| 7 | #33 Broken-photo fixtures | S | `tests/` fixtures | |
| 8 | #22 Export pipeline | O | `TenLapse.Media/Export` | |
| 8 | #29 First launch drop zone | O | App empty state view | |
| 9 | #19 Preview with proxy cache | O | preview view and cache | |
| 9 | #23 Output panel | S | output inspector section | |
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
| 15 | #37 Icon and tile assets | S | `Assets/` (needs the icon choice) | |
| 16 | #35 Motion system | O | every view, alone | |
| 17 | #39 Strings to resource files | S | every view, alone | |
| 18 | #38 Accessibility pass | O | every view, alone | |

After wave 18, 3.0 is code-complete. Milestones 4 (private beta) and 5
(relaunch) are mostly owner work; milestone 6 (3.1 creative tools) gets its
own waves once 3.0 ships.

## Owner items (Sven)

- #2 Partner Center numbers: decides x86 and the first translations.
- #7 Fix the Store description line now (listing only, no package).
- #11 Sign off the design spec. Until then `DESIGN.md` and
  `docs/design/canvas/` are treated as the approved spec.
- #30 Sample sequence: 60 to 120 frames, shot by Sven or CC0. Until it exists,
  the first-launch screen has no *Try a sample* button.
- #37 needs the icon direction before wave 15: A Five frames, B Sun path, or
  C Stacked frames (`docs/design/canvas/foundations/Icon.dc.html`).
- Later: #40 flight, #42 listing, #43 trailer, #44 rollout, #58 pricing.

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
- 2026-10-18 (#27): `ExportButtonStyle` is the only amber button. It carries a
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

## Merged

- #6 PR #60: `PRIVACY.md` and `README.md`. Owner: point the Partner Center
  privacy URL at `PRIVACY.md`; choose a license (none yet, all rights
  reserved).
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
