using System.Globalization;

namespace TenLapse.Core.Ordering;

/// <summary>Which EXIF tag a capture time came from.</summary>
public enum CaptureTimeSource
{
    /// <summary><c>DateTimeOriginal</c>: when the shutter fired. The first choice.</summary>
    DateTimeOriginal,

    /// <summary><c>DateTimeDigitized</c>: used only when the file has no usable <c>DateTimeOriginal</c>.</summary>
    DateTimeDigitized,
}

/// <summary>When a photo was taken, as the camera wrote it.</summary>
/// <param name="Time">
/// The camera's wall-clock time with the sub-second part, <see cref="DateTimeKind.Unspecified"/> (EXIF has no zone
/// in the main tag). This is what <see cref="Projects.Frame.CaptureTime"/> holds and what the sort compares.
/// </param>
/// <param name="UtcOffset">
/// The offset from UTC (<c>OffsetTimeOriginal</c> or <c>OffsetTimeDigitized</c>) when the camera wrote one, else null.
/// Most cameras do not. Store it in <see cref="Projects.Frame.CaptureUtcOffset"/> next to the time: the sort and the gap
/// markers use the UTC instant only when every timed frame has an offset, and the wall clock otherwise, because mixing
/// the two would put one camera's photos hours away from another's.
/// </param>
/// <param name="Source">Which tag <paramref name="Time"/> came from.</param>
public readonly record struct CaptureTimeReading(DateTime Time, TimeSpan? UtcOffset, CaptureTimeSource Source);

/// <summary>
/// Reads when a photo was taken from its Exif block: <c>DateTimeOriginal</c> plus
/// <c>SubSecTimeOriginal</c> and, when present, <c>OffsetTimeOriginal</c>; <c>DateTimeDigitized</c> (with its own
/// sub-seconds and offset) only when the original is missing or unusable. It never falls back to file times:
/// a copied or exported file has a new write time but the same capture time, and a file without EXIF should be
/// ordered by name, not by when it was copied.
/// </summary>
/// <remarks>
/// <para>
/// Metadata only: the reader looks at the container's metadata boxes and segments and never decodes pixels.
/// It never throws: a file that is missing, locked, truncated, not an image or has no usable time gives null.
/// </para>
/// <para>
/// Bounded (#84): it reads the six time tags with its own walkers (<see cref="ExifContainers"/>) and checks every
/// count and length against the bytes that are really there before it reads, so a damaged or hostile file costs at
/// most about 2 MB (a HEIF meta box and a split Exif item), however large its fields claim to be. MetadataExtractor,
/// used before, trusted ISO base media counts and allocated up to 15 GB on a two-byte corruption of a HEIC.
/// </para>
/// <para>
/// Formats are found by content, not by extension. JPEG, TIFF and the TIFF-based RAW formats (CR2, NEF, ARW, DNG,
/// ORF, RW2, PEF) keep the time in an Exif IFD; PNG in an <c>eXIf</c> chunk; WebP in an <c>EXIF</c> chunk; HEIC, HEIF
/// and AVIF in an Exif item of the ISO base media file; CR3 in Canon's <c>CMT2</c> box; RAF in its embedded JPEG.
/// </para>
/// <para>
/// <b>Verified and not.</b> JPEG and HEIC are checked against real files (JPEG and HEIC encoded by Windows Imaging), and
/// the TIFF layout behind DNG, ARW and NEF against hand-built containers. <b>Unverified:</b> real camera RAW (CR2, NEF, ARW, DNG,
/// ORF, RW2, PEF), CR3, RAF, PNG and WebP follow their published layouts but no camera or app file has been tried; the first check is Sven's
/// sample photos (#30). A format that does not yield a time gives null, which makes the default sort fall back to name order.
/// </para>
/// </remarks>
public static class CaptureTimeReader
{
    private const int FileBufferSize = 8 * 1024;

