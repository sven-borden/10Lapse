# 10Lapse status

Updated 2026-10-24. Roadmap: the pinned #59 and the milestones. Working agreement: `CLAUDE.md`.

## Now

2026-10-21: **3.0 is live on the Store at full rollout** (tag `v3.0.0`, on
`7c63de5`). Now: 3.1, wave 28 done 2026-10-24 (#47 frame blending, PR #138),
then #139 rating question (PR #140); wave 29, #48 stamp and title, is next when
Sven says so. `docs/test-matrix.md`
is a checklist for the live build (5.22 crop and pan, 5.23 music, 5.24 frame
blending: nobody has seen or heard them on real shoots yet).

## Waves

Wave rules are in `CLAUDE.md`. A wave starts when everything it depends on is
merged. Model: **O** is Opus 5.5, **S** is Sonnet 5.5.

Waves 0 to 24 (3.0) are done; milestone 4 stays open only for the #90 watch.

3.1 (milestone 6), planned 2026-10-21. Every 3.1 feature adds a settings group
to `ProjectSettings`/`Project`; two issues may run together when that additive
change is their only overlap (the second PR to merge rebases). #52 goes first so
each later feature adds its group to the `.10lapse` format with a round-trip
test; #51 gives the encode stage a second format, which #50's audio stream
builds on. Every new string ships in all six languages. Read first: PR #66
(`Project`, snapshot, single writer), #83 and #111 (`settings.json`,
`AppFolders`: every path the app writes), #133 (`.10lapse`: how a settings
group is added; `settings.json` is read-merge-write across windows), #65, #74
and #132 (export pipeline, `FrameRenderer`, the GIF path), #80 (`OutputPlan`:
export with the effective output), #77 (preview), #100 (motion, the reframe
exception), #134 (`FrameSequence`: holds, loop, boomerang, shared decode of a
held photo), #135 (the `look` group, per-photo exposure on the frame, the
measuring pass and the export's wait for it), #136 (`Framing`, the `crop` group,
`FrameLook.View`, a geometry step in `Draw`, the export's zoomed decode), #137
(`MusicTrack` on the export settings, the AAC stream interleaved per frame, the
preview's music, the inspector's fifth section), #138 (`BlendSettings`, the
`blend` group, `FrameRenderer.DrawBlend`, `BlendedPhotoSource`, a stamp's place
for #48).

- Wave 25: done. #51 GIF export (PR #132), #52 save and reopen (PR #133).
- Wave 26: done. #49 hold, loop, boomerang (PR #134), #45 deflicker (PR #135,
  merged 2026-10-24 after master was merged in; gate 3,710 passed).
- Wave 27: done. #46 crop and pan (PR #136), #50 music (PR #137).
- Wave 28: done. #47 frame blending (PR #138).
- Between waves: done. #139 rating question (PR #140, Sven 2026-10-24; gate
  3,822 passed; checked minimized and restored in the real app).
- Wave 29: #48 stamp and title (O). #45 to #48 all draw in `FrameRenderer`, so
  they never run together.
- #30 sample sequence once Sven picks the photos.

## Owner items (Sven)

Nothing blocks the build loop.

