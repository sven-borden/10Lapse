using TenLapse.Core.Projects;
using TenLapse.Core.Timing;

namespace TenLapse.Presentation;

/// <summary>
/// What this PC can export, as far as the Output panel (#23) needs to know. The App adapts Media's
/// <c>EncoderCapabilities</c> (#9) to it, because Presentation cannot see Media; tests use fakes. Every answer is
/// about one codec, frame shape and frame rate: a size is offered only when the answer is yes.
/// </summary>
public interface IOutputCapabilities
{
    /// <summary>True when the codec can be offered at all: an encoder works and, for HEVC, Windows can play the result back.</summary>
    bool IsAvailable(VideoFormat format);

    /// <summary>True when an export at this size works, on the graphics hardware or in software.</summary>
    /// <param name="format">The codec.</param>
    /// <param name="resolution">The resolution (the short side).</param>
    /// <param name="aspect">The frame shape; <see cref="FrameAspect.MatchPhotos"/> comes with <paramref name="photoSize"/>.</param>
    /// <param name="frameRate">The rate the clip plays at.</param>
    /// <param name="photoSize">Match photos: the most common photo size, whose shape the video takes.</param>
    bool Supports(VideoFormat format, OutputResolution resolution, FrameAspect aspect, Fraction frameRate, PixelSize? photoSize);

    /// <summary>True when the graphics hardware takes this export; false when only the slower software encoder does.</summary>
    bool UsesHardware(VideoFormat format, OutputResolution resolution, FrameAspect aspect, Fraction frameRate, PixelSize? photoSize);

    /// <summary>The sentence under the list when sizes are left out or slower: "This PC's graphics hardware exports up to 1440p."</summary>
    string Summary(VideoFormat format, FrameAspect aspect, Fraction frameRate, PixelSize? photoSize);
}

/// <summary>
/// What the panel offers when the startup probe failed or is taking too long: H.264 up to 1080p, which every PC
/// exports, and nothing else. A later real answer replaces it.
/// </summary>
public sealed class ConservativeOutputCapabilities : IOutputCapabilities
{
    /// <summary>The one instance.</summary>
    public static ConservativeOutputCapabilities Instance { get; } = new();

    private ConservativeOutputCapabilities()
    {
    }

    /// <inheritdoc />
    public bool IsAvailable(VideoFormat format) => format == VideoFormat.Mp4H264;

    /// <inheritdoc />
    public bool Supports(VideoFormat format, OutputResolution resolution, FrameAspect aspect, Fraction frameRate, PixelSize? photoSize) =>
        format == VideoFormat.Mp4H264 && resolution <= OutputResolution.Hd1080;

    /// <inheritdoc />
    public bool UsesHardware(VideoFormat format, OutputResolution resolution, FrameAspect aspect, Fraction frameRate, PixelSize? photoSize) => false;

    /// <inheritdoc />
    public string Summary(VideoFormat format, FrameAspect aspect, Fraction frameRate, PixelSize? photoSize) => OutputText.ConservativeNote;
}

/// <summary>One entry of an Output panel list: the value it stands for and the words the list shows.</summary>
/// <typeparam name="T">The setting's type.</typeparam>
/// <param name="Value">The setting.</param>
/// <param name="Label">What the list shows ("1080p · 1920 × 1080").</param>
public sealed record OutputOption<T>(T Value, string Label)
    where T : struct, Enum
{
    /// <summary>The label, which is what a ComboBox shows for an item without a template.</summary>
    public override string ToString() => Label;
}
