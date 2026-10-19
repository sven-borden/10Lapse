using TenLapse.Media.Export;

namespace TenLapse.Tests.Media;

/// <summary>Progress reports: monotonic, about 10 a second, smoothed speed, time left once a few frames are done.</summary>
public sealed class ExportProgressTests
{
    private TimeSpan _now;

    [Fact]
    public void ReportsAreMonotonicAndAtMostTenASecondPlusStageChanges()
    {
        var reports = new List<ExportProgress>();
        var tracker = new ExportProgressTracker(1_000, new Synchronous(reports.Add), () => _now);

        tracker.Enter(ExportStage.Preparing);
        _now += TimeSpan.FromSeconds(1);
        for (var frame = 1; frame <= 1_000; frame++)
        {
            _now += TimeSpan.FromMilliseconds(10); // 100 fps for 10 s
            tracker.Report(frame);
            tracker.Report(frame - 1); // late or repeated counts are ignored
        }

        tracker.Enter(ExportStage.Finishing);

        Assert.Equal(ExportStage.Preparing, reports[0].Stage);
        Assert.Equal(ExportStage.Finishing, reports[^1].Stage);
        Assert.Equal(1_000, reports[^1].Frame);
        Assert.True(reports.Zip(reports.Skip(1)).All(pair => pair.First.Frame <= pair.Second.Frame && pair.First.Stage <= pair.Second.Stage));
        // 10 s of encoding at most 10 a second, plus the stage changes and the last frame.
        Assert.InRange(reports.Count, 90, 104);
        Assert.All(reports, report => Assert.Equal(1_000, report.FrameCount));
    }

    [Fact]
    public void SpeedAndTimeLeftFollowTheEncoderSmoothly()
    {
        var reports = new List<ExportProgress>();
        var tracker = new ExportProgressTracker(600, new Synchronous(reports.Add), () => _now);
        tracker.Enter(ExportStage.Preparing);
        _now += TimeSpan.FromSeconds(3); // opening the encoder does not count as encoding

        for (var frame = 1; frame <= 300; frame++)
        {
            _now += TimeSpan.FromMilliseconds(frame % 2 == 0 ? 15 : 25); // 50 fps, unevenly
            tracker.Report(frame);
        }

        var early = reports.Where(report => report.Frame < ExportProgressTracker.MinFramesForEstimate);
        Assert.All(early, report => Assert.Null(report.TimeLeft));
        var last = reports[^1];
        Assert.InRange(last.FramesPerSecond!.Value, 45, 55);
        Assert.InRange(last.TimeLeft!.Value.TotalSeconds, 5.4, 6.6); // 300 frames left at 50 fps
        Assert.InRange(last.Fraction, 0.48, 0.5);
    }

    [Fact]
    public void TheSpeedEasesTowardsAChangeInsteadOfJumping()
    {
        var reports = new List<ExportProgress>();
        var tracker = new ExportProgressTracker(10_000, new Synchronous(reports.Add), () => _now);
        var frame = 0;
        for (var i = 0; i < 400; i++)
        {
            _now += TimeSpan.FromMilliseconds(10); // 100 fps for 4 s
            tracker.Report(++frame);
        }

        for (var i = 0; i < 25; i++)
        {
            _now += TimeSpan.FromMilliseconds(20); // then 50 fps for 0.5 s
            tracker.Report(++frame);
        }

        Assert.InRange(reports[^1].FramesPerSecond!.Value, 55, 95);
    }

    private sealed class Synchronous(Action<ExportProgress> report) : IProgress<ExportProgress>
    {
        public void Report(ExportProgress value) => report(value);
    }
}
