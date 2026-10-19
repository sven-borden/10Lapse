# 10Lapse 3.0 status

Updated 2026-10-19. Roadmap issue: #59. Working agreement: `CLAUDE.md`.

## Now

2026-10-19: in flight: PR #88 (#24 export panel) in review, PR #89 (#34 bench) fix round, #90 (new bug: intermittent start-up crash 0xC000027B) building. Queued after #88: #31, #25, #36.

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
| 9 | #19 Preview with proxy cache | O | preview view and cache | merged #77 |
| 9 | #23 Output panel | S | output inspector section | merged #76 |
| 10 | #20 Filmstrip | O | filmstrip view | merged #82 |
| 10 | #17 Timing panel | S | timing inspector section | merged #78 |
| 11 | #24 Export panel and finished state | O | export panel | PR #88, in review |
| 11 | #18 Frame panel | S | frame inspector section | merged #80 |
| 12 | #32 Golden render test | O | `tests/` golden | merged #81 |
| 12 | #25 Export while minimized, keep awake | S | export power request | |
| 13 | #21 Remember settings | S | settings persistence | merged #83 |
| 13 | #14 HEIC and RAW extension prompts | S | import InfoBars | merged #79 |
| 13 | #84 Capture-time memory bound (bug) | O | `Core/Ordering` | merged #85 |
| 13 | #86 Flaky memory assertion (bug) | S | `tests/` | merged #87 |
| 13 | #90 Start-up crash 0xC000027B (bug) | O | App start-up | building |
| 14 | #31 Rating prompt | S | Store rating | |
| 14 | #34 Benchmark | S | `tests/` bench, `docs/benchmarks.md` | PR #89, fix round |
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
- 2026-10-19 (Sven): icon direction A Five frames (#37). Design spec approved
  (#11 closed): `DESIGN.md`, the canvas, `Main.dc.html` and the motion spec are
  the spec. License stays all rights reserved; no `LICENSE` file. #29 ships
  without *Try a sample*; #30 stays open for later.

## Merged

- #6 PR #60: `PRIVACY.md` and `README.md`. Owner: point the Partner Center
  privacy URL at `PRIVACY.md`. License: all rights reserved (Sven,
  2026-10-19).
- #3 PR #61: tag `v2.1.1-legacy`, new solution, `scripts/gate.ps1`.
- #4 PR #62: Store identity 3.0.0.0, `runFullTrust` only; `scripts/pack.ps1`
  writes the unsigned x64 + ARM64 `.msixupload` to `dist/store`. Bump
  `Version` per submission; first-submission notes on #40.
- #10 PR #63: `Core.Timing`: `Fraction`, `FrameTiming` (100 ns ticks, floor),
  `FrameRates`, `TimingFormat`, `TimingWords`.
- #27 PR #64: tokens in `App/Themes/` (Light, Dark, HighContrast), text
  styles, spacing, motion tokens, control overrides. `--gallery`.
- #16 PR #66: `Core.Projects` (`Project`, `ProjectSnapshot`, `Frame`,
  settings), Presentation view models, dialogs, manual composition root.
- #8 PR #65: `Media.Export` through `IMFSinkWriter`, `VideoFileReader`, colour
  tags, no B-frames, `tools/TenLapse.Spike`. About 34 handles per export leak
  inside the NVIDIA encoder MFT (not ours).
- #28 PR #67: shell (tinted Mica title bar, command bar, Empty/Editing states,
  inspector 320, filmstrip row), InfoBar host, `FlyoutShadow`.
- #9 PR #68: `Media.Encoding` (`EncoderProbe.LoadOrProbeAsync`,
  `EncoderCapabilities`, JSON cache). This PC: H.264 4K at 30/60, HEVC 4K.
- #26 PR #69: `RollingFileLog`, `ErrorPath`, `ErrorPresenter`, global
  handlers, `TryEnqueueGuarded`, Settings with *Copy diagnostics*.
- #13 PR #70: `Media.Decoding` (`PhotoDecoder`, EXIF orientation, files closed
  before decode, sRGB skips colour conversion, truncation detected; JPEG
  missing only EOI is rejected). 4K from 24 MP JPEGs about 25–28 fps.
- #33 PR #72: `tests/.../Fixtures/Photos/` (20 files, `make_fixtures.py`,
  README), `PhotoFixtureCatalogue`, `RuntimeFixtures`.
- #15 PR #71: `Core.Ordering` (`NaturalNameComparer` = `StrCmpLogicalW`,
  `CaptureTimeReader`, UTC sort only when every frame has an offset,
  `FrameGaps.Detect`, `ReorderManually`). RAW/CR3 capture times unverified.
