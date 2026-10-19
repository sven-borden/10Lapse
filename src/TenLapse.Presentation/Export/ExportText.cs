using System.Globalization;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;

namespace TenLapse.Presentation.Export;

/// <summary>
/// Every word of the export (#24): the Export button, the export panel, the finished state, the InfoBars, the
/// notification and what Narrator hears. Whole sentences with numbered placeholders, so #39 can move them to resource
/// files and a translation can reorder them. Numbers are written in the user's culture, as the inspector's panels do.
/// </summary>
public static class ExportText
{
    // ---- The command bar ----

    /// <summary>The amber button in the command bar (Ctrl+E).</summary>
    public static string ExportVideo => "Export video";

    /// <summary>The same button, dimmed, while an export runs.</summary>
    public static string ExportingButton => "Exporting…";

    // ---- The export panel ----

    /// <summary>The caption over the file name while the export runs.</summary>
    public static string Exporting => "Exporting";

    /// <summary>The large percent: {0} the whole percent.</summary>
    public static string PercentFormat => "{0}%";

    /// <summary>The progress bar's name for a screen reader.</summary>
    public static string ProgressName => "Export progress";

    /// <summary>Before the first frame is written.</summary>
    public static string GettingReady => "Getting ready…";

    /// <summary>Frames are being written but the speed is not known yet.</summary>
    public static string EstimatingTimeLeft => "Working out the time left…";

    /// <summary>Every frame is in; the file is being completed.</summary>
    public static string Finishing => "Finishing the file…";

    /// <summary>Time left under a minute: {0} the seconds.</summary>
    public static string TimeLeftSecondsFormat => "About {0} s left";

    /// <summary>Time left from a minute, on the minute: {0} the minutes.</summary>
    public static string TimeLeftMinutesFormat => "About {0} min left";

    /// <summary>Time left from a minute, with a half minute: {0} the minutes.</summary>
    public static string TimeLeftMinutesHalfFormat => "About {0} min 30 s left";

    /// <summary>The label of the frame count row.</summary>
    public static string FrameLabel => "Frame";

    /// <summary>The frame count: {0} the frames written, {1} the total, both zero-padded to the total's width ("0312 of 0742").</summary>
    public static string FramesFormat => "{0} of {1}";

    /// <summary>The label of the speed row.</summary>
    public static string SpeedLabel => "Speed";

    /// <summary>The speed: {0} photos per second, one decimal.</summary>
    public static string SpeedFormat => "{0} photos per second";

    /// <summary>The speed before it has been measured.</summary>
    public static string SpeedMeasuring => "Measuring…";

    /// <summary>The label of the output row.</summary>
    public static string OutputLabel => "Output";

    /// <summary>The output summary: {0} the resolution ("4K"), {1} the codec ("H.264"), {2} the quality ("High").</summary>
    public static string OutputSummaryFormat => "{0} · {1} · {2}";

    /// <summary>The hint under the numbers.</summary>
    public static string MinimizeHint => "You can minimize 10Lapse. The export keeps going and Windows tells you when it's done.";

    /// <summary>The button that stops the export.</summary>
    public static string CancelExport => "Cancel export";

    /// <summary>The label over the preview while the export runs: {0} the frame being written, zero-padded ("0313").</summary>
    public static string RenderingFrameFormat => "Rendering frame {0}";

    // ---- The finished state ----

    /// <summary>The finished state's title and Narrator's announcement: {0} the file name ("Harbor sunset.mp4").</summary>
    public static string ReadyFormat => "{0} is ready";

    /// <summary>The summary under it: {0} the length ("24.7 s"), {1} the resolution ("4K"), {2} the codec, {3} the file size ("173 MB").</summary>
    public static string ReadySummaryFormat => "{0} · {1} · {2} · {3}";

    /// <summary>The label of the time the export took.</summary>
    public static string ExportedInLabel => "Exported in";

    /// <summary>The label of the folder.</summary>
    public static string SavedToLabel => "Saved to";

