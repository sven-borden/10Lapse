# Private beta: the package flight (#40)

> **Not run (2026-10-21).** Sven submitted 3.0 straight to the public listing, so #40 and #41 were closed as not
> planned. This page stays as the recipe if a later release wants a flight (a new version, a risky change). Its
> `runFullTrust` text in step 5 was the one used on the public submission.

For Sven. Everything here is in the order you do it. The code side is done: `scripts/pack.ps1` builds the upload and checks it, and the testers have `docs/release/beta-testers.md` and the issue form `.github/ISSUE_TEMPLATE/beta-report.yml`.

A package flight is a private build of the existing 10Lapse product that only the people in a flight group receive. Everyone else keeps getting what the listing offers (2.1.1). Nothing about the public listing changes, and a flight needs no store listing text, screenshots or pricing.

## 1. Build the upload

```
pwsh -File scripts/pack.ps1
```

It prints the path of `dist/store/TenLapse.App_<version>_x64_arm64_bundle.msixupload` (about 124 MB, unsigned on purpose, the Store signs it). It fails with exit 1 and lists what is wrong if anything in the packages is wrong (identity, version, device family, capabilities, runtime dependency, resources, Debug-only code). Silence about checks means they passed.

- The first flight is `3.0.0.0`, which is already in the manifest. Every later upload needs a higher version and a last digit of 0: `pwsh -File scripts/pack.ps1 -Version 3.0.1.0` writes it into `src/TenLapse.App/Package.appxmanifest` (and refuses anything that is not higher, or does not end in `.0`), then builds. Commit the manifest change with the fix it ships. The manifest is written before the build starts, so a failed pack leaves it bumped: either revert it with git (`git checkout src/TenLapse.App/Package.appxmanifest`), or fix the problem and run `pwsh -File scripts/pack.ps1` without `-Version` (the manifest already holds the new one; running with the same `-Version` again is refused as "not above").
- Nothing in `dist/` is ever committed.

## 2. Partner Center

Menus move around in Partner Center; the names below are what to look for.

1. **Flight group.** Create one (Partner Center account level, "Flight groups"; if you cannot find it, search the help for "flight group"). Type: Microsoft account. Add 10 to 20 testers by the Microsoft account email they use to sign in to the Store. Their emails live only in Partner Center, never in this repository. A flight group can be reused for every later flight.
2. **New flight.** Apps and games, 10Lapse (product `9NBN2XH6S4D8`), Package flights, Create new flight. Name it like `Beta 3.0.0` and pick the flight group.
3. **Packages.** Upload the `.msixupload`. Partner Center reads the identity (`62394glaglasven.10Lapse`, publisher `CN=A4CDA1AE-2891-435C-B73F-FCA7B39485F4`) and accepts it only if it matches the product. Do not upload the `.msixbundle` or an `.msix`.
4. **Device families.** Windows Desktop only. Uncheck **Mobile**, **Xbox** and **HoloLens** (the old listing was Universal, so they may be pre-checked). The package says `Windows.Desktop` with minimum Windows 11 (10.0.22000.0), so the Store does not offer it to older systems either way.
5. **`runFullTrust`.** It is a restricted capability: Partner Center asks for a justification (on the Packages page after the upload, and in the notes for certification). Paste:

   > 10Lapse is a WinUI 3 desktop app; full trust is how Windows App SDK desktop apps run. It reads the photos the user picks and writes the video they export. No network access.

   The package declares `runFullTrust` and nothing else (no `internetClient`), which matches that text.
6. **Notes for certification.** Say it is a private beta of 3.0, that the app needs no account and no sign-in, and that it works offline.
7. **Submit the flight.** Flights go through certification (usually hours to a day or two). The package carries no symbols file, so there is nothing else to upload.

## 3. What the testers see

