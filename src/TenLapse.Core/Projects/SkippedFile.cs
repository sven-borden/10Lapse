namespace TenLapse.Core.Projects;

/// <summary>
/// Why import skipped a file (#13 lists these under "couldn't read"). New values go at the end, so the numbers
/// of the existing ones never change.
/// </summary>
public enum SkipReason
{
    /// <summary>No installed decoder reads this format (or a kind of image inside it that Windows cannot convert).</summary>
    UnsupportedFormat,

    /// <summary>A decoder exists but the file is damaged (<see cref="Truncated"/> when it ends early).</summary>
    DecodeFailed,

    /// <summary>Windows refused access to the file.</summary>
    AccessDenied,

    /// <summary>The file disappeared between listing and reading, or its drive or network share was disconnected.</summary>
    FileNotFound,

    /// <summary>Anything else; <see cref="SkippedFile.Message"/> says what, for the log.</summary>
    Other,

    /// <summary>The file has 0 bytes.</summary>
    EmptyFile,

    /// <summary>The file ends before the image does: a copy or a download that stopped part way.</summary>
    Truncated,

    /// <summary>
    /// A known format (HEIF, AVIF, camera RAW) whose decoder, or the codec its decoder needs (HEVC for HEIC), comes
    /// in a Store extension this PC does not have (#14).
    /// </summary>
    NeedsExtension,

    /// <summary>Another program has the file open and does not let others read it.</summary>
    FileInUse,

    /// <summary>The image is too large to decode in memory.</summary>
    ImageTooLarge,

    /// <summary>An online-only cloud file (OneDrive files on demand) that could not be downloaded: offline, or the sync app is not running.</summary>
    CloudFileOffline,
}

/// <summary>A file import could not use, kept so the user can see which and why.</summary>
public sealed record SkippedFile
{
    /// <summary>Creates an entry for the "couldn't read" list.</summary>
    /// <param name="path">The file's full path.</param>
    /// <param name="reason">The kind of failure.</param>
    /// <param name="message">
    /// What happened in detail, for the log (may be empty). The list shows the text of <paramref name="reason"/>
    /// (Presentation's <c>SkippedFileText</c>), not this.
    /// </param>
    public SkippedFile(string path, SkipReason reason, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(message);
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown skip reason.");
        }

        Path = path;
        Reason = reason;
        Message = message;
    }

    /// <summary>The file's full path.</summary>
    public string Path { get; }

    /// <summary>The kind of failure.</summary>
    public SkipReason Reason { get; }

    /// <summary>What happened in detail, for the log.</summary>
    public string Message { get; }
}
