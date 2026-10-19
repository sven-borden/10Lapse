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

    /// <summary>
    /// An online-only OneDrive file (files on demand) the user chose not to download when the import asked (#12): it was
    /// never opened, so nothing was downloaded.
    /// </summary>
    CloudOnly,
}

/// <summary>A free (or cheap) Windows extension from the Microsoft Store that adds a WIC decoder or a codec one needs (#14 offers it).</summary>
/// <param name="Name">The Store name, as the user will find it ("HEIF Image Extensions").</param>
/// <param name="StoreProductId">The Store product ID for <c>ms-windows-store://pdp/?ProductId=…</c>.</param>
/// <param name="HasFee">True when the Store sells it (the HEVC Video Extensions); the InfoBar then does not call it free.</param>
public sealed record PhotoCodecExtension(string Name, string StoreProductId, bool HasFee = false);

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
    /// <param name="missingExtensions">
    /// With <see cref="SkipReason.NeedsExtension"/>, the Store extensions the file may need, in the order to install them
    /// (the decoder before the codec): the first is known to be missing, the later ones are needed too unless this PC
    /// already has them (a HEIC photo without the HEIF decoder needs the HEVC codec as well, which only the InfoBar's
    /// check of the PC can tell). Null or empty for any other reason.
    /// </param>
    public SkippedFile(string path, SkipReason reason, string message, IReadOnlyList<PhotoCodecExtension>? missingExtensions = null)
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
        MissingExtensions = missingExtensions is { Count: > 0 } ? [.. missingExtensions] : [];
    }

    /// <summary>The file's full path.</summary>
    public string Path { get; }

    /// <summary>The kind of failure.</summary>
    public SkipReason Reason { get; }

    /// <summary>What happened in detail, for the log.</summary>
    public string Message { get; }

    /// <summary>
    /// The Store extensions that would make the file readable, in install order (see the constructor); empty unless
    /// <see cref="Reason"/> is <see cref="SkipReason.NeedsExtension"/> and the cause is known.
    /// </summary>
    public IReadOnlyList<PhotoCodecExtension> MissingExtensions { get; }
}