- They must be signed in to the Microsoft Store with the invited account.
- Once the flight is published the Store offers it as a normal update of 10Lapse (Library, Get updates). A tester who never installed 10Lapse opens the Store page of 10Lapse and installs it; the Store gives them the flight build because they are in the group. Allow a few hours after publishing for it to appear.
- The Windows App Runtime 2 comes with the install; they need nothing else. The listing page still says 2.1.1 for the public, which is expected.
- Send them `docs/release/beta-testers.md` (paste the text into an email; they do not need the repository). Add your email line when you paste the guide: it tells testers without a GitHub account to send their report to Sven by email, and the repository holds no address.

## 4. Windows 10

3.0 is Windows 11 only (minimum 10.0.22000.0), and Windows 10 devices keep 2.1.1. The issue's old line "at least one Windows 10 machine" is replaced by this: **no Windows 10 tester can test 3.0**, so do not put Windows 10 users in the group expecting a beta. The one Windows 10 check worth doing, if you have such a PC (or a tester who does):

- Sign in to the Store with an account that is in the flight group, open the 10Lapse page, and install or update. It must install 2.1.1 (or leave 2.1.1 as it is) and must **not** offer 3.0, an error, or "this app doesn't work on your device". If 2.1.1 is already there, "Get updates" must say it is up to date.

This is the real test of the version rule the whole rollout depends on (#44): the Store picks the highest version a device can run, and 3.0.0.0 cannot run on Windows 10. Keep the 2.1.1 packages in the listing; do not remove them from Partner Center.

## 5. What only the flight can show

The Debug app runs unpackaged, so these were never seen for real. Ask a tester, or do them yourself on the flight build (Windows 11):

| Check | What to look for |
| --- | --- |
| Rating dialog (#31) | After the second successful export, about 4 seconds after "finished", the Store's own rating dialog should appear, if the Store shows it for a flight build; the app asks only with the window in front and idle (`App.xaml.cs:221`), at most once per package version. Never after a failed or cancelled export. Settings, Feedback card: *Rate 10Lapse* opens the Store review page, *Send feedback* opens GitHub in the browser. |
| Export notification (#24) | Start an export, minimize the window or switch to another app, wait for the end: a Windows notification names the file. Clicking it brings 10Lapse back. A failed export says the file "couldn't be exported". |
| Settings and log locations | `%LOCALAPPDATA%\Packages\62394glaglasven.10Lapse_<id>\LocalState`: `settings.json`, `encoder-capabilities.json` and a `Logs` folder (rolling `.log` files). Settings survive a restart; uninstalling removes them. |
| Proxy cache | `...\LocalCache\Proxies` fills while a big project is open (the preview's smaller copies). Windows may clear LocalCache; the preview must then rebuild it quietly. |
| Start-up crash watch (#90) | One crash at launch was seen on the dev PC and never reproduced in 980 launches (PR #91). Partner Center Health (Analytics, Health) lists crashes of Store builds, but without a symbols package its stack traces are not symbolicated, so the count is useful and the traces are not. Ask every tester who sees the app die at start, or close by itself, to open it again and send *Settings, Copy diagnostics* (the log keeps the start-up phases). #90 stays open until a clean beta. |
| First-run and file pickers | The first launch, the folder picker and the drop of a folder work for a packaged full-trust app on someone else's PC and account (work or school accounts, OneDrive-redirected Pictures). |

## 6. When a tester reports a problem

- Reports are GitHub issues, made with the *Beta report* form or the *Send feedback* button in Settings, with the *Copy diagnostics* text pasted in (`docs/release/beta-testers.md` explains it). Link or label them with `#40`.
- During the flight only **fixes** are made. No new features; those wait for 3.1.
- Every fix that reaches testers is a new flight submission with a higher version (`pack.ps1 -Version`), uploaded to the same flight (Package flights, the flight, Create new submission). Testers get it as an update.
- The public release (#44) uses the same or a higher version than the last flight; verify this in Partner Center before you submit it, and do not lower the version.
- Close #40 when the beta ran clean: no open beta-report issue that blocks a release, #90 not seen, and the checks in section 5 done. Then #44 (rollout) is next.
