using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using TenLapse.Core.Diagnostics;
using TenLapse.Core.Preview;
using TenLapse.Core.Projects;
using TenLapse.Preview;
using TenLapse.Presentation.Services;
using TenLapse.Services;
using Windows.UI;

namespace TenLapse.Debugging;

/// <summary>
/// The preview's Debug switches (#19):
/// <list type="bullet">
/// <item><c>--sample-frames=N</c> (parsed in DebugLaunch.cs) generates N photos of a sunset with their frame number
/// burnt in, <c>--sample-size=WxH</c> (default 2400x1600), and adds them in batches of 100 as they are written. They go
/// into a folder named after the count and the size (<c>Harbor sunset 742 grain 2400x1600</c>) under
/// <c>--sample-folder=PATH</c> (default %TEMP%\10Lapse debug samples). Files already there are reused, so the folder
/// grows with every count and size tried (up to about 4 GB for 10,000 photos) until <c>--sample-clean</c> deletes it
/// when the window closes. An agent passes <c>--sample-folder</c> under its own branch folder
/// (%LOCALAPPDATA%\Temp\claude\&lt;branch&gt;\), never the shared default.</item>
/// <item><c>--cache-folder=PATH</c> puts the proxy cache there instead of the app's own. Point it at an empty folder of
/// its own: the cache only ever deletes its own proxies, but they are the only files it expects there.</item>
/// <item><c>--preview-bench</c> measures the player on those photos: playback before the proxies exist, the time the
/// background pass takes, playback at the project's rate with the proxies, and two scrubs (a fast sweep over the whole
/// project and a slow drag), then writes the report to the log and to <c>--preview-bench-out=PATH</c> and closes the
/// window. It drives the player's view model, never synthetic input.</item>
/// </list>
/// </summary>
internal static partial class DebugLaunch
{
    private const int SampleBatch = 100;

    private static readonly CancellationTokenSource _closing = new();
    private static IErrorSink? _previewLog;
    private static string? _sampleFolder;

    /// <summary>The proxy cache folder from <c>--cache-folder=PATH</c>, or null.</summary>
    public static string? CacheFolder => Value("--cache-folder=");

    /// <summary>Applies the preview's switches; call before <see cref="Apply"/> (on the UI thread).</summary>
    public static void ApplyPreview(PreviewSession preview, Window window, ILog log)
    {
        _previewLog = new LogErrorSink(log);

        // Closing stops the sample generator; with --sample-clean it also deletes the generated photos.
        window.Closed += (_, _) =>
        {
            _closing.Cancel();
            if (_args.Contains("--sample-clean", StringComparer.OrdinalIgnoreCase) && _sampleFolder is { } folder)
            {
                CleanSamples(folder, log);
            }
        };
        if (_args.Contains("--preview-bench", StringComparer.OrdinalIgnoreCase))
        {
            var expected = Value("--sample-frames=") is { } text && int.TryParse(text, out var count) ? Math.Min(count, MaxSampleFrames) : 0;
            _ = preview.Errors.GuardAsync(
                () => PreviewBench.RunAsync(preview, window, log, expected, Value("--preview-bench-out=")),
                "The preview bench failed.");
        }
    }

