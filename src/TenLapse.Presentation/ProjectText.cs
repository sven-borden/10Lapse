using System.Globalization;
using TenLapse.Core;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation;

/// <summary>
/// Every user-visible string the view models build, in one place so #39 can move them to resource files.
/// Each sentence is one whole format string with numbered placeholders, never pieces glued together, so a
/// translation can reorder it; the singular and the plural are separate strings, chosen by count. The
/// timing units ("s", "fps", "per photo") stay in Core's <see cref="TimingWords"/>. English for now; numbers
/// use the invariant culture, as <see cref="TimingFormat"/> does.
/// </summary>
public static class ProjectText
{
    /// <summary>The readout for one photo. {0} the length ("0.5 s"), {1} the rate ("30 fps").</summary>
    public static string TimingReadoutOne => "1 photo → {0} at {1}";

    /// <summary>The readout for 0 or several photos. {0} the count ("742"), {1} the length, {2} the rate.</summary>
    public static string TimingReadoutMany => "{0} photos → {1} at {2}";

    /// <summary>The window title of a project with nothing unsaved. {0} the project name, {1} the product name.</summary>
    public static string WindowTitleSaved => "{0} – {1}";

    /// <summary>The window title while there are unsaved changes. {0} the project name, {1} the product name.</summary>
    public static string WindowTitleUnsaved => "● {0} – {1}";

    /// <summary>The title of the <c>New project</c> question.</summary>
    public static string NewProjectTitle => "Start a new project?";

    /// <summary>The <c>New project</c> question for a project with settings but no photos.</summary>
    public static string NewProjectMessageNoPhotos => "This project's settings will be cleared.";

    /// <summary>The <c>New project</c> question for one photo.</summary>
    public static string NewProjectMessageOne => "This project's photo and settings will be cleared. The photo file stays on your PC.";

    /// <summary>The <c>New project</c> question for several photos. {0} the count ("742").</summary>
    public static string NewProjectMessageMany => "This project's {0} photos and settings will be cleared. The photo files stay on your PC.";

    /// <summary>The button that goes ahead with <c>New project</c>.</summary>
    public static string NewProjectConfirm => "Start new project";

    /// <summary>The button that keeps everything as it is.</summary>
    public static string Cancel => "Cancel";

    /// <summary>Error context: a resource of the replaced project threw while it was disposed.</summary>
    public static string ClosingProjectFailed => "Part of the previous project could not be closed.";

    /// <summary>Error context: a view's <c>ProjectReplaced</c> handler threw.</summary>
    public static string SwitchingViewFailed => "A view could not switch to the new project.";

    /// <summary>
    /// The Critical InfoBar shown when an InfoBar's action failed, and the error context it is reported with.
    /// </summary>
    public static string InfoBarActionFailed => "That couldn't be done because of an unexpected problem. Try again, and restart 10Lapse if it keeps happening.";

    /// <summary>The InfoBar button that shows its detail list (the prototype's "View list").</summary>
    public static string InfoBarShowDetails => "View list";

    /// <summary>The same button while the list shows.</summary>
    public static string InfoBarHideDetails => "Hide list";

    /// <summary>The accessible name of an InfoBar's close button.</summary>
    public static string InfoBarDismiss => "Dismiss";

    // The error path (#26). Each sentence says what happened, then what to do.

    /// <summary>The generic failure: no kind the user can act on. Its InfoBar offers <see cref="CopyDiagnostics"/>.</summary>
    public static string ErrorUnexpected => "Something unexpected stopped that. Try again, and if it keeps happening, copy the diagnostics into a bug report.";

    /// <summary>A failure nothing caught, after which the app kept running.</summary>
    public static string ErrorUnhandled => "10Lapse ran into an unexpected problem and kept running. If something looks wrong, restart 10Lapse.";

    /// <summary>A file is gone. {0} the file name ("IMG_4402.JPG").</summary>
    public static string ErrorFileNotFoundNamed => "{0} couldn't be found. It may have been moved, renamed or deleted; check that it's still there, then try again.";

