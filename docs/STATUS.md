# 10Lapse 3.0 status

Updated 2026-10-20. Roadmap: the pinned #59 and the milestones. Working agreement: `CLAUDE.md`.

## Now

2026-10-20: **Translations before the flight (Sven): waves 23-24**, #118 it (PR #127) and #119 pt-BR (PR #128) in review and fix. Merged: #115 fr (PR #123), #116 de (PR #122), #117 es (PR #126), #124 (PR #125). Wave 21 merged (#113 PR #120, #114 PR #121). Then #117 to #119. Once #119 merges, the orchestrator runs `scripts/pack.ps1` for the flight; a `v3.0.0` tag waits for the relaunch (#44) and Sven's OK. Sven's release steps wait for them: #7, the flight #40 (`docs/release/flight.md`) with #41 (`docs/test-matrix.md`) and the #90 watch, then #42 (`docs/store/listing.md`), #43, #44. Release PRs say Refs; those issues close once Sven's part is done.

## Waves

Each wave runs at most two issues at once, only when they touch disjoint
areas. A wave starts when everything it depends on is merged. Model: **O** is
Opus 5.5, **S** is Sonnet 5.5.

Waves 0 to 20 are done; every issue in them is under Merged below (#40, #41
and #42 stay open for Sven's part). Milestones 4 (private beta) and 5
(relaunch) are owner work, except the translations in milestone 4:

- Wave 21: #113 plumbing (O) and #114 translatable English (O), disjoint
  files (each issue lists what the other owns).
- Wave 22: #115 fr and #116 de (O). Wave 23: #117 es and #118 it (O).
  Wave 24: #119 pt-BR (O). Language PRs add new files only.

Milestone 6 (3.1) gets its own waves once 3.0
ships: #45, #46, #47 and #48 all draw in `FrameRenderer`, so they run one at
a time.

## Owner items (Sven)

Nothing blocks the build loop. Everything below waits until release is near
(Sven, 2026-10-19).

- #7, before the flight: the 2.1.1 description line and the privacy URL.
- Release: #40 flight, #41 test pass, #42 listing (the sort title "Ten Lapse"
  is his call), #43 trailer, #44 rollout. Later: #58 pricing.
