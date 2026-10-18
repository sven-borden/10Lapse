# 10Lapse 3.0 status

Updated 2026-10-18. Roadmap issue: #59. Working agreement: `CLAUDE.md`.

## Now

Wave 3 is running: #8 and #27.

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
| 3 | #8 Spike: streaming encode | O | `TenLapse.Media`, a spike harness | running |
| 3 | #27 Design tokens and theme resources | S | `TenLapse.App/Themes` | running |
| 4 | #9 Spike: encoder capabilities | O | `TenLapse.Media/Encoding` | |
| 4 | #16 Project model and view models | O | `TenLapse.Core/Projects`, new `TenLapse.Presentation` | |
| 5 | #28 Title bar and app shell | O | `TenLapse.App` window and shell | |
| 5 | #13 Photo formats through WIC | O | `TenLapse.Media/Decoding` | |
| 6 | #26 One error path | O | App startup, `Core/Diagnostics` | |
| 6 | #15 Frame order | S | `TenLapse.Core/Ordering` | |
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
