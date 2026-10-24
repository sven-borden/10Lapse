# 10Lapse 3.1 Store listing (English, United States)

The text Sven pastes into Partner Center for the 3.1 update (#141; 3.0 was #42): Apps and games, 10Lapse, the 3.1
submission, Store listings, English (United States). The five translations (`listing.{fr,de,es,it,pt-BR}.md`) have
the same fields and order.

Each field below says its limit and this draft's length. The limits come from Microsoft Learn, checked 2026-10-20:
[Add and edit Store listing info](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/add-and-edit-store-listing-info),
[Screenshots and images](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images)
and the [Store Policies](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies). A limit marked
**verify** is not on those pages; check it against the form. Lengths count characters as Partner Center does, a line
break as one. Copy each field from inside its box: the boxes are plain text, without Markdown.

The Windows 10 sentence of the first submission ("the Store offers 2.1.1") is in none of the six files: Windows 10 is
not supported (2026-10-21). The live listings still have it, so the 3.1 paste replaces every field in all six
languages, the description included, even where only a few words changed.

The text says only what 3.1 does. Not in 3.1, so not in the listing: stabilization, a stamp or title (#48, not
planned), a watch folder, 8K, a sample sequence (#30), music in a GIF (a GIF has no sound). The rating question
(#139) is not a feature and is not named.

## Description

Limit 10,000 characters, plain text (no HTML, no URLs). This draft: 4,112.

```text
Turn a folder of photos into a smooth timelapse video, up to 4K, or an animated GIF.

You shot a sunset, a storm rolling in or a build from a tripod, one photo every few seconds, and now you have a folder of hundreds or thousands of photos. 10Lapse plays them back as a video: drop the folder on the window, check the preview, and export an MP4 or a GIF.

How it works
1. Choose a folder, pick photos, or drag them onto the window. 10Lapse reads JPEG, PNG, TIFF, HEIC and the RAW files of most cameras, and puts them in the order they were taken.
2. Set the speed one of three ways: frames per second, the length the video should have, or seconds per photo for a slideshow. The numbers are always on screen, for example "742 photos → 24.7 s at 30 fps".
3. Pick the shape: match your photos, 16:9 for YouTube and TV, 9:16 for phone stories and reels, 1:1, 4:5 or 4:3. Fit keeps the whole photo; Fill crops the edges so there are no bars.
4. Export an MP4 at 720p, 1080p, 1440p or 4K, in H.264, or in HEVC where the PC can encode and play it. Or export an animated GIF that loops, Small, Medium or Large (320, 480 or 640 pixels on the long side), at up to 10, 20 or 25 frames per second. The preview draws every frame with the same code as the export, so the video looks like the preview.

Make it look right
- Deflicker smooths the exposure jumps between photos. Where you changed the shutter speed, aperture or ISO during a sunset, it reads those settings from the photos and turns each step into a smooth ramp, while the scene still darkens as it did.
- Frame blending makes each frame the average of 2 to 16 nearby photos, like a long exposure: moving lights leave trails and clouds move smoothly.
- Crop and pan moves from a start framing to an end framing, zooming in up to 4x, smoothly or at an even speed. Drag the framing on the preview to set it.
- Before and After in the preview shows your photos with and without deflicker and frame blending.
- Hold the video's first and last frames for up to 10 seconds each, play the photos up to five times, or play them as a boomerang, out and back.
- Add music under an MP4 (MP3, M4A, WAV, WMA or FLAC): choose where in the song the video starts, and fade it in and out. The song ends where the video ends. A GIF has no sound.

Editing and export
- Scrub through the sequence, step frame by frame, and remove a blurry photo or drag one into place. Ctrl+Z puts it back. Removing a photo takes it out of the video, never off your disk.
- Gap markers in the filmstrip show where photos seem to be missing.
- Frame rates are exact, 29.97 included, from 0.1 to 120 frames per second.
- The Output panel estimates the file size before you export.
- An MP4 export uses the graphics card's video encoder where the PC has one. The export keeps the PC awake until it is done, keeps going when you minimize the window, and stops within a second when you cancel.
- When the export is ready, play it right there or open its folder.
- If something goes wrong, 10Lapse says what happened and what to do next.

Projects
Save a project as a .10lapse file, which keeps the list of photos, their order and every setting, and open it again later or with a double-click. The start screen lists your recent projects. If 10Lapse closes before you saved, it offers to restore your changes the next time it starts. Photos a project can't find stay in its file, so they come back once their drive is connected again.

Private by design
10Lapse works offline. There is no account, no analytics and no network access: your photos, videos and projects never leave your PC.

Made for Windows 11
10Lapse needs Windows 11 on an x64 or ARM64 PC. Light, dark and high contrast themes follow Windows, and so do animations. Everything works from the keyboard, and screen readers announce every control and the export's progress.

Optional extensions from the Microsoft Store add more photo formats: HEIC photos need HEIF Image Extension (free) and HEVC Video Extensions (paid), and camera RAW files need Raw Image Extension (free). 10Lapse names the one that is missing and links to its Store page.
```

## Short description

Limit 1,000 characters; Microsoft recommends under 270, because some views cut it there. It shows above the
description, so it says the same thing in other words. This draft: 257.

```text
Make a timelapse video from the hundreds of photos of a tripod shoot: set the speed and the shape, smooth the flicker, watch it play, and export an MP4 up to 4K or an animated GIF. 10Lapse works offline, with no account, and your photos never leave your PC.
```

## What's new in this version

Limit 1,500 characters. This draft: 1,220. It is what 3.0 users get with the update, newest first; it says only what
3.1 does and leaves out the rating question (#139).

```text
10Lapse 3.1 adds what a timelapse needs after the shoot.

- Deflicker: smooths the exposure jumps between photos. Where you changed the shutter speed, aperture or ISO during a sunset, it turns each step into a smooth ramp.
- Frame blending: each frame is the average of 2 to 16 nearby photos, like a long exposure, for light trails and smooth clouds.
- Crop and pan: move from a start framing to an end framing, zooming in up to 4x, smoothly or at an even speed.
- Animated GIF: export a GIF that loops, in three sizes, at up to 25 frames per second.
- Music under an MP4: choose where the song starts and fade it in and out. It ends with the video.
- Hold the first and last frames for up to 10 seconds, play the photos up to five times, or play them as a boomerang.
- Save your work: a .10lapse project file opens again later, the start screen lists recent projects, and if 10Lapse closes before you saved, it offers to restore your changes.
- Ctrl+O now opens a project; Choose folder moved to Ctrl+I.
- Before and After in the preview compares your photos with and without deflicker and frame blending.

As before, 10Lapse works offline and your photos never leave your PC. It needs Windows 11 on an x64 or ARM64 PC.
```

## Product features

Up to 20 features, 200 characters each; the Store adds the bullets. Paste one line per feature box. These 20 are 53
to 118 characters each. 3.1's seven features took the room of 3.0's lines merged in pairs (speed with the numbers on
screen, preview with the filmstrip, the MP4 line with the size estimate, the encoder with minimized export) and of
"Remembers your last speed, frame shape and output", which the description does not need either.

```text
Turns a folder of photos into a timelapse video up to 4K, or an animated GIF
Import a folder or chosen photos, or drag them onto the window; they line up in the order they were taken
Reads JPEG, PNG and TIFF; HEIC and camera RAW with Microsoft Store extensions
Speed by frames per second, by video length, or by seconds per photo, with the photo count and video length on screen
Exact frame rates from 0.1 to 120 fps, 29.97 included
Frame shapes: match the photos, 16:9, 9:16, 1:1, 4:5 or 4:3, with Fit or Fill
Live preview that matches the export, and a filmstrip with gap markers: drag, remove and undo
Deflicker smooths exposure jumps, and turns the exposure steps of a sunset shoot into a smooth ramp
Frame blending averages 2 to 16 nearby photos for light trails and smooth clouds
Crop and pan from a start framing to an end framing, zooming in up to 4x, Smooth or Linear
Hold at start and at end, play the photos up to five times, or play them as a boomerang
Music under an MP4: choose where the song starts, fade it in and out; it ends with the video
Save projects as .10lapse files, reopen recent ones from the start screen, restore unsaved changes after a crash
MP4 in H.264 (or HEVC where the PC supports it) at 720p, 1080p, 1440p or 4K, three quality levels, size estimate first
Animated GIF that loops, in three sizes, at up to 10, 20 or 25 frames per second
Exports MP4 on the graphics card's video encoder where there is one; keeps going when minimized, with the PC awake
Play the finished video or GIF in the app, or open its folder
Clear messages that say what went wrong and what to do next
Works offline: no account, no analytics, photos never leave your PC
Light and dark themes, high contrast, keyboard shortcuts, screen reader support
```

## Search terms

At most 7 terms (Store Policies: no more than seven unique terms or phrases, relevant to the app, no other product's
name). **Verify** in the form: it has allowed 30 characters per term and 21 words across all terms. These 7 are 9 to
23 characters, 15 words in all. The first five are #42's list.

```text
timelapse maker
time lapse
photos to video
stop motion
slideshow
image sequence to video
timelapse
```

## Additional system requirements

Up to 11 items each for minimum and recommended hardware, 200 characters each; the Store adds the bullets. The
Properties page's own system requirements (Windows 11, x64 and ARM64) come from the package. These items are 32 to
113 characters.

Minimum hardware:

```text
Windows 11 on an x64 or ARM64 PC
Optional, for HEIC photos: HEIF Image Extension (free) and HEVC Video Extensions (paid) from the Microsoft Store
Optional, for camera RAW photos: Raw Image Extension (free) from the Microsoft Store
Optional, for HEVC video: HEVC Video Extensions (paid) and a graphics card that encodes HEVC
```

Recommended hardware:

```text
A graphics card with a hardware video encoder, for faster export
8 GB of memory or more for 4K export
```

What each extension adds, for Sven's answers to reviews (#44), not for a field:

- HEIF Image Extension (free) and HEVC Video Extensions (paid): HEIC and HEIF photos from phones.
  Without them, those photos are skipped and an InfoBar names the missing extension with a button to its Store page.
- Raw Image Extension (free): RAW files from most cameras (CR2, CR3, NEF, ARW, RAF, ORF, RW2 and more). DNG opens
  without it. A RAW with a JPEG twin of the same name imports once, as the JPEG.
- HEVC Video Extensions (paid): HEVC (H.265) output. 10Lapse offers HEVC only when the PC can both encode it and play it
  back; otherwise the Format list has H.264 only.

## Screenshots

Desktop: PNG, 1366 x 768 or larger in landscape or portrait, up to 3840 x 2160, under 50 MB each, up to 10. Captions
200 characters each. Keep key content in the top two thirds (the Store may lay text over the bottom third).
`scripts/store-screenshots.ps1` makes them into `docs/store/screenshots/<language>/` (Git LFS, committed); `-Language fr`
(default `en-US`) makes the set for another listing language (#113), with that language's number format (`-Region`, #129).

The uploaded set is the one `pwsh -File scripts/store-screenshots.ps1` makes from the generated sample photos
(`--sample-plain` sunsets at 4500 x 3000, a camera's size, so the export shots show real speeds; about 2 GB of
temporary data, deleted at the end). Sven chose them over a real shoot (2026-10-20) as the more polished set.
`-PhotoFolder <a tripod shoot>` still imports a real set instead, and `-SlideshowFolder <about 24 photos>` feeds the
Per photo shot (06); without it, that shot always uses 24 generated photos.

Upload six, in this order; captions are 55 to 91 characters.

| File | Size | Caption |
|---|---|---|
| `02-editing.png` | 1920 x 1080 | `See the whole sequence play, with the photo count, speed and video length always on screen.` |
| `03-vertical.png` | 1920 x 1080 | `Frame it 9:16 for phone stories and reels, or 16:9, 1:1, 4:5 or 4:3.` |
| `04-exporting.png` | 1920 x 1080 | `Export an MP4 up to 4K and watch each frame as it is rendered, with the time left.` |
| `05-finished.png` | 1920 x 1080 | `Play the finished video right away, or open its folder.` |
| `06-per-photo.png` | 1920 x 1080 | `Make a slideshow with seconds per photo, or set frames per second or the video's length.` |
| `01-first-launch.png` | 1920 x 1080 | `Drop a folder of photos to start. Your photos never leave your PC.` |

The editing shot leads because it shows the job; the start screen comes last because it shows no photos. The script
also makes 07-settings and 08-narrow; do not upload them (Settings shows no photos; the narrow shot is portrait at
768 x 1366, below #42's 1920 x 1080).

## Until 3.0 is public (#7)

A listing-only edit to the 2.1.1 description, no package needed: replace its last line, "BETA REALEASE! SOME
FUNCTIONALITIES MAY NOT WORK CORRECTLY", with the line below. The 3.0 description above replaces the whole text once
3.0 ships. This draft: 63 characters.

```text
10Lapse 3.0 is being rebuilt from the ground up for Windows 11.
```

## Copyright and trademark info

**Verify** the limit (it has been 200 characters). This draft: 40. The repo has no license file and reserves all
rights (README.md); the 2.x listing's earlier years can go in front if Sven wants them.

```text
© 2026 Sven Borden. All rights reserved.
```

## Fields to leave as they are

- Product name: 10Lapse (the reserved name and the manifest's `DisplayName`).
- Short title and voice title: empty (Xbox and voice only). Sort title: Ten Lapse (Sven's call), so a search for the name
  spelled out finds it.
- Additional license terms: empty (the Store's standard license applies).
- Trailer: the 30 s showreel in `docs/store/showreel/` (#43), with `thumbnail.png` and the captions in
  `thumbnail-captions.md`. Store logos: #37 (icon) owns them.
- Outside the listing, on the Properties page: the privacy policy URL is
  `https://github.com/sven-borden/10Lapse/blob/master/PRIVACY.md` (#7), and support
  is `https://github.com/sven-borden/10Lapse/issues`, the page *Send feedback* opens.
