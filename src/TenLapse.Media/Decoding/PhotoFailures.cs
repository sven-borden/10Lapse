using System.Globalization;
using TenLapse.Core.Projects;

namespace TenLapse.Media.Decoding;

/// <summary>Turns what WIC, WinRT and the file system throw into a <see cref="PhotoFailure"/>, by HRESULT.</summary>
/// <remarks>
/// By HRESULT on any exception type, as the export's <c>ExportFailures</c> does: WinRT raises
/// <see cref="System.Runtime.InteropServices.COMException"/>, but also <see cref="UnauthorizedAccessException"/>,
/// <see cref="FileNotFoundException"/>, <see cref="OutOfMemoryException"/> and others for the same codes.
/// </remarks>
internal static class PhotoFailures
{
    /// <summary>Where the failure happened, which changes what a missing component means.</summary>
    public enum Stage
    {
        /// <summary>Opening the file or picking a decoder for it.</summary>
        Open,

        /// <summary>Decoding pixels with the decoder WIC picked.</summary>
        Decode,
    }

    // HRESULT -> (name for the log, reason). Win32 errors appear as HRESULT_FROM_WIN32 (0x8007xxxx) and as the
    // structured storage codes (0x8003xxxx) the WinRT stream adapters use.
    private static readonly Dictionary<int, (string Name, SkipReason Reason)> _known = new()
    {
        [Code(0x80070002)] = ("ERROR_FILE_NOT_FOUND", SkipReason.FileNotFound),
        [Code(0x80070003)] = ("ERROR_PATH_NOT_FOUND", SkipReason.FileNotFound),
        [Code(0x8007000F)] = ("ERROR_INVALID_DRIVE", SkipReason.FileNotFound),
        [Code(0x80070015)] = ("ERROR_NOT_READY", SkipReason.FileNotFound),
        [Code(0x80070035)] = ("ERROR_BAD_NETPATH", SkipReason.FileNotFound),
        [Code(0x80070043)] = ("ERROR_BAD_NET_NAME", SkipReason.FileNotFound),
        [Code(0x8007012F)] = ("ERROR_DELETE_PENDING", SkipReason.FileNotFound),
        [Code(0x80030002)] = ("STG_E_FILENOTFOUND", SkipReason.FileNotFound),
        [Code(0x80030003)] = ("STG_E_PATHNOTFOUND", SkipReason.FileNotFound),
        [Code(0x80070005)] = ("E_ACCESSDENIED", SkipReason.AccessDenied),
        [Code(0x80030005)] = ("STG_E_ACCESSDENIED", SkipReason.AccessDenied),
        [Code(0x80070020)] = ("ERROR_SHARING_VIOLATION", SkipReason.FileInUse),
        [Code(0x80070021)] = ("ERROR_LOCK_VIOLATION", SkipReason.FileInUse),
        [Code(0x80030020)] = ("STG_E_SHAREVIOLATION", SkipReason.FileInUse),
        [Code(0x80030021)] = ("STG_E_LOCKVIOLATION", SkipReason.FileInUse),
        [Code(0x80070026)] = ("ERROR_HANDLE_EOF", SkipReason.Truncated),
        [Code(0x8003001E)] = ("STG_E_READFAULT", SkipReason.Truncated),
        [Code(0x88982F72)] = ("WINCODEC_ERR_STREAMREAD", SkipReason.Truncated),
        [Code(0x88982F73)] = ("WINCODEC_ERR_STREAMNOTAVAILABLE", SkipReason.Truncated),
        [Code(0x8007000E)] = ("E_OUTOFMEMORY", SkipReason.ImageTooLarge),
        [Code(0x88982F51)] = ("WINCODEC_ERR_IMAGESIZEOUTOFRANGE", SkipReason.ImageTooLarge),
        [Code(0x88982F50)] = ("WINCODEC_ERR_COMPONENTNOTFOUND", SkipReason.UnsupportedFormat),
        [Code(0x88982F07)] = ("WINCODEC_ERR_UNKNOWNIMAGEFORMAT", SkipReason.UnsupportedFormat),
        [Code(0x88982F0B)] = ("WINCODEC_ERR_UNSUPPORTEDVERSION", SkipReason.UnsupportedFormat),
        [Code(0x88982F80)] = ("WINCODEC_ERR_UNSUPPORTEDPIXELFORMAT", SkipReason.UnsupportedFormat),
        [Code(0x88982F81)] = ("WINCODEC_ERR_UNSUPPORTEDOPERATION", SkipReason.UnsupportedFormat),
        [Code(0x80040154)] = ("REGDB_E_CLASSNOTREG", SkipReason.UnsupportedFormat),
        [Code(0xC00D5212)] = ("MF_E_TOPO_CODEC_NOT_FOUND", SkipReason.UnsupportedFormat),
        [Code(0x88982F60)] = ("WINCODEC_ERR_BADIMAGE", SkipReason.DecodeFailed),
        [Code(0x88982F61)] = ("WINCODEC_ERR_BADHEADER", SkipReason.DecodeFailed),
        [Code(0x88982F62)] = ("WINCODEC_ERR_FRAMEMISSING", SkipReason.DecodeFailed),
        [Code(0x88982F63)] = ("WINCODEC_ERR_BADMETADATAHEADER", SkipReason.DecodeFailed),
        [Code(0x88982F70)] = ("WINCODEC_ERR_BADSTREAMDATA", SkipReason.DecodeFailed),
        [Code(0x88982F46)] = ("WINCODEC_ERR_CODECTOOMANYSCANLINES", SkipReason.DecodeFailed),
        [Code(0x88982F8F)] = ("WINCODEC_ERR_UNEXPECTEDSIZE", SkipReason.DecodeFailed),
        [Code(0x88982F95)] = ("WINCODEC_ERR_INVALIDPROGRESSIVELEVEL", SkipReason.DecodeFailed),
        [Code(0x88982F96)] = ("WINCODEC_ERR_INVALIDJPEGSCANINDEX", SkipReason.DecodeFailed),
        [Code(0x88982F48)] = ("WINCODEC_ERR_INTERNALERROR", SkipReason.DecodeFailed),
    };

