using System.Globalization;
using TenLapse.Core;
using TenLapse.Core.Ordering;
using TenLapse.Core.Projects;
using TenLapse.Core.Text;
using TenLapse.Core.Timing;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation;

/// <summary>
/// Every user-visible string the view models and the views show, read from <c>Strings/ProjectText.resx</c> (#39,
/// <see cref="TextResources"/>); the XAML binds to these properties too. Each sentence is one whole format string with
/// numbered placeholders, never pieces glued together, so a translation can reorder it; the singular and the plural
/// are separate strings, chosen by count. The timing units ("s", "fps", "per photo") are Core's
/// <see cref="TimingWords"/>. Numbers use the invariant culture, as <see cref="TimingFormat"/> does.
/// <para>
/// The split: what the user sees comes from the resource file. Error contexts and log lines (their summaries say
/// so) and the Copy diagnostics text stay here as English literals: the log is for the developer and bug reports.
/// A test fails on a literal property here whose summary does not say it is for the log.
/// </para>
/// </summary>
public static class ProjectText
{
    private static readonly TextResources _strings = new("TenLapse.Presentation.Strings.ProjectText", typeof(ProjectText).Assembly);

    /// <summary>The command bar button that starts a new project, and its accessible name.</summary>
    public static string NewProject => _strings.Get();

    /// <summary>The accessible name of the title bar's dot that shows while there are unsaved changes.</summary>
    public static string UnsavedChangesName => _strings.Get();

    /// <summary>The accessible name of the narrow layout's tab row (<see cref="ShellLayout.TabsName"/>).</summary>
    public static string InspectorTabsName => _strings.Get();

    /// <summary>The accessible name of the first-launch screen's main region, for a screen reader's landmark list.</summary>
    public static string StartRegionName => _strings.Get();

    /// <summary>The accessible name of the editing screen's main region (the preview, the transport and the filmstrip).</summary>
    public static string StageRegionName => _strings.Get();

    /// <summary>The accessible name of the inspector's region (Timing, Frame, Output) while editing.</summary>
    public static string InspectorRegionName => _strings.Get();

    /// <summary>What a screen reader calls a region that is no standard landmark (the filmstrip, the export panel): the ARIA word "region" in the language.</summary>
    public static string RegionLandmarkType => _strings.Get();

    /// <summary>The readout for one photo. {0} the length ("0.5 s"), {1} the rate ("30 fps").</summary>
    public static string TimingReadoutOne => _strings.Get();

    /// <summary>The readout for 0 or several photos. {0} the count ("742"), {1} the length, {2} the rate.</summary>
    public static string TimingReadoutMany => _strings.Get();

    /// <summary>The window title of a project with nothing unsaved. {0} the project name, {1} the product name.</summary>
    public static string WindowTitleSaved => _strings.Get();

    /// <summary>The window title while there are unsaved changes. {0} the project name, {1} the product name.</summary>
    public static string WindowTitleUnsaved => _strings.Get();

    /// <summary>The title of the <c>New project</c> question.</summary>
    public static string NewProjectTitle => _strings.Get();

    /// <summary>The <c>New project</c> question for a project with settings but no photos.</summary>
    public static string NewProjectMessageNoPhotos => _strings.Get();

    /// <summary>The <c>New project</c> question for one photo.</summary>
    public static string NewProjectMessageOne => _strings.Get();

    /// <summary>The <c>New project</c> question for several photos. {0} the count ("742").</summary>
    public static string NewProjectMessageMany => _strings.Get();

    /// <summary>The button that goes ahead with <c>New project</c>.</summary>
    public static string NewProjectConfirm => _strings.Get();

    /// <summary>The button that keeps everything as it is.</summary>
    public static string Cancel => _strings.Get();

    /// <summary>Error context: a resource of the replaced project threw while it was disposed.</summary>
    public static string ClosingProjectFailed => "Part of the previous project could not be closed.";

    /// <summary>Error context: a view's <c>ProjectReplaced</c> handler threw.</summary>
    public static string SwitchingViewFailed => "A view could not switch to the new project.";

    /// <summary>
    /// The Critical InfoBar shown when an InfoBar's action failed. The log reads <see cref="InfoBarActionFailedContext"/>.
    /// </summary>
    public static string InfoBarActionFailed => _strings.Get();

    /// <summary>Error context: an InfoBar's action threw; the user sees <see cref="InfoBarActionFailed"/>.</summary>
    public static string InfoBarActionFailedContext => "An InfoBar's action failed.";

    /// <summary>The InfoBar button that shows its detail list (the prototype's "View list").</summary>
    public static string InfoBarShowDetails => _strings.Get();

    /// <summary>The same button while the list shows.</summary>
    public static string InfoBarHideDetails => _strings.Get();

    /// <summary>The accessible name of an InfoBar's close button.</summary>
    public static string InfoBarDismiss => _strings.Get();

    // The error path (#26). Each sentence says what happened, then what to do.

    /// <summary>The generic failure: no kind the user can act on. Its InfoBar offers <see cref="CopyDiagnostics"/>.</summary>
    public static string ErrorUnexpected => _strings.Get();

    /// <summary>A failure nothing caught, after which the app kept running.</summary>
    public static string ErrorUnhandled => _strings.Get();

    /// <summary>A file is gone. {0} the file name ("IMG_4402.JPG").</summary>
    public static string ErrorFileNotFoundNamed => _strings.Get();

