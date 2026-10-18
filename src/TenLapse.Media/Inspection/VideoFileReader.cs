using System.Runtime.InteropServices;
using TenLapse.Core.Timing;
using Windows.Win32;
using Windows.Win32.Media.MediaFoundation;

namespace TenLapse.Media.Inspection;

/// <summary>
/// Reads a video file back with the Media Foundation source reader: the frame
/// count, every sample's presentation time and duration, the container
/// duration, and optionally every decoded frame. Used to check what an export
/// wrote (#8, #32); it needs no FFmpeg.
/// </summary>
public static class VideoFileReader
{
    private const uint FirstVideoStream = unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_FIRST_VIDEO_STREAM);
    private const uint AllStreams = unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_ALL_STREAMS);
    private const uint MediaSource = unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_MEDIASOURCE);

    /// <summary>
    /// Reads the first video stream's compressed samples without decoding them,
    /// so it is fast even for long 4K files.
    /// </summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="COMException">Media Foundation cannot open or parse the file.</exception>
    public static VideoFileInfo Read(string path)
    {
        var fullPath = ExistingPath(path);
        using var session = new MediaFoundationSession();
        var reader = CreateReader(fullPath, null);
        try
        {
            SelectFirstVideoStream(reader);
            reader.GetCurrentMediaType(FirstVideoStream, out var type);
            string codec;
            int width, height;
            Fraction? rate;
            try
            {
                codec = CodecName(type);
                (width, height) = FrameSize(type);
                rate = FrameRate(type);
            }
            finally
            {
                Release(type);
            }

            var duration = PresentationDuration(reader);
            var samples = new List<(long Time, long Duration)>();
            while (true)
            {
                reader.ReadSample(FirstVideoStream, 0, out _, out var flags, out _, out var sample);
                if (sample is not null)
                {
                    try
                    {
                        sample.GetSampleTime(out var time);
                        samples.Add((time, SampleDuration(sample)));
                    }
                    finally
                    {
                        Release(sample);
                    }
                }

                ThrowOnStreamError(flags, fullPath);
                if (Has(flags, MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ENDOFSTREAM))
                {
                    break;
                }
            }

            // Compressed samples arrive in decode order; with B-frames that is not presentation order.
            samples.Sort((left, right) => left.Time.CompareTo(right.Time));
            return new VideoFileInfo(
                codec,
                width,
                height,
                rate,
                duration,
                [.. samples.Select(sample => sample.Time)],
                [.. samples.Select(sample => sample.Duration)]);
        }
        finally
        {
            Release(reader);
        }
    }

    /// <summary>The presentation time of the first video sample in decode order, or null if there is none. Reads one sample.</summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="COMException">Media Foundation cannot open or parse the file.</exception>
    public static long? FirstSampleTime(string path)
    {
        var fullPath = ExistingPath(path);
        using var session = new MediaFoundationSession();
        var reader = CreateReader(fullPath, null);
        try
        {
            SelectFirstVideoStream(reader);
            while (true)
            {
                reader.ReadSample(FirstVideoStream, 0, out _, out var flags, out _, out var sample);
                if (sample is not null)
                {
                    try
                    {
                        sample.GetSampleTime(out var time);
                        return time;
                    }
                    finally
                    {
                        Release(sample);
                    }
                }

                ThrowOnStreamError(flags, fullPath);
                if (Has(flags, MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ENDOFSTREAM))
                {
                    return null;
                }
            }
        }
        finally
        {
            Release(reader);
        }
    }

    /// <summary>
    /// Decodes the first video stream to BGRA and hands each frame, in
    /// presentation order, to <paramref name="handler"/>. Software decoding: about
    /// real time for 4K, instant for small test clips.
    /// </summary>
    /// <returns>The number of frames decoded.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="COMException">Media Foundation cannot open or decode the file.</exception>
    public static unsafe int ReadFrames(string path, DecodedFrameHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var fullPath = ExistingPath(path);
        using var session = new MediaFoundationSession();

        PInvoke.MFCreateAttributes(out var attributes, 1).ThrowOnFailure();
        IMFSourceReader reader;
        try
        {
            // Lets the reader add the colour converter from the decoder's NV12 to RGB32.
            attributes.SetUINT32(PInvoke.MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, 1);
            reader = CreateReader(fullPath, attributes);
        }
        finally
        {
            Release(attributes);
        }

        byte[]? flipped = null;
        try
        {
            SelectFirstVideoStream(reader);
            PInvoke.MFCreateMediaType(out var wanted).ThrowOnFailure();
            try
            {
                wanted.SetGUID(PInvoke.MF_MT_MAJOR_TYPE, PInvoke.MFMediaType_Video);
                wanted.SetGUID(PInvoke.MF_MT_SUBTYPE, PInvoke.MFVideoFormat_RGB32);
                reader.SetCurrentMediaType(FirstVideoStream, wanted);
            }
            finally
            {
                Release(wanted);
            }

            var layout = CurrentLayout(reader);
            var index = 0;
            while (true)
            {
                reader.ReadSample(FirstVideoStream, 0, out _, out var flags, out _, out var sample);
                ThrowOnStreamError(flags, fullPath);
                if (Has(flags, MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED))
                {
                    layout = CurrentLayout(reader);
                }

                if (sample is not null)
                {
                    try
                    {
                        sample.GetSampleTime(out var time);
                        sample.ConvertToContiguousBuffer(out var buffer);
                        try
                        {
                            DeliverFrame(buffer, layout, index, time, handler, ref flipped);
                        }
                        finally
                        {
                            Release(buffer);
                        }
                    }
                    finally
                    {
                        Release(sample);
                    }

                    index++;
                }

                if (Has(flags, MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ENDOFSTREAM))
                {
                    return index;
                }
            }
        }
        finally
        {
            Release(reader);
        }
    }

    private static unsafe void DeliverFrame(
        IMFMediaBuffer buffer,
        FrameLayout layout,
        int index,
        long time,
        DecodedFrameHandler handler,
        ref byte[]? flipped)
    {
        byte* first;
        int pitch;
        var twoD = buffer as IMF2DBuffer;
        if (twoD is not null)
        {
            // Lock2D gives the top row and the real pitch, negative for a bottom-up buffer.
            byte* scanline0;
            twoD.Lock2D(&scanline0, out var lockedPitch);
            first = scanline0;
            pitch = lockedPitch;
        }
        else
        {
            byte* start;
            buffer.Lock(&start, null, null);
            pitch = layout.DefaultStride;
            first = pitch >= 0 ? start : start + ((long)(layout.CodedHeight - 1) * -pitch);
        }

        try
        {
            var top = first + ((long)layout.OffsetY * pitch) + ((long)layout.OffsetX * 4);
            var rowBytes = layout.Width * 4;
            ReadOnlySpan<byte> pixels;
            int stride;
            if (pitch > 0)
            {
                stride = pitch;
                pixels = new ReadOnlySpan<byte>(top, ((layout.Height - 1) * pitch) + rowBytes);
            }
            else
            {
                stride = rowBytes;
                flipped ??= new byte[rowBytes * layout.Height];
                if (flipped.Length < rowBytes * layout.Height)
                {
                    flipped = new byte[rowBytes * layout.Height];
                }

                for (var row = 0; row < layout.Height; row++)
                {
                    new ReadOnlySpan<byte>(top + ((long)row * pitch), rowBytes).CopyTo(flipped.AsSpan(row * rowBytes));
                }

                pixels = flipped.AsSpan(0, rowBytes * layout.Height);
            }

            handler(new DecodedVideoFrame(index, time, layout.Width, layout.Height, stride, pixels));
        }
        finally
        {
            if (twoD is not null)
            {
                twoD.Unlock2D();
            }
            else
            {
                buffer.Unlock();
            }
        }
    }

    private static string ExistingPath(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The video file does not exist.", fullPath);
        }

        return fullPath;
    }

    private static IMFSourceReader CreateReader(string fullPath, IMFAttributes? attributes)
    {
        PInvoke.MFCreateSourceReaderFromURL(fullPath, attributes, out var reader).ThrowOnFailure();
        return reader;
    }

    private static void SelectFirstVideoStream(IMFSourceReader reader)
    {
        reader.SetStreamSelection(AllStreams, false);
        reader.SetStreamSelection(FirstVideoStream, true);
    }

    private static long PresentationDuration(IMFSourceReader reader)
    {
        reader.GetPresentationAttribute(MediaSource, PInvoke.MF_PD_DURATION, out var value);
        try
        {
            return (long)value.uhVal;
        }
        finally
        {
            PInvoke.PropVariantClear(ref value);
        }
    }

    private static long SampleDuration(IMFSample sample)
    {
        try
        {
            sample.GetSampleDuration(out var duration);
            return duration;
        }
        catch (COMException)
        {
            // MF_E_NO_SAMPLE_DURATION: the container gives this sample no duration.
            return -1;
        }
    }

    private static string CodecName(IMFMediaType type)
    {
        type.GetGUID(PInvoke.MF_MT_SUBTYPE, out var subtype);
        // Video subtypes are FOURCC GUIDs: {XXXXXXXX-0000-0010-8000-00AA00389B71}, the FOURCC in the first 4 bytes.
        var bytes = subtype.ToByteArray();
        var baseGuid = new Guid("00000000-0000-0010-8000-00AA00389B71").ToByteArray();
        if (bytes.AsSpan(4).SequenceEqual(baseGuid.AsSpan(4)) && bytes[..4].All(b => b is >= 0x20 and < 0x7F))
        {
            return System.Text.Encoding.ASCII.GetString(bytes, 0, 4);
        }

        return subtype.ToString("B");
    }

    private static (int Width, int Height) FrameSize(IMFMediaType type)
    {
        type.GetUINT64(PInvoke.MF_MT_FRAME_SIZE, out var packed);
        return ((int)(packed >> 32), (int)(packed & 0xFFFF_FFFF));
    }

    private static Fraction? FrameRate(IMFMediaType type)
    {
        try
        {
            type.GetUINT64(PInvoke.MF_MT_FRAME_RATE, out var packed);
            var numerator = (long)(packed >> 32);
            var denominator = (long)(packed & 0xFFFF_FFFF);
            return numerator > 0 && denominator > 0 ? new Fraction(numerator, denominator) : null;
        }
        catch (Exception exception) when (exception is COMException or OverflowException)
        {
            return null;
        }
    }

    private static unsafe FrameLayout CurrentLayout(IMFSourceReader reader)
    {
        reader.GetCurrentMediaType(FirstVideoStream, out var type);
        try
        {
            var (codedWidth, codedHeight) = FrameSize(type);
            int defaultStride;
            try
            {
                type.GetUINT32(PInvoke.MF_MT_DEFAULT_STRIDE, out var stride);
                defaultStride = unchecked((int)stride);
            }
            catch (COMException)
            {
                defaultStride = codedWidth * 4;
            }

            // The decoder pads to whole macroblocks (360 rows become 368); the aperture is the visible part.
            try
            {
                var blob = new byte[sizeof(MFVideoArea)];
                type.GetBlob(PInvoke.MF_MT_MINIMUM_DISPLAY_APERTURE, blob);
                var area = MemoryMarshal.Read<MFVideoArea>(blob);
                if (area.Area.cx > 0 && area.Area.cy > 0)
                {
                    return new FrameLayout(area.OffsetX.value, area.OffsetY.value, area.Area.cx, area.Area.cy, codedHeight, defaultStride);
                }
            }
            catch (COMException)
            {
                // No aperture: the whole frame is visible.
            }

            return new FrameLayout(0, 0, codedWidth, codedHeight, codedHeight, defaultStride);
        }
        finally
        {
            Release(type);
        }
    }

    private static void ThrowOnStreamError(MF_SOURCE_READER_FLAG flags, string fullPath)
    {
        if (Has(flags, MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ERROR))
        {
            throw new InvalidDataException($"Media Foundation could not read the video stream of {fullPath}.");
        }
    }

    private static bool Has(uint flags, MF_SOURCE_READER_FLAG flag) => (flags & (uint)flag) != 0;

    private static bool Has(MF_SOURCE_READER_FLAG flags, MF_SOURCE_READER_FLAG flag) => (flags & flag) != 0;

    private static void ThrowOnStreamError(uint flags, string fullPath) => ThrowOnStreamError((MF_SOURCE_READER_FLAG)flags, fullPath);

    private static void Release(object? comObject)
    {
        // Release now, not at the next GC: decoded 4K samples come from a small pool the decoder waits on.
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.ReleaseComObject(comObject);
        }
    }

    private readonly record struct FrameLayout(int OffsetX, int OffsetY, int Width, int Height, int CodedHeight, int DefaultStride);
}
