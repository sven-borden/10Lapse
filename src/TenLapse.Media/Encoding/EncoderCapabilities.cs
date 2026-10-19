using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Export;

namespace TenLapse.Media.Encoding;

/// <summary>Which encoder an export at a given size and rate would run on. Ordered: more is better.</summary>
public enum EncoderKind
{
    /// <summary>No encoder takes it: the export would fail.</summary>
    None,

    /// <summary>The software encoder takes it (the graphics hardware does not): it works, several times slower.</summary>
    Software,

    /// <summary>The graphics hardware takes it.</summary>
    Hardware,
}

/// <summary>One test encode of the probe: a frame written and finalized through the export's own sink writer path.</summary>
/// <param name="Format">The codec.</param>
/// <param name="Width">Output width in pixels.</param>
/// <param name="Height">Output height in pixels.</param>
/// <param name="FramesPerSecond">The frame rate it was configured with.</param>
/// <param name="Encoder">Which encoder the sink writer loaded, or <see cref="EncoderKind.None"/> when it failed.</param>
/// <param name="EncoderName">The loaded encoder's friendly name; null when it failed.</param>
/// <param name="Failure">Why it failed, classified as an export failure would be; null when it worked.</param>
/// <param name="Milliseconds">How long the test encode took.</param>
public sealed record ProbedSize(
    VideoFormat Format,
    int Width,
    int Height,
    int FramesPerSecond,
    EncoderKind Encoder,
    string? EncoderName,
    VideoExportFailure? Failure,
    double Milliseconds)
{
    /// <summary>True when this probe's size and rate are at least the other's on every axis.</summary>
    internal bool Covers(int width, int height, Fraction frameRate) =>
        Width >= width && Height >= height && new Fraction(FramesPerSecond, 1) >= frameRate;

    /// <summary>True when the other size and rate are at least this probe's on every axis.</summary>
    internal bool IsCoveredBy(int width, int height, Fraction frameRate) =>
        Width <= width && Height <= height && new Fraction(FramesPerSecond, 1) <= frameRate;
}

/// <summary>
/// What this PC can encode, from <see cref="EncoderProbe"/>: per codec, which sizes and frame rates the export
/// takes and whether the graphics hardware or the software encoder runs them. Immutable; safe on any thread.
/// </summary>
/// <remarks>
/// The answers follow from the test encodes in <see cref="Probes"/> under one assumption: an encoder that takes a
/// size and rate also takes every size no wider, no taller and no faster. So <see cref="EncoderFor"/> is the best
/// encoder among the probes that cover the request on every axis, and <see cref="EncoderKind.None"/> when no probe
/// covers it (above 120 fps, or a shape wider than 16:9 or taller than 9:16 at 4K): a conservative answer, never an
/// offer the export cannot keep. A rate between two probed rates takes the answer of the next probed rate up.
/// HEVC also needs an HEVC decoder (<see cref="CanPlayBack"/>): without one every HEVC answer is
/// <see cref="EncoderKind.None"/>, whatever the encoders can do.
/// </remarks>
public sealed class EncoderCapabilities
{
    // Failures that may not happen on the next try. EncoderFailed counts: it names no cause (E_FAIL from a driver
    // mid-update, an encoder that fails its first sample), while a size the encoder cannot take comes back as
    // SettingsRejected and a missing encoder as EncoderUnavailable, which are answers.
    private static readonly HashSet<VideoExportFailure> _transient =
    [
        VideoExportFailure.EncoderFailed,
        VideoExportFailure.NoGraphicsDevice,
        VideoExportFailure.GraphicsDeviceLost,
        VideoExportFailure.OutOfMemory,
        VideoExportFailure.DiskFull,
        VideoExportFailure.OutputFolderNotWritable,
        VideoExportFailure.OutputFolderMissing,
        VideoExportFailure.OutputFileInUse,
        VideoExportFailure.EncoderStalled,
        VideoExportFailure.EncoderStuck,
    ];

