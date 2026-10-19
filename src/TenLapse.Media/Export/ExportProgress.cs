using System.Diagnostics;

namespace TenLapse.Media.Export;

/// <summary>What a project export is doing.</summary>
public enum ExportStage
{
    /// <summary>Checking the folder, creating the graphics device and opening the encoder: no frame written yet.</summary>
    Preparing,

    /// <summary>Decoding, drawing and encoding frames.</summary>
    Encoding,

    /// <summary>Every frame is in; the encoder drains and the file is completed and moved into place.</summary>
    Finishing,
}

/// <summary>A progress report of a project export (#24's panel, taskbar and film gate).</summary>
/// <param name="Stage">What the export is doing.</param>
/// <param name="Frame">Frames the encoder has taken, 0 to <paramref name="FrameCount"/>; never goes down.</param>
/// <param name="FrameCount">Frames in the export.</param>
/// <param name="FramesPerSecond">Encoding speed, smoothed over the last seconds; null until it has been measured.</param>
/// <param name="TimeLeft">Time to the last frame at that speed, smoothed; null until a few frames are done.</param>
public sealed record ExportProgress(ExportStage Stage, int Frame, int FrameCount, double? FramesPerSecond, TimeSpan? TimeLeft)
{
    /// <summary>The share of frames done, 0 to 1.</summary>
    public double Fraction => FrameCount == 0 ? 0 : (double)Frame / FrameCount;
}

/// <summary>
/// Turns the export's frame count into <see cref="ExportProgress"/> reports: monotonic, at most one every
/// <see cref="MinInterval"/> (about 10 a second) except stage changes and the last frame, with the speed an
/// exponential moving average sampled every <see cref="SampleInterval"/>.
/// </summary>
internal sealed class ExportProgressTracker : IProgress<int>
{
    /// <summary>The shortest gap between two reports of the same stage.</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>How often the speed is sampled.</summary>
    public static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Frames done before a time left is given.</summary>
    public const int MinFramesForEstimate = 5;

    // Weight of the newest speed sample: about the last 2 s count (8 samples).
    private const double Smoothing = 0.25;

    private readonly object _gate = new();
    private readonly IProgress<ExportProgress>? _sink;
    private readonly Func<TimeSpan> _clock;
    private readonly int _count;
    private ExportStage _stage = ExportStage.Preparing;
    private int _frame;
    private TimeSpan? _lastReport;
    private ExportStage? _lastStage;
    private TimeSpan _sampleTime;
    private int _sampleFrame;
    private double? _speed;

    public ExportProgressTracker(int count, IProgress<ExportProgress>? sink, Func<TimeSpan>? clock = null)
    {
        _count = count;
        _sink = sink;
        if (clock is null)
        {
            var watch = Stopwatch.StartNew();
            clock = () => watch.Elapsed;
        }

        _clock = clock;
    }

    /// <summary>The last report made, for the result and the tests.</summary>
    public ExportProgress? Last { get; private set; }

    /// <summary>Moves to <paramref name="stage"/> (never back) and reports it at once.</summary>
    public void Enter(ExportStage stage)
    {
        lock (_gate)
        {
            if (stage <= _stage && _lastStage is not null)
            {
                return;
            }

            _stage = stage > _stage ? stage : _stage;
            Publish(_clock(), force: true);
        }
    }

    /// <summary>The encoder has taken <paramref name="value"/> frames.</summary>
    public void Report(int value)
    {
        lock (_gate)
        {
            if (value <= _frame)
            {
                return;
            }

            var now = _clock();
            if (_frame == 0)
            {
                // Speed counts from the first frame: opening the encoder is not encoding.
                (_sampleTime, _sampleFrame) = (now, value);
            }
            else if (now - _sampleTime >= SampleInterval)
            {
                var sample = (value - _sampleFrame) / (now - _sampleTime).TotalSeconds;
                _speed = _speed is { } speed ? speed + (Smoothing * (sample - speed)) : sample;
                (_sampleTime, _sampleFrame) = (now, value);
            }

            _frame = value;
            var stage = value >= _count ? ExportStage.Finishing : ExportStage.Encoding;
            _stage = stage > _stage ? stage : _stage;
            Publish(now, force: value >= _count);
        }
    }

    private void Publish(TimeSpan now, bool force)
    {
        var stageChanged = _lastStage != _stage;
        if (!force && !stageChanged && _lastReport is { } last && now - last < MinInterval)
        {
            return;
        }

        TimeSpan? left = null;
        if (_speed is > 0 and var speed && _frame >= MinFramesForEstimate)
        {
            left = TimeSpan.FromSeconds((_count - _frame) / speed);
        }

        var report = new ExportProgress(_stage, _frame, _count, _speed, left);
        (_lastReport, _lastStage, Last) = (now, _stage, report);
        _sink?.Report(report);
    }
}
