---
name: 10Lapse
description: Turn a folder of photos into a timelapse video, natively on Windows 11.
colors:
  golden-hour-amber: "#E8962E"
  deep-amber-indicator: "#B5620D"
  ember-link: "#9A5208"
  night-ink-on-amber: "#1F1404"
  mica-light: "#F3F2F0"
  layer-light: "#FAF9F7"
  card-light: "#FEFDFC"
  control-stroke-light: "#DCDAD5"
  divider-light: "#E7E5E1"
  segment-track-light: "#F0EEEB"
  track-light: "#E4E2DE"
  ink: "#1C1B19"
  ink-secondary: "#5D5A55"
  golden-hour-amber-dark: "#F0A54A"
  ember-link-dark: "#F3B05C"
  mica-dark: "#1F1E1D"
  layer-dark: "#282726"
  card-dark: "#302F2D"
  control-fill-dark: "#363431"
  control-stroke-dark: "#4A4743"
  divider-dark: "#393735"
  segment-track-dark: "#2E2D2B"
  track-dark: "#3F3D3A"
  ink-dark: "#F4F2EE"
  ink-secondary-dark: "#C3BFB8"
  success: "#0F7B0F"
  success-fill: "#DFF6DD"
  caution: "#9D5D00"
  caution-fill: "#FFF7DB"
  critical: "#C42B1C"
  critical-fill: "#FDE7E9"
  success-dark: "#6CCB5F"
  success-fill-dark: "#26381F"
  caution-dark: "#FCE100"
  caution-fill-dark: "#3B321A"
  critical-dark: "#FF99A4"
  critical-fill-dark: "#442726"
  letterbox: "#0C0C0D"
  daylight: "#C4E0F3"
  golden: "#F6CF86"
  amber: "#F29A55"
  rose: "#D9677A"
  dusk: "#6D5A9E"
  blue-hour: "#2E3A72"
  night: "#10162E"
typography:
  caption:
    fontFamily: "Segoe UI Variable Text, Segoe UI, system-ui, sans-serif"
    fontSize: "12px"
    fontWeight: 400
    lineHeight: "16px"
    fontFeature: "tnum"
  body:
    fontFamily: "Segoe UI Variable Text, Segoe UI, system-ui, sans-serif"
    fontSize: "14px"
    fontWeight: 400
    lineHeight: "20px"
  body-strong:
    fontFamily: "Segoe UI Variable Text, Segoe UI, system-ui, sans-serif"
    fontSize: "14px"
    fontWeight: 600
    lineHeight: "20px"
  subtitle:
    fontFamily: "Segoe UI Variable Display, Segoe UI, system-ui, sans-serif"
    fontSize: "20px"
    fontWeight: 600
    lineHeight: "28px"
    fontFeature: "tnum"
  title:
    fontFamily: "Segoe UI Variable Display, Segoe UI, system-ui, sans-serif"
    fontSize: "28px"
    fontWeight: 600
    lineHeight: "36px"
  title-large:
    fontFamily: "Segoe UI Variable Display, Segoe UI, system-ui, sans-serif"
    fontSize: "40px"
    fontWeight: 600
    lineHeight: "52px"
    fontFeature: "tnum"
  display:
    fontFamily: "Segoe UI Variable Display, Segoe UI, system-ui, sans-serif"
    fontSize: "68px"
    fontWeight: 600
    lineHeight: "92px"
    letterSpacing: "-0.02em"
rounded:
  thumb: "3px"
  control: "4px"
  card: "6px"
  overlay: "8px"
  round: "999px"
spacing:
  xxs: "2px"
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "16px"
  xl: "20px"
  2xl: "28px"
  3xl: "40px"
  4xl: "64px"