    /// <summary>The folder, shortened to its last two names: {0} the parent folder, {1} the folder ("Videos › 10Lapse").</summary>
    public static string SavedToFormat => "{0} › {1}";

    /// <summary>The accent button: plays the video in the preview from frame one.</summary>
    public static string Play => "Play";

    /// <summary>Its name for a screen reader.</summary>
    public static string PlayName => "Play the video";

    /// <summary>Opens File Explorer on the video.</summary>
    public static string ShowInFolder => "Show in folder";

    /// <summary>Back to the inspector.</summary>
    public static string BackToEditing => "Back to editing";

    /// <summary>The name of the check for a screen reader.</summary>
    public static string DoneName => "Done";

    // ---- InfoBars and questions ----

    /// <summary>The InfoBar once a cancelled export's unfinished file is gone.</summary>
    public static string Cancelled => "Export cancelled. The unfinished file was removed.";

    /// <summary>The InfoBar when a cancelled export's unfinished file could not be removed yet.</summary>
    public static string CancelledFileLeft => "Export cancelled. The unfinished file couldn't be removed yet, so 10Lapse removes it the next time it starts.";

    /// <summary>An edit tried while the export runs (a removal, a move, an import, a new project).</summary>
    public static string EditsWait => "The project can't change while the video exports. Wait for it to finish, or cancel the export.";

    /// <summary>Export tried while photos are still being added.</summary>
    public static string ImportStillRunning => "Photos are still being added. Export once they're all in.";

    /// <summary>The Save dialog was answered, but the project, its output or the screen changed while it was open.</summary>
    public static string ChangedDuringDialog => "The export didn't start because the project or its output changed while the Save dialog was open. Choose Export video again.";

    /// <summary>The finished video saved, but the preview can't play it (a codec Windows lacks, say).</summary>
    public static string PremiereCantPlay => "The video was saved, but 10Lapse couldn't play it here. Open it with Show in folder to check it.";

    /// <summary>The question when the window closes during an export.</summary>
    public static string CloseTitle => "Stop the export and close?";

    /// <summary>What closing does.</summary>
    public static string CloseMessage => "The video isn't finished yet. 10Lapse stops the export and removes the unfinished file.";

    /// <summary>Closes the window.</summary>
    public static string CloseConfirm => "Stop and close";

    /// <summary>Keeps the window and the export.</summary>
    public static string CloseCancel => "Keep exporting";

    /// <summary>The Save dialog's file type.</summary>
    public static string FileTypeName => "MP4 video";

    // ---- Narrator and the notification ----

    /// <summary>Narrator, as the export starts: {0} the file name.</summary>
    public static string StartedFormat => "Exporting {0}.";

    /// <summary>Narrator, at most every ten percent: {0} the percent.</summary>
    public static string ProgressAnnouncementFormat => "{0} percent exported.";

    /// <summary>Narrator, when the export fails (the InfoBar says why).</summary>
    public static string FailedAnnouncement => "The export stopped.";

    /// <summary>The notification when an export in the background failed: {0} the file name.</summary>
    public static string FailedNotificationFormat => "{0} couldn't be exported. Open 10Lapse to see why.";

    // ---- Log contexts (not shown) ----

    /// <summary>Log: the export failed.</summary>
    public static string ExportFailed => "Exporting the video failed.";

    /// <summary>Log: cancelling the export failed.</summary>
    public static string CancelFailed => "Cancelling the export failed.";

    /// <summary>Log: Show in folder failed.</summary>
    public static string ShowInFolderFailed => "Showing the video in its folder failed.";

    /// <summary>Log: playing the finished video failed.</summary>
    public static string PlayFailed => "Playing the exported video failed.";

    /// <summary>Log: going back to editing failed.</summary>
    public static string BackFailed => "Going back to editing after the export failed.";

    /// <summary>Log: a progress report could not be shown.</summary>
    public static string ProgressFailed => "Showing the export progress failed.";

    /// <summary>What <c>powercfg /requests</c> shows next to 10Lapse while a video is written.</summary>
    public static string KeepAwakeReason(string fileName) => $"Exporting {fileName}";