    /// <summary>A file is gone and its name is not known.</summary>
    public static string ErrorFileNotFound => "A file couldn't be found. It may have been moved, renamed or deleted; check that it's still there, then try again.";

    /// <summary>A folder, drive or network share is gone.</summary>
    public static string ErrorFolderNotFound => "A folder couldn't be found. Check that it still exists and that its drive is connected, then try again.";

    /// <summary>A path longer than Windows allows.</summary>
    public static string ErrorPathTooLong => "A file's path is too long for Windows. Move it to a folder with a shorter path, then try again.";

    /// <summary>Access denied, or write-protected.</summary>
    public static string ErrorAccessDenied => "Windows didn't let 10Lapse open a file or folder. Check that you have access to it, or choose another place, then try again.";

    /// <summary>A sharing or lock violation.</summary>
    public static string ErrorFileInUse => "A file is open in another app. Close it there, then try again.";

    /// <summary>The disk is full.</summary>
    public static string ErrorDiskFull => "The disk is full. Free up some space or choose another drive, then try again.";

    /// <summary>Out of memory.</summary>
    public static string ErrorOutOfMemory => "Your PC ran out of memory. Close some other apps, then try again.";

    /// <summary>A removable drive or network share went away while in use (not ready, device gone, network name deleted).</summary>
    public static string ErrorDriveDisconnected => "The drive or network folder was disconnected. Connect it again, then try again.";

    /// <summary>A file's data cannot be read (a CRC error): a damaged disk or card.</summary>
    public static string ErrorCannotRead => "A file can't be read: its drive or memory card may be damaged. Copy the photos to another drive, then try again.";

    /// <summary>Another app holds the clipboard open. No Copy diagnostics: that needs the clipboard too.</summary>
    public static string ErrorClipboardBusy => "Another app is using the clipboard. Try again in a moment.";

    /// <summary>Export: no H.264 encoder loads.</summary>
    public static string ExportFailedEncoderUnavailable => "Windows couldn't load a video encoder. Install the latest Windows updates and graphics driver, then export again.";

    /// <summary>Export: the encoder refused the size, frame rate or bitrate.</summary>
    public static string ExportFailedSettingsRejected => "The video encoder doesn't accept these settings. Choose a lower resolution or frame rate, then export again.";

    /// <summary>Export: the encoder failed for another reason.</summary>
    public static string ExportFailedEncoder => "The video encoder stopped with an error. Export again, and update your graphics driver if it keeps happening.";

    /// <summary>Export: the encoder stopped taking frames.</summary>
    public static string ExportFailedEncoderStalled => "The video encoder stopped responding. Export again, and update your graphics driver if it keeps happening.";

    /// <summary>Export: the encoder could not open while an earlier export's encoder call is still stuck.</summary>
    public static string ExportFailedEncoderStuck => "An earlier export left the video encoder stuck. Restart 10Lapse, then export again.";

    /// <summary>Export: the output folder cannot be written to.</summary>
    public static string ExportFailedFolderNotWritable => "10Lapse can't save videos in that folder. Choose another folder, then export again.";

    /// <summary>Export: another app has the output file open.</summary>
    public static string ExportFailedFileInUse => "The video file is open in another app. Close it there, then export again.";

    /// <summary>Export: the output folder, drive or share is not there.</summary>
    public static string ExportFailedFolderMissing => "The folder for the video can't be found. Connect its drive or choose another folder, then export again.";

    /// <summary>Export: the disk is full.</summary>
    public static string ExportFailedDiskFull => "The disk is full. Free up some space or choose a folder on another drive, then export again.";

    /// <summary>Export: no graphics device can run it.</summary>
    public static string ExportFailedNoGraphicsDevice => "This PC has no graphics device that can export video. Update your graphics driver, then export again.";

    /// <summary>Export: the driver reset or the GPU went away.</summary>
    public static string ExportFailedGraphicsDeviceLost => "The graphics driver reset during the export. Export again, and update your graphics driver if it keeps happening.";

