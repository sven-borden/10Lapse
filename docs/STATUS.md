# 10Lapse 3.0 status

Updated 2026-10-18. Roadmap issue: #59. Working agreement: `CLAUDE.md`.

## Now

Wave 1 is running: #3 and #6.

## Waves

Each wave runs at most two issues at once, only when they touch disjoint
areas. A wave starts when everything it depends on is merged. Model: **O** is
Opus 5.5, **S** is Sonnet 5.5.

| Wave | Issue | Model | Area | Status |
|---|---|---|---|---|
| 0 | #1 Toolchain | orchestrator | machine | done |
| 1 | #3 Tag legacy, new solution layout, gate script | O | repo root, all projects | running |
| 1 | #6 Privacy policy and README | S | `README.md`, `PRIVACY.md` | running |
| 2 | #4 Package identity and MSIX | S | `src/TenLapse.App` manifest, packaging props | |
| 2 | #10 Exact frame timing | S | `TenLapse.Core/Timing` | |
| 3 | #8 Spike: streaming encode | O | `TenLapse.Media`, a spike harness | |
| 3 | #27 Design tokens and theme resources | S | `TenLapse.App/Themes` | |
| 4 | #9 Spike: encoder capabilities | O | `TenLapse.Media/Encoding` | |
| 4 | #16 Project model and view models | O | `TenLapse.Core/Projects`, `TenLapse.App/ViewModels` | |
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

## Merged

Nothing yet.
