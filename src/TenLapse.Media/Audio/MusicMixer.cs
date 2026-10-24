using TenLapse.Core.Projects;
using TenLapse.Core.Timing;

namespace TenLapse.Media.Audio;

/// <summary>
/// The sound an MP4 export carries (#50): <paramref name="Music"/> under a video of <paramref name="VideoDuration"/>
/// ticks. <see cref="Export.VideoExportSettings.Music"/> holds it; the sink writer adds an AAC stream for it.
/// </summary>
/// <param name="Music">The song, where the video starts in it, the fades.</param>
/// <param name="VideoDuration">The video's length in 100 ns ticks: the audio track is exactly as long.</param>
public sealed record MusicTrack(MusicSettings Music, long VideoDuration)
{
    /// <summary>The track for a project's music under its video, or null when it has none.</summary>
    public static MusicTrack? For(ProjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Music.HasMusic && snapshot.Duration > 0 ? new MusicTrack(snapshot.Music, snapshot.Duration) : null;
    }
}

/// <summary>
/// Turns a <see cref="MusicTrack"/> into the 16-bit stereo PCM the AAC encoder takes (#50), at <see cref="SampleRate"/>:
/// the song from its start point, faded in and out (<see cref="MusicSettings.GainAt"/>), then silence to the video's end,
/// so the audio track always lasts as long as the video and the MP4 sink never waits for audio that will not come.
/// </summary>
/// <remarks>
/// The AAC encoder takes 44.1 or 48 kHz: a song at either keeps its rate; any other is resampled to 48 kHz by linear
/// interpolation between its samples, counted exactly (sample n of the output is at n × rate ÷ 48000 of the input),
/// which is plain but never drifts. Background music at an unusual rate is rare; a filter that removes the faint
/// aliasing can come later.
/// </remarks>
internal sealed class MusicMixer : IDisposable
{
    /// <summary>The rate a song at a rate the AAC encoder does not take is resampled to.</summary>
    public const int DefaultRate = 48_000;

    private readonly MusicDecoder _decoder;
    private readonly MusicSettings _music;
    private readonly int _sourceRate;
    private readonly long _played;
    private readonly long _frames;

    // The source frames decoded and not let go yet: _source[0..] is source frame _sourceStart onward.
    private float[] _source = new float[8192];
    private long _sourceStart;
    private int _sourceCount;
    private bool _sourceEnded;
    private long _position;

    /// <summary>Opens the song of <paramref name="track"/>.</summary>
    /// <exception cref="InvalidDataException">The song cannot be read.</exception>
    public MusicMixer(MusicTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        _music = track.Music;
        _decoder = new MusicDecoder(track.Music.Path ?? throw new ArgumentException("The track has no song.", nameof(track)), track.Music.Start);
        _sourceRate = _decoder.SampleRate;
        SampleRate = RateFor(_sourceRate);

        // A file that does not give its length plays to its end, without a fade there; the video's end fades it.
        var songLength = _decoder.Duration > 0 ? _decoder.Duration : long.MaxValue;
        _played = _music.PlayedLength(track.VideoDuration, songLength);
        _frames = FrameAt(track.VideoDuration);
    }

    /// <summary>The output rate: the song's own when the AAC encoder takes it, else <see cref="DefaultRate"/>.</summary>
    public int SampleRate { get; }

    /// <summary>Always two: left and right.</summary>
    public static int Channels => 2;

    /// <summary>The frames rendered so far.</summary>
    public long Position => _position;

    /// <summary>The track's length in output frames: the video's length.</summary>
    public long FrameCount => _frames;

    /// <summary>The output rate for a song at <paramref name="sourceRate"/>.</summary>
    public static int RateFor(int sourceRate) => sourceRate is 44_100 or 48_000 ? sourceRate : DefaultRate;

    /// <summary>The first output frame at or after <paramref name="ticks"/> into the video (the frames before it start before it).</summary>
    public long FrameAt(long ticks) => ticks <= 0 ? 0 : (long)(((Int128)ticks * SampleRate + FrameTiming.TicksPerSecond - 1) / FrameTiming.TicksPerSecond);

    /// <summary>When output frame <paramref name="frame"/> starts, in 100 ns ticks (rounded down).</summary>
    public long TimeOf(long frame) => (long)((Int128)frame * FrameTiming.TicksPerSecond / SampleRate);

    /// <summary>
    /// Fills <paramref name="pcm"/> (interleaved left and right) with the next frames, at most up to the video's end;
    /// returns the frames written, 0 once the track is complete.
    /// </summary>
    /// <exception cref="InvalidDataException">The song failed to decode partway.</exception>
    public int Render(Span<short> pcm)
    {
        var count = (int)Math.Min(pcm.Length / 2, _frames - _position);
        for (var i = 0; i < count; i++)
        {
            var frame = _position + i;
            var time = TimeOf(frame);
            var gain = (float)_music.GainAt(time, _played);
            float left = 0, right = 0;
            if (gain > 0)
            {
                (left, right) = SourceAt(frame);
            }

            pcm[i * 2] = ToPcm(left * gain);
            pcm[(i * 2) + 1] = ToPcm(right * gain);
        }

        _position += count;
        return count;
    }

    public void Dispose() => _decoder.Dispose();

    // The song at output frame `frame`: its own sample, or the line between the two source samples around it.
    private (float Left, float Right) SourceAt(long frame)
    {
        if (_sourceRate == SampleRate)
        {
            return Source(frame);
        }

        var exact = (Int128)frame * _sourceRate;
        var index = (long)(exact / SampleRate);
        var share = (float)((double)(exact % SampleRate) / SampleRate);
        var (left, right) = Source(index);
        if (share == 0)
        {
            return (left, right);
        }

        var (nextLeft, nextRight) = Source(index + 1);
        return (left + ((nextLeft - left) * share), right + ((nextRight - right) * share));
    }

    // Source frame `index` (silence past the song's end). The first index a call asks for only grows, and the line to the
    // next sample looks one frame ahead of it, so the frames more than one before `index` are let go as more are read.
    private (float Left, float Right) Source(long index)
    {
        while (index >= _sourceStart + _sourceCount && !_sourceEnded)
        {
            var drop = (int)Math.Clamp(index - 1 - _sourceStart, 0, _sourceCount);
            if (drop > 0)
            {
                Array.Copy(_source, drop * 2, _source, 0, (_sourceCount - drop) * 2);
                _sourceStart += drop;
                _sourceCount -= drop;
            }

            if ((_sourceCount + 1024) * 2 > _source.Length)
            {
                Array.Resize(ref _source, _source.Length * 2);
            }

            var read = _decoder.Read(_source.AsSpan(_sourceCount * 2));
            _sourceCount += read;
            _sourceEnded = read == 0;
        }

        var at = index - _sourceStart;
        return at >= 0 && at < _sourceCount ? (_source[at * 2], _source[(at * 2) + 1]) : (0f, 0f);
    }

    private static short ToPcm(float value) => (short)Math.Clamp(MathF.Round(value * 32767f), -32768f, 32767f);
}