- #12 PR #73: import (folder, photos, drop); 5,000 photos done in about 1.9 s,
  UI steps under 30 ms. `IUiDispatcher`, `IPhotoPicker`, `IImportFolderMemory`
  (session only; #21 persists it).
- #22 PR #74: `ProjectExporter.ExportAsync` through `FrameRenderer.Draw`.
  Hidden per-export `.partial` journaled and cleaned at next launch; a held
  target saves `Name (2).mp4`. Below 1 fps: whole-fps nominal rate. Real-encoder
  tests run serially. #25 takes the power request, awaits
  `ExportCleanup.WhenAllDone()` on close.
- #29 PR #75: `Shell/FirstLaunchView` (canvas copy, *Choose folder* amber,
  Daybreak loop via `DaybreakPlayback`, `DragVerdict`). Launch focus uses
  `FocusState.Pointer` (#38 to confirm). Debug `--drag-over`, `--reduce-motion`.
- #23 PR #76: `Shell/OutputPanel` + `OutputPanelViewModel`. Unsupported sizes
  hidden; HEVC only when available; probe at launch. `Project.Output` is the
  user's preference and only a user pick writes it (#80): `OutputPanelViewModel.
  EffectiveOutput` (`OutputPlan.Resolve`) is what is shown and what #24 must
  export with, size included (`OutputSize.For(effective.Resolution, ...)`, not
  `snapshot.VideoSize`). `CanExport` = an effective output exists; #24 must also
  require frames. `ExportBitrate` is in `Core.Projects`.
- #17 PR #78: `Shell/TimingPanel`, `Shell/SegmentedControl`; inspector is a
  scrolling stack; `ShellView` owns and disposes the panel view models.
  Separator rules: dot or comma is decimal; one separator + three digits is
  refused (except below 1 or NTSC); every snap, clamp and rounding says so.
  Panel text in the user's culture; Core invariant by default.
- #14 PR #79: one InfoBar per missing extension with a Store button (HEIF
  `9PMMSR1CGPWG`, Raw `9NCTDW2W1BH8`, HEVC paid `9NMZLZ57R3T7`).
  `CodecExtensionPrompts.Plan`; installed codecs (software decoders) never
  prompt. Wording says restart after installing. Debug `--missing-codec=`.
- #19 PR #77: preview on a render thread through `FrameRenderer.Draw` into a
  swap chain; `FrameTiming.FrameAt`, `PlaybackClock` (drops, never drifts).
  `ProxyCache` (1280 px JPEG, no metadata, 2 GB or a tenth of free space, LRU,
  30-day sweep, `LocalCache\Proxies` or `%LOCALAPPDATA%\10Lapse\Proxies`),
  `FrameImageCache` (192 MB, device generations). 5,000 photos: 0 dropped at
  30 fps, seek to picture about 10 ms, pass peak about 0.7 GB. Playhead
  follows the frame id; paused frame re-checked on disk. Pauses when
  minimized. Keys: Space (no repeat), arrows, Shift+arrows, Home/End.
  PRIVACY.md describes the copies. Debug `--sample-frames` writes real photos
  (pass `--sample-folder` under your branch folder, `--sample-clean`),
  `--cache-folder`, `--preview-bench`.
- #18 PR #80: `Shell/FramePanel` between Timing and Output (shapes Match
  photos, 16:9, 9:16, 1:1, 4:5, 4:3; Fit/Fill; `SegmentedControl.ItemNames`).
  Portrait hint (more than half portrait in 16:9 or 4:3) with *Use 9:16* and
  *Dismiss*, never automatic, kept in the view model only. `OutputPlan.Resolve`
  gives the effective output; *Use these settings* stores a shown replacement.
  `FrameText.NoSize` when Match photos has no encodable size. Debug `--frame=`.
- #32 PR #81: `tests/.../Golden/` exports 120 coded frames at 24, 25, 29.97,
  30, 60, 2 and 0.5 fps (H.264, HEVC at 0.5) and checks every frame time
  through `RecordingEncoder` (exact `FrameStart`/`FrameDuration`), the file
  read-back, and the decoded frame number; every aspect x fit against the
  preview's path (`PreviewImageSource` + `FrameRenderer.Draw`), edges within
  1 px. Adds about 20 s to the gate. MP4 header durations are one timescale
  unit short at 24, 29.97 and 60 fps (the MP4 sink; cosmetic in Explorer,
  accepted, noted on #41). `MediaSkips.CannotRunHere` skips only when no
  encoder is listed or no device works. `TENLAPSE_GOLDEN_SOFTWARE=1` forces
  the software encoder.
- #21 PR #83: `Core/Settings` (`UserSettings`, versioned `settings.json` in
  LocalState or `%LOCALAPPDATA%\10Lapse`, `SettingsLocation`), `RememberedSettings`
  (new projects start from it, 600 ms debounce, bounded flush on close, stores
  the output preference never the effective one, import and export folders).
  Loading never throws: bad fields fall back one by one, files over 256 KB are
  ignored. Atomic unique-temp writes. *Reset to defaults* in a Settings
  *Defaults* section. The portrait-hint dismissal is not stored. PRIVACY.md
  updated (effective 2026-10-19). Debug `--settings-folder=`.
- #20 PR #82: `Filmstrip/` (ItemsRepeater, `FilmstripViewModel` from project
  deltas, `ThumbnailCache` 48 MB: proxy first, embedded EXIF only before a
  proxy exists, then a small decode). `FrameHistory` undo (100 edits, removals
  and reorders only; undo in a sorted mode re-sorts). User-removed frames do not
  make gap markers. Edits pause playback and act on the grabbed frames. Delete
  and Ctrl+Z only with the strip or nothing focused. Plain Space plays or
  pauses everywhere; Shift+arrows extend the selection on thumbnails (DESIGN.md
  updated: thumbnails are list items). Gap markers vanish in Manual order.
  #24 uses `SetPending(snapshot, first)` / `ClearPending()` (dims the picture
  only). Debug `--filmstrip-bench`, `--filmstrip-remove=`.
- #84 PR #85: MetadataExtractor removed. `Core/Ordering` reads capture times
  with bounded walkers (`ExifContainers`, `ExifTimeTags`, `ByteWindow`): JPEG,
  TIFF/RAW, PNG (`eXIf` and raw-profile text chunks), WebP, HEIF/AVIF, CR3,
  RAF. Pooled 64 KB buffers, at most 4 Exif blocks, every count checked
  against the bytes; worst read about 0.5 ms and a few KB. Matches the old
  reader on 1,289+ real files. Real fixtures (MetadataExtractor samples,
  raw.pixls.us CC0) with licences in `tests/.../Ordering/Fixtures/README.md`.
- #86 PR #87: no process-wide memory deltas in tests (parallel classes skew
  them); count saves and changes instead.
