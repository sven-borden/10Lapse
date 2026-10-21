using System.Globalization;
using TenLapse.Core.Projects;
using TenLapse.Core.Text;
using TenLapse.Core.Timing;

namespace TenLapse.Presentation.Export;

/// <summary>
/// Every word of the export (#24): the Export button, the export panel, the finished state, the InfoBars, the
/// notification and what Narrator hears, read from <c>Strings/ExportText.resx</c> (#39); the log lines stay here in
/// English. Whole sentences with numbered placeholders, so a translation can reorder them. Numbers are written in the user's culture, as the inspector's panels do.
/// </summary>
public static class ExportText
{
    private static readonly TextResources _strings = new("TenLapse.Presentation.Strings.ExportText", typeof(ExportText).Assembly);

    // ---- The command bar ----

    /// <summary>The amber button in the command bar (Ctrl+E).</summary>
    public static string ExportVideo => _strings.Get();

    /// <summary>
    /// The Export button's shortcut as a screen reader names it (its accelerator key). The key E is fixed in code; translate only "Control", as Windows names that key in the language ("Strg+E" in German).
    /// </summary>
    public static string ExportKeys => _strings.Get();

    /// <summary>The same button, dimmed, while an export runs.</summary>
    public static string ExportingButton => _strings.Get();

    // ---- The export panel ----

    /// <summary>The caption over the file name while the export runs.</summary>
    public static string Exporting => _strings.Get();

    /// <summary>The running title's name for a screen reader, where focus lands as the export starts: {0} the file name.</summary>
    public static string RunningNameFormat => _strings.Get();

    /// <summary>"Exporting Harbor sunset.mp4", the running title's name for a screen reader.</summary>
    public static string RunningName(string? fileName) => string.Format(CultureInfo.CurrentCulture, RunningNameFormat, fileName ?? string.Empty);

    /// <summary>The large percent: {0} the whole percent.</summary>
    public static string PercentFormat => _strings.Get();

    /// <summary>The progress bar's name for a screen reader.</summary>
    public static string ProgressName => _strings.Get();

    /// <summary>Before the first frame is written.</summary>
    public static string GettingReady => _strings.Get();

    /// <summary>The time-left line while deflicker measures the photos before the first frame (#45).</summary>
    public static string WaitingForDeflicker => _strings.Get();

    /// <summary>"Deflicker is measuring the photos first: {0} of {1}" (#45): {0} measured, {1} all.</summary>
    public static string MeasuringFirstFormat => _strings.Get();

    /// <summary>The screen reader name of the measuring bar in the export panel (#45).</summary>
    public static string MeasuringFirstName => _strings.Get();

    /// <summary>Said once as an export waits for deflicker's measuring (#45).</summary>
    public static string MeasuringFirstAnnouncement => _strings.Get();

    /// <summary>"Deflicker is measuring the photos first: 1,240 of 5,000" in the user's culture.</summary>
    public static string MeasuringFirst(int measured, int total) =>
        string.Format(CultureInfo.CurrentCulture, MeasuringFirstFormat, measured.ToString("N0", CultureInfo.CurrentCulture), total.ToString("N0", CultureInfo.CurrentCulture));

    /// <summary>Frames are being written but the speed is not known yet.</summary>
    public static string EstimatingTimeLeft => _strings.Get();

    /// <summary>Every frame is in; the file is being completed.</summary>
    public static string Finishing => _strings.Get();

    /// <summary>Time left under a minute: {0} the seconds.</summary>
    public static string TimeLeftSecondsFormat => _strings.Get();

    /// <summary>Time left from a minute, on the minute: {0} the minutes.</summary>
    public static string TimeLeftMinutesFormat => _strings.Get();

    /// <summary>Time left from a minute, with a half minute: {0} the minutes.</summary>
    public static string TimeLeftMinutesHalfFormat => _strings.Get();

    /// <summary>The label of the frame count row: the frame (one picture of the video) being written.</summary>
    public static string FrameLabel => _strings.Get();

