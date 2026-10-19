# 10Lapse

10Lapse turns a folder of photos into a smooth timelapse video, up to 4K. It runs on Windows, and everything happens on your PC: your photos never leave it.

[Get 10Lapse from the Microsoft Store](https://apps.microsoft.com/detail/9NBN2XH6S4D8)

## Status

Version 3.0 is being rebuilt on WinUI 3 and .NET 10. The current Store release, 2.1.1, is the old UWP app; its code lives at the tag `v2.1.1-legacy`.

## Build

You need Windows 10 version 1809 or later, the .NET 10 SDK, the Windows SDK 10.0.26100 and the Windows App Runtime 2. The debug build of the app and the test host start on the installed runtime, and crash at startup without it. Visual Studio is not required; the .NET command line builds the app.

```
pwsh -File scripts/gate.ps1
```

The script restores packages, builds, and runs every test.

`scripts/launch-test.ps1` starts the Debug app many times, under CPU or GPU load if asked, and keeps the details of any launch that crashes (`Get-Help scripts/launch-test.ps1`).

The solution at the repo root has four projects:

- `src/TenLapse.App`: the WinUI 3 app, packaged as a single-project MSIX
- `src/TenLapse.Core`: project model and timing, with no UI or Windows types
- `src/TenLapse.Media`: photo decoding, frame rendering and video encoding
- `tests/TenLapse.Tests`: xUnit tests

## Privacy

10Lapse does not collect, store or transmit personal data. Read the [privacy policy](PRIVACY.md).

## License

No license has been chosen yet. All rights are reserved.