    /// <summary>Export: not enough system or video memory.</summary>
    public static string ExportFailedOutOfMemory => "There isn't enough memory to export. Close some other apps or choose a lower resolution, then export again.";

    /// <summary>Export: a frame's photo could not be loaded, when the app does not know which.</summary>
    public static string ExportFailedPhotoUnreadable => "A photo in the project can't be read. Remove it or fix the file, then export again.";

    /// <summary>Export: frame {0} (1-based), file name {1}, short reason {2} (<see cref="SkippedFileText.Reason"/>).</summary>
    public static string ExportFailedPhotoUnreadableNamed => "Photo {0}, {1}, can't be read ({2}). Remove it from the project or fix the file, then export again.";

    /// <summary>Export: frame {0} (1-based), file name {1}, no reason known.</summary>
    public static string ExportFailedPhotoUnreadableNamedNoReason => "Photo {0}, {1}, can't be read. Remove it from the project or fix the file, then export again.";

    /// <summary>Export: drawing a frame failed on the graphics device.</summary>
    public static string ExportFailedDrawing => "10Lapse couldn't draw a frame of the video. Export again, and update your graphics driver if it keeps happening.";

    /// <summary>
    /// The sentence for a photo the export could not read: which frame (0-based <paramref name="frameIndex"/>, shown
    /// 1-based), its file name and the reason when known.
    /// </summary>
    public static string ExportFailedPhoto(int frameIndex, string path, SkipReason? reason)
    {
        ArgumentNullException.ThrowIfNull(path);
        var number = Count(frameIndex + 1);
        var name = Path.GetFileName(path);
        return reason is { } known
            ? Format(ExportFailedPhotoUnreadableNamed, number, name, SkippedFileText.Reason(known))
            : Format(ExportFailedPhotoUnreadableNamedNoReason, number, name);
    }

    /// <summary>Export finished beside the target: {0} the file asked for, {1} the file written ("Harbor (2).mp4").</summary>
    public static string ExportSavedBesideFormat => "{0} is open in another app, so the video was saved as {1}.";

    /// <summary>
    /// The sentence for an export whose target another app held through the final move
    /// (<c>ProjectExportResult.TargetInUse</c>): the video is under another name next to it.
    /// </summary>
    public static string ExportSavedBeside(string targetPath, string savedPath)
    {
        ArgumentNullException.ThrowIfNull(targetPath);
        ArgumentNullException.ThrowIfNull(savedPath);
        return Format(ExportSavedBesideFormat, Path.GetFileName(targetPath), Path.GetFileName(savedPath));
    }

    /// <summary>The InfoBar action and the Settings button that copy the diagnostics.</summary>
    public static string CopyDiagnostics => "Copy diagnostics";

    /// <summary>The Success InfoBar after Copy diagnostics.</summary>
    public static string DiagnosticsCopied => "Diagnostics copied. Paste them into a GitHub issue or an email.";

    /// <summary>The first line of the diagnostics text. {0} the product name.</summary>
    public static string DiagnosticsHeading => "{0} diagnostics";

    /// <summary>The heading of the log section of the diagnostics. {0} the number of lines ("200").</summary>
    public static string DiagnosticsLogSection => "Last {0} log lines";

    /// <summary>The message box shown when the window cannot be created at startup. {0} the log folder.</summary>
    public static string StartupFailed => "10Lapse couldn't start because of an unexpected problem. Restart your PC and try again; if it keeps happening, reinstall 10Lapse from the Microsoft Store.\n\nThe log is in {0}.";

    // Settings page (#26 builds the frame and the diagnostics card; #21 and #31 add theirs).

    /// <summary>The Settings page title.</summary>
    public static string SettingsTitle => "Settings";

    /// <summary>The command bar button that opens Settings (its accessible name and tooltip).</summary>
    public static string SettingsButton => "Settings";

    /// <summary>The command bar button that leaves Settings.</summary>
    public static string Back => "Back";

