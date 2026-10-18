using System.Diagnostics;
using Microsoft.Graphics.Canvas;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Export;
using Windows.Graphics.DirectX;
using Windows.UI;

namespace TenLapse.Media.Encoding;

/// <summary>
/// Finds what this PC can encode by test-encoding the way the export does: one frame written and finalized
/// through <c>IMFSinkWriter</c> with hardware transforms on, on a Direct3D 11 device of the probe's own, to a
/// temporary file that is deleted. Run at startup, off the UI thread; the result is cached per machine.
/// </summary>
/// <remarks>
/// <para>
/// The matrix is H.264 and HEVC × 120, 60 and 30 fps × 16:9, 9:16, 4:3, 1:1 and 4:5 × 4K, 1440p, 1080p and
/// 720p (sizes from <see cref="OutputSize.For"/>). It is searched largest first, and a size is only encoded when
/// the probes so far do not settle it under the rule <see cref="EncoderCapabilities"/> assumes (an encoder that
/// takes a size and rate takes everything no larger on every axis): on a GPU that encodes 4K120 in both shapes,
/// four encodes answer the whole matrix. A rate between two probed rates is answered by the next one up (90 fps
/// by the 120 fps probes), never by a slower one.
/// </para>
/// <para>
/// A failed test encode is a "not supported" answer, classified as an export failure would be, never an
/// exception. An encoder that is not listed (HEVC without the HEVC Video Extensions or a GPU encoder) is not
/// probed at all, so HEVC is offered only when an HEVC encoder is present and a test encode worked.
/// </para>
/// <para>
/// Each test encode gets <see cref="TestEncodeTimeout"/>. One that takes longer (a hung driver) is recorded as
/// <see cref="VideoExportFailure.EncoderStalled"/> and ends the probe: the result is provisional, so it is not
/// cached, and the device the hung encode still holds is left to it rather than disposed under it.
/// </para>
/// </remarks>
public static class EncoderProbe
{
    /// <summary>
    /// The rates probed, fastest first: up to <see cref="FrameRates.Max"/>, so every rate the app accepts has an
    /// answer. A request above the fastest is answered as unsupported.
    /// </summary>
    public static IReadOnlyList<int> FramesPerSecond { get; } = [120, 60, 30];

    /// <summary>The shapes probed: the widest and the tallest first, since they settle the others.</summary>
    public static IReadOnlyList<FrameAspect> Shapes { get; } =
        [FrameAspect.Ratio16x9, FrameAspect.Ratio9x16, FrameAspect.Ratio4x3, FrameAspect.Ratio1x1, FrameAspect.Ratio4x5];

    /// <summary>
    /// How long one test encode may take before it counts as stalled. A 4K frame takes about 0.3 s here on the GPU
    /// and about 0.3 s in software; a slow laptop's software HEVC encoder a few times that.
    /// </summary>
    public static TimeSpan TestEncodeTimeout { get; } = TimeSpan.FromSeconds(8);

    private static readonly VideoFormat[] _formats = [VideoFormat.Mp4H264, VideoFormat.Mp4Hevc];

    /// <summary>
    /// The cached result when <paramref name="cachePath"/> holds one for this PC as it is now; otherwise probes,
    /// writes the cache (unless the result is provisional) and returns the new result. Runs on the thread pool,
    /// so it may be awaited from the UI thread. A cache that cannot be written is left as it is.
    /// </summary>
    /// <remarks>
    /// A cached load takes about 0.2 s and a probe about a second, but a probe is bounded only by
    /// <see cref="TestEncodeTimeout"/> per test encode: never block the UI on it without a limit. Show the
    /// output panel with what is known (H.264 at the usual sizes) and refine it when this completes, or await it
    /// with <see cref="Task.WaitAsync(TimeSpan)"/>. Cancelling stops between test encodes, not inside one.
    /// </remarks>
    /// <param name="cachePath">The JSON file: in the app's local data folder (LocalState or LocalApplicationData).</param>
    /// <param name="cancellationToken">Stops between test encodes.</param>
    public static Task<EncoderCapabilities> LoadOrProbeAsync(string cachePath, CancellationToken cancellationToken = default) =>
        LoadOrProbeAsync(cachePath, Path.GetTempPath(), cancellationToken);