components:
  button-accent:
    backgroundColor: "{colors.golden-hour-amber}"
    textColor: "{colors.night-ink-on-amber}"
    typography: "{typography.body-strong}"
    rounded: "{rounded.control}"
    padding: "0 14px"
    height: "32px"
  button-accent-hover:
    backgroundColor: "#EEA444"
    textColor: "{colors.night-ink-on-amber}"
  button-accent-pressed:
    backgroundColor: "#D78722"
    textColor: "#3A2508"
  button-standard:
    backgroundColor: "{colors.card-light}"
    textColor: "{colors.ink}"
    typography: "{typography.body}"
    rounded: "{rounded.control}"
    padding: "0 12px"
    height: "32px"
  button-standard-hover:
    backgroundColor: "#F6F5F3"
    textColor: "{colors.ink}"
  button-standard-pressed:
    backgroundColor: "{colors.segment-track-light}"
    textColor: "{colors.ink-secondary}"
  button-subtle:
    backgroundColor: "transparent"
    textColor: "{colors.ink}"
    typography: "{typography.body}"
    rounded: "{rounded.control}"
    padding: "0 10px"
    height: "32px"
  segmented-track:
    backgroundColor: "{colors.segment-track-light}"
    rounded: "5px"
    padding: "2px"
    height: "32px"
  segmented-selected:
    backgroundColor: "{colors.card-light}"
    textColor: "{colors.ink}"
    typography: "{typography.body-strong}"
    rounded: "3px"
  select:
    backgroundColor: "{colors.card-light}"
    textColor: "{colors.ink}"
    typography: "{typography.body}"
    rounded: "{rounded.control}"
    padding: "0 8px"
    height: "32px"
  toggle-on:
    backgroundColor: "{colors.deep-amber-indicator}"
    textColor: "{colors.card-light}"
    rounded: "{rounded.round}"
    width: "40px"
    height: "20px"
  play-button:
    backgroundColor: "{colors.card-light}"
    textColor: "{colors.ink}"
    rounded: "{rounded.round}"
    size: "36px"
  filmstrip-thumb:
    rounded: "{rounded.thumb}"
  infobar-caution:
    backgroundColor: "{colors.caution-fill}"
    textColor: "{colors.ink}"
    typography: "{typography.body}"
    rounded: "{rounded.card}"
    padding: "10px 8px 10px 14px"
  infobar-critical:
    backgroundColor: "{colors.critical-fill}"
    textColor: "{colors.ink}"
    rounded: "{rounded.card}"
    padding: "10px 8px 10px 14px"
  infobar-success:
    backgroundColor: "{colors.success-fill}"
    textColor: "{colors.ink}"
    rounded: "{rounded.card}"
    padding: "10px 8px 10px 14px"
  settings-card:
    backgroundColor: "{colors.card-light}"
    textColor: "{colors.ink}"
    rounded: "{rounded.card}"
    padding: "12px 16px"
---

# Design System: 10Lapse

Reference implementation: the 10Lapse 3.0 design canvas (interactive prototype, screens in light and dark, foundations, motion and components). Strategy and audience live in `PRODUCT.md`; this file is the visual contract for the WinUI 3 rewrite.

## 1. Overview

**Creative North Star: "The Quiet Film Gate"**

10Lapse is the gate a sequence passes through on its way to becoming a film. The gate itself is plain, exact and native to Windows 11; everything with color belongs to the photos moving through it. The chrome is Fluent through and through: Mica behind the title and command bars, a single content layer for the stage and inspector, stock WinUI controls, Segoe UI Variable. It should feel like part of Windows, the way Photos or Snipping Tool do, and then disappear into the job of turning 742 photos into 24.7 seconds of sunset.

Density is calm. One window, one stage, one inspector with five sections (Timing, Frame, Output, Look, Music), one accent button. Numbers are always on screen and always right, set in tabular figures. Light and dark get equal care and follow the Windows setting; high contrast maps every token to a system color.

Motion is cinematic when the user creates something and invisible the rest of the time. Choreography plays on import (the first frame develops like a print while thumbnails file into the strip), during export (frames pass an amber gate as they are encoded) and on finish (one quiet check, then the preview premieres the result). Nothing animates on launch, nothing blocks input, and every movement has a reduced-motion fallback that is a short fade.

This system rejects: the legacy 10Lapse 2.x UI (blurred background photo, three equal accent buttons, grey full-screen overlays, a "Policy!" wall of text on first run); generic SaaS dashboards (hero metrics, card grids, gradient washes); skeuomorphic camera apps (fake dials, film sprockets, leather textures); and pro editors' density.