- Translations: a Partner Center listing per language from
  `docs/store/listing.<lang>.md` (#115-#119); a native read where he can.
- #30 sample sequence (3.1): Sven picks the photos; *Try a sample* comes back
  with them.

## Decisions

- 2026-10-18: No CI/CD. `scripts/gate.ps1` run locally replaces it; #5 closed
  as not planned.
- 2026-10-18: No Visual Studio. The .NET CLI builds, runs and packs the app
  (see `CLAUDE.md`, Machine facts). #1 closed.
- 2026-10-18: The Store package depends on the Windows App Runtime 2 framework
  package, which the Store installs with the app; it is not self-contained.
  Debug builds run unpackaged for local checks.
- 2026-10-19 (Sven, #2): Windows 11 only (minimum 10.0.22000.0, #96), x64 and
  ARM64, no x86; Windows 10 devices keep 2.1.1.
- 2026-10-20 (Sven, reverses "English only in 3.0" from #2): 3.0 ships in
  en-US, fr, de, es, it and pt-BR, built before the flight. Neutral cultures
  serve every region; pt-BR is regional (pt-PT maps to it). The 2026-10-20
  audit put the work in #113 (runtime, packaging, tests) and #114 (plurals,
  format strings, culture-aware numbers, layout). Translations use Opus: the
  text is the product in those markets and no native reviewer is on hand.
- 2026-10-20 (#113, PR #120): the bundle splits by `DXFeatureLevel` only;
  every language stays in the main package (split by `Language`, the fr
  resource package never reached the bundle). `pack.ps1` checks the manifest
  languages, the satellites and the PRI description (UTF-8 and UTF-16 bytes).
  The UI culture comes from `AppLanguage.Apply`, first in `App()`, through
  `ResourceLanguages.Match`; `CurrentCulture` stays the user's.
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
- 2026-10-19 (#12): `IPhotoImportSource` (Core), `PhotoImporter` (Media),
  `ImportViewModel` (Presentation); two phases (list, then metadata with 8
  readers). OneDrive online-only files: one question per import, default
  *Skip them*. Junctions and symlinks are not followed. A RAW is dropped only
  when a readable non-RAW twin exists. Unopenable folders get their own
  InfoBar. Drops are refused while Settings is open.
- 2026-10-19 (#34): the 1 GB export memory target applies on PCs with 8 GB RAM
  or less or an iGPU (decode 4 ahead, 3 threads: 932 MB peak private at 24 MP
  to 4K, thin margin). Larger PCs use 8 and 6 for speed (about 1.3 GB).
- 2026-10-19 (#31): the rating ask comes 4 s after the second successful
  export's finished state, once per package version, never after a failure or
  cancel; a failed ask is log-only (the one exception to never silent, since the
  user did not ask). Reset to defaults keeps the count. New settings fields need
  no schema bump. *Send feedback* opens GitHub issues, *Rate 10Lapse* the Store.
- 2026-10-20 (Sven, #42): the Store screenshots use the generated sample
  photos (`scripts/store-screenshots.ps1`, "more polished"), not a real shoot.
- 2026-10-20 (Sven): videos under `docs/` are stored in Git LFS (`.gitattributes`). GitHub Free includes 10 GB of
  LFS storage and 10 GB of downloads a month, counted against the owner's account. The first is the 30 s showreel in
  `docs/store/showreel/`, which is not the #43 trailer.
- 2026-10-20 (issue review, Sven's ask): #55 merged into #45 (one exposure
  pass); #53 (screen and webcam capture: another user, and a `webcam`
  capability) and #57 (FFmpeg: beyond `PRODUCT.md`'s "up to 4K") closed as
  not planned. #7 carries the privacy URL and moved to milestone 4 with #90,
  which closes with a clean beta; #30 moved to 3.1 (P2). Milestones 0 to 3
  closed; the pinned #59 lists the open work by milestone.
- 2026-10-20 (Sven, #38): the selected segment pill keeps its designed look in
  light and dark (1.14:1 / 1.49:1 against the track; the bold label and the
  UIA selection state carry it), a known WCAG 1.4.11 gap for #41. High
  contrast maps it to the system highlight.
- 2026-10-19 (#39, PR #103): all app text is in `.resx` files (Presentation,
  Media, Core) read through `Core/Text/TextResources` behind the text classes
  (key = property name; a missing key throws). No `x:Uid`: XAML binds the text
  classes. Only the manifest description is `ms-resource:AppDescription`
  (`App/Strings/en-US/Resources.resw`); `DisplayName` stays the literal
  `10Lapse`. Log lines, error contexts (`…Context`, summary "Error context:"),
  *Copy diagnostics* and Debug text stay English in code. `StringRuleTests`
  bans literals in XAML, App and Presentation code. Debug `--pseudo-loc`,
  `scripts/pseudo-check.ps1`. **The first translation** adds `X.<lang>.resx`
  beside each `.resx` and `App/Strings/<lang>/Resources.resw` (without it the
  Store and the manifest stay EN-US), and changes code: set
  `DefaultThreadCurrentUICulture` from `ApplicationLanguages.Languages[0]`
  when packaged (tests pin en-US), CLDR plurals instead of `count == 1`,
  culture-aware `ProjectText.Count`/`Format`/sizes, format strings for the
  pieces glued in code (`TimingFormat` number + unit, readout headline +
  detail), invariant "fps"/"s" accepted when typed, and length limits.
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
  `OnClosing` goes through `WindowCloseGate` (a throw logs, stops the export,
  lets the window close). A Mica failure is log-only (flat fallback). The
  encoder probe starts after the first frame. `StartupTrace` logs start-up
  phases; `scripts/launch-test.ps1` counts launch crashes. Not reproduced in
  980 launches; #90 closes with a clean beta.
- 2026-10-19 (#37): the 16, 20, 24 and 32 px icons are full-bleed pixel-grid
  variants, not the canvas's padded tile, so they stay crisp. The splash is dark
  Mica `#1F1E1D` in both themes. The bundle keeps every scale in the main
  package (`AppxBundleAutoResourcePackageQualifiers` without Scale).
- 2026-10-19 (Sven): icon direction A Five frames (#37). Design spec approved
  (#11 closed): `DESIGN.md`, the canvas, `Main.dc.html` and the motion spec are
  the spec. License stays all rights reserved; no `LICENSE` file.

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
  2 GB, 30 days), `FrameImageCache` 192 MB; Debug `--sample-frames` with
  `--sample-folder` under your branch folder and `--sample-clean`.
- #32 PR #81 golden render tests (exact frame times, preview vs export within
  1 px; MP4 header durations one unit short at 24, 29.97, 60 fps, accepted).
- #20 PR #82 filmstrip (`FrameHistory` undo; Delete and Ctrl+Z only with the
  strip or nothing focused; Space always plays). #21 PR #83 `settings.json`
  (versioned, never throws, atomic); `--settings-folder=`.
- #84 PR #85 capture-time readers. #86 PR #87. #34 PR #89 `scripts/bench.ps1`.
- #24 PR #88 export panel, film gate, premiere; edits blocked during export;
  close asks and waits up to 2 s; taskbar progress; notifications packaged or
  Debug `--notifications`; `--export-to=`. #25 PR #92 `IKeepAwake` during
  export (system, not display).
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
- #98 PR #99: cache tests wait for state, disposals and events (recorded
  under `_gate`, raised after it).
- #35 PR #100: motion system (26 transitions, `ScreenMotion`, `ThemeCrossfade`,
  arrival develop and cascade, retime and reframe, InfoBar hold);
  `scripts/motion-check.ps1`; Debug `--motion-log`, `--motion-import=`,
  `--theme-flip-after=`, `--motion-infobar-after=`.
- #101 PR #102: timeouts are proved by their effect, ceilings are generous.
  If a timing test flakes, look first at `ImportPerformanceTests` and the
  `EncoderProbeTests` cached-load ratio.
- #39 PR #103: strings in `.resx` and `TextResources`, `StringRuleTests`,
  `--pseudo-loc` and `scripts/pseudo-check.ps1`; Frame segments end in an
  ellipsis if too long, the export time-left line wraps to two lines.
- #104 PR #105: `AllocatedByRead` takes the least of a few warm reads (a
  thread's count jumps about 8 KB at random); a flatness test keeps #84.
- #38 PR #106: names, landmarks and headings; shortcuts in `ShellShortcuts`
  (only where their control is on screen, never on Settings or under a
  dialog; a pending Timing entry is committed first); focus targets in
  `FocusTargets`; throttled announcements (`LiveProgress`, export 10 % steps
  at most every 5 s; focus on the running title, not the bar);
  `ContrastTableTests`; high contrast maps the segment pill to Highlight and
  InkSecondary to WindowText. `tools/TenLapse.A11yScan` (Axe.Windows) via
  `scripts/a11y-check.ps1`: 0 failures on 13 screens. Debug `--focus-walk`,
  `--shortcut-after`. Manual checks are on #41.
- #41 PR #107 (refs only): `docs/test-matrix.md`, 119 rows from every merged
  PR's unchecked items, Windows 11 only; Sven and the testers tick it off.
- #40 PR #108 (refs only): `scripts/pack.ps1` checks every package (identity,
  version, Windows 11 minimum, only `runFullTrust`, App Runtime 2,
  `ms-resource:` description, no Debug code) and bumps with `-Version x.y.z.0`
  (writes the manifest before building). `docs/release/flight.md` (Sven's
  Partner Center steps), `docs/release/beta-testers.md`, beta report form.
- #110 PR #111: `Core/Settings/AppFolders.Resolve` computes every app path
  once (defaults plus the Debug folder switches); the export journal sits
  beside the log folder in use. Release and packaged paths are unchanged.
- #42 PR #109 (refs only): `docs/store/listing.md` (en-US description, What's
  new, 19 features, 7 search terms, requirements, captions, the #7 interim
  line; HEVC Video Extensions marked paid; Windows 11 stated), and
  `scripts/store-screenshots.ps1` (8 shots to `dist/store-screenshots/`;
  upload 02-06 and 01). Debug `--sample-plain`.
- PR #112 (refs #43): the 30 s showreel in `docs/store/showreel/` (video in
  Git LFS), made by its `source/` (page, render, score); not the trailer.
- #113 PR #120: Debug `--lang=<tag>` (process only, never persisted);
  `TestCulture` pins en-US; `ResourceParityTests` (keys, placeholders, parse,
  plural categories per language, resx ↔ resw); scripts pass `--lang=en-US`;
  `store-screenshots.ps1 -Language`; test matrix 5.17. A translation adds
  files only (`ResourceLanguages.Shipped` lists all six).
- #114 PR #121: `Plural`/`PluralText` (CLDR cardinal rules; keys `X_one`,
  `X_other`, `X_many` in fr/es/it/pt-BR only; each key keeps exactly the
  English placeholders); display numbers in `CurrentCulture` ("Invariant on
  purpose" / "Not a plural" comments mark exceptions, `StringRuleTests`
  enforces); Store names in `Media/Strings/StoreExtensionText.resx`, never
  bound to an article or pronoun; `WrapRow`/`WrapLayout`; Debug
  `--motion-drop=`. `a11y-check.ps1` finds the build-26300 folder picker.
- #115 PR #123: French (`fr`, vous, U+202F typography, "i/s"; free/paid as
  invariant "(sans frais)"/"(achat requis)"; Ctrl+Z "remet", never
  "rétablir", which is Redo), `listing.fr.md`.
- #117 PR #126: Spanish (`es`, tú, "equipo" for PC; "video" without the
  accent for Latin America, MX Store names where ES and MX differ; `_many`
  with "de"; Ctrl+Z "devolver al proyecto"), `listing.es.md`.
- #116 PR #122: German (`de`, Sie, „…“, Strg/Entf), `listing.de.md`; Store
  names from the Store catalog service (market DE). Translator glossaries
  live in each language PR's body.
- #124 PR #125: English Store names follow the catalog (HEIF Image Extension,
  WebP Image Extension; keys unchanged); the plural-fallback test reads a
  test-only resx. Store names: `displaycatalog.mp.microsoft.com` ProductTitle.
