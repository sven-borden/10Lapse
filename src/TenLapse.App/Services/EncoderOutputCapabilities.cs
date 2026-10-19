using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Encoding;
using TenLapse.Presentation;

namespace TenLapse.Services;

/// <summary>
/// #9's <see cref="EncoderCapabilities"/> seen by the Output panel (#23): Presentation cannot reference Media, so the
/// panel asks this adapter. It answers from the probe and nothing else; HEVC is available only when it encodes and
/// Windows can play it back.
/// </summary>
/// <remarks>Compiled into the tests too (a linked file), so it uses nothing of the App's.</remarks>
internal sealed class EncoderOutputCapabilities : IOutputCapabilities
{
    private readonly EncoderCapabilities _capabilities;

    /// <param name="capabilities">What the startup probe found.</param>
    public EncoderOutputCapabilities(EncoderCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        _capabilities = capabilities;
    }

    /// <inheritdoc />
    public bool IsAvailable(VideoFormat format) => _capabilities.IsAvailable(format);

    /// <inheritdoc />
    public bool Supports(VideoFormat format, OutputResolution resolution, FrameAspect aspect, Fraction frameRate, PixelSize? photoSize) =>
        _capabilities.Supports(format, resolution, aspect, frameRate, photoSize);

    /// <inheritdoc />
    public bool UsesHardware(VideoFormat format, OutputResolution resolution, FrameAspect aspect, Fraction frameRate, PixelSize? photoSize)
    {
        var size = OutputSize.For(resolution, aspect, photoSize);
        return _capabilities.EncoderFor(format, size.Width, size.Height, frameRate) == EncoderKind.Hardware;
    }

    /// <inheritdoc />
    public string Summary(VideoFormat format, FrameAspect aspect, Fraction frameRate, PixelSize? photoSize) =>
        _capabilities.Summary(format, aspect, frameRate, photoSize);
}