    /// <summary>Creates a capability set from the encoders listed and the test encodes run.</summary>
    /// <param name="encoders">The encoders <see cref="EncoderCatalog"/> listed.</param>
    /// <param name="probes">The test encodes.</param>
    /// <param name="probeDuration">How long the whole probe took (device, encodes, clean-up).</param>
    /// <param name="deviceFailure">Why no probe could run (no graphics device), or null.</param>
    /// <param name="decoders">The decoders <see cref="EncoderCatalog.FindDecoders"/> listed: HEVC is offered only with one.</param>
    /// <exception cref="ArgumentException">A probe with a non-positive size or rate, or an unknown enum value.</exception>
    public EncoderCapabilities(
        IEnumerable<EncoderMft> encoders,
        IEnumerable<ProbedSize> probes,
        TimeSpan probeDuration,
        VideoExportFailure? deviceFailure = null,
        IEnumerable<DecoderMft>? decoders = null)
    {
        ArgumentNullException.ThrowIfNull(encoders);
        ArgumentNullException.ThrowIfNull(probes);
        Encoders = [.. encoders];
        Probes = [.. probes];
        Decoders = decoders is null ? [] : [.. decoders];
        foreach (var probe in Probes)
        {
            if (probe is null || probe.Width <= 0 || probe.Height <= 0 || probe.FramesPerSecond <= 0
                || !Enum.IsDefined(probe.Format) || !Enum.IsDefined(probe.Encoder))
            {
                throw new ArgumentException($"Not a valid probe: {probe}.", nameof(probes));
            }
        }

        ProbeDuration = probeDuration;
        DeviceFailure = deviceFailure;
    }

    /// <summary>The encoder transforms Media Foundation lists, hardware first per codec.</summary>
    public IReadOnlyList<EncoderMft> Encoders { get; }

    /// <summary>The test encodes the answers rest on.</summary>
    public IReadOnlyList<ProbedSize> Probes { get; }

    /// <summary>The decoders Media Foundation lists for the codecs that need one listed (HEVC).</summary>
    public IReadOnlyList<DecoderMft> Decoders { get; }

    /// <summary>How long the probe took when it ran (a cached result keeps the original time).</summary>
    public TimeSpan ProbeDuration { get; }

    /// <summary>
    /// Why the probe could not run at all: <see cref="VideoExportFailure.NoGraphicsDevice"/>, or
    /// <see cref="VideoExportFailure.EncoderUnavailable"/> when Media Foundation could not list encoders. Null when it ran.
    /// </summary>
    public VideoExportFailure? DeviceFailure { get; }

    /// <summary>
    /// True when the answer may change on the next try: it is not cached, so the next start probes again. That is
    /// when no probe could run (no device), when a test encode failed in a way that may not repeat (a driver reset,
    /// a full disk, a stall, or <see cref="VideoExportFailure.EncoderFailed"/>, which names no cause), or when a
    /// listed GPU encoder never produced a hardware result for its codec: a GPU encoder that takes no size at all is
    /// more likely busy, mid-update or still starting than really unable, and caching that would hide the GPU until
    /// the next driver update. A probe with hardware transforms off is provisional on a PC with a GPU encoder.
    /// </summary>
    public bool IsProvisional =>
        DeviceFailure is not null
        || Probes.Any(probe => probe.Failure is { } failure && _transient.Contains(failure))
        || Encoders.Any(encoder => encoder.IsHardware && CanPlayBack(encoder.Format) && !HasHardware(encoder.Format));

    /// <summary>
    /// True when Windows on this PC can play the codec back. H.264 decoding ships with Windows (every edition that
    /// has Media Foundation at all). HEVC decoding comes only with the HEVC Video Extensions from the Store (or a
    /// PC maker's copy), while a GPU's HEVC encoder ships with its display driver: a PC can encode HEVC it cannot
    /// play, and such a video would not open in Photos or Films &amp; TV, so HEVC needs a listed HEVC decoder.
    /// </summary>
    public bool CanPlayBack(VideoFormat format) => CanPlayBack(format, Decoders);

    /// <summary>The rule of <see cref="CanPlayBack(VideoFormat)"/> on a list of decoders: the probe skips a codec it fails.</summary>
    internal static bool CanPlayBack(VideoFormat format, IEnumerable<DecoderMft> decoders) =>
        format != VideoFormat.Mp4Hevc || decoders.Any(decoder => decoder.Format == format);

