# App icon sources and the asset generator

The icon is direction A, **Five frames**, from `docs/design/canvas/foundations/Icon.dc.html`. The SVGs here are the
source; every PNG and the `.ico` in `Assets/` is generated from them. Do not edit the generated files by hand.

| File | What it is |
|---|---|
| `TenLapse.svg` | The master on the canvas's 48 px grid: 40 px tile at (4, 4), 10 px corners, five frames with 0.75 px gaps, a sheen over the top half. Used for 30 px and up. |
| `TenLapse-16.svg`, `-20.svg`, `-24.svg` | The same drawing on 16, 20 and 24 px grids: the tile fills the canvas (Fluent: no padding when every pixel counts), no gaps, no sheen, bands whose edges land on pixels (3 3 4 3 3, 4 each, 5 5 4 5 5). Used for those three sizes only. |

The colours are the canvas's (`#8EC0EA #F3C46B #EE8F4F #B4567A #2B3A75`), not the `DESIGN.md` brand-ramp tokens.

## Regenerate

From anywhere in the repository, on Windows with the .NET 10 SDK (nothing else to install; the renderer is Win2D's
SVG support, already a dependency):

```powershell
dotnet run --project tools/TenLapse.Spike -c Debug -p:Platform=x64 -- assets --contact-sheet "$env:LOCALAPPDATA\Temp\assets.png"
```

`--contact-sheet` is optional: it writes one PNG with every taskbar size on a light and a dark taskbar, 16, 24 and 32 px
magnified, and the tiles and splash. `--source` and `--out` override the two folders. The output is deterministic, so a
second run changes no file.

## What it writes (to `Assets/`)

- `Square44x44Logo.scale-{100,125,150,200,400}` and `Square44x44Logo.targetsize-{16,20,24,30,32,36,40,48,60,64,72,80,96,256}`,
  each target size plain, `_altform-unplated` and `_altform-lightunplated`. From 30 px, the plain form has the master's
  4 px padding (the system draws a plate behind it); the unplated forms enlarge the tile to 95% of the canvas.
- `Square71x71Logo`, `Square150x150Logo`, `Wide310x150Logo`, `Square310x310Logo` at the five scales: the bare tile at 62% of
  the shorter side, centred, on transparency (the manifest's `BackgroundColor` is `transparent`).
- `SplashScreen.scale-*`: 620 x 300 at 100%, the tile at 42% of the height, on transparency; the colour
  (`#1F1E1D`, Mica Dark) is the manifest's `SplashScreen BackgroundColor`.
- `StoreLogo.scale-*`: 50 x 50 at 100%.
- `TenLapse.ico`: 16, 24, 32, 48 and 256 px as PNG entries, tile at 95% above 24 px. The exe's `ApplicationIcon` and
  `AppWindow.SetIcon` (window, taskbar, Alt+Tab) use it.

Pixel sizes follow the Windows scale tables, rounding half up (71 px at 125% is 89, at 150% 107).
