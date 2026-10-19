using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using TenLapse.Core.Diagnostics;
using TenLapse.Core.Preview;
using TenLapse.Filmstrip;
using TenLapse.Presentation;
using TenLapse.Presentation.Services;

namespace TenLapse.Debugging;

/// <summary>
/// The import bench (#34): <c>--import-bench=FOLDER [--import-bench-out=PATH] [--import-bench-delay=S]</c> imports the photos of
/// FOLDER (its own files, no subfolders) through the same <see cref="ImportViewModel"/> the Choose folder button uses, once the
/// window and the filmstrip are up and S seconds (4 by default, so the background encoder probe has finished) have passed. It
/// reports, from the moment the import starts, the time to the first frames in the project, to the first thumbnail on screen,
/// to a filled first view (every thumbnail in view has its picture), and to the import's end (metadata read, sorted), how many
/// frames carry a capture time and a size at the end, where the thumbnails came from (the photo's embedded picture, the proxy
/// cache or the photo itself) and the process's private bytes. Then it writes the report to the log and to the path, prints a
/// BENCH line of key=value pairs for scripts/bench.ps1, and closes the window. It calls the view model, never synthetic input.
/// </summary>
internal static partial class DebugLaunch
{
    private static FilmstripView? _benchStrip;

    /// <summary>Starts the import bench when asked; call after <see cref="ApplyPreview"/> and the shell's construction.</summary>
    public static void ApplyImport(ImportViewModel import, ShellViewModel shell, ErrorPresenter errors)
    {
        if (Value("--import-bench=") is not { Length: > 0 } folder)
        {
            return;
        }

        var delay = Value("--import-bench-delay=") is { } text && double.TryParse(text, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0 ? seconds : 4;
        _ = errors.GuardAsync(
            () => ImportBench.RunAsync(import, shell, _benchStrip, _benchWindow, _benchLog, folder, TimeSpan.FromSeconds(delay), Value("--import-bench-out=")),
            "The import bench failed.");
    }
}

/// <summary>The measurements behind <c>--import-bench</c> (see <see cref="DebugLaunch"/>). Runs on the UI thread.</summary>
internal static class ImportBench
{
    public static async Task RunAsync(
        ImportViewModel import, ShellViewModel shell, FilmstripView? strip, Window? window, ILog? log, string folder, TimeSpan delay, string? reportPath)
    {
        ArgumentNullException.ThrowIfNull(strip);
        ArgumentNullException.ThrowIfNull(window);
        var report = new StringBuilder();
        try
        {
            await WaitAsync(() => strip.IsLoaded && strip.ViewModel is not null && strip.Thumbnails is not null && strip.ActualWidth > 0, TimeSpan.FromMinutes(2));
            await Task.Delay(delay);
            var thumbnails = strip.Thumbnails!;
            var project = shell.CurrentProject.Model;
            var baseline = PrivateBytes();
            Line(report, $"Import bench: {folder}; window {strip.XamlRoot.Content.ActualSize.X:0} DIPs wide at scale {strip.XamlRoot.RasterizationScale:0.##}; thumbnails {strip.ViewModel!.ThumbnailWidth}x{strip.ViewModel.ThumbnailHeight} DIPs; private bytes before {Mb(baseline):0} MB.");

            var loads = new ConcurrentQueue<(ThumbnailOrigin Origin, double Milliseconds, double At)>();
            var clock = new Stopwatch();
            void OnReady(object? sender, ThumbnailReadyEventArgs e) => loads.Enqueue((e.Origin, e.Elapsed.TotalMilliseconds, clock.Elapsed.TotalMilliseconds));
            thumbnails.Ready += OnReady;

            double? firstFrames = null;
            double? firstThumbnail = null;
            double? viewFilled = null;
            var peak = baseline;
            var lastSample = TimeSpan.Zero;
            void OnRendering(object? sender, object e)
            {
                var now = clock.Elapsed;
                if (firstFrames is null && project.Frames.Length > 0)
                {
                    firstFrames = now.TotalMilliseconds;
                }

                var realized = strip.RealizedThumbs().ToList();
                if (firstThumbnail is null && realized.Any(thumb => thumb.HasPicture && IsInView(strip, thumb)))
                {
                    firstThumbnail = now.TotalMilliseconds;
                }

                if (viewFilled is null && firstThumbnail is not null)
                {
                    var inView = realized.Where(thumb => IsInView(strip, thumb)).ToList();
                    if (inView.Count > 0 && inView.All(thumb => thumb.HasPicture))
                    {
                        viewFilled = now.TotalMilliseconds;
                    }
                }

                if (now - lastSample >= TimeSpan.FromMilliseconds(100))
                {
                    lastSample = now;
                    peak = Math.Max(peak, PrivateBytes());
                }
            }

            CompositionTarget.Rendering += OnRendering;
            double done;
            try
            {
                clock.Start();
                await import.ImportAsync([folder]);
                await import.WhenIdleAsync();
                done = clock.Elapsed.TotalMilliseconds;
                peak = Math.Max(peak, PrivateBytes());

                // The first view may still be filling when the import ends (a 24 MP proxy takes a moment): wait for it.
                await WaitAsync(() => viewFilled is not null, TimeSpan.FromSeconds(60), allowTimeout: true);
            }
            finally
            {
                CompositionTarget.Rendering -= OnRendering;
            }

            var frames = project.Frames;
            var withTime = frames.Count(frame => frame.CaptureTime is not null);
            var withSize = frames.Count(frame => frame.PixelSize is not null);
            var skipped = project.SkippedFiles.Length;
            var timeText = frames.Length > 0 && frames[0].CaptureTime is { } first && frames[^1].CaptureTime is { } last ? $"{first:s} to {last:s}" : "none";
            Line(report, $"First frames in the project {Ms(firstFrames)} after the import started; first thumbnail on screen {Ms(firstThumbnail)}; first view filled {Ms(viewFilled)}; import done {Ms(done)} ({frames.Length} frames, {skipped} skipped).");
            Line(report, $"Metadata at the end: capture time on {withTime} of {frames.Length} (span {timeText}), pixel size on {withSize}.");

            var early = loads.Where(load => load.At <= (viewFilled ?? done)).ToArray();
            Line(report, $"Thumbnails loaded before the first view was filled: {Origins(early)}; in all: {Origins([.. loads])}.");
            thumbnails.Ready -= OnReady;

            await Task.Delay(TimeSpan.FromSeconds(2));
            var settled = SettledPrivateBytes();
            Line(report, $"Private bytes: peak {Mb(peak):0} MB during the import, settled {Mb(settled):0} MB afterwards (managed heap {Mb(GC.GetTotalMemory(false)):0} MB); thumbnails in memory {thumbnails.Count}, {Mb(thumbnails.Bytes):0.0} MB; working set {Mb(Environment.WorkingSet):0} MB.");
            Line(report, $"BENCH import frames={frames.Length} firstFramesMs={Num(firstFrames)} firstThumbnailMs={Num(firstThumbnail)} viewFilledMs={Num(viewFilled)} importDoneMs={Num(done)} withCaptureTime={withTime} withSize={withSize} skipped={skipped} peakPrivateMB={Mb(peak):0} settledPrivateMB={Mb(settled):0}");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Line(report, $"FAILED: {exception.GetType().Name}: {exception.Message}");
            log?.Error("The import bench failed.", exception);
        }

        var text = report.ToString();
        log?.Info(text);
        if (!string.IsNullOrEmpty(reportPath))
        {
            await File.WriteAllTextAsync(reportPath, text);
        }

        window.Close();
    }

