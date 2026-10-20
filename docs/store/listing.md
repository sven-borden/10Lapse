# 10Lapse 3.0 Store listing (English, United States)

The text Sven pastes into Partner Center for the 3.0 update (#42): Apps and games, 10Lapse, the 3.0 submission,
Store listings, English (United States). The package is English only, so the listing is too (#62, #39); a listing
in another language comes with that language's resource files.

Each field below says its limit and this draft's length. The limits come from Microsoft Learn, checked 2026-10-20:
[Add and edit Store listing info](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/add-and-edit-store-listing-info),
[Screenshots and images](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images)
and the [Store Policies](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies). A limit marked
**verify** is not on those pages; check it against the form. Lengths count characters as Partner Center does, a line
break as one. Copy each field from inside its box: the boxes are plain text, without Markdown.

The text says only what 3.0 does. Not in 3.0, so not in the listing: music or sound, deflicker, stabilization, GIF
output, saving a project, a watch folder, 8K, a sample sequence (#30). Those belong to 3.1 issues; add them here when
they ship.

## Description

Limit 10,000 characters, plain text (no HTML, no URLs). This draft: 2,606.

```text
Turn a folder of photos into a smooth timelapse video, up to 4K.

You shot a sunset, a storm rolling in or a build from a tripod, one photo every few seconds, and now you have a folder of hundreds or thousands of photos. 10Lapse plays them back as a video: drop the folder on the window, check the preview, and export an MP4.

How it works
1. Choose a folder, pick photos, or drag them onto the window. 10Lapse reads JPEG, PNG, TIFF, HEIC and the RAW files of most cameras, and puts them in the order they were taken.
2. Set the speed one of three ways: frames per second, the length the video should have, or seconds per photo for a slideshow. The numbers are always on screen, for example "742 photos → 24.7 s at 30 fps".
3. Pick the shape: match your photos, 16:9 for YouTube and TV, 9:16 for phone stories and reels, 1:1, 4:5 or 4:3. Fit keeps the whole photo; Fill crops the edges so there are no bars.
4. Export an MP4 at 720p, 1080p, 1440p or 4K, in H.264, or in HEVC where the PC can encode and play it. The preview draws every frame with the same code as the export, so the video looks like the preview.

Editing and export
- Scrub through the sequence, step frame by frame, and remove a blurry photo or drag one into place. Ctrl+Z puts it back. Removing a photo takes it out of the video, never off your disk.
- Gap markers in the filmstrip show where photos seem to be missing.
- Frame rates are exact, 29.97 included, from 0.1 to 120 frames per second.
- The Output panel estimates the file size before you export.
- The export uses the graphics card's video encoder where the PC has one. It keeps the PC awake until the video is done, keeps going when you minimize the window, and stops within a second when you cancel.
- When the video is ready, play it right there or open its folder.
- If something goes wrong, 10Lapse says what happened and what to do next.

Private by design
10Lapse works offline. There is no account, no analytics and no network access: your photos and videos never leave your PC.

Made for Windows 11
10Lapse 3.0 needs Windows 11 on an x64 or ARM64 PC; on Windows 10, the Store offers the older 10Lapse 2.1.1 instead. Light, dark and high contrast themes follow Windows, and so do animations. Everything works from the keyboard, and screen readers announce every control and the export's progress.

Optional extensions from the Microsoft Store add more photo formats: HEIC photos need HEIF Image Extensions (free) and HEVC Video Extensions (paid), and camera RAW files need Raw Image Extension (free). 10Lapse names the one that is missing and links to its Store page.
```

## Short description

Limit 1,000 characters; Microsoft recommends under 270, because some views cut it there. It shows above the
description, so it says the same thing in other words. This draft: 218.

```text
Make a timelapse video from the hundreds of photos of a tripod shoot: set the speed and the shape, watch it play, and export an MP4 up to 4K. 10Lapse works offline, with no account, and your photos never leave your PC.
```

## What's new in this version

Limit 1,500 characters. This draft: 1,286. It names what the reviews of 2.1.1 reported (#44) and says only what 3.0
does; it does not claim a crash rate.

```text
10Lapse 3.0 is rebuilt from the ground up for Windows 11.

- Rewritten from scratch, so the code behind the 2.1.1 crashes is gone. When something goes wrong, 10Lapse says what happened and what to do next.
- Imports HEIC and .jpeg photos. HEIC needs HEIF Image Extensions (free) and HEVC Video Extensions (paid) from the Microsoft Store; 10Lapse tells you which one is missing.
- Export up to 4K, in H.264, or HEVC where the PC supports it, with progress, the time left and a Cancel button that stops the export within a second.
- Keeps working when you switch away or minimize: the export goes on, with progress on the taskbar and a notification when the video is ready.
- A new Per photo speed for slideshows, beside frames per second and video length.
- A live preview and a filmstrip: scrub, step, remove or reorder photos, and undo.
- Frame shapes for TVs, phones and social posts: 16:9, 9:16, 1:1, 4:5, 4:3 or the shape of your photos.
- Your last speed, frame shape and output are remembered for the next project.
- Light and dark themes, keyboard shortcuts, screen reader support and high contrast.
- Private: no account, and now no network access at all; your photos never leave your PC.

3.0 needs Windows 11 on an x64 or ARM64 PC. On Windows 10 the Store keeps 10Lapse 2.1.1.
```

## Product features

Up to 20 features, 200 characters each; the Store adds the bullets. Paste one line per feature box. These 19 are 36
to 100 characters each.

```text
Turns a folder of photos into a timelapse video, up to 4K
Import a folder, chosen photos, or drag them onto the window
Reads JPEG, PNG and TIFF; HEIC and camera RAW with Microsoft Store extensions
Puts photos in the order they were taken; drag a thumbnail to move it
Speed by frames per second, by video length, or by seconds per photo
Exact frame rates from 0.1 to 120 fps, 29.97 included
Photo count, speed and video length always on screen
Frame shapes: match the photos, 16:9, 9:16, 1:1, 4:5 or 4:3, with Fit or Fill
Live preview that matches the exported video
Filmstrip with thumbnails, gap markers for missing photos, remove and undo
MP4 in H.264 (or HEVC where the PC supports it) at 720p, 1080p, 1440p or 4K, in three quality levels
File size estimate before you export
Exports on the graphics card's video encoder where the PC has one
Keeps exporting when minimized, and keeps the PC awake until it is done
Play the finished video in the app or open its folder
Clear messages that say what went wrong and what to do next
Works offline: no account, no analytics, photos never leave your PC
Light and dark themes, high contrast, keyboard shortcuts, screen reader support
Remembers your last speed, frame shape and output
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
Optional, for HEIC photos: HEIF Image Extensions (free) and HEVC Video Extensions (paid) from the Microsoft Store
Optional, for camera RAW photos: Raw Image Extension (free) from the Microsoft Store
Optional, for HEVC video: HEVC Video Extensions (paid) and a graphics card that encodes HEVC
```

Recommended hardware:

```text
A graphics card with a hardware video encoder, for faster export
8 GB of memory or more for 4K export
```

What each extension adds, for Sven's answers to reviews (#44), not for a field:

- HEIF Image Extensions (free) and HEVC Video Extensions (paid): HEIC and HEIF photos from phones.
  Without them, those photos are skipped and an InfoBar names the missing extension with a button to its Store page.
- Raw Image Extension (free): RAW files from most cameras (CR2, CR3, NEF, ARW, RAF, ORF, RW2 and more). DNG opens
  without it. A RAW with a JPEG twin of the same name imports once, as the JPEG.
- HEVC Video Extensions (paid): HEVC (H.265) output. 10Lapse offers HEVC only when the PC can both encode it and play it
  back; otherwise the Format list has H.264 only.

## Screenshots

Desktop: PNG, 1366 x 768 or larger in landscape or portrait, up to 3840 x 2160, under 50 MB each, up to 10. Captions
200 characters each. Keep key content in the top two thirds (the Store may lay text over the bottom third).
`scripts/store-screenshots.ps1` makes them into `dist/store-screenshots/<language>/` (git-ignored); `-Language fr`
(default `en-US`) makes the set for another listing language (#113).

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
- Trailers and Store logos: #43 (trailer) and #37 (icon) own them.
- Outside the listing, on the Properties page: the privacy policy URL is
  `https://github.com/sven-borden/10Lapse/blob/master/PRIVACY.md` (#7), and support
  is `https://github.com/sven-borden/10Lapse/issues`, the page *Send feedback* opens.