    // A missing component while decoding a format that comes from a Store extension: the decoder is there but a
    // codec it needs is not (a HEIC photo without the HEVC decoder).
    private static readonly HashSet<int> _missingComponent =
        [Code(0x88982F50), Code(0x80040154), Code(0xC00D5212)];

    /// <summary>The failure for an exception thrown at <paramref name="stage"/>.</summary>
    /// <param name="decoderFor">The installed decoder for an extension; <see cref="PhotoCodecs.DecoderFor"/> unless a test stands in for a PC without one.</param>
    public static PhotoFailure Classify(Exception exception, string path, Stage stage, Func<string, PhotoDecoderInfo?>? decoderFor = null)
    {
        var format = KnownPhotoFormats.ForPath(path);
        var hresult = exception.HResult;
        var (name, reason) = _known.TryGetValue(hresult, out var known)
            ? known
            : exception switch
            {
                OutOfMemoryException or InsufficientMemoryException => ("E_OUTOFMEMORY", SkipReason.ImageTooLarge),
                FileNotFoundException or DirectoryNotFoundException or DriveNotFoundException => ("not found", SkipReason.FileNotFound),
                UnauthorizedAccessException => ("access denied", SkipReason.AccessDenied),
                EndOfStreamException => ("end of stream", SkipReason.Truncated),
                _ => ("unknown", SkipReason.Other),
            };

        if (reason == SkipReason.UnsupportedFormat && format?.Extension is not null && NeedsExtension(path, stage, hresult, decoderFor ?? PhotoCodecs.DecoderFor))
        {
            reason = SkipReason.NeedsExtension;
        }

        var detail = string.Create(
            CultureInfo.InvariantCulture,
            $"{stage}: {name} (0x{hresult:X8}, {exception.GetType().Name}): {exception.Message}");
        return new PhotoFailure(path, reason, detail, exception, format);
    }

    /// <summary>A failure the decoder found itself, without an exception.</summary>
    public static PhotoFailure Found(string path, SkipReason reason, string detail) =>
        new(path, reason, detail, exception: null, KnownPhotoFormats.ForPath(path));

    // No installed decoder claims the extension (the HEIF or RAW extension is missing), or the one that does is
    // missing a codec it needs. A format whose decoder is installed but which WIC did not recognize is just unreadable.
    private static bool NeedsExtension(string path, Stage stage, int hresult, Func<string, PhotoDecoderInfo?> decoderFor) =>
        decoderFor(Path.GetExtension(path)) is null || (stage == Stage.Decode && _missingComponent.Contains(hresult));

    private static int Code(uint value) => unchecked((int)value);
}