    /// <summary>A file is gone and its name is not known.</summary>
    public static string ErrorFileNotFound => _strings.Get();

    /// <summary>A folder, drive or network share is gone.</summary>
    public static string ErrorFolderNotFound => _strings.Get();

    /// <summary>A path longer than Windows allows.</summary>
    public static string ErrorPathTooLong => _strings.Get();

    /// <summary>Access denied, or write-protected.</summary>
    public static string ErrorAccessDenied => _strings.Get();

    /// <summary>A sharing or lock violation.</summary>
    public static string ErrorFileInUse => _strings.Get();

    /// <summary>The disk is full.</summary>
    public static string ErrorDiskFull => _strings.Get();

    /// <summary>Out of memory.</summary>
    public static string ErrorOutOfMemory => _strings.Get();

    /// <summary>A removable drive or network share went away while in use (not ready, device gone, network name deleted).</summary>
    public static string ErrorDriveDisconnected => _strings.Get();

    /// <summary>A file's data cannot be read (a CRC error): a damaged disk or card.</summary>
    public static string ErrorCannotRead => _strings.Get();

    /// <summary>Another app holds the clipboard open. No Copy diagnostics: that needs the clipboard too.</summary>
    public static string ErrorClipboardBusy => _strings.Get();

    /// <summary>Export: no H.264 encoder loads.</summary>
    public static string ExportFailedEncoderUnavailable => _strings.Get();

    /// <summary>Export: the encoder refused the size, frame rate or bitrate.</summary>
    public static string ExportFailedSettingsRejected => _strings.Get();

    /// <summary>Export: the encoder failed for another reason.</summary>
    public static string ExportFailedEncoder => _strings.Get();

    /// <summary>Export: the encoder stopped taking frames.</summary>
    public static string ExportFailedEncoderStalled => _strings.Get();

    /// <summary>Export: the encoder could not open while an earlier export's encoder call is still stuck.</summary>
    public static string ExportFailedEncoderStuck => _strings.Get();

    /// <summary>Export: the output folder cannot be written to.</summary>
    public static string ExportFailedFolderNotWritable => _strings.Get();

    /// <summary>Export: another app has the output file open.</summary>
    public static string ExportFailedFileInUse => _strings.Get();

    /// <summary>Export: the output folder, drive or share is not there.</summary>
    public static string ExportFailedFolderMissing => _strings.Get();

    /// <summary>Export: the disk is full.</summary>
    public static string ExportFailedDiskFull => _strings.Get();

    /// <summary>Export: no graphics device can run it.</summary>
    public static string ExportFailedNoGraphicsDevice => _strings.Get();

    /// <summary>Export: the driver reset or the GPU went away.</summary>
    public static string ExportFailedGraphicsDeviceLost => _strings.Get();

    /// <summary>Export: not enough system or video memory.</summary>
    public static string ExportFailedOutOfMemory => _strings.Get();

    /// <summary>Export: a frame's photo could not be loaded, when the app does not know which.</summary>
    public static string ExportFailedPhotoUnreadable => _strings.Get();

    /// <summary>Export: frame {0} (1-based), file name {1}, short reason {2} (<see cref="SkippedFileText.Reason"/>).</summary>
    public static string ExportFailedPhotoUnreadableNamed => _strings.Get();

    /// <summary>Export: frame {0} (1-based), file name {1}, no reason known.</summary>
    public static string ExportFailedPhotoUnreadableNamedNoReason => _strings.Get();

    /// <summary>Export: drawing a frame failed on the graphics device.</summary>
    public static string ExportFailedDrawing => _strings.Get();

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
    public static string ExportSavedBesideFormat => _strings.Get();

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
    public static string CopyDiagnostics => _strings.Get();

    /// <summary>The Success InfoBar after Copy diagnostics.</summary>
    public static string DiagnosticsCopied => _strings.Get();

    /// <summary>Diagnostics text: its first line (English, like the log it carries). {0} the product name.</summary>
    public static string DiagnosticsHeading => "{0} diagnostics";

    /// <summary>Diagnostics text: the heading of its log section. {0} the number of lines ("200").</summary>
    public static string DiagnosticsLogSection => "Last {0} log lines";

    /// <summary>The message box shown when the window cannot be created at startup. {0} the log folder.</summary>
    public static string StartupFailed => _strings.Get();

    // Settings page (#26 builds the frame and the diagnostics card; #21 and #31 add theirs).

    /// <summary>The Settings page title.</summary>
    public static string SettingsTitle => _strings.Get();

    /// <summary>The command bar button that opens Settings (its accessible name and tooltip).</summary>
    public static string SettingsButton => _strings.Get();

    /// <summary>The command bar button that leaves Settings.</summary>
    public static string Back => _strings.Get();

    /// <summary>The Settings section that holds the diagnostics card (the prototype's About, where #31's cards join it).</summary>
    public static string SettingsAboutSection => _strings.Get();

    /// <summary>The diagnostics card's title.</summary>
    public static string DiagnosticsCardTitle => _strings.Get();

    /// <summary>The diagnostics card's caption.</summary>
    public static string DiagnosticsCardCaption => _strings.Get();

    // ---- Remembered settings (#21)

    /// <summary>The Settings section that holds the reset card.</summary>
    public static string SettingsDefaultsSection => _strings.Get();

