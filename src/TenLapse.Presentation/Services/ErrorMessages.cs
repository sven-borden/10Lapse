using System.Reflection;
using System.Runtime.InteropServices;

namespace TenLapse.Presentation.Services;

/// <summary>
/// Which sentence a failure gets (<see cref="For"/>), whether it is a cancellation (<see cref="IsCancellation"/>)
/// and whether the process can go on after it (<see cref="IsFatal"/>).
/// </summary>
public static class ErrorMessages
{
    // Win32 errors as HRESULTs, as .NET and COM interop carry them on IOException, COMException and others.
    private const int FileNotFound = unchecked((int)0x80070002);
    private const int PathNotFound = unchecked((int)0x80070003);
    private const int AccessDenied = unchecked((int)0x80070005);
    private const int OutOfMemoryHResult = unchecked((int)0x8007000E);
    private const int NotEnoughMemory = unchecked((int)0x80070008);
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);
    private const int HandleDiskFull = unchecked((int)0x80070027);
    private const int DiskFull = unchecked((int)0x80070070);
    private const int WriteProtect = unchecked((int)0x80070013);
    private const int BadNetPath = unchecked((int)0x80070035);
    private const int InvalidDrive = unchecked((int)0x8007000F);

    // A removable drive or a network share that went away while the app used it, or a medium that cannot be read.
    private const int NotReady = unchecked((int)0x80070015);
    private const int DeviceNotExist = unchecked((int)0x80070037);
    private const int NetNameDeleted = unchecked((int)0x80070040);
    private const int Crc = unchecked((int)0x80070017);

    // Another app holds the clipboard open (CLIPBRD_E_CANT_OPEN).
    private const int ClipboardCantOpen = unchecked((int)0x800401D0);

    // How deep the inner exceptions are searched for a kind the user can act on.
    private const int MaxDepth = 8;

    /// <summary>The message for the generic case: an unexpected problem, with <c>Copy diagnostics</c>.</summary>
    public static ErrorMessage Unexpected { get; } = new(InfoBarSeverity.Critical, ProjectText.ErrorUnexpected, OffersDiagnostics: true);

    /// <summary>
    /// What the user is told about <paramref name="exception"/>. The exception and then its inner exceptions are
    /// each offered to the <paramref name="mappers"/> in order, then to the built-in kinds: a file or folder that is
    /// gone, a drive or share disconnected, a file that cannot be read, access denied, a file open in another app, a
    /// full disk, too little memory, a path too long, a busy clipboard. A missing or unloadable .NET assembly is a
    /// broken install, not a file of the user's: it stops the search at <see cref="Unexpected"/>. Anything else is
    /// <see cref="Unexpected"/>. A mapper that throws is skipped.
    /// </summary>
    public static ErrorMessage For(Exception exception, IReadOnlyList<IErrorMessageMapper>? mappers = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        foreach (var candidate in Chain(exception))
        {
            foreach (var mapper in mappers ?? [])
            {
                ErrorMessage? mapped;
                try
                {
                    mapped = mapper.Map(candidate);
                }
                catch (Exception)
                {
                    mapped = null;
                }

                if (mapped is not null)
                {
                    return mapped;
                }
            }

            if (BuiltIn(candidate) is { } builtIn)
            {
                return builtIn;
            }
        }

        return Unexpected;
    }

    /// <summary>True for a cancellation (<see cref="OperationCanceledException"/>, or an aggregate of nothing else): not an error.</summary>
    public static bool IsCancellation(Exception exception) => exception switch
    {
        OperationCanceledException => true,
        AggregateException aggregate => aggregate.Flatten().InnerExceptions is { Count: > 0 } inner && inner.All(IsCancellation),
        _ => false,
    };

    /// <summary>
    /// True when the process should not go on after <paramref name="exception"/>: corrupted state (access
    /// violation, a native exception), or code that cannot run (invalid program, a bad image). Everything else, out
    /// of memory included, is recoverable: the action failed, the app keeps going.
    /// </summary>
    public static bool IsFatal(Exception exception) => exception is AccessViolationException
        or SEHException
        or InvalidProgramException
        or BadImageFormatException
        or StackOverflowException;

    // The exception, then the one inside it, unwrapping single-exception aggregates and reflection wrappers.
    private static IEnumerable<Exception> Chain(Exception exception)
    {
        Exception? current = exception;
        for (var depth = 0; current is not null && depth < MaxDepth; depth++)
        {
            switch (current)
            {
                case AggregateException aggregate when aggregate.Flatten().InnerExceptions.Count == 1:
                    current = aggregate.Flatten().InnerExceptions[0];
                    continue;
                case TargetInvocationException { InnerException: { } inner }:
                    current = inner;
                    continue;
            }

            yield return current;
            current = current.InnerException;
        }
    }

    // A .NET assembly that is missing or cannot be loaded: a broken install, not a file of the user's. Its
    // FileNotFoundException names the assembly ("TenLapse.Media, Version=3.0.0.0, Culture=neutral, ...").
    private static bool IsAssemblyLoadFailure(Exception exception) => exception switch
    {
        FileLoadException => true,
        FileNotFoundException { FileName: { } name } => name.Contains(", Version=", StringComparison.OrdinalIgnoreCase)
            || name.Contains(", Culture=", StringComparison.OrdinalIgnoreCase)
            || name.Contains("PublicKeyToken=", StringComparison.OrdinalIgnoreCase),
        FileNotFoundException { Message: { } message } => message.StartsWith("Could not load file or assembly", StringComparison.Ordinal),
        _ => false,
    };

    private static ErrorMessage? BuiltIn(Exception exception)
    {
        if (IsAssemblyLoadFailure(exception))
        {
            // The unexpected path (with Copy diagnostics), not "couldn't be found, it may have been moved".
            return Unexpected;
        }

        switch (exception)
        {
            case FileNotFoundException { FileName: { Length: > 0 } path } when Path.GetFileName(path) is { Length: > 0 } name:
                return Critical(ProjectText.Format(ProjectText.ErrorFileNotFoundNamed, name));
            case FileNotFoundException:
                return Critical(ProjectText.ErrorFileNotFound);
            case DirectoryNotFoundException or DriveNotFoundException:
                return Critical(ProjectText.ErrorFolderNotFound);
            case PathTooLongException:
                return Critical(ProjectText.ErrorPathTooLong);
            case UnauthorizedAccessException:
                return Critical(ProjectText.ErrorAccessDenied);
            case OutOfMemoryException:
                return Critical(ProjectText.ErrorOutOfMemory);
        }

        return exception.HResult switch
        {
            FileNotFound => Critical(ProjectText.ErrorFileNotFound),
            PathNotFound or BadNetPath or InvalidDrive => Critical(ProjectText.ErrorFolderNotFound),
            AccessDenied or WriteProtect => Critical(ProjectText.ErrorAccessDenied),
            SharingViolation or LockViolation => Critical(ProjectText.ErrorFileInUse),
            DiskFull or HandleDiskFull => Critical(ProjectText.ErrorDiskFull),
            OutOfMemoryHResult or NotEnoughMemory => Critical(ProjectText.ErrorOutOfMemory),
            NotReady or DeviceNotExist or NetNameDeleted => Critical(ProjectText.ErrorDriveDisconnected),
            Crc => Critical(ProjectText.ErrorCannotRead),
            ClipboardCantOpen => Critical(ProjectText.ErrorClipboardBusy),
            _ => null,
        };
    }

    private static ErrorMessage Critical(string text) => new(InfoBarSeverity.Critical, text);
}
