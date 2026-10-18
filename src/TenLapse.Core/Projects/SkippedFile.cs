namespace TenLapse.Core.Projects;

/// <summary>Why import skipped a file (#13 lists these under "couldn't read").</summary>
public enum SkipReason
{
    /// <summary>No installed decoder reads this format.</summary>
    UnsupportedFormat,

    /// <summary>A decoder exists but the file is damaged or truncated.</summary>
    DecodeFailed,

    /// <summary>Windows refused access to the file.</summary>
    AccessDenied,

    /// <summary>The file disappeared between listing and reading.</summary>
    FileNotFound,

    /// <summary>Anything else; <see cref="SkippedFile.Message"/> says what.</summary>
    Other,
}

/// <summary>A file import could not use, kept so the user can see which and why.</summary>
public sealed record SkippedFile
{
    /// <summary>Creates an entry for the "couldn't read" list.</summary>
    /// <param name="path">The file's full path.</param>
    /// <param name="reason">The kind of failure.</param>
    /// <param name="message">What happened, in words for the list (may be empty).</param>
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

    /// <summary>What happened, for the list.</summary>
    public string Message { get; }
}