    private static void AddSampleFrames(Project project, int count, IInfoBarService infoBars)
    {
        project.Name = "Harbor sunset";
        var log = _previewLog ?? throw new InvalidOperationException("ApplyPreview runs before Apply.");
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        var (width, height) = SampleSize();
        var root = Value("--sample-folder=") ?? Path.Combine(Path.GetTempPath(), "10Lapse debug samples");

        // Each photo is drawn from its place in the sequence (index / count), so a folder holds one count only.
        var folder = Path.Combine(root, string.Create(CultureInfo.InvariantCulture, $"Harbor sunset {count} grain {width}x{height}"));
        _sampleFolder = folder;
        var stop = _closing.Token;

        // The brand's day-to-night ramp is for the sample sequence (DESIGN.md, Brand ramp); read on the UI thread.
        var ramp = new[] { "DaylightColor", "GoldenColor", "AmberColor", "RoseColor", "DuskColor", "BlueHourColor", "NightColor" }
            .Select(key => (Color)Application.Current.Resources[key])
            .ToArray();
        var land = (Color)Application.Current.Resources["LetterboxColor"];

        _ = Task.Run(async () =>
        {
            try
            {
                Directory.CreateDirectory(folder);
                var start = new DateTime(2026, 6, 21, 19, 0, 0, DateTimeKind.Unspecified);
                for (var first = 0; first < count; first += SampleBatch)
                {
                    var batch = new Frame[Math.Min(SampleBatch, count - first)];
                    await Parallel.ForEachAsync(
                        Enumerable.Range(first, batch.Length),
                        new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = stop },
                        async (index, _) =>
                        {
                            var path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"IMG_{index + 1:0000}.JPG"));
                            if (!File.Exists(path) || new FileInfo(path).Length == 0)
                            {
                                await DrawSampleAsync(path, index, count, width, height, ramp, land);
                            }

                            var info = new FileInfo(path);
                            batch[index - first] = new Frame(path, info.Length, info.LastWriteTimeUtc)
                            {
                                PixelSize = new PixelSize(width, height),
                                CaptureTime = start.AddSeconds(5 * index),
                            };
                        });
                    dispatcher.TryEnqueueGuarded(
                        log,
                        () =>
                        {
                            if (!project.IsClosed)
                            {
                                project.AddFrames(batch);
                            }
                        },
                        "Adding the sample photos failed.");
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                // The window closed.
            }
            catch (Exception exception)
            {
                log.Report(exception, "Generating the sample photos failed.");
                infoBars.Show(InfoBarSeverity.Critical, "The sample photos could not be generated. The log says why.");
            }
        });
    }

    // Best effort: a photo a load still holds open stays, and the log says so.
    private static void CleanSamples(string folder, ILog log)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Warning($"--sample-clean could not delete every sample photo in {folder}.", exception);
        }
    }

    private static (int Width, int Height) SampleSize()
    {
        var parts = (Value("--sample-size=") ?? string.Empty).Split('x', 'X');
        return parts.Length == 2 && int.TryParse(parts[0], out var width) && int.TryParse(parts[1], out var height) && width >= 16 && height >= 16
            ? (width, height)
            : (2400, 1600);
    }

    // A sunset in the brand's ramp: the sky darkens through the sequence, the sun sinks, the frame number sits top left
    // and a bar along the bottom grows, so a wrong or late frame shows at a glance.
    private static async Task DrawSampleAsync(string path, int index, int count, int width, int height, Color[] ramp, Color land)
    {
        var t = count <= 1 ? 0f : (float)index / (count - 1);
        var device = CanvasDevice.GetSharedDevice();
        using var target = new CanvasRenderTarget(device, width, height, 96);
        using (var session = target.CreateDrawingSession())
        {
            var horizon = height * 0.72f;
            using (var sky = new CanvasLinearGradientBrush(session, Along(ramp, t), Along(ramp, Math.Min(1, t + 0.25f)))
            {
                StartPoint = new Vector2(0, 0),
                EndPoint = new Vector2(0, horizon),
            })
            {
                session.FillRectangle(0, 0, width, horizon, sky);
            }

            var sunY = height * (0.25f + (0.55f * t));
            session.FillCircle(width * 0.63f, sunY, height * 0.06f, Along(ramp, 0.2f + (0.3f * t)));
            session.FillRectangle(0, horizon, width, height - horizon, land);

            // Fine grain over everything, different in every frame, so the files weigh and decode like photos rather
            // than like flat gradients.
            using (var grain = new TurbulenceEffect
            {
                Size = new Vector2(width, height),
                Frequency = new Vector2(0.08f),
                Octaves = 4,
                Seed = index,
            })
            using (session.CreateLayer(0.18f))
            {
                session.DrawImage(grain);
            }
            session.FillRectangle(0, height - (height * 0.02f), width * t, height * 0.02f, ramp[0]);

            using var format = new CanvasTextFormat { FontFamily = "Segoe UI Variable Display", FontSize = height * 0.12f };
            session.DrawText(string.Create(CultureInfo.InvariantCulture, $"{index + 1:0000}"), height * 0.04f, height * 0.03f, ramp[0], format);
        }

        // A name of its own: two runs on the same folder never write the same file, and never move a half-written one
        // into place.
        var temporary = string.Create(CultureInfo.InvariantCulture, $"{path}.{Guid.NewGuid():N}.tmp");
        await target.SaveAsync(temporary, CanvasBitmapFileFormat.Jpeg, 0.9f);
        File.Move(temporary, path, overwrite: true);
    }

    private static Color Along(Color[] ramp, float t)
    {
        var position = Math.Clamp(t, 0, 1) * (ramp.Length - 1);
        var low = (int)Math.Floor(position);
        var high = Math.Min(ramp.Length - 1, low + 1);
        var f = position - low;
        byte Mix(byte a, byte b) => (byte)Math.Round(a + ((b - a) * f));
        return Color.FromArgb(255, Mix(ramp[low].R, ramp[high].R), Mix(ramp[low].G, ramp[high].G), Mix(ramp[low].B, ramp[high].B));
    }
}

