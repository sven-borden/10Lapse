using TenLapse.Core.Preview;
using TenLapse.Core.Timing;

namespace TenLapse.Tests.Preview;

public sealed class PlaybackStatsTests
{
    private const long Frequency = 1_000_000; // microseconds

    [Fact]
    public void ThirtyFpsOnSixtyHertzShowsEveryFrameForTwoRefreshes()
    {
        // 6 MHz, so every 60 Hz vblank falls on a whole count (100,000 apart).
        var clock = new PlaybackClock(new Fraction(30, 1), 1000, 0, 0, 6_000_000);
        var stats = new PlaybackStats(clock);

        // A present at every 60 Hz vblank, drawing only when the frame changes, as the render loop does.
        var last = -1L;
        for (var vblank = 0; vblank < 600; vblank++)
        {
            var t = vblank * 100_000L;
            var reading = clock.Read(t);
            if (reading.Occurrence != last)
            {
                stats.Record(t, reading, PresentedImage.Proxy);
                last = reading.Occurrence;
            }
        }

        var summary = stats.Summarize();
        Assert.Equal(300, summary.FramesDue);
        Assert.Equal(300, summary.FramesShown);
        Assert.Equal(0, summary.Dropped);
        Assert.Equal(0, summary.HeldPresents);
        Assert.InRange(summary.OnScreen.Mean, 33.2, 33.5);
        Assert.InRange(summary.OnScreen.Max, 33.2, 33.5);
        Assert.InRange(summary.Lateness.Max, 0, 1);
        Assert.InRange(summary.IdealFrameMilliseconds, 33.33, 33.34);
    }

    [Fact]
    public void AFrameThatNeverGotItsImageIsDroppedAndItsPresentsHeld()
    {
        var clock = new PlaybackClock(new Fraction(10, 1), 100, 0, 0, Frequency);
        var stats = new PlaybackStats(clock);
        for (var frame = 0; frame < 10; frame++)
        {
            var t = frame * 100_000L;
            stats.Record(t, clock.Read(t), frame == 4 ? PresentedImage.Held : frame == 7 ? PresentedImage.Quick : PresentedImage.Proxy);
        }

        var summary = stats.Summarize();
        Assert.Equal(10, summary.FramesDue);
        Assert.Equal(9, summary.FramesShown);
        Assert.Equal(1, summary.Dropped);
        Assert.Equal(1, summary.HeldPresents);
        Assert.Equal(1, summary.QuickPresents);

        // Frame 3 stayed on screen while 4 was missing: 200 ms.
        Assert.Equal(200, summary.OnScreen.Max, 3);
        Assert.Contains("1 dropped", summary.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DistributionsUseTheNearestRank()
    {
        var values = Enumerable.Range(1, 100).Select(value => (double)value).ToList();
        var distribution = Distribution.Of(values);

        Assert.Equal(50.5, distribution.Mean);
        Assert.Equal(5, distribution.P5);
        Assert.Equal(50, distribution.Median);
        Assert.Equal(95, distribution.P95);
        Assert.Equal(100, distribution.Max);
        Assert.Equal(0, Distribution.Of([]).Count);
    }
}