    /// <summary>The Settings section that holds the diagnostics card (the prototype's About, where #31's cards join it).</summary>
    public static string SettingsAboutSection => "About";

    /// <summary>The diagnostics card's title.</summary>
    public static string DiagnosticsCardTitle => "Diagnostics";

    /// <summary>The diagnostics card's caption.</summary>
    public static string DiagnosticsCardCaption => "Copies the app and Windows versions, your graphics card, the video encoders and the last 200 log lines, to paste into a bug report. The log lines can name files and folders 10Lapse worked with, never the photos themselves. Nothing is sent from your PC.";

    // Error contexts: what was being done, for the log only.

    /// <summary>Error context: <c>New project</c> failed.</summary>
    public static string NewProjectFailed => "Starting a new project failed.";

    /// <summary>Error context: Copy diagnostics failed.</summary>
    public static string CopyDiagnosticsFailed => "Copying the diagnostics failed.";

    /// <summary>Error context: opening Settings failed.</summary>
    public static string OpenSettingsFailed => "Opening Settings failed.";

    /// <summary>Error context: leaving Settings failed.</summary>
    public static string CloseSettingsFailed => "Leaving Settings failed.";

    /// <summary>Error context: an exception reached <c>Application.UnhandledException</c>.</summary>
    public static string UnhandledUiFailure => "An exception reached the UI thread unhandled.";

    /// <summary>Error context: a faulted task nobody awaited (<c>TaskScheduler.UnobservedTaskException</c>).</summary>
    public static string UnobservedTaskFailure => "A background task failed and nothing awaited it.";

    /// <summary>Error context: an exception that ends the process (<c>AppDomain.UnhandledException</c>).</summary>
    public static string ProcessFailure => "An unhandled exception is closing 10Lapse.";

    /// <summary>Error context: the main window could not be created at startup.</summary>
    public static string StartupWindowFailed => "The main window could not be created.";

    /// <summary>Error context: the app's resources (App.xaml) could not be loaded at startup.</summary>
    public static string StartupResourcesFailed => "The app's resources (App.xaml) could not be loaded.";

    /// <summary>Error context: the error path itself failed while showing an error.</summary>
    public static string ShowingErrorFailed => "An error could not be shown to the user.";

    /// <summary>Error context (log only): the InfoBar presenter failed to show, hide or animate an InfoBar.</summary>
    public static string InfoBarSyncFailed => "Showing or hiding an InfoBar failed.";

    /// <summary>Error context (log only): following a change of the Windows colors or high contrast failed.</summary>
    public static string SystemColorsFailed => "Following the Windows colors failed.";

    /// <summary>Error context (log only): moving focus after opening or leaving Settings failed.</summary>
    public static string SettingsFocusFailed => "Moving focus after opening or leaving Settings failed.";

    /// <summary>Log line for a cancellation, which is not an error. {0} the context.</summary>
    public static string LogCancelled => "Cancelled, not an error: {0}";