/// <summary>The measurements behind <c>--preview-bench</c> (see <see cref="DebugLaunch"/>). Runs on the UI thread.</summary>
internal static class PreviewBench
{
    // What the app holds besides the frame images (the runtime, WinUI, Win2D and the driver, the decoders' buffers):
    // about 250 MB idle with a warm cache. Private bytes after a proxy pass stay under the image budget plus this.
    private const long MemoryMargin = 320L << 20;

    public static async Task RunAsync(PreviewSession preview, Window window, ILog log, int expectedFrames, string? reportPath)
    {
        var player = preview.Player;
        var report = new StringBuilder();
        var started = Stopwatch.StartNew();
        await WaitAsync(() => preview.Loop is not null && player.FrameCount > 0 && player.FrameCount >= expectedFrames, TimeSpan.FromMinutes(10));
        var (done, total) = preview.Images.ProxyProgress;
        Line(report, $"Preview bench: {player.FrameCount} frames, video {player.State.Snapshot.VideoSize.Width}x{player.State.Snapshot.VideoSize.Height} at {player.State.Snapshot.EffectiveRate.Rate.ToDouble():0.###} fps, frames ready after {started.Elapsed.TotalSeconds:0.0} s, proxies already built {done} of {total}.");

        // 1. Playback before the proxies exist: small decodes and held frames.
        var cold = await PlayAsync(preview, TimeSpan.FromSeconds(5));
        Line(report, $"Cold playback (5 s, proxies being built): {cold}");

        // 2. The background pass, and the process's private bytes during it and once it is done (paused: nothing
        // allocates then, so garbage the pass left would stay resident).
        player.Seek(0);
        var pass = Stopwatch.StartNew();
        var peak = PrivateBytes();
        await WaitAsync(() => preview.Images.ProxyProgress is var progress && progress.Done == progress.Total, TimeSpan.FromMinutes(60), () => peak = Math.Max(peak, PrivateBytes()));
        var (count, bytes) = preview.Proxies.Size;
        Line(report, $"Proxy pass finished {pass.Elapsed.TotalSeconds:0.0} s after cold playback ({started.Elapsed.TotalSeconds:0.0} s after the frames were ready); cache holds {count} proxies, {bytes / 1048576.0:0.0} MB, bound {preview.Proxies.Options.MaxBytes / 1048576.0:0} MB.");
        await Task.Delay(TimeSpan.FromSeconds(2));
        var after = PrivateBytes();
        var budget = new FrameImageCacheOptions().MemoryBudgetBytes;
        var limit = budget + MemoryMargin;
        Line(report, $"Memory: private bytes peak during the pass {peak / 1048576.0:0} MB, 2 s after it (paused) {after / 1048576.0:0} MB; image budget {budget / 1048576.0:0} MB + margin {MemoryMargin / 1048576.0:0} MB = {limit / 1048576.0:0} MB: {(after <= limit ? "within" : "OVER")}.");

        // 3. Playback with every proxy built.
        player.Seek(0);
        await Task.Delay(500);
        var warm = await PlayAsync(preview, TimeSpan.FromSeconds(10));
        Line(report, $"Warm playback (10 s): {warm}");

        // 4. Scrubbing: a fast sweep over the whole project in 4 s, then a slow drag of one frame per display frame for 3 s.
        var sweep = await ScrubAsync(preview, TimeSpan.FromSeconds(4), slow: false);
        Line(report, $"Fast sweep (all frames in 4 s): {sweep}");
        var drag = await ScrubAsync(preview, TimeSpan.FromSeconds(3), slow: true);
        Line(report, $"Slow drag (1 frame per display frame, 3 s): {drag}");
        Line(report, $"Images in memory: {preview.Images.Count}, {preview.Images.Bytes / 1048576.0:0.0} MB; working set {Environment.WorkingSet / 1048576.0:0} MB.");

        var text = report.ToString();
        log.Info(text);
        if (!string.IsNullOrEmpty(reportPath))
        {
            await File.WriteAllTextAsync(reportPath, text);
        }

        window.Close();
    }

