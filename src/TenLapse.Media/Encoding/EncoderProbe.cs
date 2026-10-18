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
/// The matrix is H.264 and HEVC × 60 and 30 fps × 16:9, 9:16, 4:3, 1:1 and 4:5 × 4K, 1440p, 1080p and 720p
/// (sizes from <see cref="OutputSize.For"/>). It is searched largest first, and a size is only encoded when the
/// probes so far do not settle it under the rule <see cref="EncoderCapabilities"/> assumes (an encoder that takes
/// a size and rate takes everything no larger on every axis): on a GPU that encodes 4K60 in both shapes, four
/// encodes answer the whole matrix.
/// </para>
/// <para>
/// A failed test encode is a "not supported" answer, classified as an export failure would be, never an
/// exception. An encoder that is not listed (HEVC without the HEVC Video Extensions or a GPU encoder) is not
/// probed at all, so HEVC is offered only when an HEVC encoder is present and a test encode worked.
/// </para>
/// </remarks>
public static class EncoderProbe
{
    /// <summary>The rates probed, fastest first. A request above the fastest is answered as unsupported.</summary>
    public static IReadOnlyList<int> FramesPerSecond { get; } = [60, 30];

    /// <summary>The shapes probed: the widest and the tallest first, since they settle the others.</summary>
    public static IReadOnlyList<FrameAspect> Shapes { get; } =
        [FrameAspect.Ratio16x9, FrameAspect.Ratio9x16, FrameAspect.Ratio4x3, FrameAspect.Ratio1x1, FrameAspect.Ratio4x5];

    private static readonly VideoFormat[] _formats = [VideoFormat.Mp4H264, VideoFormat.Mp4Hevc];

    /// <summary>
    /// The cached result when <paramref name="cachePath"/> holds one for this PC as it is now; otherwise probes,
    /// writes the cache (unless the result is provisional) and returns the new result. Runs on the thread pool,
    /// so it may be awaited from the UI thread. A cache that cannot be written is left as it is.
    /// </summary>
    /// <param name="cachePath">The JSON file: in the app's local data folder (LocalState or LocalApplicationData).</param>
    /// <param name="cancellationToken">Stops between test encodes.</param>
    public static Task<EncoderCapabilities> LoadOrProbeAsync(string cachePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(cachePath);
        return Task.Run(
            () =>
            {
                var encoders = EncoderCatalog.Find();
                var key = EncoderCacheKey.ForThisMachine(encoders);
                if (EncoderCapabilitiesCache.TryRead(cachePath, key) is { } cached)
                {
                    return cached;
                }

                var capabilities = Probe(encoders, cancellationToken);
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
    /// <see cref="LoadOrProbeAsync"/>.
    /// </summary>
    public static Task<EncoderCapabilities> ProbeAsync(bool hardwareAcceleration, CancellationToken cancellationToken = default) =>
        Task.Run(() => Probe(EncoderCatalog.Find(), cancellationToken, hardwareAcceleration), cancellationToken);

    // On an MTA thread: the device must not belong to an STA (see VideoExporter's thread rule).
    private static EncoderCapabilities Probe(IReadOnlyList<EncoderMft> encoders, CancellationToken cancellationToken, bool hardwareAcceleration = true)
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

        var probes = new List<ProbedSize>();
        using (device)
        {
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

                foreach (var fps in FramesPerSecond)
                {
                    foreach (var shape in Shapes)
                    {
                        for (var resolution = OutputResolution.Uhd2160; resolution >= OutputResolution.Hd720; resolution--)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var size = OutputSize.For(resolution, shape);
                            if (IsSettled(probes, format, size.Width, size.Height, fps, ceiling))
                            {
                                continue;
                            }

                            var probe = TestEncode(device, format, size.Width, size.Height, fps, hardwareAcceleration);
                            probes.Add(probe);
                            if (probe.Failure == VideoExportFailure.GraphicsDeviceLost)
                            {
                                // Every later encode on this device would fail too; the result is provisional.
                                return new EncoderCapabilities(encoders, probes, clock.Elapsed);
                            }
                        }
                    }
                }
            }
        }

        return new EncoderCapabilities(encoders, probes, clock.Elapsed);
    }

    /// <summary>
    /// True when the probes so far fix the answer for this size and rate: the best result among the probes that
    /// cover it equals the worst among those it covers (or <paramref name="ceiling"/>, the best any encode of this
    /// codec can get).
    /// </summary>
    internal static bool IsSettled(IReadOnlyList<ProbedSize> probes, VideoFormat format, int width, int height, int fps, EncoderKind ceiling)
    {
        var rate = new Fraction(fps, 1);
        var atLeast = EncoderKind.None;
        var atMost = ceiling;
        foreach (var probe in probes)
        {
            if (probe.Format != format)
            {
                continue;
            }

            if (probe.Covers(width, height, rate) && probe.Encoder > atLeast)
            {
                atLeast = probe.Encoder;
            }

            if (probe.IsCoveredBy(width, height, rate) && probe.Encoder < atMost)
            {
                atMost = probe.Encoder;
            }
        }

        return atLeast >= atMost;
    }

    private static ProbedSize TestEncode(VideoDevice device, VideoFormat format, int width, int height, int fps, bool hardwareAcceleration)
    {
        var path = Path.Combine(Path.GetTempPath(), $"10lapse-probe-{Guid.NewGuid():N}.mp4");
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
                throw ExportFailures.Classify(exception, VideoExportFailure.EncoderFailed, setup: true);
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
                    throw ExportFailures.Classify(exception, VideoExportFailure.EncoderFailed);
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
            // A leftover probe file in %TEMP% is harmless; the answer is what matters.
        }
    }
}