**Key Characteristics:**
- Native Windows 11 Fluent, one typeface, stock controls.
- Neutral chrome warmed by a hair; the photos carry the color.
- Golden-hour amber marks only the primary action, progress, the playhead and the selected frame.
- Always-visible math: "742 photos → 24.7 s at 30 frames per second".
- 4 px grid, 32 px controls, 20 px stage and inspector padding, 16 px section rhythm.
- Inspector docks right at 320 px from 1000 px wide; below that it folds into a 260 px tabbed panel under the preview (Timing, Frame, Output, Look, Music tabs; the canvas predates the Look (#45) and Music (#50) tabs and still shows three; the Output selects in a row; the export panel and the finished state in two columns, head left and details right, so Cancel export and Back to editing stay in view, with a 32 px gutter; the tab row is hidden while exporting and when finished, which gives those panels its 50 px, unlike the canvas, which keeps it); smallest window 640 × 480, where the panel gives height to the preview down to 152 px (it scrolls). The switch is instant, and Tab order and focus follow the visual order in both layouts.
- Motion tokens: Tap 83 ms, Control 167 ms, Reveal 250 ms, Scene 400 ms, Cinema 700 ms; Enter `cubic-bezier(0.16, 1, 0.3, 1)`, Exit `cubic-bezier(0.7, 0, 0.84, 0)`, Move `cubic-bezier(0.65, 0, 0.35, 1)`, linear for playback and progress.

## 2. Colors

Restrained: warm-tinted neutrals plus one amber, with a day-to-night ramp reserved for the brand.

### Primary
- **Golden Hour Amber** (light `golden-hour-amber`, dark `golden-hour-amber-dark`): the fill of the one accent button per screen (Export video, Choose folder, Play on the finished state), always with Night Ink text. Never a background, never decoration.
- **Deep Amber Indicator** (light `deep-amber-indicator`; dark uses `golden-hour-amber-dark`): every state indicator that must reach 3:1 against the layer: the playhead ring on the selected thumbnail, the scrubber fill, the export progress bar and film gate, toggles that are on, the drag-over border. Golden Hour Amber on the light layer is only 2.26:1, which is why this token exists.
- **Ember Link** (`ember-link`, dark `ember-link-dark`): text actions such as "Try it with a sample sequence" and "Back to editing".
- **Night Ink on Amber** (`night-ink-on-amber`): text and icons on amber, 7.6:1 light and 8.8:1 dark.

### Neutral
- **Mica** (`mica-light`, `mica-dark`): the window ground behind the title bar and command bar.
- **Layer** (`layer-light`, `layer-dark`): the content surface for the stage, inspector and settings page, separated from Mica by one divider.
- **Card** (`card-light`, `card-dark`): settings rows, flyouts, InfoBars, standard button fill in light. In dark, standard controls use `control-fill-dark`.
- **Control Stroke** (`control-stroke-light`, `control-stroke-dark`): the 1 px border of buttons, selects and segmented tracks.
- **Divider** (`divider-light`, `divider-dark`): rules between inspector sections.
- **Segment Track** and **Track** (`segment-track-*`, `track-*`): the recessed ground of segmented controls; the unfilled part of bars and scrubbers.
- **Ink** (`ink`, `ink-dark`): all primary text, 16.4:1 and 13.3:1 on layer.
- **Ink Secondary** (`ink-secondary`, `ink-secondary-dark`): labels, captions, readout detail, 6.5:1 and 8.1:1. There is no lighter grey for text.
- **Letterbox** (`letterbox`): the preview gate behind a frame that has no picture yet or cannot be read. The bars Fit leaves inside the frame are the export's black (#000000), because the preview draws through the export's renderer. Same in both themes.

### Status
Fluent's status set, used only in InfoBars and the finished state, always paired with an icon: **Success** (`success`, `success-fill`, dark variants), **Caution** (`caution`, `caution-fill`), **Critical** (`critical`, `critical-fill`).

### Brand ramp
**Daylight, Golden, Amber, Rose, Dusk, Blue Hour, Night** (`daylight` to `night`): a sunset sequence, the brand's own color story. For the app icon, Store art, the sample sequence and empty-state visuals.

### Named Rules
**The Photos Are The Color Rule.** Chrome stays neutral. Amber covers well under a tenth of any screen, and only on the primary action, progress, the playhead and the selected frame.

**The Indicator Rule.** In light theme, anything that conveys state without text uses Deep Amber Indicator, never Golden Hour Amber.

**The Ramp Stays Outside Rule.** The day-to-night ramp never colors UI chrome or text. If a control is rose or dusk, it is wrong.

## 3. Typography

**Display Font:** Segoe UI Variable Display (with Segoe UI, system-ui)
**Body Font:** Segoe UI Variable Text (with Segoe UI, system-ui)

**Character:** One family, the Windows 11 system face. Text optical size up to 14 px, Display above. Hierarchy comes from Fluent's ramp and weight, not from a second typeface.

### Hierarchy
- **Display** (600, 68 px, 92 px, tracked -2%): the wordmark on splash and Store art only.
- **Title large** (600, 40 px, 52 px, tabular): the export percent.
- **Title** (600, 28 px, 36 px): screen titles on first launch and settings.
- **Subtitle** (600, 20 px, 28 px, tabular): the speed readout "742 photos → 24.7 s", panel titles.
- **Body strong** (600, 14 px, 20 px): section headers, the selected segment, accent buttons.
- **Body** (400, 14 px, 20 px): running text, buttons, menu items; prose capped near 46ch on first launch.
- **Caption** (400, 12 px, 16 px, tabular): labels, file details, time codes, hints.

### Named Rules
**The Tabular Rule.** Every number that changes (frame counters, time codes, speed, percent, file size) uses tabular figures, and frame numbers are zero-padded to the width of the total ("0312 of 0742").

**The Sentence Case Rule.** Labels and buttons are sentence case and name the action: "Export video", "Cancel export", never "OK" or "Submit". No exclamation marks, no "Oops".

## 4. Elevation

Mostly flat, Fluent style. Depth comes from tonal layering: Mica ground, a Layer surface with one divider, Cards with a 1 px border and no shadow. Shadows appear only on things that float above content.

### Shadow Vocabulary
- **Flyout** (`box-shadow: 0 8px 28px rgba(28, 27, 25, 0.16), 0 2px 6px rgba(28, 27, 25, 0.06)`; dark `0 8px 28px rgba(0, 0, 0, 0.5), 0 2px 6px rgba(0, 0, 0, 0.3)`): InfoBars floating over the preview, select menus, the first-launch sample frame.
- **Pill** (`box-shadow: 0 1px 2px rgba(28, 27, 25, 0.14), 0 0 0 1px rgba(28, 27, 25, 0.04)`; dark `0 1px 2px rgba(0, 0, 0, 0.4)`): the selected pill inside a segmented control.
- **Focus** (`box-shadow: 0 0 0 1px <ground>, 0 0 0 3px <ink>`): the Windows double focus ring on every control.

### Named Rules
**The Float Only Rule.** Only flyouts cast a shadow. Cards, panels and the inspector never do. If a settings row has a shadow, remove it.

## 5. Components

Stock WinUI 3 controls with these tokens. The filmstrip, scrubber, preview gate and export gate are the only bespoke pieces.

### Buttons
- **Shape:** gently squared corners (4 px), 32 px tall.
- **Accent:** Golden Hour Amber fill, Night Ink text, Body strong, 14 px side padding, faint warm 1 px border. Hover lightens, pressed deepens. One per screen.
- **Standard:** Card fill, Control Stroke border, Ink text. Hover and pressed step toward Segment Track.
- **Subtle:** transparent with an icon, for the command bar (Add photos, Sort, New project, Settings). Hover is a 6% ink wash.
- **Disabled:** 40% opacity, still readable.
- **Focus:** the double ring from Elevation.

### Segmented control
- **Track:** Segment Track fill, Control Stroke border, 5 px corners, 2 px inset, 32 px tall.
- **Selected:** a neutral Card pill with the Pill shadow and Body strong text. Never amber: it marks a setting, not an action. The pill is 1.14:1 (light) and 1.49:1 (dark) against the track, below WCAG 1.4.11's 3:1; Sven kept it as designed (2026-10-20), because the bold label and the radio selection state carry it. In high contrast the pill is Highlight and its label HighlightText (`SegmentPillInk`, Ink in light and dark), since Window and ButtonFace are one color in Windows' contrast themes.
- **Motion:** the pill slides between options (250 ms Move). Arrow keys move the selection.
- **Used for:** speed mode (Per second, Length, Per photo), speed value (24, 25, 30, 50, 60), direction (Forward, Boomerang) and times played (1 to 5, absent for a GIF) under them (#49, with the two hold boxes after), shape (3:2, 16:9, 9:16, 1:1), Fit or Fill, crop and pan's Video, Start and End and its Smooth or Linear under the Frame section's Crop and pan switch (#46; Video, Start and End is the preview's alone, never saved), the narrow-window panel tabs, and Before and After at the end of the transport row (under the preview) while deflicker or frame blending is on (#45, #47; preview only, never saved or exported; Before shows the photos without either, and the switch goes back to After whenever both are off). The canvas predates Before and After; this line and `TransportBar.xaml` describe it.

### Inputs and toggles
- **Select:** Card fill, Control Stroke border, 4 px corners, 32 px. Options the hardware can't encode are left out, with one caption line saying why. The selected entry's 3 px pip in the open list is Deep Amber Indicator, and it applies to every ComboBox in the app (`ComboBoxItemPillFill` in `ControlOverrides.xaml`). The dropdown flyout can't be captured with PrintWindow, so the pip is unverified on screen.
- **Toggle:** 40 × 20 px pill. Off: Ink Secondary outline and knob. On: Deep Amber Indicator track, Card knob. The knob slides 167 ms Move.
- **Slider (the Look section, #45 and #47):** a Caption Ink Secondary label on the left and its value on the right in Caption tabular ("80%", "8"), the slider under them; its filled part and thumb are Ink Secondary (Ink on hover and press), as the filmstrip's size slider, so the inspector never competes with Export. The Look section holds Deflicker (its strength while on) and Frame blending (#47; while on, Photos blended, 2 to 16, and its strength), each a row like the Crop and pan switch. The canvas predates Frame blending; this line and `LookPanel.xaml` describe it.

### Cards and InfoBars
- **Settings card:** Card fill, Divider border, 6 px corners, 12 × 16 px padding, at least 68 px tall: icon, title with caption, control on the right.
- **InfoBar:** floats over the top of the preview (Flyout shadow, 6 px corners) so the layout never shifts. Caution, Critical, Success or neutral fill, an icon, one sentence of what happened, one action, a dismiss button. In from -8 px over 250 ms, out over 167 ms.

### Navigation
- **Title bar:** 40 px on Mica: app icon, "10Lapse" in Caption, project name in Ink Secondary, system caption buttons.
- **Command bar:** 48 px on Mica: subtle buttons left (File, Add photos, New project), Settings icon and the accent Export button right. Settings opens a full page with a Back button, not a dialog. File (#52) is a subtle menu button first, as Windows apps keep their File menu: Open, and Save and Save as once the project has photos.
- **Keyboard (#38, #52):** Ctrl+I Choose folder, Ctrl+Shift+I Choose photos, Ctrl+N New project, Ctrl+E Export video, Ctrl+O Open, Ctrl+S Save, Ctrl+Shift+S Save as (#52 gave Ctrl+O, Choose folder in 3.0, to Open), Space play or pause, the arrows step frames, Delete removes, Ctrl+Z undoes, Escape leaves Settings and dialogs. A shortcut works where its control is on screen and does nothing elsewhere; each control names its keys (tooltip, accelerator key). Tab follows the visual order: the command bar, an InfoBar's buttons, the stage (Play, scrubber, Before and After while deflicker or frame blending is on, filmstrip, thumbnail size), then the inspector (or its tab row and section when narrow). Focus moves with the work: Back on Settings and the Settings button after it, Play when the first photos land, Choose folder after New project, the export's title (a heading named "Exporting" and the file) as an export starts, Play the video when it is ready, Export video after a cancel, the frame on screen after a removal. No animation moves or delays it.
- **Screen readers (#38):** landmarks for the start screen and the stage (main), the filmstrip (region), the inspector (form, "Video settings"; "Export video" while the export panel shows) and Settings (main); headings for the project name, the panels and Settings sections. The export is heard at its start, every ten percent at most and never twice within five seconds, "Finishing the file." and its end; an import at its start, every five seconds at most and its photo count; every InfoBar as it enters.

### Preview gate (signature)
The photo sits in a gate with 6 px corners on the Layer. Changing shape animates the gate's clip-path inset (400 ms Move) so the crop closes around the new shape; Fit or Fill scales the photo inside it. On arrival the first frame develops: blur 14 px to 0, scale 1.03 to 1, opacity in, over 700 ms Enter. Crop and pan (#46; the canvas predates it): with Start or End chosen in the Frame section the preview pauses on the first or last frame and shows the whole frame, dimmed outside the framing with the Letterbox at 62 % (`PreviewPillBrush`), the framing itself a 2 px Deep Amber Indicator on the photo border with four 12 px corner handles (a selection indicator); drag inside moves it, a corner resizes it at the video's shape, and with focus the arrows move it (Shift further) and plus and minus zoom it. Playing goes back to Video, which shows the move as the export draws it.

### Filmstrip (signature)
18 thumbnails at 3:2 with 4 px gaps and 3 px corners (12 in a narrow window), with a caption row: "Frame 0312 of 0742" and file name with capture time. Thumbnails are the items of one selectable list, not buttons (#20): a click selects a frame and shows it, ←/→ do the same from the keyboard, and Shift+←/→ on a thumbnail extends the selection instead of jumping ten frames. Plain Space plays or pauses everywhere, a thumbnail focused included, as the canvas keyboard map has it; Ctrl+Space and Shift+Space select. The playhead thumbnail carries a 2 px Layer gap and a 2 px Deep Amber Indicator ring; a selected frame a 1 px Ink ring, drawn right outside the amber ring when it is also the playhead; frames not yet exported show their picture at 35% opacity, never their rings. A gap marker (a neutral Ink Secondary line and dot in the seam) warns that photos seem to be missing; it shows in name and capture-time order only, so every marker vanishes in Manual order, and a frame the user removed leaves none. On import they rise 8 px into place, 400 ms Enter, 18 ms apart.

### Export panel (signature)
Export runs inline: the inspector swaps (out 167 ms to -12 px, in 400 ms from +16 px) to a panel with the file name, a Title large percent, a progress bar, the film gate (thumbnails sliding linearly past a 2 px Deep Amber Indicator frame, edges masked), frame count, speed, output summary and Cancel export. An export started while deflicker still measures the photos (#45) waits in its first stage and says so under the bar ("Deflicker is measuring the photos first: 1,240 of 5,000", with a 4 px bar of its own; the time-left line reads "Waiting for deflicker…"). The finished state draws a Success check (500 ms after 200 ms), states "Harbor sunset.mp4 is ready" with length, resolution, codec and size, and offers Play (accent), Show in folder, and Back to editing. In the finished state of the second successful export, once per app version, a card under Back to editing asks "Do you like 10Lapse?" with Yes and No (#139): standard buttons, never accent; it rises 6 px into place once the check has drawn (400 ms after 700 ms; a short fade with animations off), its heading is announced, and it comes after Back to editing in Tab order. Yes shows the Store's rating dialog (else its review page, else one InfoBar), No only closes it; the version is recorded as it shows, so an unanswered card does not come back. Inline, never a dialog, and nothing above it moves. Over the preview, a pill reads "Rendering frame 0313" while the export runs: a Deep Amber Indicator dot and Ink Dark caption text on the Letterbox at 62 % (`PreviewPill*` tokens, the same in both themes because it sits on the photo). Play and the premiere play the finished MP4 itself over the gate, so the user sees the file, not a re-render.

## 6. Do's and Don'ts

### Do:
- **Do** keep chrome neutral and let the photos carry the color (The Photos Are The Color Rule).
- **Do** use Deep Amber Indicator (`#B5620D`) for every light-theme indicator; check 3:1 for indicators and 4.5:1 for text.
- **Do** show photo count, speed and length together, in tabular figures, at all times.
- **Do** use stock WinUI controls, Mica and the system title bar; familiar Windows patterns beat invented ones.
- **Do** animate only transform, opacity, filter and clip-path, with the motion tokens, and give every animation a reduced-motion fade.
- **Do** float InfoBars over the preview and say what happened and what to do next, with one action.
- **Do** design light, dark and high contrast together; switching theme crossfades over 250 ms.

### Don't:
- **Don't** bring back the legacy 10Lapse 2.x UI: blurred background photo, three equal accent buttons, grey full-screen overlays, a "Policy!" wall of text on first run.
- **Don't** look like a generic SaaS dashboard: hero metrics, card grids, gradient washes.
- **Don't** go skeuomorphic: fake dials, film sprockets, leather textures.
- **Don't** reach for pro editors' density (DaVinci Resolve, Premiere); 10Lapse has one job and a handful of controls.
- **Don't** put more than one accent button on a screen, or use amber for a selected segment.
- **Don't** use a modal for export or settings; export runs in the inspector, settings is a page.
- **Don't** animate on launch, block input during a transition, or use bounce, overshoot or springs.
- **Don't** use the day-to-night ramp in chrome or text, or any text grey lighter than Ink Secondary.
- **Don't** use emoji, gradient text, side-stripe borders or decorative glass.