    /// <summary><see cref="LoadOrProbeAsync(string, CancellationToken)"/> with the test encodes written to <paramref name="temporaryFolder"/>.</summary>
    internal static Task<EncoderCapabilities> LoadOrProbeAsync(string cachePath, string temporaryFolder, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(cachePath);
        ArgumentException.ThrowIfNullOrEmpty(temporaryFolder);
        return Task.Run(
            () =>
            {
                if (ListEncoders() is not { } encoders)
                {
                    return NoMediaFoundation();
                }

                var key = EncoderCacheKey.ForThisMachine(encoders);
                if (EncoderCapabilitiesCache.TryRead(cachePath, key) is { } cached)
                {
                    return cached;
                }

                var capabilities = Probe(encoders, hardwareAcceleration: true, temporaryFolder, TestEncodeTimeout, cancellationToken);
                if (!capabilities.IsProvisional)
                {
                    try
                    {
                        EncoderCapabilitiesCache.Write(cachePath, key, capabilities);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        // Not cached: the next start probes again, which costs time but answers the same.
                    }
                }

                return capabilities;
            },
            cancellationToken);
    }

    /// <summary>Probes without the cache. Runs on the thread pool.</summary>
    public static Task<EncoderCapabilities> ProbeAsync(CancellationToken cancellationToken = default) =>
        ProbeAsync(hardwareAcceleration: true, cancellationToken);

    /// <summary>
    /// Probes without the cache; with <paramref name="hardwareAcceleration"/> off, as a PC without a GPU encoder
    /// would answer (the sink writer then loads only software encoders). For diagnostics and tests; the app uses
    /// <see cref="LoadOrProbeAsync(string, CancellationToken)"/>.
    /// </summary>
    public static Task<EncoderCapabilities> ProbeAsync(bool hardwareAcceleration, CancellationToken cancellationToken = default) =>
        Task.Run(
            () => ListEncoders() is { } encoders
                ? Probe(encoders, hardwareAcceleration, Path.GetTempPath(), TestEncodeTimeout, cancellationToken)
                : NoMediaFoundation(),
            cancellationToken);