    private static bool IsInView(FilmstripView strip, FilmstripThumb thumb)
    {
        var scroller = strip.ScrollHost;
        var bounds = thumb.TransformToVisual(scroller).TransformBounds(new Windows.Foundation.Rect(0, 0, thumb.ActualWidth, thumb.ActualHeight));
        return bounds.Right > 0 && bounds.Left < scroller.ViewportWidth;
    }

    private static string Origins((ThumbnailOrigin Origin, double Milliseconds, double At)[] loads) =>
        loads.Length == 0
            ? "none"
            : string.Join(", ", loads.GroupBy(load => load.Origin).Select(group => string.Create(CultureInfo.InvariantCulture, $"{group.Count()} {group.Key} (mean {group.Average(load => load.Milliseconds):0.0} ms)")));

    private static string Ms(double? value) => value is { } ms ? string.Create(CultureInfo.InvariantCulture, $"{ms:0} ms") : "never";

    private static string Num(double? value) => value is { } ms ? ms.ToString("0", CultureInfo.InvariantCulture) : "-1";

    private static double Mb(long bytes) => bytes / 1048576.0;

    private static long PrivateBytes()
    {
        using var process = Process.GetCurrentProcess();
        return process.PrivateMemorySize64;
    }

    private static long SettledPrivateBytes()
    {
        for (var round = 0; round < 2; round++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }

        return PrivateBytes();
    }

    private static async Task WaitAsync(Func<bool> condition, TimeSpan limit, bool allowTimeout = false)
    {
        var waited = Stopwatch.StartNew();
        while (!condition())
        {
            if (waited.Elapsed > limit)
            {
                if (allowTimeout)
                {
                    return;
                }

                throw new TimeoutException("The import bench waited too long.");
            }

            await Task.Delay(10);
        }
    }

    private static void Line(StringBuilder report, FormattableString line) =>
        report.AppendLine(line.ToString(CultureInfo.InvariantCulture));
}