    /// <summary>Reads the capture time of the photo at <paramref name="path"/>; null when there is none or the file cannot be read.</summary>
    public static CaptureTimeReading? ReadFile(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                FileBufferSize,
                FileOptions.None);
            return Read(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the capture time from a photo's bytes. The stream must be seekable and positioned at the start of the
    /// file; it is left open. Null when there is no usable time, the stream is not seekable or the data is damaged.
    /// </summary>
    public static CaptureTimeReading? Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            return stream.CanSeek ? ExifContainers.Read(ByteWindow.Over(stream)) : null;
        }
        catch (Exception)
        {
            // The walkers check every bound before they read, so this is for the stream itself failing (a file that
            // goes away, a network share that drops): one bad file must not fail a 5,000-file batch.
            return null;
        }
    }

    /// <summary>The capture time from one Exif block's tags: the original time first, the digitized time only when there is no usable original.</summary>
    internal static CaptureTimeReading? FromTags(ExifTimeTags tags)
    {
        if (TryParseExifTime(tags.Original, out var original))
        {
            return new CaptureTimeReading(
                original.AddTicks(ParseSubSeconds(tags.SubSecOriginal)),
                ParseOffset(tags.OffsetOriginal),
                CaptureTimeSource.DateTimeOriginal);
        }

        if (TryParseExifTime(tags.Digitized, out var digitized))
        {
            return new CaptureTimeReading(
                digitized.AddTicks(ParseSubSeconds(tags.SubSecDigitized)),
                ParseOffset(tags.OffsetDigitized),
                CaptureTimeSource.DateTimeDigitized);
        }

        return null;
    }

    /// <summary>
    /// Parses an Exif date and time, <c>yyyy:MM:dd HH:mm:ss</c>. Some firmware writes <c>-</c> or <c>/</c> between the
    /// date parts or a <c>T</c> between date and time; an unset clock (<c>0000:00:00 00:00:00</c>, blanks) is not a time.
    /// </summary>
    internal static bool TryParseExifTime(string? text, out DateTime time)
    {
        time = default;
        if (text is null)
        {
            return false;
        }

        var s = text.AsSpan().Trim().TrimEnd('\0');
        if (s.Length < 19
            || !IsSeparator(s[4]) || !IsSeparator(s[7]) || s[10] is not (' ' or 'T') || s[13] != ':' || s[16] != ':'
            || !TryTwo(s, 0, 2, out var century) || !TryTwo(s, 2, 2, out var yearInCentury)
            || !TryTwo(s, 5, 2, out var month) || !TryTwo(s, 8, 2, out var day)
            || !TryTwo(s, 11, 2, out var hour) || !TryTwo(s, 14, 2, out var minute) || !TryTwo(s, 17, 2, out var second))
        {
            return false;
        }

        var year = (century * 100) + yearInCentury;
        if (year is < 1 or > 9999 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)
            || hour > 23 || minute > 59 || second > 59)
        {
            return false;
        }

        time = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
        return true;

        static bool IsSeparator(char c) => c is ':' or '-' or '/' or '.';

        static bool TryTwo(ReadOnlySpan<char> span, int start, int length, out int value)
        {
            value = 0;
            for (var i = start; i < start + length; i++)
            {
                if (span[i] is not (>= '0' and <= '9'))
                {
                    return false;
                }

                value = (value * 10) + (span[i] - '0');
            }

            return true;
        }
    }

    /// <summary>
    /// The sub-second part as ticks. The tag is the digits after the decimal point: <c>"03"</c> is 0.03 s and
    /// <c>"3"</c> is 0.3 s. Anything that is not digits (or nothing) is zero; digits past the seventh are dropped.
    /// </summary>
    internal static long ParseSubSeconds(string? text)
    {
        if (text is null)
        {
            return 0;
        }

        var s = text.AsSpan().Trim().TrimEnd('\0');
        long ticks = 0;
        var scale = TimeSpan.TicksPerSecond / 10;
        foreach (var c in s)
        {
            if (c is not (>= '0' and <= '9'))
            {
                return 0;
            }

            if (scale == 0)
            {
                break;
            }

            ticks += (c - '0') * scale;
            scale /= 10;
        }

        return ticks;
    }

    /// <summary>The UTC offset from <c>OffsetTime*</c> (<c>+02:00</c>, <c>-05:30</c>, <c>Z</c>), or null when absent or unreadable.</summary>
    internal static TimeSpan? ParseOffset(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var s = text.AsSpan().Trim().TrimEnd('\0');
        if (s is "Z" or "z")
        {
            return TimeSpan.Zero;
        }

        if (s.Length is not (6 or 5) || s[0] is not ('+' or '-'))
        {
            return null;
        }

        var colon = s.Length == 6;
        if (colon && s[3] != ':')
        {
            return null;
        }

        var minutesAt = colon ? 4 : 3;
        if (!int.TryParse(s[1..3], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(s.Slice(minutesAt, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || hours > 14 || minutes > 59)
        {
            return null;
        }

        var offset = new TimeSpan(hours, minutes, 0);
        return s[0] == '-' ? -offset : offset;
    }
}