    /// <summary>
    /// The search: for each codec with a listed encoder, every rate, shape and resolution of the matrix, largest
    /// first, skipping what the probes so far settle (<see cref="IsSettled"/>), with <paramref name="testEncode"/>
    /// run for the rest, each within <paramref name="timeout"/>.
    /// </summary>
    /// <remarks>
    /// <para>It stops early in three cases, all of which leave the result provisional (not cached):</para>
    /// <list type="bullet">
    /// <item>The graphics device was lost: every later encode on it would fail too.</item>
    /// <item>A test encode stalled: the driver may be hung, and the next encode would wait as long again.</item>
    /// <item>
    /// For one codec: a test encode failed for no size-related reason (<see cref="VideoExportFailure.EncoderFailed"/>,
    /// such as an encoder that takes the settings and then fails on the first sample) and the smallest size at the
    /// slowest rate (720p 16:9) fails as well. The encoder is broken, not limited by size, and probing every other
    /// size would only repeat the failure.
    /// </item>
    /// </list>
    /// </remarks>
    /// <param name="encoders">The listed encoders: a codec without one is not probed.</param>
    /// <param name="testEncode">One test encode: format, width, height and frames per second in, the result out; never throws.</param>
    /// <param name="hardwareAcceleration">False when the sink writer may load only software encoders.</param>
    /// <param name="timeout">How long one test encode may take before it counts as stalled.</param>
    /// <param name="cancellationToken">Stops between test encodes.</param>
    /// <param name="stalled">True when a test encode stalled: it may still be running, on the caller's device.</param>
    internal static List<ProbedSize> Search(
        IReadOnlyList<EncoderMft> encoders,
        Func<VideoFormat, int, int, int, ProbedSize> testEncode,
        bool hardwareAcceleration,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        out bool stalled)
    {
        var probes = new List<ProbedSize>();
        stalled = false;
        foreach (var format in _formats)
        {
            if (!encoders.Any(encoder => encoder.Format == format))
            {
                continue;
            }

            // Without a listed GPU encoder (or with hardware off) nothing can come out as hardware, so a
            // software result settles every smaller size too.
            var ceiling = hardwareAcceleration && encoders.Any(encoder => encoder.Format == format && encoder.IsHardware)
                ? EncoderKind.Hardware
                : EncoderKind.Software;
            var floorChecked = false;
            foreach (var (width, height, fps) in Cells())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsSettled(probes, format, width, height, fps, ceiling))
                {
                    continue;
                }

                var probe = Run(testEncode, format, width, height, fps, timeout);
                probes.Add(probe);
                if (probe.Failure is VideoExportFailure.GraphicsDeviceLost or VideoExportFailure.EncoderStalled)
                {
                    stalled = probe.Failure == VideoExportFailure.EncoderStalled;
                    return probes;
                }

                if (probe.Failure == VideoExportFailure.EncoderFailed && !floorChecked)
                {
                    floorChecked = true;
                    var floor = OutputSize.For(OutputResolution.Hd720, FrameAspect.Ratio16x9);
                    var slowest = FramesPerSecond[^1];
                    if (!IsSettled(probes, format, floor.Width, floor.Height, slowest, ceiling))
                    {
                        var floorProbe = Run(testEncode, format, floor.Width, floor.Height, slowest, timeout);
                        probes.Add(floorProbe);
                        if (floorProbe.Failure is VideoExportFailure.GraphicsDeviceLost or VideoExportFailure.EncoderStalled)
                        {
                            stalled = floorProbe.Failure == VideoExportFailure.EncoderStalled;
                            return probes;
                        }
                    }

                    if (BestCovering(probes, format, floor.Width, floor.Height, slowest) == EncoderKind.None)
                    {
                        break;
                    }
                }
            }
        }

        return probes;
    }

    /// <summary>
    /// True when the probes so far fix the answer for this size and rate: the best result among the probes that
    /// cover it equals the worst among those it covers (or <paramref name="ceiling"/>, the best any encode of this
    /// codec can get).
    /// </summary>
    internal static bool IsSettled(IReadOnlyList<ProbedSize> probes, VideoFormat format, int width, int height, int fps, EncoderKind ceiling)
    {
        var rate = new Fraction(fps, 1);
        var atMost = ceiling;
        foreach (var probe in probes)
        {
            if (probe.Format == format && probe.IsCoveredBy(width, height, rate) && probe.Encoder < atMost)
            {
                atMost = probe.Encoder;
            }
        }

        return BestCovering(probes, format, width, height, fps) >= atMost;
    }

    // The matrix in search order: fastest rate first, then each shape from 4K down.
    private static IEnumerable<(int Width, int Height, int Fps)> Cells()
    {
        foreach (var fps in FramesPerSecond)
        {
            foreach (var shape in Shapes)
            {
                for (var resolution = OutputResolution.Uhd2160; resolution >= OutputResolution.Hd720; resolution--)
                {
                    var size = OutputSize.For(resolution, shape);
                    yield return (size.Width, size.Height, fps);
                }
            }
        }
    }

    // The best result among the probes at least this large and fast.
    private static EncoderKind BestCovering(IReadOnlyList<ProbedSize> probes, VideoFormat format, int width, int height, int fps)
    {
        var rate = new Fraction(fps, 1);
        var best = EncoderKind.None;
        foreach (var probe in probes)
        {
            if (probe.Format == format && probe.Covers(width, height, rate) && probe.Encoder > best)
            {
                best = probe.Encoder;
            }
        }

        return best;
    }

    // One test encode on a thread of its own, given up on after the timeout: a hung driver call cannot be
    // cancelled, so the thread is left to it and the probe goes on without it (Search then stops).
    private static ProbedSize Run(Func<VideoFormat, int, int, int, ProbedSize> testEncode, VideoFormat format, int width, int height, int fps, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        var encode = Task.Factory.StartNew(
            () => testEncode(format, width, height, fps),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        return encode.Wait(timeout)
            ? encode.GetAwaiter().GetResult()
            : new ProbedSize(format, width, height, fps, EncoderKind.None, null, VideoExportFailure.EncoderStalled, clock.Elapsed.TotalMilliseconds);
    }

    // Null when Media Foundation itself is missing (a Windows N edition without the Media Feature Pack): MFStartup
    // or MFTEnumEx fails. That answers "nothing can be exported", as a provisional result, never an exception.
    private static IReadOnlyList<EncoderMft>? ListEncoders()
    {
        try
        {
            return EncoderCatalog.Find();
        }
        catch (Exception exception) when (ExportFailures.ShouldClassify(exception))
        {
            return null;
        }
    }

    private static EncoderCapabilities NoMediaFoundation() =>
        new([], [], TimeSpan.Zero, VideoExportFailure.EncoderUnavailable);

    // On an MTA thread: the device must not belong to an STA (see VideoExporter's thread rule). The test encodes
    // run on threads of their own (MTA too), one at a time.
    private static EncoderCapabilities Probe(
        IReadOnlyList<EncoderMft> encoders,
        bool hardwareAcceleration,
        string temporaryFolder,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        VideoDevice device;
        try
        {
            device = VideoDevice.Create();
        }
        catch (VideoExportException exception)
        {
            return new EncoderCapabilities(encoders, [], clock.Elapsed, exception.Failure);
        }

        var stalled = false;
        try
        {
            var probes = Search(
                encoders,
                (format, width, height, fps) => TestEncode(device, format, width, height, fps, hardwareAcceleration, temporaryFolder),
                hardwareAcceleration,
                timeout,
                cancellationToken,
                out stalled);
            return new EncoderCapabilities(encoders, probes, clock.Elapsed);
        }
        finally
        {
            // A stalled encode may still be using the device: leave it to the finalizers rather than pull it away.
            if (!stalled)
            {
                device.Dispose();
            }
        }
    }

    private static ProbedSize TestEncode(
        VideoDevice device,
        VideoFormat format,
        int width,
        int height,
        int fps,
        bool hardwareAcceleration,
        string temporaryFolder)
    {
        var path = Path.Combine(temporaryFolder, $"10lapse-probe-{Guid.NewGuid():N}.mp4");
        var clock = Stopwatch.StartNew();
        var rate = new Fraction(fps, 1);
        var settings = new VideoExportSettings(width, height, rate) { Format = format, HardwareAcceleration = hardwareAcceleration };
        try
        {
            SinkWriterEncoder encoder;
            try
            {
                encoder = new SinkWriterEncoder(path, settings, device);
            }
            catch (Exception exception) when (ExportFailures.ShouldClassify(exception))
            {
                throw ExportFailures.Classify(exception, VideoExportFailure.EncoderFailed, format, setup: true);
            }

            using (encoder)
            {
                try
                {
                    // A new target, disposed once the encoder has it, as the export does (Media Foundation holds the texture).
                    using (var target = new CanvasRenderTarget(
                        device.Canvas, width, height, 96f, DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Premultiplied))
                    {
                        using (var session = target.CreateDrawingSession())
                        {
                            session.Clear(Color.FromArgb(255, 32, 32, 32));
                        }

                        encoder.Write(target, 0, FrameTiming.FrameDuration(0, rate));
                    }

                    encoder.Finish();
                }
                catch (Exception exception) when (ExportFailures.ShouldClassify(exception))
                {
                    throw ExportFailures.Classify(exception, VideoExportFailure.EncoderFailed, format);
                }

                var info = encoder.Encoder;
                var kind = info?.IsHardware == true ? EncoderKind.Hardware : EncoderKind.Software;
                return new ProbedSize(format, width, height, fps, kind, info?.Name, null, clock.Elapsed.TotalMilliseconds);
            }
        }
        catch (VideoExportException exception)
        {
            return new ProbedSize(format, width, height, fps, EncoderKind.None, null, exception.Failure, clock.Elapsed.TotalMilliseconds);
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover probe file in the temporary folder is harmless; the answer is what matters.
        }
    }
}