    /// <summary>The frame count: {0} the frames written, {1} the total, both zero-padded to the total's width ("0312 of 0742").</summary>
    public static string FramesFormat => _strings.Get();

    /// <summary>The label of the speed row.</summary>
    public static string SpeedLabel => _strings.Get();

    /// <summary>The export's speed: {0} photos per second, with one decimal under 10 ("4.3"); the form follows the number as shown.</summary>
    public static PluralText SpeedPhotos => _strings.Plural();

    /// <summary>The speed before it has been measured.</summary>
    public static string SpeedMeasuring => _strings.Get();

    /// <summary>The label of the output row.</summary>
    public static string OutputLabel => _strings.Get();

    /// <summary>The output summary: {0} the resolution ("4K"), {1} the codec ("H.264"), {2} the quality ("High").</summary>
    public static string OutputSummaryFormat => _strings.Get();

    /// <summary>The hint under the numbers.</summary>
    public static string MinimizeHint => _strings.Get();

    /// <summary>The button that stops the export.</summary>
    public static string CancelExport => _strings.Get();

    /// <summary>The label over the preview while the export runs: {0} the frame being written, zero-padded ("0313").</summary>
    public static string RenderingFrameFormat => _strings.Get();

    // ---- The finished state ----

    /// <summary>The finished state's title and Narrator's announcement: {0} the file name ("Harbor sunset.mp4").</summary>
    public static string ReadyFormat => _strings.Get();

    /// <summary>The summary under it: {0} the length ("24.7 s"), {1} the resolution ("4K"), {2} the codec, {3} the file size ("173 MB").</summary>
    public static string ReadySummaryFormat => _strings.Get();

    /// <summary>The label of the time the export took.</summary>
    public static string ExportedInLabel => _strings.Get();

    /// <summary>The label of the folder.</summary>
    public static string SavedToLabel => _strings.Get();

    /// <summary>The folder, shortened to its last two names: {0} the parent folder, {1} the folder ("Videos › 10Lapse").</summary>
    public static string SavedToFormat => _strings.Get();

    /// <summary>The accent button: plays the video in the preview from frame one.</summary>
    public static string Play => _strings.Get();

    /// <summary>Its name for a screen reader.</summary>
    public static string PlayName => _strings.Get();

    /// <summary>Opens File Explorer on the video.</summary>
    public static string ShowInFolder => _strings.Get();

    /// <summary>Back to the inspector.</summary>
    public static string BackToEditing => _strings.Get();

    // ---- InfoBars and questions ----

    /// <summary>The InfoBar once a cancelled export's unfinished file is gone.</summary>
    public static string Cancelled => _strings.Get();

    /// <summary>The InfoBar when a cancelled export's unfinished file could not be removed yet.</summary>
    public static string CancelledFileLeft => _strings.Get();

    /// <summary>An edit tried while the export runs (a removal, a move, an import, a new project).</summary>
    public static string EditsWait => _strings.Get();

    /// <summary>Export tried while photos are still being added.</summary>
    public static string ImportStillRunning => _strings.Get();

    /// <summary>
    /// The Save dialog was answered, but the project, its output or the screen changed while it was open. {0} the Export
    /// button's label (<see cref="ExportVideo"/>).
    /// </summary>
    public static string ChangedDuringDialogFormat => _strings.Get();

    /// <summary><see cref="ChangedDuringDialogFormat"/> with the button it names.</summary>
    public static string ChangedDuringDialog => string.Format(CultureInfo.CurrentCulture, ChangedDuringDialogFormat, ExportVideo);

    /// <summary>
    /// The finished video saved, but the preview can't play it (a codec Windows lacks, say). {0} the button's label
    /// (<see cref="ShowInFolder"/>).
    /// </summary>
    public static string PremiereCantPlayFormat => _strings.Get();

    /// <summary><see cref="PremiereCantPlayFormat"/> with the button it names.</summary>
    public static string PremiereCantPlay => string.Format(CultureInfo.CurrentCulture, PremiereCantPlayFormat, ShowInFolder);

