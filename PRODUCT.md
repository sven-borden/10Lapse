# Product

## Register

product

## Users

Hobby and enthusiast photographers on Windows who have shot a sequence from a tripod (a sunset, clouds, a build, a city at night) and now have a folder of hundreds or thousands of photos. They use a laptop or desktop at home, usually the evening after the shoot, and want to see the day turn to night without learning a video editor. Some arrive expecting a slideshow maker; the app has to teach the idea of a timelapse in one sentence.

Job to be done: turn a folder of photos into a smooth, correctly timed video, in the right order, at the right shape for where it will be posted, without crashes or guesswork.

## Product Purpose

10Lapse is a Microsoft Store app that turns a sequence of photos into a timelapse video, up to 4K. Version 3.0 is a rewrite (WinUI 3, Windows App SDK, .NET 10) shipped as an update to the existing listing. Success: every export works the first time, the numbers on screen are always right, and the Store rating recovers from 1.0 to above 4.

## Brand Personality

Calm, precise, crafted. The app behaves like a well-made camera instrument: quiet chrome, exact numbers, and the photos as the only strong color. Copy is plain and specific ("742 photos → 24.7 s at 30 fps"), never cute. Motion is cinematic at the moments the user creates something (photos arriving, the export running, the finished video), and invisible the rest of the time.

## Anti-references

- The legacy 10Lapse 2.x UI: blurred background photo, three equal accent buttons, grey full-screen overlays, a "Policy!" wall of text on first run.
- Generic SaaS dashboards: hero metrics, card grids, gradient washes.
- Skeuomorphic camera apps: fake dials, film sprockets, leather textures.
- Pro editors' density (DaVinci Resolve, Premiere): 10Lapse has one job and a handful of controls.

References: native Windows 11 apps (Photos, Snipping Tool, Clipchamp, Dev Home): Mica, Fluent controls, Segoe UI Variable, system title bar.

## Design Principles

1. **The photos are the color.** Chrome stays neutral; golden-hour amber marks only the primary action, progress, the playhead and the selected frame.
2. **Always show the math.** Photo count, speed and length sit together and are always correct.
3. **Native first.** Familiar Windows patterns beat invented ones; the app should feel like part of Windows.
4. **Cinematic when you create, invisible when you work.** Choreography plays on import, export and finish, never on launch, never blocking input, and every animation has a reduced-motion version.
5. **Never silent.** Every long task shows progress; every failure says what happened and what to do next.

## Accessibility & Inclusion

WCAG 2.2 AA: text contrast at least 4.5:1, visible focus, full keyboard operation, screen reader names on every control, Windows high contrast themes. Respect the Windows animation setting and offer an in-app "Reduce motion" switch; reduced motion swaps movement for short fades. Colors that carry meaning also differ in lightness.