    /// <summary>The reset card's title.</summary>
    public static string ResetCardTitle => _strings.Get();

    /// <summary>The reset card's caption.</summary>
    public static string ResetCardCaption => _strings.Get();

    /// <summary>The reset card's button.</summary>
    public static string ResetButton => _strings.Get();

    /// <summary>The InfoBar that says the reset is done.</summary>
    public static string ResetDone => _strings.Get();

    /// <summary>Error context: resetting the settings failed.</summary>
    public static string ResetSettingsFailed => "Resetting the settings failed.";

    /// <summary>Error context (log only): following the project for the remembered settings failed.</summary>
    public static string SettingsAttachFailed => "Remembering the settings of this session failed.";

    /// <summary>Error context (log only): the remembered settings could not be written.</summary>
    public static string SettingsSaveFailed => "Saving your settings failed. They will be tried again after the next change.";

    /// <summary>Error context (log only): loading the remembered settings failed unexpectedly; the defaults are used.</summary>
    public static string SettingsLoadFailed => "Loading the remembered settings failed; using the defaults.";

    /// <summary>Log line: the final write of the settings took too long and was left to finish in the background.</summary>
    public static string SettingsSaveTimedOut => "Saving the settings took longer than 2 seconds; it carries on in the background.";

    /// <summary>Log line: what the settings file could not give at start-up (one line, so it is logged once).</summary>
    public static string SettingsLoadProblems(IReadOnlyList<string> problems) =>
        "Some remembered settings could not be read and use their defaults: " + string.Join(" ", problems);

    /// <summary>Log line: the settings were loaded, and how long it took.</summary>
    public static string SettingsLoaded(double milliseconds) =>
        Format("Remembered settings loaded in {0:0.0} ms.", milliseconds);

    /// <summary>Log line: a start-up phase begins (#90), with the time since the process started.</summary>
    public static string StartupPhase(string phase, double milliseconds) =>
        Format("Start-up: {0} ({1:0} ms).", phase, milliseconds);

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

    /// <summary>Error context: the window's size, state or visibility changed and following it failed (#90).</summary>
    public static string WindowChangeFailed => "Following a change of the window's size, state or visibility failed.";

    /// <summary>Error context: the tinted Mica backdrop failed; the window shows the flat Mica color instead (#90).</summary>
    public static string BackdropFailed => "Showing the Mica backdrop failed; the window shows its flat Mica color instead.";

    /// <summary>Error context (log only): moving focus after a screen change (Settings, the first photos, New project) failed (#38).</summary>
    public static string ScreenFocusFailed => "Moving focus after a screen change failed.";

    /// <summary>Error context (log only): raising a screen reader notification failed (#38); nothing else depends on it.</summary>
    public static string AnnouncementFailed => "Raising a screen reader notification failed.";

    /// <summary>Error context (log only): switching between the docked and the narrow inspector layout, or sizing the narrow panel, failed (#36).</summary>
    public static string LayoutSwitchFailed => "Switching the inspector between the side and the narrow layout failed.";

    /// <summary>Error context (log only): keeping focus where it was when the layout switched failed (#36).</summary>
    public static string LayoutFocusFailed => "Moving focus after the layout switched failed.";

    /// <summary>Error context (log only): an animation failed to start or finish (#35); the screen is at its end state.</summary>
    public static string MotionFailed => "An animation failed; the screen shows its end state.";

    /// <summary>Error context (log only): the arrival choreography (the preview developing, the strip filing in) failed (#35).</summary>
    public static string ArrivalFailed => "Animating the photos' arrival failed; the editor shows at rest.";