    /// <summary>Log: Windows would not keep the PC awake for the export (the export goes on).</summary>
    public static string KeepAwakeFailed => "Asking Windows to keep the PC awake during the export failed. The export goes on; the PC may go to sleep.";

    /// <summary>Log: letting go of the export's power request failed.</summary>
    public static string KeepAwakeReleaseFailed => "Letting go of the export's power request failed.";

    /// <summary>Log: the taskbar progress could not be set.</summary>
    public static string TaskbarFailed => "Setting the taskbar progress failed.";

    /// <summary>Log: the notification could not be shown or registered.</summary>
    public static string NotificationFailed => "Showing the export notification failed.";

    /// <summary>Log: the close check failed.</summary>
    public static string CloseFailed => "Stopping the export to close the window failed.";

    /// <summary>Log: the export panel could not swap or move focus.</summary>
    public static string PanelFailed => "Showing the export panel failed.";

    /// <summary>Log: the finished video could not be played in the preview.</summary>
    public static string PremiereFailed => "Playing the exported video in the preview failed.";

    // ---- Builders ----

    /// <summary>"46%" (whole percent, rounded down so it reaches 100 only at the end).</summary>
    public static string Percent(double fraction, CultureInfo? culture = null) =>
        string.Format(culture ?? CultureInfo.CurrentCulture, PercentFormat, WholePercent(fraction));

    /// <summary>The whole percent of <paramref name="fraction"/>, rounded down, 0 to 100.</summary>
    public static int WholePercent(double fraction) => (int)Math.Floor(Math.Clamp(fraction, 0, 1) * 100);

    /// <summary>
    /// "About 40 s left" under a minute, then whole minutes with a half ("About 2 min 30 s left"), as the canvas writes it.
    /// </summary>
    public static string TimeLeft(TimeSpan left, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var seconds = (long)Math.Round(Math.Max(0, left.TotalSeconds));
        if (seconds < 60)
        {
            return string.Format(culture, TimeLeftSecondsFormat, seconds);
        }

        var minutes = seconds / 60;
        return string.Format(culture, seconds % 60 >= 30 ? TimeLeftMinutesHalfFormat : TimeLeftMinutesFormat, minutes);
    }

    /// <summary>The time-left line for a progress report.</summary>
    public static string TimeLeft(ExportStatus status, CultureInfo? culture = null) => status.Phase switch
    {
        ExportPhase.Preparing => GettingReady,
        ExportPhase.Finishing => Finishing,
        _ => status.TimeLeft is { } left ? TimeLeft(left, culture) : EstimatingTimeLeft,
    };

    /// <summary>"0312 of 0742": both zero-padded to the total's width (DESIGN.md, the Tabular Rule).</summary>
    public static string Frames(int done, int total, CultureInfo? culture = null)
    {
        var width = Math.Max(4, total.ToString(CultureInfo.InvariantCulture).Length);
        return string.Format(culture ?? CultureInfo.CurrentCulture, FramesFormat, Pad(done, width), Pad(total, width));
    }

    /// <summary>"Rendering frame 0313": the 1-based frame being written, zero-padded to the total's width.</summary>
    public static string RenderingFrame(int frameIndex, int total, CultureInfo? culture = null)
    {
        var width = Math.Max(4, total.ToString(CultureInfo.InvariantCulture).Length);
        return string.Format(culture ?? CultureInfo.CurrentCulture, RenderingFrameFormat, Pad(Math.Clamp(frameIndex + 1, 1, Math.Max(1, total)), width));
    }

    /// <summary>"4.3 photos per second", or <see cref="SpeedMeasuring"/> before it is known.</summary>
    public static string Speed(double? framesPerSecond, CultureInfo? culture = null) =>
        framesPerSecond is > 0 and var speed
            ? string.Format(culture ?? CultureInfo.CurrentCulture, SpeedFormat, speed.ToString(speed < 10 ? "0.0" : "0", culture ?? CultureInfo.CurrentCulture))
            : SpeedMeasuring;

