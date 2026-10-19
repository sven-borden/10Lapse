using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using TenLapse.Core.Diagnostics;
using TenLapse.Core.Preview;
using TenLapse.Filmstrip;
using TenLapse.Presentation.Services;
using TenLapse.Preview;

namespace TenLapse.Debugging;

/// <summary>
/// The filmstrip's Debug switch (#20): <c>--filmstrip-bench [--filmstrip-bench-out=PATH] [--filmstrip-bench-seconds=S]</c>
/// with <c>--sample-frames=N</c> waits until the N photos are in, then scrolls the strip programmatically from the first
/// thumbnail to the last at an even speed (20 s by default) and back in jumps of one view, and reports the private bytes
/// along the way, the thumbnail loads (count and time by origin, cancelled), the UI thread's frame times
/// (CompositionTarget.Rendering intervals) and what the cache holds; then closes the window. It drives the scroller, never
/// synthetic input.
/// </summary>
internal static partial class DebugLaunch
{
    private static Window? _benchWindow;
    private static ILog? _benchLog;

    /// <summary>
    /// Starts the filmstrip bench when asked; called by the shell once its strip is attached. <c>--filmstrip-remove=10,11,12</c>
    /// removes those frame numbers once the sample photos are in, as Delete would (selection, removal, Narrator's
    /// notification and the gap marker it leaves), and <c>--filmstrip-undo-after=S</c> undoes it S seconds later.
    /// </summary>
    public static void ApplyFilmstrip(FilmstripView strip, PreviewSession preview)
    {
        var expected = Value("--sample-frames=") is { } text && int.TryParse(text, out var count) ? Math.Min(count, MaxSampleFrames) : 0;
        if (Value("--filmstrip-remove=") is { } numbers)
        {
            var remove = numbers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(number => int.TryParse(number, out var n) ? n : 0)
                .Where(n => n > 0)
                .ToList();
            var undoAfter = Value("--filmstrip-undo-after=") is { } after && double.TryParse(after, CultureInfo.InvariantCulture, out var wait) ? wait : (double?)null;
            _ = preview.Errors.GuardAsync(() => RemoveSampleFramesAsync(strip, expected, remove, undoAfter), "Removing sample frames failed.");
        }

        if (!_args.Contains("--filmstrip-bench", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var seconds = Value("--filmstrip-bench-seconds=") is { } s && double.TryParse(s, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : 20;
        _ = preview.Errors.GuardAsync(
            () => FilmstripBench.RunAsync(strip, preview, expected, TimeSpan.FromSeconds(seconds), Value("--filmstrip-bench-out="), () => _benchWindow, () => _benchLog),
            "The filmstrip bench failed.");
    }

    private static async Task RemoveSampleFramesAsync(FilmstripView strip, int expected, List<int> numbers, double? undoAfter)
    {
        while (strip.ViewModel is not { } viewModel || viewModel.Items.Count == 0 || viewModel.Items.Count < expected)
        {
            await Task.Delay(200);
        }

        var model = strip.ViewModel!;
        var indices = numbers.Select(number => number - 1).Where(index => index < model.Items.Count).ToList();
        if (indices.Count == 0)
        {
            return;
        }

        model.Click(indices[0], shift: false, control: false);
        foreach (var index in indices.Skip(1))
        {
            model.AddToSelection(index);
        }

        model.Remove();
        if (undoAfter is { } seconds)
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            model.Undo();
        }
    }

    /// <summary>The window the filmstrip bench closes when done, and the log it writes to (ApplyPreview sets them).</summary>
    private static void RememberBench(Window window, ILog log)
    {
        _benchWindow = window;
        _benchLog = log;
    }
}

/// <summary>The measurements behind <c>--filmstrip-bench</c> (see <see cref="DebugLaunch"/>). Runs on the UI thread.</summary>
internal static class FilmstripBench
{
    public static async Task RunAsync(
        FilmstripView strip, PreviewSession preview, int expectedFrames, TimeSpan sweep, string? reportPath, Func<Window?> window, Func<ILog?> log)
    {
        var report = new StringBuilder();
        var started = Stopwatch.StartNew();
        await WaitAsync(() => window() is not null && strip.IsLoaded && strip.ViewModel is { } vm && vm.Items.Count > 0 && vm.Items.Count >= expectedFrames, TimeSpan.FromMinutes(20));
        var viewModel = strip.ViewModel!;
        var thumbnails = strip.Thumbnails!;
        var scroller = strip.ScrollHost;
        var count = viewModel.Items.Count;
        Line(report, $"Filmstrip bench: {count} frames ready after {started.Elapsed.TotalSeconds:0.0} s; thumbnails {viewModel.ThumbnailWidth}x{viewModel.ThumbnailHeight} DIPs at scale {strip.XamlRoot.RasterizationScale:0.##}, loaded at {ThumbnailSize.For(viewModel.ThumbnailHeight, strip.XamlRoot.RasterizationScale)} px.");

        var loads = new ConcurrentQueue<(ThumbnailOrigin Origin, double Milliseconds)>();
        void OnReady(object? sender, ThumbnailReadyEventArgs e) => loads.Enqueue((e.Origin, e.Elapsed.TotalMilliseconds));
        thumbnails.Ready += OnReady;
        try
        {
            // Let the first view fill and the import settle.
            scroller.ChangeView(0, null, null, disableAnimation: true);
            await Task.Delay(TimeSpan.FromSeconds(3));
            var cancelledBefore = thumbnails.Cancelled;
            var baseline = PrivateBytes();
            Line(report, $"Before scrolling: private bytes {Mb(baseline)} MB; thumbnails in memory {thumbnails.Count}, {Mb(thumbnails.Bytes):0.0} MB; first view loaded {loads.Count}.");
            loads.Clear();

            // 1. An even sweep from the first thumbnail to the last.
            var frames = new List<double>();
            var memory = new List<(double At, long Bytes)>();
            var peak = baseline;
            var clock = Stopwatch.StartNew();
            var last = clock.Elapsed.TotalMilliseconds;
            var finished = new TaskCompletionSource();
            void OnRendering(object? sender, object e)
            {
                var now = clock.Elapsed.TotalMilliseconds;
                frames.Add(now - last);
                last = now;
                var fraction = Math.Min(1, clock.Elapsed / sweep);
                scroller.ChangeView(fraction * scroller.ScrollableWidth, null, null, disableAnimation: true);
                if (memory.Count == 0 || fraction - memory[^1].At >= 0.25 || (fraction >= 1 && memory[^1].At < 1))
                {
                    var bytes = PrivateBytes();
                    memory.Add((fraction, bytes));
                }

                if (fraction >= 1)
                {
                    finished.TrySetResult();
                }
            }

            var sampler = Task.Run(async () =>
            {
                while (!finished.Task.IsCompleted)
                {
                    peak = Math.Max(peak, PrivateBytes());
                    await Task.Delay(100);
                }
            });
            CompositionTarget.Rendering += OnRendering;
            try
            {
                await finished.Task;
            }
            finally
            {
                CompositionTarget.Rendering -= OnRendering;
            }

            await sampler;
            var sweepLoads = loads.ToArray();
            loads.Clear();
            Line(report, $"Sweep over all {count} frames in {sweep.TotalSeconds:0} s ({scroller.ScrollableWidth / sweep.TotalSeconds:0} DIPs/s): UI frames {frames.Count}, interval {Distribution.Of(frames)} ms, over 33 ms {frames.Count(ms => ms > 33.4)}, over 50 ms {frames.Count(ms => ms > 50)}.");
            Line(report, $"Sweep memory: private bytes {string.Join(", ", memory.Select(m => string.Create(CultureInfo.InvariantCulture, $"{m.At:P0} {Mb(m.Bytes)} MB")))}; peak {Mb(peak)} MB (baseline {Mb(baseline)} MB).");
            Line(report, $"Sweep loads: {Loads(sweepLoads)}; cancelled {thumbnails.Cancelled - cancelledBefore}.");

            // 2. Settle at the end: how long the last view takes to fill.
            var settle = Stopwatch.StartNew();
            await WaitAsync(() => strip.RealizedThumbs().Where(thumb => IsInView(strip, thumb)).All(thumb => thumb.HasPicture), TimeSpan.FromSeconds(30));
            Line(report, $"Last view filled {settle.Elapsed.TotalMilliseconds:0} ms after the sweep stopped.");

            // 3. Jumps back by one view at a time, 300 ms apart: how long each view takes to fill.
            var fills = new List<double>();
            loads.Clear();
            var cancelledJumps = thumbnails.Cancelled;
            for (var offset = scroller.ScrollableWidth; offset > 0; offset -= scroller.ViewportWidth * 4)
            {
                scroller.ChangeView(offset, null, null, disableAnimation: true);
                var fill = Stopwatch.StartNew();
                await Task.Delay(16);
                await WaitAsync(() => strip.RealizedThumbs().Where(thumb => IsInView(strip, thumb)).All(thumb => thumb.HasPicture), TimeSpan.FromSeconds(10));
                fills.Add(fill.Elapsed.TotalMilliseconds);
                await Task.Delay(300);
            }

            Line(report, $"Jumps of four views ({fills.Count}): view filled after {Distribution.Of(fills)} ms; loads {Loads([.. loads])}; cancelled {thumbnails.Cancelled - cancelledJumps}.");

            await Task.Delay(TimeSpan.FromSeconds(2));
            var after = PrivateBytes();
            Line(report, $"After: private bytes {Mb(after)} MB ({Mb(after - baseline):+0;-0} MB against the baseline); thumbnails in memory {thumbnails.Count}, {Mb(thumbnails.Bytes):0.0} MB; elements realized {strip.RealizedThumbs().Count()}; working set {Mb(Environment.WorkingSet)} MB; proxies built {preview.Images.ProxyProgress.Done} of {preview.Images.ProxyProgress.Total}.");
        }
        finally
        {
            thumbnails.Ready -= OnReady;
        }

        var text = report.ToString();
        log()?.Info(text);

        if (!string.IsNullOrEmpty(reportPath))
        {
            await File.WriteAllTextAsync(reportPath, text);
        }

        window()?.Close();
    }

    private static bool IsInView(FilmstripView strip, FilmstripThumb thumb)
    {
        var scroller = strip.ScrollHost;
        var bounds = thumb.TransformToVisual(scroller).TransformBounds(new Windows.Foundation.Rect(0, 0, thumb.ActualWidth, thumb.ActualHeight));
        return bounds.Right > 0 && bounds.Left < scroller.ViewportWidth;
    }

    private static string Loads((ThumbnailOrigin Origin, double Milliseconds)[] loads) =>
        loads.Length == 0
            ? "none"
            : string.Join("; ", loads.GroupBy(load => load.Origin).Select(group => string.Create(CultureInfo.InvariantCulture, $"{group.Count()} from {group.Key}, {Distribution.Of([.. group.Select(load => load.Milliseconds)])} ms")));

    private static double Mb(long bytes) => bytes / 1048576.0;

    private static long PrivateBytes()
    {
        using var process = Process.GetCurrentProcess();
        return process.PrivateMemorySize64;
    }

    private static async Task WaitAsync(Func<bool> condition, TimeSpan limit)
    {
        var waited = Stopwatch.StartNew();
        while (!condition())
        {
            if (waited.Elapsed > limit)
            {
                throw new TimeoutException("The filmstrip bench waited too long.");
            }

            await Task.Delay(50);
        }
    }

    private static void Line(StringBuilder report, FormattableString line) =>
        report.AppendLine(line.ToString(CultureInfo.InvariantCulture));
}
