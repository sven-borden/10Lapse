# 10Lapse: working agreement for Claude Code

Read this first in every session. Current state, the wave plan, what is in
flight, owner items and decisions: `docs/STATUS.md`. Design: `PRODUCT.md`
(who and why), `DESIGN.md` (tokens and rules, with `.impeccable/design.json`
as its machine-readable sidecar) and the prototype in `docs/design/canvas/`
(`Main.dc.html` is the interactive prototype of every screen, the `Screen-*`
files are its states, `motion/Motion.dc.html` the motion spec). Code
conventions live next to the code; do not duplicate them here.

## Who and what

- Owner: Sven. 10Lapse turns a folder of photos into a timelapse video. It is
  on the Microsoft Store (product `9NBN2XH6S4D8`). 3.0 is a rewrite on WinUI 3,
  Windows App SDK 2.x and .NET 10, shipped as an update to the existing
  listing with the same package identity. It was submitted to the Store on
  2026-10-21, straight to the public listing (no beta flight); the Store keeps
  offering 2.1.1 until certification and the rollout (#44) are done. The 2.1.1
  packages were removed from that submission: Windows 10 PCs that have 2.1.1
  keep it, new Windows 10 customers get nothing (open, see `docs/STATUS.md`). The legacy UWP 2.1.1 code lives at the tag
  `v2.1.1-legacy`. The next work is 3.1 (milestone 6) and whatever the
  rollout and the first reviews bring up.
- Sven steers by short messages. Decide with sensible defaults, record the
  reasoning in the PR and in `docs/STATUS.md`, and ask only for choices he
  alone can make: Partner Center, signing, money, the icon direction, the
  sample photos. Do not create outward-facing things (issues, releases,
  Store submissions) beyond the agreed loop without saying so.

## Build process

- Waves: `docs/STATUS.md` orders the open issues into waves. At most two
  implementer agents run at once, and only when their issues touch disjoint
  areas of the code; each works in its own git worktree. Otherwise one at a
  time. The main session orchestrates and never implements an issue itself.
- Models: Opus 5.5 for complex issues (engine, architecture, design-critical
  screens), Sonnet 5.5 for well-bounded ones (chores, docs, panels that follow
  an existing pattern). Implementers launch as `subagent_type: "implementer"`
  with `model` passed explicitly. Never Fable, never `fork`.
- Per issue: the agent reads the issue, `docs/STATUS.md`, and the "What the
  next issues should know" section of the merged PRs its issue builds on;
  branches from an up-to-date `master` (`feat/NN-slug`, `chore/NN-slug`,
  `docs/NN-slug`, `fix/NN-slug`); commits after every working step; pushes
  early; opens the PR once the gate is green; and hands back at "PR open". It
  never merges.
- No CI/CD (Sven, 2026-10-18). The local gate replaces it:
  `pwsh -File scripts/gate.ps1` (checks every csproj is in `TenLapse.slnx`,
  restores, builds x64 Debug with warnings as errors, runs every test, builds
  x64 Release). The orchestrator reruns the gate on the PR branch
  before merging.
- Review: one read-only reviewer agent per PR, same model tier as the build.
  The findings go to a **fresh** implementer with a fixer brief, not back to
  the agent that built the PR. Then the orchestrator merges
  (`gh pr merge --merge --delete-branch`), pulls `master` and updates
  `docs/STATUS.md`.

## Machine facts (checked 2026-10-18)

- .NET 10 SDK 10.0.402 is a user install in `%LOCALAPPDATA%\Microsoft\dotnet`,
  on the user `PATH` with `DOTNET_ROOT` set. A shell started before that may
  not have it: prepend the folder to `PATH`. Set `DOTNET_CLI_TELEMETRY_OPTOUT=1`
  and `DOTNET_NOLOGO=1`.
- No Visual Studio. The .NET CLI alone builds a WinUI 3 app (Windows App SDK
  2.5.1, `Microsoft.Windows.SDK.BuildTools` 10.0.28000.2705, Win2D 1.4.0,
  target `net10.0-windows10.0.26100.0`),
  launches it unpackaged, and packs an MSIX with
  `-p:GenerateAppxPackageOnBuild=true`. On MSIX builds the warning "Path to
  `mspdbcmf.exe` could not be found" is expected: that tool ships with Visual
  Studio, and only the symbols package is skipped.
- Developer Mode is off and the account is not an administrator, so a packaged
  build cannot be registered here. Run the app unpackaged (Debug) to check it.
- Windows App Runtime 2 and Windows SDK 10.0.26100 are installed. The GPU is an
  NVIDIA GeForce RTX 4060 Ti (hardware H.264 and HEVC encode). The machine is
  x64: ARM64 builds compile but cannot run here.
- Git Bash and PowerShell 7 are both available.

## Every brief says

- Commit after every working step. Commit messages are plain descriptive
  sentences ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`
  (or `Claude Sonnet 5.5`, per the agent's model) and the `Claude-Session:` line
  the brief gives.
- The PR body has: Summary, How it was checked, What the next issues should
  know, Handoff for the fixer (10 to 15 lines: decisions, traps, where the
  tricky code is, what was tried and dropped), `Closes #N`, then the Claude
  Code line.
- Batch independent reads into one message, keep command output small, never
  wait in a loop. Done is "PR open".
- Never commit a `.pfx`, a certificate or any secret.
- This is Sven's own desktop. Never send synthetic input to it: no
  `SendKeys`, `SendInput`, `keybd_event`, `mouse_event`, `SetCursorPos`
  clicks or drags, and no `SetForegroundWindow` tricks. Drive the app only
  through UI Automation patterns on its own window (`InvokePattern`,
  `ValuePattern`, `SelectionItemPattern`) or Debug launch switches. If a
  check needs real input, skip it and say so.
- Stop every process you started before you hand back, and never stop one
  you did not start.
- Two agents may run at once: write logs, screenshots and test data only
  under your own worktree or a folder named after your branch in
  `%LOCALAPPDATA%\Temp\claude\`, never to a shared file name.

## Product invariants

- Photos never leave the PC. The app has no network access, no analytics and
  no accounts; the manifest declares `runFullTrust` and nothing else.
- Never silent: every failure becomes an InfoBar that says what happened and
  what to do. No `async void` except event handlers that await one guarded
  call.
- Exact timing: frame rate is a rational number; each frame's start is computed
  from its index in 100 ns ticks, never accumulated.
- Preview and export draw frames through the same processing code.
- Removing a frame removes it from the project, never from disk.
- Unbuilt actions are absent, not disabled.
- Colors come only from the token dictionaries. Amber is for Export, progress
  and selection indicators, as `DESIGN.md` defines.
- Motion respects `UISettings.AnimationsEnabled`: with animations off, movement
  becomes a short fade or nothing.

## Housekeeping

- Update `docs/STATUS.md` at every merge and whenever the order, the owner
  items or the decisions change. It is the memory of this repo; keep it under
  about 20 kB.
