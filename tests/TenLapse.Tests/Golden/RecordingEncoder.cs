using Microsoft.Graphics.Canvas;
using TenLapse.Media.Export;
using TenLapse.Media.Inspection;

namespace TenLapse.Tests.Golden;

/// <summary>
/// Wraps the real encoder and records each frame's time and duration as the exporter hands them over, before Media
/// Foundation's timescale rounds them: the file shows the sink's arithmetic, this shows ours (#32 review, M3).
/// </summary>
internal sealed class RecordingEncoder : IVideoEncoder
{
    private readonly IVideoEncoder _inner;
    private readonly EncoderRecord _record;

    public RecordingEncoder(IVideoEncoder inner, EncoderRecord record)
    {
        (_inner, _record) = (inner, record);
        record.Opened();
    }

    public VideoEncoderInfo? Encoder => _inner.Encoder;

    public void Write(CanvasRenderTarget target, long time, long duration)
    {
        _record.Add(time, duration);
        _inner.Write(target, time, duration);
    }

    public void Finish() => _inner.Finish();

    public void Dispose() => _inner.Dispose();
}

/// <summary>What <see cref="RecordingEncoder"/> saw during one export: how many encoders were opened, and every write in order.</summary>
internal sealed class EncoderRecord
{
    private readonly Lock _lock = new();
    private readonly List<(long Time, long Duration)> _writes = [];
    private int _opened;

    /// <summary>How many encoders the export opened (one, for an export that ran once).</summary>
    public int EncodersOpened => Volatile.Read(ref _opened);

    /// <summary>Each <see cref="IVideoEncoder.Write"/>'s time and duration, in the order the exporter called it.</summary>
    public IReadOnlyList<(long Time, long Duration)> Writes
    {
        get
        {
            lock (_lock)
            {
                return [.. _writes];
            }
        }
    }

    public void Opened() => Interlocked.Increment(ref _opened);

    public void Add(long time, long duration)
    {
        lock (_lock)
        {
            _writes.Add((time, duration));
        }
    }
}
