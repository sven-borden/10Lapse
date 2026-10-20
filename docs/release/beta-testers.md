# 10Lapse 3.0 private beta: tester guide

Thank you for testing. This takes about half an hour, and the more of your own photos you use, the better.

## What 10Lapse 3.0 is

10Lapse turns a folder of photos into a timelapse video, up to 4K. Version 3.0 is a full rewrite of the Microsoft Store app you may know as 2.1.1. It needs **Windows 11**. 10Lapse itself has no account, no sign-in and no network access: your photos never leave your PC.

## Install it

1. On Windows 11, sign in to the **Microsoft Store** with the Microsoft account Sven added to the beta.
2. Open the Store, then Library, then **Get updates**. Or open the Store page of 10Lapse and install it. You get the beta build instead of the public one. It can take a few hours after you were invited.
3. Open **10Lapse**. To see which version you have: Windows Settings, Apps, Installed apps, 10Lapse, Advanced options. It should start with 3.0.

Still on 2.1.1 after a few hours? Tell Sven which Windows version you run.

## What to try

Use your own photos. Notice anything odd, slow or confusing.

- **A folder of your photos.** Pick it or drop it onto the window. Does the order look right? Is the preview smooth?
- **iPhone photos (HEIC).** First without the free *HEIF Image Extension* from the Microsoft Store, then with it. Without it, 10Lapse should say what to install.
- **A big set.** A few thousand photos, or large ones (24 megapixels and up).
- **Export** at a few settings: another resolution, frame rate or codec (H.264, HEVC). Play the video.
- **Minimize 10Lapse during an export** and wait. When it ends, a Windows notification should say so, and clicking it should bring 10Lapse back.
- **Cancel** an export half way. The window should go back to normal.
- **Export twice**, and keep the window in front for the second export. A few seconds after the second finished export the Microsoft Store should ask you to rate 10Lapse. Tell Sven if it does not, or if it asks again straight away.
- **Remove frames** you do not want. Your photo files on disk are never changed or deleted.
- **Close and reopen.** Do your choices come back? Did the app ever close by itself while starting? That matters a lot.

## Report a problem

1. In 10Lapse, open **Settings** (command bar) and choose **Copy diagnostics**.
2. Open the form: <https://github.com/sven-borden/10Lapse/issues/new?template=beta-report.yml>. The **Send feedback** button in Settings opens GitHub too; pick **Beta report** there.
3. Fill in the short form and paste the diagnostics into the last box.

You need a free GitHub account. If you would rather not, send the same text to Sven by email.

The diagnostics list the app version, your Windows version, the video encoders and the last lines of the log. They hold no photos, and your Windows user name is replaced, but they can include the names of folders and files. Read them before you paste, and never attach your photos to a public issue.

## What 10Lapse never does

- It has **no network access**: no analytics, no accounts, nothing sent anywhere. Only when you press them, *Rate 10Lapse* and the HEIF link open the Store, and *Send feedback* opens GitHub in your browser.
- Your **photos never leave your PC** and are never changed. Removing a frame removes it from the project, not from the disk.
- A Windows notification names the video file and nothing else.

To remove the beta: Windows Settings, Apps, Installed apps. Your photos and exported videos stay where they are.
