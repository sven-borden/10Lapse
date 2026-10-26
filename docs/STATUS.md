# 10Lapse status

Updated 2026-10-26. Roadmap: the pinned #59 and the milestones. Working agreement: `CLAUDE.md`.

## Now

2026-10-25: **3.1.0.0 is live on the Store** (tag `v3.1.0` on `04e6f74`, PR #142
for #141; catalog revision 10:14 UTC, Sven confirmed 2026-10-26), without its
listing text (owner items). 3.0 went live 2026-10-21 (`v3.0.0` on `7c63de5`).
The borden.ch 10Lapse page says 3.1 is live (status as of 26 Oct 2026).
Milestone 6 keeps only #30, which waits for Sven's photos. Next: what Health
and the first reviews bring up.
`docs/test-matrix.md` is a checklist for the live build (5.22 crop and pan, 5.23 music, 5.24 frame
blending: nobody has seen or heard them on real shoots yet). 2026-10-24: a 59 s motion reel of 3.1 for borden.ch
is in `docs/showreel/` (PR #143, Sven's request; it passes its `source/flashes.py`, WCAG 2.3.1).

## Waves

Wave rules are in `CLAUDE.md`. A wave starts when everything it depends on is
merged. Model: **O** is Opus 5.5, **S** is Sonnet 5.5.

Waves 0 to 24 (3.0) are done; milestone 4 stays open only for the #90 watch.

3.1 (milestone 6), planned 2026-10-21. Every 3.1 feature adds a settings group
to `ProjectSettings`/`Project`; two issues may run together when that additive
change is their only overlap (the second PR to merge rebases). A new group goes
into the `.10lapse` format with a round-trip test. Every new string ships in all six languages. Read first: PR #66
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
`blend` group, `FrameRenderer.DrawBlend`, `BlendedPhotoSource`).

- Waves 25 to 28: done. #51 GIF (PR #132), #52 save and reopen (#133), #49
  hold, loop, boomerang (#134), #45 deflicker (#135), #46 crop and pan (#136),
  #50 music (#137), #47 frame blending (#138); #139 rating question (#140).
- #48 stamp and title: closed, not planned (Sven, 2026-10-24).
- #30 sample sequence once Sven picks the photos.

## Owner items (Sven)

Nothing blocks the build loop.

- Paste the six 3.1 listings (`docs/store/listing*.md`, every section) in a
  Partner Center submission that changes the listing only: on 2026-10-26 the
  live Description, Short description and What's new are still the 3.0 copy in
  all six languages, with "on Windows 10, the Store offers the older 10Lapse
  2.1.1". #141 is closed (Sven, 2026-10-25), so this line is the only record;
  check the catalog
  (`displaycatalog.mp.microsoft.com/v7.0/products?bigIds=9NBN2XH6S4D8&market=US&languages=en-US`)
  for the 3.1 text, then drop it.
- Watch Partner Center Health for start-up crashes (#90); traces are not
  symbolicated, so a count is all it gives.
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
  `FrameLookTests.TheReframePathAppliesTheSameLook`); a new drawing step must go
  through that call too or update the path. #46 did: `PhotoRect` takes the framed
  placement (`FrameRenderer.Placement`), so the reframe moves the framed photo.
- 2026-10-24 (#45, PR #135): exposure lives on the frame (`Frame.Exposure`, in
  `.10lapse`), never in an app-data cache; the `look` group is project-only.
  The correction belongs to the photo, so holds, loops and boomerangs repeat
  it. Autosave waits at most 10 s. Details: PR #135 "Decisions".
- 2026-10-24 (Sven): #46 built by the main session ("no agent, do yourself"),
  no tests run while building and no review pass ("just commit, push, gate,
  merge"); the gate before the merge still runs every test. #50 ("go with
  next") and #47 ("let's resume where we are") were built the same way.
- 2026-10-24 (#46, PR #136): a framing is a rectangle in shares of the frame,
  4x zoom at most; the move runs over the photos' frames (holds stand still,
  plays and a boomerang are one move). A zoomed export decodes larger and
  fewer frames ahead. Details: PR #136.
- 2026-10-24 (#50, PR #137): music is MP4 only, never loops (silence after a
  short song), fades up to 10 s; the audio is written up to each frame's start
  before the frame (the MP4 sink must not wait on either stream). Details: PR
  #137.
- 2026-10-24 (#47, PR #138): a blend is of photos, not video frames: the
  average in linear light of the 2 to 16 photos around each photo, after its
  deflicker, so holds, plays and the boomerang repeat blended photos and crop
  and pan frames the blend. Details: PR #138 "Decisions".
- `ProjectChanges` bits taken: 9 `Sequence`, 10 `Look`, 11 `Crop`, 12 `Music`,
  13 `Blend`.
- 2026-10-24 (#139, PR #140): the rating ask is an inline card in the finished
  state; the Store is called only after Yes, No closes it; once per version,
  recorded only when the card arrives with the window in front. Details: PR
  #140.
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
- 2026-10-25 (Sven): the 3.0 Store trailer stays for 3.1; no Store cut of the
  motion reel. It shows 3.0 only and fails `docs/showreel/source/flashes.py`
  from 1.6, 3.8, 10.8 and 20.2 s (worst 100 % of a 10° field, limit 25 %).

## Merged

Merged work: `gh pr list --state merged`; each PR's "What the next issues should
know" has the details. Debug launch switches are documented in
`src/TenLapse.App/Debugging/*.cs`, each `scripts/*.ps1` in its own header, and
the benchmarks in `docs/benchmarks.md`.