    /// <summary>The encoder an export of this size and rate would run on (see the remarks).</summary>
    public EncoderKind EncoderFor(VideoFormat format, int width, int height, Fraction frameRate)
    {
        var best = EncoderKind.None;
        if (!CanPlayBack(format))
        {
            return best;
        }

        foreach (var probe in Probes)
        {
            if (probe.Format == format && probe.Encoder > best && probe.Covers(width, height, frameRate))
            {
                best = probe.Encoder;
            }
        }

        return best;
    }

    /// <summary>True when an export of this size and rate works, on the hardware or in software.</summary>
    public bool Supports(VideoFormat format, int width, int height, Fraction frameRate) =>
        EncoderFor(format, width, height, frameRate) != EncoderKind.None;

    /// <summary>True when an export at this resolution, shape and rate works (sizes from <see cref="OutputSize.For"/>).</summary>
    /// <exception cref="ArgumentException"><see cref="FrameAspect.MatchPhotos"/> without a photo size.</exception>
    public bool Supports(VideoFormat format, OutputResolution resolution, FrameAspect aspect, Fraction frameRate, PixelSize? photoSize = null)
    {
        var size = OutputSize.For(resolution, aspect, photoSize);
        return Supports(format, size.Width, size.Height, frameRate);
    }

    /// <summary>
    /// The largest resolution (the short side, see <see cref="OutputResolution"/>) whose export at this shape and
    /// rate runs on at least <paramref name="minimum"/>; null when not even 720p does. #23 offers every resolution
    /// up to it with <see cref="EncoderKind.Software"/>, and names the hardware's limit with <see cref="EncoderKind.Hardware"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <see cref="FrameAspect.MatchPhotos"/> without a photo size, or <paramref name="minimum"/> is <see cref="EncoderKind.None"/>.
    /// </exception>
    public OutputResolution? MaxResolution(
        VideoFormat format,
        FrameAspect aspect,
        Fraction frameRate,
        PixelSize? photoSize = null,
        EncoderKind minimum = EncoderKind.Software)
    {
        if (minimum is not (EncoderKind.Software or EncoderKind.Hardware))
        {
            throw new ArgumentOutOfRangeException(nameof(minimum), minimum, "Ask for the software or the hardware limit.");
        }

        for (var resolution = OutputResolution.Uhd2160; resolution >= OutputResolution.Hd720; resolution--)
        {
            var size = OutputSize.For(resolution, aspect, photoSize);
            if (EncoderFor(format, size.Width, size.Height, frameRate) >= minimum)
            {
                return resolution;
            }
        }

        return null;
    }

    /// <summary>
    /// True when at least one test encode of this codec worked and Windows can play it back
    /// (<see cref="CanPlayBack(VideoFormat)"/>): offer HEVC only then.
    /// </summary>
    public bool IsAvailable(VideoFormat format) =>
        CanPlayBack(format) && Probes.Any(probe => probe.Format == format && probe.Encoder != EncoderKind.None);

    /// <summary>True when the graphics hardware encodes this codec at some size.</summary>
    public bool HasHardware(VideoFormat format) =>
        Probes.Any(probe => probe.Format == format && probe.Encoder == EncoderKind.Hardware);

    /// <summary>The name of the encoder that ran the largest test encode of this codec, hardware first; null when none worked.</summary>
    public string? EncoderName(VideoFormat format) =>
        Probes
            .Where(probe => probe.Format == format && probe.Encoder != EncoderKind.None)
            .OrderByDescending(probe => probe.Encoder)
            .ThenByDescending(probe => (long)probe.Width * probe.Height * probe.FramesPerSecond)
            .Select(probe => probe.EncoderName)
            .FirstOrDefault();

    /// <summary>
    /// The note for the output panel, such as "This PC's graphics hardware exports up to 1440p." (words in
    /// <see cref="EncoderText"/>).
    /// </summary>
    /// <exception cref="ArgumentException"><see cref="FrameAspect.MatchPhotos"/> without a photo size.</exception>
    public string Summary(VideoFormat format, FrameAspect aspect, Fraction frameRate, PixelSize? photoSize = null) =>
        CanPlayBack(format)
            ? EncoderText.Summary(
                format,
                MaxResolution(format, aspect, frameRate, photoSize, EncoderKind.Hardware),
                MaxResolution(format, aspect, frameRate, photoSize))
            : EncoderText.NoPlaybackSummary(format);
}
