using TenLapse.Core.Projects;
using TenLapse.Media.Encoding;

namespace TenLapse.Media.Export;

/// <summary>Why an export failed, in categories the app turns into a sentence that says what to do (#22).</summary>
public enum VideoExportFailure
{
    /// <summary>No encoder for the codec (H.264 or HEVC) could be loaded.</summary>
    EncoderUnavailable,

    /// <summary>The encoder refused the size, frame rate or bitrate.</summary>
    SettingsRejected,

    /// <summary>The encoder failed while writing, for a reason none of the other categories names.</summary>
    EncoderFailed,

    /// <summary>The encoder stopped taking frames.</summary>
    EncoderStalled,

    /// <summary>The output folder cannot be written to (access denied, write-protected).</summary>
    OutputFolderNotWritable,

    /// <summary>Another app has the output file open.</summary>
    OutputFileInUse,

    /// <summary>The output folder, drive or network share does not exist or is not reachable.</summary>
    OutputFolderMissing,

    /// <summary>The output disk is full.</summary>
    DiskFull,

    /// <summary>
    /// No graphics device can run the export: no hardware Direct3D 11 device with video support
    /// (only the Microsoft Basic Render Driver, a remote session, DXGI_ERROR_UNSUPPORTED), or creating it failed.
    /// </summary>
    NoGraphicsDevice,

    /// <summary>The graphics driver reset or the GPU was removed during the export (DXGI_ERROR_DEVICE_REMOVED and kin).</summary>
    GraphicsDeviceLost,

    /// <summary>Not enough system or video memory.</summary>
    OutOfMemory,

    /// <summary>
    /// A frame's photo could not be loaded: <see cref="VideoExportException.FrameIndex"/> and
    /// <see cref="VideoExportException.SourcePath"/> say which; the inner exception is the source's own, a
    /// <see cref="Decoding.PhotoDecodeException"/> (with its <see cref="SkipReason"/>) for photo files.
    /// </summary>
    FrameUnreadable,

    /// <summary>Drawing a frame failed on the graphics device for a reason none of the other categories names.</summary>
    DrawingFailed,
}

/// <summary>An export failed for a reason the app can explain; <see cref="Failure"/> says which.</summary>
public sealed class VideoExportException : Exception
{
    /// <summary>Creates the exception.</summary>
    public VideoExportException(string message, VideoExportFailure failure, Exception? innerException = null)
        : base(message, innerException) => Failure = failure;

    /// <summary>What went wrong, for the message the app shows (#22).</summary>
    public VideoExportFailure Failure { get; }

    /// <summary>The frame (0-based, in playback order) being loaded, drawn or encoded when it failed; null for a failure of the whole export.</summary>
    public int? FrameIndex { get; init; }

    /// <summary>The photo file of <see cref="FrameIndex"/>, when the source knows it.</summary>
    public string? SourcePath { get; init; }

    /// <summary>
    /// What <c>ID3D11Device::GetDeviceRemovedReason</c> answered when the failure was classified as
    /// <see cref="VideoExportFailure.GraphicsDeviceLost"/> (DXGI_ERROR_DEVICE_HUNG, _REMOVED, _RESET, DXGI_ERROR_DRIVER_INTERNAL_ERROR), for the log.
    /// </summary>
    public int? DeviceRemovedReason { get; init; }
}

/// <summary>
/// Turns a failure from native code into a <see cref="VideoExportException"/> by its HRESULT, whatever
/// exception type the interop raised it as: CsWin32's <c>ThrowOnFailure</c> and COM interop throw
/// <see cref="UnauthorizedAccessException"/>, <see cref="DirectoryNotFoundException"/>,
/// <see cref="ArgumentException"/>, <see cref="OutOfMemoryException"/> and others, not only
/// <see cref="System.Runtime.InteropServices.COMException"/>.
/// </summary>
internal static class ExportFailures
{
    /// <summary>
    /// True for an exception that comes from a mistake in our own code: it must surface as the bug it is,
    /// not as an export failure the app explains away. <see cref="InvalidCastException"/> counts: a failed
    /// interface cast is our wrong assumption (it was the STA bug of #8).
    /// </summary>
    public static bool IsBug(Exception exception) => exception switch
    {
        ArgumentNullException or NullReferenceException or InvalidCastException or IndexOutOfRangeException => true,

        // Ours carry a COR_E_* HRESULT (facility 0x13); the same types mapped from a native HRESULT do not
        // (E_ILLEGAL_METHOD_CALL arrives as InvalidOperationException).
        InvalidOperationException or NotSupportedException => ((uint)exception.HResult >> 16) == 0x8013,
        _ => false,
    };

    /// <summary>True when <see cref="Classify"/> should handle the exception: not ours already, not a cancellation, not a bug.</summary>
    public static bool ShouldClassify(Exception exception) =>
        exception is not (VideoExportException or OperationCanceledException) && !IsBug(exception);