    /// <summary>Error context (log only): putting focus on the drop zone's Choose folder at launch failed.</summary>
    public static string FirstLaunchFocusFailed => "Putting focus on Choose folder at launch failed.";

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
        var (length, rate) = TimingLengthAndRate(frameCount, timing);
        return frameCount == 1
            ? Format(TimingReadoutOne, length, rate)
            : Format(TimingReadoutMany, Count(frameCount), length, rate);
    }

    /// <summary>The first part of the Timing panel's readout. {0} the length ("24.7 s").</summary>
    public static string TimingHeadlineOne => _strings.Get();

    /// <summary>The first part of the readout for 0 or several photos. {0} the count ("742"), {1} the length.</summary>
    public static string TimingHeadlineMany => _strings.Get();

    /// <summary>The second part of the readout. {0} the rate ("30 fps", "2 s per photo").</summary>
    public static string TimingDetail => _strings.Get();

    /// <summary>
    /// <see cref="TimingReadout"/> in two parts for the Timing panel, which sets the first in the larger type:
    /// ("742 photos → 24.7 s", "at 30 fps"). Joined with a space they read exactly as <see cref="TimingReadout"/>.
    /// </summary>
    /// <param name="frameCount">The number of photos.</param>
    /// <param name="timing">The speed.</param>
    /// <param name="culture">The culture of the numbers (decimal separator, grouping); invariant when null, as <see cref="TimingReadout"/> is.</param>
    public static (string Headline, string Detail) TimingReadoutParts(int frameCount, TimingSettings timing, CultureInfo? culture = null)
    {
        var (length, rate) = TimingLengthAndRate(frameCount, timing, culture);
        var headline = frameCount == 1
            ? Format(TimingHeadlineOne, length)
            : Format(TimingHeadlineMany, Count(frameCount, culture), length);
        return (headline, Format(TimingDetail, rate));
    }

    // The clip length and the rate label at the effective rate for this count.
    private static (string Length, string Rate) TimingLengthAndRate(int frameCount, TimingSettings timing, CultureInfo? culture = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameCount);
        ArgumentNullException.ThrowIfNull(timing);

        var effective = timing.EffectiveRate(frameCount).Rate;
        var length = TimingFormat.Duration(FrameTiming.Duration(frameCount, effective), culture);
        var rate = timing.Mode == SpeedMode.PerPhoto
            ? TimingFormat.SecondsPerPhoto(effective, culture)
            : TimingFormat.FrameRate(effective, culture);
        return (length, rate);
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

    // ---- Preview (#19): the player, its transport and its cache. One block so other issues' strings merge around it. ----

    /// <summary>The play button's name while paused.</summary>
    public static string PreviewPlay => _strings.Get();

    /// <summary>The play button's name while playing.</summary>
    public static string PreviewPause => _strings.Get();

    /// <summary>The scrubber's name.</summary>
    public static string PreviewScrubberName => _strings.Get();

    /// <summary>The transport's frame counter. {0} the frame number, {1} the total, both zero-padded to the same width ("0312 / 0742").</summary>
    public static string PreviewCounterFormat => _strings.Get();

    /// <summary>The frame counter as Narrator reads it. {0} the frame number, {1} the total ("Frame 312 of 742").</summary>
    public static string PreviewCounterNameFormat => _strings.Get();

    /// <summary>The time code as Narrator reads it. {0} the time, {1} the length.</summary>
    public static string PreviewTimecodeNameFormat => _strings.Get();

    /// <summary>A frame whose photo cannot be read, drawn in its place. {0} the file name.</summary>
    public static string PreviewPlaceholderFormat => _strings.Get();

    /// <summary>One photo of the project cannot be read while previewing. The detail list names it.</summary>
    public static string PreviewFrameUnreadableOne => _strings.Get();

    /// <summary>Several photos cannot be read while previewing. {0} the count ("3"). The detail list names them.</summary>
    public static string PreviewFrameUnreadableMany => _strings.Get();

    /// <summary>The proxy cache cannot be written: the preview works from the photos, slower.</summary>
    public static string PreviewCacheFailed => _strings.Get();

    /// <summary>Error context (log only): play or pause failed.</summary>
    public static string PreviewPlayFailed => "Playing or pausing the preview failed.";

    /// <summary>Error context (log only): a transport key or the scrubber failed.</summary>
    public static string PreviewSeekFailed => "Moving the preview's playhead failed.";

    /// <summary>Error context (log only): setting the window's icon failed; the window keeps the default icon.</summary>
    public static string WindowIconFailed => "Setting the window icon failed; the default icon stays.";

    /// <summary>Error context (log only): following a change of the project in the preview failed.</summary>
    public static string PreviewProjectFailed => "Following the project in the preview failed.";

    /// <summary>Error context: drawing the preview failed.</summary>
    public static string PreviewDrawFailed => "Drawing the preview failed.";

    /// <summary>Error context: the preview's drawing surface could not be made again after the graphics device was lost; it keeps trying.</summary>
    public static string PreviewRestartFailed => "Starting the preview again after the graphics driver reset failed. It keeps trying by itself.";

    /// <summary>Error context (log only): advancing the counter during playback failed.</summary>
    public static string PreviewTickFailed => "Advancing the preview's counter failed.";

    /// <summary>Error context (log only): a frame could not be read for the preview. {0} the path.</summary>
    public static string PreviewFrameFailedLog => "The preview could not read {0}.";

    /// <summary>Error context (log only): the proxy cache failed.</summary>
    public static string PreviewCacheFailedLog => "The preview cache could not save a proxy.";

    /// <summary>Error context (log only): the preview's frame cache failed in its own code.</summary>
    public static string PreviewCacheUnexpected => "The preview's frame cache failed.";

    /// <summary>Error context (log only): showing the preview's problems failed.</summary>
    public static string PreviewProblemsFailed => "Telling about a preview problem failed.";

    /// <summary>Error context (log only): cleaning up the proxy cache failed.</summary>
    public static string PreviewCacheCleanupFailed => "Cleaning up the preview cache failed.";

    /// <summary>"0312 / 0742": frame <paramref name="number"/> (from 1) of <paramref name="total"/>, zero-padded to four digits or the total's width.</summary>
    public static string PreviewCounter(int number, int total)
    {
        var (padded, paddedTotal) = PadFrames(number, total);
        return Format(PreviewCounterFormat, padded, paddedTotal);
    }

    /// <summary>"Frame 312 of 742": the counter for Narrator.</summary>
    public static string PreviewCounterName(int number, int total)
    {
        PadFrames(number, total);
        return Format(PreviewCounterNameFormat, number, total);
    }

    /// <summary>"Time 00:10.40 of 00:24.73": the time code for Narrator.</summary>
    public static string PreviewTimecodeName(long ticks, long lengthTicks) =>
        Format(PreviewTimecodeNameFormat, TimingFormat.Timecode(ticks), TimingFormat.Timecode(lengthTicks));

    /// <summary>"IMG_4402.JPG can't be read", drawn in the frame's place.</summary>
    public static string PreviewPlaceholder(string fileName) => Format(PreviewPlaceholderFormat, fileName);

    /// <summary>The InfoBar message for <paramref name="count"/> photos the preview cannot read.</summary>
    public static string PreviewFrameUnreadable(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return count == 1 ? PreviewFrameUnreadableOne : Format(PreviewFrameUnreadableMany, Count(count));
    }

    private static (string Number, string Total) PadFrames(int number, int total)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(total);
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number, total);
        var digits = "D" + Math.Max(4, total.ToString(CultureInfo.InvariantCulture).Length).ToString(CultureInfo.InvariantCulture);
        return (number.ToString(digits, CultureInfo.InvariantCulture), total.ToString(digits, CultureInfo.InvariantCulture));
    }

    // ---- End of the preview block (#19). ----

    // ---- Import (#12): Add photos, drag and drop, progress. One block so other issues' strings merge around it. ----

    /// <summary>The command bar button that opens the import menu.</summary>
    public static string AddPhotos => _strings.Get();

    /// <summary>The Add photos menu item that opens the folder picker.</summary>
    public static string ChooseFolder => _strings.Get();

    /// <summary>The Add photos menu item that opens the file picker.</summary>
    public static string ChoosePhotos => _strings.Get();

    /// <summary>The Add photos menu toggle that searches a folder's subfolders too (folders picked or dropped).</summary>
    public static string IncludeSubfolders => _strings.Get();

    /// <summary>The import progress while the first photos are being looked for.</summary>
    public static string ImportLooking => _strings.Get();

    /// <summary>The import progress while listing, one photo found so far.</summary>
    public static string ImportFoundOne => _strings.Get();

    /// <summary>The import progress while listing. {0} the photos found so far ("1,240").</summary>
    public static string ImportFoundMany => _strings.Get();

    /// <summary>The import progress while reading one photo.</summary>
    public static string ImportAddingOne => _strings.Get();

    /// <summary>The import progress while reading. {0} the photos read ("1,240"), {1} all of them ("5,000").</summary>
    public static string ImportAddingMany => _strings.Get();

    /// <summary>The accessible name of the progress's Cancel button.</summary>
    public static string ImportCancelName => _strings.Get();

    /// <summary>Narrator once an import's progress goes and the project holds one photo.</summary>
    public static string ImportDoneOne => _strings.Get();

    /// <summary>Narrator once an import's progress goes. {0} the photos in the project now ("742").</summary>
    public static string ImportDoneMany => _strings.Get();

    /// <summary>The drag caption when the count is not known (folders, or items still being looked at).</summary>
    public static string DragCaptionAny => _strings.Get();

    /// <summary>The drag caption for one photo file.</summary>
    public static string DragCaptionOne => _strings.Get();

    /// <summary>The drag caption for several photo files. {0} the count ("120").</summary>
    public static string DragCaptionMany => _strings.Get();

    /// <summary>The drag caption while Settings is open: the drop is refused until the user goes back.</summary>
    public static string DragCaptionSettingsOpen => _strings.Get();

    /// <summary>The title of the question a drop onto a project with photos asks.</summary>
    public static string DropTitle => _strings.Get();

    /// <summary>The drop question for a project with one photo.</summary>
    public static string DropMessageOne => _strings.Get();

    /// <summary>The drop question for a project with several photos. {0} the count ("742").</summary>
    public static string DropMessageMany => _strings.Get();

    /// <summary>The drop question's default button: add to the project that is open.</summary>
    public static string DropAddToProject => _strings.Get();

    /// <summary>A Caution InfoBar: nothing to import was found.</summary>
    public static string ImportNothingFound => _strings.Get();

    /// <summary>The same with subfolders off, when a folder was searched.</summary>
    public static string ImportNothingFoundSubfolders => _strings.Get();

    /// <summary>A Neutral InfoBar: every photo found was in the project already.</summary>
    public static string ImportAlreadyAdded => _strings.Get();

    /// <summary>A Neutral InfoBar after Cancel, when no photo was kept.</summary>
    public static string ImportStoppedNone => _strings.Get();

    /// <summary>A Neutral InfoBar after Cancel, one photo kept.</summary>
    public static string ImportStoppedOne => _strings.Get();

    /// <summary>A Neutral InfoBar after Cancel. {0} the photos kept ("1,240").</summary>
    public static string ImportStoppedMany => _strings.Get();

    /// <summary>
    /// A Caution InfoBar: a folder of the import would not open, or a path was no longer there. {0} the folder's name
    /// (its full path for a drive).
    /// </summary>
    public static string ImportFolderFailedOne => _strings.Get();

    /// <summary>The same for several folders, listed under View list. {0} the count ("3").</summary>
    public static string ImportFolderFailedMany => _strings.Get();

    /// <summary>The title of the question an import asks when photos are only in OneDrive (files on demand).</summary>
    public static string CloudDownloadTitle => _strings.Get();

    /// <summary>The question for one online-only photo. {0} its size ("12 MB").</summary>
    public static string CloudDownloadOne => _strings.Get();

    /// <summary>The question for several online-only photos. {0} the count ("1,240"), {1} their size ("2.4 GB").</summary>
    public static string CloudDownloadMany => _strings.Get();

    /// <summary>The question's button that downloads the photos and adds them.</summary>
    public static string CloudDownloadConfirm => _strings.Get();

    /// <summary>The question's default button for one photo: leave it in the cloud and list it as skipped.</summary>
    public static string CloudDownloadSkipOne => _strings.Get();

    /// <summary>The question's default button for several photos.</summary>
    public static string CloudDownloadSkipMany => _strings.Get();

    /// <summary>A size under 1 GB. {0} the megabytes, whole ("340").</summary>
    public static string SizeMegabytes => _strings.Get();

    /// <summary>A size of 1 GB or more. {0} the gigabytes, one decimal ("2.4").</summary>
    public static string SizeGigabytes => _strings.Get();

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

    /// <summary>Narrator once the import's progress goes: the project's frames now ("742 photos in the project.").</summary>
    public static string ImportDoneAnnouncement(int frames) =>
        frames == 1 ? ImportDoneOne : Format(ImportDoneMany, Count(frames));

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

    // ---- Photo extensions (#14): the InfoBars that send the user to the Microsoft Store. One block. ----

    /// <summary>The button of an extension InfoBar. It opens the Store app; nothing is downloaded by 10Lapse.</summary>
    public static string CodecGetFromStore => _strings.Get();

    /// <summary>One photo needs one free extension. {0} its Store name ("HEIF Image Extensions").</summary>
    public static string CodecNeedsOneFree => _strings.Get();

    /// <summary>Several photos need one free extension. {0} the count, {1} its Store name.</summary>
    public static string CodecNeedsManyFree => _strings.Get();

    /// <summary>One photo needs one extension the Store sells (HEVC). {0} its Store name.</summary>
    public static string CodecNeedsOneFee => _strings.Get();

    /// <summary>Several photos need one extension the Store sells. {0} the count, {1} its Store name.</summary>
    public static string CodecNeedsManyFee => _strings.Get();

    /// <summary>One photo needs several extensions. {0} how many in words, {1} the list.</summary>
    public static string CodecNeedsOneSeveral => _strings.Get();

    /// <summary>Several photos need several extensions. {0} the count, {1} how many in words, {2} the list ("the free HEIF Image Extensions, then the HEVC Video Extensions (a small fee)").</summary>
    public static string CodecNeedsManySeveral => _strings.Get();

    /// <summary>One free extension inside a list. {0} its Store name.</summary>
    public static string CodecListFree => _strings.Get();

    /// <summary>One extension the Store sells, inside a list. {0} its Store name.</summary>
    public static string CodecListFee => _strings.Get();

    /// <summary>"Two", for how many extensions a list names.</summary>
    public static string CodecCountTwo => _strings.Get();

    /// <summary>"Several", for how many extensions a list names when it is more than two.</summary>
    public static string CodecCountSeveral => _strings.Get();

    /// <summary>Between the extensions of a list, in install order.</summary>
    public static string CodecListSeparator => _strings.Get();

    /// <summary>The InfoBar that follows the first button of a list. {0} the next extension as <see cref="CodecListFree"/> or <see cref="CodecListFee"/>.</summary>
    public static string CodecNextFormat => _strings.Get();

    /// <summary>When Windows could not open the Store page. {0} the extension's Store name, to search for.</summary>
    public static string CodecStoreFallbackFormat => _strings.Get();

    /// <summary>The log context when opening the Store page threw.</summary>
    public static string CodecStoreOpenFailed => "Opening the Microsoft Store page of a photo extension failed.";

    /// <summary>The words of the InfoBar for <paramref name="count"/> photos that need <paramref name="needed"/>, in install order.</summary>
    public static string CodecNeeded(int count, IReadOnlyList<PhotoCodecExtension> needed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentNullException.ThrowIfNull(needed);
        ArgumentOutOfRangeException.ThrowIfZero(needed.Count);
        var number = count.ToString("N0", CultureInfo.InvariantCulture);
        if (needed.Count == 1)
        {
            var extension = needed[0];
            return (count == 1, extension.HasFee) switch
            {
                (true, false) => Format(CodecNeedsOneFree, extension.Name),
                (true, true) => Format(CodecNeedsOneFee, extension.Name),
                (false, false) => Format(CodecNeedsManyFree, number, extension.Name),
                (false, true) => Format(CodecNeedsManyFee, number, extension.Name),
            };
        }

        var list = string.Join(CodecListSeparator, needed.Select(CodecListed));
        var words = needed.Count == 2 ? CodecCountTwo : CodecCountSeveral;
        return count == 1 ? Format(CodecNeedsOneSeveral, words, list) : Format(CodecNeedsManySeveral, number, words, list);
    }

    /// <summary>The InfoBar raised after the button for an earlier extension of a list was pressed.</summary>
    public static string CodecNext(PhotoCodecExtension next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return Format(CodecNextFormat, CodecListed(next));
    }

    /// <summary>The InfoBar for a Store page that would not open, naming the extension to search for.</summary>
    public static string CodecStoreFallback(PhotoCodecExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        return Format(CodecStoreFallbackFormat, extension.Name);
    }

    private static string CodecListed(PhotoCodecExtension extension) =>
        Format(extension.HasFee ? CodecListFee : CodecListFree, extension.Name);

    // ---- End of the photo extensions block (#14). ----

    // ---- First launch (#29): the drop zone and its small timelapse. ----

    /// <summary>The drop zone's title at rest.</summary>
    public static string FirstLaunchTitle => _strings.Get();

    /// <summary>The drop zone's title while photos are dragged over the window.</summary>
    public static string FirstLaunchDropTitle => _strings.Get();

    /// <summary>
    /// What a timelapse is, in one sentence after the lead: photos shot at an interval, played back as a clip
    /// (Main.dc.html, the approved copy). {0} the product name, {1} the photo count ("300"), {2} the frame rate
    /// ("30"), {3} the whole seconds of the clip ("10").
    /// </summary>
    public static string FirstLaunchExplanationFormat => _strings.Get();

    /// <summary>The privacy line under the actions.</summary>
    public static string FirstLaunchPrivacy => _strings.Get();

    /// <summary>The drop zone's accent button, the same command as <see cref="ChooseFolder"/>.</summary>
    public static string FirstLaunchChooseFolder => _strings.Get();

    /// <summary>The drop zone's standard button, the same command as <see cref="ChoosePhotos"/>.</summary>
    public static string FirstLaunchChoosePhotos => _strings.Get();

    /// <summary>A frame's place in a sequence. {0} the frame number, {1} the total, both zero-padded to four digits ("0312", "0742").</summary>
    public static string FrameOfTotal => _strings.Get();

    /// <summary>A time code and the clip length. {0} the time code ("00:10.40"), {1} the length as a time code ("00:24.73").</summary>
    public static string TimecodeOfTotal => _strings.Get();

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
    /// must make a whole number of seconds at a whole frame rate ("300 of them become a 10-second clip at 30 frames per second").
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

    // ---- Filmstrip (#20): thumbnails, selection, removal, undo, reorder and gap markers. One block so other issues' strings merge around it. ----

    /// <summary>The filmstrip's name for Narrator: a list of the project's frames.</summary>
    public static string FilmstripName => _strings.Get();

    /// <summary>A thumbnail's name for Narrator. {0} the frame number from 1, not padded ("312"), {1} the file name ("IMG_0312.JPG").</summary>
    public static string FilmstripItemNameFormat => _strings.Get();

    /// <summary>The caption with several frames selected. {0} the frame caption ("Frame 0312 of 0742"), {1} how many are selected ("3").</summary>
    public static string FilmstripCaptionSelectedFormat => _strings.Get();

    /// <summary>The file caption with a capture time. {0} the file name, {1} the time the photo was taken ("19:26:05").</summary>
    public static string FilmstripFileTimeFormat => _strings.Get();

    /// <summary>A gap marker's text (tooltip and Narrator) for one number skipped.</summary>
    public static string FilmstripGapMissingOne => _strings.Get();

    /// <summary>A gap marker's text for several numbers skipped. {0} how many ("3").</summary>
    public static string FilmstripGapMissingMany => _strings.Get();

    /// <summary>A gap marker's text for a long interval. {0} the interval ("2 min 10 s"), {1} the usual one ("5.0 s").</summary>
    public static string FilmstripGapPause => _strings.Get();

    /// <summary>Narrator's state of the thumbnail at the playhead.</summary>
    public static string FilmstripPlayheadStatus => _strings.Get();

    /// <summary>The context menu's removal; Delete does the same. The photos stay on disk.</summary>
    public static string FilmstripRemove => _strings.Get();

    /// <summary>The context menu's undo; Ctrl+Z does the same.</summary>
    public static string FilmstripUndo => _strings.Get();

    /// <summary>The context menu's move one place earlier; Alt+Left does the same.</summary>
    public static string FilmstripMoveEarlier => _strings.Get();

    /// <summary>The context menu's move one place later; Alt+Right does the same.</summary>
    public static string FilmstripMoveLater => _strings.Get();

    /// <summary>The key shown beside the context menu's removal.</summary>
    public static string FilmstripRemoveKeys => _strings.Get();

    /// <summary>The keys shown beside the context menu's undo.</summary>
    public static string FilmstripUndoKeys => _strings.Get();

    /// <summary>The keys shown beside the context menu's move earlier.</summary>
    public static string FilmstripMoveEarlierKeys => _strings.Get();

    /// <summary>The keys shown beside the context menu's move later.</summary>
    public static string FilmstripMoveLaterKeys => _strings.Get();

    /// <summary>The thumbnail size slider's name.</summary>
    public static string FilmstripThumbnailSize => _strings.Get();

    /// <summary>Narrator after removing one frame.</summary>
    public static string FilmstripRemovedOne => _strings.Get();

    /// <summary>Narrator after removing several frames. {0} how many ("3").</summary>
    public static string FilmstripRemovedMany => _strings.Get();

    /// <summary>Narrator after an undo put one frame back.</summary>
    public static string FilmstripRestoredOne => _strings.Get();

    /// <summary>Narrator after an undo put several frames back. {0} how many.</summary>
    public static string FilmstripRestoredMany => _strings.Get();

    /// <summary>An undo of a removal that found every photo back in the project already (imported again).</summary>
    public static string FilmstripRestoredNone => _strings.Get();

    /// <summary>The context menu's Undo of a removal of one frame.</summary>
    public static string FilmstripUndoRemoveOne => _strings.Get();

    /// <summary>The context menu's Undo of a removal of several frames. {0} the number of frames removed ("12").</summary>
    public static string FilmstripUndoRemoveMany => _strings.Get();

    /// <summary>The context menu's Undo of a move.</summary>
    public static string FilmstripUndoMove => _strings.Get();

    /// <summary>Narrator after an undo of a move.</summary>
    public static string FilmstripReorderUndone => _strings.Get();

    /// <summary>Narrator when Ctrl+Z has nothing to undo.</summary>
    public static string FilmstripNothingToUndo => _strings.Get();

    /// <summary>Narrator after moving one frame. {0} its number before, {1} its place now.</summary>
    public static string FilmstripMovedOne => _strings.Get();

    /// <summary>Narrator after moving several frames. {0} how many, {1} the first one's place now.</summary>
    public static string FilmstripMovedMany => _strings.Get();

    /// <summary>Log-only context: removing frames failed.</summary>
    public static string FilmstripRemoveFailed => "Removing frames from the project failed.";

    /// <summary>Log-only context: undoing failed.</summary>
    public static string FilmstripUndoFailed => "Undoing the last filmstrip change failed.";

    /// <summary>Log-only context: moving frames failed.</summary>
    public static string FilmstripMoveFailed => "Moving frames in the filmstrip failed.";

    /// <summary>Log-only context: a click, a key or Narrator selecting frames failed.</summary>
    public static string FilmstripSelectFailed => "Selecting frames in the filmstrip failed.";

    /// <summary>Log-only context: following the project's frames failed.</summary>
    public static string FilmstripProjectFailed => "Following the project in the filmstrip failed.";

    /// <summary>Log-only context: putting a thumbnail on screen failed.</summary>
    public static string FilmstripThumbnailFailed => "Showing a filmstrip thumbnail failed.";

    /// <summary>Log-only context: the thumbnail cache failed.</summary>
    public static string FilmstripThumbnailsUnexpected => "The filmstrip's thumbnail cache failed.";

    /// <summary>Log-only context: keeping the playhead in view or loading what is in view failed.</summary>
    public static string FilmstripScrollFailed => "Following the playhead or the view in the filmstrip failed.";

    /// <summary>"Frame 312, IMG_0312.JPG".</summary>
    public static string FilmstripItemName(int number, string fileName) => Format(FilmstripItemNameFormat, number, fileName);

    /// <summary>"Frame 0312 of 0742", with " · 3 selected" when more than one frame is selected; empty without frames.</summary>
    public static string FilmstripCaption(int number, int total, int selected)
    {
        if (total <= 0)
        {
            return string.Empty;
        }

        var caption = FrameCaption(Math.Clamp(number, 1, total), total);
        return selected > 1 ? Format(FilmstripCaptionSelectedFormat, caption, Count(selected)) : caption;
    }

    /// <summary>"IMG_0312.JPG · 19:26:05", or the name alone when the capture time is not known.</summary>
    public static string FilmstripFileCaption(string fileName, DateTime? captureTime) =>
        captureTime is { } time ? Format(FilmstripFileTimeFormat, fileName, time.ToString("HH:mm:ss", CultureInfo.InvariantCulture)) : fileName;

    /// <summary>What a gap marker says.</summary>
    public static string FilmstripGap(FrameGap gap)
    {
        ArgumentNullException.ThrowIfNull(gap);
        if (gap.Kinds.HasFlag(FrameGapKinds.Numbering) && gap.MissingFrames > 0)
        {
            return gap.MissingFrames == 1 ? FilmstripGapMissingOne : Format(FilmstripGapMissingMany, Count(gap.MissingFrames));
        }

        return gap.Interval is { } interval && gap.TypicalInterval is { } typical
            ? Format(FilmstripGapPause, TimingFormat.Duration(interval.Ticks), TimingFormat.Duration(typical.Ticks))
            : FilmstripGapMissingOne;
    }

    /// <summary>What Narrator hears after a removal of <paramref name="count"/> frames.</summary>
    public static string FilmstripRemoved(int count) => count == 1 ? FilmstripRemovedOne : Format(FilmstripRemovedMany, Count(count));

    /// <summary>What Narrator hears after an undo put <paramref name="count"/> frames back.</summary>
    public static string FilmstripRestored(int count) => count switch
    {
        0 => FilmstripRestoredNone,
        1 => FilmstripRestoredOne,
        _ => Format(FilmstripRestoredMany, Count(count)),
    };

    /// <summary>The context menu's Undo, named after <paramref name="next"/>, the edit it would undo; plain "Undo" for none.</summary>
    public static string FilmstripUndoText(FrameEdit? next) => next switch
    {
        { Kind: FrameEditKind.Removal, Count: 1 } => FilmstripUndoRemoveOne,
        { Kind: FrameEditKind.Removal } edit => Format(FilmstripUndoRemoveMany, Count(edit.Count)),
        { Kind: FrameEditKind.Reorder } => FilmstripUndoMove,
        _ => FilmstripUndo,
    };

    /// <summary>What Narrator hears after a move: the frame's old number (one frame) or the count, and its place now (from 1).</summary>
    public static string FilmstripMoved(int count, int oldNumber, int newPlace) =>
        count == 1 ? Format(FilmstripMovedOne, oldNumber, newPlace) : Format(FilmstripMovedMany, Count(count), newPlace);

    // ---- End of the filmstrip block (#20). ----

    // "5,000": grouped, invariant (3.0 is English only; a translation would pass the culture, as the Timing panel does).
    private static string Count(int count, CultureInfo? culture = null) => count.ToString("N0", culture ?? CultureInfo.InvariantCulture);

    /// <summary>Fills a format string with the invariant culture (3.0 is English only, so its numbers stay unlocalized).</summary>
    internal static string Format(string format, params object[] values) =>
        string.Format(CultureInfo.InvariantCulture, format, values);
}