    /// <summary>
    /// The Timing readout, e.g. "742 photos → 24.7 s at 30 fps", at the effective rate for this count
    /// (<see cref="TimingSettings.EffectiveRate"/>: in Length mode a clamped rate gives the length the clip
    /// really has). In <see cref="SpeedMode.PerPhoto"/> the rate reads per photo ("40 photos → 20.0 s at
    /// 0.5 s per photo"); otherwise as <see cref="TimingFormat.FrameRate"/>.
    /// </summary>
    public static string TimingReadout(int frameCount, TimingSettings timing)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameCount);
        ArgumentNullException.ThrowIfNull(timing);

        var effective = timing.EffectiveRate(frameCount).Rate;
        var length = TimingFormat.Duration(FrameTiming.Duration(frameCount, effective));
        var rate = timing.Mode == SpeedMode.PerPhoto
            ? TimingFormat.SecondsPerPhoto(effective)
            : TimingFormat.FrameRate(effective);
        return frameCount == 1
            ? Format(TimingReadoutOne, length, rate)
            : Format(TimingReadoutMany, Count(frameCount), length, rate);
    }

    /// <summary>
    /// The window title: "10Lapse" for a fresh project; "Harbor sunset – 10Lapse" once it has photos;
    /// "● Harbor sunset – 10Lapse" while it has unsaved changes.
    /// </summary>
    public static string WindowTitle(string projectName, bool hasFrames, bool hasUnsavedChanges)
    {
        if (!hasFrames && !hasUnsavedChanges)
        {
            return Product.Name;
        }

        return Format(hasUnsavedChanges ? WindowTitleUnsaved : WindowTitleSaved, projectName, Product.Name);
    }

    /// <summary>The question <c>New project</c> asks when there is work to lose.</summary>
    public static Confirmation NewProjectConfirmation(int frameCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameCount);

        var message = frameCount switch
        {
            0 => NewProjectMessageNoPhotos,
            1 => NewProjectMessageOne,
            _ => Format(NewProjectMessageMany, Count(frameCount)),
        };
        return new Confirmation(NewProjectTitle, message, NewProjectConfirm, Cancel);
    }

    // ---- Import (#12): Add photos, drag and drop, progress. One block so other issues' strings merge around it. ----

    /// <summary>The command bar button that opens the import menu.</summary>
    public static string AddPhotos => "Add photos";

    /// <summary>The Add photos menu item that opens the folder picker.</summary>
    public static string ChooseFolder => "Choose folder…";

    /// <summary>The Add photos menu item that opens the file picker.</summary>
    public static string ChoosePhotos => "Choose photos…";

    /// <summary>The Add photos menu toggle that searches a folder's subfolders too (folders picked or dropped).</summary>
    public static string IncludeSubfolders => "Include subfolders";

    /// <summary>The import progress while the first photos are being looked for.</summary>
    public static string ImportLooking => "Looking for photos…";

    /// <summary>The import progress while listing, one photo found so far.</summary>
    public static string ImportFoundOne => "Found 1 photo…";

    /// <summary>The import progress while listing. {0} the photos found so far ("1,240").</summary>
    public static string ImportFoundMany => "Found {0} photos…";

    /// <summary>The import progress while reading one photo.</summary>
    public static string ImportAddingOne => "Adding 1 photo…";

    /// <summary>The import progress while reading. {0} the photos read ("1,240"), {1} all of them ("5,000").</summary>
    public static string ImportAddingMany => "Adding {0} of {1} photos…";

    /// <summary>The accessible name of the progress's Cancel button.</summary>
    public static string ImportCancelName => "Stop adding photos";

    /// <summary>The drag caption when the count is not known (folders, or items still being looked at).</summary>
    public static string DragCaptionAny => "Add photos";

    /// <summary>The drag caption for one photo file.</summary>
    public static string DragCaptionOne => "Add 1 photo";

    /// <summary>The drag caption for several photo files. {0} the count ("120").</summary>
    public static string DragCaptionMany => "Add {0} photos";

    /// <summary>The drag caption while Settings is open: the drop is refused until the user goes back.</summary>
    public static string DragCaptionSettingsOpen => "Close Settings to add photos";

    /// <summary>The title of the question a drop onto a project with photos asks.</summary>
    public static string DropTitle => "Add these photos to this project?";

    /// <summary>The drop question for a project with one photo.</summary>
    public static string DropMessageOne => "This project has 1 photo. Add the new photos to it, or start a new project with only the new ones. Starting a new project clears this one's photo and settings; the file stays on your PC.";

    /// <summary>The drop question for a project with several photos. {0} the count ("742").</summary>
    public static string DropMessageMany => "This project has {0} photos. Add the new photos to it, or start a new project with only the new ones. Starting a new project clears this one's photos and settings; the files stay on your PC.";

    /// <summary>The drop question's default button: add to the project that is open.</summary>
    public static string DropAddToProject => "Add to project";

    /// <summary>A Caution InfoBar: nothing to import was found.</summary>
    public static string ImportNothingFound => "No photos 10Lapse can read were found there. Choose a folder or files with JPEG, PNG, TIFF or other photos, then try again.";

    /// <summary>The same with subfolders off, when a folder was searched.</summary>
    public static string ImportNothingFoundSubfolders => "No photos were found in that folder. If they're in its subfolders, turn on Include subfolders under Add photos, then try again.";

    /// <summary>A Neutral InfoBar: every photo found was in the project already.</summary>
    public static string ImportAlreadyAdded => "Those photos are already in this project.";

    /// <summary>A Neutral InfoBar after Cancel, when no photo was kept.</summary>
    public static string ImportStoppedNone => "Stopped adding photos. None were added.";

    /// <summary>A Neutral InfoBar after Cancel, one photo kept.</summary>
    public static string ImportStoppedOne => "Stopped adding photos. 1 photo was added.";

    /// <summary>A Neutral InfoBar after Cancel. {0} the photos kept ("1,240").</summary>
    public static string ImportStoppedMany => "Stopped adding photos. {0} photos were added.";

    /// <summary>
    /// A Caution InfoBar: a folder of the import would not open, or a path was no longer there. {0} the folder's name
    /// (its full path for a drive).
    /// </summary>
    public static string ImportFolderFailedOne => "Couldn't open the folder {0}. Check that its drive or memory card is connected and that you can open it in File Explorer, then add it again.";

    /// <summary>The same for several folders, listed under View list. {0} the count ("3").</summary>
    public static string ImportFolderFailedMany => "Couldn't open {0} folders. Check that their drives or memory cards are connected and that you can open them in File Explorer, then add them again.";

    /// <summary>The title of the question an import asks when photos are only in OneDrive (files on demand).</summary>
    public static string CloudDownloadTitle => "Download from OneDrive?";

    /// <summary>The question for one online-only photo. {0} its size ("12 MB").</summary>
    public static string CloudDownloadOne => "1 photo is only in OneDrive. Download it now? About {0}.";

    /// <summary>The question for several online-only photos. {0} the count ("1,240"), {1} their size ("2.4 GB").</summary>
    public static string CloudDownloadMany => "{0} photos are only in OneDrive. Download them now? About {1}.";

    /// <summary>The question's button that downloads the photos and adds them.</summary>
    public static string CloudDownloadConfirm => "Download";

    /// <summary>The question's default button for one photo: leave it in the cloud and list it as skipped.</summary>
    public static string CloudDownloadSkipOne => "Skip it";

    /// <summary>The question's default button for several photos.</summary>
    public static string CloudDownloadSkipMany => "Skip them";

    /// <summary>A size under 1 GB. {0} the megabytes, whole ("340").</summary>
    public static string SizeMegabytes => "{0} MB";

    /// <summary>A size of 1 GB or more. {0} the gigabytes, one decimal ("2.4").</summary>
    public static string SizeGigabytes => "{0} GB";

    /// <summary>Error context: an import failed part way.</summary>
    public static string ImportFailed => "Importing photos failed.";

    /// <summary>Error context: Choose folder failed.</summary>
    public static string ChooseFolderFailed => "Choosing a folder to import failed.";

    /// <summary>Error context: Choose photos failed.</summary>
    public static string ChoosePhotosFailed => "Choosing photos to import failed.";

    /// <summary>Error context: a drop onto the window failed.</summary>
    public static string ImportDropFailed => "Adding dropped photos failed.";

    /// <summary>Error context: the progress's Cancel failed.</summary>
    public static string CancelImportFailed => "Stopping the import failed.";

    /// <summary>Error context (log only): a UI-thread step of an import failed to run; the import itself reports what it means.</summary>
    public static string ImportUiStepFailed => "A step of an import on the UI thread failed.";

    /// <summary>Error context (log only): counting the photos being dragged over the window failed; the caption stays generic.</summary>
    public static string DragCountFailed => "Counting the dragged photos failed.";

    /// <summary>The question a drop onto a project with <paramref name="frameCount"/> photos asks.</summary>
    public static Choice DropChoice(int frameCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameCount);
        var message = frameCount == 1 ? DropMessageOne : Format(DropMessageMany, Count(frameCount));
        return new Choice(DropTitle, message, DropAddToProject, NewProjectConfirm, Cancel);
    }

    /// <summary>"Add 120 photos", or "Add photos" when <paramref name="photoCount"/> is not known.</summary>
    public static string DragCaption(int? photoCount) => photoCount switch
    {
        null or <= 0 => DragCaptionAny,
        1 => DragCaptionOne,
        _ => Format(DragCaptionMany, Count(photoCount.Value)),
    };

    /// <summary>The listing progress: "Looking for photos…", then "Found 1,240 photos…".</summary>
    public static string ImportListingProgress(int found)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(found);
        return found switch
        {
            0 => ImportLooking,
            1 => ImportFoundOne,
            _ => Format(ImportFoundMany, Count(found)),
        };
    }

    /// <summary>The reading progress: "Adding 1,240 of 5,000 photos…".</summary>
    public static string ImportReadingProgress(int read, int total)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(read);
        ArgumentOutOfRangeException.ThrowIfLessThan(total, read);
        return total == 1 ? ImportAddingOne : Format(ImportAddingMany, Count(read), Count(total));
    }

    /// <summary>The InfoBar after Cancel, with the photos the import kept.</summary>
    public static string ImportStopped(int kept)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(kept);
        return kept switch
        {
            0 => ImportStoppedNone,
            1 => ImportStoppedOne,
            _ => Format(ImportStoppedMany, Count(kept)),
        };
    }

    /// <summary>
    /// The question an import asks before reading <paramref name="count"/> online-only photos of <paramref name="bytes"/>
    /// in all (their reported sizes): <see cref="CloudDownloadConfirm"/>, or the default button, which skips them.
    /// </summary>
    public static Confirmation CloudDownload(int count, long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        var size = Size(bytes);
        return count == 1
            ? new Confirmation(CloudDownloadTitle, Format(CloudDownloadOne, size), CloudDownloadConfirm, CloudDownloadSkipOne)
            : new Confirmation(CloudDownloadTitle, Format(CloudDownloadMany, Count(count), size), CloudDownloadConfirm, CloudDownloadSkipMany);
    }

    /// <summary>"340 MB" under 1 GB (at least 1 MB, rounded up), "2.4 GB" from there; binary units, as File Explorer counts them.</summary>
    public static string Size(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        const double megabyte = 1024 * 1024;
        const double gigabyte = megabyte * 1024;
        return bytes >= gigabyte
            ? Format(SizeGigabytes, (bytes / gigabyte).ToString("N1", CultureInfo.InvariantCulture))
            : Format(SizeMegabytes, Math.Max(1, Math.Ceiling(bytes / megabyte)).ToString("N0", CultureInfo.InvariantCulture));
    }

    /// <summary>The InfoBar for one folder (or path) the import could not open: named by its folder name.</summary>
    public static string ImportFolderFailed(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return Format(ImportFolderFailedOne, string.IsNullOrEmpty(name) ? path : name);
    }

    /// <summary>The InfoBar for several folders the import could not open.</summary>
    public static string ImportFoldersFailed(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 2);
        return Format(ImportFolderFailedMany, Count(count));
    }

    // ---- End of the import block (#12). ----

    // ---- First launch (#29): the drop zone and its small timelapse. ----

    /// <summary>The drop zone's title at rest.</summary>
    public static string FirstLaunchTitle => "Drop a folder of photos to make a timelapse";

    /// <summary>The drop zone's title while photos are dragged over the window.</summary>
    public static string FirstLaunchDropTitle => "Drop to start your timelapse";

    /// <summary>
    /// What a timelapse is, in one sentence after the lead. {0} the product name, {1} the photo count ("300"),
    /// {2} the frame rate ("30"), {3} the whole seconds of the clip ("10").
    /// </summary>
    public static string FirstLaunchExplanationFormat => "{0} plays your photos back as a video. {1} photos at {2} frames per second make a {3}-second clip.";

    /// <summary>The privacy line under the actions.</summary>
    public static string FirstLaunchPrivacy => "Your photos never leave this PC.";

    /// <summary>The drop zone's accent button, the same command as <see cref="ChooseFolder"/>.</summary>
    public static string FirstLaunchChooseFolder => "Choose folder";

    /// <summary>The drop zone's standard button, the same command as <see cref="ChoosePhotos"/>.</summary>
    public static string FirstLaunchChoosePhotos => "Choose photos";

    /// <summary>A frame's place in a sequence. {0} the frame number, {1} the total, both zero-padded to four digits ("0312", "0742").</summary>
    public static string FrameOfTotal => "Frame {0} of {1}";

    /// <summary>A time code and the clip length. {0} the time code ("00:10.40"), {1} the length as a time code ("00:24.73").</summary>
    public static string TimecodeOfTotal => "{0} / {1}";

    /// <summary>Log-only context: drawing the first-launch timelapse failed (the drop zone stays usable without it).</summary>
    public static string DaybreakDrawFailed => "Drawing the first-launch timelapse failed.";

    /// <summary>Log-only context: advancing the first-launch timelapse failed.</summary>
    public static string DaybreakTickFailed => "Advancing the first-launch timelapse failed.";

    /// <summary>Log-only context: following a drag that left the window or one of its parts failed.</summary>
    public static string DragLeaveFailed => "Following a drag leaving the window failed.";

    /// <summary>Log-only context: showing or hiding the drop zone's drag-over look failed.</summary>
    public static string DropZoneLookFailed => "Showing the drop zone's drag-over look failed.";

    /// <summary>
    /// The explanation sentence for <paramref name="photos"/> photos at <paramref name="rate"/>, its length taken from
    /// <see cref="FrameTiming.Duration"/> so the example can never disagree with the app's own math. The example
    /// must make a whole number of seconds at a whole frame rate ("300 photos at 30 frames per second make a 10-second clip").
    /// </summary>
    public static string FirstLaunchExplanation(int photos, Fraction rate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(photos);
        if (rate.Denominator != 1 || rate.Numerator <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), rate, "The example needs a whole frame rate.");
        }

        var ticks = FrameTiming.Duration(photos, rate);
        if (ticks % FrameTiming.TicksPerSecond != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(photos), photos, "The example must make a whole number of seconds.");
        }

        return Format(FirstLaunchExplanationFormat, Product.Name, Count(photos), rate.Numerator, ticks / FrameTiming.TicksPerSecond);
    }

    /// <summary>
    /// "Frame 0312 of 0742": <paramref name="number"/> (from 1) of <paramref name="total"/>, both zero-padded to four
    /// digits or the total's width if wider, so the caption keeps its width (DESIGN.md, The Tabular Rule and its example).
    /// </summary>
    public static string FrameCaption(int number, int total)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(total);
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number, total);
        var digits = "D" + Math.Max(4, total.ToString(CultureInfo.InvariantCulture).Length).ToString(CultureInfo.InvariantCulture);
        return Format(FrameOfTotal, number.ToString(digits, CultureInfo.InvariantCulture), total.ToString(digits, CultureInfo.InvariantCulture));
    }

    /// <summary>"00:10.40 / 00:24.73": the time at <paramref name="ticks"/> and the clip's <paramref name="lengthTicks"/>, as time codes.</summary>
    public static string TimecodeCaption(long ticks, long lengthTicks) =>
        Format(TimecodeOfTotal, TimingFormat.Timecode(ticks), TimingFormat.Timecode(lengthTicks));

    // ---- End of the first-launch block (#29). ----

    // "5,000": grouped, invariant until #39 localizes numbers with the words.
    private static string Count(int count) => count.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>Fills a format string with the invariant culture (numbers stay unlocalized until #39).</summary>
    internal static string Format(string format, params object[] values) =>
        string.Format(CultureInfo.InvariantCulture, format, values);
}