    /// <summary>"4K · H.264 · High".</summary>
    public static string OutputSummary(OutputSettings output, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        return string.Format(culture ?? CultureInfo.CurrentCulture, OutputSummaryFormat, OutputText.ResolutionName(output.Resolution), OutputText.CodecName(output.Format), OutputText.QualityOption(output.Quality));
    }

    /// <summary>"Harbor sunset.mp4 is ready".</summary>
    public static string Ready(string fileName) => string.Format(CultureInfo.CurrentCulture, ReadyFormat, fileName);

    /// <summary>"24.7 s · 4K · H.264 · 173 MB", from the result and the real file size.</summary>
    public static string ReadySummary(ExportedVideo video, OutputResolution resolution, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(video);
        culture ??= CultureInfo.CurrentCulture;
        return string.Format(culture, ReadySummaryFormat, TimingFormat.Duration(video.Duration, culture), OutputText.ResolutionName(resolution), OutputText.CodecName(video.Format), FileSize(video.FileBytes, culture));
    }

    /// <summary>
    /// The finished file's real size to three significant figures, trailing zeros left out ("173 MB", "18.4 MB",
    /// "1.23 GB"), in units of 1,024 as Explorer counts them; "Less than 1 MB" below a megabyte. The Output panel's
    /// estimate keeps two figures (<see cref="OutputText.Size"/>): it is a guess, this is the file.
    /// </summary>
    public static string FileSize(long bytes, CultureInfo? culture = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        culture ??= CultureInfo.CurrentCulture;
        const double megabyte = 1L << 20;
        const double gigabyte = 1L << 30;
        if (bytes < megabyte)
        {
            return OutputText.UnderOneMegabyte;
        }

        var megabytes = bytes / megabyte;
        return Math.Round(megabytes) < 1000
            ? string.Format(culture, OutputText.Megabytes, ThreeFigures(megabytes, culture))
            : string.Format(culture, OutputText.Gigabytes, ThreeFigures(Math.Max(1, bytes / gigabyte), culture));
    }

    private static string ThreeFigures(double value, CultureInfo culture) =>
        value.ToString(value < 9.995 ? "0.##" : value < 99.95 ? "0.#" : "0", culture);

    /// <summary>"Videos › 10Lapse": the video's folder by its last two names (one when it is a drive's root).</summary>
    public static string SavedTo(string videoPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(videoPath);
        var folder = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(videoPath) ?? string.Empty);
        var name = Path.GetFileName(folder);
        if (name.Length == 0)
        {
            return folder.Length == 0 ? videoPath : folder;
        }

        var parent = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(folder) ?? string.Empty);
        var parentName = Path.GetFileName(parent);
        if (parentName.Length == 0)
        {
            // The folder is at a drive's root: "D: › Videos".
            parentName = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        return parentName.Length == 0 ? name : string.Format(CultureInfo.CurrentCulture, SavedToFormat, parentName, name);
    }

    /// <summary>"Exporting Harbor sunset.mp4."</summary>
    public static string Started(string fileName) => string.Format(CultureInfo.CurrentCulture, StartedFormat, fileName);

    /// <summary>"40 percent exported."</summary>
    public static string ProgressAnnouncement(int percent) => string.Format(CultureInfo.CurrentCulture, ProgressAnnouncementFormat, percent);

    /// <summary>"Harbor sunset.mp4 couldn't be exported. Open 10Lapse to see why."</summary>
    public static string FailedNotification(string fileName) => string.Format(CultureInfo.CurrentCulture, FailedNotificationFormat, fileName);

    /// <summary>
    /// The Save dialog's suggested name: the project name with what Windows does not allow in a file name left out, and
    /// <c>.mp4</c>. A name with nothing left becomes the default project name.
    /// </summary>
    public static string SuggestedFileName(string projectName)
    {
        var name = new string([.. (projectName ?? string.Empty).Where(c => c >= ' ' && !"<>:\"/\\|?*".Contains(c))]).Trim().TrimEnd('.').Trim();
        return (name.Length == 0 ? Project.DefaultName : name) + ".mp4";
    }

    private static string Pad(int value, int width) => value.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
}