    /// <summary>The category an HRESULT names wherever it occurs, or null.</summary>
    /// <param name="setup">True while the encoder is being set up, where an invalid argument means the settings were refused.</param>
    public static VideoExportFailure? FromHResult(int hresult, bool setup = false) => unchecked((uint)hresult) switch
    {
        // E_ACCESSDENIED, ERROR_WRITE_PROTECT
        0x80070005 or 0x80070013 => VideoExportFailure.OutputFolderNotWritable,

        // ERROR_SHARING_VIOLATION, ERROR_LOCK_VIOLATION, ERROR_USER_MAPPED_FILE
        0x80070020 or 0x80070021 or 0x800704C8 => VideoExportFailure.OutputFileInUse,

        // ERROR_PATH_NOT_FOUND, ERROR_INVALID_DRIVE, ERROR_NOT_READY, ERROR_BAD_NETPATH, ERROR_BAD_NET_NAME, ERROR_DEV_NOT_EXIST
        0x80070003 or 0x8007000F or 0x80070015 or 0x80070035 or 0x80070043 or 0x80070037 => VideoExportFailure.OutputFolderMissing,

        // ERROR_DISK_FULL, ERROR_HANDLE_DISK_FULL, STG_E_MEDIUMFULL
        0x80070070 or 0x80070027 or 0x80030070 => VideoExportFailure.DiskFull,

        // DXGI_ERROR_UNSUPPORTED, DXGI_ERROR_NOT_CURRENTLY_AVAILABLE, DXGI_ERROR_SDK_COMPONENT_MISSING
        0x887A0004 or 0x887A0022 or 0x887A002D => VideoExportFailure.NoGraphicsDevice,

        // DXGI_ERROR_DEVICE_REMOVED, _HUNG, _RESET, DXGI_ERROR_DRIVER_INTERNAL_ERROR, D2DERR_RECREATE_TARGET
        0x887A0005 or 0x887A0006 or 0x887A0007 or 0x887A0020 or 0x8899000C => VideoExportFailure.GraphicsDeviceLost,

        // E_OUTOFMEMORY, ERROR_NOT_ENOUGH_MEMORY, ERROR_OUTOFMEMORY
        0x8007000E or 0x80070008 => VideoExportFailure.OutOfMemory,

        // MF_E_TOPO_CODEC_NOT_FOUND, MF_E_TRANSFORM_NOT_POSSIBLE_FOR_CURRENT_MEDIATYPE_COMBINATION
        0xC00D5212 or 0xC00D6D60 => VideoExportFailure.EncoderUnavailable,

        // MF_E_INVALIDMEDIATYPE, MF_E_UNSUPPORTED_RATE
        0xC00D36B4 or 0xC00D3E9D => VideoExportFailure.SettingsRejected,

        // E_INVALIDARG
        0x80070057 when setup => VideoExportFailure.SettingsRejected,
        _ => null,
    };

    /// <summary>Wraps a native failure in the category its HRESULT names, or in <paramref name="fallback"/>.</summary>
    /// <param name="format">The codec being written, which the message names.</param>
    public static VideoExportException Classify(Exception exception, VideoExportFailure fallback, VideoFormat format, bool setup = false) =>
        Wrap(exception, FromHResult(exception.HResult, setup) ?? fallback, format);

    /// <summary>Wraps <paramref name="exception"/> as <paramref name="failure"/>, keeping it as the inner exception.</summary>
    /// <param name="format">The codec being written, which the message names.</param>
    public static VideoExportException Wrap(Exception exception, VideoExportFailure failure, VideoFormat format) =>
        new($"{Summary(failure, format)}: {exception.Message}", failure, exception);

    /// <summary>
    /// A plain English summary for logs and tests, naming the codec (H.264 or HEVC) where the failure is the
    /// encoder's; the app words its own sentences from <see cref="VideoExportFailure"/>.
    /// </summary>
    public static string Summary(VideoExportFailure failure, VideoFormat format) => failure switch
    {
        VideoExportFailure.EncoderUnavailable => $"No {EncoderText.CodecName(format)} encoder could be loaded",
        VideoExportFailure.SettingsRejected => $"The {EncoderText.CodecName(format)} encoder refused these settings",
        VideoExportFailure.EncoderStalled => $"The {EncoderText.CodecName(format)} encoder stopped taking frames",
        VideoExportFailure.OutputFolderNotWritable => "The output folder cannot be written to",
        VideoExportFailure.OutputFileInUse => "Another app has the output file open",
        VideoExportFailure.OutputFolderMissing => "The output folder does not exist",
        VideoExportFailure.DiskFull => "The disk is full",
        VideoExportFailure.NoGraphicsDevice => "No graphics device can run the export",
        VideoExportFailure.GraphicsDeviceLost => "The graphics driver reset during the export",
        VideoExportFailure.OutOfMemory => "There is not enough memory for the export",
        VideoExportFailure.FrameUnreadable => "A frame's photo could not be loaded",
        VideoExportFailure.DrawingFailed => "Drawing a frame failed",
        _ => $"The {EncoderText.CodecName(format)} encoder failed",
    };

    /// <summary>The DXGI name of a device removed reason, for messages: "DXGI_ERROR_DEVICE_HUNG", or the HRESULT in hex.</summary>
    public static string DeviceRemovedName(int reason) => unchecked((uint)reason) switch
    {
        0x887A0005 => "DXGI_ERROR_DEVICE_REMOVED",
        0x887A0006 => "DXGI_ERROR_DEVICE_HUNG",
        0x887A0007 => "DXGI_ERROR_DEVICE_RESET",
        0x887A0020 => "DXGI_ERROR_DRIVER_INTERNAL_ERROR",
        0x887A0001 => "DXGI_ERROR_INVALID_CALL",
        var other => $"0x{other:X8}",
    };
}
