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

    /// <summary>Export: no H.264 encoder loads.</summary>
    public static string ExportFailedEncoderUnavailable => "Windows couldn't load a video encoder. Install the latest Windows updates and graphics driver, then export again.";

    /// <summary>Export: the encoder refused the size, frame rate or bitrate.</summary>
    public static string ExportFailedSettingsRejected => "The video encoder doesn't accept these settings. Choose a lower resolution or frame rate, then export again.";

    /// <summary>Export: the encoder failed for another reason.</summary>
    public static string ExportFailedEncoder => "The video encoder stopped with an error. Export again, and update your graphics driver if it keeps happening.";

    /// <summary>Export: the encoder stopped taking frames.</summary>
    public static string ExportFailedEncoderStalled => "The video encoder stopped responding. Export again, and update your graphics driver if it keeps happening.";

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
    public static string ExportFailedGraphicsDeviceLost => "The graphics driver stopped during the export. Export again, and update your graphics driver if it keeps happening.";

    /// <summary>Export: not enough system or video memory.</summary>
    public static string ExportFailedOutOfMemory => "There isn't enough memory to export. Close some other apps or choose a lower resolution, then export again.";

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
    public static string DiagnosticsCardCaption => "Copies the app and Windows versions, your graphics card and the last 200 log lines, to paste into a bug report. Nothing is sent from your PC.";

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

    // "5,000": grouped, invariant until #39 localizes numbers with the words.
    private static string Count(int count) => count.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>Fills a format string with the invariant culture (numbers stay unlocalized until #39).</summary>
    internal static string Format(string format, params object[] values) =>
        string.Format(CultureInfo.InvariantCulture, format, values);
}