    /// <summary>
    /// The finished GIF saved, but it is too large to play in the preview (<see cref="PremierePlan.GifPlayLimit"/>), so the
    /// preview shows its first frame. {0} the button's label (<see cref="ShowInFolder"/>).
    /// </summary>
    public static string PremiereGifFirstFrameFormat => _strings.Get();

    /// <summary><see cref="PremiereGifFirstFrameFormat"/> with the button it names.</summary>
    public static string PremiereGifFirstFrame => string.Format(CultureInfo.CurrentCulture, PremiereGifFirstFrameFormat, ShowInFolder);

    /// <summary>The question when the window closes during an export.</summary>
    public static string CloseTitle => _strings.Get();

    /// <summary>What closing does.</summary>
    public static string CloseMessage => _strings.Get();

    /// <summary>Closes the window.</summary>
    public static string CloseConfirm => _strings.Get();

    /// <summary>Keeps the window and the export.</summary>
    public static string CloseCancel => _strings.Get();

    /// <summary>The Save dialog's file type.</summary>
    public static string FileTypeName => _strings.Get();

    // ---- Narrator and the notification ----

    /// <summary>Narrator, as the export starts: {0} the file name.</summary>
    public static string StartedFormat => _strings.Get();

    /// <summary>Narrator, at most every ten percent: {0} the percent.</summary>
    public static string ProgressAnnouncementFormat => _strings.Get();

    /// <summary>Narrator, when the export fails (the InfoBar says why).</summary>
    public static string FailedAnnouncement => _strings.Get();

    /// <summary>Narrator, once every frame is written and the file is being completed.</summary>
    public static string FinishingAnnouncement => _strings.Get();

    /// <summary>The notification when an export in the background failed: {0} the file name.</summary>
    public static string FailedNotificationFormat => _strings.Get();

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

    /// <summary>"Exporting Harbor sunset.mp4", what <c>powercfg /requests</c> shows next to 10Lapse (<see cref="RunningNameFormat"/>).</summary>
    public static string KeepAwakeReason(string fileName) => string.Format(CultureInfo.CurrentCulture, RunningNameFormat, fileName);

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

    /// <summary>Log: checking for a running export as the window closed failed; the window closes anyway (#90).</summary>
    public static string CloseCheckFailed => "Checking for a running export as the window closed failed; the window closes, and a running export is stopped.";

