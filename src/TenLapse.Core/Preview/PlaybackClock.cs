using TenLapse.Core.Timing;

namespace TenLapse.Core.Preview;

/// <summary>
/// Where playback is at a moment of wall-clock time (#19): the preview starts it at a frame and a timestamp, and from
/// then on every question ("which frame belongs on screen now?") is answered from the elapsed time with
/// <see cref="FrameTiming"/>, never by counting frames. A late or skipped draw therefore costs a dropped frame, not
/// drift: the next draw shows the frame the clock says, exactly where export puts it. The clip loops.
/// </summary>
/// <remarks>
/// Immutable and thread-safe: the UI thread's counter and the render thread read the same instance and agree. Any
/// change (a seek, a new rate, frames added or removed) makes a new clock anchored at the frame on screen.
/// Timestamps are in the units of <see cref="Frequency"/> (<see cref="System.Diagnostics.Stopwatch"/> ticks in the app).
/// </remarks>
public sealed class PlaybackClock
{
    private readonly long _startTicks;

    /// <summary>Starts playback at <paramref name="startFrame"/> at the moment <paramref name="startTimestamp"/>.</summary>
    /// <param name="rate">Frames per second (the project's effective rate).</param>
    /// <param name="frameCount">Frames in the clip, at least one.</param>
    /// <param name="startFrame">The frame on screen at <paramref name="startTimestamp"/> (0-based).</param>
    /// <param name="startTimestamp">The moment playback starts, in <paramref name="frequency"/> units.</param>
    /// <param name="frequency">Timestamp units per second.</param>
    public PlaybackClock(Fraction rate, int frameCount, int startFrame, long startTimestamp, long frequency)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameCount);
        ArgumentOutOfRangeException.ThrowIfNegative(startFrame);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(startFrame, frameCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frequency);

        Rate = rate;
        FrameCount = frameCount;
        StartFrame = startFrame;
        StartTimestamp = startTimestamp;
        Frequency = frequency;
        _startTicks = FrameTiming.FrameStart(startFrame, rate);
        Length = FrameTiming.Duration(frameCount, rate);
    }

    /// <summary>Frames per second.</summary>
    public Fraction Rate { get; }

    /// <summary>Frames in the clip.</summary>
    public int FrameCount { get; }

    /// <summary>The frame playback started at.</summary>
    public int StartFrame { get; }

    /// <summary>When playback started, in <see cref="Frequency"/> units.</summary>
    public long StartTimestamp { get; }

    /// <summary>Timestamp units per second.</summary>
    public long Frequency { get; }

    /// <summary>The clip's length in 100 ns ticks; playback loops after it.</summary>
    public long Length { get; }

    /// <summary>The frame that belongs on screen at <paramref name="timestamp"/>; a moment before the start reads as the start.</summary>
    public int FrameAt(long timestamp) => Read(timestamp).Frame;

    /// <summary>Everything about the moment <paramref name="timestamp"/>: the frame, which pass of the loop, and when that frame began.</summary>
    public PlaybackReading Read(long timestamp)
    {
        // Elapsed clip time since the clip's own zero, not wrapped: the loop count and lateness come from it.
        var elapsed = timestamp <= StartTimestamp
            ? 0
            : (long)((Int128)(timestamp - StartTimestamp) * FrameTiming.TicksPerSecond / Frequency);
        var total = (Int128)_startTicks + elapsed;
        var loop = (long)(total / Length);
        var within = (long)(total % Length);
        var frame = (int)Math.Min(FrameCount - 1, FrameTiming.FrameAt(within, Rate));
        var occurrence = (loop * FrameCount) + frame;
        return new PlaybackReading(frame, occurrence, (Int128)loop * Length + FrameTiming.FrameStart(frame, Rate), total);
    }

    /// <summary>The timestamp at which clip time <paramref name="clipTicks"/> (as in <see cref="PlaybackReading"/>, not wrapped) is reached.</summary>
    public long TimestampAt(Int128 clipTicks)
    {
        var sinceStart = clipTicks - _startTicks;
        return StartTimestamp + (long)(sinceStart * Frequency / FrameTiming.TicksPerSecond);
    }
}

/// <summary>One reading of a <see cref="PlaybackClock"/>.</summary>
/// <param name="Frame">The frame on screen (0-based).</param>
/// <param name="Occurrence">
/// The frame counted across loops from the start of the first pass (<c>loop * frameCount + frame</c>), so two readings
/// tell how many frames lay between them even across the loop point.
/// </param>
/// <param name="OccurrenceStart">When this showing of the frame began, in clip ticks since the clip's zero, not wrapped.</param>
/// <param name="ClipTicks">The moment read, in the same ticks.</param>
public readonly record struct PlaybackReading(int Frame, long Occurrence, Int128 OccurrenceStart, Int128 ClipTicks);
