# 10Lapse 3.0 test matrix

The pass before the 3.0 release (issue #41). Sven and the beta testers work
through it on real machines; none of it could be done on the dev PC, where
synthetic input is not allowed, the scale never changes, and there is no
ARM64, Intel, AMD or Qualcomm hardware. The numbers in brackets are the issue
or PR each row comes from.

How to use it: copy a section into issue #41, tick a row when the result is as
expected, and open an issue for any row that is not (name the machine, the
Windows build and the row). **Run** says where a row runs: *all* on every
machine in the pass, *once* on one machine, or the machine named. A row with
no Run runs once, on the main machine (the NVIDIA PC).

The rows write `->`, `...` and `.` where the screen shows an arrow, an ellipsis
and a middle dot (for example "742 photos -> 24.7 s"); match the meaning, not
the character.

Sections: 1 Machines, 2 Install paths, 3 Extensions, 4 Photo sets,
5 Features, 6 Per-machine measurements, 7 Accessibility, 8 Known and accepted.

## 1. Machines

3.0 needs Windows 11 (minimum 10.0.22000.0), x64 or ARM64, English only
(Sven, 2026-10-19, #2, #96, #97). Pick machines so that every row of the
table is covered by at least one.

| Machine | Why | Rows to run |
|---|---|---|
| Windows 11 24H2 or 25H2, x64, NVIDIA | the dev PC's vendor (RTX 4060 Ti: hardware H.264 and HEVC) | all |
| Windows 11 x64, Intel graphics | Intel encoder and converter, often an iGPU | all, plus 6.2 (an iGPU counts as tight) |
| Windows 11 x64, AMD graphics | AMD encoder | all |
| Windows 11 ARM64 (Snapdragon, Qualcomm encoder) | 3.0 builds for ARM64 but never ran on it (#65, #74, #100) | all, plus 6.1 |
| A PC with 8 GB of RAM or less, or an iGPU | the 1 GB export memory target (#34, #89) | 6.2, 5.8 |
| One PC at 100, 150 and 200 % scaling | scaling was never seen on screen (#67, #94) | 5.13 |
| Light, dark and high contrast themes | high contrast was never seen on screen (#27, #64) | 5.12, 5.13, 7 |
| Animation effects on and off | (#35, #100) | 5.11 |
| Windows 11 21H2 (build 22000) | the minimum build, never run (#2) | 2.1 and 5.14.1, once |
| A Windows 10 22H2 PC with 10Lapse 2.1.1 | 3.0 does not install there | 2.4 |

- [ ] **1.1 Machines covered.** Write the machines used into issue #41: Windows
  build (`winver`), CPU, GPU and driver, RAM, scaling.

## 2. Install paths

- [ ] **2.1 Clean install from the flight** (#40). Run: all. Do: install 3.0
  from the Store flight on a PC that never had 10Lapse. Expect: it installs
  and starts, the first-launch screen shows (5.1), the icon and tile are the
  Five frames icon (5.16). The Store installs the Windows App Runtime 2
  framework package with it (STATUS, 2026-10-18); no extra install step. The
  flight is the install path for testers (Developer Mode is off on the dev
  PC and a tester's PC usually has it off too; see 2.5).
- [ ] **2.2 Update in place from 2.1.1** (#2, #62). Run: once, on a PC with
  2.1.1 from the Store. Do: update to 3.0 from the flight with 2.1.1 open (the
  Store asks to close it, or updates once it is closed). Expect: same listing
  and package identity, so it arrives as an update, not a second app; the first
  launch of 3.0 shows 5.1.1.
- [ ] **2.3 Uninstall** (#60). Do: uninstall 3.0. Expect: it goes cleanly; the
  privacy policy says settings are removed with the app.
- [ ] **2.4 Windows 10 keeps 2.1.1** (#96, #97). Run: a Windows 10 PC with
  2.1.1. Expect: the Store does not offer 3.0 and 2.1.1 keeps working. This is
  the only Windows 10 check.
- [ ] **2.5 Install from the package outside the flight** (optional, #62).
  Run: only a PC where Sven or a tester has turned Developer Mode on
  (sideloading an MSIX needs it, and the dev PC has it off). Do: install the
  package that `scripts/pack.ps1` builds (#40). Expect: it installs and starts
  like 2.1. If nobody has such a PC, skip it and say so in #41: the flight is
  the only install path.
- [ ] **2.6 Settings after the update from 2.1.1** (#2, #16, #21). Run: once, on
  the PC from 2.2. Expect: 3.0 does not read 2.1.1's settings and starts at its
  defaults (30 fps, 16:9 Fill, 1080p, H.264 High, capture-time order); change
  one setting, restart: it is remembered.
- [ ] **2.7 Settings after uninstall and reinstall** (#21, #60). Run: once. Do:
  change settings, uninstall 3.0 (2.3), install it again. Expect: it starts at
  the defaults of 2.6, with no memory of the last folders; 5.10.4's export
  count starts again from zero.

## 3. Extensions

HEVC Video, HEIF Image and Raw Image Extensions, present and absent. The
InfoBar is one per missing extension, never one per photo, with the photo count
and a *Get it from the Store* button that opens the Store app (no browser, no
network from 10Lapse) (#14, PR #79). Run: all, with and without. Uninstall an
extension in Settings > Apps, add the photos again, restart 10Lapse after each
install or uninstall (the wording tells the user to restart).

- [ ] **3.1 HEIF Image Extensions missing, iPhone HEIC folder** (#14).
  Expect: "These N photos need the free HEIF Image Extensions from Microsoft."
  The button opens the Store page for the extension.
- [ ] **3.2 Raw Image Extension missing, RAW folder** (#14). Expect: "These N
  photos need the free Raw Image Extension from Microsoft." DNG and other RAW
  that the PC can already decode do not prompt.
- [ ] **3.3 HEVC Video Extensions missing, HEVC-coded HEIC** (#14). Expect:
  "These N photos need the HEVC Video Extensions from Microsoft, which costs a
  small fee." A PC that already has a software HEVC decoder must not be
  prompted (no false paid upsell); those files stay in the generic "couldn't
  be read" list.
- [ ] **3.4 HEIF and HEVC both missing** (#14). Expect: one InfoBar naming
  both; its button opens the first, then a follow-up InfoBar at the front of the
  queue: "Next, get the HEVC Video Extensions (a small fee). When both are
  installed, restart 10Lapse and add the photos again."
- [ ] **3.5 Store cannot open** (#14). Do: turn the Store off or block it if
  you can. Expect: a Caution InfoBar "Couldn't open the Microsoft Store. Search
  the Store for <name>, install it, then restart 10Lapse and add the photos
  again."
- [ ] **3.6 After installing and restarting** (#14). Expect: the same photos
  import with no prompt. Whether a running 10Lapse picks up an extension
  installed meanwhile was never checked: note what you see.
- [ ] **3.7 All three present** (#14). Expect: HEIC, RAW and HEVC photos import
  with no extension InfoBar.
- [ ] **3.8 HEVC export without the HEVC extension** (#9, PR #68). Expect:
  HEVC is not offered as a format and the Output panel's line says "This PC
  cannot play HEVC video, so it does not export it." (a PC can have an HEVC
  encoder in its GPU driver but no decoder; PR #68).
- [ ] **3.9 HEVC export with the extension** (#9). Expect: HEVC is offered
  where a hardware or software HEVC encoder works; the exported file plays in
  the PC's default player.

## 4. Photo sets

Import each set by *Choose folder*, then once by drag and drop (5.1). For each:
the count is right, nothing is skipped without an InfoBar, the order is
sensible (5.2), the preview and a 1080p export show every photo upright,
uncropped (Fit) or filling (Fill) and not stretched.

- [ ] **4.1 iPhone HEIC** (#13, #14). Needs HEIF (and HEVC for newer files).
  Expect: portrait photos upright (EXIF orientation applied), colours as in
  Photos (Display P3 is converted to sRGB; no real iPhone HEIC was ever tried,
  PR #70, #72).
- [ ] **4.2 Android JPEG** (#13). Expect: upright, capture time order matches
  the shooting order.
- [ ] **4.3 DSLR 24 MP JPEG** (#13, #34). Expect: import and filmstrip are
  quick (first thumbnails within 2 s on 1,000 photos, #89), no stutter in the
  preview.
- [ ] **4.4 DSLR RAW** (#13, #14, #71). Needs Raw Image Extension. Expect: CR2,
  NEF, ARW, DNG, ORF, RW2, PEF and CR3 import. No camera RAW was ever tried
  here (PR #71 "Unverified, no sample file"): check the capture time order
  too; a file with no readable time falls back to name order.
- [ ] **4.5 RAW + JPEG pairs** (#13, #73). Do: a folder with `DSC_0001.NEF` and
  `DSC_0001.JPG` pairs. Expect: each pair imports once, as the JPEG. Without
  the extension that would decode the non-RAW twin (for example HEIF), both are
  kept.
- [ ] **4.6 Mixed portrait and landscape** (#18). Expect: no distortion; with
  more than half the photos portrait and the shape 16:9 or 4:3, the quiet
  suggestion appears with *Use 9:16* and *Dismiss*, and never changes the
  shape on its own.
- [ ] **4.7 5,000 photos** (#12, #34, #73, #89). Do: 5,000 photos at 24 MP, to
  a 4K export. Expect: the import list fills in about 2 s on the dev PC,
  progress with Cancel in the command bar, the filmstrip scrolls smoothly, the
  export finishes (about 13 fps on the dev PC with the tight profile, 383 s).
- [ ] **4.8 OneDrive online-only files** (#12, PR #73). Run: a PC with OneDrive
  Files On-Demand. Do: import a folder that holds online-only photos. Expect:
  nothing downloads by itself; one question "N photos are only in OneDrive.
  Download them now? About X GB." with *Download* and *Skip them* (default;
  Esc skips). *Skip them* leaves them out with the skipped-photos InfoBar;
  *Download* adds and reads them. Never tried against a real placeholder.
- [ ] **4.9 A broken photo** (#12, #13, #72). Do: a folder with a truncated
  JPEG, an empty file and a text file named `.heic`. Expect: the good photos
  import; one InfoBar lists the skipped ones with a reason (*View list*); no
  extension prompt for the broken ones; the project exports.
- [ ] **4.10 Folder that will not open** (#12). Do: import from an SD card or
  a network folder, remove it mid-way. Expect: a Caution InfoBar "Couldn't open
  the folder <name>. Check that its drive or memory card is connected and that
  you can open it in File Explorer, then add it again."
- [ ] **4.11 Photos that disappear during an export** (#13, #74). Do: delete
  or lock a source file while exporting. Expect: the export fails with an
  InfoBar that names the frame; no half file is left.

## 5. Features

### 5.1 Import and first launch (#29, #73, #75)

- [ ] **5.1.1 First launch** (#29, PR #75). Expect: a dashed drop zone with
  "Drop a folder of photos to make a timelapse", the one amber *Choose folder*
  and *Choose photos*, "Your photos never leave this PC.", no Export button.
  The Daybreak timelapse plays while the window is active.
- [ ] **5.1.2 Choose folder, Choose photos, Include subfolders** (#12, PR #73).
  Expect: the picker opens in Pictures; with subfolders on, subfolders are
  walked; hidden files are left out; the project is named after the folder.
- [ ] **5.1.3 Real drag and drop from Explorer** (#12, #29, PR #73, #75). Never
  tried with a real pointer. Do: drag a folder over the window, leave, drop.
  Expect: while over the window the zone shows "Drop to start your timelapse"
  in amber and the cursor caption says what happens; leaving resets it; the
  drop imports. Drop on a project that has photos: a question "Add these photos
  to this project?" with *Add to project* (default), *Start new project*,
  *Cancel*. Dropping while Settings is open is refused.
- [ ] **5.1.4 Cancel and New project during an import** (#12, #66). Expect:
  both return at once; a late batch adds nothing.
- [ ] **5.1.5 Skipped photos InfoBar** (#12). Expect: one InfoBar with the
  count and a list of names and reasons.

### 5.2 Order (#15, #20)

- [ ] **5.2.1 Default order** (#15). Expect: capture time when every photo has
  one, otherwise name in Explorer's order.
- [ ] **5.2.2 Reorder by hand** (#20, #15). Do: drag a thumbnail. Expect: the
  frame moves, the order becomes manual, a later import does not re-sort it
  (a real pointer drag was never tried, PR #82).
- [ ] **5.2.3 Gap markers** (#15, #20). Expect: numbering gaps in name order
  and time gaps in capture-time order get a marker; none in manual order.

### 5.3 Timing (#10, #17)

- [ ] **5.3.1 Per second** (#17). Do: pick 12, 24, 25, 30, 50, 60; type 29.97,
  23,976, 200, abc, 1,000. Expect: 29.97 is the exact 30000/1001; a dot or a
  comma is the decimal point; 200 gives "Frame rates run from 1 to 120 fps, so
  200 is now 120 fps."; 1,000 is refused with "Type 1000 without a separator,
  or 1.5 for one and a half." (not read as 1000 or 1.0; the example follows the
  PC's decimal mark); text that is not a number is refused with a sentence.
- [ ] **5.3.2 Length and Per photo** (#17). Do: 10, 15, 30, 60 s, `1:30`; 0.25,
  0.5, 1, 2, 5 s. Expect: "at 24.73 fps" style rate under the readout; a line
  when the photos cannot reach the target at 1 to 120 fps.
- [ ] **5.3.3 Readout** (#10, #17). Expect: "742 photos -> 24.7 s" and "at
  30 fps" follow every change.

### 5.4 Frame (#18)

- [ ] **5.4.1 Shape and Fit or Fill** (#18). Do: Match, 16:9, 9:16, 1:1, 4:5,
  4:3, each with Fit and Fill. Expect: the preview and the exported video show
  the same framing (Fit letterboxes, Fill crops).
- [ ] **5.4.2 Match photos** (#18). Expect: the caption names the size it
  takes.

### 5.5 Output (#9, #23)

- [ ] **5.5.1 Only what this PC can do** (#9, #23, PR #76). Run: all. Expect:
  resolution, format and quality lists offer only what the PC confirmed (a size
  it cannot encode is absent, not greyed), with one line saying why. Write
  down which line each machine shows: "This PC's graphics hardware exports up
  to {size}." or "...; larger sizes use the slower software encoder." or "This
  PC's graphics hardware cannot encode {size}; the slower software encoder
  exports up to {size}." (6.5 relies on the last two).
- [ ] **5.5.2 Checking state** (#23, #90). Expect: right after launch the
  section reads "Checking what this PC can encode..." for a few seconds on a
  new machine or driver (the probe takes about 4 s cold, 0.02 to 0.25 s cached),
  then the lists appear in one step with no flicker. After 15 s without an
  answer it settles on H.264 up to 1080p.
- [ ] **5.5.3 Replaced choice** (#23, #76). Do: choose 4K and HEVC, set a rate
  or shape the PC cannot do at that size. Expect: the panel selects what the PC
  can do and says so ("4K can't be exported on this PC with these settings, so
  1440p is selected."); changing the shape back restores your choice.
- [ ] **5.5.4 Estimate** (#23). Expect: "About 180 MB" style size follows
  every setting; compare it with the real file in 5.8.

### 5.6 Preview (#19)

- [ ] **5.6.1 Frame-accurate playback** (#19). Expect: scrubbing and playing
  show the frame at the playhead; the first look at a large folder is quick;
  proxies fill `LocalCache\Proxies` (2 GB cap).
- [ ] **5.6.2 Keyboard** (#19, #38). See 7.1 for the keys. Space plays and
  pauses and never repeats while held.
- [ ] **5.6.3 Window hidden or minimized** (#19). Expect: playback pauses and
  continues when shown again.
- [ ] **5.6.4 Display reset** (#19, #74). Never provoked. If a driver reset
  happens, expect the preview to recover
  without a restart, at most one InfoBar a minute.

### 5.7 Filmstrip (#20)

- [ ] **5.7.1 Select, remove, undo** (#20). Do: click a thumbnail, press Delete,
  then Ctrl+Z. Expect: the frame leaves the project only (the file stays on
  disk) and comes back on undo; the preview jumps to the clicked frame.
- [ ] **5.7.2 Thumbnail size slider and scrolling** (#20). Expect: smooth with
  5,000 frames; thumbnails fill in from the middle of the view outwards.
- [ ] **5.7.3 Mouse wheel and touch scrolling** (#20, #24). Never tried. Also
  during an export.
- [ ] **5.7.4 Context menu and tooltips** (#20, #39). Never seen: right-click a
  thumbnail, hover the controls. Expect: English, no raw key names; the menu
  lists Remove from project (Delete), Undo (Ctrl+Z), Move earlier (Alt+Left)
  and Move later (Alt+Right), each with its key shown.

### 5.8 Export (#8, #22, #24, #34)

Rows 5.8.1 to 5.8.4 are one row per machine, ticked on that machine. Do: export
a 60-photo project at each offered format (H.264, HEVC) and each offered
resolution and quality. Expect: it finishes; the file plays in the default
player at the right size; the Finished summary ("24.7 s . 4K . H.264 .
173 MB") matches the file. Write down the largest size the machine offered.

- [ ] **5.8.1 Every format and resolution, NVIDIA PC** (#22, #23).
- [ ] **5.8.2 Every format and resolution, Intel PC** (#22, #23).
- [ ] **5.8.3 Every format and resolution, AMD PC** (#22, #23).
- [ ] **5.8.4 Every format and resolution, ARM64 PC** (#22, #23).
- [ ] **5.8.5 Frame rates** (#10, #81). Do: export at 24, 29.97 and 60 fps, and
  one rate below 1 fps (one photo per 2 s). Expect: the right length. Explorer's
  *Length* can read one unit short (see 8.1).
- [ ] **5.8.6 Save dialog** (#24). Expect: it suggests `<project name>.mp4` in
  the Videos folder, then in the last folder used; a name already in use saves
  `Name (2).mp4` and never overwrites. Cancel in the dialog changes nothing.
  The real dialog was never seen (an empty file the picker creates must not
  stay behind on cancel).
- [ ] **5.8.7 Progress** (#24, #92). Expect: the export panel with file name,
  large percent, time left, the film gate, "Rendering frame 0313" over the
  preview, the filmstrip dimming what is still to come, taskbar progress
  (indeterminate while preparing). Never seen in the taskbar.
- [ ] **5.8.8 Minimize during an export** (#25, PR #92). Do: minimize at once.
  Expect: it keeps running at full speed (the dev PC: 128 vs 117 fps minimized)
  and the PC does not sleep. Also run it with the display off for 10 minutes,
  on a laptop with Modern Standby (never done).
- [ ] **5.8.9 Cancel** (#22, #24). Do: Cancel export during an export of several
  GB. Expect: back to the inspector at once (under 1 s), then "Export cancelled.
  The unfinished file was removed.", no `.mp4` or hidden `.partial` left.
- [ ] **5.8.10 Close during an export** (#24, #25). Do: close the window.
  Expect: "Stop the export and close?" *Keep exporting* continues and holds the
  PC awake; *Stop and close* ends the app within about 2 s and leaves no
  partial file.
- [ ] **5.8.11 Edits blocked during an export** (#24). Expect: no reorder,
  removal, import, New project or Reset to defaults while it runs.
- [ ] **5.8.12 A failure** (#22, #26). Do: a full disk or a read-only target
  folder. Expect: an InfoBar that says what happened and what to do, with
  *Copy diagnostics*; the previous file at that name is untouched.
- [ ] **5.8.13 Sleep or sign-out during an export** (#22, #25). Do: start a
  long export, put the PC to sleep (or sign out) and resume. Expect: the export
  continues or fails with an InfoBar; no half-written `.mp4`; the next start
  removes any `.partial`. Note what happens.
- [ ] **5.8.14 Memory** (#34, #89). See 6.2.

### 5.9 Finished and premiere (#24)

- [ ] **5.9.1 Finished state** (#24). Expect: a check that draws in, "<name>.mp4
  is ready", the summary line, *Play* (amber), *Show in folder*, *Back to
  editing*; Export video stays disabled until you go back.
- [ ] **5.9.2 Premiere** (#24). Expect: the finished MP4 plays over the gate
  1.1 s after the finish, once, stopping on the last frame; *Play* plays it
  again. If it cannot play: Caution "The video was saved, but 10Lapse couldn't
  play it here. Open it with Show in folder to check it." (never seen).
- [ ] **5.9.3 Play and Show in folder** (#24). Never pressed. Expect: the
  default player opens the file; Explorer selects it.

### 5.10 Settings, reset, rating, notifications (#21, #26, #31, #24)

- [ ] **5.10.1 Settings are remembered** (#21). Do: set timing, shape, Fit,
  resolution, format, quality; close; start. Expect: all are back, and New
  project starts from them. The packaged path (`LocalState\settings.json`) was
  never run; a damaged `settings.json` must not stop the app.
- [ ] **5.10.2 Reset to defaults** (#21, #31). Expect: an InfoBar, panels back
  to Per second / 30, 16:9, Fill, 1080p, H.264, High; refused during an export.
- [ ] **5.10.3 Settings page** (#26). Expect: Back, Escape, Alt+Left and the
  mouse back button leave it; *Copy diagnostics* copies a report with the
  profile path and user name redacted.
- [ ] **5.10.4 Rating ask** (#31, PR #93). Run: a flight build (the real call
  needs package identity and the Store), on a machine that has not exported
  with this version (fresh install, or delete `LocalState\settings.json`). Do:
  two successful exports. Expect: 4 s after the second finished state, the
  Store's rating dialog appears once, only while the window is active; not
  after a failed or cancelled export; a restart does not ask again. The Store
  may stay quiet (it rate-limits): the app counts it as asked. The export count
  and the asked version persist across restarts and Reset to defaults, so a
  tester who exported before never sees the ask. A later flight with a new
  package version asks once more.
- [ ] **5.10.5 Feedback card** (#31). Expect: *Send feedback* opens the GitHub
  new-issue page, *Rate 10Lapse* opens the Store review page for
  `9NBN2XH6S4D8`; a launch failure gives one InfoBar that says what to do.
- [ ] **5.10.6 Notifications** (#24, PR #88). Run: a packaged build. Do: start
  an export, switch to another app or minimize. Expect: when it finishes, a
  toast "<name>.mp4 is ready"; when it fails (a full disk as in 5.8.12, while
  minimized), a toast "<name>.mp4 couldn't be exported. Open 10Lapse to see
  why."; none on cancel; none while the window is active; no folder or photo
  names. Click the toast and note what happens (never tried).

### 5.11 Motion and reduced motion (#35, PR #100)

Toggle Settings > Accessibility > Visual effects > Animation effects. Run: all.
The value is read at each start of an animation, so a running 10Lapse follows
it; this was never flipped while the app ran.

- [ ] **5.11.1 Animations on** (#35). Expect: screen changes, the panel swap,
  the film gate, the check, the segment pill slide, the drag look and Daybreak
  move as `docs/design/canvas/motion/Motion.dc.html` shows; InfoBars never
  appear in the middle of a transition. Watch how each animation looks frame by
  frame (never done).
- [ ] **5.11.2 Animations off** (#35, #29). Expect: movement becomes a short
  fade or nothing: panel swap fades (150 ms in, 90 ms out), the film gate
  strip stands still with only its strengths changing, Daybreak shows a still
  at frame 0181 with no lift, the check only fades.
- [ ] **5.11.3 Theme switch while running** (#35). Do: switch Windows between
  light and dark (and in the middle of the premiere). Expect: a 250 ms
  crossfade of the whole window, no flash, no stale colours; the preview and
  the filmstrip photos do not flicker.
- [ ] **5.11.4 Drag leave** (#35). Expect: the drag look fades out when a real
  drag leaves the window (only the fade-in was seen).

### 5.12 Themes and high contrast (#27, #64, #38)

- [ ] **5.12.1 Light and dark** (#27). Do: switch while running and on each
  screen (first launch, Editing, Settings, Exporting, Finished, the dialogs,
  every InfoBar kind). Expect: tokens only, text readable, amber only on Export,
  progress and selection.

Rows 5.12.2 to 5.12.5 are one per Windows contrast theme. Do: switch to the
theme and go through every screen (first launch, Editing, Settings, Exporting,
Finished, the dialogs). Expect: every surface and text uses the system colours,
nothing is invisible, the selected segment is Highlight, disabled controls are
GrayText.

- [ ] **5.12.2 Aquatic** (#27, #64, #38).
- [ ] **5.12.3 Desert** (#27, #64, #38).
- [ ] **5.12.4 Dusk** (#27, #64, #38).
- [ ] **5.12.5 Night sky** (#27, #64, #38).
- [ ] **5.12.6 Controls and bands in high contrast** (#27, #64). Expect: the
  toggle switch's off state, hover and press look right; the Mica band is reset
  to the system colour; the Daybreak is hidden (#75).
- [ ] **5.12.7 Fixed colours in high contrast** (#17, #64). Expect: the
  Rendering frame pill's dot and the letterbox stay fixed colours on purpose
  (they sit on the photo); the segment pill is Window on ButtonFace with no
  outline, and still reads.
- [ ] **5.12.8 Switch into and out of high contrast while running** (#28). The
  app listens to `UISettings.ColorValuesChanged`. Expect: it follows without a
  restart.
- [ ] **5.12.9 Transparency effects off, or battery saver** (#28, #67). Expect:
  the flat fallback instead of Mica, no error.

### 5.13 Scaling and layout (#28, #36, #94)

Run at 100, 150 and 200 %.

- [ ] **5.13.1 Sharpness** (#94, #95). Expect: icons, the segment pill, ring
  and thumbnails crisp at 150 and 200 %. Never seen above 100 %.
- [ ] **5.13.2 Docked and narrow** (#36, PR #94). Do: drag the window from wide
  to narrow across 1,000 DIPs (1,500 px at 150 %, 2,000 px at 200 %). Expect:
  below it the inspector is a 260 px panel under the stage with the Timing,
  Frame and Output tabs; above it the side panel; no flicker or lost state
  when crossing back and forth.
- [ ] **5.13.3 Minimum size** (#28). Expect: the window cannot go below 640 x
  480 DIPs, also after maximize and restore, at every scale.
- [ ] **5.13.4 Narrow export and finished** (#36, #24). Expect: two columns,
  the tabs hidden; Cancel export, Play, Show in folder and Back to editing
  stay in view at 640 x 480; the time-left line wraps to two lines, and
  "Finishing the file..." wraps without clipping (never caught on screen,
  #103).
- [ ] **5.13.5 Moving between monitors of different scale** (#82, #94). A DPI
  change was never tried (it needs a second monitor). Do: drag the window across.
  Expect: layout and sharpness follow, the docked or narrow state matches the
  new width in DIPs.
- [ ] **5.13.6 Title bar** (#28). Expect: dragging the title bar moves the
  window; the command bar buttons stay clickable.

### 5.14 Start-up and stability (#90, #91, #26)

- [ ] **5.14.1 Crash-free start** (#90, PR #91). Run: all, and the beta
  testers over the beta. Do: start the app many times (cold after a reboot,
  with the GPU busy, with a game open). Expect: it starts every time. The
  start-up crash seen on the dev PC (code 0xC000027B, fault offset 0x3a9dbd,
  E_UNEXPECTED) was not reproduced in 980 launches. A crash: note the time,
  the Windows event 1000/1001 in Event Viewer > Windows Logs > Application,
  and the log in `LocalState\Logs`. #90 stays open until the beta is clean.
- [ ] **5.14.2 Encoder probe after the first frame** (#90, #91). Expect: the
  window and the drop zone appear before the encoder probe (5.5.2) answers; the
  probe never blocks the start.
- [ ] **5.14.3 Two instances** (#26). Do: start 10Lapse twice. Expect: each
  writes its own log file; neither breaks.
- [ ] **5.14.4 Log** (#26). Expect: one file per process per day, 7 days, in
  `LocalState\Logs`; it may include file and folder names (the privacy policy
  says so).

### 5.15 Privacy (#6)

- [ ] **5.15.1 No network** (#6, #62). Do: block 10Lapse in the firewall, or
  watch its connections during a full session. Expect: none. The manifest
  declares `runFullTrust` and nothing else.

### 5.16 Icon and tiles (#37, PR #95)

- [ ] **5.16.1 Icon everywhere** (#37). Expect: the Five frames icon crisp in the
  taskbar (light and dark taskbars), Start, Alt+Tab, the title bar, the Store
  and Settings > Apps, at each scale. The splash is dark Mica in both themes
  (accepted: light-theme users see a dark splash for a moment).

## 6. Per-machine measurements

- [ ] **6.1 Bench on each machine** (#34, PR #89). Run: all, with the repo and
  the .NET 10 SDK. Do: `pwsh -File scripts/bench.ps1 -Quick` (500 photos of
  12 MP, about 5 minutes, needs 11 GB free). Expect: first view of the strip
  within 2 s; paste the printed row into `docs/benchmarks.md`. ARM64 is
  measured for the first time.
- [ ] **6.2 Memory on the 8 GB or iGPU PC** (#34, PR #89). Do: `bench.ps1
  -Quick`, then `-Full` (5,000 photos of 24 MP, 56 GB free) on the tight PC.
  Expect: private bytes under 1 GB (the default profile on such a PC is the
  tight one, 4 frames ahead on 3 threads; 932 to 939 MB on the dev PC, 8 %
  under the target). A tight-memory peak over 1 GB is a release question. The
  RAM and iGPU detection was never run on a real tight PC.
- [ ] **6.3 Golden tests on another GPU vendor** (#32, PR #81). Run: Intel,
  AMD, Qualcomm. Do: `dotnet test tests/TenLapse.Tests --filter
  "FullyQualifiedName~GoldenTimingTests|FullyQualifiedName~GoldenGeometryTests"` and compare the per-case lines
  (encoder, sample offsets, header duration, worst mean error, worst edge, bar
  shift) with the dev PC's. Expect: exact frame times, geometry within 1 px of
  the preview. Hardware encoders are not asserted to be identical.
- [ ] **6.4 Golden tests on the software encoder** (#32). Do: the same with
  `TENLAPSE_GOLDEN_SOFTWARE=1`. Expect: pass; mean error up to 3.25 against a
  limit of 4. A PC that falls back to the software encoder is near the limit:
  raise the limit only from a measurement, never the edge tolerance.
- [ ] **6.5 Software encoder in the app** (#9, #32). Do: on a PC with no
  hardware H.264 encoder (a virtual machine, an old GPU), export. Expect: the
  Output panel says it is the slower software encoder; the export works; the
  picture is right (bars up to 1 px off, 8.2).
- [ ] **6.6 ARM64 build** (#3, #62). Run: Snapdragon. Expect: the Store
  bundle's ARM64 package installs and runs natively; everything in section 5
  works. Nothing ARM64 has ever run.
- [ ] **6.7 Launch test** (#90, PR #91). Run: the dev machine, a few PCs.
  `scripts/launch-test.ps1` starts the Debug app many times (see
  `Get-Help scripts/launch-test.ps1`); expect 0 crashes and 0 hangs.

## 7. Accessibility

The manual checks from PR #106 (#38). Axe.Windows finds no failures on 13
screens (`scripts/a11y-check.ps1`); these parts need a human with a keyboard,
Narrator or real high contrast. Run: once on a clean PC, and 7.3 on each
theme.

- [ ] **7.1 Real key presses** (#38). Ctrl+O, Ctrl+Shift+O, Ctrl+N and Ctrl+E
  work on Editing; none works on Settings or under a dialog. Space, the arrows,
  Delete and Ctrl+Z work in the strip and with nothing focused. Escape works on
  Settings and in each dialog. Type a rate in the Timing box and press Ctrl+E
  at once: the video uses the typed rate.
- [ ] **7.2 The whole export with the keyboard alone** (#38). Ctrl+O, pick a
  folder in the picker, Tab to Timing and Output and change them, Ctrl+E, the
  Save dialog, Enter on *Play the video*, *Show in folder*, Ctrl+N.
- [ ] **7.3 Visible focus everywhere** (#38). In light, dark and high contrast,
  on Mica and on the Layer: the segmented pills, the thumbnails (the 6 px ring
  room, #82), the scrubber, the Export accent button, the running export's
  title (FocusStop), *Back to editing*.
- [ ] **7.4 Known WCAG 1.4.11 gap** (#38). The selected segment's pill is 1.14:1
  (light) and 1.49:1 (dark) against the track, kept by Sven's call
  (2026-10-20). Check that the selection reads from the bold label.
- [ ] **7.5 Narrator: export** (#38). Expect: announcements for start, 10 %
  steps at most every 5 s, "Finishing the file.", ready and stopped; focus on
  "Exporting <file>, heading level 2" at the start; no reading of every
  percent.
- [ ] **7.6 Narrator: import and the rest** (#38, #76, #80). Expect: the
  import announces its first text, then every 5 s, then the photo count;
  InfoBars are read as they enter; the Frame panel's suggestion is not read
  twice (#80); the Output panel's "Checking..." and notice are read (#76,
  unverified with Narrator); landmark navigation (D) lands on the named
  landmarks; headings (H) are right; the strip reads as a list with its
  positions, and scan mode reads on past the edge of the viewport.
- [ ] **7.7 Real high contrast** (#38, #64). Expect: every token is a system
  colour; the selected segment is Highlight. Nothing has been seen on screen.
  See 5.12.2 to 5.12.7.
- [ ] **7.8 Narrow tabs with the keyboard** (#38, #94). Expect: arrow keys on the
  Timing/Frame/Output tab row keep the focus on the row while selection moves
  (UIA Select does not move focus). Also check Tab in both layouts.
- [ ] **7.9 Unrealized thumbnails** (#38). The filmstrip's peer has no
  ItemContainer or VirtualizedItem pattern, so a screen reader cannot bring an
  unrealized frame in by itself (the Scroll pattern can). Open a follow-up if
  Narrator users need it.
- [ ] **7.10 Finished state with Narrator** (#24, #38). Expect: focus lands on
  *Play the video*, and Narrator reads the "<name>.mp4 is ready" title and the
  summary line.
- [ ] **7.11 Reorder with the keyboard** (#20, #38, PR #82). Focus a thumbnail
  (or select several) and press Alt+Left and Alt+Right. Expect: the selected
  frames move one place earlier or later, the order becomes manual (5.2.2).
  Alt+arrows with another control focused do nothing to the strip.

## 8. Known and accepted

Not bugs unless they change. Keep these out of the failure list.

- **8.1 MP4 header durations are one timescale unit short** (#32, PR #81). The
  Media Foundation MP4 sink writes the movie header duration (`mvhd`/`mdhd`)
  one unit short at 24, 29.97 and 60 fps (119,999 of 120,000); the sample table
  is exact and playback is unaffected. Explorer's *Length* truncates: a 2 s clip
  at 60 fps shows 00:00:01. Exact at 25, 30, 2 and 0.5 fps. Do not report it.
- **8.2 Software H.264 encoder tolerance** (#32). Geometry mean error up to
  3.25 levels (limit 4), bars can be 1 px off; NVENC is 1.67 and 0 px.
- **8.3 Declared rate below 1 fps** (#32, PR #81). H.264 declares the real
  rate (1/2); HEVC declares the nominal 1/1 (the frame times are exact in both).
- **8.4 The segment pill's contrast** (#38, STATUS 2026-10-20). See 7.4.
- **8.5 No symbols for the Store build** (#3, #62). The Release package build
  warns that `mspdbcmf.exe` is missing, so the symbols package is skipped.
  Crash reports from the Store (Partner Center Health) therefore have no
  symbols. Watch Health for crashes during the flight anyway.
- **8.6 Dark splash in the light theme** (#37, PR #95). The manifest splash
  cannot follow the theme.
- **8.7 NVIDIA handle leak** (#22, PR #74). The NVIDIA encoder leaks about 34
  handles per export; not ours.
- **8.8 Canvas deviations kept** (#24, #36). Detail rows are 14 px (canvas
  13 px), *Back to editing* has no underline, the premiere plays once, the
  disabled Export button is neutral rather than dimmed amber; the narrow
  export and finished screens use two columns and hide the tabs.
- **8.9 No *Try a sample* button** (#29, #30). Deferred until a sample set
  exists.
- **8.10 English only** (#2, #39). Translations come later.
- **8.11 No thumbnail-to-preview flight** (#35). A thumbnail click is an instant
  seek by design.