    /// <summary>Log: stopping the export after that failed check failed too.</summary>
    public static string CloseStopFailed => "Stopping the export as the window closed failed.";

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
        var width = Math.Max(4, Digits(total));
        return string.Format(culture ?? CultureInfo.CurrentCulture, FramesFormat, Pad(done, width), Pad(total, width));
    }

    /// <summary>"Rendering frame 0313": the 1-based frame being written, zero-padded to the total's width.</summary>
    public static string RenderingFrame(int frameIndex, int total, CultureInfo? culture = null)
    {
        var width = Math.Max(4, Digits(total));
        return string.Format(culture ?? CultureInfo.CurrentCulture, RenderingFrameFormat, Pad(Math.Clamp(frameIndex + 1, 1, Math.Max(1, total)), width));
    }

    /// <summary>"4.3 photos per second", or <see cref="SpeedMeasuring"/> before it is known.</summary>
    public static string Speed(double? framesPerSecond, CultureInfo? culture = null)
    {
        if (framesPerSecond is not (> 0 and var speed))
        {
            return SpeedMeasuring;
        }

        culture ??= CultureInfo.CurrentCulture;
        var pattern = speed < 10 ? "0.0" : "0";

        // The plural form follows the number as shown, its decimal included ("1.0 photos per second" in English).
        // Invariant on purpose: only the digits are read back, to pick the form.
        var shown = decimal.Parse(speed.ToString(pattern, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return SpeedPhotos.Format(culture, shown, shown.ToString(pattern, culture));
    }

    /// <summary>"4K · H.264 · High".</summary>
    public static string OutputSummary(OutputSettings output, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        return string.Format(culture ?? CultureInfo.CurrentCulture, OutputSummaryFormat, OutputText.ResolutionName(output.Resolution), OutputText.CodecName(output.Format), OutputText.QualityOption(output.Quality));
    }

    /// <summary>
    /// The output row for an animated GIF (#51): "480 × 270 · GIF · 10 fps", with the rate the GIF plays at (the preset, or
    /// the video's own when slower) in place of a quality, which a GIF does not have.
    /// </summary>
    public static string GifOutputSummary(PixelSize size, Fraction rate, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return string.Format(culture, OutputSummaryFormat, OutputText.PixelSizeText(size), OutputText.CodecName(VideoFormat.Gif), TimingFormat.FrameRate(rate, culture));
    }

    /// <summary>The output row for what a snapshot exports: <see cref="OutputSummary"/>, or <see cref="GifOutputSummary"/> for a GIF.</summary>
    public static string OutputSummary(ProjectSnapshot snapshot, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var output = snapshot.Output;
        return output.Format == VideoFormat.Gif
            ? GifOutputSummary(snapshot.VideoSize, GifOutput.EffectiveRate(snapshot.EffectiveRate.Rate, output.Gif.Rate), culture)
            : OutputSummary(output, culture);
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
    /// The finished summary for any format: <see cref="ReadySummary(ExportedVideo, OutputResolution, CultureInfo?)"/> for a
    /// video, "10.0 s · 480 × 270 · GIF · 4.21 MB" for a GIF, whose size has no resolution name.
    /// </summary>
    public static string ReadySummary(ExportedVideo video, OutputSettings output, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(video);
        ArgumentNullException.ThrowIfNull(output);
        if (video.Format != VideoFormat.Gif)
        {
            return ReadySummary(video, output.Resolution, culture);
        }

        culture ??= CultureInfo.CurrentCulture;
        return string.Format(culture, ReadySummaryFormat, TimingFormat.Duration(video.Duration, culture), OutputText.PixelSizeText(video.Size), OutputText.CodecName(video.Format), FileSize(video.FileBytes, culture));
    }

    /// <summary>
    /// The finished file's real size to three significant figures, trailing zeros left out ("173 MB", "18.4 MB",
    /// "1.23 GB"), in units of 1,024 as Explorer counts them; "Less than 1 MB" below a megabyte. The Output panel's
    /// estimate keeps two figures (<see cref="OutputText.Size"/>): it is a guess, this is the file.
    /// </summary>
    public static string FileSize(long bytes, CultureInfo? culture = null) => OutputText.Size(bytes, 3, culture);

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
    /// <c>.mp4</c> (<c>.gif</c> for an animated GIF, #51). A name with nothing left becomes the default project name.
    /// </summary>
    public static string SuggestedFileName(string projectName, VideoFormat format = VideoFormat.Mp4H264)
    {
        var name = new string([.. (projectName ?? string.Empty).Where(c => c >= ' ' && !"<>:\"/\\|?*".Contains(c))]).Trim().TrimEnd('.').Trim();
        return (name.Length == 0 ? Project.DefaultName : name) + FileExtension(format);
    }

    /// <summary>The file extension a format is saved with: <c>.gif</c> for an animated GIF, <c>.mp4</c> for a video.</summary>
    public static string FileExtension(VideoFormat format) => format == VideoFormat.Gif ? ".gif" : ".mp4";

    /// <summary>The Save dialog's file type for a file of <paramref name="extension"/>: "Animated GIF" or "MP4 video".</summary>
    public static string FileTypeNameFor(string extension) =>
        string.Equals(extension, ".gif", StringComparison.OrdinalIgnoreCase) ? OutputText.GifFormatOption : FileTypeName;

    // Invariant on purpose: zero-padded counter digits keep their width (DESIGN.md, the Tabular Rule).
    private static string Pad(int value, int width) => value.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');

    // Invariant on purpose: only the number of digits is wanted.
    private static int Digits(int value) => value.ToString(CultureInfo.InvariantCulture).Length;
}
