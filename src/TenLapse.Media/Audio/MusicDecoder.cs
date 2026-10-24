using System.Runtime.InteropServices;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Inspection;
using Windows.Win32;
using Windows.Win32.Media.MediaFoundation;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Variant;

namespace TenLapse.Media.Audio;

/// <summary>
/// Decodes the first audio stream of a file with the Media Foundation source reader (#50): whatever Windows can play
/// (MP3, AAC and M4A, WAV, WMA, FLAC), as interleaved stereo float samples at the file's own rate, from a point in it.
/// One mono channel plays on both sides; more than two keep the front pair with the centre mixed in.
/// </summary>
/// <remarks>
/// The reader is asked for PCM by subtype alone, which every Windows audio decoder gives without a converter; the bit
/// depth it settles on (8, 16, 24 or 32 bits, or float) is turned into floats here. Not thread-safe; one thread at a time.
/// </remarks>
internal sealed unsafe class MusicDecoder : IDisposable
{
    private const uint FirstAudioStream = unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_FIRST_AUDIO_STREAM);
    private const uint AllStreams = unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_ALL_STREAMS);
    private const uint MediaSource = unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_MEDIASOURCE);

    // The centre channel's share in each side of the stereo mix of a file with more than two channels (-3 dB).
    private const float CentreShare = 0.70710678f;

    private readonly MediaFoundationSession _session = new();
    private readonly IMFSourceReader _reader;
    private readonly string _path;
    private readonly long _start;
    private Format _format;

    // Decoded stereo frames not handed out yet, and frames still to drop after a seek that landed early.
    private float[] _pending = [];
    private int _pendingStart;
    private int _pendingCount;
    private long _skip;
    private bool _first = true;
    private bool _ended;
    private bool _disposed;

    /// <summary>Opens <paramref name="path"/> and moves to <paramref name="start"/> into it.</summary>
    /// <exception cref="InvalidDataException">The file has no audio stream Windows can decode, or cannot be read (the inner exception says why).</exception>
    public MusicDecoder(string path, TimeSpan start = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(start, TimeSpan.Zero);
        _path = Path.GetFullPath(path);
        _start = start.Ticks;
        try
        {
            if (!File.Exists(_path))
            {
                throw new FileNotFoundException("The music file does not exist.", _path);
            }

            PInvoke.MFCreateSourceReaderFromURL(_path, null, out _reader).ThrowOnFailure();
            _reader.SetStreamSelection(AllStreams, false);
            _reader.SetStreamSelection(FirstAudioStream, true);
            AskForPcm();
            _format = CurrentFormat();
            Duration = PresentationDuration();
            if (_start > 0)
            {
                var position = default(PROPVARIANT);
                position.vt = VARENUM.VT_I8;
                position.hVal = _start;
                _reader.SetCurrentPosition(Guid.Empty, position);
            }
        }
        catch (Exception exception) when (exception is COMException or FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            Dispose();
            throw new InvalidDataException($"The music file {_path} cannot be read: {exception.Message}", exception);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>The file's own sample rate, in samples per second.</summary>
    public int SampleRate => _format.Rate;

    /// <summary>The song's whole length in 100 ns ticks, as the file says; 0 when it does not say.</summary>
    public long Duration { get; }

    /// <summary>
    /// What the music section needs of a file the user chose: its length. Decodes the first samples too, so a file whose
    /// codec is missing is refused now rather than at the export.
    /// </summary>
    /// <exception cref="InvalidDataException">The file has no audio Windows can decode, or cannot be read.</exception>
    public static MusicFileInfo Probe(string path)
    {
        using var decoder = new MusicDecoder(path);
        Span<float> some = stackalloc float[256];
        decoder.Read(some);
        return new MusicFileInfo(TimeSpan.FromTicks(Math.Max(0, decoder.Duration)));
    }

    /// <summary>
    /// Fills <paramref name="stereo"/> with the next interleaved stereo frames (left, right) at <see cref="SampleRate"/>.
    /// Returns the frames written: fewer than asked only at the song's end, then 0.
    /// </summary>
    /// <exception cref="InvalidDataException">The decoder failed partway (the file is damaged).</exception>
    public int Read(Span<float> stereo)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var wanted = stereo.Length / 2;
        var written = 0;
        while (written < wanted)
        {
            if (_pendingCount == 0 && !Decode())
            {
                break;
            }

            var take = Math.Min(_pendingCount, wanted - written);
            _pending.AsSpan(_pendingStart * 2, take * 2).CopyTo(stereo[(written * 2)..]);
            _pendingStart += take;
            _pendingCount -= take;
            written += take;
        }

        return written;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Release(_reader);
        _session.Dispose();
    }

    // Reads the next decoded sample into _pending; false at the end of the stream.
    private bool Decode()
    {
        while (!_ended)
        {
            IMFSample? sample;
            uint flags;
            long time;
            try
            {
                _reader.ReadSample(FirstAudioStream, 0, out _, out flags, out time, out sample);
            }
            catch (COMException exception)
            {
                throw new InvalidDataException($"The music file {_path} could not be decoded: {exception.Message}", exception);
            }

            if ((flags & (uint)MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ERROR) != 0)
            {
                Release(sample);
                throw new InvalidDataException($"Media Foundation could not read the audio of {_path}.");
            }

            if ((flags & (uint)MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED) != 0)
            {
                _format = CurrentFormat();
            }

            _ended = (flags & (uint)MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ENDOFSTREAM) != 0;
            if (sample is null)
            {
                continue;
            }

            try
            {
                if (_first)
                {
                    // A seek lands on or before the point asked for: the frames before it are dropped.
                    _first = false;
                    _skip = _start > time ? (long)((Int128)(_start - time) * _format.Rate / FrameTiming.TicksPerSecond) : 0;
                }

                Convert(sample);
            }
            finally
            {
                Release(sample);
            }

            if (_skip > 0 && _pendingCount > 0)
            {
                var dropped = (int)Math.Min(_skip, _pendingCount);
                _pendingStart += dropped;
                _pendingCount -= dropped;
                _skip -= dropped;
            }

            if (_pendingCount > 0)
            {
                return true;
            }
        }

        return false;
    }

    // One decoded sample's bytes as stereo floats in _pending.
    private void Convert(IMFSample sample)
    {
        sample.ConvertToContiguousBuffer(out var buffer);
        try
        {
            byte* data;
            uint length;
            buffer.Lock(&data, null, &length);
            try
            {
                var format = _format;
                var frames = (int)(length / (uint)format.BlockAlign);
                if (_pending.Length < frames * 2)
                {
                    _pending = new float[frames * 2];
                }

                _pendingStart = 0;
                _pendingCount = frames;
                var output = _pending.AsSpan(0, frames * 2);
                for (var frame = 0; frame < frames; frame++)
                {
                    var at = data + ((long)frame * format.BlockAlign);
                    var left = Value(at, 0, format);
                    float right;
                    if (format.Channels == 1)
                    {
                        right = left;
                    }
                    else
                    {
                        right = Value(at, 1, format);
                        if (format.Channels > 2)
                        {
                            var centre = Value(at, 2, format) * CentreShare;
                            left = (left + centre) / (1 + CentreShare);
                            right = (right + centre) / (1 + CentreShare);
                        }
                    }

                    output[frame * 2] = left;
                    output[(frame * 2) + 1] = right;
                }
            }
            finally
            {
                buffer.Unlock();
            }
        }
        finally
        {
            Release(buffer);
        }
    }

    // Channel `channel` of the frame at `at`, -1 to 1.
    private static float Value(byte* at, int channel, Format format)
    {
        var bytes = format.BitsPerSample / 8;
        var sample = at + (channel * bytes);
        if (format.IsFloat)
        {
            return *(float*)sample;
        }

        return format.BitsPerSample switch
        {
            8 => (*sample - 128) / 128f,
            16 => *(short*)sample / 32768f,
            24 => ((sample[0] << 8) | (sample[1] << 16) | (sample[2] << 24)) / 2147483648f,
            _ => *(int*)sample / 2147483648f,
        };
    }

    // PCM by subtype alone: the decoder keeps the file's rate and channels and picks its bit depth; float when it gives
    // no PCM.
    private void AskForPcm()
    {
        foreach (var subtype in new[] { PInvoke.MFAudioFormat_PCM, PInvoke.MFAudioFormat_Float })
        {
            PInvoke.MFCreateMediaType(out var wanted).ThrowOnFailure();
            try
            {
                wanted.SetGUID(PInvoke.MF_MT_MAJOR_TYPE, PInvoke.MFMediaType_Audio);
                wanted.SetGUID(PInvoke.MF_MT_SUBTYPE, subtype);
                _reader.SetCurrentMediaType(FirstAudioStream, wanted);
                return;
            }
            catch (COMException) when (subtype == PInvoke.MFAudioFormat_PCM)
            {
                // MF_E_INVALIDMEDIATYPE: this decoder gives float only.
            }
            finally
            {
                Release(wanted);
            }
        }
    }

    private Format CurrentFormat()
    {
        _reader.GetCurrentMediaType(FirstAudioStream, out var type);
        try
        {
            type.GetGUID(PInvoke.MF_MT_SUBTYPE, out var subtype);
            type.GetUINT32(PInvoke.MF_MT_AUDIO_NUM_CHANNELS, out var channels);
            type.GetUINT32(PInvoke.MF_MT_AUDIO_SAMPLES_PER_SECOND, out var rate);
            type.GetUINT32(PInvoke.MF_MT_AUDIO_BITS_PER_SAMPLE, out var bits);
            var isFloat = subtype == PInvoke.MFAudioFormat_Float;
            if ((!isFloat && subtype != PInvoke.MFAudioFormat_PCM)
                || channels is 0 or > 32
                || rate is < 1000 or > 768_000
                || (isFloat ? bits != 32 : bits is not (8 or 16 or 24 or 32)))
            {
                throw new InvalidDataException($"The audio of {_path} decodes to a format 10Lapse cannot read ({channels} channels, {rate} Hz, {bits} bits).");
            }

            return new Format((int)rate, (int)channels, (int)bits, isFloat);
        }
        finally
        {
            Release(type);
        }
    }

    private long PresentationDuration()
    {
        try
        {
            _reader.GetPresentationAttribute(MediaSource, PInvoke.MF_PD_DURATION, out var value);
            try
            {
                return (long)value.uhVal;
            }
            finally
            {
                PInvoke.PropVariantClear(ref value);
            }
        }
        catch (COMException)
        {
            // MF_E_ATTRIBUTENOTFOUND: a stream that does not say how long it is.
            return 0;
        }
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.ReleaseComObject(comObject);
        }
    }

    private readonly record struct Format(int Rate, int Channels, int BitsPerSample, bool IsFloat)
    {
        public int BlockAlign => Channels * BitsPerSample / 8;
    }
}

/// <summary>What the app reads of an audio file the user chooses for the music (#50).</summary>
public static class MusicFile
{
    /// <summary>The song's length, after decoding its first samples (<see cref="MusicDecoder.Probe"/>).</summary>
    /// <exception cref="InvalidDataException">The file has no audio Windows can decode, or cannot be read.</exception>
    public static MusicFileInfo Read(string path) => MusicDecoder.Probe(path);

    /// <summary>The file types the music's file dialog offers: what Windows decodes without an extension from the Store.</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".mp3", ".m4a", ".aac", ".wav", ".wma", ".flac"];
}