    private static async Task<PlaybackSummary> PlayAsync(PreviewSession preview, TimeSpan duration)
    {
        var loop = preview.Loop ?? throw new InvalidOperationException("The preview is not on screen.");
        var gate = new Lock();
        PlaybackStats? stats = null;
        void OnPresented(long timestamp, PlaybackReading reading, PresentedImage image)
        {
            lock (gate)
            {
                stats?.Record(timestamp, reading, image);
            }
        }

        loop.Presented += OnPresented;
        try
        {
            preview.Player.Play();
            lock (gate)
            {
                stats = new PlaybackStats(preview.Player.Clock ?? throw new InvalidOperationException("Playback did not start."));
            }

            await Task.Delay(duration);
            preview.Player.Pause();
        }
        finally
        {
            loop.Presented -= OnPresented;
        }

        lock (gate)
        {
            return stats!.Summarize();
        }
    }

    private static async Task<string> ScrubAsync(PreviewSession preview, TimeSpan duration, bool slow)
    {
        var loop = preview.Loop ?? throw new InvalidOperationException("The preview is not on screen.");
        var player = preview.Player;
        var count = player.FrameCount;
        var shown = new ConcurrentDictionary<int, long>();
        var shownTimes = new ConcurrentQueue<long>();
        void OnShown(int index, FrameImageQuality quality, long timestamp)
        {
            if (quality == FrameImageQuality.Proxy)
            {
                shown.TryAdd(index, timestamp);
                shownTimes.Enqueue(timestamp);
            }
        }

        var targets = new List<(int Frame, long At)>();
        var start = slow ? count / 3 : 0;
        player.Seek(start);
        await Task.Delay(300);
        shown.Clear();
        shownTimes.Clear();
        loop.FrameShown += OnShown;
        var clock = Stopwatch.StartNew();
        var finished = new TaskCompletionSource();
        var step = 0;
        void OnRendering(object? sender, object e)
        {
            if (clock.Elapsed >= duration)
            {
                finished.TrySetResult();
                return;
            }

            var frame = slow
                ? Math.Min(count - 1, start + step++)
                : PresentationFrame(clock.Elapsed.TotalSeconds / duration.TotalSeconds, count);
            if (targets.Count == 0 || targets[^1].Frame != frame)
            {
                player.Seek(frame);
                targets.Add((frame, Stopwatch.GetTimestamp()));
            }
        }

        CompositionTarget.Rendering += OnRendering;
        try
        {
            await finished.Task;
        }
        finally
        {
            CompositionTarget.Rendering -= OnRendering;
        }

        await Task.Delay(500);
        loop.FrameShown -= OnShown;

        // A target counts as shown when its proxy reached the screen before the next target was asked for.
        var latencies = new List<double>();
        for (var i = 0; i < targets.Count; i++)
        {
            var (frame, at) = targets[i];
            var next = i + 1 < targets.Count ? targets[i + 1].At : long.MaxValue;
            if (shown.TryGetValue(frame, out var when) && when >= at && when < next)
            {
                latencies.Add((when - at) * 1000.0 / Stopwatch.Frequency);
            }
        }

        var times = shownTimes.ToArray();
        var gaps = times.Zip(times.Skip(1), (a, b) => (b - a) * 1000.0 / Stopwatch.Frequency).ToList();
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{targets.Count} seeks, {latencies.Count} shown while current ({100.0 * latencies.Count / Math.Max(1, targets.Count):0.0} %), seek to picture {Distribution.Of(latencies)} ms; gaps between new pictures {Distribution.Of(gaps)} ms");
    }

    private static int PresentationFrame(double fraction, int count) =>
        Math.Clamp((int)Math.Round(fraction * (count - 1)), 0, count - 1);

    private static long PrivateBytes()
    {
        using var process = Process.GetCurrentProcess();
        return process.PrivateMemorySize64;
    }

    private static async Task WaitAsync(Func<bool> condition, TimeSpan limit, Action? sample = null)
    {
        var waited = Stopwatch.StartNew();
        while (!condition())
        {
            sample?.Invoke();
            if (waited.Elapsed > limit)
            {
                throw new TimeoutException("The preview bench waited too long.");
            }

            await Task.Delay(100);
        }
    }

    private static void Line(StringBuilder report, FormattableString line) =>
        report.AppendLine(line.ToString(CultureInfo.InvariantCulture));
}