- Watch Partner Center Health for start-up crashes (#90); traces are not
  symbolicated, so a count is all it gives.
- The live listing text (all six languages) still has the sentence that
  Windows 10 keeps 2.1.1; the `listing*.md` files no longer do. Fix it in the
  first listing update, with no resubmission just for that.
- A native read of the fr, de, es, it and pt-BR texts where he can; whether
  Portugal gets a pt-PT listing reusing the pt-BR text (else it may show
  English); a real-timelapse trailer later (#43's original brief).
- Later: #58 pricing. #30: *Try a sample* comes back with Sven's photos.

## Decisions

- 2026-10-20 (Sven): translations are done by Opus: the text is the product in
  those markets and no native reviewer is on hand.
- 2026-10-20 (Sven): Git LFS holds the videos and Store screenshots under
  `docs/` (`.gitattributes`). GitHub Free includes 10 GB of LFS storage and 10
  GB of downloads a month, counted against the owner's account.
- 2026-10-19 (#35, PR #100): the one exception to "preview and export draw
  through the same code": during a 400 ms reframe
  `PreviewRenderLoop.DrawPicture` draws the photo by hand, with geometry from
  `FrameRenderer.Layout`, instead of `FrameRenderer.Draw`. #45's correction
  shows there (it calls `FrameRenderer.DrawPhoto`; tested by
  `FrameLookTests.TheReframePathAppliesTheSameLook`); #46 to #48 must go through
  that call too or update the path. #46 did: `PhotoRect` takes the framed
  placement (`FrameRenderer.Placement`), so the reframe moves the framed photo.
- 2026-10-24 (#45, PR #135): exposure measurements live on the frame
  (`Frame.Exposure`, saved in `.10lapse`), never in an app-data cache; the
  `look` group is project-only, not in `settings.json`. The correction belongs
  to the photo, so a hold, loop or boomerang repeats it. Cancelling a pass the
  app started keeps the Deflicker setting; cancelling right after the user
  turned it on turns it off. Autosave waits at most 10 s. `ProjectChanges` bits
  9 (`Sequence`) and 10 (`Look`) are taken. The reasoning: PR #135's
  "Decisions".
- 2026-10-24 (Sven): #46 built by the main session ("no agent, do yourself"),
  no tests run while building and no review pass ("just commit, push, gate,
  merge"); the gate before the merge still runs every test. #50 ("go with
  next") and #47 ("let's resume where we are") were built the same way.
- 2026-10-24 (#46, PR #136): a framing is a rectangle of the frame's shape in
  shares of the frame (any aspect or resolution), 4x zoom at most; the move
  runs over the photos' frames, holds stand still, plays and a boomerang are
  one move; a steady zoom about the point both framings share; Video, Start
  and End are the preview's alone. A zoomed export decodes larger and fewer
  frames ahead (by the zoom squared, at least 2). `ProjectChanges` bit 11
  (`Crop`) is taken; the full `Project` constructor ends with `crop`.
- 2026-10-24 (#50, PR #137): music is MP4 only (a GIF has no sound); it starts
  with the video at a chosen point of the song, never loops (silence after a
  short song), fades on square curves up to 10 s; the audio track is as long as
  the video and written up to each frame's start before it (the MP4 sink must
  not wait on either stream). The file stores the song's full and relative
  paths and its length. `ProjectChanges` bit 12 (`Music`) is taken; the full
  `Project` constructor ends with `music`.
- 2026-10-24 (#47, PR #138): a blend is of photos, not video frames: each
  photo is the average in linear light of the 2 to 16 photos around it
  (centred, one more before when even, fewer at the ends), each with its own
  deflicker stops, so holds, plays and the boomerang repeat blended photos and
  crop and pan frames the blend. Strength mixes the blend with the photo. A
  group of its own (`blend`, bit 13; the full `Project` constructor ends with
  `blend`). A blended export decodes each photo once and holds at most 16 4K
  photos' pixels (a deep zoom is decoded softer to stay within it).
- 2026-10-24 (#139, PR #140): the rating ask is an inline card in the finished
  state, "Do you like 10Lapse?"; the Store is called only after Yes (its dialog,
  then the review page, then an InfoBar), No closes it. Once per version,
  recorded only when the card arrives with the window in front
  (`RatingPrompt.Arrive`; `RememberedSettings.TryRecordRatingAsked` reads the
  stored value, so a second window never asks again).
- Timing tests (PR #102): prove a timeout by its effect and give ceilings a wide
  margin. If one flakes, look first at `ImportPerformanceTests` and the
  `EncoderProbeTests` cached-load ratio.
- Translation rules (glossaries, forms of address and Store-name tables are in
  the PR bodies: de #122, fr #123, es #126, it #127, pt-BR #128; English Store
  names #125): Ctrl+Z puts back (fr remet, es devolver, it rimettere, pt-BR
  devolve), never the Redo or Restore verb; "remove" is out of the project,
  "delete" only files on disk; free/paid labels never agree with a Store name;
  `_many` where millions take de/di; Store names come from
  `displaycatalog.mp.microsoft.com` (ProductTitle); es uses "video" and the MX
  names for a single neutral file.

## Merged

Merged work: `gh pr list --state merged`; each PR's "What the next issues should
know" has the details. Debug launch switches are documented in
`src/TenLapse.App/Debugging/*.cs`, each `scripts/*.ps1` in its own header, and
the benchmarks in `docs/benchmarks.md`.
