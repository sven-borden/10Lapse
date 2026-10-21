namespace TenLapse.Core.Ordering;

/// <summary>A photo's exposure settings as the camera wrote them (#45); null where its Exif block lacks one.</summary>
/// <param name="ExposureTime">The shutter time in seconds.</param>
/// <param name="FNumber">The aperture's f-number.</param>
/// <param name="Iso">The ISO sensitivity.</param>
public readonly record struct ExposureSettings(double? ExposureTime, double? FNumber, double? Iso);

/// <summary>
/// Reads a photo's exposure settings (shutter time, f-number, ISO) from its Exif IFD, for deflicker's exposure steps
/// (#45). The same bounded walk and the same formats as <see cref="CaptureTimeReader"/> (#84): metadata only, never
/// throws, null for a file without them.
/// </summary>
public static class ExposureReader
{
    private const int FileBufferSize = 8 * 1024;

    /// <summary>Reads the exposure settings of the photo at <paramref name="path"/>; null when there are none or the file cannot be read.</summary>
    public static ExposureSettings? ReadFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileBufferSize, FileOptions.None);
            return Read(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Reads the exposure settings from a photo's bytes: a seekable stream at the start of the file, left open.</summary>
    public static ExposureSettings? Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            return stream.CanSeek ? ExifContainers.ReadExposure(ByteWindow.Over(stream)) : null;
        }
        catch (Exception)
        {
            // As in CaptureTimeReader: the walkers check their bounds, so this is the stream itself failing.
            return null;
        }
    }
}
