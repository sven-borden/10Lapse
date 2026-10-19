using System.Diagnostics.CodeAnalysis;
using TenLapse.Core.Projects;

namespace TenLapse.Media.Decoding;

/// <summary>Why a photo could not be read, with what the log needs.</summary>
public sealed record PhotoFailure
{
    /// <summary>Describes a failure.</summary>
    /// <param name="path">The file.</param>
    /// <param name="reason">The kind of failure, which picks the text the user sees.</param>
    /// <param name="detail">What happened in detail, for the log: the error and its HRESULT name where there is one.</param>
    /// <param name="exception">The original exception, for the log; null when the decoder found the problem itself.</param>
    /// <param name="format">The known format of the file by its extension, when there is one.</param>
    /// <param name="missingExtension">With <see cref="SkipReason.NeedsExtension"/>, the Store extension to install (#14 offers it).</param>
    public PhotoFailure(
        string path, SkipReason reason, string detail, Exception? exception = null, KnownPhotoFormat? format = null, PhotoCodecExtension? missingExtension = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(detail);
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown skip reason.");
        }

        Path = path;
        Reason = reason;
        Detail = detail;
        Exception = exception;
        Format = format;
        MissingExtension = missingExtension;
    }

    /// <summary>The file.</summary>
    public string Path { get; }

    /// <summary>The kind of failure.</summary>
    public SkipReason Reason { get; }

    /// <summary>What happened in detail, for the log.</summary>
    public string Detail { get; }

    /// <summary>The original exception, or null.</summary>
    public Exception? Exception { get; }

    /// <summary>The file's known format by extension, or null.</summary>
    public KnownPhotoFormat? Format { get; }

    /// <summary>
    /// With <see cref="SkipReason.NeedsExtension"/>, the Store extension that is missing: the one adding the decoder
    /// (<see cref="KnownPhotoFormat.Container"/>) or, when that is installed, the codec it needs
    /// (<see cref="KnownPhotoFormat.Codec"/>: HEVC for a HEIC photo). Null otherwise.
    /// </summary>
    public PhotoCodecExtension? MissingExtension { get; }

    /// <summary>The entry for the project's "couldn't read" list.</summary>
    public SkippedFile ToSkippedFile() => new(Path, Reason, Detail, MissingExtension is null ? null : NeededExtensions());

    // The missing extension, then (when it is the decoder) the codec the format's images need inside it: a PC without
    // the HEIF decoder may lack HEVC too, and a failure cannot tell, because the file never reached a decoder. The
    // InfoBar asks the PC (#14).
    private List<PhotoCodecExtension> NeededExtensions()
    {
        List<PhotoCodecExtension> needed = [MissingExtension!];
        if (Format is { Codec: { } codec, Container: { } container } && container == MissingExtension)
        {
            needed.Add(codec);
        }

        return needed;
    }
}

/// <summary>A photo read or decoded, or why not. Never both.</summary>
/// <typeparam name="T">What a success carries.</typeparam>
public sealed class PhotoResult<T>
    where T : class
{
    private PhotoResult(T? value, PhotoFailure? failure)
    {
        Value = value;
        Failure = failure;
    }

    /// <summary>True when <see cref="Value"/> is set; false when <see cref="Failure"/> is.</summary>
    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Failure))]
    public bool Succeeded => Value is not null;

    /// <summary>What was read, or null on a failure.</summary>
    public T? Value { get; }

    /// <summary>Why it failed, or null on a success.</summary>
    public PhotoFailure? Failure { get; }

    /// <summary>A success.</summary>
    public static PhotoResult<T> Success(T value) => new(value ?? throw new ArgumentNullException(nameof(value)), null);

    /// <summary>A failure.</summary>
    public static PhotoResult<T> Failed(PhotoFailure failure) => new(null, failure ?? throw new ArgumentNullException(nameof(failure)));
}

/// <summary>
/// A photo the export needed could not be decoded. Thrown by <see cref="Export.PhotoFileSource"/>, which cannot
/// skip a frame; the export rethrows it as it is, so its caller (#22) can name the frame and the reason.
/// </summary>
public sealed class PhotoDecodeException : Exception
{
    /// <summary>Wraps a failure of frame <paramref name="frameIndex"/>.</summary>
    public PhotoDecodeException(PhotoFailure failure, int frameIndex)
        : base($"Frame {frameIndex} ({failure?.Path}) could not be decoded: {failure?.Reason}. {failure?.Detail}", failure?.Exception)
    {
        ArgumentNullException.ThrowIfNull(failure);
        Failure = failure;
        FrameIndex = frameIndex;
    }

    /// <summary>Why it failed.</summary>
    public PhotoFailure Failure { get; }

    /// <summary>The frame's index in the export.</summary>
    public int FrameIndex { get; }
}
